using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
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
            public bool Dirty { get; set; }
        }

        private static readonly object sync = new();
        private static readonly Dictionary<uint, Dictionary<ulong, Grid>> stores = new();
        private static string folder;

        private static uint Zone(uint zone) => zone == 0 ? TerrainStore.HomeContinent : zone;

        private static Dictionary<ulong, Grid> Of(uint zone)
        {
            zone = Zone(zone);
            if (!stores.TryGetValue(zone, out var store))
                stores[zone] = store = new();
            return store;
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

        /// <summary>Writes changed grids (from the server tick with the terrain overlays).</summary>
        public static void Flush()
        {
            if (folder == null)
                return;
            var pending = new List<(uint Zone, ulong Id, byte[] Data)>();
            lock (sync)
                foreach ((uint zone, var store) in stores)
                    foreach ((ulong id, Grid grid) in store)
                    {
                        if (!grid.Dirty)
                            continue;
                        grid.Dirty = false;
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
            foreach ((uint zone, ulong id, byte[] data) in pending)
            {
                try
                {
                    string zoneFolder = Path.Combine(folder, zone.ToString());
                    Directory.CreateDirectory(zoneFolder);
                    File.WriteAllBytes(Path.Combine(zoneFolder, $"{id:X16}.own"), data);
                }
                catch (Exception e)
                {
                    log.Error(e, $"Could not save voxel ownership of cell {id:X16}.");
                }
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
                    grid.Dirty = true;
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
                    grid.Dirty = true;
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
        /// ClientOwnershipRequest (C→S 0xF4 sub 4): answers every listed cell — up to date when the client's cached
        /// version matches, otherwise the cell's grid (an empty one where nobody built).
        /// </summary>
        public static void HandleRequest(WorldSession session, byte[] body)
        {
            var cells = new List<(ulong Cell, uint Version)>();
            try
            {
                int p = 0;
                int n = BitConverter.ToInt32(body, p);
                p += 4;
                if (n < 0 || n > 20000 || p + n * 12 > body.Length)
                {
                    log.Warn($"{session.CharacterName}: voxel ownership request with {n} cell(s) in {body.Length} bytes: {BitConverter.ToString(body, 0, Math.Min(body.Length, 48))}");
                    return;
                }
                for (int i = 0; i < n; i++, p += 12)
                    cells.Add((BitConverter.ToUInt64(body, p), BitConverter.ToUInt32(body, p + 8)));
            }
            catch (ArgumentException)
            {
                return;
            }

            uint zone = session.ZoneId;
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

            var names = DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(data.SelectMany(d => d.Owners).Distinct());
            if (upToDate.Count > 0)
            {
                var output = new List<byte>();
                var writer = new GamePacketWriter(output);
                writer.Write(UpToDateSub);
                writer.Write(upToDate.Count);
                foreach (ulong cell in upToDate)
                    writer.Write(cell);
                session.EnqueueMessage(ZoneMessageOpcode.VoxelConstructionBase, output.ToArray());
            }
            foreach ((ulong cell, uint version, byte[] raw, List<ulong> owners) in data)
                session.EnqueueMessage(ZoneMessageOpcode.VoxelConstructionBase, BuildData(cell, version, raw, owners, names));
            log.Debug($"{session.CharacterName}: voxel ownership of {cells.Count} cell(s): {upToDate.Count} up to date, " +
                $"{data.Count} sent ({data.Count(d => d.Raw != null)} with builders).");
        }

        /// <summary>One cell's EnvOwnershipDataResponse (sub 0x17).</summary>
        private static byte[] BuildData(ulong cell, uint version, byte[] owner, List<ulong> owners, Dictionary<ulong, string> names)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(DataSub);
            writer.Write(0ul);
            writer.Write(cell);
            byte[] blob = owner == null ? Array.Empty<byte>() : TerrainStore.Pack(GridBytes(cell, owner));
            writer.Write(blob.Length);
            if (blob.Length > 0)
                writer.WriteBytes(blob);
            // Index 0 is "nobody" (never a voxel's owner).
            writer.Write(owners.Count + 1);
            writer.Write("");
            foreach (ulong guid in owners)
                writer.Write(names.GetValueOrDefault(guid, ""));
            writer.Write(owners.Count + 1);
            writer.Write(0ul);
            foreach (ulong guid in owners)
                writer.Write(guid);
            writer.Write(0);
            writer.Write(version);
            return output.ToArray();
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
