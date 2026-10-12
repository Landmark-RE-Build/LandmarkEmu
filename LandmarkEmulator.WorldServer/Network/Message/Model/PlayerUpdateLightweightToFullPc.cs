using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Shared.Network.Message.Model.Shared;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Turns a lightweight character placeholder into a full, visible character.
    /// </summary>
    /// <remarks>
    /// Layout per the 0.589 client (PlayerUpdate case 0x5B). After this packet's own fields the client
    /// reads three more from the same buffer: a movement body, a PlayerUpdateUpdateStat and a
    /// ClientUpdateUpdateCalculatedStats, each with its own opcode header.
    /// The character data blob is found by transient id, so it must match the lightweight spawn.
    /// Unknown fields are zero and every list empty, which keeps the reply small.
    /// </remarks>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateLightweightToFullPcWire)]
    public class PlayerUpdateLightweightToFullPc : IWritable
    {
        /// <summary>
        /// Smallest valid movement body: type 1 carries only a 2-bit-length uint. The 6th byte is the movement
        /// version, which must be 1 like the clients' own updates.
        /// </summary>
        public static readonly byte[] EmptyMovement = { 0x01, 0, 0, 0, 0, 1, 0 };

        public ulong Guid { get; set; }
        public uint TransientId { get; set; }
        public uint ModelId { get; set; }
        public CharacterModelInfo Model { get; set; } = new();
        public byte[] Movement { get; set; } = EmptyMovement;
        public List<(uint StatId, float Value)> Stats { get; } = new();

        public void Write(GamePacketWriter writer)
        {
            var blob = new List<byte>();
            WriteCharacterData(new GamePacketWriter(blob));
            writer.Write(false);                // not compressed
            writer.Write((uint)blob.Count);
            writer.WriteBytes(blob.ToArray());

            writer.WriteBytes(Movement);

            // PlayerUpdateUpdateStat: base 0x26, sub-opcode byte 0x53.
            writer.WriteBytes(new byte[] { 0x26, 0x00, 0x53 });
            writer.Write(Guid);
            writer.Write(0u);

            // ClientUpdateUpdateCalculatedStats: base 0x29, u16 sub-opcode 0x2C.
            writer.WriteBytes(new byte[] { 0x29, 0x00, 0x2C, 0x00 });
            writer.Write(Guid);
            writer.Write((uint)Stats.Count);
            foreach ((uint statId, float value) in Stats)
            {
                writer.Write(statId);
                writer.Write(value);
            }
        }

        private void WriteCharacterData(GamePacketWriter writer)
        {
            writer.WriteUIntWith2BitLength((int)TransientId);
            for (int i = 0; i < 6; i++)
                writer.Write(0u);
            writer.Write(0u);                   // empty list
            Model.Write(writer);
            writer.Write(0u);
            writer.Write("");
            writer.Write("");
            writer.Write("");
            // Temporary appearance model (ProxiedCharacter::SwitchToTemporaryAppearance). Non-zero swaps the character
            // to that bare model and hides its own body, so it stays 0 unless a transformation is wanted.
            writer.Write(0u);
            for (int i = 0; i < 3; i++)
                writer.Write(0u);
            for (int i = 0; i < 4; i++)
                writer.Write(0u);
            writer.Write(0u);                   // empty list
            writer.Write(0u);
            writer.Write(0u);
            writer.WriteBytes(new byte[3]);     // 3 bools
            writer.Write(0u);                   // byte array
            writer.Write(0u);                   // empty list
            writer.Write(0u);                   // byte array
        }
    }
}
