using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Sent when the player logs out to character select. The client then waits for a
    /// <see cref="CharacterSelectSessionResponse"/> carrying a new login session.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.CharacterSelectSessionRequest)]
    public class CharacterSelectSessionRequest : IReadable
    {
        public void Read(GamePacketReader reader)
        {
        }
    }

    /// <summary>
    /// On success the client stores <see cref="SessionId"/> as its login session and logs back in to the login server
    /// with it (0.589 client: PacketCharacterSelectSessionResponse). On failure it shuts down.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.CharacterSelectSessionResponse)]
    public class CharacterSelectSessionResponse : IWritable
    {
        public bool Success { get; set; }
        public string SessionId { get; set; } = "";

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Success);
            writer.Write(SessionId);
        }
    }
}
