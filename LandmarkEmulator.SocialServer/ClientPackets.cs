using LandmarkEmulator.Shared.Network;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// The client packets the social service sends (through the world / gateway, TunnelPacketToExternalConnection),
    /// whole: the zone opcode's wire bytes, then the body, in the 0.695 client's numbering, sent as given (docs/protocol.md §1.6). Layouts are the ones the world server
    /// sent before the split (Game/Friends.cs, Game/Ignores.cs, Game/SystemMessages.cs), traced in the client:
    /// <list type="bullet">
    /// <item>FriendBase 0x4D (wire bytes 4D 00, u8 sub; ClientFriendsDataManager dispatcher FUN_143493360): 1 List
    /// {u32 n, FriendData × n} (FUN_14348cf60), 2 Online {string name, u64 guid, bool} (handler FUN_143493150), 3 Offline
    /// {u64} (FUN_143490ae0), 6 Add {FriendData}, 7 Remove {u64}, 8 FriendMessage {u8 code, u64, name block, name block}
    /// (FUN_14348c3a0).</item>
    /// <item>IgnoreBase 0x72 (wire bytes 72 00, u8 sub; dispatcher FUN_143494180): 1 List {u32 n × {u64 guid, string
    /// name}}, 2 Add {u64, string}, 3 Remove {u64}.</item>
    /// <item>ShowSystemMessage 0x69 (wire bytes 69 00; reader FUN_142b8b220): {u32 text id, string key, u32 n × {string
    /// name, string value, u32 value text id}, u32, u32}.</item>
    /// </list>
    /// </summary>
    public static class ClientPackets
    {
        private static readonly byte[] CommandBase = { 0x1E, 0x00 }, FriendBase = { 0x4D, 0x00 }, IgnoreBase = { 0x72, 0x00 }, ShowSystemMessage = { 0x69, 0x00 },
            MailBase = { 0xF8, 0x00 };

        public const byte FriendListSub = 1, FriendOnlineSub = 2, FriendOfflineSub = 3, FriendAddSub = 6, FriendRemoveSub = 7,
            FriendMessageSub = 8;
        public const byte IgnoreListSub = 1, IgnoreAddSub = 2, IgnoreRemoveSub = 3;

        /// <summary>FriendMessage code 0x0C: "Friend request failed.  Player [player] does not exist." (UI.FriendRequestInvalidName).</summary>
        public const byte FriendMessageInvalidName = 0x0C;
        /// <summary>FriendMessage codes of a request (0.695 FUN_143542b80): 1 accepted (9500), 2 declined (9501), 3 timed out
        /// (9502), 5 "Friend request sent to [*player*]." (9503), 8 NoFriendSpam.</summary>
        public const byte FriendMessageAccepted = 1, FriendMessageDeclined = 2, FriendMessageTimedOut = 3, FriendMessageRequested = 5,
            FriendMessageNoSpam = 8;

        /// <summary>CommandPacketConfirmFriendRequest (0x1E sub 0x13 {u64 asker, name block asker, name block, bool}; 0.695
        /// reader FUN_1433823c0, name blocks FUN_142c25b90 {3 × u32, string first, string last}): the client's "Friend
        /// Invite" popup. The client drops it unless every byte is read.</summary>
        public static byte[] ConfirmFriendRequest(ulong asker, string askerName) => Build(CommandBase, w =>
        {
            w.WriteUShort(0x13);
            w.Write(asker);
            foreach (string first in new[] { askerName ?? "", "" })
            {
                w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write(first);
                w.Write("");
            }
            w.Write(false);
        });

        /// <summary>Client texts: 558682 "[*message*]" (plain line), 560744 FriendYourself, 560710 FriendAlreadyOnList,
        /// 560713 FriendRemoved ("[*player*] has been removed from your friends list.").</summary>
        public const uint PlainMessageText = 558682, FriendYourselfText = 560744, FriendAlreadyOnListText = 560710,
            FriendRemovedText = 560713;

        /// <summary>A friend as the list shows it (FriendData).</summary>
        public readonly record struct Friend(ulong Guid, string Name, bool Online, DateTime? LastOnline);

        public static byte[] FriendList(IReadOnlyCollection<Friend> friends) => Build(FriendBase, w =>
        {
            w.Write(FriendListSub);
            w.Write((uint)friends.Count);
            foreach (Friend f in friends)
                WriteFriend(w, f);
        });

        public static byte[] FriendOnline(string name, ulong guid) => Build(FriendBase, w =>
        {
            w.Write(FriendOnlineSub);
            w.Write(name ?? "");
            w.Write(guid);
            w.Write(true);
        });

        public static byte[] FriendOffline(ulong guid) => Build(FriendBase, w => { w.Write(FriendOfflineSub); w.Write(guid); });

        public static byte[] FriendAdd(Friend friend) => Build(FriendBase, w => { w.Write(FriendAddSub); WriteFriend(w, friend); });

        public static byte[] FriendRemove(ulong guid) => Build(FriendBase, w => { w.Write(FriendRemoveSub); w.Write(guid); });

        public static byte[] FriendMessage(byte code, ulong guid, string player, string newName = "") => Build(FriendBase, w =>
        {
            w.Write(FriendMessageSub);
            w.Write(code);
            w.Write(guid);
            WriteName(w, player);
            WriteName(w, newName);
        });

        public static byte[] IgnoreList(IReadOnlyDictionary<ulong, string> list) => Build(IgnoreBase, w =>
        {
            w.Write(IgnoreListSub);
            w.Write((uint)list.Count);
            foreach ((ulong guid, string name) in list)
            {
                w.Write(guid);
                w.Write(name ?? "");
            }
        });

        public static byte[] IgnoreAdd(ulong guid, string name) => Build(IgnoreBase, w => { w.Write(IgnoreAddSub); w.Write(guid); w.Write(name ?? ""); });

        public static byte[] IgnoreRemove(ulong guid) => Build(IgnoreBase, w => { w.Write(IgnoreRemoveSub); w.Write(guid); });

        /// <summary>The client's own system line for text <paramref name="textId"/> with [*name*] parameters.</summary>
        public static byte[] SystemMessage(uint textId, params (string Name, string Value)[] parameters) => Build(ShowSystemMessage, w =>
        {
            w.Write(textId);
            w.Write("");
            w.Write((uint)parameters.Length);
            foreach ((string name, string value) in parameters)
            {
                w.Write(name ?? "");
                w.Write(value ?? "");
                w.Write(0u);
            }
            w.Write(0u);
            w.Write(0u);
        });

        /// <summary>A plain system line (text 558682 "[*message*]"), as the world's Chat.System.</summary>
        public static byte[] SystemLine(string message) => SystemMessage(PlainMessageText, ("message", message));

        /// <summary>
        /// FriendData (FUN_14348c040): name block, u64 guid, bool online; offline: u64 last online (unix seconds);
        /// online: 4 × u32, bool (true skips 3 × u32), u32, bool, 5 × u32 (presence / position fields, meaning L66: zero);
        /// then a string.
        /// </summary>
        private static void WriteFriend(GamePacketWriter w, Friend f)
        {
            WriteName(w, f.Name);
            w.Write(f.Guid);
            w.Write(f.Online);
            if (!f.Online)
                w.Write(f.LastOnline.HasValue
                    ? (ulong)new DateTimeOffset(DateTime.SpecifyKind(f.LastOnline.Value, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0ul);
            else
            {
                for (int i = 0; i < 4; i++)
                    w.Write(0u);
                w.Write(true);
                w.Write(0u);
                w.Write(false);
                for (int i = 0; i < 5; i++)
                    w.Write(0u);
            }
            w.Write("");
        }

        /// <summary>
        /// MailBase 0xF8 (wire bytes F8 00, u32 sub; 0.589 dispatcher FUN_1432c9770): the sub in the 0.695 client's
        /// numbering (MailService; sent as given, docs/protocol.md §1.6), then the body.
        /// </summary>
        public static byte[] Mail(uint sub, Action<GamePacketWriter> body) => Build(MailBase, w =>
        {
            w.Write(sub);
            body(w);
        });

        /// <summary>A name block {3 × u32, string first, string last}.</summary>
        internal static void WriteName(GamePacketWriter w, string first)
        {
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(first ?? "");
            w.Write("");
        }

        private static byte[] Build(byte[] opcode, Action<GamePacketWriter> body)
        {
            var output = new List<byte>(opcode);
            body(new GamePacketWriter(output));
            return output.ToArray();
        }
    }
}
