using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// The zone's part of mail while the mailboxes live in the social service (Internal/Social/MailMessages.cs,
    /// social-service.md §5.1: "zones keep the inventory / coin side of mail — escrow on compose, delivery on consume").
    /// <list type="bullet">
    /// <item>Inbox, read, delete and take-attachments requests are handed on whole.</item>
    /// <item>Attach-to-compose (0x15) is answered here: it only checks the bag (Mail.Attach).</item>
    /// <item>Compose (1): read and checked against the bag here (Mail.ReadCompose / CheckBag), then the items and coins
    /// leave the bag into escrow and ComposeRequestStage2 goes to the service, which answers the client. Stage3 drops
    /// the escrow; ComposeResponseReturnAttachments gives it back — to the bag, or, when the sender has left this
    /// world, as a returned mail. While the service can't be reached nothing is taken and the client gets result 3
    /// "Mail service is unavailable". A compose in escrow when the link drops is sent again on reconnect (the service
    /// stores it once, by token).</item>
    /// <item>ConsumeAttachmentZoneRequest: items and coins go to the player, cash on delivery is charged, and the
    /// result per slot goes back (ConsumeAttachmentZoneResponse).</item>
    /// <item>MailReadData: the reading window's packet, with item records (Mail.SendRead).</item>
    /// <item>System mail from the zone (vendors, upkeep, Player Studio, returns) goes as 0xF8/0x16; held and sent on
    /// reconnect while the service is away.</item>
    /// </list>
    /// Inventory changes run on the world thread (<see cref="Update"/>): the link's reading task only queues them.
    /// Known limit: escrow and the held messages live in memory — a world that stops between taking a compose's items
    /// and the service's answer loses them (a window of one round trip).
    /// </summary>
    internal sealed class RemoteMail
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(1);

        // Results the zone produces for the service (the client's codes).
        private const uint CurrencyFull = 0x1E, InsufficientFundsForCod = 0x20;

        private sealed class Escrow
        {
            public MailComposeStage2 Message;
            /// <summary>The bag items held, with the wear each keeps (given back as they were if the mail is refused).</summary>
            public List<Mail.MailItem> Items;
            public uint Coins;
        }

        private readonly Func<ISocialMessage, bool> send;
        private readonly Func<WorldSession, ulong?> connectionOf;
        private readonly Func<ulong, WorldSession> sessionOf;

        private readonly object sync = new();
        private readonly Dictionary<ulong, Escrow> escrow = new();
        /// <summary>Messages that could not be sent (system mail, hand-over results): sent first on reconnect.</summary>
        private readonly List<ISocialMessage> held = new();
        private readonly Dictionary<uint, TaskCompletionSource<bool>> queries = new();
        private readonly ConcurrentQueue<Action> worldQueue = new();
        private int nextQuery;
        /// <summary>Compose and system-mail tokens: unique per process, and with the start time in the high bits not reused
        /// by a restarted world.</summary>
        private static long nextToken = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 16;

        public RemoteMail(Func<ISocialMessage, bool> send, Func<WorldSession, ulong?> connectionOf, Func<ulong, WorldSession> sessionOf)
        {
            this.send = send;
            this.connectionOf = connectionOf;
            this.sessionOf = sessionOf;
        }

        /// <summary>World thread: runs what the link queued.</summary>
        public void Update()
        {
            while (worldQueue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    log.Error(e, "Mail work from the social service failed.");
                }
            }
        }

        // ---- From the client --------------------------------------------------------------------------------------------

        /// <summary>A MailBase request from a player on this world (World thread).</summary>
        public void Handle(WorldSession session, MailRequest request, Action<WorldSession, uint, byte[]> forward)
        {
            try
            {
                switch (request.SubOpcode)
                {
                    case Mail.ComposeRequest:
                        Compose(session, new GamePacketReader(request.Body));
                        break;
                    case Mail.AttachItemRequest:
                        Mail.Attach(session, request.Body);
                        break;
                    case MailMessages.InboxListRequest:
                    case MailMessages.ReadMessageRequest:
                    case MailMessages.DeleteMessageRequest:
                    case MailMessages.ConsumeAttachmentRequest:
                        if (connectionOf(session) is null)
                            Unavailable(session, request.SubOpcode, request.Body);
                        else
                            forward(session, request.SubOpcode, request.Body);
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

        /// <summary>Inbox list result "mail service unavailable" (the client's dispatcher FUN_1432c9770 case 7 tests 0x13).</summary>
        private const uint InboxUnavailable = 0x13, ReadUnavailable = 0x15, DeleteUnavailable = 0x17, ConsumeUnavailable = 0x1B;
        /// <summary>Client text Mail.Error.Inbox.ServiceUnavailable "Mail service is unavailable. Please try again later."</summary>
        private const uint InboxUnavailableText = 562285;

        /// <summary>
        /// The service can't be reached: the request is answered here with the client's own "mail service unavailable"
        /// results (social-service.md §5.6; result table 0x14521a980, FUN_143584fd0 maps 0x13 / 0x15 / 0x17 / 0x18 /
        /// 0x1B to text 562285): read 0x15 (the reading window shows it), delete 0x17 and take 0x1B (the client prints
        /// it). The inbox window ignores its result field (FUN_14358a7d0) and would open empty, so the inbox answer
        /// (result 0x13, no rows) comes with the same text as a system line.
        /// </summary>
        private static void Unavailable(WorldSession session, uint sub, byte[] body)
        {
            var reader = new GamePacketReader(body);
            switch (sub)
            {
                case MailMessages.InboxListRequest:
                    Mail.Send(session, Mail.InboxListResponse, w =>
                    {
                        w.Write(InboxUnavailable);
                        w.Write(session.PlayerGuid);
                        w.WriteUShort(1);
                        w.Write(0u);
                    });
                    SystemMessages.Show(session, InboxUnavailableText);
                    break;
                case MailMessages.ReadMessageRequest:
                    Mail.SendRead(session, new MailReadData { MailId = reader.ReadULong(), Result = ReadUnavailable });
                    break;
                case MailMessages.DeleteMessageRequest:
                {
                    ulong id = reader.ReadULong();
                    Mail.Send(session, Mail.DeleteMessageResponse, w =>
                    {
                        w.Write(id);
                        w.Write(DeleteUnavailable);
                    });
                    break;
                }
                case MailMessages.ConsumeAttachmentRequest:
                {
                    ulong mail = reader.ReadULong(), attachment = reader.ReadULong();
                    Mail.Send(session, Mail.ConsumeAttachmentResponse, w =>
                    {
                        w.Write(mail);
                        w.Write(attachment);
                        w.Write(ConsumeUnavailable);
                    });
                    break;
                }
            }
            log.Info($"{session.CharacterName}: mail request 0x{sub:X} while the social service is unreachable (answered unavailable).");
        }

        private void Compose(WorldSession session, GamePacketReader reader)
        {
            if (connectionOf(session) is not ulong connection)
            {
                Mail.Reply(session, Mail.ServiceUnavailable);
                return;
            }
            uint check = Mail.ReadCompose(reader, out Mail.ComposeDraft draft);
            // The Feedback button's mail names its recipient by guid only (Mail.ResolveRecipients); the service takes names.
            if (check == Mail.Success)
                Mail.ResolveRecipients(draft, Mail.CharacterNameOf);
            if (check == Mail.Success && draft.Coins > session.Coins)
                check = Mail.InsufficientFunds;
            List<(uint Definition, uint Count)> items = null;
            if (check == Mail.Success)
                check = Mail.CheckBag(session, draft, out items);
            // A trial key goes to a friend, not to another character of the sender's own account (Membership; the names
            // this world's character database knows are checked).
            if (check == Mail.Success)
                check = Membership.CheckTrialKeyMail(session, draft.Names, items);
            if (check != Mail.Success)
            {
                Mail.Reply(session, check);
                return;
            }

            // Into escrow: coins first, then the items (all or nothing).
            if (draft.Coins > 0 && !session.ChangeCoins(-draft.Coins))
            {
                Mail.Reply(session, Mail.InsufficientFunds);
                return;
            }
            // The very stacks attached leave the bag (Mail.TakeItems: all or nothing), so the mail carries their wear.
            List<Mail.MailItem> held = draft.Items.ToList();
            if (!Mail.TakeItems(session, held))
            {
                GiveBack(session, new List<Mail.MailItem>(), draft.Coins);
                Mail.Reply(session, Mail.ItemGone);
                return;
            }

            var attachments = new List<MailAttachment>();
            // A mailed trial key arrives as its NO_TRADE copy (Membership.AsMailed); the escrow keeps the sender's own copy
            // to give back if the mail is refused. Each item keeps its wear (audit-wiki-gaps #1).
            foreach (Mail.MailItem item in held)
                attachments.Add(new MailAttachment((uint)attachments.Count, MailAttachmentType.Item, Membership.AsMailed(item.Definition),
                    item.Count, item.Wear));
            if (draft.Coins > 0)
                attachments.Add(new MailAttachment((uint)attachments.Count, MailAttachmentType.Coins, 0, draft.Coins));
            if (draft.Cod > 0)
                attachments.Add(new MailAttachment((uint)attachments.Count, MailAttachmentType.CashOnDelivery, 0, draft.Cod));
            var message = new MailComposeStage2
            {
                Token = (ulong)Interlocked.Increment(ref nextToken), ConnectionId = connection, SenderGuid = session.PlayerGuid,
                SenderName = session.CharacterName ?? "", Recipients = draft.Names, Subject = draft.Subject, Body = draft.Body,
                Attachments = attachments
            };
            lock (sync)
                escrow[message.Token] = new Escrow { Message = message, Items = held, Coins = draft.Coins };
            if (!send(message))
            {
                lock (sync)
                    escrow.Remove(message.Token);
                GiveBack(session, held, draft.Coins);
                Mail.Reply(session, Mail.ServiceUnavailable);
                return;
            }
            log.Debug($"{session.CharacterName}: compose {message.Token:X} to the social service ({held.Count} item(s), {draft.Coins} coins in escrow).");
        }

        private static void GiveBack(WorldSession session, IEnumerable<Mail.MailItem> items, uint coins)
        {
            foreach (Mail.MailItem item in items)
                session.GiveItem(item.Definition, item.Count, new Inventory.ItemState(item.Wear));
            if (coins > 0)
                session.ChangeCoins(coins);
        }

        // ---- From the service ---------------------------------------------------------------------------------------------

        /// <summary>Stage3 / ComposeResponseReturnAttachments (link thread).</summary>
        public void ComposeAnswered(MailComposeResult result)
        {
            Escrow held;
            lock (sync)
                if (!escrow.Remove(result.Token, out held))
                    return;
            if (result.Stored)
                return;
            worldQueue.Enqueue(() =>
            {
                if (held.Items.Count == 0 && held.Coins == 0)
                    return;
                if (sessionOf(held.Message.SenderGuid) is WorldSession sender)
                {
                    // A relog that is replacing the sender's session holds the inventory and wallet that will be saved
                    // (Network.ZoneSessions.ReplacementOf, review 2026-10-18b #4).
                    sender = Network.ZoneSessions.ReplacementOf(sender) ?? sender;
                    GiveBack(sender, held.Items, held.Coins);
                    log.Info($"{sender.CharacterName}: refused mail (result 0x{result.Result:X}), attachments given back.");
                    return;
                }
                // The sender left this world meanwhile: the attachments go back to them by mail.
                SystemMail(held.Message.SenderGuid, "Mail", Mail.Clean("Returned: " + held.Message.Subject, 128),
                    "Your mail could not be delivered; its attachments are returned.", held.Coins,
                    held.Items.Select(i => (i.Definition, i.Count)));
            });
        }

        /// <summary>ConsumeAttachmentZoneRequest (link thread → world thread).</summary>
        public void HandOver(MailConsumeZoneRequest request) => worldQueue.Enqueue(() =>
        {
            WorldSession session = sessionOf(request.CharacterGuid);
            var results = new List<(uint Slot, uint Result)>();
            foreach (MailAttachment a in request.Attachments)
            {
                uint result = Mail.Success;
                if (session == null)
                    result = Mail.ServiceUnavailable;          // the player left: nothing handed over, nothing marked taken
                else if (a.Type == MailAttachmentType.CashOnDelivery)
                    result = session.ChangeCoins(-a.Count) ? Mail.Success : InsufficientFundsForCod;
                else if (a.Type == MailAttachmentType.Coins)
                    result = session.ChangeCoins(a.Count) ? Mail.Success : CurrencyFull;
                else
                    session.GiveItem(a.Definition, a.Count, new Inventory.ItemState(a.Wear));
                results.Add((a.Slot, result));
            }
            SendOrHold(new MailConsumeZoneResponse
            {
                Token = request.Token, CharacterGuid = request.CharacterGuid, MailId = request.MailId, Results = results
            });
        });

        /// <summary>MailReadData (link thread → world thread): the reading window's packet.</summary>
        public void Read(MailReadData data) => worldQueue.Enqueue(() =>
        {
            if (sessionOf(data.CharacterGuid) is WorldSession session)
                Mail.SendRead(session, data);
        });

        public void QueryAnswered(MailAttachmentReply reply)
        {
            TaskCompletionSource<bool> waiting;
            lock (sync)
                queries.Remove(reply.RequestId, out waiting);
            waiting?.TrySetResult(reply.Found);
        }

        /// <summary>The link is back: composes still in escrow and held messages go (again).</summary>
        public void Reconnected()
        {
            List<ISocialMessage> again;
            lock (sync)
            {
                again = held.ToList();
                held.Clear();
                again.AddRange(escrow.Values.Select(e => (ISocialMessage)e.Message));
            }
            for (int i = 0; i < again.Count; i++)
                if (!send(again[i]))
                {
                    // Lost again: the rest waits for the next reconnect (composes stay in escrow anyway).
                    lock (sync)
                        held.AddRange(again.Skip(i).Where(m => m is not MailComposeStage2));
                    return;
                }
            if (again.Count > 0)
                log.Info($"Mail: {again.Count} message(s) sent again after the social service came back.");
        }

        // ---- From the zone ------------------------------------------------------------------------------------------------

        /// <summary>A mail from the game; held while the service is away.</summary>
        public void SystemMail(ulong recipient, string from, string subject, string body, uint coins,
            IEnumerable<(uint Item, uint Count)> items = null, uint kind = 0)
        {
            var attachments = new List<MailAttachment>();
            if (coins > 0)
                attachments.Add(new MailAttachment(0, MailAttachmentType.Coins, 0, coins));
            if (items != null)
                foreach ((uint item, uint count) in items.Where(i => i.Count > 0))
                    attachments.Add(new MailAttachment((uint)attachments.Count, MailAttachmentType.Item, item, count));
            SendOrHold(new SystemMail
            {
                Token = (ulong)Interlocked.Increment(ref nextToken), RecipientGuid = recipient, Kind = kind, From = from ?? "",
                Subject = subject ?? "",
                // The client's letter texts carry <br> line breaks, but the mail window escapes markup in bodies: plain
                // line breaks instead (as Mail.SendSystemMail).
                Body = (body ?? "").Replace("<br>", "\n").Replace("<BR>", "\n"),
                Attachments = attachments
            });
        }

        /// <summary>
        /// Does the player have a mail still holding an item of this definition? Asked of the service, waiting at most
        /// <see cref="QueryTimeout"/>; true when it can't be asked (the caller then holds back a replacement item until
        /// the next login, rather than handing out a second one).
        /// </summary>
        public bool HasAttachment(ulong recipient, uint definition)
        {
            var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            uint id;
            lock (sync)
            {
                id = (uint)Interlocked.Increment(ref nextQuery);
                queries[id] = waiting;
            }
            if (!send(new MailAttachmentQuery { RequestId = id, CharacterGuid = recipient, Definition = definition })
                || !waiting.Task.Wait(QueryTimeout))
            {
                lock (sync)
                    queries.Remove(id);
                log.Warn($"Mail attachment query for {recipient:X} unanswered: treated as \"has it\".");
                return true;
            }
            return waiting.Task.Result;
        }

        private void SendOrHold(ISocialMessage message)
        {
            if (!SendOrHoldQuiet(message))
                log.Warn($"Social service unreachable: mail message 0x{message.Sub:X} held until it is back.");
        }

        private bool SendOrHoldQuiet(ISocialMessage message)
        {
            if (send(message))
                return true;
            lock (sync)
                held.Add(message);
            return false;
        }
    }
}
