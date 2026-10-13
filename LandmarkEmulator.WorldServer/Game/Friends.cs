using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Friends lists (one-way, stored in character_friend). FriendBase 0x4D, u8 sub (client dispatcher in
    /// ClientFriendsDataManager):
    /// - 1 List {u32 count, FriendData × count} (FUN_14348cf60), sent once on world entry.
    /// - 2 Online {string name, u64 guid, bool} / 3 Offline {u64 guid}: sent only to players who list the one who
    ///   came or went.
    /// - 6 Add {FriendData} / 7 Remove {u64 guid}.
    /// FriendData (FUN_14348c040): name {3 × u32, string first, string last}, u64 guid, bool online; offline: u64
    /// last online (unix seconds); online: 4 × u32, bool (true skips 3 × u32), u32, bool, 5 × u32; then a string.
    /// Requests are Command 0x1E subs: 0x10 AddFriendRequest {string name, u64 guid} (by name from "/friend add Name",
    /// or empty name + guid from a player's menu; the client prints "X has been added as a friend" itself),
    /// 0x12 RemoveFriendRequest {name}.
    /// </summary>
    public static class Friends
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte ListSub = 1, OnlineSub = 2, OfflineSub = 3, AddSub = 6, RemoveSub = 7, MessageSub = 8;

        /// <summary>
        /// FriendMessage (sub 8, reader FUN_14348c3a0, handler FUN_143492e90): {u8 code, u64 guid, name block player,
        /// name block new name}; the client shows its own text for the code with the name as [*player*]: 0x0C
        /// "Friend request failed.  Player [player] does not exist." (UI.FriendRequestInvalidName). Adding yourself or
        /// someone already listed the client refuses itself before sending (FUN_142cb1460: FriendYourself,
        /// FriendAlreadyOnList).
        /// </summary>
        private const byte MessageInvalidName = 0x0C;
        /// <summary>Client text 560713 "[*player*] has been removed from your friends list." (key FriendRemoved).</summary>
        private const uint FriendRemovedText = 560713;
        private const int MaxFriends = 200;

        /// <summary>World entry: the player's list, and an "online" note to everyone who lists them.</summary>
        public static void Enter(WorldSession session)
        {
            var friends = DatabaseManager.Instance.CharacterDatabase.GetFriends(session.PlayerGuid);
            var online = OnlineByGuid();
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(ListSub);
            writer.Write((uint)friends.Count);
            foreach (CharacterModel friend in friends)
                WriteFriend(writer, friend.Id, friend.Name, online.ContainsKey(friend.Id), friend.LastOnline);
            session.EnqueueMessage(ZoneMessageOpcode.FriendBase, output.ToArray());

            byte[] note = Build(w => { w.Write(OnlineSub); w.Write(session.CharacterName); w.Write(session.PlayerGuid); w.Write(true); });
            foreach (WorldSession watcher in Watchers(session.PlayerGuid, online))
                watcher.EnqueueMessage(ZoneMessageOpcode.FriendBase, note);
        }

        /// <summary>World exit: an "offline" note to everyone who lists the player.</summary>
        public static void Leave(WorldSession session)
        {
            byte[] note = Build(w => { w.Write(OfflineSub); w.Write(session.PlayerGuid); });
            foreach (WorldSession watcher in Watchers(session.PlayerGuid, OnlineByGuid()))
                watcher.EnqueueMessage(ZoneMessageOpcode.FriendBase, note);
        }

        /// <summary>
        /// AddFriendRequest (Command 0x10 {string name, u64 guid}): "/friend add Name" sends a name, the player menu sends a
        /// guid. Adds the friend (one-way), saves it and sends FriendBase sub 6 Add; the client prints the confirmation itself.
        /// </summary>
        public static void HandleAdd(WorldSession session, byte[] body)
        {
            var reader = new GamePacketReader(body);
            string name = reader.ReadString()?.Trim() ?? "";
            ulong guid = reader.BytesRemaining >= 8 ? reader.ReadULong() : 0;
            var database = DatabaseManager.Instance.CharacterDatabase;
            CharacterModel target = name.Length > 0 ? database.FindCharacterByName(name)
                : guid != 0 ? database.GetCharacterById(guid).GetAwaiter().GetResult() : null;
            if (target?.DeleteTime != null)
                target = null;
            if (target == null)
            {
                session.EnqueueMessage(ZoneMessageOpcode.FriendBase, Build(w =>
                {
                    w.Write(MessageSub);
                    w.Write(MessageInvalidName);
                    w.Write(0ul);
                    WriteName(w, name);
                    WriteName(w, "");
                }));
                log.Info($"{session.CharacterName} tried to add a friend named \"{name}\": no such player.");
                return;
            }
            if (target.Id == session.PlayerGuid)
            {
                SystemMessages.Show(session, 560744);   // FriendYourself: "You already think of yourself as a friend."
                return;
            }
            var friends = database.GetFriends(session.PlayerGuid);
            if (friends.Any(f => f.Id == target.Id))
            {
                SystemMessages.Show(session, 560710, ("player", target.Name));   // FriendAlreadyOnList
                return;
            }
            if (friends.Count >= MaxFriends)
            {
                Chat.System(session, "Your friends list is full.");
                return;
            }

            ulong characterId = session.PlayerGuid, friendId = target.Id;
            database.Save(c => c.CharacterFriend.Add(new CharacterFriendModel { CharacterId = characterId, FriendId = friendId }));

            var online = OnlineByGuid();
            session.EnqueueMessage(ZoneMessageOpcode.FriendBase,
                Build(w => { w.Write(AddSub); WriteFriend(w, target.Id, target.Name, online.ContainsKey(target.Id), target.LastOnline); }));
            if (online.TryGetValue(target.Id, out WorldSession other))
                Chat.System(other, $"{session.CharacterName} added you as a friend.");
            log.Info($"{session.CharacterName} added {target.Name} as a friend.");
        }

        /// <summary>RemoveFriendRequest body: the friend's name {3 × u32, string first, string last} (client FUN_14351df90).</summary>
        public static void HandleRemove(WorldSession session, byte[] body)
        {
            var database = DatabaseManager.Instance.CharacterDatabase;
            var friends = database.GetFriends(session.PlayerGuid);
            CharacterModel friend = null;
            try
            {
                var reader = new GamePacketReader(body);
                reader.ReadUInt();
                reader.ReadUInt();
                reader.ReadUInt();
                string name = reader.ReadString()?.Trim();
                friend = friends.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
            }
            if (friend == null)
            {
                log.Debug($"{session.CharacterName} removed an unknown friend: {BitConverter.ToString(body)}");
                return;
            }

            ulong characterId = session.PlayerGuid, friendId = friend.Id;
            database.Save(c => c.CharacterFriend.Remove(new CharacterFriendModel { CharacterId = characterId, FriendId = friendId }));
            // The client drops the row (Character.FriendsList) and means to say "[X] has been removed from your friends
            // list." (FUN_143493b70 → FUN_143353000 with key "UI.FriendRemoved"), but this client's CodeStringMappings.txt
            // (the only source of its key lookup, FUN_142c78c20) has that text as "FriendRemoved", so the lookup finds
            // nothing and the line never shows. The server shows the same text by id through the client's
            // system-message path (PacketShowSystemMessage, text 560713, [*player*]).
            session.EnqueueMessage(ZoneMessageOpcode.FriendBase, Build(w => { w.Write(RemoveSub); w.Write(friendId); }));
            SystemMessages.Show(session, FriendRemovedText, ("player", friend.Name));
            log.Info($"{session.CharacterName} removed {friend.Name} as a friend.");
        }

        /// <summary>
        /// Online players by character guid.
        /// </summary>
        private static Dictionary<ulong, WorldSession> OnlineByGuid()
        {
            var online = new Dictionary<ulong, WorldSession>();
            foreach (WorldSession s in PlayerManager.Snapshot())
                online[s.PlayerGuid] = s;
            return online;
        }

        /// <summary>
        /// Online players who have this character on their friends list (one database index lookup).
        /// </summary>
        private static IEnumerable<WorldSession> Watchers(ulong guid, Dictionary<ulong, WorldSession> online)
        {
            foreach (ulong watcher in DatabaseManager.Instance.CharacterDatabase.GetFriendOf(guid))
                if (watcher != guid && online.TryGetValue(watcher, out WorldSession s))
                    yield return s;
        }

        /// <summary>
        /// Writes a FriendData record: name block, guid, online flag, then the last-online time (offline) or the presence
        /// fields (online), and a trailing string.
        /// </summary>
        private static void WriteFriend(GamePacketWriter writer, ulong guid, string name, bool online, DateTime? lastOnline)
        {
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(name ?? "");
            writer.Write("");
            writer.Write(guid);
            writer.Write(online);
            if (!online)
                writer.Write(lastOnline.HasValue ? (ulong)new DateTimeOffset(DateTime.SpecifyKind(lastOnline.Value, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0ul);
            else
            {
                for (int i = 0; i < 4; i++)
                    writer.Write(0u);
                writer.Write(true);
                writer.Write(0u);
                writer.Write(false);
                for (int i = 0; i < 5; i++)
                    writer.Write(0u);
            }
            writer.Write("");
        }

        /// <summary>A name block {3 x u32, string first, string last}.</summary>
        private static void WriteName(GamePacketWriter writer, string first)
        {
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(first ?? "");
            writer.Write("");
        }

        /// <summary>
        /// Collects what a writer callback writes into a byte array.
        /// </summary>
        private static byte[] Build(Action<GamePacketWriter> write)
        {
            var output = new List<byte>();
            write(new GamePacketWriter(output));
            return output.ToArray();
        }
    }
}
