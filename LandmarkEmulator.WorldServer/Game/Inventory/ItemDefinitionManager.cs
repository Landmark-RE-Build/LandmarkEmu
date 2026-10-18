using System.Linq;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;

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
            /// <summary>
            /// May the item's auto-resupply switch be turned on? The client's own check before it sends Inventory 0x2D
            /// sub 0x0D (FUN_142f1d360, called by the sender FUN_142f261b0) [P]: the definition's u32 at +0x100 and at
            /// +0x28 above 0, its u32 at +0xfc above 0 and its stack size (+0xe4) above 1, the last two through the
            /// definition-variable overrides (ClientDefinitionVariableManager +0x75df0, FUN_143c2caa0 / FUN_143c2cb40;
            /// the raw value when there is none, and this server sends none). What +0x28 / +0xfc / +0x100 mean is not
            /// known yet [I: price-like, the toggle sits on the CoinStoreScreen Lua table]. No 0.695 definition passes:
            /// only 5 Cloth Cap has the three set (100, 1, 1), and it stacks to 1.
            /// </summary>
            public bool AutoResupplyAllowed { get; init; }
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
            /// UNIQUE_EQUIP: third flag byte bit 7 (client definition +0x0E). Set on exactly the 21 items of equipment slot
            /// 3 "Accessory" in the live dump (rings, bracers, orbs, earrings, brooches, belts; checked over all 2,885
            /// records, 2026-10-16) and on no other item — the tooltip line "UI.Tooltip.UniqueEquip" (text 562455
            /// "Unique Equipped", FUN_1434655a0 tooltip bit 2) and the transfer refusal 0x22 (Equip.UniqueEquip 562352
            /// "Only a single instance of this type of item may be equipped at a time.", dialog FUN_142f1eb90 opened
            /// from the TransferItemResponse handler FUN_142f202e0) are its client side. The bit-to-flag mapping is
            /// INFERRED from that exact split (no client code reads +0x0E in a decompiled path yet; trace/systems/
            /// inventory.md "Item flags"). Patch note P4840 (26 Apr 2016): "Multiple copies of the Ring of Bounty can
            /// no longer be equipped at the same time" (TASKS 6.54).
            /// </summary>
            public bool UniqueEquip { get; init; }
            /// <summary>
            /// Record field u15 (the 2nd u32 after the model strings; the client's reader FUN_142c176d0 stores it at
            /// definition +0xcc). Read as the item's type: in the server's dump it is 1 on most items, 6 on exactly the 58
            /// "Recipe:" items whose grant is a Recipes row and 45 on exactly the 526 "Recipe:" items whose grant is a
            /// PropDefs row (checked over all 2,885 records, 2026-10-16). INFERRED meaning ("item type"); the split is
            /// exact, so <see cref="RecipeBook"/> uses it to tell the two grant kinds apart (the id ranges overlap: 390
            /// prop grants are also valid recipe ids).
            /// </summary>
            public uint TypeId { get; init; }
            /// <summary>
            /// The record's grant: the first u32 after the record body (after the u59 list, client definition +0x238, read
            /// by the LZ4 wrapper FUN_142c19850 right after FUN_142c176d0 returns; every record in the dump ends with
            /// exactly 20 bytes there, so this is also "record length − 20", trace/0695/items-695.md §1.1). What it names
            /// depends on <see cref="TypeId"/>: a prop id (45, prop recipe items), a recipe id (6, recipe items), a title
            /// id (4519 → 60) or a server-side reward set (fireworks packs 4444-4449 → 1124-1129). 0 when none.
            /// </summary>
            public uint GrantId { get; init; }
            /// <summary>The u32 after <see cref="GrantId"/> (definition +0x23c); 0 in every captured record.</summary>
            public uint GrantAmount { get; init; }
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
        /// The login list as it goes out: <see cref="loginPacket"/> cut into packets of at most
        /// <see cref="LoginChunkSize"/> definitions each (one packet when the setting is 0). See <see cref="SendLoginList"/>.
        /// </summary>
        private static List<byte[]> loginPackets = new();

        /// <summary>
        /// Most item definitions per 0x4B packet at world entry. Setting ItemDefinitionChunkSize (WorldServer.json /
        /// environment, Docker ITEM_DEFINITION_CHUNK_SIZE); default 100, 0 = the whole list in one packet as the live
        /// dump has it. Only the cut differs: the client ends up with the same cache either way (see SendLoginList).
        /// </summary>
        public static int LoginChunkSize =>
            Math.Max(0, LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration?.GetValue("ItemDefinitionChunkSize", 100) ?? 100);

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
            var entries = new List<byte[]>((int)count);   // every entry in list order, for the packets SendLoginList cuts
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
                entries.Add(chunk);
            }
            // Items the dump lacks, built from a template item (ItemDefinitionBuilder): added to the table and the list.
            var built = new List<byte[]>();
            foreach (ItemDefinitionBuilder.Spec spec in ItemDefinitionBuilder.Items)
            {
                if (result.ContainsKey(spec.Id) || !result.TryGetValue(spec.Template, out ItemDefinition template)
                    || ItemDefinitionBuilder.Build(spec, template.RawData) is not byte[] chunk)
                {
                    log.Warn($"Item {spec.Id} ({spec.Note}) not built: id taken, template {spec.Template} missing or unfit.");
                    continue;
                }
                ItemDefinition definition = Parse(Lz4Block.Decode(chunk.AsSpan(4, BitConverter.ToUInt16(chunk, 0)), BitConverter.ToUInt16(chunk, 2)));
                definition.RawData = chunk;
                result[definition.Id] = definition;
                built.Add(chunk);
                entries.Add(chunk);
            }
            if (built.Count > 0)
            {
                // The list: {u16 opcode, u16 sub, u32 size (bytes after it), u32 count, entries}.
                var grown = new List<byte>(data);
                foreach (byte[] chunk in built)
                    grown.AddRange(chunk);
                data = grown.ToArray();
                BitConverter.TryWriteBytes(data.AsSpan(4), data.Length - 8);
                BitConverter.TryWriteBytes(data.AsSpan(8), count + (uint)built.Count);
                log.Info($"Built {built.Count} item definition(s) the dump lacks.");
            }
            definitions = result;
            loginPacket = data.AsSpan(2).ToArray();   // the CommandBase body as sent (u16 sub 0x4B onwards)
            loginPackets = CutLoginList(entries, LoginChunkSize);
            var index = new Dictionary<uint, (ItemDefinition, ItemAbility)>();
            foreach (ItemDefinition definition in result.Values)
                foreach (ItemAbility ability in definition.Abilities)
                    index.TryAdd(ability.AbilityId, (definition, ability));
            abilityIndex = index;
            log.Info($"Loaded {definitions.Count} item definitions (login list: {loginPackets.Count} packet(s)).");
        }

        /// <summary>
        /// Cuts the login list into CommandBase 0x4B bodies of at most <paramref name="perPacket"/> entries, each in the
        /// list's own layout {u16 sub 0x4B, u32 size (bytes after it), u32 count, entries} and in list order. 0 (or a
        /// size at least the whole list) gives the one packet the dump holds, byte for byte.
        /// </summary>
        private static List<byte[]> CutLoginList(List<byte[]> entries, int perPacket)
        {
            var packets = new List<byte[]>();
            if (perPacket <= 0 || perPacket >= entries.Count)
            {
                packets.Add(loginPacket);
                return packets;
            }
            for (int first = 0; first < entries.Count; first += perPacket)
            {
                int n = Math.Min(perPacket, entries.Count - first);
                int bytes = 0;
                for (int i = first; i < first + n; i++)
                    bytes += entries[i].Length;
                var body = new byte[2 + 4 + 4 + bytes];
                BitConverter.TryWriteBytes(body.AsSpan(0), (ushort)0x4B);
                BitConverter.TryWriteBytes(body.AsSpan(2), 4 + bytes);
                BitConverter.TryWriteBytes(body.AsSpan(6), n);
                int at = 10;
                for (int i = first; i < first + n; i++)
                {
                    entries[i].CopyTo(body, at);
                    at += entries[i].Length;
                }
                packets.Add(body);
            }
            return packets;
        }

        /// <summary>
        /// At world entry: the whole item definition list, CommandBase 0x4B ItemDefinitions {u16 sub, u32 size, u32 count,
        /// per item u16 compressed size, u16 size, LZ4 block}, as the live server sent it (the packet dump is that login
        /// packet). The client handles it in FUN_143358210 (0.589; 0.695 FUN_1434042d0), logging a line to
        /// ItemDefinitions.log per item and opening that file for each line, which is most of the "Loading into world"
        /// wait (about 15 s for 2,885 items on a PC with real-time file scanning). Skipped when the same client logs back
        /// in (see <see cref="reloginKeys"/>).
        /// <para>
        /// Sent as several 0x4B packets of <see cref="LoginChunkSize"/> definitions (2026-10-18, "the client stops
        /// responding at 16 %"). The whole list in one packet is handled in one go on the client's main thread, while
        /// its run state is Connecting (0xb; the loading bar stays at the 16 % that StartingLogin left, FUN_142d5cbc0
        /// case 6 → 15 + 1): the 0.695 test copy logged SlowFrameMs=25523 for that one frame (Logs, 2026-10-18 04:38,
        /// 2,964 "Received item definition data" lines at ~8.5 ms each) and Windows marked the window Not Responding.
        /// In Connecting the client gives the gateway connection 1000 ms per pass (FUN_142df6120(…, 1000), part_0065
        /// LAB_142cd531e; the budget reaches the threaded UdpManager's event delivery FUN_142b60910 in EDX, which
        /// hands over queued packets one by one until 1000 ms have passed [P, disassembly 0x142df6120 → 0x143a0b370 →
        /// 0x143a0b440]); the run loop then pumps Windows messages ("BC::GiveTime::Windows messages", FUN_142d8c1e0)
        /// and draws the loading screen. So with the list cut up, no pass runs past about 1 s plus one packet.
        /// What the client keeps is the same [P]: each 0x4B is read whole (FUN_143382c90: u16 sub, u32 size, blob;
        /// the dispatcher FUN_1433baf00 case 0x4b drops a packet with bytes left over) and FUN_1434042d0 adds its
        /// entries to the cache (FUN_143402db0, no clearing) and then rebuilds the Character.Materials data source
        /// from the whole cache (FUN_143406d30), once per packet. The order is kept, and SendSelfToClient still
        /// follows the last packet (OnCharacterLogin queues it after this).
        /// </para>
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
            foreach (byte[] packet in loginPackets)
                session.EnqueueMessage(ZoneMessageOpcode.CommandBase, packet);
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
            reader.ReadByte();
            byte flags2 = reader.ReadByte();
            uint nameId = reader.ReadUInt();
            uint descriptionId = reader.ReadUInt();
            uint imageSetId = reader.ReadUInt();
            var u20 = new uint[4];                      // client definition +0x24, +0x28, +0x2c, +0x30
            for (int i = 0; i < 4; i++) u20[i] = reader.ReadUInt();
            uint equipSlot = reader.ReadUInt();
            string model = reader.ReadString();
            reader.ReadString();
            var u14 = new uint[5];
            for (int i = 0; i < 5; i++) u14[i] = reader.ReadUInt();
            reader.ReadUInt();                          // min stack
            reader.ReadUInt();
            uint maxStack = reader.ReadUInt();
            string tintAlias = reader.ReadString();
            var ue8 = new uint[10];                     // client definition +0xe8 .. +0x10c
            for (int i = 0; i < 10; i++) ue8[i] = reader.ReadUInt();
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
            // The record's tail (client FUN_142c176d0 after the abilities: three strings → +0x80 / +0x98 / +0xb0, u32 →
            // +0x184, u32 list → +0x1d0 (FUN_142c34a10); then the LZ4 wrapper FUN_142c19850 reads u32 → +0x238 (grant),
            // +0x23c, +0x240, +0xf8 and a list → +0x248). Read defensively: a short record keeps grant 0.
            uint grantId = 0, grantAmount = 0;
            try
            {
                reader.ReadString();
                reader.ReadString();
                reader.ReadString();
                reader.ReadUInt();
                uint u59 = reader.ReadUInt();
                for (uint i = 0; i < u59; i++) reader.ReadUInt();
                grantId = reader.ReadUInt();
                grantAmount = reader.ReadUInt();
            }
            catch (Exception)
            {
                grantId = grantAmount = 0;
            }
            return new ItemDefinition
            {
                Id = id, NameId = nameId, DescriptionId = descriptionId, ImageSetId = imageSetId, Categories = categories, EquipSlotId = equipSlot, MaxStack = maxStack,
                ModelFile = model, TintAlias = tintAlias, Abilities = abilities, PropId = u35[3],
                DurabilitySet = afterQuality[4], Quality = quality, AmbientEffect = u14[3], NoTrade = (flags0 & 0x80) != 0,
                UniqueEquip = (flags2 & 0x80) != 0,
                TypeId = u14[1], GrantId = grantId, GrantAmount = grantAmount,
                // FUN_142f1d360: +0x100 > 0, +0x28 > 0, +0xfc > 0, stack size > 1.
                AutoResupplyAllowed = ue8[6] > 0 && u20[1] > 0 && ue8[5] > 0 && maxStack > 1
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
