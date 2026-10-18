using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// The player's own character stats (ClientUpdate 0x29, u16 sub 6; 0.695 reader FUN_142c492a0, handler FUN_142cd9980
    /// case 5): <c>u32 count, count × CharacterStat</c> [P]. The client stores each in its player data (+0x75670,
    /// FUN_142fd48b0) and then lets the local character copy its acceleration times into the animation (vtable +0x588).
    /// Game/CharacterStats holds the values (the live capture).
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateUpdateStat)]
    public class ClientUpdateUpdateStat : IWritable
    {
        public List<Game.CharacterStats.Stat> Stats { get; } = new();

        public void Write(GamePacketWriter writer) => Game.CharacterStats.WriteList(writer, Stats);
    }
}
