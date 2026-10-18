using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Flying projectiles the client draws: the Projectiles family 0xEE (OPEN-LEADS L138, trace/0695/agent-l138-c1.md §1).
    /// The client's only reader of ProjectileDefinitions.txt is this family's handler (FUN_14318fbe0, zone dispatcher case
    /// 0x73), and nothing else in the client creates a ClientProjectile, so a shot with its model and trail is drawn only
    /// when the server sends it [P]. The projectile is visual: the client sends nothing back and the server keeps the hit
    /// timing (distance / the definition's SPEED).
    /// <para>
    /// Wire (after the opcode bytes EE 00): i16 sub, then
    /// <list type="bullet">
    /// <item>1 Create (reader FUN_14318ce10): u32 projectile id (+0x18), u32 ProjectileDefinitions id (+0x1c), vec3 origin
    /// (+0x20), vec3 direction (+0x2c: a unit vector is scaled by the definition's speed, def +0x24, in the init
    /// FUN_143c9fa70; any other length is used as the velocity), u64 shooter guid (+0x38 → projectile +0x20: when the
    /// definition names a bone, the shot starts at that bone of the shooter's actor, FUN_1436d5c40), bool (+0x40 →
    /// projectile +0x8c: gravity on), f32 (+0x48 → +0x48: the maximum range — every frame the base update FUN_143c9fca0
    /// clamps the shot to that distance from its start and marks it stopped, so 0 pins it at the origin; tested 2026-10-17
    /// with re/test/projectile_probe.js) [P], vec3 (+0x4c → +0x3c) [I: aim point], f32 (+0x44 → +0xa8: homing turn rate,
    /// used only while a homing point +0x90 is set and the time +0xb8 has passed) [P], u64 (+0x58: the homing target's guid), f32
    /// (+0x60: seconds for the turn rate to fade to 0; +0xac = the +0x44 value / it), vec3 (+0x64 → +0x9c: a fixed homing
    /// point), u64 (+0x70 → +0xb8: homing start time) [P: trace/0695/agent-leads2-a.md L156]; only +0x4c is unread.</item>
    /// <item>2 Update (FUN_14318d480): u32 id, vec3, vec3 (a new course; not needed for straight shots).</item>
    /// <item>3 Delete (FUN_14318d320): u32 id, 4 × f32 impact point (x, y, z, 1), u32, bool hit, u64 target guid
    /// (→ FUN_1436d6b60, then the manager drops the projectile).</item>
    /// </list>
    /// Positions are in the local frame, like the creatures'. Bandwidth: one Create and one Delete per shot, to the
    /// players who can see it (the caller passes them).
    /// </para>
    /// </summary>
    public static class Projectiles
    {
        // ushort: the writer has no short overload, and a short would go out as a 4-byte int (the reader then rejects the body).
        public const ushort CreateSub = 1, UpdateSub = 2, DeleteSub = 3;

        private static int nextId;

        /// <summary>A new projectile id (the client files projectiles by it).</summary>
        public static uint NewId() => (uint)Interlocked.Increment(ref nextId);

        /// <summary>
        /// Shows a projectile flying from <paramref name="origin"/> along <paramref name="direction"/> (normalised here, so
        /// the definition's speed applies) to the viewers. <paramref name="shooter"/> is the entity it leaves from (0: from
        /// the origin itself); <paramref name="aim"/> and <paramref name="target"/> fill the aim fields [I].
        /// </summary>
        /// <param name="speed">0: the definition's speed (a unit direction is sent); otherwise the velocity is sent as
        /// direction × speed (the client uses a vector that isn't unit length as the velocity, FUN_143c9fa70).</param>
        public static void Create(IEnumerable<WorldSession> viewers, uint id, uint definition, Vector3 origin, Vector3 direction,
            ulong shooter, Vector3 aim, ulong target, float speed = 0f, float range = 0f)
        {
            // The range ends the flight: by default where the aim point is.
            if (range <= 0f)
                range = Vector3.Distance(origin, aim);
            Vector3 dir = direction.LengthSquared() > 0f ? Vector3.Normalize(direction) : Vector3.UnitZ;
            if (speed > 0f)
                dir *= speed;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(CreateSub);
            w.Write(id);
            w.Write(definition);
            Put(w, origin);
            Put(w, dir);
            w.Write(shooter);
            w.Write(false);         // +0x40 → projectile +0x8c: gravity
            w.Write(range);         // +0x48 → projectile +0x48: maximum range
            Put(w, aim);            // +0x4c → projectile +0x3c [I: the aim point]
            w.Write(0f);            // +0x44 → projectile +0xa8: homing turn rate (no homing)
            w.Write(target);        // +0x58 → projectile +0x28: homing target guid (no homing: turn rate 0)
            w.Write(0f);            // +0x60: turn-rate fade time
            Put(w, Vector3.Zero);   // +0x64 → projectile +0x9c: fixed homing point
            w.Write(0ul);           // +0x70 → projectile +0xb8: homing start time
            Send(viewers, output);
        }

        /// <summary>Ends a projectile at <paramref name="impact"/> (hit or miss) for the viewers.</summary>
        public static void Delete(IEnumerable<WorldSession> viewers, uint id, Vector3 impact, bool hit, ulong target)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(DeleteSub);
            w.Write(id);
            Put(w, impact);
            w.Write(1f);
            w.Write(0u);
            w.Write(hit);
            w.Write(target);
            Send(viewers, output);
        }

        private static void Put(GamePacketWriter w, Vector3 v)
        {
            w.Write(v.X);
            w.Write(v.Y);
            w.Write(v.Z);
        }

        private static void Send(IEnumerable<WorldSession> viewers, List<byte> output)
        {
            byte[] body = output.ToArray();
            foreach (WorldSession viewer in viewers)
                if (viewer.RezoneTarget == null)
                    viewer.EnqueueMessage(ZoneMessageOpcode.ProjectileBase, body);
        }
    }
}
