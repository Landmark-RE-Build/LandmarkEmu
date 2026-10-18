using LandmarkEmulator.Database.Social;
using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Social;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// One world's link to the service. The world dials in (SOCIAL_ADDRESS) and says which world it is with its first
    /// message, PresenceSnapshot (social-service.md §5.3). While the world server is its own gateway (§6 step 4) the
    /// world id is also the gateway id.
    /// </summary>
    public sealed class WorldLink
    {
        public InternalLink Link { get; }
        /// <summary>The world (ZoneServerId), 0 until its PresenceSnapshot arrived.</summary>
        public uint WorldId { get; set; }

        public WorldLink(InternalLink link) => Link = link;

        public override string ToString() => WorldId != 0 ? $"world {WorldId} ({Link.Remote})" : Link.Remote?.ToString() ?? "?";
    }

    /// <summary>
    /// The social service's core: accepts the worlds' links, runs every message through one work loop (so presence,
    /// friends and ignore state need no locks and messages of one link keep their order — §7 "Ordering"), and delivers
    /// client packets back through the world that holds the player: TunnelPacketToExternalConnection for one player,
    /// TunnelPacketToExternalConnectionList (0x16) with every recipient on that world for a fan-out — one frame per
    /// world, never one per recipient (§5.3 bandwidth).
    /// The areas live in <see cref="PresenceService"/>, <see cref="FriendService"/>, <see cref="IgnoreService"/>,
    /// <see cref="GuildService"/>, <see cref="ChatService"/>, <see cref="MailService"/>, the cross-world Game Finder
    /// <see cref="MatchmakingService"/> and the groups across worlds <see cref="GroupService"/>.
    /// </summary>
    public sealed class SocialHub : IDisposable
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly InternalListener listener;
        private readonly Channel<Action> work = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource stop = new();
        private readonly List<WorldLink> links = new();
        private Timer ticker;
        private Task loop;

        public ISocialStore Store { get; }
        public PresenceService Presence { get; }
        public FriendService Friends { get; }
        public IgnoreService Ignores { get; }
        public GuildService Guilds { get; }
        public ChatService Chat { get; }
        public MailService Mail { get; }
        public MatchmakingService Matchmaking { get; }
        public GroupService Groups { get; }

        /// <summary>The port the service listens on (useful with port 0 in tests).</summary>
        public int Port => listener.Port;

        public SocialHub(ISocialStore store, IPAddress address, int port, TimeSpan presenceGrace)
        {
            Store = store;
            listener = new InternalListener(address, port);
            Presence = new PresenceService(this, presenceGrace);
            Friends = new FriendService(this);
            Ignores = new IgnoreService(this);
            Guilds = new GuildService(this);
            Chat = new ChatService(this);
            Mail = new MailService(this);
            Matchmaking = new MatchmakingService(this);
            Groups = new GroupService(this);
        }

        public void Start()
        {
            Guilds.Load();
            loop = Task.Run(WorkLoop);
            listener.Accepted += OnAccepted;
            listener.Start();
            // Presence grace timers (§5.5), changed Game Finder counts (§9) and lapsed group invitations (§10) are
            // checked once a second.
            ticker = new Timer(_ => Post(() =>
            {
                Presence.Tick();
                SendStatusWhenDue();
                Matchmaking.Tick();
                Groups.Tick();
                Friends.Tick();
            }), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            log.Info($"Social service listening on port {Port}.");
        }

        private DateTime nextStatus;
        private readonly DateTime started = DateTime.UtcNow;

        /// <summary>
        /// Every <see cref="ServiceStatus.Interval"/> (work loop): presence, groups and chat rooms to every linked world, for
        /// the worlds' admin monitor page (TASKS B.7).
        /// </summary>
        private void SendStatusWhenDue()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextStatus)
                return;
            nextStatus = now + ServiceStatus.Interval;
            List<WorldLink> worlds = links.ToList();
            ServicePacket status = ServiceStatus.Build("Social service", new List<(string, string)>
            {
                ("Up for", $"{(int)(now - started).TotalHours}h {(now - started).Minutes}m"),
                ("Worlds linked", string.Join(", ", worlds.Select(w => w.ToString()))),
                ("Characters online", Presence.OnlineCount.ToString()),
                ("Groups", Groups.GroupCount.ToString()),
                ("Chat rooms", Chat.RoomCount.ToString())
            });
            foreach (WorldLink world in worlds)
                world.Link.Send(status);
        }

        /// <summary>Runs <paramref name="action"/> on the work loop (after everything queued before it).</summary>
        public void Post(Action action) => work.Writer.TryWrite(action);

        private async Task WorkLoop()
        {
            try
            {
                await foreach (Action action in work.Reader.ReadAllAsync(stop.Token))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        // A failing request (e.g. the database briefly away) must not stop the service.
                        log.Error(e, "Social work item failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void OnAccepted(InternalLink link)
        {
            var world = new WorldLink(link);
            link.Tag = world;
            link.Received += (_, message) => Post(() => OnMessage(world, message));
            link.Closed += (_, error) => Post(() => OnClosed(world, error));
            Post(() => links.Add(world));
            link.Start();
            log.Info($"World link from {link.Remote}.");
        }

        private void OnClosed(WorldLink world, Exception error)
        {
            links.Remove(world);
            log.Info($"Link to {world} closed{(error != null ? ": " + error.Message : ".")}");
            Presence.LinkLost(world);
            Matchmaking.LinkLost(world);
        }

        private void OnMessage(WorldLink world, IInternalMessage message)
        {
            if (message is not ServicePacket packet)
            {
                log.Warn($"{world} sent internal opcode {message.Opcode}, which the social service doesn't take.");
                return;
            }
            ISocialMessage social;
            try
            {
                social = SocialMessages.Parse(packet);
            }
            catch (Exception e) when (e is System.IO.InvalidDataException)
            {
                log.Warn($"Unreadable service packet 0x{packet.Family:X}/0x{packet.Sub:X} from {world}: {e.Message}");
                return;
            }
            switch (social)
            {
                case PresenceSnapshot snapshot:
                    Presence.Snapshot(world, snapshot);
                    break;
                case CharacterEnteredWorld entered:
                    Presence.Entered(world, entered.Character);
                    break;
                case CharacterLeftWorld left:
                    Presence.Left(world, left);
                    Matchmaking.Left(world, left.CharacterGuid);
                    break;
                case CharacterZoneChanged zone:
                    Presence.ZoneChanged(zone);
                    break;
                case CharacterDirectoryUpdate update:
                {
                    // A deleted character keeps its directory row (marked deleted; names in mail and guild rosters still
                    // resolve) but leaves every friends list and its guild (TASKS 6.42). The name it had comes from the
                    // directory: a deleted character's own name is cleared in the character database.
                    string had = update.Change == DirectoryChange.Deleted ? Store.GetCharacter(update.CharacterGuid)?.Name ?? update.Name : null;
                    Store.UpsertCharacter(update.CharacterGuid, update.AccountId, update.Name, update.Change == DirectoryChange.Deleted);
                    if (update.Change == DirectoryChange.Deleted)
                    {
                        Friends.CharacterDeleted(update.CharacterGuid);
                        Guilds.CharacterDeleted(update.CharacterGuid, had ?? "");
                    }
                    break;
                }
                case PresenceQuery query:
                    Presence.Query(world, query);
                    break;
                // The login server's character select: the account's characters' friends (its link never sends a
                // PresenceSnapshot, so it stays a link without a world and gets nothing else but the status page).
                case CharacterSelectFriendsQuery select:
                    SendService(world, Friends.CharacterSelectFriends(select));
                    break;
                // Guild requests first: their u16 subs overlap the Command subs (Mute 0x10 = AddFriendRequest 0x10).
                case ClientRequestForward { Family: SocialMessages.GuildFamily } guild:
                    Guilds.Handle(world, guild);
                    break;
                case ClientRequestForward { Family: SocialMessages.ChatFamily } chat:
                    Chat.Handle(world, chat);
                    break;
                case ClientRequestForward { Family: MailMessages.MailFamily } mail:
                    Mail.Handle(world, mail);
                    break;
                case MailComposeStage2 compose:
                    Mail.Compose(world, compose);
                    break;
                case MailConsumeZoneResponse consumed:
                    Mail.ConsumeDone(consumed);
                    break;
                case SystemMail system:
                    Mail.System(system);
                    break;
                case MailAttachmentQuery query:
                    Mail.AttachmentQuery(world, query);
                    break;
                case MatchmakingRequest search:
                    Matchmaking.Request(world, search);
                    break;
                case MatchmakingGames published:
                    Matchmaking.Games(world, published);
                    break;
                // Groups across worlds (§10): the client's GroupsBase requests handed on by the zone, and the zones'
                // party-frame records.
                case ClientRequestForward { Family: GroupMessages.GroupFamily } group:
                    Groups.Handle(world, group);
                    break;
                case GroupMemberRow row:
                    Groups.RowReceived(world, row);
                    break;
                case ClientRequestForward { Family: SocialMessages.CommandFamily, Sub: SocialMessages.ChatChannelOn or SocialMessages.ChatChannelOff } channel:
                    Chat.HandleChannel(world, channel);
                    break;
                case ClientRequestForward { Sub: SocialMessages.AddFriendRequest } add:
                    Friends.HandleAdd(world, add);
                    break;
                case ClientRequestForward { Sub: SocialMessages.ConfirmFriendResponse } answer:
                    Friends.HandleResponse(world, answer);
                    break;
                case ClientRequestForward { Sub: SocialMessages.RemoveFriendRequest } remove:
                    Friends.HandleRemove(world, remove);
                    break;
                case ClientRequestForward { Sub: SocialMessages.IgnoreRequest } ignore:
                    Ignores.Handle(world, ignore);
                    break;
                default:
                    log.Warn($"{world} sent service packet 0x{packet.Family:X}/0x{packet.Sub:X}, which the social service doesn't take (yet).");
                    break;
            }
        }

        /// <summary>Sends a service message to a world.</summary>
        public void SendService(WorldLink world, ISocialMessage message)
        {
            if (world != null)
                world.Link.Send(message.ToPacket());
        }

        /// <summary>A client packet for one connected character (nothing when it has no connection now).</summary>
        public void SendTo(OnlineCharacter character, byte[] packet)
        {
            if (character?.World == null)
                return;
            character.World.Link.Send(new TunnelToExternal
            {
                Destination = new ExternalDestination(character.ConnectionId, GatewayChannel.World, ExternalDestination.Reliable),
                Packet = packet
            });
        }

        /// <summary>
        /// One client packet for many characters: one TunnelPacketToExternalConnectionList (0x16) per world with every
        /// recipient there (a single recipient goes as TunnelPacketToExternalConnection).
        /// </summary>
        public void SendTo(IEnumerable<OnlineCharacter> characters, byte[] packet)
        {
            foreach (IGrouping<WorldLink, OnlineCharacter> group in characters.Where(c => c.World != null).GroupBy(c => c.World))
            {
                var destinations = group.Select(c => new ExternalDestination(c.ConnectionId, GatewayChannel.World, ExternalDestination.Reliable))
                    .Distinct().ToList();
                if (destinations.Count == 1)
                    group.Key.Link.Send(new TunnelToExternal { Destination = destinations[0], Packet = packet });
                else
                    group.Key.Link.Send(new TunnelToExternalList
                    {
                        Channel = GatewayChannel.World, Flags = ExternalDestination.Reliable, Destinations = destinations, Packet = packet
                    });
            }
        }

        public void Dispose()
        {
            ticker?.Dispose();
            listener.Dispose();
            Post(() =>
            {
                foreach (WorldLink world in links.ToList())
                    world.Link.Close();
            });
            work.Writer.TryComplete();
            try
            {
                loop?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }
            stop.Cancel();
        }
    }
}
