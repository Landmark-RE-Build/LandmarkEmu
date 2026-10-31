using LandmarkEmulator.WorldServer.Game.Terrain;
using System;
using System.Collections.Generic;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A build edit that comes with NewVersion 0 (0.695 Add-tool clicks) gives the layer a new version, so the change
    /// reaches everything keyed by version, and its editor's next BaseVersion 0 still applies while nobody else changed
    /// the cell (trace/0695/phaseE4-logic.md F1; the org's #12 kept the old version instead).
    /// </summary>
    public class EditVersionRuleTests : IDisposable
    {
        private static int nextZone = 9400;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly ulong cell = TerrainStore.CellId(TerrainEdits.Level, 300, 20, 300);

        public void Dispose() => TerrainEdits.ClearCell(cell, zone);

        private void Edit(int index, ulong owner, uint clientVersion)
        {
            var result = new BlockData();
            result.SetSolid(index, 7);
            var changed = new byte[BlockData.Count / 8];
            changed[index >> 3] |= (byte)(0x80 >> (index & 7));
            (int[] indices, _) = TerrainEdits.Prepare(cell, changed, result, zone, new Dictionary<ushort, int>());
            TerrainEdits.Commit(cell, indices, result, zone, owner, clientVersion: clientVersion);
        }

        private uint Version => TerrainEdits.LayerVersion(cell, zone, TerrainEdits.BuildLayer);

        [Fact]
        public void NewVersionZero_StillChangesTheVersion()
        {
            Edit(10, owner: 1, clientVersion: 0);
            uint first = Version;
            Assert.NotEqual(0u, first);              // a new cell is listed (version 0 = nothing there)
            Edit(11, owner: 1, clientVersion: 0);
            Assert.NotEqual(first, Version);         // every change is a new version
        }

        [Fact]
        public void TheEditorsBaseZero_IsAccepted_UntilSomeoneElseChangesTheCell()
        {
            Edit(10, owner: 1, clientVersion: 0);
            Assert.True(TerrainEdits.AcceptsBaseZero(cell, zone, 1));
            Assert.False(TerrainEdits.AcceptsBaseZero(cell, zone, 2));
            Edit(12, owner: 2, clientVersion: 0);   // another builder
            Assert.False(TerrainEdits.AcceptsBaseZero(cell, zone, 1));
            Edit(13, owner: 1, clientVersion: 0x1234ABCD);   // a real CRC: versions agree again, no exception needed
            Assert.Equal(0x1234ABCDu, Version);
            Assert.False(TerrainEdits.AcceptsBaseZero(cell, zone, 1));
        }
    }
}
