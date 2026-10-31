using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A dead player's body as the other clients see it (OPEN-LEADS L196 / docs/observer-sync.md OL-2).
    /// <para>
    /// <b>What the client does [P, 0.695 decompile].</b> The player animation network has a control parameter
    /// <c>State_Dead</c> (PlayerAnimationNetwork64.mrn ControlParameters|State_Dead; 26 conditions "State_Dead &gt; 0.5"
    /// drive Death_01 / Dead, SwimDeath …). Every frame the character update (FUN_142eb1950, at 0x142eb24de-0x142eb250a)
    /// sets it from the entity's knocked-out test — the +0x250 interface's vtable +0xb0, FUN_142ea9ef0: entity state word
    /// +0xa20 <b>bit 7</b> — and the neighbouring State_Stun / KnockedDown / KnockedBack / Snared / Frozen / Staggered from
    /// bits 6, 38, 9, 39, 13 and 23. So nothing else holds a body down: AnimationSelection 156 "Death" (variable
    /// State_Dead 1) is overwritten on the next frame, and the KnockedOut packet's handler for a remote character
    /// (ProxiedCharacter vtable +0x3f0, FUN_142ead890) is empty. The bit is written only by the spawn flags and
    /// PlayerUpdate UpdateCharacterState 0x0F / <b>UpdateCharacterStateDelta 0x51</b>; the queued delta's apply
    /// (FUN_142eb6a10) runs the death transition when the test turns true (the PlayerDeathVfx effect from
    /// StringHashToValue — 0 in 0.695 —, FUN_142f88950 for a remote character, vtable +0x490 and FUN_142eb12d0) and the
    /// revive (vtable +0x488) when it turns false. So live set bit 7 on the other clients' copies of a dead player [I
    /// strong: the only writer of the bit for a remote character].
    /// </para>
    /// <para>
    /// <b>Ours.</b> On a death (<see cref="Died"/>, from Health.Damage) every other client on the landscape gets the
    /// Delta {set bit 7}; a player introduced to a dead player gets it after the character (<see cref="Introduce"/>, from
    /// Health.IntroduceDead); when the player is alive again (<see cref="Revived"/>: Revive Here, a graveyard, a revive
    /// by a friend, a site game's respawn, the dev revive) every client told the death is told {clear bit 7}, wherever it
    /// is now (a client that re-zoned since has no such entity and ignores it). The dead player's own client is never
    /// sent the bit: a Delta setting bit 7 on the dead client itself crashed the 0.589 client (trace/systems/
    /// abilities-combat.md, 2026-10-14; the crash site sits in the per-frame update's block for state bit 40, so the
    /// cause was likely that test's mask [I]); the local player's own death pose stays as it is (runtime capture lead in
    /// trace/0695/phaseE2-social.md). Bandwidth: one 42-byte packet per viewer per death and per revive.
    /// Setting <see cref="WorldServerConfiguration.DeadBodiesVisible"/> (Docker DEAD_BODIES_VISIBLE, default true = live).
    /// </para>
    /// </summary>
    public static class DeadBodies
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Entity state bit 7, "knocked out" (+0xa20 &gt;&gt; 7 &amp; 1, FUN_142ea9ef0) [P].</summary>
        public const ulong KnockedOutBit = 1UL << 7;

        /// <summary>Is the setting on (read at each death)?</summary>
        public static bool Visible
            => ConfigurationManager<WorldServerConfiguration>.Instance.Config?.DeadBodiesVisible ?? true;

        private static readonly object sync = new();
        /// <summary>Per dead player: the clients told the bit (they get the clear).</summary>
        private static readonly Dictionary<ulong, HashSet<WorldSession>> told = new();

        /// <summary>The player died (Health.Damage): the other clients on the landscape lay the body down.</summary>
        public static void Died(WorldSession player)
        {
            if (!Visible || AdminCommands.IsHidden(player))
                return;
            List<WorldSession> viewers = PlayerManager.InZone(player.ZoneId).Where(v => v != player && !v.Disconnected).ToList();
            lock (sync)
            {
                if (!told.TryGetValue(player.PlayerGuid, out HashSet<WorldSession> set))
                    told[player.PlayerGuid] = set = new HashSet<WorldSession>();
                foreach (WorldSession v in viewers)
                    set.Add(v);
            }
            byte[] delta = Combat.BlockStance.DeltaBody(player.PlayerGuid, KnockedOutBit, 0, (uint)ServerClock.Milliseconds);
            foreach (WorldSession v in viewers)
                v.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire, delta);
            log.Debug($"{player.CharacterName}'s body down on {viewers.Count} client(s).");
        }

        /// <summary>
        /// <paramref name="viewer"/> has just been shown <paramref name="subject"/>, who is dead (Health.IntroduceDead): the
        /// body is laid down on the viewer's new copy too.
        /// </summary>
        public static void Introduce(WorldSession viewer, WorldSession subject)
        {
            if (viewer == subject || !Visible || AdminCommands.IsHidden(subject))
                return;
            lock (sync)
            {
                if (!told.TryGetValue(subject.PlayerGuid, out HashSet<WorldSession> set))
                    told[subject.PlayerGuid] = set = new HashSet<WorldSession>();
                set.Add(viewer);
            }
            viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire,
                Combat.BlockStance.DeltaBody(subject.PlayerGuid, KnockedOutBit, 0, (uint)ServerClock.Milliseconds));
        }

        /// <summary>The player is alive again: every client told the death gets the bit cleared.</summary>
        public static void Revived(WorldSession player)
        {
            List<WorldSession> clients;
            lock (sync)
            {
                if (!told.Remove(player.PlayerGuid, out HashSet<WorldSession> set))
                    return;
                clients = set.Where(v => !v.Disconnected).ToList();
            }
            if (clients.Count == 0)
                return;
            byte[] clear = Combat.BlockStance.DeltaBody(player.PlayerGuid, 0, KnockedOutBit, (uint)ServerClock.Milliseconds);
            foreach (WorldSession v in clients)
                v.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire, clear);
            log.Debug($"{player.CharacterName}'s body up again on {clients.Count} client(s).");
        }

        /// <summary>
        /// A player left the world: their own entry goes (their entity is gone from every client) and they leave the
        /// others' told lists.
        /// </summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                told.Remove(session.PlayerGuid);
                foreach (HashSet<WorldSession> set in told.Values)
                    set.Remove(session);
            }
        }

        /// <summary>Tests / dev view: the clients told about this player's body.</summary>
        internal static int ToldCount(ulong player)
        {
            lock (sync)
                return told.TryGetValue(player, out HashSet<WorldSession> set) ? set.Count : 0;
        }
    }
}
