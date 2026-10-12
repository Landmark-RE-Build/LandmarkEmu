using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Client packets that only report something and need no answer. They are read raw so they don't show up as
    /// unreadable in the log.
    /// </summary>
    public abstract class InformationalMessage : IReadable
    {
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);
    }

    /// <summary>ClientMetrics (C→S): frame-rate and performance statistics, sent periodically.</summary>
    [ZoneMessage(ZoneMessageOpcode.ClientMetrics)]
    public class ClientMetrics : InformationalMessage { }

    /// <summary>AddClientPortraitCrc (0x75, C→S, ScreenshotManager::PostPortraitToHttp): u32 CRC-32 of portrait.png.</summary>
    [ZoneMessage(ZoneMessageOpcode.AddClientPortraitCrc)]
    public class AddClientPortraitCrc : IReadable
    {
        public uint Crc { get; private set; }
        public void Read(GamePacketReader reader) => Crc = reader.BytesRemaining >= 4 ? reader.ReadUInt() : 0;
    }

    /// <summary>FotomatBase (C→S): portrait requests (Game.Portraits).</summary>
    [ZoneMessage(ZoneMessageOpcode.FotomatBase)]
    public class FotomatRequest : InformationalMessage { }

    /// <summary>SetLocale (C→S): the client's language.</summary>
    [ZoneMessage(ZoneMessageOpcode.SetLocale)]
    public class SetLocale : InformationalMessage { }

    /// <summary>RatingBase (C→S): rating-system notifications.</summary>
    [ZoneMessage(ZoneMessageOpcode.RatingBase)]
    public class RatingRequest : InformationalMessage { }

    /// <summary>PlayerUpdate Loading (C→S): the client's loading-screen state.</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateLoading)]
    public class PlayerUpdateLoading : InformationalMessage { }

    /// <summary>ClientPcDataBase (C→S): the player's PC hardware details.</summary>
    [ZoneMessage(ZoneMessageOpcode.ClientPcDataBase)]
    public class ClientPcData : InformationalMessage { }

    /// <summary>ClientInitializationDetails (C→S): client start-up details.</summary>
    [ZoneMessage(ZoneMessageOpcode.ClientInitializationDetails)]
    public class ClientInitializationDetails : InformationalMessage { }

    /// <summary>WarpNetwork 0x113 (u32 sub, then the body): see <see cref="Game.WarpNetwork"/>.</summary>
    [ZoneMessage(ZoneMessageOpcode.WarpNetworkWire)]
    public class WarpNetworkRequest : IReadable
    {
        public uint SubOpcode { get; private set; }
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader)
        {
            SubOpcode = reader.ReadUInt();
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>
    /// TeleportBase 0xFC from the client: a u8 sub-opcode, then its body. Sub 2 RequestLocations (Travel → Other
    /// Worlds), 0x0B RandomUndergroundRequest (Travel → An Underground Adventure).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.TeleportBase)]
    public class TeleportRequest : IReadable
    {
        public const byte RequestLocations = 2, RandomUnderground = 0x0B;

        public byte SubOpcode { get; private set; }
        public byte[] Body { get; private set; }

        public void Read(GamePacketReader reader)
        {
            SubOpcode = reader.ReadByte();
            Body = reader.ReadBytes(reader.BytesRemaining);
        }
    }
}
