using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Gateway.Network.Message;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.WorldServer;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Packets;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LandmarkEmulator.Tests.Support
{
    /// <summary>A client connection that keeps what it was sent.</summary>
    public sealed class FakeLink : IClientLink
    {
        public ulong ConnectionId { get; init; } = 1;
        public bool CanSend => true;
        public bool Closed { get; private set; }
        public List<byte[]> Sent { get; } = new();
        public void Send(byte[] packet) => Sent.Add(packet);
        public void Close() => Closed = true;
    }

    /// <summary>
    /// In-memory world state for rule tests: a world configuration (restored on Dispose), sessions with an inventory (no
    /// database: the inventory's saves fail quietly in the background), item definitions, and the packets a session queued.
    /// Everything is set through the same private state the server uses, so no game code changes behaviour for tests.
    /// </summary>
    public sealed class FakeWorld : IDisposable
    {
        private readonly WorldServerConfiguration previous;
        private readonly Dictionary<uint, ItemDefinitionManager.ItemDefinition> previousDefinitions;
        private static readonly FieldInfo DefinitionsField = typeof(ItemDefinitionManager).GetField("definitions", BindingFlags.NonPublic | BindingFlags.Static);

        public WorldServerConfiguration Config { get; }

        /// <summary>A database that is never reached: its context fails at once (no connection string), inside Save's catch.</summary>
        private sealed class NoDatabase : LandmarkEmulator.Database.Configuration.IDatabaseConfig
        {
            public LandmarkEmulator.Database.Configuration.IConnectionString GetConnectionString(LandmarkEmulator.Database.Configuration.DatabaseType type) => null;
        }

        static FakeWorld()
        {
            // The message table (EnqueueMessage looks a model's opcode up in it); built once per process.
            EnsureMessageTable();
            var db = LandmarkEmulator.Shared.Database.DatabaseManager.Instance;
            if (db.CharacterDatabase == null)
                SetProperty(db, "CharacterDatabase", new LandmarkEmulator.Database.Character.CharacterDatabase(new NoDatabase()));
            // Item guids continue from the database's highest; start them here instead.
            FieldInfo next = typeof(Inventory).GetField("nextItemGuid", BindingFlags.NonPublic | BindingFlags.Static)!;
            if ((ulong)next.GetValue(null) == 0)
                next.SetValue(null, 0x6000_0000ul);
            // Game-table lookups an inventory makes, empty: no resource-bag items, no cost multipliers, no tool stats.
            FieldInfo resources = typeof(Inventory).GetField("resourceSlots", BindingFlags.NonPublic | BindingFlags.Static)!;
            resources.SetValue(null, resources.GetValue(null) ?? new Dictionary<uint, (uint Slot, uint Max)>());
            FieldInfo cost = typeof(Inventory).GetField("costMultipliers", BindingFlags.NonPublic | BindingFlags.Static)!;
            cost.SetValue(null, cost.GetValue(null) ?? new Dictionary<uint, float>());
            Inventory.SendItemStats = false;
        }

        /// <summary>Builds the zone message table unless it is built (a second Initialise throws on its duplicate keys).</summary>
        public static void EnsureMessageTable()
        {
            lock (typeof(ZoneMessageManager))
                if (typeof(ZoneMessageManager).GetField("serverMessageOpcodes", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(ZoneMessageManager.Instance) == null)
                    ZoneMessageManager.Instance.Initialise();
        }

        public FakeWorld(Action<WorldServerConfiguration> configure = null)
        {
            previous = ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            Config = new WorldServerConfiguration();
            configure?.Invoke(Config);
            SetConfig(Config);
            previousDefinitions = (Dictionary<uint, ItemDefinitionManager.ItemDefinition>)DefinitionsField.GetValue(null);
            DefinitionsField.SetValue(null, new Dictionary<uint, ItemDefinitionManager.ItemDefinition>(previousDefinitions));
            if (EquippedSlotsField.GetValue(null) == null)
                EquipSlots((2, 2), (4, 7), (5, 8), (8, 3));
        }

        public static void SetConfig(WorldServerConfiguration config)
            => typeof(ConfigurationManager<WorldServerConfiguration>).GetProperty("Config")!
                .GetSetMethod(true)!.Invoke(ConfigurationManager<WorldServerConfiguration>.Instance, new object[] { config });

        private static readonly FieldInfo EquippedSlotsField = typeof(Inventory).GetField("equippedSlots", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>
        /// The equipped container's (equipment slot, slot number) pairs, as InventoryDefinition.txt would give them (the
        /// game tables are not loaded in tests): main hand 2 at 2, feet 4 at 7, grappling hook 5 at 8, chest 8 at 3.
        /// </summary>
        public static void EquipSlots(params (uint EquipSlotId, uint SlotNumber)[] slots)
            => EquippedSlotsField.SetValue(null, slots.ToHashSet());

        public void Define(ItemDefinitionManager.ItemDefinition definition)
            => ((Dictionary<uint, ItemDefinitionManager.ItemDefinition>)DefinitionsField.GetValue(null))[definition.Id] = definition;

        private static ulong nextItem = 0x5000_0000;

        public static CharacterItemModel Item(uint definition, uint count = 1, uint container = Inventory.BagContainer, uint slot = 0,
            float wear = 0f, DateTime? tradeableAfter = null)
            => new() { Id = ++nextItem, ItemDefinitionId = definition, Count = count, Container = container, Slot = slot, Wear = wear,
                TradeableAfter = tradeableAfter };

        /// <summary>A logged-in player with these items (at least one, or the inventory would hand out the starter kit).</summary>
        public WorldSession Session(ulong guid, string name, params CharacterItemModel[] items) => Session(guid, name, null, items);

        /// <summary>As <see cref="Session(ulong, string, CharacterItemModel[])"/>, signed in with this account (membership).</summary>
        public WorldSession Session(ulong guid, string name, LandmarkEmulator.Database.Auth.Model.AccountModel account,
            params CharacterItemModel[] items)
        {
            var session = new WorldSession(new FakeLink(), account, "0.695.0", ClientProtocol.LandmarkClientProtocol_709, "");
            foreach (CharacterItemModel item in items)
                item.CharacterId = guid;
            var character = new CharacterModel { Id = guid, Name = name, Items = items.Length > 0 ? items.ToList()
                : new List<CharacterItemModel> { Item(3253) } };
            typeof(WorldSession).GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, character);
            SetProperty(session, nameof(WorldSession.PlayerGuid), guid);
            SetProperty(session, nameof(WorldSession.Inventory), new Inventory(character));
            return session;
        }

        public static void SetProperty(object target, string name, object value)
            => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetSetMethod(true)!.Invoke(target, new[] { value });

        /// <summary>The packets queued for a session (opcode and body), taken out of its queue.</summary>
        public static List<(ZoneMessageOpcode Opcode, byte[] Body)> Outgoing(WorldSession session)
        {
            var queue = (ConcurrentQueue<ZonePacket>)typeof(WorldSession)
                .GetField("outgoingPackets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session);
            var list = new List<(ZoneMessageOpcode, byte[])>();
            while (queue.TryDequeue(out ZonePacket packet))
                list.Add((packet.Opcode, packet.Data));
            return list;
        }

        public void Dispose()
        {
            SetConfig(previous);
            DefinitionsField.SetValue(null, previousDefinitions);
        }
    }

    /// <summary>Calls private static members of the server (pure helpers that have no public entry point).</summary>
    public static class Private
    {
        public static object Call(Type type, string method, params object[] args)
        {
            MethodInfo m = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(x => x.Name == method && x.GetParameters().Length == args.Length)
                .FirstOrDefault(x => x.GetParameters().Zip(args).All(p => p.Second == null || p.First.ParameterType.IsInstanceOfType(p.Second)
                    || p.First.ParameterType.IsByRef))
                ?? throw new MissingMethodException(type.Name, method);
            try
            {
                return m.Invoke(null, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }

        public static T Call<T>(Type type, string method, params object[] args) => (T)Call(type, method, args);

        public static object Field(Type type, string name)
            => type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null);

        public static Type Nested(Type type, string name) => type.GetNestedType(name, BindingFlags.NonPublic | BindingFlags.Public)!;
    }
}
