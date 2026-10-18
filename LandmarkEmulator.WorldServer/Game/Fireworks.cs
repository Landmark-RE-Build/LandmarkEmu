using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Fireworks (30 Jun 2016, patch notes P5913-P5915; TASKS 3.9 / 4.12; trace/0695/spec-fireworks.md). This class holds
    /// the item side, spec §5.2 D-F:
    /// <list type="bullet">
    /// <item><b>D, packs.</b> The six Lumen Station packs 4444-4449 (category 290 "Fireworks", no ability, so the bag's
    /// "Use" sends Inventory 0x2D sub 0x15 ActivateItem → <see cref="ItemUses.TryActivate"/> → <see cref="TryOpenPack"/>)
    /// unpack into the consumables their descriptions name (spec §2.1, text 577414-577426). Their grantIds 1124-1129
    /// point at live reward sets with no client table, so the descriptions are the only source.</item>
    /// <item><b>E.3, Pack of Unstable Fireworks</b> 4443: a cast item (ability 1237, type 4, record offset 215), used
    /// through <see cref="Abilities.HandleCast"/> → <see cref="Consumables.TryUse"/> → <see cref="OpenUnstablePack"/>:
    /// "Leaves behind 50 'safe' fireworks when used, of a somewhat random variety" (577412).</item>
    /// <item><b>E.1 / E.2, the 22 consumables.</b> Their definitions are not in the live login list (L24 / L25). They are
    /// built by <see cref="Inventory.ItemDefinitionBuilder"/> from the 4443 record (<see cref="Kinds"/> holds the data),
    /// each with an ability id of ours, so a use is a cast like the Cozy Campfire's (3894, ability 1098, same record
    /// shape), answered by <see cref="Launch"/>: the matching emitter composite plays at the player.</item>
    /// <item><b>F, drops.</b> The emitter items 4455-4476 (dropped at random like other props, P5915) through
    /// <see cref="RandomEmitterItem"/> (Gathering.RollDrops); the "Recipe:" items 4477-4498, bought for Lumens, stay out
    /// of random recipe drops (<see cref="IsEmitterRecipeItem"/>, RecipeBook.RandomUnknownRecipeItem).</item>
    /// </list>
    /// The emitter props themselves (A-B: one-shot launch, story-tool action 577506) are PropEffects.Launch and StoryTools.
    /// </summary>
    public static class Fireworks
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// One firework consumable (spec §2.2). <paramref name="ItemId"/> is a candidate only (unproven, lead L24): the 22-id
        /// gap 4421-4442 between 4420 and the Unstable pack 4443, in the client's text order (577366-577408), which is
        /// mostly but not strictly id order. <paramref name="AbilityId"/> is ours (1215-1236, unused in the live dump, just
        /// below 4443's 1237); live's ids are unknown (L25). <paramref name="Composite"/> is the emitter composite of the
        /// same name (ActorCompositeEffectDefinitions.xml 12836-12857 = InteractableCompositeEffectSet 129-150; matched by
        /// name, inferred: the client has no other firework composite). Names, descriptions and icons are the client's.
        /// </summary>
        public sealed record Kind(uint ItemId, string Name, uint NameId, uint DescriptionId, uint ImageSetId, uint AbilityId, uint Composite);

        /// <summary>First of the candidate item ids (L24) and of our ability ids.</summary>
        private const uint FirstItem = 4421, FirstAbility = 1215;

        private static Kind K(int index, string name, uint nameId, uint descriptionId, uint icon, uint composite)
            => new(FirstItem + (uint)index, name, nameId, descriptionId, icon, FirstAbility + (uint)index, composite);

        /// <summary>
        /// The 22 consumables in the spec §2.2 order (= candidate id order). Text quirks, proven from text_695: for Purple,
        /// Green and Blue, Purple and Pink the description string comes before the name (577370 / 577375 / 577377), and the
        /// Gold and White description is the later 577438 (577375 right after its name is Green and Blue's). Red and Orange:
        /// ImageSets has two "Standard01_Redorange" rows (21696 / 21697); the first is used. Blue Triple Fan uses the
        /// Triplefan_Green art 21703, as its emitter does (21725).
        /// </summary>
        public static readonly Kind[] Kinds =
        {
            K(0, "Gold Fireworks", 577366, 577367, 21692, 12836),                 // props_Fireworks_Standard01_Gold
            K(1, "Green Fireworks", 577368, 577369, 21693, 12837),
            K(2, "Purple Fireworks", 577371, 577370, 21694, 12838),               // description before name
            K(3, "Red Fireworks", 577372, 577373, 21695, 12839),
            K(4, "Gold and White Fireworks", 577374, 577438, 21698, 12840),       // description is the later 577438
            K(5, "Green and Blue Fireworks", 577376, 577375, 21699, 12841),       // description before name
            K(6, "Purple and Pink Fireworks", 577378, 577377, 21700, 12842),      // description before name
            K(7, "Red and Orange Fireworks", 577379, 577380, 21696, 12843),       // 21696 or 21697
            K(8, "Gold Curved Streamers", 577381, 577382, 21689, 12855),          // streamer composites 12854-12857
            K(9, "Green Curved Streamers", 577383, 577384, 21690, 12856),
            K(10, "Red Curved Streamers", 577385, 577386, 21691, 12857),
            K(11, "Gold Straight Streamers", 577387, 577388, 21701, 12854),
            K(12, "Blue Triple Fan Fireworks", 577389, 577390, 21703, 12844),     // Triplefan_Green art
            K(13, "Gold Triple Fan Fireworks", 577391, 577392, 21702, 12845),
            K(14, "Purple Triple Fan Fireworks", 577393, 577394, 21704, 12846),
            K(15, "Red Triple Fan Fireworks", 577395, 577396, 21705, 12847),
            K(16, "Gold Twinkle Fireworks", 577397, 577398, 21706, 12848),
            K(17, "Green Twinkle Fireworks", 577399, 577400, 21707, 12849),
            K(18, "Purple Twinkle Fireworks", 577401, 577402, 21708, 12850),
            K(19, "Red Twinkle Fireworks", 577403, 577404, 21709, 12851),
            K(20, "Gold Whirly Fireworks", 577405, 577406, 21710, 12852),
            K(21, "Purple Whirly Fireworks", 577407, 577408, 21711, 12853),
        };

        private static readonly Dictionary<uint, Kind> byAbility = Kinds.ToDictionary(k => k.AbilityId);

        /// <summary>The consumable whose (our) ability this is, or null.</summary>
        public static Kind ByAbility(uint ability) => byAbility.GetValueOrDefault(ability);

        /// <summary>Pack of Unstable Fireworks (4443, text 577410 / 577412, icon 21687) and its live ability (1237, type 4).</summary>
        public const uint UnstablePack = 4443, UnstableAbility = 1237;

        /// <summary>How many consumables the Unstable pack leaves (577412: "Leaves behind 50 'safe' fireworks").</summary>
        private const int UnstableCount = 50;

        /// <summary>
        /// Packs 4444-4449 → contents, read from their descriptions (spec §2.1; text 577414 / 577418 / 577420 / 577422 /
        /// 577424 / 577426). Indexes are into <see cref="Kinds"/>.
        /// </summary>
        private static readonly Dictionary<uint, (int Kind, uint Count)[]> packs = new()
        {
            [4444] = new[] { (0, 5u), (1, 5u), (2, 5u), (3, 5u) },        // Pack of Fireworks: Gold, Green, Purple, Red
            [4445] = new[] { (4, 5u), (5, 5u), (6, 5u), (7, 5u) },        // Fancy: Gold and White, Green and Blue, Purple and Pink, Red and Orange
            [4446] = new[] { (8, 5u), (9, 5u), (10, 5u), (11, 5u) },      // Streamers: Gold / Green / Red Curved, Gold Straight
            [4447] = new[] { (12, 5u), (13, 5u), (14, 5u), (15, 5u) },    // Triple Fan: Blue, Gold, Purple, Red
            [4448] = new[] { (16, 5u), (17, 5u), (18, 5u), (19, 5u) },    // Twinkle: Gold, Green, Purple, Red
            [4449] = new[] { (20, 10u), (21, 10u) },                      // Whirly: 10 Gold, 10 Purple
        };

        /// <summary>Is this one of the six packs 4444-4449 used from the bag?</summary>
        public static bool IsPack(uint itemId) => packs.ContainsKey(itemId);

        /// <summary>
        /// How far a firework is seen, metres (ours, as StoryTools' emitters: the bursts reach 50-80 m up and are visible
        /// from afar).
        /// </summary>
        private const float ViewRange = 300f;

        /// <summary>
        /// Minimum time between two launches by one player, any kind (ours): held-key or hotbar spam can't flood the
        /// viewers. Also sent as each consumable ability's own cooldown (<see cref="CooldownOf"/>), so the client greys
        /// the hotbar slot; a launch of another kind inside the window is refused (CastFail returns its cooldown).
        /// </summary>
        public const float CooldownSeconds = 2f;

        /// <summary>When each player may launch again.</summary>
        private static readonly ConditionalWeakTable<WorldSession, StrongBox<DateTime>> nextLaunch = new();

        /// <summary>The client-side cooldown of a firework consumable's ability (seconds), 0 for any other ability.</summary>
        public static float CooldownOf(uint ability) => byAbility.ContainsKey(ability) ? CooldownSeconds : 0f;

        /// <summary>Players on the landscape within <see cref="ViewRange"/> of a point who are not re-zoning (anything
        /// sent during a re-zone can hang their client: project rule "cave teleport hang").</summary>
        private static List<WorldSession> Viewers(uint zone, Vector3 at)
            => PlayerManager.InZone(zone).Where(p => p.RezoneTarget == null && Vector3.Distance(p.Position, at) <= ViewRange).ToList();

        /// <summary>
        /// A firework consumable is used (spec §5.2 E.2): its composite plays at the player's position as a
        /// TargetLocation effect (<see cref="HitEffects.PlayAt"/>, PlayerUpdate PlayCompositeEffect), so the rocket goes up
        /// where it was lit while the player moves on. The burst composites launch at t = 0 and burst at t = 1 s; the
        /// streamer ones play at once (ActorCompositeEffectDefinitions.xml 14183-14412). Whether live attached it to the
        /// character instead (PlayOn) is unknown (L25); P5914 says only "over your head". One body goes to every viewer.
        /// False (nothing used up) while the player's <see cref="CooldownSeconds"/> runs.
        /// </summary>
        public static bool Launch(WorldSession session, Kind kind)
        {
            DateTime now = DateTime.UtcNow;
            StrongBox<DateTime> next = nextLaunch.GetValue(session, _ => new StrongBox<DateTime>(DateTime.MinValue));
            lock (next)
            {
                if (now < next.Value)
                    return false;
                next.Value = now + TimeSpan.FromSeconds(CooldownSeconds);
            }
            List<WorldSession> viewers = Viewers(session.ZoneId, session.Position);
            if (!viewers.Contains(session))
                viewers.Add(session);
            HitEffects.PlayAt(session.Position, kind.Composite, viewers);
            log.Debug($"{session.CharacterName} launched {kind.Name} (composite {kind.Composite}) for {viewers.Count} viewer(s).");
            return true;
        }

        /// <summary>
        /// Uses a pack 4444-4449 from the bag (ItemUses → ActivateItem): if all its stacks fit
        /// (<see cref="Inventory.Inventory.CanReceive"/>, counting part-full stacks and free slots; the pack's own slot is
        /// not counted as freed), one pack is taken and its fireworks given. Without room the pack stays and a chat line
        /// says so, as for the Builder's Bags (live's own text for this is unknown). False when the item is no pack.
        /// </summary>
        public static bool TryOpenPack(WorldSession session, CharacterItemModel pack)
        {
            if (!packs.TryGetValue(pack.ItemDefinitionId, out var contents))
                return false;
            var give = contents.Select(c => (Kinds[c.Kind].ItemId, c.Count)).ToList();
            if (!session.Inventory.CanReceive(give, new Dictionary<ulong, uint>()))
            {
                Chat.System(session, "You need more room in your inventory to open this pack.");
                return true;
            }
            if (!session.TakeItem(pack.ItemDefinitionId, 1))
                return true;
            foreach ((uint item, uint count) in give)
                session.GiveItem(item, count);
            Chat.System(session, "You unpack " + Describe(give) + ".");
            log.Info($"{session.CharacterName} opened fireworks pack {pack.ItemDefinitionId}.");
            return true;
        }

        private static readonly Random random = new();

        /// <summary>
        /// Pack of Unstable Fireworks (ability 1237; spec §5.2 E.3): 50 consumables, each kind picked at random from the
        /// 22 (577412 "of a somewhat random variety"; the real mix was a live reward set, unknown). The roll is made first
        /// and must fit the bag whole (up to 22 new stacks), else the use is refused (CastFail) and nothing is taken. On
        /// success the pack is taken here (the caller must not take another). Live's own flourish, if any, is unknown
        /// (L25), so none is played.
        /// </summary>
        public static bool OpenUnstablePack(WorldSession session, CharacterItemModel pack)
        {
            var rolled = new Dictionary<uint, uint>();
            lock (random)
                for (int i = 0; i < UnstableCount; i++)
                {
                    uint item = Kinds[random.Next(Kinds.Length)].ItemId;
                    rolled[item] = rolled.GetValueOrDefault(item) + 1;
                }
            var give = rolled.Select(r => (r.Key, r.Value)).OrderBy(r => r.Key).ToList();
            if (!session.Inventory.CanReceive(give, new Dictionary<ulong, uint>()))
            {
                Chat.System(session, "You need more room in your inventory to open this pack.");
                return false;
            }
            if (!session.TakeItem(pack.ItemDefinitionId, 1))
                return false;
            foreach ((uint item, uint count) in give)
                session.GiveItem(item, count);
            Chat.System(session, "The unstable pack leaves behind " + Describe(give) + ".");
            log.Info($"{session.CharacterName} opened a Pack of Unstable Fireworks ({give.Count} kinds).");
            return true;
        }

        private static string Describe(IEnumerable<(uint Item, uint Count)> items)
            => string.Join(", ", items.Select(i => $"{i.Count} {NameOf(i.Item)}"));

        private static string NameOf(uint item)
            => Kinds.FirstOrDefault(k => k.ItemId == item) is { } k
                ? TextManager.Instance.GetTextForId(k.NameId) ?? k.Name
                : $"item {item}";

        /// <summary>
        /// Emitter items 4455-4476 (place props 2949-2970; spec §2.3 / §3.1). "Rare" kinds (260-Lumen recipes: Gold and
        /// White … Red and Orange, the four Triple Fans, both Whirlies, per each prop's 145-PropBase description) drop
        /// half as often as "uncommon" ones (130 Lumens). The weights are ours.
        /// </summary>
        public const uint FirstEmitterItem = 4455, LastEmitterItem = 4476;

        /// <summary>The emitter recipe items 4477-4498 (Lumen Station recipes 5339-5360).</summary>
        public const uint FirstEmitterRecipeItem = 4477, LastEmitterRecipeItem = 4498;

        /// <summary>Indexes k (item 4455 + k) of the rare emitters (spec §3.1 table).</summary>
        private static readonly HashSet<int> rareEmitters = new() { 4, 5, 6, 7, 12, 13, 14, 15, 20, 21 };

        /// <summary>A random emitter item for a rare prop drop: uncommon kinds weigh 2, rare ones 1.</summary>
        public static uint RandomEmitterItem(Random rng)
        {
            const int count = (int)(LastEmitterItem - FirstEmitterItem + 1);
            int total = Enumerable.Range(0, count).Sum(k => rareEmitters.Contains(k) ? 1 : 2);
            int pick = rng.Next(total);
            for (int k = 0; k < count; k++)
            {
                pick -= rareEmitters.Contains(k) ? 1 : 2;
                if (pick < 0)
                    return FirstEmitterItem + (uint)k;
            }
            return FirstEmitterItem;
        }

        /// <summary>
        /// Is this a "Recipe: Fireworks Emitter (…)" item 4477-4498? Those are crafted for Lumens at the Lumen Station
        /// (recipes 5339-5360) and are kept out of random recipe drops (spec §5.2 F).
        /// </summary>
        public static bool IsEmitterRecipeItem(uint itemId) => itemId is >= FirstEmitterRecipeItem and <= LastEmitterRecipeItem;
    }
}
