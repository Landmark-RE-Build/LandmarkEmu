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
    /// Player chat rooms (/join room [password], /leave room; wiki Chat: "/join creates or joins a channel and opens a new
    /// tab"). ChatBase 0x13 with a u16 sub, on the World channel:
    /// - C→S 0x0A JoinRoomRequest {string room, string password} (FUN_1432ba400; typed through the command runner the
    ///   password can arrive inside the room string, "room password"); 0x0C LeaveRoomRequest {string room, bool}.
    /// - S→C 0x0B JoinRoomResponse / 0x0D LeaveRoomResponse {string room, u32 result} (readers FUN_1432aa140 / 210):
    ///   1 = Success; errors are the client's PlayerPublicChat table (FUN_143ab0000 / 143aaffe0 / 143ab0020: 8 already in
    ///   room, 12 invalid password, 15 in too many rooms, 19 room name inappropriate, 22 room no longer exists, 23 not in
    ///   room ...), shown by FUN_1432c0770 with its text.
    /// - S→C 0x09 RoomInfoNotification {string room, u32 n, n × ChatUser {u64 guid, name block}, u32 kind} (reader
    ///   FUN_1432aa2e0, room manager FUN_14357d8d0): kind 1 = you entered (the full user list; the client opens the room's
    ///   tab), 2 = users joined, 5 = a user left (the player themselves: the room closes), 7 = room destroyed,
    ///   8 = ownership granted.
    /// Messages to a room are PacketChat channel 10 with the room's name as the last trailing string (writer
    /// FUN_1432a93e0); <see cref="Chat"/> relays them to the room's members only.
    /// </summary>
    public static class ChatRooms
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const ushort JoinRequest = 0x0A, JoinResponse = 0x0B, LeaveRequest = 0x0C, LeaveResponse = 0x0D, RoomInfo = 0x09;
        /// <summary>The chat channel of player rooms.</summary>
        public const ushort RoomChannel = 10;
        private const uint Success = 1, AlreadyInRoom = 8, InvalidPassword = 12, InTooManyRooms = 15, RoomNameTooLong = 17,
            PasswordTooLong = 18, RoomNameInappropriate = 19, LeaveRoomNotFound = 22, LeaveNotInRoom = 23;
        private const uint KindEntered = 1, KindUsersJoined = 2, KindUserLeft = 5;
        /// <summary>
        /// Client constants Chat.PlayerPublicMaxChatRooms 10, Chat.MaxRoomNameCharLength 32 (FUN_143aafcf0),
        /// Chat.MaxRoomPasswordCharLength 32 (FUN_143aafd90).
        /// </summary>
        private const int MaxRoomsPerPlayer = 10, MaxNameLength = 32, MaxPasswordLength = 32;

        private sealed class Room
        {
            public string Name;
            public string Password;
            public ulong Owner;
            public readonly List<WorldSession> Members = new();
        }

        private static readonly object sync = new();
        private static readonly Dictionary<string, Room> rooms = new(StringComparer.OrdinalIgnoreCase);

        public static void Handle(WorldSession session, ushort sub, byte[] body)
        {
            var reader = new GamePacketReader(body);
            try
            {
                if (sub == JoinRequest)
                {
                    string name = reader.ReadString().Trim();
                    string password = reader.BytesRemaining >= 4 ? reader.ReadString().Trim() : "";
                    int space = name.IndexOf(' ');
                    if (password.Length == 0 && space > 0)
                        (name, password) = (name[..space], name[(space + 1)..].Trim());
                    Join(session, name, password);
                }
                else if (sub == LeaveRequest)
                    Leave(session, reader.ReadString().Trim());
                else
                {
                    Network.OpcodeStats.Unanswered("ChatRooms", sub);
                    log.Debug($"Unhandled chat room request {sub} from {session.CharacterName}.");
                }
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable chat room request {sub} from {session.CharacterName}: {e.Message}");
            }
        }

        private static void Join(WorldSession session, string name, string password)
        {
            uint result;
            Room room = null;
            List<WorldSession> others = null;
            lock (sync)
            {
                rooms.TryGetValue(name, out room);
                result = name.Length == 0 || name.Length > MaxNameLength ? RoomNameTooLong
                    : password.Length > MaxPasswordLength ? PasswordTooLong
                    : IsBlacklisted(name) ? RoomNameInappropriate
                    : room != null && room.Members.Contains(session) ? AlreadyInRoom
                    : rooms.Values.Count(r => r.Members.Contains(session)) >= MaxRoomsPerPlayer ? InTooManyRooms
                    : room != null && room.Password.Length > 0 && room.Password != password ? InvalidPassword
                    : Success;
                if (result == Success)
                {
                    if (room == null)
                        rooms[name] = room = new Room { Name = name, Password = password, Owner = session.PlayerGuid };
                    others = room.Members.ToList();
                    room.Members.Add(session);
                }
            }
            SendResult(session, JoinResponse, room?.Name ?? name, result);
            if (result != Success)
                return;
            List<WorldSession> everyone;
            lock (sync)
                everyone = room.Members.ToList();
            session.EnqueueMessage(ZoneMessageOpcode.ChatBase, BuildInfo(room.Name, everyone, KindEntered));
            byte[] joined = BuildInfo(room.Name, new[] { session }, KindUsersJoined);
            foreach (WorldSession other in others)
                other.EnqueueMessage(ZoneMessageOpcode.ChatBase, joined);
            log.Info($"{session.CharacterName} joined chat room '{room.Name}' ({everyone.Count} member(s)).");
        }

        private static void Leave(WorldSession session, string name)
        {
            Room room;
            List<WorldSession> members = null;
            uint result;
            lock (sync)
            {
                result = !rooms.TryGetValue(name, out room) ? LeaveRoomNotFound
                    : !room.Members.Contains(session) ? LeaveNotInRoom : Success;
                if (result == Success)
                {
                    members = room.Members.ToList();
                    room.Members.Remove(session);
                    if (room.Members.Count == 0)
                        rooms.Remove(room.Name);
                }
            }
            SendResult(session, LeaveResponse, room?.Name ?? name, result);
            if (result != Success)
                return;
            // The leaver's client closes the room on the response itself (a "user left" about themselves then finds no
            // room: "Room no longer exists"); the others are told.
            byte[] left = BuildInfo(room.Name, new[] { session }, KindUserLeft);
            foreach (WorldSession member in members.Where(m => m != session))
                member.EnqueueMessage(ZoneMessageOpcode.ChatBase, left);
            log.Info($"{session.CharacterName} left chat room '{room.Name}'.");
        }

        /// <summary>A player left the world: out of every room (the others see them leave).</summary>
        public static void Forget(WorldSession session)
        {
            List<string> names;
            lock (sync)
                names = rooms.Values.Where(r => r.Members.Contains(session)).Select(r => r.Name).ToList();
            foreach (string name in names)
            {
                List<WorldSession> remaining;
                lock (sync)
                {
                    if (!rooms.TryGetValue(name, out Room room))
                        continue;
                    room.Members.Remove(session);
                    remaining = room.Members.ToList();
                    if (remaining.Count == 0)
                        rooms.Remove(name);
                }
                byte[] left = BuildInfo(name, new[] { session }, KindUserLeft);
                foreach (WorldSession member in remaining)
                    member.EnqueueMessage(ZoneMessageOpcode.ChatBase, left);
            }
        }

        /// <summary>The members of a room the sender belongs to (empty when the room doesn't exist or they aren't in it).</summary>
        public static List<WorldSession> MembersFor(WorldSession sender, string name)
        {
            lock (sync)
                return rooms.TryGetValue(name ?? "", out Room room) && room.Members.Contains(sender) ? room.Members.ToList()
                    : new List<WorldSession>();
        }

        private static void SendResult(WorldSession session, ushort sub, string name, uint result)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(sub);
            w.Write(name);
            w.Write(result);
            session.EnqueueMessage(ZoneMessageOpcode.ChatBase, output.ToArray());
        }

        private static byte[] BuildInfo(string name, IEnumerable<WorldSession> users, uint kind)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(RoomInfo);
            w.Write(name);
            var list = users.ToList();
            w.Write((uint)list.Count);
            foreach (WorldSession u in list)
            {
                w.Write(u.PlayerGuid);
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(u.CharacterName ?? "");
                w.Write("");
            }
            w.Write(kind);
            return output.ToArray();
        }

        private static bool IsBlacklisted(string text) =>
            LandmarkEmulator.Shared.GameTable.GameTableManager.Instance.BlackList?.Entries.Any(e =>
                e.RequiresExactMatch ? string.Equals(text, e.Word, StringComparison.OrdinalIgnoreCase)
                    : text.Contains(e.Word, StringComparison.OrdinalIgnoreCase)) ?? false;
    }
}
