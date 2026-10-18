using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ActivityBase 0xA0, client → server [P, 0.695]: u16 0xA0, u8 1, u8 kind (header writer FUN_142c12c10 writes the u16
    /// opcode and the low byte of +0x10, the sender FUN_142c41640 then the low byte of +0x18), then the kind's body.
    /// Kind 4 = ActivityPacketActivityListRefreshRequest, no body (sent twice, zone and world channel, after
    /// PacketMembershipActivation 0x66 in the zone dispatcher case 0x2c, and on leaving a mini game, FUN_14331a850);
    /// kind 2 = ActivityPacketJoinActivityRequest (Lua RequestToJoinActivity, FUN_142da26b0: u32 activity id,
    /// u32 …). Read whole; <see cref="Handlers.ActivityHandler"/> answers.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ActivityBase)]
    public class ActivityRequest : IReadable
    {
        public byte Group { get; private set; }
        public byte Kind { get; private set; }
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader)
        {
            Group = reader.BytesRemaining > 0 ? reader.ReadByte() : (byte)0;
            Kind = reader.BytesRemaining > 0 ? reader.ReadByte() : (byte)0;
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}
