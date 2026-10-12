using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Spawns an NPC (creature) entity: PlayerUpdate 0x59, the lightweight base alone (client case 0x58 reads only
    /// FUN_142b8cfc0, then FUN_142c43fe0 spawns it) — the same base <see cref="PlayerUpdateAddLightweightProp"/> uses.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateAddLightweightNpcWire)]
    public class PlayerUpdateAddLightweightNpc : IWritable
    {
        public static readonly (double X, double Y, double Z) ZoneOrigin = (25584000d, 20000d, 25564500d);

        public ulong Guid { get; set; }
        public uint TransientId { get; set; }
        public string Name { get; set; } = "";
        public uint NameId { get; set; }
        /// <summary>Models.txt id (2 = Freeport stone-circle campfire).</summary>
        public uint ModelId { get; set; }
        public Vector4 Position { get; set; }
        public Vector4 Rotation { get; set; } = new(0f, 0f, 0f, 1f);
        public Vector4 Scale { get; set; } = new(1f, 1f, 1f, 1f);
        /// <summary>
        /// Control mode (signed byte, reader +0x19c → entity +0x41a0 via SetupLightweightNpc 142cbaca0). The per-frame
        /// update of a proxied character (vtable +0x4c8, FUN_142e130e0) only follows the server's position updates when
        /// it is non-zero (FUN_142df5160) — remote players are forced to 2. 0 leaves the NPC where it spawned.
        /// </summary>
        public sbyte ControlMode { get; set; }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.WriteUIntWith2BitLength((int)TransientId);
            writer.Write("");                    // name looked up through a string key
            writer.Write(Name);                  // literal name
            writer.Write(NameId);                // name text id (used when both strings are empty)
            writer.Write(0u);
            writer.Write((byte)0);
            writer.Write(ModelId);
            writer.Write(Scale);
            writer.Write("");                    // texture alias
            writer.Write("");                    // tint alias
            writer.Write("");
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(Position);
            writer.Write(Rotation);
            writer.Write(new Vector4(0f, 0f, 0f, 0f));
            writer.Write(0u);
            writer.Write(0u);
            // Character model info: customisations, skin, string, race, gender.
            writer.Write(0u);
            writer.Write(0u);
            writer.Write("");
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);                    // spawn flags passed to SpawnProxiedProp
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((byte)ControlMode);
            writer.Write(0u);
            writer.Write(false);
            writer.WriteBytes(new byte[3]);      // flag bytes
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(0ul);
            writer.Write(0ul);                   // attachment block: 0 = none
            writer.Write(ZoneOrigin.X);
            writer.Write(ZoneOrigin.Y);
            writer.Write(ZoneOrigin.Z);
            writer.Write(0u);                    // draw distance (0 = default)
            writer.Write(0u);                    // u32 list
            writer.Write(0u);
            writer.Write(0ul);
            writer.Write(0ul);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((byte)0);               // effect tags
            writer.Write(0u);
            writer.Write(0u);                    // list
        }
    }
}
