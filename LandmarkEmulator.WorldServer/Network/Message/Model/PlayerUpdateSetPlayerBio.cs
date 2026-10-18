using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PlayerUpdate 0x80 SetPlayerBio (0.695 only, class PlayerUpdatePacketSetPlayerBio): the player's profile text,
    /// sent by FUN_142fd4290 after the client's own bad-word check, at most about every 1.5 s. Wire: u16 0x26, u32 sub
    /// 0x80, str bio (trace/0695/packets-695.md §1); WorldSession.GetOpcode reads the u32 sub.
    /// The client needs no answer (its reader of a 0x26/0x80 from the server discards it); the player's own bio comes
    /// back in SendSelfToClient, other players' in the Inspect reply.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateSetPlayerBioWire)]
    public class PlayerUpdateSetPlayerBio : IReadable
    {
        public string Bio { get; private set; } = "";

        public void Read(GamePacketReader reader)
        {
            Bio = reader.ReadString();
        }
    }
}
