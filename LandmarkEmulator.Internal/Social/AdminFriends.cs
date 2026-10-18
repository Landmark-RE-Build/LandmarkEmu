using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// The admin friend commands, shared by both places that carry them out — the world's own friends code when the
    /// social service runs in-process (Game/Friends.cs) and the social service (SocialServer/FriendService.cs) when
    /// SOCIAL_ADDRESS is set — so the two give the same answers:
    /// <list type="bullet">
    /// <item>/friend forceadd &lt;target&gt; [source] — AdminCommandPacketForceAddFriendRequest, Command 0x1E / 0x4CF
    /// (0.695 command FUN_1433a5e60 → writer FUN_143389b30, client channel 1 = World);</item>
    /// <item>/friend remove &lt;target&gt; [source] — AdminCommandPacketForceRemoveFriendRequest, 0x1E / 0x4D0 (writer
    /// FUN_143389ce0; the client prints "Removing friend:  %s" itself and expects no answer).</item>
    /// </list>
    /// Body after the u16 sub: identity target (+0x20), identity source (+0xD0) [P]; an identity (FUN_142ddc260) is
    /// u32, u32, u32, string first (StringFixed&lt;16&gt;), string second — the typed name in the first string (normalised by
    /// FUN_143b87120), the rest empty / 0. Without a typed source the command fills in the player's own name (player
    /// vtable +0x60 → +8), so the source is normally the admin; an empty one is still read as the admin.
    /// <para>
    /// The client has no reply for either sub (trace/families/001E_Command.md lists only the two C→S request classes; the
    /// only "case 0x4cf / 0x4d0" in the 0.695 decompile is an HRESULT switch, FUN_1406a2014), so what the players see is the friends-list traffic of a normal add / remove: the source's list
    /// gets FriendBase 0x4D sub 6 Add (the friend's row with its online state) or sub 7 Remove + text 560713, and the
    /// zone of the one listed learns who lists them (friend online / offline notes). Lists are one-way, so the target's
    /// own list does not change, exactly as when they accept a normal request. The admin gets one line with the outcome.
    /// </para>
    /// Live ran these in the shared social service (social-service.md §5.1 item 6, §5.3: World-channel Command subs
    /// 0x4CF-0x4D2 routed to `social`).
    /// </summary>
    public static class AdminFriends
    {
        /// <summary>Command subs (0.695 = 0.589).</summary>
        public const ushort ForceAddSub = 0x4CF, ForceRemoveSub = 0x4D0;

        /// <summary>The longest friends list (ours, as the normal add).</summary>
        public const int MaxFriends = 200;

        /// <summary>The names an admin friend command carries.</summary>
        public sealed record Request(string Target, string Source);

        /// <summary>
        /// Reads the client's body after the u16 sub: identity target, identity source. False when it runs short.
        /// </summary>
        public static bool TryRead(byte[] body, out Request request)
        {
            request = null;
            if (body == null)
                return false;
            try
            {
                var r = new InternalReader(body);
                string target = ReadIdentity(r);
                string source = r.Remaining > 0 ? ReadIdentity(r) : "";
                request = new Request(target, source);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        private static string ReadIdentity(InternalReader r)
        {
            r.U32(); r.U32(); r.U32();
            string first = (r.Str() ?? "").Trim();
            string second = (r.Str() ?? "").Trim();
            return second.Length > 0 ? (first + " " + second).Trim() : first;
        }

        /// <summary>Writes a request body the way the client does (tests, tools).</summary>
        public static byte[] Write(string target, string source)
        {
            var w = new InternalWriter();
            foreach (string name in new[] { target, source })
                w.U32(0).U32(0).U32(0).Str(name ?? "").Str("");
            return w.ToArray();
        }

        /// <summary>
        /// Force-add: may <paramref name="source"/> get <paramref name="target"/> on its list (null = no such living
        /// character)? Added, or why not.
        /// </summary>
        public static AdminFriendOutcome DecideAdd(ulong? source, ulong? target, IReadOnlyCollection<ulong> sourceFriends)
        {
            if (source is not ulong s)
                return AdminFriendOutcome.SourceUnknown;
            if (target is not ulong t)
                return AdminFriendOutcome.TargetUnknown;
            if (s == t)
                return AdminFriendOutcome.Self;
            if (sourceFriends.Contains(t))
                return AdminFriendOutcome.AlreadyListed;
            if (sourceFriends.Count >= MaxFriends)
                return AdminFriendOutcome.ListFull;
            return AdminFriendOutcome.Added;
        }

        /// <summary>
        /// Force-remove: which of the source's friends (guid, name) the typed name means — matched by name,
        /// case-insensitively, as RemoveFriendRequest — or the reason there is none.
        /// </summary>
        public static AdminFriendOutcome DecideRemove<T>(ulong? source, string target, IEnumerable<T> sourceFriends,
            Func<T, string> nameOf, out T friend)
        {
            friend = default;
            if (source == null)
                return AdminFriendOutcome.SourceUnknown;
            string name = (target ?? "").Trim();
            foreach (T f in sourceFriends)
                if (string.Equals(nameOf(f), name, StringComparison.OrdinalIgnoreCase))
                {
                    friend = f;
                    return AdminFriendOutcome.Removed;
                }
            return AdminFriendOutcome.NotOnList;
        }

        /// <summary>The command's word, for logs and lines.</summary>
        public static string Verb(uint sub) => sub == ForceAddSub ? "forceadd" : sub == ForceRemoveSub ? "remove" : $"0x{sub:X}";

        /// <summary>
        /// The admin's line for an outcome. <paramref name="sourceIsAdmin"/>: the list was the admin's own ("your").
        /// </summary>
        public static string Describe(uint sub, AdminFriendOutcome outcome, string target, string source, bool sourceIsAdmin)
        {
            string whose = sourceIsAdmin ? "your" : $"{source}'s";
            return outcome switch
            {
                AdminFriendOutcome.Added => $"Added {target} to {whose} friends list.",
                AdminFriendOutcome.Removed => $"Removed {target} from {whose} friends list.",
                AdminFriendOutcome.NotOnList => $"{target} is not on {whose} friends list: nothing removed.",
                AdminFriendOutcome.AlreadyListed => $"{target} is already on {whose} friends list.",
                AdminFriendOutcome.SourceUnknown => $"/friend {Verb(sub)}: there is no character named \"{source}\".",
                AdminFriendOutcome.TargetUnknown => $"/friend {Verb(sub)}: there is no character named \"{target}\".",
                AdminFriendOutcome.Self => $"/friend {Verb(sub)}: {source} can't be on their own friends list.",
                AdminFriendOutcome.ListFull => $"{(sourceIsAdmin ? "Your" : source + "'s")} friends list is full ({MaxFriends}).",
                AdminFriendOutcome.ServiceUnavailable => $"/friend {Verb(sub)}: the social service is unavailable; nothing changed.",
                AdminFriendOutcome.Unreadable => $"/friend {Verb(sub)}: the command could not be read; nothing changed.",
                _ => $"/friend {Verb(sub)}: the server could not read or save the friends list (see the server log); nothing changed.",
            };
        }
    }

    /// <summary>What an admin friend command did.</summary>
    public enum AdminFriendOutcome : byte
    {
        Added = 1,
        Removed = 2,
        /// <summary>Remove: the target is not on the source's list.</summary>
        NotOnList = 3,
        /// <summary>Add: the target is already on the source's list.</summary>
        AlreadyListed = 4,
        /// <summary>No living character has the source's name.</summary>
        SourceUnknown = 5,
        /// <summary>Add: no living character has the target's name.</summary>
        TargetUnknown = 6,
        /// <summary>Add: target and source are the same character.</summary>
        Self = 7,
        /// <summary>Add: the source's list has <see cref="AdminFriends.MaxFriends"/> entries.</summary>
        ListFull = 8,
        /// <summary>The world could not reach the social service.</summary>
        ServiceUnavailable = 9,
        /// <summary>The client body ran short.</summary>
        Unreadable = 10,
        /// <summary>The friends list could not be read or saved (database error).</summary>
        Failed = 11,
    }

    /// <summary>
    /// AdminFriendResult (ours, 0x1E/0x1000A, social → the admin's zone): the outcome of a force-add / force-remove the
    /// zone handed on (ClientRequestForward 0x1E/0x4CF or 0x4D0), so the zone logs what really happened and tells the
    /// admin. Body: u64 adminConnection, u64 adminGuid, u32 sub (0x4CF / 0x4D0), u8 outcome, string target (the
    /// character's name when found, else as typed), string source (likewise), u64 sourceGuid (0 = not found).
    /// </summary>
    public sealed class AdminFriendResult : ISocialMessage
    {
        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.AdminFriendResultSub;
        public ulong AdminConnection { get; init; }
        public ulong AdminGuid { get; init; }
        public uint CommandSub { get; init; }
        public AdminFriendOutcome Outcome { get; init; }
        public string Target { get; init; } = "";
        public string Source { get; init; } = "";
        public ulong SourceGuid { get; init; }

        public void Write(InternalWriter w) => w.U64(AdminConnection).U64(AdminGuid).U32(CommandSub).U8((byte)Outcome)
            .Str(Target).Str(Source).U64(SourceGuid);

        public static AdminFriendResult Read(InternalReader r) => new()
        {
            AdminConnection = r.U64(), AdminGuid = r.U64(), CommandSub = r.U32(), Outcome = (AdminFriendOutcome)r.U8(),
            Target = r.Str(), Source = r.Str(), SourceGuid = r.U64()
        };
    }
}
