using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using LandmarkEmulator.WorldServer.Network.Message.Model.Shared;
using Microsoft.EntityFrameworkCore;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// In-game mail (Notifications → Open Inbox → Compose). MailBase 0xF8 with a u32 sub (client dispatcher
    /// FUN_1432c9770). Player-to-player mail with subject and body, kept per recipient (table mail), with up to four item
    /// attachments, coins, or cash on delivery (the recipient pays the asked coins to the sender before taking the
    /// items).
    /// Part of the social service: callers outside it use <see cref="Social.SocialServices"/> (LocalSocialService runs
    /// this code; social-service.md §6 step 1). With a remote social service the mailboxes live there
    /// (SocialServer/MailService.cs) and only the zone's part runs here, through <see cref="Social.RemoteMail"/>: the
    /// compose window's bag checks (<see cref="ReadCompose"/>, <see cref="CheckBag"/>, <see cref="Attach"/>), escrow, and
    /// the reading window's packet (<see cref="SendRead"/>).
    /// </summary>
    public static class Mail
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // MailBase subs, 0.695 numbers [P]: the 0.695 id table (re/out/packet_ids_695.tsv, cMailPacketId*) inserted
        // DeveloperMailPlayer at 4 and moved every 0.589 sub from 4 up by one (0.695 S→C dispatcher; packets-695.md
        // "Changed layouts" A3, coverage-695.md §0xF8). Client → server:
        internal const uint ComposeRequest = 1, InboxListRequest = 7, ReadMessageRequest = 9, DeleteMessageRequest = 0xB,
            ConsumeAttachmentRequest = 0x10, AttachItemRequest = 0x15;
        // Server → client:
        internal const uint ComposeResponse = 5, InboxListResponse = 8, ReadMessageResponse = 0xA, DeleteMessageResponse = 0xC,
            NewMailNotification = 0xF, ConsumeAttachmentResponse = 0x11, EnableWindow = 0x14, AttachItemResponse = 0x16;

        /// <summary>
        /// Result codes (client text table at 0x14521a980): 0 Success, 1 unable to find user, 5 attachment is invalid,
        /// 0xD user has too much mail in inbox, 0xE user has sent too many mail messages, 0x14 message not found.
        /// </summary>
        internal const uint Success = 0, UserNotFound = 1, ServiceUnavailable = 3, SelfWithAttachment = 2, AttachmentInvalid = 5,
            ItemGone = 7, NoTradeItem = 0xA, ChestWithItems = 0x12, IgnoredByTarget = 0xF, MultipleWithAttachment = 0xB, TooManyAttachments = 0xC, InboxFull = 0xD, SentTooMany = 0xE,
            MessageNotFound = 0x14, AttachmentNotFound = 0x1A, InsufficientFunds = 6, CurrencyFull = 0x1E,
            MustPayCodFirst = 0x1F, InsufficientFundsForCod = 0x20;

        /// <summary>Stored attachment "definitions" that aren't items: sent coins, and the coins asked on delivery.</summary>
        private const uint CoinsEntry = 0, CodEntry = uint.MaxValue;

        /// <summary>Attachment slots in the compose window.</summary>
        private const int MaxAttachments = 4;

        /// <summary>Most messages an inbox keeps; sending to a full inbox is refused (InboxFull).</summary>
        private const int MaxInbox = 100;
        /// <summary>Most recipients one message may have.</summary>
        private const int MaxRecipients = 5;
        /// <summary>Name blocks read from a compose's "To" list at most (the client writes one per typed name).</summary>
        private const uint MaxNameBlocks = 64;
        /// <summary>Sending limit: at most this many messages per sender in <see cref="SendWindow"/>.</summary>
        private const int MaxSends = 20;
        private static readonly TimeSpan SendWindow = TimeSpan.FromMinutes(10);
        /// <summary>The compose window's own limits (client constants Mail.MaxSubjectCharLength 128, MaxBodyCharLength 4096).</summary>
        private const int MaxSubject = 128, MaxBody = 4096;
        /// <summary>
        /// Client constant Mail.ExpireTimeMinutes (43200 = 30 days): the inbox shows the time left from the sent time
        /// (FUN_1435849b0). The server deletes expired messages that hold nothing; ones with items or coins still
        /// waiting stay until they are taken, so nothing is lost.
        /// </summary>
        private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(43200);

        private static readonly ConcurrentDictionary<ulong, Queue<DateTime>> recentSends = new();

        /// <summary>Writes a MailBase packet: the u32 sub-opcode, then the body.</summary>
        internal static void Send(WorldSession session, uint sub, Action<GamePacketWriter> write)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(sub);
            write(writer);
            session.EnqueueMessage(ZoneMessageOpcode.MailBase, output.ToArray());
        }

        /// <summary>
        /// PacketMailEnableWindow {u64 mailbox}: makes the client request its inbox and open the Inbox window (what a
        /// mailbox prop would send). Not sent on login: the client would open the inbox unseen and then ignore the
        /// Open Inbox button. Zone side: mailbox proximity stays with the zone (social-service.md §5.1), so the mailbox
        /// interaction calls this directly.
        /// </summary>
        public static void OpenFor(WorldSession session) => Send(session, EnableWindow, w => w.Write(session.PlayerGuid));

        /// <summary>Mail requests from the client.</summary>
        public static void Handle(WorldSession session, MailRequest request)
        {
            try
            {
                var reader = new GamePacketReader(request.Body);
                switch (request.SubOpcode)
                {
                    case InboxListRequest:
                        SendInbox(session);
                        break;
                    case ComposeRequest:
                        Compose(session, reader);
                        break;
                    case ReadMessageRequest:
                        MarkRead(session, reader.ReadULong());
                        break;
                    case DeleteMessageRequest:
                        Delete(session, reader.ReadULong());
                        break;
                    case AttachItemRequest:
                        Attach(session, request.Body);
                        break;
                    case ConsumeAttachmentRequest:
                        TakeAttachments(session, reader.ReadULong(), reader.ReadULong());
                        break;
                    default:
                        Network.OpcodeStats.Unanswered("Mail", request.SubOpcode);
                        log.Debug($"Unhandled mail request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                        break;
                }
            }
            catch (Exception e)
            {
                log.Warn($"Bad mail request 0x{request.SubOpcode:X2} from {session.CharacterName}: {e.Message}");
            }
        }

        /// <summary>
        /// InboxListResponse (sub 8; 0.589 reader FUN_1432c75d0, 0.695 FUN_143374400): u32 list type (0), u64 owner, u16 flags (bit 0: complete; the
        /// window opens on it), u32 n, n × MailData (FUN_1432c6f60): u64 id, u64, sender name {u32, u32, u32, string
        /// first, string last}, string subject, string body, u64 sent (Unix s; the window shows days until it
        /// expires), u32 read, u16 attachment count, u32 kind flags (0 = player mail; 1/2/4/8/0xF0/0x1000 are system
        /// kinds). Newest first.
        /// </summary>
        private static void SendInbox(WorldSession session)
        {
            List<MailModel> inbox;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                DateTime cutoff = DateTime.UtcNow - Expiry;
                var expired = context.Mail.Where(m => m.RecipientGuid == session.PlayerGuid && m.SentAt < cutoff).ToList()
                    .Where(m => !ParseAttachments(m.Attachments).Any(a => a.Count > 0)).ToList();
                if (expired.Count > 0)
                {
                    context.Mail.RemoveRange(expired);
                    context.SaveChanges();
                    log.Info($"Deleted {expired.Count} expired message(s) from {session.CharacterName}'s inbox.");
                }
                inbox = context.Mail.AsNoTracking().Where(m => m.RecipientGuid == session.PlayerGuid)
                    .OrderByDescending(m => m.SentAt).Take(MaxInbox).ToList();
            }
            Send(session, InboxListResponse, w =>
            {
                w.Write(0u);
                w.Write(session.PlayerGuid);
                w.WriteUShort(1);
                w.Write((uint)inbox.Count);
                foreach (MailModel m in inbox)
                {
                    w.Write(m.Id);
                    w.Write(m.SenderGuid);
                    WriteName(w, m.SenderName);
                    w.Write(m.Subject ?? "");
                    w.Write(m.Body ?? "");
                    w.Write((ulong)new DateTimeOffset(DateTime.SpecifyKind(m.SentAt, DateTimeKind.Utc)).ToUnixTimeSeconds());
                    w.Write(m.IsRead ? 1u : 0u);
                    w.WriteUShort((ushort)ParseAttachments(m.Attachments).Count(a => a.Count > 0));
                    w.Write(m.Kind);
                }
            });
        }

        /// <summary>A PlayerName (FUN_142b8c3c0): u32, u32, u32, string first name, string last name.</summary>
        private static void WriteName(GamePacketWriter w, string name)
        {
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(name ?? "");
            w.Write("");
        }

        /// <summary>A compose read from the client (PacketMailComposeRequest, sub 1).</summary>
        internal sealed class ComposeDraft
        {
            /// <summary>
            /// The compose's leading u64, the data source's "ToGuid" (FUN_14363b170 → packet +0x18): 0 for the mail window;
            /// the site owner's guid for the Feedback button's mail (ClaimFeedback.as sets ToGuid and no "To" names). See
            /// <see cref="ResolveRecipients"/>.
            /// </summary>
            public ulong ToGuid;
            public readonly List<string> Names = new();
            public string Subject = "", Body = "";
            /// <summary>Bag items by item guid.</summary>
            public readonly List<(ulong Guid, uint Count)> Attached = new();
            /// <summary>
            /// The attached bag items once <see cref="CheckBag"/> passed them, in <see cref="Attached"/> order, with the
            /// state each keeps on the way (wear; audit-wiki-gaps #1).
            /// </summary>
            public readonly List<MailItem> Items = new();
            /// <summary>Coins sent; coins asked on delivery.</summary>
            public uint Coins, Cod;
        }

        /// <summary>
        /// Reads PacketMailComposeRequest (sub 1, built by FUN_14358c2e0 from the compose window): u64, u32 n × PlayerName
        /// "To", u32 m × PlayerName (unused), string subject, string body, u32 k × attachment. Attachments (writer
        /// FUN_143581400): u32 n × {u64 id, u32 type, ...}; type 1 = {u64 item guid, u32 count, u32 definition, u32} — a
        /// bag item, or coins when the guid is 0 (count = coins); type 5 = {u32 coins asked on delivery} (the compose
        /// window's coin pane: send money / COD). Success, or the result that refuses it.
        /// </summary>
        internal static uint ReadCompose(GamePacketReader reader, out ComposeDraft draft)
        {
            draft = new ComposeDraft();
            draft.ToGuid = reader.ReadULong();
            uint to = reader.ReadUInt();
            for (uint i = 0; i < to && i < MaxNameBlocks; i++)
            {
                // The client splits the "To" text on ' ', ';' and ',' (FUN_143638a10) and writes one name block per piece,
                // an empty "To" as one empty block: blank names are no recipients.
                string name = (ReadName(reader) ?? "").Trim();
                if (name.Length > 0 && draft.Names.Count < MaxRecipients)
                    draft.Names.Add(name);
            }
            uint cc = reader.ReadUInt();
            for (uint i = 0; i < cc && i < MaxRecipients; i++)
                ReadName(reader);
            draft.Subject = Clean(reader.ReadString(), MaxSubject);
            draft.Body = Clean(reader.ReadString(), MaxBody);
            uint attachmentCount = reader.BytesRemaining >= 4 ? reader.ReadUInt() : 0;
            if (attachmentCount > MaxAttachments)
                return TooManyAttachments;
            for (uint i = 0; i < attachmentCount; i++)
            {
                reader.ReadULong();
                uint type = reader.ReadUInt();
                if (type == 5)
                {
                    draft.Cod = reader.ReadUInt();
                    continue;
                }
                if (type != 1)
                    return AttachmentInvalid;
                ulong guid = reader.ReadULong();
                uint count = reader.ReadUInt();
                reader.ReadUInt();
                reader.ReadUInt();
                if (guid == 0)
                    draft.Coins += count;
                else
                    draft.Attached.Add((guid, count));
            }
            // Cash on delivery only makes sense for items, and a mail either sends coins or asks for them.
            if (draft.Cod > 0 && (draft.Attached.Count == 0 || draft.Coins > 0))
                return AttachmentInvalid;
            // The coins are Lumens (item 1579): on live they could not change hands (setting LumenTrading off,
            // Landmark Wipe Q&A 14 Mar 2016), neither sent nor asked for on delivery: "can't send NO_TRADE item".
            if ((draft.Coins > 0 || draft.Cod > 0) && !Trades.LumenTradingAllowed)
                return NoTradeItem;
            return Success;
        }

        /// <summary>
        /// PacketMailComposeRequest has two senders that name the recipient differently (docs/request-variants.md):
        /// Mail.SendMessage (FUN_1435b9f00, the mail window: data source GameData.EmailMessageData.Compose) sends the typed
        /// "To" names and ToGuid 0; Mail.SendMessageForClaimFeedback (FUN_1435b9f30, the site walk-on window's Feedback
        /// button → ClaimFeedback.swf, data source ComposeClaimFeedback) sends ToGuid = the site owner (walk-on OwnerGuid,
        /// or WinnerFeedbackReceipientGuid for an admin contest site) and no name. Both go through FUN_14363f210 →
        /// FUN_14363b170. With no names, the recipient is the character ToGuid names (<paramref name="nameOf"/>: its name,
        /// null when there is none), so the feedback mail arrives instead of "user not found".
        /// </summary>
        internal static void ResolveRecipients(ComposeDraft draft, Func<ulong, string> nameOf)
        {
            if (draft.Names.Count > 0 || draft.ToGuid == 0)
                return;
            string name = nameOf(draft.ToGuid);
            if (!string.IsNullOrWhiteSpace(name))
                draft.Names.Add(name.Trim());
        }

        /// <summary>A character's name by guid from this world's character database (deleted characters: null).</summary>
        internal static string CharacterNameOf(ulong guid)
            => DatabaseManager.Instance.CharacterDatabase.GetCharacterById(guid).GetAwaiter().GetResult() is { DeleteTime: null } c
                ? c.Name : null;

        /// <summary>
        /// The attached items must still be in the sender's bag (not equipped), enough of them, tradeable, and not chests
        /// holding items. Success with the definitions and counts (and <see cref="ComposeDraft.Items"/>: the same with
        /// each item's guid and state), or the result that refuses them.
        /// </summary>
        internal static uint CheckBag(WorldSession session, ComposeDraft draft, out List<(uint Definition, uint Count)> items)
        {
            items = new List<(uint Definition, uint Count)>();
            draft.Items.Clear();
            foreach ((ulong guid, uint count) in draft.Attached)
            {
                CharacterItemModel item = session.Inventory.Items.FirstOrDefault(i => i.Id == guid
                    && i.Container != Inventory.Inventory.EquippedContainer);
                if (item == null || count == 0 || count > item.Count
                    || (item.Container == Inventory.Inventory.VaultContainer && !Vaults.CanAccess(session)))
                    return ItemGone;
                // Mail keeps only kinds and counts (as Attach refuses it): a Player Design item would arrive empty and its
                // design, keyed to the sender's item guid, would be lost with the item.
                if (OverStackCap(count) || item.ItemDefinitionId == DesignItems.PlayerDesignItem)
                    return AttachmentInvalid;
                if (Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { NoTrade: true })
                    return NoTradeItem;
                // A store purchase in its waiting period is not tradeable yet (audit-wiki-gaps #7): "can't send NO_TRADE item".
                if (Inventory.Inventory.IsTradeLocked(item))
                    return NoTradeItem;
                if (Chests.HasItems(item.Id))
                    return ChestWithItems;
                items.Add((item.ItemDefinitionId, count));
                draft.Items.Add(new MailItem(item.Id, item.ItemDefinitionId, count, item.Wear));
            }
            // The same stack in two slots may not add up to more than it holds (the items leave by guid, TakeItems).
            foreach (var group in draft.Items.GroupBy(i => i.Guid))
                if (session.Inventory.Get(group.Key) is not { } stack || stack.Count < (uint)group.Sum(i => (long)i.Count))
                    return ItemGone;
            return Success;
        }

        /// <summary>
        /// Is this many of one item more than one attachment may carry? Wiki Trading: at most 9,000 of a gathered
        /// material (e.g. Copper Ore) per mail [W] (audit-wiki-gaps #16). The client has no cap
        /// of its own [P]: its attach request (Lua Mail.AttachItemToCompose, 0.695 FUN_1435ba0b0 → FUN_14363e9d0) and the
        /// compose writer (FUN_14363f210 / FUN_143634c00) send the dragged stack's count as it is, and the compose window
        /// (EmailComposeWindow / AttachmentListItemRenderer.as) limits only text lengths; the resources tab holds up to
        /// 24,000,000 per resource (InventoryDefinition.txt SLOT_VALUE), so 9,000 was a server rule. Answered with 5
        /// "attachment is invalid". Setting MailMaxStack (0 = no cap).
        /// </summary>
        private static bool OverStackCap(uint count)
        {
            uint cap = Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config.MailMaxStack;
            return cap > 0 && count > cap;
        }

        /// <summary>
        /// One attached bag item: its stack, kind, count and the wear it keeps (a worn tool arrives worn; the attachment
        /// text and the social service store it).
        /// </summary>
        internal readonly record struct MailItem(ulong Guid, uint Definition, uint Count, float Wear);

        /// <summary>
        /// Takes checked attachments (<see cref="CheckBag"/>) out of the sender's bag: the very stacks attached, so the
        /// item that leaves is the one whose state the mail carries. False (and nothing taken) when one has gone.
        /// </summary>
        internal static bool TakeItems(WorldSession session, IReadOnlyList<MailItem> items)
        {
            foreach (var group in items.GroupBy(i => i.Guid))
                if (session.Inventory.Get(group.Key) is not { } stack || stack.Count < (uint)group.Sum(i => (long)i.Count))
                    return false;
            foreach (MailItem item in items)
                session.TakeFromStack(item.Guid, item.Count);
            return true;
        }

        /// <summary>
        /// PacketMailComposeRequest (<see cref="ReadCompose"/>). Every recipient must exist; each gets their own copy.
        /// Answered with ComposeResponse (sub 5: u32 result, + PlayerName for results 1 and 0xD — which name failed), and
        /// online recipients get NewMailInboxNotification.
        /// </summary>
        private static void Compose(WorldSession session, GamePacketReader reader)
        {
            uint check = ReadCompose(reader, out ComposeDraft draft);
            if (check != Success)
            {
                Reply(session, check);
                return;
            }
            ResolveRecipients(draft, CharacterNameOf);
            List<string> names = draft.Names;
            uint coins = draft.Coins, cod = draft.Cod;
            if (coins > session.Coins)
            {
                Reply(session, InsufficientFunds);
                return;
            }

            if (names.Count == 0)
            {
                Reply(session, UserNotFound, "");
                return;
            }
            if (!MaySend(session.PlayerGuid))
            {
                Reply(session, SentTooMany);
                return;
            }

            var recipients = new List<CharacterModel>();
            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                CharacterModel character = DatabaseManager.Instance.CharacterDatabase.FindCharacterByName(name.Trim());
                if (character == null)
                {
                    Reply(session, UserNotFound, name);
                    return;
                }
                // "user is being ignored by target" (the recipient's ignore list is known while they are online).
                if (Ignores.Ignoring(PlayerManager.FindByGuid(character.Id), session))
                {
                    Reply(session, IgnoredByTarget, name);
                    return;
                }
                recipients.Add(character);
            }

            // Items go to one other player and must still be in the sender's bag (not equipped), enough of them.
            var items = new List<(uint Definition, uint Count)>();
            if (draft.Attached.Count > 0 || coins > 0)
            {
                if (recipients.Count > 1)
                {
                    Reply(session, MultipleWithAttachment);
                    return;
                }
                if (recipients[0].Id == session.PlayerGuid)
                {
                    Reply(session, SelfWithAttachment);
                    return;
                }
                check = CheckBag(session, draft, out items);
                // A trial key goes to a friend: not to another character of the sender's own account (Membership).
                if (check == Success)
                    check = Membership.CheckTrialKeyMail(session, recipients.Select(r => r.AccountId), items);
                if (check != Success)
                {
                    Reply(session, check);
                    return;
                }
            }
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                foreach (CharacterModel r in recipients)
                    if (context.Mail.Count(m => m.RecipientGuid == r.Id) >= MaxInbox)
                    {
                        Reply(session, InboxFull, r.Name);
                        return;
                    }
                if (coins > 0 && !session.ChangeCoins(-coins))
                {
                    Reply(session, InsufficientFunds);
                    return;
                }
                if (!TakeItems(session, draft.Items))
                {
                    if (coins > 0)
                        session.ChangeCoins(coins);
                    Reply(session, ItemGone);
                    return;
                }
                // A mailed trial key arrives as its NO_TRADE copy, so it travels by mail only once (Membership). Each item
                // keeps its wear (audit-wiki-gaps #1).
                var stored = draft.Items.Select(i => new Stored(Membership.AsMailed(i.Definition), i.Count, i.Wear)).ToList();
                if (coins > 0)
                    stored.Add(new Stored(CoinsEntry, coins));
                if (cod > 0)
                    stored.Add(new Stored(CodEntry, cod));
                foreach (CharacterModel r in recipients)
                    context.Mail.Add(new MailModel
                    {
                        RecipientGuid = r.Id,
                        SenderGuid = session.PlayerGuid,
                        SenderName = session.CharacterName,
                        Subject = draft.Subject,
                        Body = draft.Body,
                        SentAt = DateTime.UtcNow,
                        Attachments = FormatAttachments(stored)
                    });
                context.SaveChanges();
            }
            Reply(session, Success);
            log.Info($"{session.CharacterName} mailed {string.Join(", ", recipients.Select(r => r.Name))}.");
            foreach (CharacterModel r in recipients)
                if (PlayerManager.Snapshot().FirstOrDefault(p => p.PlayerGuid == r.Id) is { } online)
                    Send(online, NewMailNotification, w => w.Write((ushort)1));   // {u16 count}: required by the client (L67)
        }

        private static string ReadName(GamePacketReader reader)
        {
            reader.ReadUInt();
            reader.ReadUInt();
            reader.ReadUInt();
            string first = reader.ReadString();
            reader.ReadString();
            return first;
        }

        /// <summary>Trims, drops control characters except line breaks, and caps the length.</summary>
        internal static string Clean(string text, int max)
        {
            text = new string((text ?? "").Where(c => !char.IsControl(c) || c == '\n' || c == '\r').ToArray()).Trim();
            return text.Length > max ? text[..max] : text;
        }

        /// <summary>ComposeResponse (sub 5; 0.589 reader FUN_1432c8190, 0.695 FUN_143374fc0).</summary>
        internal static void Reply(WorldSession session, uint result, string name = null)
            => Send(session, ComposeResponse, w =>
            {
                w.Write(result);
                if (result == UserNotFound || result == InboxFull)
                    WriteName(w, name ?? "");
            });

        /// <summary>Sliding-window send limit per sender.</summary>
        private static bool MaySend(ulong sender)
        {
            Queue<DateTime> sends = recentSends.GetOrAdd(sender, _ => new Queue<DateTime>());
            lock (sends)
            {
                DateTime now = DateTime.UtcNow;
                while (sends.Count > 0 && now - sends.Peek() > SendWindow)
                    sends.Dequeue();
                if (sends.Count >= MaxSends)
                    return false;
                sends.Enqueue(now);
                return true;
            }
        }

        /// <summary>ReadMessageRequest (sub 9) {u64 id}: the player opened a message. Marked read, and answered with
        /// ReadMessageResponse (<see cref="SendRead"/>).</summary>
        private static void MarkRead(WorldSession session, ulong id)
        {
            MailModel m;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                m = context.Mail.FirstOrDefault(x => x.Id == id && x.RecipientGuid == session.PlayerGuid);
                if (m != null && !m.IsRead)
                {
                    m.IsRead = true;
                    context.SaveChanges();
                }
            }
            if (m == null)
            {
                SendRead(session, new MailReadData { MailId = id, Result = MessageNotFound });
                return;
            }
            // Taken attachments keep their slot (ids stay the positions + 1).
            List<Stored> attached = ParseAttachments(m.Attachments);
            var waiting = new List<MailAttachment>();
            for (int i = 0; i < attached.Count; i++)
                if (attached[i].Count > 0)
                    waiting.Add(new MailAttachment((uint)i, attached[i].Definition switch
                    {
                        CoinsEntry => MailAttachmentType.Coins,
                        CodEntry => MailAttachmentType.CashOnDelivery,
                        _ => MailAttachmentType.Item
                    }, attached[i].Definition, attached[i].Count, attached[i].Wear));
            SendRead(session, new MailReadData
            {
                MailId = m.Id, Result = Success, Kind = m.Kind, SenderGuid = m.SenderGuid, SenderName = m.SenderName ?? "",
                Subject = m.Subject ?? "", Body = m.Body ?? "",
                SentAt = (ulong)new DateTimeOffset(DateTime.SpecifyKind(m.SentAt, DateTimeKind.Utc)).ToUnixTimeSeconds(),
                Attachments = waiting
            });
        }

        /// <summary>
        /// ReadMessageResponse (sub 0xA; 0.589 reader FUN_1432c77e0, 0.695 FUN_143374610), which opens the reading window (Lua ReadEmailMessage): u64
        /// id, u32 result (0 ok, 0x14 not found — a failure ends here), u32 kind flags, then on success u64 sender, sender
        /// name, string subject, string body, u64 sent, u32, u32 n × attachment, u32 n + n bytes (HTML; empty). Written
        /// by the zone for the local and the remote mailboxes alike (the item records need the game's item tables).
        /// </summary>
        internal static void SendRead(WorldSession session, MailReadData m)
        {
            Send(session, ReadMessageResponse, w =>
            {
                w.Write(m.MailId);
                if (m.Result != Success)
                {
                    // A failure ends after the kind flags.
                    w.Write(m.Result);
                    w.Write(0u);
                    return;
                }
                w.Write(Success);
                w.Write(m.Kind);
                w.Write(m.SenderGuid);
                WriteName(w, m.SenderName);
                w.Write(m.Subject ?? "");
                w.Write(m.Body ?? "");
                w.Write(m.SentAt);
                w.Write(1u);
                // Attachments (FUN_1432c6c60): u64 attachment id, u32 type 2 = a full item record (the inventory's,
                // FUN_142b87110) so the reading window shows the real item; other types show a placeholder.
                w.Write((uint)m.Attachments.Count);
                foreach (MailAttachment a in m.Attachments)
                {
                    w.Write(AttachmentId((int)a.Slot));
                    if (a.Type != MailAttachmentType.Item)
                    {
                        // 6 = coins to take, 5 = coins to pay first (FUN_1432c6c60): both shown in the coin pane.
                        w.Write(a.Type == MailAttachmentType.Coins ? 6u : 5u);
                        w.Write(a.Count);
                        continue;
                    }
                    w.Write(2u);
                    var item = new InventoryItem
                    {
                        Guid = AttachmentItemGuid(m.MailId, (int)a.Slot),
                        Count = a.Count,
                        OwnerType = 1,
                        Wear = BitConverter.SingleToUInt32Bits(a.Wear),   // the condition the item arrives with
                        SecondsSinceLastCast = int.MaxValue
                    };
                    Inventory.Inventory.FillDefinition(item, a.Definition);
                    // The 0.589 record (FUN_142b87110) ends at the u32 after the metadata block (no login-packet byte).
                    item.WriteRecord(w);
                }
                w.Write(0u);
            });
        }

        /// <summary>Does the player have a letter still holding an item of this definition?</summary>
        public static bool HasAttachment(ulong recipient, uint itemDefinition)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.Mail.Where(m => m.RecipientGuid == recipient && m.Attachments != null && m.Attachments != "")
                .Select(m => m.Attachments).AsEnumerable()
                .Any(a => ParseAttachments(a).Any(x => x.Definition == itemDefinition && x.Count > 0));
        }

        /// <summary>
        /// Which of these definitions the player has on a letter still holding the item: one read of the recipient's
        /// letters for all of them (<see cref="HasAttachment"/> reads them once per definition). Blocking: off the world
        /// thread (review 2026-10-18b #5).
        /// </summary>
        public static HashSet<uint> AttachmentKinds(ulong recipient, IReadOnlyCollection<uint> itemDefinitions)
        {
            var wanted = itemDefinitions.ToHashSet();
            var found = new HashSet<uint>();
            if (wanted.Count == 0)
                return found;
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            foreach (string text in context.Mail.Where(m => m.RecipientGuid == recipient && m.Attachments != null && m.Attachments != "")
                .Select(m => m.Attachments).AsEnumerable())
                foreach (Stored a in ParseAttachments(text))
                    if (a.Count > 0 && wanted.Contains(a.Definition))
                        found.Add(a.Definition);
            return found;
        }

        /// <summary>One stored attachment slot: an item kind (or <see cref="CoinsEntry"/> / <see cref="CodEntry"/>), its
        /// count (0 = taken) and the item's wear.</summary>
        private readonly record struct Stored(uint Definition, uint Count, float Wear = 0f);

        /// <summary>
        /// "definition:count[:wear];..." ⇄ list. The wear field (invariant float) is written only for a worn item, so
        /// older texts read unchanged (and SocialImport reads both forms).
        /// </summary>
        private static string FormatAttachments(IEnumerable<Stored> items)
            => string.Join(";", items.Select(i => i.Wear > 0f
                ? $"{i.Definition}:{i.Count}:{i.Wear.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : $"{i.Definition}:{i.Count}"));

        private static string FormatAttachments(IEnumerable<(uint Definition, uint Count)> items)
            => FormatAttachments(items.Select(i => new Stored(i.Definition, i.Count)));

        private static List<Stored> ParseAttachments(string text)
        {
            var list = new List<Stored>();
            foreach (string part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length is 2 or 3 && uint.TryParse(f[0], out uint d) && uint.TryParse(f[1], out uint c))
                    list.Add(new Stored(d, c, f.Length == 3 && float.TryParse(f[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float wear) ? wear : 0f));
            }
            return list;
        }

        /// <summary>Attachment ids are their position + 1 (0 means "all" in a retrieve request).</summary>
        private static ulong AttachmentId(int index) => (ulong)index + 1;

        /// <summary>A display-only guid for an attached item (never in anyone's inventory).</summary>
        private static ulong AttachmentItemGuid(ulong mailId, int index) => 0x7000_0000_0000_0000ul | (mailId << 4) | (uint)index;

        /// <summary>
        /// ConsumeAttachmentRequest (sub 0x10, 0.589 writer FUN_14358c180, Lua Mail.RetrieveAttachment / RetrieveAllAttachments):
        /// {u64 mail id, u64 attachment id (0 = all), u8}. The items go into the recipient's bag and leave the mail;
        /// answered with ConsumeAttachmentResponse (sub 0x11; 0.589 reader FUN_1432c73a0, 0.695 FUN_1433741d0): u64 mail, u64 attachment, u32 result.
        /// </summary>
        private static void TakeAttachments(WorldSession session, ulong mailId, ulong attachmentId)
        {
            var replies = new List<(ulong Attachment, uint Result)>();
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                MailModel m = context.Mail.FirstOrDefault(x => x.Id == mailId && x.RecipientGuid == session.PlayerGuid);
                List<Stored> attached = ParseAttachments(m?.Attachments);
                if (m == null)
                    replies.Add((attachmentId, MessageNotFound));
                else
                {
                    // Taken attachments keep their place with count 0, so the ids (positions + 1) the client holds stay valid.
                    int codIndex = attached.FindIndex(a => a.Definition == CodEntry && a.Count > 0);
                    for (int i = 0; i < attached.Count; i++)
                    {
                        if (attached[i].Count == 0 || (attachmentId != 0 && AttachmentId(i) != attachmentId))
                            continue;
                        if (i == codIndex)
                        {
                            // Taking the COD entry is paying it (Lua Mail.PayCashOnDelivery sends its id); "take all"
                            // never pays. The coins go to the sender by mail.
                            if (attachmentId == 0)
                                continue;
                            if (!session.ChangeCoins(-attached[i].Count))
                            {
                                replies.Add((AttachmentId(i), InsufficientFundsForCod));
                                continue;
                            }
                            PayCod(context, m, attached[i].Count, session.CharacterName);
                            attached[i] = attached[i] with { Count = 0 };
                            codIndex = -1;
                            replies.Add((AttachmentId(i), Success));
                            continue;
                        }
                        if (codIndex >= 0)
                        {
                            replies.Add((AttachmentId(i), MustPayCodFirst));
                            continue;
                        }
                        if (attached[i].Definition == CoinsEntry)
                        {
                            if (!session.ChangeCoins(attached[i].Count))
                            {
                                replies.Add((AttachmentId(i), CurrencyFull));
                                continue;
                            }
                        }
                        else
                            session.GiveItem(attached[i].Definition, attached[i].Count, new Inventory.ItemState(attached[i].Wear));
                        attached[i] = attached[i] with { Count = 0 };
                        replies.Add((AttachmentId(i), Success));
                    }
                    if (replies.Count == 0)
                        replies.Add((attachmentId, AttachmentNotFound));
                    else if (replies.Any(r => r.Result == Success))
                    {
                        m.Attachments = attached.All(a => a.Count == 0) ? "" : FormatAttachments(attached);
                        context.SaveChanges();
                        log.Info($"{session.CharacterName} took {replies.Count} attachment(s) from mail {mailId}.");
                    }
                }
            }
            // One reply per attachment: the reading window removes the attachment with that id (FUN_14358abb0).
            foreach ((ulong attachment, uint result) in replies)
                Send(session, ConsumeAttachmentResponse, w =>
                {
                    w.Write(mailId);
                    w.Write(attachment);
                    w.Write(result);
                });
        }

        /// <summary>
        /// A mail from the game (a vendor's earnings, returned items): coins (0 = none) and items attached, the
        /// recipient told at once when online. A kind (MailModel.Kind) makes the client write its own sender / subject /
        /// body text for that kind.
        /// </summary>
        public static void SendSystemMail(ulong recipient, string from, string subject, string body, uint coins,
            IEnumerable<(uint Item, uint Count)> items = null, uint kind = 0)
        {
            var attached = new List<(uint Definition, uint Count)>();
            if (coins > 0)
                attached.Add((CoinsEntry, coins));
            if (items != null)
                attached.AddRange(items.Where(i => i.Count > 0).Select(i => (i.Item, i.Count)));
            var mail = new MailModel
            {
                RecipientGuid = recipient,
                SenderGuid = 0,
                SenderName = from,
                Subject = Clean(subject, MaxSubject),
                // The client's letter texts carry <br> line breaks, but the mail window escapes markup in bodies (it
                // showed "<br><br>" literally): plain line breaks instead.
                Body = Clean((body ?? "").Replace("<br>", "\n").Replace("<BR>", "\n"), MaxBody),
                SentAt = DateTime.UtcNow,
                Attachments = FormatAttachments(attached),
                Kind = kind
            };
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.Mail.Add(mail)).ContinueWith(_ =>
            {
                log.Info($"System mail {mail.Id} to 0x{recipient:X}: \"{mail.Subject}\" ({mail.Attachments}).");
                if (PlayerManager.FindByGuid(recipient) is WorldSession online)
                    Send(online, NewMailNotification, w => w.Write((ushort)1));   // {u16 count}: required by the client (L67)
            });
        }

        /// <summary>The recipient paid cash on delivery: the coins go back to the sender as a mail with coins attached.</summary>
        private static void PayCod(LandmarkEmulator.Database.Character.CharacterContext context, MailModel paid, uint coins, string payer)
        {
            context.Mail.Add(new MailModel
            {
                RecipientGuid = paid.SenderGuid,
                SenderGuid = paid.RecipientGuid,
                SenderName = payer,
                Subject = Clean("COD payment: " + paid.Subject, MaxSubject),
                Body = payer + " paid " + coins + " coins on delivery.",
                SentAt = DateTime.UtcNow,
                Attachments = FormatAttachments(new[] { new Stored(CoinsEntry, coins) })
            });
            if (PlayerManager.Snapshot().FirstOrDefault(p => p.PlayerGuid == paid.SenderGuid) is { } online)
                Send(online, NewMailNotification, w => w.Write((ushort)1));   // {u16 count}: required by the client (L67)
        }

        /// <summary>
        /// AttachItemToComposeRequest (sub 0x15, 0.589 writer FUN_14358baa0, from Lua Mail.AttachItemToCompose when an item is
        /// dragged onto a compose slot): {u32 type (1 = bag item), u64, u32 bag slot, u32 count, u32 container, bool}.
        /// Answered with AttachItemToComposeResponse (sub 0x16; 0.589 reader FUN_1432c70e0, 0.695 FUN_143373f10): u32 result, then the request's
        /// fields echoed (result 0 shows the item in the slot). The items only leave the bag when the mail is sent.
        /// </summary>
        internal static void Attach(WorldSession session, byte[] body)
        {
            uint result = Success;
            if (body.Length >= 25)
            {
                uint slot = BitConverter.ToUInt32(body, 12), count = BitConverter.ToUInt32(body, 16),
                    container = BitConverter.ToUInt32(body, 20);
                CharacterItemModel item = session.Inventory.Items.FirstOrDefault(i => i.Container == container && i.Slot == slot);
                // Mail keeps only kinds and counts: a Player Design item would lose its design. A stack over the mail cap
                // (MailMaxStack, 9,000 as live) is refused as well.
                if (BitConverter.ToUInt32(body, 0) != 1 || item == null || count == 0 || count > item.Count
                    || container == Inventory.Inventory.EquippedContainer || item.ItemDefinitionId == DesignItems.PlayerDesignItem
                    || OverStackCap(count) || (container == Inventory.Inventory.VaultContainer && !Vaults.CanAccess(session)))
                    result = AttachmentInvalid;
                // The client's own reasons: "can't send NO_TRADE item", "can't send chest item (with items in it)".
                else if (Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { NoTrade: true })
                    result = NoTradeItem;
                // A store purchase still waiting to become tradeable (audit-wiki-gaps #7): refused as no-trade, and the
                // client's own reason line 577061 "That item was recently purchased and may not be traded yet."
                else if (Inventory.Inventory.IsTradeLocked(item))
                {
                    result = NoTradeItem;
                    SystemMessages.Show(session, Trades.RecentlyPurchasedText);
                }
                else if (Chests.HasItems(item.Id))
                    result = ChestWithItems;
            }
            else
                result = AttachmentInvalid;
            Send(session, AttachItemResponse, w =>
            {
                w.Write(result);
                w.WriteBytes(body);
            });
        }

        /// <summary>DeleteMessageRequest (sub 0xB) {u64 id} → DeleteMessageResponse (sub 0xC: u64 id, u32 result).</summary>
        private static void Delete(WorldSession session, ulong id)
        {
            uint result;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                MailModel m = context.Mail.FirstOrDefault(x => x.Id == id && x.RecipientGuid == session.PlayerGuid);
                if (m == null)
                    result = MessageNotFound;
                else
                {
                    context.Mail.Remove(m);
                    context.SaveChanges();
                    result = Success;
                }
            }
            Send(session, DeleteMessageResponse, w =>
            {
                w.Write(id);
                w.Write(result);
            });
        }
    }
}
