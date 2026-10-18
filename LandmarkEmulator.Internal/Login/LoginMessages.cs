using LandmarkEmulator.Internal.Social;

namespace LandmarkEmulator.Internal.Login
{
    /// <summary>
    /// Gateway ↔ login service (the auth server's internal port, LOGIN_INTERNAL_ADDRESS, default auth:20210;
    /// trace/architecture/gateway-service.md §3.7). Live's handover between its login server and gateways is not known
    /// (L72: an "expected connections" mode existed in the client's UdpLibrary), so these are ours: the gateway asks the
    /// login service to consume a client's single-use server ticket instead of reading the auth database itself, and
    /// asks it for a relog session when it has to send a client back to character select. ServicePacket family
    /// <see cref="LoginFamily"/> (0xF6, the client's CharacterSelectSession area), subs from 0x10001 up (ours).
    /// </summary>
    public static class LoginMessages
    {
        public const ushort LoginFamily = 0xF6;
        public const uint TicketConsumeSub = 0x10001, TicketConsumeReplySub = 0x10002, RelogSessionSub = 0x10003, RelogSessionReplySub = 0x10004;
        /// <summary>World channel ↔ zone (world-channel-service.md §6.5): NoteRelogin 0x10060 (the spec's id), ZoneRelogTag ours.</summary>
        public const uint NoteReloginSub = 0x10060, ZoneRelogTagSub = 0x10063;

        /// <summary>The login message in a service packet, or null.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            if (packet.Family != LoginFamily)
                return null;
            var r = new InternalReader(packet.Body);
            return packet.Sub switch
            {
                TicketConsumeSub => TicketConsume.Read(r),
                TicketConsumeReplySub => TicketConsumeReply.Read(r),
                RelogSessionSub => RelogSession.Read(r),
                RelogSessionReplySub => RelogSessionReply.Read(r),
                NoteReloginSub => NoteRelogin.Read(r),
                ZoneRelogTagSub => ZoneRelogTag.Read(r),
                _ => null
            };
        }
    }

    /// <summary>
    /// TicketConsume (gateway → login): u32 requestId, string ticket, u64 characterGuid, then (since 2026-10-17) u32 world.
    /// The ticket is cleared (single use) and the character must belong to its account. The trailing world is read only
    /// when present: a gateway built before it sends none (world 0 = not known, as before), and a login service built
    /// before it ignores the extra bytes, so a mixed deploy still logs in (review 2026-10-18 #9).
    /// </summary>
    public sealed class TicketConsume : ISocialMessage
    {
        public ushort Family => LoginMessages.LoginFamily;
        public uint Sub => LoginMessages.TicketConsumeSub;
        public uint RequestId { get; init; }
        public string Ticket { get; init; } = "";
        public ulong CharacterGuid { get; init; }
        /// <summary>The world the gateway serves (zone_server id; 0 = not known): a ticket for another world is refused.</summary>
        public uint WorldId { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).Str(Ticket).U64(CharacterGuid).U32(WorldId);
        public static TicketConsume Read(InternalReader r)
            => new() { RequestId = r.U32(), Ticket = r.Str(), CharacterGuid = r.U64(), WorldId = r.Remaining >= 4 ? r.U32() : 0 };
    }

    /// <summary>
    /// TicketConsumeReply (login → gateway): u32 requestId, bool ok, u64 accountId, string accountName, u64 characterGuid,
    /// string characterName, u32 zoneId (the character's landscape). Not ok: unknown or used ticket, or not their
    /// character.
    /// </summary>
    public sealed class TicketConsumeReply : ISocialMessage
    {
        public ushort Family => LoginMessages.LoginFamily;
        public uint Sub => LoginMessages.TicketConsumeReplySub;
        public uint RequestId { get; init; }
        public bool Ok { get; init; }
        public ulong AccountId { get; init; }
        public string AccountName { get; init; } = "";
        public ulong CharacterGuid { get; init; }
        public string CharacterName { get; init; } = "";
        public uint ZoneId { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).Bool(Ok).U64(AccountId).Str(AccountName).U64(CharacterGuid)
            .Str(CharacterName).U32(ZoneId);
        public static TicketConsumeReply Read(InternalReader r) => new()
        {
            RequestId = r.U32(), Ok = r.Bool(), AccountId = r.U64(), AccountName = r.Str(), CharacterGuid = r.U64(),
            CharacterName = r.Str(), ZoneId = r.U32()
        };
    }

    /// <summary>
    /// RelogSession (gateway or world channel → login): u32 requestId, u64 accountId (0 = the account of
    /// <see cref="CharacterGuid"/>), u64 characterGuid, string key ("" = the login service makes one) — a login session
    /// key for the account, saved as its session key (as a logout does), for CharacterSelectSessionResponse 0xF7.
    /// </summary>
    public sealed class RelogSession : ISocialMessage
    {
        public ushort Family => LoginMessages.LoginFamily;
        public uint Sub => LoginMessages.RelogSessionSub;
        public uint RequestId { get; init; }
        public ulong AccountId { get; init; }
        public ulong CharacterGuid { get; init; }
        public string Key { get; init; } = "";

        public void Write(InternalWriter w) => w.U32(RequestId).U64(AccountId).U64(CharacterGuid).Str(Key);
        public static RelogSession Read(InternalReader r) => new() { RequestId = r.U32(), AccountId = r.U64(), CharacterGuid = r.U64(), Key = r.Str() };
    }

    /// <summary>RelogSessionReply (login → requester): u32 requestId, string sessionKey ("" = none), u64 accountId.</summary>
    public sealed class RelogSessionReply : ISocialMessage
    {
        public ushort Family => LoginMessages.LoginFamily;
        public uint Sub => LoginMessages.RelogSessionReplySub;
        public uint RequestId { get; init; }
        public string SessionKey { get; init; } = "";
        public ulong AccountId { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId).Str(SessionKey).U64(AccountId);
        public static RelogSessionReply Read(InternalReader r) => new() { RequestId = r.U32(), SessionKey = r.Str(), AccountId = r.U64() };
    }

    /// <summary>
    /// NoteRelogin (world channel → zone, 0xF6/0x10060): u64 accountId, string sessionKey — the account is going back to
    /// character select with this key; the zone remembers it so the relogin keeps the client's cached item list
    /// (ItemDefinitionManager.NoteRelogin).
    /// </summary>
    public sealed class NoteRelogin : ISocialMessage
    {
        public ushort Family => LoginMessages.LoginFamily;
        public uint Sub => LoginMessages.NoteReloginSub;
        public ulong AccountId { get; init; }
        public string SessionKey { get; init; } = "";

        public void Write(InternalWriter w) => w.U64(AccountId).Str(SessionKey);
        public static NoteRelogin Read(InternalReader r) => new() { AccountId = r.U64(), SessionKey = r.Str() };
    }

    /// <summary>
    /// ZoneRelogTag (ours, zone → world channel, 0xF6/0x10063): string tag — the start every relogin key of this zone has
    /// ("R" + world + item-list fingerprint, ItemDefinitionManager.RelogTag); sent when the world channel connects.
    /// </summary>
    public sealed class ZoneRelogTag : ISocialMessage
    {
        public ushort Family => LoginMessages.LoginFamily;
        public uint Sub => LoginMessages.ZoneRelogTagSub;
        public string Tag { get; init; } = "";

        public void Write(InternalWriter w) => w.Str(Tag);
        public static ZoneRelogTag Read(InternalReader r) => new() { Tag = r.Str() };
    }
}
