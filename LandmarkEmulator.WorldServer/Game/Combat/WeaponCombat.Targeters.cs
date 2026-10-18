using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Combat
{
    /// <summary>
    /// Melee swings as client-run Sweep targeters (OPEN-LEADS L170; trace/0695/agent-leads5.md, docs/protocol.md).
    /// <para>
    /// <b>What the client does [P, 0.695 decompile].</b> The Abilities CasterInfoReply carries a second list after the
    /// client-run movement effects, <c>List&lt;ClientExecutedTargeterData&gt;</c> (reply +0x118; layout in
    /// <see cref="ClientExecutedTargeter"/>). When a cast starts, right after "send cast to zone", the client builds one
    /// targeter per element (FUN_142e0f810 → FUN_142e257b0). The client has two kinds: Sweep (0xDEECF7D8) and Block
    /// (0xCC82431F). A Sweep waits its delay, then for its duration turns an angle from its start to its end value round the
    /// player's facing. On every update it looks for characters within its radius (sphere query) whose bounds fall inside
    /// the angle swept since the last update (FUN_14354d190 / FUN_14354e3a0). Each one not hit before (HitTracker) is sent
    /// to the server as ClientTargeterFindTarget 0x27/0x24, while fewer than the element's target limit were hit
    /// (FUN_14354e800). When the duration is over it sends ClientTargeterEnd 0x27/0x23 (FUN_14354d570). A character with
    /// entity state bit 45 (+0xa20) stops the sweep: the client plays composite effect 508 (unless the table overrides it)
    /// on the player and ends the sweep. Nothing in the client answers FindTarget: no S→C packet is waited for. The damage
    /// is the server's.
    /// </para>
    /// <para>
    /// Timing detail [P]: the sweep's progress is counted from the cast start (+0x138, set at init), not from the end of
    /// the delay. With delay ≥ duration, the first active update covers the whole arc at once (an instant cone at the
    /// strike), and later updates only test the end angle. The server may also end a client targeter early. The S→C
    /// dispatcher FUN_142e1aab0 case 0x23 reads ClientTargeterEnd {u64 cast guid, u32 cast hash, u32 id}
    /// (FUN_142e02360), finds the targeter by those three, and ends it (slot 6), which then sends its own End. Not used
    /// here: the client already enforces the target limit.
    /// </para>
    /// <para>
    /// <b>Block.</b> The Block targeter (FUN_14354cda0) tests the element's shapes for characters with entity state bit 45
    /// whose block component (0xC5838838: arc +0xa0, height +0xa4; FUN_14354dc10) faces the attacker. On a match it plays the
    /// same blocked effect and ends. It never sends FindTarget. So it is the attacker's "was I blocked" check against a
    /// blocking character. It is not the block stance itself. Our blocking attacks (Molten Rebuke, Corrupting Revenge:
    /// <see cref="Shape.Block"/>) are a server state against creature attacks, which are the server's; the other clients
    /// are shown the stance (bit 45 + the Block effect, <see cref="BlockStance"/>), so their Sweeps stop on the blocker.
    /// Our replies carry no Block targeter: which attacks carried one on live, and their shapes, are not known [I].
    /// </para>
    /// <para>
    /// <b>Which swings [I].</b> A Sweep goes with a held melee weapon's (<see cref="IsMelee"/>) single-hit, uncharged
    /// <see cref="Shape.Cone"/> attack: the 2016 texts "Swing your axe at all opponents in front of you" (Fiery Cleave) and
    /// "damage any opponents in front of you" (Decimate). The numbers are the server cone's: radius = the attack's Range,
    /// angles −Size … +Size (the half-arc), target limit = MaxTargets. The timing comes from the weapon's attack animation:
    /// delay = the earliest Release (its AttackTime in the client's animation data), duration = the spread of the Releases
    /// over the combo stages plus 0.35 s of motion (<see cref="WeaponAttacks.SweepTiming"/>). The sweep's origin is the
    /// caster (Origin 1), taken again on every update (Follow 2). A target is reported only once per swing (re-hit after
    /// duration + 1 s). Charged attacks are left out: their cast starts at the button press, before the swing. Channels
    /// (Ticks &gt; 1) and the other shapes are left out too. The server resolves those, as before.
    /// </para>
    /// <para>
    /// <b>One authority per swing (no double hits).</b> When a cast's caster info carried a Sweep, the cast does no damage
    /// at once (<see cref="DeferToSweep"/>). The swing waits for the client:
    /// <list type="bullet">
    /// <item>each FindTarget is checked against the server's cone (<see cref="Creatures.InSweep"/>: alive, same landscape,
    /// within Range + <see cref="SweepRangeSlack"/> m, inside the half-arc + <see cref="SweepArcSlack"/>° of the swing's
    /// facing or the player's facing now, and in sight for a cavern creature). The target limit, one hit per opponent
    /// per swing, and the swing's time window are also checked. Each target that passes is struck through the normal
    /// path (<see cref="Strike"/>: Erupt, damage, the attack's rule, affinity properties, armour);</item>
    /// <item>ClientTargeterEnd closes the swing: the attack's rule for the whole swing runs (<see cref="Finish"/>);</item>
    /// <item>if neither message arrives by the delay + duration + <see cref="SweepGraceSeconds"/>, the client ran no
    /// targeter for this cast (for example an older caster info). Only then the server resolves the swing itself, once,
    /// as before. If FindTargets came but no End, the swing is just closed.</item>
    /// </list>
    /// A FindTarget for a cast with no waiting swing (site games and encounters hit first, dev swings, setting off) is
    /// ignored. A reported character that is not one of our creatures (another player) is ignored: player-vs-player hits are
    /// the server's (InstancedGames / Encounters) [I].
    /// </para>
    /// Setting <see cref="WorldServerConfiguration.MeleeTargeters"/> (Docker MELEE_TARGETERS, default true = live; dev
    /// "meleetargeters on|off"). With it off, replies carry no targeter and every swing is the server's.
    /// </summary>
    public static partial class WeaponCombat
    {
        private static bool? meleeTargeters;

        /// <summary>The setting MeleeTargeters unless the dev command switched it.</summary>
        public static bool MeleeTargeters
        {
            get => meleeTargeters ?? LandmarkEmulator.Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.MeleeTargeters ?? true;
            set => meleeTargeters = value;
        }

        /// <summary>The one targeter element of a weapon reply (echoed back in FindTarget / End).</summary>
        private const uint SweepTargeterId = 1;
        /// <summary>FindTarget check: metres beyond the attack's Range (the opponent's body, movement during the sweep) [I].</summary>
        private const float SweepRangeSlack = 2f;
        /// <summary>FindTarget check: degrees beyond the half-arc (the player turning during the sweep, body width) [I].</summary>
        private const float SweepArcSlack = 20f;
        /// <summary>Seconds after the sweep's end to wait for the client before closing the swing [I].</summary>
        private const float SweepGraceSeconds = 1.5f;

        /// <summary>A swing waiting for its client sweep.</summary>
        private sealed class PendingSweep
        {
            public Swing Swing;
            public (Vector3 Origin, Vector3 Direction)? Ray;
            public ulong CastGuid;
            public uint CastHash;
            /// <summary>A FindTarget or End came: the client ran the targeter.</summary>
            public bool Reported;
            public bool Done;
            public int Refused;
            public readonly HashSet<ulong> Claimed = new();
        }

        private static readonly List<PendingSweep> sweeps = new();

        /// <summary>
        /// The attack a cast id runs with this weapon when it is swung as a client Sweep (melee, Cone, one hit, not charged),
        /// else null. The caster info (<see cref="SweepOf"/>) and the cast (<see cref="DeferToSweep"/>) use the same test.
        /// </summary>
        private static Attack SweepAttack(ItemDefinitionManager.ItemDefinition weapon, uint ability)
        {
            if (!MeleeTargeters || !IsMelee(weapon))
                return null;
            Attack a = AttackOf(weapon, ability);
            return a is { Shape: Shape.Cone, Charged: false, Ticks: 1 } ? a : null;
        }

        /// <summary>The Sweep targeter for a weapon cast's caster info, or null (see the class notes for the numbers).</summary>
        public static ClientExecutedTargeter SweepOf(ItemDefinitionManager.ItemDefinition weapon, uint ability)
        {
            Attack a = SweepAttack(weapon, ability);
            if (a == null)
                return null;
            var (delay, duration) = WeaponAttacks.SweepTiming(weapon, ability);
            return new ClientExecutedTargeter
            {
                Id = SweepTargeterId,
                Type = ClientExecutedTargeter.Sweep,
                Origin = 1,
                Follow = 2,
                Delay = delay,
                // 0 = no client-side limit: the client counts every character it reports (corpses, allies, other
                // players) against it, so the real target could go unreported; the server enforces MaxTargets itself
                // (SweepFoundTarget) [P] (trace/0695/agent-l170-followup.md §1).
                MaxTargets = 0,
                Duration = duration,
                StartAngle = -a.Size,
                EndAngle = a.Size,
                RehitSeconds = duration + 1f,
                Radius = a.Range
            };
        }

        /// <summary>
        /// Holds a swing for its client sweep when the cast's caster info carried one (true: no damage now). The fallback
        /// timer closes it after the sweep's window.
        /// </summary>
        private static bool DeferToSweep(Swing swing, (Vector3 Origin, Vector3 Direction)? ray, ItemDefinitionManager.ItemDefinition weapon,
            uint ability, AbilityCastAttempt cast)
        {
            if (SweepAttack(weapon, ability) == null)
                return false;
            var (delay, duration) = WeaponAttacks.SweepTiming(weapon, ability);
            var pending = new PendingSweep { Swing = swing, Ray = ray, CastGuid = cast.CastGuid, CastHash = cast.CastHash };
            lock (sync)
                sweeps.Add(pending);
            // The attack's area effect, as Resolve plays it (none of the swept attacks has one today).
            if (swing.Attack.Effect != 0)
            {
                Vector3 at = swing.Player.Position + swing.Forward * Math.Min(swing.Attack.Range / 2f, 8f);
                HitEffects.PlayAt(at, swing.Attack.Effect, AreaViewers(swing.Player, at));
            }
            Later((int)((delay + duration + SweepGraceSeconds) * 1000f), () => CloseSweep(pending, timedOut: true));
            log.Debug($"{swing.Player.CharacterName} cast {ability} (0x{cast.CastGuid:X}/{cast.CastHash:X}): waiting for the client's sweep.");
            return true;
        }

        /// <summary>Is a swing of this cast waiting for its client sweep (then nothing else may strike for it now)?</summary>
        public static bool AwaitingSweep(WorldSession p, AbilityCastAttempt cast)
        {
            lock (sync)
                return sweeps.Any(s => s.Swing.Player == p && s.CastGuid == cast.CastGuid && s.CastHash == cast.CastHash && !s.Done);
        }

        private static PendingSweep FindSweep(WorldSession p, ulong castGuid, uint castHash)
            => sweeps.FirstOrDefault(s => s.Swing.Player == p && s.CastGuid == castGuid && s.CastHash == castHash && !s.Done);

        /// <summary>ClientTargeterFindTarget 0x27/0x24: the client's sweep hit a character; strike it if the server's cone agrees.</summary>
        public static void SweepFoundTarget(WorldSession p, AbilityClientTargeterFindTarget msg)
        {
            PendingSweep s;
            lock (sync)
            {
                s = FindSweep(p, msg.CastGuid, msg.CastHash);
                if (s != null && msg.TargeterId == SweepTargeterId)
                    s.Reported = true;
                else
                    s = null;
            }
            if (s == null)
            {
                log.Debug($"{p.CharacterName} FindTarget 0x{msg.Target:X} for cast 0x{msg.CastGuid:X}/{msg.CastHash:X} (targeter {msg.TargeterId}): no swing waiting, ignored.");
                return;
            }
            // The server's cone (outside this class's lock: Creatures has its own).
            Attack a = s.Swing.Attack;
            bool inCone = msg.Target != p.PlayerGuid && Creatures.InSweep(p.ZoneId, msg.Target, p.Position,
                new[] { s.Swing.Forward, Facing(p) }, a.Range + SweepRangeSlack, a.Size + SweepArcSlack);
            bool claim;
            lock (sync)
            {
                bool fresh = !s.Done && !s.Claimed.Contains(msg.Target);
                claim = fresh && inCone && (a.MaxTargets <= 0 || s.Claimed.Count < a.MaxTargets);
                if (claim)
                    s.Claimed.Add(msg.Target);
                else if (fresh)
                    s.Refused++;
            }
            if (!claim)
            {
                log.Debug($"{p.CharacterName} FindTarget 0x{msg.Target:X}: outside the server's cone / limit, refused.");
                return;
            }
            Strike(s.Swing, msg.Target, s.Swing.Damage, s.Swing.Attack.OnHit);
        }

        /// <summary>ClientTargeterEnd 0x27/0x23: the client's sweep is over; the swing is closed.</summary>
        public static void SweepEnded(WorldSession p, AbilityClientTargeterEnd msg)
        {
            PendingSweep s;
            lock (sync)
            {
                s = FindSweep(p, msg.CastGuid, msg.CastHash);
                if (s != null && msg.TargeterId == SweepTargeterId)
                    s.Reported = true;
                else
                    s = null;
            }
            if (s == null)
            {
                log.Debug($"{p.CharacterName} ClientTargeterEnd for cast 0x{msg.CastGuid:X}/{msg.CastHash:X} (targeter {msg.TargeterId}): no swing waiting.");
                return;
            }
            CloseSweep(s, timedOut: false);
        }

        /// <summary>
        /// Closes a waiting swing once: its whole-swing rule after the client's hits, or (nothing came from the client in
        /// time) the server's own resolution of the swing.
        /// </summary>
        private static void CloseSweep(PendingSweep s, bool timedOut)
        {
            lock (sync)
            {
                if (s.Done)
                    return;
                s.Done = true;
                sweeps.Remove(s);
            }
            WorldSession p = s.Swing.Player;
            if (p.Disconnected)
                return;
            if (!s.Reported)
            {
                log.Info($"{p.CharacterName} cast 0x{s.CastGuid:X}: no client sweep came, the server resolves the swing.");
                Resolve(s.Swing, s.Ray);
                return;
            }
            Finish(s.Swing);
            log.Info($"{p.CharacterName} sweep of cast 0x{s.CastGuid:X} {(timedOut ? "timed out" : "ended")}: {s.Swing.Hits.Count} hit, {s.Refused} refused.");
        }

        private static void ForgetSweeps(WorldSession p)
        {
            lock (sync)
                sweeps.RemoveAll(s => s.Swing.Player == p);
        }
    }
}
