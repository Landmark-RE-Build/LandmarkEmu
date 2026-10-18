using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Account entitlements (TASKS 6.59; trace/0695/agent-store-entitlements.md §3): what live gave accounts rather than
    /// characters earning it, decided by the user on 2026-10-17 as "everyone gets them for now (also as tester items),
    /// behind a server setting". Three packages, each granted once per character (<see cref="Unlocks.EntitlementGranted"/>)
    /// at world entry while WorldServer.json <c>FounderEntitlements</c> is on and the package is listed in
    /// <c>EntitlementPackages</c>; delivered per package as live (<c>EntitlementDelivery</c> auto: founder into the bag,
    /// yearzero and promo by mail; <see cref="DeliversToBag"/>), or all by mail / all into the bag.
    /// <list type="bullet">
    /// <item><b>founder</b> — the Founder's Packs (wiki "Founder's Packs"; Trailblazer = every tier's goodies): the three
    /// flags 435 Settler / 436 Explorer / 437 Trailblazer Flag and 441 Void Vault [P live item records]; the tier
    /// pickaxes 2568 / 2569 / 2570 ("An upgraded version of the Founder's Pickaxe", their own descriptions [P]) in place of
    /// the Founder's Pickaxe itself, item 434, which the live item list no longer has [P recipe 1874 names it, I swap];
    /// the outfits Tech Commander's Gear, Tech Sergeant's Gear, Noble's Regalia and Courtier's Regalia (server-built records,
    /// <see cref="Cosmetics"/>; Regalia meshes from their icons, tints [I]).
    /// The recipes live kept for founders to remake them [I mapping, P recipes]: 271-273 (the flags, Workshop 30), 1713-1715
    /// (the tier pickaxes, Stone Forge 48), 1844-1846 (Change Mod for each tier pickaxe, Stone Forge 48) — none is
    /// default-learned and no "Recipe:" item teaches any of them (gameData695 Recipes / CraftingStationRecipeMappings).</item>
    /// <item><b>yearzero</b> — mailed by live to accounts active at the alpha's first anniversary (wiki "Year Zero
    /// Trophy"): 2483 Year Zero Trophy [P] and one Year Zero Survivor's Outfit in one of four colours at random [W]
    /// (server-built, <see cref="Cosmetics.YearZeroBlue"/>..). The letter is the client's own: sender 568827 "Landmark
    /// Team", subject 568828 "Congratulations, You Survived Year Zero!", body 568829 [P text; no client code reads these
    /// ids, so live's server sent them].</item>
    /// <item><b>promo</b> — 2014 giveaways and event souvenirs: 406 Iron Portcullis (a giveaway-only rare, forum
    /// "About those rare items (e.g., Iron Portcullis)", P1138-P1147), 1392 Fan Faire 2013 Souvenir Flag, 1394 Fan Faire
    /// 2014 Souvenir Flag, 1395 Layers of Landmark ("awarded to attendees of the … Fan Faire", their descriptions) [P].</item>
    /// </list>
    /// The props of these items (e.g. Settler Flag → prop 387, Void Vault → 407, Year Zero Trophy → 2043, Iron Portcullis →
    /// 823) are palette props for everyone today (no "Recipe:" item grants them; item-sources-audit-695.md §3.2): they
    /// stay known only while their package is on (<see cref="PropAllowed"/>, used by
    /// <see cref="RecipeBook.KnowsProp"/>); an item in the bag places one either way ("Exceptions placed from the
    /// inventory: promotional / event items (Iron Portcullis, Year Zero Trophy …)", trace/systems/building.md).
    ///
    /// <para>Not granted (open leads in the report): Mastercraft Bracer, Ring of Bounty,
    /// Mega Pocket, the Founder's Pickaxe 434 and the title Founder — the client has their names (560066-560108) but
    /// neither the live item list nor the server has an item record for them.</para>
    /// <para>Live's "restore missing Founder's Pack items" is the Founder's Chest at every Spire
    /// (<see cref="Spire.FounderChestProp"/> 898 → <see cref="RestoreFounderItems"/>).</para>
    /// </summary>
    public static class Entitlements
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>One package: its id (the <see cref="Unlocks.EntitlementGranted"/> value) and settings key.</summary>
        public sealed record Package(uint Id, string Key, string Subject, string Body, uint[] Items, uint[] Recipes);

        public const uint FounderPackage = 1, YearZeroPackage = 2, PromoPackage = 3;

        // Item ids: live records [P] unless server-built (Cosmetics).
        public const uint SettlerFlag = 435, ExplorerFlag = 436, TrailblazerFlag = 437, VoidVault = 441,
            SettlersPickaxe = 2568, ExplorersPickaxe = 2569, TrailblazersPickaxe = 2570, YearZeroTrophy = 2483,
            IronPortcullis = 406, FanFaire2013Flag = 1392, FanFaire2014Flag = 1394, LayersOfLandmark = 1395;

        /// <summary>The Year Zero letter's texts (client 568827-568829) [P].</summary>
        private const uint LandmarkTeamText = 568827, YearZeroSubjectText = 568828, YearZeroBodyText = 568829;

        /// <summary>
        /// Most attachments put on one letter: the compose window's four slots (Mail.MaxAttachments). Whether the reading
        /// window shows more is not proven [I], so a package with more items goes out as several letters.
        /// </summary>
        private const int PerLetter = 4;

        /// <summary>The Year Zero Survivor's Outfit colours, one of which each character gets (wiki: four random colours).</summary>
        private static readonly uint[] YearZeroOutfits =
            { Cosmetics.YearZeroBlue, Cosmetics.YearZeroWhite, Cosmetics.YearZeroRed, Cosmetics.YearZeroBlack };

        public static readonly Package[] Packages =
        {
            new(FounderPackage, "founder", "Your Founder's Pack",
                "Thank you for being a Landmark founder! Your Founder's Pack items are attached. You can also craft the "
                + "Settler's, Explorer's and Trailblazer's Pickaxes (and Change Mods for them) at a Stone Forge, and the "
                + "founder flags at a Workshop.",
                new[] { SettlerFlag, ExplorerFlag, TrailblazerFlag, VoidVault, SettlersPickaxe, ExplorersPickaxe, TrailblazersPickaxe,
                    Cosmetics.TechCommandersGear, Cosmetics.TechSergeantsGear, Cosmetics.NoblesRegalia, Cosmetics.CourtiersRegalia },
                new uint[] { 271, 272, 273, 1713, 1714, 1715, 1844, 1845, 1846 }),
            // Items: the trophy; the outfit colour is picked per character (Grant).
            new(YearZeroPackage, "yearzero", null, null, new[] { YearZeroTrophy }, Array.Empty<uint>()),
            new(PromoPackage, "promo", "Landmark giveaways",
                "Some of the rare items Landmark gave away at events and in promotions are attached: enjoy!",
                new[] { IronPortcullis, FanFaire2013Flag, FanFaire2014Flag, LayersOfLandmark }, Array.Empty<uint>()),
        };

        private static WorldServerConfiguration Config =>
            Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config;

        /// <summary>The packages switched on (none while FounderEntitlements is off).</summary>
        public static IEnumerable<Package> Enabled()
        {
            var config = Config;
            if (config == null || !config.FounderEntitlements)
                return Array.Empty<Package>();
            var keys = (config.EntitlementPackages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => k.ToLowerInvariant()).ToHashSet();
            return Packages.Where(p => keys.Contains(p.Key) || keys.Contains("all"));
        }

        private static Dictionary<uint, uint> packageOfProp;

        /// <summary>Prop id → the package whose item places it (built once from the item records' PropId).</summary>
        private static Dictionary<uint, uint> PackageOfProp
        {
            get
            {
                if (packageOfProp != null)
                    return packageOfProp;
                var map = new Dictionary<uint, uint>();
                foreach (Package package in Packages)
                    foreach (uint item in package.Items)
                        if (Inventory.ItemDefinitionManager.Get(item) is { PropId: > 0 } definition)
                            map.TryAdd(definition.PropId, package.Id);
                return packageOfProp = map;
            }
        }

        /// <summary>
        /// May this prop be placed from the palette as far as entitlements go? Props of entitlement items only while
        /// their package is switched on; every other prop: yes.
        /// </summary>
        public static bool PropAllowed(uint propId)
            => !PackageOfProp.TryGetValue(propId, out uint package) || Enabled().Any(p => p.Id == package);

        /// <summary>World entry: grants each switched-on package the character hasn't had yet.</summary>
        public static void Enter(WorldSession session)
        {
            foreach (Package package in Enabled())
                if (!Unlocks.Has(session, Unlocks.EntitlementGranted, package.Id))
                    Grant(session, package);
        }

        /// <summary>Grants one package: recipes, then the items (bag or mail), and records it.</summary>
        private static void Grant(WorldSession session, Package package)
        {
            if (!Unlocks.Add(session, Unlocks.EntitlementGranted, package.Id))
                return;
            var items = package.Items.ToList();
            if (package.Id == YearZeroPackage)
                items.Add(YearZeroOutfits[Random.Shared.Next(YearZeroOutfits.Length)]);
            items = items.Where(i => Inventory.ItemDefinitionManager.Get(i) != null).ToList();
            int learned = RecipeBook.TeachRecipes(session, package.Recipes);

            var toMail = new List<uint>();
            bool bag = DeliversToBag(package, Config?.EntitlementDelivery);
            foreach (uint item in items)
            {
                if (bag && session.Inventory.SpaceFor(item) >= 1)
                    session.GiveItem(item, 1);
                else
                    toMail.Add(item);
            }
            if (toMail.Count > 0)
                MailItems(session, package, toMail);
            log.Info($"Entitlement '{package.Key}' granted to {session.CharacterName}: {items.Count} item(s) "
                + $"({items.Count - toMail.Count} to the bag, {toMail.Count} by mail), {learned} recipe(s) learned.");
        }

        /// <summary>
        /// The Founder's Chest's answers: live's own texts, which no client code reads (the 0.695 decompile has none of
        /// these ids), so live's server sent them [P texts; I which case each answered, from their wording]:
        /// 562156 "We are unable to retrieve any lost Founder's Pack items because you do not own a Founder's Pack";
        /// 562130 "… while you have a pending Build Site mail. Please retrieve your Build Site mail before restoring …";
        /// 562157 "… because you are not currently missing any. Please note that consumable items cannot be retrieved.";
        /// 562158 "Retrieving your missing Founder's Pack items. Please note that consumable items cannot be retrieved.".
        /// Not used: 567828 "unable to restore … at this time" (a service failure on live [I]) and 562124-562126 "Grant
        /// Settlers / Explorers / Trailblazers Pack Items If Missing" (look like GM tool labels per tier [I]; here every
        /// character has the Trailblazer tier, i.e. all of them).
        /// </summary>
        private const uint NoFounderPackText = 562156, PendingSiteMailText = 562130, NothingMissingText = 562157,
            RetrievingText = 562158;

        /// <summary>
        /// The Founder's Chest at the Spires (trace/0695/audit-wiki-gaps.md #11): the Founder's Chest at each hub gives
        /// back whatever is missing (28 Mar 2014) [P]; it replaces tools that can't be crafted, such as the Founder's
        /// Pickaxe, and other founder's pack items that were lost or deleted (wiki Spire) [W]. Gives back the founder
        /// package's items the
        /// character no longer holds anywhere, and re-teaches its recipes.
        /// <list type="bullet">
        /// <item>Only while the founder package is switched on (FounderEntitlements + EntitlementPackages); else 562156. A
        /// character that never got the package (it was switched on later) gets it now, as at world entry.</item>
        /// <item>Refused with 562130 while the character has lapsed-site contents still to retrieve
        /// (<see cref="SiteContents"/>: live's "pending Build Site mail", which could still hold the items).</item>
        /// <item>"Held" = in the bag, vault, equipment or overflow; placed in the world by this character; in a chest they
        /// placed or on their site, or in a picked-up chest in their bag (<see cref="Chests.HoldsFor"/>); or attached to a
        /// letter still in their mailbox. Only NO_TRADE (soulbound) items are given back — the pickaxe is soulbound and
        /// can't be traded (wiki Founder's Pickaxe), so a tradeable one could have been handed on and coming back would
        /// copy it [I]; consumables are never given back (live's own text says so; the package has none).</item>
        /// </list>
        /// Items go into the bag, the rest by mail as the package's letter.
        /// <para>The checks in memory run on the world thread; the mailbox check for the items not held otherwise is one
        /// read of the recipient's letters (<c>ISocialService.MailAttachmentKinds</c>) on a pool thread, and the rest
        /// finishes at the session's next flush. A click within <see cref="FounderChestCooldown"/> of the last one is
        /// ignored. Until 2026-10-18 each founder item ran its own synchronous mailbox scan on the tick, and spam-clicking
        /// the chest stalled the world (review 2026-10-18b #5).</para>
        /// </summary>
        public static void RestoreFounderItems(WorldSession session)
        {
            DateTime now = DateTime.UtcNow;
            if (founderChestUsed.TryGetValue(session.PlayerGuid, out DateTime last) && now - last < FounderChestCooldown)
            {
                log.Debug($"{session.CharacterName}: Founder's Chest clicked again within {FounderChestCooldown.TotalSeconds:0} s, ignored.");
                return;
            }
            founderChestUsed[session.PlayerGuid] = now;
            foreach (var (guid, at) in founderChestUsed)
                if (now - at >= FounderChestCooldown)
                    founderChestUsed.TryRemove(guid, out _);

            string Text(uint id, string fallback) => TextManager.Instance.GetTextForId(id) ?? fallback;
            Package package = Enabled().FirstOrDefault(p => p.Id == FounderPackage);
            if (package == null)
            {
                Chat.System(session, Text(NoFounderPackText, "We are unable to retrieve any lost Founder's Pack items because you do not own a Founder's Pack"));
                log.Info($"{session.CharacterName}: Founder's Chest, no founder package (FounderEntitlements / EntitlementPackages).");
                return;
            }
            if (Unlocks.Values(session.PlayerGuid, Unlocks.SiteContents).Count > 0)
            {
                Chat.System(session, Text(PendingSiteMailText, "We are unable to restore your Founder's Pack items while you have a pending Build Site mail. Please retrieve your Build Site mail before restoring your Founder's Pack items."));
                log.Info($"{session.CharacterName}: Founder's Chest refused, lapsed-site contents still to retrieve.");
                return;
            }
            if (!Unlocks.Has(session, Unlocks.EntitlementGranted, package.Id))
            {
                Chat.System(session, Text(RetrievingText, "Retrieving your missing Founder's Pack items. Please note that consumable items cannot be retrieved."));
                Grant(session, package);
                return;
            }

            List<uint> candidates = NotHeldOutsideMail(session, package);
            if (candidates.Count == 0)
            {
                FinishRestore(session, package, candidates);
                return;
            }
            ulong recipient = session.PlayerGuid;
            var job = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    return Social.SocialServices.Service.MailAttachmentKinds(recipient, candidates);
                }
                catch (Exception e)
                {
                    // Can't be checked: everything counts as held (no second copy of a soulbound item).
                    log.Warn($"{session.CharacterName}: Founder's Chest mailbox check failed ({e.Message}); nothing given back.");
                    return candidates.ToHashSet();
                }
            });
            session.EnqueueEvent(new LandmarkEmulator.Shared.Game.Events.TaskGenericEvent<HashSet<uint>>(job, mailed =>
            {
                if (session.Disconnected)
                    return;
                // Checked again: the bag, props and chests may have changed while the mailbox was read.
                var stillMissing = NotHeldOutsideMail(session, package).Where(i => !mailed.Contains(i)).ToList();
                FinishRestore(session, package, stillMissing);
            }));
        }

        /// <summary>A click on the Founder's Chest within this long of the last one is ignored (review 2026-10-18b #5).</summary>
        internal static readonly TimeSpan FounderChestCooldown = TimeSpan.FromSeconds(5);

        /// <summary>Per character: the last Founder's Chest click (expired entries are dropped at the next click).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, DateTime> founderChestUsed = new();

        /// <summary>
        /// The package's soulbound items this character holds nowhere the world knows of in memory: not in the bag,
        /// vault, equipment or overflow, not placed, not in one of their chests (world thread). Letters are checked apart.
        /// </summary>
        private static List<uint> NotHeldOutsideMail(WorldSession session, Package package)
        {
            var placed = Props.OwnedBy(session.PlayerGuid).Select(p => (p.PropId, p.PaidByItem)).ToList();
            var missing = new List<uint>();
            foreach (uint item in package.Items)
            {
                var definition = Inventory.ItemDefinitionManager.Get(item);
                if (definition == null)
                    continue;
                if (!definition.NoTrade)
                {
                    log.Debug($"Founder's Chest: item {item} is tradeable, not given back.");
                    continue;
                }
                bool held = session.Inventory.CountOf(item) > 0
                    || placed.Any(p => p.PaidByItem == item || definition.PropId != 0 && p.PropId == definition.PropId)
                    || Chests.HoldsFor(session, item);
                if (!held)
                    missing.Add(item);
            }
            return missing;
        }

        /// <summary>The Founder's Chest's answer (world thread): re-teaches the recipes and gives back what is missing.</summary>
        private static void FinishRestore(WorldSession session, Package package, List<uint> missing)
        {
            string Text(uint id, string fallback) => TextManager.Instance.GetTextForId(id) ?? fallback;
            int learned = RecipeBook.TeachRecipes(session, package.Recipes);
            if (missing.Count == 0)
            {
                Chat.System(session, Text(NothingMissingText, "We are unable to retrieve any Founder's Pack items because you are not currently missing any. Please note that consumable items cannot be retrieved."));
                log.Info($"{session.CharacterName}: Founder's Chest, nothing missing ({learned} recipe(s) re-learned).");
                return;
            }
            Chat.System(session, Text(RetrievingText, "Retrieving your missing Founder's Pack items. Please note that consumable items cannot be retrieved."));
            var toMail = new List<uint>();
            foreach (uint item in missing)
            {
                if (session.Inventory.SpaceFor(item) >= 1)
                    session.GiveItem(item, 1);
                else
                    toMail.Add(item);
            }
            if (toMail.Count > 0)
                MailItems(session, package, toMail);
            log.Info($"{session.CharacterName}: Founder's Chest gave back {missing.Count} item(s) ({string.Join(", ", missing)}; "
                + $"{toMail.Count} by mail), {learned} recipe(s) re-learned.");
        }

        /// <summary>
        /// Does this package go into the bag (else by mail)? Setting EntitlementDelivery: "auto" (default, also empty or
        /// unknown) = live's way per package — founder into the bag (founders find the pick on their character at
        /// login, wiki "Founder's Pickaxe") [W], yearzero by mail (wiki "Year Zero Trophy"; the client's own letter
        /// texts
        /// 568827-568829) [W/P], promo by mail [I: live's way not known]; "mail" / "bag" = every package that way; or
        /// "key=mail|bag" pairs, comma separated, where a package not named keeps its auto way.
        /// </summary>
        public static bool DeliversToBag(Package package, string setting)
        {
            bool auto = package.Id == FounderPackage;
            string value = (setting ?? "").Trim();
            if (value.Equals("bag", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value.Equals("mail", StringComparison.OrdinalIgnoreCase))
                return false;
            foreach (string pair in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && kv[0].Equals(package.Key, StringComparison.OrdinalIgnoreCase))
                {
                    if (kv[1].Equals("bag", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (kv[1].Equals("mail", StringComparison.OrdinalIgnoreCase))
                        return false;
                    log.Warn($"EntitlementDelivery: '{kv[1]}' for {package.Key} is not mail or bag; {(auto ? "bag" : "mail")} (live's) is used.");
                }
            }
            return auto;
        }

        /// <summary>The package's letter(s), at most <see cref="PerLetter"/> items each.</summary>
        private static void MailItems(WorldSession session, Package package, List<uint> items)
        {
            string Text(uint id, string fallback) => TextManager.Instance.GetTextForId(id) ?? fallback;
            string from = Text(LandmarkTeamText, "Landmark Team");
            string subject = package.Subject, body = package.Body;
            if (package.Id == YearZeroPackage)
            {
                subject = Text(YearZeroSubjectText, "Congratulations, You Survived Year Zero!");
                body = Text(YearZeroBodyText, "");
            }
            int letters = (items.Count + PerLetter - 1) / PerLetter;
            for (int i = 0; i < letters; i++)
            {
                var attached = items.Skip(i * PerLetter).Take(PerLetter).Select(item => (item, 1u)).ToList();
                string title = letters > 1 ? $"{subject} ({i + 1}/{letters})" : subject;
                Social.SocialServices.Service.SendSystemMail(session.PlayerGuid, from, title, body, 0, attached);
            }
        }
    }
}
