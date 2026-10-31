using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game.Npc;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A world-NPC question takes only the asked player's answer and has its own id range, so a Story Tools answer
    /// (ids from 1) is never taken by it (trace/0695/phaseE4-logic.md F2).
    /// </summary>
    public class WorldNpcQuestionRuleTests : IDisposable
    {
        private const ulong Asked = 0x7A00_0001, Other = 0x7A00_0002;
        private readonly FakeWorld world = new();
        private readonly ulong id = (1UL << 62) | 0x7A_0000;

        private static IDictionary Questions => (IDictionary)Private.Field(typeof(WorldNpcs), "questions");

        public WorldNpcQuestionRuleTests()
        {
            Type pendingType = Private.Nested(typeof(WorldNpcs), "PendingQuestion");
            object pending = Activator.CreateInstance(pendingType, true)!;
            pendingType.GetField("NpcId")!.SetValue(pending, "no-such-npc");
            pendingType.GetField("AskedOf")!.SetValue(pending, Asked);
            pendingType.GetField("Until")!.SetValue(pending, DateTime.UtcNow.AddMinutes(1));
            lock (Private.Field(typeof(WorldNpcs), "sync"))
                Questions[id] = pending;
        }

        public void Dispose()
        {
            lock (Private.Field(typeof(WorldNpcs), "sync"))
                Questions.Remove(id);
            world.Dispose();
        }

        [Fact]
        public void OnlyTheAskedPlayer_Answers()
        {
            WorldSession other = world.Session(Other, "Other");
            WorldSession asked = world.Session(Asked, "Asked");
            Assert.False(WorldNpcs.DialogResponse(other, id, true));   // left for Story Tools / nobody
            Assert.True(WorldNpcs.DialogResponse(asked, id, true));
            Assert.True(WorldNpcs.DialogResponse(asked, id, true));    // ours, already answered: still consumed
        }

        [Fact]
        public void StoryToolsIds_AreNotWorldNpcQuestions()
        {
            WorldSession asked = world.Session(Asked, "Asked");
            Assert.False(WorldNpcs.DialogResponse(asked, 0x7A_0000, true));
        }
    }
}
