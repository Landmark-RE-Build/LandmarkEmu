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

    /// <summary>0x73 SetEnabled (client FUN_143005b30): u64 entity, u8 enabled.</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateSetEnabledWire)]
    public class PlayerUpdateSetEnabledRequest : PlayerUpdateRawRequest { }

    /// <summary>0x77 EmoteStringById (client FUN_1433080a0, e.g. "Emote.SitOther"): an emote line by text id.</summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateEmoteStringByIdWire)]
    public class PlayerUpdateEmoteStringById : PlayerUpdateRawRequest { }
}
