using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Crimson Parlor unlockers (TASKS 4.9 / 4.10): the Fantasy Parlor Pack 4419 and the Cowboy Hat 4521 / Ninja Mask 4522
    /// / Samurai Helmet 4523 unlockers. Used from the bag (Inventory 0x2D sub 0x15 ActivateItem → <see cref="ItemUses.TryActivate"/>),
    /// they add Parlor options and tints to the character's customization knowledge. Full trace: trace/0695/leads-L39.md.
    /// <para>
    /// <b>The client's knowledge store</b> [P] (ClientPlayer +0x7ae28, ClientKnownCharCustomizationManager, RTTI 1446ee310)
    /// holds two int sets: set A at +0x08 (player +0x7ae30) = CharacterCreateOptions IDs, tested through vtable +0x8
    /// (FUN_142fd1200); set B at +0x20 (player +0x7ae48) = ArtTint IDs, tested through vtable +0x10 (FUN_142fd1440).
    /// The option-list builder FUN_1430a33a0 lists a row that is not DEFAULT_UNLOCKED (OptionEntry +0x30) only for an
    /// admin, when set A holds its ID, or when the Marketplace sells its UNLOCKING_ITEM_ID (+0x3c; the server's store
    /// bundle for 4419, <see cref="StoreBundles"/>, which also brings the Parlor's Unlock button); otherwise the row is
    /// not shown at all. The tint list FUN_1430a4720 maps each tint's alias hash to its ArtTint ID (hash map
    /// DAT_145431310 +0x2980) and lists a tint that is not DEFAULT_UNLOCKED (ArtTint +0x40) only when set B holds that ID
    /// (or admin / Marketplace, FUN_1430a3210).
    /// </para>
    /// <para>
    /// <b>Wire</b> [P]: SendSelfToClient fills both sets at login (0.695 reader FUN_142c19bd0: u32 n × u32 into +0x7ae30,
    /// then u32 n × u32 into +0x7ae48; written from <see cref="KnownOptions"/> / <see cref="KnownTints"/> by
    /// WorldSession's SendSelf). CharacterCustomization 0x11C sub 2 AddKnowledgeResponse adds to one set while playing
    /// (FUN_1430a29d0 → FUN_14368a830, reader FUN_14368a5d0): u16 0x11C, u8 2, u32 n, n × u32 id, u32 type, nothing more
    /// (the client drops the packet unless it is consumed exactly); type 3 → set A, type 4 → set B; ids already known are
    /// skipped. The client then rebuilds the open Parlor's lists itself (FUN_1430a33a0, FUN_1430a3d30).
    /// </para>
    /// <para>
    /// <b>Grants</b>: the CharacterCreateOptions rows whose UNLOCKING_ITEM_ID is the item [P data] plus the rows
    /// CharCreateOptionEqualities ties them to [I: the hat rows for other races / genders carry item 0, and the item texts
    /// 577700 / 577702 / 577704 promise the hat "for Luminaries of all genders and sizes"], and for 4419 the tints of
    /// ArtTintGroupUnlock groups 2, 5-9 (45 ArtTint ids 1166-1210, the fantasy skintones) [P data].
    /// Stored as <see cref="Unlocks.ParlorKnowledge"/> rows, value = type &lt;&lt; 24 | id (type 3 option, 4 tint, as on
    /// the wire), so a later single-option unlocker fits the same rows.
    /// </para>
    /// </summary>
    public static class Unlockers
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The unlocker items (ids the 0.695 client's own tables name: CharacterCreateOptions / ArtTintGroupUnlock UNLOCKING_ITEM_ID).</summary>
        public const uint FantasyParlorPack = 4419, CowboyHat = 4521, NinjaMask = 4522, SamuraiHelmet = 4523;

        /// <summary>AddKnowledgeResponse sub-opcode and knowledge types (FUN_14368a830: 3 → set A options, 4 → set B tints).</summary>
        private const byte AddKnowledgeSub = 2;
        public const uint OptionType = 3, TintType = 4;

        /// <summary>
        /// ArtTintGroupUnlock.txt (0.695: ID, DESCRIPTION, UNLOCKING_ITEM_ID) — every row unlocks with 4419 ("landmark_v2:
        /// customization: skintone: fantasy 01: black / blue / green / red / white / yellow"). The table is not in the
        /// server's game data folder, so its six rows are kept here.
        /// </summary>
        private static readonly Dictionary<uint, uint> tintGroupItem = new()
        {
            [2] = FantasyParlorPack, [5] = FantasyParlorPack, [6] = FantasyParlorPack,
            [7] = FantasyParlorPack, [8] = FantasyParlorPack, [9] = FantasyParlorPack,
        };

        /// <summary>
        /// ArtTintGroupUnlockTint.txt (0.695: ID 54-118, ART_TINT_ID, UNLOCK_GROUP_ID): group → ArtTint ids (45 rows).
        /// </summary>
        private static readonly Dictionary<uint, uint[]> tintGroupTints = new()
        {
            [2] = new uint[] { 1209, 1210 },
            [5] = Range(1166, 1175),
            [6] = Range(1186, 1195),
            [7] = Range(1196, 1205),
            [8] = Range(1206, 1208),
            [9] = Range(1176, 1185),
        };

        /// <summary>
        /// The ArtTints.txt (0.695) rows with DEFAULT_UNLOCKED 0 — the only tints the Parlor hides — by id and ALIAS_NAME:
        /// 1165 DarkElf_Skin_14 (no unlocker) and 1166-1210 Human_Skin_090-092, 100-141 (the 4419 skintones). The PlayerSet
        /// carries tints as alias hashes (<see cref="AliasHash"/>; checked against AuthServer Resources/ArtTints.json,
        /// whose {id, hash} list has the same ids and hashes).
        /// </summary>
        private static readonly Dictionary<uint, uint> lockedTintByHash = BuildLockedTints();

        private static uint[] Range(uint first, uint last)
            => Enumerable.Range((int)first, (int)(last - first + 1)).Select(i => (uint)i).ToArray();

        private static Dictionary<uint, uint> BuildLockedTints()
        {
            var aliases = new Dictionary<uint, string> { [1165] = "DarkElf_Skin_14" };
            for (uint id = 1166; id <= 1168; id++)
                aliases[id] = $"Human_Skin_{90 + id - 1166:000}";
            for (uint id = 1169; id <= 1210; id++)
                aliases[id] = $"Human_Skin_{100 + id - 1169:000}";
            return aliases.ToDictionary(a => AliasHash(a.Value), a => a.Key);
        }

        /// <summary>
        /// The client's tint alias hash (FUN_143cd0ef0 builds the hash → id map with it): one-at-a-time over signed chars
        /// with arithmetic shifts — the same function as the Game Table setting keys.
        /// </summary>
        private static uint AliasHash(string alias) => InstancedGames.SettingKeyHash(alias);

        /// <summary>CharacterCreateOptions rows: ID → (DEFAULT_UNLOCKED, UNLOCKING_ITEM_ID).</summary>
        private static Dictionary<uint, (bool DefaultUnlocked, uint Item)> options;

        /// <summary>Item → (option ids, tint ids) it unlocks.</summary>
        private static Dictionary<uint, (uint[] Options, uint[] Tints)> grants;

        private static void Ensure()
        {
            if (grants != null)
                return;
            // CharacterCreateOptions.txt: ID^RACE_ID^GENDER_ID^SLOT_ID^ART_SOURCE_ACTOR_ID^PARENT_OPTION_ID^
            // OVERRIDE_WHEN_CHAR_CREATE_DONE^DISPLAYABLE_ART_TINT_TYPE^DEFAULT_UNLOCKED^DESCRIPTION^UNLOCKING_ITEM_ID
            // (client reader FUN_143c6a010: DEFAULT_UNLOCKED → def +0x28, UNLOCKING_ITEM_ID → def +0x2c).
            var rows = GameDataFiles.Rows("CharacterCreateOptions.txt")
                .GroupBy(r => r.U(0))
                .ToDictionary(g => g.Key, g => (DefaultUnlocked: g.First().U(8) != 0, Item: g.First().U(10)));
            var equalities = GameDataFiles.Rows("CharCreateOptionEqualities.txt")
                .GroupBy(r => r.U(1)).ToDictionary(g => g.Key, g => g.Select(r => r.U(2)).ToList());
            var result = new Dictionary<uint, (uint[], uint[])>();
            foreach (uint item in rows.Values.Where(r => r.Item != 0).Select(r => r.Item).Concat(tintGroupItem.Values).Distinct())
            {
                var granted = new SortedSet<uint>();
                foreach (var (id, row) in rows)
                {
                    if (row.Item != item || row.DefaultUnlocked)
                        continue;
                    granted.Add(id);
                    if (equalities.TryGetValue(id, out var same))
                        foreach (uint other in same)
                            if (rows.TryGetValue(other, out var o) && !o.DefaultUnlocked)
                                granted.Add(other);
                }
                uint[] tints = tintGroupItem.Where(g => g.Value == item)
                    .SelectMany(g => tintGroupTints.TryGetValue(g.Key, out var t) ? t : System.Array.Empty<uint>())
                    .Distinct().OrderBy(t => t).ToArray();
                result[item] = (granted.ToArray(), tints);
            }
            options = rows;
            grants = result;
            log.Info("Parlor unlockers: " + string.Join(", ", result.Select(g => $"{g.Key} → {g.Value.Item1.Length} options, {g.Value.Item2.Length} tints")));
        }

        /// <summary>Is this one of the Parlor unlocker items?</summary>
        public static bool Handles(uint itemId)
        {
            Ensure();
            return grants.ContainsKey(itemId);
        }

        /// <summary>The CharacterCreateOptions ids the character knows beyond the defaults (SendSelf list +0x7ae30, type 3).</summary>
        public static List<uint> KnownOptions(ulong characterId) => Known(characterId, OptionType);

        /// <summary>The ArtTint ids the character knows beyond the defaults (SendSelf list +0x7ae48, type 4).</summary>
        public static List<uint> KnownTints(ulong characterId) => Known(characterId, TintType);

        private static List<uint> Known(ulong characterId, uint type)
            => Unlocks.Values(characterId, Unlocks.ParlorKnowledge)
                .Where(v => v >> 24 == type).Select(v => v & 0xFFFFFF).OrderBy(v => v).ToList();

        /// <summary>The "already unlocked" refusal of each item (ShowActivationFailure; trace/0695/leads-L33-L35-L36.md).</summary>
        private static uint AlreadyText(uint itemId) => itemId switch
        {
            FantasyParlorPack => ItemUses.ParlorPackKnownText,
            CowboyHat => ItemUses.CowboyHatKnownText,
            NinjaMask => ItemUses.NinjaMaskKnownText,
            SamuraiHelmet => ItemUses.SamuraiHelmetKnownText,
            _ => ItemUses.FailedItemRequirementText,
        };

        /// <summary>
        /// Uses an unlocker: adds the options / tints the character doesn't know yet (saved, then AddKnowledgeResponse per
        /// type with only the new ids) and takes one item. Nothing new → ShowActivationFailure with the item's
        /// "already unlocked" text and the item is kept. False when the item isn't an unlocker.
        /// </summary>
        public static bool TryUse(WorldSession session, CharacterItemModel item)
        {
            uint itemId = item.ItemDefinitionId;
            if (!Handles(itemId))
                return false;
            var (grantOptions, grantTints) = grants[itemId];
            ulong characterId = session.PlayerGuid;
            var have = Unlocks.Values(characterId, Unlocks.ParlorKnowledge).ToHashSet();
            uint[] newOptions = grantOptions.Where(o => !have.Contains(OptionType << 24 | o)).ToArray();
            uint[] newTints = grantTints.Where(t => !have.Contains(TintType << 24 | t)).ToArray();
            if (newOptions.Length == 0 && newTints.Length == 0)
            {
                ItemUses.Refuse(session, grantOptions.Length + grantTints.Length == 0 ? ItemUses.FailedItemRequirementText : AlreadyText(itemId));
                return true;
            }
            if (!session.TakeItem(itemId, 1))
                return true;
            // One save for the whole pack (4419 adds 69 rows).
            Unlocks.Replace(session, Unlocks.ParlorKnowledge, have
                .Concat(newOptions.Select(o => OptionType << 24 | o))
                .Concat(newTints.Select(t => TintType << 24 | t)));
            if (newOptions.Length > 0)
                SendAddKnowledge(session, OptionType, newOptions);
            if (newTints.Length > 0)
                SendAddKnowledge(session, TintType, newTints);
            log.Info($"{session.CharacterName} used Parlor unlocker {itemId}: {newOptions.Length} options " +
                $"({string.Join(",", newOptions)}), {newTints.Length} tints.");
            return true;
        }

        /// <summary>
        /// CharacterCustomization 0x11C sub 2 AddKnowledgeResponse (reader FUN_14368a5d0): u8 sub 2, u32 n, n × u32 id,
        /// u32 type (3 options, 4 tints). Additive on the client; nothing may follow the type.
        /// </summary>
        private static void SendAddKnowledge(WorldSession session, uint type, IReadOnlyCollection<uint> ids)
        {
            var output = new List<byte>(9 + 4 * ids.Count);
            var w = new GamePacketWriter(output);
            w.Write(AddKnowledgeSub);
            w.Write((uint)ids.Count);
            foreach (uint id in ids)
                w.Write(id);
            w.Write(type);
            session.EnqueueMessage(ZoneMessageOpcode.CharacterCustomizationBase, output.ToArray());
        }

        /// <summary>
        /// Does the character know this CharacterCreateOptions row? Default-unlocked rows (and ids the table lacks) count
        /// as known; others only when unlocked (set A).
        /// </summary>
        public static bool KnowsOption(ulong characterId, uint optionId)
        {
            Ensure();
            if (!options.TryGetValue(optionId, out var row) || row.DefaultUnlocked)
                return true;
            return Unlocks.Values(characterId, Unlocks.ParlorKnowledge).Contains(OptionType << 24 | optionId);
        }

        /// <summary>
        /// Does the character know the tint with this alias hash (as PlayerSet carries it)? Every tint is default-unlocked
        /// except the 46 of <see cref="lockedTintByHash"/>; those only when unlocked (set B).
        /// </summary>
        public static bool KnowsTintHash(ulong characterId, uint aliasHash)
            => !lockedTintByHash.TryGetValue(aliasHash, out uint tintId)
                || Unlocks.Values(characterId, Unlocks.ParlorKnowledge).Contains(TintType << 24 | tintId);
    }
}
