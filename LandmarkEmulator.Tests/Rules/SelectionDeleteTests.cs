using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The Selection tool's Delete key (ClientSelectionAreaRequest action 2) and Cut (1) (BuildingDelete.cs;
    /// trace/0695/voxdel-*.md; player report 2026-10-31: deleted voxels came back after a paste or after placing voxels).
    /// The client erases the box itself and sends no voxels; the server used to take both for a Copy and erase nothing.
    /// </summary>
    public class SelectionDeleteTests : IDisposable
    {
        private static int nextZone = 9600;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly FakeWorld world = new();
        private readonly List<ulong> touched = new();
        private readonly List<ClaimModel> savedClaims;
        private readonly List<WorldSession> players = (List<WorldSession>)Private.Field(typeof(PlayerManager), "players");
        private WorldSession joined;

        private const ushort Stone = 7;
        private static readonly ulong Cell = TerrainStore.CellId(TerrainEdits.Level, 3000, 1000, 3000);

        public SelectionDeleteTests()
        {
            if (EnvironmentServer.Instance == null)
                EnvironmentServer.Connect(new LocalEnvironmentServer((z, min, max) => new()));
            var claims = (List<ClaimModel>)Private.Field(typeof(Claims), "claims");
            lock (Private.Field(typeof(Claims), "sync"))
                savedClaims = claims.ToList();
        }

        public void Dispose()
        {
            var claims = (List<ClaimModel>)Private.Field(typeof(Claims), "claims");
            lock (Private.Field(typeof(Claims), "sync"))
            {
                claims.Clear();
                claims.AddRange(savedClaims);
            }
            if (joined != null)
                lock (Private.Field(typeof(PlayerManager), "sync"))
                    players.Remove(joined);
            foreach (ulong cell in touched.Distinct())
                TerrainEdits.ClearCell(cell, zone);
            world.Dispose();
        }

        /// <summary>A player standing in the test cell, in the player list (cell broadcasts reach them).</summary>
        private WorldSession Builder(ulong guid, bool site)
        {
            WorldSession session = world.Session(guid, "Builder" + (guid & 0xFF));
            var character = (CharacterModel)typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            (System.Numerics.Vector3 min, System.Numerics.Vector3 max) = TerrainStore.LocalBounds(Cell);
            System.Numerics.Vector3 centre = (min + max) / 2;
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (centre.X, centre.Y, centre.Z);
            lock (Private.Field(typeof(PlayerManager), "sync"))
                players.Add(session);
            joined = session;
            if (site)
            {
                (double ox, double oy, double oz) = TerrainStore.CellOrigin(Cell);
                double size = TerrainEdits.CellSize;
                var claims = (List<ClaimModel>)Private.Field(typeof(Claims), "claims");
                lock (Private.Field(typeof(Claims), "sync"))
                    claims.Add(new ClaimModel
                    {
                        Id = 0x7700 + guid, ZoneId = zone, OwnerGuid = guid,
                        MinX = ox - 10, MinY = oy - 10, MinZ = oz - 10, MaxX = ox + size + 10, MaxY = oy + size + 10, MaxZ = oz + size + 10
                    });
            }
            FakeWorld.Outgoing(session);
            return session;
        }

        /// <summary>A wall of placed stone in the cell: x 10..14, y 5..9, z 20.</summary>
        private List<int> Wall()
        {
            var values = new List<TerrainEdits.VoxelValue>();
            for (int x = 10; x <= 14; x++)
                for (int y = 5; y <= 9; y++)
                    values.Add(new TerrainEdits.VoxelValue(BlockData.Index(x, y, 20), 3, Stone, BlockData.CentreOffset, BlockData.CentreOffset, BlockData.CentreOffset));
            TerrainEdits.Write(Cell, values, zone);
            touched.Add(Cell);
            return values.Select(v => v.Index).ToList();
        }

        /// <summary>ClientSelectionAreaRequest as the client writes it (FUN_14315ae20), with the box in the test cell.</summary>
        private static byte[] Selection(uint action, (uint X, uint Y, uint Z) origin, (uint X, uint Y, uint Z) size, params ulong[] cells)
            => Wire.Write(w =>
            {
                w.Write((byte)0xFF);
                w.Write(action);
                w.Write(Cell);
                w.Write(origin.X); w.Write(origin.Y); w.Write(origin.Z);
                w.Write(size.X); w.Write(size.Y); w.Write(size.Z);
                w.Write(0u);                                       // props
                for (int i = 0; i < 16; i++)
                    w.Write(i % 5 == 0 ? 1 : 0);                   // identity rotation
                w.Write(0u);                                       // material map
                w.Write(0ul);                                      // design guid
                w.Write("");
                w.Write(0ul);                                      // claim guid
                w.Write((uint)cells.Length);
                foreach (ulong cell in cells)
                {
                    w.Write(cell);
                    w.Write(0x1111u);                              // layer-1 version before the client's erase
                    w.Write(0x2222u);                              // and after
                }
                w.Write(0u);                                       // blocks (a Delete sends none)
                w.Write(1u);                                       // paste mode
                w.Write((byte)0);                                  // remove voxels
                w.Write("");
                w.Write((byte)0);
            });

        private static uint ResultOf(IEnumerable<(ZoneMessageOpcode Opcode, byte[] Body)> sent)
        {
            byte[] body = sent.Single(p => p.Opcode == ZoneMessageOpcode.VoxelConstructionBase && p.Body[0] == 7).Body;
            return BitConverter.ToUInt32(body, 5);   // u8 sub 7, u32 request type 2, u32 result
        }

        private List<TerrainEdits.VoxelValue> Stored(IEnumerable<int> indices)
        {
            var mask = new byte[BlockData.Count / 8];
            foreach (int i in indices)
                mask[i >> 3] |= (byte)(0x80 >> (i & 7));
            return TerrainEdits.Read(Cell, mask, zone);
        }

        [Fact]
        public void DeleteKey_ErasesTheBoxOnTheServer_AndSendsTheBuilderTheResult()
        {
            WorldSession session = Builder(0x7100_0000_0000_0031, site: true);
            List<int> wall = Wall();

            // The whole wall but its top row (y 9); the client also lists the cell next to the box (its one-voxel margin).
            (uint level, uint cx, uint cy, uint cz) = TerrainStore.Decode(Cell);
            ulong margin = TerrainStore.CellId(level, cx, cy, cz + 1);
            Building.Handle(session, Building.AddTemporaryBlockSub, Selection(Building.DeleteAction, (10, 5, 20), (5, 4, 1), Cell, margin));

            List<TerrainEdits.VoxelValue> after = Stored(wall);
            Assert.All(after.Where(v => v.Index % BlockData.Size < 9), v => Assert.Equal((1, (ushort)0), ((int)(v.Flags & 1), v.Material)));
            Assert.All(after.Where(v => v.Index % BlockData.Size == 9), v => Assert.Equal(Stone, v.Material));

            var sent = FakeWorld.Outgoing(session);
            Assert.Equal(0u, ResultOf(sent));
            // The cell is answered first (it is pending on the client), then the server's cell reaches the deleter too.
            int answer = sent.FindLastIndex(p => p.Opcode == ZoneMessageOpcode.TerrainCellClientCellUpdateResponse);
            int update = sent.FindIndex(p => p.Opcode == ZoneMessageOpcode.TerrainCellCellsUpdate);
            Assert.True(answer >= 0 && update > answer, string.Join(", ", sent.Select(p => p.Opcode)));
            // Every listed cell goes back to the deleter, the untouched margin cell too (the client wrote air there as well).
            Assert.Equal(2, sent.Count(p => p.Opcode == ZoneMessageOpcode.TerrainCellCellsUpdate));
            // Not a copy: no ClipboardData (sub 6), the paste clipboard is left alone.
            Assert.False(sent.Any(p => p.Opcode == ZoneMessageOpcode.VoxelConstructionBase && p.Body[0] == 6));
        }

        [Fact]
        public void DeleteKey_WithoutBuildPermission_IsTakenBackOnTheClient()
        {
            WorldSession session = Builder(0x7100_0000_0000_0032, site: false);
            List<int> wall = Wall();

            Building.Handle(session, Building.AddTemporaryBlockSub, Selection(Building.DeleteAction, (10, 5, 20), (5, 5, 1), Cell));

            Assert.All(Stored(wall), v => Assert.Equal(Stone, v.Material));   // nothing erased
            var sent = FakeWorld.Outgoing(session);
            Assert.NotEqual(0u, ResultOf(sent));
            int answer = sent.FindIndex(p => p.Opcode == ZoneMessageOpcode.TerrainCellClientCellUpdateResponse);
            int resend = sent.FindIndex(p => p.Opcode == ZoneMessageOpcode.TerrainCellCellsUpdate);
            Assert.True(answer >= 0 && resend > answer, "the server's cell undoes the client's own erase: " + string.Join(", ", sent.Select(p => p.Opcode)));
        }

        [Fact]
        public void UnknownAction_IsNotTakenForACopy()
        {
            WorldSession session = Builder(0x7100_0000_0000_0033, site: true);
            Wall();
            Building.Handle(session, Building.AddTemporaryBlockSub, Selection(9, (10, 5, 20), (5, 5, 1), Cell));
            var sent = FakeWorld.Outgoing(session);
            Assert.NotEqual(0u, ResultOf(sent));
            Assert.False(sent.Any(p => p.Opcode == ZoneMessageOpcode.VoxelConstructionBase && p.Body[0] == 6));
            Assert.True(sent.Any(p => p.Opcode == ZoneMessageOpcode.TerrainCellCellsUpdate));
        }

        [Fact]
        public void DeleteMasks_CoverSizeVoxelsPerAxis_AcrossCells()
        {
            var request = Activator.CreateInstance(Private.Nested(typeof(Building), "SelectionRequest"))!;
            void Set(string field, object value) => request.GetType().GetField(field)!.SetValue(request, value);
            Set("Cell", Cell);
            Set("X", 38u); Set("Y", 0u); Set("Z", 0u);
            Set("SizeX", 4u); Set("SizeY", 2u); Set("SizeZ", 1u);
            object[] args = { request, 0L };
            var masks = (Dictionary<ulong, byte[]>)typeof(Building).GetMethod("DeleteMasks", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
            Assert.Equal(8L, (long)args[1]);
            (uint level, uint cx, uint cy, uint cz) = TerrainStore.Decode(Cell);
            ulong next = TerrainStore.CellId(level, cx + 1, cy, cz);
            Assert.Equal(new[] { Cell, next }.OrderBy(c => c), masks.Keys.OrderBy(c => c));
            int Bits(byte[] m) => m.Sum(b => System.Numerics.BitOperations.PopCount(b));
            Assert.Equal(4, Bits(masks[Cell]));    // x 38, 39 × y 0, 1
            Assert.Equal(4, Bits(masks[next]));    // x 0, 1 of the next cell
            Assert.NotEqual(0, masks[Cell][BlockData.Index(39, 1, 0) >> 3] & (0x80 >> (BlockData.Index(39, 1, 0) & 7)));
        }

        [Fact]
        public void PasteWithRemoveVoxels_TakesTheCopysSolidsOut()
        {
            var byCell = new Dictionary<ulong, List<TerrainEdits.VoxelValue>>
            {
                [Cell] = new()
                {
                    new(1, 3, Stone, 100, 120, 140),     // solid → material 0, vertex kept
                    new(2, 2, 0, 90, 90, 90),            // vertex-only → kept
                    new(3, 1, 0, 128, 128, 128)          // air in the copy → left alone
                }
            };
            Dictionary<ulong, List<TerrainEdits.VoxelValue>> erased = Building.AsErase(byCell);
            // The solid becomes material 0 with its vertex (a smooth hole, as the client's FUN_142a7eeb0); the vertex-only
            // voxel keeps its vertex; the copy's air leaves the target alone.
            Assert.Equal(new[] { new TerrainEdits.VoxelValue(1, 3, 0, 100, 120, 140), new TerrainEdits.VoxelValue(2, 2, 0, 90, 90, 90) },
                erased[Cell].ToArray());
        }

        [Fact]
        public void ACellListOverTheLimit_IsStillAnswered()
        {
            WorldSession session = Builder(0x7100_0000_0000_0034, site: true);
            ulong[] cells = Enumerable.Range(0, 1100).Select(i => TerrainStore.CellId(TerrainEdits.Level, 3000 + (uint)i, 1000, 3000)).ToArray();
            Building.Handle(session, Building.AddTemporaryBlockSub, Selection(Building.DeleteAction, (0, 0, 0), (1, 1, 1), cells));
            var sent = FakeWorld.Outgoing(session);
            Assert.Equal(cells.Length, sent.Count(p => p.Opcode == ZoneMessageOpcode.TerrainCellClientCellUpdateResponse));
        }
    }
}
