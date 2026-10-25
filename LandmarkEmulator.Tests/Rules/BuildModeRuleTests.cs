using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// A bag placement's build mode (Game/BuildModeHolster.cs, review 2026-10-18b #6 / #7): only a build mode the client
    /// confirmed opening for the placement is closed, and the close runs on the world tick.
    /// </summary>
    public class BuildModeRuleTests : IDisposable
    {
        private readonly FakeWorld world = new(c => c.PlaceOpensBuildMode = true);
        private static ulong nextGuid = 0x7300_0000;

        public BuildModeRuleTests()
        {
            // The main hand's equipped slot (InventoryDefinition.txt is not loaded in tests): 2 at 2, as FakeWorld's.
            var field = typeof(LandmarkEmulator.WorldServer.Game.Inventory.Inventory)
                .GetField("equipSlotToContainerSlot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            if (field.GetValue(null) == null)
                field.SetValue(null, new Dictionary<uint, uint> { [2] = 2 });
        }

        public void Dispose() => world.Dispose();

        private static Props.PlacedProp Prop(ulong guid) => new() { PropId = 1, Spawn = new PlayerUpdateAddLightweightProp { Guid = guid } };

        private static Dictionary<WorldSession, DateTime> Map(string name)
            => (Dictionary<WorldSession, DateTime>)Private.Field(typeof(BuildModeHolster), name);

        private static bool OpenedByPlacement(WorldSession s)
            => ((HashSet<WorldSession>)Private.Field(typeof(BuildModeHolster), "openedByPlacement")).Contains(s);

        private static void Backdate(string map, WorldSession s, TimeSpan by) => Map(map)[s] = Map(map)[s] - by;

        [Fact]
        public void ConfirmedOpen_IsClosedOnTheTickAfterCancel()
        {
            WorldSession s = world.Session(++nextGuid, "Placer");
            ulong prop = ++nextGuid;
            try
            {
                BuildModeHolster.OpenForPlacement(s, Prop(prop));
                Assert.NotEmpty(FakeWorld.Outgoing(s));                // the open code went out
                BuildModeHolster.BuildMode(s, true);                   // the client's CommandPacketBuilderMode {1}
                Assert.True(OpenedByPlacement(s));
                BuildModeHolster.PlacementEnded(prop);                 // cancel (unplaced prop removed)
                BuildModeHolster.Update();
                Assert.Empty(FakeWorld.Outgoing(s));                   // not yet: CloseDelay
                Backdate("closeDue", s, BuildModeHolster.CloseDelay);
                BuildModeHolster.Update();
                Assert.NotEmpty(FakeWorld.Outgoing(s));                // the close code
                Assert.False(OpenedByPlacement(s));
            }
            finally
            {
                BuildModeHolster.Forget(s);
            }
        }

        [Fact]
        public void FirstSave_DoesNotCloseBuildMode()
        {
            // The client repeats Save while the gizmo is open; closing on first Save tore the ghost down.
            WorldSession s = world.Session(++nextGuid, "Settler");
            ulong prop = ++nextGuid;
            try
            {
                BuildModeHolster.OpenForPlacement(s, Prop(prop));
                BuildModeHolster.BuildMode(s, true);
                FakeWorld.Outgoing(s);
                BuildModeHolster.PlacementSettled(prop);               // first Save
                Assert.False(Map("closeDue").ContainsKey(s));
                BuildModeHolster.Update();
                Assert.Empty(FakeWorld.Outgoing(s));
                Assert.True(OpenedByPlacement(s));                    // still the placement's build mode
            }
            finally
            {
                BuildModeHolster.Forget(s);
            }
        }

        [Fact]
        public void UnansweredOpen_ThenTab_IsThePlayersOwn()
        {
            WorldSession s = world.Session(++nextGuid, "Tabber");
            ulong prop = ++nextGuid;
            try
            {
                BuildModeHolster.OpenForPlacement(s, Prop(prop));     // the client's guard said no: no answer
                Backdate("openPending", s, BuildModeHolster.OpenConfirmWindow + TimeSpan.FromSeconds(1));
                BuildModeHolster.BuildMode(s, true);                   // the player's own Tab, later
                Assert.False(OpenedByPlacement(s));
                FakeWorld.Outgoing(s);
                BuildModeHolster.PlacementEnded(prop);
                Assert.False(Map("closeDue").ContainsKey(s));          // nothing of the placement's to close
                BuildModeHolster.Update();
                Assert.Empty(FakeWorld.Outgoing(s));
            }
            finally
            {
                BuildModeHolster.Forget(s);
            }
        }

        [Fact]
        public void GoneSession_IsNotClosed()
        {
            WorldSession s = world.Session(++nextGuid, "Gone");
            ulong prop = ++nextGuid;
            try
            {
                BuildModeHolster.OpenForPlacement(s, Prop(prop));
                BuildModeHolster.BuildMode(s, true);
                BuildModeHolster.PlacementEnded(prop);
                Backdate("closeDue", s, BuildModeHolster.CloseDelay);
                ((FakeLink)s.Link).Close();
                FakeWorld.Outgoing(s);
                BuildModeHolster.Update();
                Assert.Empty(FakeWorld.Outgoing(s));
                Assert.False(Map("closeDue").ContainsKey(s));
            }
            finally
            {
                BuildModeHolster.Forget(s);
            }
        }
    }
}
