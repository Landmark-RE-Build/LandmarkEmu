using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Abilities 0x27 sub 2 (C→S) {u32 ability id}: the client wants an ability's caster info (range, cast time, costs)
    /// before it will use it. Answered in MiscHandler.AbilityCasterInfoRequest.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.AbilityCasterInfoRequest)]
    public class AbilityCasterInfoRequest : IReadable
    {
        /// <summary>
        /// The ability id: the 0.695 client sends it from FUN_142e234c0 when its caster-info cache (keyed by this id) has
        /// no entry for the ability [P].
        /// </summary>
        public uint AbilityId { get; set; }

        public void Read(GamePacketReader reader)
        {
            AbilityId = reader.ReadUInt();
        }
    }
}
