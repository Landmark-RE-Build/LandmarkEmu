using System;

namespace LandmarkEmulator.Database.Auth.Model
{
    public class AccountModel
    {
        public ulong Id { get; set; }
        public string Username { get; set; }
        public string Salt { get; set; }
        public string Verifier { get; set; }
        public string SessionKey { get; set; }
        public DateTime SessionKeyExpiration { get; set; }
        public string ServerTicket { get; set; }
        /// <summary>When <see cref="ServerTicket"/> was issued (UTC): it is refused after AuthDatabase.TicketLifetime.</summary>
        public DateTime? ServerTicketIssuedAt { get; set; }
        /// <summary>The world (zone_server id) <see cref="ServerTicket"/> was issued for; 0 = any (local test logins only).</summary>
        public uint ServerTicketWorld { get; set; }
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// Membership type as the client keeps it (0.695 player data +0xfc, sent in SendSelfToClient and
        /// PacketMembershipActivation 0x66): 0 none, 1 trial (Lua Ui.IsTrialMember), 2 member (Ui.ShouldShowMemberNagScreen
        /// is false only for 2). Only counts until <see cref="MemberUntil"/>; see <see cref="MemberTypeAt"/>.
        /// trace/0695/agent-membership.md.
        /// </summary>
        public byte MemberType { get; set; }

        /// <summary>When the membership ends (UTC); null = never had one.</summary>
        public DateTime? MemberUntil { get; set; }

        /// <summary>The account's one trial key ("Invite your friends!" letter, 29 Aug 2016) has been mailed to it.</summary>
        public bool TrialKeyMailed { get; set; }

        /// <summary>The account has used a trial key (live: "a one-time free trial") and can't use another.</summary>
        public bool TrialRedeemed { get; set; }
        /// <summary>When the account last got the member Lumen stipend (Game/Membership, every 30 days while a member).</summary>
        public DateTime? MemberStipendAt { get; set; }

        /// <summary>The membership type in force at <paramref name="utcNow"/>: <see cref="MemberType"/> until it ends, then 0.</summary>
        public byte MemberTypeAt(DateTime utcNow) => MemberUntil is DateTime until && until > utcNow ? MemberType : (byte)0;
    }
}
