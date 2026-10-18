using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// The zone's part of groups across worlds (TASKS B.9, social-service.md §10), owned by
    /// <see cref="RemoteSocialService"/> and reached through <see cref="SocialServices.Groups"/>:
    /// <list type="bullet">
    /// <item>Requests: the client's GroupsBase invite / join / leave / kick / disband / set owner go to the service
    /// (ClientRequestForward family 0x2B, the client's own sub), an invite or join right after the requester's
    /// GroupMemberRow.</item>
    /// <item>Rows: <see cref="Groups.Update"/> publishes the party-frame record of every grouped player on this world
    /// when it changed (once a second at most, GroupMemberRow 0x2B / 0x10001).</item>
    /// <item>State: the service's GroupStateForZone (0x2B / 0x10002) is kept per character (<see cref="Of"/>); what it
    /// changes in the game (the "join a group" goal, telling a client its group is gone after a service restart, a fresh
    /// row) runs on the world thread (<see cref="Groups.ServiceState"/>).</item>
    /// </list>
    /// The client packets about the group (popups, results, party window, member rows) come from the service through
    /// this world's link (TunnelToExternal / List) like every other social packet.
    /// </summary>
    public sealed class RemoteGroups : IGroupService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly Func<ISocialMessage, bool> send;
        private readonly Func<WorldSession, ulong?> connectionOf;
        private readonly Func<bool> connected;
        private readonly Action<Action> onWorldThread;
        private readonly uint worldId;

        private readonly object sync = new();
        /// <summary>GroupStateForZone per character hosted here (absent = not grouped).</summary>
        private readonly Dictionary<ulong, GroupView> groups = new();

        public RemoteGroups(Func<ISocialMessage, bool> send, Func<WorldSession, ulong?> connectionOf, Func<bool> connected,
            Action<Action> onWorldThread, uint worldId)
        {
            this.send = send;
            this.connectionOf = connectionOf;
            this.connected = connected;
            this.onWorldThread = onWorldThread;
            this.worldId = worldId;
        }

        public bool IsConnected => connected();

        public bool Request(WorldSession session, byte sub, byte[] body, byte[] memberRecord, int locationOffset)
        {
            if (connectionOf(session) is not ulong connection)
                return false;
            if (memberRecord != null && !PublishRow(session, memberRecord, locationOffset))
                return false;
            return send(new ClientRequestForward
            {
                Family = GroupMessages.GroupFamily, Sub = sub, ConnectionId = connection, CharacterGuid = session.PlayerGuid,
                Body = body ?? Array.Empty<byte>()
            });
        }

        public bool PublishRow(WorldSession session, byte[] memberRecord, int locationOffset)
            => send(new GroupMemberRow
            {
                CharacterGuid = session.PlayerGuid, WorldId = worldId, ZoneId = session.ZoneId, LocationOffset = (uint)locationOffset,
                Record = memberRecord
            });

        public GroupView Of(ulong character)
        {
            lock (sync)
                return groups.TryGetValue(character, out GroupView view) ? view : null;
        }

        public IReadOnlyList<ulong> GroupedHere()
        {
            lock (sync)
                return groups.Keys.ToList();
        }

        /// <summary>GroupStateForZone from the service (link thread): the cache now, the game side on the world thread.</summary>
        internal void State(GroupStateForZone state)
        {
            uint previous;
            lock (sync)
            {
                previous = groups.TryGetValue(state.CharacterGuid, out GroupView old) ? old.Id : 0;
                if (state.GroupId == 0)
                    groups.Remove(state.CharacterGuid);
                else
                    groups[state.CharacterGuid] = new GroupView(state.GroupId, state.Leader, state.Members.ToArray());
            }
            log.Debug($"Group state for {state.CharacterGuid:X}: group {state.GroupId} ({state.Members.Count} members), flags {state.Flags}.");
            onWorldThread(() => Groups.ServiceState(state.CharacterGuid, previous, state.GroupId, state.Flags));
        }

        /// <summary>The character left this world: its group is no longer this zone's business (the service keeps it).</summary>
        internal void Forget(ulong character)
        {
            lock (sync)
                groups.Remove(character);
        }
    }
}
