using LandmarkEmulator.WorldServer.Network;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// Groups across worlds as the zone sees them (TASKS B.9, social-service.md §10): only there when the social service
    /// is remote (SOCIAL_ADDRESS, <see cref="SocialServices.Groups"/>); in-process <see cref="Groups"/> keeps its own
    /// groups for this world exactly as before. Implemented by <see cref="RemoteGroups"/> inside
    /// <see cref="RemoteSocialService"/>.
    /// <para>
    /// The service owns membership, invitations, leaders and the GroupsBase packets about them; the zone hands the
    /// client's requests on (<see cref="Request"/>), publishes the party-frame record of the grouped players it hosts
    /// (<see cref="PublishRow"/>), and keeps the group of each of its players (<see cref="Of"/>, from GroupStateForZone)
    /// for what it does itself with the members on this world.
    /// </para>
    /// </summary>
    public interface IGroupService
    {
        /// <summary>Is the link to the service up now?</summary>
        bool IsConnected { get; }

        /// <summary>
        /// Hands a GroupsBase request (invite 1, join 2, leave 4, kick 5, disband 6, set owner 8) on, preceded by the
        /// requester's member record when given (invite and join: a new group needs every member's row); false (nothing
        /// sent) while the service is unreachable or the player has not entered the world.
        /// </summary>
        bool Request(WorldSession session, byte sub, byte[] body, byte[] memberRecord, int locationOffset);

        /// <summary>GroupMemberRow: a grouped player's party-frame record changed; false while unreachable.</summary>
        bool PublishRow(WorldSession session, byte[] memberRecord, int locationOffset);

        /// <summary>The character's group (null = not in one), for characters this world hosts.</summary>
        GroupView Of(ulong character);

        /// <summary>The characters on this world the service has put in a group.</summary>
        IReadOnlyList<ulong> GroupedHere();
    }

    /// <summary>A group as the service last described it to this zone: id, leader, every member (on every world).</summary>
    public sealed record GroupView(uint Id, ulong Leader, IReadOnlyList<ulong> Members);
}
