using LandmarkEmulator.Database.Social.Model;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// The mailboxes, moved out of the world server's Game/Mail.cs with the same client behaviour (social-service.md
    /// §5.1 item 5; MailBase 0xF8, u32 sub, client dispatcher FUN_1432c9770). Mail is shared by every world: a letter
    /// to a player on another world arrives in the same inbox.
    /// <list type="bullet">
    /// <item>Inbox (6 → 7), delete (0xA → 0xB): handed on whole by the zone, answered here.</item>
    /// <item>Read (9): marked read here; the reading window's ReadMessageResponse (0xA) carries full item records, so the
    /// zone writes it from <see cref="MailReadData"/>.</item>
    /// <item>Compose (1): the zone checks the attachments against the bag (owned, not equipped, NO_TRADE, chests),
    /// takes items and coins into escrow and sends ComposeRequestStage2; this side checks recipients (exist, ignore),
    /// the multiple-recipient / self rules for attachments, full inboxes and the send limit, stores one copy per
    /// recipient, answers the client (ComposeResponse 4) and tells the zone to drop (Stage3) or give back
    /// (ComposeResponseReturnAttachments) its escrow.</item>
    /// <item>Taking attachments (0x10): the cash-on-delivery rules are applied here; what may be handed over goes to the
    /// zone as ConsumeAttachmentZoneRequest (the zone gives items / coins, or takes the COD coins) and is marked taken
    /// from its answer; one ConsumeAttachmentResponse (0x11) per attachment, as before.</item>
    /// <item>System mail (0x17) from zones and other services; NewMailInboxNotification (0xF) to recipients online
    /// on any world.</item>
    /// </list>
    /// Result codes are the client's (text table 0x14521a980), the limits as before (Mail.cs).
    /// </summary>
    public sealed class MailService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // Server → client subs, 0.695 numbers (as Game/Mail.cs; re/out/packet_ids_695.tsv cMailPacketId*).
        private const uint ComposeResponse = 5, InboxListResponse = 8, DeleteMessageResponse = 0xC, NewMailNotification = 0xF,
            ConsumeAttachmentResponse = 0x11;

        // Result codes.
        private const uint Success = 0, UserNotFound = 1, SelfWithAttachment = 2, ServiceUnavailable = 3,
            IgnoredByTarget = 0xF, MultipleWithAttachment = 0xB, InboxFull = 0xD, SentTooMany = 0xE, MessageNotFound = 0x14,
            AttachmentNotFound = 0x1A, MustPayCodFirst = 0x1F;

        private const int MaxInbox = 100, MaxRecipients = 5, MaxSends = 20, MaxSubject = 128, MaxBody = 4096;
        private static readonly TimeSpan SendWindow = TimeSpan.FromMinutes(10);
        /// <summary>Client constant Mail.ExpireTimeMinutes (43200 = 30 days); mail still holding something is kept.</summary>
        private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(43200);
        /// <summary>How long an attachment handed to a zone stays locked while its answer is awaited.</summary>
        private static readonly TimeSpan HandOverTimeout = TimeSpan.FromSeconds(60);

        private readonly SocialHub hub;
        private readonly Dictionary<ulong, Queue<DateTime>> recentSends = new();
        /// <summary>Attachment slots handed to a zone and not answered yet: (mail, slot) → when.</summary>
        private readonly Dictionary<(ulong Mail, uint Slot), DateTime> handingOver = new();
        private ulong nextToken = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 16;

        public MailService(SocialHub hub) => this.hub = hub;

        // ---- Client requests handed on --------------------------------------------------------------------------------

        public void Handle(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
            {
                log.Warn($"Mail request 0x{request.Sub:X} for {request.CharacterGuid:X} on {world} without a current connection: dropped.");
                return;
            }
            try
            {
                var r = new GamePacketReader(request.Body);
                switch (request.Sub)
                {
                    case MailMessages.InboxListRequest:
                        SendInbox(player);
                        break;
                    case MailMessages.ReadMessageRequest:
                        Read(player, r.ReadULong());
                        break;
                    case MailMessages.DeleteMessageRequest:
                        Delete(player, r.ReadULong());
                        break;
                    case MailMessages.ConsumeAttachmentRequest:
                        TakeAttachments(player, r.ReadULong(), r.ReadULong());
                        break;
                }
            }
            catch (Exception e)
            {
                log.Warn($"Bad mail request 0x{request.Sub:X} from {player}: {e.Message}");
            }
        }

        /// <summary>
        /// InboxListResponse (8, 0.695 reader FUN_143374400): u32 list type (0), u64 owner, u16 flags (bit 0: complete), u32 n,
        /// n × MailData (FUN_1432c6f60): u64 id, u64 sender, name block, string subject, string body, u64 sent (Unix s),
        /// u32 read, u16 attachment count, u32 kind. Newest first; expired messages that hold nothing are deleted first.
        /// </summary>
        private void SendInbox(OnlineCharacter player)
        {
            int expired = hub.Store.DeleteExpiredMail(player.Guid, DateTime.UtcNow - Expiry);
            if (expired > 0)
                log.Info($"Deleted {expired} expired message(s) from {player}'s inbox.");
            IReadOnlyList<SocialMailModel> inbox = hub.Store.GetInbox(player.Guid, MaxInbox);
            hub.SendTo(player, ClientPackets.Mail(InboxListResponse, w =>
            {
                w.Write(0u);
                w.Write(player.Guid);
                w.WriteUShort(1);
                w.Write((uint)inbox.Count);
                foreach (SocialMailModel m in inbox)
                {
                    w.Write(m.Id);
                    w.Write(m.SenderGuid);
                    ClientPackets.WriteName(w, m.SenderName);
                    w.Write(m.Subject ?? "");
                    w.Write(m.Body ?? "");
                    w.Write(UnixSeconds(m.SentAt));
                    w.Write(m.IsRead ? 1u : 0u);
                    w.WriteUShort((ushort)m.Attachments.Count(a => !a.Taken));
                    w.Write(m.Kind);
                }
            }));
        }

        /// <summary>ReadMessageRequest (9) {u64 id}: marked read; the zone writes the reading window's packet.</summary>
        private void Read(OnlineCharacter player, ulong id)
        {
            SocialMailModel m = hub.Store.GetMail(id, player.Guid);
            if (m != null && !m.IsRead)
                hub.Store.MarkMailRead(id);
            hub.SendService(player.World, m == null
                ? new MailReadData { ConnectionId = player.ConnectionId, CharacterGuid = player.Guid, MailId = id, Result = MessageNotFound }
                : new MailReadData
                {
                    ConnectionId = player.ConnectionId, CharacterGuid = player.Guid, MailId = id, Result = Success, Kind = m.Kind,
                    SenderGuid = m.SenderGuid, SenderName = m.SenderName ?? "", Subject = m.Subject ?? "", Body = m.Body ?? "",
                    SentAt = UnixSeconds(m.SentAt), Attachments = Waiting(m).ToList()
                });
        }

        /// <summary>DeleteMessageRequest (0xB) {u64 id} → DeleteMessageResponse (0xC: u64 id, u32 result).</summary>
        private void Delete(OnlineCharacter player, ulong id)
        {
            uint result = hub.Store.DeleteMail(id, player.Guid) ? Success : MessageNotFound;
            hub.SendTo(player, ClientPackets.Mail(DeleteMessageResponse, w =>
            {
                w.Write(id);
                w.Write(result);
            }));
        }

        /// <summary>
        /// ConsumeAttachmentRequest (0x10) {u64 mail id, u64 attachment id (0 = all), u8}: the cash-on-delivery slot is
        /// paid only when asked for by its own id ("take all" never pays); while it is unpaid nothing else may be taken
        /// (MustPayCodFirst). What may go is handed to the zone; the rest is answered at once.
        /// </summary>
        private void TakeAttachments(OnlineCharacter player, ulong mailId, ulong attachmentId)
        {
            ExpireHandOvers();
            SocialMailModel m = hub.Store.GetMail(mailId, player.Guid);
            var replies = new List<(ulong Attachment, uint Result)>();
            var handOver = new List<MailAttachment>();
            if (m == null)
                replies.Add((attachmentId, MessageNotFound));
            else
            {
                List<MailAttachment> waiting = Waiting(m).ToList();
                bool codUnpaid = waiting.Any(a => a.Type == MailAttachmentType.CashOnDelivery);
                foreach (MailAttachment a in waiting)
                {
                    if (attachmentId != 0 && AttachmentId(a.Slot) != attachmentId)
                        continue;
                    if (handingOver.ContainsKey((m.Id, a.Slot)))
                        continue;                               // already on its way to the player
                    if (a.Type == MailAttachmentType.CashOnDelivery)
                    {
                        if (attachmentId != 0)
                            handOver.Add(a);
                        continue;
                    }
                    if (codUnpaid)
                    {
                        replies.Add((AttachmentId(a.Slot), MustPayCodFirst));
                        continue;
                    }
                    handOver.Add(a);
                }
                if (replies.Count == 0 && handOver.Count == 0)
                    replies.Add((attachmentId, AttachmentNotFound));
            }
            foreach ((ulong attachment, uint result) in replies)
                SendConsumeResponse(player, mailId, attachment, result);
            if (handOver.Count == 0)
                return;
            foreach (MailAttachment a in handOver)
                handingOver[(mailId, a.Slot)] = DateTime.UtcNow;
            hub.SendService(player.World, new MailConsumeZoneRequest
            {
                Token = ++nextToken, ConnectionId = player.ConnectionId, CharacterGuid = player.Guid, MailId = mailId, Attachments = handOver
            });
        }

        /// <summary>
        /// ConsumeAttachmentZoneResponse: the slots the zone handed over (or, for COD, charged) are marked taken; a paid
        /// COD goes to the sender as a mail with the coins; each attachment is answered to the client.
        /// </summary>
        public void ConsumeDone(MailConsumeZoneResponse response)
        {
            foreach ((uint slot, uint _) in response.Results)
                handingOver.Remove((response.MailId, slot));
            SocialMailModel m = hub.Store.GetMail(response.MailId, response.CharacterGuid);
            if (m == null)
            {
                log.Warn($"Attachment hand-over for mail {response.MailId}, which is gone.");
                return;
            }
            var taken = response.Results.Where(r => r.Result == Success).Select(r => r.Slot).ToList();
            if (taken.Count > 0)
            {
                hub.Store.SetMailAttachmentsTaken(m.Id, taken);
                SocialMailAttachmentModel cod = m.Attachments.FirstOrDefault(a => !a.Taken
                    && a.Type == (byte)MailAttachmentType.CashOnDelivery && taken.Contains(a.Slot));
                if (cod != null)
                    PayCod(m, cod.Count);
                log.Info($"{response.CharacterGuid:X} took {taken.Count} attachment(s) from mail {m.Id}.");
            }
            OnlineCharacter player = hub.Presence.Find(response.CharacterGuid);
            if (player is { Connected: true })
                foreach ((uint slot, uint result) in response.Results)
                    SendConsumeResponse(player, response.MailId, AttachmentId(slot), result);
        }

        /// <summary>ConsumeAttachmentResponse (0x11, 0.695 reader FUN_1433741d0): u64 mail, u64 attachment, u32 result.</summary>
        private void SendConsumeResponse(OnlineCharacter player, ulong mailId, ulong attachment, uint result)
            => hub.SendTo(player, ClientPackets.Mail(ConsumeAttachmentResponse, w =>
            {
                w.Write(mailId);
                w.Write(attachment);
                w.Write(result);
            }));

        /// <summary>The recipient paid cash on delivery: the coins go back to the sender as a mail with coins attached.</summary>
        private void PayCod(SocialMailModel paid, uint coins)
        {
            if (paid.SenderGuid == 0)
                return;
            string payer = hub.Store.GetCharacter(paid.RecipientGuid)?.Name ?? "";
            Store(new[]
            {
                new SocialMailModel
                {
                    RecipientGuid = paid.SenderGuid, SenderGuid = paid.RecipientGuid, SenderName = payer,
                    Subject = Clean("COD payment: " + paid.Subject, MaxSubject), Body = payer + " paid " + coins + " coins on delivery.",
                    SentAt = DateTime.UtcNow,
                    Attachments = { new SocialMailAttachmentModel { Slot = 0, Type = (byte)MailAttachmentType.Coins, Count = coins } }
                }
            });
        }

        // ---- From zones -----------------------------------------------------------------------------------------------

        /// <summary>ComposeRequestStage2: the recipients' side of a compose whose attachments the zone holds.</summary>
        public void Compose(WorldLink world, MailComposeStage2 compose)
        {
            OnlineCharacter sender = hub.Presence.Find(compose.SenderGuid);
            if (sender is not { Connected: true })
                sender = null;
            void Answer(uint result, string name = null)
            {
                if (sender != null)
                    hub.SendTo(sender, ClientPackets.Mail(ComposeResponse, w =>
                    {
                        w.Write(result);
                        if (result == UserNotFound || result == InboxFull)
                            ClientPackets.WriteName(w, name ?? "");
                    }));
                hub.SendService(world, new MailComposeResult
                {
                    Stored = result == Success, Token = compose.Token, SenderGuid = compose.SenderGuid, Result = result
                });
            }

            // Sent again after a lost link: it is stored already.
            if (hub.Store.HasMailToken(compose.Token))
            {
                Answer(Success);
                return;
            }
            var names = compose.Recipients.Select(n => (n ?? "").Trim()).Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRecipients).ToList();
            if (names.Count == 0)
            {
                Answer(UserNotFound, "");
                return;
            }
            if (!MaySend(compose.SenderGuid))
            {
                Answer(SentTooMany);
                return;
            }
            var recipients = new List<SocialCharacterModel>();
            foreach (string name in names)
            {
                SocialCharacterModel character = hub.Store.FindCharacterByName(name);
                if (character == null)
                {
                    Answer(UserNotFound, name);
                    return;
                }
                // "user is being ignored by target": the lists are all here now, online or not.
                if (hub.Ignores.Ignoring(character.Guid, compose.SenderGuid))
                {
                    Answer(IgnoredByTarget, name);
                    return;
                }
                recipients.Add(character);
            }
            if (compose.Attachments.Any(a => a.Type != MailAttachmentType.CashOnDelivery))
            {
                if (recipients.Count > 1)
                {
                    Answer(MultipleWithAttachment);
                    return;
                }
                if (recipients[0].Guid == compose.SenderGuid)
                {
                    Answer(SelfWithAttachment);
                    return;
                }
            }
            foreach (SocialCharacterModel r in recipients)
                if (hub.Store.CountInbox(r.Guid) >= MaxInbox)
                {
                    Answer(InboxFull, r.Name);
                    return;
                }

            string subject = Clean(compose.Subject, MaxSubject), body = Clean(compose.Body, MaxBody);
            try
            {
                Store(recipients.Select(r => new SocialMailModel
                {
                    RecipientGuid = r.Guid, SenderGuid = compose.SenderGuid, SenderName = compose.SenderName ?? "",
                    Subject = subject, Body = body, SentAt = DateTime.UtcNow, Token = compose.Token,
                    Attachments = Rows(compose.Attachments)
                }).ToList());
            }
            catch (Exception e)
            {
                // The database is away: the zone gives the items back ("Mail service is unavailable").
                log.Error(e, $"Storing mail from {compose.SenderName} failed.");
                Answer(ServiceUnavailable);
                return;
            }
            Answer(Success);
            log.Info($"{compose.SenderName} mailed {string.Join(", ", recipients.Select(r => r.Name))}.");
        }

        /// <summary>System mail from a zone or service: stored once per token; the recipient is told when online.</summary>
        public void System(SystemMail mail)
        {
            if (hub.Store.HasMailToken(mail.Token))
                return;
            Store(new[]
            {
                new SocialMailModel
                {
                    RecipientGuid = mail.RecipientGuid, SenderGuid = 0, SenderName = mail.From ?? "",
                    Subject = Clean(mail.Subject, MaxSubject), Body = Clean(mail.Body, MaxBody), SentAt = DateTime.UtcNow,
                    Kind = mail.Kind, Token = mail.Token, Attachments = Rows(mail.Attachments)
                }
            });
            log.Info($"System mail to {mail.RecipientGuid:X}: \"{mail.Subject}\" ({mail.Attachments.Count} attachment(s)).");
        }

        public void AttachmentQuery(WorldLink world, MailAttachmentQuery query)
            => hub.SendService(world, new MailAttachmentReply
            {
                RequestId = query.RequestId, Found = hub.Store.HasWaitingMailItem(query.CharacterGuid, query.Definition)
            });

        // ---- Helpers --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Stores messages and tells the online recipients: NewMailInboxNotification (0xF) {u16 count}. The 0.695 reader
        /// FUN_1433752c0 needs the count (an empty body is dropped: no prompt, no inbox refresh); the client shows "You have
        /// [count] message(s)" (562631 / 562632) and re-requests an open inbox (trace/0695/agent-leads2-d1.md L67) [P]. The
        /// count is the messages just delivered to that recipient [I].
        /// </summary>
        private void Store(IReadOnlyCollection<SocialMailModel> mail)
        {
            hub.Store.AddMail(mail);
            foreach (var group in mail.GroupBy(m => m.RecipientGuid))
                if (hub.Presence.Find(group.Key) is { Connected: true } online)
                {
                    ushort count = (ushort)Math.Min(ushort.MaxValue, group.Count());
                    hub.SendTo(online, ClientPackets.Mail(NewMailNotification, w => w.Write(count)));
                }
        }

        private static List<SocialMailAttachmentModel> Rows(IEnumerable<MailAttachment> attachments)
            => attachments.Where(a => a.Count > 0).Select((a, i) => new SocialMailAttachmentModel
            {
                Slot = (uint)i, Type = (byte)a.Type, ItemDefinition = a.Type == MailAttachmentType.Item ? a.Definition : 0, Count = a.Count,
                Wear = a.Type == MailAttachmentType.Item ? a.Wear : 0f
            }).ToList();

        private static IEnumerable<MailAttachment> Waiting(SocialMailModel m)
            => m.Attachments.Where(a => !a.Taken && a.Count > 0).OrderBy(a => a.Slot)
                .Select(a => new MailAttachment(a.Slot, (MailAttachmentType)a.Type, a.ItemDefinition, a.Count, a.Wear));

        /// <summary>Attachment ids are the slot + 1 (0 means "all" in a take request).</summary>
        private static ulong AttachmentId(uint slot) => (ulong)slot + 1;

        private static ulong UnixSeconds(DateTime at) => (ulong)new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToUnixTimeSeconds();

        /// <summary>Sliding-window send limit per sender.</summary>
        private bool MaySend(ulong sender)
        {
            if (!recentSends.TryGetValue(sender, out Queue<DateTime> sends))
                recentSends[sender] = sends = new Queue<DateTime>();
            DateTime now = DateTime.UtcNow;
            while (sends.Count > 0 && now - sends.Peek() > SendWindow)
                sends.Dequeue();
            if (sends.Count >= MaxSends)
                return false;
            sends.Enqueue(now);
            return true;
        }

        /// <summary>A zone that never answered (it went away): its slots may be asked for again.</summary>
        private void ExpireHandOvers()
        {
            DateTime cutoff = DateTime.UtcNow - HandOverTimeout;
            foreach (var key in handingOver.Where(h => h.Value < cutoff).Select(h => h.Key).ToList())
                handingOver.Remove(key);
        }

        /// <summary>Drops control characters except line breaks, trims, and caps the length.</summary>
        private static string Clean(string text, int max)
        {
            text = new string((text ?? "").Where(c => !char.IsControl(c) || c == '\n' || c == '\r').ToArray()).Trim();
            return text.Length > max ? text[..max] : text;
        }
    }
}
