using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// Mail between a zone (the world server) and the social service (trace/architecture/social-service.md §5.4, §6
    /// step 5). MailBase 0xF8 family, client sub ids in the 0.695 numbering (the server speaks it natively,
    /// docs/protocol.md §1.6). Live's server-only mail ids are known by name only (the 0.695 client id table,
    /// re/out/packet_ids_695.tsv, L8): ComposeRequestStage2 (2), ComposeRequestStage3 (3),
    /// ComposeResponseReturnAttachments (6), ConsumeAttachmentZoneRequest (0x12) / Response (0x13), System (0x17,
    /// cMailPacketIdSystem; 0.589 numbers 2, 3, 5, 0x11, 0x12, 0x16). This server uses those ids for the same steps; the
    /// bodies are ours. The zone and the social service must run the same numbering (one build of both).
    /// <para>
    /// The split (§5.1): the zone keeps the inventory and coin side — it checks the bag items a compose attaches,
    /// takes them and the coins into escrow, and hands them over or back; the service keeps the mailboxes — recipients,
    /// ignore, inbox limits, send limit, storage, expiry, cash on delivery. A read is answered by the zone
    /// (<see cref="MailReadData"/>): the reading window shows full item records, which only the zone (with the game's
    /// item tables) can write.
    /// </para>
    /// </summary>
    public static class MailMessages
    {
        public const ushort MailFamily = 0xF8;

        // Client requests handed on whole (ClientRequestForward, family 0xF8): inbox, read, delete, take attachments.
        public const uint InboxListRequest = 7, ReadMessageRequest = 9, DeleteMessageRequest = 0xB, ConsumeAttachmentRequest = 0x10;

        // Live's server-only ids (names from the client's id table).
        public const uint ComposeStage2Sub = 2, ComposeStage3Sub = 3, ComposeReturnAttachmentsSub = 6,
            ConsumeZoneRequestSub = 0x12, ConsumeZoneResponseSub = 0x13, SystemMailSub = 0x17;

        // Ours.
        public const uint ReadDataSub = 0x10001, AttachmentQuerySub = 0x10002, AttachmentReplySub = 0x10003;

        /// <summary>The mail message in a service packet, or null when it isn't one.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            var r = new InternalReader(packet.Body);
            return packet.Sub switch
            {
                InboxListRequest or ReadMessageRequest or DeleteMessageRequest or ConsumeAttachmentRequest
                    => ClientRequestForward.Read(packet.Sub, r, MailFamily),
                ComposeStage2Sub => MailComposeStage2.Read(r),
                ComposeStage3Sub => MailComposeResult.Read(r, stored: true),
                ComposeReturnAttachmentsSub => MailComposeResult.Read(r, stored: false),
                ConsumeZoneRequestSub => MailConsumeZoneRequest.Read(r),
                ConsumeZoneResponseSub => MailConsumeZoneResponse.Read(r),
                SystemMailSub => SystemMail.Read(r),
                ReadDataSub => MailReadData.Read(r),
                AttachmentQuerySub => MailAttachmentQuery.Read(r),
                AttachmentReplySub => MailAttachmentReply.Read(r),
                _ => null
            };
        }

        internal static void WriteList(InternalWriter w, IReadOnlyList<MailAttachment> list)
        {
            w.U32((uint)list.Count);
            foreach (MailAttachment a in list)
                a.Write(w);
        }

        internal static List<MailAttachment> ReadList(InternalReader r)
        {
            uint n = r.U32();
            SocialMessages.CheckCount(n, 64, "mail attachments");
            var list = new List<MailAttachment>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add(MailAttachment.Read(r));
            return list;
        }
    }

    /// <summary>What a mail attachment slot holds.</summary>
    public enum MailAttachmentType : byte
    {
        /// <summary>Items of one definition.</summary>
        Item = 0,
        /// <summary>Coins sent with the mail (the recipient takes them).</summary>
        Coins = 1,
        /// <summary>Cash on delivery: coins the recipient pays to the sender before taking the items.</summary>
        CashOnDelivery = 2
    }

    /// <summary>
    /// One attachment slot (ours): u32 slot (its place in the mail; the client's attachment id is slot + 1), u8 type,
    /// u32 item definition (0 for coins / COD), u32 count (items, or coins), f32 wear (the item's durability points lost:
    /// a mailed item keeps its condition, audit-wiki-gaps #1; 0 for coins and unworn items).
    /// </summary>
    public readonly record struct MailAttachment(uint Slot, MailAttachmentType Type, uint Definition, uint Count, float Wear = 0f)
    {
        public void Write(InternalWriter w) => w.U32(Slot).U8((byte)Type).U32(Definition).U32(Count).F32(Wear);
        public static MailAttachment Read(InternalReader r) => new(r.U32(), (MailAttachmentType)r.U8(), r.U32(), r.U32(), r.F32());
    }

    /// <summary>
    /// ComposeRequestStage2 (0xF8/2, zone → social): a compose the zone has checked and whose attachments it now holds
    /// in escrow. Body (ours): u64 token (the zone's, unique; the service stores it with the mail so a compose sent again
    /// after a lost link is stored once), u64 connectionId, u64 senderGuid, string senderName, u32 n × string recipient
    /// name, string subject, string body, attachment list.
    /// </summary>
    public sealed class MailComposeStage2 : ISocialMessage
    {
        public ushort Family => MailMessages.MailFamily;
        public uint Sub => MailMessages.ComposeStage2Sub;
        public ulong Token { get; init; }
        public ulong ConnectionId { get; init; }
        public ulong SenderGuid { get; init; }
        public string SenderName { get; init; } = "";
        public IReadOnlyList<string> Recipients { get; init; } = Array.Empty<string>();
        public string Subject { get; init; } = "";
        public string Body { get; init; } = "";
        public IReadOnlyList<MailAttachment> Attachments { get; init; } = Array.Empty<MailAttachment>();

        public void Write(InternalWriter w)
        {
            w.U64(Token).U64(ConnectionId).U64(SenderGuid).Str(SenderName).U32((uint)Recipients.Count);
            foreach (string name in Recipients)
                w.Str(name);
            w.Str(Subject).Str(Body);
            MailMessages.WriteList(w, Attachments);
        }

        public static MailComposeStage2 Read(InternalReader r)
        {
            ulong token = r.U64(), connection = r.U64(), sender = r.U64();
            string senderName = r.Str();
            uint n = r.U32();
            SocialMessages.CheckCount(n, 16, "mail recipients");
            var names = new List<string>((int)n);
            for (uint i = 0; i < n; i++)
                names.Add(r.Str());
            return new()
            {
                Token = token, ConnectionId = connection, SenderGuid = sender, SenderName = senderName, Recipients = names,
                Subject = r.Str(), Body = r.Str(), Attachments = MailMessages.ReadList(r)
            };
        }
    }

    /// <summary>
    /// The service's answer to a compose (social → zone): ComposeRequestStage3 (0xF8/3) when it was stored — the zone
    /// drops its escrow — or ComposeResponseReturnAttachments (0xF8/6) when it was refused — the zone gives the escrow
    /// back. Body (ours): u64 token, u64 senderGuid, u32 result (the client's mail result code). The service has
    /// already sent the client its ComposeResponse.
    /// </summary>
    public sealed class MailComposeResult : ISocialMessage
    {
        public ushort Family => MailMessages.MailFamily;
        public uint Sub => Stored ? MailMessages.ComposeStage3Sub : MailMessages.ComposeReturnAttachmentsSub;
        public bool Stored { get; init; }
        public ulong Token { get; init; }
        public ulong SenderGuid { get; init; }
        public uint Result { get; init; }

        public void Write(InternalWriter w) => w.U64(Token).U64(SenderGuid).U32(Result);
        public static MailComposeResult Read(InternalReader r, bool stored) => new()
        {
            Stored = stored, Token = r.U64(), SenderGuid = r.U64(), Result = r.U32()
        };
    }

    /// <summary>
    /// ConsumeAttachmentZoneRequest (0xF8/0x12, social → zone): hand these attachments of a mail to the player (or, for
    /// cash on delivery, take the coins from them). Body (ours): u64 token, u64 connectionId, u64 characterGuid, u64 mail
    /// id, attachment list.
    /// </summary>
    public sealed class MailConsumeZoneRequest : ISocialMessage
    {
        public ushort Family => MailMessages.MailFamily;
        public uint Sub => MailMessages.ConsumeZoneRequestSub;
        public ulong Token { get; init; }
        public ulong ConnectionId { get; init; }
        public ulong CharacterGuid { get; init; }
        public ulong MailId { get; init; }
        public IReadOnlyList<MailAttachment> Attachments { get; init; } = Array.Empty<MailAttachment>();

        public void Write(InternalWriter w)
        {
            w.U64(Token).U64(ConnectionId).U64(CharacterGuid).U64(MailId);
            MailMessages.WriteList(w, Attachments);
        }

        public static MailConsumeZoneRequest Read(InternalReader r) => new()
        {
            Token = r.U64(), ConnectionId = r.U64(), CharacterGuid = r.U64(), MailId = r.U64(), Attachments = MailMessages.ReadList(r)
        };
    }

    /// <summary>
    /// ConsumeAttachmentZoneResponse (0xF8/0x13, zone → social): what the zone did. Body (ours): u64 token, u64
    /// characterGuid, u64 mail id, u32 n × {u32 slot, u32 result (0 handed over / paid; 0x1E currency full; 0x20 not
    /// enough coins for the COD; 3 player gone)}. Self-contained, so the service can apply it even after it restarted.
    /// </summary>
    public sealed class MailConsumeZoneResponse : ISocialMessage
    {
        public ushort Family => MailMessages.MailFamily;
        public uint Sub => MailMessages.ConsumeZoneResponseSub;
        public ulong Token { get; init; }
        public ulong CharacterGuid { get; init; }
        public ulong MailId { get; init; }
        public IReadOnlyList<(uint Slot, uint Result)> Results { get; init; } = Array.Empty<(uint, uint)>();

        public void Write(InternalWriter w)
        {
            w.U64(Token).U64(CharacterGuid).U64(MailId).U32((uint)Results.Count);
            foreach ((uint slot, uint result) in Results)
                w.U32(slot).U32(result);
        }

        public static MailConsumeZoneResponse Read(InternalReader r)
        {
            ulong token = r.U64(), character = r.U64(), mail = r.U64();
            uint n = r.U32();
            SocialMessages.CheckCount(n, 64, "attachment results");
            var list = new List<(uint, uint)>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add((r.U32(), r.U32()));
            return new() { Token = token, CharacterGuid = character, MailId = mail, Results = list };
        }
    }

    /// <summary>
    /// System mail (0xF8/0x17, cMailPacketIdSystem; zone or another service → social): a mail from the game (vendor
    /// earnings and returns, site upkeep, Player Studio, mail returned to its sender). Body (ours): u64 token (unique per
    /// sender; stored once even when sent again after a lost link), u64 recipientGuid, u32 kind (the client's mail kind
    /// flags), string from, string subject, string body, attachment list (coins and items).
    /// </summary>
    public sealed class SystemMail : ISocialMessage
    {
        public ushort Family => MailMessages.MailFamily;
        public uint Sub => MailMessages.SystemMailSub;
        public ulong Token { get; init; }
        public ulong RecipientGuid { get; init; }
        public uint Kind { get; init; }
        public string From { get; init; } = "";
        public string Subject { get; init; } = "";
        public string Body { get; init; } = "";
        public IReadOnlyList<MailAttachment> Attachments { get; init; } = Array.Empty<MailAttachment>();

        public void Write(InternalWriter w)
        {
            w.U64(Token).U64(RecipientGuid).U32(Kind).Str(From).Str(Subject).Str(Body);
            MailMessages.WriteList(w, Attachments);
        }

        public static SystemMail Read(InternalReader r) => new()
        {
            Token = r.U64(), RecipientGuid = r.U64(), Kind = r.U32(), From = r.Str(), Subject = r.Str(), Body = r.Str(),
            Attachments = MailMessages.ReadList(r)
        };
    }

    /// <summary>
    /// MailReadData (ours, 0xF8/0x10001, social → zone): the answer to a ReadMessageRequest, for the zone to write the
    /// client's ReadMessageResponse (sub 0xA) with full item records. Body: u64 connectionId, u64 characterGuid, u64 mail
    /// id, u32 result (0 or 0x14 not found; nothing more follows a failure), u32 kind, u64 senderGuid, string senderName,
    /// string subject, string body, u64 sent (Unix s), attachment list (only the ones still waiting).
    /// </summary>
    public sealed class MailReadData : ISocialMessage
    {
        public ushort Family => MailMessages.MailFamily;
        public uint Sub => MailMessages.ReadDataSub;
        public ulong ConnectionId { get; init; }
        public ulong CharacterGuid { get; init; }
        public ulong MailId { get; init; }
        public uint Result { get; init; }
        public uint Kind { get; init; }
        public ulong SenderGuid { get; init; }
        public string SenderName { get; init; } = "";
        public string Subject { get; init; } = "";
        public string Body { get; init; } = "";
        public ulong SentAt { get; init; }
        public IReadOnlyList<MailAttachment> Attachments { get; init; } = Array.Empty<MailAttachment>();

        public void Write(InternalWriter w)
        {
            w.U64(ConnectionId).U64(CharacterGuid).U64(MailId).U32(Result);
            if (Result != 0)
                return;
            w.U32(Kind).U64(SenderGuid).Str(SenderName).Str(Subject).Str(Body).U64(SentAt);
            MailMessages.WriteList(w, Attachments);
        }

        public static MailReadData Read(InternalReader r)
        {
            ulong connection = r.U64(), character = r.U64(), mail = r.U64();
            uint result = r.U32();
            if (result != 0)
                return new() { ConnectionId = connection, CharacterGuid = character, MailId = mail, Result = result };
            return new()
            {
                ConnectionId = connection, CharacterGuid = character, MailId = mail, Result = result, Kind = r.U32(),
                SenderGuid = r.U64(), SenderName = r.Str(), Subject = r.Str(), Body = r.Str(), SentAt = r.U64(),
                Attachments = MailMessages.ReadList(r)
            };
        }
    }

    /// <summary>
    /// MailAttachmentQuery (ours, 0xF8/0x10002, zone → social): does the character have a mail still holding an item of
    /// this definition? (Build Site Contents: an item waiting in mail counts as owned.) Body: u32 requestId, u64
    /// characterGuid, u32 definition. Answered with <see cref="MailAttachmentReply"/>.
    /// </summary>
    public sealed class MailAttachmentQuery : ISocialMessage
    {
        public ushort Family => MailMessages.MailFamily;
        public uint Sub => MailMessages.AttachmentQuerySub;
        public uint RequestId { get; init; }
        public ulong CharacterGuid { get; init; }
        public uint Definition { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).U64(CharacterGuid).U32(Definition);
        public static MailAttachmentQuery Read(InternalReader r) => new() { RequestId = r.U32(), CharacterGuid = r.U64(), Definition = r.U32() };
    }

    /// <summary>MailAttachmentReply (ours, 0xF8/0x10003, social → zone): u32 requestId, bool found.</summary>
    public sealed class MailAttachmentReply : ISocialMessage
    {
        public ushort Family => MailMessages.MailFamily;
        public uint Sub => MailMessages.AttachmentReplySub;
        public uint RequestId { get; init; }
        public bool Found { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).Bool(Found);
        public static MailAttachmentReply Read(InternalReader r) => new() { RequestId = r.U32(), Found = r.Bool() };
    }
}
