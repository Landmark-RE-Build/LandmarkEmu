using LandmarkEmulator.Shared.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Inventory
{
    /// <summary>
    /// Item definitions the live server had but the definitions dump (Resources/PacketDumps/44-CommandBase, a live login
    /// list from 2016) doesn't hold: Build Site Expansions, items added after the capture... (TASKS 4.8). Each is made from
    /// the decompressed record of the closest dump item (its template), with fixed-size fields changed in place — id,
    /// flags, name / description texts, icon, max stack, the prop it places, quality, the first category and the first
    /// ability's id, the grant — so the record keeps the template's exact layout (re/items_decode.py); the model path and
    /// tint alias strings can be replaced too (the bytes after them move, nothing else changes). The record is wrapped like
    /// the dump's {u16 compressed size, u16 size, LZ4 block} (a literal-only block) and added to the login list and the
    /// 0x4A answers.
    /// </summary>
    public static class ItemDefinitionBuilder
    {
        /// <summary>What to change on a copy of a template item.</summary>
        public sealed record Spec(uint Id, uint Template, string Note)
        {
            public uint? NameId, DescriptionId, ImageSetId, MaxStack, PropId, Quality, Category, AbilityId;
            public bool? NoTrade;

            /// <summary>
            /// The record's grant (client definition +0x238: a title id, the prop / recipe a "Recipe:" item teaches…).
            /// </summary>
            public uint? GrantId;

            /// <summary>
            /// The model path and tint alias (u32-length strings; the record is re-laid out around them), e.g.
            /// "Char_Biped_[RACE][GENDER]_Entities_Holiday_Light_001_Chest.adr" / "Holiday_Light_00" for an outfit.
            /// </summary>
            public string ModelFile, TintAlias;
        }

        /// <summary>Offsets of the patchable fields in one decompressed record (walked like ItemDefinitionManager.Parse).</summary>
        private sealed class Offsets
        {
            public int MaxStack, U35, Quality, FirstCategory = -1, FirstAbility = -1;

            /// <summary>The model string (always at 39), the tint alias string, the grant u32 (-1 when the record is short).</summary>
            public int Model = ModelOffset, Tint, Grant = -1;
        }

        /// <summary>Id, 3 flag bytes, name, description, icon, 4 × u32, equipment slot: the model string follows.</summary>
        private const int ModelOffset = 7 + 4 * 3 + 4 * 4 + 4;

        private static Offsets Walk(byte[] r)
        {
            int p = 0;
            void U32(int n = 1) => p += 4 * n;
            void Str() => p += 4 + BitConverter.ToInt32(r, p);
            var o = new Offsets();
            p = 7 + 4 * 3;              // id, 3 flag bytes, name, description, icon
            U32(4);
            U32();                      // equipment slot
            Str(); Str();               // model, ?
            U32(5);
            U32(2);                     // min stack, ?
            o.MaxStack = p; U32();
            o.Tint = p;
            Str();                      // tint alias
            U32(10);
            Str(); Str();
            o.U35 = p; U32(5);
            o.Quality = p; U32();
            U32(6);
            uint categories = BitConverter.ToUInt32(r, p); U32();
            if (categories > 0)
                o.FirstCategory = p;
            U32((int)categories);
            U32(6);
            uint abilities = BitConverter.ToUInt32(r, p); U32();
            if (abilities > 0)
                o.FirstAbility = p;
            // The tail, as ItemDefinitionManager.Parse / the client's FUN_142c176d0 + FUN_142c19850 read it: each ability
            // {u32 id, u32 type, string input, string category, u32, u8}; three strings (secondary model, ?, secondary
            // tint); u32; the u59 u32 list (+0x1d0); then the grant u32 (+0x238).
            try
            {
                for (uint i = 0; i < abilities; i++)
                {
                    U32(2); Str(); Str(); U32(); p += 1;
                }
                Str(); Str(); Str();
                U32();
                uint u59 = BitConverter.ToUInt32(r, p); U32();
                U32((int)u59);
                if (p + 4 <= r.Length)
                    o.Grant = p;
            }
            catch (ArgumentOutOfRangeException)
            {
                o.Grant = -1;
            }
            return o;
        }

        /// <summary>Replaces the u32-length string at <paramref name="at"/> with <paramref name="value"/> (ASCII).</summary>
        private static byte[] Splice(byte[] record, int at, string value)
        {
            int oldLength = 4 + BitConverter.ToInt32(record, at);
            byte[] text = System.Text.Encoding.ASCII.GetBytes(value);
            var result = new byte[record.Length - oldLength + 4 + text.Length];
            record.AsSpan(0, at).CopyTo(result);
            BitConverter.TryWriteBytes(result.AsSpan(at), text.Length);
            text.CopyTo(result, at + 4);
            record.AsSpan(at + oldLength).CopyTo(result.AsSpan(at + 4 + text.Length));
            return result;
        }

        /// <summary>Builds a definition chunk (as in the login list) from a template's chunk; null when it can't.</summary>
        public static byte[] Build(Spec spec, byte[] templateChunk)
        {
            ushort compressed = BitConverter.ToUInt16(templateChunk, 0);
            ushort size = BitConverter.ToUInt16(templateChunk, 2);
            byte[] record = Lz4Block.Decode(templateChunk.AsSpan(4, compressed), size);
            Offsets o = Walk(record);
            void Put(int offset, uint value) => BitConverter.TryWriteBytes(record.AsSpan(offset), value);
            Put(0, spec.Id);
            if (spec.NoTrade is bool noTrade)
                record[4] = (byte)(noTrade ? record[4] | 0x80 : record[4] & ~0x80);
            if (spec.NameId is uint name) Put(7, name);
            if (spec.DescriptionId is uint description) Put(11, description);
            if (spec.ImageSetId is uint icon) Put(15, icon);
            if (spec.MaxStack is uint stack) Put(o.MaxStack, stack);
            if (spec.PropId is uint prop) Put(o.U35 + 12, prop);
            if (spec.Quality is uint quality) Put(o.Quality, quality);
            if (spec.Category is uint category)
            {
                if (o.FirstCategory < 0)
                    return null;
                Put(o.FirstCategory, category);
            }
            if (spec.AbilityId is uint ability)
            {
                if (o.FirstAbility < 0)
                    return null;
                Put(o.FirstAbility, ability);
            }
            if (spec.GrantId is uint grant)
            {
                if (o.Grant < 0)
                    return null;
                Put(o.Grant, grant);
            }
            // Strings last, the later one first, so the fixed offsets above stay valid.
            if (spec.TintAlias is string tint)
                record = Splice(record, o.Tint, tint);
            if (spec.ModelFile is string model)
                record = Splice(record, o.Model, model);
            if (record.Length > ushort.MaxValue)
                return null;
            byte[] block = Lz4Block.EncodeLiterals(record);
            var chunk = new byte[4 + block.Length];
            BitConverter.TryWriteBytes(chunk.AsSpan(0), (ushort)block.Length);
            BitConverter.TryWriteBytes(chunk.AsSpan(2), (ushort)record.Length);
            block.CopyTo(chunk, 4);
            return chunk;
        }

        /// <summary>
        /// Ids for items whose live id isn't known: ours, far above every dump id (largest 4524) so a later find of the
        /// real id can't collide. Recorded with each spec.
        /// </summary>
        public const uint OwnIdBase = 900000;

        /// <summary>Build Site Expansion (TASKS 3.5, L86): ours, id unknown.</summary>
        public const uint BuildSiteExpansion = OwnIdBase + 1;

        /// <summary>
        /// Thank You Token (TASKS 3.11 / 4.7): name 577640 "Thank You Token", description 577641 "Thanks for being awesome!"
        /// [P texts]; goal objective 2209 "Thank You Tokens received from emptied Reward Chests" [P]. No live item id or icon
        /// is known (OPEN-LEADS L24): id ours, the Lumen record 1579 as template (a plain stackable), icon image set 1643
        /// icon_currency_sc (a coin) [I].
        /// </summary>
        public const uint ThankYouToken = OwnIdBase + 401;

        /// <summary>
        /// The trial key (29 Aug 2016, TASKS 3.12; trace/0695/agent-membership.md §3). Live's key was a code ("LandmarkTrial",
        /// entered on landmarkthegame.com) in an in-game letter (client texts 577594 "Invite your friends!" / 577595); the
        /// 0.695 client has no key item (not in the item dump, items_summary.tsv, or any text). Ours: an item in that letter.
        /// <see cref="TrialKey"/> is the sender's copy (tradeable: it can be mailed); mailing it turns it into
        /// <see cref="TrialKeyMailed"/> (NO_TRADE), so a key travels by mail once; using the mailed copy gives the
        /// recipient's account trial membership (<see cref="Membership"/>). Ids ours.
        /// </summary>
        public const uint TrialKey = OwnIdBase + 501, TrialKeyMailed = OwnIdBase + 502;

        /// <summary>
        /// Template of the trial keys: 3711 Inventory Expansion (slot 0, no ability, used from the bag → ActivateItem →
        /// ItemUses.TryActivate), as the figurines and Parlor unlockers.
        /// </summary>
        public const uint TrialKeyTemplate = 3711;

        /// <summary>
        /// A trial key: name 577594 "Invite your friends!" (the subject of live's key letter) [P text, I use], description
        /// 557342 "Become a Premium Member!" [P text, I use: no client text describes the key], icon image set 10783
        /// Giftbox02 (icon_props_Theme_Seasonal_Winter_Giftbox_Giftbox02) [I: a gift for a friend], one per stack.
        /// </summary>
        private static Spec TrialKeyItem(uint id, bool noTrade, string note)
            => new(id, TrialKeyTemplate, note)
            {
                NameId = 577594, DescriptionId = 557342, ImageSetId = 10783, MaxStack = 1, NoTrade = noTrade
            };

        /// <summary>
        /// Cosmetic pet figurines (28 Jul 2016 update, TASKS 3.10, trace/0695/spec-pets.md §2): ours, ids unknown (L24).
        /// None is in the live login list (gameData695/items_summary.tsv); the gaps 4499-4507 in the dump's id order are
        /// only candidates (spec §2 "Item ids are not known"), so the figurines get their own block OwnIdBase + 101..107
        /// (in the client's text order 577507..577538) until a real id turns up. <see cref="Pets"/> maps them to their pets.
        /// </summary>
        public const uint ShadowWolfFigurine = OwnIdBase + 101, StagFigurine = OwnIdBase + 102, CockatriceFigurine = OwnIdBase + 103,
            BunnyFigurine = OwnIdBase + 104, BeetleFigurine = OwnIdBase + 105, JackalopeFigurine = OwnIdBase + 106,
            LopEarBunnyFigurine = OwnIdBase + 107;

        /// <summary>
        /// Template of the figurines: 3711 Inventory Expansion (slot 0, no ability, used from the bag), the same template as
        /// the Build Site Expansion. The spec's closest item, 4129 Repair Bot Beacon (Gadget, ability (1173, type 4)), was
        /// not used: an item with a type-4 ability is cast (AbilityCastAttempt, MODLOG 2026-10-11), and a cast of an
        /// ability that Consumables doesn't list falls to Abilities.HandleCast's generic tool path, which takes one of a
        /// slot-0 item (IsConsumable) — the "unlimited uses" figurine would be used up. Without an ability the client's
        /// right-click (ItemContainerBase.as:303-316 → Lua ClientInventory.ActivateItem → FUN_143457220) sends Inventory
        /// 0x2D sub 0x15 ActivateItem {u64 guid} instead, answered by ItemUses.TryActivate → Pets.Summon. Spec §3.1: both
        /// paths work on 0.695 and live's ability id for pets is unknown (L27), so no invented ability id is needed.
        /// </summary>
        public const uint FigurineTemplate = 3711;

        /// <summary>Item category 291 "Pet" (text 577509; in the 0.695 category dump, used by no login-list item).</summary>
        public const uint PetCategory = 291;

        /// <summary>
        /// Item category 289 "Parlor Option" (text 577338; in the 0.695 category dump 39-ReferenceDataItemCategoryDefinitions,
        /// used by no login-list item) for the Crimson Parlor unlockers.
        /// </summary>
        public const uint ParlorOptionCategory = 289;

        /// <summary>
        /// A Crimson Parlor unlocker (TASKS 4.9 / 4.10, trace/0695/leads-L39.md). The ids are live's own: the 0.695 client
        /// names them as UNLOCKING_ITEM_ID in CharacterCreateOptions.txt (4419 on the 24 "[Unlock] Fantasy …" rows, 4521 /
        /// 4522 / 4523 on the hat rows 295 / 305 / 301) and ArtTintGroupUnlock.txt (4419 on all six fantasy skintone
        /// groups); the live login list (dump) lacks them. Template 3711 Inventory Expansion, like the figurines: no
        /// ability, so the bag's "Use" sends ActivateItem (0x2D sub 0x15) and <see cref="ItemUses.TryActivate"/> →
        /// <see cref="Unlockers.TryUse"/> answers. Category 289, tradeable (577356: "This item can be traded…"; the hats
        /// were Outpost / chest loot, P6505); stack size the template's.
        /// </summary>
        private static Spec ParlorUnlocker(uint id, string note, uint name, uint description, uint icon)
            => new(id, FigurineTemplate, note)
            {
                NameId = name, DescriptionId = description, ImageSetId = icon, Category = ParlorOptionCategory, NoTrade = false
            };

        /// <summary>
        /// One figurine: the client's own name / description texts and ImageSets icon (spec §2 table), category 291, a stack
        /// of one. Tradeable: the Lop Ear dropped as loot; NO_TRADE of the Marketplace ones is unproven (spec §5.2, §6.9).
        /// </summary>
        private static Spec Figurine(uint id, string note, uint name, uint description, uint icon)
            => new(id, FigurineTemplate, note)
            {
                NameId = name, DescriptionId = description, ImageSetId = icon, MaxStack = 1, Category = PetCategory, NoTrade = false
            };

        /// <summary>
        /// Template of the 22 firework consumables: 4443 Pack of Unstable Fireworks (category 290 "Fireworks", flags
        /// [8,0,0], stack 100, quality 2, one ability (1237, type 4, input "", category "Combat")). Its decompressed record
        /// (282 bytes) has the same shape as the Cozy Campfire's (3894, ability 1098): ability count at 211 and the first
        /// ability's id at 215 (spec-fireworks.md §5.2 E.1; checked 2026-10-16 against the dump: 4443 → 1237 at 215, 3894 →
        /// 1098 at 215, and <see cref="Walk"/> lands its FirstAbility there). Its grantId (record length − 20) is 0, so the
        /// copies grant nothing. With a type-4 ability the item is cast (AbilityCastAttempt) like the campfire, and
        /// Abilities.HandleCast finds it in the bag (slot 0 = IsConsumable) and hands the ability to Consumables.
        /// </summary>
        public const uint FireworkTemplate = 4443;

        /// <summary>Item category 290 "Fireworks" (category name text 577365), the packs' own.</summary>
        public const uint FireworksCategory = 290;

        /// <summary>
        /// One firework consumable (TASKS 4.12 / 3.9, spec-fireworks.md §2.2 / §5.2 E.1), from <see cref="Fireworks.Kinds"/>:
        /// <list type="bullet">
        /// <item>id: the candidate 4421-4442 — UNPROVEN (lead L24): the 22-id gap below 4443 in the client's text order,
        /// not seen in any capture. If a real id turns up it replaces the candidate here and in Fireworks.Kinds.</item>
        /// <item>ability: OURS, 1215-1236 (unused in the live dump, below 4443's 1237); live's ids are unknown (L25). The
        /// server answers their CasterInfo itself (Abilities.BuildItemAbilityInfo), so no client table needs them.</item>
        /// <item>name / description / icon: the client's own text_695 / ImageSets (note the swapped description strings,
        /// see Fireworks.Kinds); category 290 and stack 100 as the packs; tradeable flags as the template.</item>
        /// </list>
        /// </summary>
        private static Spec Firework(Fireworks.Kind kind)
            => new(kind.ItemId, FireworkTemplate, kind.Name + " (candidate id, L24)")
            {
                NameId = kind.NameId, DescriptionId = kind.DescriptionId, ImageSetId = kind.ImageSetId,
                MaxStack = 100, Category = FireworksCategory, AbilityId = kind.AbilityId
            };

        /// <summary>
        /// Template of the outfits (TASKS 4.17 / §4 "Outfits", trace/0695/cosmetics-695.md): 2521 Colonist's Elegant Robe
        /// (Russet) — chest slot 1, category 29, flags [0,0,0] (tradeable: the 2016 drops could be traded to friends,
        /// P6569),
        /// stack 1, quality 0, no ability, model Colony_Light_000 + tint Colony_Light_10. Every live outfit in the dump has
        /// this shape; only the texts, icon, model path and tint alias differ.
        /// </summary>
        public const uint OutfitTemplate = 2521;

        private static Spec Outfit(Cosmetics.Outfit o)
            => new(o.Id, OutfitTemplate, o.Name + " (own id, " + o.Proof + ")")
            {
                NameId = o.NameId, DescriptionId = o.DescriptionId, ImageSetId = o.ImageSetId, ModelFile = o.ModelFile, TintAlias = o.Tint
            };

        /// <summary>
        /// Template of the title items (TASKS 4.16): 4519 Title: Champion of the People — category 229, flags [136,16,0]
        /// (NO_TRADE), icon 1759, no description, grant 60. Copies get their own name and grant; <see cref="Titles"/> finds
        /// the title text from the name.
        /// </summary>
        public const uint TitleTemplate = 4519;

        private static Spec TitleItem(Cosmetics.TitleItem t)
            => new(t.Id, TitleTemplate, t.Name + " (own id, grant " + t.GrantId + " [I])")
            {
                NameId = t.NameId, GrantId = t.GrantId
            };

        /// <summary>
        /// One "Recipe: Barren …" item (OPEN-LEADS L44, <see cref="BarrenRecipes"/>): a copy of 3561 "Recipe: Elite Barren
        /// Captain" (type 45, flags [8,16,0], quality 4, category 274) with the client's own name / description texts
        /// 574376-574399, its creature prop as the grant and that prop's PropDefs icon [P texts and record shape; id ours].
        /// </summary>
        private static Spec BarrenRecipeItem(BarrenRecipes.Recipe r)
            => new(r.ItemId, BarrenRecipes.Template, r.Name + " (own id, L44)")
            {
                NameId = r.NameId, DescriptionId = r.DescriptionId, ImageSetId = r.ImageSetId, GrantId = r.PropId
            };

        /// <summary>
        /// The built items. Texts / icons are the client's own; templates and ids as noted.
        /// </summary>
        public static readonly Spec[] Items = new Spec[]
        {
            // Cosmetic pet figurines (spec-pets.md §2): name, description ("This weathered figurine houses the soul of a
            // fearless Shadow Wolf Pup who will loyally follow you on your many adventures." …), icon Pets_* 21734-21740.
            // The second "Cockatrice Figurine" name text 577547 is not used (whether it was a second item is unknown, §6.1).
            Figurine(ShadowWolfFigurine, "Shadow Wolf Figurine", 577507, 577508, 21740),   // Pets_Wolf_Pup01
            Figurine(StagFigurine, "Stag Figurine", 577512, 577513, 21739),                // Pets_Deer_Fawn01
            Figurine(CockatriceFigurine, "Cockatrice Figurine", 577515, 577516, 21738),    // Pets_Cockatrice01
            Figurine(BunnyFigurine, "Bunny Figurine", 577518, 577519, 21735),              // Pets_Bunny_Bunny01
            Figurine(BeetleFigurine, "Beetle Figurine", 577521, 577522, 21734),            // Pets_Beetle_Klicnik01
            Figurine(JackalopeFigurine, "Jackalope Figurine", 577532, 577533, 21736),      // Pets_Bunny_Jackalope01
            Figurine(LopEarBunnyFigurine, "Lop Ear Bunny Figurine", 577538, 577539, 21737), // Pets_Bunny_Lopear01

            // Crimson Parlor unlockers (live ids, see ParlorUnlocker). 4419: name 577355 "Fantasy Parlor Pack", description
            // 577356 ("When consumed from your inventory this delightful pack unlocks … four Fantasy themed Faces, two …
            // Eyes, and several … Skintones"; 577361 "Unlocks four Fantasy themed Face options …" reads like the
            // Marketplace product text), icon 21675 Conucopia01 — the one image set in Images/icons_barbershop that is no
            // option icon, inside the Parlor icon block 21667-21680 [I]. Hats: name / description 577699-577704 ("Unlocks
            // access to the Cowboy Hat (Hair) option for Luminaries of all genders and sizes…"), icons 21775-21777
            // wearable_Hats_Cowboy / _Ninja / _Samurai.
            ParlorUnlocker(Unlockers.FantasyParlorPack, "Fantasy Parlor Pack", 577355, 577356, 21675),
            ParlorUnlocker(Unlockers.CowboyHat, "Cowboy Hat unlocker", 577699, 577700, 21775),
            ParlorUnlocker(Unlockers.NinjaMask, "Ninja Mask unlocker", 577701, 577702, 21776),
            ParlorUnlocker(Unlockers.SamuraiHelmet, "Samurai Helmet unlocker", 577703, 577704, 21777),

            // Build Site Expansion: name 571204, description 571205 ("increases the number of Build Sites you can place by
            // one"), icon 10464 flag_claim_expansion (the Attached flag 591 uses flag_claim_attached 1158, the same naming);
            // tradeable (bundle text 571247 "Both items can be traded"); used from the bag like the Inventory Expansion
            // 3711, its template. Id ours (L86). Forum (trace/0695/agent-leads-sweep.md L86): the developers, Jul 2016,
            // a bought expansion goes to the inventory, to use or to trade to a friend (57682-1); a player: right-click
            // uses it and the item disappears (56442-3). So NO_TRADE
            // is off here (Trades / Mail check it) and a use takes one (Claims.UseExpansion) and adds the slot.
            new(BuildSiteExpansion, 3711, "Build Site Expansion")
            {
                NameId = 571204, DescriptionId = 571205, ImageSetId = 10464, NoTrade = false
            },
            new(ThankYouToken, 1579, "Thank You Token")
            {
                NameId = 577640, DescriptionId = 577641, ImageSetId = 1643
            },
            // Trial keys (TASKS 3.12): the copy a player is mailed (mailable) and the mailed copy (NO_TRADE, usable).
            TrialKeyItem(TrialKey, false, "Trial key (own id; mail it to a friend)"),
            TrialKeyItem(TrialKeyMailed, true, "Trial key, mailed (own id; use it for trial membership)"),
        }
        // The 22 firework consumables (Firework above; candidate ids 4421-4442, our abilities 1215-1236).
        .Concat(Fireworks.Kinds.Select(Firework))
        // Outfits (Skeletal Bodysuits, Holiday Sweaters, Colonist's Flight Suits, Year Zero, Tech gear) and the late
        // titles: Cosmetics.Outfits / Cosmetics.Titles, own ids OwnIdBase + 301.. / + 201.. .
        .Concat(Cosmetics.Outfits.Select(Outfit))
        .Concat(Cosmetics.Titles.Select(TitleItem))
        // The twelve "Recipe: Barren …" items (L44): own ids OwnIdBase + 701..712 (BarrenRecipes.All).
        .Concat(BarrenRecipes.All.Select(BarrenRecipeItem)).ToArray();
    }
}
