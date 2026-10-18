using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Handlers;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Saved bag filters (Game.InventoryFilters, docs/protocol.md §14 "Bag filters"): PlayerFilterUpdatePacket 0xDD sub 4
    /// {u32 FilterId, string FilterName, string FilterString, u32 Op} (writer FUN_142fc3d20; op 0 add / update, 1 remove,
    /// 2 reset from FUN_142efd5e0 / FUN_142efd330 / FUN_142efd4f0) changes the character's set, which comes back in
    /// SendSelfToClient's InventoryFilterManager (reader FUN_142c3d470) with NextFilterId one past the highest id.
    /// </summary>
    public class InventoryFilterRuleTests : IDisposable
    {
        private readonly FakeWorld world = new();
        private static ulong nextGuid = 0xF1700;

        public InventoryFilterRuleTests() => FakeWorld.EnsureMessageTable();

        public void Dispose() => world.Dispose();

        private static PlayerFilterUpdate Update(uint op, uint id, string name = "", string filter = "")
        {
            byte[] body = Wire.Write(new PlayerFilterUpdate { Op = op, FilterId = id, FilterName = name, FilterString = filter });
            Assert.True(PlayerFilterUpdate.TryParse(body, out PlayerFilterUpdate update));
            return update;
        }

        private static InventoryFilters.Outcome Apply(List<CharacterInventoryFilterModel> filters, PlayerFilterUpdate update)
            => InventoryFilters.Apply(filters, update, out _);

        /// <summary>The whole zone packet as the client sends it: u16 0xDD, u8 sub 4, then the body.</summary>
        private static ClientPcDataRequest Packet(PlayerFilterUpdate update)
        {
            byte[] packet = new byte[] { 0xDD, 0x00, 0x04 }.Concat(Wire.Write(update)).ToArray();
            (ZoneMessageOpcode? opcode, int length) = WorldSession.GetOpcode(packet);
            Assert.Equal(ZoneMessageOpcode.ClientPcDataBase, opcode);
            var request = new ClientPcDataRequest();
            request.Read(new GamePacketReader(packet.Skip(length).ToArray()));
            return request;
        }

        [Fact]
        public void Add_ThenNextFilterIdIsOnePastTheHighest()
        {
            var filters = new List<CharacterInventoryFilterModel>();
            Assert.Equal(1u, InventoryFilters.NextFilterId(filters));   // live capture and the client's default
            Assert.Equal(InventoryFilters.Outcome.Added, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 1, "Ores", "3,4")));
            Assert.Equal(InventoryFilters.Outcome.Added, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 7, "Wood", "5")));
            Assert.Equal(8u, InventoryFilters.NextFilterId(filters));
            Assert.Equal(new uint[] { 1, 7 }, filters.Select(f => f.FilterId).ToArray());
        }

        [Fact]
        public void AddWithAnExistingId_ReplacesIt_SameValuesChangeNothing()
        {
            var filters = new List<CharacterInventoryFilterModel>();
            Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 2, "Ores", "3"));
            Assert.Equal(InventoryFilters.Outcome.Updated, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 2, "Metals", "3,9")));
            Assert.Equal(InventoryFilters.Outcome.Unchanged, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 2, "Metals", "3,9")));
            CharacterInventoryFilterModel only = Assert.Single(filters);
            Assert.Equal("Metals", only.Name);
            Assert.Equal("3,9", only.FilterString);
        }

        [Fact]
        public void Remove_AndReset()
        {
            var filters = new List<CharacterInventoryFilterModel>();
            Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 1, "A", "1"));
            Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 2, "B", "2"));
            // RemoveFilter sends the removed filter's strings too; only the id counts.
            Assert.Equal(InventoryFilters.Outcome.Removed, Apply(filters, Update(PlayerFilterUpdate.Remove, 1, "A", "1")));
            Assert.Equal(InventoryFilters.Outcome.Unchanged, Apply(filters, Update(PlayerFilterUpdate.Remove, 1, "A", "1")));
            Assert.Equal(new uint[] { 2 }, filters.Select(f => f.FilterId).ToArray());
            Assert.Equal(InventoryFilters.Outcome.Cleared, Apply(filters, Update(PlayerFilterUpdate.Reset, 0)));
            Assert.Empty(filters);
            Assert.Equal(InventoryFilters.Outcome.Unchanged, Apply(filters, Update(PlayerFilterUpdate.Reset, 0)));
            Assert.Equal(1u, InventoryFilters.NextFilterId(filters));
        }

        [Fact]
        public void WhatTheClientNeverSends_AndOverTheCaps_IsRejected()
        {
            var filters = new List<CharacterInventoryFilterModel>();
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 0, "A", "1")));
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 1, "", "1")));
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 1, "A", "")));
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters, Update(PlayerFilterUpdate.Remove, 0)));
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters, Update(3, 1, "A", "1")));
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters,
                Update(PlayerFilterUpdate.AddOrUpdate, 1, new string('n', InventoryFilters.MaxNameBytes + 1), "1")));
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters,
                Update(PlayerFilterUpdate.AddOrUpdate, 1, "A", new string('1', InventoryFilters.MaxFilterStringBytes + 1))));
            // The caps are bytes: 128 two-byte characters are 256 bytes.
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters,
                Update(PlayerFilterUpdate.AddOrUpdate, 1, new string('é', 128), "1")));
            Assert.Empty(filters);

            Assert.Equal(InventoryFilters.Outcome.Added, Apply(filters,
                Update(PlayerFilterUpdate.AddOrUpdate, 1, new string('n', InventoryFilters.MaxNameBytes),
                    new string('1', InventoryFilters.MaxFilterStringBytes))));
            for (uint id = 2; id <= InventoryFilters.MaxFilters; id++)
                Assert.Equal(InventoryFilters.Outcome.Added, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, id, "F", "1")));
            Assert.Equal(InventoryFilters.Outcome.Rejected, Apply(filters,
                Update(PlayerFilterUpdate.AddOrUpdate, InventoryFilters.MaxFilters + 1, "F", "1")));
            // At the limit an existing filter can still be changed.
            Assert.Equal(InventoryFilters.Outcome.Updated, Apply(filters, Update(PlayerFilterUpdate.AddOrUpdate, 2, "G", "2")));
            Assert.Equal(InventoryFilters.MaxFilters, filters.Count);
        }

        [Fact]
        public void Handler_KeepsTheChangeOnTheSession_SendsNoReply_AndSendSelfCarriesIt()
        {
            WorldSession s = world.Session(++nextGuid, "Filterer");
            s.InventoryFilters = new List<CharacterInventoryFilterModel>();
            FakeWorld.Outgoing(s);

            MiscHandler.HandleClientPcData(s, Packet(Update(PlayerFilterUpdate.AddOrUpdate, 1, "Ores", "3,4")));
            MiscHandler.HandleClientPcData(s, Packet(Update(PlayerFilterUpdate.AddOrUpdate, 2, "Wood", "5")));
            MiscHandler.HandleClientPcData(s, Packet(Update(PlayerFilterUpdate.Remove, 1, "Ores", "3,4")));
            Assert.Empty(FakeWorld.Outgoing(s));   // the client's 0xDD reader has no sub 4

            SendSelfToClient.InventoryFilterManager self = InventoryFilters.SelfData(s.InventoryFilters);
            var entry = Assert.Single(self.Filters);
            Assert.Equal(2u, entry.Key);
            Assert.Equal(2u, entry.FilterId);
            Assert.Equal("Wood", entry.FilterName);
            Assert.Equal("5", entry.FilterString);
            Assert.Equal(3u, self.NextFilterId);
        }

        [Fact]
        public void Handler_IgnoresOtherSubs_AndUnreadableBodies()
        {
            WorldSession s = world.Session(++nextGuid, "Filterer2");
            s.InventoryFilters = new List<CharacterInventoryFilterModel>();

            var speech = new ClientPcDataRequest();
            speech.Read(new GamePacketReader(new byte[] { 0x01, 0x05, 0x00, 0x00, 0x00 }));
            Assert.Equal(ClientPcDataRequest.SetSpeechPack, speech.SubOpcode);
            MiscHandler.HandleClientPcData(s, speech);

            var truncated = new ClientPcDataRequest();
            truncated.Read(new GamePacketReader(new byte[] { 0x04, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 }));
            MiscHandler.HandleClientPcData(s, truncated);
            Assert.Empty(s.InventoryFilters);

            // No character list yet (before login): nothing happens.
            WorldSession early = world.Session(++nextGuid, "Early");
            MiscHandler.HandleClientPcData(early, Packet(Update(PlayerFilterUpdate.AddOrUpdate, 1, "A", "1")));
            Assert.Null(early.InventoryFilters);
        }
    }
}
