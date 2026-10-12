using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Network.Message.Model
{
    /// <summary>
    /// Spawns a prop entity (PlayerUpdate 0x62; reader FUN_142b8dc00 = lightweight base FUN_142b8cfc0 + prop part,
    /// handled by BaseClient::HandlePlayerUpdateAddLightweightProp FUN_142c44700 → SpawnProxiedProp).
    /// The client places it at (Position × 10 + Origin) − its zone origin, scaled back by 0.1, so with Origin set to
    /// the zone origin, Position is in the same local frame as the player's own position.
    /// </summary>
    [ZoneMessage(ZoneMessageOpcode.PlayerUpdateAddLightweightPropWire)]
    public class PlayerUpdateAddLightweightProp : IWritable
    {
        public static readonly (double X, double Y, double Z) ZoneOrigin = (25584000d, 20000d, 25564500d);

        public ulong Guid { get; set; }
        public uint TransientId { get; set; }
        public string Name { get; set; } = "";
        public uint NameId { get; set; }
        /// <summary>
        /// Whether the client draws the name over the entity. The live game showed no labels on props (interactable
        /// ones get a hover outline instead), so it is off by default; <see cref="NameId"/> is still kept for the
        /// placement window and saving.
        /// </summary>
        public bool ShowName { get; set; }
        /// <summary>Models.txt id (2 = Freeport stone-circle campfire).</summary>
        public uint ModelId { get; set; }
        public Vector4 Position { get; set; }
        public Vector4 Rotation { get; set; } = new(0f, 0f, 0f, 1f);
        public Vector4 Scale { get; set; } = new(1f, 1f, 1f, 1f);
        public ulong OwnerGuid { get; set; }
        /// <summary>Tint alias from the prop tint palette (<see cref="Game.Props.SendTintList"/>); empty for none.</summary>
        public string TintAlias { get; set; } = "";

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Guid);
            writer.WriteUIntWith2BitLength((int)TransientId);
            writer.Write("");                    // name looked up through a string key
            writer.Write(Name);                  // literal name
            writer.Write(ShowName ? NameId : 0u); // name text id (used when both strings are empty); 0 = no label
            writer.Write(0u);
            writer.Write((byte)0);
            writer.Write(ModelId);
            writer.Write(Scale);
            writer.Write("");                    // texture alias
            writer.Write(TintAlias);             // tint alias ("GlobalTint_Coral"; "" = the model's own colours)
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
            writer.Write((byte)0);
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
            // Prop part.
            writer.Write(OwnerGuid);
            writer.Write((byte)1);               // collision type 1: flags only
            writer.Write(0u);
            writer.Write((byte)0);
            writer.Write(0u);                    // flags
            writer.Write(0u);                    // list
            writer.Write(0u);
            writer.Write(0ul);
            writer.Write(false);
        }
    }
}
