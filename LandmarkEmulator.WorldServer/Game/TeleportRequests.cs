using LandmarkEmulator.WorldServer.Network;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Teleport requests from the client: Command 0x1E subs 0x51 TeleportToLocation {vec4 position} (writer FUN_14338db30),
    /// 0x52 TeleportToLocationEx {vec4 position, vec4 rotation, vec4} (FUN_14338dd40) and 0x3F6 TeleportManagedToLocation
    /// {vec4 position, u64 mover} (FUN_14338d910), all built by FUN_1433c80a0 (0.695) [P]
    /// (trace/0695/agent-l140.md §1, §3 B1).
    /// <para>
    /// Who sends them [P]:
    /// </para>
    /// <list type="bullet">
    /// <item>The Escape menu's <b>"Evac to Safety"</b> (Data.xml MainMenu → Ui.EvacToSafety → Lua
    /// Voxel.EvacLocalPlayerToSafepoint, native FUN_1435fdb70). A player outside the zone's bounds sends
    /// ZoneSafeTeleportRequest 0x7C instead (<see cref="Health.EvacuateToSafePoint"/>). Otherwise:
    /// <list type="bullet">
    /// <item>on a build site (the client's current claim, FUN_142ccb810 → FUN_142dbbf40): the claim box (+0x3a8 min,
    /// +0x3b4 max) centre plus (half its 3-D diagonal + 10 m, DAT_1446f4800) along the player's heading — a point just
    /// outside the site in the direction the player faces (FUN_142c47650 turns (d, 0, 0) by the heading quaternion);</item>
    /// <item>elsewhere: the player's own position (FUN_142d0a1f0).</item>
    /// </list>
    /// Both go through FUN_142d0a0d0, which keeps x / z and puts y on the ground (y below the ground or more than 5 m
    /// above it, DAT_14469adc0, becomes ground + 5), then FUN_1433c80a0 with the player's current heading — so evac sends
    /// 0x51, or 0x3F6 when the player stands on a managed object (actor +0x1459 &amp; 0x10).</item>
    /// <item>GM commands (admin-player flag +0x7b0d8, which SendSelf sets for our Admins): /location x y z
    /// (FUN_1433af380, 0x52 when a heading is given) and /teleporttocamera (FUN_1433b5430) send the exact point; the world
    /// map's Admin.MapTeleport (FUN_143054920) goes through FUN_142d0a0d0 (ground-clamped).</item>
    /// </list>
    /// <para>
    /// <b>Every request must be answered with ClientUpdate TeleportToLocation.</b> FUN_1433c80a0 sets the actor's
    /// teleport-pending bit (+0x1457 |= 8) before sending; while it is set the actor update (FUN_142eb1950 skips
    /// FUN_142eb6a10 / FUN_142ecb870) and FUN_143329ed0 stop, i.e. the player cannot move. Only the TeleportToLocation
    /// handler FUN_142d59f30 clears it (&amp; 0xf7; its one caller is the ClientUpdate case 0xB of FUN_142cd9980) [P].
    /// A re-zone (ClientBeginZoning) does not, so the answer is always a plain TeleportToLocation, whatever
    /// ShortTeleportDistance says; a refused request is answered with the player's current position.
    /// </para>
    /// <para>
    /// Rules (ours: the server's check of a client-chosen point): admins (WorldServer.json Admins, the same players whose
    /// client has +0x7b0d8) go anywhere on their landscape, at the exact point. Everyone else is moved only for an evac:
    /// x / z within <see cref="NearRadius"/> of the server's position for them (the evac's own-spot case; the slack covers
    /// position-update lag), or — standing on a build site — within the site's half diagonal + 10 m + <see cref="NearRadius"/>
    /// of its centre (the site case); at most once every <see cref="Cooldown"/>. Setting PlayerTeleportRequests (default
    /// true = live; false = every non-admin request is answered in place).
    /// </para>
    /// </summary>
    public static class TeleportRequests
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort ToLocationSub = 0x51, ToLocationExSub = 0x52, ManagedToLocationSub = 0x3F6;

        /// <summary>How far (metres, x / z) a player's own evac point may be from where the server has them (lag slack).</summary>
        public const float NearRadius = 8f;

        /// <summary>The client's distance beyond the site box's half diagonal for the site evac (DAT_1446f4800 = 10.0) [P].</summary>
        private const float SiteMargin = 10f;

        /// <summary>Non-admin requests moved at most once per this (the 0x7C safe-point request uses the same 10 s).</summary>
        public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

        /// <summary>A re-zone still loading delays the answer; after this many 0.5 s retries it goes out anyway.</summary>
        private const int MaxDeferrals = 120;

        /// <summary>PlayerTeleportRequests: players' evac requests move them (true = live).</summary>
        public static bool Enabled =>
            enabled ??= LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration?.GetValue("PlayerTeleportRequests", true) ?? true;
        private static bool? enabled;

        private static readonly Dictionary<WorldSession, DateTime> lastMove = new();

        /// <summary>Routes Command 0x51 / 0x52 / 0x3F6 (InteractionHandler.HandleCommand).</summary>
        public static void Handle(WorldSession session, ushort sub, byte[] body)
        {
            if (body.Length < 16)
            {
                // Unreadable: still answered, the client is frozen until a TeleportToLocation arrives.
                log.Warn($"{session.CharacterName}: teleport request 0x{sub:X} too short ({body.Length} bytes); answered in place.");
                Answer(session, session.Position, snapToGround: true, session.Facing, 0);
                return;
            }
            var requested = new Vector3(BitConverter.ToSingle(body, 0), BitConverter.ToSingle(body, 4), BitConverter.ToSingle(body, 8));
            Quaternion facing = session.Facing;
            ulong mover = 0;
            if (sub == ToLocationExSub && body.Length >= 32)
            {
                // The new heading: (axis · sin(h/2), cos(h/2)) about DAT_1455f07a0 (the up axis [I]), FUN_1433c80a0.
                var q = new Quaternion(BitConverter.ToSingle(body, 16), BitConverter.ToSingle(body, 20),
                    BitConverter.ToSingle(body, 24), BitConverter.ToSingle(body, 28));
                if (q.LengthSquared() > 0.0001f && float.IsFinite(q.LengthSquared()))
                    facing = Quaternion.Normalize(q);
            }
            else if (sub == ManagedToLocationSub && body.Length >= 24)
                mover = BitConverter.ToUInt64(body, 16);

            bool admin = Admin.IsAdmin(session);
            Vector3? target = null;
            string why;
            if (!IsFinite(requested))
                why = "not a number";
            else if (admin)
            {
                target = requested;
                why = "admin";
            }
            else if (!Enabled)
                why = "PlayerTeleportRequests off";
            else if (OnCooldown(session))
                why = "cooldown";
            else
            {
                why = Allowed(session, requested);
                // On a mover: the point is the actor's position, which FUN_1433c80a0 sends as for 0x51 [P]. Whether a rider's
                // actor position is the world point or one relative to the mover is not proven [I]: when the point fails as
                // a world point and the mover is ours, it is tried relative to the mover.
                if (why == null)
                    target = requested;
                else if (mover != 0 && Props.MoverPosition(mover) is Vector3 at && Allowed(session, at + requested) == null)
                {
                    target = at + requested;
                    why = null;
                }
            }

            if (target is Vector3 to)
            {
                if (!admin)
                    lock (lastMove)
                        lastMove[session] = DateTime.UtcNow;
                // Players: the client already put the point on the ground; snapping also keeps them clear of tree trunks
                // (Teleport.To). Not on a mover (it would drop them off it [I]) and not for an admin's exact point.
                bool snap = !admin && mover == 0;
                log.Info($"{session.CharacterName}: teleport request 0x{sub:X} to {to.X:0.#} {to.Y:0.#} {to.Z:0.#}"
                    + (mover != 0 ? $" (mover 0x{mover:X})" : "") + (admin ? " (admin)" : " (evac)") + ".");
                Answer(session, to, snap, facing, 0);
            }
            else
            {
                log.Info($"{session.CharacterName}: teleport request 0x{sub:X} to {requested.X:0.#} {requested.Y:0.#} {requested.Z:0.#} refused ({why}); answered in place.");
                Answer(session, session.Position, snapToGround: false, facing, 0);
            }
        }

        /// <summary>Null when a non-admin may be moved to <paramref name="point"/>, else why not.</summary>
        private static string Allowed(WorldSession session, Vector3 point)
        {
            Vector3 at = session.Position;
            if (Horizontal(point, at) <= NearRadius)
                return null;
            if (Claims.SiteBoxAt(session.ZoneId, at) is (Vector3 centre, float halfDiagonal)
                && Horizontal(point, centre) <= halfDiagonal + SiteMargin + NearRadius)
                return null;
            return "too far";
        }

        private static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

        private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        private static bool OnCooldown(WorldSession session)
        {
            DateTime now = DateTime.UtcNow;
            lock (lastMove)
            {
                // Old entries go (players who left included).
                List<WorldSession> stale = null;
                foreach (var e in lastMove)
                    if (now - e.Value >= Cooldown)
                        (stale ??= new List<WorldSession>()).Add(e.Key);
                if (stale != null)
                    foreach (WorldSession s in stale)
                        lastMove.Remove(s);
                return lastMove.ContainsKey(session);
            }
        }

        /// <summary>
        /// Sends the TeleportToLocation that ends the client's wait (<see cref="Teleport.To"/> with loadingScreen false: a
        /// move in place, never a re-zone). While a re-zone of the player is still loading, the answer waits for it (one
        /// zone change at a time) and goes to the latest position then.
        /// </summary>
        private static void Answer(WorldSession session, Vector3 position, bool snapToGround, Quaternion facing, int deferrals)
        {
            if (Teleport.RezoneLoading(session) && deferrals < MaxDeferrals)
            {
                Teleport.After(session, 500, () => Answer(session, position, snapToGround, facing, deferrals + 1));
                return;
            }
            Teleport.To(session, position, snapToGround, loadingScreen: false, facing: facing);
        }
    }
}
