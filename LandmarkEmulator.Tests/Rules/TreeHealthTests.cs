using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Handlers;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
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
    /// Tree health and the harvest target (Gathering.Health.cs; trace/0695/healthbar-*.md; player report 2026-10-31: no
    /// health bar on trees, NPC bars only after aggro). The client shows a tree's bar while it is its harvest target
    /// (slot 9) and its Health resource is below 100 % and above 0 %; a creature's needs its Life after the full data.
    /// </summary>
    public class TreeHealthTests : IDisposable
    {
        private static int nextZone = 9500;
        private readonly uint zone = (uint)System.Threading.Interlocked.Increment(ref nextZone);
        private readonly FakeWorld world = new();

        public TreeHealthTests()
        {
            TerrainHeights.TestGround = (x, z, z0) => z0 == zone ? 1010f : null;
            var rolled = (Dictionary<uint, string>)Private.Field(typeof(Gathering), "rolled");
            string generator = (string)typeof(Gathering).GetProperty("Generator", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            lock (rolled)
                rolled[zone] = generator;
            var slots = typeof(LandmarkEmulator.WorldServer.Game.Inventory.Inventory)
                .GetField("equipSlotToContainerSlot", BindingFlags.NonPublic | BindingFlags.Static)!;
            if (slots.GetValue(null) == null)
                slots.SetValue(null, new Dictionary<uint, uint> { [2] = 2 });
        }

        public void Dispose()
        {
            Gathering.TreeHealth = Gathering.TreeHealthModel.Health;
            TerrainHeights.TestGround = null;
            world.Dispose();
        }

        private static readonly Type NodeClass = Private.Nested(typeof(Gathering), "Node");

        private static T Get<T>(object node, string member)
        {
            FieldInfo field = NodeClass.GetField(member);
            return (T)(field != null ? field.GetValue(node) : NodeClass.GetProperty(member)!.GetValue(node));
        }

        /// <summary>The first standing tree of this landscape (models have no names in tests: size 0, 350 health).</summary>
        private object FirstTree()
        {
            for (int rz = 0; rz < 32; rz++)
                for (int rx = 0; rx < 32; rx++)
                    foreach (object node in (IEnumerable)Private.Call(typeof(Gathering), "Region", zone, rx, rz))
                        if (Get<object>(node, "Type").ToString() == "Tree" && Get<bool>(node, "Standing"))
                            return node;
            throw new InvalidOperationException("No tree generated on the flat test ground.");
        }

        /// <summary>A player standing 2 m from the tree, with nothing queued.</summary>
        private (WorldSession Session, ulong Guid, Vector3 At) Beside(object tree, ulong playerGuid)
        {
            var spawn = Get<PlayerUpdateAddLightweightProp>(tree, "Spawn");
            var at = new Vector3(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
            WorldSession session = world.Session(playerGuid, "Logger" + (playerGuid & 0xFF));
            var character = (LandmarkEmulator.Database.Character.Model.CharacterModel)typeof(WorldSession)
                .GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (at.X + 2f, at.Y, at.Z);
            FakeWorld.Outgoing(session);
            return (session, Get<ulong>(tree, "Guid"), at);
        }

        /// <summary>The Health records (ResourcesBase SetCharacterResource) in what was sent: guid, row, type, value.</summary>
        private static List<(ulong Guid, uint Row, uint Type, uint Value)> Health(IEnumerable<(ZoneMessageOpcode Opcode, byte[] Body)> sent)
            => sent.Where(p => p.Opcode == ZoneMessageOpcode.ResourcesBase).Select(p =>
            {
                var reader = new GamePacketReader(p.Body);
                reader.ReadUInt();                     // release counter
                Assert.Equal(2, reader.ReadByte());    // sub 2 SetCharacterResource
                ulong guid = reader.ReadULong();
                uint row = reader.ReadUInt(), type = reader.ReadUInt();
                Assert.Equal(0u, reader.ReadUInt());   // no MAX variable: the client uses the row's MAX_VALUE
                return (guid, row, type, reader.ReadUInt());
            }).ToList();

        private static TargetRequest Target(ushort sub, Action<GamePacketWriter> body)
            => Wire.Read<TargetRequest>(Wire.Write(w => { w.WriteUShort(sub); body(w); })).Message;

        [Fact]
        public void TreeSize_ComesFromTheModelNameAndFootprint()
        {
            var names = (Dictionary<uint, string>)Private.Field(typeof(ModelFootprints), "names");
            var footprints = (Dictionary<uint, (float Radius, Vector3 Offset)>)Private.Field(typeof(ModelFootprints), "footprints");
            var models = new (uint Id, string Name, float Radius, int Size)[]
            {
                (990001, "Biome_JaggedPine_Trees_DiamondPine_Large01.adr", 7f, 3),
                (990002, "Biome_Feerrott_Trees_Banyans_Large01.adr", 15.6f, 4),
                (990003, "Biome_JaggedPine_Trees_Hugeface01.adr", 5f, 4),
                (990004, "Biome_OceangreenHills_Trees_Birch_Medium02.adr", 2f, 2),
                (990005, "Biome_Lavastorm_Trees_Trees_Short01.adr", 1f, 1),
                (990006, "Biome_Lavastorm_Trees_Trees_TallBare01.adr", 3f, 3),
                (990007, "Biome_Ashfang_Trees_DeadLog01.adr", 2f, 0),
                (990008, "Biome_Feerrott_Trees_Palms_Curvy01.adr", 3f, 2),   // no size word: its footprint
                (990009, "Biome_Ashfang_Trees_SmallDeadTree01.adr", 1.5f, 1)
            };
            try
            {
                foreach (var m in models)
                {
                    names[m.Id] = m.Name;
                    footprints[m.Id] = (m.Radius, Vector3.Zero);
                }
                foreach (var m in models)
                    Assert.True(Gathering.TreeSize(m.Id) == m.Size, $"{m.Name}: size {Gathering.TreeSize(m.Id)}, expected {m.Size}");
            }
            finally
            {
                foreach (var m in models)
                {
                    names.Remove(m.Id);
                    footprints.Remove(m.Id);
                }
            }
        }

        [Fact]
        public void Chop_TakesTheAxeDamage_SendsTheSwingerTheTreesHealth_AndFellsItAtZero()
        {
            object tree = FirstTree();
            (WorldSession session, ulong guid, Vector3 at) = Beside(tree, 0x7100_0000_0000_0011);
            Assert.Equal(18u, Get<uint>(tree, "LifeRow"));           // "Tree - Very Small": 350 of 351
            Assert.Equal(350u, Get<uint>(tree, "Health"));

            Assert.True(Gathering.Chop(session, guid, at));
            var first = FakeWorld.Outgoing(session);
            Assert.Equal((guid, 18u, 1u, 350u - Gathering.AxeDamage), Health(first).Single());
            // No click before the swing (a held button): the server puts the tree in the harvest slot itself, once.
            Assert.Equal(Gathering.SetTargetBody(guid, Gathering.HarvestTargetSlot), first.Single(p => p.Opcode == ZoneMessageOpcode.TargetBase).Body);
            Assert.True(Gathering.Chop(session, guid, at));
            Assert.DoesNotContain(ZoneMessageOpcode.TargetBase, FakeWorld.Outgoing(session).Select(p => p.Opcode));

            // 350 / 80: the fifth swing fells it; no record of 0 is sent (the tree is removed instead).
            for (int swing = 3; swing <= 5; swing++)
            {
                Assert.True(Get<bool>(tree, "Standing"));
                Assert.True(Gathering.Chop(session, guid, at));
            }
            Assert.False(Get<bool>(tree, "Standing"));
            Assert.All(Health(FakeWorld.Outgoing(session)), r => Assert.NotEqual(0u, r.Value));
            Assert.Equal(350u, Get<uint>(tree, "Health"));           // undamaged when it stands again
        }

        /// <summary>
        /// The axe targets trees only (player report 2026-10-31: it chopped and outlined the Spire, props, anything): the axes'
        /// caster info carries target rule 20 "Target is Harvestable Tree" and trees their profile 200-214; a swing that names
        /// something else fells nothing, not even the tree beside it (trace/0695/axe-a-client.md, axe-b-server.md).
        /// </summary>
        [Fact]
        public void Axes_TargetTreesOnly()
        {
            var item = new LandmarkEmulator.WorldServer.Game.Inventory.ItemDefinitionManager.ItemDefinition { Id = 3259, NameId = 1 };
            foreach (uint ability in new uint[] { 1020, 176, 173, 1037 })
                Assert.Equal(Abilities.TreeTargetRequirement, Abilities.BuildItemAbilityInfo(item,
                    new LandmarkEmulator.WorldServer.Game.Inventory.ItemDefinitionManager.ItemAbility(ability, 2, "", "")).U90);
            Assert.Equal(0u, Abilities.BuildItemAbilityInfo(item,
                new LandmarkEmulator.WorldServer.Game.Inventory.ItemDefinitionManager.ItemAbility(1018, 3, "", "")).U90);   // a pick: anything

            object tree = FirstTree();
            Assert.InRange(Get<PlayerUpdateAddLightweightProp>(tree, "Spawn").Profile, 200u, 214u);
            Assert.Equal(200u, Get<PlayerUpdateAddLightweightProp>(tree, "Spawn").Profile);   // size 0 (no model names in tests), Plain Wood

            (WorldSession session, _, Vector3 at) = Beside(tree, 0x7100_0000_0000_0021);
            Assert.False(Gathering.Chop(session, 0x5000_0000_0000_0099, at));   // a creature (or prop) beside the tree
            Assert.Equal(350u, Get<uint>(tree, "Health"));
            Assert.Empty(Health(FakeWorld.Outgoing(session)));
        }

        [Fact]
        public void ItemUpdate_KeepsTheItemsQualityAndStats()
        {
            var random = new InventoryItem.DefinitionData.RandomData { Quality = 2 };
            random.Stats.Add(new InventoryItem.DefinitionData.RandomData.StatInfo { StatId = 12, NameText = new LandmarkEmulator.Shared.Game.LandmarkText(uint.MaxValue) });
            byte[] withData = Wire.Write(new ClientUpdateItemUpdate { Guid = 5, Count = 20, Random = random });
            byte[] empty = Wire.Write(new ClientUpdateItemUpdate { Guid = 5, Count = 20 });
            Assert.Equal(100, empty.Length);                                    // the 0.695 reader's 104 minus the header
            // guid 8, count 4, consumed 4, rental 4, expiry 8, slot 4, container 4 → the first block's quality at 36.
            Assert.Equal(2u, BitConverter.ToUInt32(withData, 36));
            Assert.True(withData.Length > empty.Length);
        }

        [Fact]
        public void HitsMode_KeepsTheFixedSwings_AndSendsNoHealth()
        {
            Gathering.TreeHealth = Gathering.TreeHealthModel.Hits;
            object tree = FirstTree();
            (WorldSession session, ulong guid, Vector3 at) = Beside(tree, 0x7100_0000_0000_0012);
            int hits = Get<int>(tree, "MaxHits");
            for (int i = 0; i < hits; i++)
            {
                Assert.True(Get<bool>(tree, "Standing"));
                Assert.True(Gathering.Chop(session, guid, at));
            }
            Assert.False(Get<bool>(tree, "Standing"));
            Assert.Empty(Health(FakeWorld.Outgoing(session)));
        }

        [Fact]
        public void TargetingATree_PutsItInTheHarvestSlot_WithItsHealthFirst()
        {
            object tree = FirstTree();
            (WorldSession session, ulong guid, _) = Beside(tree, 0x7100_0000_0000_0013);

            // The client asks for slot 8 (a node clicked before its full data is still friendly).
            InteractionHandler.HandleTarget(session, Target(2, w => { w.Write(guid); w.Write(8u); }));
            var sent = FakeWorld.Outgoing(session);
            Assert.Equal(new[] { ZoneMessageOpcode.ResourcesBase, ZoneMessageOpcode.TargetBase }, sent.Select(p => p.Opcode));
            Assert.Equal((guid, 18u, 1u, 350u), Health(sent).Single());
            Assert.Equal(Wire.Write(w => { w.WriteUShort(6); w.Write(guid); w.Write(Gathering.HarvestTargetSlot); w.Write(true); }), sent[1].Body);
            Assert.Equal(guid, session.CurrentTarget);

            // Removing slot 9 forgets the harvest target: a swing then reaches nobody else.
            InteractionHandler.HandleTarget(session, Target(3, w => w.Write(Gathering.HarvestTargetSlot)));
            Assert.Equal(0ul, session.CurrentTarget);
            var targets = (IDictionary)Private.Field(typeof(Gathering), "harvestTargets");
            lock (Private.Field(typeof(Gathering), "sync"))
                Assert.False(targets.Contains(session));
        }

        [Fact]
        public void AnotherPlayersHarvestTarget_FollowsTheSwings()
        {
            object tree = FirstTree();
            (WorldSession chopper, ulong guid, Vector3 at) = Beside(tree, 0x7100_0000_0000_0014);
            (WorldSession watcher, _, _) = Beside(tree, 0x7100_0000_0000_0015);
            InteractionHandler.HandleTarget(watcher, Target(2, w => { w.Write(guid); w.Write(9u); }));
            FakeWorld.Outgoing(watcher);

            Assert.True(Gathering.Chop(chopper, guid, at));
            Assert.Equal((guid, 18u, 1u, 270u), Health(FakeWorld.Outgoing(watcher)).Single());
        }

        [Fact]
        public void FullData_SendsNodesNeutral_AndTheHarvestersTreeHealthAfterIt()
        {
            object tree = FirstTree();
            (WorldSession session, ulong guid, Vector3 at) = Beside(tree, 0x7100_0000_0000_0016);
            uint transient = Get<PlayerUpdateAddLightweightProp>(tree, "Spawn").TransientId;

            // Undamaged and not targeted: the full data alone (neutral).
            Assert.True(Gathering.SendFull(session, guid));
            var sent = FakeWorld.Outgoing(session);
            Assert.Single(sent);
            Assert.Equal(Wire.Write(new PlayerUpdateLightweightToFullProp { TransientId = transient, Disposition = 1 }), sent[0].Body);
            Assert.NotEqual(Wire.Write(new PlayerUpdateLightweightToFullProp { TransientId = transient }), sent[0].Body);

            // The harvester's tree: the full data clears the client's record, so the health follows it.
            Assert.True(Gathering.Chop(session, guid, at));
            FakeWorld.Outgoing(session);
            Assert.True(Gathering.SendFull(session, guid));
            sent = FakeWorld.Outgoing(session);
            Assert.Equal(ZoneMessageOpcode.ResourcesBase, sent.Last().Opcode);
            Assert.Equal((guid, 18u, 1u, 270u), Health(sent).Single());

            // Someone passing the damaged tree can't see its bar: the full data alone.
            (WorldSession passer, _, _) = Beside(tree, 0x7100_0000_0000_0018);
            Assert.True(Gathering.SendFull(passer, guid));
            Assert.Empty(Health(FakeWorld.Outgoing(passer)));
        }

        [Fact]
        public void TheLastHealth_StaysAboveTheClientsZeroPercent()
        {
            object tree = FirstTree();
            (WorldSession session, ulong guid, _) = Beside(tree, 0x7100_0000_0000_0019);
            // 2 health of 351 is 0 % in the client's integer maths (TargetAlive fails); 1 % of 351 is 4.
            NodeClass.GetField("Health")!.SetValue(tree, 2u);
            Private.Call(typeof(Gathering), "SendLife", session, tree);
            Assert.Equal((guid, 18u, 1u, 4u), Health(FakeWorld.Outgoing(session)).Single());
        }

        [Fact]
        public void RemovingTheHarvestSlot_KeepsTheCreatureTarget_EvenWhenRepeated()
        {
            object tree = FirstTree();
            (WorldSession session, ulong guid, _) = Beside(tree, 0x7100_0000_0000_001A);
            const ulong creature = 0x5000_0000_0000_0042;
            InteractionHandler.HandleTarget(session, Target(2, w => { w.Write(creature); w.Write(3u); }));
            InteractionHandler.HandleTarget(session, Target(2, w => { w.Write(guid); w.Write(9u); }));
            Assert.Equal(guid, session.CurrentTarget);

            // The tree falls; the client asks to remove slot 9 every frame until the answer arrives.
            InteractionHandler.HandleTarget(session, Target(3, w => w.Write(9u)));
            InteractionHandler.HandleTarget(session, Target(3, w => w.Write(9u)));
            Assert.Equal(creature, session.CurrentTarget);
            InteractionHandler.HandleTarget(session, Target(3, w => w.Write(3u)));
            Assert.Equal(0ul, session.CurrentTarget);
        }

        [Fact]
        public void ANeutralCreatureInTheHarvestSlot_EndsTheTreeTarget()
        {
            object tree = FirstTree();
            (WorldSession session, ulong guid, _) = Beside(tree, 0x7100_0000_0000_001B);
            InteractionHandler.HandleTarget(session, Target(2, w => { w.Write(guid); w.Write(9u); }));
            InteractionHandler.HandleTarget(session, Target(2, w => { w.Write(0x5000_0000_0000_0043ul); w.Write(9u); }));
            var targets = (IDictionary)Private.Field(typeof(Gathering), "harvestTargets");
            lock (Private.Field(typeof(Gathering), "sync"))
                Assert.False(targets.Contains(session));
        }

        [Fact]
        public void CreatureGuids_AreNotNodes()
        {
            Assert.True(Gathering.IsNode(0x4000_0000_0000_1234));
            Assert.False(Gathering.IsNode(0x5000_0000_0000_1234));   // creatures share bit 62 with nodes
            Assert.False(Gathering.IsNode(0x5100_0000_0000_1234));   // cavern creatures
        }

        [Fact]
        public void CreatureFullData_IsFollowedByItsLife()
        {
            WorldSession session = world.Session(0x7100_0000_0000_0017, "Hunter");
            var character = (LandmarkEmulator.Database.Character.Model.CharacterModel)typeof(WorldSession)
                .GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (100f, 1010f, 100f);
            // Resources.txt is not shipped with the tests: one Life row stands in for the table's.
            FieldInfo rows = typeof(Creatures).GetField("lifeRows", BindingFlags.NonPublic | BindingFlags.Static)!;
            object previous = rows.GetValue(null);
            rows.SetValue(null, new List<(uint Row, uint Max)> { (4, 100000) });
            List<(ZoneMessageOpcode Opcode, byte[] Body)> sent;
            ulong creature;
            try
            {
                Creatures.SpawnNear(session, 3f, 0f);
                creature = Creatures.NearestGuid(session, 10f);
                Assert.NotEqual(0ul, creature);
                FakeWorld.Outgoing(session);

                Assert.True(Creatures.SendFull(session, creature));
                sent = FakeWorld.Outgoing(session);
            }
            finally
            {
                rows.SetValue(null, previous);
            }
            int full = sent.FindIndex(p => p.Opcode == ZoneMessageOpcode.PlayerUpdateLightweightToFullNpcWire);
            int life = sent.FindIndex(p => p.Opcode == ZoneMessageOpcode.ResourcesBase);
            Assert.True(full >= 0, string.Join(", ", sent.Select(p => p.Opcode)));
            Assert.True(life > full, "Life must come after the full data, which clears it: " + string.Join(", ", sent.Select(p => p.Opcode)));
            (ulong guid, _, uint type, uint value) = Health(sent).Single();
            Assert.Equal((creature, 1u), (guid, type));
            Assert.True(value > 0);
        }
    }
}
