using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The Selection tool's Delete key and Cut (Ctrl+X): ClientSelectionAreaRequest 0xF4/2 with action 2 and 1
    /// (trace/0695/voxdel-a..d-*.md, 2026-10-31).
    /// <para>
    /// The client chain [P]: InputProfile SelectionTool "Delete" → Lua VoxelClipboard.Delete (FUN_1435dd210) → the selection
    /// cursor (FUN_143793a10) → FUN_143161140 → FUN_143161530(box, 2); Cut is the same with 1, Copy with 0. For 1 and 2 the
    /// client erases the box in its own layer 1 at once (FUN_1431608c0), marks every cell it touches pending, and sends the
    /// box with each cell's version before and after the erase. A Delete carries no voxels (empty block list); a Cut also
    /// carries the copied blocks. The server has to do the erase itself.
    /// </para>
    /// <para>
    /// Until 2026-10-31 the server took both for a Copy: nothing was erased, refunded, broadcast or undoable, and the
    /// cells were answered ok, so the client kept an erase the server never made. Whatever later sent the server's copy of
    /// such a cell brought the voxels back — a paste (which sends the changed cells to the paster too), the next brush edit
    /// in the cell (its base version, the client's post-erase one, no longer matched: the edit was refused as stale and the
    /// server's cell re-sent, dropping the new voxels), undo, a refetch after a re-zone — and other players never saw the
    /// delete (player report 2026-10-31). The Delete also replaced the paste clipboard with an empty one.
    /// </para>
    /// <para>
    /// Live (the client's own help page, HelpPages/Tools_Large.html "Selection tool"): "DELETE will remove both props and
    /// voxels", "Resources for all deleted items are returned to you", "Both cut and copy affect props as well as blocks",
    /// "Cut props are refunded"; P2954 (26 Mar 2015) the Delete key turns the props in the selection back into resources.
    /// </para>
    /// <para>
    /// What the server does: the box's voxels become air in layer 1 under the build rules (build permission over the
    /// whole box, ZoneTerrain.MayApply; placed voxels refunded; one undo step), the props standing in it that the player
    /// may remove come back as materials, then the cells are answered and the player gets the server's copy of every cell the box touches (others: the changed ones) —
    /// cells. The erase the client made is VoxelFarm's own (op 1 over [origin, origin + (size − 1) voxels + 0.5 dm]); the
    /// server erases the same grid points [I: the op's exact edge and vertex rules are not traced], and sending the player
    /// the server's result keeps both copies the same whatever the client drew. Any refusal re-sends the cells the same
    /// way, so the client's erase is taken back instead of staying on that one screen.
    /// </para>
    /// </summary>
    public static partial class Building
    {
        public const uint CutAction = 1, DeleteAction = 2;

        /// <summary>
        /// Most voxels one selection delete may cover (its undo step holds each erased voxel's value before and after: up to
        /// ~24 MB; the player's whole history is bounded too, <see cref="MaxUndoValues"/>). The client allows 125,000,000
        /// (FUN_143166fb0); a larger box is refused and taken back on the client (OPEN-LEADS L249).
        /// </summary>
        private const long MaxDeleteVoxels = 1_000_000;

        /// <summary>Answers a Delete (2) or Cut (1). Cut copies first (with the props), then deletes like Delete.</summary>
        private static void HandleDeleteOrCut(WorldSession session, byte[] body, SelectionRequest request)
        {
            uint zone = session.ZoneId;
            string name = request.Action == CutAction ? "Cut" : "Delete";
            // A Cut reads the box like a copy: Get Inspired (the client clips the selection to such areas itself,
            // FUN_1436ea360 → FUN_142dbcae0 0x100) and no Player Studio design of someone else's under it.
            if (request.Action == CutAction && !MayCopy(session, request))
            {
                Refuse(session, request, InsufficientPermissions, $"{session.CharacterName} tried to cut from a build site without Get Inspired.");
                return;
            }
            if (request.Action == CutAction && CoversTemplateInstance(session, request))
            {
                Refuse(session, request, OnlyClaimOwnerTemplateInstances, $"{session.CharacterName} tried to cut a placed Player Studio design.");
                return;
            }
            Dictionary<ulong, byte[]> masks = DeleteMasks(request, out long voxels);
            if (masks == null || voxels > MaxDeleteVoxels || masks.Count > MaxCellsPerEdit)
            {
                Refuse(session, request, Failed, $"{session.CharacterName}: selection {name.ToLowerInvariant()} of " +
                    $"{request.SizeX}×{request.SizeY}×{request.SizeZ} voxels is too large ({voxels} voxels, {masks?.Count ?? 0} cells).");
                return;
            }
            foreach ((ulong cell, byte[] mask) in masks)
                if (!ZoneTerrain.MayApply(cell, mask, session.PlayerGuid, zone))
                {
                    Refuse(session, request, InsufficientPermissions, $"{session.CharacterName} tried to {name.ToLowerInvariant()} in cell {cell:X} without build permission.");
                    return;
                }

            Dictionary<ulong, List<TerrainEdits.VoxelValue>> byCell = ErasedVoxels(masks, zone);
            WriteResult written = null;
            if (byCell.Count > 0)
            {
                written = Env.WriteCells(zone, session.PlayerGuid, byCell.Select(c => (c.Key, c.Value)).ToList(), Budget(session), true);
                if (written.Unavailable)
                {
                    Refuse(session, request, Failed, null);
                    ZoneTerrain.SendUnavailable(session);
                    return;
                }
                if (!PayFor(session, written.Delta))   // a delete only gives back; nothing to charge
                    log.Warn($"{session.CharacterName}'s selection {name.ToLowerInvariant()} could not settle its materials.");
            }
            SendResponse(session, AddTemporaryBlockSub, 0);
            if (request.Action == CutAction)
                Copy(session, body, request);   // the clipboard and its ClipboardData, props included, before they go
            // Delete: the props standing in the erased box. Cut: those its copy took (the copy's box, one voxel larger).
            (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = request.Action == CutAction ? SelectionBox(request) : DeleteBox(request);
            int props = Props.DisassembleInBox(session, min, max, zone);

            if (written != null)
            {
                var record = new Edit(zone, name);
                record.Cells.AddRange(written.Cells);
                Remember(session, record);
                Observe(session, zone, ObservedDelete, record, false);
            }
            // The cells first (they are pending on the client until answered, and it drops updates for pending cells). Then
            // the player gets the server's copy of EVERY listed cell: the client wrote air over the whole box, also in cells the
            // server leaves as they are (open air), and would otherwise keep its own layer version there, so its next edit in
            // that cell came back stale. Everyone else gets only the cells that changed.
            SendCellResponses(session, request.Cells);
            foreach (ulong cell in request.Cells.Concat(byCell.Keys).Distinct())
                ZoneTerrain.Resend(session, cell);
            ZoneTerrain.Broadcast(byCell.Keys, session, zone);
            log.Info($"{session.CharacterName}: selection {name.ToLowerInvariant()} {request.SizeX}×{request.SizeY}×{request.SizeZ} at " +
                $"{request.X},{request.Y},{request.Z}: {byCell.Values.Sum(v => v.Count)} voxel(s) erased in {byCell.Count} cell(s), {props} prop(s) taken down.");
        }

        /// <summary>
        /// A refused selection request whose box the client may already have erased (Delete, Cut, an unknown action):
        /// the result code (the client shows its message, and clears its clipboard for any code but 0 — FUN_143162830 case
        /// 7), the cell answers, then the server's copy of each cell, which takes the client's own erase back.
        /// </summary>
        private static void Refuse(WorldSession session, SelectionRequest request, uint result, string why)
        {
            if (why != null)
                log.Info(why);
            SendResponse(session, AddTemporaryBlockSub, result);
            SendCellResponses(session, request.Cells);
            // Above the cell limit only the cells near the player (the ones it holds in full) go back; the box is then far
            // larger than any delete the server makes (MaxDeleteVoxels).
            bool all = request.Cells.Count <= MaxCellsPerEdit;
            foreach (ulong cell in request.Cells)
            {
                (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = TerrainStore.LocalBounds(cell);
                if (all || System.Numerics.Vector3.Distance(session.Position, (min + max) / 2) <= RefusedResendRange)
                    ZoneTerrain.Resend(session, cell);
            }
        }

        /// <summary>Metres around the player within which a refused oversized selection's cells are sent back.</summary>
        private const float RefusedResendRange = 256f;

        /// <summary>The erased box in local metres (props standing in it are taken down): from the origin voxel, size voxels.</summary>
        private static (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) DeleteBox(SelectionRequest request)
        {
            (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = SelectionBox(request);
            float v = (float)TerrainEdits.VoxelSize / 10;
            return (min, max - new System.Numerics.Vector3(v, v, v));
        }

        /// <summary>
        /// The grid points a selection Delete or Cut erases, per cell (8000-byte masks): from the request's origin voxel,
        /// size voxels per axis (the client's erase box [origin, origin + (size − 1) voxels + 0.5 dm], FUN_1431608c0 +
        /// FUN_143c18b20). Null for a box of another level; an axis of size 0 erases nothing.
        /// </summary>
        private static Dictionary<ulong, byte[]> DeleteMasks(SelectionRequest request, out long voxels)
        {
            voxels = (long)request.SizeX * request.SizeY * request.SizeZ;
            (uint level, uint cellX, uint cellY, uint cellZ) = TerrainStore.Decode(request.Cell);
            if (level != TerrainEdits.Level)
                return null;
            var masks = new Dictionary<ulong, byte[]>();
            if (voxels == 0 || voxels > MaxDeleteVoxels)
                return masks;
            long x0 = (long)cellX * BlockData.Size + request.X, y0 = (long)cellY * BlockData.Size + request.Y,
                z0 = (long)cellZ * BlockData.Size + request.Z;
            for (long z = z0; z < z0 + request.SizeZ; z++)
                for (long x = x0; x < x0 + request.SizeX; x++)
                    for (long y = y0; y < y0 + request.SizeY; y++)
                    {
                        ulong cell = TerrainStore.CellId(level, (uint)(x / BlockData.Size), (uint)(y / BlockData.Size), (uint)(z / BlockData.Size));
                        if (!masks.TryGetValue(cell, out byte[] mask))
                        {
                            if (masks.Count >= MaxCellsPerEdit)
                                return masks;
                            masks[cell] = mask = new byte[BlockData.Count / 8];
                        }
                        int index = BlockData.Index((int)(x % BlockData.Size), (int)(y % BlockData.Size), (int)(z % BlockData.Size));
                        mask[index >> 3] |= (byte)(0x80 >> (index & 7));
                    }
            return masks;
        }

        /// <summary>
        /// What the erase writes into layer 1: air (material bit, material 0, the stored form a Remove brush's carved voxel
        /// has) over every voxel of the box that isn't air there already — placed voxels and the generated ground alike.
        /// Left out, to keep the layer small: voxels no layer of the server stores whose corner lies more than 2 m above the
        /// baked ground of their column, certainly air already (the digs' rule: the height grid is 2 m and bilinear, and
        /// with a one-voxel margin slopes and ridges kept ground the client had carved, MODLOG 2026-10-11).
        /// </summary>
        private const float OpenAirMargin = 2f;

        private static Dictionary<ulong, List<TerrainEdits.VoxelValue>> ErasedVoxels(Dictionary<ulong, byte[]> masks, uint zone)
        {
            var byCell = new Dictionary<ulong, List<TerrainEdits.VoxelValue>>();
            double voxel = TerrainEdits.VoxelSize;
            foreach ((ulong cell, byte[] mask) in masks)
            {
                List<TerrainEdits.VoxelValue> build = Env.Read(cell, mask, zone, TerrainEdits.BuildLayer);
                // The other layers only where they are stored (the version index is local: no round trip to a remote service).
                var otherLayers = new HashSet<int>();
                foreach (uint layer in new[] { TerrainEdits.PoiLayer, TerrainEdits.DigLayer })
                    if (Env.LayerVersion(cell, zone, layer) != 0)
                        otherLayers.UnionWith(Env.Read(cell, mask, zone, layer).Where(v => v.Flags != 0).Select(v => v.Index));
                (double ox, double oy, double oz) = TerrainStore.CellOrigin(cell);
                var groundOfColumn = new Dictionary<int, float?>();
                var air = new List<TerrainEdits.VoxelValue>();
                foreach (TerrainEdits.VoxelValue v in build)
                {
                    if ((v.Flags & 1) != 0 && v.Material == 0)
                        continue;   // already air in layer 1
                    if (v.Flags == 0 && !otherLayers.Contains(v.Index))
                    {
                        int y = v.Index % BlockData.Size, x = v.Index / BlockData.Size % BlockData.Size, z = v.Index / (BlockData.Size * BlockData.Size);
                        int column = x + BlockData.Size * z;
                        if (!groundOfColumn.TryGetValue(column, out float? ground))
                        {
                            // Global decimetres → local metres (the zone origin, / 10), as TerrainEdits.ToGlobal inverts.
                            float lx = (float)((ox + (x + 0.5) * voxel - TerrainStore.ZoneOrigin.X) / 10);
                            float lz = (float)((oz + (z + 0.5) * voxel - TerrainStore.ZoneOrigin.Z) / 10);
                            groundOfColumn[column] = ground = TerrainHeights.HeightAt(lx, lz, zone);
                        }
                        float voxelY = (float)((oy + y * voxel - TerrainStore.ZoneOrigin.Y) / 10);
                        if (ground is float g && voxelY > g + OpenAirMargin)
                            continue;   // open air above the generated ground
                    }
                    air.Add(new TerrainEdits.VoxelValue(v.Index, 1, 0, BlockData.CentreOffset, BlockData.CentreOffset, BlockData.CentreOffset));
                }
                if (air.Count > 0)
                    byCell[cell] = air;
            }
            return byCell;
        }
    }
}
