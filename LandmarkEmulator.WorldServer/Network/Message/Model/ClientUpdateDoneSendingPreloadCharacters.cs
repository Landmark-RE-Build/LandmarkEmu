using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientUpdate DoneSendingPreloadCharacters (S→C, 0x29 sub 0x1B): the characters the client should know before
    /// entering have all been sent. Body: bool <see cref="WaitForLoad"/> (0.695 reader FUN_142c48880 → +0x18) [P].
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateDoneSendingPreloadCharacters)]
    public class ClientUpdateDoneSendingPreloadCharacters : IWritable
    {
        /// <summary>
        /// True: the client marks every entity it holds as required (FUN_142cd9980 case 0x1A → FUN_142f86f80 sets entity
        /// +0x1458 bit 4) and keeps the loading screen until each is loaded (WaitForWorldReady's RequiredCharactersLoaded,
        /// FUN_142f86ee0: entity vtable +0x4f8) [P].
        /// </summary>
        public bool WaitForLoad { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(WaitForLoad);
        }
    }
}
