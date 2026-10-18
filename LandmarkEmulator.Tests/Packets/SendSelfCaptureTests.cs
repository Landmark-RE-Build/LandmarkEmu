using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.IO;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// The live login packet (Resources/PacketDumps/SendSelf.bin: u16 opcode 0x0F, u32 size, body; captured 2017-02-20,
    /// the 0.695 era) against the 0.695 model: SendSelfToClient.Read (the 0.695 reader FUN_142c19bd0's layout) must
    /// consume the body to the last byte and Write must give it back byte for byte, so renaming fields can never move
    /// one. The values checked are live ones the model's defaults follow (docs/sendself.md).
    /// </summary>
    public class SendSelfCaptureTests
    {
        private static byte[] Body()
        {
            byte[] data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "SendSelf.bin"));
            uint size = BitConverter.ToUInt32(data, 2);
            Assert.Equal((uint)data.Length - 6u, size);
            return data.AsSpan(6).ToArray();
        }

        [Fact]
        public void LiveCapture_ReadsWithThe695Layout_AndWritesBackByteForByte()
        {
            byte[] body = Body();
            var reader = new GamePacketReader(body);
            var message = new SendSelfToClient();
            message.Read(reader);
            Assert.Equal(0u, reader.BytesRemaining, "the 0.695 reader must use the whole body");

            byte[] again = Wire.Write(message);
            Assert.Equal(body, again, "SendSelfToClient.Write must give the live body back unchanged");
        }

        [Fact]
        public void LiveCapture_ManagerBlocks_MatchTheModelDefaults()
        {
            var message = new SendSelfToClient();
            message.Read(new GamePacketReader(Body()));
            var defaults = new SendSelfToClient();

            // Inventory filters: live NextFilterId 1, the client's own default (was 0 in the model until 2026-10-18).
            Assert.Equal(1u, message.InventoryFilters.NextFilterId);
            Assert.Equal(message.InventoryFilters.NextFilterId, defaults.InventoryFilters.NextFilterId);
            // The trailing byte of the acquire timer and item timer managers: 0x80 live.
            Assert.Equal(defaults.AcquireTimers.Flags160, message.AcquireTimers.Flags160);
            Assert.Equal(defaults.ItemTimers.ListenerFlags, message.ItemTimers.ListenerFlags);
            Assert.Equal(defaults.SkillPoints.Slot771b8, message.SkillPoints.Slot771b8);
            // One experience record, level 1 (the client's value for a missing record too).
            Assert.Single(message.ExperienceRecords);
            Assert.Equal(1u, message.ExperienceRecords[0].Level);
        }

        [Fact]
        public void LiveCapture_NamedTailAndProfileFields_MatchTheModelDefaults()
        {
            var message = new SendSelfToClient();
            message.Read(new GamePacketReader(Body()));
            var defaults = new SendSelfToClient();

            // The last played profile per type (+0x1d0): live sent it empty, and so does the server now.
            Assert.Empty(message.LastPlayedProfileByType);
            Assert.Empty(defaults.LastPlayedProfileByType);
            Assert.Empty(message.AbilityCooldowns);
            // The cast key seed (+0x753f0): live sent 0x5BB9EB0B; the server sends 0 (it never checks the cast key).
            Assert.Equal(0x5BB9EB0Bu, message.CastKeySeed);
            Assert.Equal(0u, defaults.CastKeySeed);
            Assert.Equal(defaults.CurrentAdventureId, message.CurrentAdventureId);

            // The vehicle slots: no vehicles, and seat 0xFFFFFFFF ("in no vehicle", the client's own default) live.
            Assert.Equal(0ul, message.OwnedVehicleGuid);
            Assert.Equal(0u, message.Unused244a8);
            Assert.Equal(0ul, message.OccupiedVehicleGuid);
            Assert.Equal(0ul, message.Unused24618);
            Assert.Equal(0u, message.Unused24620);
            Assert.Equal(0xFFFFFFFFu, message.OccupiedVehicleSeat);
            Assert.Equal(message.OccupiedVehicleSeat, defaults.OccupiedVehicleSeat);
            Assert.False(message.IsAdminMaybe);
            Assert.Equal("", message.Bio);
        }

        [Fact]
        public void LiveCapture_ReadCapture_IsThe695Layout()
        {
            byte[] file = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "SendSelf.bin"));
            var reader = new GamePacketReader(file.AsSpan(2).ToArray());
            var captured = new SendSelfToClient();
            captured.ReadCapture(reader);
            Assert.Equal(0u, reader.BytesRemaining, "ReadCapture must use the whole capture");
            Assert.Equal(Body(), Wire.Write(captured), "ReadCapture and Write must agree byte for byte");
        }
    }
}
