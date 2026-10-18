using LandmarkEmulator.Database.Character.Model;
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
    /// Inspect (0.695, 30 Jun 2016 "Bio and Inspect"; trace/0695/packets-695.md §2): right-click another player → Inspect
    /// (Lua Ui.InspectPlayer) sends RequestInspectPlayerPacket, InspectBase 0x91 sub 3 {u64 character}. The answer is
    /// StartInspectPacket, sub 1 (reader FUN_142c4e670 / body FUN_142c2f8c0): u64 character, u32 n, n bytes of blob,
    /// which the client parses into an InspectProxy (FUN_143089c10) and shows in InspectWidget (Character.InspectData):
    /// <list type="number">
    /// <item>u32 n × {u32 key, u32 value}: profile entries (L28; none sent).</item>
    /// <item>u32 Luminary Score (<see cref="Achievements.LuminaryScore"/>).</item>
    /// <item>u32 number of build sites.</item>
    /// <item>u32 n × u64 site id (the client counts those its gallery knows as NumberOfGalleryListings).</item>
    /// <item>string bio.</item>
    /// <item>u32 (InspectProxy +0x110, meaning not found: L28; 0).</item>
    /// <item>u32 n × {i32 equipment slot, item record (FUN_142c208e0 = the 0.589 record), item definition (u16 packed
    /// size, u16 size, LZ4 ClientItemDefinition, FUN_142c19850)}: the equipped items.</item>
    /// <item>u32 (InspectProxy +0x11c, L28; 0).</item>
    /// </list>
    /// The inspected player must be one the client has as an actor, so only players on this world are answered.
    /// StopInspect (sub 2) needs no answer. 0.589 has no request (its parser differs; never sent to 0.589 clients).
    /// </summary>
    public static class Inspect
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte StartInspectSub = 1, StopInspectSub = 2, RequestInspectSub = 3;

        [ZoneMessageHandler(ZoneMessageOpcode.InspectBase)]
        public static void Handle(WorldSession session, InspectRequest request)
        {
            switch (request.SubOpcode)
            {
                case RequestInspectSub when request.Body.Length >= 8:
                    Start(session, BitConverter.ToUInt64(request.Body, 0));
                    break;
                case StopInspectSub:
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Inspect", request.SubOpcode);
                    break;
            }
        }

        /// <summary>StartInspect for a player on this world (also used by the dev command "inspect").</summary>
        public static void Start(WorldSession session, ulong character)
        {
            if (PlayerManager.FindByGuid(character) is not WorldSession target || target.Inventory == null)
            {
                // A player no longer here: the client's own "[player] is not online at this." (L175; nothing for a guid
                // that is no character).
                if (character != session.PlayerGuid && PlayerManager.FindByGuid(character) == null)
                    TargetNotOnline.Tell(session, character);
                log.Debug($"{session.CharacterName} asked to inspect 0x{character:X}, who isn't here.");
                return;
            }
            var blob = new List<byte>();
            var w = new GamePacketWriter(blob);
            w.Write(0u);                                    // profile entries (L28)
            w.Write(Achievements.LuminaryScore(target.PlayerGuid));
            List<ulong> sites = Claims.All().Where(c => c.OwnerGuid == target.PlayerGuid).Select(c => c.Id).ToList();
            w.Write((uint)sites.Count);
            w.Write((uint)sites.Count);
            foreach (ulong site in sites)
                w.Write(site);
            w.Write(target.Bio ?? "");
            w.Write(0u);                                    // +0x110 (L28)
            var equipped = new List<(uint Slot, CharacterItemModel Item, byte[] Definition)>();
            foreach (CharacterItemModel item in target.Inventory.Items.Where(i => i.Container == Inventory.Inventory.EquippedContainer))
                if (Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { RawData: not null } definition)
                    equipped.Add((definition.EquipSlotId, item, definition.RawData));
            w.Write((uint)equipped.Count);
            foreach ((uint slot, CharacterItemModel item, byte[] definition) in equipped)
            {
                w.Write(slot);
                Inventory.Inventory.ToPacket(item).WriteRecord(w);
                w.WriteBytes(definition);
            }
            w.Write(0u);                                    // +0x11c (L28)

            var output = new List<byte>();
            var p = new GamePacketWriter(output);
            p.Write(StartInspectSub);
            p.Write(target.PlayerGuid);
            p.Write((uint)blob.Count);
            p.WriteBytes(blob.ToArray());
            session.EnqueueMessage(ZoneMessageOpcode.InspectBase, output.ToArray());
            log.Info($"{session.CharacterName} inspects {target.CharacterName} ({sites.Count} site(s), {equipped.Count} equipped item(s)).");
        }
    }
}
