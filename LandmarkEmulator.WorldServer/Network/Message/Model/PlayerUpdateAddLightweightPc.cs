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
        public byte Unknown0 { get; set; }
        public uint ModelId { get; set; }
        public uint Unknown1 { get; set; }
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

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.WriteUIntWith2BitLength((int)TransientId);
            Identity.Write(writer);
            writer.Write(Unknown0);
            writer.Write(ModelId);
            writer.Write(Unknown1);
            writer.Write(Position);
            writer.Write(Rotation);
            writer.Write(Scale);
            writer.WriteBytes(new byte[3]);     // 3 bools
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
