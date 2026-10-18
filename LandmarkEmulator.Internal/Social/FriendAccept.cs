using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>What an accepted friend request changes (<see cref="FriendAccept.Decide"/>).</summary>
    /// <param name="Blocked">One of the two ignores the other: nothing changes, nobody is told.</param>
    /// <param name="AddToAsker">The one who accepted goes on the asker's list.</param>
    /// <param name="AddToAccepter">The asker goes on the list of the one who accepted (mutual requests only).</param>
    /// <param name="AskerFull">The asker's list has no room for them: "Your friends list is full." to the asker.</param>
    /// <param name="AccepterFull">The accepter's list has no room (mutual only): the same line to the accepter.</param>
    public sealed record FriendAcceptPlan(bool Blocked, bool AddToAsker, bool AddToAccepter, bool AskerFull, bool AccepterFull)
    {
        /// <summary>At least one list gets a row (then the asker gets FriendMessage 1 "has accepted your friend request").</summary>
        public bool Changes => AddToAsker || AddToAccepter;
    }

    /// <summary>
    /// The answer to a friend request (ConfirmFriendResponse 0x1E/0x14, accepted), shared by both places that carry it
    /// out — the world's own friends code when the social service runs in-process (Game/Friends.cs) and the social
    /// service (SocialServer/FriendService.cs) — so the two give the same answers.
    /// <para><b>Mutual (live, the default).</b> An accepted request makes the friendship mutual: each of the two gets the
    /// other. [W] wiki "Friends List": the Friends section is "a list of people that you either sent friend requests to or
    /// from whom you accepted friend requests". [P] the 0.695 client never adds a friend by itself: Accept in the Friend
    /// Invite popup (GaqItemFriendInvite) only sends the response, and a row appears only through FriendAdd (0x4D/6,
    /// handler FUN_143542520: adds the row and shows UI.FriendAdded 560711 "[*player*] has been added as a friend."), so
    /// the accepter's list can only get the asker from the server. Setting FriendRequestsMutual
    /// (FRIEND_REQUESTS_MUTUAL) false keeps the old one-way lists: only the asker's list gets the one who accepted.</para>
    /// <para><b>Limits.</b> Each side's list cap (200, ours) counts for its own row: a full side gets no row and is told
    /// "Your friends list is full."; the other side still gets its row. An ignore either way (asker ignores accepter or
    /// accepter ignores asker, checked at the answer: the ignore may be newer than the request) stops the whole accept
    /// with nothing said, as the Steam friends import did ("as long as they aren't ignoring you, and vice-versa",
    /// update notes 7 Aug 2014, forum 45642) [I for the request path].</para>
    /// <para>Removing a friend stays one-sided [I]: the client has a single removal text, 560713 "[*player*] has been
    /// removed from your friends list." (UI.FriendRemoved, shown by the FriendRemove handler to the list's owner), and no
    /// "removed you" text; no source says a removal leaves the other list. Admin /friend forceadd stays one-way (an admin
    /// tool, AdminFriends).</para>
    /// </summary>
    public static class FriendAccept
    {
        /// <summary>
        /// What accepting changes. <paramref name="askerFriends"/> / <paramref name="accepterFriends"/>: the guids on each
        /// list now. <paramref name="max"/>: the longest list (ours, 200).
        /// </summary>
        public static FriendAcceptPlan Decide(bool mutual, ulong asker, ulong accepter, IReadOnlyCollection<ulong> askerFriends,
            IReadOnlyCollection<ulong> accepterFriends, bool askerIgnoresAccepter, bool accepterIgnoresAsker, int max = AdminFriends.MaxFriends)
        {
            if (asker == accepter || askerIgnoresAccepter || accepterIgnoresAsker)
                return new FriendAcceptPlan(true, false, false, false, false);
            askerFriends ??= System.Array.Empty<ulong>();
            accepterFriends ??= System.Array.Empty<ulong>();
            bool askerHas = askerFriends.Contains(accepter);
            bool askerFull = !askerHas && askerFriends.Count >= max;
            bool accepterHas = !mutual || accepterFriends.Contains(asker);
            bool accepterFull = !accepterHas && accepterFriends.Count >= max;
            return new FriendAcceptPlan(false, !askerHas && !askerFull, !accepterHas && !accepterFull, askerFull, accepterFull);
        }
    }
}
