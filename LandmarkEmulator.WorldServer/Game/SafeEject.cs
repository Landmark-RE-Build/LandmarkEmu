using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// PlayerUpdate SafeEject (0x26 sub 0x4C, C→S, body <c>u8 1</c>): the 0.695 client's "I am being launched" report.
    /// Traced 2026-10-18 from re/decomp_all_695; docs/protocol.md 4.1 has the short form.
    /// <para>
    /// <b>Writer</b> FUN_142ebdda0 (0.589 FUN_142e1f330, same code) [P]: if entity state bit 26 (the u64 word at entity
    /// +0xa20, see <see cref="Combat.BlockStance"/>) is clear, it sets bit 26 on the local player (vtable +0x268 → +0x270,
    /// LocalProxiedCharacter FUN_14332bba0) and sends the packet once: header FUN_142c13350 (<c>26 00 4C 00 00 00</c>), then
    /// one byte from packet +0x18 = 1 (send template FUN_142e85070; channel Zone, channels-695.tsv). While bit 26 stays
    /// set the writer sends nothing more.
    /// </para>
    /// <para>
    /// <b>Triggers</b> [P], all six callers: the client's launchers, at the moment they give the player their velocity —
    /// <list type="bullet">
    /// <item>ClientAreaChainThrustPad (area type 24, the Spring-loaded Flinger 1188 and the older launcher area 8):
    /// FUN_1436da050 → FUN_1436d9c90;</item>
    /// <item>ClientAreaThrower (area type 32, the Launcher 1233): FUN_1436dc4a0 → FUN_1436dbf00;</item>
    /// <item>PhysicsAntiGravThrustPadCallback (area type 12 AntiGravThrust): FUN_1436d8250;</item>
    /// <item>the server-sent thrust pads (ThrustPadDataPacket 0xCF, list walked by FUN_142fdffa0 every frame from
    /// FUN_142cd6830): pad type 1 FUN_142fe0f00, type 2 FUN_142fe01c0, type 3 FUN_142fe0bc0 (a launch at a target entity).
    /// These also send ThrustPadPlayerEntered 0xCF/3 {u32 pad id} (FUN_142e99e50). The server sends no ThrustPadData yet,
    /// so on this server only the three area launchers fire.</item>
    /// </list>
    /// It is <b>not</b> an "unstuck" request: no string, Lua binding or UI path sends it, and the client's way out of a
    /// stuck spot is the Escape menu's Evac to Safety (ZoneSafeTeleportRequest / CommandPacketTeleportToLocation, answered
    /// by <see cref="Health.EvacuateToSafePoint"/> and <see cref="Teleport"/>). The runtime case of 2026-10-18 (harness
    /// teleports onto props) is a player put down on a flinger / launcher pad, which then fired (enabled since 345a2bb).
    /// </para>
    /// <para>
    /// <b>What the client does while it waits</b> [P]: nothing blocks input or movement; the launch runs on the client's
    /// physics at once. Bit 26 is read in three places only: the writer's own guard (no second SafeEject); thrust-pad type 1
    /// (FUN_142fe0f00 fires only while bit 26 is clear); and LocalProxiedCharacter vtable +0x288 FUN_142eaa920 ("bit 26 or
    /// IsFalling +0x1450 &amp; 0x40", the PS2-lineage "ballistic" test). The client never clears bit 26 itself: the only
    /// writers of +0xa20 are the bit setters (no caller passes 26 to the clear slot +0x278 / FUN_143325a20), the constructor,
    /// and the server's queued state records — PlayerUpdate 0x0F UpdateCharacterState (whole word) and 0x51
    /// UpdateCharacterStateDelta (set / clear masks), applied by FUN_142eb6a10 through vtable +0x260 (LocalProxiedCharacter
    /// FUN_14332bb00). Bit 26 is in none of the masks that reset the controller on a state change (static initialiser
    /// 0x143c25f90: 0x4000a000, 0x1a8a0, 0x800040, 0x400081aace …) [P]. So without a server answer the first launch
    /// leaves bit 26 set until the entity is rebuilt: no further SafeEject, and type-1 pads never fire again.
    /// </para>
    /// <para>
    /// <b>The answer</b>: the server does not send SafeEject back — the S→C PlayerUpdate dispatcher FUN_142cdfa60 has no
    /// case for sub 0x4C (cases 0x4A and 0x4C of <c>sub − 1</c> are subs 0x4B and 0x4D) [P]. What live sent is not captured.
    /// Ours [I]: the server keeps its copy of the state ("launched") and ends it when the player has landed — the movement
    /// state word (<see cref="Consumables.Airborne"/>: jumping 0x10, falling 0x20, double jump 0x800, gliding 0x1000) goes
    /// from airborne to grounded — with UpdateCharacterStateDelta <c>{guid, source 0, set 0, clear 1 &lt;&lt; 26, time 0}</c>
    /// to that player only (time 0 applies at once). The Delta is the one packet that can clear the bit without touching
    /// the bits the client keeps itself. A launch that never leaves the ground ends after <see cref="GroundedGrace"/>.
    /// Re-zones wait (no packet to a loading client, cave-teleport rule). Landmark had no fall damage after 2014
    /// (trace/0695/agent-l138-a.md), so the state changes nothing else on the server; <see cref="IsLaunched"/> is there
    /// for any later rule.
    /// </para>
    /// Setting <see cref="WorldServerConfiguration.SafeEjectReset"/> (Docker SAFE_EJECT_RESET, default true; dev
    /// "safeeject [clear]"). false: the report is only logged and the client keeps bit 26 (as before 2026-10-18).
    /// </summary>
    public static class SafeEject
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Entity state bit 26: the client's "launched / safe-ejecting" bit (FUN_142ebdda0) [P].</summary>
        public const ulong LaunchedBit = 1UL << 26;

        /// <summary>A launch whose player never shows an airborne movement state ends after this long on the ground (ours).</summary>
        public static readonly TimeSpan GroundedGrace = TimeSpan.FromSeconds(3);

        /// <summary>Reports closer together than this are ignored (the client cannot send two without a clear; ours).</summary>
        public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);

        private static bool? reset;

        /// <summary>The setting SafeEjectReset unless the dev command switched it.</summary>
        public static bool Reset
        {
            get => reset ?? LandmarkEmulator.Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.SafeEjectReset ?? true;
            set => reset = value;
        }

        /// <summary>One player's launch: when it was reported, and whether they have been seen in the air since.</summary>
        private sealed class Launch
        {
            public DateTime Since;
            public bool SawAirborne;
        }

        private static readonly Dictionary<WorldSession, Launch> launched = new();
        /// <summary>The last report accepted per player (rate limit).</summary>
        private static readonly Dictionary<WorldSession, DateTime> lastReport = new();

        /// <summary>Has the player reported a launch that has not ended yet?</summary>
        public static bool IsLaunched(WorldSession session)
        {
            lock (launched)
                return launched.ContainsKey(session);
        }

        /// <summary>The client's SafeEject: <paramref name="flag"/> is the body byte (always 1 from the writer).</summary>
        public static void Report(WorldSession session, byte flag)
        {
            DateTime now = DateTime.UtcNow;
            bool fresh;
            lock (launched)
            {
                if (lastReport.TryGetValue(session, out DateTime last) && now - last < MinInterval)
                {
                    log.Debug($"{session.CharacterName}: SafeEject again after {(now - last).TotalMilliseconds:0} ms, ignored.");
                    return;
                }
                lastReport[session] = now;
                fresh = !launched.ContainsKey(session);
                launched[session] = new Launch { Since = now, SawAirborne = Consumables.Airborne(session) };
            }
            log.Info($"{session.CharacterName}: SafeEject {flag} (launched by a pad / flinger / launcher) at {session.Position}"
                + $"{(fresh ? "" : "; the last launch had not ended")}{(Reset ? "" : "; SafeEjectReset off, bit 26 stays set")}.");
        }

        /// <summary>
        /// From <see cref="WorldSession.FromClient"/> after each movement update: a launched player who has landed gets
        /// bit 26 cleared.
        /// </summary>
        public static void Moved(WorldSession session)
        {
            Launch l;
            lock (launched)
                if (!launched.TryGetValue(session, out l))
                    return;
            bool airborne = Consumables.Airborne(session);
            if (airborne)
            {
                l.SawAirborne = true;
                return;
            }
            if (!ShouldEnd(airborne, l.SawAirborne, DateTime.UtcNow - l.Since) || Teleport.RezoneLoading(session))
                return;
            End(session, l.SawAirborne ? "landed" : "never left the ground");
        }

        /// <summary>
        /// Has a launch ended? Only on the ground, and only after the player was seen in the air, or after
        /// <see cref="GroundedGrace"/> (the report comes while the player still stands on the pad).
        /// </summary>
        public static bool ShouldEnd(bool airborne, bool sawAirborne, TimeSpan elapsed)
            => !airborne && (sawAirborne || elapsed >= GroundedGrace);

        /// <summary>Ends the player's launch: clears bit 26 on their client (when <see cref="Reset"/>), logged.</summary>
        public static bool End(WorldSession session, string why)
        {
            Launch l;
            lock (launched)
                if (!launched.Remove(session, out l))
                    return false;
            if (Reset && !session.Disconnected)
                session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDeltaWire, ClearBody(session.PlayerGuid));
            log.Info($"{session.CharacterName}: launch ended ({why}, {(DateTime.UtcNow - l.Since).TotalSeconds:0.0} s)"
                + $"{(Reset ? ", bit 26 cleared" : "")}.");
            return true;
        }

        /// <summary>UpdateCharacterStateDelta for the player's own entity: set 0, clear bit 26, time 0 (at once).</summary>
        internal static byte[] ClearBody(ulong guid) => Combat.BlockStance.DeltaBody(guid, 0, LaunchedBit, 0);

        /// <summary>A player left: their launch and rate-limit entry go (no packets).</summary>
        public static void Forget(WorldSession session)
        {
            lock (launched)
            {
                launched.Remove(session);
                lastReport.Remove(session);
            }
        }

        /// <summary>The dev command's view of the launches not ended yet.</summary>
        public static string DevState()
        {
            lock (launched)
                return launched.Count == 0 ? "none"
                    : string.Join("; ", launched.Select(p => $"{p.Key.CharacterName} since {p.Value.Since:HH:mm:ss.f}"
                        + $"{(p.Value.SawAirborne ? ", in the air" : "")}"));
        }
    }
}
