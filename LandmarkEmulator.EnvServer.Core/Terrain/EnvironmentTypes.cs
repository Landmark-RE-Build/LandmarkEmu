using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The player's building materials for one voxel operation (terrain-service.md §5.2 "material budget"; the decision in
    /// §5.5): what the zone knows about the player's bag, handed to the Environment Server with the edit so the service can
    /// refuse an unaffordable edit (Response 0xC InsufficientMaterials) before it writes anything. The zone charges exactly
    /// the material difference the service returns, with Building.PayFor, on the same world-thread turn.
    /// <para>
    /// Per item: <c>Held</c> = items in the bag, <c>Credit</c> = voxels already paid for but not built (Building's carry-over,
    /// ≥ 0). The check is PayFor's arithmetic: per item, owed = voxels − credit; items needed = ⌈owed / VoxelsPerItem⌉
    /// when owed &gt; 0; refused when the bag holds fewer.
    /// </para>
    /// </summary>
    public sealed class MaterialBudget
    {
        public Dictionary<uint, (uint Held, int Credit)> Items { get; } = new();

        /// <summary>
        /// Does the budget pay for a material difference (solid voxels added + / removed − per voxel material)?
        /// <paramref name="missingItem"/> is the first item short (0 when covered).
        /// </summary>
        public bool Covers(IReadOnlyDictionary<ushort, int> delta, out uint missingItem)
        {
            missingItem = 0;
            var voxelsByItem = new Dictionary<uint, int>();
            foreach ((ushort material, int voxels) in delta)
            {
                uint item = BuildMaterials.ItemFor(material);
                if (item != 0 && voxels != 0)
                    voxelsByItem[item] = voxelsByItem.GetValueOrDefault(item) + voxels;
            }
            foreach ((uint item, int voxels) in voxelsByItem)
            {
                (uint held, int credit) = Items.GetValueOrDefault(item);
                int owed = voxels - credit;
                int needed = owed > 0 ? (owed + BuildMaterials.VoxelsPerItem - 1) / BuildMaterials.VoxelsPerItem : 0;
                if (needed > 0 && held < (uint)needed)
                {
                    missingItem = item;
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Result codes of an Environment Server operation: the client's VoxelConstruction Response codes (FUN_1430b6560).</summary>
    public static class EnvResult
    {
        public const uint Ok = 0, Failed = 1, InsufficientMaterials = 0xC;
    }

    /// <summary>One cell of a build edit (ApplyDiffRequest 0xF4/1), as the zone read it from the client's packet.</summary>
    public sealed class EditCell
    {
        public ulong Cell { get; init; }
        /// <summary>The 8000-byte changed mask (MSB first).</summary>
        public byte[] Changed { get; init; }
        /// <summary>The client's cell after the edit (SerializeBlockData format 2), decoded.</summary>
        public BlockData Result { get; init; }
        /// <summary>The client's packed blob as it came (mask + cell, block codec): what a remote service is sent, unchanged.</summary>
        public byte[] Packed { get; init; }
        /// <summary>True when the request's tail listed this cell's versions (ApplyDiffRequest +0xc8).</summary>
        public bool HasVersions { get; init; }
        /// <summary>The layer-1 version the client edited from, and the version it gave the layer after the edit.</summary>
        public uint BaseVersion { get; init; }
        public uint NewVersion { get; init; }
    }

    /// <summary>A whole build edit for the Environment Server: all cells or none (Building's rule).</summary>
    public sealed class EditRequest
    {
        public uint Zone { get; init; }
        public ulong Owner { get; init; }
        public List<EditCell> Cells { get; init; } = new();
        /// <summary>Null: no material check (dev tools).</summary>
        public MaterialBudget Budget { get; init; }
        /// <summary>Dev "cellcompare on": also report how the stored layer compares with the client's cell (first 16 cells).</summary>
        public bool Compare { get; init; }
    }

    /// <summary>What a build edit did.</summary>
    public sealed class EditResult
    {
        /// <summary><see cref="EnvResult"/>: Ok, InsufficientMaterials (nothing written), Failed (unreadable / service away).</summary>
        public uint Result { get; set; }
        /// <summary>True when the Environment Server could not be reached (terrain-service.md §6): nothing was written.</summary>
        public bool Unavailable { get; set; }
        /// <summary>Solid voxels added (+) / removed (−) per material: what the zone charges.</summary>
        public Dictionary<ushort, int> Delta { get; set; } = new();
        /// <summary>Cells edited from an older copy (someone changed them meanwhile): not applied; the zone resends them.</summary>
        public List<ulong> Stale { get; set; } = new();
        /// <summary>The written cells with the touched voxels before and after (the undo record).</summary>
        public List<(ulong Cell, List<TerrainEdits.VoxelValue> Before, List<TerrainEdits.VoxelValue> After)> Cells { get; set; } = new();
        public int Voxels { get; set; }
        /// <summary>The "cellcompare" line (null unless asked).</summary>
        public string CompareReport { get; set; }
    }

    /// <summary>What writing voxel values to several cells did (undo / redo, paste).</summary>
    public sealed class WriteResult
    {
        public uint Result { get; set; }
        public bool Unavailable { get; set; }
        public Dictionary<ushort, int> Delta { get; set; } = new();
        /// <summary>Per cell the values before and after (only when asked for).</summary>
        public List<(ulong Cell, List<TerrainEdits.VoxelValue> Before, List<TerrainEdits.VoxelValue> After)> Cells { get; set; } = new();
    }

    /// <summary>What the Heal tool's clear did: per cell the cleared voxels' old values and their new (empty) ones.</summary>
    public sealed class ClearResult
    {
        public bool Unavailable { get; set; }
        /// <summary>Material difference (negative: what comes back to the player).</summary>
        public Dictionary<ushort, int> Delta { get; set; } = new();
        public List<(ulong Cell, List<TerrainEdits.VoxelValue> Before, List<TerrainEdits.VoxelValue> After)> Cells { get; set; } = new();
        public int Voxels { get; set; }
    }

    /// <summary>
    /// Which voxels a destruction may change (a dig sphere, a Linking Area destroy), as data so it can travel to a remote
    /// Environment Server (it replaced a per-voxel callback; terrain-service.md §5.5). Built by the zone from its build
    /// sites (VoxelDestruction.MaskFor / OutsideSites). A voxel centre (global dm) inside a region takes the first region
    /// that holds it: a Linking Area set to block → no; else an area set to allow → yes; else the region's own
    /// <see cref="Region.Allowed"/>. Outside every region → yes. This is exactly VoxelDestruction's Rules.At turned into a
    /// yes / no (Outside and Allowed → yes; EditMode and Blocked → no).
    /// </summary>
    public sealed class VoxelFilter
    {
        /// <summary>A Linking Area's cylinder (global dm, InArea) and whether it allows destruction.</summary>
        public readonly record struct Area(double X, double Y, double Z, double Radius, double HalfHeight, bool Allow)
        {
            public bool Holds(double x, double y, double z)
                => (x - X) * (x - X) + (z - Z) * (z - Z) <= Radius * Radius && Math.Abs(y - Y) <= HalfHeight;
        }

        /// <summary>A build site's box (global dm, bounds included) with its default and its areas.</summary>
        public sealed record Region(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ, bool Allowed,
            IReadOnlyList<Area> Areas)
        {
            public bool Contains(double x, double y, double z)
                => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;
        }

        public List<Region> Regions { get; } = new();

        public bool Allows(double x, double y, double z)
        {
            foreach (Region region in Regions)
            {
                if (!region.Contains(x, y, z))
                    continue;
                bool inAllowed = false;
                foreach (Area area in region.Areas)
                    if (area.Holds(x, y, z))
                    {
                        if (!area.Allow)
                            return false;
                        inAllowed = true;
                    }
                return inAllowed || region.Allowed;
            }
            return true;
        }

        /// <summary>The filter as the stores' per-voxel callback (null filter → null: everything allowed).</summary>
        public static Func<double, double, double, bool> AsCallback(VoxelFilter filter) => filter == null ? null : filter.Allows;
    }

    /// <summary>
    /// A global dm box (bounds included) for <see cref="IEnvironmentServer.OwnedBy"/>: the voxels whose centre lies inside.
    /// </summary>
    public readonly record struct VoxelBox(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
    {
        /// <summary>Is the centre of voxel <paramref name="index"/> of a level-2 cell inside the box?</summary>
        public bool HoldsVoxel(ulong cell, int index)
        {
            (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
            double voxel = TerrainEdits.VoxelSize;
            int y = index % BlockData.Size, x = index / BlockData.Size % BlockData.Size, z = index / (BlockData.Size * BlockData.Size);
            double gx = ((double)cx * BlockData.Size + x + 0.5) * voxel, gy = ((double)cy * BlockData.Size + y + 0.5) * voxel,
                gz = ((double)cz * BlockData.Size + z + 0.5) * voxel;
            return gx >= MinX && gx <= MaxX && gy >= MinY && gy <= MaxY && gz >= MinZ && gz <= MaxZ;
        }
    }

    /// <summary>Thrown by a remote Environment Server when a call that must not silently do nothing (a stamp) can't reach it.</summary>
    public sealed class EnvironmentUnavailableException : Exception
    {
        public EnvironmentUnavailableException(string message) : base(message) { }
    }

    /// <summary>Helpers shared by the in-process and the remote Environment Server.</summary>
    public static class EnvironmentMasks
    {
        /// <summary>The 8000-byte changed mask (MSB first) of a list of voxel values.</summary>
        public static byte[] MaskOf(IEnumerable<TerrainEdits.VoxelValue> values)
        {
            var mask = new byte[BlockData.Count / 8];
            foreach (TerrainEdits.VoxelValue v in values)
                mask[v.Index >> 3] |= (byte)(1 << (7 - (v.Index & 7)));
            return mask;
        }

        /// <summary>Adds one material difference into another.</summary>
        public static void AddTo(Dictionary<ushort, int> total, IEnumerable<KeyValuePair<ushort, int>> delta)
        {
            foreach ((ushort material, int n) in delta)
                total[material] = total.GetValueOrDefault(material) + n;
        }

        /// <summary>Clears values: same voxels, everything zero (no override: the ground underneath shows).</summary>
        public static List<TerrainEdits.VoxelValue> Cleared(IEnumerable<TerrainEdits.VoxelValue> values)
            => values.Select(v => new TerrainEdits.VoxelValue(v.Index, 0, 0, 0, 0, 0)).ToList();
    }
}
