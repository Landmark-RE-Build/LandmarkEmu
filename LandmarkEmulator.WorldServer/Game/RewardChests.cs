using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Reward Chests (0.695, 29 Aug 2016; TASKS 3.11; trace/0695/spec-reward-chests.md). A builder places a chest item
    /// (4510 Common / 4514 Superb / 4515 Legendary, crafted at the Lumen Station for 130 / 260 / 780 Lumens; the retired
    /// 4511-4513 "[OLD]" tiers) on their build site like any prop item (P6412); visitors loot it while the site is in
    /// Play Mode. What the client proves and what is ours:
    ///
    /// | Rule | Source |
    /// |---|---|
    /// | Props 3003-3008 (PropDefs 0.695, PROP_RECIPE_ID 0: no storage window, not in the 50-chest cap) | [P] PropDefs, PropBase dump |
    /// | Uses 10 / 20 / 40 (Common / Superb / Legendary) | [P] item descriptions 577607 / 577632 / 577635 |
    /// | Old tiers 3004-3006: 10 uses | [I] their dump description is 577607 |
    /// | Loot only while the site is in Play Mode; the chest's owner never gets loot ("PlayerLoot.OwnerCantLoot" 577633) | [P] P6416, CodeStringMappings |
    /// | Each player has a lockout per chest: "Player looting cooldown" (EditableProperties 104, 15-120 min, default 30) | [P] P6413, table row 104 |
    /// | Count remaining (row 106 loot_chest_remaining_count, BLOCKS_USER_MODIFICATION 1, drawn as a PropertyTextLine) | [P] table, 0.695 FUN_142fb71f0 |
    /// | The whole group gets loot from one click | [P] P6503 (29 Sep) |
    /// | Links: event 577597 "When this chest is looted...", action 577598 "...Loot this chest" | [P] storytools-695 §4.15 |
    /// | Lock / Unlock Links and "PlayerLoot.ChestLocked" 577651 on clicks of a locked chest | [I, strong] 577651 + forum 57918 #7 |
    /// | A paid-out chest can't be picked up: PickUpReply error 0xE → 577600 | [P] 0.695 FUN_142fba9f0 case 6 |
    /// | At 0 the chest is consumed and the owner is mailed "Reward Chest Expiration!" (577638 / 577639) | [P] texts, P6502 |
    /// | Goal 1490 "No Good Deed..." objective 2209 "Thank You Tokens received from emptied Reward Chests" | [P] server achievement dump |
    ///
    /// None of the PlayerLoot / ClaimPlayerLoot keys is referenced by client code (0.695 decompile), so the server sends
    /// them through PacketShowSystemMessage (<see cref="SystemMessages.ShowKey"/>) [P route; I that live did the same].
    ///
    /// State, all server-side (no per-tick traffic): the remaining count is the prop setting
    /// <see cref="RemainingKey"/> (absent = never looted = full uses), and each player's lockout is the prop setting
    /// <c>loot_chest_lockout_&lt;character guid hex&gt;</c> holding the minute it ends (minutes since 2016-01-01; a
    /// float keeps whole minutes exactly until 2047). Both live in world_prop_setting with the chest, survive restarts
    /// and stowing (the prop guid is kept), and go away with the chest (<see cref="PropSettings.PropRemoved"/>), so no
    /// new table is needed and at most "uses" lockout rows exist per chest. Ours (inferred): live restarted nightly, and
    /// a lockout lost on restart would be an exploit (spec §5.6).
    /// </summary>
    public static class RewardChests
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly object sync = new();
        private static readonly Random random = new();

        /// <summary>PropDefs 3003 Common, 3004-3006 [OLD] Stonedelver's / Orefinder's / Gemseeker's, 3007 Superb, 3008 Legendary.</summary>
        public const uint CommonProp = 3003, StoneProp = 3004, MetalProp = 3005, GemProp = 3006, SuperbProp = 3007, LegendaryProp = 3008;

        /// <summary>EditableProperties 104 "Player looting cooldown" (577599; minutes, 15-120, default 30).</summary>
        public const string CooldownKey = "loot_chest_player_cooldown";
        /// <summary>EditableProperties 106 (no label text; BLOCKS_USER_MODIFICATION 1): the count remaining.</summary>
        public const string RemainingKey = "loot_chest_remaining_count";
        /// <summary>Ours: the prop-setting key prefix of one character's lockout on a chest (not a table row).</summary>
        private const string LockoutKeyPrefix = "loot_chest_lockout_";

        /// <summary>Link sentences (storytools-695 §4.15): "When this chest is looted..." and "...Loot this chest".</summary>
        public const uint LootedEvent = 577597, LootAction = 577598;

        /// <summary>PropBase PickUpReply error 0xE → Prop.PropPickupErrorPartialLootChest (577600), 0.695 FUN_142fba9f0 case 6.</summary>
        public const uint PartialLootChestError = 0xE;

        /// <summary>Server-sent CodeStringMappings keys (0.695; texts 577633, 577651, 577634).</summary>
        private const string OwnerCantLootKey = "PlayerLoot.OwnerCantLoot", ChestLockedKey = "PlayerLoot.ChestLocked",
            LockoutKey = "ClaimPlayerLoot.ChestsYouCantLoot";

        /// <summary>The owner's letter: sender (577637 "Terrogaunt" — [I] the developer who presented the feature, P6458;
        /// it sits right before the subject), subject 577638, body 577639 [P texts].</summary>
        private const uint MailFromText = 577637, MailSubjectText = 577638, MailBodyText = 577639;

        /// <summary>Goal 1490 "No Good Deed..." objective 2209 "Thank You Tokens received from emptied Reward Chests".</summary>
        private const uint ThanksObjective = 2209;

        /// <summary>
        /// Ours (inferred; spec U2): group members get loot from one click when on the same landscape within this distance
        /// of the chest — the treasure chests' share range (<see cref="LootChests"/>). Live's range is unknown.
        /// </summary>
        private const float GroupRange = 50f;

        /// <summary>
        /// Ours (inferred; spec U2): one charge per player who gets loot (P6414 how many loots are left), so a group of
        /// four uses four. False would charge one per loot event instead.
        /// </summary>
        private static readonly bool ChargePerRecipient = true;

        private static readonly DateTime LockoutEpoch = new(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly HashSet<uint> chestProps = new() { CommonProp, StoneProp, MetalProp, GemProp, SuperbProp, LegendaryProp };

        /// <summary>Is this prop definition a reward chest?</summary>
        public static bool IsRewardChestProp(uint propId) => chestProps.Contains(propId);

        /// <summary>Is this placed prop a reward chest?</summary>
        public static bool IsRewardChest(ulong guid) => Props.Find(guid) is Props.PlacedProp p && IsRewardChestProp(p.PropId);

        /// <summary>Uses a fresh chest has: 10 / 20 / 40 [P item descriptions]; the old tiers 10 [I].</summary>
        public static int UsesOf(uint propId) => propId switch { SuperbProp => 20, LegendaryProp => 40, _ => 10 };

        /// <summary>Loots left: the saved count, else the tier's full uses (never looted).</summary>
        public static int Remaining(ulong guid, uint propId)
            => PropSettings.TryGetSaved(guid, RemainingKey, out float v) ? Math.Max(0, (int)v) : UsesOf(propId);

        /// <summary>Remaining loots of a placed chest (0 for anything else); the settings window's count row.</summary>
        public static int Remaining(ulong guid) => Props.Find(guid) is Props.PlacedProp p && IsRewardChestProp(p.PropId) ? Remaining(guid, p.PropId) : 0;

        /// <summary>
        /// Has the chest given out items (fewer loots left than it started with)? Then its owner can't pick it up
        /// (PickUpReply error <see cref="PartialLootChestError"/>, P: 0.695 FUN_142fba9f0 case 6). Delete stays allowed:
        /// the client has no matching delete error [I].
        /// </summary>
        public static bool HasPaidOut(ulong guid, uint propId) => IsRewardChestProp(propId) && Remaining(guid, propId) < UsesOf(propId);

        /// <summary>The chest's "Player looting cooldown" (row 104), clamped to the table's 15-120 minutes.</summary>
        public static int CooldownMinutes(ulong guid) => Math.Clamp((int)PropSettings.Get(guid, CooldownKey), 15, 120);

        private static string LockoutKeyOf(ulong character) => LockoutKeyPrefix + character.ToString("X16");

        private static float NowMinutes() => (float)Math.Floor((DateTime.UtcNow - LockoutEpoch).TotalMinutes);

        /// <summary>Minutes left on the character's lockout for this chest (0: may loot).</summary>
        public static int LockoutLeft(ulong chest, ulong character)
            => PropSettings.TryGetSaved(chest, LockoutKeyOf(character), out float until) ? Math.Max(0, (int)(until - NowMinutes())) : 0;

        private static ulong? SiteOf(Props.PlacedProp chest)
            => Claims.ClaimAt(chest.Zone, PositionOf(chest));

        private static Vector3 PositionOf(Props.PlacedProp chest) => new(chest.Spawn.Position.X, chest.Spawn.Position.Y, chest.Spawn.Position.Z);

        /// <summary>
        /// Shows a server-sent loot line by its CodeStringMappings key (0.589, whose table lacks the keys, was given the
        /// text in chat).
        /// </summary>
        private static void Notice(WorldSession session, string key) => SystemMessages.ShowKey(session, key);

        // ---- clicks ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// A player clicked a reward chest (Command InteractRequest, before the site-access check: looting is what the
        /// chest is for, and Play Mode is the only stated rule — P6416; "No Touching" visitors are not refused, spec U7 [I]).
        /// Returns false for anything that isn't a placed reward chest.
        ///
        /// In Edit Mode nothing is looted (P6416); the line shown then is ours, the same as the story tools' other Play-
        /// Mode-only props (spec U4: live's reaction unknown). In Play Mode the click first runs the chest's own "When a
        /// Luminary clicks this object..." Links (every prop has it, P6432), then: a locked chest (Lock Link) refuses with
        /// "This chest is locked!" (577651) [I]; otherwise <see cref="Loot"/>.
        /// </summary>
        public static bool Click(WorldSession session, ulong guid)
        {
            if (Props.Find(guid) is not Props.PlacedProp chest || !IsRewardChestProp(chest.PropId) || !chest.Placed)
                return false;
            CreditPending(session);
            if (SiteOf(chest) is not ulong site || !Claims.IsPlaying(site))
            {
                Chat.System(session, "Nothing happens. (The build site is not in Play Mode.)");
                log.Info($"{session.CharacterName} clicked reward chest 0x{guid:X} outside Play Mode.");
                return true;
            }
            StoryTools.FireClickLinks(session, guid);
            if (StoryTools.IsLocked(guid))
            {
                Notice(session, ChestLockedKey);
                log.Info($"{session.CharacterName} clicked reward chest 0x{guid:X}: locked.");
                return true;
            }
            Loot(session, guid, () => StoryTools.ChestLooted(session, guid));
            return true;
        }

        // ---- looting --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Loots a chest for <paramref name="trigger"/> and their group (P6503), by a click or by the Link "...Loot this
        /// chest" (577598; a locked chest still loots from a Link: players' locked chests give loot as the player
        /// completes encounters, 57918 #7 [I]). Returns whether anyone got loot.
        ///
        /// 1. The site must be in Play Mode.
        /// 2. Recipients: the trigger plus their group members on the same landscape within <see cref="GroupRange"/> of
        ///    the chest [I range]; never the chest's owner (P6416: a triggering owner sees "Sorry! Owners cannot loot their
        ///    own chests!", owners among the group are skipped silently [I]); not the dead or players mid re-zone (ours:
        ///    anything sent during a re-zone can hang the client); not players under lockout (a locked-out trigger sees
        ///    "There are chests on this build site that you have a lockout timer for!" — [I] the only lockout text, U1).
        /// 3. One charge per recipient while charges last (<see cref="ChargePerRecipient"/>) [I]; each recipient gets the
        ///    tier's loot as their own loot bag (LootPinatas, toasts on pickup as for the treasure chests; spec U5 [I]) and
        ///    a lockout of the chest's cooldown.
        /// 4. <paramref name="looted"/> runs once per loot event ("When this chest is looted...", 577597), then an empty
        ///    chest is consumed (<see cref="Consume"/>) — after the Links, so a chain started by the last loot still runs.
        /// </summary>
        public static bool Loot(WorldSession trigger, ulong guid, Action looted)
        {
            if (trigger == null || Props.Find(guid) is not Props.PlacedProp chest || !IsRewardChestProp(chest.PropId) || !chest.Placed)
                return false;
            if (SiteOf(chest) is not ulong site || !Claims.IsPlaying(site))
                return false;
            if (Health.IsDead(trigger))
                return false;
            Vector3 at = PositionOf(chest);
            var candidates = new List<WorldSession> { trigger };
            candidates.AddRange(Groups.MembersOf(trigger).Where(m => m != trigger && !m.Disconnected && m.ZoneId == chest.Zone
                && Vector3.Distance(m.Position, at) <= GroupRange));
            if (trigger.PlayerGuid == chest.OwnerGuid)
                Notice(trigger, OwnerCantLootKey);

            var given = new List<(WorldSession Player, List<(uint Item, uint Count)> Loot)>();
            int left;
            lock (sync)
            {
                left = Remaining(guid, chest.PropId);
                if (left <= 0)
                    return false;
                var recipients = new List<WorldSession>();
                foreach (WorldSession p in candidates)
                {
                    if (p.PlayerGuid == chest.OwnerGuid || p.RezoneTarget != null || Health.IsDead(p))
                        continue;
                    if (LockoutLeft(guid, p.PlayerGuid) > 0)
                    {
                        if (p == trigger)
                            Notice(trigger, LockoutKey);
                        continue;
                    }
                    recipients.Add(p);
                }
                if (recipients.Count == 0)
                {
                    log.Info($"{trigger.CharacterName} tried reward chest 0x{guid:X}: nobody may loot it ({left} left).");
                    return false;
                }
                int charges = ChargePerRecipient ? Math.Min(recipients.Count, left) : 1;
                if (ChargePerRecipient)
                    recipients = recipients.Take(charges).ToList();
                left -= charges;
                PropSettings.Set(guid, RemainingKey, left);
                float until = NowMinutes() + CooldownMinutes(guid);
                foreach (WorldSession p in recipients)
                {
                    PropSettings.Set(guid, LockoutKeyOf(p.PlayerGuid), until);
                    given.Add((p, Roll(p, chest.PropId)));
                }
            }
            foreach (var (player, loot) in given)
                LootPinatas.SpawnBag(player, at + new Vector3(0f, 0.6f, 0f), loot);
            log.Info($"{trigger.CharacterName} looted reward chest {chest.PropId} 0x{guid:X}: {string.Join(", ", given.Select(g => g.Player.CharacterName))} "
                + $"got loot; {left} left, lockout {CooldownMinutes(guid)} min.");
            looted?.Invoke();
            if (left <= 0)
                Consume(guid);
            return true;
        }

        // ---- loot tables ----------------------------------------------------------------------------------------------

        /// <summary>One weighted choice of a resource and its amount (mean ± spread, one standard deviation).</summary>
        private readonly record struct Pick(uint Item, double Weight, int Mean, int Spread);

        // Resource item ids (the item dump, re/out/item_names.tsv).
        private const uint TreeFiber = 921, TreeSap = 922, PlainWood = 71, BurledWood = 72, StripedWood = 73, Stone = 46,
            Sand = 45, Dirt = 44, Ice = 76, Copper = 34, Tin = 35, Iron = 36, Silver = 37, Gold = 39, Marble = 47,
            Amaranthine = 48, Obsidian = 49, Alabaster = 50, Agate = 51, Emerald = 56, Sapphire = 57, Ruby = 58, Diamond = 59,
            Lumen = 1579;

        private static readonly uint[] essences = { 1587, 1588, 1589, 1590, 1591, 1592, 1593 };

        /// <summary>Common roll, wood (P: a player's 100 loots, 57918 #19): Striped 30 %, Burled 33 %, Plain 37 %, 197 ± 28.</summary>
        private static readonly Pick[] woods = { new(StripedWood, 30, 197, 28), new(BurledWood, 33, 197, 28), new(PlainWood, 37, 197, 28) };

        /// <summary>Common roll, bulk (P, 57918 #19): Sand 28 %, Dirt 32 %, Ice 40 %, about 1650 ± 220.</summary>
        private static readonly Pick[] bulk = { new(Sand, 28, 1650, 220), new(Dirt, 32, 1650, 220), new(Ice, 40, 1650, 220) };

        /// <summary>Common roll, one uncommon resource (P, 57918 #19; ± ~100).</summary>
        private static readonly Pick[] uncommon =
        {
            new(Copper, 23, 784, 100), new(Tin, 13, 787, 100), new(Iron, 10, 818, 100), new(Amaranthine, 17, 783, 100),
            new(Marble, 14, 795, 100), new(Agate, 14, 585, 100), new(Sapphire, 9, 577, 100)
        };

        /// <summary>
        /// The retired tiers' uncommon pick by their draft descriptions (577612 stone / 577613 metal / 577616 gem) [I].
        /// </summary>
        private static readonly Pick[] oldStones = { new(Marble, 1, 795, 100), new(Amaranthine, 1, 783, 100), new(Alabaster, 1, 700, 100), new(Obsidian, 1, 600, 100) };
        private static readonly Pick[] oldMetals = { new(Copper, 1, 784, 100), new(Tin, 1, 787, 100), new(Iron, 1, 818, 100), new(Silver, 1, 600, 100), new(Gold, 1, 500, 100) };
        private static readonly Pick[] oldGems = { new(Agate, 1, 585, 100), new(Sapphire, 1, 577, 100), new(Emerald, 1, 450, 80), new(Ruby, 1, 450, 80), new(Diamond, 1, 350, 80) };

        /// <summary>
        /// Superb / Legendary "rare stone, metal, and gems" (577632): one rare metal, one rare stone and one rare gem from
        /// the deep tiers (ItemUses tiers 3-4). Amounts are ours (spec U8: no measurements exist).
        /// </summary>
        private static readonly Pick[] rareMetals = { new(Silver, 60, 400, 80), new(Gold, 40, 300, 60) };
        private static readonly Pick[] rareStones = { new(Obsidian, 1, 400, 80) };
        private static readonly Pick[] rareGems = { new(Emerald, 40, 300, 60), new(Ruby, 40, 300, 60), new(Diamond, 20, 200, 50) };

        /// <summary>
        /// One recipient's loot for a chest tier:
        /// <list type="bullet">
        /// <item>Common (and every tier): Tree Fiber 164 ± 23, Tree Sap 124 ± 32, one wood, Stone 812 ± 116, one of Sand /
        /// Dirt / Ice, one uncommon — all measured by a player over 100 loots (57918 #19) [P]. The same player got 2
        /// recipes, 17 essences and 45 Lumens over those 100 loots: a recipe at 2 %, an essence at 17 %, and 3 Lumens at
        /// 15 % here (the Lumen split is ours).</item>
        /// <item>Old tiers 3004-3006: the uncommon pick from their own stone / metal / gem list [I].</item>
        /// <item>Superb (577632 "an abundance of exceptional resources, such as rare stone, metal, and gems in addition to
        /// common resources"): the Common roll plus one rare metal, stone and gem [P wording, I amounts].</item>
        /// <item>Legendary (577635 "outpost loot, such as lumens, essence, and the occasional recipe in addition to a ton of
        /// common and exceptional resources"): the Superb roll with doubled resource amounts, plus the Lost Crystal's
        /// extras (Outposts: recipe at 30 %, an essence 1-3, Adventure Essence 2-4, Lumens 2-5) [P wording, I amounts].</item>
        /// </list>
        /// Seasonal extras: Superb / Legendary add a rare hat unlocker (P6505, <see cref="RareDrops.Hat"/>) and the season's
        /// outfit drop (P6569, <see cref="RareDrops.ChestSeasonal"/>, off unless LANDMARK_SEASON is set).
        /// </summary>
        private static List<(uint Item, uint Count)> Roll(WorldSession player, uint propId)
        {
            double scale = propId == LegendaryProp ? 2.0 : 1.0;
            var loot = new Dictionary<uint, uint>();
            void Add(uint item, uint count)
            {
                if (item != 0 && count > 0)
                    loot[item] = loot.GetValueOrDefault(item) + count;
            }
            void AddPick(Pick p) => Add(p.Item, Amount(p.Mean * scale, p.Spread * scale));
            AddPick(new Pick(TreeFiber, 1, 164, 23));
            AddPick(new Pick(TreeSap, 1, 124, 32));
            AddPick(Choose(woods));
            AddPick(new Pick(Stone, 1, 812, 116));
            AddPick(Choose(bulk));
            AddPick(Choose(propId switch { StoneProp => oldStones, MetalProp => oldMetals, GemProp => oldGems, _ => uncommon }));
            if (propId is SuperbProp or LegendaryProp)
            {
                AddPick(Choose(rareMetals));
                AddPick(Choose(rareStones));
                AddPick(Choose(rareGems));
            }
            if (random.NextDouble() < (propId == LegendaryProp ? 0.3 : 0.02))
                Add(RecipeBook.RandomUnknownRecipeItem(player, random), 1);
            if (propId is SuperbProp or LegendaryProp)
            {
                Add(RareDrops.Hat(random), 1);
                // The season's drop (Colonist's Flight Suits / Holiday Sweaters in winter, P6569; RareDrops.Season).
                Add(RareDrops.ChestSeasonal(random), 1);
            }
            if (propId == LegendaryProp)
            {
                Add(essences[random.Next(essences.Length)], (uint)random.Next(1, 4));
                Add(Gathering.AdventureEssence, (uint)random.Next(2, 5));
                Add(Lumen, (uint)random.Next(2, 6));
            }
            else
            {
                if (random.NextDouble() < 0.17)
                    Add(essences[random.Next(essences.Length)], 1);
                if (random.NextDouble() < 0.15)
                    Add(Lumen, 3);
            }
            return loot.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        private static Pick Choose(Pick[] picks)
        {
            double r = random.NextDouble() * picks.Sum(p => p.Weight);
            foreach (Pick p in picks)
                if ((r -= p.Weight) < 0)
                    return p;
            return picks[^1];
        }

        /// <summary>A normal draw of mean ± spread (Box-Muller), at least 1.</summary>
        private static uint Amount(double mean, double spread)
        {
            double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
            double n = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return (uint)Math.Max(1, Math.Round(mean + n * spread));
        }

        // ---- consumption ----------------------------------------------------------------------------------------------

        /// <summary>
        /// The last loot was taken (P6502): the chest disappears for everyone (no refund; its Links, settings and lockouts
        /// go with it) and its owner is mailed "Reward Chest Expiration!" (577638 / 577639, plain strings resolved here
        /// with fallbacks, as SiteUpkeep's letters; whether live used a text-id mail kind is U12).
        ///
        /// Attachment: one Common roll as the "bonus resources" of the August mail (P6415) [I; U9]. The September reward,
        /// very likely a "Thank You Token" (577640 / 577641): sent with it (Inventory.ItemDefinitionBuilder.ThankYouToken, our
        /// id; L24 for live's). The owner's goal objective 2209 is credited now when online, else at their next site entry or
        /// chest click (<see cref="CreditPending"/>) [I: whether live counted the mail or the token taken is U10].
        /// </summary>
        private static void Consume(ulong guid)
        {
            if (Props.Find(guid) is not Props.PlacedProp chest)
                return;
            ulong owner = chest.OwnerGuid;
            Props.Despawn(guid);
            static string Txt(uint id, string fallback) => Shared.GameTable.Text.TextManager.Instance.GetTextForId(id) is string s && s.Length > 0 ? s : fallback;
            WorldSession online = PlayerManager.FindByGuid(owner);
            List<(uint Item, uint Count)> bonus = online != null ? Roll(online, CommonProp) : RollWithoutRecipe();
            bonus.Add((Inventory.ItemDefinitionBuilder.ThankYouToken, 1));
            Social.SocialServices.Service.SendSystemMail(owner, Txt(MailFromText, "Terrogaunt"), Txt(MailSubjectText, "Reward Chest Expiration!"),
                Txt(MailBodyText, "Greetings! We're writing to let you know that a Reward Chest on your build site has been fully consumed. "
                    + "Thanks for giving back to the community!"), 0, bonus);
            log.Info($"Reward chest {chest.PropId} 0x{guid:X} is empty and consumed; owner 0x{owner:X} mailed {bonus.Count} resource stack(s) "
                + "(incl. a Thank You Token).");
            if (online != null)
                Achievements.Progress(online, ThanksObjective);
            else
            {
                // Read the offline owner's unlocks first, so the cache is complete before the row is added.
                Unlocks.ValuesOfAnyone(owner, Unlocks.RewardChestThanks);
                Unlocks.Add(owner, Unlocks.RewardChestThanks, (uint)(guid ^ (guid >> 32)));
            }
        }

        /// <summary>A Common roll for an offline owner (the recipe roll needs the owner's known recipes: left out).</summary>
        private static List<(uint Item, uint Count)> RollWithoutRecipe()
        {
            var loot = new List<(uint, uint)>
            {
                (TreeFiber, Amount(164, 23)), (TreeSap, Amount(124, 32)), (Stone, Amount(812, 116))
            };
            foreach (Pick p in new[] { Choose(woods), Choose(bulk), Choose(uncommon) })
                loot.Add((p.Item, Amount(p.Mean, p.Spread)));
            return loot;
        }

        /// <summary>Credits goal objective 2209 for chests emptied while the owner was offline (one per chest).</summary>
        private static void CreditPending(WorldSession session)
        {
            List<uint> pending = Unlocks.Values(session.PlayerGuid, Unlocks.RewardChestThanks);
            if (pending.Count == 0)
                return;
            Unlocks.Clear(session, Unlocks.RewardChestThanks);
            Achievements.Progress(session, ThanksObjective, (uint)pending.Count);
            log.Info($"{session.CharacterName}: {pending.Count} emptied reward chest(s) credited to goal objective {ThanksObjective}.");
        }

        // ---- site entry -----------------------------------------------------------------------------------------------

        /// <summary>The last site each character was told about, and when (re-entries without leaving are not re-told).</summary>
        private static readonly Dictionary<ulong, (ulong Claim, DateTime At)> told = new();

        /// <summary>Ours: a report of the same site again within this time is the client re-entering it (Map Depths), not a visit.</summary>
        private static readonly TimeSpan RetellAfter = TimeSpan.FromMinutes(5);

        /// <summary>
        /// A 0.695 player entered a build site (ClaimsPacketPlayerEnteredClaim, <see cref="Claims"/>.Entered): if any reward
        /// chest there has a running lockout for them, "There are chests on this build site that you have a lockout timer
        /// for!" (ClaimPlayerLoot.ChestsYouCantLoot 577634) once per entry. [I] The key is site-level ("ClaimPlayerLoot", not
        /// "PlayerLoot") and server-only; when live sent it is U1 (trace/0695/leads-L33-L35-L36.md, L33). Zero packets in
        /// the common case. Also credits goal objective 2209 for chests emptied while the player was offline.
        /// </summary>
        public static void Entered(WorldSession session, ulong claim)
        {
            CreditPending(session);
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                if (told.TryGetValue(session.PlayerGuid, out var last) && last.Claim == claim && now - last.At < RetellAfter)
                    return;
                told[session.PlayerGuid] = (claim, now);
            }
            bool lockedOut = Props.PlacedAll(chestProps)
                .Any(c => c.Zone == session.ZoneId && LockoutLeft(c.Guid, session.PlayerGuid) > 0 && Claims.ClaimAt(c.Zone, c.Position) == claim);
            if (!lockedOut)
                return;
            Notice(session, LockoutKey);
            log.Info($"{session.CharacterName} entered site {claim} with a reward chest lockout running.");
        }
    }
}
