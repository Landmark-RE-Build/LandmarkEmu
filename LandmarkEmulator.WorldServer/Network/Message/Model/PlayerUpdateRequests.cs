using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>A PlayerUpdate request whose body is kept raw (handlers read the fields they need).</summary>
    public abstract class PlayerUpdateRawRequest : IReadable
    {
        public byte[] Body { get; private set; }
        public void Read(GamePacketReader reader) => Body = reader.ReadBytes(reader.BytesRemaining);
    }

    /// <summary>0x0A RemoveTemporaryAppearance (client FUN_143303a20): u64 character.</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateRemoveTemporaryAppearanceWire)]
    public class PlayerUpdateRemoveTemporaryAppearanceRequest : PlayerUpdateRawRequest { }

    /// <summary>0x18 RequestStripEffect (Lua RequestStripEffect FUN_143520c70 → FUN_142ca2be0): the effect to drop.</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateRequestStripEffectWire)]
    public class PlayerUpdateRequestStripEffect : PlayerUpdateRawRequest { }

    /// <summary>0x1A PreferredLanguages (Lua SetPreferredLanguage FUN_143518e50 → FUN_142f344b0): u64, u32 language flags.</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdatePreferredLanguagesWire)]
    public class PlayerUpdatePreferredLanguages : PlayerUpdateRawRequest { }

    /// <summary>
    /// 0x4C SafeEject (writer FUN_142ebdda0, send template FUN_142e85070): one byte after the header, packet +0x18, always 1
    /// [P]. Sent when a launcher (flinger, thrower, anti-grav or thrust pad) throws the local player; see Game/SafeEject.cs.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateSafeEjectWire)]
    public class PlayerUpdateSafeEjectRequest : IReadable
    {
        public byte Flag { get; private set; }
        public void Read(GamePacketReader reader) => Flag = reader.BytesRemaining > 0 ? reader.ReadByte() : (byte)0;
    }

    /// <summary>0x73 SetEnabled (client FUN_143005b30): u64 entity, u8 enabled.</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateSetEnabledWire)]
    public class PlayerUpdateSetEnabledRequest : PlayerUpdateRawRequest { }

    /// <summary>0x77 EmoteStringById (client FUN_1433080a0, e.g. "Emote.SitOther"): an emote line by text id.</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateEmoteStringByIdWire)]
    public class PlayerUpdateEmoteStringById : PlayerUpdateRawRequest { }
}
