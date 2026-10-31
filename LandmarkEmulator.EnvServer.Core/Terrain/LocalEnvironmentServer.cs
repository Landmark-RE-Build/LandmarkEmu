using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The Environment Server inside the world process: today's terrain code (<see cref="TerrainEdits"/>,
    /// <see cref="TerrainLod"/>, <see cref="OreVeins"/>, <see cref="VoxelOwnership"/>, <see cref="TerrainStore"/>,
    /// <see cref="VoxelTemplate"/>) behind <see cref="IEnvironmentServer"/>, unchanged (terrain-service.md §7 step 2:
    /// "library split, no behaviour change"). Every call goes straight to the static store it always used, so the
    /// stores keep their own locks and timers. The `envserver` container (LandmarkEmulator.EnvServer) runs this same class
    /// behind its message handler, so the remote and the in-process service share one implementation.
    /// <para>
    /// The transactions (<see cref="ApplyEdit"/>, <see cref="WriteCells"/>, <see cref="ClearCells"/>; spec §5.5) are the
    /// loops Building and BuildingClipboard ran before, moved here unchanged: the stale-version check, Prepare, the
    /// material check (now against the budget, PayFor's arithmetic), Commit with the client's version.
    /// </para>
    /// </summary>
    public sealed class LocalEnvironmentServer : IEnvironmentServer
    {
        public bool Available => true;

        private readonly Func<uint, (double, double, double), (double, double, double),
            List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> sitesIn;

        /// <param name="sitesIn">The build-site boxes (global dm) on a landscape that touch a box: the heal and the
        /// layer move keep site voxels, and veins keep out of sites. The zone's claims (Claims.BoxesIn) until the claim
        /// side sends ZoneAddClaim / ZoneRemoveClaim boxes (spec §5.2).</param>
        public LocalEnvironmentServer(Func<uint, (double, double, double), (double, double, double),
            List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)>> sitesIn)
        {
            this.sitesIn = sitesIn;
            OreVeins.SitesIn = sitesIn;
            TerrainLod.CopiesChanged = (zone, layer, copies) => LodCopiesChanged?.Invoke(zone, layer, copies);
        }

        // ---- start-up and upkeep -------------------------------------------------------------------------------------

        public void Load(string terrainEditsFolder) => TerrainEdits.Load(terrainEditsFolder);
        public void LoadVeins(string gameDirectory, uint continent) => OreVeins.Load(gameDirectory, continent);
        public void Flush() => TerrainEdits.Flush();
        public void CompactNow(bool all) => TerrainEdits.CompactNow(all);
        public string MemoryReport() => TerrainEdits.MemoryReport();
        public void MigrateToLayers() => TerrainEdits.MigrateToLayers(sitesIn);

        // ---- cells -------------------------------------------------------------------------------------------------

        public bool Has(ulong cell, uint zone) => TerrainEdits.Has(cell, zone);
        public bool HasCapturedCell(ulong cell) => TerrainStore.Get(cell) != null;
        public uint LayerVersion(ulong cell, uint zone, uint layer) => TerrainEdits.LayerVersion(cell, zone, layer);
        public uint[] LayerVersions(ulong cell, uint zone) => TerrainEdits.LayerVersions(cell, zone);
        public uint CurrentVersion(ulong cell, uint zone) => TerrainEdits.CurrentVersion(cell, zone);
        public byte[] BuildCellsUpdate(ulong cell, uint zone, bool always = false) => TerrainEdits.BuildUpdate(cell, zone, always);

        public IReadOnlyList<byte[]> BuildCellsUpdates(IReadOnlyList<ulong> cells, uint zone, bool always = false)
        {
            var updates = new byte[cells.Count][];
            for (int i = 0; i < cells.Count; i++)
                updates[i] = TerrainEdits.BuildUpdate(cells[i], zone, always);
            return updates;
        }

        public byte[] BuildCapturedCellsUpdate(IEnumerable<ulong> cells) => TerrainStore.BuildCellsUpdate(cells);
        public byte[] BuildManifest(uint zone) => TerrainStore.BuildManifest(zone);
        public void SampleVersion(ulong cell, uint zone, uint[] held) => TerrainEdits.SampleVersion(cell, zone, held);
        public string Describe(Vector3 local, uint zone) => TerrainEdits.Describe(local, zone);

        // ---- build edits, undo / redo, pastes ------------------------------------------------------------------------

        public EditResult ApplyEdit(EditRequest request)
        {
            uint zone = request.Zone;
            var result = new EditResult();
            var delta = new Dictionary<ushort, int>();
            // One read pass per cell: the undo record's "before" and the material cost (TerrainEdits.Prepare).
            var prepared = new List<(EditCell Cell, int[] Indices, List<TerrainEdits.VoxelValue> Before)>();
            foreach (EditCell cell in request.Cells)
            {
                // Edited from an older copy (someone else changed the cell meanwhile): not applied or charged; the zone
                // sends the client the server's cell, which replaces its own drawing of it. A base that names the content the
                // server holds under another number (the client's own version of a layer the server wrote) is the same copy
                // (TerrainEdits.HoldsClientCopy).
                if (cell.HasVersions && TerrainEdits.LayerVersion(cell.Cell, zone, TerrainEdits.BuildLayer) != cell.BaseVersion
                    && !(cell.BaseVersion == 0 && TerrainEdits.AcceptsBaseZero(cell.Cell, zone, request.Owner))
                    && !TerrainEdits.HoldsClientCopy(cell.Cell, zone, cell.BaseVersion))
                {
                    result.Stale.Add(cell.Cell);
                    continue;
                }
                (int[] indices, List<TerrainEdits.VoxelValue> before) = TerrainEdits.Prepare(cell.Cell, cell.Changed, cell.Result, zone, delta);
                prepared.Add((cell, indices, before));
            }
            result.Delta = delta;
            if (request.Budget != null && !request.Budget.Covers(delta, out _))
            {
                result.Result = EnvResult.InsufficientMaterials;
                return result;
            }
            // One write pass per cell; the written values are the undo record's "after".
            foreach ((EditCell cell, int[] indices, List<TerrainEdits.VoxelValue> before) in prepared)
            {
                if (indices.Length == 0)
                    continue;
                List<TerrainEdits.VoxelValue> after = TerrainEdits.Commit(cell.Cell, indices, cell.Result, zone, request.Owner,
                    clientVersion: cell.HasVersions ? cell.NewVersion : 0);
                result.Voxels += indices.Length;
                result.Cells.Add((cell.Cell, before, after));
            }
            if (request.Compare)
                result.CompareReport = CompareReport(request, result.Stale);
            return result;
        }

        /// <summary>
        /// Dev "cellcompare": does the server now hold exactly the client's cell? (first 16 cells; TerrainEdits.Compare.)
        /// Checked 2026-10-14 with sphere edits in the real client: the only differences were voxels the server holds and
        /// the sent cell lacks (client 0 / server 2-3).
        /// </summary>
        private static string CompareReport(EditRequest request, List<ulong> stale)
        {
            int cf = 0, cm = 0, co = 0, outside = 0, compared = 0;
            lock (TerrainEdits.LastOutside)
                TerrainEdits.LastOutside.Clear();
            foreach (EditCell cell in request.Cells.Take(16))
            {
                if (stale.Contains(cell.Cell))
                    continue;
                var c = TerrainEdits.Compare(cell.Cell, request.Zone, cell.Result, cell.Changed);
                cf += c.Flags; cm += c.Materials; co += c.Offsets; outside += c.OutsideMask; compared++;
            }
            string pairs;
            lock (TerrainEdits.LastOutside)
                pairs = string.Join(", ", TerrainEdits.LastOutside.Select(p => $"{p.Key.Client}/{p.Key.Server}×{p.Value}"));
            return $"client vs server cell after edit ({compared} cells): {cf} flag, {cm} material, {co} offset differences, "
                + $"{outside} outside the changed mask" + (outside > 0 ? $" (client/server flags: {pairs})." : ".");
        }

        public WriteResult WriteCells(uint zone, ulong owner, IReadOnlyList<(ulong Cell, List<TerrainEdits.VoxelValue> Values)> cells,
            MaterialBudget budget, bool returnValues)
        {
            var result = new WriteResult();
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in cells)
                EnvironmentMasks.AddTo(result.Delta, TerrainEdits.MaterialDelta(cell, values, zone));
            if (budget != null && !budget.Covers(result.Delta, out _))
            {
                result.Result = EnvResult.InsufficientMaterials;
                return result;
            }
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in cells)
            {
                if (!returnValues)
                {
                    TerrainEdits.Write(cell, values, zone, owner);
                    continue;
                }
                byte[] mask = EnvironmentMasks.MaskOf(values);
                List<TerrainEdits.VoxelValue> before = TerrainEdits.Read(cell, mask, zone);
                TerrainEdits.Write(cell, values, zone, owner);
                result.Cells.Add((cell, before, TerrainEdits.Read(cell, mask, zone)));
            }
            return result;
        }

        public ClearResult ClearCells(uint zone, ulong owner, IReadOnlyList<(ulong Cell, byte[] Changed)> cells)
        {
            var result = new ClearResult();
            // Placed blocks that go come back as materials (negative cost), as Remove does.
            foreach ((ulong cell, byte[] changed) in cells)
                EnvironmentMasks.AddTo(result.Delta, TerrainEdits.MaterialDelta(cell,
                    EnvironmentMasks.Cleared(TerrainEdits.Read(cell, changed, zone).Where(v => v.Flags != 0)), zone));
            foreach ((ulong cell, byte[] changed) in cells)
            {
                List<TerrainEdits.VoxelValue> cleared = EnvironmentMasks.Cleared(TerrainEdits.Read(cell, changed, zone).Where(v => v.Flags != 0));
                if (cleared.Count == 0)
                    continue;
                List<TerrainEdits.VoxelValue> old = TerrainEdits.Read(cell, EnvironmentMasks.MaskOf(cleared), zone);
                TerrainEdits.Write(cell, cleared, zone, owner);
                result.Cells.Add((cell, old, cleared));
                result.Voxels += cleared.Count;
            }
            return result;
        }

        public List<TerrainEdits.VoxelValue> Read(ulong cell, byte[] changed, uint zone, uint layer = TerrainEdits.BuildLayer)
            => TerrainEdits.Read(cell, changed, zone, layer);

        public void Write(ulong cell, List<TerrainEdits.VoxelValue> values, uint zone, ulong owner = 0, uint layer = TerrainEdits.BuildLayer)
            => TerrainEdits.Write(cell, values, zone, owner, layer);

        public Dictionary<ushort, int> MaterialDelta(ulong cell, List<TerrainEdits.VoxelValue> values, uint zone, uint layer = TerrainEdits.BuildLayer)
            => TerrainEdits.MaterialDelta(cell, values, zone, layer);

        // ---- digs, box operations, heals -----------------------------------------------------------------------------

        public (List<ulong> Cells, int Voxels) Sphere(Vector3 local, double radius, ushort material, uint zone,
            Dictionary<ushort, int> oreMined = null, HashSet<ulong> oreCells = null, ushort? shellMaterial = null,
            VoxelFilter allowed = null, bool carveBuilds = false, TimeSpan? healDelay = null)
        {
            if (shellMaterial is ushort shell)
                TerrainEdits.ShellMaterial = shell;
            return TerrainEdits.Sphere(local, radius, material, zone, oreMined, oreCells, TerrainEdits.DigLayer,
                VoxelFilter.AsCallback(allowed), carveBuilds, healDelay);
        }

        public (List<ulong> Cells, int Voxels) DestroyArea(uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight,
            VoxelFilter allowed = null)
            => TerrainEdits.DestroyArea(zone, centre, radius, halfHeight, VoxelFilter.AsCallback(allowed));

        public (List<ulong> Cells, int Voxels) HealArea(uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight)
            => TerrainEdits.HealArea(zone, centre, radius, halfHeight);

        public List<ulong> RevertBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, uint zone)
            => TerrainEdits.RevertBox(minX, minY, minZ, maxX, maxY, maxZ, zone);

        public void ClearCell(ulong cell, uint zone) => TerrainEdits.ClearCell(cell, zone);
        public IReadOnlyList<(uint Zone, List<ulong> Cells)> Regenerate() => TerrainEdits.Regenerate(sitesIn);
        public void SetRegenerationDelay(TimeSpan delay) => TerrainEdits.SetRegenerationDelay(delay);
        public List<ulong> SetTemplateInstances(uint zone, IReadOnlyList<TemplateInstanceLookup.Instance> instances, bool announce = true)
            => TerrainEdits.TouchBuildLayers(zone, TemplateInstanceLookup.Replace(zone, instances,
                (cell, placer) => VoxelOwnership.OwnedIn(zone, cell, placer), announce));

        public void MarkPermanent(IEnumerable<ulong> cells, uint zone) => TerrainEdits.MarkPermanent(cells, zone);

        // ---- exported-template stamps --------------------------------------------------------------------------------

        public List<ulong> StampTemplate(VoxelTemplate template, uint cellX, uint cellY, uint cellZ, uint zone)
            => template.Stamp(cellX, cellY, cellZ, zone);

        public (List<ulong> Cells, List<ulong> Changed) StampTemplateAt(VoxelTemplate template, long voxelX, long voxelY, long voxelZ, uint zone, int turns = 0)
            => template.StampAt(voxelX, voxelY, voxelZ, zone, turns);

        // ---- voxel ownership -----------------------------------------------------------------------------------------

        public (List<ulong> UpToDate, List<(ulong Cell, uint Version, byte[] Raw, List<ulong> Owners)> Data) OwnershipQuery(uint zone,
            IReadOnlyList<(ulong Cell, uint Version)> cells)
            => VoxelOwnership.Query(zone, cells);

        public List<(ulong Cell, List<int> Indices)> OwnedBy(uint zone, ulong owner, VoxelBox box)
            => VoxelOwnership.OwnedBy(zone, owner, box.HoldsVoxel);

        public Dictionary<ulong, int> OwnershipCensus(uint zone, ulong cell) => VoxelOwnership.Census(zone, cell);

        // ---- creature navigation -------------------------------------------------------------------------------------

        public SolidityGrid SolidityOf(uint zone, VoxelBox box) => SolidityGrid.Build(zone, box);

        // ---- ore veins -----------------------------------------------------------------------------------------------

        public IReadOnlyList<(uint Zone, List<ulong> Cells)> UpdateVeins(IEnumerable<(uint Zone, Vector3 Position)> players)
            => OreVeins.Update(players);

        public List<ulong> EnsureVeinsAround(uint zone, Vector3 local) => OreVeins.EnsureAround(zone, local);
        public void DisturbVeins(IEnumerable<ulong> cells, uint zone) => OreVeins.Disturbed(cells, zone);

        public List<ulong> ClearVeinsInSite(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
            => OreVeins.ClearSite(zone, min, max);

        public List<(OreVeins.Vein Vein, Vector3 Nearest, float Distance)> VeinsNear(Vector3 position, float radius, uint zone)
            => OreVeins.Near(position, radius, zone);

        public (uint Item, Vector3 At, float Distance)? NearestVein(Vector3 position, float radius, ISet<uint> items, uint zone)
            => OreVeins.Nearest(position, radius, items, zone);

        public uint OreItemFor(ushort material) => OreVeins.ItemFor(material);

        // ---- LOD copies ----------------------------------------------------------------------------------------------

        public void BuildLodCopies() => TerrainLod.BuildAll();
        public void UpdateLodCopies() => TerrainLod.Update();
        public void RefreshLodCopies(uint zone) => TerrainLod.RefreshNow(zone);

        public event Action<uint, uint, IReadOnlyList<(ulong Cell, uint Version)>> LodCopiesChanged;
    }
}
