using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using System;
using System.Collections;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Creature props in Play Mode (player report 2026-10-31: "when I switched to Play Mode the creature props stayed while
    /// they also spawned into monsters"): a creature spawner is hidden while its site plays, like the story tools, so only
    /// its creature stands there; the creature faces the prop's placed rotation.
    /// </summary>
    public class CreaturePropsPlayModeTests : IDisposable
    {
        /// <summary>Barren Flameblade, a Creatures palette prop (any id works: the kind is set in the cache below).</summary>
        private const uint CreatureProp = 2451;
        private const uint PlainProp = 990001;
        private readonly IDictionary kindCache = (IDictionary)Private.Field(typeof(StoryTools), "kindCache");

        public CreaturePropsPlayModeTests()
        {
            lock (kindCache)
            {
                kindCache[CreatureProp] = Private.Field(typeof(StoryTools), "spawnerKind");
                kindCache[PlainProp] = null;
            }
        }

        public void Dispose()
        {
            lock (kindCache)
            {
                kindCache.Remove(CreatureProp);
                kindCache.Remove(PlainProp);
            }
        }

        private static bool Hides(uint propId) => (bool)Private.Call(typeof(Props), "HidesInPlayMode", propId);

        [Fact]
        public void CreatureProps_AreHiddenInPlayMode_LikeTheStoryTools()
        {
            Assert.True(Props.IsSpawner(CreatureProp));
            Assert.True(Hides(CreatureProp));
            Assert.True(Hides(StoryTools.StoryNodeProp));
            Assert.False(Hides(PlainProp));
        }

        [Fact]
        public void TheCreatureFacesItsPropsRotation()
        {
            // A quarter turn about +Y: +Z (forward) becomes +X, heading π/2 as MovementBody writes it.
            Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            Assert.Equal(MathF.PI / 2, Creatures.HeadingOf(new Vector4(q.X, q.Y, q.Z, q.W))!.Value, 0.001f);
            Assert.Equal(0f, Creatures.HeadingOf(new Vector4(0f, 0f, 0f, 1f))!.Value, 0.001f);
            Assert.Null(Creatures.HeadingOf(Vector4.Zero));
        }
    }
}
