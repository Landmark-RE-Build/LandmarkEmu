using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// PlayerUpdate BindingCustomization (C→S): the player's key bindings. The client keeps them locally, so the body is
    /// not read.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateBindingCustomization)]
    public class PlayerUpdateBindingCustomization : IReadable
    {
        public void Read(GamePacketReader reader)
        {
            reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>
    /// Abilities 0x27 sub 0x16 EffectUpdatesRequest (empty, FUN_142f33950): on every zone load the client clears its
    /// effect list and asks for the player's current effects. The server keeps no lasting effects, so nothing to send.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityEffectUpdatesRequest)]
    public class AbilityEffectUpdatesRequest : IReadable
    {
        public void Read(GamePacketReader reader)
        {
            reader.ReadBytes(reader.BytesRemaining);
        }
    }

    /// <summary>Abilities 0x27 sub 0x1E RequestCancelEffect (buff bar cancel): header, then i32 effect id (last 4 bytes).</summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityRequestCancelEffect)]
    public class AbilityRequestCancelEffect : IReadable
    {
        public uint EffectId { get; private set; }

        public void Read(GamePacketReader reader)
        {
            byte[] body = reader.ReadBytes(reader.BytesRemaining);
            if (body.Length >= 4)
                EffectId = System.BitConverter.ToUInt32(body, body.Length - 4);
        }
    }

    /// <summary>
    /// Abilities 0x27 sub 0x17 DelayedTargetInfo {u32 ability, target point...}: aim data a reticle ability (e.g. 799, the
    /// six-shooters) sends after its cast; the server already takes the aim from the cast itself (AimRay).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityDelayedTargetInfo)]
    public class AbilityDelayedTargetInfo : IReadable
    {
        public void Read(GamePacketReader reader)
        {
            reader.ReadBytes(reader.BytesRemaining);
        }
    }
}
