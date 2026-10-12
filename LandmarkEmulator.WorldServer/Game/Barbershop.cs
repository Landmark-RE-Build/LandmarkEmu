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
    /// u32 skin tint, string, u32, u32 — the whole new look, the same fields character creation stores.
    /// Price (CharCustomizationSlotCost.txt, per race and slot): MODEL_COST when a slot's option changes, TINT_COST when
    /// its tint changes; the skin tint is the head slot's (2) tint. Paid in Lumens.
    /// </summary>
    public static class Barbershop
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const byte PlayerSetSub = 1;
        private const uint HeadSlot = 2;

        /// <summary>(race, slot) → (model cost, tint cost).</summary>
        private static Dictionary<(uint Race, uint Slot), (uint Model, uint Tint)> costs;

        private static Dictionary<(uint Race, uint Slot), (uint Model, uint Tint)> Costs => costs ??= GameDataFiles.Rows("CharCustomizationSlotCost.txt")
            .Where(r => r.Length > 5)
            .ToDictionary(r => (uint.Parse(r[2]), uint.Parse(r[3])), r => (uint.Parse(r[4]), uint.Parse(r[5])));

        [ZoneMessageHandler(ZoneMessageOpcode.CharacterCustomizationBase)]
        public static void Handle(WorldSession session, CharacterCustomizationRequest request)
        {
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

            uint cost = Price(session, slots, skinTint);
            if (cost > 0 && !session.ChangeCoins(-(long)cost))
            {
                Chat.System(session, $"You need {cost} Lumens for that.");
                log.Info($"{session.CharacterName} could not afford a {cost} Lumen makeover.");
                PlayerManager.Reshow(session);   // put their old look back on their screen
                return;
            }
            session.SetAppearance(slots, skinTint);
            PlayerManager.Reshow(session);
            log.Info($"{session.CharacterName} changed their look for {cost} Lumens: " +
                string.Join(", ", slots.Select(s => $"{s.Slot}:{s.Option}/{s.Tint:X}")) + $", skin {skinTint:X}.");
        }

        /// <summary>What the new look costs against the character's current one.</summary>
        private static uint Price(WorldSession session, List<(uint Slot, uint Option, uint Tint)> slots, uint skinTint)
        {
            var (current, currentSkin) = session.Appearance;
            uint race = session.Race;
            uint cost = 0;
            foreach (var (slot, option, tint) in slots)
            {
                if (!Costs.TryGetValue((race, slot), out var price))
                    continue;
                current.TryGetValue(slot, out var was);
                if (was.Option != option)
                    cost += price.Model;
                if (was.Tint != tint)
                    cost += price.Tint;
            }
            if (skinTint != currentSkin && Costs.TryGetValue((race, HeadSlot), out var head))
                cost += head.Tint;
            return cost;
        }
    }
}
