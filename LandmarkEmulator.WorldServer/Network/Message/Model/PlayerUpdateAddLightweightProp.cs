using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System;
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

        /// <summary>
        /// Collidable: flag byte 2 (packet +0x299) bit 0x08, applied by SetupLightweightNpc FUN_142d54040 through
        /// SetCollidable FUN_142ec2c20 (entity +0x1459 bit 4), which adds or removes the model's collision body
        /// (FUN_143d0c060) [P]. Sent 0 before 2026-10-17, so a prop whose model loaded after its setup lost its collision:
        /// players fell through the Spire's deck after travel (trace/0695/agent-physics-hooks.md,
        /// agent-prop-collision.md). The model's own .cdt still decides the shape; models without one stay walk-through.
        /// </summary>
        public bool Collidable { get; set; } = true;

        /// <summary>
        /// Control mode (signed byte at packet +0x19c → entity +0x41A0, FUN_142d54040, shared with NPC spawns). The
        /// per-frame update FUN_142eb1950 plays buffered PlayerUpdate position (0x7F) records only when FUN_142e93340
        /// passes, which needs +0x41A0 ≠ 0 [P]; creatures spawn with 2. 0 (default) = a static prop: its position updates
        /// are queued and never played. The moving part of a platform uses 2 (Props.Platforms;
        /// trace/0695/agent-e3-platform.md §2).
        /// </summary>
        public sbyte ControlMode { get; set; }
        /// <summary>
        /// +0x1a0 the entity's profile (ProfileDefinitions; entity +0x1474, FUN_142ec55e0): what TargetIsProfile tests. Trees
        /// carry 200-214 for the axes' target rule (Gathering.SetLife); 0 for every other prop.
        /// </summary>
        public uint Profile { get; set; }

        /// <summary>
        /// The prop's enabled flag: the last byte (packet +0x490) → prop +0x4E9C via FUN_1430abea0 in the spawn handler
        /// FUN_142cde1c0; the client defaults it to 1 (FUN_142c66de0 / FUN_1430a7760) [P]. Only the movers read it: the
        /// Translocation Pad's enter (FUN_1436db8b0) and the flinger (FUN_1436d9c90) return at once when it is 0 — sent as
        /// 0 until 2026-10-18, so no pad or flinger ever fired (round 91; trace/0695/agent-movers-trigger.md). Placed props
        /// send their on / off state (PlacedProp.Disabled).
        /// </summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Models.txt id (2 = Freeport stone-circle campfire).</summary>
        public uint ModelId { get; set; }
        public Vector4 Position { get; set; }
        public Vector4 Rotation { get; set; } = new(0f, 0f, 0f, 1f);
        public Vector4 Scale { get; set; } = new(1f, 1f, 1f, 1f);
        public ulong OwnerGuid { get; set; }
        /// <summary>Tint alias from the prop tint palette (<see cref="Game.Props.SendTintList"/>); empty for none.</summary>
        public string TintAlias { get; set; } = "";
        /// <summary>
        /// The trigger volume (AreaDefinition guid, family 0xEA) this prop stands for; 0 = none. Read at base packet
        /// +0x268 and copied to the prop entity +0x240 (0.695 FUN_142d54040); the Area Tool only opens a prop for box
        /// editing when it is set and the client knows that area (FUN_143739860). Set for Linking Areas made with the
        /// Area Tool (<see cref="Game.AreaDefinitions"/>).
        /// </summary>
        public ulong AreaGuid { get; set; }

        /// <summary>
        /// Base packet +0x260: a static area (GlobalAreas.txt id) the client builds for this prop from its own tables
        /// (AreaProperties / GlobalAreaShapes; 0.695 FUN_142d54040) — the movers' teleporter, flinger, launcher and
        /// platform areas (Props.Movers, trace/0695/movers-695.md §3). 0 = none.
        /// </summary>
        public uint StaticAreaId { get; set; }

        /// <summary>
        /// Base packet +0x270: the group key handed to the static area's properties (the parts of one mover pack share
        /// it; a flinger finds its target by it, FUN_142f85170). 0 = none.
        /// </summary>
        public ulong GroupGuid { get; set; }

        /// <summary>
        /// Base packet +0x280: AnimationSelection ids the client plays on the entity as it is created (0.695 reader
        /// FUN_142c39ea0, records {u32 selection, bool +4, bool +5} by FUN_142c22da0; applied by FUN_142d54040, which
        /// calls AnimationSelectionManager FUN_142dfc010 for each; 0.589 FUN_142cbaca0 has the same loop). The two bools
        /// are sent as 0 (+5 clears entity +0x1461 bit 4, +4 is copied to entity +0xa39; neither is needed here). Used
        /// for the Respawn Point's enabled / disabled look (selections 848 / 849, <see cref="Game.Props"/> RespawnLook).
        /// Null or empty = none (the u32 count 0 the packet always had).
        /// </summary>
        public uint[] SpawnAnimations { get; set; }

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
            writer.Write((byte)ControlMode);     // +0x19c control mode (see ControlMode)
            writer.Write(Profile);               // +0x1a0 profile → entity +0x1474
            writer.Write(false);
            writer.WriteBytes(new byte[] { 0, (byte)(Collidable ? 0x08 : 0), 0 });   // flag bytes (+0x298..+0x29a)
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(0ul);
            writer.Write(0ul);                   // attachment block: 0 = none
            writer.Write(ZoneOrigin.X);
            writer.Write(ZoneOrigin.Y);
            writer.Write(ZoneOrigin.Z);
            writer.Write(0u);                    // draw distance (0 = default)
            writer.Write(0u);                    // u32 list
            writer.Write(StaticAreaId);          // +0x260 static area (teleporter, flinger...); 0 = none
            writer.Write(AreaGuid);              // +0x268 area guid (see AreaGuid)
            writer.Write(GroupGuid);             // +0x270 group key of the static area's properties
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((byte)0);               // effect tags
            writer.Write(0u);
            // +0x280 spawn animations (see SpawnAnimations).
            uint[] animations = SpawnAnimations ?? Array.Empty<uint>();
            writer.Write((uint)animations.Length);
            foreach (uint selection in animations)
            {
                writer.Write(selection);
                writer.Write((byte)0);
                writer.Write((byte)0);
            }
            // Prop part.
            writer.Write(OwnerGuid);
            writer.Write((byte)1);               // collision type 1: flags only
            writer.Write(0u);
            writer.Write((byte)0);
            writer.Write(0u);                    // flags
            writer.Write(0u);                    // list
            writer.Write(0u);
            writer.Write(0ul);
            writer.Write(Enabled);               // +0x490 the prop's enabled flag (see Enabled)
        }
    }
}
