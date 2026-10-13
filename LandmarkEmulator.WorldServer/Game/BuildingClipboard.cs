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
    /// A copy carries one block: the selection's voxels in a 40³ grid with the box's origin at (0, 0, 0) (the same
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

        /// <summary>What a player last copied: the selection's voxels (grid origin = the box's minimum corner).</summary>
        /// <summary>
        /// What a paste places: blocks by cell relative to the box's corner (a copy or a saved design is one block, a
        /// prefab design many), and the box size in voxels.
        /// </summary>
        private sealed record Clipboard(Dictionary<(int X, int Y, int Z), BlockData> Cells, uint SizeX, uint SizeY, uint SizeZ)
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
            public List<ulong> Cells = new();
            /// <summary>Paste of a design: its guid (0 for the clipboard). Saving a design: its name and tags.</summary>
            public ulong TemplateGuid;
            public string Name = "", Tags = "";
        }

        private static SelectionRequest ReadSelection(byte[] body)
        {
            var r = new SelectionRequest { Action = BitConverter.ToUInt32(body, 1), Cell = BitConverter.ToUInt64(body, 5) };
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
                p += 16;
                uint type = BitConverter.ToUInt32(body, p);
                int length = (int)BitConverter.ToUInt32(body, p + 4);
                p += 8;
                if (r.Voxels == null)
                {
                    // Just the 8000-byte mask when nothing in the box is solid.
                    byte[] raw = TerrainStore.Unpack(body, p, length);
                    r.Voxels = raw.Length > BlockData.Count / 8 ? BlockData.Deserialize(raw, BlockData.Count / 8) : new BlockData();
                }
                p += length;
                if (type - 2 < 2)
                    p += 4;
            }
            r.BlocksEnd = p;
            p += 4 + 1;
            r.Tags = ReadString(body, ref p);
            p += 1;
            if (p != body.Length)
                throw new System.IO.InvalidDataException($"parsed to {p} of {body.Length} bytes");
            if (r.SizeX >= BlockData.Size || r.SizeY >= BlockData.Size || r.SizeZ >= BlockData.Size)
                throw new System.IO.InvalidDataException($"box {r.SizeX}×{r.SizeY}×{r.SizeZ} is too big");
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
            SelectionRequest request;
            try
            {
                request = ReadSelection(body);
            }
            catch (Exception e)
            {
                log.Warn($"Unreadable selection request from {session.CharacterName}: {e.Message}");
                SendResponse(session, AddTemporaryBlockSub, Failed);
                return;
            }
            if (request.Action == PasteAction)
            {
                List<ulong> changed = Paste(session, request);
                // The client holds the listed cells as pending (and ignores their updates) until it hears back, so
                // answer them before the new voxels go out.
                SendCellResponses(session, request.Cells);
                if (changed != null)
                    TerrainEdits.Broadcast(changed, session.ZoneId);
                return;
            }
            if (request.Action == SaveDesignAction)
            {
                SortedDictionary<ushort, uint> materials = CountMaterials(request.Voxels);
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

        /// <summary>
        /// Test hook (dev "placedesign"): places one of the player's designs unturned with its corner at a point (local
        /// metres), through the same path as the client's paste request.
        /// </summary>
        public static bool PlaceDesign(WorldSession session, ulong guid, System.Numerics.Vector3 at)
        {
            (double gx, double gy, double gz) = TerrainEdits.ToGlobal(at);
            long vx = (long)Math.Floor(gx / TerrainEdits.VoxelSize), vy = (long)Math.Floor(gy / TerrainEdits.VoxelSize),
                vz = (long)Math.Floor(gz / TerrainEdits.VoxelSize);
            var request = new SelectionRequest
            {
                Action = PasteAction,
                Cell = TerrainStore.CellId(TerrainEdits.Level, (uint)(vx / BlockData.Size), (uint)(vy / BlockData.Size), (uint)(vz / BlockData.Size)),
                X = (uint)(vx % BlockData.Size), Y = (uint)(vy % BlockData.Size), Z = (uint)(vz % BlockData.Size),
                TemplateGuid = guid
            };
            request.Rotation[0] = request.Rotation[5] = request.Rotation[10] = request.Rotation[15] = 1;
            List<ulong> changed = Paste(session, request);
            if (changed == null)
                return false;
            TerrainEdits.Broadcast(changed, session.ZoneId);
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
            SortedDictionary<ushort, uint> materials = CountMaterials(request.Voxels);
            List<Props.DesignProp> props = PropsInSelection(session, request);
            lock (sync)
            {
                if (request.Voxels != null && (materials.Count > 0 || props.Count > 0))
                    clipboards[session.PlayerGuid] = new Clipboard(request.Voxels, request.SizeX, request.SizeY, request.SizeZ) { Props = props };
                else
                    clipboards.Remove(session.PlayerGuid);
            }

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
        /// Places the player's clipboard: grid point s goes to M·s (M = the request's 3 × 3 rotation, rows first), moved
        /// so the turned box's minimum corner lands on the request's origin; a voxel's in-voxel offset turns with it.
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

            // Where the turned box's minimum corner sits relative to M·s.
            int[] size = { (int)clipboard.SizeX, (int)clipboard.SizeY, (int)clipboard.SizeZ };
            var shift = new int[3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    shift[i] -= Math.Min(0, m[i * 4 + j] * size[j]);
            long baseX = (long)cellX * BlockData.Size + request.X + shift[0];
            long baseY = (long)cellY * BlockData.Size + request.Y + shift[1];
            long baseZ = (long)cellZ * BlockData.Size + request.Z + shift[2];

            var byCell = new Dictionary<ulong, List<TerrainEdits.VoxelValue>>();
            for (int sz = 0; sz <= size[2]; sz++)
                for (int sx = 0; sx <= size[0]; sx++)
                    for (int sy = 0; sy <= size[1]; sy++)
                    {
                        if (clipboard.At(sx, sy, sz) is not (BlockData source, int from)
                            || (source.Flags[from] & 1) == 0 || source.Materials[from] == 0)
                            continue;
                        int[] s = { sx, sy, sz };
                        long gx = baseX + Dot(m, 0, s), gy = baseY + Dot(m, 1, s), gz = baseZ + Dot(m, 2, s);
                        if (gx < 0 || gy < 0 || gz < 0)
                            continue;
                        ulong cell = TerrainStore.CellId(TerrainEdits.Level, (uint)(gx / BlockData.Size), (uint)(gy / BlockData.Size), (uint)(gz / BlockData.Size));
                        int to = TerrainEdits.VoxelIndex((int)(gx % BlockData.Size), (int)(gy % BlockData.Size), (int)(gz % BlockData.Size));
                        int[] o = { source.Offsets[from * 3] - BlockData.CentreOffset, source.Offsets[from * 3 + 1] - BlockData.CentreOffset,
                            source.Offsets[from * 3 + 2] - BlockData.CentreOffset };
                        if (!byCell.TryGetValue(cell, out var values))
                            byCell[cell] = values = new List<TerrainEdits.VoxelValue>();
                        values.Add(new TerrainEdits.VoxelValue(to, source.Flags[from], source.Materials[from],
                            Offset(m, 0, o), Offset(m, 1, o), Offset(m, 2, o)));
                    }
            if ((byCell.Count == 0 && clipboard.Props.Count == 0) || byCell.Count > (request.TemplateGuid != 0 ? MaxCellsPerDesign : MaxCellsPerEdit))
            {
                SendResponse(session, AddTemporaryBlockSub, Failed);
                return null;
            }

            uint zone = session.ZoneId;
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in byCell)
            {
                var mask = new byte[BlockData.Count / 8];
                foreach (TerrainEdits.VoxelValue v in values)
                    mask[v.Index >> 3] |= (byte)(1 << (7 - (v.Index & 7)));
                if (!TerrainEdits.MayApply(cell, mask, session.PlayerGuid, zone))
                {
                    log.Info($"{session.CharacterName} tried to paste into cell {cell:X} without permission.");
                    SendResponse(session, AddTemporaryBlockSub, InsufficientPermissions);
                    return null;
                }
            }
            var delta = new Dictionary<ushort, int>();
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in byCell)
                foreach ((ushort material, int n) in TerrainEdits.MaterialDelta(cell, values, zone))
                    delta[material] = delta.GetValueOrDefault(material) + n;
            if (!PayFor(session, delta))
            {
                SendResponse(session, AddTemporaryBlockSub, InsufficientMaterials);
                return null;
            }

            var record = new Edit(zone, request.TemplateGuid != 0 ? "Place design" : "Paste");
            int voxels = 0;
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in byCell)
            {
                var mask = new byte[BlockData.Count / 8];
                foreach (TerrainEdits.VoxelValue v in values)
                    mask[v.Index >> 3] |= (byte)(1 << (7 - (v.Index & 7)));
                List<TerrainEdits.VoxelValue> before = TerrainEdits.Read(cell, mask, zone);
                TerrainEdits.Write(cell, values, zone, session.PlayerGuid);
                record.Cells.Add((cell, before, TerrainEdits.Read(cell, mask, zone)));
                voxels += values.Count;
            }
            PlaceProps(session, clipboard.Props, m, baseX, baseY, baseZ, record);
            Remember(session, record);
            if (request.TemplateGuid != 0)
                Achievements.DesignPlaced(session, Templates.NameOf(session, request.TemplateGuid));
            SendResponse(session, AddTemporaryBlockSub, 0);
            // Every listed cell the server holds goes out (the paster's client dropped its preview of them).
            List<ulong> changed = byCell.Keys.Concat(request.Cells.Where(c => TerrainEdits.Has(c, zone))).Distinct().ToList();
            log.Debug($"{session.CharacterName} pasted {voxels} voxels into {byCell.Count} cell(s) at {request.X},{request.Y},{request.Z} turned [{m[0]} {m[1]} {m[2]}; {m[4]} {m[5]} {m[6]}; {m[8]} {m[9]} {m[10]}].");
            return changed;
        }

        /// <summary>
        /// The props of a paste: each goes where its spot in the box lands (the same turn as the voxels: a point p of
        /// the box, in voxels, goes to base + M·p, plus one voxel on each axis the turn flips), turned with the paste.
        /// Each is paid with an item of it from the player's bag; the ones the player has none of are listed in
        /// VoxelConstruction 9 ZoneMissingPropList (reader FUN_1430b31a0, handler FUN_1430b8590: chat
        /// "Construction.SkippedProps" then "count : name" lines): {u32 n, n × i32 name text, u32 n, n × i32 count}.
        /// </summary>
        private static void PlaceProps(WorldSession session, List<Props.DesignProp> props, int[] m, long baseX, long baseY, long baseZ, Edit record)
        {
            if (props.Count == 0)
                return;
            double v = TerrainEdits.VoxelSize;
            var origin = TerrainStore.ZoneOrigin;
            var turn = System.Numerics.Quaternion.CreateFromRotationMatrix(new System.Numerics.Matrix4x4(
                m[0], m[4], m[8], 0, m[1], m[5], m[9], 0, m[2], m[6], m[10], 0, 0, 0, 0, 1));
            var skipped = new Dictionary<uint, int>();
            foreach (Props.DesignProp prop in props)
            {
                double[] p = { prop.Offset.X * 10 / v, prop.Offset.Y * 10 / v, prop.Offset.Z * 10 / v };
                double[] g = new double[3];
                long[] b = { baseX, baseY, baseZ };
                for (int i = 0; i < 3; i++)
                {
                    g[i] = b[i];
                    for (int j = 0; j < 3; j++)
                    {
                        g[i] += m[i * 4 + j] * p[j];
                        if (m[i * 4 + j] < 0)
                            g[i] += 1;
                    }
                }
                var at = new System.Numerics.Vector3((float)((g[0] * v - origin.X) / 10), (float)((g[1] * v - origin.Y) / 10), (float)((g[2] * v - origin.Z) / 10));
                var q = new System.Numerics.Quaternion(prop.Rotation.X, prop.Rotation.Y, prop.Rotation.Z, prop.Rotation.W);
                if (q.LengthSquared() < 1e-6f)
                    q = System.Numerics.Quaternion.Identity;
                q = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Concatenate(q, turn));
                var rotation = new System.Numerics.Vector4(q.X, q.Y, q.Z, q.W);
                ulong guid = Props.PlaceDesignProp(session, prop, at, rotation, out bool missing);
                if (guid != 0)
                    record.PastedProps.Add((prop, at, rotation, guid));
                else if (missing)
                {
                    uint name = Props.NameIdOf(prop.PropId);
                    skipped[name] = skipped.GetValueOrDefault(name) + 1;
                }
                else
                    log.Debug($"{session.CharacterName}: pasted prop {prop.PropId} not placed at {at} (no model, or no build permission there).");
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
            log.Debug($"{session.CharacterName} pasted {record.PastedProps.Count} of {props.Count} props ({skipped.Values.Sum()} skipped).");
        }

        public const byte ZoneMissingPropListSub = 9;

        private static int Dot(int[] m, int row, int[] v) => m[row * 4] * v[0] + m[row * 4 + 1] * v[1] + m[row * 4 + 2] * v[2];

        private static byte Offset(int[] m, int row, int[] centred)
            => (byte)Math.Clamp(BlockData.CentreOffset + Dot(m, row, centred), 0, 255);

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
