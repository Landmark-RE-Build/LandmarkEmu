using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;
using NLog;

namespace LandmarkEmulator.WorldServer.Network.Message.Model.Shared
{
    /// <summary>
    /// A reward bundle, the client's ClientRewardBundle (base RewardBundleBase, both RTTI). Reader 0.695 FUN_142c2f040
    /// (0.589 FUN_142b95550, same code); it fills the object through fields and virtual setters, and the offsets below
    /// are object offsets. Used by achievements and objectives (Achievement / Objective), encounters (three per
    /// EncounterDetails), RewardBundlePacket (Reward 0x35 sub 1, handled by FUN_1431e2df0 → PlayerRewardEvent →
    /// FUN_1431e2670 "BaseClient::HandleRewardPacket") and the SendSelf collections.
    ///
    /// What reads what (0.695): HandleRewardPacket reads the experience (vtable +0x78), the coins (+0x98 with currency
    /// 0) and the entries ("You gained %d tokens", item and attribute notices); the Journal reads the entries only
    /// ("HasReward", "Rewards", "ObjectiveRewards"). The other getters (vtable +0x48 .. +0x178) have no caller found in
    /// the 0.695 decompile (searched: direct calls of every getter, and virtual calls at those slots in every function
    /// that also walks the entries) — they are named by slot below.
    ///
    /// Live values: the goal capture (Resources/PacketDumps/71-AchievementBase, 2,089 bundles). 1,946 are empty and all
    /// of those have <see cref="IconId"/> and <see cref="NameTextId"/> = 0xFFFFFFFF; the defaults here are those values.
    /// </summary>
    public class RewardBundle : IReadable, IWritable
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// One currency of the bundle (+0x80 HashListMap, reader FUN_142c35320: key = currency id, value = amount;
        /// the getter vtable +0x98 looks a currency up, currency 0 being <see cref="Coins"/>) [P].
        /// </summary>
        public class Currency : IReadable, IWritable
        {
            public uint CurrencyId { get; set; }
            public uint Quantity { get; set; }

            public void Read(GamePacketReader reader)
            {
                CurrencyId = reader.ReadUInt();
                Quantity = reader.ReadUInt();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(CurrencyId);
                writer.Write(Quantity);
            }
        }

        /// <summary>
        /// One reward entry: u32 type, then the entry class's own reader (vtable +0x78; factory = bundle vtable +0x1c0,
        /// classes RewardBundleEntryItem, ...Experience, ...Token, ...Recipe, ...PropRecipe, ...Trophy, ...). Every entry
        /// starts with the base fields (RewardBundleEntryBase reader FUN_143b86880: bool, 6 × u32 = icon, ?, name text,
        /// count, item id, ?, string, u32, bool, u32, u32: 42 bytes with an empty string); only type 1 (Item) is
        /// modelled here, and it carries an item definition only when the bundle's <see cref="IncludeItemData"/> is set.
        /// Live: every captured entry is type 1 without item data.
        /// </summary>
        public class BundleRewardEntry : IReadable, IWritable
        {
            public uint Type { get; set; }
            public byte[] Data { get; set; }

