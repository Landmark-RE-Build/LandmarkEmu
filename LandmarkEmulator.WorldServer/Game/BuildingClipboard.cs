using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Copy and paste with the Select tool (Ctrl+C / Ctrl+V), both through VoxelConstruction sub 2
    /// ClientSelectionAreaRequest (writer FUN_1430b96a0 for paste; constructor FUN_1430b3990):
    /// <code>
    /// u8 layer (FF), u32 action (0 copy, 4 paste), u64 level-2 cell id, u32 x, y, z origin (voxels in that cell),
    /// u32 sx, sy, sz size (voxels), PropData list, 4 × 4 i32 rotation matrix (paste; identity = no turn),
    /// material map {u32 n × (u16 material, u32 voxels)}, u64, string, u64,
    /// cell-version list {u32 n × (u64 cell, u64 version)} (every cell the box touches),
    /// block list {u32 n × (u64 key, u64, u32 type, u32 len + packed block, u32 if type is 2 or 3)}, u32, u8, string, u8
    /// </code>
    /// A copy carries one block per cell of the box (key = a level-2 cell id holding the cell's place from the box's
    /// corner; a box within 40 voxels a side is one block): the selection's voxels with the box's origin at (0, 0, 0) (the same
    /// packed format as a build edit: 8000-byte mask + SerializeBlockData format 2; index y fastest, then x, then z;
    /// the box's (size + 1) vertices per axis). The server keeps that grid as the player's clipboard and answers
    /// ClipboardData (sub 6, reader FUN_1430b0d80): {PropData list, material map, block list, 3 × u32 size, u32,
    /// bool has voxels}. The client only lets the paste tool start when the material map is not empty (cursor
    /// FUN_1436d6ac0), and checks the player holds that many of each material before it places (FUN_1436d6b20).
    /// A paste sends no voxels at all — only where the box goes and how it is turned — so the server places its own
    /// copy: solid voxels overwrite the target (empty ones leave it as it is), under the same permission and material
    /// rules as any build, as one undoable edit, and every player (the paster included) gets the changed cells.
    /// </summary>
    public static partial class Building
    {
        public const uint CopyAction = 0, SaveDesignAction = 3, PasteAction = 4;

        /// <summary>
        /// Per-axis nudge on the paste request origin (L199). Frida on FUN_1436cd920: identity packets sit one voxel
        /// above the area/preview corner on every axis (server − areaVoxels = (1,1,1)), so those axes need −1. On a
        /// turned paste the client already folds the min-corner shift into the packet on flipped axes and omits that
        /// +1 there (2026-10-25 yaw90: packet − area = (−sizeZ, +1, +1); with a blanket −1 the flipped axis landed one
        /// low). Apply −1 only where <c>shift[i] == 0</c>.
        /// </summary>
        internal const int PasteOriginAdjust = -1;

        /// <summary>What a player last copied: the selection's voxels (grid origin = the box's minimum corner).</summary>
        /// <summary>
        /// What a paste places: blocks by cell relative to the box's corner (a copy or a saved design is one block, a
        /// prefab design many), and the box size in voxels.
        /// </summary>
        internal sealed record Clipboard(Dictionary<(int X, int Y, int Z), BlockData> Cells, uint SizeX, uint SizeY, uint SizeZ)
        {
            public Clipboard(BlockData voxels, uint sizeX, uint sizeY, uint sizeZ)
                : this(new Dictionary<(int, int, int), BlockData> { [(0, 0, 0)] = voxels }, sizeX, sizeY, sizeZ) { }

            /// <summary>The props that stood in the box (offsets from its minimum corner).</summary>
            public List<Props.DesignProp> Props { get; init; } = new();

            /// <summary>The block and index of a voxel of the box, or null where nothing is stored.</summary>
            public (BlockData Block, int Index)? At(int x, int y, int z)
                => Cells.TryGetValue((x / BlockData.Size, y / BlockData.Size, z / BlockData.Size), out BlockData block)
                    ? (block, TerrainEdits.VoxelIndex(x % BlockData.Size, y % BlockData.Size, z % BlockData.Size)) : null;
        }

        /// <summary>Most cells one design placement may change (prefabs such as the castle pieces span up to ~50).</summary>
        private const int MaxCellsPerDesign = 256;

        private static readonly Dictionary<ulong, Clipboard> clipboards = new();

        /// <summary>The fields of a ClientSelectionAreaRequest the server uses.</summary>
        private sealed class SelectionRequest
        {
            public uint Action;
            public ulong Cell;
            public uint X, Y, Z, SizeX, SizeY, SizeZ;
            public int[] Rotation = new int[16];
            public int PropsStart, PropsEnd, BlocksStart, BlocksEnd;
            public uint Props, Blocks;
            public BlockData Voxels;
            /// <summary>
            /// Every block of a copy / design save by its cell in the box (block key = a level-2 cell id whose x, y, z are
            /// the cell's place counted from the box's corner; a 17×4×53 copy sent (0,0,0) and (0,0,1), 2026-10-14).
            /// </summary>
            public Dictionary<(int X, int Y, int Z), BlockData> BlockCells = new();
            public List<ulong> Cells = new();
            /// <summary>Paste of a design: its guid (0 for the clipboard). Saving a design: its name and tags.</summary>
            public ulong TemplateGuid;
            public string Name = "", Tags = "";
            /// <summary>The box is larger than one cell's 40³ grid (fine for a paste, which places the server's copy).</summary>
            public bool LargerThanCell;
            /// <summary>
            /// Paste mode (request +0x148, constructor default 1; the paste writer FUN_1430b96a0 copies the paste cursor's
            /// +0x2c8: 2 Paste Air, 3 Paste Interior Air, 1 when Remove voxels is set) and the Remove voxels ("Paste as
            /// Delete") flag (+0x14c, cursor +0x2c4). The paste applies the mode to interior empty space (0xFFFC: air in modes
            /// 2 and 3, else not written; <see cref="PastedMaterial"/>); surrounding air and Remove voxels are read for the
            /// tutorial goals only.
            /// </summary>
            public uint PasteMode;
            public bool AsDelete;
        }

        /// <summary>Reads a request; on a malformed one it throws, leaving what was read (the cell list) in <paramref name="r"/>.</summary>
        private static SelectionRequest ReadSelection(byte[] body) => ReadSelection(body, new SelectionRequest());

        private static SelectionRequest ReadSelection(byte[] body, SelectionRequest r)
        {
            r.Action = BitConverter.ToUInt32(body, 1);
            r.Cell = BitConverter.ToUInt64(body, 5);
            int p = 13;
            r.X = BitConverter.ToUInt32(body, p);
            r.Y = BitConverter.ToUInt32(body, p + 4);
            r.Z = BitConverter.ToUInt32(body, p + 8);
            r.SizeX = BitConverter.ToUInt32(body, p + 12);
            r.SizeY = BitConverter.ToUInt32(body, p + 16);
            r.SizeZ = BitConverter.ToUInt32(body, p + 20);
            p += 24;
            r.PropsStart = p;
            r.Props = BitConverter.ToUInt32(body, p);
            p += 4;
            for (uint i = 0; i < r.Props; i++)
                p = SkipPropData(body, p);
            r.PropsEnd = p;
            for (int i = 0; i < 16; i++)
                r.Rotation[i] = BitConverter.ToInt32(body, p + i * 4);
            p += 64;
            p += 4 + (int)BitConverter.ToUInt32(body, p) * 6;   // material map (the client's own; the server counts)
            r.TemplateGuid = BitConverter.ToUInt64(body, p);    // clipboard +0x20: the design being pasted
            p += 8;
            r.Name = ReadString(body, ref p);
            p += 8;
            uint cellCount = BitConverter.ToUInt32(body, p);   // every cell the box touches, with the client's version
            p += 4;
            if (cellCount > MaxCellsPerEdit)
                throw new System.IO.InvalidDataException($"{cellCount} cells");
            for (uint i = 0; i < cellCount; i++, p += 16)
                r.Cells.Add(BitConverter.ToUInt64(body, p));
            r.BlocksStart = p;
            r.Blocks = BitConverter.ToUInt32(body, p);
            p += 4;
            for (uint i = 0; i < r.Blocks; i++)
            {
                ulong key = BitConverter.ToUInt64(body, p);
                p += 16;
                uint type = BitConverter.ToUInt32(body, p);
                int length = (int)BitConverter.ToUInt32(body, p + 4);
                p += 8;
                // Just the 8000-byte mask when nothing in the block is solid.
                byte[] raw = TerrainStore.Unpack(body, p, length);
                BlockData block = raw.Length > BlockData.Count / 8 ? BlockData.Deserialize(raw, BlockData.Count / 8) : new BlockData();
                r.Voxels ??= block;
                r.BlockCells[((int)((key >> 36) & 0x3FFFF), (int)(((key >> 18) & 0x3FFFF) >> 6), (int)(key & 0x3FFFF))] = block;
                p += length;
                if (type - 2 < 2)
                    p += 4;
            }
            r.BlocksEnd = p;
            r.PasteMode = BitConverter.ToUInt32(body, p);
            r.AsDelete = body[p + 4] != 0;
            p += 4 + 1;
            r.Tags = ReadString(body, ref p);
            p += 1;
            if (p != body.Length)
                throw new System.IO.InvalidDataException($"parsed to {p} of {body.Length} bytes");
            // A copy or design save sends the box's voxels in one 40³ grid; a paste sends none (the server places its
            // own copy, which may span many cells: a design such as the 85×17×65 Castle Courtyard).
            r.LargerThanCell = r.SizeX >= BlockData.Size || r.SizeY >= BlockData.Size || r.SizeZ >= BlockData.Size;
            return r;
        }

        private static string ReadString(byte[] body, ref int p)
        {
            int length = (int)BitConverter.ToUInt32(body, p);
            string text = System.Text.Encoding.UTF8.GetString(body, p + 4, length);
            p += 4 + length;
            return text;
        }

        /// <summary>Solid voxels per material in a grid.</summary>
        /// <summary>Solid voxels per material over every block of a copy / design save.</summary>
        private static SortedDictionary<ushort, uint> CountMaterials(SelectionRequest request)
        {
            var materials = new SortedDictionary<ushort, uint>();
            foreach (BlockData block in request.BlockCells.Values)
                foreach ((ushort m, uint n) in CountMaterials(block))
                    materials[m] = materials.GetValueOrDefault(m) + n;
            return materials;
        }

        private static SortedDictionary<ushort, uint> CountMaterials(BlockData voxels)
        {
            var materials = new SortedDictionary<ushort, uint>();
            if (voxels == null)
                return materials;
            for (int v = 0; v < BlockData.Count; v++)
                if ((voxels.Flags[v] & 1) != 0 && voxels.Materials[v] != 0)
                    materials[voxels.Materials[v]] = materials.GetValueOrDefault(voxels.Materials[v]) + 1;
            return materials;
        }

        private static void HandleSelection(WorldSession session, byte[] body)
        {
            var request = new SelectionRequest();
            try
            {
                ReadSelection(body, request);
            }
            catch (Exception e)
            {
                // The client holds every cell the box touches as pending until each is answered; without the answers
                // the build tools stayed locked after a refused (e.g. too big) request (2026-10-14).
                log.Warn($"Unreadable selection request from {session.CharacterName}: {e.Message}");
                SendResponse(session, AddTemporaryBlockSub, Failed);
                SendCellResponses(session, request.Cells);
                return;
            }
            if (request.LargerThanCell && request.Action is not (PasteAction or CopyAction or SaveDesignAction))
            {
                log.Info($"{session.CharacterName}: selection {request.SizeX}×{request.SizeY}×{request.SizeZ} (action {request.Action}) "
                    + "is larger than one cell; only copies, design saves and pastes may be.");
                try
                {
                    // Sample for decoding multi-block copies / design saves (Reports/, private research data).
                    string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Reports", "selections");
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"sel-{request.Action}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bin"), body);
                }
                catch (Exception) { }
                SendResponse(session, AddTemporaryBlockSub, Failed);
                SendCellResponses(session, request.Cells);
                return;
            }
            if (request.Action == PasteAction)
            {
                List<ulong> changed = Paste(session, request);
                // The client holds the listed cells as pending (and ignores their updates) until it hears back, so
                // answer them before the new voxels go out.
                SendCellResponses(session, request.Cells);
                if (changed != null)
                    ZoneTerrain.Broadcast(changed, session.ZoneId);
                return;
            }
            // Copy and Save Design read what the selection covers: every build site under it must grant the player
            // Get Inspired (Claims.PermissionCopy); otherwise Response 0xE, the client's "insufficient permissions" line.
            if (!MayCopy(session, request))
            {
                log.Info($"{session.CharacterName} tried to copy from a build site without Get Inspired.");
                SendResponse(session, AddTemporaryBlockSub, InsufficientPermissions);
                SendCellResponses(session, request.Cells);
                return;
            }
            // ... and no Player Studio design placed on someone else's site may lie under it (live's template instances,
            // TemplateInstances; audit-wiki-gaps #5): result 0x1A, the client's 563373 "Only the Build Site owner may place,
            // copy or create Designs using Designs purchased from the marketplace."
            if (CoversTemplateInstance(session, request))
            {
                log.Info($"{session.CharacterName} tried to copy / save a design over a placed Player Studio design.");
                SendResponse(session, AddTemporaryBlockSub, OnlyClaimOwnerTemplateInstances);
                SendCellResponses(session, request.Cells);
                return;
            }
            if (request.Action == SaveDesignAction)
            {
                SortedDictionary<ushort, uint> materials = CountMaterials(request);
                if (!Templates.HasRoom(session, materials.Values.Sum(n => (long)n)))
                {
                    // The client's own message offers Voxel Storage Expanders (FUN_1430b6560).
                    SendResponse(session, AddTemporaryBlockSub, ExceedsTemplateVoxels);
                    SendCellResponses(session, request.Cells);
                    return;
                }
                SendResponse(session, AddTemporaryBlockSub, 0);
                Templates.Save(session, request.Name, request.Tags, request.SizeX, request.SizeY, request.SizeZ,
                    body.AsSpan(request.BlocksStart, request.BlocksEnd - request.BlocksStart).ToArray(), materials,
                    PropsInSelection(session, request));
            }
            else
            {
                SendResponse(session, AddTemporaryBlockSub, 0);
                Copy(session, body, request);
            }
            SendCellResponses(session, request.Cells);   // a copy marks the cells pending too (edits there wait for it)
        }

        /// <summary>Does every cell the selection covers lie where the player may copy (Get Inspired)?</summary>
        private static bool MayCopy(WorldSession session, SelectionRequest request)
        {
            // The selection box itself (world units, as CoversTemplateInstance): the props copied or saved with it are read
            // from the server's own list inside that box (PropsInSelection), not from the cells the request lists, so a
            // request listing no cells (or fewer than its box covers) copied the props of a site without Get Inspired
            // (bughunt-build B9).
            (double bx, double by, double bz) = TerrainStore.CellOrigin(request.Cell);
            double voxel = TerrainEdits.VoxelSize;
            var boxMin = (bx + request.X * voxel, by + request.Y * voxel, bz + request.Z * voxel);
            var boxMax = (boxMin.Item1 + (request.SizeX + 1) * voxel, boxMin.Item2 + (request.SizeY + 1) * voxel, boxMin.Item3 + (request.SizeZ + 1) * voxel);
            if (!Claims.MayCopyIn(session.PlayerGuid, session.ZoneId, boxMin, boxMax))
                return false;
            double size = TerrainEdits.CellSize;
            foreach (ulong cell in request.Cells)
            {
                (uint level, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
                if (level != TerrainEdits.Level)
                    continue;
                double ox = cx * size, oy = cy * size, oz = cz * size;
                if (!Claims.MayCopyIn(session.PlayerGuid, session.ZoneId, (ox, oy, oz), (ox + size, oy + size, oz + size)))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Does the copy / design-save box overlap a placed Player Studio design on a site the player doesn't own
        /// (<see cref="TemplateInstances.Protects"/>)? The box in world units: from the origin voxel's corner, size + 1
        /// voxels per axis (as <see cref="SelectionBox"/>).
        /// </summary>
        private static bool CoversTemplateInstance(WorldSession session, SelectionRequest request)
        {
            (double ox, double oy, double oz) = TerrainStore.CellOrigin(request.Cell);
            double v = TerrainEdits.VoxelSize;
            var min = (ox + request.X * v, oy + request.Y * v, oz + request.Z * v);
            var max = (min.Item1 + (request.SizeX + 1) * v, min.Item2 + (request.SizeY + 1) * v, min.Item3 + (request.SizeZ + 1) * v);
            return TemplateInstances.Protects(session.PlayerGuid, session.ZoneId, min, max);
        }

        /// <summary>
        /// Test hook (dev "placedesign"): places one of the player's designs unturned with its corner at a point (local
        /// metres), through the same path as the client's paste request.
        /// </summary>
        public static bool PlaceDesign(WorldSession session, ulong guid, System.Numerics.Vector3 at)
        {
            (double gx, double gy, double gz) = TerrainEdits.ToGlobal(at);
            long vx = (long)Math.Floor(gx / TerrainEdits.VoxelSize), vy = (long)Math.Floor(gy / TerrainEdits.VoxelSize),
                vz = (long)Math.Floor(gz / TerrainEdits.VoxelSize);
            // Unturned PlaceDesign: same −1 as Paste on axes with no min-corner shift (identity → all three).
            long xr = vx - PasteOriginAdjust, yr = vy - PasteOriginAdjust, zr = vz - PasteOriginAdjust;
            var request = new SelectionRequest
            {
                Action = PasteAction,
                Cell = TerrainStore.CellId(TerrainEdits.Level, (uint)(xr / BlockData.Size), (uint)(yr / BlockData.Size), (uint)(zr / BlockData.Size)),
                X = (uint)(xr % BlockData.Size), Y = (uint)(yr % BlockData.Size), Z = (uint)(zr % BlockData.Size),
                TemplateGuid = guid,
                PasteMode = PasteSolidsMode
            };
            request.Rotation[0] = request.Rotation[5] = request.Rotation[10] = request.Rotation[15] = 1;
            List<ulong> changed = Paste(session, request);
            if (changed == null)
                return false;
            ZoneTerrain.Broadcast(changed, session.ZoneId);
            return true;
        }

        /// <summary>
        /// The selection box of a copy or design save in local metres: from the origin voxel's corner, (size + 1) voxels
        /// per axis (the box spans size + 1 vertices).
        /// </summary>
        private static (System.Numerics.Vector3 Min, System.Numerics.Vector3 Max) SelectionBox(SelectionRequest request)
        {
            (double ox, double oy, double oz) = TerrainStore.CellOrigin(request.Cell);
            double v = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            var min = new System.Numerics.Vector3((float)((ox + request.X * v - origin.X) / 10), (float)((oy + request.Y * v - origin.Y) / 10),
                (float)((oz + request.Z * v - origin.Z) / 10));
            var size = new System.Numerics.Vector3((request.SizeX + 1) * (float)v / 10, (request.SizeY + 1) * (float)v / 10, (request.SizeZ + 1) * (float)v / 10);
            return (min, min + size);
        }

        /// <summary>The placed props inside a copy / design selection (the client sends none; live kept them).</summary>
        private static List<Props.DesignProp> PropsInSelection(WorldSession session, SelectionRequest request)
        {
            (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = SelectionBox(request);
            return Props.InBox(session.ZoneId, min, max);
        }

        /// <summary>
        /// TemplateCommon::PropData (writer FUN_1430afea0, reader FUN_142f09280; fields in trace/systems/building.md):
        /// u64, u32 name text, u32 prop definition, f32×3 offset (m), f32×4 rotation, f32×3 scale, u32 n × 16 bytes,
        /// u64, u64, u32, u32 n × 20 bytes, u32 tint (art tint id), string name, string text, u32×3.
        /// </summary>
        public static void WritePropData(List<byte> output, Props.DesignProp prop)
        {
            var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
            w.Write(0ul);
            w.Write(Props.NameIdOf(prop.PropId));
            w.Write(prop.PropId);
            foreach (float f in new[] { prop.Offset.X, prop.Offset.Y, prop.Offset.Z, prop.Rotation.X, prop.Rotation.Y, prop.Rotation.Z,
                         prop.Rotation.W, prop.Scale.X, prop.Scale.Y, prop.Scale.Z })
                w.Write(f);
            w.Write(0u);
            w.Write(0ul);
            w.Write(0ul);
            w.Write(0u);
            w.Write(0u);
            w.Write(Props.TintIdOf(prop.Tint));
            w.Write(prop.Name ?? "");
            w.Write(prop.Text ?? "");
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
        }

        /// <summary>
        /// Test hook (dev "copyhere"): copies the box of <paramref name="size"/> voxels per axis around the player's feet,
        /// props only (no voxels), as the player's clipboard. Returns how many props it holds.
        /// </summary>
        public static int DevCopy(WorldSession session, uint size)
        {
            (double gx, double gy, double gz) = TerrainEdits.ToGlobal(session.Position);
            long vx = (long)Math.Floor(gx / TerrainEdits.VoxelSize) - size / 2, vy = (long)Math.Floor(gy / TerrainEdits.VoxelSize) - 2,
                vz = (long)Math.Floor(gz / TerrainEdits.VoxelSize) - size / 2;
            var request = new SelectionRequest
            {
                Cell = TerrainStore.CellId(TerrainEdits.Level, (uint)(vx / BlockData.Size), (uint)(vy / BlockData.Size), (uint)(vz / BlockData.Size)),
                X = (uint)(vx % BlockData.Size), Y = (uint)(vy % BlockData.Size), Z = (uint)(vz % BlockData.Size),
                SizeX = size, SizeY = size, SizeZ = size
            };
            List<Props.DesignProp> props = PropsInSelection(session, request);
            lock (sync)
                clipboards[session.PlayerGuid] = new Clipboard(new BlockData(), size, size, size) { Props = props };
            return props.Count;
        }

        /// <summary>Test hook (dev "pasteat"): pastes the player's clipboard unturned with its corner at a local point.</summary>
        public static bool DevPaste(WorldSession session, System.Numerics.Vector3 at) => PlaceDesign(session, 0, at);

        private static void Copy(WorldSession session, byte[] body, SelectionRequest request)
        {
            SortedDictionary<ushort, uint> materials = CountMaterials(request);
            List<Props.DesignProp> props = PropsInSelection(session, request);
            lock (sync)
            {
                if (request.Voxels != null && (materials.Count > 0 || props.Count > 0))
                    clipboards[session.PlayerGuid] = new Clipboard(new Dictionary<(int, int, int), BlockData>(request.BlockCells),
                        request.SizeX, request.SizeY, request.SizeZ) { Props = props };
                else
                    clipboards.Remove(session.PlayerGuid);
            }
            if (materials.Count > 0 || props.Count > 0)
                Achievements.Copied(session);   // Building Block Basics "Copy [Ctrl-C]" (Achievements.Tutorial.cs)

            // The box's props go back in the clipboard data; the client loads them into its paste clipboard
            // (FUN_1430b7b10 → FUN_143ad8170), shows them in the preview and sends them back with the paste.
            var output = new List<byte> { ClipboardDataSub };
            output.AddRange(BitConverter.GetBytes(props.Count));
            foreach (Props.DesignProp prop in props)
                WritePropData(output, prop);
            output.AddRange(BitConverter.GetBytes(materials.Count));
            foreach ((ushort material, uint n) in materials)
            {
                output.AddRange(BitConverter.GetBytes(material));
                output.AddRange(BitConverter.GetBytes(n));
            }
            output.AddRange(body.AsSpan(request.BlocksStart, request.BlocksEnd - request.BlocksStart).ToArray());
            output.AddRange(BitConverter.GetBytes(request.SizeX));
            output.AddRange(BitConverter.GetBytes(request.SizeY));
            output.AddRange(BitConverter.GetBytes(request.SizeZ));
            output.AddRange(BitConverter.GetBytes(0u));
            output.Add(request.Blocks > 0 ? (byte)1 : (byte)0);
            session.EnqueueMessage(Network.Message.ZoneMessageOpcode.VoxelConstructionBase, output.ToArray());
            log.Debug($"{session.CharacterName} copied {request.SizeX}×{request.SizeY}×{request.SizeZ} ({props.Count} props, " +
                $"materials {string.Join(" ", materials.Select(m => $"{m.Key}×{m.Value}"))}).");
        }

        /// <summary>
        /// Places the player's clipboard: grid point s goes where the client's paste preview puts it
        /// (<c>s.x·row0 + s.y·row1 + s.z·row2</c>, rows from the request's 4×4 i32 matrix), moved so the turned box's
        /// minimum corner lands on the request's origin; a voxel's in-voxel offset turns with it.
        /// </summary>
        private static List<ulong> Paste(WorldSession session, SelectionRequest request)
        {
            Clipboard clipboard = null;
            if (request.TemplateGuid != 0)
            {
                // Placing a design: its stored voxels, not the clipboard.
                if (Templates.VoxelsOf(session, request.TemplateGuid) is { } design)
                    clipboard = new Clipboard(design.Cells, design.SizeX, design.SizeY, design.SizeZ)
                    {
                        Props = Templates.PropsOf(request.TemplateGuid)
                    };
            }
            else
                lock (sync)
                    clipboards.TryGetValue(session.PlayerGuid, out clipboard);
            if (clipboard == null)
            {
                log.Debug($"{session.CharacterName} pasted with an empty clipboard.");
                SendResponse(session, AddTemporaryBlockSub, Failed);
                return null;
            }
            (uint level, uint cellX, uint cellY, uint cellZ) = TerrainStore.Decode(request.Cell);
            int[] m = request.Rotation;
            if (level != TerrainEdits.Level || !IsRotation(m))
            {
                SendResponse(session, AddTemporaryBlockSub, Failed);
                return null;
            }

            // Where the turned box's minimum corner sits relative to the preview map (s → Σ s_j·row_j).
            int[] size = { (int)clipboard.SizeX, (int)clipboard.SizeY, (int)clipboard.SizeZ };
            var shift = new int[3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    shift[i] -= Math.Min(0, m[j * 4 + i] * size[j]);
            // −1 only on axes the client did not already fold into the packet via the min-corner shift (see PasteOriginAdjust).
            int adjX = shift[0] == 0 ? PasteOriginAdjust : 0;
            int adjY = shift[1] == 0 ? PasteOriginAdjust : 0;
            int adjZ = shift[2] == 0 ? PasteOriginAdjust : 0;
            long baseX = (long)cellX * BlockData.Size + request.X + shift[0] + adjX;
            long baseY = (long)cellY * BlockData.Size + request.Y + shift[1] + adjY;
            long baseZ = (long)cellZ * BlockData.Size + request.Z + shift[2] + adjZ;

            Dictionary<ulong, List<TerrainEdits.VoxelValue>> byCell = TurnedVoxels(clipboard, m, baseX, baseY, baseZ, request.PasteMode);
            if ((byCell.Count == 0 && clipboard.Props.Count == 0) || byCell.Count > (request.TemplateGuid != 0 ? MaxCellsPerDesign : MaxCellsPerEdit))
            {
                SendResponse(session, AddTemporaryBlockSub, Failed);
                return null;
            }

            uint zone = session.ZoneId;
            MergeVertexOnly(byCell, zone, session.PlayerGuid);
            // The box of a placed Player Studio copy (world units), recorded once it is placed (TemplateInstances).
            (double X, double Y, double Z)? studioMin = null, studioMax = null;
            // TASKS 6.45: a design bought in Player Studio goes only on the player's own site — placing one on someone
            // else's site got an error message (13 Apr 2016, P4632; P4954). The
            // client's message is result 0x1A, Construction.OnlyClaimOwnerMayPlaceOrCopyTemplateInstances 563373 "Only the
            // Build Site owner may place, copy or create Designs using Designs purchased from the marketplace." (0.589
            // FUN_1430b6560, 0.695 part_0087.c:32798). Checked against every site the turned box touches.
            if (request.TemplateGuid != 0 && Templates.IsPlayerStudioCopy(session.PlayerGuid, request.TemplateGuid))
            {
                double v = TerrainEdits.VoxelSize;
                long[] lo = { long.MaxValue, long.MaxValue, long.MaxValue }, hi = { long.MinValue, long.MinValue, long.MinValue };
                long[] b = { baseX, baseY, baseZ };
                for (int corner = 0; corner < 8; corner++)
                {
                    int[] s = { (corner & 1) != 0 ? size[0] : 0, (corner & 2) != 0 ? size[1] : 0, (corner & 4) != 0 ? size[2] : 0 };
                    for (int i = 0; i < 3; i++)
                    {
                        long g = b[i] + Dot(m, i, s);
                        lo[i] = Math.Min(lo[i], g);
                        hi[i] = Math.Max(hi[i], g + 1);
                    }
                }
                if (!Claims.OwnsEverySiteIn(session.PlayerGuid, zone, (lo[0] * v, lo[1] * v, lo[2] * v), (hi[0] * v, hi[1] * v, hi[2] * v)))
                {
                    log.Info($"{session.CharacterName} tried to place Player Studio design {request.TemplateGuid} on a site they don't own.");
                    SendResponse(session, AddTemporaryBlockSub, OnlyClaimOwnerTemplateInstances);
                    return null;
                }
                studioMin = (lo[0] * v, lo[1] * v, lo[2] * v);
                studioMax = (hi[0] * v, hi[1] * v, hi[2] * v);
            }
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in byCell)
            {
                var mask = new byte[BlockData.Count / 8];
                foreach (TerrainEdits.VoxelValue v in values)
                    mask[v.Index >> 3] |= (byte)(1 << (7 - (v.Index & 7)));
                if (!ZoneTerrain.MayApply(cell, mask, session.PlayerGuid, zone))
                {
                    log.Info($"{session.CharacterName} tried to paste into cell {cell:X} without permission.");
                    SendResponse(session, AddTemporaryBlockSub, InsufficientPermissions);
                    return null;
                }
            }
            // One Environment Server operation (job type 11 "Construction Paste Template/Clipboard"): the cost checked there
            // against the bag, then every cell written, with each cell's touched voxels before and after for the undo record
            // (LocalEnvironmentServer.WriteCells: the read / write / read loop that was here).
            WriteResult written = Env.WriteCells(zone, session.PlayerGuid, byCell.Select(c => (c.Key, c.Value)).ToList(), Budget(session), true);
            if (written.Unavailable)
            {
                SendResponse(session, AddTemporaryBlockSub, Failed);
                ZoneTerrain.SendUnavailable(session);
                return null;
            }
            if (written.Result == InsufficientMaterials)
            {
                SendResponse(session, AddTemporaryBlockSub, InsufficientMaterials);
                return null;
            }
            if (!PayFor(session, written.Delta))
                log.Warn($"{session.CharacterName}'s paste was written but its materials could not be taken any more.");

            var record = new Edit(zone, request.TemplateGuid != 0 ? "Place design" : "Paste");
            record.Cells.AddRange(written.Cells);
            int voxels = byCell.Values.Sum(v => v.Count);
            PlaceProps(session, clipboard.Props, m, baseX, baseY, baseZ, record);
            Remember(session, record);
            string designName = request.TemplateGuid != 0 ? Templates.NameOf(session, request.TemplateGuid) : "";
            if (request.TemplateGuid != 0)
                Achievements.DesignPlaced(session, designName);
            Achievements.Pasted(session, designName, request.PasteMode, request.AsDelete);   // tutorial goals
            if (studioMin is { } placedMin && studioMax is { } placedMax)
                TemplateInstances.Record(session, zone, request.TemplateGuid, placedMin, placedMax);
            SendResponse(session, AddTemporaryBlockSub, 0);
            // Every listed cell the server holds goes out (the paster's client dropped its preview of them).
            List<ulong> changed = byCell.Keys.Concat(request.Cells.Where(c => Env.Has(c, zone))).Distinct().ToList();
            // Info while L199 (preview vs paste origin) is open: area − copy sizes are the margin the client
            // baked into the request; a +2 / +2 on x/z with a 1-voxel shift matches FUN_1436cd920's −1 margins.
            log.Info($"{session.CharacterName} pasted {voxels} voxels into {byCell.Count} cell(s) at {request.X},{request.Y},{request.Z} "
                + $"(client area {request.SizeX}×{request.SizeY}×{request.SizeZ}, copy {clipboard.SizeX}×{clipboard.SizeY}×{clipboard.SizeZ}, "
                + $"area−copy {(int)request.SizeX - (int)clipboard.SizeX},{(int)request.SizeY - (int)clipboard.SizeY},{(int)request.SizeZ - (int)clipboard.SizeZ}, "
                + $"first voxel at {baseX},{baseY},{baseZ}) "
                + $"turned [{m[0]} {m[1]} {m[2]}; {m[4]} {m[5]} {m[6]}; {m[8]} {m[9]} {m[10]}].");
            return changed;
        }

        /// <summary>
        /// The voxels a paste writes: grid point s of the clipboard goes to base + preview(s) (same map as the client's
        /// paste cursor: Σ s_j·row_j; base already holds the shift that puts the turned box's minimum corner on the
        /// request's origin). The clipboard's solid voxels are written; empty ones leave the target as it is.
        /// <para>
        /// A voxel carries two things that live in different places (docs/terrain.md "How the client draws a dig"): its
        /// material belongs to the grid point (the client samples at voxel corners), its vertex to the cube [s, s+1]³
        /// stored at s (ContourEx FUN_142ac4590 joins the vertices of the cubes around each edge whose two voxels differ;
        /// a cube without one is drawn at its centre). A turn that flips an axis maps that cube onto the cube one voxel
        /// lower on that axis (cube [s, s+1] → [M·s − 1, M·s]), so the vertex goes to base + M·s − n (n = 1 on each
        /// flipped axis) at M·(u − ½) + ½. The cubes of the copy's surface whose corner voxel is not solid (every face
        /// towards −x, −y or −z, and after a flip the faces the flip turns that way) keep their vertex too: as a
        /// vertex-only voxel (flag 2, the target's own material kept, <see cref="MergeVertexOnly"/>), the way the client's
        /// own smooth brushes send them, wherever a corner of that cube is one of the paste's solid voxels.
        /// </para>
        /// <para>
        /// Until 2026-10-21 the vertex stayed with the material at base + M·s and vertices of cubes with a non-solid
        /// corner were dropped: every surface vertex of a turned paste or design sat one voxel (0.4 m) off along each
        /// flipped axis, and the faces whose cube corner is air were drawn at cube centres (0.4 m steps on smooth shapes)
        /// (Tests/Rules/BuildPlacementTests).
        /// </para>
        /// <para>
        /// Which material each clipboard voxel writes is <see cref="PastedMaterial"/> (the client's paste rule): interior
        /// empty space (0xFFFC) is never written as a material — air in the Paste Air / Paste Interior Air modes, nothing
        /// otherwise. Air written that way takes the vertex of its cube like a solid voxel does.
        /// </para>
        /// </summary>
        internal static Dictionary<ulong, List<TerrainEdits.VoxelValue>> TurnedVoxels(Clipboard clipboard, int[] m, long baseX, long baseY, long baseZ,
            uint pasteMode = PasteSolidsMode)
        {
            int[] size = { (int)clipboard.SizeX, (int)clipboard.SizeY, (int)clipboard.SizeZ };
            long[] b = { baseX, baseY, baseZ };
            // n: one voxel on each world axis the turn flips (coefficient of some clipboard axis is −1).
            var flip = new int[3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if (m[j * 4 + i] < 0)
                        flip[i] = 1;
            // The target voxels the paste makes solid (global voxel → material), the ones it makes air (interior empty
            // space in the air modes), and the vertices of the target cubes.
            var solid = new Dictionary<(long X, long Y, long Z), ushort>();
            var air = new HashSet<(long X, long Y, long Z)>();
            var vertex = new Dictionary<(long X, long Y, long Z), (byte, byte, byte)>();
            for (int sz = 0; sz <= size[2]; sz++)
                for (int sx = 0; sx <= size[0]; sx++)
                    for (int sy = 0; sy <= size[1]; sy++)
                    {
                        if (clipboard.At(sx, sy, sz) is not (BlockData source, int from))
                            continue;
                        byte flags = source.Flags[from];
                        int[] s = { sx, sy, sz };
                        long gx = b[0] + Dot(m, 0, s), gy = b[1] + Dot(m, 1, s), gz = b[2] + Dot(m, 2, s);
                        if (PastedMaterial(flags, source.Materials[from], pasteMode, sx == 0 || sy == 0 || sz == 0) is ushort written)
                        {
                            if (written != 0)
                                solid[(gx, gy, gz)] = written;
                            else
                                air.Add((gx, gy, gz));
                        }
                        if ((flags & 2) != 0)
                        {
                            int[] o = { source.Offsets[from * 3] - BlockData.CentreOffset, source.Offsets[from * 3 + 1] - BlockData.CentreOffset,
                                source.Offsets[from * 3 + 2] - BlockData.CentreOffset };
                            vertex[(gx - flip[0], gy - flip[1], gz - flip[2])] = (Offset(m, 0, o), Offset(m, 1, o), Offset(m, 2, o));
                        }
                    }
            var byCell = new Dictionary<ulong, List<TerrainEdits.VoxelValue>>();
            void Add(long gx, long gy, long gz, TerrainEdits.VoxelValue value)
            {
                ulong cell = TerrainStore.CellId(TerrainEdits.Level, (uint)(gx / BlockData.Size), (uint)(gy / BlockData.Size), (uint)(gz / BlockData.Size));
                if (!byCell.TryGetValue(cell, out var values))
                    byCell[cell] = values = new List<TerrainEdits.VoxelValue>();
                values.Add(value with { Index = TerrainEdits.VoxelIndex((int)(gx % BlockData.Size), (int)(gy % BlockData.Size), (int)(gz % BlockData.Size)) });
            }
            foreach (((long gx, long gy, long gz), ushort material) in solid)
            {
                if (gx < 0 || gy < 0 || gz < 0)
                    continue;
                Add(gx, gy, gz, vertex.TryGetValue((gx, gy, gz), out var v)
                    ? new TerrainEdits.VoxelValue(0, 3, material, v.Item1, v.Item2, v.Item3)
                    : new TerrainEdits.VoxelValue(0, 1, material, BlockData.CentreOffset, BlockData.CentreOffset, BlockData.CentreOffset));
            }
            foreach ((long gx, long gy, long gz) in air)
            {
                if (gx < 0 || gy < 0 || gz < 0)
                    continue;
                Add(gx, gy, gz, vertex.TryGetValue((gx, gy, gz), out var v)
                    ? new TerrainEdits.VoxelValue(0, 3, 0, v.Item1, v.Item2, v.Item3)
                    : new TerrainEdits.VoxelValue(0, 1, 0, BlockData.CentreOffset, BlockData.CentreOffset, BlockData.CentreOffset));
            }
            foreach (((long gx, long gy, long gz), (byte o0, byte o1, byte o2)) in vertex)
            {
                if (gx < 0 || gy < 0 || gz < 0 || solid.ContainsKey((gx, gy, gz)) || air.Contains((gx, gy, gz)))
                    continue;
                // Only on the pasted shape's own surface: a corner of the cube is one of its solid voxels.
                bool touches = false;
                for (int d = 0; d < 8 && !touches; d++)
                    touches = solid.ContainsKey((gx + (d & 1), gy + (d >> 1 & 1), gz + (d >> 2)));
                if (touches)
                    Add(gx, gy, gz, new TerrainEdits.VoxelValue(0, 2, 0, o0, o1, o2));
            }
            return byCell;
        }

        /// <summary>
        /// Paste modes (ClientSelectionAreaRequest +0x148, OffGridPasteClientRequest +0x80; the Fine Tuning menu
        /// FUN_1436b14b0): 1 the default (constructor value; also sent with Remove voxels), 2 "Paste with Surrounding Empty
        /// Space" (562542 "Paste air with blocks?"), 3 "Paste with Interior Empty Space" (571841 "Paste air with interior
        /// blocks?").
        /// </summary>
        public const uint PasteSolidsMode = 1, PasteAirMode = 2, PasteInteriorAirMode = 3;

        /// <summary>
        /// The material a paste writes for one clipboard voxel, or null when it writes none there (the target keeps what
        /// it has): the client's own paste rule, VoxelFarm CClipboardData paste FUN_142a7eeb0 (trace/0695/ruin-stamping.md
        /// §1.2), for the materials the server applies:
        /// <list type="bullet">
        /// <item>a solid material is written as it is;</item>
        /// <item><b>interior empty space</b> (0xFFFC, <see cref="VoxelTemplate.InteriorAir"/>: the air a copy finds enclosed
        /// by the build, FUN_143f9a150 → FUN_143f9c330; every castle design is mostly that — Castle Long Room 480,372 of
        /// its 782,232 set voxels) is written as <b>air</b> (material 0) in the Paste Air (2) and Paste Interior Air (3)
        /// modes, except on the box's first slice (index 0 on any axis), and not at all in the others;</item>
        /// <item>surrounding air (material 0) is not written (the client writes it in modes 2 and 4: not applied yet, like
        /// Remove voxels; TASKS 6.57).</item>
        /// </list>
        /// Until 2026-10-21 interior empty space was written raw, as material 0xFFFC. The client's mesher draws 0xFFFC as
        /// air (ContourEx FUN_142ac4590), so the design looked right, but its occluder builder counts every voxel with the
        /// material bit and a material ≠ 0 whose VoxelMaterialLibrary entry is opaque (FUN_142ae77d0, FUN_142ae8610's
        /// 100-point check), and entry 0xFFFC is the copy of entry 0 (FUN_143ff41f0 writes it at +0x37ff20 = 0xFFFC × 0x38;
        /// TRANSPARENT_TYPE_ID 0 → +8 = 0, FUN_143ffa310). So every cell of a placed design got occluder quads across its
        /// rooms and doorways (VoxelVisibility FUN_1437879d0 → Deep software occlusion), and from inside a design all the
        /// terrain around it was culled while its flora still drew (user report 2026-10-21).
        /// </summary>
        internal static ushort? PastedMaterial(byte flags, ushort material, uint pasteMode, bool firstSlice)
        {
            if ((flags & 1) == 0 || material == 0)
                return null;
            if (material == VoxelTemplate.InteriorAir)
                return pasteMode is PasteAirMode or PasteInteriorAirMode && !firstSlice ? (ushort)0 : null;
            return material;
        }

        /// <summary>
        /// The vertex-only values of a paste (flag 2, no material: <see cref="TurnedVoxels"/>, OffGridVoxels) keep what the
        /// target layer already holds there: a voxel with a material keeps it and only takes the vertex.
        /// </summary>
        internal static void MergeVertexOnly(Dictionary<ulong, List<TerrainEdits.VoxelValue>> byCell, uint zone, ulong playerGuid)
        {
            foreach (ulong empty in byCell.Keys.ToList())
            {
                // Vertex-only voxels where the player may not build (just outside the site) are left out, not refused.
                List<TerrainEdits.VoxelValue> values = byCell[empty];
                if (values.Exists(v => v.Flags == 2))
                {
                    Func<int, bool> may = ZoneTerrain.VoxelPermission(empty, playerGuid, zone);
                    values.RemoveAll(v => v.Flags == 2 && !may(v.Index));
                }
                if (values.Count == 0)
                    byCell.Remove(empty);
            }
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in byCell)
            {
                var only = values.Where(v => v.Flags == 2).ToList();
                if (only.Count == 0)
                    continue;
                var held = Env.Read(cell, EnvironmentMasks.MaskOf(only), zone).ToDictionary(v => v.Index);
                for (int i = 0; i < values.Count; i++)
                    if (values[i].Flags == 2 && held.TryGetValue(values[i].Index, out var old) && (old.Flags & 1) != 0)
                        values[i] = values[i] with { Flags = (byte)(old.Flags | 2), Material = old.Material };
            }
        }

        /// <summary>
        /// The props of a paste: each goes where its spot in the box lands (the same turn as the voxels: a point p of
        /// the box, in voxels, goes to base + M·p, plus one voxel on each axis the turn flips), turned with the paste.
        /// Each is paid with an item of it from the player's bag, else built from its recipe materials when the player
        /// knows the recipe (<see cref="Props.PlaceDesignProp"/>, TASKS 6.40). The ones left out are listed in the
        /// client's two messages:
        /// <list type="bullet">
        /// <item>VoxelConstruction 9 ZoneMissingPropList (0.589 reader FUN_1430b31a0, handler FUN_1430b8590: chat
        /// "Construction.SkippedProps" then "count : name" lines): {u32 n, n × i32 name text, u32 n, n × i32 count} —
        /// no item and not enough materials;</item>
        /// <item>VoxelConstruction 8 ZoneUnknownPropList (0.589 reader FUN_1430b3240 → FUN_1430b20f0, handler
        /// FUN_1430b87a0; 0.695 FUN_14315d360 / FUN_1431633e0, same shape): a hash list {u32 n, n × (u32 key, u32 name
        /// text, i32 count)}, shown as "Construction.MissingPropKnowledge" then "count : name" lines — no item and the
        /// recipe unknown. The key is only the list's hash key (the prop id here).</item>
        /// </list>
        /// </summary>
        private static void PlaceProps(WorldSession session, List<Props.DesignProp> props, int[] m, long baseX, long baseY, long baseZ, Edit record)
        {
            if (props.Count == 0)
                return;
            double v = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            var turn = System.Numerics.Quaternion.CreateFromRotationMatrix(new System.Numerics.Matrix4x4(
                m[0], m[4], m[8], 0, m[1], m[5], m[9], 0, m[2], m[6], m[10], 0, 0, 0, 0, 1));
            PlaceProps(session, props, prop =>
            {
                double[] p = { prop.Offset.X * 10 / v, prop.Offset.Y * 10 / v, prop.Offset.Z * 10 / v };
                double[] g = new double[3];
                long[] b = { baseX, baseY, baseZ };
                for (int i = 0; i < 3; i++)
                {
                    g[i] = b[i];
                    for (int j = 0; j < 3; j++)
                    {
                        g[i] += m[j * 4 + i] * p[j];
                        if (m[j * 4 + i] < 0)
                            g[i] += 1;
                    }
                }
                var at = new System.Numerics.Vector3((float)((g[0] * v - origin.X) / 10), (float)((g[1] * v - origin.Y) / 10), (float)((g[2] * v - origin.Z) / 10));
                return (at, turn);
            }, record);
        }

        /// <summary>
        /// Places a paste's props where <paramref name="place"/> puts each (local metres, and the paste's turn, which is
        /// applied after the prop's own rotation) and reports the ones left out (see the overload above).
        /// </summary>
        private static void PlaceProps(WorldSession session, List<Props.DesignProp> props,
            Func<Props.DesignProp, (System.Numerics.Vector3 At, System.Numerics.Quaternion Turn)> place, Edit record)
        {
            if (props.Count == 0)
                return;
            var skipped = new Dictionary<uint, int>();
            var unknown = new Dictionary<uint, (uint Name, int Count)>();
            foreach (Props.DesignProp prop in props)
            {
                (System.Numerics.Vector3 at, System.Numerics.Quaternion turn) = place(prop);
                var q = new System.Numerics.Quaternion(prop.Rotation.X, prop.Rotation.Y, prop.Rotation.Z, prop.Rotation.W);
                if (q.LengthSquared() < 1e-6f)
                    q = System.Numerics.Quaternion.Identity;
                q = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Concatenate(q, turn));
                var rotation = new System.Numerics.Vector4(q.X, q.Y, q.Z, q.W);
                ulong guid = Props.PlaceDesignProp(session, prop, at, rotation, out Props.DesignPropShortfall shortfall);
                if (guid != 0)
                    record.PastedProps.Add((prop, at, rotation, guid));
                else if (shortfall == Props.DesignPropShortfall.Missing)
                {
                    uint name = Props.NameIdOf(prop.PropId);
                    skipped[name] = skipped.GetValueOrDefault(name) + 1;
                }
                else if (shortfall == Props.DesignPropShortfall.Unknown)
                    unknown[prop.PropId] = (Props.NameIdOf(prop.PropId), unknown.GetValueOrDefault(prop.PropId).Count + 1);
                else
                    log.Debug($"{session.CharacterName}: pasted prop {prop.PropId} not placed at {at} (no model, or no build permission there).");
            }
            if (unknown.Count > 0)
            {
                var output = new List<byte>();
                var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
                w.Write(ZoneUnknownPropListSub);
                w.Write((uint)unknown.Count);
                foreach ((uint propId, (uint name, int count)) in unknown)
                {
                    w.Write(propId);
                    w.Write(name);
                    w.Write(count);
                }
                session.EnqueueMessage(Network.Message.ZoneMessageOpcode.VoxelConstructionBase, output.ToArray());
            }
            if (skipped.Count > 0)
            {
                var output = new List<byte>();
                var w = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
                w.Write(ZoneMissingPropListSub);
                w.Write((uint)skipped.Count);
                foreach (uint name in skipped.Keys)
                    w.Write(name);
                w.Write((uint)skipped.Count);
                foreach (int n in skipped.Values)
                    w.Write(n);
                session.EnqueueMessage(Network.Message.ZoneMessageOpcode.VoxelConstructionBase, output.ToArray());
            }
            log.Debug($"{session.CharacterName} pasted {record.PastedProps.Count} of {props.Count} props ({skipped.Values.Sum()} skipped, "
                + $"{unknown.Values.Sum(u => u.Count)} without the recipe).");
        }

        public const byte ZoneUnknownPropListSub = 8, ZoneMissingPropListSub = 9;

        /// <summary>
        /// Paste-preview map for one world axis: packet stores cursor rows at +0x270/+0x280/+0x290 row-major in the
        /// 4×4 i32 block; the client draws <c>Σ s_j·row_j</c> (FUN_14315feb0). Using row·s instead was the transpose
        /// (Frida 2026-10-24: same matrix bytes, placed yaw mirrored vs preview).
        /// </summary>
        private static int Dot(int[] m, int axis, int[] v) => m[axis] * v[0] + m[4 + axis] * v[1] + m[8 + axis] * v[2];

        private static byte Offset(int[] m, int axis, int[] centred)
            => (byte)Math.Clamp(BlockData.CentreOffset + Dot(m, axis, centred), 0, 255);

        /// <summary>Is the 3 × 3 part a quarter-turn rotation or mirror (one ±1 per row and column)?</summary>
        private static bool IsRotation(int[] m)
        {
            for (int i = 0; i < 3; i++)
            {
                int row = 0, column = 0;
                for (int j = 0; j < 3; j++)
                {
                    if (Math.Abs(m[i * 4 + j]) > 1 || Math.Abs(m[j * 4 + i]) > 1)
                        return false;
                    row += Math.Abs(m[i * 4 + j]);
                    column += Math.Abs(m[j * 4 + i]);
                }
                if (row != 1 || column != 1)
                    return false;
            }
            return true;
        }
    }
}
