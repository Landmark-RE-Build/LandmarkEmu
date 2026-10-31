using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Emotes with a tool in hand (Game/EmoteHolster.cs, 2026-10-21): the 0.695 animation network plays emotes and
    /// stances only with WeaponIdle 0, so the held item is put away first, the emote follows, and the item comes back when
    /// the player walks off (walk stances: at /stand).
    /// </summary>
    public class EmoteHolsterRuleTests
    {
        private const uint Pick = 990101;
        private static ulong nextGuid = 0x7B10_0000_0000_0000;

        public EmoteHolsterRuleTests()
        {
            // The main hand's equipped slot (InventoryDefinition.txt is not loaded in tests): 2 at 2, as FakeWorld's.
            var field = typeof(Inventory).GetField("equipSlotToContainerSlot", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (field.GetValue(null) == null)
                field.SetValue(null, new Dictionary<uint, uint> { [2] = 2 });
            EmoteHolster.PutAwayLead = TimeSpan.FromMilliseconds(10);
        }

        private static (WorldSession Session, CharacterItemModel Pick) Holding(FakeWorld world, params CharacterItemModel[] more)
        {
            // A pick: its mining ability (1018) marks it a tool, so equipping it never looks for a weapon class.
            world.Define(new ItemDefinitionManager.ItemDefinition
            {
                Id = Pick, EquipSlotId = 2, ModelFile = "Wield_Pick_Test.adr",
                Abilities = new() { new ItemDefinitionManager.ItemAbility(1018, 0, "", "") }
            });
            CharacterItemModel pick = FakeWorld.Item(Pick, 1, Inventory.EquippedContainer, 2);
            ulong guid = System.Threading.Interlocked.Increment(ref nextGuid);
            WorldSession s = world.Session(guid, "Holster" + (guid & 0xFFFF),
                more.Prepend(FakeWorld.Item(3253, 1, Inventory.BagContainer, 0)).Prepend(pick).ToArray());
            return (s, pick);
        }

        private static uint[] Selections(WorldSession s)
            => FakeWorld.Outgoing(s).Where(p => p.Opcode == ZoneMessageOpcode.PlayerUpdateRequestAnimationSelectionWire)
                .Select(p => BitConverter.ToUInt32(p.Body, 8)).ToArray();

        /// <summary>Runs the world tick's ability timers (where a delayed emote plays) until <paramref name="done"/>.</summary>
        private static void WaitFor(Func<bool> done)
        {
            for (int i = 0; i < 300; i++)
            {
                Abilities.Update();
                if (done())
                    return;
                System.Threading.Thread.Sleep(10);
            }
        }

        [Fact]
        public void Wave_WithAPick_PutsItAwayFirst_AndWalkingBringsItBack()
        {
            using var world = new FakeWorld(c => c.EmotesPutAwayHeldItem = true);
            (WorldSession s, CharacterItemModel pick) = Holding(world);
            try
            {
                Emotes.Run(s, "wave", "");
                Assert.Equal(Inventory.BagContainer, pick.Container);       // in the bag before the emote
                Assert.True(EmoteHolster.HasPutAway(s));
                var got = new List<uint>();
                WaitFor(() => { got.AddRange(Selections(s)); return got.Contains(217u); });
                Assert.Contains(217u, got);                                   // then the wave

                EmoteHolster.Moved(s, s.Position + new Vector3(0.2f, 0f, 0f));
                Assert.Equal(Inventory.BagContainer, pick.Container);       // a shuffle is not walking off
                EmoteHolster.Moved(s, s.Position + new Vector3(1f, 0f, 0f));
                Assert.Equal(Inventory.EquippedContainer, pick.Container);  // back in hand
                Assert.False(EmoteHolster.HasPutAway(s));
            }
            finally
            {
                Emotes.Forget(s);
            }
        }

        [Fact]
        public void Skip_KeepsThePickAwayWhileWalking_UntilStand()
        {
            using var world = new FakeWorld(c => c.EmotesPutAwayHeldItem = true);
            (WorldSession s, CharacterItemModel pick) = Holding(world);
            try
            {
                Emotes.Run(s, "skip", "");
                Assert.Equal(Inventory.BagContainer, pick.Container);
                EmoteHolster.Moved(s, s.Position + new Vector3(10f, 0f, 0f));
                Assert.Equal(Inventory.BagContainer, pick.Container);       // still skipping
                Emotes.Run(s, "stand", "");
                Assert.Equal(Inventory.EquippedContainer, pick.Container);
            }
            finally
            {
                Emotes.Forget(s);
            }
        }

        [Fact]
        public void EmoteAgainAfterTakingAnotherTool_OnlyTheNewestComesBack()
        {
            using var world = new FakeWorld(c => c.EmotesPutAwayHeldItem = true);
            CharacterItemModel axe = FakeWorld.Item(Pick, 1, Inventory.BagContainer, 5);
            (WorldSession s, CharacterItemModel pick) = Holding(world, axe);
            try
            {
                Emotes.Run(s, "cheer", "");                                    // the pick goes away
                LandmarkEmulator.WorldServer.Network.Handlers.InventoryHandler.Equip(s, axe.Id, 2);   // hotbar: the axe
                Emotes.Run(s, "cheer", "");                                    // the axe goes away
                Assert.Equal(Inventory.BagContainer, axe.Container);
                EmoteHolster.Moved(s, s.Position + new Vector3(2f, 0f, 0f));
                Assert.Equal(Inventory.EquippedContainer, axe.Container);     // the axe, the last one held
                Assert.Equal(Inventory.BagContainer, pick.Container);
            }
            finally
            {
                Emotes.Forget(s);
            }
        }

        [Fact]
        public void Mood_LeavesThePickInHand()
        {
            using var world = new FakeWorld(c => c.EmotesPutAwayHeldItem = true);
            (WorldSession s, CharacterItemModel pick) = Holding(world);
            try
            {
                Emotes.Run(s, "happy", "");
                Assert.Equal(Inventory.EquippedContainer, pick.Container);
                Assert.False(EmoteHolster.HasPutAway(s));
            }
            finally
            {
                Emotes.Forget(s);
            }
        }

        [Fact]
        public void SettingOff_IsLive_NothingPutAway_EmoteAtOnce()
        {
            using var world = new FakeWorld(c => c.EmotesPutAwayHeldItem = false);
            (WorldSession s, CharacterItemModel pick) = Holding(world);
            try
            {
                Emotes.Run(s, "wave", "");
                Assert.Equal(Inventory.EquippedContainer, pick.Container);
                Assert.Contains(217u, Selections(s));
            }
            finally
            {
                Emotes.Forget(s);
            }
        }

        [Fact]
        public void SomethingElseInHand_IsNotReplaced()
        {
            using var world = new FakeWorld(c => c.EmotesPutAwayHeldItem = true);
            (WorldSession s, CharacterItemModel pick) = Holding(world);
            try
            {
                Emotes.Run(s, "kneel", "");
                Assert.Equal(Inventory.BagContainer, pick.Container);
                // The player takes the pick out again themselves (hotbar) before walking off: walking off leaves the hand
                // as it is and just forgets the record.
                LandmarkEmulator.WorldServer.Network.Handlers.InventoryHandler.Equip(s, pick.Id, 2);
                Assert.Equal(Inventory.EquippedContainer, pick.Container);
                EmoteHolster.Moved(s, s.Position + new Vector3(2f, 0f, 0f));
                Assert.Equal(Inventory.EquippedContainer, pick.Container);
                Assert.False(EmoteHolster.HasPutAway(s));
            }
            finally
            {
                Emotes.Forget(s);
            }
        }
    }
}
