using LandmarkEmulator.WorldServer.Game;
using System.Numerics;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The prop waiting for its placement session spawns inside the player's build site: the 0.695 cursor stays red when
    /// the session's start point is in another site or none (FUN_142fbd5f0 / FUN_142fc1950; trace/0695/phaseE3-deep.md F1).
    /// </summary>
    public class PlacementSpawnRuleTests
    {
        [Fact]
        public void HighInTheSite_SpawnsTwentyMetresBelow()
        {
            Vector3 spawn = Props.PlacementSpawnPoint(new Vector3(10, 50, 20), (-40f, 140f));
            Assert.Equal(new Vector3(10, 30, 20), spawn);
        }

        [Fact]
        public void NearTheFloor_StaysInsideTheBox()
        {
            Vector3 spawn = Props.PlacementSpawnPoint(new Vector3(10, -30, 20), (-40f, 140f));
            Assert.Equal(-39f, spawn.Y);
            Assert.InRange(spawn.Y, -40f, 140f);
        }

        [Fact]
        public void OnTheFloor_GoesAboveThePlayerInsideTheBox()
        {
            Vector3 spawn = Props.PlacementSpawnPoint(new Vector3(10, -39.5f, 20), (-40f, 140f));
            Assert.Equal(-19.5f, spawn.Y);
            Vector3 low = Props.PlacementSpawnPoint(new Vector3(10, -39.5f, 20), (-40f, -30f));
            Assert.Equal(-31f, low.Y);   // a short box: just under its top
        }

        [Fact]
        public void OffSite_KeepsTheOldDepth()
        {
            Assert.Equal(new Vector3(0, -20, 0), Props.PlacementSpawnPoint(Vector3.Zero, null));
        }
    }
}
