using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Combat
{
    /// <summary>
    /// A blocking stance as the other clients see it (OPEN-LEADS L170; trace/0695/agent-l170-followup.md §2).
    /// <para>
    /// <b>What the client does [P, 0.695 decompile].</b> Two client checks look at a character that blocks:
    /// <list type="bullet">
    /// <item>the <b>Sweep</b> targeter (melee swings, FUN_14354d190) stops on any character in its arc whose entity state
    /// word (+0xa20) has <b>bit 45</b>: it plays composite effect 508 ("blocked") on the attacker and ends the sweep with
    /// ClientTargeterEnd, so no FindTarget follows for anyone. No facing or hostility test;</item>
    /// <item>the <b>Block</b> targeter (FUN_14354cda0 → FUN_14354dc10) needs bit 45 <b>and</b> an ability effect of type
    /// <see cref="BlockEffectType"/> ("Block", the effect-type name table FUN_142e7ee80) on the target, with the
    /// attacker's origin inside the effect's arc round the target's facing (cos(effect +0xa0 × 0.5°): +0xa0 is the
    /// <b>full</b> arc in degrees) and within half its height (|Δy| ≤ effect +0xa4 × 0.5). On a match it plays 508 and
    /// skips the cast's success effect: the attacker's "my hit was blocked".</item>
    /// </list>
    /// Bit 45 is written only by PlayerUpdate UpdateCharacterState 0x26/0x0F (the whole word) and
    /// <b>UpdateCharacterStateDelta 0x26/0x51</b> (set / clear masks). The Delta's reader FUN_142c2c7a0 (dispatcher
    /// FUN_142cdfa60 case 0x50, through FUN_142c4fb90, which refuses a body with bytes left over):
    /// <c>u16 0x26, u32 0x51, u64 guid (+0x18), u64 source (+0x20), u64 set (+0x28), u64 clear (+0x30), u32 time (+0x38)</c>.
    /// The handler finds the character by guid (FUN_142f85390; unknown guid = ignored) and queues {time, set, clear}
    /// (FUN_142ecdd20); FUN_142eb6a10 applies it once (client now − interpolation delay) − time ≥ 1 as
    /// <c>word = (word | set) &amp; ~clear</c>. Time is the server clock (ServerClock, the clock every client is synchronised
    /// to), so the bit changes when the attacker sees the blocker's stance, not before. Source: 0 (only a source equal to
    /// the local player's guid changes the timing). The whole-word 0x0F is never used here: it would also overwrite the bits
    /// the client keeps itself (bit 0 from the constructor, bit 7 from the spawn flags).
    /// </para>
    /// <para>
    /// The Block effect is put on the character by Abilities EffectStart 0x27/0x0C (reader FUN_142e028b0, handler
    /// FUN_142e19660: carrier = +0x78, looked up with FUN_142cd1cb0; packet +0x38 → effect type +0x20, +0x110 → +0xa0,
    /// +0x114 → +0xa4) and taken off by EffectEnd 0x27/0x0D (reader FUN_142e02440) with the same key {u64, u32, effect id}.
    /// An EffectStart whose key the carrier already has is ignored (FUN_142e19660 walks the list from vtable +0x220), so a
    /// refreshed stance is ended and started again. Layout: <see cref="Buffs.CharacterEffectStart"/>.
    /// </para>
    /// <para>
    /// <b>Ours.</b> The server stays the authority on damage: a player's stance is <see cref="Status.Block"/> (Molten Rebuke,
    /// Corrupting Revenge: <see cref="WeaponCombat.Incoming"/>, dot &gt; 0.2 from the facing), a creature's the shield block
    /// (<see cref="CreatureAbilities.Role.Block"/>, immune for <see cref="CreatureAbilities.BlockTime"/>). This class only
    /// tells the <b>other</b> clients, so their own swings agree: on start the Delta (set = bit 45) and the Block effect;
    /// on end (the block caught a hit, timed out, death) the Delta (clear = bit 45) and EffectEnd. A sweep the client stops
    /// sends no FindTarget for the blocker, so the server does no damage either; nothing strikes on an End without hits.
    /// <list type="bullet">
    /// <item>Who: the viewers in range (<see cref="Statuses.Viewers"/>: a player's landscape within 150 m, a creature's
    /// region viewers), never the blocker's own client (its sweeps never test the local player; the "usable while
    /// blocking" ability flags read the local effect list, which no ability of ours needs). The end goes to exactly the
    /// clients that were told the start, wherever they are now, so no bit is left set.</item>
    /// <item>Newcomers: a player shown the blocker while the stance lasts (<see cref="Introduce"/>: PlayerManager's
    /// introductions, a creature's spawn) gets the current state after the character itself.</item>
    /// <item>Bandwidth: packets only on a change (start, refresh, end), 42 + ~300 bytes per viewer; nothing per tick.</item>
    /// </list>
    /// Values [I]: arc <see cref="PlayerArc"/> = 157° = 2 × acos 0.2, the server's own head-on test; a creature's
    /// shield blocks every kind of damage (wiki: Barren Protector), so <see cref="CreatureArc"/> = 360°; height
    /// <see cref="Height"/> = 4 m for both. Live's values are not known (no capture, AbilityEffectTypeSettings.txt is
    /// header-only in 0.695). Whether live put bit 45 on creatures is open (L170); the 2016 notes only describe the
    /// players' stance (the next head-on hit does no damage, Corrupting Revenge, 25 May 2016).
    /// </para>
    /// Setting <see cref="WorldServerConfiguration.BlockStanceVisible"/> (Docker BLOCK_STANCE_VISIBLE, default true = live;
    /// dev "blockvisible on|off").
    /// </summary>
    public static class BlockStance
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Ability effect type "Block" (the hash FUN_14354dc10 compares, effect +0x20 == -0x3a7c77c8) [P].</summary>
        public const uint BlockEffectType = 0xC5838838;
        /// <summary>Entity state bit 45 (+0xa20 >> 45 &amp; 1: Sweep FUN_14354d190, Block targeter FUN_14354dc10) [P].</summary>
        public const ulong BlockingBit = 1UL << 45;
        /// <summary>A player's stance: full arc in degrees, 2 × acos 0.2 = the server's head-on test [I].</summary>
        public const float PlayerArc = 157f;
        /// <summary>A creature's shield block: every direction ("immune to all damage") [I].</summary>
        public const float CreatureArc = 360f;
        /// <summary>Block effect height in metres (the attacker's origin within half of it, up or down) [I].</summary>
        public const float Height = 4f;
        /// <summary>The effect's name for its id (<see cref="Buffs.EffectId"/>); the key is the blocker's guid.</summary>
        private const string EffectName = "block-stance";

        private static bool? visible;

        /// <summary>The setting BlockStanceVisible unless the dev command switched it.</summary>
        public static bool Visible
        {
            get => visible ?? LandmarkEmulator.Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.BlockStanceVisible ?? true;
            set => visible = value;
        }

        /// <summary>One visible stance: its numbers and the clients that were told it started.</summary>
        private sealed class Stance
        {
            public float Arc;
            public DateTime Until;
            /// <summary>A creature's stance ends by time here; a player's when the Block state ends (Statuses).</summary>
            public bool Creature;
            public readonly HashSet<WorldSession> Told = new();
        }

        private static readonly Dictionary<ulong, Stance> stances = new();
        private static readonly object sync = new();

        // ---- Start / end -------------------------------------------------------------------------------------------

        /// <summary>
        /// A player's Block state was put on or refreshed (<see cref="Statuses.Apply"/>): the other clients in range are told,
        /// or, for a refresh, get the effect again with the new duration.
        /// </summary>
        public static void PlayerBlocked(ulong player, float seconds) => Start(player, PlayerArc, seconds, creature: false);

        /// <summary>A creature raised its shield (<see cref="Creatures.StoryUseAbility"/>), immune for <paramref name="time"/>.</summary>
        public static void CreatureBlocked(ulong creature, TimeSpan time) => Start(creature, CreatureArc, (float)time.TotalSeconds, creature: true);

        private static void Start(ulong guid, float arc, float seconds, bool creature)
        {
            if (!Visible)
                return;
            // Viewers first, outside this lock (Creatures' own lock is taken there).
            List<WorldSession> viewers = ViewersOf(guid);
            uint now = (uint)ServerClock.Milliseconds;
            bool refresh;
            HashSet<WorldSession> before;
            List<WorldSession> told;
            lock (sync)
            {
                refresh = stances.TryGetValue(guid, out Stance s);
                if (!refresh)
                    stances[guid] = s = new Stance();
                before = new HashSet<WorldSession>(s.Told);
                s.Arc = arc;
                s.Until = DateTime.UtcNow.AddSeconds(seconds);
                s.Creature = creature;
                foreach (WorldSession v in viewers)
                    s.Told.Add(v);
                told = s.Told.Where(v => !v.Disconnected).ToList();
            }
            byte[] set = DeltaBody(guid, BlockingBit, 0, now);
            byte[] effectEnd = Buffs.CharacterEffectEnd(guid, EffectName);
            byte[] effect = Buffs.CharacterEffectStart(guid, EffectName, BlockEffectType, arc, Height, seconds, now);
            foreach (WorldSession v in told)
            {
                // A refresh: those told before already have the bit; their effect is ended and started again with the new
                // duration (the client ignores an EffectStart whose key the character already carries). New viewers get both.
                if (before.Contains(v))
                    v.EnqueueMessage(ZoneMessageOpcode.AbilityEffectEnd, effectEnd);
                else
                    v.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire, set);
                v.EnqueueMessage(ZoneMessageOpcode.AbilityEffectStart, effect);
            }
            log.Debug($"Block stance {guid:X}: {(refresh ? "refreshed" : "started")} for {seconds:0.#} s, {told.Count} viewers.");
        }

        /// <summary>The player's Block state ended (caught a hit, ran out, death): everyone told the start is told the end.</summary>
        public static void PlayerUnblocked(ulong player) => End(player);

        private static void End(ulong guid)
        {
            List<WorldSession> told;
            lock (sync)
            {
                if (!stances.Remove(guid, out Stance s))
                    return;
                told = s.Told.Where(v => !v.Disconnected).ToList();
            }
            if (told.Count == 0)
                return;
            byte[] clear = DeltaBody(guid, 0, BlockingBit, (uint)ServerClock.Milliseconds);
            byte[] effectEnd = Buffs.CharacterEffectEnd(guid, EffectName);
            foreach (WorldSession v in told)
            {
                v.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire, clear);
                v.EnqueueMessage(ZoneMessageOpcode.AbilityEffectEnd, effectEnd);
            }
            log.Debug($"Block stance {guid:X} ended, {told.Count} viewers.");
        }

        /// <summary>
        /// <paramref name="viewer"/> has just been shown <paramref name="subject"/> (a player introduced, a creature
        /// spawned): a stance still up is sent after the character, so the client has the entity to put it on.
        /// </summary>
        public static void Introduce(WorldSession viewer, ulong subject)
        {
            Stance s;
            float left;
            lock (sync)
            {
                if (!stances.TryGetValue(subject, out s))
                    return;
                left = (float)(s.Until - DateTime.UtcNow).TotalSeconds;
                if (left <= 0)
                    return;
                s.Told.Add(viewer);
            }
            uint now = (uint)ServerClock.Milliseconds;
            viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire, DeltaBody(subject, BlockingBit, 0, now));
            viewer.EnqueueMessage(ZoneMessageOpcode.AbilityEffectStart,
                Buffs.CharacterEffectStart(subject, EffectName, BlockEffectType, s.Arc, Height, left, now));
        }

        /// <summary>A player shown another player: only within the stance's range (<see cref="Statuses.Viewers"/>' 150 m).</summary>
        public static void Introduce(WorldSession viewer, WorldSession subject)
        {
            if (viewer != subject && viewer.ZoneId == subject.ZoneId && Vector3.Distance(viewer.Position, subject.Position) < ViewRange)
                Introduce(viewer, subject.PlayerGuid);
        }

        /// <summary>Range for players (as <see cref="Statuses.Viewers"/>, the state effects' range).</summary>
        private const float ViewRange = 150f;

        /// <summary>
        /// A blocker left the world or a creature died / despawned: its entity is gone from every client with it, so the
        /// stance is dropped without packets. A leaving player is also dropped from the told lists.
        /// </summary>
        public static void Forget(ulong guid)
        {
            lock (sync)
                stances.Remove(guid);
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                stances.Remove(session.PlayerGuid);
                foreach (Stance s in stances.Values)
                    s.Told.Remove(session);
            }
        }

        /// <summary>From <see cref="Statuses.Update"/> (4 × a second): creatures' shields that ran out.</summary>
        public static void Update(DateTime now)
        {
            List<ulong> over;
            lock (sync)
            {
                if (stances.Count == 0)
                    return;
                over = stances.Where(p => p.Value.Creature && p.Value.Until <= now).Select(p => p.Key).ToList();
            }
            foreach (ulong guid in over)
                End(guid);
        }

        /// <summary>The dev command's view: the stances up now.</summary>
        public static string DevState()
        {
            lock (sync)
                return stances.Count == 0 ? "none"
                    : string.Join("; ", stances.Select(p => $"{p.Key:X} arc {p.Value.Arc} until {p.Value.Until:HH:mm:ss.f}, {p.Value.Told.Count} told"));
        }

        // ---- Packets -----------------------------------------------------------------------------------------------

        /// <summary>The viewers who get a stance: <see cref="Statuses.Viewers"/> without the blocker; none for a hidden admin.</summary>
        private static List<WorldSession> ViewersOf(ulong guid)
        {
            if (PlayerManager.FindByGuid(guid) is WorldSession p && AdminCommands.IsHidden(p))
                return new List<WorldSession>();
            return Statuses.Viewers(guid).Where(v => v.PlayerGuid != guid && !v.Disconnected).ToList();
        }

        /// <summary>
        /// UpdateCharacterStateDelta body after the opcode (26 00 51 00 00 00, session): u64 guid, u64 source = 0,
        /// u64 set, u64 clear, u32 time (server ms). 36 bytes; the reader wants exactly these (FUN_142c4fb90).
        /// </summary>
        internal static byte[] DeltaBody(ulong guid, ulong set, ulong clear, uint time)
        {
            var output = new List<byte>(36);
            var w = new GamePacketWriter(output);
            w.Write(guid);
            w.Write(0ul);
            w.Write(set);
            w.Write(clear);
            w.Write(time);
            return output.ToArray();
        }
    }
}
