using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The twelve "Recipe: Barren …" items: how the non-elite Barren creature props (Bowman, Coldmage, Fireshot, Flameblade,
    /// Flamemage, Grunt, Icebrute, Lightbringer, Protector, Shadowshot, Stormmancer, Stormshot; props 2445-2459 without the
    /// four elites) are unlocked (OPEN-LEADS L44, user decision 2026-10-17 "Unlock via drops").
    /// <para><b>Evidence.</b></para>
    /// <list type="bullet">
    /// <item>[P] The developers: players start with a few basic characters and collect more while adventuring (Domino,
    /// 15 Mar 2016, forum 56205 #1); more creatures unlock through fighting, goals or Lumens at the Lumen Shop (21 Mar
    /// 2016, patchnotes.tsv P4224).</item>
    /// <item>[P] The wiki's "Innate Creature Props (List)" has no Barren creature, and all twelve Barren pages say
    /// "Innate: No" (trace/0695/wiki-content-audit.md §2.7, item-sources-audit-695.md §3.2).</item>
    /// <item>[P] The client's text (0.589 and 0.695) holds the items' names 574376-574387 "Recipe: Barren Bowman" …
    /// "Recipe: Barren Stormshot" and descriptions 574388-574399 "This recipe will teach you how to place the Barren … on
    /// your Build Site." (both alphabetical), but no item record of the live login list (Resources/PacketDumps/
    /// 44-CommandBase) uses them, and no 0.695 table (Recipes type 2, CraftingStationRecipeMappings, grantIds) names
    /// them. So the records are built here (<see cref="Inventory.ItemDefinitionBuilder"/>), with ids of ours.</item>
    /// <item>[I] That they dropped: unlocked by fighting (P4224) and the 15 Mar post; the Lumen Station sells the
    /// four Elite Barren recipes (3561-3564, recipes r2234-r2237) but has no row for these twelve.</item>
    /// </list>
    /// <para><b>The records</b> copy 3561 "Recipe: Elite Barren Captain" (<see cref="Template"/>): item type 45 (the grant is a
    /// PropDefs row), flags [8,16,0], stack 1, quality 4, category 274 (the four Elite Barren recipes' own), no ability
    /// (the bag's "Use" sends ActivateItem → <see cref="RecipeBook.Learn"/>), the same as every one of the 526 live prop
    /// recipe items (checked over the dump 2026-10-17: all 526 have flags [8,16,0] and quality 4). Changed: id, name,
    /// description, the grant (the prop taught) and the icon — 523 of the 526 live prop recipe items show their prop's own
    /// PropDefs IMAGESET_ID (3561 → 11065 = prop 2455's), so each one here shows its creature's (0.695 PropDefs.txt).</para>
    /// <para><b>Effect.</b> A prop some "Recipe:" item grants is no longer free to place from the palette
    /// (<see cref="RecipeBook.KnowsProp"/>), so the twelve stop being innate the moment these records exist; a bag item of
    /// the prop still places it without the recipe (P4462 / P4569), which keeps the Dungeon Builder's Starter Set (2 Barren
    /// Flameblades 3162 + 1 Barren Bowman 3156, <see cref="ItemUses.TryActivate"/>) and its goal Cry Havoc! (478: "Place the
    /// Barren Flameblade character from your Props tab" ×2, then defeat them) working for a new character. Characters
    /// who already know a prop are refused the item with 568756 "You've already learned this prop!" (no migration: the
    /// accounts are reset, user 2026-10-17).</para>
    /// <para><b>Drops.</b> Like the Elite Barren recipes, they join every random recipe roll
    /// (<see cref="RecipeBook.RandomUnknownRecipeItem"/>: creature kills, Outpost Elites, Lost Chests and Crystals,
    /// gathering, Reward Chests). With ~500 candidates in that pool one given recipe would hardly ever turn up, so killing
    /// a Barren also rolls for one of these twelve (<see cref="RollFor"/>, from Creatures.Kill): <see cref="KillChance"/>
    /// per Barren kill (the general per-kill recipe rate, Creatures.KillRecipeChance) and <see cref="EliteChance"/> for a
    /// Barren Outpost Elite (the Elite's own recipe roll) [I: the rates are ours, as all recipe rates are]. "Barren" is the
    /// creature's kind name, as the "Barren killed" goals count it (Achievements.Killed): the Outposts' "Spawner: Ruins:
    /// Barren Patroller / Melee / Ranged / Caster / BOSS" garrisons (props 2702-2706) and any non-story Barren. Story
    /// creatures (spawned by a player's own placed props) never drop loot (Creatures.Kill), so nobody farms their own site.
    /// Bandwidth: 12 extra definitions in the login item list (sent once per client), nothing per tick.</para>
    /// </summary>
    public static class BarrenRecipes
    {
        /// <summary>One "Recipe: Barren …" item: our id, the prop it teaches, the client's texts and the prop's icon.</summary>
        public sealed record Recipe(uint ItemId, string Name, uint PropId, uint PropItemId, uint NameId, uint DescriptionId, uint ImageSetId);

        /// <summary>Template: 3561 "Recipe: Elite Barren Captain" (grant prop 2455; see the class notes).</summary>
        public const uint Template = 3561;

        /// <summary>Our ids: <see cref="Inventory.ItemDefinitionBuilder.OwnIdBase"/> + 701..712, in the texts' order.</summary>
        private const uint Own = Inventory.ItemDefinitionBuilder.OwnIdBase + 700;

        /// <summary>
        /// Chance that a Barren kill also drops one of these twelve (one the killer doesn't know). 1 %, the same as the
        /// general recipe roll of a creature kill (Creatures.KillRecipeChance) [I].
        /// </summary>
        public const double KillChance = 0.01;

        /// <summary>
        /// The same for a Barren Outpost Elite ("Elite Barren BOSS"): 30 %, the rate of the Elite's own recipe roll (25
        /// May 2016, P5628: much more loot; Creatures.Kill) [I].
        /// </summary>
        public const double EliteChance = 0.3;

        /// <summary>
        /// The twelve, in the client's text order (names 574376-574387, descriptions 574388-574399). Prop ids and icons from
        /// the 0.695 PropDefs.txt (NAME_STRING_ID → the creature's name, IMAGESET_ID); the prop items 3156-3170 place them.
        /// </summary>
        public static readonly Recipe[] All =
        {
            new(Own + 1, "Recipe: Barren Bowman", 2445, 3156, 574376, 574388, 11008),
            new(Own + 2, "Recipe: Barren Coldmage", 2459, 3170, 574377, 574389, 11055),
            new(Own + 3, "Recipe: Barren Fireshot", 2446, 3157, 574378, 574390, 11009),
            new(Own + 4, "Recipe: Barren Flameblade", 2451, 3162, 574379, 574391, 11058),
            new(Own + 5, "Recipe: Barren Flamemage", 2456, 3167, 574380, 574392, 11051),
            new(Own + 6, "Recipe: Barren Grunt", 2450, 3161, 574381, 574393, 11057),
            new(Own + 7, "Recipe: Barren Icebrute", 2452, 3163, 574382, 574394, 11020),
            new(Own + 8, "Recipe: Barren Lightbringer", 2457, 3168, 574383, 574395, 11053),
            new(Own + 9, "Recipe: Barren Protector", 2453, 3164, 574384, 574396, 11064),
            new(Own + 10, "Recipe: Barren Shadowshot", 2447, 3158, 574385, 574397, 11012),
            new(Own + 11, "Recipe: Barren Stormmancer", 2458, 3169, 574386, 574398, 11052),
            new(Own + 12, "Recipe: Barren Stormshot", 2448, 3159, 574387, 574399, 11010),
        };

        /// <summary>Is the creature a Barren (its kind name, as the "Barren killed" goals read it)?</summary>
        public static bool IsBarren(string creatureName)
            => creatureName?.Contains("Barren", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>
        /// One of the twelve the player hasn't learned yet, at random; 0 when they know all twelve (or the records were not
        /// built). No daily limit: P4438's limit is for armour and weapon recipes only (Pacing).
        /// </summary>
        public static uint RandomUnknown(WorldSession session, Random random)
        {
            List<Recipe> unknown = All.Where(r => Inventory.ItemDefinitionManager.Get(r.ItemId) != null
                && !Unlocks.Has(session, Unlocks.PropTaught, r.PropId)).ToList();
            return unknown.Count == 0 ? 0 : unknown[random.Next(unknown.Count)].ItemId;
        }

        /// <summary>
        /// The Barren roll of a kill (non-story creatures only; the caller checks): for a Barren, at <see cref="KillChance"/>
        /// (<see cref="EliteChance"/> for an Outpost Elite), a recipe the killer doesn't know; else 0.
        /// </summary>
        public static uint RollFor(WorldSession killer, string creatureName, bool outpostElite, Random random)
        {
            if (killer == null || !IsBarren(creatureName))
                return 0;
            return random.NextDouble() < (outpostElite ? EliteChance : KillChance) ? RandomUnknown(killer, random) : 0;
        }
    }
}
