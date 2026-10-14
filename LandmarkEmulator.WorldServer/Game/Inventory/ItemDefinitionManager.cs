using System.Linq;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Inventory
{
    /// <summary>
    /// What the server knows about each item definition, read from the item definitions packet the server sends at
    /// login (the live game's list). Only the fields the server needs are kept.
    /// </summary>
    public static class ItemDefinitionManager
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public class ItemDefinition
        {
            public uint Id { get; init; }
            public uint NameId { get; init; }
            /// <summary>
            /// Equipment slot (EquipmentSlotDefinitions.txt): 2 = main hand, 4 = feet, 5 = grappling hook, 8 = chest...
            /// </summary>
            public uint EquipSlotId { get; init; }
            public uint MaxStack { get; init; }
            public string ModelFile { get; init; }
            /// <summary>
            /// The weapon's ambient composite effect (record field u17, after the model strings): set on three weapons, e.g.
            /// 11824 PFX_Weapon_Ambient_Fire_Bow_01, 11825 ..Lightning_Sword2H_01, 11826 ..Physical_Sword1H_01.
            /// </summary>
            public uint AmbientEffect { get; init; }
            public uint DescriptionId { get; init; }
            public uint ImageSetId { get; init; }
            /// <summary>Item category ids (ReferenceData item categories), e.g. 2 for the pick.</summary>
            public List<uint> Categories { get; init; } = new();
            public string TintAlias { get; init; }
            /// <summary>
            /// Abilities the item gives. Type 3 is the primary use (the pick's swing, ability 1018); the client picks its
            /// tool mode (mining, logging, reaping) from which types the held item has.
            /// </summary>
            public List<ItemAbility> Abilities { get; init; } = new();
            /// <summary>The prop this item places (PropDefs id; item field u35_39[3]), 0 if none.</summary>
            public uint PropId { get; init; }
            /// <summary>
            /// DurabilitySet.txt id (definition +0x168; the 5th u32 after quality): 2 weapons, 3 armour, 0 = never wears
            /// (gathering tools, materials).
            /// </summary>
            public uint DurabilitySet { get; init; }
            /// <summary>Quality (RefItemQuality.txt: 0 common … 3, 4); colours the name and sets the cost multiplier.</summary>
            public uint Quality { get; init; }
            /// <summary>
            /// NO_TRADE (first flag byte bit 7, client definition +0x0C: tooltip "UI.Tooltip.NoTrade", Lua NoTrade): Lumens,
            /// Build Site Flags, store expansions, titles, founder pickaxes...
            /// </summary>
            public bool NoTrade { get; init; }
            /// <summary>
            /// The definition exactly as in the definitions packet (u16 compressed size, u16 decompressed size, LZ4
            /// block). Tooltips carry it back to the client, which decompresses it (FUN_142b800a0).
            /// </summary>
            public byte[] RawData { get; set; }
        }

        public record ItemAbility(uint AbilityId, uint Type, string Input, string Category);

        private static Dictionary<uint, ItemDefinition> definitions = new();
        private static Dictionary<uint, (ItemDefinition, ItemAbility)> abilityIndex = new();

        private static byte[] loginPacket;

        /// <summary>
        /// Accounts logging out to character select, with the login session key the server handed their client
        /// (<see cref="Network.Handlers.CharacterSelectHandler"/>). The client keeps its item definition cache across
        /// a logout (logging in again, it logs "Overwriting existing item definition in cache" for every one), so when
        /// that same client comes back - the account's session key is still the one issued here; starting a new client
        /// through the login service replaces it - the login list is not sent again.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, string> reloginKeys = new();

        public static ItemDefinition Get(uint id) => definitions.TryGetValue(id, out ItemDefinition d) ? d : null;
        public static int Count => definitions.Count;
        public static IEnumerable<ItemDefinition> All => definitions.Values;

        /// <summary>
        /// Reads the item definitions packet (opcode bytes, u32 size, u32 count, then per item a u16 compressed size,
        /// u16 decompressed size and an LZ4 block).
        /// </summary>
        public static void Load(string packetPath)
        {
            var result = new Dictionary<uint, ItemDefinition>();
            byte[] data = File.ReadAllBytes(packetPath);
            int offset = 4 + 4;
            uint count = BitConverter.ToUInt32(data, offset);
            offset += 4;
            for (uint i = 0; i < count; i++)
            {
                ushort compressed = BitConverter.ToUInt16(data, offset);
                ushort decompressed = BitConverter.ToUInt16(data, offset + 2);
                byte[] chunk = data.AsSpan(offset, 4 + compressed).ToArray();
                offset += 4;
                byte[] item = Lz4Block.Decode(data.AsSpan(offset, compressed), decompressed);
                offset += compressed;

                ItemDefinition definition = Parse(item);
                definition.RawData = chunk;
                result[definition.Id] = definition;
            }
            definitions = result;
            loginPacket = data.AsSpan(2).ToArray();   // the CommandBase body as sent (u16 sub 0x4B onwards)
            var index = new Dictionary<uint, (ItemDefinition, ItemAbility)>();
            foreach (ItemDefinition definition in result.Values)
                foreach (ItemAbility ability in definition.Abilities)
                    index.TryAdd(ability.AbilityId, (definition, ability));
            abilityIndex = index;
            log.Info($"Loaded {definitions.Count} item definitions.");
        }

        /// <summary>
        /// At world entry: the whole item definition list, CommandBase 0x4B ItemDefinitions {u16 sub, u32 size, u32 count,
        /// per item u16 compressed size, u16 size, LZ4 block}, as the live server sent it (the packet dump is that login
        /// packet). The client handles it in FUN_143358210, logging two lines to ItemDefinitions.log per item and
        /// opening that file for each line, which is most of the "Loading into world" wait (about 15 s for 2,885 items
        /// on a PC with real-time file scanning). Skipped when the same client logs back in (see <see cref="reloginKeys"/>).
        /// </summary>
        public static void SendLoginList(WorldSession session)
        {
            // Only a client that came back from character select (its world ticket starts with "R": it signed in to
            // the login server with a relogin key) can still hold a list. A freshly started client - launcher, direct
            // ticket, a restart after a crash - has none, whatever key the account last had: skipping the list for one
            // left it without definitions, and using an item then read a null definition in the client (2026-10-14).
            bool sameClient = session.LoginTicket.StartsWith("R", StringComparison.Ordinal);
            // A relogin session this server issued with this item list (RelogTag) means the client still holds it,
            // even across a restart of this server; another server, or changed items, send the list again.
            if (sameClient && session.Account?.SessionKey?.StartsWith(RelogTag, StringComparison.Ordinal) == true)
            {
                reloginKeys.TryRemove(session.Account.Id, out _);
                log.Info($"Account {session.Account.Id} is back from character select with this server's item list: not resent.");
                return;
            }
            if (sameClient && session.Account != null && reloginKeys.TryRemove(session.Account.Id, out string key) && key == session.Account.SessionKey)
            {
                log.Info($"Account {session.Account.Id} is back from character select on the same client: its item definitions are cached, not resent.");
                return;
            }
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, loginPacket);
        }

        /// <summary>The account is going to character select with this login session key (its client stays running).</summary>
        public static void NoteRelogin(ulong accountId, string sessionKey) => reloginKeys[accountId] = sessionKey;

        /// <summary>
        /// Start of every relogin session key this server issues (logout to character select, shutdown): "R", this
        /// world's zone_server id and a fingerprint of its item list, 12 hex digits in all. A client that comes back
        /// with it was given this list here; resending made it overwrite all 2,885 cached definitions, and after a
        /// world restart that relog then rebuilt its terrain for 1-2 minutes (2026-10-14).
        /// </summary>
        public static string RelogTag => relogTag ??= "R" + (ConfigurationTag() & 0xFFF).ToString("X3")
            + (Fingerprint() & 0xFFFFFFFF).ToString("X8");
        private static string relogTag;

        /// <summary>A fresh relogin session key (32 hex-ish characters) starting with <see cref="RelogTag"/>.</summary>
        public static string NewRelogKey() => (RelogTag + Convert.ToHexString(Shared.Network.Cryptography.RandomProvider.GetBytes(16u)))[..32];

        private static ulong ConfigurationTag() =>
            LandmarkEmulator.Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.ZoneServerId ?? 0;

        private static ulong Fingerprint()
        {
            ulong hash = 14695981039346656037UL;   // FNV-1a over the login list as sent
            foreach (byte b in loginPacket ?? Array.Empty<byte>())
                hash = (hash ^ b) * 1099511628211UL;
            return hash;
        }

        /// <summary>
        /// CommandBase 0x49 ItemDefinitionRequest {u32 id}: the client asks for a definition it lacks (FUN_14335a2e0,
        /// from its icon / tooltip wait lists). Answer: 0x4A ItemDefinitionReply {u16 sub, u32 size, one entry as in the
        /// login list}; the client adds it (FUN_143356cf0 with the refresh flag) and drops its pending request.
        /// </summary>
        public static void SendDefinition(WorldSession session, uint id)
        {
            ItemDefinition definition = Get(id);
            if (definition == null)
            {
                log.Debug($"Client asked for unknown item definition {id}.");
                return;
            }
            var output = new List<byte>(6 + definition.RawData.Length);
            output.AddRange(BitConverter.GetBytes((ushort)0x4A));
            output.AddRange(BitConverter.GetBytes(definition.RawData.Length));
            output.AddRange(definition.RawData);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
        }

        /// <summary>
        /// Reads the fields the server needs from one decompressed item definition: id, name/description text ids,
        /// image set, categories, equipment slot, stack size, model and tint, the prop it places, and its abilities
        /// (the rest of the record is skipped).
        /// </summary>
        private static ItemDefinition Parse(byte[] item)
        {
            var reader = new GamePacketReader(item);
            uint id = reader.ReadUInt();
            byte flags0 = reader.ReadByte();
            reader.ReadByte(); reader.ReadByte();
            uint nameId = reader.ReadUInt();
            uint descriptionId = reader.ReadUInt();
            uint imageSetId = reader.ReadUInt();
            for (int i = 0; i < 4; i++) reader.ReadUInt();
            uint equipSlot = reader.ReadUInt();
            string model = reader.ReadString();
            reader.ReadString();
            var u14 = new uint[5];
            for (int i = 0; i < 5; i++) u14[i] = reader.ReadUInt();
            reader.ReadUInt();                          // min stack
            reader.ReadUInt();
            uint maxStack = reader.ReadUInt();
            string tintAlias = reader.ReadString();
            for (int i = 0; i < 10; i++) reader.ReadUInt();
            reader.ReadString();
            reader.ReadString();
            var u35 = new uint[5];
            for (int i = 0; i < 5; i++) u35[i] = reader.ReadUInt();
            uint quality = reader.ReadUInt();
            var afterQuality = new uint[6];
            for (int i = 0; i < 6; i++) afterQuality[i] = reader.ReadUInt();
            var categories = new List<uint>();
            uint categoryCount = reader.ReadUInt();
            for (int i = 0; i < categoryCount; i++) categories.Add(reader.ReadUInt());
            for (int i = 0; i < 6; i++) reader.ReadUInt();
            var abilities = new List<ItemAbility>();
            uint abilityCount = reader.ReadUInt();
            for (int i = 0; i < abilityCount; i++)
            {
                uint abilityId = reader.ReadUInt();
                uint type = reader.ReadUInt();
                string input = reader.ReadString();
                string category = reader.ReadString();
                reader.ReadUInt();
                reader.ReadByte();
                abilities.Add(new ItemAbility(abilityId, type, input, category));
            }
            return new ItemDefinition
            {
                Id = id, NameId = nameId, DescriptionId = descriptionId, ImageSetId = imageSetId, Categories = categories, EquipSlotId = equipSlot, MaxStack = maxStack,
                ModelFile = model, TintAlias = tintAlias, Abilities = abilities, PropId = u35[3],
                DurabilitySet = afterQuality[4], Quality = quality, AmbientEffect = u14[3], NoTrade = (flags0 & 0x80) != 0
            };
        }

        /// <summary>Item definitions that place a given prop.</summary>
        public static IEnumerable<ItemDefinition> ItemsForProp(uint propId)
            => definitions.Values.Where(d => d.PropId == propId);

        /// <summary>
        /// The item that gives an ability, and the ability's entry on it; null when no item has it.
        /// </summary>
        public static (ItemDefinition Item, ItemAbility Ability)? FindAbility(uint abilityId)
            => abilityIndex.TryGetValue(abilityId, out var found) ? found : null;
    }
}
