using System;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// An ability's definition for the client (Abilities 0x27, u16 sub 3; reader FUN_142d63690, stored by
    /// FUN_142d7a7d0). The client asks for it (sub 2) when an item with the ability is held. Fields are named after
    /// what the client does with them where that is known; the rest keep their client struct offset.
    /// Captured replies (lightstone 221, 1098) decode exactly with this layout (re/caster_info.py).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCasterInfoReply)]
    public class AbilityCasterInfoReply : IWritable
    {
        public uint AbilityId { get; set; }
        /// <summary>Ability kind hash. 0xDFB05E5F is one the client's cast path accepts (seen in captures).</summary>
        public uint KindHash { get; set; } = 0xDFB05E5F;
        /// <summary>Resource pool the ability costs, and how much (record [2], [3]).</summary>
        public uint CostResource { get; set; } = 2;
        public uint CostAmount { get; set; }
        /// <summary>When set, casting does not require the cost resource pool to be above 0.</summary>
        public bool IgnoreCostPool { get; set; }
        public uint U2c { get; set; }
        public float U70 { get; set; }
        public uint U74 { get; set; }
        public uint U88 { get; set; }
        public uint U30 { get; set; }
        public uint NameId { get; set; }
        /// <summary>Re-check which tool mode the held item puts the player in when this arrives.</summary>
        public bool UpdatesToolMode { get; set; }
        public float MaxRange { get; set; }
        public float MinRange { get; set; }
        public uint U68 { get; set; }
        /// <summary>
        /// Width of the ability's area arc, degrees (record +0x60; the sickle's plant-harvest wedge, FUN_1433abd90 →
        /// reaping SetParam 11 in radians). MinRange/MaxRange are the arc's inner and outer reach.
        /// </summary>
        public float ArcDegrees { get; set; }
        public uint U90 { get; set; }
        public bool B94 { get; set; }
        public uint U9c { get; set; }
        public uint U98 { get; set; }
        public uint Ua0 { get; set; }
        public bool Ba4 { get; set; } = true;
        public bool Ba5 { get; set; } = true;
        public float Ua8 { get; set; }
        /// <summary>Animation played when the cast starts (245 = the lightstone's use animation).</summary>
        public uint AnimationId { get; set; }
        public uint Ub0 { get; set; }
        public uint Ub4 { get; set; }
        /// <summary>Seconds the caster is busy after a confirmed cast, then extra recovery (FUN_142f346e0).</summary>
        public float CastTime { get; set; }
        public float RecoveryTime { get; set; }
        public uint DescriptionId { get; set; }
        public bool Bc4 { get; set; }
        public bool Bca { get; set; } = true;
        public uint Ue4 { get; set; } = 1;
        /// <summary>
        /// Charged abilities (kind 0xF7150354): the longest and shortest charge, seconds (reply +0xd0 / +0xd4 → record
        /// +0xb4 / +0xb8; FUN_142d767b0 waits for the shortest, then sends FinishChargedCast when the button is let go).
        /// </summary>
        public float MaxCharge { get; set; }
        public float MinCharge { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(AbilityId);
            writer.Write(KindHash);
            writer.Write(CostResource);
            writer.Write(CostAmount);
            writer.Write(IgnoreCostPool);
            writer.Write(U2c);
            writer.Write(U70);
            writer.Write(U74);
            writer.Write(U88);
            writer.Write(U30);
            writer.Write(NameId);
            writer.Write(UpdatesToolMode);
            writer.Write(MaxRange);
            writer.Write(MinRange);
            writer.Write(U68);
            writer.Write("");
            writer.Write("");
            writer.Write(ArcDegrees);
            writer.Write(U90);
            writer.Write(B94);
            writer.Write(U9c);
            writer.Write(U98);
            writer.Write(Ua0);
            writer.Write(Ba4);
            writer.Write(Ba5);
            writer.Write(Ua8);
            writer.Write(AnimationId);
            writer.Write(Ub0);
            writer.Write(Ub4);
            writer.Write(false); // b8
            writer.Write(false); // b9
            writer.Write(false); // ba
            writer.Write(CastTime);
            writer.Write(RecoveryTime);
            writer.Write(false); // bd
            writer.Write(DescriptionId);
            writer.Write(Bc4);
            writer.Write(0u);    // executed effects
            writer.Write(false); // c5
            writer.Write(false); // c6
            writer.Write(false); // c9
            writer.Write(Bca);
            writer.Write(false); // cb
            writer.Write(false); // cc
            writer.Write(0u);    // int map
            writer.Write(false); // ce
            writer.Write(MaxCharge); // d0
            writer.Write(MinCharge); // d4
            for (int i = 0; i < 3; i++)
                writer.Write(0u);  // d8..e0
            writer.Write(false); // bc
            writer.Write(false); // c7
            writer.Write(false); // c8
            writer.Write(Ue4);
            writer.Write(0u);    // e8
            writer.Write(0u);    // ec
            writer.Write(false); // bb
            writer.Write(false); // cd
            writer.Write(false); // f0
            writer.Write(false); // f1
            writer.Write(false); // f2
            writer.Write(0u);    // executed targeters
            writer.Write(false); // f3
        }
    }

    /// <summary>
    /// The client starting an ability (Abilities sub 1, built in FUN_142d71800, written by FUN_142d626d0). Only the
    /// start is read; the rest (target position, direction, target guid...) is kept raw.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCastAttempt)]
    public class AbilityCastAttempt : IReadable
    {
        public ulong CastGuid { get; private set; }
        public uint CastHash { get; private set; }
        public uint Sequence { get; private set; }
        public uint AbilityId { get; private set; }
        public float TargetX { get; private set; }
        public float TargetY { get; private set; }
        public float TargetZ { get; private set; }
        /// <summary>Everything after the target point (normal, target ids…), kept for logging until decoded.</summary>
        public byte[] Tail { get; private set; }

        /// <summary>
        /// Where the tool's targeting ring is (tail bytes 44-55: x, y, z), the spot the player picked. The first
        /// target point is the camera-line hit instead. Null when the tail is too short or empty.
        /// </summary>
        public System.Numerics.Vector3? RingPoint
        {
            get
            {
                if (Tail == null || Tail.Length < 56)
                    return null;
                var p = new System.Numerics.Vector3(BitConverter.ToSingle(Tail, 44), BitConverter.ToSingle(Tail, 48), BitConverter.ToSingle(Tail, 52));
                return p == System.Numerics.Vector3.Zero || float.IsNaN(p.X) ? null : p;
            }
        }

        /// <summary>
        /// The aim ray of a reticle cast (ranged weapons): somewhere in the tail the client puts the camera position (vec3,
        /// then w = 1.0) followed by the unit aim direction (vec3). Found by scanning for that shape near
        /// <paramref name="player"/>; null when the tail has none.
        /// </summary>
        public (System.Numerics.Vector3 Origin, System.Numerics.Vector3 Direction)? AimRay(System.Numerics.Vector3 player)
        {
            if (Tail == null)
                return null;
            for (int k = 0; k + 28 <= Tail.Length; k++)
            {
                if (BitConverter.ToSingle(Tail, k + 12) != 1f)
                    continue;
                var origin = new System.Numerics.Vector3(BitConverter.ToSingle(Tail, k), BitConverter.ToSingle(Tail, k + 4), BitConverter.ToSingle(Tail, k + 8));
                var dir = new System.Numerics.Vector3(BitConverter.ToSingle(Tail, k + 16), BitConverter.ToSingle(Tail, k + 20), BitConverter.ToSingle(Tail, k + 24));
                if (float.IsNaN(origin.X) || float.IsNaN(dir.X) || MathF.Abs(dir.Length() - 1f) > 0.01f)
                    continue;
                if (System.Numerics.Vector3.Distance(origin, player) < 30f)
                    return (origin, dir);
            }
            return null;
        }

        public void Read(GamePacketReader reader)
        {
            CastGuid = reader.ReadULong();
            CastHash = reader.ReadUInt();
            Sequence = reader.ReadUInt();
            AbilityId = reader.ReadUInt();
            TargetX = reader.ReadSingle();
            TargetY = reader.ReadSingle();
            TargetZ = reader.ReadSingle();
            Tail = reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>
    /// Confirms a cast (Abilities sub 7; reader FUN_142d63510, handler FUN_142d7a370): the client finishes the
    /// pending cast, plays its animation and may cast again.
    /// </summary>
    /// <summary>
    /// Abilities sub 0x0E FinishChargedCast (built in FUN_142d767b0 when a charged ability's button is let go):
    /// {u32 ability, u32, vec4 camera position (w = 1), vec4 aim direction (w = 0)} — the aim at release. The charge
    /// fraction stays on the client; the server times the hold itself.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityFinishChargedCast)]
    public class AbilityFinishChargedCast : IReadable
    {
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);

        /// <summary>The aim when the button was let go (camera position and unit direction), if the body has it.</summary>
        public (System.Numerics.Vector3 Origin, System.Numerics.Vector3 Direction)? Aim
        {
            get
            {
                if (Body == null || Body.Length < 40 || BitConverter.ToSingle(Body, 20) != 1f)
                    return null;
                var o = new System.Numerics.Vector3(BitConverter.ToSingle(Body, 8), BitConverter.ToSingle(Body, 12), BitConverter.ToSingle(Body, 16));
                var d = new System.Numerics.Vector3(BitConverter.ToSingle(Body, 24), BitConverter.ToSingle(Body, 28), BitConverter.ToSingle(Body, 32));
                return MathF.Abs(d.Length() - 1f) < 0.02f ? (o, d) : null;
            }
        }

        /// <summary>Does the body name this ability (u32 anywhere in it)?</summary>
        public bool Names(uint ability)
        {
            for (int i = 0; i + 4 <= Body.Length; i++)
                if (BitConverter.ToUInt32(Body, i) == ability)
                    return true;
            return false;
        }
    }

    /// <summary>Abilities sub 0x0F CancelCast (FUN_142d71590): the player gave up a cast in progress (a charge).</summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCancelCast)]
    public class AbilityCancelCast : IReadable
    {
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);
    }

    /// <summary>
    /// CastFail (0x27 sub 6, reader FUN_142d63390, handler FUN_142d79ee0): the answer to a cast the server refused. The
    /// client stops the cast's animation, clears its casting / charging state when it is this ability (+0x2a0), resets
    /// the cooldown it started for it on the hotbar (FUN_142d87d90, Abilities::CooldownSearchFunctor) and drops the
    /// pending cast (+0x478 list, matched by cast guid and hash) — where CastSuccess would have let the cooldown run.
    /// Same backtimed header as CastSuccess; body: u64 caster (+0x20), u64 cast guid (+0x28), u32 cast hash (+0x30),
    /// u32 ability (+0x38), u32 (+0x3c), u32 animation (+0x40, played when not 0), u32 (+0x44), u32 kind (+0x48; 2 on a
    /// harvest shows Harvest.CastFailMessage).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCastFail)]
    public class AbilityCastFail : IWritable
    {
        public ulong CasterGuid { get; set; }
        public ulong CastGuid { get; set; }
        public uint CastHash { get; set; }
        public uint AbilityId { get; set; }
        public uint U3C { get; set; }
        public uint AnimationId { get; set; }
        public uint U44 { get; set; }
        public uint Kind { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write((byte)6);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(CasterGuid);
            writer.Write(CastGuid);
            writer.Write(CastHash);
            writer.Write(AbilityId);
            writer.Write(U3C);
            writer.Write(AnimationId);
            writer.Write(U44);
            writer.Write(Kind);
        }
    }

    [ZoneMessage(ZoneMessageOpcode.AbilityCastSuccess)]
    public class AbilityCastSuccess : IWritable
    {
        public ulong CasterGuid { get; set; }
        public ulong CastGuid { get; set; }
        public uint CastHash { get; set; }
        public uint AbilityId { get; set; }
        public uint AnimationId { get; set; }
        public uint U40 { get; set; }
        public uint U44 { get; set; }
        public bool Force { get; set; } = true;

        public void Write(GamePacketWriter writer)
        {
            // Backtimed ability header after the opcode: the sub-opcode again, a flag byte and a u32 time.
            // (GamePacketWriter has no u16 overload; a ushort would be written as 4 bytes.)
            writer.Write((byte)7);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(CasterGuid);
            writer.Write(CastGuid);
            writer.Write(CastHash);
            writer.Write(AbilityId);
            writer.Write(AnimationId);
            writer.Write(U40);
            writer.Write(U44);
            writer.Write(Force);
        }
    }
}
