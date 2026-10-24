using LandmarkEmulator.WorldServer.Game.Terrain;
using System.Collections.Generic;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The terrain store's background jobs (EnvServer.Core TerrainEdits / TerrainLod, 2026-10-24) keep their own short lists
    /// instead of walking every edited cell every 5 s: the LOD check looks only when a cell version moved (ChangeStamp),
    /// the memory tidy-up walks the cells held in memory (expanded / packed), and the heal sweep only the layer-0 keys.
    /// </summary>
    public class TerrainBackgroundJobTests
    {
        private static List<TerrainEdits.VoxelValue> OneVoxel() => new()
        {
            new(TerrainEdits.VoxelIndex(2, 2, 2), 3, 7, BlockData.CentreOffset, BlockData.CentreOffset, BlockData.CentreOffset)
        };

        [Fact]
        public void ChangeStamp_MovesOnAnEdit_NotOnAnIdleFlush()
        {
            uint zone = 9511;
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, 160100, 130, 160100);
            try
            {
                long before = TerrainEdits.ChangeStamp;
                TerrainEdits.Write(cell, OneVoxel(), zone);
                Assert.True(TerrainEdits.ChangeStamp > before);

                TerrainEdits.Flush();
                long settled = TerrainEdits.ChangeStamp;
                TerrainEdits.Flush();
                TerrainEdits.CompactNow(all: true);
                // Other tests may edit cells meanwhile, so only this test's own idle steps are checked: none of them
                // changes a version (the LOD check stays asleep while nothing is edited).
                Assert.True(TerrainEdits.ChangeStamp >= settled);
            }
            finally
            {
                TerrainEdits.ClearCell(cell, zone);
                TerrainEdits.Flush();
            }
        }

        [Fact]
        public void MemoryTidyUp_TracksExpandedAndPackedCells()
        {
            uint zone = 9512;
            ulong cell = TerrainStore.CellId(TerrainEdits.Level, 160110, 130, 160110);
            try
            {
                TerrainEdits.Write(cell, OneVoxel(), zone);
                Assert.Equal((true, false), TerrainEdits.Residency(cell, zone));   // edited: expanded

                TerrainEdits.Flush();                    // saved, so it may be packed
                TerrainEdits.CompactNow(all: true);
                Assert.Equal((false, true), TerrainEdits.Residency(cell, zone));   // packed, in the spill candidates

                TerrainEdits.Read(cell, new byte[BlockData.Count / 8], zone);
                Assert.Equal((false, true), TerrainEdits.Residency(cell, zone));   // nothing read: stays packed

                TerrainEdits.Write(cell, OneVoxel(), zone);
                Assert.Equal((true, false), TerrainEdits.Residency(cell, zone));   // edited again: expanded
            }
            finally
            {
                TerrainEdits.ClearCell(cell, zone);
                TerrainEdits.Flush();
            }
        }

        [Fact]
        public void HealSweep_ListsNewLayerZeroCells_AndNotBuilding()
        {
            uint zone = 9513;
            ulong poi = TerrainStore.CellId(TerrainEdits.Level, 160120, 130, 160120);
            ulong build = TerrainStore.CellId(TerrainEdits.Level, 160121, 130, 160120);
            try
            {
                int before = TerrainEdits.HealSweepCount(zone);

                TerrainEdits.Write(poi, OneVoxel(), zone, layer: TerrainEdits.PoiLayer);
                Assert.Equal(before + 1, TerrainEdits.HealSweepCount(zone));   // a new world-stamp cell is picked up

                TerrainEdits.Write(build, OneVoxel(), zone, layer: TerrainEdits.BuildLayer);
                Assert.Equal(before + 1, TerrainEdits.HealSweepCount(zone));   // building never heals: not listed
            }
            finally
            {
                TerrainEdits.ClearCell(poi, zone);
                TerrainEdits.ClearCell(build, zone);
                TerrainEdits.Flush();
            }
        }
    }
}
