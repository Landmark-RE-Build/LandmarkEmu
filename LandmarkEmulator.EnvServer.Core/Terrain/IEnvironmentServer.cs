using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The Environment Server: the world's terrain / voxel back end (trace/architecture/terrain-service.md). The client
    /// names it in its monitor page (EnvironmentServerMonitorState, FUN_14320ea00 / FUN_143163fe0, job types named by
    /// FUN_143c74ef0 / FUN_143bb4ef0) and in the construction replies it handles (VoxelConstructionPacketEnvOwnership*
    /// 0xF4/0x16, 0x17; EnvServerResponse 0xF4/0x15 in its id table). It owns the voxel overlays per (landscape, cell,
    /// layer) with their versions, the edits, heals, pastes, undo writes, voxel ownership, ore veins, LOD copies and
    /// template stamps (ruins, the Spire base).
    /// <para>
    /// The zone server stays the only thing that talks to the client about terrain (spec §3.1): it reads the packets,
    /// checks permissions (claims) and materials, keeps the per-player order and re-zone holds, decides who gets full
    /// cell data and who a 21-byte note (ZoneTerrain), and calls this interface for everything stored.
    /// By default it runs in the world process (<see cref="LocalEnvironmentServer"/>); with ENVSERVER_ADDRESS set the
    /// zone's RemoteEnvironmentServer carries the same calls as internal messages to the `envserver` container (spec §5.2,
    /// §7 steps 3-6; LandmarkEmulator.EnvServer). Method groups are tagged with the client's job type numbers (spec §1.1).
    /// </para>
    /// <para>
    /// Remote split (spec §5.5, decided 2026-10-16): no voxel mirror in the zone. Every voxel write is one transaction in
    /// the service (<see cref="ApplyEdit"/>, <see cref="WriteCells"/>, <see cref="ClearCells"/>, digs, stamps), with the
    /// zone's permission checked before and its material budget inside; the zone keeps only the version index and a cache
    /// of finished CellsUpdate bodies (spec §5.3), which answer the frequent synchronous reads (<see cref="Has"/>,
    /// <see cref="LayerVersion"/>, <see cref="BuildManifest"/>, <see cref="BuildCellsUpdate"/>). So the interface carries
    /// data, never callbacks: <see cref="VoxelFilter"/>, <see cref="VoxelBox"/>, <see cref="MaterialBudget"/>.
    /// </para>
    /// <para>Landscapes are StaticZoneData zone ids (0 = the home continent); positions are client local metres unless
    /// a name says global (dm); cells are VoxelFarm level-2 ids unless noted.</para>
    /// </summary>
    public interface IEnvironmentServer
    {
        /// <summary>
        /// Can it serve requests now? Always true in-process; a remote one is false while its link is down or its version
        /// index is not in step yet (terrain-service.md §6: edits refused with a message, cells from the cache).
        /// </summary>
        bool Available { get; }

        // ---- start-up and upkeep -------------------------------------------------------------------------------------

        /// <summary>Loads the saved overlays and ownership grids (TerrainEdits folder: one file per cell layer).</summary>
        void Load(string terrainEditsFolder);

        /// <summary>Loads the ore vein spawn data of the landscapes (.vdt, ResourceVeinGroupVeins).</summary>
        void LoadVeins(string gameDirectory, uint continent);

        /// <summary>Writes changed overlays and ownership grids to disk and packs idle cells (world tick).</summary>
        void Flush();

        /// <summary>Packs expanded cells now (all saved ones with <paramref name="all"/>).</summary>
        void CompactNow(bool all);

        /// <summary>Dev / log: cells held, how many expanded, the compact bytes.</summary>
        string MemoryReport();

        /// <summary>One-time move of build-site voxels from layer 0 to layer 1 (marker file layers.v2; world tick).</summary>
        void MigrateToLayers();

        // ---- cells: versions, CellsUpdate bodies, the zone-in manifest (job type 18) --------------------------------

        /// <summary>Does the server hold anything for this cell (an edited layer, ore veins, or a coarse copy)?</summary>
        bool Has(ulong cell, uint zone);

        /// <summary>Is the cell in the captured live store (home continent, dev mesh data)?</summary>
        bool HasCapturedCell(ulong cell);

        /// <summary>The version a CellsUpdate carries for one layer of a cell (0 = nothing there); any level.</summary>
        uint LayerVersion(ulong cell, uint zone, uint layer);

        /// <summary>
        /// Every layer's version of a level-2 cell, read together (what manifest notes for a changed cell carry; spec
        /// §5.2 CellVersionsChanged).
        /// </summary>
        uint[] LayerVersions(ulong cell, uint zone);

        /// <summary>One number for "what the server would send for this cell now" (all layers; de-duplication).</summary>
        uint CurrentVersion(ulong cell, uint zone);

        /// <summary>
        /// The client CellsUpdate body (TerrainCell 0xEC/5) for a cell, every layer with its own version, ore composed
        /// into layer 2; null when the server has nothing for the cell, unless <paramref name="always"/>.
        /// </summary>
        byte[] BuildCellsUpdate(ulong cell, uint zone, bool always = false);

        /// <summary>
        /// <see cref="BuildCellsUpdate"/> for several cells at once (one round trip to a remote service); the list is in
        /// the order of <paramref name="cells"/>, null where there is nothing (or, remote, the service is away and the
        /// cell isn't cached).
        /// </summary>
        IReadOnlyList<byte[]> BuildCellsUpdates(IReadOnlyList<ulong> cells, uint zone, bool always = false);

        /// <summary>CellsUpdate body for cells of the captured live store.</summary>
        byte[] BuildCapturedCellsUpdate(IEnumerable<ulong> cells);

        /// <summary>The client ModifiedCellManifest body (TerrainCell 0x0A) of a landscape.</summary>
        byte[] BuildManifest(uint zone);

        /// <summary>Research: keeps a few cells whose layer-1 version the client holds differently (version-samples).</summary>
        void SampleVersion(ulong cell, uint zone, uint[] held);

        /// <summary>Dev "cellstats": what each stored layer of the cell at a local point holds.</summary>
        string Describe(Vector3 local, uint zone);

        // ---- build edits (7 Apply Diff Cell), undo / redo (9), pastes (11) ------------------------------------------

        /// <summary>
        /// A whole build edit (ApplyDiffRequest 0xF4/1, job type 7) in one transaction, after the zone checked the
        /// permissions (ZoneTerrain.MayApply): cells edited from an older layer-1 copy are left out (<see cref="EditResult.Stale"/>);
        /// the material difference of the rest is checked against the budget (refused whole: InsufficientMaterials, nothing
        /// written); then every cell's changed voxels are written to layer 1, owned by <see cref="EditRequest.Owner"/>,
        /// adopting the client's new layer version. Returns the undo record (before / after per cell) and the difference
        /// the zone charges. Was Prepare / Commit / Compare called per cell from Building (spec §5.5).
        /// </summary>
        EditResult ApplyEdit(EditRequest request);

        /// <summary>
        /// Writes voxel values to several cells as one operation (undo / redo, job type 9; paste, 11): the material
        /// difference is checked against <paramref name="budget"/> first (null = no check); refused whole with
        /// InsufficientMaterials. With <paramref name="returnValues"/> each cell's touched voxels before and after come back.
        /// </summary>
        WriteResult WriteCells(uint zone, ulong owner, IReadOnlyList<(ulong Cell, List<TerrainEdits.VoxelValue> Values)> cells,
            MaterialBudget budget, bool returnValues);

        /// <summary>
        /// The Heal tool (an ApplyDiff cell that is only a mask): every overridden voxel of layer 1 in the mask goes back to
        /// nothing (the generated ground shows). Returns per cell the old and new values and the material difference
        /// (negative: the refund).
        /// </summary>
        ClearResult ClearCells(uint zone, ulong owner, IReadOnlyList<(ulong Cell, byte[] Changed)> cells);

        /// <summary>The stored values of the voxels set in a changed mask (zero where nothing is stored).</summary>
        List<TerrainEdits.VoxelValue> Read(ulong cell, byte[] changed, uint zone, uint layer = TerrainEdits.BuildLayer);

        /// <summary>Writes voxel values (undo / redo, paste, heal tool); solid ones become <paramref name="owner"/>'s (0 = leave).</summary>
        void Write(ulong cell, List<TerrainEdits.VoxelValue> values, uint zone, ulong owner = 0, uint layer = TerrainEdits.BuildLayer);

        /// <summary>Net solid voxels per material that writing <paramref name="values"/> would add (+) or remove (−).</summary>
        Dictionary<ushort, int> MaterialDelta(ulong cell, List<TerrainEdits.VoxelValue> values, uint zone, uint layer = TerrainEdits.BuildLayer);

        // ---- digs, box operations (14 Area Iterator), heals (4 Temporary Modifications) -----------------------------

        /// <summary>
        /// Digs (material 0) or fills a sphere around a local point in the dig layer; returns the cells changed and the
        /// voxels changed. Ore voxels carved are counted in <paramref name="oreMined"/> (their cells in
        /// <paramref name="oreCells"/>). <paramref name="shellMaterial"/>, when given, becomes the material of the hole's
        /// walls in the old solid-shell dig (dev "digmode 5"; it stays for later digs); the default dig keeps the ground's
        /// own material on the walls (vertex-only voxels, TerrainEdits.SurfaceVertices).
        /// <para>
        /// Temporary destruction (0.695 Play Mode sites, TASKS 2.1 / 2.6): <paramref name="allowed"/> (global dm voxel
        /// centre) leaves out voxels where destruction is not allowed; <paramref name="carveBuilds"/> carves building
        /// voxels in the open air too (without changing their ownership); <paramref name="healDelay"/> is the effect's own
        /// heal time (dynamite 100 s, P6037), else the 20-minute default. See TerrainEdits.Sphere.
        /// </para>
        /// </summary>
        (List<ulong> Cells, int Voxels) Sphere(Vector3 local, double radius, ushort material, uint zone,
            Dictionary<ushort, int> oreMined = null, HashSet<ulong> oreCells = null, ushort? shellMaterial = null,
            VoxelFilter allowed = null, bool carveBuilds = false, TimeSpan? healDelay = null);

        /// <summary>
        /// Linking Area "...Destroy Voxels in this area." (577548): the building voxels in a vertical cylinder (global dm
        /// centre, radius, half height) are destroyed in the destruction layer, healing after 10000 s (P6203); voxels
        /// <paramref name="allowed"/> refuses stay. Returns the cells changed and the voxels destroyed (TerrainEdits.DestroyArea).
        /// </summary>
        (List<ulong> Cells, int Voxels) DestroyArea(uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight,
            VoxelFilter allowed = null);

        /// <summary>
        /// Linking Area "...Heal Voxels in this area." (577549): the destruction layer inside the cylinder is cleared.
        /// Returns the cells changed and the voxels healed (TerrainEdits.HealArea).
        /// </summary>
        (List<ulong> Cells, int Voxels) HealArea(uint zone, (double X, double Y, double Z) centre, double radius, double halfHeight);

        /// <summary>Drops every override whose voxel centre lies in a global box (dm); returns the cells changed.</summary>
        List<ulong> RevertBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, uint zone);

        /// <summary>Dev "clearcell": clears every layer of a cell (and its ownership).</summary>
        void ClearCell(ulong cell, uint zone);

        /// <summary>
        /// The heal (world tick, checked once a minute): cells untouched for the regeneration delay drop their overrides
        /// outside build sites and permanent cells. Returns the cells that grew back per landscape.
        /// </summary>
        IReadOnlyList<(uint Zone, List<ulong> Cells)> Regenerate();

        /// <summary>Dev "regendelay": changes the heal delay and checks again right away.</summary>
        void SetRegenerationDelay(TimeSpan delay);

        /// <summary>Marks cells as part of the world (stamped structures): the heal leaves them alone.</summary>
        void MarkPermanent(IEnumerable<ulong> cells, uint zone);

        /// <summary>
        /// A landscape's placed Player Studio designs, all of them (<see cref="TemplateInstanceLookup"/>; the world's
        /// Game.TemplateInstances): layer 1 of their cells carries them to the client for Voxel Vision. Returns the cells
        /// whose building layer got a new version (the caller sends them again); <paramref name="announce"/> false (start-up,
        /// a reconnect) sets them without new versions.
        /// </summary>
        List<ulong> SetTemplateInstances(uint zone, IReadOnlyList<TemplateInstanceLookup.Instance> instances, bool announce = true);

        // ---- exported-template stamps (8 Construction Template): ruins, the Spire base ------------------------------

        /// <summary>
        /// Writes a template into layer 0 with its corner at a level-2 cell; returns the cells changed. A remote service
        /// that can't be reached throws <see cref="EnvironmentUnavailableException"/> (so no "stamped" marker is written for
        /// a stamp that never happened).
        /// </summary>
        List<ulong> StampTemplate(VoxelTemplate template, uint cellX, uint cellY, uint cellZ, uint zone);

        /// <summary>
        /// Stamps a template with its corner at a global voxel, turned <paramref name="turns"/> quarter turns about +y in its
        /// own box (0 = as stored: the outpost ruins, Landmarks of Landmark; the cavern graveyards use their placement's
        /// turn, VoxelTemplate.StampAt); returns its cells and the changed ones.
        /// </summary>
        (List<ulong> Cells, List<ulong> Changed) StampTemplateAt(VoxelTemplate template, long voxelX, long voxelY, long voxelZ, uint zone, int turns = 0);

        // ---- voxel ownership (10 Gather Ownership Info, 17 Cell Ownership Reusable) ---------------------------------

        /// <summary>
        /// ClientOwnershipRequest's store half: the cells whose cached version is current, and for the rest their
        /// version, grid (null = nobody built) and owner list.
        /// </summary>
        (List<ulong> UpToDate, List<(ulong Cell, uint Version, byte[] Raw, List<ulong> Owners)> Data) OwnershipQuery(uint zone,
            IReadOnlyList<(ulong Cell, uint Version)> cells);

        /// <summary>The voxels one player owns on a landscape, per cell, limited to those whose centre lies in <paramref name="box"/>.</summary>
        List<(ulong Cell, List<int> Indices)> OwnedBy(uint zone, ulong owner, VoxelBox box);

        /// <summary>Dev: voxels per owner in one cell.</summary>
        Dictionary<ulong, int> OwnershipCensus(uint zone, ulong cell);

        // ---- creature navigation (14 Area Iterator; OPEN-LEADS L133) ------------------------------------------------

        /// <summary>
        /// The coarse solidity of the stored voxels in a global dm box (<see cref="SolidityGrid"/>: 1.2 m × 0.4 m × 1.2 m
        /// blocks, written / solid bit sets, Sight.AddRuin's rule over the composite of layers 0-2): what Game/Pathing lays over a build site's
        /// walkable columns so creatures walk around player walls and props (and over a Chaos Cavern outpost players dug
        /// into). Called from a background thread of the zone, once per rebuild of a site (debounced); null when a remote
        /// service can't be reached (the zone keeps its old grid and asks again later).
        /// </summary>
        SolidityGrid SolidityOf(uint zone, VoxelBox box);

        // ---- ore veins (15 Spawn Vein, 16 Validate Cells) -----------------------------------------------------------

        /// <summary>
        /// World tick: generates the vein regions around the players (landscape, local position) and moves disturbed
        /// veins; returns the new or changed ore cells per landscape.
        /// </summary>
        IReadOnlyList<(uint Zone, List<ulong> Cells)> UpdateVeins(IEnumerable<(uint Zone, Vector3 Position)> players);

        /// <summary>Generates the vein regions around a position (before a manifest); returns the new ore cells.</summary>
        List<ulong> EnsureVeinsAround(uint zone, Vector3 local);

        /// <summary>Ore was mined in these cells: their veins move on after RESPAWN_TIME_DIRTY.</summary>
        void DisturbVeins(IEnumerable<ulong> cells, uint zone);

        /// <summary>
        /// A build site was placed (global dm box): the veins touching it spawn again elsewhere. Returns the changed ore
        /// cells (empty when no vein moved).
        /// </summary>
        List<ulong> ClearVeinsInSite(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max);

        /// <summary>Veins with a point within <paramref name="radius"/> metres of a local position, nearest first.</summary>
        List<(OreVeins.Vein Vein, Vector3 Nearest, float Distance)> VeinsNear(Vector3 position, float radius, uint zone);

        /// <summary>The nearest vein of one of <paramref name="items"/>: (item, where, distance), or null.</summary>
        (uint Item, Vector3 At, float Distance)? NearestVein(Vector3 position, float radius, ISet<uint> items, uint zone);

        /// <summary>The raw resource item an ore material gives, or 0.</summary>
        uint OreItemFor(ushort material);

        // ---- LOD copies (0-3 Cell <layer> Lod, 6 Composite Update Lod) ----------------------------------------------

        /// <summary>Builds the coarse copies of every landscape with edits (start-up, in the background).</summary>
        void BuildLodCopies();

        /// <summary>World tick: refreshes the copies of layers whose edits have gone quiet (in the background).</summary>
        void UpdateLodCopies();

        /// <summary>Dev "lodbuild": refreshes a landscape's copies now.</summary>
        void RefreshLodCopies(uint zone);

        /// <summary>
        /// Copies of (landscape, layer) changed after a refresh: (landscape, layer, cells with their new versions). Raised
        /// on a background thread; the zone sends its players a manifest note per copy.
        /// </summary>
        event Action<uint, uint, IReadOnlyList<(ulong Cell, uint Version)>> LodCopiesChanged;
    }
}
