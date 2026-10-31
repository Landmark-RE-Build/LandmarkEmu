using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The barbershop (The Crimson Parlor at the Spire; <see cref="Spire.IsBarber"/>). Opening it is client-side: the
    /// server runs CharacterCustomization.StartCustomization in the client (<see cref="ClientLua"/>), which shows the
    /// Barbershop widget with the character's current look. Purchase sends CharacterCustomization 0x11C sub 1 PlayerSet
    /// (built in FUN_142ffeec0; captured): u32 1, u64 character guid, u32 n × (u32 slot, u32 option, u32 tint),
    /// u32 skin tint, string, u32, u32 — the whole new look, the same fields character creation stores. 0.695 (writer
    /// FUN_1430a0530) appends u32 new base-actor id and u32 0, read past here; using the base actor is the 0.695 Parlor
    /// work (trace/0695/functions-695.md B4 / B5).
    /// Price: CharCustomizationSlotCost.txt (per race and slot: MODEL_COST, TINT_COST), paid in Lumens; see
    /// <see cref="Price"/>.
    /// <para>
    /// 0.695 (28 Jul 2016: the Parlor can change gender and size, i.e. race): the Parlor asks for the character's
    /// equipped attachments
    /// when it opens (EquippedAttachmentRequest sub 3 {u64 character, -1 = self}; answered with EquippedAttachmentReply
    /// sub 4, see <see cref="SendAttachments"/>), PlayerSet carries a new race and gender (the customization data's own
    /// race / gender fields) plus the new base actor, and the price is the client's own (<see cref="Price"/>).
    /// </para>
    /// </summary>
    public static class Barbershop
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const byte PlayerSetSub = 1, EquippedAttachmentRequestSub = 3, EquippedAttachmentReplySub = 4;

        /// <summary>(race, slot) → (model cost, tint cost).</summary>
        private static Dictionary<(uint Race, uint Slot), (uint Model, uint Tint)> costs;

        private static Dictionary<(uint Race, uint Slot), (uint Model, uint Tint)> Costs => costs ??= GameDataFiles.Rows("CharCustomizationSlotCost.txt")
            .Where(r => r.Length > 5)
            .ToDictionary(r => (uint.Parse(r[2]), uint.Parse(r[3])), r => (uint.Parse(r[4]), uint.Parse(r[5])));

        [ZoneMessageHandler(ZoneMessageOpcode.CharacterCustomizationBase)]
        public static void Handle(WorldSession session, CharacterCustomizationRequest request)
        {
            if (request.SubOpcode == EquippedAttachmentRequestSub)
            {
                SendAttachments(session);
                return;
            }
            if (request.SubOpcode != PlayerSetSub)
            {
                log.Debug($"{session.CharacterName}: unhandled customization sub {request.SubOpcode}: {BitConverter.ToString(request.Body)}");
                return;
            }
            var reader = new GamePacketReader(request.Body);
            reader.ReadUInt();
            ulong guid = reader.ReadULong();
            uint n = reader.ReadUInt();
            if (guid != session.PlayerGuid || n > 16)
                return;
            var slots = new List<(uint Slot, uint Option, uint Tint)>();
            for (uint i = 0; i < n; i++)
                slots.Add((reader.ReadUInt(), reader.ReadUInt(), reader.ReadUInt()));
            uint skinTint = reader.ReadUInt();
            // The rest of the customization data: string, race, gender.
            uint race = session.Race, gender = session.GenderId;
            if (reader.BytesRemaining >= 4)
            {
                reader.ReadString();
                if (reader.BytesRemaining >= 8)
                {
                    race = reader.ReadUInt();
                    gender = reader.ReadUInt();
                }
            }
            uint profile = 0;
            bool newBody = race != session.Race || gender != session.GenderId;
            if (newBody && (profile = PresetProfile(race, gender)) == 0)
            {
                log.Warn($"{session.CharacterName} asked for race {race} / gender {gender}, which has no character preset.");
                PlayerManager.Reshow(session);
                return;
            }

            if (LockedChoice(session, slots, skinTint) is string locked)
            {
                // The client never blocks a locked pick itself (BarberShopWidget.as:386-390 → ConfirmCustomization sends
                // PlayerSet as is), so the server refuses it; no client text exists for this, hence a plain line.
                Chat.System(session, "That look uses a Crimson Parlor option you haven't unlocked yet.");
                log.Info($"{session.CharacterName} picked a locked Parlor {locked}; look refused.");
                PlayerManager.Reshow(session);
                return;
            }

            uint cost = Price(session, slots, race, gender);
            if (cost > 0 && !session.ChangeCoins(-(long)cost))
            {
                Chat.System(session, $"You need {cost} Lumens for that.");
                log.Info($"{session.CharacterName} could not afford a {cost} Lumen makeover.");
                PlayerManager.Reshow(session);   // put their old look back on their screen
                return;
            }
            session.SetAppearance(slots, skinTint);
            if (newBody)
            {
                session.SetBody(race, gender, profile);
                log.Info($"{session.CharacterName} is now race {race}, gender {gender} (profile {profile}).");
            }
            SendLookChange(session);
            log.Info($"{session.CharacterName} changed their look for {cost} Lumens: " +
                string.Join(", ", slots.Select(s => $"{s.Slot}:{s.Option}/{s.Tint:X}")) + $", skin {skinTint:X}.");
        }

        /// <summary>
        /// EquippedAttachmentReply (0x11C sub 4, reader FUN_1430a0cc0): u32 n x CharacterAttachmentData, the same records as
        /// the attachment list of the full player data; the client keeps them at customization manager +0x1360 (L34).
        /// The Parlor dresses its preview from this list, so it holds what the character wears: each visible equipment
        /// item's attachment, the same record as the attachment part of EquipmentSetCharacterEquipmentSlot (model,
        /// texture alias, tint alias, decal alias, u32 tint, u32 composite effect, u32 slot, u32 0, bool; record reader
        /// FUN_142c185c0) [P layout]. Sent empty before 2026-10-17, the preview — and, after Purchase, the character —
        /// showed the bare creation clothes instead of the worn outfit (user report; harness: Holiday Sweater → brown
        /// starter top).
        /// </summary>
        private static void SendAttachments(WorldSession session)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(EquippedAttachmentReplySub);
            WriteWorn(session, writer);
            session.EnqueueMessage(ZoneMessageOpcode.CharacterCustomizationBase, output.ToArray());
        }

        /// <summary>
        /// The new look to the buyer and everyone on the landscape: PlayerUpdate 0x1B CustomizationChange (0.695 reader
        /// FUN_142c27ad0: u64 character, bool, customization data FUN_142c18710 = slots / skin / string / race / gender,
        /// u32 n × attachment records) [P]. The client's case (dispatcher FUN_142cdfa60) finds the entity, for its own
        /// player also stores the look in its player data (FUN_142f160a0), and rebuilds the model with those
        /// attachments (FUN_1430a1e60); bool false also refreshes the actor (FUN_142cd0600 / FUN_142ceb390) [P; I what
        /// the refresh is]. Without it the buyer's client kept its old look once the Parlor closed (user report
        /// 2026-10-17: "when pressing purchase it stays the same as before").
        /// </summary>
        private static void SendLookChange(WorldSession session)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(session.PlayerGuid);
            writer.Write(false);
            session.ModelInfo.Write(writer);
            WriteWorn(session, writer);
            byte[] body = output.ToArray();
            foreach (WorldSession viewer in PlayerManager.Snapshot().Where(p => p.ZoneId == session.ZoneId).Append(session).Distinct())
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateCustomizationChangeWire, body);
        }

        /// <summary>
        /// u32 n × the attachment record of each visible equipment item (see <see cref="SendAttachments"/>), with the
        /// main-hand item's off-hand model (shield, quiver, second gun: WorldSession.BuildSecondarySlot) as its own record:
        /// the client rebuilds the model from this list alone (FUN_1430a1e60), so a record left out is a hand left empty.
        /// </summary>
        private static void WriteWorn(WorldSession session, GamePacketWriter writer)
        {
            var worn = session.EquipmentSlotPackets();
            writer.Write((uint)worn.Count);
            foreach (var shown in worn)
            {
                writer.Write(shown.ModelName ?? "");
                writer.Write(shown.TextureAlias ?? "");
                writer.Write(shown.TintAlias ?? "");
                writer.Write("");
                writer.Write(shown.TintId);
                writer.Write(shown.CompositeEffect);
                writer.Write(shown.EquipSlotId);
                writer.Write(0u);
                writer.Write(false);
            }
        }

        #region 0.695 price

        /// <summary>
        /// Gender and size change prices (FUN_1430a5f10: DAT_1446f40b8 = 60 for a gender change, DAT_144703b10 = 30 for a
        /// race change, unless overridden by hash). The overrides are the client's own table StringHashToValue.txt, whose
        /// 0.695 copy has CharacterCustomization.GenderChangeCost^60 and RaceChangeCost^30: the same numbers, so the price
        /// shown and the price charged agree (trace/0695/phaseE3-ui.md F5).
        /// </summary>
        private const uint GenderChangeCost = 60, RaceChangeCost = 30;

        /// <summary>CharCustOptionMult: (OPTION_TYPE 1 option / 2 tint, OPTION_ID) -> multiplier (default 1).</summary>
        private static Dictionary<(uint Type, uint Id), float> multipliers;

        /// <summary>CharCreateOptionEqualities: option -> the options it counts as the same as.</summary>
        private static Dictionary<uint, HashSet<uint>> equalities;

        /// <summary>(race, gender) -> (profile, slot -> preset option) from the character creation presets.</summary>
        private static Dictionary<(uint Race, uint Gender), (uint Profile, Dictionary<uint, uint> Slots)> presets;

        private static void EnsurePresets()
        {
            if (presets != null)
                return;
            multipliers = GameDataFiles.Rows("CharCustOptionMult.txt")
                .GroupBy(r => (r.U(2), r.U(3))).ToDictionary(g => g.Key, g => g.First().F(4));
            equalities = GameDataFiles.Rows("CharCreateOptionEqualities.txt")
                .GroupBy(r => r.U(1)).ToDictionary(g => g.Key, g => g.Select(r => r.U(2)).ToHashSet());
            var optionSlot = GameDataFiles.Rows("CharacterCreateOptions.txt").GroupBy(r => r.U(0)).ToDictionary(g => g.Key, g => g.First().U(3));
            var selection = GameDataFiles.Rows("CharacterCreateSelectionDefinitions.txt").GroupBy(r => r.U(0)).ToDictionary(g => g.Key, g => g.First().U(1));
            var mapping = GameDataFiles.Rows("CharacterPresetToSelectionMappingDefinitions.txt").ToList();
            var result = new Dictionary<(uint, uint), (uint, Dictionary<uint, uint>)>();
            foreach (string[] preset in GameDataFiles.Rows("CharacterCreatePresetDefinitions.txt"))
            {
                var slots = new Dictionary<uint, uint>();
                foreach (string[] m in mapping.Where(m => m.U(1) == preset.U(0)))
                    if (selection.TryGetValue(m.U(2), out uint option) && optionSlot.TryGetValue(option, out uint slot))
                        slots[slot] = option;
                result[(preset.U(1), preset.U(2))] = (preset.U(3), slots);
            }
            presets = result;
        }

        /// <summary>The profile a character of this race and gender has (its creation preset), 0 if there's none.</summary>
        private static uint PresetProfile(uint race, uint gender)
        {
            EnsurePresets();
            return presets.TryGetValue((race, gender), out var preset) ? preset.Profile : 0;
        }

        private static float Multiplier(uint type, uint id)
            => multipliers.TryGetValue((type, id), out float m) ? m : 1f;

        private static bool Equal(uint option, uint other)
            => equalities.TryGetValue(option, out var same) && same.Contains(other);

        /// <summary>
        /// The 0.695 Parlor price, as the client shows it (CharacterCustomizationData.CurrentCost "TotalCost", worked out
        /// in FUN_1430a5f10): a gender change, a race change, then per slot the character already has: MODEL_COST x
        /// option multiplier when the option changed - free when the new option counts as the same as the old one
        /// (CharCreateOptionEqualities) or, after a race / gender change, is (or counts as) the new body's preset option -
        /// and TINT_COST x tint multiplier when the tint changed. Slot costs are the new race's. (0.589 charged MODEL_COST
        /// / TINT_COST per changed slot plus the head slot's TINT_COST for a new skin tint, with no multipliers.)
        /// </summary>
        private static uint Price(WorldSession session, List<(uint Slot, uint Option, uint Tint)> slots, uint race, uint gender)
        {
            EnsurePresets();
            var (current, _) = session.Appearance;
            bool bodyChanged = race != session.Race || gender != session.GenderId;
            float cost = 0;
            if (gender != session.GenderId)
                cost += GenderChangeCost;
            if (race != session.Race)
                cost += RaceChangeCost;
            presets.TryGetValue((race, gender), out var preset);
            foreach (var (slot, option, tint) in slots)
            {
                if (!current.TryGetValue(slot, out var was) || !Costs.TryGetValue((race, slot), out var price))
                    continue;
                if (option != was.Option)
                {
                    bool free = Equal(option, was.Option);
                    if (!free && bodyChanged && preset.Slots != null && preset.Slots.TryGetValue(slot, out uint presetOption))
                        free = option == presetOption || Equal(option, presetOption);
                    if (!free)
                        cost += (int)(price.Model * Multiplier(1, option));
                }
                if (tint != was.Tint)
                    cost += (int)(price.Tint * Multiplier(2, tint));
            }
            return (uint)cost;
        }

        #endregion

        /// <summary>
        /// The first pick of a new look the character may not have (null when all are allowed): a CharacterCreateOptions
        /// row that is not DEFAULT_UNLOCKED, or one of the 46 non-default tints, that <see cref="Unlockers"/> has not
        /// unlocked for them — unless the slot already wears it (for options: or one that counts as the same through
        /// CharCreateOptionEqualities, as after a race / gender change). This is the server half of the 0.695 Parlor's
        /// gating (FUN_1430a33a0 hides such rows, FUN_1430a4720 such tints, unless known; trace/0695/leads-L39.md).
        /// Unlocked options and tints count as owned: they pass here, and the price is the client's own either way
        /// (FUN_1430a5f10 never looks at knowledge).
        /// </summary>
        private static string LockedChoice(WorldSession session, List<(uint Slot, uint Option, uint Tint)> slots, uint skinTint)
        {
            EnsurePresets();
            var (current, currentSkin) = session.Appearance;
            ulong characterId = session.PlayerGuid;
            foreach (var (slot, option, tint) in slots)
            {
                bool wears = current.TryGetValue(slot, out var was);
                if (!(wears && (option == was.Option || Equal(was.Option, option) || Equal(option, was.Option)))
                    && !Unlockers.KnowsOption(characterId, option))
                    return $"option {option} (slot {slot})";
                if (!(wears && tint == was.Tint) && tint != currentSkin && !Unlockers.KnowsTintHash(characterId, tint))
                    return $"tint {tint:X} (slot {slot})";
            }
            if (skinTint != currentSkin && !Unlockers.KnowsTintHash(characterId, skinTint))
                return $"skin tint {skinTint:X}";
            return null;
        }
    }
}
