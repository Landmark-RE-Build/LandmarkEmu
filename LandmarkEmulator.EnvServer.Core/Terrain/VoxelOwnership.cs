using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// Who built each voxel — the data behind Voxel Vision (site information window → Voxel Vision: the list of
    /// builders on a site, each one's voxel count and share, and their voxels highlighted in their colour; wiki "Voxel
    /// Vision"). Every voxel a player's edit leaves solid is recorded as theirs; air, heals, digs and regrowth clear it.
    /// <para>
    /// Client flow (0.589): the widget calls Lua VoxelOwnership.RequestAllCellsForClaim(claim) (FUN_14354c980), which
    /// works out the site's level-2 cells itself and sends VoxelConstruction ClientOwnershipRequest (0xF4 sub 4, built
    /// in FUN_143086450): {u32 n, n × {u64 cell, u32 cached version (0xFFFFFFFF = none)}}. Every cell must be answered
    /// before the request completes (FUN_143083510) and Lua VoxelVision:OnRequestAllCellsForClaimResponse runs:
    /// <list type="bullet">
    /// <item>EnvOwnershipUpToDateResponse (sub 0x16, reader FUN_1430b3060, handler FUN_1430839c0): {u32 n, u64 cells}
    /// — the client's cached copy is current.</item>
    /// <item>EnvOwnershipDataResponse (sub 0x17, reader FUN_1430b0ed0, handler FUN_143083700): {u64 (unused by the
    /// handler), u64 cell, u32 n + n bytes grid blob, u32 n + names, u32 n + u64 character guids, u32 n + i64 (unused),
    /// u32 version}. Names and guids pair up into the cell's owner list; an empty blob means nobody owns anything.</item>
    /// </list>
    /// The grid blob is the client's packed codec (A1 B2 C3 D4, BE raw size, zlib) around FUN_14307ee80's format:
    /// u8 version 4, u64, bool false (no i64 list), u32 n + n × u16 (0 here), then the 40³ owner indices in 64 chunks of
    /// 10³ (chunk index = z/10·16 + x/10·4 + y/10): an 8-byte chunk mask, a 125-byte voxel mask per present chunk (bit
    /// = dz·100 + dx·10 + dy), then one byte per set voxel (FUN_14307e800; two bytes only with ≥ 255 palette entries).
    /// Both masks are MSB first. The value is the voxel's index into the owner list; 0 is nobody, so the list starts with
    /// a blank entry (the builder rows, FUN_143603e50, skip index 0).
    /// </para>
    /// </summary>
    public static class VoxelOwnership
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte RequestSub = 4, UpToDateSub = 0x16, DataSub = 0x17;

        private sealed class Grid
        {
            /// <summary>Per voxel (TerrainEdits.VoxelIndex order): 0 = nobody, else 1 + index into <see cref="Owners"/>.</summary>
            public byte[] Owner { get; } = new byte[BlockData.Count];
            public List<ulong> Owners { get; } = new();
            public uint Version { get; set; } = 1;

            /// <summary>
            /// Changed since its last save. Set only by <see cref="SetDirty"/> (through MarkDirty / ClearDirty), which keeps
            /// the <see cref="dirty"/> index in step: a grid flagged without the index would never be saved.
            /// </summary>
            public bool Dirty { get; private set; }

            /// <summary>Flags the grid and updates <see cref="dirty"/> together (caller holds <see cref="sync"/>).</summary>
            public void SetDirty(uint zone, ulong id, bool value)
            {
                Dirty = value;
                if (value)
                    dirty.Add((zone, id));
                else
                    dirty.Remove((zone, id));
            }
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Dictionary<ulong, Grid>> stores = new();
        /// <summary>
        /// Grids with <see cref="Grid.Dirty"/> set. <see cref="Flush"/> walks this instead of every ownership cell.
        /// </summary>
        private static readonly HashSet<(uint Zone, ulong Id)> dirty = new();
        private static string folder;

        private static uint Zone(uint zone) => zone == 0 ? TerrainStore.HomeContinent : zone;

        private static Dictionary<ulong, Grid> Of(uint zone)
        {
            zone = Zone(zone);
            if (!stores.TryGetValue(zone, out var store))
                stores[zone] = store = new();
            return store;
        }

        /// <summary>How many grids are waiting for <see cref="Flush"/> (tests).</summary>
        public static int DirtyCount
        {
            get { lock (sync) return dirty.Count; }
        }

        /// <summary>Caller holds <see cref="sync"/>.</summary>
        private static void MarkDirty(uint zone, ulong id, Grid grid)
        {
            grid.SetDirty(Zone(zone), id, true);
        }

        /// <summary>Caller holds <see cref="sync"/>.</summary>
        private static void ClearDirty(uint zone, ulong id, Grid grid)
        {
            grid.SetDirty(Zone(zone), id, false);
        }

        /// <summary>
        /// The voxels one player owns on a landscape, per cell (voxel indices in wire order), limited to those
        /// <paramref name="keep"/> accepts — a site design's contents (<see cref="SiteDesigns"/>).
        /// </summary>
        public static List<(ulong Cell, List<int> Indices)> OwnedBy(uint zone, ulong owner, Func<ulong, int, bool> keep)
        {
            var result = new List<(ulong, List<int>)>();
            lock (sync)
                foreach ((ulong cell, Grid grid) in Of(zone))
                {
                    int slot = grid.Owners.IndexOf(owner);
                    if (slot < 0)
                        continue;
                    byte mark = (byte)(slot + 1);
                    var indices = new List<int>();
                    for (int i = 0; i < grid.Owner.Length; i++)
                        if (grid.Owner[i] == mark && keep(cell, i))
                            indices.Add(i);
                    if (indices.Count > 0)
                        result.Add((cell, indices));
                }
            return result;
        }

        /// <summary>Loads the saved grids: &lt;terrain edits folder&gt;/Ownership/&lt;zone&gt;/&lt;cell&gt;.own.</summary>
        public static void Load(string terrainEditsFolder)
        {
            folder = Path.Combine(terrainEditsFolder, "Ownership");
            Directory.CreateDirectory(folder);
            int count = 0;
            foreach (string zoneFolder in Directory.GetDirectories(folder))
            {
                if (!uint.TryParse(Path.GetFileName(zoneFolder), out uint zone))
                    continue;
                foreach (string file in Directory.GetFiles(zoneFolder, "*.own"))
                {
                    try
                    {
                        ulong id = Convert.ToUInt64(Path.GetFileNameWithoutExtension(file), 16);
                        using var reader = new BinaryReader(File.OpenRead(file));
                        var grid = new Grid { Version = reader.ReadUInt32() };
                        int owners = reader.ReadByte();
                        for (int i = 0; i < owners; i++)
                            grid.Owners.Add(reader.ReadUInt64());
                        int packed = reader.ReadInt32();
                        byte[] raw = TerrainStore.Unpack(reader.ReadBytes(packed), 0, packed);
                        raw.CopyTo(grid.Owner, 0);
                        Of(zone)[id] = grid;
                        count++;
                    }
                    catch (Exception e)
                    {
                        log.Error(e, $"Could not load voxel ownership {file}.");
                    }
                }
            }
            log.Info($"Loaded voxel ownership for {count} cell(s).");
        }

        /// <summary>The next "could not save" error logged (one a minute while saves fail), and the next try after a failure.</summary>
        private static DateTime nextWarning, retryAt;

        /// <summary>Writes changed grids (from the server tick with the terrain overlays).</summary>
        public static void Flush()
        {
            if (folder == null || DateTime.UtcNow < retryAt)
                return;
            var pending = new List<(uint Zone, ulong Id, byte[] Data)>();
            lock (sync)
            {
                if (dirty.Count == 0)
                    return;
                foreach ((uint zone, ulong id) in dirty.ToList())
                {
                    if (!Of(zone).TryGetValue(id, out Grid grid) || !grid.Dirty)
                    {
                        dirty.Remove((zone, id));
                        continue;
                    }
                    ClearDirty(zone, id, grid);
                    using var stream = new MemoryStream();
                    using var writer = new BinaryWriter(stream);
                    writer.Write(grid.Version);
                    writer.Write((byte)grid.Owners.Count);
                    foreach (ulong owner in grid.Owners)
                        writer.Write(owner);
                    byte[] packed = TerrainStore.Pack(grid.Owner);
                    writer.Write(packed.Length);
                    writer.Write(packed);
                    writer.Flush();
                    pending.Add((zone, id, stream.ToArray()));
                }
            }
            int failed = 0;
            string error = null;
            foreach ((uint zone, ulong id, byte[] data) in pending)
            {
                try
                {
                    // Written beside the file and moved over it: until 2026-10-18 the file itself was rewritten, so a crash
                    // or a full disk part-way left a cut-off .own file, which Load then skipped (the cell's builder
                    // ownership gone: anyone could take the voxels).
                    string zoneFolder = Path.Combine(folder, zone.ToString());
                    Directory.CreateDirectory(zoneFolder);
                    string file = Path.Combine(zoneFolder, $"{id:X16}.own");
                    File.WriteAllBytes(file + ".tmp", data);
                    File.Move(file + ".tmp", file, overwrite: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Not saved: the grid is marked dirty again so the next flush retries (it was marked clean above, and
                    // was otherwise only written again after its next change).
                    lock (sync)
                        if (Of(zone).TryGetValue(id, out Grid grid))
                            MarkDirty(zone, id, grid);
                    failed++;
                    error = e.Message;
                }
            }
            if (failed > 0)
                retryAt = DateTime.UtcNow.AddSeconds(5);
            if (failed > 0 && DateTime.UtcNow >= nextWarning)
            {
                nextWarning = DateTime.UtcNow.AddMinutes(1);
                log.Error($"Could not save voxel ownership of {failed} cell(s) ({error}); they are saved again on the next flush.");
            }
        }

        /// <summary>
        /// Records the voxels an edit changed: those left solid (<paramref name="solid"/>) become <paramref name="owner"/>'s,
        /// the rest (air, heals) nobody's. <paramref name="owner"/> 0 (digging, regrowth, site removal) clears them all.
        /// </summary>
        public static void Set(uint zone, ulong cell, IEnumerable<int> indices, Func<int, bool> solid, ulong owner)
        {
            lock (sync)
            {
                var store = Of(zone);
                store.TryGetValue(cell, out Grid grid);
                if (grid == null && owner == 0)
                    return;
                if (grid == null)
                    store[cell] = grid = new Grid();
                byte mine = 0;
                bool changed = false;
                foreach (int i in indices)
                {
                    byte value = 0;
                    if (owner != 0 && solid(i))
                    {
                        if (mine == 0)
                            mine = IndexOf(grid, owner);
                        value = mine;
                    }
                    if (value == 0 && grid.Owner[i] == 0)
                        continue;
                    if (grid.Owner[i] != value)
                    {
                        grid.Owner[i] = value;
                        changed = true;
                    }
                }
                if (changed)
                {
                    grid.Version++;
                    MarkDirty(zone, cell, grid);
                }
            }
        }

        /// <summary>Everything in a cell goes back to nobody (the cell's edits were cleared).</summary>
        public static void ClearCell(uint zone, ulong cell)
        {
            lock (sync)
                if (Of(zone).TryGetValue(cell, out Grid grid) && grid.Owner.Any(o => o != 0))
                {
                    Array.Clear(grid.Owner);
                    grid.Version++;
                    MarkDirty(zone, cell, grid);
                }
        }

        /// <summary>The 1-based owner-list index of a character in a cell, adding them (max 254 builders a cell).</summary>
        private static byte IndexOf(Grid grid, ulong owner)
        {
            int at = grid.Owners.IndexOf(owner);
            if (at < 0)
            {
                // A full list reuses the slot of a builder who no longer owns any voxel here.
                if (grid.Owners.Count >= 254)
                {
                    var used = new HashSet<byte>(grid.Owner);
                    at = Enumerable.Range(0, grid.Owners.Count).FirstOrDefault(i => !used.Contains((byte)(i + 1)), -1);
                    if (at < 0)
                        return 0;
                    grid.Owners[at] = owner;
                    return (byte)(at + 1);
                }
                grid.Owners.Add(owner);
                at = grid.Owners.Count - 1;
            }
            return (byte)(at + 1);
        }

        /// <summary>
        /// The store's half of a ClientOwnershipRequest (C→S 0xF4 sub 4; the zone reads the packet and sends the replies,
        /// ZoneTerrain.HandleOwnershipRequest): per listed cell, up to date when the client's cached version matches,
        /// otherwise the cell's version, a copy of its grid (null where nobody built) and its owner list.
        /// </summary>
        public static (List<ulong> UpToDate, List<(ulong Cell, uint Version, byte[] Raw, List<ulong> Owners)> Data) Query(uint zone,
            IReadOnlyList<(ulong Cell, uint Version)> cells)
        {
            var upToDate = new List<ulong>();
            var data = new List<(ulong Cell, uint Version, byte[] Raw, List<ulong> Owners)>();
            lock (sync)
            {
                var store = Of(zone);
                foreach ((ulong cell, uint cached) in cells)
                {
                    store.TryGetValue(cell, out Grid grid);
                    uint version = grid?.Version ?? 0;
                    if (cached == version)
                        upToDate.Add(cell);
                    else
                        data.Add((cell, version, grid != null && grid.Owner.Any(o => o != 0) ? (byte[])grid.Owner.Clone() : null,
                            grid?.Owners.ToList() ?? new List<ulong>()));
                }
            }
            return (upToDate, data);
        }

        /// <summary>The raw owner grid in the client's format (FUN_14307ee80, version 4).</summary>
        public static byte[] GridBytes(ulong cell, byte[] owner)
        {
            const int S = BlockData.Size, C = 10;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)4);
            writer.Write(cell);
            writer.Write(false);
            writer.Write(0);
            var chunkMask = new byte[8];
            var masks = new List<byte[]>();
            var values = new List<byte>();
            int chunk = 0;
            for (int z0 = 0; z0 < S; z0 += C)
                for (int x0 = 0; x0 < S; x0 += C)
                    for (int y0 = 0; y0 < S; y0 += C, chunk++)
                    {
                        var mask = new byte[125];
                        bool any = false;
                        int bit = 0;
                        for (int dz = 0; dz < C; dz++)
                            for (int dx = 0; dx < C; dx++)
                                for (int dy = 0; dy < C; dy++, bit++)
                                {
                                    byte o = owner[TerrainEdits.VoxelIndex(x0 + dx, y0 + dy, z0 + dz)];
                                    if (o == 0)
                                        continue;
                                    mask[bit >> 3] |= (byte)(1 << (7 - (bit & 7)));
                                    values.Add(o);
                                    any = true;
                                }
                        if (!any)
                            continue;
                        chunkMask[chunk >> 3] |= (byte)(1 << (7 - (chunk & 7)));
                        masks.Add(mask);
                    }
            writer.Write(chunkMask);
            foreach (byte[] mask in masks)
                writer.Write(mask);
            writer.Write(values.ToArray());
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>Builders of a cell and how many voxels each owns (dev / tests).</summary>
        public static Dictionary<ulong, int> Census(uint zone, ulong cell)
        {
            lock (sync)
            {
                var result = new Dictionary<ulong, int>();
                if (!Of(zone).TryGetValue(cell, out Grid grid))
                    return result;
                foreach (byte o in grid.Owner)
                    if (o != 0 && o <= grid.Owners.Count)
                        result[grid.Owners[o - 1]] = result.GetValueOrDefault(grid.Owners[o - 1]) + 1;
                return result;
            }
        }
    }
}
