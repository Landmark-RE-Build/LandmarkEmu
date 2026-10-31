using System;
using System.Collections.Generic;
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
        /// <summary>
        /// +0x90 → ability record +0x70: the RequirementExpressions id an entity must pass to be this ability's target (the
        /// logging state's cursor, outline and swing, FUN_143730e50 → FUN_1432f5a00 with the entity as the target). Below
        /// 1 = anything passes, so with 0 an axe locked onto the Spire, props and NPCs; an id not in the table passes
        /// nothing (trace/0695/axe-a-client.md). Axes send 20 "Target is Harvestable Tree" (profiles 200-214,
        /// <see cref="Game.Abilities.TreeTargetRequirement"/>).
        /// </summary>
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

        /// <summary>
        /// KeywordDefinitions ids shown in the ability's tooltip (reply +0x138 "int map", reader FUN_142c34f90: u32 count,
        /// then u32 key / u32 value pairs; the value lands at node +0, which the tooltip FUN_14376c210 looks up as the
        /// keyword and draws in its UI colour, skipping HIDE_FROM_CLIENT rows) [P]. Keys are 1, 2, … (ours).
        /// OPEN-LEADS L158.
        /// </summary>
        public List<uint> Keywords { get; set; } = new();
        public float MinCharge { get; set; }

        /// <summary>
        /// ClientExecutedEffectData (reply +0xf8, reader FUN_142e04680: u32 count, 14 × u32 each, FUN_142e03a00) [P]: the
        /// client-run movement effects of sprint / jump / glide abilities (Game.MovementEffects, OPEN-LEADS L160).
        /// </summary>
        public List<Game.MovementEffects.Effect> ExecutedEffects { get; } = new();

        /// <summary>
        /// ClientExecutedTargeterData (reply +0x118 → record +0x100, reader FUN_142e04720, element FUN_142e03c60) [P]: the
        /// targeters the client runs itself from the cast start (FUN_142e0f810 → FUN_142e257b0), e.g. a melee weapon's Sweep
        /// (Game.Combat.WeaponCombat.Targeters, OPEN-LEADS L170).
        /// </summary>
        public List<ClientExecutedTargeter> ExecutedTargeters { get; } = new();

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
            writer.Write((uint)ExecutedEffects.Count);    // executed effects (+0xf8)
            foreach (Game.MovementEffects.Effect effect in ExecutedEffects)
                effect.Write(writer);
            writer.Write(false); // c5
            writer.Write(false); // c6
            writer.Write(false); // c9
            writer.Write(Bca);
            writer.Write(false); // cb
            writer.Write(false); // cc
            writer.Write((uint)Keywords.Count);    // int map: keyword tags
            for (int i = 0; i < Keywords.Count; i++)
            {
                writer.Write((uint)(i + 1));
                writer.Write(Keywords[i]);
            }
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
            writer.Write((uint)ExecutedTargeters.Count);  // executed targeters (+0x118)
            foreach (ClientExecutedTargeter targeter in ExecutedTargeters)
                targeter.Write(writer);
            writer.Write(false); // f3
        }
    }

    /// <summary>
    /// One ClientExecutedTargeterData element of the caster info (OPEN-LEADS L170). Wire order (FUN_142e03c60) [P]: u32 +0x00,
    /// u32 +0x04, u32 +0x08, u32 +0x0c, u32 +0x14, u32 +0x10, u32 +0x18 … +0x28, then List&lt;ShapeDefinition&gt; (u32 count;
    /// FUN_142e047c0 / element FUN_142e03e60). What the client does with each field [P use, 0.695 decompile]:
    /// <list type="bullet">
    /// <item>+0x00 <see cref="Id"/>: echoed in ClientTargeterFindTarget 0x27/0x24 and ClientTargeterEnd 0x27/0x23.</item>
    /// <item>+0x04 <see cref="Type"/>: the ObjectFactory key (looked up as value ^ 0xA142E048, registered the same way in
    /// FUN_142e081c0): <see cref="Sweep"/> 0xDEECF7D8 (ClientExecutedTargeterSweep), <see cref="Block"/> 0xCC82431F
    /// (ClientExecutedTargeterBlock). An unknown type builds nothing.</item>
    /// <item>+0x08 <see cref="Origin"/>: where the targeter is anchored (FUN_14354d880): 1 = the caster's position, 3 = a
    /// point on the camera line, anything else = the cast's aim point.</item>
    /// <item>+0x0c <see cref="Follow"/>: 2 = the anchor is taken again on every update (FUN_14354d190 / FUN_14354cda0).</item>
    /// <item>+0x10 <see cref="MaxTargets"/> (u32): FindTarget is sent only while fewer opponents than this were hit
    /// (FUN_14354e800; 0 = no limit).</item>
    /// <item>+0x14 <see cref="Delay"/> (f32, s): the targeter starts this long after the cast start plus the record's
    /// +0x148 (reply +0x74, FUN_142e16cb0) (FUN_14354ebb0).</item>
    /// <item>+0x18 <see cref="Duration"/> (f32, s): it runs this long, then ends and sends ClientTargeterEnd
    /// (FUN_14354e9b0 → slot 5 FUN_14354d640).</item>
    /// <item>+0x1c / +0x20 <see cref="StartAngle"/> / <see cref="EndAngle"/> (f32, degrees): over the duration the sweep
    /// moves linearly from one to the other; each update tests the angle swept since the last one against every
    /// character's bounds, measured round the caster's up axis from its facing (FUN_14354e3a0 → FUN_142eaae30, atan2,
    /// × 57.2958).</item>
    /// <item>+0x24 <see cref="RehitSeconds"/> (f32, s): a character already hit is reported again only after this long
    /// (HitTracker, × 1000 ms).</item>
    /// <item>+0x28 <see cref="Radius"/> (f32, m): the Sweep's sphere query round the anchor (FUN_142b946b0, a float radius
    /// as its other callers pass).</item>
    /// <item>Shapes: read by the Block targeter only (FUN_143c22990 / FUN_143c24d40: kind +0x10 1 sphere radius +0x30 [P],
    /// 2 box of half sizes +0x34 / +0x38 / +0x30, 3 a cached physics shape, 4 a capsule [names I]; offsets +0x20..+0x28, yaw +0x2c, sizes +0x30..+0x38); the Sweep never reads them. Not sent (count 0).</item>
    /// </list>
    /// +0x08 … +0x0c are u32; the other fields after +0x10 are read as floats.
    /// </summary>
    public sealed class ClientExecutedTargeter
    {
        public const uint Sweep = 0xDEECF7D8, Block = 0xCC82431F;

        public uint Id { get; set; }
        public uint Type { get; set; }
        public uint Origin { get; set; }
        public uint Follow { get; set; }
        public float Delay { get; set; }
        public uint MaxTargets { get; set; }
        public float Duration { get; set; }
        public float StartAngle { get; set; }
        public float EndAngle { get; set; }
        public float RehitSeconds { get; set; }
        public float Radius { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Id);
            writer.Write(Type);
            writer.Write(Origin);
            writer.Write(Follow);
            writer.Write(Delay);       // +0x14 (before +0x10 on the wire)
            writer.Write(MaxTargets);  // +0x10
            writer.Write(Duration);
            writer.Write(StartAngle);
            writer.Write(EndAngle);
            writer.Write(RehitSeconds);
            writer.Write(Radius);
            writer.Write(0u);          // List<ShapeDefinition>: none (the Sweep reads none)
        }
    }

    /// <summary>
    /// ClientTargeterFindTarget (Abilities 0x27, u16 sub 0x24; built in FUN_14354e800, written by FUN_14354c760) [P]: the
    /// client's Sweep hit a character. Body: u64 the character hit, u64 cast guid + u32 cast hash (the targeter's +0x38 /
    /// +0x40, the same pair the cast attempt carries), u32 the targeter data id (element +0x00), then 16 bytes the writer
    /// sends as zeros (packet +0x40..+0x4c, never set by FUN_14354e800).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityClientTargeterFindTarget)]
    public class AbilityClientTargeterFindTarget : IReadable
    {
        public ulong Target { get; private set; }
        public ulong CastGuid { get; private set; }
        public uint CastHash { get; private set; }
        public uint TargeterId { get; private set; }
        public byte[] Tail { get; private set; }

        public void Read(GamePacketReader reader)
        {
            Target = reader.ReadULong();
            CastGuid = reader.ReadULong();
            CastHash = reader.ReadUInt();
            TargeterId = reader.ReadUInt();
            Tail = reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>
    /// ClientTargeterEnd (Abilities 0x27, u16 sub 0x23; FUN_14354d570, written by FUN_14354c520) [P]: a client targeter
    /// finished (its duration ran out, or a Sweep / Block met a blocking character). Body: u64 cast guid, u32 cast hash,
    /// u32 targeter data id. Sent once per targeter (guarded by targeter +0x60).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityClientTargeterEnd)]
    public class AbilityClientTargeterEnd : IReadable
    {
        public ulong CastGuid { get; private set; }
        public uint CastHash { get; private set; }
        public uint TargeterId { get; private set; }

        public void Read(GamePacketReader reader)
        {
            CastGuid = reader.ReadULong();
            CastHash = reader.ReadUInt();
            TargeterId = reader.ReadUInt();
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

    /// <summary>
    /// Abilities sub 0x0F CancelCast: the player gave up a cast in progress (a charge) or let go of a channel's key.
    /// 0.695 builder FUN_142e0f5a0 (ClientAbilityManager CancelCast): the packet object is {vftable, 0x27, 0x0F, bool},
    /// written by FUN_142e048b0 as u16 0x27, u16 0x0F (FUN_142e00c50) and then the one bool byte [P]. It names no
    /// ability or cast: the server has to know which cast is pending. The bool is 1 when the input manager interrupts the
    /// cast for another action (FUN_1434793b0 "interrupting ability %d for entry %d"), 0 when a channel's key is let go
    /// (FUN_1434783f0 "GiveTime: Channel: end ability") or an action state cancels it (FUN_143454c10 / d30 …).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCancelCast)]
    public class AbilityCancelCast : IReadable
    {
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);

        /// <summary>The trailing bool: true when another action interrupted the cast.</summary>
        public bool Interrupted => Body is { Length: > 0 } && Body[^1] != 0;
    }

    /// <summary>
    /// CastContinue (0x27 sub 8; reader FUN_142e011b0, handler FUN_142e17b90) [P]: the server's "keep going" for a
    /// channelled cast. Same backtimed header as CastSuccess; body: u64 caster (+0x20), u64 cast guid (+0x28), u32 cast
    /// hash (+0x30), u32 ability (+0x38), u32 +0x3c, u32 +0x40, u32 +0x44 (not read by the handler), u32 animation
    /// (+0x48, played when not 0), u32 effect (+0x4c, caster vtable +0x4a8 when not 0), u32 (+0x50, caster vtable
    /// +0x250(0, it)), bool (+0x54: when set the caster's own client skips the rest).
    /// <para>
    /// The handler acts only while the ability is the caster's current one (ClientAbilityManager +0x2a0). For the local
    /// player it starts the global cooldown from the record's cast / recovery times, plays the ActiveTime pair (+0x940 /
    /// +0x944) and, when no channelled effect is running (+0x2b8 empty), starts the cast's ClientExecutedEffects again
    /// (FUN_142e255e0). It never resets the cast state, unlike CastSuccess / CastFail, so the channel goes on.
    /// </para>
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCastContinue)]
    public class AbilityCastContinue : IWritable
    {
        public ulong CasterGuid { get; set; }
        public ulong CastGuid { get; set; }
        public uint CastHash { get; set; }
        public uint AbilityId { get; set; }
        public uint AnimationId { get; set; }
        public uint EffectId { get; set; }
        public uint U50 { get; set; }
        public bool SkipOwn { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write((byte)8);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(CasterGuid);
            writer.Write(CastGuid);
            writer.Write(CastHash);
            writer.Write(AbilityId);
            writer.Write(0u);    // +0x3c
            writer.Write(0u);    // +0x40
            writer.Write(0u);    // +0x44
            writer.Write(AnimationId);
            writer.Write(EffectId);
            writer.Write(U50);
            writer.Write(SkipOwn);
        }
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
