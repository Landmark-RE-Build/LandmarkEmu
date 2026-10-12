using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// ClientUpdate 0x20 RespawnLocations (S→C, client reader FUN_142b83ae0): the respawn points the death window offers.
    /// The client keeps the list; its "graveyard" button (Lua ShowGraveyardRespawn, FUN_1433878f0) shows when an entry
    /// has type 11, and ReviveAtGraveyard sends PlayerUpdate 0x3B Respawn with type 11.
    /// Record (FUN_142b95320): u64 guid, u8 type, vec4 position, 4 × u32, string name, u32, bool, u32, bool,
    /// u32 n × {u32, u32}.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.ClientUpdateRespawnLocations)]
    public class ClientUpdateRespawnLocations : IWritable
    {
        public class RespawnLocation
        {
            public ulong Guid { get; set; }
            /// <summary>Respawn type: 11 graveyard / safe point, 12 hub.</summary>
            public byte Type { get; set; } = 11;
            public Vector4 Position { get; set; }
            public string Name { get; set; } = "";

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Guid);
                writer.Write(Type);
                writer.Write(Position);
                for (int i = 0; i < 4; i++)
                    writer.Write(0u);
                writer.Write(Name);
                writer.Write(0u);
                writer.Write(false);
                writer.Write(0u);
                writer.Write(false);
                writer.Write(0u);                // u32 pair list
            }
        }

        public List<RespawnLocation> RespawnLocations { get; set; } = new();

        public void Write(GamePacketWriter writer)
        {
            writer.Write((uint)RespawnLocations.Count);
            foreach (RespawnLocation location in RespawnLocations)
                location.Write(writer);
        }
    }
}
