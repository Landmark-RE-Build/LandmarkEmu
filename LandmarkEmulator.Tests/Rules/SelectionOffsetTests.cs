using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Where a selection's box is, and what each action takes from it (BuildingSelectionBox.cs; trace/0695/selection-offset.md,
    /// workflow wf_a6205152-f54, 2026-11-01). Per axis, G = cell · 40 + request origin, S = request size, v = 4 dm:
    /// <list type="bullet">
    /// <item>the box the player sees is [(G − ½)v, (G + S − ½)v], grid points G .. G + S − 1 [P: FUN_143794700,
    /// FUN_1436eec30, FUN_143c1a8a0];</item>
    /// <item>Delete / Cut erase exactly those grid points; the client's own local erase (FUN_1431608c0 + FUN_1440084e0)
    /// stops one layer short on the + faces, and the server must not copy it;</item>
    /// <item>a copy's grid starts one voxel lower (FUN_143c18800: origin − 1, size + 1), so copied props are measured from
    /// grid point 0 = (G − 1)v and a paste puts each back on the voxel it stood on, for every turn;</item>
    /// <item>the props taken are those whose body point (2 cm up their own up axis from the pivot) is in the visible box,
    /// half-open per axis (our tie rule [I], L250), and Delete, Cut, Copy, Get Inspired and the placed-design check all
    /// use that box at their call sites (driven through Building.Handle).</item>
    /// </list>
    /// Until 2026-11-01 the props boxes were up to 1.5 voxels past the + faces and pasted props landed one voxel low (two on
    /// the axes a turn flips).
    /// </summary>
    public class SelectionOffsetTests : IDisposable
    {
        private static int nextZone = 9700;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly FakeWorld world = new();
        private readonly List<Props.PlacedProp> placed = new();
        private static ulong nextGuid = 0x7F00_0000_0000_0000;
        private static ulong nextClaim = 0x7F55_0000;

        /// <summary>The selecting player (and owner of the test props).</summary>
        private const ulong Owner = 0x7F01;

        /// <summary>Another player: the owner of a neighbouring build site, who grants the selector nothing.</summary>
        private const ulong Neighbour = 0x7F02;

        private const uint PropA = 4242, PropB = 4243;

        private static readonly FieldInfo ChestsField = typeof(Chests).GetField("chestProps", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static IDictionary Kinds => (IDictionary)Private.Field(typeof(StoryTools), "kindCache");
        private readonly object previousChests;
        private readonly List<uint> addedKinds = new();
        private readonly List<ClaimModel> addedClaims = new();
        private readonly List<SiteTemplateInstanceModel> addedInstances = new();
        private bool touchedCell, setGround;
        private (object Loaded, object Settings)? previousTables;

        private static readonly FieldInfo StoryLinksLoaded = typeof(StoryTools).GetField("loaded", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly FieldInfo PropSettingDefinitions = typeof(PropSettings).GetField("definitions", BindingFlags.NonPublic | BindingFlags.Static)!;

        public SelectionOffsetTests()
        {
            // A take-down asks whether each prop is a chest and a creature spawner, from game tables that tests don't load:
            // none of them (restored on Dispose).
            previousChests = ChestsField.GetValue(null);
            ChestsField.SetValue(null, previousChests ?? new Dictionary<uint, uint>());
            lock (Kinds)
                foreach (uint id in new[] { PropA, PropB })
                    if (!Kinds.Contains(id))
                    {
                        Kinds[id] = null;
                        addedKinds.Add(id);
                    }
        }

        /// <summary>A cell near the landscape's origin, so local metres (floats) are exact to well under a millimetre.</summary>
        private static readonly ulong Cell = TerrainStore.CellId(TerrainEdits.Level, 159900, 130, 159800);

        /// <summary>The test selection: origin (10, 5, 20) in <see cref="Cell"/>, 4 × 3 × 2 voxels.</summary>
        private static readonly uint[] At = { 10, 5, 20 }, Size = { 4, 3, 2 };

        private static readonly double V = TerrainEdits.VoxelSize;

        public void Dispose()
        {
            lock (Private.Field(typeof(Props), "sync"))
                foreach (Props.PlacedProp p in placed)
                {
                    ((IList)Private.Field(typeof(Props), "props")).Remove(p);
                    ((IDictionary)Private.Field(typeof(Props), "byGuid")).Remove(p.Spawn.Guid);
                }
            lock (Private.Field(typeof(Claims), "sync"))
                foreach (ClaimModel claim in addedClaims)
                    ((List<ClaimModel>)Private.Field(typeof(Claims), "claims")).Remove(claim);
            if (addedInstances.Count > 0)
                lock (Private.Field(typeof(TemplateInstances), "sync"))
                    foreach (SiteTemplateInstanceModel row in addedInstances)
                        ((List<SiteTemplateInstanceModel>)Private.Call(typeof(TemplateInstances), "Loaded")).Remove(row);
            if (touchedCell)
                TerrainEdits.ClearCell(Cell, zone);
            if (setGround)
                TerrainHeights.TestGround = null;
            if (previousTables is (object loaded, object settings))
            {
                StoryLinksLoaded.SetValue(null, loaded);
                PropSettingDefinitions.SetValue(null, settings);
            }
            ChestsField.SetValue(null, previousChests);
            lock (Kinds)
                foreach (uint id in addedKinds)
                    Kinds.Remove(id);
            world.Dispose();
        }

        // ---- helpers -------------------------------------------------------------------------------------------------

        /// <summary>The global grid point of the selection's origin on each axis (G).</summary>
        private static long[] G()
        {
            (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(Cell);
            return new[] { cx * 40L + At[0], cy * 40L + At[1], cz * 40L + At[2] };
        }

        /// <summary>Local metres of a global point given in voxels (grid point g sits at g·v).</summary>
        private static Vector3 Local(double gx, double gy, double gz)
        {
            var o = TerrainStore.ZoneOrigin;
            return new Vector3((float)((gx * V - o.X) / 10), (float)((gy * V - o.Y) / 10), (float)((gz * V - o.Z) / 10));
        }

        /// <summary>A local point in global voxels.</summary>
        private static (double X, double Y, double Z) Voxels(Vector3 local)
        {
            (double x, double y, double z) = TerrainEdits.ToGlobal(local);
            return (x / V, y / V, z / V);
        }

        /// <summary>
        /// A placed prop at <paramref name="at"/> (local metres), upright unless turned by <paramref name="rotation"/>;
        /// <paramref name="free"/>: a free placement (a take-down refunds nothing, so no recipe table is asked).
        /// </summary>
        private Props.PlacedProp Place(Vector3 at, uint propId = PropA, Vector4 rotation = default, bool free = false)
        {
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            var p = new Props.PlacedProp
            {
                PropId = propId,
                OwnerGuid = Owner,
                Spawn = new PlayerUpdateAddLightweightProp
                {
                    Guid = guid, TransientId = (uint)guid, Position = new Vector4(at, 1f),
                    Rotation = rotation == default ? new Vector4(0f, 0f, 0f, 1f) : rotation   // the spawn's own default: upright
                },
                Zone = zone,
                Placed = true,
                Free = free
            };
            lock (Private.Field(typeof(Props), "sync"))
            {
                ((IList)Private.Field(typeof(Props), "props")).Add(p);
                ((IDictionary)Private.Field(typeof(Props), "byGuid"))[guid] = p;
            }
            placed.Add(p);
            return p;
        }

        /// <summary>A SelectionRequest (private) for the test box with an action.</summary>
        private static object Request(uint action)
        {
            object request = Activator.CreateInstance(Private.Nested(typeof(Building), "SelectionRequest"))!;
            void Set(string field, object value) => request.GetType().GetField(field)!.SetValue(request, value);
            Set("Action", action);
            Set("Cell", Cell);
            Set("X", At[0]); Set("Y", At[1]); Set("Z", At[2]);
            Set("SizeX", Size[0]); Set("SizeY", Size[1]); Set("SizeZ", Size[2]);
            return request;
        }

        private static bool Marked(byte[] mask, int index) => (mask[index >> 3] & (0x80 >> (index & 7))) != 0;

        /// <summary>
        /// A prop's rotation whose up axis (rotation · +Y) is <paramref name="normal"/>: a prop placed on a surface with
        /// that normal (Align to Surface).
        /// </summary>
        private static Vector4 UpAlong(Vector3 normal)
        {
            Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
            Quaternion q = axis.LengthSquared() < 1e-6f
                ? normal.Y > 0 ? Quaternion.Identity : Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI)
                : Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.UnitY, normal), -1f, 1f)));
            Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitY, q), normal) < 1e-5f);
            return new Vector4(q.X, q.Y, q.Z, q.W);
        }

        /// <summary>
        /// The selecting player in the test zone, standing in the test cell; with <paramref name="site"/> they own a build
        /// site over the whole cell (build, Get Inspired and take-down rights there). The zone's ground lies far below the
        /// cell, so a Delete / Cut finds only open air to erase (no baked height grid is read) and the props are what is
        /// tested.
        /// </summary>
        private WorldSession Selector(bool site)
        {
            if (EnvironmentServer.Instance == null)
                EnvironmentServer.Connect(new LocalEnvironmentServer((z, min, max) => new()));
            uint landscape = zone;
            TerrainHeights.TestGround = (x, z, z0) => z0 == landscape ? -100_000f : null;
            setGround = true;
            StandInForTables();
            WorldSession session = world.Session(Owner, "Selector");
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            (Vector3 min, Vector3 max) = TerrainStore.LocalBounds(Cell);
            Vector3 centre = (min + max) / 2;
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (centre.X, centre.Y, centre.Z);
            if (site)
            {
                (double ox, double oy, double oz) = TerrainStore.CellOrigin(Cell);
                double size = TerrainEdits.CellSize;
                AddClaim(Owner, (ox - 10, oy - 10, oz - 10), (ox + size + 10, oy + size + 10, oz + size + 10));
            }
            FakeWorld.Outgoing(session);
            return session;
        }

        /// <summary>
        /// What a take-down reads from the database, which tests don't have, stood in for until Dispose: the story-tool links
        /// and the prop settings (none). The props a test takes down are free placements, so no recipe table is read either.
        /// </summary>
        private void StandInForTables()
        {
            if (previousTables != null)
                return;
            previousTables = (StoryLinksLoaded.GetValue(null), PropSettingDefinitions.GetValue(null));
            StoryLinksLoaded.SetValue(null, true);
            PropSettingDefinitions.SetValue(null, PropSettingDefinitions.GetValue(null) ?? Activator.CreateInstance(PropSettingDefinitions.FieldType));
        }

        /// <summary>A build site (global decimetres) in the test zone; default access grants nothing (no Get Inspired).</summary>
        private ClaimModel AddClaim(ulong owner, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            var claim = new ClaimModel
            {
                Id = System.Threading.Interlocked.Increment(ref nextClaim), ZoneId = zone, OwnerGuid = owner,
                MinX = min.X, MinY = min.Y, MinZ = min.Z, MaxX = max.X, MaxY = max.Y, MaxZ = max.Z
            };
            lock (Private.Field(typeof(Claims), "sync"))
                ((List<ClaimModel>)Private.Field(typeof(Claims), "claims")).Add(claim);
            addedClaims.Add(claim);
            return claim;
        }

        private void RemoveClaim(ClaimModel claim)
        {
            lock (Private.Field(typeof(Claims), "sync"))
                ((List<ClaimModel>)Private.Field(typeof(Claims), "claims")).Remove(claim);
        }

        /// <summary>A placed Player Studio design's record on <paramref name="site"/> (global decimetres).</summary>
        private SiteTemplateInstanceModel AddInstance(ClaimModel site, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            var row = new SiteTemplateInstanceModel
            {
                Id = System.Threading.Interlocked.Increment(ref nextClaim), ZoneId = zone, SiteGuid = site.Id, TemplateGuid = 0x7F77,
                PlacedBy = site.OwnerGuid, MinX = min.X, MinY = min.Y, MinZ = min.Z, MaxX = max.X, MaxY = max.Y, MaxZ = max.Z
            };
            lock (Private.Field(typeof(TemplateInstances), "sync"))
                ((List<SiteTemplateInstanceModel>)Private.Call(typeof(TemplateInstances), "Loaded")).Add(row);
            addedInstances.Add(row);
            return row;
        }

        private void RemoveInstance(SiteTemplateInstanceModel row)
        {
            lock (Private.Field(typeof(TemplateInstances), "sync"))
                ((List<SiteTemplateInstanceModel>)Private.Call(typeof(TemplateInstances), "Loaded")).Remove(row);
        }

        /// <summary>
        /// ClientSelectionAreaRequest for the test box as the client writes it (FUN_14315ae20), listing
        /// <paramref name="cells"/>, with no blocks (as a Delete sends it; a Copy without blocks keeps no clipboard).
        /// </summary>
        private static byte[] Body(uint action, params ulong[] cells)
            => Wire.Write(w =>
            {
                w.Write((byte)0xFF);
                w.Write(action);
                w.Write(Cell);
                foreach (uint a in At) w.Write(a);
                foreach (uint s in Size) w.Write(s);
                w.Write(0u);                                        // props (the client sends none)
                for (int i = 0; i < 16; i++)
                    w.Write(i % 5 == 0 ? 1 : 0);                    // identity
                w.Write(0u);                                        // material map
                w.Write(0ul);                                       // design guid
                w.Write("");
                w.Write(0ul);                                       // claim guid
                w.Write((uint)cells.Length);
                foreach (ulong cell in cells)
                {
                    w.Write(cell);
                    w.Write(0x1111u);                               // layer-1 version before the client's action
                    w.Write(0x2222u);                               // and after
                }
                w.Write(0u);                                        // blocks
                w.Write(1u);                                        // paste mode
                w.Write((byte)0);                                   // remove voxels
                w.Write("");
                w.Write((byte)0);
            });

        /// <summary>The selection request's result code (VoxelConstruction Response: u8 sub 7, u32 request type 2, u32 result).</summary>
        private static uint ResultOf(IEnumerable<(ZoneMessageOpcode Opcode, byte[] Body)> sent)
        {
            byte[] body = sent.Single(p => p.Opcode == ZoneMessageOpcode.VoxelConstructionBase && p.Body[0] == 7).Body;
            return BitConverter.ToUInt32(body, 5);
        }

        /// <summary>A Copy of the test box listing no cells, so only the box itself is checked: its result code.</summary>
        private static uint CopyResult(WorldSession session)
        {
            Building.Handle(session, Building.AddTemporaryBlockSub, Body(Building.CopyAction));
            return ResultOf(FakeWorld.Outgoing(session));
        }

        /// <summary>The 48 quarter-turn rotations and mirrors as the paste request's 4 × 4 i32 matrix (row j: where clipboard axis j goes).</summary>
        private static IEnumerable<int[]> Turns(bool rotationsOnly)
        {
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            int[] parity = { 1, -1, -1, 1, 1, -1 };
            for (int p = 0; p < perms.Length; p++)
                for (int s = 0; s < 8; s++)
                {
                    int[] sign = { (s & 1) != 0 ? -1 : 1, (s & 2) != 0 ? -1 : 1, (s & 4) != 0 ? -1 : 1 };
                    if (rotationsOnly && parity[p] * sign[0] * sign[1] * sign[2] != 1)
                        continue;
                    var m = new int[16];
                    m[15] = 1;
                    for (int j = 0; j < 3; j++)
                        m[j * 4 + perms[p][j]] = sign[j];
                    yield return m;
                }
        }

        // ---- the visible box ---------------------------------------------------------------------------------------------

        [Fact]
        public void VisibleBox_FacesSitOnBlockFaces()
        {
            ((double X, double Y, double Z) min, (double X, double Y, double Z) max) = Building.VisibleBox(Cell, At[0], At[1], At[2], Size[0], Size[1], Size[2]);
            long[] g = G();
            Assert.Equal((g[0] - 0.5) * V, min.X);
            Assert.Equal((g[1] - 0.5) * V, min.Y);
            Assert.Equal((g[2] - 0.5) * V, min.Z);
            Assert.Equal((g[0] + Size[0] - 0.5) * V, max.X);
            Assert.Equal((g[1] + Size[1] - 0.5) * V, max.Y);
            Assert.Equal((g[2] + Size[2] - 0.5) * V, max.Z);

            // The client's own rounding of a dragged box into the request (FUN_143c1a8a0: origin = floor((min + 0.75v) / v
            // + 0.5), size = floor((max − min) / v + 0.75)) gives the request back from the visible box.
            double[] lo = { min.X, min.Y, min.Z }, hi = { max.X, max.Y, max.Z };
            for (int a = 0; a < 3; a++)
            {
                Assert.Equal(g[a], (long)Math.Floor((lo[a] + 0.75 * V) / V + 0.5));
                Assert.Equal(Size[a], (uint)Math.Floor((hi[a] - lo[a]) / V + 0.75));
            }

            // The local-metre form is the same box.
            (Vector3 lmin, Vector3 lmax) = Building.VisibleBoxLocal(Cell, At[0], At[1], At[2], Size[0], Size[1], Size[2]);
            Assert.True(Vector3.Distance(lmin, Local(g[0] - 0.5, g[1] - 0.5, g[2] - 0.5)) < 1e-4f);
            Assert.True(Vector3.Distance(lmax, Local(g[0] + Size[0] - 0.5, g[1] + Size[1] - 0.5, g[2] + Size[2] - 0.5)) < 1e-4f);
            // And a copy's grid point 0 is one voxel below the origin voxel on every axis.
            Assert.True(Vector3.Distance(Building.ClipboardGridZero(Cell, At[0], At[1], At[2]), Local(g[0] - 1, g[1] - 1, g[2] - 1)) < 1e-4f);
        }

        // ---- voxels: Delete / Cut ----------------------------------------------------------------------------------------

        [Fact]
        public void DeleteMasks_EraseTheVisibleBox_FirstAndLastLayerIn_NeighboursOut()
        {
            object[] args = { Request(Building.DeleteAction), 0L };
            var masks = (Dictionary<ulong, byte[]>)typeof(Building).GetMethod("DeleteMasks", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
            Assert.Equal((long)(Size[0] * Size[1] * Size[2]), (long)args[1]);
            byte[] mask = Assert.Single(masks).Value;   // the box lies inside the one cell
            int[] inside = { (int)At[0] + 1, (int)At[1] + 1, (int)At[2] };
            for (int a = 0; a < 3; a++)
            {
                int Index(int along)
                {
                    int[] p = (int[])inside.Clone();
                    p[a] = along;
                    return BlockData.Index(p[0], p[1], p[2]);
                }
                Assert.True(Marked(mask, Index((int)At[a])), $"axis {a}: G erased");
                Assert.True(Marked(mask, Index((int)(At[a] + Size[a] - 1))), $"axis {a}: G + S − 1 erased");
                Assert.False(Marked(mask, Index((int)At[a] - 1)), $"axis {a}: G − 1 kept");
                Assert.False(Marked(mask, Index((int)(At[a] + Size[a]))), $"axis {a}: G + S kept");
            }
        }

        /// <summary>
        /// The client's own local erase, as a model pinned from the 0.695 client (FUN_1431608c0 → VoxelFarm op 1 over
        /// [G·v, (G + S − 1)·v + 0.5 dm] (FUN_143c18b20 + DAT_144c41158), stamped by FUN_1440084e0 as [(G − ½)v,
        /// (G + S − 1.375)v]): grid points G .. G + S − 2. It is one layer short of the visible box on the + faces, and a
        /// selection one voxel thick erases nothing at all on the client. The server erases the visible box (DeleteMasks)
        /// and re-sends every listed cell, which replaces the client's short erase; copying the client's rule would leave
        /// the + face layer of every Delete / Cut standing.
        /// </summary>
        private static bool ClientErases(long g, long origin, long size) => g >= origin - 0.5 && g <= origin + size - 1.375;

        /// <summary>
        /// The server's erase (DeleteMasks, along x through the test box) against the client's model above, for sizes 1 to
        /// 6: the server takes every grid point of the visible box, the + face layer included, which the client's own erase
        /// leaves. The model is the decoded rule written down, not evidence of the client's behaviour (that is the trace);
        /// what this pins is that the server does not copy it.
        /// </summary>
        [Fact]
        public void ClientsOwnErase_IsOneLayerShortOnThePlusFaces_SoTheServerMustNotCopyIt()
        {
            MethodInfo deleteMasks = typeof(Building).GetMethod("DeleteMasks", BindingFlags.NonPublic | BindingFlags.Static)!;
            long[] g = G();
            long cellBase = g[0] - At[0];
            for (uint size = 1; size <= 6; size++)
            {
                object request = Request(Building.DeleteAction);
                request.GetType().GetField("SizeX")!.SetValue(request, size);
                object[] args = { request, 0L };
                byte[] mask = Assert.Single((Dictionary<ulong, byte[]>)deleteMasks.Invoke(null, args)!).Value;   // inside the one cell
                // The server's erase along x (DeleteMasks) and the client model over the same range, in global grid points.
                var server = Enumerable.Range(0, BlockData.Size).Where(x => Marked(mask, BlockData.Index(x, (int)At[1], (int)At[2])))
                    .Select(x => cellBase + x).ToList();
                var client = Enumerable.Range(0, BlockData.Size).Select(x => cellBase + x).Where(p => ClientErases(p, g[0], size)).ToList();
                Assert.Equal(Enumerable.Range(0, (int)size).Select(i => g[0] + i), server);   // the server: G .. G + S − 1
                Assert.Equal(server.Take((int)size - 1), client);                              // the client: all of it but the last layer
                Assert.Contains(g[0] + size - 1, server);                                      // the server takes the + face layer
                Assert.DoesNotContain(g[0] + size - 1, client);                                // the client's own erase leaves it
                ((double X, double Y, double Z) _, (double X, double Y, double Z) max) = Building.VisibleBox(Cell, At[0], At[1], At[2], size, Size[1], Size[2]);
                Assert.True((g[0] + size - 1) * V < max.X, $"size {size}: the + face layer lies inside the box the player sees");
            }
            // A one-voxel-thick selection: the client erases nothing locally; the server's resend is all the player sees.
            Assert.False(Enumerable.Range(90, 30).Any(p => ClientErases(p, 100, 1)));
        }

        // ---- props: which ones a selection takes ------------------------------------------------------------------------

        /// <summary>
        /// Upright props (no rotation: up +Y): the box tests the point 2 cm above the pivot, half-open per axis. On x and z
        /// that is the pivot's own half-open split; on y a prop standing on the floor is in and one on the top out even
        /// sunk up to 3 cm into the block it stands on.
        /// </summary>
        [Fact]
        public void PropsBox_TakesTheCentresOfTheFirstAndLastLayer_LeavesTheNeighbours_AndFacesSplitHalfOpen()
        {
            long[] g = G();
            double[] mid = { g[0] + 1, g[1] + 1, g[2] + 0.5 };   // inside on the other two axes
            var expected = new Dictionary<Props.PlacedProp, bool>();
            float tol = Building.PivotTieTolerance;
            Assert.Equal(2 * tol, Building.PropBodyNudge, 1e-7f);
            for (int a = 0; a < 3; a++)
            {
                bool up = a == 1;   // the axis the body point is lifted along
                Vector3 On(double along, float nudge = 0f)
                {
                    double[] p = (double[])mid.Clone();
                    p[a] = along;
                    Vector3 at = Local(p[0], p[1], p[2]);
                    Vector3 axis = a == 0 ? Vector3.UnitX : a == 1 ? Vector3.UnitY : Vector3.UnitZ;
                    return at + axis * nudge;
                }
                expected[Place(On(g[a]))] = true;                              // centre of G
                expected[Place(On(g[a] + Size[a] - 1))] = true;                // centre of G + S − 1
                expected[Place(On(g[a] - 1))] = false;                         // centre of G − 1
                expected[Place(On(g[a] + Size[a]))] = false;                   // centre of G + S
                // Ties: a pivot on the − face (standing on the box's floor) is in, on the + face (standing on its top) out.
                expected[Place(On(g[a] - 0.5))] = true;
                expected[Place(On(g[a] - 0.5, -tol / 2))] = true;              // within the tolerance of the − face
                expected[Place(On(g[a] + Size[a] - 0.5))] = false;
                expected[Place(On(g[a] + Size[a] - 0.5, -tol / 2))] = false;   // within the tolerance of the + face
                // 2 cm off a face: on x / z clearly outside / inside; on y sunk 2 cm into the block under the floor (still
                // standing on the floor: in) or into the top layer (still standing on the top: out).
                expected[Place(On(g[a] - 0.5, -2 * tol))] = up;
                expected[Place(On(g[a] + Size[a] - 0.5, -2 * tol))] = !up;
                if (up)
                {
                    // 4 cm: past the 3 cm the 2 cm lift allows, the pivot's side decides again.
                    expected[Place(On(g[a] - 0.5, -4 * tol))] = false;
                    expected[Place(On(g[a] + Size[a] - 0.5, -4 * tol))] = true;
                }
            }
            // Turned about y only (up stays +Y): on the floor still in.
            expected[Place(Local(mid[0], g[1] - 0.5, mid[2]), rotation: new Vector4(0, MathF.Sin(0.6f), 0, MathF.Cos(0.6f)))] = true;
            (Vector3 min, Vector3 max) = Building.VisibleBoxLocal(Cell, At[0], At[1], At[2], Size[0], Size[1], Size[2]);
            HashSet<Props.PlacedProp> taken = Props.StandingIn(zone, min, max).ToHashSet();
            foreach ((Props.PlacedProp prop, bool inside) in expected)
                Assert.True(taken.Contains(prop) == inside,
                    $"prop at {prop.Spawn.Position} (box {min} .. {max}) should be {(inside ? "taken" : "left")}");
            Assert.Equal(expected.Count(e => e.Value), taken.Count);
        }

        /// <summary>
        /// Props on a face go with the side their body is on (the point 2 cm up their own up axis from the pivot,
        /// Building.PropBodyPoint): with the box drawn flush with a building's outer walls, signs on all four walls (their
        /// up axis the wall's outward normal, Align to Surface) are left, as props on its roof are; with it flush with a
        /// room's interior, torches on all four inner walls and a lamp under the ceiling are taken. Before the review of
        /// 2026-11-01 the pivot alone decided: − faces always in, + faces always out.
        /// </summary>
        [Fact]
        public void PropsOnAFace_GoWithTheSideTheirBodyIsOn()
        {
            long[] g = G();
            double[] lo = { g[0] - 0.5, g[1] - 0.5, g[2] - 0.5 }, hi = { g[0] + Size[0] - 0.5, g[1] + Size[1] - 0.5, g[2] + Size[2] - 0.5 };
            double[] mid = { g[0] + 1, g[1] + 1, g[2] + 0.5 };
            var expected = new Dictionary<Props.PlacedProp, string>();
            foreach (int a in new[] { 0, 2 })
            {
                Vector3 axis = a == 0 ? Vector3.UnitX : Vector3.UnitZ;
                foreach (bool plus in new[] { false, true })
                {
                    double[] p = (double[])mid.Clone();
                    p[a] = plus ? hi[a] : lo[a];
                    Vector3 at = Local(p[0], p[1], p[2]);
                    Vector3 outward = plus ? axis : -axis;
                    expected[Place(at, rotation: UpAlong(outward))] = "out: a sign on the outside of a wall";
                    expected[Place(at, rotation: UpAlong(-outward))] = "in: a torch on the inside of a wall";
                }
            }
            expected[Place(Local(mid[0], hi[1], mid[2]), rotation: UpAlong(-Vector3.UnitY))] = "in: a lamp hanging from the ceiling just above the box";
            expected[Place(Local(mid[0], lo[1], mid[2]), rotation: UpAlong(-Vector3.UnitY))] = "out: a lamp hanging under the box's floor";
            expected[Place(Local(mid[0], lo[1], mid[2]))] = "in: an upright prop on the floor";
            expected[Place(Local(mid[0], hi[1], mid[2]))] = "out: an upright prop on the top";

            (Vector3 min, Vector3 max) = Building.VisibleBoxLocal(Cell, At[0], At[1], At[2], Size[0], Size[1], Size[2]);
            HashSet<Props.PlacedProp> taken = Props.StandingIn(zone, min, max).ToHashSet();
            foreach ((Props.PlacedProp prop, string what) in expected)
                Assert.True(taken.Contains(prop) == what.StartsWith("in"), $"{what}: prop at {prop.Spawn.Position}, up {Vector3.Transform(Vector3.UnitY,
                    new Quaternion(prop.Spawn.Rotation.X, prop.Spawn.Rotation.Y, prop.Spawn.Rotation.Z, prop.Spawn.Rotation.W))}");
            // The body point itself: 2 cm along the up axis; a zero quaternion is upright.
            Vector3 pivot = Local(mid[0], mid[1], mid[2]);
            Assert.True(Vector3.Distance(Building.PropBodyPoint(pivot, UpAlong(Vector3.UnitX)), pivot + Vector3.UnitX * Building.PropBodyNudge) < 1e-5f);
            Assert.True(Vector3.Distance(Building.PropBodyPoint(pivot, default), pivot + Vector3.UnitY * Building.PropBodyNudge) < 1e-5f);
        }

        /// <summary>
        /// The copy's props (PropsInSelection: Copy, Cut, Save Design) and StandingIn over the same visible box (what
        /// DisassembleInBox takes down) agree prop for prop, with offsets from grid point 0. That HandleDeleteOrCut passes
        /// that box to the take-down (one line for Delete and Cut) and that Get Inspired and the placed-design check use it
        /// are checked through Building.Handle below; a Cut through Handle is not driven here, as its ClipboardData reads
        /// the tint table from the game data files.
        /// </summary>
        [Fact]
        public void CutAndCopy_TakeTheSameProps_MeasuredFromGridPointZero()
        {
            long[] g = G();
            Place(Local(g[0], g[1], g[2]));                                     // on the origin voxel
            Place(Local(g[0] + 3, g[1] + 2, g[2] + 1));                         // on the last voxel
            Place(Local(g[0] + 1, g[1] - 0.5, g[2] + 1));                       // on the box's floor
            Place(Local(g[0] + 1, g[1] + 2.5, g[2] + 1));                       // on its top: left
            Place(Local(g[0] + 4, g[1] + 1, g[2]));                             // one past the + x face: left
            Place(Local(g[0] - 1, g[1] + 1, g[2]));                             // one past the − x face: left

            WorldSession session = world.Session(Owner, "Selector");
            var character = (CharacterModel)typeof(WorldSession)
                .GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            character.ZoneId = zone;

            // The copy (PropsInSelection) and StandingIn over the same visible box agree prop for prop.
            var copied = (List<Props.DesignProp>)Private.Call(typeof(Building), "PropsInSelection", session, Request(Building.CutAction));
            (Vector3 min, Vector3 max) = Building.VisibleBoxLocal(Cell, At[0], At[1], At[2], Size[0], Size[1], Size[2]);
            List<Props.PlacedProp> taken = Props.StandingIn(zone, min, max);
            Assert.Equal(3, copied.Count);
            Vector3 zero = Building.ClipboardGridZero(Cell, At[0], At[1], At[2]);
            Assert.Equal(taken.Select(p => new Vector3(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z) - zero).OrderBy(o => (o.X, o.Y, o.Z)),
                copied.Select(c => c.Offset).OrderBy(o => (o.X, o.Y, o.Z)));
            // Offsets from grid point 0 = (G − 1)v: the prop on the origin voxel is one voxel (0.4 m) in on every axis.
            float step = (float)(V / 10);
            Assert.Contains(copied, c => Vector3.Distance(c.Offset, new Vector3(step, step, step)) < 1e-4f);
        }

        // ---- the boxes at their call sites (Building.Handle) ------------------------------------------------------------

        /// <summary>
        /// A Delete through Building.Handle takes down the props standing in the visible box: the one on the box's floor
        /// and the one in the − half of the origin voxel's block go, the one on its top and the one in the − half of block
        /// G + S on x stay. The box before 2026-11-01, [G·v, (G + S)·v], did each of those the other way round.
        /// </summary>
        [Fact]
        public void DeleteKey_TakesDownThePropsStandingInTheVisibleBox()
        {
            WorldSession session = Selector(site: true);
            touchedCell = true;
            long[] g = G();
            double x = g[0] + 1, y = g[1] + 1, z = g[2] + 0.5;   // inside on the other axes
            var expected = new Dictionary<Props.PlacedProp, string>
            {
                [Place(Local(x, g[1] - 0.5, z), free: true)] = "taken: standing on the box's floor",
                [Place(Local(x, g[1] + Size[1] - 0.5, z), free: true)] = "left: standing on its top",
                [Place(Local(g[0] - 0.25, y, z), free: true)] = "taken: in the − x half of the origin voxel's block",
                [Place(Local(g[0] + Size[0] - 0.25, y, z), free: true)] = "left: in the − x half of block G + S, past the + x face",
                [Place(Local(x, y, z), free: true)] = "taken: on a voxel inside",
            };
            Building.Handle(session, Building.AddTemporaryBlockSub, Body(Building.DeleteAction, Cell));
            Assert.Equal(0u, ResultOf(FakeWorld.Outgoing(session)));
            foreach ((Props.PlacedProp prop, string what) in expected)
                Assert.True((Props.Find(prop.Spawn.Guid) == null) == what.StartsWith("taken"), $"{what}: prop at {prop.Spawn.Position}");
        }

        /// <summary>
        /// Get Inspired (MayCopy) is checked over the visible box: a neighbour's site without it that starts at (G + S)·v
        /// on x, past the + face, does not stop a Copy (the old box [G·v, (G + S + 1)·v] reached it: 0xE); one that ends at
        /// (G − ¼)·v, inside the block of grid point G, does (the old box started at G·v and missed it). The request lists
        /// no cells, so only the box is checked.
        /// </summary>
        [Fact]
        public void Copy_ChecksGetInspiredOverTheVisibleBox()
        {
            WorldSession session = Selector(site: false);
            long[] g = G();
            (double ox, double oy, double oz) = TerrainStore.CellOrigin(Cell);
            double size = TerrainEdits.CellSize;
            Assert.Equal(0u, CopyResult(session));   // open land

            ClaimModel past = AddClaim(Neighbour, ((g[0] + Size[0]) * V, oy - 10, oz - 10), ((g[0] + Size[0] + 10) * V, oy + size + 10, oz + size + 10));
            Assert.Equal(0u, CopyResult(session));
            RemoveClaim(past);

            AddClaim(Neighbour, ((g[0] - 10) * V, oy - 10, oz - 10), ((g[0] - 0.25) * V, oy + size + 10, oz + size + 10));
            Assert.Equal(Building.InsufficientPermissions, CopyResult(session));
        }

        /// <summary>
        /// The placed Player Studio design check (CoversTemplateInstance) uses the visible box too: a design on a neighbour's
        /// site that starts at (G + S)·v on x does not stop a Copy (the old box reached it: 0x1A), one that ends at
        /// (G − ¼)·v does.
        /// </summary>
        [Fact]
        public void Copy_ChecksPlacedDesignsOverTheVisibleBox()
        {
            WorldSession session = Selector(site: false);
            long[] g = G();
            (double ox, double oy, double oz) = TerrainStore.CellOrigin(Cell);
            double size = TerrainEdits.CellSize;
            // The neighbour's site lies well away from the box (its own Get Inspired is not what is checked here).
            ClaimModel site = AddClaim(Neighbour, (ox + 5000, oy, oz), (ox + 5100, oy + 100, oz + 100));

            SiteTemplateInstanceModel past = AddInstance(site, ((g[0] + Size[0]) * V, oy - 10, oz - 10), ((g[0] + Size[0] + 5) * V, oy + size + 10, oz + size + 10));
            Assert.Equal(0u, CopyResult(session));
            RemoveInstance(past);

            AddInstance(site, ((g[0] - 5) * V, oy - 10, oz - 10), ((g[0] - 0.25) * V, oy + size + 10, oz + size + 10));
            Assert.Equal(Building.OnlyClaimOwnerTemplateInstances, CopyResult(session));
        }

        // ---- props: copy + paste ---------------------------------------------------------------------------------------

        /// <summary>
        /// Copies a prop standing on clipboard voxel k (the copy grid point of global voxel G − 1 + k) the way the server
        /// does (Props.InBox from the visible box, offsets from grid point 0), and a clipboard holding just that voxel as
        /// the client's copy grid would (grid point 0 = G − 1).
        /// </summary>
        private (Props.DesignProp Centre, Props.DesignProp OnTop, Building.Clipboard Clipboard) CopyOneVoxel(int[] k)
        {
            long[] g = G();
            double[] at = { g[0] - 1 + k[0], g[1] - 1 + k[1], g[2] - 1 + k[2] };
            Place(Local(at[0], at[1], at[2]));                 // on the voxel's centre
            Place(Local(at[0], at[1] + 0.5, at[2]), 4243);     // standing on its top face
            (Vector3 min, Vector3 max) = Building.VisibleBoxLocal(Cell, At[0], At[1], At[2], Size[0], Size[1], Size[2]);
            List<Props.DesignProp> props = Props.InBox(zone, min, max, Building.ClipboardGridZero(Cell, At[0], At[1], At[2]));
            Assert.Equal(2, props.Count);
            var block = new BlockData();
            int i = BlockData.Index(k[0], k[1], k[2]);
            block.Flags[i] = 1;
            block.Materials[i] = 26;
            return (props.Single(p => p.PropId == 4242), props.Single(p => p.PropId == 4243), new Building.Clipboard(block, Size[0], Size[1], Size[2]));
        }

        /// <summary>The single solid voxel of a paste, in global voxels.</summary>
        private static (long X, long Y, long Z) SolidOf(Dictionary<ulong, List<TerrainEdits.VoxelValue>> byCell)
        {
            var solids = byCell.SelectMany(c => c.Value.Where(v => (v.Flags & 1) != 0 && v.Material != 0).Select(v => (c.Key, v.Index))).ToList();
            (ulong cell, int index) = Assert.Single(solids);
            (_, uint cx, uint cy, uint cz) = TerrainStore.Decode(cell);
            return (cx * 40L + index / 40 % 40, cy * 40L + index % 40, cz * 40L + index / 1600);
        }

        private static void AssertAt((double X, double Y, double Z) expected, Vector3 local, string what)
        {
            (double x, double y, double z) = Voxels(local);
            double off = Math.Max(Math.Abs(x - expected.X), Math.Max(Math.Abs(y - expected.Y), Math.Abs(z - expected.Z)));
            Assert.True(off < 1e-3, $"{what}: at ({x:0.###}, {y:0.###}, {z:0.###}) voxels, expected ({expected.X}, {expected.Y}, {expected.Z})");
        }

        [Fact]
        public void OnGridPaste_PropFollowsItsVoxel_ForEveryTurn()
        {
            (Props.DesignProp centre, Props.DesignProp onTop, Building.Clipboard clip) = CopyOneVoxel(new[] { 2, 1, 2 });   // global G + (1, 0, 1)
            long[] origin = { 159901L * 40 + 7, 131L * 40 + 9, 159801L * 40 + 6 };
            int turns = 0;
            foreach (int[] m in Turns(rotationsOnly: false))
            {
                var name = $"turn [{m[0]} {m[1]} {m[2]}; {m[4]} {m[5]} {m[6]}; {m[8]} {m[9]} {m[10]}]";
                long[] b = (long[])origin.Clone();
                (long X, long Y, long Z) voxel = SolidOf(Building.TurnedVoxels(clip, m, b[0], b[1], b[2]));
                AssertAt(voxel, Building.PastedPropAt(centre, m, b[0], b[1], b[2]), name + ", prop on the centre");
                // The prop on the voxel's top face lands on the face the turn takes it to: + ½ row 1.
                AssertAt((voxel.X + 0.5 * m[4], voxel.Y + 0.5 * m[5], voxel.Z + 0.5 * m[6]), Building.PastedPropAt(onTop, m, b[0], b[1], b[2]),
                    name + ", prop on the top face");
                turns++;
            }
            Assert.Equal(48, turns);
        }

        [Fact]
        public void OnGridPaste_PropsTurnTheWayTheVoxelsDo()
        {
            // A direction d of the copy goes to Σ d_j·row_j (the voxels' map, Building.Dot): the props' turn must do the same.
            // Until 2026-11-01 the quaternion came from the matrix's columns: quarter turns faced the opposite way.
            int turns = 0;
            foreach (int[] m in Turns(rotationsOnly: true))
            {
                Quaternion turn = Building.PasteTurn(m);
                for (int j = 0; j < 3; j++)
                {
                    Vector3 d = j == 0 ? Vector3.UnitX : j == 1 ? Vector3.UnitY : Vector3.UnitZ;
                    Vector3 got = Vector3.Transform(d, turn);
                    var want = new Vector3(m[j * 4], m[j * 4 + 1], m[j * 4 + 2]);
                    Assert.True(Vector3.Distance(got, want) < 1e-5f, $"turn [{string.Join(",", m.Take(11))}]: axis {j} → {got}, want {want}");
                }
                turns++;
            }
            Assert.Equal(24, turns);
        }

        /// <summary>
        /// A mirror (det −1, the V flip) has no quaternion: PasteTurn gives a rotation, so no mirror's map is reproduced, and a
        /// plane mirror (one axis negated, or two swapped: symmetric, trace 1) gives the identity, so each prop keeps the
        /// facing it was copied with while its position is mirrored (docs/gameplay.md). What a mirrored prop should face is
        /// open (its own symmetry plane is not traced).
        /// </summary>
        [Fact]
        public void OnGridPaste_AMirrorMovesTheProps_ButCannotMirrorThem()
        {
            int mirrors = 0, planes = 0;
            foreach (int[] m in Turns(rotationsOnly: false))
            {
                int det = m[0] * (m[5] * m[10] - m[6] * m[9]) - m[1] * (m[4] * m[10] - m[6] * m[8]) + m[2] * (m[4] * m[9] - m[5] * m[8]);
                if (det == 1)
                    continue;
                Quaternion turn = Quaternion.Normalize(Building.PasteTurn(m));
                bool same = true;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 d = j == 0 ? Vector3.UnitX : j == 1 ? Vector3.UnitY : Vector3.UnitZ;
                    same &= Vector3.Distance(Vector3.Transform(d, turn), new Vector3(m[j * 4], m[j * 4 + 1], m[j * 4 + 2])) < 1e-4f;
                }
                Assert.False(same, $"mirror [{string.Join(",", m.Take(11))}]: a rotation cannot be a mirror");
                bool symmetric = m[1] == m[4] && m[2] == m[8] && m[6] == m[9];
                if (symmetric && m[0] + m[5] + m[10] == 1)
                {
                    Assert.True(MathF.Abs(MathF.Abs(turn.W) - 1f) < 1e-5f, $"plane mirror [{string.Join(",", m.Take(11))}]: {turn}, want the identity");
                    planes++;
                }
                mirrors++;
            }
            Assert.Equal(24, mirrors);
            Assert.Equal(9, planes);
        }

        [Fact]
        public void OffGridPaste_PropFollowsItsVoxel_AndMatchesTheOnGridPaste()
        {
            (Props.DesignProp centre, Props.DesignProp onTop, Building.Clipboard clip) = CopyOneVoxel(new[] { 1, 2, 2 });   // global G + (0, 1, 1)
            long[] b = { 159901L * 40 + 7, 131L * 40 + 9, 159801L * 40 + 6 };
            // Identity off-grid paste at a whole-voxel origin: grid point 0 at the origin, voxel and prop together.
            float[] identity = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
            var byCell = Building.OffGridVoxels(clip, identity, b[0] * V, b[1] * V, b[2] * V, out _, out _, out _, out _, out _, out _);
            (long X, long Y, long Z) voxel = SolidOf(byCell);
            Assert.Equal((b[0] + 1, b[1] + 2, b[2] + 2), voxel);
            AssertAt(voxel, Building.OffGridPropAt(centre, identity, b[0] * V, b[1] * V, b[2] * V), "off-grid identity, prop on the centre");
            AssertAt((voxel.X, voxel.Y + 0.5, voxel.Z), Building.OffGridPropAt(onTop, identity, b[0] * V, b[1] * V, b[2] * V),
                "off-grid identity, prop on the top face");
            // A quarter-turn transform at a whole-voxel origin puts the props where the on-grid paste puts them.
            foreach (int[] m in Turns(rotationsOnly: false))
            {
                float[] t = { m[0], m[1], m[2], 0, m[4], m[5], m[6], 0, m[8], m[9], m[10], 0, 0, 0, 0, 1 };
                foreach (Props.DesignProp prop in new[] { centre, onTop })
                    Assert.True(Vector3.Distance(Building.OffGridPropAt(prop, t, b[0] * V, b[1] * V, b[2] * V), Building.PastedPropAt(prop, m, b[0], b[1], b[2])) < 1e-3f,
                        $"turn [{string.Join(",", m.Take(11))}]: off-grid and on-grid props differ");
            }
        }

        // ---- the request's cell versions ---------------------------------------------------------------------------------

        [Fact]
        public void SelectionRequest_KeepsEachCellsVersionsBeforeAndAfter()
        {
            ulong other = TerrainStore.CellId(TerrainEdits.Level, 159900, 130, 159801);
            byte[] body = Wire.Write(w =>
            {
                w.Write((byte)0xFF);
                w.Write(Building.DeleteAction);
                w.Write(Cell);
                foreach (uint a in At) w.Write(a);
                foreach (uint s in Size) w.Write(s);
                w.Write(0u);                                        // props
                for (int i = 0; i < 16; i++)
                    w.Write(i % 5 == 0 ? 1 : 0);                    // identity
                w.Write(0u);                                        // material map
                w.Write(0ul);
                w.Write("");
                w.Write(0ul);
                w.Write(2u);
                w.Write(Cell); w.Write(0x1111u); w.Write(0x2222u);
                w.Write(other); w.Write(0x3333u); w.Write(0x4444u);
                w.Write(0u);                                        // blocks
                w.Write(1u);
                w.Write((byte)0);
                w.Write("");
                w.Write((byte)0);
            });
            object request = Private.Call(typeof(Building), "ReadSelection", body);
            var versions = (Dictionary<ulong, (uint Before, uint After)>)request.GetType().GetField("CellVersions")!.GetValue(request);
            Assert.Equal((0x1111u, 0x2222u), versions[Cell]);
            Assert.Equal((0x3333u, 0x4444u), versions[other]);
            Assert.Equal(new List<ulong> { Cell, other }, (List<ulong>)request.GetType().GetField("Cells")!.GetValue(request));
        }
    }
}
