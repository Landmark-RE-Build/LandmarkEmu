using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player hit points, death and respawn (the server owns them; the client only shows them).
    /// - ClientUpdate 0x01 Hitpoints {u32 current, u32 max} drives the health bar (client FUN_142b83000).
    /// - At 0 the server sends EncounterBase 0x2C {i16 0x7D, u32 pending, u32 encounter} (Encounters.SendDeathWindow) → "You have died." window (RespawnHandler:Show);
    ///   {u16 0x7E} hides it.
    /// - The window answers with PlayerUpdate 0x3B Respawn {u32 type, ...}: 10 = revive here (costs
    ///   <see cref="ReviveCost"/> Lumen, item 1579, the client checks the count first), 11/12 = nearest safe point / hub:
    ///   the nearest graveyard of the layer the player died on (<see cref="Graveyards"/>: a cavern graveyard in the Chaos
    ///   Caverns, a surface graveyard / respawn point / the Spire / their site above ground).
    /// </summary>
    public static class Health
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Base maximum hit points, with no body armour: the player's Life resource (Resources.txt id 4, type 1 Health,
        /// MAX_VALUE 900). The real maximum is <see cref="Max"/> (armour weight raises it, Combat.GearStats); this
        /// constant is what the client falls back to when the Life record carries no MAX_VALUE variable.
        /// </summary>
        public const uint MaxHealth = 900;

        /// <summary>
        /// The player's maximum hit points now: 900 plus the worn armour's life bonus (Combat.GearStats, TASKS 5.1).
        /// This is what the HUD bar, the character sheet's Health line and other players' views of this player show
        /// (sent as the Life record's MAX_VALUE variable, see <see cref="SendHitpoints"/>).
        /// </summary>
        public static uint Max(WorldSession session) => Combat.GearStats.MaxHealth(session);

        /// <summary>
        /// Resources.txt Life (row 4) PACKET_BROADCAST_RANGE: 30 m. Changes to a player's life go to the other players
        /// this close as well (their target frame / nameplate bar reads the entity's Life resource, FUN_143711bb0 /
        /// FUN_143b8c310); farther players get it when they next meet the player (<see cref="IntroduceDead"/>).
        /// </summary>
        private const float LifeBroadcastRange = 30f;
        private const uint HealthResourceId = 4, HealthResourceType = 1;
        public const uint LumenItem = 1579, ReviveCost = 15;

        /// <summary>
        /// Reviving another player (player right-click "Revive Player", Lua Ui.RevivePlayer FUN_14351ace0): the client's
        /// action manager casts Abilities.ReviveInteractionAbilityId (ResourceConstants 246) at the dead player, but only
        /// when its copy of that player holds RevivePoints (resource type 12; Resources.txt id 29, 50 / 50) above 0
        /// (FUN_1433abc00 → FUN_143ad04e0(entity resources, 12)). So a death tells every client on the landscape the dead
        /// player's RevivePoints, and the cast revives them in place. 0.589 has no accept prompt (UI.DoYouAcceptRevive
        /// is only a text mapping, no code uses it) and no point cost is defined, so the points are not spent.
        /// </summary>
        public const uint ReviveInteractionAbility = 246, RevivePointsId = 29, RevivePointsType = 12, RevivePoints = 50;
        /// <summary>How close the reviver must stand (metres).</summary>
        private const float ReviveRange = 8f;
        public const uint RespawnHere = 10;
        /// <summary>Death window "graveyard" choice (free): respawn at the nearest respawn site (Graveyards).</summary>
        public const uint RespawnSafePoint = 11;
        /// <summary>
        /// Death window "Flee" (Lua ReviveFlee). The window shows its free respawn button when a type-11 location is
        /// listed (FUN_1433878f0), labelled Flee; the click requests type 12 and the client then loops until it finds a
        /// type-12 location (FUN_143387ee0): without one it hung for good. Both go to the nearest respawn site.
        /// </summary>
        public const uint RespawnFlee = 12;

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, uint> current = new();
        private static readonly HashSet<WorldSession> dead = new();

        /// <summary>
        /// After a revive creatures ignore the player for a few seconds (hotfix 26 Apr 2016, P4717: a few seconds to
        /// recover after a revive before creatures attack again), and that short invisibility to creatures ends when
        /// the player enters combat (16 Jun 2016, P5832). Live's length is unknown (L89): ours.
        /// </summary>
        public static readonly TimeSpan ReviveGrace = TimeSpan.FromSeconds(5);

        /// <summary>When each revived player's grace ends.</summary>
        private static readonly Dictionary<WorldSession, DateTime> graceUntil = new();

        /// <summary>Whether creatures ignore this player (just revived, not yet fighting).</summary>
        public static bool Unnoticed(WorldSession session)
        {
            lock (sync)
            {
                if (!graceUntil.TryGetValue(session, out DateTime until))
                    return false;
                if (DateTime.UtcNow < until)
                    return true;
                graceUntil.Remove(session);
                return false;
            }
        }

        /// <summary>The player attacked a creature: the post-revive invisibility drops (P5832).</summary>
        public static void EnteredCombat(WorldSession session)
        {
            if (session == null)
                return;
            lock (sync)
                graceUntil.Remove(session);
        }

        /// <summary>
        /// A player's current hit points (full when not tracked yet).
        /// </summary>
        public static uint Current(WorldSession session)
        {
            lock (sync)
                if (current.TryGetValue(session, out uint hp))
                    return hp;
            return Max(session);
        }

        /// <summary>
        /// Is the player dead (death window shown, waiting to respawn)?
        /// </summary>
        public static bool IsDead(WorldSession session)
        {
            lock (sync)
                return dead.Contains(session);
        }

        /// <summary>
        /// Sends the health bar on world entry (with the armour's maximum) and records the armour stats the client now
        /// has, so equipment changes later send only what changed (Combat.GearStats.Remember).
        /// </summary>
        public static void SendInitial(WorldSession session)
        {
            Combat.GearStats.Remember(session);
            // Others already met this player (PlayerManager.Introduce → IntroduceDead), so only the player is told.
            SendHitpoints(session, Current(session), broadcast: false);
        }

        /// <summary>
        /// Drops a player's health state when they leave; they come back with full health.
        /// </summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                current.Remove(session);
                dead.Remove(session);
                graceUntil.Remove(session);
                reviveOnArrival.Remove(session);
                CharacterResources.Forget(session);
                WeaponAttacks.Forget(session);
                lastHurt.Remove(session);
                lastEvacuation.Remove(session);
            }
            Combat.GearStats.Forget(session);
        }

        /// <summary>
        /// Takes hit points away; at 0 the player dies. <paramref name="attacker"/> is the player who dealt the damage
        /// (a site game's PvP hit), null for creatures, falls and the like; <paramref name="sourceGuid"/> is the
        /// creature that dealt it (0 when none). With a source the victim is told who hit them (LastDamageTime), which
        /// is what draws the client's damage-direction arrows round the reticle.
        /// </summary>
        /// <summary>Dev / tests: players who take no damage (dev "god on").</summary>
        public static readonly HashSet<WorldSession> Invulnerable = new();

        /// <summary>Dev / tests: alive again with full health where they stand, death window closed.</summary>
        public static void DevRevive(WorldSession session)
        {
            if (!IsDead(session))
                return;
            Revive(session);
            Encounters.SendDeathWindow(session, false);
        }

        public static void Damage(WorldSession session, uint amount, WorldSession attacker = null, ulong sourceGuid = 0)
        {
            uint hp, lost;
            bool died;
            lock (sync)
            {
                if (dead.Contains(session) || Invulnerable.Contains(session))
                    return;
                hp = Current(session);
                lost = Math.Min(amount, hp);
                hp -= lost;
                lastHurt[session] = DateTime.UtcNow;
                current[session] = hp;
                died = hp == 0;
                if (died)
                    dead.Add(session);
            }
            SendHitpoints(session, hp);
            ulong source = attacker?.PlayerGuid ?? sourceGuid;
            if (source != 0 && source != session.PlayerGuid)
                SendLastDamage(session, source);
            if (attacker != null && attacker != session)
                HitConfirmed(attacker);
            if (lost > 0)
                Outposts.Interrupt(session);
            Durability.Damaged(session, lost, died);
            // Armour that just wore down to 0 % gives half its life / damage bonus (Combat.GearStats).
            if (lost > 0 && !died)
                Combat.GearStats.Refresh(session);
            if (died)
            {
                // A player in a running site game respawns at the game's spawn points instead (no death window).
                if (InstancedGames.Died(session, attacker))
                    return;
                SendRespawnLocations(session);
                Encounters.SendDeathWindow(session, true);
                byte[] knockedOut = BuildKnockedOut(session, attacker);
                foreach (WorldSession viewer in PlayerManager.InZone(session.ZoneId))
                {
                    CharacterResources.SendFor(viewer, session.PlayerGuid, RevivePointsId, RevivePointsType, RevivePoints);
                    viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateKnockedOutWire, knockedOut);
                }
                log.Info($"{session.CharacterName} died.");
            }
        }

        /// <summary>
        /// Combat LastDamageTime (CombatBase 0x24 → bytes 24 00, i16 sub 0x0D; reader FUN_14331e310, the combat
        /// dispatcher FUN_14331da00 case 0x0C): {u64 attacker}. The client (FUN_142e248b0) stamps
        /// GrapplingHook.LastDamageTime with the time of the hit and hands the attacker to the ReticleManager
        /// (FUN_143156ad0): when the attacker is the player's current target the reticle flashes its hit marker
        /// (GameEvents:ShowReticleHitMarker), otherwise it works out the attacker's direction from the camera and puts it
        /// in the next of the three Reticle.HitIndicator1-3 rows ("Value" = degrees), which the HUD's reticle shows as an
        /// arrow fading over Reticle.HitIndicatorFadeTime (1000 ms). Live sent it with every hit a player took, and to
        /// the player who landed a hit with their own guid (see <see cref="HitConfirmed"/>).
        /// </summary>
        private static void SendLastDamage(WorldSession session, ulong attacker)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((ushort)0x0D);
            w.Write(attacker);
            session.EnqueueMessage(ZoneMessageOpcode.CombatBase, output.ToArray());
        }

        /// <summary>
        /// A player's attack landed (on a creature or another player): LastDamageTime with the player's own guid, which
        /// the client's ReticleManager (FUN_143156ad0) turns into GameEvents:ShowReticleHitMarker — the Combat HUD's
        /// reticle flashes its hit marker (events.lua ShowReticleHitMarker → CombatHudWidget.showReticleHitMarker).
        /// </summary>
        public static void HitConfirmed(WorldSession attacker) => SendLastDamage(attacker, attacker.PlayerGuid);

        /// <summary>Was the player hurt within the last <paramref name="span"/> (in combat)?</summary>
        public static bool HurtWithin(WorldSession session, TimeSpan span)
        {
            lock (sync)
                return lastHurt.TryGetValue(session, out DateTime at) && DateTime.UtcNow - at < span;
        }

        /// <summary>Regeneration of the health resource (Resources.txt REGEN_PER_MS 0.05 → 50 per second).</summary>
        private const uint RegenPerSecond = 50;
        /// <summary>No regeneration this long after taking damage (out of combat only).</summary>
        private static readonly TimeSpan RegenDelay = TimeSpan.FromSeconds(8);
        private static readonly Dictionary<WorldSession, DateTime> lastHurt = new();
        private static DateTime nextRegen = DateTime.MinValue;

        /// <summary>Server tick: once a second, wounded living players regenerate (one small packet each).</summary>
        public static void Update()
        {
            UpdateRevivals();
            DateTime now = DateTime.UtcNow;
            if (now < nextRegen)
                return;
            nextRegen = now.AddSeconds(1);
            List<(WorldSession Session, uint Hp)> tracked;
            lock (sync)
                tracked = current.Where(p => !dead.Contains(p.Key)
                    && (!lastHurt.TryGetValue(p.Key, out DateTime hurt) || now - hurt > RegenDelay)).Select(p => (p.Key, p.Value)).ToList();
            // The maximum depends on the armour (inventory), so it is read outside the lock.
            foreach (var (session, hp) in tracked)
                if (hp < Max(session))
                    Heal(session, RegenPerSecond);
        }

        /// <summary>
        /// Restores hit points (regeneration), up to the maximum; nothing for the dead, and nothing sent when already at
        /// full health.
        /// </summary>
        public static void Heal(WorldSession session, uint amount)
        {
            uint hp, max = Max(session);
            lock (sync)
            {
                if (dead.Contains(session) || Current(session) >= max)
                    return;
                hp = Math.Min(max, Current(session) + amount);
                current[session] = hp;
            }
            SendHitpoints(session, hp);
        }

        /// <summary>
        /// Brings a dead player back with full health where they are (a site game's respawn moves them afterwards).
        /// </summary>
        public static void Revive(WorldSession session)
        {
            uint max = Max(session);
            lock (sync)
            {
                dead.Remove(session);
                current[session] = max;
                graceUntil[session] = DateTime.UtcNow + ReviveGrace;
            }
            SendHitpoints(session, max);
        }

        /// <summary>
        /// The armour's maximum life changed (equip / unequip / armour broke; Combat.GearStats.Refresh): the player keeps
        /// the same share of their life (full stays full, so swapping armour neither heals nor hurts; [I] ours), and the
        /// bar is sent again with the new maximum. A dead player stays at 0.
        /// </summary>
        public static void MaxChanged(WorldSession session, uint oldMax, uint newMax)
        {
            uint hp;
            lock (sync)
            {
                if (dead.Contains(session))
                    hp = 0;
                else if (current.TryGetValue(session, out uint now))
                {
                    hp = oldMax == 0 ? newMax : (uint)Math.Round((double)now * newMax / oldMax);
                    hp = Math.Clamp(hp, 1u, newMax);
                    current[session] = hp;
                }
                else
                    hp = newMax;                       // untracked = full
            }
            SendHitpoints(session, hp);
        }

        /// <summary>
        /// Meeting a player (PlayerManager.Introduce): a dead player's RevivePoints, and the player's Life when it isn't
        /// the default full 900 (hurt, or a maximum raised by armour), so the newcomer's target frame / nameplate shows
        /// the right bar. A full, unarmoured player costs nothing: the client already assumes Resources.txt's 900.
        /// </summary>
        public static void IntroduceDead(WorldSession viewer, WorldSession subject)
        {
            if (IsDead(subject))
                CharacterResources.SendFor(viewer, subject.PlayerGuid, RevivePointsId, RevivePointsType, RevivePoints);
            uint max = Max(subject), hp = IsDead(subject) ? 0 : Current(subject);
            if (viewer != subject && (hp != max || max != MaxHealth))
                SendLife(viewer, subject, hp, max);
        }

        /// <summary>A revive in progress: who revives, when the channel ends, and whether ReadyToRevive went out.</summary>
        private sealed class Revival
        {
            public WorldSession Reviver;
            public DateTime ReadyAt;
            public bool Asked;
        }

        /// <summary>Revives in progress, by the dead player.</summary>
        private static readonly Dictionary<WorldSession, Revival> reviving = new();

        /// <summary>
        /// How long a revive channels before the dead player is asked (StartRevive's duration). Ability 246 is server-only
        /// data (not in AbilityEx.txt), so the length is ours.
        /// </summary>
        private const uint ReviveMilliseconds = 3000;
        /// <summary>Players this close to the dead body are told about the revive (their clients record the reviver).</summary>
        private const float ReviveViewRange = 100f;
        /// <summary>CombatSpam.txt row 8 "Revive": 564592 "YOU have been revived.", 564593 "YOU have revived [*TargetPlayer*].".</summary>
        private const uint RevivedText = 564592, YouRevivedText = 564593;

        /// <summary>
        /// The revive cast (246) from <paramref name="reviver"/> at a dead player nearby. As the live server's packets
        /// read in the client's PlayerUpdate dispatcher (FUN_142c45fa0):
        /// 1. StartRevive (0x42 {u64 dead, u64 reviver, u32 duration ms}, reader FUN_142bb40f0) to the players near the
        ///    body: every client records the reviver, start time and duration on its copy of the dead player
        ///    (FUN_142e2abe0: +0x1560 / +0x1550 / +0x1558), and the reviver's own client shows the HUD revive bar for the
        ///    dead player's name (FUN_142ea2cc0 mode 2).
        /// 2. After the duration, ReadyToRevive (0x44 {u64 dead, u64 reviver, u64, bool, u32}) to the dead player, whose
        ///    client records the reviver and answers ReadyToReviveResponse(1) by itself (0.589 shows no prompt); the answer
        ///    revives them. When an entity comes back to life the client can print CombatSpam "Revive" with the recorded
        ///    reviver (entity vtable +0x488 → FUN_142e20a10 → FUN_142f43730).
        /// 3. StopRevive (0x43 {u64 dead, u64 reviver, bool}, reader FUN_142bb4370) if the revive breaks off (the reviver
        ///    dies, leaves or walks away, the dead player respawns): hides the reviver's bar (FUN_142ea23c0) and clears the
        ///    dead player's record (FUN_142e2b210).
        /// </summary>
        public static void ReviveBy(WorldSession reviver, WorldSession target)
        {
            if (target == null || target == reviver || !IsDead(target) || IsDead(reviver) || target.ZoneId != reviver.ZoneId
                || System.Numerics.Vector3.Distance(target.Position, reviver.Position) > ReviveRange)
            {
                Chat.System(reviver, "There is no one here to revive.");
                return;
            }
            lock (sync)
            {
                if (reviving.ContainsKey(target))
                    return;
                reviving[target] = new Revival { Reviver = reviver, ReadyAt = DateTime.UtcNow.AddMilliseconds(ReviveMilliseconds) };
            }
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(target.PlayerGuid);
            w.Write(reviver.PlayerGuid);
            w.Write(ReviveMilliseconds);
            byte[] start = output.ToArray();
            foreach (WorldSession viewer in NearBody(target, reviver))
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateStartReviveWire, start);
            log.Info($"{reviver.CharacterName} is reviving {target.CharacterName}.");
        }

        /// <summary>The players near a dead body, plus the reviver and the dead player themselves.</summary>
        private static IEnumerable<WorldSession> NearBody(WorldSession target, WorldSession reviver)
            => PlayerManager.InZone(target.ZoneId)
                .Where(p => p == target || p == reviver
                    || System.Numerics.Vector3.Distance(p.Position, target.Position) <= ReviveViewRange);

        /// <summary>Revives whose channel ended ask the dead player; broken-off ones are stopped.</summary>
        private static void UpdateRevivals()
        {
            DateTime now = DateTime.UtcNow;
            var ask = new List<(WorldSession Target, WorldSession Reviver)>();
            var stop = new List<(WorldSession Target, WorldSession Reviver)>();
            lock (sync)
            {
                if (reviving.Count == 0)
                    return;
                foreach (var (target, revival) in reviving.ToList())
                {
                    WorldSession reviver = revival.Reviver;
                    bool broken = !dead.Contains(target) || target.Disconnected || reviver.Disconnected || dead.Contains(reviver)
                        || reviver.ZoneId != target.ZoneId
                        || System.Numerics.Vector3.Distance(target.Position, reviver.Position) > ReviveRange * 2;
                    if (broken)
                    {
                        reviving.Remove(target);
                        stop.Add((target, reviver));
                    }
                    else if (!revival.Asked && now >= revival.ReadyAt)
                    {
                        revival.Asked = true;
                        ask.Add((target, reviver));
                    }
                }
            }
            foreach (var (target, reviver) in stop)
            {
                var output = new List<byte>();
                var w = new GamePacketWriter(output);
                w.Write(target.PlayerGuid);
                w.Write(reviver.PlayerGuid);
                w.Write(false);
                byte[] packet = output.ToArray();
                foreach (WorldSession viewer in NearBody(target, reviver))
                    viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateStopReviveWire, packet);
                log.Info($"{reviver.CharacterName} stopped reviving {target.CharacterName}.");
            }
            foreach (var (target, reviver) in ask)
            {
                var output = new List<byte>();
                var w = new GamePacketWriter(output);
                w.Write(target.PlayerGuid);
                w.Write(reviver.PlayerGuid);
                w.Write(0ul);
                w.Write(false);
                w.Write(0u);
                target.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateReadyToReviveWire, output.ToArray());
            }
        }

        /// <summary>ReadyToReviveResponse (0x3D {u8 accept, u64 own guid}): accepted, the player is revived in place.</summary>
        public static void HandleReadyToReviveResponse(WorldSession session, byte[] body)
        {
            bool accept = body.Length > 0 && body[0] != 0;
            WorldSession reviver = null;
            lock (sync)
                if (reviving.TryGetValue(session, out Revival revival) && revival.Asked)
                {
                    reviving.Remove(session);
                    reviver = revival.Reviver;
                }
            log.Info($"{session.CharacterName} answered the revive: {(accept ? "accept" : "decline")} ({BitConverter.ToString(body)}).");
            if (reviver == null || !IsDead(session))
                return;
            if (!accept)
            {
                Chat.System(reviver, $"{session.CharacterName} declined the revive.");
                return;
            }
            Revive(session);
            Encounters.SendDeathWindow(session, false);
            // The other clients still hold the knocked-out entity: they meet the player again, alive.
            PlayerManager.Reshow(session);
            SystemMessages.Show(session, RevivedText, ("SourcePlayer", reviver.CharacterName));
            SystemMessages.Show(reviver, YouRevivedText, ("TargetPlayer", session.CharacterName));
            log.Info($"{reviver.CharacterName} revived {session.CharacterName}.");
        }

        /// <summary>
        /// KnockedOut (PlayerUpdate 0x39; reader FUN_142b8efc0): what every client on the landscape is told when a
        /// player goes down: u64 victim, victim info, a stat block, killer info, a stat block, a damage report, u32.
        /// The client adds a kill-feed line and runs the victim's own knocked-out handling (entity vtable +0x3f0), which
        /// is what makes the body revivable on other screens.
        /// Character info (FUN_142b8bda0): string name, u64 guid, u32 x 4, u8, u32 x 9, {list of 3-u32 entries, u32,
        /// string, u32, u32}, u32 x 7, u64, u32 x 2, u32 x 3, u32. Stat block (FUN_142b96d60): u64, u32 x 12. Damage
        /// report (FUN_142b84fd0): u8 x 2, u32, u64 x 3, u32 x 8, u32 x 4, u32 x 4.
        /// </summary>
        private static byte[] BuildKnockedOut(WorldSession victim, WorldSession killer)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            void Info(string name, ulong guid)
            {
                w.Write(name ?? "");
                w.Write(guid);
                for (int i = 0; i < 4; i++) w.Write(0u);
                w.Write((byte)0);
                for (int i = 0; i < 9; i++) w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write("");
                w.Write(0u); w.Write(0u);
                for (int i = 0; i < 7; i++) w.Write(0u);
                w.Write(0ul);
                w.Write(0u); w.Write(0u);
                for (int i = 0; i < 3; i++) w.Write(0u);
                w.Write(0u);
            }
            void Stats(ulong guid)
            {
                w.Write(guid);
                for (int i = 0; i < 12; i++) w.Write(0u);
            }
            w.Write(victim.PlayerGuid);
            Info(victim.CharacterName, victim.PlayerGuid);
            Stats(victim.PlayerGuid);
            Info(killer?.CharacterName ?? "", killer?.PlayerGuid ?? 0ul);
            Stats(killer?.PlayerGuid ?? 0ul);
            w.Write((byte)0); w.Write((byte)0);
            w.Write(0u);
            w.Write(killer?.PlayerGuid ?? 0ul);
            w.Write(victim.PlayerGuid);
            w.Write(0ul);
            for (int i = 0; i < 16; i++) w.Write(0u);
            w.Write(0u);
            return output.ToArray();
        }

        private static readonly HashSet<WorldSession> reviveOnArrival = new();

        /// <summary>A re-zone finished (Teleport.FinishRezone): a respawn that moved the player shows them alive now.</summary>
        public static void Arrived(WorldSession session)
        {
            bool revive;
            lock (sync)
                revive = reviveOnArrival.Remove(session);
            if (!revive)
                return;
            SendHitpoints(session, Current(session));
            Encounters.SendDeathWindow(session, false);
        }

        /// <summary>The death window's choice. Reviving in place costs Lumen; the other choices are free.</summary>
        /// <summary>When each player last asked to be evacuated (at most once every 10 s).</summary>
        private static readonly Dictionary<WorldSession, DateTime> lastEvacuation = new();

        /// <summary>
        /// ZoneSafeTeleportRequest: the client found its player outside the zone (fell out of the world) or asked for its
        /// safe point. Moves them to the nearest respawn site (<see cref="Graveyards"/>), as the death window's free
        /// respawn does; with none, back onto the ground where they are. Throttled: the client may repeat it every frame
        /// until the move arrives.
        /// </summary>
        public static void EvacuateToSafePoint(WorldSession session)
        {
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                if (lastEvacuation.TryGetValue(session, out DateTime last) && now - last < TimeSpan.FromSeconds(10))
                    return;
                lastEvacuation[session] = now;
            }
            if (session.RezoneTarget != null)
                return;
            Graveyards.Site site = Graveyards.Nearest(session, session.Position);
            if (site != null)
                Teleport.ToZone(session, site.Position, site.Zone, site.Facing, snapToGround: !site.Underground);
            else
                Teleport.To(session, session.Position, snapToGround: true);
            log.Info($"{session.CharacterName} evacuated to {site?.Name ?? "the ground below"} (safe point request).");
        }

        public static void HandleRespawn(WorldSession session, uint type)
        {
            if (!IsDead(session))
                return;
            if (type == RespawnHere && !session.TakeItem(LumenItem, ReviveCost))
            {
                Chat.System(session, $"You need {ReviveCost} Lumen to revive here.");
                return;
            }
            uint max = Max(session);
            lock (sync)
            {
                dead.Remove(session);
                current[session] = max;
                graceUntil[session] = DateTime.UtcNow + ReviveGrace;
            }
            // The graveyard choice: the respawn site nearest to where the player fell.
            Graveyards.Site site = type != RespawnHere ? StoryTools.RespawnSite(session, session.Position, respawning: true) : null;
            if (site != null)
            {
                // Back at a site's Starting Location: its "When a Luminary respawns at this Build Site..." Links run.
                if (site.Name == "Build Site" && site.ClaimId != 0)
                    StoryTools.ExpectArrival(session, site.ClaimId, respawn: true);
                // A cavern graveyard: no snap to the terrain (the client would put the player on the surface above).
                Teleport.ToZone(session, site.Position, site.Zone, site.Facing, snapToGround: !site.Underground);
            }
            log.Info($"{session.CharacterName} respawned (type {type}) at {site?.Name ?? "the spot they fell"}.");
            // Moved: the hit points and the window's close wait until the re-zone has finished (Arrived). Sent in the
            // same burst as ClientBeginZoning, hit points could hang the client (docs/terrain.md, long teleports).
            if (session.RezoneTarget != null)
            {
                lock (sync)
                    reviveOnArrival.Add(session);
                return;
            }
            SendHitpoints(session, max);
            Encounters.SendDeathWindow(session, false);
        }

        /// <summary>
        /// Offers the free respawn next to "Revive Here" (which needs Lumen): the nearest respawn site
        /// (<see cref="Graveyards"/>; where the player fell when there is none), listed twice: as type 12 first (the
        /// Flee click looks for that type and hangs the client without it, see <see cref="RespawnFlee"/>) and as type 11
        /// (which makes the window show the button).
        /// </summary>
        private static void SendRespawnLocations(WorldSession session)
        {
            Graveyards.Site site = StoryTools.RespawnSite(session, session.Position, respawning: false);
            Vector3 at = site?.Position ?? session.Position;
            string name = site?.Name ?? "Respawn";
            var message = new Network.Message.Model.ClientUpdateRespawnLocations();
            foreach (uint type in new[] { RespawnFlee, RespawnSafePoint })
                message.RespawnLocations.Add(new()
                {
                    Guid = session.PlayerGuid,
                    Type = (byte)type,
                    Position = new Vector4(at, 1f),
                    Name = name
                });
            session.EnqueueMessage(message);
        }

        /// <summary>
        /// Updates the player's health: the HUD bar (the Life resource, with the armour's maximum) and ClientUpdate
        /// Hitpoints {u32 current, u32 max} (FUN_142c1c7c0; the dispatcher only builds a combat-text entry from it, it
        /// does not move the bar). Other players within <see cref="LifeBroadcastRange"/> get the Life resource too.
        /// </summary>
        private static void SendHitpoints(WorldSession session, uint hp, bool broadcast = true)
        {
            uint max = Max(session);
            SendLife(session, session, hp, max);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(hp);
            writer.Write(max);
            session.EnqueueMessage(ZoneMessageOpcode.ClientUpdateHitpoints, output.ToArray());
            if (!broadcast)
                return;
            foreach (WorldSession viewer in PlayerManager.InZone(session.ZoneId))
                if (viewer != session && Vector3.Distance(viewer.Position, session.Position) <= LifeBroadcastRange)
                    SendLife(viewer, session, hp, max);
        }

        /// <summary>
        /// The Life resource of <paramref name="subject"/> on <paramref name="viewer"/>'s client (see
        /// <see cref="CharacterResources"/>). The MAX_VALUE variable rides along only when the maximum isn't the table's
        /// 900: a Set replaces the record's variables, so leaving it out resets the client to 900 (unequipped armour).
        /// </summary>
        private static void SendLife(WorldSession viewer, WorldSession subject, uint hp, uint max)
            => CharacterResources.SendFor(viewer, subject.PlayerGuid, HealthResourceId, HealthResourceType, hp,
                max == MaxHealth ? null : (MaxHealth, max));

    }
}
