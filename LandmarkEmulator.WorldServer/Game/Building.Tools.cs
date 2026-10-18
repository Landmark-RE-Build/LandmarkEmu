using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Concurrent;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The build-tool switch (ToolBase 0x117 sub 1 ToolPacketActivate, C→S) and what the server does with it. Traced from
    /// the 0.695 client (trace/0695/mesh-shapes-695.md; addresses are 0.695, the 0.589 code is the same, functions.tsv):
    ///
    /// - Wire (writer FUN_143452ac0 → base header FUN_143452480 then the packet body FUN_143452550, serialized in field
    ///   order): u16 0x117, u8 sub 1, u8 by item, u32 item definition id, u32 ToolTypes id. The "by item" form is
    ///   sent when an inventory tool is activated (ClientInventory.ActivateItem FUN_143457220: flag 1, item id, tool 0);
    ///   the tool form by every other activation (FUN_143468840 at part_0105 ~28696: flag 0, item 0, the id of the
    ///   ToolTypes.txt row whose NAME hash matches the requested tool; row parser FUN_143cbcf70 puts the hash at +0x50,
    ///   vtable slot +0x10 FUN_143cbce80 returns the ID at +8).
    /// - Sent on every switch to a tool (Tool.ActivateTool, Voxel.ActivateTool, prop gizmos, paste, tints, and the
    ///   0.695 Mesh Shapes tab: Mesh.SelectMesh / PlaceMesh FUN_143607fe0 / FUN_143608060 activate "StampBrush" when
    ///   no tool is active). Leaving build mode (tool 0) sends nothing. The Tool family has no reply.
    /// - The Mesh Shapes themselves never reach the server: the chosen VoxelShapes row stays in the client's tool
    ///   manager (+0x80, FUN_14346a510) and the voxelized result arrives as the usual ApplyDiffRequest (Building.cs).
    ///
    /// The server keeps the active tool per player so that the effect other players see for an edit (VoxelConstruction
    /// ResponseObserved, <see cref="Building.Observe"/>) is the one the client would pick for that tool: its handler
    /// FUN_143166bd0 maps operation 0-2 to brush 11 FillStamp, or 4 Stamp when the packet's bool is set (the bool also
    /// selects the ShapePlacement vs ShapePlacementNoDestroy effect, FUN_143086570), 3 → 2 Subtractive, 4 → 1 Heal,
    /// 5 → 0 Smoothing, 6 → 5 Paint (ModalToolBrushDefinitions BRUSH_TYPE values).
    /// </summary>
    public static partial class Building
    {
        /// <summary>ToolBase sub 1 ToolPacketActivate (C→S).</summary>
        public const byte ToolActivateSub = 1;

        /// <summary>
        /// ToolTypes.txt IDs (client table, same rows in 0.589 and 0.695): the id ToolPacketActivate carries for a tool
        /// activation. Names are the table's NAME column (what Lua activates by, e.g. "StampBrush" = the Add tool, which
        /// the Mesh Shapes tab uses too).
        /// </summary>
        public enum ToolType : uint
        {
            None = 0,
            ClaimPlacement = 1,
            Selection = 2,
            Smoothing = 3,          // Smooth Tool
            TriggerVolume = 4,      // Area Tool
            HealingBrush = 5,       // Heal Tool
            SubtractiveBrush = 6,   // Remove Tool
            Location = 7,
            Props = 8,              // Prop Tool
            PasteVoxel = 9,         // Paste Cursor
            StampBrush = 10,        // Add Tool (shapes and Mesh Shapes)
            PaintBrush = 11,
            PickerBrush = 12,
            FillBrush = 13,         // Line Tool
            PropBrush = 14,
            PathFindVisualize = 15,
            FillStampBrush = 17,    // Fill Brush (fills empty space only: the NoDestroy placement effect)
        }

        /// <summary>Each player's last activated build tool (and the item that activated it, for the by-item form).</summary>
        private static readonly ConcurrentDictionary<ulong, (ToolType Tool, uint Item)> activeTools = new();

        /// <summary>The build tool the player last switched to (<see cref="ToolType.None"/> before any switch).</summary>
        public static ToolType ActiveTool(WorldSession session)
            => activeTools.TryGetValue(session.PlayerGuid, out var t) ? t.Tool : ToolType.None;

        /// <summary>
        /// ToolBase 0x117 from the client. Sub 1 Activate: {u8 by item, u32 item id, u32 ToolTypes id} (layout in the
        /// class summary); remembered for the edit effects, nothing is sent back (the client switches by itself).
        /// </summary>
        [Network.Message.ZoneMessageHandler(Network.Message.ZoneMessageOpcode.ToolBase)]
        public static void HandleTool(WorldSession session, Network.Message.Model.ToolRequest request)
        {
            if (request.SubOpcode != ToolActivateSub)
            {
                Network.OpcodeStats.Unanswered("Tool 0x117", request.SubOpcode);
                log.Debug($"Tool packet 0x{request.SubOpcode:X2} from {session.CharacterName}: {BitConverter.ToString(request.Body)}");
                return;
            }
            byte[] body = request.Body;
            if (body.Length < 9)
            {
                log.Debug($"Short ToolPacketActivate from {session.CharacterName}: {BitConverter.ToString(body)}");
                return;
            }
            bool byItem = body[0] != 0;
            uint item = BitConverter.ToUInt32(body, 1);
            uint tool = BitConverter.ToUInt32(body, 5);
            // An item activation names the item, not a tool (tool 0): the edit effect then falls back to what the
            // edit did (ObservedOperation).
            activeTools[session.PlayerGuid] = ((ToolType)tool, byItem ? item : 0);
            Achievements.ToolActivated(session, tool);   // Building Block Basics tool steps (Achievements.Tutorial.cs)
            log.Debug($"{session.CharacterName} switched build tool to {(ToolType)tool} ({tool}){(byItem ? $" by item {item}" : "")}.");
        }

        /// <summary>
        /// The ResponseObserved operation the active tool stands for, and whether its placement effect is the Stamp one
        /// (true: brush 4 Stamp, ShapePlacement) or the Fill Brush one (false: brush 11 FillStamp, ShapePlacementNoDestroy).
        /// False when the tool says nothing about the effect (no tool, an item, a prop or selection tool).
        /// </summary>
        private static bool ObservedByTool(ToolType tool, out uint operation, out bool stamp)
        {
            stamp = tool != ToolType.FillStampBrush;
            switch (tool)
            {
                case ToolType.StampBrush:
                case ToolType.FillStampBrush:
                case ToolType.PasteVoxel:
                    operation = ObservedPlacement;
                    return true;
                case ToolType.SubtractiveBrush:
                    operation = ObservedDelete;
                    return true;
                case ToolType.HealingBrush:
                    operation = ObservedHeal;
                    return true;
                case ToolType.Smoothing:
                    operation = ObservedSmooth;
                    return true;
                case ToolType.PaintBrush:
                    operation = ObservedPaint;
                    return true;
                default:
                    operation = ObservedPlacement;
                    return false;
            }
        }
    }
}
