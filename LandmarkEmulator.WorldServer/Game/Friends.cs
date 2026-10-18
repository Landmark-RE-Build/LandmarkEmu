using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal.Social;
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
    /// Friends lists (stored per direction in character_friend; an accepted request fills both lists, <see cref="Mutual"/>).
    /// FriendBase 0x4D, u8 sub (client dispatcher in
    /// ClientFriendsDataManager):
    /// - 1 List {u32 count, FriendData × count} (FUN_14348cf60), sent once on world entry.
    /// - 2 Online {string name, u64 guid, bool} / 3 Offline {u64 guid}: sent only to players who list the one who
    ///   came or went.
    /// - 6 Add {FriendData} / 7 Remove {u64 guid}.
    /// FriendData (FUN_14348c040): name {3 × u32, string first, string last}, u64 guid, bool online; offline: u64
    /// last online (unix seconds); online: 4 × u32, bool (true skips 3 × u32), u32, bool, 5 × u32; then a string.
    /// Requests are Command 0x1E subs: 0x10 AddFriendRequest {string name, u64 guid} (by name from "/friend add Name",
    /// or empty name + guid from a player's menu), 0x12 RemoveFriendRequest {identity, u64 guid} (by name, or by guid from the menu), and the confirmation round trip
    /// 0x13 ConfirmFriendRequest (to the one asked) / 0x14 ConfirmFriendResponse (their answer), see
    /// <see cref="HandleAdd"/>.
    /// Part of the social service: callers outside it use <see cref="Social.SocialServices"/> (LocalSocialService runs
    /// this code; social-service.md §6 step 1).
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
        /// <summary>
        /// The other FriendMessage codes of a request (0.695 handler FUN_143542b80): 1 UI.FriendRequestAccepted "[*player*]
        /// has accepted your friend request." (9500), 2 UI.FriendRequestDeclined (9501), 3 UI.FriendRequestTimedOut "Your
        /// friend request for [*player*] has timed out." (9502), 5 UI.FriendRequested "Friend request sent to [*player*]."
        /// (9503), 8 NoFriendSpam.
        /// </summary>
        public const byte MessageAccepted = 1, MessageDeclined = 2, MessageTimedOut = 3, MessageRequested = 5, MessageNoSpam = 8;
        /// <summary>Command subs of the confirmation (u16 after CommandBase).</summary>
        public const ushort ConfirmRequestSub = 0x13, ConfirmResponseSub = 0x14;
        /// <summary>How long a request waits for its answer (ours, as group invites: the client has no timer of its own).</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);
        /// <summary>Requests waiting for an answer: (asker, asked) → when they lapse.</summary>
        private static readonly Dictionary<(ulong From, ulong To), DateTime> pending = new();
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
        /// guid. The one asked has to agree (TASKS 5.4): live sent them CommandPacketConfirmFriendRequest (0x13), which the
        /// 0.695 client shows as a GAQ "Friend Invite" popup (handler case 0x13 → FUN_1433da6a0, GaqItemFriendInvite; its
        /// Accept / Refuse send 0x14 with 1 / 0, FUN_1433da1c0 / FUN_1433da270; with the player's own "Automatically Refuse
        /// Friend Requests" option, /autofriendsoff, the client refuses by itself). The asker gets FriendMessage 5 "Friend
        /// request sent to [player]."; the answer arrives in <see cref="HandleResponse"/>. Who is asked while offline:
        /// FriendMessage 3 "timed out" at once [I: the popup needs a client to show it]. Admin force-add adds without asking:
        /// <see cref="HandleAdminForce"/>.
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

            var online = OnlineByGuid();
            if (!online.TryGetValue(target.Id, out WorldSession asked))
            {
                session.EnqueueMessage(ZoneMessageOpcode.FriendBase, Message(MessageTimedOut, target.Id, target.Name));
                log.Info($"{session.CharacterName} asked {target.Name} to be friends: offline, timed out at once.");
                return;
            }
            var key = (session.PlayerGuid, target.Id);
            lock (pending)
            {
                if (pending.TryGetValue(key, out DateTime lapses) && lapses > DateTime.UtcNow)
                {
                    session.EnqueueMessage(ZoneMessageOpcode.FriendBase, Message(MessageNoSpam, target.Id, target.Name));
                    return;
                }
                pending[key] = DateTime.UtcNow + RequestTimeout;
            }
            asked.EnqueueMessage(ZoneMessageOpcode.CommandBase, ConfirmRequest(session.PlayerGuid, session.CharacterName));
            session.EnqueueMessage(ZoneMessageOpcode.FriendBase, Message(MessageRequested, target.Id, target.Name));
            log.Info($"{session.CharacterName} asked {target.Name} to be friends.");
            ulong askerId = session.PlayerGuid;
            _ = System.Threading.Tasks.Task.Delay(RequestTimeout).ContinueWith(_ =>
            {
                lock (pending)
                {
                    if (!pending.TryGetValue(key, out DateTime lapses) || lapses > DateTime.UtcNow)
                        return;
                    pending.Remove(key);
                }
                if (OnlineByGuid().TryGetValue(askerId, out WorldSession asker))
                    asker.EnqueueMessage(ZoneMessageOpcode.FriendBase, Message(MessageTimedOut, target.Id, target.Name));
            });
        }

        /// <summary>
        /// CommandPacketConfirmFriendRequest body after CommandBase: u16 sub 0x13, u64 asker, name block asker {3 × u32,
        /// string first, string last}, name block (unused), bool (0.695 reader FUN_1433823c0, name blocks FUN_142c25b90; the
        /// handler uses the guid and the first name block). The client drops the packet unless every byte is read.
        /// </summary>
        public static byte[] ConfirmRequest(ulong asker, string askerName) => Build(w =>
        {
            w.WriteUShort(ConfirmRequestSub);
            w.Write(asker);
            WriteName(w, askerName);
            WriteName(w, "");
            w.Write(false);
        });

        /// <summary>
        /// ConfirmFriendResponse (Command 0x14, from the one asked; packet ctor FUN_1433d7500: u64 asker (+0x20), u32 accept
        /// (+0x28; 1 Accept / 0 Refuse), the asker's name block (+0x30)). Accepted: the asker's list gets them (FriendAdd) and
        /// FriendMessage 1, and, mutual (<see cref="Mutual"/>, live), their list gets the asker (FriendAdd with the asker's
        /// online state; the client shows "[asker] has been added as a friend."); caps and ignores as
        /// Internal/Social/FriendAccept.cs. Refused: FriendMessage 2. An answer to no waiting request (lapsed) is ignored.
        /// </summary>
        public static void HandleResponse(WorldSession session, byte[] body)
        {
            if (!TryReadResponse(body, out ulong askerId, out bool accepted))
            {
                log.Warn($"{session.CharacterName}: unreadable ConfirmFriendResponse {BitConverter.ToString(body ?? Array.Empty<byte>())}");
                return;
            }
            log.Debug($"{session.CharacterName} ConfirmFriendResponse {BitConverter.ToString(body)}");
            lock (pending)
                if (!pending.Remove((askerId, session.PlayerGuid)))
                {
                    log.Info($"{session.CharacterName} answered a friend request from {askerId:X} that no longer waits.");
                    return;
                }
            OnlineByGuid().TryGetValue(askerId, out WorldSession asker);
            if (!accepted)
            {
                asker?.EnqueueMessage(ZoneMessageOpcode.FriendBase, Message(MessageDeclined, session.PlayerGuid, session.CharacterName));
                log.Info($"{session.CharacterName} declined {askerId:X}'s friend request.");
                return;
            }
            bool mutual = Mutual;
            ulong accepterId = session.PlayerGuid;
            var database = DatabaseManager.Instance.CharacterDatabase;
            FriendAcceptPlan plan;
            CharacterModel askerRow;
            try
            {
                askerRow = database.GetCharacterById(askerId).GetAwaiter().GetResult();
                bool askerIgnores, accepterIgnores;
                using (var context = database.NewContext())
                {
                    askerIgnores = context.CharacterIgnore.Any(i => i.CharacterId == askerId && i.IgnoredId == accepterId);
                    accepterIgnores = context.CharacterIgnore.Any(i => i.CharacterId == accepterId && i.IgnoredId == askerId);
                }
                plan = FriendAccept.Decide(mutual, askerId, accepterId, database.GetFriends(askerId).Select(f => f.Id).ToList(),
                    database.GetFriends(accepterId).Select(f => f.Id).ToList(), askerIgnores, accepterIgnores);
                if (plan.Blocked)
                {
                    log.Info($"{session.CharacterName} accepted {askerId:X}'s friend request, but one of them ignores the other: nothing added.");
                    return;
                }
                if (plan.AddToAccepter && askerRow == null)
                    plan = plan with { AddToAccepter = false };
                // Both rows in one save: the asker lists the one who accepted; mutual (live), they list the asker too.
                if (plan.Changes)
                    database.Save(c =>
                    {
                        if (plan.AddToAsker)
                            c.CharacterFriend.Add(new CharacterFriendModel { CharacterId = askerId, FriendId = accepterId });
                        if (plan.AddToAccepter)
                            c.CharacterFriend.Add(new CharacterFriendModel { CharacterId = accepterId, FriendId = askerId });
                    }).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                log.Error(e, $"{session.CharacterName} accepted {askerId:X}'s friend request: reading or saving the friends lists failed.");
                return;
            }
            if (plan.AddToAsker)
                asker?.EnqueueMessage(ZoneMessageOpcode.FriendBase,
                    Build(w => { w.Write(AddSub); WriteFriend(w, accepterId, session.CharacterName, true, null); }));
            if (plan.AddToAccepter)
            {
                lock (pending)
                    pending.Remove((accepterId, askerId));
                session.EnqueueMessage(ZoneMessageOpcode.FriendBase,
                    Build(w => { w.Write(AddSub); WriteFriend(w, askerId, asker?.CharacterName ?? askerRow.Name, asker != null, askerRow.LastOnline); }));
            }
            if (plan.AskerFull && asker != null)
                Chat.System(asker, "Your friends list is full.");
            if (plan.AccepterFull)
                Chat.System(session, "Your friends list is full.");
            if (plan.Changes)
                asker?.EnqueueMessage(ZoneMessageOpcode.FriendBase, Message(MessageAccepted, accepterId, session.CharacterName));
            log.Info($"{session.CharacterName} accepted {askerId:X}'s friend request ({(mutual ? "mutual" : "one-way")}: "
                + $"to the asker's list {(plan.AddToAsker ? "added" : plan.AskerFull ? "full" : "already there")}"
                + (mutual ? $", the asker to theirs {(plan.AddToAccepter ? "added" : plan.AccepterFull ? "full" : "already there")}" : "") + ").");
        }

        /// <summary>
        /// An accepted request makes the friendship mutual (setting FriendRequestsMutual, FRIEND_REQUESTS_MUTUAL, default
        /// true = live; read at each answer); false = one-way lists. Rules in Internal/Social/FriendAccept.cs.
        /// </summary>
        public static bool Mutual => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.FriendRequestsMutual ?? true;

        /// <summary>
        /// The response body after the u16 sub: u64 asker, the accept flag, the asker's name block {3 × u32, string first,
        /// string last} (the ctor copies the popup's 0xB0-byte name object, read by FUN_142c25b90). The flag's wire width
        /// isn't in the traced ctor (a 4-byte field), so the layout that ends exactly at the body's end decides: u32, else u8.
        /// </summary>
        public static bool TryReadResponse(byte[] body, out ulong asker, out bool accepted)
        {
            asker = 0;
            accepted = false;
            if (body == null || body.Length < 9)
                return false;
            asker = BitConverter.ToUInt64(body, 0);
            foreach (int width in new[] { 4, 1 })
            {
                long o = 8 + width + 12;
                for (int s = 0; s < 2 && o + 4 <= body.Length; s++)
                    o += 4 + BitConverter.ToUInt32(body, (int)o);
                if (o == body.Length)
                {
                    accepted = width == 4 ? BitConverter.ToUInt32(body, 8) != 0 : body[8] != 0;
                    return true;
                }
            }
            accepted = body[8] != 0;
            return true;
        }

        /// <summary>FriendMessage (sub 8) {u8 code, u64 guid, name block player, name block new name}.</summary>
        private static byte[] Message(byte code, ulong guid, string player) => Build(w =>
        {
            w.Write(MessageSub);
            w.Write(code);
            w.Write(guid);
            WriteName(w, player);
            WriteName(w, "");
        });

        /// <summary>
        /// RemoveFriendRequest body: identity {3 × u32, string first, string last}, u64 guid — the name from the friends
        /// window / "/friend remove" (Ui.RemoveFriend), the guid from the player right-click menu (Ui.RemoveFriendByGuid);
        /// Internal/Social/FriendRemoveRequest.cs. One-sided: only the remover's list loses the row [I].
        /// </summary>
        public static void HandleRemove(WorldSession session, byte[] body)
        {
            if (!FriendRemoveRequest.TryRead(body, out FriendRemoveRequest remove))
            {
                log.Debug($"{session.CharacterName} removed an unknown friend: {BitConverter.ToString(body ?? Array.Empty<byte>())}");
                return;
            }
            var database = DatabaseManager.Instance.CharacterDatabase;
            CharacterModel friend = remove.Find(database.GetFriends(session.PlayerGuid), f => f.Id, f => f.Name);
            if (friend == null)
            {
                log.Debug($"{session.CharacterName} removed \"{remove.Name}\" ({remove.Guid:X}), who is not on the friends list.");
                return;
            }

            ulong characterId = session.PlayerGuid, friendId = friend.Id;
            database.Save(c => c.CharacterFriend.Remove(new CharacterFriendModel { CharacterId = characterId, FriendId = friendId }));
            SendRemoved(session, friend);
            log.Info($"{session.CharacterName} removed {friend.Name} as a friend.");
        }

        /// <summary>FriendBase sub 7 Remove and text 560713 to the list's owner.</summary>
        private static void SendRemoved(WorldSession session, CharacterModel friend)
        {
            ulong friendId = friend.Id;
            // The client drops the row (Character.FriendsList) and means to say "[X] has been removed from your friends
            // list." (FUN_143493b70 → FUN_143353000 with key "UI.FriendRemoved"), but this client's CodeStringMappings.txt
            // (the only source of its key lookup, FUN_142c78c20) has that text as "FriendRemoved", so the lookup finds
            // nothing and the line never shows. The server shows the same text by id through the client's
            // system-message path (PacketShowSystemMessage, text 560713, [*player*]).
            session.EnqueueMessage(ZoneMessageOpcode.FriendBase, Build(w => { w.Write(RemoveSub); w.Write(friendId); }));
            SystemMessages.Show(session, FriendRemovedText, ("player", friend.Name));
        }

        /// <summary>
        /// Admin /friend forceadd | remove &lt;target&gt; [source] in-process (0x1E/0x4CF / 0x4D0; rules and layout in
        /// Internal/Social/AdminFriends.cs, the same the social service applies): the source is any living character,
        /// online or not (empty = the admin). Force-add saves the row and, with the source online, sends them FriendAdd
        /// (sub 6) with the friend's online state; force-remove deletes the row and sends the source sub 7 + text 560713,
        /// as <see cref="HandleRemove"/>. Online / offline notes need nothing more here: <see cref="Watchers"/> reads
        /// character_friend each time. The outcome goes to <see cref="AdminCommands.ReportFriendResult"/> only once the
        /// database has the change.
        /// </summary>
        public static void HandleAdminForce(WorldSession admin, ushort sub, byte[] body)
        {
            if (!AdminFriends.TryRead(body, out AdminFriends.Request command))
            {
                AdminCommands.ReportFriendResult(admin, sub, AdminFriendOutcome.Unreadable, "", admin.CharacterName, true);
                return;
            }
            var database = DatabaseManager.Instance.CharacterDatabase;
            string sourceName = command.Source.Length > 0 ? command.Source : admin.CharacterName ?? "";
            string targetName = command.Target;
            CharacterModel source = null;
            AdminFriendOutcome outcome;
            try
            {
                source = sourceName.Length > 0 ? database.FindCharacterByName(sourceName) : null;
                if (source != null)
                    sourceName = source.Name;
                List<CharacterModel> friends = source != null ? database.GetFriends(source.Id) : new List<CharacterModel>();
                var online = OnlineByGuid();
                WorldSession owner = source != null && online.TryGetValue(source.Id, out WorldSession s) ? s : null;
                if (sub == AdminFriends.ForceAddSub)
                {
                    CharacterModel target = targetName.Length > 0 ? database.FindCharacterByName(targetName) : null;
                    if (target != null)
                        targetName = target.Name;
                    outcome = AdminFriends.DecideAdd(source?.Id, target?.Id, friends.Select(f => f.Id).ToList());
                    if (outcome == AdminFriendOutcome.Added)
                    {
                        ulong characterId = source.Id, friendId = target.Id;
                        database.Save(c => c.CharacterFriend.Add(new CharacterFriendModel { CharacterId = characterId, FriendId = friendId }))
                            .GetAwaiter().GetResult();
                        lock (pending)
                            pending.Remove((characterId, friendId));
                        owner?.EnqueueMessage(ZoneMessageOpcode.FriendBase, Build(w =>
                        {
                            w.Write(AddSub);
                            WriteFriend(w, target.Id, target.Name, online.ContainsKey(target.Id), target.LastOnline);
                        }));
                    }
                }
                else
                {
                    outcome = AdminFriends.DecideRemove(source?.Id, targetName, friends, f => f.Name, out CharacterModel friend);
                    if (outcome == AdminFriendOutcome.Removed)
                    {
                        targetName = friend.Name;
                        ulong characterId = source.Id, friendId = friend.Id;
                        database.Save(c => c.CharacterFriend.Remove(new CharacterFriendModel { CharacterId = characterId, FriendId = friendId }))
                            .GetAwaiter().GetResult();
                        if (owner != null)
                            SendRemoved(owner, friend);
                    }
                }
            }
            catch (Exception e)
            {
                log.Error(e, $"Admin {admin.CharacterName} /friend {AdminFriends.Verb(sub)} {targetName} [{sourceName}]: reading or saving the friends list failed.");
                outcome = AdminFriendOutcome.Failed;
            }
            bool sourceIsAdmin = source != null ? source.Id == admin.PlayerGuid
                : string.Equals(sourceName, admin.CharacterName, StringComparison.OrdinalIgnoreCase);
            AdminCommands.ReportFriendResult(admin, sub, outcome, targetName, sourceName, sourceIsAdmin);
        }

        /// <summary>
        /// A character was deleted (TASKS 6.42; P4624, 13 Apr 2016: deleted characters leave friend and guild lists):
        /// its rows go from character_friend in both directions, and owners online drop it
        /// from their list (sub 7 Remove, as a manual removal, without the "removed" line). Idempotent.
        /// </summary>
        public static void CharacterDeleted(ulong characterId)
        {
            var database = DatabaseManager.Instance.CharacterDatabase;
            List<ulong> owners = database.GetFriendOf(characterId);
            database.Save(c => c.CharacterFriend.RemoveRange(c.CharacterFriend.Where(f => f.CharacterId == characterId || f.FriendId == characterId)))
                .ContinueWith(t => log.Error(t.Exception, $"Failed to drop deleted character {characterId:X} from friends lists."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            var online = OnlineByGuid();
            byte[] remove = Build(w => { w.Write(RemoveSub); w.Write(characterId); });
            foreach (ulong owner in owners)
                if (online.TryGetValue(owner, out WorldSession session))
                    session.EnqueueMessage(ZoneMessageOpcode.FriendBase, remove);
            if (owners.Count > 0)
                log.Info($"Deleted character {characterId:X} removed from {owners.Count} friends list(s).");
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
