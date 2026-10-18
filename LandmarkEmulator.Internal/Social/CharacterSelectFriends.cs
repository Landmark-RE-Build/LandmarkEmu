using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// One friend of a character as character select needs it: who, and where they are when online. The login server
    /// turns it into the client's Friends::Friend record of the character payload (AuthServer CharacterSelectInfoReply,
    /// reader FUN_1430ad9e0); the client counts each world's online friends from it (FUN_1430c43e0, column FriendsOnline).
    /// </summary>
    public sealed record CharacterSelectFriend(ulong Guid, string Name, bool Online, uint WorldId, uint ZoneId)
    {
        public void Write(InternalWriter w) => w.U64(Guid).Str(Name ?? "").Bool(Online).U32(WorldId).U32(ZoneId);
        public static CharacterSelectFriend Read(InternalReader r) => new(r.U64(), r.Str(), r.Bool(), r.U32(), r.U32());
    }

    /// <summary>
    /// CharacterSelectFriendsQuery (ours, 0x1E/0x10008, login → social): the friends lists of an account's characters for
    /// its character select. Body: u32 requestId, u32 n, n × u64 characterGuid (at most <see cref="MaxCharacters"/>).
    /// </summary>
    public sealed class CharacterSelectFriendsQuery : ISocialMessage
    {
        public const int MaxCharacters = 64;

        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.CharacterSelectFriendsQuerySub;
        public uint RequestId { get; init; }
        public IReadOnlyList<ulong> Characters { get; init; } = Array.Empty<ulong>();

        public void Write(InternalWriter w)
        {
            w.U32(RequestId).U32((uint)Characters.Count);
            foreach (ulong guid in Characters)
                w.U64(guid);
        }

        public static CharacterSelectFriendsQuery Read(InternalReader r)
        {
            uint id = r.U32(), n = r.U32();
            SocialMessages.CheckCount(n, MaxCharacters, "characters");
            var list = new List<ulong>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add(r.U64());
            return new() { RequestId = id, Characters = list };
        }
    }

    /// <summary>
    /// CharacterSelectFriendsReply (ours, 0x1E/0x10009, social → login): u32 requestId, u32 n, n × {u64 characterGuid,
    /// u32 m, m × <see cref="CharacterSelectFriend"/>}. Every character asked for is answered (an empty list when it has
    /// no friends).
    /// </summary>
    public sealed class CharacterSelectFriendsReply : ISocialMessage
    {
        /// <summary>The friends list's own limit (FriendService.MaxFriends).</summary>
        public const int MaxFriends = 200;

        public ushort Family => SocialMessages.CommandFamily;
        public uint Sub => SocialMessages.CharacterSelectFriendsReplySub;
        public uint RequestId { get; init; }
        public IReadOnlyDictionary<ulong, IReadOnlyList<CharacterSelectFriend>> Friends { get; init; }
            = new Dictionary<ulong, IReadOnlyList<CharacterSelectFriend>>();

        public void Write(InternalWriter w)
        {
            w.U32(RequestId).U32((uint)Friends.Count);
            foreach ((ulong character, IReadOnlyList<CharacterSelectFriend> friends) in Friends)
            {
                w.U64(character).U32((uint)friends.Count);
                foreach (CharacterSelectFriend friend in friends)
                    friend.Write(w);
            }
        }

        public static CharacterSelectFriendsReply Read(InternalReader r)
        {
            uint id = r.U32(), n = r.U32();
            SocialMessages.CheckCount(n, CharacterSelectFriendsQuery.MaxCharacters, "characters");
            var map = new Dictionary<ulong, IReadOnlyList<CharacterSelectFriend>>();
            for (uint i = 0; i < n; i++)
            {
                ulong character = r.U64();
                uint m = r.U32();
                SocialMessages.CheckCount(m, MaxFriends, "friends");
                var friends = new List<CharacterSelectFriend>((int)m);
                for (uint j = 0; j < m; j++)
                    friends.Add(CharacterSelectFriend.Read(r));
                map[character] = friends;
            }
            return new() { RequestId = id, Friends = map };
        }
    }

    /// <summary>
    /// The login server's link to the social service (SOCIAL_ADDRESS, as the worlds dial it): asks for the friends lists
    /// of the characters on the character select screen (<see cref="CharacterSelectFriendsQuery"/>). The service answers
    /// on the same link; a request waits at most <see cref="Timeout"/> and an unreachable service gives null, so character
    /// select never waits long on it.
    /// </summary>
    public sealed class SocialFriendsClient : IDisposable
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        private readonly InternalDialer dialer;
        private readonly Action<string> log;
        private readonly object sync = new();
        private InternalLink link;
        private int nextRequest;
        private readonly Dictionary<uint, TaskCompletionSource<CharacterSelectFriendsReply>> waiting = new();

        public SocialFriendsClient(string address, Action<string> log)
        {
            this.log = log ?? (_ => { });
            dialer = new InternalDialer(address.Trim());
            dialer.Connected += newLink =>
            {
                newLink.Received += (_, message) => OnReceived(message);
                newLink.Closed += (l, error) =>
                {
                    lock (sync)
                        if (link == l)
                            link = null;
                    this.log($"Social service link lost{(error != null ? ": " + error.Message : "")}; reconnecting.");
                };
                lock (sync)
                    link = newLink;
                this.log("Social service connected (character select friends).");
            };
            dialer.Start();
        }

        public void Dispose() => dialer.Dispose();

        private void OnReceived(IInternalMessage message)
        {
            // The service also sends every link its status page (ServiceStatus); only the replies matter here.
            if (message is not ServicePacket { Family: SocialMessages.CommandFamily, Sub: SocialMessages.CharacterSelectFriendsReplySub } packet)
                return;
            CharacterSelectFriendsReply reply;
            try
            {
                reply = CharacterSelectFriendsReply.Read(new InternalReader(packet.Body));
            }
            catch (Exception e) when (e is System.IO.InvalidDataException or System.IO.EndOfStreamException or ArgumentException)
            {
                log($"Unreadable CharacterSelectFriendsReply: {e.Message}");
                return;
            }
            TaskCompletionSource<CharacterSelectFriendsReply> request;
            lock (sync)
                waiting.Remove(reply.RequestId, out request);
            request?.TrySetResult(reply);
        }

        /// <summary>The characters' friends (character guid → friends), or null when the service didn't answer in time.</summary>
        public async Task<IReadOnlyDictionary<ulong, IReadOnlyList<CharacterSelectFriend>>> GetFriends(IEnumerable<ulong> characters)
        {
            List<ulong> asked = characters.Distinct().Take(CharacterSelectFriendsQuery.MaxCharacters).ToList();
            var request = new TaskCompletionSource<CharacterSelectFriendsReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            uint id;
            bool sent;
            lock (sync)
            {
                id = (uint)Interlocked.Increment(ref nextRequest);
                waiting[id] = request;
                sent = link != null && link.Send(new CharacterSelectFriendsQuery { RequestId = id, Characters = asked }.ToPacket());
            }
            if (sent && await Task.WhenAny(request.Task, Task.Delay(Timeout)) == request.Task)
                return request.Task.Result.Friends;
            lock (sync)
                waiting.Remove(id);
            log(sent ? "Social service did not answer the character select friends in time." : "Social service unreachable (character select friends).");
            return null;
        }
    }
}
