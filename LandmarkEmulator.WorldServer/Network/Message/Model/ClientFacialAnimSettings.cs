using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// C→S <c>0xF3</c> ClientFacialAnimSettings: single opt-in byte for receiving proximity facial anim
    /// (<c>/soemote receiver</c>). Maps to SendSelf <c>FacialAnimReceiver</c> (+0x285). No S→C counterpart.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientFacialAnimSettings)]
    public class ClientFacialAnimSettings : IReadable
    {
        /// <summary><c>1</c> = receive <c>0xF2</c>; <c>0</c> = opt out (server must not send F2).</summary>
        public bool Receiver { get; set; } = true;

        public void Read(GamePacketReader reader)
        {
            Receiver = reader.BytesRemaining > 0 && reader.ReadByte() != 0;
        }
    }
}
