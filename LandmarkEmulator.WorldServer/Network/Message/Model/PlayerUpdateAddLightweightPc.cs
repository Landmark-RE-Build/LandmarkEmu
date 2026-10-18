using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Spawns another player's character on the client.
    /// </summary>
    /// <remarks>
    /// Layout per the 0.589 client's reader (details in MODLOG.md). Field names past the identity are guesses; zero is accepted.
    /// </remarks>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateAddLightweightPcWire)]
    public class PlayerUpdateAddLightweightPc : IWritable
    {
        public ulong Guid { get; set; }
        public uint TransientId { get; set; }
        public SendSelfToClient.CharacterIdentity Identity { get; set; } = new();
        /// <summary>
        /// +0xd8 (0.695 reader FUN_142c26e50) the character's faction: case 0x57 of the PlayerUpdate dispatcher
        /// FUN_142cdfa60 copies it into the new character's data (+0xc4), FUN_142f8a0b0 hands it to the entity (vtable
        /// +0x238 → entity +0x1448), and the friend / foe tests compare it between two characters when both are 1-3
        /// (FUN_142ea8f90 and its neighbours; the shared engine's three factions) [P use; I name]. Landmark has no
        /// factions: 0, the client's own default (DAT_1455fe51c, .bss).
        /// </summary>
        public byte Faction { get; set; }
        public uint ModelId { get; set; }
        /// <summary>
        /// +0xe0 the character's current profile id: FUN_142ec55e0 stores it at entity +0x1474, the slot the client's
        /// "profile select %d" uses (part_0063) and looks profile definitions up by (FUN_142e9e610) [P]. Sent as the
        /// player's own profile (SendSelfToClient.CurrentProfile); 0 until 2026-10-18.
        /// <para>Checked against the 0.695 client for review 2026-10-18b #8 [P]: the reader FUN_142c26e50 takes it right
        /// after ModelId (+0xdc); the dispatcher's case 0x57 (FUN_142cdfa60) hands it to FUN_142ec55e0 on the new entity.
        /// That setter is the one every profile source uses: the own player's from its SendSelf character data (+600,
        /// CurrentProfile, FUN_142cb1f60), PlayerUpdate 0x3D (case 0x3d, ReadyToReviveResponse in the server's enum,
        /// on any entity found by guid), FUN_142cd9980, FUN_142cdd710 and FUN_142d54040. It sets +0x1474, re-derives
        /// three entity flags (+0x1453 bits 0x40 / 0x20 / 0x10) from whether the id equals three client constants, and
        /// re-runs FUN_142ecc900 (entity +0x41f0, +0x1453 bit 0x04) and the entity's vtable +0x660 refresh — exactly
        /// what the player's own client does for its own entity with the same id. With 0 (no profile row) the remote
        /// entity kept its initial state and none of that ran [I: the slot starts at 0, so the setter's change test
        /// skipped it]. Other clients now see the player as its own client does, which is the reading the client was
        /// built for. The two-client look check (walk, swim, combat stance, build mode) stays on the test checklist.</para>
        /// </summary>
        public uint ProfileId { get; set; }
        public Vector4 Position { get; set; }
        public Vector4 Rotation { get; set; } = new(0f, 0f, 0f, 1f);

        /// <summary>
        /// Copied onto the character; probably the model scale (0 would hide it).
        /// </summary>
        public float Scale { get; set; } = 1f;

        /// <summary>
        /// Must match the version byte in this character's movement updates (ClientUpdateMovementVersion, 1), or the
        /// client drops every non-full update for it (0.589 client).
        /// </summary>
        public byte MovementVersion { get; set; } = 1;

        /// <summary>
        /// The three bools after the scale [P, 0.695 reader FUN_142c26e50 +0x114 / +0x115 / +0x116, used by the
        /// PlayerUpdate dispatcher FUN_142cdfa60 case 0x57]: underage (entity +0x1453 bit 0x08, as SendSelf's IsUnderage),
        /// member (FUN_142ec2ff0 without the effect: entity +0x1452 bit 0, the player's membership, Game.Membership), and
        /// GM (entity +0x1453 bit 0x80, the bit the requirement class ClientRequirementIsGM tests, FUN_1437107a0 [P]):
        /// false, as for every live player (only Daybreak's GM characters had it; which requirement rows test it is not
        /// traced, so the server's admins are not flagged).
        /// </summary>
        public bool IsUnderage { get; set; }
        public bool IsMember { get; set; }
        public bool IsGm { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.WriteUIntWith2BitLength((int)TransientId);
            Identity.Write(writer);
            writer.Write(Faction);
            writer.Write(ModelId);
            writer.Write(ProfileId);
            writer.Write(Position);
            writer.Write(Rotation);
            writer.Write(Scale);
            writer.Write(IsUnderage);
            writer.Write(IsMember);
            writer.Write(IsGm);
            writer.Write(0ul);                  // mount guid?
            writer.Write(0u);
            writer.Write(0u);
            writer.WriteBytes(new byte[5]);     // 5 bools
            writer.Write(MovementVersion);
            writer.Write(0ul);
            writer.Write(0ul);
            writer.Write(0ul);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0ul);
            writer.WriteBytes(new byte[1]);
        }
    }
}
