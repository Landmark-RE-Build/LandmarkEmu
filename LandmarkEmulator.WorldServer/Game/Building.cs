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

        /// <summary>The voxel store behind every edit (the Environment Server; terrain-service.md).</summary>
        private static IEnvironmentServer Env => EnvironmentServer.Instance;

        public const byte ApplyDiffSub = 1;
        /// <summary>
        /// Upper bound on cells in one edit (sanity only). The client's big brushes touch 140+ cells (2026-10-13); the
        /// old limit of 64 refused them without cell replies, which left every cell pending on the client (tool hung,
        /// its drawing of the edit discarded).
        /// </summary>
        private const int MaxCellsPerEdit = 1024;

        /// <summary>
        /// Log how the stored cell compares with the client's after each edit (dev "cellcompare on|off"). Checked
        /// 2026-10-14 with sphere edits in the real client: the only differences were voxels the server holds and the
        /// sent cell lacks (client 0 / server 2-3), i.e. the cell in an ApplyDiff carries the edit, not the whole layer,
        /// and the server keeps everything the client sent. Fill / Line / Smooth shapes are the client's own.
        /// </summary>
        public static bool CompareWithClient;

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

        /// <summary>
        /// The player's building materials for the Environment Server's check (terrain-service.md §5.2 / §5.5): per
        /// material item the count in the bag and the voxel credit (what <see cref="PayFor"/> works with), so the service
        /// can refuse an unaffordable edit before it writes anything; the zone then charges exactly the returned delta.
        /// </summary>
        internal static MaterialBudget Budget(WorldSession session)
        {
            var budget = new MaterialBudget();
            IReadOnlyCollection<uint> materialItems = BuildMaterials.Items;
            foreach (var item in session.Inventory.Items)
                if (materialItems.Contains(item.ItemDefinitionId))
                {
                    (uint held, int c) = budget.Items.GetValueOrDefault(item.ItemDefinitionId);
                    budget.Items[item.ItemDefinitionId] = ((uint)Math.Min(uint.MaxValue, held + (long)item.Count), c);
                }
            lock (sync)
                foreach (((ulong player, uint item), int c) in credit)
                    if (player == session.PlayerGuid && c != 0)
                        budget.Items[item] = (budget.Items.GetValueOrDefault(item).Held, c);
            return budget;
        }

        /// <summary>Response (sub 7) result codes the client knows (FUN_1430b6560).</summary>
        /// <summary>
        /// Response results with their own client message (FUN_1430b6560); anything else non-zero shows
        /// Construction.GeneralError. 0x1A: Construction.OnlyClaimOwnerMayPlaceOrCopyTemplateInstances.
        /// </summary>
        public const uint Failed = 1, InsufficientMaterials = 0xC, InsufficientProps = 0xD, InsufficientPermissions = 0xE,
            ExceedsTemplateVoxels = 0x11, OnlyClaimOwnerTemplateInstances = 0x1A;
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
            /// <summary>Props a paste placed (with where and how turned), taken back on undo and placed again on redo.</summary>
            public List<(Props.DesignProp Prop, System.Numerics.Vector3 Position, System.Numerics.Vector4 Rotation, ulong Guid)> PastedProps { get; } = new();
            /// <summary>A prop move instead of voxels: the prop and its place before and after.</summary>
            public (ulong Guid, Props.Transform Before, Props.Transform After)? PropMove { get; set; }
            /// <summary>The prop edit session (Props.Access) a prop move belongs to; later saves of it extend it.</summary>
            public object Session { get; init; }
        }

        /// <summary>
        /// A prop moved with the placement gizmo becomes one undo step per edit session (live sent PropBase 0x10
        /// UndoRedoNotification to put it back). The client repeats Save while the gizmo is open, so saves of the same
        /// session update the step's "after" instead of adding steps.
        /// </summary>
        public static void RememberPropMove(WorldSession session, object editSession, ulong guid, Props.Transform before, Props.Transform after)
        {
            lock (sync)
            {
                if (history.TryGetValue(session.PlayerGuid, out var h) && h.Undo.Count > 0 && h.Undo[^1] is { PropMove: { } move } top
                    && top.Session == editSession && move.Guid == guid)
                {
                    top.PropMove = (guid, move.Before, after);
                    return;
                }
            }
            Remember(session, new Edit(session.ZoneId, "Move Prop") { PropMove = (guid, before, after), Session = editSession });
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

        // ToolBase sub 1 ToolPacketActivate (the tool switch) is handled in Building.Tools.cs (HandleTool).

        /// <summary>
        /// ToolBase 0x117 sub 2 UndoStackChange (reader FUN_1433b4760, handler in FUN_1433ba050): {u32 undo count,
        /// u32 redo count, list&lt;string&gt; undo history names}. The counts enable the undo / redo buttons and Ctrl+Z / Ctrl+Y;
        /// the names fill the Tool.UndoStack list.
        /// </summary>
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
            if (edit.PropMove is { } propMove)
            {
                bool moved = Props.UndoMove(session, propMove.Guid, redo ? propMove.After : propMove.Before);
                SendResponse(session, UndoRedoSub, moved ? 0 : InsufficientPermissions);
                SendUndoStack(session);
                if (moved)
                    Achievements.UndoRedone(session, redo);   // Modular Thinking (Achievements.Tutorial.cs)
                log.Debug($"{session.CharacterName} {(redo ? "redid" : "undid")} a move of prop 0x{propMove.Guid:X}: {(moved ? "ok" : "refused")}.");
                return;
            }
            foreach (var cell in edit.Cells)
            {
                var mask = new byte[BlockData.Count / 8];
                foreach (TerrainEdits.VoxelValue v in cell.Before)
                    mask[v.Index >> 3] |= (byte)(1 << (7 - (v.Index & 7)));
                if (!ZoneTerrain.MayApply(cell.Cell, mask, session.PlayerGuid, zone))
                {
                    SendResponse(session, UndoRedoSub, InsufficientPermissions);
                    return;
                }
            }
            // The voxels back (or forward) in one Environment Server operation, the cost checked there against the bag
            // (job type 9 "Construction Undo Redo"; the history itself stays here: it also holds pasted props and prop moves).
            WriteResult written = edit.Cells.Count == 0 ? new WriteResult()
                : Env.WriteCells(zone, session.PlayerGuid, edit.Cells.Select(c => (c.Cell, redo ? c.After : c.Before)).ToList(), Budget(session), false);
            if (written.Unavailable)
            {
                // A remote Environment Server away (spec §6): the step stays where it was, so it can be tried again.
                lock (sync)
                    if (history.TryGetValue(session.PlayerGuid, out var h) && (redo ? h.Undo : h.Redo).Remove(edit))
                        (redo ? h.Redo : h.Undo).Add(edit);
                SendResponse(session, UndoRedoSub, Failed);
                ZoneTerrain.SendUnavailable(session);
                return;
            }
            if (written.Result == InsufficientMaterials)
            {
                // Nothing was written: the step stays where it was, as above, so it can be tried again once the player has
                // the materials. It used to stay moved to the other stack, so the next undo went past it to the step before
                // and the refused one could only be "redone", which changes nothing (bughunt-build B8).
                lock (sync)
                    if (history.TryGetValue(session.PlayerGuid, out var h) && (redo ? h.Undo : h.Redo).Remove(edit))
                        (redo ? h.Redo : h.Undo).Add(edit);
                SendResponse(session, UndoRedoSub, InsufficientMaterials);
                SendUndoStack(session);
                return;
            }
            if (!PayFor(session, written.Delta))
                log.Warn($"{session.CharacterName}'s {(redo ? "redo" : "undo")} was written but its materials could not be taken any more.");
            // A paste's props: undo picks them up (what paid for each comes back: its bag item, or its recipe materials,
            // Props.Refund.AsPlaced), redo places them again (bag item first, else materials: Props.PlaceDesignProp).
            for (int i = 0; i < edit.PastedProps.Count; i++)
            {
                var placed = edit.PastedProps[i];
                if (!redo)
                {
                    if (placed.Guid != 0)
                        Props.PickUp(session, placed.Guid, false);
                    edit.PastedProps[i] = (placed.Prop, placed.Position, placed.Rotation, 0);
                }
                else
                    edit.PastedProps[i] = (placed.Prop, placed.Position, placed.Rotation,
                        Props.PlaceDesignProp(session, placed.Prop, placed.Position, placed.Rotation, out _));
            }
            ZoneTerrain.Broadcast(edit.Cells.Select(c => c.Cell), zone);
            // ClientUndoRedoRequest (FUN_1430bc070 undo / FUN_1430ba020 redo) changes nothing on the client; the OK
            // Response refreshes its open Voxel Vision ownership view (FUN_1430bc0d0), as after a build.
            SendResponse(session, UndoRedoSub, 0);
            SendUndoStack(session);
            Achievements.UndoRedone(session, redo);   // Modular Thinking (Achievements.Tutorial.cs)
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
                if (!ZoneTerrain.MayApply(id, changed, session.PlayerGuid, zone))
                {
                    SendResponse(session, ApplyDiffSub, InsufficientPermissions);
                    SendCellResponses(session, heals.Select(h => h.Id));
                    foreach ((ulong refused, _) in heals)
                        ZoneTerrain.Resend(session, refused);
                    return;
                }
            // One Environment Server operation (LocalEnvironmentServer.ClearCells: the loop that was here): every overridden
            // voxel under the brush cleared; placed blocks that go come back as materials (negative cost), as Remove does.
            ClearResult cleared = Env.ClearCells(zone, session.PlayerGuid, heals);
            if (cleared.Unavailable)
            {
                SendResponse(session, ApplyDiffSub, Failed);
                SendCellResponses(session, heals.Select(h => h.Id));
                foreach ((ulong refused, _) in heals)
                    ZoneTerrain.Resend(session, refused);
                ZoneTerrain.SendUnavailable(session);
                return;
            }
            PayFor(session, cleared.Delta);
            var record = new Edit(zone, "Heal");
            record.Cells.AddRange(cleared.Cells);
            int repaired = cleared.Voxels;
            if (record.Cells.Count > 0)
                Remember(session, record);
            SendResponse(session, ApplyDiffSub, 0);
            SendCellResponses(session, heals.Select(h => h.Id));
            // Back to the server's truth for the healer (placed blocks reappear) and the change to everyone else.
            foreach ((ulong id, _) in heals)
                if (Env.Has(id, zone))
                    ZoneTerrain.Resend(session, id);
            if (record.Cells.Count > 0)
            {
                ZoneTerrain.Broadcast(record.Cells.Select(c => c.Cell), session, zone);
                Observe(session, zone, ObservedHeal, record, stamp: true);   // bool unused for Heal (operation 4)
            }
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
        /// A refused edit: the builder's client has already drawn it, and nothing else takes it back (the refusal only
        /// shows a message), so the blocks stayed on screen until the cell reloaded and then vanished (Halloween Black
        /// without enough Stone, 2026-10-13). Sending the server's copy of each cell removes them at once.
        /// </summary>
        private static void UndoLocalEdit(WorldSession session, IEnumerable<ulong> cells)
        {
            foreach (ulong cell in cells)
                ZoneTerrain.Resend(session, cell);
        }

        /// <summary>
        /// The ApplyDiffRequest fields after the cell list (writer FUN_1430b0510, decoded 2026-10-13 from live edits):
        /// u64 the cell the tool aimed at (+0x70); i32×3 voxel position (+0x78) and i32×3 size (+0x84) of the brush;
        /// materials used (+0x90: u32 n, n × (u16 material, u32 count)); u64 build site (+0xc0, FUN_1430b7610);
        /// per edited cell (+0xc8, FUN_1430b7070): u64 cell, u32 the layer-1 version the edit was based on, u32 the
        /// version the client gave the layer after the edit; u32 operation kind (+0xe0, see <see cref="ObservedOperation"/>),
        /// f32×3 (+0xe4); u32 n × (u32, vec3, vec3) (+0xf0); u8 (+0x108). Returns the per-cell versions, or null if the
        /// tail can't be read; <paramref name="kind"/> is the operation kind (0 if unread).
        /// </summary>
        private static Dictionary<ulong, (uint Base, uint New)> ReadEditVersions(byte[] body, int p, out uint kind)
        {
            kind = 0;
            try
            {
                p += 8 + 12 + 12;
                int materials = BitConverter.ToInt32(body, p);
                if (materials > 0 && materials < 256)
                {
                    // The client's own count per material (what its pre-check FUN_1435fd7c0 compares with the items
                    // held); logged to compare with what the server charges.
                    var counts = new List<string>();
                    for (int m = 0; m < materials; m++)
                        counts.Add($"{BitConverter.ToUInt16(body, p + 4 + m * 6)}={BitConverter.ToUInt32(body, p + 6 + m * 6)}");
                    log.Debug($"Client materials: {string.Join(", ", counts)}");
                }
                p += 4 + materials * 6;
                p += 8;
                int n = BitConverter.ToInt32(body, p);
                p += 4;
                if (n < 0 || n > MaxCellsPerEdit)
                    return null;
                var versions = new Dictionary<ulong, (uint, uint)>();
                for (int i = 0; i < n; i++, p += 16)
                    versions[BitConverter.ToUInt64(body, p)] = (BitConverter.ToUInt32(body, p + 8), BitConverter.ToUInt32(body, p + 12));
                if (p + 4 <= body.Length)
                    kind = BitConverter.ToUInt32(body, p);
                return versions;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }


        /// <summary>VoxelConstruction sub 0xA ResponseObserved (S→C): another player's edit, for its effect.</summary>
        public const byte ResponseObservedSub = 0xA;

        /// <summary>
        /// ResponseObserved operation types (handler FUN_1430bbd60 → brush type for the effect, FUN_142fe6610 /
        /// FUN_142fe6bf0; brush types from ModalToolBrushDefinitions): 0–2 placement (brush 11 FillStamp, or 4 Stamp when
        /// the bool is set; 1 always draws the box effect), 3 delete (2 SubtractiveBrush), 4 heal (1 HealingBrush),
        /// 5 smooth (0 Smoothing), 6 paint (5 PaintBrush).
        /// </summary>
        public const uint ObservedPlacement = 0, ObservedBoxPlacement = 1, ObservedDelete = 3, ObservedHeal = 4,
            ObservedSmooth = 5, ObservedPaint = 6;

        /// <summary>Players this close (metres) to an edit see its effect.</summary>
        private const float ObservedRange = 100f;

        /// <summary>
        /// The ResponseObserved operation for an edit. The request's operation kind (ApplyDiffRequest +0xe0, set by the
        /// client per operation: 1 box/fill placement without an aimed cell and 2 with one, FUN_1430bb0e0 (0.695
        /// FUN_143165f30, request +0x118); 3 any sculpting brush, FUN_1430ba210 (0.695 FUN_143164e90); 4–9 the other
        /// operations) does not say which sculpting brush was used. That comes from the tool the player last switched
        /// to (ToolPacketActivate, Building.Tools.cs: Add / Fill / Paste → placement, Remove → Delete, Heal, Smooth,
        /// Paint) and, when no tool is known (an item activation, or a client that never sent the switch), from what the
        /// edit did: only removed voxels is Delete, only new materials on solid voxels is Paint, only added voxels is a
        /// placement, anything else (moved surfaces) Smooth. <paramref name="stamp"/> is the packet's bool: the Stamp
        /// placement effect (Add tool, Mesh Shapes, paste) rather than the Fill Brush's no-destroy one.
        /// </summary>
        private static uint ObservedOperation(WorldSession session, uint kind, Edit record, out bool stamp)
        {
            bool known = ObservedByTool(ActiveTool(session), out uint byTool, out stamp);
            if (kind == 1 || kind == 2)
                return kind;
            if (known)
                return byTool;
            int added = 0, removed = 0, painted = 0, other = 0;
            foreach ((_, List<TerrainEdits.VoxelValue> before, List<TerrainEdits.VoxelValue> after) in record.Cells)
                for (int i = 0; i < before.Count && i < after.Count; i++)
                {
                    bool was = (before[i].Flags & 1) != 0, now = (after[i].Flags & 1) != 0;
                    if (!was && now)
                        added++;
                    else if (was && !now)
                        removed++;
                    else if (was && before[i].Material != after[i].Material)
                        painted++;
                    else
                        other++;
                }
            if (removed > 0 && added == 0 && painted == 0)
                return ObservedDelete;
            if (painted > 0 && added == 0 && removed == 0)
                return ObservedPaint;
            if (added > 0 && removed == 0)
                return ObservedPlacement;
            return ObservedSmooth;
        }

        /// <summary>
        /// Sends the players near an edit (not the builder, whose client played the effect itself) VoxelConstruction
        /// ResponseObserved (reader FUN_1430b11e0): u8 sub, u32 operation, the edit's bounds as two double×4 (global
        /// VoxelFarm units, dm; the client subtracts its scene origin and scales by 0.1 to metres), u32×3 placement
        /// direction (the client's own box effects use 0,1,0), bool placement replaces what was there
        /// (ShapePlacement vs ShapePlacementNoDestroy; brush 4 Stamp vs 11 FillStamp in the client's FUN_143166bd0).
        /// </summary>
        private static void Observe(WorldSession session, uint zone, uint operation, Edit record, bool stamp)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach ((ulong cell, _, List<TerrainEdits.VoxelValue> after) in record.Cells)
            {
                (double ox, double oy, double oz) = TerrainStore.CellOrigin(cell);
                double voxel = TerrainStore.CellSize((uint)(cell >> 60)) / BlockData.Size;
                foreach (TerrainEdits.VoxelValue v in after)
                {
                    // VoxelIndex: y + Size * (x + Size * z).
                    int y = v.Index % BlockData.Size, x = v.Index / BlockData.Size % BlockData.Size,
                        z = v.Index / (BlockData.Size * BlockData.Size);
                    double gx = ox + x * voxel, gy = oy + y * voxel, gz = oz + z * voxel;
                    minX = Math.Min(minX, gx); minY = Math.Min(minY, gy); minZ = Math.Min(minZ, gz);
                    maxX = Math.Max(maxX, gx + voxel); maxY = Math.Max(maxY, gy + voxel); maxZ = Math.Max(maxZ, gz + voxel);
                }
            }
            if (minX > maxX)
                return;
            var centre = new System.Numerics.Vector3((float)(minX + maxX) / 20f, (float)(minY + maxY) / 20f, (float)(minZ + maxZ) / 20f);
            SendObserved(PlayerManager.InZone(zone).Where(viewer => viewer != session
                && System.Numerics.Vector3.Distance(viewer.Position, centre) <= ObservedRange),
                operation, (minX, minY, minZ), (maxX, maxY, maxZ), stamp);
        }

        /// <summary>
        /// Sends ResponseObserved (layout on <see cref="Observe"/>) for an edit box in global dm. <paramref name="stamp"/>
        /// is the trailing bool: true for the Stamp placement effect (brush 4), false for the Fill Brush's (brush 11,
        /// no-destroy); it only matters for operations 0-2.
        /// </summary>
        public static void SendObserved(IEnumerable<WorldSession> viewers, uint operation, (double X, double Y, double Z) min,
            (double X, double Y, double Z) max, bool stamp = true)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(ResponseObservedSub);
            writer.Write(operation);
            foreach (double d in new[] { min.X, min.Y, min.Z, 1.0, max.X, max.Y, max.Z, 1.0 })
                writer.Write(d);
            writer.Write(0u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(stamp);
            byte[] packet = output.ToArray();
            foreach (WorldSession viewer in viewers)
                viewer.EnqueueMessage(Network.Message.ZoneMessageOpcode.VoxelConstructionBase, packet);
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
                ZoneTerrain.HandleOwnershipRequest(session, body);
                return;
            }
            if (sub == OffGridPasteSub)
            {
                // OffGridPasteClientRequest: a paste at a free position and turn (BuildingOffGridPaste.cs).
                HandleOffGridPaste(session, body);
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
                Network.OpcodeStats.Unanswered("VoxelConstruction 0xF4", sub);
                log.Debug($"Construction packet 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body, 0, Math.Min(body.Length, 64))}");
                return;
            }
            uint zone = session.ZoneId;   // every landscape keeps its own edits (TerrainEdits)
            var watch = System.Diagnostics.Stopwatch.StartNew();
            long vertexOnlyBefore = System.Threading.Interlocked.Read(ref BlockData.VertexOnlySeen);
            var edits = new List<(ulong Id, byte[] Changed, BlockData Result, byte[] Packed)>();
            var heals = new List<(ulong Id, byte[] Changed)>();
            var seen = new List<ulong>();
            Dictionary<ulong, (uint Base, uint New)> versions = null;
            uint kind = 0;
            try
            {
                int p = 1 + 24;
                int count = BitConverter.ToInt32(body, p);
                p += 4;
                if (count < 0 || count > MaxCellsPerEdit)
                    throw new InvalidDataException($"{count} cells");
                for (int n = 0; n < count; n++)
                {
                    // Entry (writer FUN_1430b1d30 / FUN_1430b0b50): u64 key, u64 cell, u32 type, u32 length + blob,
                    // and for types 2 and 3 one more u32.
                    ulong id = BitConverter.ToUInt64(body, p);
                    seen.Add(id);
                    uint type = BitConverter.ToUInt32(body, p + 16);
                    int length = BitConverter.ToInt32(body, p + 20);
                    p += 24;
                    int blob = p;
                    byte[] raw = TerrainStore.Unpack(body, p, length);
                    p += length;
                    if (type - 2u < 2u)
                        p += 4;
                    var changed = new byte[BlockData.Count / 8];
                    Array.Copy(raw, 0, changed, 0, changed.Length);
                    // The Heal tool (VoxelEraserCursor) sends only the mask: "these voxels back to the generated ground".
                    if (raw.Length == changed.Length)
                        heals.Add((id, changed));
                    else
                        // The packed blob as it came goes to a remote Environment Server unchanged (spec §8, no re-encoding).
                        edits.Add((id, changed, BlockData.Deserialize(raw, changed.Length),
                            EnvironmentServer.IsRemote ? body.AsSpan(blob, length).ToArray() : null));
                }
                versions = ReadEditVersions(body, p, out kind);
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable build edit from {session.CharacterName}: {e.Message}");
                SendResponse(session, ApplyDiffSub, Failed);
                // Every cell the client edited stays pending until it hears about it: answer the ones read so far and
                // give them back their real contents.
                SendCellResponses(session, seen);
                UndoLocalEdit(session, seen);
                return;
            }

            long tParse = watch.ElapsedMilliseconds;
            if (heals.Count > 0)
            {
                Heal(session, heals, zone);
                if (edits.Count == 0)
                    return;
            }

            // All or nothing: every cell must be allowed and the materials for the whole edit paid for.
            foreach ((ulong id, byte[] changed, _, _) in edits)
                if (!ZoneTerrain.MayApply(id, changed, session.PlayerGuid, zone))
                {
                    log.Info($"{session.CharacterName} tried to build in cell {id:X} without permission.");
                    SendResponse(session, ApplyDiffSub, InsufficientPermissions);
                    SendCellResponses(session, edits.Select(e => e.Id));
                    UndoLocalEdit(session, edits.Select(e => e.Id));
                    return;
                }
            long tCheck = watch.ElapsedMilliseconds;
            // The edit as one transaction in the Environment Server (terrain-service.md §5.5): cells edited from an older
            // copy left out, the material cost checked against the player's bag (the budget, PayFor's arithmetic), then
            // one write pass per cell (LocalEnvironmentServer.ApplyEdit: TerrainEdits.Prepare / Commit, as here before).
            var request = new EditRequest
            {
                Zone = zone, Owner = session.PlayerGuid, Budget = Budget(session), Compare = CompareWithClient && log.IsDebugEnabled
            };
            foreach ((ulong id, byte[] changed, BlockData result, byte[] packed) in edits)
            {
                (uint Base, uint New) v = default;
                bool has = versions != null && versions.TryGetValue(id, out v);
                request.Cells.Add(new EditCell
                {
                    Cell = id, Changed = changed, Result = result, Packed = packed, HasVersions = has, BaseVersion = v.Base, NewVersion = v.New
                });
            }
            EditResult done = Env.ApplyEdit(request);
            long tApply = watch.ElapsedMilliseconds;
            if (done.Unavailable)
            {
                // A remote Environment Server away (spec §6): refused at once, every cell answered, the server's copy back
                // where the zone has it, and the reason shown (LostClaimServerConnection 0xF9/0x12 → text 562155).
                log.Info($"{session.CharacterName}'s build edit refused: the Environment Server is unreachable.");
                SendResponse(session, ApplyDiffSub, Failed);
                SendCellResponses(session, edits.Select(e => e.Id));
                UndoLocalEdit(session, edits.Select(e => e.Id));
                ZoneTerrain.SendUnavailable(session);
                return;
            }
            foreach (ulong id in done.Stale)
                log.Debug($"{session.CharacterName}: edit of cell {id:X} based on version "
                    + $"{(versions != null && versions.TryGetValue(id, out var sv) ? sv.Base : 0):X}, server has "
                    + $"{Env.LayerVersion(id, zone, TerrainEdits.BuildLayer):X}; not applied.");
            if (done.Result == InsufficientMaterials)
            {
                SendResponse(session, ApplyDiffSub, InsufficientMaterials);
                SendCellResponses(session, edits.Select(e => e.Id));
                UndoLocalEdit(session, edits.Select(e => e.Id));
                return;
            }
            // The budget covered it; the bag can only have changed if something else took items in between (logged).
            if (!PayFor(session, done.Delta))
                log.Warn($"{session.CharacterName}'s edit was written but its materials could not be taken any more.");
            foreach ((ushort material, int n) in done.Delta)
                if (n > 0)
                    Achievements.BlocksPlaced(session, BuildMaterials.NameOf(material), (uint)n);
                else if (n < 0)
                    Achievements.BlocksRemoved(session, BuildMaterials.NameOf(material), (uint)-n);
            var record = new Edit(zone);
            record.Cells.AddRange(done.Cells);
            List<ulong> cells = done.Cells.Select(c => c.Cell).ToList();
            if (done.CompareReport != null)
                log.Debug($"{session.CharacterName}: {done.CompareReport}");
            // The client waits for this before it sends its next edit (an error code shows its own message).
            SendResponse(session, ApplyDiffSub, 0);
            SendCellResponses(session, edits.Select(e => e.Id));
            UndoLocalEdit(session, done.Stale);
            if (record.Cells.Count > 0)
            {
                Remember(session, record);
                Achievements.BuildEdit(session, kind);   // Building Block Basics paint step (Achievements.Tutorial.cs)
            }
            if (cells.Count == 0)
                return;
            ZoneTerrain.Broadcast(cells, session, zone);
            // The effect others see: from the tool the builder switched to (Add / Mesh Shapes = Stamp, Fill Brush =
            // FillStamp, Remove, Heal, Smooth, Paint), else from what the edit did.
            uint operation = ObservedOperation(session, kind, record, out bool stamp);
            Observe(session, zone, operation, record, stamp);
            // Nothing goes back to the builder: their client committed the edit to layer 1 and updated its own
            // manifest when the per-cell reply confirmed it (FUN_143f52b60), the same layer the server keeps it in.
            log.Debug($"{session.CharacterName} built: {done.Voxels} voxels in {cells.Count} cell(s) ({watch.ElapsedMilliseconds} ms: "
                + $"read {tParse}, checks {tCheck - tParse}, store {tApply - tCheck}{(EnvironmentServer.IsRemote ? " (Environment Server)" : "")}, "
                + $"rest {watch.ElapsedMilliseconds - tApply}); vertex-only voxels "
                + $"{System.Threading.Interlocked.Read(ref BlockData.VertexOnlySeen) - vertexOnlyBefore} ({(BlockData.KeepVertexOnly ? "kept" : "dropped")}).");
        }
    }
}
