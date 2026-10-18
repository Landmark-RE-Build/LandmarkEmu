using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
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
    /// Build palettes (PaletteBase 0xF5, u16 sub): the radial menu / quick slots of the build tools.
    ///
    /// The client keeps the palettes the server sends: sub 5 PaletteUpdate (handler FUN_142e74800, reader
    /// FUN_142b8c490): string name, u64 id, u32 n × (u32 slot key, u32 slot, u32 type, u64 value) (FUN_142b9bf70 /
    /// FUN_142b8c560), bool, u32. In build mode the radial menu lists the palettes while none is chosen (HUD palette
    /// id -1, FUN_143397410) and picking one selects it (FUN_143393b50). Only a chosen palette with an id above 0 is
    /// edited through the server: RadialMenu.AssignPaletteSlot (FUN_14354aca0 → FUN_142e75cc0) sends 3 AddEntry
    /// {u64 palette, u32 slot, u32 type, u64 value}, RemovePaletteSlot sends 4 DeleteEntry {u64 palette, u32 slot}
    /// (FUN_142e75f10) followed by 7 SlotUpdates. 1 Create / 2 Delete manage extra palettes (not offered here).
    ///
    /// Each player has one palette, saved slot by slot (character_palette_entry) and sent whole after every change.
    /// </summary>
    public static class Palettes
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const ushort PaletteUpdateSub = 5, AddEntrySub = 3, DeleteEntrySub = 4, SlotUpdatesSub = 7;
        private const uint MaxSlot = 64;

        /// <summary>A player's palette id: their guid with a tag in the high bits (never 0).</summary>
        public static ulong PaletteId(WorldSession session) => 0x5041_0000_0000_0000UL | session.PlayerGuid;

        private static List<CharacterPaletteEntryModel> Entries(ulong characterId)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.CharacterPaletteEntry.Where(e => e.CharacterId == characterId).OrderBy(e => e.Slot).ToList();
        }

        /// <summary>Sends the player's palette with its saved slots (at zone-in and after each change).</summary>
        public static void Send(WorldSession session)
        {
            List<CharacterPaletteEntryModel> entries = Entries(session.PlayerGuid);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(PaletteUpdateSub);
            writer.Write("Palette");
            writer.Write(PaletteId(session));
            writer.Write((uint)entries.Count);
            foreach (CharacterPaletteEntryModel entry in entries)
            {
                writer.Write(entry.Slot);
                writer.Write(entry.Slot);
                writer.Write(entry.Type);
                writer.Write(entry.Value);
            }
            // Read-only flag (palette +0x54): the client sends AddEntry / DeleteEntry only for a palette with it 0
            // (FUN_142f14850 / FUN_142f14a10 test it) [P] — sent true until 2026-10-18, so palette changes never reached
            // the server and were lost on relog.
            writer.Write(false);
            writer.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.PaletteBase, output.ToArray());
        }

        /// <summary>Saves one palette slot (AddEntry; dev "paletteslot") and re-sends the palette.</summary>
        public static void SetSlot(WorldSession session, uint slot, uint type, ulong value)
        {
            if (slot > MaxSlot)
                return;
            ulong characterId = session.PlayerGuid;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                c.CharacterPaletteEntry.RemoveRange(c.CharacterPaletteEntry.Where(e => e.CharacterId == characterId && e.Slot == slot));
                c.CharacterPaletteEntry.Add(new CharacterPaletteEntryModel { CharacterId = characterId, Slot = slot, Type = type, Value = value });
            }).ContinueWith(_ => Send(session));
            log.Info($"{session.CharacterName}: palette slot {slot} = type {type} value {value}.");
        }

        [ZoneMessageHandler(ZoneMessageOpcode.PaletteBase)]
        public static void Handle(WorldSession session, PaletteRequest request)
        {
            var reader = new GamePacketReader(request.Body);
            ulong characterId = session.PlayerGuid;
            switch (request.SubOpcode)
            {
                case AddEntrySub when request.Body.Length >= 24:
                {
                    ulong palette = reader.ReadULong();
                    uint slot = reader.ReadUInt(), type = reader.ReadUInt();
                    ulong value = reader.ReadULong();
                    if (palette == PaletteId(session))
                        SetSlot(session, slot, type, value);
                    break;
                }
                case DeleteEntrySub when request.Body.Length >= 12:
                {
                    ulong palette = reader.ReadULong();
                    uint slot = reader.ReadUInt();
                    if (palette != PaletteId(session))
                        return;
                    DatabaseManager.Instance.CharacterDatabase.Save(c =>
                        c.CharacterPaletteEntry.RemoveRange(c.CharacterPaletteEntry.Where(e => e.CharacterId == characterId && e.Slot == slot)))
                        .ContinueWith(_ => Send(session));
                    log.Info($"{session.CharacterName}: palette slot {slot} cleared.");
                    break;
                }
                case SlotUpdatesSub:
                    // SlotUpdates (FUN_142e74ee0, after a DeleteEntry): the client re-packs its PALETTES (not their
                    // entries) into radial-menu positions, an array of PaletteIdToSlot. Each player has one server
                    // palette, so its position never changes and nothing is stored.
                    log.Debug($"{session.CharacterName}: palette positions {BitConverter.ToString(request.Body)}");
                    break;
                default:
                    log.Info($"{session.CharacterName}: palette sub {request.SubOpcode}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }
    }
}
