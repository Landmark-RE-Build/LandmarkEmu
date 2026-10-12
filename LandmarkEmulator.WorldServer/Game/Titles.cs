using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Game;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player titles, unlocked by using a "Title: …" item and shown around the player's name.
    ///
    /// A title is {u32 id, u32 type, u32 name text}: type 1 = prefix ("PlayerTitleFormatPrefix", "Blockhead Name"),
    /// 2 = suffix ("Name the Dramatic"; the title text itself starts with a space). The client data has no title table, so
    /// a title's id is the id of the item that grants it and its text is the one next to the item name
    /// ("Title: the Dramatic" 575472 → " the Dramatic" 575471).
    ///
    /// Packets (PlayerTitleBase 0x94, u8 sub; client handler FUN_142e89bd0):
    /// - 1 Add (S→C) {title}: adds one to the title list;
    /// - 4 RequestSelect (C→S, Lua SelectPlayerTitle → FUN_142e89ea0) {u32 title id, 0 = none};
    /// - PlayerUpdate 0x26 sub 0x1C PlayerTitle (S→C) {u64 player guid, title}: the title shown on that player
    ///   (dispatcher case 0x1B = sub - 1; handler FUN_142e26600; for the client's own player it also marks it selected).
    /// SendSelfToClient carries the owned list and the selected id at login. Both are kept in <see cref="Unlocks"/>.
    /// </summary>
    public static class Titles
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const byte AddSub = 1, RequestSelectSub = 4;

        public sealed record Title(uint Id, uint Type, uint NameId);

        private static Dictionary<uint, Title> byItem;

        /// <summary>Title items → titles, from the item names ("Title: X") and the text just before or after them.</summary>
        private static Dictionary<uint, Title> TitleItems()
        {
            if (byItem != null)
                return byItem;
            var map = new Dictionary<uint, Title>();
            foreach (var item in ItemDefinitionManager.All)
            {
                string name = TextManager.Instance.GetTextForId(item.NameId) ?? "";
                if (!name.StartsWith("Title: "))
                    continue;
                string title = name["Title: ".Length..];
                // The title's own text sits within a few ids of the item name.
                for (int d = -4; d <= 4; d++)
                {
                    uint id = (uint)(item.NameId + d);
                    string text = TextManager.Instance.GetTextForId(id);
                    if (text == title || text == " " + title)
                    {
                        map[item.Id] = new Title(item.Id, text.StartsWith(' ') ? 2u : 1u, id);
                        break;
                    }
                }
                // Otherwise anywhere in the text table (e.g. "Title: Curator" 576980 → "Curator" 577059), nearest first.
                if (!map.ContainsKey(item.Id))
                {
                    var found = TextManager.Instance.FindIds(title).Select(i => (Id: i, Type: 1u))
                        .Concat(TextManager.Instance.FindIds(" " + title).Select(i => (Id: i, Type: 2u)))
                        .Where(t => t.Id != item.NameId)
                        .OrderBy(t => Math.Abs((long)t.Id - item.NameId)).ToList();
                    if (found.Count > 0)
                        map[item.Id] = new Title(item.Id, found[0].Type, found[0].Id);
                }
                if (!map.ContainsKey(item.Id))
                    log.Warn($"Title item {item.Id} ({name}): no title text found.");
            }
            log.Info($"{map.Count} title items.");
            return byItem = map;
        }

        public static Title Get(uint id) => TitleItems().GetValueOrDefault(id);

        private static uint Shown(ulong characterId) => Unlocks.Values(characterId, Unlocks.TitleShown).FirstOrDefault();

        /// <summary>The SendSelfToClient title block: owned titles and the one shown.</summary>
        public static SendSelfToClient.TitleData SelfData(ulong characterId)
        {
            var data = new SendSelfToClient.TitleData();
            foreach (uint id in Unlocks.Values(characterId, Unlocks.TitleOwned))
                if (Get(id) is { } t)
                    data.TitleList.Add(new SendSelfToClient.TitleData.Title { TitleId = t.Id, TitleType = t.Type, NameId = new LandmarkText(t.NameId) });
            data.CurrentTitleId = Shown(characterId);
            return data;
        }

        /// <summary>Using a title item: the title is added (once) and the item used up.</summary>
        public static bool TryUseItem(WorldSession session, CharacterItemModel item)
        {
            if (Get(item.ItemDefinitionId) is not { } title)
                return false;
            if (Unlocks.Has(session, Unlocks.TitleOwned, title.Id))
            {
                Chat.System(session, "You already have that title.");
                return true;
            }
            if (!session.TakeItem(item.ItemDefinitionId, 1))
                return true;
            Unlocks.Add(session, Unlocks.TitleOwned, title.Id);

            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(AddSub);
            Write(w, title);
            session.EnqueueMessage(ZoneMessageOpcode.PlayerTitleBase, output.ToArray());
            Chat.System(session, $"New title: {TextManager.Instance.GetTextForId(title.NameId)?.Trim()}.");
            log.Info($"{session.CharacterName} unlocked title {title.Id}.");
            return true;
        }

        /// <summary>PlayerTitleBase from the client: RequestSelect {u32 title id} (0 clears the title).</summary>
        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            if (sub != RequestSelectSub || body.Length < 4)
                return;
            uint id = BitConverter.ToUInt32(body, 0);
            if (id != 0 && !Unlocks.Has(session, Unlocks.TitleOwned, id))
                return;
            uint before = Shown(session.PlayerGuid);
            Unlocks.Set(session, Unlocks.TitleShown, id);
            log.Info($"{session.CharacterName} shows title {id} (was {before}).");
            Broadcast(session);
        }

        /// <summary>Dev: forgets the player's titles (memory and database; the client keeps its list until relog).</summary>
        public static void DevReset(WorldSession session)
        {
            Unlocks.Clear(session, Unlocks.TitleOwned, Unlocks.TitleShown);
            Broadcast(session);
        }

        /// <summary>Does the player show a title (others are told about it when they first see them)?</summary>
        public static bool Shows(WorldSession session) => Shown(session.PlayerGuid) != 0;

        /// <summary>Shows a player's title to everyone who can see them (and to themselves).</summary>
        public static void Broadcast(WorldSession session)
        {
            byte[] packet = UpdatePacket(session);
            foreach (WorldSession viewer in PlayerManager.InZone(session.ZoneId))
                viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdatePlayerTitleWire, packet);
        }

        /// <summary>The PlayerUpdate PlayerTitle body for a player (guid, title; an empty title shows none).</summary>
        public static byte[] UpdatePacket(WorldSession session)
        {
            uint shown = Shown(session.PlayerGuid);
            Title title = shown != 0 ? Get(shown) : null;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(session.PlayerGuid);
            Write(w, title ?? new Title(0, 0, 0));
            return output.ToArray();
        }

        private static void Write(GamePacketWriter w, Title title)
        {
            w.Write(title.Id);
            w.Write(title.Type);
            w.Write(title.NameId);
        }
    }
}
