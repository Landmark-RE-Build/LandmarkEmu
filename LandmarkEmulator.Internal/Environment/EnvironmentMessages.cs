using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace LandmarkEmulator.Internal.Environment
{
    /// <summary>
    /// Service messages between a world's zone process and its Environment Server (the `envserver` container,
    /// LandmarkEmulator.EnvServer; trace/architecture/terrain-service.md §5). The zone dials ENVSERVER_ADDRESS
    /// (envserver:20500) and carries both directions on that one link as <see cref="ServicePacket"/>s (u16 family, u32 sub,
    /// body; internal-transport.md §3).
    /// <para>
    /// <b>Envelope</b> (spec §5.1): every request body starts with u32 request id (0 = no answer wanted) and u32 landscape
    /// (StaticZoneData zone id), then the payload. Every answer is one <b>EnvServerResponse 0xF4/0x15</b> (the client's
    /// id-table name for "Environment Server → zone results"; no client reader): u32 request id, u32 result (the client's
    /// VoxelConstruction Response codes: 0 ok, 1 failed, 0xC insufficient materials), then the payload of the request's
    /// kind. The service sends the version changes a request caused (<see cref="CellVersionsChanged"/>) before its answer
    /// on the same link, so the zone's version index is current when the call returns.
    /// </para>
    /// <para>
    /// <b>Family / sub</b>: the client's where a name exists (0.589 numbering, as in the client's id tables; every one is
    /// "same" in 0.695, trace/0695/coverage-695.md): TerrainCell 0xEC — ClientCellRequest 1 (the zone's fetch of cell bodies),
    /// ModifiedCellManifestUpdate 0xB (<see cref="CellVersionsChanged"/>, batched); VoxelConstruction 0xF4 — ApplyDiffRequest
    /// 1, ClientOwnershipRequest 4 (Voxel Vision), EnvServerResponse 0x15, SpawnExportedTemplateRequest 0x18 (stamps);
    /// Claims 0xF9 — RevertTerrain 0x33 (a box back to the generated ground); Command 0x1E —
    /// EnvServerDataMonitorUpdateRequest 0x4AB (the job counters, ClientEnvironmentServerMetrics). Everything else is
    /// <b>ours</b>, from 0x10000 up in the family of its area. Every body is ours: live's internal layouts are not found
    /// yet (L48); the client never sees them.
    /// </para>
    /// <para>
    /// Voxel value lists (<see cref="EnvVoxel"/>, 10 bytes each) and 8000-byte changed masks travel deflated
    /// (<see cref="EnvWire.Packed"/>): an edit's before / after lists and masks are mostly zeros.
    /// </para>
    /// </summary>
    public static class EnvironmentMessages
    {
        public const ushort TerrainCellFamily = 0xEC, ConstructionFamily = 0xF4, ClaimsFamily = 0xF9, CommandFamily = 0x1E;

        // ---- the client's ids -------------------------------------------------------------------------------------------
        /// <summary>0xEC/1 ClientCellRequest (zone → env): fetch CellsUpdate bodies. Payload: bool always, cells.
        /// Answer: u32 n × {u64 cell, blob (empty = nothing there)} in the asked order.</summary>
        public const uint CellFetchSub = 0x1;
        /// <summary>0xEC/0xB ModifiedCellManifestUpdate (env → zone): <see cref="CellVersionsChanged"/>.</summary>
        public const uint CellVersionsChangedSub = 0xB;
        /// <summary>0xF4/1 ApplyDiffRequest: <see cref="EnvApplyEdit"/>.</summary>
        public const uint ApplyDiffSub = 0x1;
        /// <summary>0xF4/4 ClientOwnershipRequest: <see cref="EnvOwnership"/>.</summary>
        public const uint OwnershipSub = 0x4;
        /// <summary>0xF4/0x15 EnvServerResponse: every answer (summary).</summary>
        public const uint EnvServerResponseSub = 0x15;
        /// <summary>0xF4/0x18 SpawnExportedTemplateRequest: <see cref="EnvStamp"/>.</summary>
        public const uint SpawnTemplateSub = 0x18;
        /// <summary>0xF9/0x33 RevertTerrain: payload box (6 × f64 global dm). Answer: changed cells.</summary>
        public const uint RevertTerrainSub = 0x33;
        /// <summary>0x1E/0x4AB EnvServerDataMonitorUpdateRequest: <see cref="EnvMetrics"/>.</summary>
        public const uint MonitorRequestSub = 0x4AB;

        // ---- ours, TerrainCell family --------------------------------------------------------------------------------
        /// <summary>Zone → env first on every link: u32 world id, string name. Answer: <see cref="EnvIndex"/> + ore item map.</summary>
        public const uint HelloSub = 0x10000;
        /// <summary>Zone → env after a sequence gap: the whole version index again. Answer: <see cref="EnvIndex"/>.</summary>
        public const uint VersionIndexSub = 0x10001;
        /// <summary>Zone → env (no answer): <see cref="EnvKeepOut"/>, the build-site and no-vein boxes.</summary>
        public const uint KeepOutSub = 0x10002;
        /// <summary>Zone → env (no answer): the players' landscapes and positions for the vein regions: u32 n × {u32 zone, f32 × 3}.</summary>
        public const uint PositionsSub = 0x10003;
        public const uint SphereSub = 0x10010, DestroyAreaSub = 0x10011, HealAreaSub = 0x10012, ClearCellSub = 0x10013,
            RegenerationDelaySub = 0x10014, MarkPermanentSub = 0x10015, CompactSub = 0x10016, SampleVersionSub = 0x10017,
            MemoryReportSub = 0x10018, DescribeSub = 0x10019, RefreshLodSub = 0x1001A;
        public const uint EnsureVeinsSub = 0x10020, DisturbVeinsSub = 0x10021, ClearVeinsSub = 0x10022, VeinsNearSub = 0x10023;

        // ---- ours, VoxelConstruction family ----------------------------------------------------------------------------
        public const uint WriteCellsSub = 0x10010, ClearCellsSub = 0x10011, ReadSub = 0x10012, WriteSub = 0x10013,
            MaterialDeltaSub = 0x10014, OwnedBySub = 0x10015, CensusSub = 0x10016;
        /// <summary>
        /// Zone → env: the creature navigation's solidity grid of a box (OPEN-LEADS L133). Payload: box (6 × f64 global dm).
        /// Answer: bool has grid, then SolidityGrid (EnvServer.Core Terrain/SiteSolidity.cs: corner 3 × u64, u32 NX NY NZ,
        /// u32 cells / written / solid counts, written and solid bit sets packed).
        /// </summary>
        public const uint SolidityOfSub = 0x10017;

        /// <summary>Answer result: the request was not understood or failed in the service (the zone treats it as away).</summary>
        public const uint ResultFailed = 1;

        /// <summary>A request: u32 request id, u32 landscape, payload.</summary>
        public static ServicePacket Request(ushort family, uint sub, uint requestId, uint landscape, Action<InternalWriter> payload = null)
        {
            var w = new InternalWriter();
            w.U32(requestId).U32(landscape);
            payload?.Invoke(w);
            return new ServicePacket { Family = family, Sub = sub, Body = w.ToArray() };
        }

        public static (uint RequestId, uint Landscape, InternalReader Payload) ReadRequest(ServicePacket packet)
        {
            var r = new InternalReader(packet.Body);
            return (r.U32(), r.U32(), r);
        }

        public static bool IsReply(ServicePacket packet) => packet.Family == ConstructionFamily && packet.Sub == EnvServerResponseSub;

        /// <summary>An answer (EnvServerResponse 0xF4/0x15): u32 request id, u32 result, payload.</summary>
        public static ServicePacket Reply(uint requestId, uint result, Action<InternalWriter> payload = null)
        {
            var w = new InternalWriter();
            w.U32(requestId).U32(result);
            payload?.Invoke(w);
            return new ServicePacket { Family = ConstructionFamily, Sub = EnvServerResponseSub, Body = w.ToArray() };
        }

        public static (uint RequestId, uint Result, InternalReader Payload) ReadReply(ServicePacket packet)
        {
            var r = new InternalReader(packet.Body);
            return (r.U32(), r.U32(), r);
        }
    }

    /// <summary>A voxel value (TerrainEdits.VoxelValue): index in the 40³ cell, flags, material, three vertex offsets.</summary>
    public readonly record struct EnvVoxel(int Index, byte Flags, ushort Material, byte O0, byte O1, byte O2);

    /// <summary>Why a <see cref="CellVersionsChanged"/> was sent (ours): the zone acts on it differently.</summary>
    public enum EnvChangeReason : byte
    {
        /// <summary>This link's own request (the caller sends what it needs; index only).</summary>
        Request = 0,
        /// <summary>Another zone's request (the cells go to this zone's players: full data near, notes far).</summary>
        OtherZone = 1,
        /// <summary>The heal queue (job type 4): sent to the players in range (ZoneTerrain.Regenerate).</summary>
        Heal = 2,
        /// <summary>Ore veins spawned or moved (job types 15, 16): manifest notes (ZoneTerrain.Announce).</summary>
        Veins = 3,
        /// <summary>LOD copies refreshed (job types 0-3, 6): manifest notes (ZoneTerrain.LodCopiesChanged).</summary>
        Lod = 4,
        /// <summary>Found by the periodic comparison of the whole index (anything not reported otherwise).</summary>
        Reconcile = 5
    }

    /// <summary>
    /// CellVersionsChanged (env → zone; spec §5.2), ModifiedCellManifestUpdate 0xEC/0xB in a list: u32 landscape, u64
    /// sequence (one counter for the service; a gap → the zone fetches the whole index again), u8 reason
    /// (<see cref="EnvChangeReason"/>, ours), u32 n × {u64 cell, u32 layer code (1-4 = layers 0-3, the client's
    /// ModifiedCellManifestUpdate code, FUN_14309c1b0), u32 version (0 = the layer is gone), u8 flag}.
    /// </summary>
    public sealed class CellVersionsChanged
    {
        public uint Landscape { get; init; }
        public ulong Sequence { get; init; }
        public EnvChangeReason Reason { get; init; }
        public List<(ulong Cell, uint Code, uint Version, byte Flag)> Entries { get; init; } = new();

        public ServicePacket ToPacket()
        {
            var w = new InternalWriter();
            w.U32(Landscape).U64(Sequence).U8((byte)Reason).U32((uint)Entries.Count);
            foreach ((ulong cell, uint code, uint version, byte flag) in Entries)
                w.U64(cell).U32(code).U32(version).U8(flag);
            return new ServicePacket { Family = EnvironmentMessages.TerrainCellFamily, Sub = EnvironmentMessages.CellVersionsChangedSub, Body = w.ToArray() };
        }

        public static CellVersionsChanged Read(ServicePacket packet)
        {
            var r = new InternalReader(packet.Body);
            uint landscape = r.U32();
            ulong sequence = r.U64();
            var reason = (EnvChangeReason)r.U8();
            // Each entry is u64 cell + u32 code + u32 version + u8 flag = 17 bytes (21 refused every push of more than a
            // few entries: the zone's link dropped and resynced each time, found on the isolated stack 2026-10-16).
            uint n = EnvWire.Count(r, 17);
            var entries = new List<(ulong, uint, uint, byte)>((int)n);
            for (uint i = 0; i < n; i++)
                entries.Add((r.U64(), r.U32(), r.U32(), r.U8()));
            return new CellVersionsChanged { Landscape = landscape, Sequence = sequence, Reason = reason, Entries = entries };
        }
    }

    /// <summary>
    /// The version index (spec §5.3 item 1), answer to Hello / VersionIndex: u64 sequence, u32 landscapes × {u32 landscape,
    /// u32 n × {u64 cell, u8 layer, u32 version}} — every (cell, layer) the service holds with its version: edits, ore vein
    /// cells (layer 2), LOD copies (other levels). Hello's answer adds u32 n × {u16 ore material, u32 raw resource item}.
    /// </summary>
    public sealed class EnvIndex
    {
        public ulong Sequence { get; init; }
        public Dictionary<uint, List<(ulong Cell, byte Layer, uint Version)>> Landscapes { get; init; } = new();
        public List<(ushort Material, uint Item)> OreItems { get; init; } = new();

        public void Write(InternalWriter w, bool withOre)
        {
            w.U64(Sequence).U32((uint)Landscapes.Count);
            foreach ((uint landscape, var entries) in Landscapes)
            {
                w.U32(landscape).U32((uint)entries.Count);
                foreach ((ulong cell, byte layer, uint version) in entries)
                    w.U64(cell).U8(layer).U32(version);
            }
            if (!withOre)
                return;
            w.U32((uint)OreItems.Count);
            foreach ((ushort material, uint item) in OreItems)
                w.U16(material).U32(item);
        }

        public static EnvIndex Read(InternalReader r, bool withOre)
        {
            ulong sequence = r.U64();
            var landscapes = new Dictionary<uint, List<(ulong, byte, uint)>>();
            for (uint n = EnvWire.Count(r, 8); n > 0; n--)
            {
                uint landscape = r.U32();
                uint count = EnvWire.Count(r, 13);
                var entries = new List<(ulong, byte, uint)>((int)count);
                for (uint i = 0; i < count; i++)
                    entries.Add((r.U64(), r.U8(), r.U32()));
                landscapes[landscape] = entries;
            }
            var ore = new List<(ushort, uint)>();
            if (withOre)
                for (uint n = EnvWire.Count(r, 6); n > 0; n--)
                    ore.Add((r.U16(), r.U32()));
            return new EnvIndex { Sequence = sequence, Landscapes = landscapes, OreItems = ore };
        }
    }

    /// <summary>
    /// A material budget (spec §5.2; MaterialBudget): u32 n × {u32 item, u32 held, i32 credit voxels}.
    /// </summary>
    public sealed class EnvBudget
    {
        public List<(uint Item, uint Held, int Credit)> Items { get; init; } = new();

        public void Write(InternalWriter w)
        {
            w.U32((uint)Items.Count);
            foreach ((uint item, uint held, int credit) in Items)
                w.U32(item).U32(held).U32((uint)credit);
        }

        public static EnvBudget Read(InternalReader r)
        {
            var items = new List<(uint, uint, int)>();
            for (uint n = EnvWire.Count(r, 12); n > 0; n--)
                items.Add((r.U32(), r.U32(), (int)r.U32()));
            return new EnvBudget { Items = items };
        }
    }

    /// <summary>
    /// ApplyDiffRequest 0xF4/1 (zone → env; spec §5.2, the decision in §5.5): the zone has read the client's request and
    /// checked every cell's permission. Payload: u64 player, bool compare (dev "cellcompare"), bool budget + <see cref="EnvBudget"/>,
    /// u32 n × {u64 cell, bool versions, u32 base version, u32 new version, blob = the client's packed cell exactly as it
    /// came (block codec around the 8000-byte mask + SerializeBlockData format 2; not re-encoded, spec §8)}.
    /// Answer (result 0 / 0xC): delta u32 n × {u16 material, i32 voxels}, u32 n × u64 stale cells, i32 voxels written,
    /// string compare report, u32 n × {u64 cell, packed voxels before, packed voxels after}.
    /// </summary>
    public sealed class EnvApplyEdit
    {
        public ulong Owner { get; init; }
        public bool Compare { get; init; }
        public EnvBudget Budget { get; init; }
        public List<(ulong Cell, bool HasVersions, uint Base, uint New, byte[] Packed)> Cells { get; init; } = new();

        public void Write(InternalWriter w)
        {
            w.U64(Owner).Bool(Compare).Bool(Budget != null);
            Budget?.Write(w);
            w.U32((uint)Cells.Count);
            foreach ((ulong cell, bool has, uint b, uint n, byte[] packed) in Cells)
            {
                w.U64(cell).Bool(has).U32(b).U32(n);
                EnvWire.Blob(w, packed);
            }
        }

        public static EnvApplyEdit Read(InternalReader r)
        {
            ulong owner = r.U64();
            bool compare = r.Bool();
            EnvBudget budget = r.Bool() ? EnvBudget.Read(r) : null;
            var cells = new List<(ulong, bool, uint, uint, byte[])>();
            for (uint n = EnvWire.Count(r, 21); n > 0; n--)
                cells.Add((r.U64(), r.Bool(), r.U32(), r.U32(), EnvWire.Blob(r)));
            return new EnvApplyEdit { Owner = owner, Compare = compare, Budget = budget, Cells = cells };
        }

        public sealed class Answer
        {
            public List<(ushort Material, int Voxels)> Delta { get; init; } = new();
            public List<ulong> Stale { get; init; } = new();
            public int Voxels { get; init; }
            public string CompareReport { get; init; } = "";
            public List<(ulong Cell, List<EnvVoxel> Before, List<EnvVoxel> After)> Cells { get; init; } = new();

            public void Write(InternalWriter w)
            {
                EnvWire.Delta(w, Delta);
                EnvWire.Cells(w, Stale);
                w.U32((uint)Voxels).Str(CompareReport ?? "");
                EnvWire.CellValues(w, Cells);
            }

            public static Answer Read(InternalReader r) => new()
            {
                Delta = EnvWire.Delta(r), Stale = EnvWire.Cells(r), Voxels = (int)r.U32(), CompareReport = r.Str(), Cells = EnvWire.CellValues(r)
            };
        }
    }

    /// <summary>
    /// WriteCells 0xF4/0x10010 (ours, zone → env): voxel values written to several cells as one operation — undo / redo
    /// (job type 9; the undo history stays in the zone, which also holds the props a paste placed) and paste / design
    /// placement (11). Payload: u64 player, u8 job type, bool return values, bool budget + <see cref="EnvBudget"/>,
    /// u32 n × {u64 cell, packed voxels}. Answer (0 / 0xC): delta, u32 n × {u64 cell, packed before, packed after}.
    /// </summary>
    public sealed class EnvWriteCells
    {
        public ulong Owner { get; init; }
        public byte JobType { get; init; }
        public bool ReturnValues { get; init; }
        public EnvBudget Budget { get; init; }
        public List<(ulong Cell, List<EnvVoxel> Values)> Cells { get; init; } = new();

        public void Write(InternalWriter w)
        {
            w.U64(Owner).U8(JobType).Bool(ReturnValues).Bool(Budget != null);
            Budget?.Write(w);
            w.U32((uint)Cells.Count);
            foreach ((ulong cell, List<EnvVoxel> values) in Cells)
            {
                w.U64(cell);
                EnvWire.Voxels(w, values);
            }
        }

        public static EnvWriteCells Read(InternalReader r)
        {
            ulong owner = r.U64();
            byte job = r.U8();
            bool values = r.Bool();
            EnvBudget budget = r.Bool() ? EnvBudget.Read(r) : null;
            var cells = new List<(ulong, List<EnvVoxel>)>();
            for (uint n = EnvWire.Count(r, 16); n > 0; n--)
                cells.Add((r.U64(), EnvWire.Voxels(r)));
            return new EnvWriteCells { Owner = owner, JobType = job, ReturnValues = values, Budget = budget, Cells = cells };
        }
    }

    /// <summary>
    /// ClearCells 0xF4/0x10011 (ours, zone → env): the Heal tool. Payload: u64 player, u32 n × {u64 cell, packed mask}.
    /// Answer: delta (the refund, negative), i32 voxels, u32 n × {u64 cell, packed before, packed after}.
    /// </summary>
    public sealed class EnvClearCells
    {
        public ulong Owner { get; init; }
        public List<(ulong Cell, byte[] Mask)> Cells { get; init; } = new();

        public void Write(InternalWriter w)
        {
            w.U64(Owner).U32((uint)Cells.Count);
            foreach ((ulong cell, byte[] mask) in Cells)
            {
                w.U64(cell);
                EnvWire.Packed(w, mask);
            }
        }

        public static EnvClearCells Read(InternalReader r)
        {
            ulong owner = r.U64();
            var cells = new List<(ulong, byte[])>();
            for (uint n = EnvWire.Count(r, 16); n > 0; n--)
                cells.Add((r.U64(), EnvWire.Packed(r)));
            return new EnvClearCells { Owner = owner, Cells = cells };
        }
    }

    /// <summary>
    /// Which voxels a destruction may change (VoxelFilter; spec §5.5): u32 regions × {6 × f64 box (global dm), bool allowed,
    /// u32 areas × {f64 x, y, z, radius, half height, bool allow}}. No filter = u32 0.
    /// </summary>
    public sealed class EnvFilter
    {
        public List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ, bool Allowed,
            List<(double X, double Y, double Z, double Radius, double HalfHeight, bool Allow)> Areas)> Regions { get; init; } = new();

        public void Write(InternalWriter w)
        {
            w.U32((uint)Regions.Count);
            foreach (var region in Regions)
            {
                EnvWire.F64(w, region.MinX); EnvWire.F64(w, region.MinY); EnvWire.F64(w, region.MinZ);
                EnvWire.F64(w, region.MaxX); EnvWire.F64(w, region.MaxY); EnvWire.F64(w, region.MaxZ);
                w.Bool(region.Allowed).U32((uint)region.Areas.Count);
                foreach (var a in region.Areas)
                {
                    EnvWire.F64(w, a.X); EnvWire.F64(w, a.Y); EnvWire.F64(w, a.Z); EnvWire.F64(w, a.Radius); EnvWire.F64(w, a.HalfHeight);
                    w.Bool(a.Allow);
                }
            }
        }

        public static EnvFilter Read(InternalReader r)
        {
            var filter = new EnvFilter();
            for (uint n = EnvWire.Count(r, 53); n > 0; n--)
            {
                double minX = EnvWire.F64(r), minY = EnvWire.F64(r), minZ = EnvWire.F64(r), maxX = EnvWire.F64(r), maxY = EnvWire.F64(r),
                    maxZ = EnvWire.F64(r);
                bool allowed = r.Bool();
                var areas = new List<(double, double, double, double, double, bool)>();
                for (uint m = EnvWire.Count(r, 41); m > 0; m--)
                    areas.Add((EnvWire.F64(r), EnvWire.F64(r), EnvWire.F64(r), EnvWire.F64(r), EnvWire.F64(r), r.Bool()));
                filter.Regions.Add((minX, minY, minZ, maxX, maxY, maxZ, allowed, areas));
            }
            return filter;
        }
    }

    /// <summary>
    /// A dig / fill sphere 0xEC/0x10010 (ours; spec "TerrainDigRequest"): f32 × 3 local position, f64 radius, u16 material
    /// (0 = carve), bool shell + u16 shell material, <see cref="EnvFilter"/>, bool carve builds, bool heal delay + f64
    /// seconds, bool count ore. Answer: changed cells, i32 voxels, ore mined u32 n × {u16 material, i32 voxels, u32 raw
    /// resource item (OreVeins.ItemFor, so the zone needs no second call)}, ore cells.
    /// </summary>
    public sealed class EnvSphere
    {
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public double Radius { get; init; }
        public ushort Material { get; init; }
        public ushort? Shell { get; init; }
        public EnvFilter Filter { get; init; }
        public bool CarveBuilds { get; init; }
        public double? HealSeconds { get; init; }
        public bool CountOre { get; init; }

        public void Write(InternalWriter w)
        {
            w.F32(X).F32(Y).F32(Z);
            EnvWire.F64(w, Radius);
            w.U16(Material).Bool(Shell.HasValue).U16(Shell ?? 0);
            (Filter ?? new EnvFilter()).Write(w);
            w.Bool(CarveBuilds).Bool(HealSeconds.HasValue);
            EnvWire.F64(w, HealSeconds ?? 0);
            w.Bool(CountOre);
        }

        public static EnvSphere Read(InternalReader r)
        {
            float x = r.F32(), y = r.F32(), z = r.F32();
            double radius = EnvWire.F64(r);
            ushort material = r.U16();
            bool hasShell = r.Bool();
            ushort shell = r.U16();
            EnvFilter filter = EnvFilter.Read(r);
            bool carve = r.Bool(), hasHeal = r.Bool();
            double heal = EnvWire.F64(r);
            return new EnvSphere
            {
                X = x, Y = y, Z = z, Radius = radius, Material = material, Shell = hasShell ? shell : null,
                Filter = filter.Regions.Count == 0 ? null : filter, CarveBuilds = carve, HealSeconds = hasHeal ? heal : null, CountOre = r.Bool()
            };
        }
    }

    /// <summary>
    /// SpawnExportedTemplateRequest 0xF4/0x18 (zone → env; spec §4.5): u8 mode (0 = corner at a level-2 cell, Stamp;
    /// 1 = corner at a global voxel, StampAt), i64 × 3 corner, u8 source (1 = file name in the game's templates folder,
    /// 2 = inline minimal .otd, VoxelTemplate.CellsAsOtd), string name, blob otd; mode 2 (corner at a global voxel, turned
    /// in its box: the cavern graveyards) adds u8 quarter turns at the end. Answer: cells, changed cells, string stamp
    /// report.
    /// </summary>
    public sealed class EnvStamp
    {
        public const byte CellCorner = 0, VoxelCorner = 1, VoxelCornerTurned = 2, FromFile = 1, Inline = 2;
        public byte Mode { get; init; }
        public long X { get; init; }
        public long Y { get; init; }
        public long Z { get; init; }
        public byte Source { get; init; }
        public string Name { get; init; } = "";
        public byte[] Otd { get; init; } = Array.Empty<byte>();
        /// <summary>Quarter turns about +y (mode <see cref="VoxelCornerTurned"/> only).</summary>
        public byte Turns { get; init; }

        public void Write(InternalWriter w)
        {
            w.U8(Mode).U64((ulong)X).U64((ulong)Y).U64((ulong)Z).U8(Source).Str(Name);
            EnvWire.Blob(w, Otd);
            if (Mode == VoxelCornerTurned)
                w.U8(Turns);
        }

        public static EnvStamp Read(InternalReader r)
        {
            byte mode = r.U8();
            long x = (long)r.U64(), y = (long)r.U64(), z = (long)r.U64();
            byte source = r.U8();
            string name = r.Str();
            byte[] otd = EnvWire.Blob(r);
            return new EnvStamp
            {
                Mode = mode, X = x, Y = y, Z = z, Source = source, Name = name, Otd = otd,
                Turns = mode == VoxelCornerTurned ? r.U8() : (byte)0
            };
        }
    }

    /// <summary>
    /// ClientOwnershipRequest 0xF4/4 (zone → env; Voxel Vision, job type 10): u32 n × {u64 cell, u32 cached version}.
    /// Answer: up-to-date cells, u32 n × {u64 cell, u32 version, bool grid + blob raw grid, u32 n × u64 owners}.
    /// </summary>
    public static class EnvOwnership
    {
        public static void WriteAnswer(InternalWriter w, List<ulong> upToDate, List<(ulong Cell, uint Version, byte[] Raw, List<ulong> Owners)> data)
        {
            EnvWire.Cells(w, upToDate);
            w.U32((uint)data.Count);
            foreach ((ulong cell, uint version, byte[] raw, List<ulong> owners) in data)
            {
                w.U64(cell).U32(version).Bool(raw != null);
                EnvWire.Blob(w, raw);
                EnvWire.Cells(w, owners);
            }
        }

        public static (List<ulong> UpToDate, List<(ulong Cell, uint Version, byte[] Raw, List<ulong> Owners)> Data) ReadAnswer(InternalReader r)
        {
            List<ulong> upToDate = EnvWire.Cells(r);
            var data = new List<(ulong, uint, byte[], List<ulong>)>();
            for (uint n = EnvWire.Count(r, 21); n > 0; n--)
            {
                ulong cell = r.U64();
                uint version = r.U32();
                bool has = r.Bool();
                byte[] raw = EnvWire.Blob(r);
                data.Add((cell, version, has ? raw : null, EnvWire.Cells(r)));
            }
            return (upToDate, data);
        }
    }

    /// <summary>
    /// Ore veins near a point 0xEC/0x10023 (ours; spec "VeinQuery"): f32 × 3 local position, f32 radius. Answer: u32 n ×
    /// {i64 vein id, u32 mapping (ResourceVeinGroupVeins id), u32 item, string kind name, bool cave, i32 × 2 region,
    /// u32 n × point {f64 x, y, z, width, u32 seed}, cells, f32 × 3 nearest point (local), f32 distance}, nearest first.
    /// </summary>
    public sealed record EnvVein(long Id, uint Mapping, uint Item, string Name, bool Cave, int RegionX, int RegionZ,
        List<(double X, double Y, double Z, double Width, uint Seed)> Points, List<ulong> Cells, float NearX, float NearY, float NearZ, float Distance)
    {
        public void Write(InternalWriter w)
        {
            w.U64((ulong)Id).U32(Mapping).U32(Item).Str(Name ?? "").Bool(Cave).U32((uint)RegionX).U32((uint)RegionZ).U32((uint)Points.Count);
            foreach (var p in Points)
            {
                EnvWire.F64(w, p.X); EnvWire.F64(w, p.Y); EnvWire.F64(w, p.Z); EnvWire.F64(w, p.Width);
                w.U32(p.Seed);
            }
            EnvWire.Cells(w, Cells);
            w.F32(NearX).F32(NearY).F32(NearZ).F32(Distance);
        }

        public static EnvVein Read(InternalReader r)
        {
            long id = (long)r.U64();
            uint mapping = r.U32(), item = r.U32();
            string name = r.Str();
            bool cave = r.Bool();
            int rx = (int)r.U32(), rz = (int)r.U32();
            var points = new List<(double, double, double, double, uint)>();
            for (uint n = EnvWire.Count(r, 36); n > 0; n--)
                points.Add((EnvWire.F64(r), EnvWire.F64(r), EnvWire.F64(r), EnvWire.F64(r), r.U32()));
            List<ulong> cells = EnvWire.Cells(r);
            return new EnvVein(id, mapping, item, name, cave, rx, rz, points, cells, r.F32(), r.F32(), r.F32(), r.F32());
        }
    }

    /// <summary>
    /// The zone's keep-out boxes 0xEC/0x10002 (ours; spec §5.2 ZoneAddAllClaims / BlockVeins in one snapshot): u32
    /// landscapes × {u32 landscape, u32 n × site box, u32 n × no-vein box} (6 × f64 global dm each). Build sites: the heal
    /// and the layer move keep their voxels and veins keep out (Claims.BoxesIn in-process); no-vein boxes: the Spire
    /// platforms (OreVeins.NoVeins, P5797). Sent whole on every link and whenever it changes (checked every few seconds).
    /// </summary>
    public sealed class EnvKeepOut
    {
        public Dictionary<uint, (List<double[]> Sites, List<double[]> NoVeins)> Landscapes { get; init; } = new();

        public void Write(InternalWriter w)
        {
            w.U32((uint)Landscapes.Count);
            foreach ((uint landscape, (List<double[]> sites, List<double[]> noVeins)) in Landscapes)
            {
                w.U32(landscape);
                EnvWire.Boxes(w, sites);
                EnvWire.Boxes(w, noVeins);
            }
        }

        public static EnvKeepOut Read(InternalReader r)
        {
            var keepOut = new EnvKeepOut();
            for (uint n = EnvWire.Count(r, 12); n > 0; n--)
            {
                uint landscape = r.U32();
                keepOut.Landscapes[landscape] = (EnvWire.Boxes(r), EnvWire.Boxes(r));
            }
            return keepOut;
        }
    }

    /// <summary>
    /// The Environment Server's job counters (answer to 0x1E/0x4AB, spec §1.1 / §5.2): the client's
    /// ClientEnvironmentServerMetrics layout as the monitor page FUN_14320ea00 prints it — header "Pending" then "Active":
    /// u32 19 × pending (+0x20), u32 19 × active (+0x6C), "Total Jobs" u32 pending total (+0xB8) and u32 active total
    /// (+0xBC) — current counts, not finished jobs (trace/0695/agent-leads2-d2.md L128) [P layout] — then u32 n × ruin
    /// layer {u32 desired, u32 active, u32 failed, i32 last id}.
    /// </summary>
    public sealed class EnvMetrics
    {
        public const int JobTypes = 19;
        public uint[] Active { get; init; } = new uint[JobTypes];
        public uint[] Pending { get; init; } = new uint[JobTypes];
        public uint PendingTotal => (uint)System.Linq.Enumerable.Sum(Pending, v => (long)v);
        public uint ActiveTotal => (uint)System.Linq.Enumerable.Sum(Active, v => (long)v);
        public List<(uint Desired, uint Active, uint Failed, int LastId)> RuinLayers { get; init; } = new();

        public void Write(InternalWriter w)
        {
            for (int i = 0; i < JobTypes; i++)
                w.U32(Pending[i]);
            for (int i = 0; i < JobTypes; i++)
                w.U32(Active[i]);
            w.U32(PendingTotal).U32(ActiveTotal).U32((uint)RuinLayers.Count);
            foreach ((uint desired, uint active, uint failed, int last) in RuinLayers)
                w.U32(desired).U32(active).U32(failed).U32((uint)last);
        }

        public static EnvMetrics Read(InternalReader r)
        {
            var m = new EnvMetrics();
            for (int i = 0; i < JobTypes; i++)
                m.Pending[i] = r.U32();
            for (int i = 0; i < JobTypes; i++)
                m.Active[i] = r.U32();
            var layers = new List<(uint, uint, uint, int)>();
            r.U32();    // pending total
            r.U32();    // active total
            for (uint n = EnvWire.Count(r, 16); n > 0; n--)
                layers.Add((r.U32(), r.U32(), r.U32(), (int)r.U32()));
            return new EnvMetrics { Active = m.Active, Pending = m.Pending, RuinLayers = layers };
        }
    }

    /// <summary>Field codecs shared by the Environment Server messages.</summary>
    public static class EnvWire
    {
        /// <summary>The largest blob or deflated list accepted (the link's frame limit is 16 MB).</summary>
        public const int MaxBlob = 15 * 1024 * 1024;

        /// <summary>A list count, refused when the rest of the packet can't hold that many items of <paramref name="minSize"/> bytes.</summary>
        public static uint Count(InternalReader r, int minSize)
        {
            uint n = r.U32();
            if (minSize > 0 && n > r.Remaining / minSize + 1)
                throw new InvalidDataException($"{n} entries cannot fit in {r.Remaining} bytes.");
            return n;
        }

        public static void F64(InternalWriter w, double v) => w.U64(BitConverter.DoubleToUInt64Bits(v));
        public static double F64(InternalReader r) => BitConverter.UInt64BitsToDouble(r.U64());

        /// <summary>u32 length + bytes (a null blob is written empty).</summary>
        public static void Blob(InternalWriter w, byte[] data)
        {
            data ??= Array.Empty<byte>();
            w.U32((uint)data.Length).Bytes(data);
        }

        public static byte[] Blob(InternalReader r)
        {
            uint n = r.U32();
            if (n > MaxBlob)
                throw new InvalidDataException($"{n}-byte blob refused.");
            return r.Bytes((int)n);
        }

        /// <summary>Deflated bytes: u32 raw length, u32 n + deflate data.</summary>
        public static void Packed(InternalWriter w, byte[] raw)
        {
            raw ??= Array.Empty<byte>();
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
                deflate.Write(raw, 0, raw.Length);
            w.U32((uint)raw.Length);
            Blob(w, output.ToArray());
        }

        public static byte[] Packed(InternalReader r)
        {
            uint length = r.U32();
            if (length > 64 * 1024 * 1024)
                throw new InvalidDataException($"{length}-byte packed list refused.");
            byte[] packed = Blob(r);
            var raw = new byte[length];
            using var deflate = new DeflateStream(new MemoryStream(packed), CompressionMode.Decompress);
            deflate.ReadExactly(raw);
            return raw;
        }

        public static void Cells(InternalWriter w, IReadOnlyCollection<ulong> cells)
        {
            cells ??= Array.Empty<ulong>();
            w.U32((uint)cells.Count);
            foreach (ulong cell in cells)
                w.U64(cell);
        }

        public static List<ulong> Cells(InternalReader r)
        {
            uint n = Count(r, 8);
            var cells = new List<ulong>((int)n);
            for (uint i = 0; i < n; i++)
                cells.Add(r.U64());
            return cells;
        }

        /// <summary>A material difference: u32 n × {u16 material, i32 voxels}.</summary>
        public static void Delta(InternalWriter w, IReadOnlyCollection<(ushort Material, int Voxels)> delta)
        {
            w.U32((uint)delta.Count);
            foreach ((ushort material, int voxels) in delta)
                w.U16(material).U32((uint)voxels);
        }

        public static List<(ushort Material, int Voxels)> Delta(InternalReader r)
        {
            var delta = new List<(ushort, int)>();
            for (uint n = Count(r, 6); n > 0; n--)
                delta.Add((r.U16(), (int)r.U32()));
            return delta;
        }

        /// <summary>A voxel value list, deflated: 10 bytes per value {i32 index, u8 flags, u16 material, u8 × 3 offsets}.</summary>
        public static void Voxels(InternalWriter w, IReadOnlyCollection<EnvVoxel> values)
        {
            values ??= Array.Empty<EnvVoxel>();
            var raw = new byte[values.Count * 10];
            int p = 0;
            foreach (EnvVoxel v in values)
            {
                BitConverter.TryWriteBytes(raw.AsSpan(p), v.Index);
                raw[p + 4] = v.Flags;
                BitConverter.TryWriteBytes(raw.AsSpan(p + 5), v.Material);
                raw[p + 7] = v.O0;
                raw[p + 8] = v.O1;
                raw[p + 9] = v.O2;
                p += 10;
            }
            Packed(w, raw);
        }

        public static List<EnvVoxel> Voxels(InternalReader r)
        {
            byte[] raw = Packed(r);
            if (raw.Length % 10 != 0)
                throw new InvalidDataException($"Voxel list of {raw.Length} bytes.");
            var values = new List<EnvVoxel>(raw.Length / 10);
            for (int p = 0; p < raw.Length; p += 10)
                values.Add(new EnvVoxel(BitConverter.ToInt32(raw, p), raw[p + 4], BitConverter.ToUInt16(raw, p + 5), raw[p + 7], raw[p + 8], raw[p + 9]));
            return values;
        }

        /// <summary>u32 n × {u64 cell, voxels before, voxels after}.</summary>
        public static void CellValues(InternalWriter w, IReadOnlyCollection<(ulong Cell, List<EnvVoxel> Before, List<EnvVoxel> After)> cells)
        {
            w.U32((uint)cells.Count);
            foreach ((ulong cell, List<EnvVoxel> before, List<EnvVoxel> after) in cells)
            {
                w.U64(cell);
                Voxels(w, before);
                Voxels(w, after);
            }
        }

        public static List<(ulong Cell, List<EnvVoxel> Before, List<EnvVoxel> After)> CellValues(InternalReader r)
        {
            var cells = new List<(ulong, List<EnvVoxel>, List<EnvVoxel>)>();
            for (uint n = Count(r, 24); n > 0; n--)
                cells.Add((r.U64(), Voxels(r), Voxels(r)));
            return cells;
        }

        /// <summary>A box: 6 × f64 (min x, y, z, max x, y, z), global dm.</summary>
        public static void Box(InternalWriter w, double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            F64(w, minX); F64(w, minY); F64(w, minZ); F64(w, maxX); F64(w, maxY); F64(w, maxZ);
        }

        public static double[] Box(InternalReader r) => new[] { F64(r), F64(r), F64(r), F64(r), F64(r), F64(r) };

        public static void Boxes(InternalWriter w, IReadOnlyCollection<double[]> boxes)
        {
            w.U32((uint)boxes.Count);
            foreach (double[] b in boxes)
                Box(w, b[0], b[1], b[2], b[3], b[4], b[5]);
        }

        public static List<double[]> Boxes(InternalReader r)
        {
            var boxes = new List<double[]>();
            for (uint n = Count(r, 48); n > 0; n--)
                boxes.Add(Box(r));
            return boxes;
        }
    }
}
