using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The grief system the client carries (PlanetSide 2's friendly-fire penalty, kept in Landmark's client): a player
    /// has grief points, damage points and a grief level — 1-3 warning levels, 4 equipment lock, 5 suspension.
    /// <para><b>Client side [P]</b> (both builds; 0.695 addresses, 0.589 in brackets):</para>
    /// <list type="bullet">
    /// <item>State: player +0x76920 {i32 grief points, i32 damage points, i32 level, time}; the client's dump (0.589
    /// FUN_142e43250) names them griefPoints / damagePoints / griefLevel and derives isEquipmentLock = level &gt; 3 and
    /// isAccountSuspended = level &gt; 4. SendSelfToClient fills the three i32 at login (FUN_142c324d0 [FUN_142b98950];
    /// <see cref="SendSelfToClient.GriefData"/>), so the state survives a relog.</item>
    /// <item>GriefBase 0xC0, u8 sub after the opcode (dispatcher FUN_142ee1bf0 [FUN_142e42e60]):
    /// 1 SetGriefPoints {i32 points, i32 damage points} (FUN_142ee1d80; a negative value keeps the stored one) → chat
    /// "You have %d grief points." with the stored points; 2 SetGriefLevel {i32 old, i32 new} (reader FUN_142ee1ce0,
    /// disassembled: the two i32 go to FUN_142ee1850 as old / new) → "You have entered / left grief level %d" (1-3),
    /// "… grief equipment lock" (4), "… grief suspension" (5), stores the new level, calls Lua
    /// HudHandler:OnGriefLevelChanged(old, new) (no shipped script defines it; the 0.589 Hud swf's GriefManager would show
    /// a "weapons disabled" text at level 4, but nothing forwards the call to it); 3 Reset (header only) zeroes it
    /// silently.</item>
    /// <item>Level ≥ 4 on the client: the "Knife" and "QuickUse" ability slots refuse (FUN_142e932b0 / FUN_142e93380) and
    /// fire modes without FireModes GRIEF_IMMUNE (flag 0x10) can't fire (FUN_142e93a40). Landmark 0.695 ships no
    /// weapon definitions that reach those checks (trace/0695/agent-l138-c1.md §2), so the server enforces the lock
    /// itself: weapon and combat-gear attacks are refused (<see cref="Abilities.HandleCast"/>); tools, consumables and
    /// movement stay usable [I].</item>
    /// <item>What earns grief, in the client's own words: the warnings 56075-56078 (CodeStringMappings UI.GriefLevelOne
    /// to Four) "You have been firing on ally forces. …", "Check your fire! Uncontrolled damage to ally forces will not be
    /// tolerated.", "… your weapons will be locked, this is your final warning.", "… Weapon lock is now active." and
    /// 56079 UI.GriefLevelWeaponsEnabled "Weapon lock is now deactivated." [P text]. So grief comes from damage to
    /// allies, and nothing else in the client or the 2014-2017 sources names another source (forum, wiki, patch notes
    /// searched 2026-10-17: no "grief points" anywhere).</item>
    /// </list>
    /// <para><b>Server side</b>: allies exist only in team site games (InstancedGames: same team; everyone else in
    /// Landmark is either an enemy there, a duel opponent or not attackable). With <see cref="Enabled"/> (setting
    /// GriefAwards, off by default because live's amounts are unknown — OPEN-LEADS L144) a hit aimed at a teammate lands
    /// (<see cref="InstancedGames.Hit"/>) and <see cref="AllyDamage"/> turns the damage into grief: one grief point per
    /// GriefDamagePerPoint damage, the remainder kept as the client's "damage points" [I]; levels from GriefLevelPoints
    /// [I]; points wear off at GriefDecayPerHour in real time [I]; level 5 suspends the character for
    /// GriefSuspensionMinutes — the session ends and the world turns the character away until it's over [I]. Rule sets
    /// whose rules score team kills (IgRuleTypes 3 ScoreOnTeamKill: 4 Traitors! "Kill teammates while …", 5 All Scoring)
    /// make hitting teammates part of the game, so they earn no grief [I].</para>
    /// <para>The state is kept per character in character_unlock (<see cref="Unlocks.GriefPoints"/> … kinds 27-31),
    /// coalesced: award changes go to the client (one "You have %d grief points." line) and the database at most every
    /// <see cref="FlushInterval"/>; level changes at once. Admin / dev commands (GriefAdmin 0x1E / 0x4DA, dev
    /// <c>grief</c>) set and reset it whatever the setting.</para>
    /// </summary>
    public static class Grief
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte SetPointsSub = 1, SetLevelSub = 2, ResetSub = 3;
        public const int EquipmentLockLevel = 4, SuspensionLevel = 5;

        /// <summary>Client texts (text_695 / text_589, same ids): the level 1-4 warnings and the end of the weapon lock.</summary>
        public const uint WarningLevelOneText = 56075, WeaponsEnabledText = 56079;

        /// <summary>IgRuleTypes 3 ScoreOnTeamKill ("Player kills his own teammate."): such a game scores team kills.</summary>
        public const uint TeamKillRuleType = 3;

        /// <summary>At most this often an award's new points go to the client and the database.</summary>
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(15);
        /// <summary>A suspended player reads the notice this long before the session ends.</summary>
        private static readonly TimeSpan SuspensionNotice = TimeSpan.FromSeconds(10);
        private static readonly DateTime Epoch = new(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class State
        {
            public WorldSession Session;
            public int Points, DamagePoints, Level;
            /// <summary>Minutes since 2016: when points last wore off (0 = no points to decay) / when a suspension ends (0 = none).</summary>
            public uint DecayedAt, SuspendedUntil;
            /// <summary>Points changed since the client and the database last heard.</summary>
            public bool Dirty;
            public DateTime NextFlush;
            public DateTime? CloseAt;
            /// <summary>The values in the database (only changed kinds are written).</summary>
            public (int Points, int Damage, int Level, uint DecayedAt, uint SuspendedUntil) Saved;
        }

        private static readonly ConcurrentDictionary<ulong, State> states = new();
        private static DateTime nextUpdate;

        private static WorldServerConfiguration Config => ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>Does friendly fire in team site games earn grief (setting GriefAwards; off = live's numbers unknown)?</summary>
        public static bool Enabled => Config?.GriefAwards == true;

        private static uint NowMinute => (uint)Math.Max(1, (DateTime.UtcNow - Epoch).TotalMinutes);

        /// <summary>The points at which levels 1-5 begin (GriefLevelPoints), ascending.</summary>
        private static int[] Thresholds
        {
            get
            {
                int[] parsed = (Config?.GriefLevelPoints ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => int.TryParse(t, out int v) ? v : -1).Where(v => v > 0).Take(SuspensionLevel).ToArray();
                return parsed.Length > 0 ? parsed.OrderBy(v => v).ToArray() : new[] { 10, 20, 30, 40, 60 };
            }
        }

        private static int LevelFor(int points) => Thresholds.Count(t => points >= t);

        private static State Of(WorldSession session) => states.GetOrAdd(session.PlayerGuid, _ => Load(session));

        /// <summary>Reads a character's grief state from its unlocks (loaded at login).</summary>
        private static State Load(WorldSession session)
        {
            int Value(uint kind) => (int)Unlocks.Values(session.PlayerGuid, kind).FirstOrDefault();
            var state = new State
            {
                Session = session,
                Points = Value(Unlocks.GriefPoints),
                DamagePoints = Value(Unlocks.GriefDamagePoints),
                Level = Value(Unlocks.GriefLevel),
                DecayedAt = (uint)Value(Unlocks.GriefDecayedAt),
                SuspendedUntil = (uint)Value(Unlocks.GriefSuspendedUntil),
            };
            state.Saved = (state.Points, state.DamagePoints, state.Level, state.DecayedAt, state.SuspendedUntil);
            return state;
        }

        public static int LevelOf(WorldSession session) => session.PlayerGuid == 0 ? 0 : Of(session).Level;

        /// <summary>
        /// Is the player in grief equipment lock (level ≥ 4, as the client's own checks)? The server then refuses weapon
        /// and combat-gear attacks (<see cref="Abilities.HandleCast"/>); see the class notes for what the client locks.
        /// </summary>
        public static bool EquipmentLocked(WorldSession session) => LevelOf(session) >= EquipmentLockLevel;

        /// <summary>Is the character serving a grief suspension?</summary>
        public static bool Suspended(WorldSession session)
            => session.PlayerGuid != 0 && Of(session) is State s && s.SuspendedUntil != 0 && NowMinute < s.SuspendedUntil;

        /// <summary>
        /// The SendSelfToClient grief block at login: the stored state after the points that wore off while the player
        /// was away, and with an expired suspension lifted. Nothing is sent: the client takes its state from this block.
        /// </summary>
        public static SendSelfToClient.GriefData SelfData(WorldSession session)
        {
            var state = Load(session);
            states[session.PlayerGuid] = state;
            lock (state)
            {
                Decay(state, notify: false);
                if (state.SuspendedUntil != 0 && NowMinute >= state.SuspendedUntil)
                {
                    state.SuspendedUntil = 0;
                    state.Level = Math.Min(LevelFor(state.Points), SuspensionLevel - 1);
                }
                Save(state);
                return new SendSelfToClient.GriefData { Points = state.Points, DamagePoints = state.DamagePoints, Level = state.Level };
            }
        }

        /// <summary>The player entered the world: a suspended character is told and turned away.</summary>
        public static void Enter(WorldSession session)
        {
            State state = Of(session);
            lock (state)
            {
                state.Session = session;
                if (state.SuspendedUntil == 0 || NowMinute >= state.SuspendedUntil)
                    return;
                state.CloseAt = DateTime.UtcNow + SuspensionNotice;
                DateTime until = Epoch.AddMinutes(state.SuspendedUntil);
                Chat.System(session, $"You are under grief suspension until {until:HH:mm} UTC.");
                log.Info($"{session.CharacterName}: grief suspension until {until:u}; turned away.");
            }
        }

        /// <summary>
        /// A session ends: unsaved points are written, and the cached state goes — unless a newer session of the
        /// character (a quick relog) already loaded its own.
        /// </summary>
        public static void Leave(WorldSession session)
        {
            if (!states.TryGetValue(session.PlayerGuid, out State state) || state.Session != session)
                return;
            lock (state)
                Save(state);
            states.TryRemove(new KeyValuePair<ulong, State>(session.PlayerGuid, state));
        }

        /// <summary>
        /// Damage a player dealt to an ally (a teammate in a team site game; <see cref="InstancedGames.Hit"/>): grief for
        /// the attacker while <see cref="Enabled"/> (or <paramref name="force"/>, the dev command). Every
        /// GriefDamagePerPoint damage makes a grief point, the remainder stays as damage points [I]; reaching a level's
        /// points raises the level at once, with its warning.
        /// </summary>
        public static void AllyDamage(WorldSession attacker, WorldSession victim, uint damage, bool force = false)
        {
            if ((!Enabled && !force) || damage == 0)
                return;
            State state = Of(attacker);
            lock (state)
            {
                int perPoint = (int)Math.Max(1u, Config?.GriefDamagePerPoint ?? 100);
                state.DamagePoints += (int)Math.Min(damage, 1_000_000u);
                state.Points += state.DamagePoints / perPoint;
                state.DamagePoints %= perPoint;
                if (state.Points > 0 && state.DecayedAt == 0)
                    state.DecayedAt = NowMinute;
                state.Dirty = true;
                log.Info($"{attacker.CharacterName} hit ally {victim?.CharacterName ?? "(dev)"} for {damage}: {state.Points} grief points, {state.DamagePoints} damage points.");
                int level = LevelFor(state.Points);
                if (state.SuspendedUntil == 0 && level > state.Level)
                {
                    ChangeLevel(state, level);
                    if (level >= SuspensionLevel)
                        Suspend(state);
                }
            }
        }

        /// <summary>Admin / dev: sets the grief level (GriefBase 2; the client prints the entered / left line).</summary>
        public static void SetLevel(WorldSession session, int level)
        {
            State state = Of(session);
            lock (state)
                ChangeLevel(state, Math.Clamp(level, 0, SuspensionLevel));
        }

        /// <summary>Admin / dev: sets points and / or damage points (a negative value keeps one), as GriefBase 1.</summary>
        public static void SetPoints(WorldSession session, int points, int damagePoints)
        {
            State state = Of(session);
            lock (state)
            {
                if (points >= 0)
                    state.Points = points;
                if (damagePoints >= 0)
                    state.DamagePoints = damagePoints;
                state.DecayedAt = state.Points > 0 ? (state.DecayedAt == 0 ? NowMinute : state.DecayedAt) : 0;
                state.Dirty = false;
                SendPoints(session, points, damagePoints);
                Save(state);
            }
        }

        /// <summary>
        /// Dev: GriefBase 1 with both values -1, which changes nothing and makes the client print the points it holds
        /// ("You have %d grief points.") — the check that the login block reached it.
        /// </summary>
        public static void Show(WorldSession session) => SendPoints(session, -1, -1);

        /// <summary>Admin / dev: everything back to 0, the suspension lifted (GriefBase 3, which the client applies silently).</summary>
        public static void Reset(WorldSession session)
        {
            State state = Of(session);
            lock (state)
            {
                bool wasLocked = state.Level >= EquipmentLockLevel;
                state.Points = state.DamagePoints = state.Level = 0;
                state.DecayedAt = state.SuspendedUntil = 0;
                state.Dirty = false;
                state.CloseAt = null;
                Send(session, ResetSub, _ => { });
                if (wasLocked)
                    SystemMessages.Show(session, WeaponsEnabledText);
                Save(state);
            }
            log.Info($"{session.CharacterName}: grief reset.");
        }

        /// <summary>
        /// World tick (once a second): points wear off, coalesced award changes go out, suspensions end or turn the
        /// player away.
        /// </summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextUpdate || states.IsEmpty)
                return;
            nextUpdate = now.AddSeconds(1);
            foreach (State state in states.Values)
            {
                WorldSession session = state.Session;
                if (session == null)
                    continue;
                if (session.Disconnected)
                {
                    // Saved and dropped: a session that timed out before entering the world reaches neither
                    // PlayerManager.Leave path, and its state (holding the whole session) stayed here (review #7).
                    Leave(session);
                    continue;
                }
                lock (state)
                {
                    if (state.CloseAt is DateTime close && now >= close)
                    {
                        state.CloseAt = null;
                        Save(state);
                        log.Info($"{session.CharacterName}: grief suspension, session closed.");
                        session.OnDisconnect();
                        continue;
                    }
                    if (state.SuspendedUntil != 0 && NowMinute >= state.SuspendedUntil)
                    {
                        state.SuspendedUntil = 0;
                        ChangeLevel(state, Math.Min(LevelFor(state.Points), SuspensionLevel - 1));
                    }
                    Decay(state, notify: true);
                    if (state.Dirty && now >= state.NextFlush)
                    {
                        state.Dirty = false;
                        state.NextFlush = now + FlushInterval;
                        SendPoints(session, state.Points, state.DamagePoints);
                        Save(state);
                    }
                }
            }
        }

        /// <summary>
        /// Points wear off (GriefDecayPerHour, real time since <see cref="State.DecayedAt"/>); with no points left the
        /// damage points go too. A level the remaining points no longer reach drops (not during a suspension).
        /// </summary>
        private static void Decay(State state, bool notify)
        {
            uint perHour = Config?.GriefDecayPerHour ?? 30;
            if (perHour == 0 || state.Points <= 0 || state.DecayedAt == 0)
                return;
            uint now = NowMinute;
            if (now <= state.DecayedAt)
                return;
            long lost = (long)(now - state.DecayedAt) * perHour / 60;
            if (lost <= 0)
                return;
            state.DecayedAt += (uint)Math.Max(1, lost * 60 / perHour);
            state.Points = (int)Math.Max(0, state.Points - lost);
            if (state.Points == 0)
            {
                state.DamagePoints = 0;
                state.DecayedAt = 0;
            }
            // Wearing off alone sends nothing (a "You have %d grief points." line every few minutes would be noise); the
            // points go out with the next level change or award flush.
            int level = LevelFor(state.Points);
            if (state.SuspendedUntil == 0 && level < state.Level)
            {
                if (notify)
                {
                    state.Dirty = true;
                    ChangeLevel(state, level);
                }
                else
                    state.Level = level;
            }
            Save(state);
        }

        /// <summary>
        /// A new level: the points (when they changed), then GriefBase 2 (old, new) — the client prints its entered / left
        /// line — and the matching warning (56075-56078 going up through 1-4, 56079 when the weapon lock ends) [I: which line live showed
        /// when]. Saved at once.
        /// </summary>
        private static void ChangeLevel(State state, int level)
        {
            WorldSession session = state.Session;
            int old = state.Level;
            state.Level = level;
            if (session != null && !session.Disconnected)
            {
                if (state.Dirty)
                {
                    state.Dirty = false;
                    state.NextFlush = DateTime.UtcNow + FlushInterval;
                    SendPoints(session, state.Points, state.DamagePoints);
                }
                Send(session, SetLevelSub, w => { w.Write(old); w.Write(level); });
                if (level > old && level >= 1 && level <= EquipmentLockLevel)
                    SystemMessages.Show(session, WarningLevelOneText + (uint)(level - 1));
                else if (old >= EquipmentLockLevel && level < EquipmentLockLevel)
                    SystemMessages.Show(session, WeaponsEnabledText);
            }
            Save(state);
            log.Info($"{session?.CharacterName}: grief level {old} → {level} ({state.Points} points).");
        }

        /// <summary>Level 5 by awards: the suspension starts, and the session ends after a notice.</summary>
        private static void Suspend(State state)
        {
            uint minutes = Math.Max(1u, Config?.GriefSuspensionMinutes ?? 60);
            state.SuspendedUntil = NowMinute + minutes;
            state.CloseAt = DateTime.UtcNow + SuspensionNotice;
            Save(state);
            if (state.Session != null)
                Chat.System(state.Session, $"You have been suspended for {minutes} minutes for damaging allies.");
            log.Info($"{state.Session?.CharacterName}: grief suspension for {minutes} minutes.");
        }

        /// <summary>Writes the kinds that changed since the last save (character_unlock single rows; 0 = no row).</summary>
        private static void Save(State state)
        {
            WorldSession session = state.Session;
            if (session == null || session.PlayerGuid == 0)
                return;
            var saved = state.Saved;
            if (saved.Points != state.Points)
                Unlocks.Set(session, Unlocks.GriefPoints, (uint)Math.Max(0, state.Points));
            if (saved.Damage != state.DamagePoints)
                Unlocks.Set(session, Unlocks.GriefDamagePoints, (uint)Math.Max(0, state.DamagePoints));
            if (saved.Level != state.Level)
                Unlocks.Set(session, Unlocks.GriefLevel, (uint)Math.Max(0, state.Level));
            if (saved.DecayedAt != state.DecayedAt)
                Unlocks.Set(session, Unlocks.GriefDecayedAt, state.DecayedAt);
            if (saved.SuspendedUntil != state.SuspendedUntil)
                Unlocks.Set(session, Unlocks.GriefSuspendedUntil, state.SuspendedUntil);
            state.Saved = (state.Points, state.DamagePoints, state.Level, state.DecayedAt, state.SuspendedUntil);
        }

        private static void SendPoints(WorldSession session, int points, int damagePoints)
            => Send(session, SetPointsSub, w => { w.Write(points); w.Write(damagePoints); });

        private static void Send(WorldSession session, byte sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(sub);
            body(w);
            session.EnqueueMessage(ZoneMessageOpcode.GriefBase, output.ToArray());
        }
    }
}
