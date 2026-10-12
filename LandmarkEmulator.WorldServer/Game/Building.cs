using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Voxel building with the build tools (Add, Remove, Smooth, Paint, ...). The client edits its own copy and sends
    /// the result: VoxelConstructionBase 0xF4 sub 1 ApplyDiffRequest. Body after the sub: u8, 24 bytes, u32 n, then n ×
    /// {u64 cell id, u64 cell id, u32 (4), u32 length, packed blob}, then material usage and cell versions (unused).
    /// The packed blob (A1B2C3D4 codec) holds an 8000-byte mask of the voxels the edit changed (MSB first), then the
    /// cell's voxels in SerializeBlockData format 2. The server keeps the changed voxels in its overlays (so they
    /// persist and reach everyone else) once the build-site permissions allow them.
    /// </summary>
    public static partial class Building
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte ApplyDiffSub = 1;
        private const int MaxCellsPerEdit = 64;

        /// <summary>Voxels built (+) or taken down (-) per player and item, not yet settled as whole items.</summary>
        private static readonly Dictionary<(ulong Player, uint Item), int> credit = new();
        private static readonly object sync = new();

        /// <summary>
        /// Charges the builder for the materials an edit adds and refunds what it takes away, in whole items
        /// (<see cref="BuildMaterials.VoxelsPerItem"/> voxels each; leftovers carry over to the next edit). False,
        /// with nothing charged, when the player lacks the items.
        /// </summary>
        private static bool PayFor(WorldSession session, Dictionary<ushort, int> delta)
        {
            var voxelsByItem = new Dictionary<uint, int>();
            foreach ((ushort material, int voxels) in delta)
            {
                uint item = BuildMaterials.ItemFor(material);
                if (item != 0 && voxels != 0)
                    voxelsByItem[item] = voxelsByItem.GetValueOrDefault(item) + voxels;
            }
            log.Debug($"Build materials: {string.Join(", ", delta.Select(d => $"{d.Key}={d.Value} (item {BuildMaterials.ItemFor(d.Key)})"))}");
            if (voxelsByItem.Count == 0)
                return true;
            lock (sync)
            {
                var plan = new List<(uint Item, int Items, int Credit)>();
                foreach ((uint item, int voxels) in voxelsByItem)
                {
                    // credit = voxels already paid for but not built (>= 0); building spends it first.
                    int owed = voxels - credit.GetValueOrDefault((session.PlayerGuid, item));
                    int items = owed > 0
                        ? (owed + BuildMaterials.VoxelsPerItem - 1) / BuildMaterials.VoxelsPerItem
                        : -(-owed / BuildMaterials.VoxelsPerItem);
                    int newCredit = items * BuildMaterials.VoxelsPerItem - owed;
                    if (items > 0 && session.Inventory.CountOf(item) < (uint)items)
                    {
                        string name = Inventory.ItemDefinitionManager.Get(item) is { } definition
                            ? LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(definition.NameId) ?? "material"
                            : "material";
                        log.Debug($"{session.CharacterName} needs {items} × {name} to build (has {session.Inventory.CountOf(item)}).");
                        return false;
                    }
                    plan.Add((item, items, newCredit));
                }
                foreach ((uint item, int items, int newCredit) in plan)
                {
                    if (items > 0)
                        session.TakeItem(item, (uint)items);
                    else if (items < 0)
                        session.GiveItem(item, (uint)-items);
                    credit[(session.PlayerGuid, item)] = newCredit;
                    if (items != 0)
                        log.Debug($"{session.CharacterName} {(items > 0 ? "paid" : "got back")} {Math.Abs(items)} × item {item} (now {session.Inventory.CountOf(item)}).");
                }
            }
            return true;
        }

        /// <summary>Response (sub 7) result codes the client knows (FUN_1430b6560).</summary>
        public const uint Failed = 1, InsufficientMaterials = 0xC, InsufficientProps = 0xD, InsufficientPermissions = 0xE;
        public const byte ResponseSub = 7;

        /// <summary>VoxelConstruction Response: u8 sub 7, u32 request type, u32 result (0 = done), bool.</summary>
        private static void SendResponse(WorldSession session, uint requestType, uint result)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(ResponseSub);
            writer.Write(requestType);
            writer.Write(result);
            writer.Write(false);
            session.EnqueueMessage(Network.Message.ZoneMessageOpcode.VoxelConstructionBase, output.ToArray());
        }

        /// <summary>VoxelConstruction sub 3 (C→S) {u32 0 undo / 1 redo}: Ctrl+Z / Ctrl+Y and the top-bar buttons.</summary>
        public const byte UndoRedoSub = 3, AddTemporaryBlockSub = 2;   // sub 2 is ClientSelectionAreaRequest

        /// <summary>Edits each player can undo, newest last.</summary>
        private const int UndoDepth = 20;

        /// <summary>One applied edit: per cell, the touched voxels' overlay values before and after (sparse).</summary>
        private sealed class Edit
        {
            public Edit(uint zone, string name = "Build")
            {
                Zone = zone;
                Name = name;
            }
            public uint Zone { get; }
            /// <summary>Shown in the client's undo history (Tool.UndoStack).</summary>
            public string Name { get; }
            public List<(ulong Cell, List<TerrainEdits.VoxelValue> Before, List<TerrainEdits.VoxelValue> After)> Cells { get; } = new();
        }

        private static readonly Dictionary<ulong, (List<Edit> Undo, List<Edit> Redo)> history = new();

        /// <summary>Adds a new edit to the player's undo history (and forgets what they could redo).</summary>
        private static void Remember(WorldSession session, Edit edit)
        {
            lock (sync)
            {
                if (!history.TryGetValue(session.PlayerGuid, out var h))
                    history[session.PlayerGuid] = h = (new List<Edit>(), new List<Edit>());
                h.Undo.Add(edit);
                if (h.Undo.Count > UndoDepth)
                    h.Undo.RemoveAt(0);
                h.Redo.Clear();
            }
            SendUndoStack(session);
        }

        /// <summary>
        /// ToolBase 0x117 sub 2 UndoStackChange (reader FUN_1433b4760, handler in FUN_1433ba050): {u32 undo count,
        /// u32 redo count, list&lt;string&gt; undo history names}. The counts enable the undo / redo buttons and Ctrl+Z / Ctrl+Y;
        /// the names fill the Tool.UndoStack list.
        /// </summary>
        /// <summary>
        /// ToolBase from the client: sub 1 Activate (FUN_1433aa0c0) when the player switches build tool. The client
        /// switches by itself and the Tool family has no reply, so it is only informational.
        /// </summary>
        [Network.Message.ZoneMessageHandler(Network.Message.ZoneMessageOpcode.ToolBase)]
        public static void HandleTool(Network.WorldSession session, Network.Message.Model.ToolRequest request)
        {
        }

        private static void SendUndoStack(WorldSession session)
        {
            List<string> names;
            int redo;
            lock (sync)
            {
                history.TryGetValue(session.PlayerGuid, out var h);
                names = h.Undo?.Select(e => e.Name).Reverse().ToList() ?? new List<string>();
                redo = h.Redo?.Count ?? 0;
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)2);
            writer.Write((uint)names.Count);
            writer.Write((uint)redo);
            writer.Write((uint)names.Count);
            foreach (string name in names)
                writer.Write(name);
            session.EnqueueMessage(Network.Message.ZoneMessageOpcode.ToolBase, output.ToArray());
        }

        /// <summary>
        /// Undo (or redo) the player's last edit: its voxels go back to their earlier (or later) values, materials are
        /// refunded or charged as for a build, and every player on the landscape, the builder included, gets the cells
        /// (the client doesn't change them itself). Refused when a cell is no longer the player's to edit, or the
        /// player is on another landscape.
        /// </summary>
        private static void UndoRedo(WorldSession session, bool redo)
        {
            Edit edit;
            lock (sync)
            {
                if (!history.TryGetValue(session.PlayerGuid, out var h) || (redo ? h.Redo : h.Undo).Count == 0)
                {
                    log.Debug($"{session.CharacterName}: nothing to {(redo ? "redo" : "undo")}.");
                    return;
                }
                List<Edit> from = redo ? h.Redo : h.Undo;
                edit = from[^1];
                if (edit.Zone != session.ZoneId)
                    return;
                from.RemoveAt(from.Count - 1);
                (redo ? h.Undo : h.Redo).Add(edit);
            }
            uint zone = edit.Zone;
            foreach (var cell in edit.Cells)
            {
                var mask = new byte[BlockData.Count / 8];
                foreach (TerrainEdits.VoxelValue v in cell.Before)
                    mask[v.Index >> 3] |= (byte)(1 << (7 - (v.Index & 7)));
                if (!TerrainEdits.MayApply(cell.Cell, mask, (x, y, z) => Claims.MayEdit(session.PlayerGuid, x, y, z, zone)))
                {
                    SendResponse(session, UndoRedoSub, InsufficientPermissions);
                    return;
                }
            }
            var delta = new Dictionary<ushort, int>();
            foreach (var cell in edit.Cells)
                foreach ((ushort material, int n) in TerrainEdits.MaterialDelta(cell.Cell, redo ? cell.After : cell.Before, zone))
                    delta[material] = delta.GetValueOrDefault(material) + n;
            if (!PayFor(session, delta))
            {
                SendResponse(session, UndoRedoSub, InsufficientMaterials);
                return;
            }
            foreach (var cell in edit.Cells)
                TerrainEdits.Write(cell.Cell, redo ? cell.After : cell.Before, zone, session.PlayerGuid);
            TerrainEdits.Broadcast(edit.Cells.Select(c => c.Cell), zone);
            SendUndoStack(session);
            log.Debug($"{session.CharacterName} {(redo ? "redid" : "undid")} an edit of {edit.Cells.Count} cell(s).");
        }

        public const byte ClipboardDataSub = 6;

        /// <summary>
        /// Heal tool: returns the terrain under the brush to its natural (generated) state. Every overlay voxel it covers
        /// is cleared: dug ground comes back and placed blocks go, their materials refunded as with Remove. Undoable,
        /// and only where the player may build. Everyone in range gets the cells, the healer included: their client
        /// already cleared every voxel under the brush. (The client sends a heal only when the brush touches voxels it
        /// holds as its own edits, i.e. placed blocks; dug holes the server sent don't trigger one.)
        /// </summary>
        private static void Heal(WorldSession session, List<(ulong Id, byte[] Changed)> heals, uint zone)
        {
            foreach ((ulong id, byte[] changed) in heals)
                if (!TerrainEdits.MayApply(id, changed, (x, y, z) => Claims.MayEdit(session.PlayerGuid, x, y, z, zone)))
                {
                    SendResponse(session, ApplyDiffSub, InsufficientPermissions);
                    SendCellResponses(session, heals.Select(h => h.Id));
                    foreach ((ulong refused, _) in heals)
                        TerrainEdits.Resend(session, refused);
                    return;
                }
            // Placed blocks that go come back as materials (negative cost), as Remove does.
            var refund = new Dictionary<ushort, int>();
            foreach ((ulong id, byte[] changed) in heals)
                foreach ((ushort material, int n) in TerrainEdits.MaterialDelta(id,
                    TerrainEdits.Read(id, changed, zone).Where(v => v.Flags != 0).Select(v => new TerrainEdits.VoxelValue(v.Index, 0, 0, 0, 0, 0)).ToList(), zone))
                    refund[material] = refund.GetValueOrDefault(material) + n;
            PayFor(session, refund);
            var record = new Edit(zone, "Heal");
            int repaired = 0;
            foreach ((ulong id, byte[] changed) in heals)
            {
                List<TerrainEdits.VoxelValue> before = TerrainEdits.Read(id, changed, zone);
                var cleared = before.Where(v => v.Flags != 0)
                    .Select(v => new TerrainEdits.VoxelValue(v.Index, 0, 0, 0, 0, 0)).ToList();
                if (cleared.Count == 0)
                    continue;
                var mask = new byte[BlockData.Count / 8];
                foreach (TerrainEdits.VoxelValue v in cleared)
                    mask[v.Index >> 3] |= (byte)(1 << (7 - (v.Index & 7)));
                List<TerrainEdits.VoxelValue> old = TerrainEdits.Read(id, mask, zone);
                TerrainEdits.Write(id, cleared, zone, session.PlayerGuid);
                record.Cells.Add((id, old, cleared));
                repaired += cleared.Count;
            }
            if (record.Cells.Count > 0)
                Remember(session, record);
            SendResponse(session, ApplyDiffSub, 0);
            SendCellResponses(session, heals.Select(h => h.Id));
            // Back to the server's truth for the healer (placed blocks reappear) and the change to everyone else.
            foreach ((ulong id, _) in heals)
                if (TerrainEdits.Has(id, zone))
                    TerrainEdits.Resend(session, id);
            if (record.Cells.Count > 0)
                TerrainEdits.Broadcast(record.Cells.Select(c => c.Cell), session, zone);
            log.Debug($"{session.CharacterName} healed {repaired} voxels in {heals.Count} cell(s).");
        }

        /// <summary>
        /// Skips one PropData (client reader FUN_142f09280 / writer FUN_1430afea0): u64, 12 × u32, u32 n × 16 bytes,
        /// u64, u64, u32, u32 n × 20 bytes, u32, string, string, 3 × u32.
        /// </summary>
        private static int SkipPropData(byte[] b, int p)
        {
            p += 8 + 12 * 4;
            p += 4 + (int)BitConverter.ToUInt32(b, p) * 16;
            p += 8 + 8 + 4;
            p += 4 + (int)BitConverter.ToUInt32(b, p) * 20;
            p += 4;
            p += 4 + (int)BitConverter.ToUInt32(b, p);
            p += 4 + (int)BitConverter.ToUInt32(b, p);
            return p + 12;
        }

        /// <summary>
        /// TerrainCell 0xEC sub 3 ClientCellUpdateResponse (reader FUN_14309bcc0: u32 sub, u64 cell, bool ok, string error)
        /// for every cell of an edit. The client keeps each edited cell pending until it hears this (VoxelFarm core
        /// +0x5a60); without it the request times out ~50 s later (TerrainCellPendingRequestTimeout). When ok is false
        /// the string is shown as an alert, so refusals answer ok too and leave the message to the Response (sub 7).
        /// </summary>
        private static void SendCellResponses(WorldSession session, IEnumerable<ulong> cells)
        {
            foreach (ulong cell in cells)
            {
                var output = new List<byte>();
                var writer = new GamePacketWriter(output);
                writer.WriteBytes(new byte[2]);   // high half of the 32-bit sub-opcode
                writer.Write(cell);
                writer.Write(true);
                writer.Write("");
                session.EnqueueMessage(Network.Message.ZoneMessageOpcode.TerrainCellClientCellUpdateResponse, output.ToArray());
            }
        }

        /// <summary>
        /// VoxelConstructionBase 0xF4. Sub 1 ApplyDiffRequest: for each touched cell the client sends an 8000-byte changed
        /// mask and its edited cell (block format 2). Each changed voxel is checked against claim permissions, the material
        /// difference is charged or refunded, the overlay is updated and saved, the result is broadcast to nearby players
        /// (not back to the builder) and the client gets a Response (sub 7). Refused cells are re-sent so the builder's view
        /// matches the server.
        /// </summary>
        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            if (sub == UndoRedoSub && body.Length >= 4)
            {
                UndoRedo(session, BitConverter.ToUInt32(body, 0) == 1);
                return;
            }
            if (sub == VoxelOwnership.RequestSub)
            {
                // ClientOwnershipRequest: Voxel Vision's builders of a site (VoxelOwnership.cs).
                VoxelOwnership.HandleRequest(session, body);
                return;
            }
            if (sub == AddTemporaryBlockSub)
            {
                // ClientSelectionAreaRequest: copy or paste (BuildingClipboard.cs).
                HandleSelection(session, body);
                return;
            }
            if (sub != ApplyDiffSub)
            {
                log.Debug($"Construction packet 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body, 0, Math.Min(body.Length, 64))}");
                return;
            }
            uint zone = session.ZoneId;   // every landscape keeps its own edits (TerrainEdits)
            var edits = new List<(ulong Id, byte[] Changed, BlockData Result)>();
            var heals = new List<(ulong Id, byte[] Changed)>();
            try
            {
                int p = 1 + 24;
                int count = BitConverter.ToInt32(body, p);
                p += 4;
                if (count < 0 || count > MaxCellsPerEdit)
                    throw new InvalidDataException($"{count} cells");
                for (int n = 0; n < count; n++)
                {
                    ulong id = BitConverter.ToUInt64(body, p);
                    int length = BitConverter.ToInt32(body, p + 20);
                    p += 24;
                    byte[] raw = TerrainStore.Unpack(body, p, length);
                    p += length;
                    var changed = new byte[BlockData.Count / 8];
                    Array.Copy(raw, 0, changed, 0, changed.Length);
                    // The Heal tool (VoxelEraserCursor) sends only the mask: "these voxels back to the generated ground".
                    if (raw.Length == changed.Length)
                        heals.Add((id, changed));
                    else
                        edits.Add((id, changed, BlockData.Deserialize(raw, changed.Length)));
                }
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable build edit from {session.CharacterName}: {e.Message}");
                SendResponse(session, ApplyDiffSub, Failed);
                return;
            }

            if (heals.Count > 0)
            {
                Heal(session, heals, zone);
                if (edits.Count == 0)
                    return;
            }

            // All or nothing: every cell must be allowed and the materials for the whole edit paid for.
            foreach ((ulong id, byte[] changed, _) in edits)
                if (!TerrainEdits.MayApply(id, changed, (x, y, z) => Claims.MayEdit(session.PlayerGuid, x, y, z, zone)))
                {
                    log.Info($"{session.CharacterName} tried to build in cell {id:X} without permission.");
                    SendResponse(session, ApplyDiffSub, InsufficientPermissions);
                    SendCellResponses(session, edits.Select(e => e.Id));
                    return;
                }
            var delta = new Dictionary<ushort, int>();
            foreach ((ulong id, byte[] changed, BlockData result) in edits)
                foreach ((ushort material, int n) in TerrainEdits.MaterialDelta(id, changed, result, zone))
                    delta[material] = delta.GetValueOrDefault(material) + n;
            if (!PayFor(session, delta))
            {
                SendResponse(session, ApplyDiffSub, InsufficientMaterials);
                SendCellResponses(session, edits.Select(e => e.Id));
                return;
            }
            foreach ((ushort material, int n) in delta)
                if (n > 0)
                    Achievements.BlocksPlaced(session, BuildMaterials.NameOf(material), (uint)n);
                else if (n < 0)
                    Achievements.BlocksRemoved(session, BuildMaterials.NameOf(material), (uint)-n);
            var cells = new List<ulong>();
            var record = new Edit(zone);
            int voxels = 0;
            foreach ((ulong id, byte[] changed, BlockData result) in edits)
            {
                List<TerrainEdits.VoxelValue> before = TerrainEdits.Read(id, changed, zone);
                if (TerrainEdits.ApplyDiff(id, changed, result, (x, y, z) => true, out int changedVoxels, zone, session.PlayerGuid))
                {
                    voxels += changedVoxels;
                    cells.Add(id);
                    record.Cells.Add((id, before, TerrainEdits.Read(id, changed, zone)));
                }
            }
            if (record.Cells.Count > 0)
                Remember(session, record);
            // The client waits for this before it sends its next edit; an error code shows its own message.
            SendResponse(session, ApplyDiffSub, 0);
            SendCellResponses(session, edits.Select(e => e.Id));
            if (cells.Count == 0)
                return;
            TerrainEdits.Broadcast(cells, session, zone);
            log.Debug($"{session.CharacterName} built: {voxels} voxels in {cells.Count} cell(s).");
        }
    }
}
