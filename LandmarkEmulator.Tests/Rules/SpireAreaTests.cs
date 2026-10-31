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
    /// The Spire area and trees under new build sites (player report 2026-10-31: "players should not be able to claim the Spire
    /// area; trees do not disappear inside a placed claim"; trace/0695/spire-claims.md, claim-trees.md). The client's own
    /// GlobalAreas 3 is a 90 m sphere round the Spire with claim exclusion; live cleared the trees inside a placed site.
    /// </summary>
    public class SpireAreaTests : IDisposable
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private static int nextZone = 9700;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly FakeWorld world = new();

        public SpireAreaTests()
        {
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? 1010f : null;
            var rolled = (Dictionary<uint, string>)Private.Field(typeof(Gathering), "rolled");
            string generator = (string)typeof(Gathering).GetProperty("Generator", Static)!.GetValue(null);
            lock (rolled)
                rolled[zone] = generator;
        }

        public void Dispose()
        {
            Spire.AreaEnabled = true;
            TerrainHeights.TestGround = null;
            ((IDictionary)typeof(Spire).GetField("sites", Static)!.GetValue(null)!).Remove(zone);
            world.Dispose();
        }

        /// <summary>A Spire on the test landscape with its Hub at <paramref name="hub"/> (local metres).</summary>
        private void SpireAt(Vector3 hub)
        {
            Type siteType = typeof(Spire).GetNestedType("Site", BindingFlags.NonPublic)!;
            object site = Activator.CreateInstance(siteType)!;
            siteType.GetField("Zone")!.SetValue(site, zone);
            siteType.GetField("Position")!.SetValue(site, hub);
            siteType.GetField("Props")!.SetValue(site, new List<PlayerUpdateAddLightweightProp>());
            ((IDictionary)typeof(Spire).GetField("sites", Static)!.GetValue(null)!)[zone] = site;
        }

        private static (double X, double Y, double Z) Global(Vector3 local) => TerrainEdits.ToGlobal(local);

        [Fact]
        public void ExclusionSphere_IsTheClientsColumnAndSphereTest()
        {
            var hub = new Vector3(0, 0, 0);
            // A 210 × 180 × 210 m site: half 105 m, half diagonal 173.64 m; the area's radius 90 m.
            Assert.True(Claims.InExclusionSphere(hub, 90f, new Vector3(194, 0, 0), 1050, 900));
            Assert.False(Claims.InExclusionSphere(hub, 90f, new Vector3(195, 0, 0), 1050, 900));   // the column test: |dx| < 195
            Assert.True(Claims.InExclusionSphere(hub, 90f, new Vector3(0, -200, 0), 1050, 900));   // straight below: only the sphere
            Assert.False(Claims.InExclusionSphere(hub, 90f, new Vector3(0, -270, 0), 1050, 900));
            Assert.True(Claims.InExclusionSphere(hub, 90f, new Vector3(150, 0, 150), 1050, 900));  // |d| 212 m < 263.64
            Assert.False(Claims.InExclusionSphere(hub, 90f, new Vector3(190, 0, 190), 1050, 900)); // in the column, |d| 268.7 m
        }

        [Fact]
        public void SitesInTheSpireArea_AreRefused_UnlessTheSettingIsOff()
        {
            SpireAt(new Vector3(100, 1010, 100));
            var near = Global(new Vector3(200, 1050, 100));    // middle 100 m from the Hub
            var far = Global(new Vector3(400, 1050, 100));
            Assert.True(Spire.BlocksSite(zone, near.X, near.Y, near.Z, 1050, 900));
            Assert.False(Spire.BlocksSite(zone, far.X, far.Y, far.Z, 1050, 900));
            Assert.True(Spire.InArea(zone, new Vector3(150, 1010, 100)));
            Assert.False(Spire.InArea(zone, new Vector3(250, 1010, 100)));
            Spire.AreaEnabled = false;
            Assert.False(Spire.BlocksSite(zone, near.X, near.Y, near.Z, 1050, 900));
            Assert.False(Spire.InArea(zone, new Vector3(150, 1010, 100)));
        }

        [Fact]
        public void TheHubCarriesTheArea()
        {
            var hub = new PlayerUpdateAddLightweightProp { Guid = 0x5350_0001_CC00_0001 };
            typeof(Spire).GetMethod("GiveArea", Static)!.Invoke(null, new object[] { hub });
            Assert.Equal(Spire.AreaId, hub.StaticAreaId);
            Assert.Equal(0x5341_0001_CC00_0001ul, hub.AreaGuid);
            Assert.Equal(" ", hub.Name);   // no "area" label over the Spire
        }

        [Fact]
        public void APlacedSite_TakesTheTreesInItOut_ForWhoeverSeesThem()
        {
            Type nodeClass = Private.Nested(typeof(Gathering), "Node");
            T Get<T>(object node, string member)
            {
                FieldInfo field = nodeClass.GetField(member);
                return (T)(field != null ? field.GetValue(node) : nodeClass.GetProperty(member)!.GetValue(node));
            }
            object tree = null;
            int treeRx = 0, treeRz = 0;
            for (int rz = 0; rz < 32 && tree == null; rz++)
                for (int rx = 0; rx < 32 && tree == null; rx++)
                    foreach (object node in (IEnumerable)Private.Call(typeof(Gathering), "Region", zone, rx, rz))
                        if (Get<object>(node, "Type").ToString() == "Tree" && Get<bool>(node, "Standing"))
                        {
                            (tree, treeRx, treeRz) = (node, rx, rz);
                            break;
                        }
            Assert.NotNull(tree);
            var spawn = Get<PlayerUpdateAddLightweightProp>(tree, "Spawn");
            var at = new Vector3(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);

            // A player who has that region on screen.
            WorldSession viewer = world.Session(0x7100_0000_0000_0041, "Viewer");
            var players = (List<WorldSession>)Private.Field(typeof(PlayerManager), "players");
            lock (Private.Field(typeof(PlayerManager), "sync"))
                players.Add(viewer);
            var sent = (IDictionary)Private.Field(typeof(Gathering), "sent");
            long region = (long)Private.Call(typeof(Gathering), "Pack", zone, treeRx, treeRz);
            lock (Private.Field(typeof(Gathering), "sync"))
                sent[viewer] = new HashSet<long> { region };
            FakeWorld.Outgoing(viewer);
            try
            {
                var g = Global(at);
                Gathering.SitePlaced(zone, (g.X - 100, g.Y - 100, g.Z - 100), (g.X + 100, g.Y + 900, g.Z + 100));
                Assert.False(Get<bool>(tree, "Standing"));
                var all = FakeWorld.Outgoing(viewer);
                var removals = all.Where(p => p.Opcode == ZoneMessageOpcode.PlayerUpdateRemovePlayerWire).ToList();
                Assert.True(removals.Any(p => BitConverter.ToUInt64(p.Body, p.Body.Length - 8) == spawn.Guid),   // after RemovePlayer's u16
                    $"guid {spawn.Guid:X}; sent: " + string.Join(", ", all.Select(p => $"{p.Opcode} {BitConverter.ToString(p.Body.Take(10).ToArray())}")));
            }
            finally
            {
                lock (Private.Field(typeof(PlayerManager), "sync"))
                    players.Remove(viewer);
                lock (Private.Field(typeof(Gathering), "sync"))
                    sent.Remove(viewer);
            }
        }
    }
}
