using LandmarkEmulator.Shared.GameTable.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A character that logged in again before its old session left (review 2026-10-18b #4): what the old session is
    /// still owed goes to the new one.
    /// </summary>
    public class RelogRuleTests : IDisposable
    {
        private const uint Plank = 30;
        private readonly FakeWorld world = new();

        public RelogRuleTests() => world.Define(new ItemDefinitionManager.ItemDefinition { Id = Plank, MaxStack = 100 });

        public void Dispose() => world.Dispose();

        /// <summary>Queues a craft for the session, due in the future (Crafting's private job list).</summary>
        private static void QueueCraft(WorldSession session, uint item, uint perCraft, uint count)
        {
            Type jobType = Private.Nested(typeof(Crafting), "Job");
            object job = Activator.CreateInstance(jobType, nonPublic: true)!;
            void Set(string name, object value) => jobType.GetProperty(name)!.GetSetMethod(true)!.Invoke(job, new[] { value });
            Set("Session", session);
            Set("Recipe", new RecipeEntry { Id = 9001, ItemDefinitionId = item, ItemCount = perCraft });
            Set("Count", count);
            Set("Done", DateTime.UtcNow.AddMinutes(5));
            ((IList)Private.Field(typeof(Crafting), "jobs")).Add(job);
        }

        [Fact]
        public void CraftAtLogout_GoesToTheReplacingSession()
        {
            WorldSession old = world.Session(0x7200_0001, "Relogger");
            WorldSession fresh = world.Session(0x7200_0001, "Relogger");
            ZoneSessions.Add(old);
            ZoneSessions.Add(fresh);
            try
            {
                Assert.True(ReferenceEquals(fresh, ZoneSessions.ReplacementOf(old)));
                QueueCraft(old, Plank, 2, 3);
                Crafting.Forget(old);
                Assert.Equal(6u, fresh.Inventory.CountOf(Plank));
                Assert.Equal(0u, old.Inventory.CountOf(Plank));
            }
            finally
            {
                ZoneSessions.Remove(old);
                ZoneSessions.Remove(fresh);
            }
        }

        [Fact]
        public void CraftAtLogout_WithoutReplacement_StaysWithTheSession()
        {
            WorldSession only = world.Session(0x7200_0002, "Leaver");
            ZoneSessions.Add(only);
            try
            {
                Assert.Null(ZoneSessions.ReplacementOf(only));
                QueueCraft(only, Plank, 1, 4);
                Crafting.Forget(only);
                Assert.Equal(4u, only.Inventory.CountOf(Plank));
            }
            finally
            {
                ZoneSessions.Remove(only);
            }
        }
    }
}