            public void Read(GamePacketReader reader)
            {
                Type = reader.ReadUInt();
                if (Type == 1)
                {
                    // TODO: Seems to always be 42 bytes when Type's 1 but what if type is different?!
                    Data = reader.ReadBytes(42u);
                }
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Type);
                if (Type == 1)
                {
                    // TODO: Handle the 42 bytes discovered in packets.
                    writer.WriteBytes(Data);
                }
            }
        }

        /// <summary>
        /// +0x20e0: passed to every entry before it reads (entry vtable +0x70 → entry +0x5e); the Item entry then also
        /// reads an item definition [P] (decode_achievements.py). Live: false.
        /// </summary>
        public bool IncludeItemData { get; set; }
        /// <summary>+0x7c coins (currency 0): HandleRewardPacket's "coins" amount, which plays a coin effect [P]. Live: 0.</summary>
        public uint Coins { get; set; }
        /// <summary>+0x80 the other currencies [P]. Live: none.</summary>
        public List<Currency> Currencies { get; set; } = new();
        /// <summary>+0x74 experience: HandleRewardPacket's "experience" amount (vtable +0x78) [P]. Live: 0.</summary>
        public uint Experience { get; set; }
        /// <summary>+0x78 u32 (vtable +0x80 adds, +0x88 gets). No reader found [P]. Live: 0.</summary>
        public uint Slot78 { get; set; }
        /// <summary>+0x20a8 u32 (vtable +0x118 / +0x120). No reader found [P]. Live: 0.</summary>
        public uint Slot20A8 { get; set; }
        /// <summary>+0x20d0 i64 (vtable +0xf8 / +0x100). No reader found [P]. Live: 0.</summary>
        public ulong Slot20D0 { get; set; }
        /// <summary>+0x20d8 u32 (vtable +0xd8 / +0xe0). No reader found [P]. Live: 0.</summary>
        public uint Slot20D8 { get; set; }
        /// <summary>+0x20b0 u32 (vtable +0x128 / +0x130). No reader found [P]. Live: 0.</summary>
        public uint Slot20B0 { get; set; }
        /// <summary>+0x20b4 u32 (vtable +0x138 / +0x140). No reader found [P]. Live: 0.</summary>
        public uint Slot20B4 { get; set; }
        /// <summary>+0x20b8 u32 (vtable +0x148 / +0x150). No reader found [P]. Live: 0.</summary>
        public uint Slot20B8 { get; set; }
        /// <summary>+0x20bc u32 (vtable +0x158 / +0x160). No reader found [P]. Live: 0.</summary>
        public uint Slot20BC { get; set; }
        /// <summary>
        /// +0x20e8 u64 (setter vtable +0x60, getter +0x50). HandleRewardPacket fetches it into a local it never uses [P].
        /// Live: 0.
        /// </summary>
        public ulong Slot20E8 { get; set; }
        /// <summary>
        /// +0x20f0 u64 (setter vtable +0x68, getter +0x58; no reader found [P]). Live: the captured character's own guid
        /// (0x4B561BAE72200691) on the 143 bundles with entries, 0 on the empty ones — so the receiving character [I];
        /// Game.Achievements writes the receiving player's guid there.
        /// </summary>
        public ulong CharacterId { get; set; }
        /// <summary>
        /// +0x20c4 the bundle's icon; 0xFFFFFFFF = use the first entry's icon (getter vtable +0xc0, FUN_143b85620) [P].
        /// Live: 0xFFFFFFFF on empty bundles, the first entry's icon on all 143 others (12147 Lumen, 1755, 11143).
        /// </summary>
        public uint IconId { get; set; } = uint.MaxValue;
        /// <summary>
        /// +0x20c8 the bundle's name text; 0xFFFFFFFF = use the first entry's name (getter vtable +0xd0, FUN_143b856c0 →
        /// entry vtable +0x20) [P]. Live: 0xFFFFFFFF on empty bundles, else e.g. 565430 "Lumen", 574253 "Castle Wall -
        /// Long", 575482 "Recipe: The Outcast".
        /// </summary>
        public uint NameTextId { get; set; } = uint.MaxValue;
        public uint BundleRewardEntryCount { get; set; }
        public List<BundleRewardEntry> BundleRewardEntries { get; set; } = new(); // TODO: Write out BundleRewardEntries reader
        /// <summary>
        /// +0x20ac u32, read last (vtable +0x178 / +0x180; no reader found [P]). Live: 0 on empty bundles, else one
        /// number per reward tier: 933 = 15 Lumens (61 bundles), 934 = 35 Lumens + an item, 935 = 80 Lumens, 936 = 180
        /// Lumens + an item, 942 = one recipe, 968 / 1051 = 30 Lumens (1051 also on the castle designs of goals 471 and
        /// 477): the live server's reward (table) id [I].
        /// </summary>
        public uint BundleId { get; set; }

        public void Read(GamePacketReader reader)
        {
            IncludeItemData = reader.ReadBool();
            Coins = reader.ReadUInt();

            uint currencyCount = reader.ReadUInt();
            for (int i = 0; i < currencyCount; i++)
            {
                Currency currency = new();
                currency.Read(reader);
                Currencies.Add(currency);
            }

            Experience = reader.ReadUInt();
            Slot78 = reader.ReadUInt();
            Slot20A8 = reader.ReadUInt();
            Slot20D0 = reader.ReadULong();
            Slot20D8 = reader.ReadUInt();
            Slot20B0 = reader.ReadUInt();
            Slot20B4 = reader.ReadUInt();
            Slot20B8 = reader.ReadUInt();
            Slot20BC = reader.ReadUInt();
            Slot20E8 = reader.ReadULong();
            CharacterId = reader.ReadULong();
            IconId = reader.ReadUInt();
            NameTextId = reader.ReadUInt();

            uint bundleRewardEntryCount = reader.ReadUInt();
            BundleRewardEntryCount = bundleRewardEntryCount;
            for (int i = 0; i < BundleRewardEntryCount; i++)
            {
                BundleRewardEntry rewardEntry = new();
                rewardEntry.Read(reader);
                BundleRewardEntries.Add(rewardEntry);
            }

            BundleId = reader.ReadUInt();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(IncludeItemData);
            writer.Write(Coins);

            writer.Write((uint)Currencies.Count);
            foreach (Currency currency in Currencies)
                currency.Write(writer);

            writer.Write(Experience);
            writer.Write(Slot78);
            writer.Write(Slot20A8);
            writer.Write(Slot20D0);
            writer.Write(Slot20D8);
            writer.Write(Slot20B0);
            writer.Write(Slot20B4);
            writer.Write(Slot20B8);
            writer.Write(Slot20BC);
            writer.Write(Slot20E8);
            writer.Write(CharacterId);
            writer.Write(IconId);
            writer.Write(NameTextId);

            writer.Write((uint)BundleRewardEntries.Count);
            foreach (BundleRewardEntry bundleRewardEntry in BundleRewardEntries)
                bundleRewardEntry.Write(writer);

            writer.Write(BundleId);
        }
    }
}
