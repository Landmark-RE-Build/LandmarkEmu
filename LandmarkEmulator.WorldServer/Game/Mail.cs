using LandmarkEmulator.Database.Character.Model;
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
    /// </summary>
    public static class Mail
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // Client → server.
        private const uint ComposeRequest = 1, InboxListRequest = 6, ReadMessageRequest = 8, DeleteMessageRequest = 10,
            ConsumeAttachmentRequest = 0xF, AttachItemRequest = 0x14;
        // Server → client.
        private const uint ComposeResponse = 4, InboxListResponse = 7, ReadMessageResponse = 9, DeleteMessageResponse = 0xB,
            NewMailNotification = 0xE, ConsumeAttachmentResponse = 0x10, EnableWindow = 0x13, AttachItemResponse = 0x15;

        /// <summary>
        /// Result codes (client text table at 0x14521a980): 0 Success, 1 unable to find user, 5 attachment is invalid,
        /// 0xD user has too much mail in inbox, 0xE user has sent too many mail messages, 0x14 message not found.
        /// </summary>
        private const uint Success = 0, UserNotFound = 1, SelfWithAttachment = 2, AttachmentInvalid = 5,
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
        private static void Send(WorldSession session, uint sub, Action<GamePacketWriter> write)
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
        /// Open Inbox button.
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
        /// InboxListResponse (sub 7, reader FUN_1432c75d0): u32 list type (0), u64 owner, u16 flags (bit 0: complete; the
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

        /// <summary>
        /// PacketMailComposeRequest (sub 1, built by FUN_14358c2e0 from the compose window): u64, u32 n × PlayerName
        /// "To", u32 m × PlayerName (unused), string subject, string body, u32 k × attachment. Every recipient must exist;
        /// each gets their own copy. Answered with ComposeResponse (sub 4: u32 result, + PlayerName for results 1 and
        /// 0xD — which name failed), and online recipients get NewMailInboxNotification.
        /// </summary>
        private static void Compose(WorldSession session, GamePacketReader reader)
        {
            reader.ReadULong();
            var names = new List<string>();
            uint to = reader.ReadUInt();
            for (uint i = 0; i < to && i < MaxRecipients; i++)
                names.Add(ReadName(reader));
            uint cc = reader.ReadUInt();
            for (uint i = 0; i < cc && i < MaxRecipients; i++)
                ReadName(reader);
            string subject = Clean(reader.ReadString(), MaxSubject);
            string body = Clean(reader.ReadString(), MaxBody);
            // Attachments (writer FUN_143581400): u32 n × {u64 id, u32 type, ...}; type 1 = {u64 item guid, u32 count, u32
            // definition, u32} — a bag item, or coins when the guid is 0 (count = coins); type 5 = {u32 coins asked on
            // delivery} (the compose window's coin pane: send money / COD).
            uint attachmentCount = reader.BytesRemaining >= 4 ? reader.ReadUInt() : 0;
            if (attachmentCount > MaxAttachments)
            {
                Reply(session, TooManyAttachments);
                return;
            }
            var attached = new List<(ulong Guid, uint Count)>();
            uint coins = 0, cod = 0;
            for (uint i = 0; i < attachmentCount; i++)
            {
                reader.ReadULong();
                uint type = reader.ReadUInt();
                if (type == 5)
                {
                    cod = reader.ReadUInt();
                    continue;
                }
                if (type != 1)
                {
                    Reply(session, AttachmentInvalid);
                    return;
                }
                ulong guid = reader.ReadULong();
                uint count = reader.ReadUInt();
                reader.ReadUInt();
                reader.ReadUInt();
                if (guid == 0)
                    coins += count;
                else
                    attached.Add((guid, count));
            }
            // Cash on delivery only makes sense for items, and a mail either sends coins or asks for them.
            if (cod > 0 && (attached.Count == 0 || coins > 0))
            {
                Reply(session, AttachmentInvalid);
                return;
            }
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
            if (attached.Count > 0 || coins > 0)
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
                foreach ((ulong guid, uint count) in attached)
                {
                    CharacterItemModel item = session.Inventory.Items.FirstOrDefault(i => i.Id == guid
                        && i.Container != Inventory.Inventory.EquippedContainer);
                    if (item == null || count == 0 || count > item.Count)
                    {
                        Reply(session, ItemGone);
                        return;
                    }
                    if (Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { NoTrade: true })
                    {
                        Reply(session, NoTradeItem);
                        return;
                    }
                    if (Chests.HasItems(item.Id))
                    {
                        Reply(session, ChestWithItems);
                        return;
                    }
                    items.Add((item.ItemDefinitionId, count));
                }
                foreach (var group in items.GroupBy(i => i.Definition))
                    if (session.Inventory.CountOf(group.Key) < (uint)group.Sum(i => i.Count))
                    {
                        Reply(session, ItemGone);
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
                foreach ((uint definition, uint count) in items)
                    session.TakeItem(definition, count);
                if (coins > 0)
                    items.Add((CoinsEntry, coins));
                if (cod > 0)
                    items.Add((CodEntry, cod));
                foreach (CharacterModel r in recipients)
                    context.Mail.Add(new MailModel
                    {
                        RecipientGuid = r.Id,
                        SenderGuid = session.PlayerGuid,
                        SenderName = session.CharacterName,
                        Subject = subject,
                        Body = body,
                        SentAt = DateTime.UtcNow,
                        Attachments = FormatAttachments(items)
                    });
                context.SaveChanges();
            }
            Reply(session, Success);
            log.Info($"{session.CharacterName} mailed {string.Join(", ", recipients.Select(r => r.Name))}.");
            foreach (CharacterModel r in recipients)
                if (PlayerManager.Snapshot().FirstOrDefault(p => p.PlayerGuid == r.Id) is { } online)
                    Send(online, NewMailNotification, _ => { });
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
        private static string Clean(string text, int max)
        {
            text = new string((text ?? "").Where(c => !char.IsControl(c) || c == '\n' || c == '\r').ToArray()).Trim();
            return text.Length > max ? text[..max] : text;
        }

        /// <summary>ComposeResponse (sub 4, reader FUN_1432c8190).</summary>
        private static void Reply(WorldSession session, uint result, string name = null)
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

        /// <summary>
        /// ReadMessageRequest (sub 8) {u64 id}: the player opened a message. Marked read, and answered with
        /// ReadMessageResponse (sub 9, reader FUN_1432c77e0), which opens the reading window (Lua ReadEmailMessage):
        /// u64 id, u32 result (0 ok, 0x14 not found — a failure ends here), u32 kind flags, then on success u64 sender,
        /// sender name, string subject, string body, u64 sent, u32, u32 n × attachment, u32 n + n bytes (HTML; empty).
        /// </summary>
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
            Send(session, ReadMessageResponse, w =>
            {
                w.Write(id);
                if (m == null)
                {
                    // A failure ends after the kind flags.
                    w.Write(MessageNotFound);
                    w.Write(0u);
                    return;
                }
                w.Write(Success);
                w.Write(m.Kind);
                w.Write(m.SenderGuid);
                WriteName(w, m.SenderName);
                w.Write(m.Subject ?? "");
                w.Write(m.Body ?? "");
                w.Write((ulong)new DateTimeOffset(DateTime.SpecifyKind(m.SentAt, DateTimeKind.Utc)).ToUnixTimeSeconds());
                w.Write(1u);
                // Attachments (FUN_1432c6c60): u64 attachment id, u32 type 2 = a full item record (the inventory's,
                // FUN_142b87110) so the reading window shows the real item; other types show a placeholder.
                List<(uint Definition, uint Count)> attached = ParseAttachments(m.Attachments);
                w.Write((uint)attached.Count(a => a.Count > 0));
                for (int i = 0; i < attached.Count; i++)
                {
                    if (attached[i].Count == 0)
                        continue;                       // already taken; ids stay the slot positions
                    w.Write(AttachmentId(i));
                    if (attached[i].Definition is CoinsEntry or CodEntry)
                    {
                        // 6 = coins to take, 5 = coins to pay first (FUN_1432c6c60): both shown in the coin pane.
                        w.Write(attached[i].Definition == CoinsEntry ? 6u : 5u);
                        w.Write(attached[i].Count);
                        continue;
                    }
                    w.Write(2u);
                    var item = new InventoryItem
                    {
                        Guid = AttachmentItemGuid(m.Id, i),
                        Count = attached[i].Count,
                        OwnerType = 1,
                        SecondsSinceLastCast = int.MaxValue
                    };
                    Inventory.Inventory.FillDefinition(item, attached[i].Definition);
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

        /// <summary>"definition:count;..." ⇄ list.</summary>
        private static string FormatAttachments(IEnumerable<(uint Definition, uint Count)> items)
            => string.Join(";", items.Select(i => $"{i.Definition}:{i.Count}"));

        private static List<(uint Definition, uint Count)> ParseAttachments(string text)
        {
            var list = new List<(uint, uint)>();
            foreach (string part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length == 2 && uint.TryParse(f[0], out uint d) && uint.TryParse(f[1], out uint c))
                    list.Add((d, c));
            }
            return list;
        }

        /// <summary>Attachment ids are their position + 1 (0 means "all" in a retrieve request).</summary>
        private static ulong AttachmentId(int index) => (ulong)index + 1;

        /// <summary>A display-only guid for an attached item (never in anyone's inventory).</summary>
        private static ulong AttachmentItemGuid(ulong mailId, int index) => 0x7000_0000_0000_0000ul | (mailId << 4) | (uint)index;

        /// <summary>
        /// ConsumeAttachmentRequest (sub 0xF, writer FUN_14358c180, Lua Mail.RetrieveAttachment / RetrieveAllAttachments):
        /// {u64 mail id, u64 attachment id (0 = all), u8}. The items go into the recipient's bag and leave the mail;
        /// answered with ConsumeAttachmentResponse (sub 0x10, reader FUN_1432c73a0): u64 mail, u64 attachment, u32 result.
        /// </summary>
        private static void TakeAttachments(WorldSession session, ulong mailId, ulong attachmentId)
        {
            var replies = new List<(ulong Attachment, uint Result)>();
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                MailModel m = context.Mail.FirstOrDefault(x => x.Id == mailId && x.RecipientGuid == session.PlayerGuid);
                List<(uint Definition, uint Count)> attached = ParseAttachments(m?.Attachments);
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
                            attached[i] = (attached[i].Definition, 0);
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
                            session.GiveItem(attached[i].Definition, attached[i].Count);
                        attached[i] = (attached[i].Definition, 0);
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
            var attached = new List<(uint, uint)>();
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
                    Send(online, NewMailNotification, _ => { });
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
                Attachments = FormatAttachments(new[] { (CoinsEntry, coins) })
            });
            if (PlayerManager.Snapshot().FirstOrDefault(p => p.PlayerGuid == paid.SenderGuid) is { } online)
                Send(online, NewMailNotification, _ => { });
        }

        /// <summary>
        /// AttachItemToComposeRequest (sub 0x14, writer FUN_14358baa0, from Lua Mail.AttachItemToCompose when an item is
        /// dragged onto a compose slot): {u32 type (1 = bag item), u64, u32 bag slot, u32 count, u32 container, bool}.
        /// Answered with AttachItemToComposeResponse (sub 0x15, reader FUN_1432c70e0): u32 result, then the request's
        /// fields echoed (result 0 shows the item in the slot). The items only leave the bag when the mail is sent.
        /// </summary>
        private static void Attach(WorldSession session, byte[] body)
        {
            uint result = Success;
            if (body.Length >= 25)
            {
                uint slot = BitConverter.ToUInt32(body, 12), count = BitConverter.ToUInt32(body, 16),
                    container = BitConverter.ToUInt32(body, 20);
                CharacterItemModel item = session.Inventory.Items.FirstOrDefault(i => i.Container == container && i.Slot == slot);
                // Mail keeps only kinds and counts: a Player Design item would lose its design.
                if (BitConverter.ToUInt32(body, 0) != 1 || item == null || count == 0 || count > item.Count
                    || container == Inventory.Inventory.EquippedContainer || item.ItemDefinitionId == DesignItems.PlayerDesignItem)
                    result = AttachmentInvalid;
                // The client's own reasons: "can't send NO_TRADE item", "can't send chest item (with items in it)".
                else if (Inventory.ItemDefinitionManager.Get(item.ItemDefinitionId) is { NoTrade: true })
                    result = NoTradeItem;
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

        /// <summary>DeleteMessageRequest (sub 10) {u64 id} → DeleteMessageResponse (sub 0xB: u64 id, u32 result).</summary>
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
