using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Event / limited cosmetics and the late titles the live login list (Resources/PacketDumps/44-CommandBase) lacks
    /// (TASKS 4.16 / 4.17 and the §4 "Outfits" line; trace/0695/cosmetics-695.md). Definitions are built by
    /// <see cref="Inventory.ItemDefinitionBuilder"/> from a template record with the fields below patched in. [P] = proven
    /// from the client's own data, [I] = inferred (see the trace file for the evidence).
    ///
    /// What a worn outfit needs on the client (cosmetics-695.md §1): an item record with equip slot 1 (chest), the model
    /// path "Char_Biped_[RACE][GENDER]_Entities_&lt;mesh&gt;_Chest.adr" (the client expands [RACE][GENDER] to HumanMale /
    /// HumanFemale / HumanLargeMale / HumanLargeFemale) and a tint alias, which must be one of the ArtTints rows
    /// (Resources\ArtTints.txt in the game's install folder) that the mesh's tint type lists (ArtSourceActorTintTypes →
    /// ArtTintTypeTints). Every mesh used here ships in the 0.695 packs for all four body types, and every alias is in
    /// its type's list [P]. Which alias gives which colour is NOT proven: the colour data for an alias was not found
    /// (searched: all 8.2 GB of 0.695 packs and the loose files for the alias names, the .adr / .dma files; L-cos-1).
    /// The picks below follow the item order of each family and the alias names the dump proves on other meshes of the
    /// same tint type; check each in game against its icon (cosmetics-695.md §6).
    ///
    /// Ids: none of these items is in any capture, so all ids are ours (<see cref="Inventory.ItemDefinitionBuilder.OwnIdBase"/>
    /// + 201.. titles, + 301.. outfits). If a real id turns up it replaces ours here.
    /// </summary>
    public static class Cosmetics
    {
        private const uint Own = Inventory.ItemDefinitionBuilder.OwnIdBase;

        /// <summary>
        /// One outfit. <paramref name="Mesh"/> is the part between "Entities_" and "_Chest.adr". <paramref name="Tint"/> is
        /// the ArtTints alias. <paramref name="Proof"/> says how mesh / tint / icon were matched.
        /// </summary>
        public sealed record Outfit(uint Id, string Name, uint NameId, uint DescriptionId, uint ImageSetId, string Mesh, string Tint,
            string Proof)
        {
            /// <summary>The item record's model path (the dump's form, e.g. 2521's Colony_Light_000).</summary>
            public string ModelFile => $"Char_Biped_[RACE][GENDER]_Entities_{Mesh}_Chest.adr";
        }

        // Skeletal Bodysuits (Halloween; 28 Sep 2016: skeleton bodysuits can be found too, P6506).
        public const uint SkeletalWhite = Own + 301, SkeletalPink = Own + 302, SkeletalOrange = Own + 303, SkeletalCyan = Own + 304,
            SkeletalGreen = Own + 305, SkeletalRed = Own + 306;

        // Holiday Sweaters: the three of the 2015 holiday gift box (wiki "Limited Edition Outfits"), Outpost drops from
        // 6 Dec 2016 (P6567); the 2016 Green / Blue were event / stream giveaways (P6570, notes 577745 / 577746).
        public const uint SweaterGreenTan = Own + 311, SweaterRedBrown = Own + 312, SweaterBrownGreen = Own + 313,
            SweaterGreen2016 = Own + 314, SweaterBlue2016 = Own + 315;

        // Colonist's Flight Suits: Silver (2015, text 564149) and the three festive colours of 6 Dec 2016 (P6566, P6569).
        public const uint FlightSuitSilver = Own + 321, FlightSuitMerryRed = Own + 322, FlightSuitForestGreen = Own + 323,
            FlightSuitFestiveBlue = Own + 324;

        // Year Zero Survivor's Outfits (closed-beta anniversary grant, wiki "Year Zero Survivor's Outfit").
        public const uint YearZeroBlue = Own + 331, YearZeroWhite = Own + 332, YearZeroRed = Own + 333, YearZeroBlack = Own + 334;

        // Founder's Pack outfits (wiki "Limited Edition Outfits": Explorer / Trailblazer packs).
        public const uint TechSergeantsGear = Own + 341, TechSergeantsGearOrange = Own + 342, TechCommandersGear = Own + 343,
            NoblesRegalia = Own + 344, CourtiersRegalia = Own + 345;

        /// <summary>
        /// The outfits. Names / descriptions / icons are the client's (text_695, Images/ImageSets.txt) [P]. Mesh per family
        /// [I, strong]: the family's icon shows that mesh and no dump item uses it (cosmetics-695.md §2). Tints [I] as noted.
        /// </summary>
        public static readonly Outfit[] Outfits =
        {
            // Skeleton_Light_000 (tint type 574: Skeleton_Light_00..06, all unused by the dump). Icons
            // wearable_Light_Costume_Skeleton_000_<colour> 2295 / 10735-10739 [P]. 00 = the mesh's default alias and the
            // oldest suit (White, text 565425); the rest in text order 572805-572809 [I]; Skeleton_Light_06 unused.
            new(SkeletalWhite, "Skeletal Bodysuit (White)", 565425, 565426, 2295, "Skeleton_Light_000", "Skeleton_Light_00", "tint = mesh default [I]"),
            new(SkeletalPink, "Skeletal Bodysuit (Pink)", 572805, 565426, 10738, "Skeleton_Light_000", "Skeleton_Light_01", "tint by text order [I]"),
            new(SkeletalOrange, "Skeletal Bodysuit (Orange)", 572806, 565426, 10737, "Skeleton_Light_000", "Skeleton_Light_02", "tint by text order [I]"),
            new(SkeletalCyan, "Skeletal Bodysuit (Cyan)", 572807, 565426, 10735, "Skeleton_Light_000", "Skeleton_Light_03", "tint by text order [I]"),
            new(SkeletalGreen, "Skeletal Bodysuit (Green)", 572808, 565426, 10736, "Skeleton_Light_000", "Skeleton_Light_04", "tint by text order [I]"),
            new(SkeletalRed, "Skeletal Bodysuit (Red)", 572809, 565426, 10739, "Skeleton_Light_000", "Skeleton_Light_05", "tint by text order [I]"),

            // Holiday_Light_001 (tint type 585: only Holiday_Light_00 / 02 / 03; id 711 "_01" is absent from ArtTints).
            // Icons wearable_Light_Holiday_000_GreenTan 9649 / RedBrown 9650 / BrownGreen 9648 [P]; description 566191.
            // 00 = mesh default = the first sweater (566189); 02 / 03 in text order [I].
            new(SweaterGreenTan, "Holiday Sweater (Green & Tan)", 566189, 566191, 9649, "Holiday_Light_001", "Holiday_Light_00", "tint = mesh default [I]"),
            new(SweaterRedBrown, "Holiday Sweater (Red & Brown)", 566207, 566191, 9650, "Holiday_Light_001", "Holiday_Light_02", "tint by text order [I]"),
            new(SweaterBrownGreen, "Holiday Sweater (Brown & Green)", 566208, 566191, 9648, "Holiday_Light_001", "Holiday_Light_03", "tint by text order [I]"),
            // The 2016 colours: the final client has no further Holiday tint and no new sweater icon, so live must have
            // reused existing ones; which is unknown (L-cos-1). Placeholders: Green → the green 00 and its icon; Blue →
            // 03 and the BrownGreen icon. The description is the old sweaters' (577745 / 577746 are developer notes).
            new(SweaterGreen2016, "Holiday Sweater (Green)", 577741, 566191, 9649, "Holiday_Light_001", "GreenGreen00", "tint: all green, worn on the test copy 2026-10-17 (ArtTints colours, agent-leads2-b.md L103); icon placeholder [I]"),
            new(SweaterBlue2016, "Holiday Sweater (Blue)", 577742, 566191, 9648, "Holiday_Light_001", "Colony_Light_09", "tint: navy blue, worn on the test copy 2026-10-17 (ArtTints colours, agent-leads2-b.md L103); icon placeholder [I]"),

            // Colony_Heavy_001 (tint type 276, shared with Battle Armor / Patrol Armor, whose dump items name the aliases:
            // 02 Green, 03 Red, 04 Blue, 05 Grey [P on those meshes]). The flight-suit icons (1905
            // wearable_Heavy_Colony_000_ProxySilver, 10415 wearable_Heavy_Colony_001_Pink) show this one-piece suit, and its
            // .adr default alias is Colony_Heavy_05 (grey) = Silver [I]. The festive suits have no icon of their own in
            // 0.695: the Silver icon is used. Descriptions 564150 / 577738 ("… worn by festive space colonists").
            // Worn on the test copy with every alias (dev weartint, 2026-10-17; OPEN-LEADS L103): on this mesh 00 black,
            // 01 dark teal, 02 green, 03 navy, 04 black / white, 05 silver, 06 black, 07 dark grey, 08 steel blue, 09 black /
            // dark red, 10 orange, 11 pink (= icon 10415 Colony_001_Pink), 12 blue, 13 deep red, 14 bronze — the Battle Armor
            // names do not carry over to this mesh, so the festive suits use the aliases that look their colour.
            new(FlightSuitSilver, "Colonist's Flight Suit (Silver)", 564149, 564150, 1905, "Colony_Heavy_001", "Colony_Heavy_05", "tint = mesh default (Grey) [I]"),
            new(FlightSuitMerryRed, "Colonist's Flight Suit (Merry Red)", 577737, 577738, 1905, "Colony_Heavy_001", "Colony_Heavy_13", "worn on the test copy 2026-10-17: deep red on this mesh (03 shows navy) [P runtime]"),
            new(FlightSuitForestGreen, "Colonist's Flight Suit (Forest Green)", 577739, 577738, 1905, "Colony_Heavy_001", "Colony_Heavy_02", "alias named Green on Battle Armor 2514; worn: green [P runtime]"),
            new(FlightSuitFestiveBlue, "Colonist's Flight Suit (Festive Blue)", 577740, 577738, 1905, "Colony_Heavy_001", "Colony_Heavy_12", "worn on the test copy 2026-10-17: blue on this mesh (04 shows black / white) [P runtime]"),

            // Anniversary_Light_000 (tint type 601: Anniversary_00..03, unused by the dump; 00 = mesh default). Icons
            // wearable_Light_Costume_Anniversary_000_<colour> 10118-10121 [P]; description 568789. Tints in text order
            // 568788 / 568790-568792 [I].
            new(YearZeroBlue, "Year Zero Survivor's Outfit (Blue)", 568788, 568789, 10119, "Anniversary_Light_000", "Anniversary_00", "tint by text order [I]"),
            new(YearZeroWhite, "Year Zero Survivor's Outfit (White)", 568790, 568789, 10121, "Anniversary_Light_000", "Anniversary_01", "tint by text order [I]"),
            new(YearZeroRed, "Year Zero Survivor's Outfit (Red)", 568791, 568789, 10120, "Anniversary_Light_000", "Anniversary_02", "tint by text order [I]"),
            new(YearZeroBlack, "Year Zero Survivor's Outfit (Black)", 568792, 568789, 10118, "Anniversary_Light_000", "Anniversary_03", "tint by text order [I]"),

            // Founder_000 / Founder_001 (tint type 47: Founder_00..09; 05-07 are the dump's PlanetSide Commander's Gear
            // 1385-1387 on Founder_000). Icons 749 wearable_Medium_Founder_000, 750 wearable_Medium_Founder_001, 2162
            // wearable_Medium_Founder_001_OrangeBlack [P]; 564996 "black pants and shirt with orange accents" matches the
            // OrangeBlack icon. Tints = each mesh's .adr default (Founder_00 / Founder_01) [I]; the Orange variant's alias
            // is unknown (Founder_08 used as a placeholder, unused by the dump).
            new(TechCommandersGear, "Tech Commander's Gear", 560078, 560092, 749, "Founder_000", "Founder_00", "tint = mesh default [I]"),
            new(TechSergeantsGear, "Tech Sergeant's Gear", 560076, 560090, 750, "Founder_001", "Founder_01", "tint = mesh default [I]"),
            new(TechSergeantsGearOrange, "Tech Sergeant's Gear (Orange)", 564995, 564996, 2162, "Founder_001", "Founder_08", "placeholder tint [I]"),

            // The Regalia (OPEN-LEADS L149; trace/0695/agent-assets.md §1). Meshes Trailblazer_000 / TrailBlazer_001 (both
            // spellings are the files' own), all four bodies in the 0.695 packs (ArtSourceActors 25 / 28 / 694 / 719 and
            // 244 / 247 / 695 / 718), used by no item in the live dump [P]. Icons 754 wearable_Medium_TrailBlazer_000 / 755
            // wearable_Medium_TrailBlazer_001 (Images/icons_wearables/*_512.dds, rendered): 000 = fantasy armour with a
            // long blue cloak = Noble's (fantasy armour with an animated cloak, wiki "Noble's Regalia"), 001 = white
            // shirt, red vest, tan gloves and trousers, no cloak = Courtier's (wiki "Courtier's Regalia", word for word)
            // [I, strong]. Same tier pattern as the Tech gear: _000 = the Trailblazer outfit, _001 = the Explorer one; the
            // pre-modular full-body actors agree (Models 391 / 393 TrailBlazer_01, 414 / 415 Courtier_01, both with the
            // Wearables_Medium_TrailBlazer textures). Names 560077 / 560075 and descriptions 560091 "The Noble's Regalia is
            // an exclusive outfit worn by fearless Trailblazers." / 560089 "... the most audacious of Explorers." [P text].
            // Tint type 43 (TrailBlazer_00..05); each mesh's .adr default on the human bodies (Trailblazer_000 →
            // TrailBlazer_01, TrailBlazer_001 → TrailBlazer_00) [I].
            new(NoblesRegalia, "Noble's Regalia", 560077, 560091, 754, "Trailblazer_000", "TrailBlazer_01", "mesh by icon + wiki [I, strong]; tint = mesh default [I]"),
            new(CourtiersRegalia, "Courtier's Regalia", 560075, 560089, 755, "TrailBlazer_001", "TrailBlazer_00", "mesh by icon + wiki [I, strong]; tint = mesh default [I]"),
        };

        /// <summary>
        /// One title item ("Title: X", category 229). The title itself (its text and prefix / suffix type) is found by
        /// <see cref="Titles"/> from the item name like every dump title. <paramref name="GrantId"/>: the dump's 30 title
        /// items carry grantIds 31-60 in id order (4519 Champion of the People = 60) [P]; the later titles' grant ids are
        /// unknown, 61-66 are a continuation [I]. <see cref="Titles"/> keys titles by item id, so the grant is only
        /// written into the record.
        /// </summary>
        public sealed record TitleItem(uint Id, string Name, uint NameId, uint GrantId, string Source);

        public const uint TitleLumerianLens = Own + 201, TitleHeroOfLandmark = Own + 202, TitleLandmarkExplorer = Own + 203,
            TitleHolidayHero = Own + 204, TitleCreatorOfWinterWonderlands = Own + 205, TitleIcyArchitect = Own + 206;

        /// <summary>
        /// The six late title items (text_695 577697, 577727-577734; title texts 577698 / 577731 / 577059 / 577730 / 577732 /
        /// 577735) [P]. Sources from the developer notes in the text table [P wording, I mapping]: 577743 "giveaway title for
        /// sunset players", 577744 "Title for builders on winter island 2016"; "Landmark Explorer" is the name of the
        /// Twitch channel that ran the Dec 2016 giveaways (P6570). None was a drop: dev / admin grant only.
        /// </summary>
        public static readonly TitleItem[] Titles =
        {
            new(TitleLumerianLens, "Title: The Lumerian Lens", 577697, 61, "giveaway (577743?) [I]"),
            new(TitleHeroOfLandmark, "Title: Hero of Landmark", 577727, 62, "giveaway [I]"),
            new(TitleLandmarkExplorer, "Title: Landmark Explorer", 577728, 63, "Twitch giveaways (P6570) [I]"),
            new(TitleHolidayHero, "Title: Holiday Hero", 577729, 64, "winter event [I]"),
            new(TitleCreatorOfWinterWonderlands, "Title: Creator of Winter Wonderlands", 577733, 65, "Winter Holiday Island builders (577744) [I]"),
            new(TitleIcyArchitect, "Title: Icy Architect", 577734, 66, "Winter Holiday Island builders (577744) [I]"),
        };

        /// <summary>Halloween 2016 Outpost drops besides the hats (P6506; stopped 6 Dec 2016, P6565). All six colours [I].</summary>
        public static readonly uint[] HalloweenOutfits = { SkeletalWhite, SkeletalPink, SkeletalOrange, SkeletalCyan, SkeletalGreen, SkeletalRed };

        /// <summary>Winter 2016 Outpost drops from 6 Dec: three festive flight suits (P6566) and the three older sweaters (P6567).</summary>
        public static readonly uint[] WinterOutpostOutfits =
            { FlightSuitMerryRed, FlightSuitForestGreen, FlightSuitFestiveBlue, SweaterGreenTan, SweaterRedBrown, SweaterBrownGreen };

        /// <summary>Winter 2016 drops of the higher-end Reward Chests: the festive flight suits (P6569).</summary>
        public static readonly uint[] WinterChestOutfits = { FlightSuitMerryRed, FlightSuitForestGreen, FlightSuitFestiveBlue };

        public static Outfit Find(uint id) => Outfits.FirstOrDefault(o => o.Id == id);

        public static IEnumerable<uint> AllIds => Outfits.Select(o => o.Id).Concat(Titles.Select(t => t.Id));
    }
}
