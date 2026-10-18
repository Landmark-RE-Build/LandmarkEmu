using LandmarkEmulator.Internal.Environment;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The store's types as Environment Server messages and back (LandmarkEmulator.Internal/Environment): used by the
    /// zone's RemoteEnvironmentServer and by the service (LandmarkEmulator.EnvServer), so both sides convert the same way.
    /// </summary>
    public static class EnvironmentWire
    {
        public static EnvVoxel ToWire(TerrainEdits.VoxelValue v) => new(v.Index, v.Flags, v.Material, v.O0, v.O1, v.O2);
        public static TerrainEdits.VoxelValue FromWire(EnvVoxel v) => new(v.Index, v.Flags, v.Material, v.O0, v.O1, v.O2);

        public static List<EnvVoxel> ToWire(IEnumerable<TerrainEdits.VoxelValue> values)
            => values?.Select(ToWire).ToList() ?? new List<EnvVoxel>();

        public static List<TerrainEdits.VoxelValue> FromWire(IEnumerable<EnvVoxel> values)
            => values?.Select(FromWire).ToList() ?? new List<TerrainEdits.VoxelValue>();

        public static List<(ulong Cell, List<EnvVoxel> Before, List<EnvVoxel> After)> ToWire(
            IEnumerable<(ulong Cell, List<TerrainEdits.VoxelValue> Before, List<TerrainEdits.VoxelValue> After)> cells)
            => cells.Select(c => (c.Cell, ToWire(c.Before), ToWire(c.After))).ToList();

        public static List<(ulong Cell, List<TerrainEdits.VoxelValue> Before, List<TerrainEdits.VoxelValue> After)> FromWire(
            IEnumerable<(ulong Cell, List<EnvVoxel> Before, List<EnvVoxel> After)> cells)
            => cells.Select(c => (c.Cell, FromWire(c.Before), FromWire(c.After))).ToList();

        public static EnvBudget ToWire(MaterialBudget budget)
            => budget == null ? null : new EnvBudget { Items = budget.Items.Select(i => (i.Key, i.Value.Held, i.Value.Credit)).ToList() };

        public static MaterialBudget FromWire(EnvBudget budget)
        {
            if (budget == null)
                return null;
            var result = new MaterialBudget();
            foreach ((uint item, uint held, int credit) in budget.Items)
                result.Items[item] = (held, credit);
            return result;
        }

        public static EnvFilter ToWire(VoxelFilter filter)
        {
            var wire = new EnvFilter();
            if (filter != null)
                foreach (VoxelFilter.Region r in filter.Regions)
                    wire.Regions.Add((r.MinX, r.MinY, r.MinZ, r.MaxX, r.MaxY, r.MaxZ, r.Allowed,
                        r.Areas.Select(a => (a.X, a.Y, a.Z, a.Radius, a.HalfHeight, a.Allow)).ToList()));
            return wire;
        }

        public static VoxelFilter FromWire(EnvFilter wire)
        {
            if (wire == null || wire.Regions.Count == 0)
                return null;
            var filter = new VoxelFilter();
            foreach (var r in wire.Regions)
                filter.Regions.Add(new VoxelFilter.Region(r.MinX, r.MinY, r.MinZ, r.MaxX, r.MaxY, r.MaxZ, r.Allowed,
                    r.Areas.Select(a => new VoxelFilter.Area(a.X, a.Y, a.Z, a.Radius, a.HalfHeight, a.Allow)).ToList()));
            return filter;
        }

        public static List<(ushort Material, int Voxels)> ToWire(Dictionary<ushort, int> delta)
            => delta?.Select(d => (d.Key, d.Value)).ToList() ?? new List<(ushort, int)>();

        public static Dictionary<ushort, int> FromWire(List<(ushort Material, int Voxels)> delta)
        {
            var result = new Dictionary<ushort, int>();
            foreach ((ushort material, int voxels) in delta)
                result[material] = result.GetValueOrDefault(material) + voxels;
            return result;
        }

        /// <summary>
        /// Every layer version of a cell as the version index keeps it (layers 0-3): level-2 cells read together
        /// (TerrainEdits.LayerVersions, ore in layer 2), coarse LOD cells per layer (TerrainLod).
        /// </summary>
        public static uint[] IndexVersions(ulong cell, uint zone)
        {
            var versions = new uint[4];
            if (TerrainStore.Decode(cell).Level == TerrainEdits.Level)
                TerrainEdits.LayerVersions(cell, zone).CopyTo(versions, 0);
            else
                for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                    versions[layer] = TerrainEdits.LayerVersion(cell, zone, layer);
            return versions;
        }
    }
}
