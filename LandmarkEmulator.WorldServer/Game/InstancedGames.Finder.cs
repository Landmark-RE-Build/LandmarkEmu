using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Social;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The 0.695 Game Finder (29 Sep 2016 Game Table rework; trace/0695/packets-695.md §5a-5b, TASKS 3.25).
    /// <para>
    /// The window's Search button (GameFinder.lua GameFinderSearchStart → Lua InstancedGame.StartMatchmaking /
    /// StopMatchmaking) sends InstancedGameMatchmakingRequest 0x10D / 0x19 {u64 Game Finder flag mask (start only), u64
    /// player guid, u8 searching, u8}; the client marks itself matchmaking (manager +0x402) on its own.
    /// </para>
    /// <para>
    /// The answer is InstancedGameMatchmakingUpdate 0x10D / 0x1A {u64 (unread), u32 Players, u32 ActiveGames, u32
    /// QueuedGames, u32 InactiveGames, u32 SearchState, u64 match flags} (handler FUN_1434e9a50): it fills
    /// GameFinder.MatchmakingInfo and calls Lua UpdateGameFinder. SearchState 0 = "Searching..." (info page, button
    /// "Stop", chat "You've been entered into the search queue"), 3 = removed from the queue (options page, "Search"),
    /// 1 = match found (the client clears its matchmaking flag, plays a sound and prints "You've been queued for a match!"
    /// with the kind bits; nothing else). The window shows only the last values received (FUN_1434ed2e0), so a searching
    /// player gets a new update whenever the counts change. The leading u64 is stored by the reader (FUN_1434d7270, packet
    /// +0x18) and read by nothing in the 0.695 client (the handler takes +0x20..+0x38 only and the packet object is
    /// dropped after it): it cannot have carried a world or game the client used (L31). We send the player guid there,
    /// as the request carries it [I].
    /// </para>
    /// <para>
    /// The counts, as the window labels them (UI/Default/GameFinder.xml): "Players in matchmaking" (players searching
    /// on this world), "Currently Active games" (site games with a round on), "Games awaiting more players" (games
    /// with players but no round yet) and "Inactive games" (games nobody is in). Games switched off by their owner aren't
    /// counted. Live counted across worlds from 14 Oct 2016 ("Game finder should now work between worlds").
    /// </para>
    /// <para>
    /// Across worlds (world-channel-service.md §6.8, social-service.md §9), only when the social service is remote
    /// (<see cref="SocialServices.Matchmaking"/>): a search is handed to the service (MatchmakingRequest) and this world's
    /// games are published to it (MatchmakingGames: on change, at most once every <see cref="PublishInterval"/>, and in
    /// full after every reconnect, <see cref="PublishFinder"/>). The service answers with the counts of every world
    /// (MatchmakingUpdate → <see cref="FinderServiceUpdate"/>), written to the client with the same writer
    /// (<see cref="SendMatchmaking"/>), so the client sees the packets it sees without the service. Matching stays here
    /// (<see cref="TryMatch"/>: a game on this world and landscape, joined through its queue as before). A game on another
    /// world that would match (the service names it with its landscape, kind and name) is offered the same way when that
    /// world is reachable through the gateway (<see cref="OfferRemote"/>, L31): "Match found!" and the client's Game Action
    /// Queue entry with "Join Now" for that game (Request type 5, position 0 — the client builds the entry from the packet
    /// alone). Join Now (Request 6 value 1 with its guid) hands the player over to that world (B.10 hand-over with
    /// JoinGame, <see cref="CrossWorld.JoinGame"/>); the zone there re-zones them to the game's site and joins them
    /// (<see cref="ArriveToJoin"/>). Leave Queue, or no answer within <see cref="RemoteOfferTime"/>, drops the offer
    /// (Request type 4). Where no other world is reachable the player is only told, as before. The client's packets carry
    /// no world (the u64 above is unread), so this move is ours [I]. While the service is unreachable a search falls back
    /// to this world's own counts. In-process (no SOCIAL_ADDRESS) nothing here changes.
    /// </para>
    /// <para>
    /// Afterwards the player stays on the game's world (setting FinderReturnHome off, the default): the 14 Oct 2016 note
    /// says only that the finder works between worlds, a match on the player's own world never moves them back either, and
    /// the Landscapes window lists every world to travel home by [I]. With FinderReturnHome on (on the searcher's world)
    /// the hand-over carries the world, landscape and spot they left from (HandOver / AttachConnection ReturnTo); the game's
    /// world takes them back there a few seconds after they leave the game while still on its landscape
    /// (<see cref="TickReturns"/>; at once when the game was gone on arrival), arriving at that spot (ArriveAt). Going on
    /// to a Chaos Cavern after an Adventure, or travelling on by themselves, cancels the way back.
    /// </para>
    /// <para>
    /// Game kinds (the flag word's bits, GameFinder.GameFlags rows, FUN_1434ed2e0): 2 FFA Games, 4 Team Games, 8 PvE,
    /// 0x10 PvP, 0x40 In Progress Games. Inferred: the counts include only games of the kinds the searcher ticked (none
    /// ticked in a pair = either), and a search matches a game of those kinds on the searcher's landscape that someone is
    /// waiting in (or, with In Progress ticked, a running one) and that can take them: state 1 "Match found!" with the
    /// game's kind bits (the client prints "You've been queued for a match! Match details:" with them), then the
    /// player is put in that game's queue, whose Game Action Queue entry offers "Join Now" (InstancedGames.Queue.cs).
    /// The client has no other way to move a matched player (L31, closed client side: Request types 4 / 5 / 6 only, the
    /// update's u64 unread); games on other landscapes of this world are not matched (a join moves players only within a
    /// landscape).
    /// </para>
    /// </summary>
    public static partial class InstancedGames
    {
        /// <summary>InstancedGameMatchmakingRequest 0x19 (C→S) / MatchmakingUpdate 0x1A (S→C): 0.695 only (packets-695.md §5a / §5b).</summary>
        private const uint SubMatchmakingRequest = 0x19, SubMatchmakingUpdate = 0x1A;

        private const uint SearchSearching = 0, SearchMatchFound = 1, SearchStopped = 3;

        private const ulong FlagFfa = 2, FlagTeams = 4, FlagPvE = 8, FlagPvP = 0x10, FlagInProgress = 0x40;

        /// <summary>A game's Game Finder kind bits.</summary>
        private static ulong KindFlags(Game game)
            => (game.Rules.TeamCount == 0 ? FlagFfa : FlagTeams) | (game.Rules.Cooperative ? FlagPvE : FlagPvP)
               | (game.State >= StateWaitingForReady && game.State <= StateRunning ? FlagInProgress : 0);

        /// <summary>Is the game of a kind the searcher ticked (FFA / Teams, PvE / PvP; an unticked pair = either)?</summary>
        private static bool OfKind(Game game, ulong search)
        {
            ulong kind = KindFlags(game);
            ulong teams = search & (FlagFfa | FlagTeams), pvp = search & (FlagPvE | FlagPvP);
            return (teams == 0 || (teams & kind) != 0) && (pvp == 0 || (pvp & kind) != 0);
        }

        private class Searcher
        {
            public ulong Flags;
            public (uint, uint, uint, uint) LastSent;
            /// <summary>The search is with the shared service: it sends the counts (of every world).</summary>
            public bool Remote;
            /// <summary><see cref="LastSent"/> holds the service's counts (not this world's own).</summary>
            public bool HasRemoteCounts;
            /// <summary>The other world's game the player was last told about (FinderServiceUpdate).</summary>
            public (uint World, ulong Game) ToldOther;
        }

        private static readonly Dictionary<WorldSession, Searcher> searchers = new();

        /// <summary>A game of another world offered to a player who searched (L31): its queue entry shows "Join Now".</summary>
        private sealed class RemoteOffer
        {
            public uint World;
            public uint Zone;
            public ulong Game;
            public uint NameId;
            public string Name;
            public DateTime OfferedAt;
        }

        private static readonly Dictionary<WorldSession, RemoteOffer> remoteOffers = new();

        /// <summary>
        /// How long a remote offer waits for Join Now: the rule sets' MaxQueueJoinTime default (general setting 4, 30 s;
        /// the other world's own setting isn't published) [I].
        /// </summary>
        private static readonly TimeSpan RemoteOfferTime = TimeSpan.FromSeconds(30);

        /// <summary>Players handed over to join a game here (<see cref="ArriveToJoin"/>): joined once their re-zone is over.</summary>
        private static readonly Dictionary<WorldSession, (ulong Game, DateTime Until)> arrivingToJoin = new();
        private static readonly TimeSpan ArrivalJoinWait = TimeSpan.FromSeconds(60);

        /// <summary>Is FinderReturnHome on (players a match takes to another world are brought back after the game)?</summary>
        public static bool ReturnHomeEnabled
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.FinderReturnHome == true;

        /// <summary>A player here for a Game Finder match from another world who goes back afterwards (FinderReturnHome there).</summary>
        private sealed class Visitor
        {
            public ulong Game;
            public Internal.WorldSpot Home;
            /// <summary>When they were first seen out of every game and queue here (null while in one).</summary>
            public DateTime? LeftAt;
        }

        private static readonly Dictionary<WorldSession, Visitor> visitors = new();

        /// <summary>
        /// Out of the game this long before the way back starts: the game's leave packets (membership, state 0, scoreboard)
        /// must not share a burst with the re-zone (as the Chaos Cavern trip's 2 s) [I].
        /// </summary>
        private static readonly TimeSpan ReturnDelay = TimeSpan.FromSeconds(3);

        /// <summary>Dev "finderwaiting": games published to the Game Finder as if someone were waiting in them (tests, L31).</summary>
        private static readonly HashSet<ulong> devWaiting = new();

        /// <summary>Players who stopped a search with the service: its SearchState 3 answer is passed on to them (only).</summary>
        private static readonly HashSet<WorldSession> stopping = new();

        /// <summary>Minimum time between two MatchmakingGames of this world (bandwidth rule; changes in between are coalesced).</summary>
        private static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(5);
        private static List<MatchmakingGame> publishedGames;
        private static DateTime nextPublish = DateTime.MinValue;

        private static bool HandleFinder(WorldSession session, uint sub, byte[] body)
        {
            if (sub != SubMatchmakingRequest)
                return false;
            if (body.Length < 17)
                return true;
            var reader = new GamePacketReader(body);
            ulong flags = reader.ReadULong();
            reader.ReadULong();
            bool searching = reader.ReadByte() != 0;
            IMatchmakingService service = SocialServices.Matchmaking;
            if (searching)
            {
                var searcher = new Searcher { Flags = flags };
                lock (sync)
                {
                    searchers[session] = searcher;
                    stopping.Remove(session);
                }
                // With the shared service its answer (SearchState 0, counts of every world) is the reply; without it, or
                // while it is unreachable, this world's own counts as before.
                if (service != null && service.Search(session.PlayerGuid, flags, true))
                    searcher.Remote = true;
                else
                {
                    searcher.LastSent = FinderCounts(flags);
                    SendMatchmaking(session, searcher.LastSent, SearchSearching, flags);
                }
                log.Info($"{session.CharacterName} is searching for games (Game Finder flags 0x{flags:X}{(searcher.Remote ? ", every world" : "")}).");
                TryMatch(session, searcher);
            }
            else
            {
                Searcher was;
                lock (sync)
                    searchers.Remove(session, out was);
                // The client leaves the stop's flag field unset (packets-695.md §5a): the search's own mask is used.
                ulong wasFlags = was?.Flags ?? 0;
                if (service != null && service.Search(session.PlayerGuid, wasFlags, false))
                {
                    lock (sync)
                        stopping.Add(session);
                }
                else
                    SendMatchmaking(session, FinderCounts(wasFlags), SearchStopped, wasFlags);
                log.Info($"{session.CharacterName} stopped searching for games.");
            }
            return true;
        }

        /// <summary>
        /// The shared service's MatchmakingUpdate for a player of this world (world thread, RemoteSocialService). Written
        /// to the client with <see cref="SendMatchmaking"/>. Answers to requests that are over are dropped: SearchState 0
        /// for a player who is no longer searching with the service (matched here meanwhile, or stopped), 3 for a player
        /// who did not ask to stop (taken out after a match here). Unchanged counts are not sent again (the window keeps
        /// the last values). A game on another world that would match is offered (<see cref="OfferRemote"/>) when that world
        /// is reachable, else told to the player once per game.
        /// </summary>
        public static void FinderServiceUpdate(MatchmakingUpdate update)
        {
            if (PlayerManager.FindByGuid(update.CharacterGuid) is not WorldSession session || session.Disconnected)
                return;
            var counts = (update.Players, update.Active, update.Waiting, update.Inactive);
            if (update.SearchState == SearchStopped)
            {
                bool asked;
                lock (sync)
                    asked = stopping.Remove(session);
                if (asked)
                    SendMatchmaking(session, counts, SearchStopped, update.Flags);
                return;
            }
            if (update.SearchState != SearchSearching)
                return;
            Searcher searcher;
            lock (sync)
                searchers.TryGetValue(session, out searcher);
            if (searcher == null || !searcher.Remote)
                return;
            if (!searcher.HasRemoteCounts || searcher.LastSent != counts)
            {
                searcher.LastSent = counts;
                searcher.HasRemoteCounts = true;
                SendMatchmaking(session, counts, SearchSearching, searcher.Flags);
            }
            var other = (update.OtherWorld, update.OtherGame);
            if (update.OtherWorld == 0 || session.RezoneTarget != null)
                return;
            if (CrossWorld.CanReach(session, update.OtherWorld) && update.OtherZone != 0)
            {
                OfferRemote(session, searcher, update);
                return;
            }
            if (searcher.ToldOther != other)
            {
                searcher.ToldOther = other;
                Chat.System(session, "A game matching your search is open on another world that can't be reached from here; keep searching to find one on this world.");
                log.Info($"Game Finder: {session.CharacterName} matches game 0x{update.OtherGame:X} on world {update.OtherWorld}, not reachable through this world's gateway.");
            }
        }

        /// <summary>
        /// A match on another world (L31): the search ends (here and with the service; its SearchState 3 answer is dropped),
        /// the client gets "Match found!" with that game's kind bits, and its Game Action Queue entry for the game with
        /// "Join Now" (Request type 5, place 0, no wait). Taking it hands the player over (<see cref="HandleRemoteOffer"/>).
        /// </summary>
        private static void OfferRemote(WorldSession session, Searcher searcher, MatchmakingUpdate update)
        {
            lock (sync)
            {
                searchers.Remove(session);
                remoteOffers[session] = new RemoteOffer
                {
                    World = update.OtherWorld, Zone = update.OtherZone, Game = update.OtherGame, NameId = update.OtherNameId,
                    Name = update.OtherName ?? "", OfferedAt = DateTime.UtcNow
                };
            }
            // Out of this world's queues: one offer at a time.
            Dequeue(session, tell: true);
            SocialServices.Matchmaking?.Search(session.PlayerGuid, searcher.Flags, false);
            SendMatchmaking(session, searcher.LastSent, SearchMatchFound, update.OtherFlags);
            SendQueueRequest(session, RequestQueuePosition, 0, 0, 0, update.OtherGame, update.OtherNameId, update.OtherName);
            Chat.System(session, "The game you were matched with is on another world: Join Now takes you there.");
            log.Info($"Game Finder: {session.CharacterName} matched game 0x{update.OtherGame:X} on world {update.OtherWorld}, landscape {update.OtherZone}; offered.");
        }

        /// <summary>
        /// The queue entry's buttons for a remote offer (Request type 6 value 1 "Join Now" / value 0 "Leave Queue", 3 Join,
        /// or 7 Leave): Join Now hands the player over to the game's world (<see cref="CrossWorld.JoinGame"/>), the others
        /// drop the offer. True when the request was for the player's remote offer.
        /// </summary>
        private static bool HandleRemoteOffer(WorldSession session, short type, short value, ulong guid)
        {
            if (type != RequestMembership && type != RequestLeave && type != RequestJoin)
                return false;
            RemoteOffer offer;
            lock (sync)
            {
                if (!remoteOffers.TryGetValue(session, out offer) || guid != offer.Game)
                    return false;
                remoteOffers.Remove(session);
            }
            bool join = type == RequestJoin || (type == RequestMembership && value == 1);
            if (join && CrossWorld.JoinGame(session, offer.World, offer.Zone, offer.Game))
            {
                log.Info($"Game Finder: {session.CharacterName} takes game 0x{offer.Game:X} on world {offer.World}.");
                return true;
            }
            SendQueueRequest(session, RequestLeftQueue, 0, 0, 0, offer.Game, offer.NameId, offer.Name);
            if (join)
                Chat.System(session, "That world can't be reached any more.");
            return true;
        }

        /// <summary>
        /// A player handed over from another world to join a game here (AttachConnection JoinGame, L31): re-zoned to the
        /// game's build site (beside its Game Table) on its landscape; the join itself follows once the re-zone is over
        /// (<see cref="TickArrivals"/>; the player already chose Join Now there). False when the game is gone or switched
        /// off: the caller then brings them to the landscape's usual arrival.
        /// </summary>
        public static bool ArriveToJoin(WorldSession session, ulong gameGuid)
        {
            EnsureLoaded();
            Game game = GameByGuid(gameGuid);
            if (game == null || game.Disabled)
            {
                Chat.System(session, "The game you were matched with has closed.");
                log.Info($"Game Finder: {session.CharacterName} arrived for game 0x{gameGuid:X}, which is gone.");
                return false;
            }
            var at = game.Position + new System.Numerics.Vector3(2f, 0f, 2f);
            if (Terrain.TerrainHeights.HeightAt(at.X, at.Z, game.Zone) is float ground && ground > at.Y)
                at.Y = ground;
            Teleport.Rezone(session, at + new System.Numerics.Vector3(0f, 1f, 0f), game.Zone);
            lock (sync)
                arrivingToJoin[session] = (game.Guid, DateTime.UtcNow + ArrivalJoinWait);
            log.Info($"Game Finder: {session.CharacterName} arrives from another world for {Title(game)} on site {game.ClaimId}.");
            return true;
        }

        /// <summary>
        /// A Game Finder arrival from another world whose world asked to have them back (AttachConnection ReturnTo,
        /// FinderReturnHome there): remembered until they leave the game (<see cref="TickReturns"/>). <paramref name="joining"/>
        /// false = the game was gone on arrival: back as soon as the arrival's re-zone is over.
        /// </summary>
        public static void ExpectReturn(WorldSession session, ulong gameGuid, Internal.WorldSpot home, bool joining)
        {
            if (!home.IsSet || home.World == GatewayLink.ServerWorldId)
                return;
            lock (sync)
                visitors[session] = new Visitor { Game = gameGuid, Home = home };
            log.Info($"Game Finder: {session.CharacterName} goes back to world {home.World}, landscape {home.Zone} after the game"
                + (joining ? "." : " (it's gone: at once)."));
        }

        /// <summary>
        /// Every sync (FinderReturnHome trips, see the class notes): a visitor out of every game and queue here for
        /// <see cref="ReturnDelay"/>, not moving, still on the game's landscape, is handed back to the world, landscape and
        /// spot they came from (<see cref="CrossWorld.ReturnHome"/>). Visitors who travelled on (another landscape, a Chaos
        /// Cavern trip) or left are forgotten.
        /// </summary>
        private static void TickReturns(DateTime now)
        {
            List<(WorldSession Session, Visitor Visitor)> due = new();
            lock (sync)
            {
                if (visitors.Count == 0)
                    return;
                foreach ((WorldSession session, Visitor v) in visitors.ToList())
                {
                    Game game = gamesByClaim.Values.FirstOrDefault(g => g.Guid == v.Game);
                    bool cavern = cavernTrips.Any(t => t.Session == session);
                    // Travelled on by themselves (another landscape; a gone game's arrival is the landscape's own) or left.
                    uint zone = game?.Zone ?? session.ZoneId;
                    if (session.Disconnected || cavern || (session.ZoneId != zone && session.RezoneTarget == null))
                    {
                        visitors.Remove(session);
                        continue;
                    }
                    bool inOne = arrivingToJoin.ContainsKey(session)
                        || gamesByClaim.Values.Any(g => g.Find(session) != null || g.Queue.Any(q => q.Session == session));
                    if (inOne || session.RezoneTarget != null)
                    {
                        if (inOne)
                            v.LeftAt = null;
                        continue;
                    }
                    v.LeftAt ??= now;
                    if (now - v.LeftAt.Value < ReturnDelay)
                        continue;
                    visitors.Remove(session);
                    due.Add((session, v));
                }
            }
            foreach ((WorldSession session, Visitor v) in due)
            {
                if (CrossWorld.ReturnHome(session, v.Home))
                {
                    log.Info($"Game Finder: {session.CharacterName} left the game; back to world {v.Home.World}.");
                    continue;
                }
                Chat.System(session, "Your world can't be reached from here; the Landscapes window lists the worlds you can travel to.");
                log.Info($"Game Finder: {session.CharacterName} can't be taken back to world {v.Home.World} (not reachable).");
            }
        }

        /// <summary>
        /// Dev "finderwaiting &lt;table guid hex&gt; [off]" / "finderwaiting off": this world publishes that game to the shared
        /// Game Finder with at least one player in it, so another world's search is matched with it without a second client
        /// (two-world tests, L31). Its own counts and joins are unchanged. Returns the waiting list and this world's games.
        /// </summary>
        public static string DevFinderWaiting(string args)
        {
            EnsureLoaded();
            string[] a = (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            lock (sync)
            {
                if (a.Length > 0 && a[0] == "off")
                    devWaiting.Clear();
                else if (a.Length > 0 && ulong.TryParse(a[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? a[0][2..] : a[0],
                    System.Globalization.NumberStyles.HexNumber, null, out ulong guid))
                {
                    if (a.Length > 1 && a[1] == "off")
                        devWaiting.Remove(guid);
                    else
                        devWaiting.Add(guid);
                }
                // Published again on the next sync.
                publishedGames = null;
                nextPublish = DateTime.MinValue;
                return $"Game Finder: waiting {(devWaiting.Count == 0 ? "none" : string.Join(", ", devWaiting.Select(g => $"0x{g:X}")))}; games: "
                    + string.Join(", ", gamesByClaim.Values.Select(g => $"{Title(g)} table 0x{g.Guid:X} site {g.ClaimId} landscape {g.Zone} "
                        + $"state {g.State}, {g.Players.Count} player(s){(g.Disabled ? ", off" : "")}{(g.CanJoin ? "" : ", can't join")}"));
            }
        }

        /// <summary>
        /// Every sync: remote offers not taken in time are dropped (queue entry removed); arrivals whose re-zone is over
        /// join their game (a full one puts them in its queue, as a join here does).
        /// </summary>
        private static void TickArrivals()
        {
            DateTime now = DateTime.UtcNow;
            List<(WorldSession Session, RemoteOffer Offer)> expired;
            List<(WorldSession Session, ulong Game)> ready;
            lock (sync)
            {
                expired = remoteOffers.Where(o => o.Key.Disconnected || now - o.Value.OfferedAt > RemoteOfferTime)
                    .Select(o => (o.Key, o.Value)).ToList();
                foreach (var e in expired)
                    remoteOffers.Remove(e.Session);
                foreach (WorldSession gone in arrivingToJoin.Where(a => a.Key.Disconnected || now > a.Value.Until).Select(a => a.Key).ToList())
                    arrivingToJoin.Remove(gone);
                ready = arrivingToJoin.Where(a => a.Key.RezoneTarget == null).Select(a => (a.Key, a.Value.Game)).ToList();
                foreach (var r in ready)
                    arrivingToJoin.Remove(r.Session);
            }
            foreach ((WorldSession session, RemoteOffer offer) in expired)
            {
                if (session.Disconnected)
                    continue;
                SendQueueRequest(session, RequestLeftQueue, 0, 0, 0, offer.Game, offer.NameId, offer.Name);
                Chat.System(session, "You didn't join the game on the other world in time and left its queue.");
            }
            foreach ((WorldSession session, ulong guid) in ready)
            {
                if (GameByGuid(guid) is Game game && game.Zone == session.ZoneId)
                    JoinGame(session, game);
                else
                    Chat.System(session, "The game you were matched with has closed.");
            }
            TickReturns(now);
        }

        /// <summary>
        /// With the shared service, every sync: after a (re)connect this world's searches again and its games in full;
        /// otherwise the games when they changed, at most once every <see cref="PublishInterval"/>. Searchers fall back to
        /// this world's own counts while the service is unreachable.
        /// </summary>
        private static void PublishFinder(IMatchmakingService service)
        {
            DateTime now = DateTime.UtcNow;
            bool resync = service.TakeResync();
            List<(WorldSession Session, Searcher Searcher)> list;
            lock (sync)
                list = searchers.Select(s => (s.Key, s.Value)).ToList();
            bool connected = service.IsConnected;
            foreach ((WorldSession session, Searcher searcher) in list)
            {
                if (resync || (!searcher.Remote && connected))
                {
                    bool sent = service.Search(session.PlayerGuid, searcher.Flags, true);
                    if (sent && !searcher.Remote)
                        searcher.HasRemoteCounts = false;
                    searcher.Remote = sent;
                }
                else if (searcher.Remote && !connected)
                {
                    // Service gone: this world's own counts from now on (TickFinder), until it is back.
                    searcher.Remote = false;
                    searcher.HasRemoteCounts = false;
                }
            }
            List<MatchmakingGame> games;
            lock (sync)
                games = gamesByClaim.Values.Where(g => !g.Disabled).OrderBy(g => g.Guid)
                    .Select(g => new MatchmakingGame(g.Guid, g.Zone,
                        KindFlags(g) | (g.CanJoin ? MatchmakingMessages.FlagOpen : 0) | (g.Queue.Count > 0 ? MatchmakingMessages.FlagQueued : 0),
                        (uint)Math.Max(g.Players.Count, devWaiting.Contains(g.Guid) ? 1 : 0), g.State) { NameId = g.Rules.NameId, Name = g.CustomName ?? "" })
                    .ToList();
            if (!resync && (MatchmakingGames.Same(games, publishedGames) || now < nextPublish))
                return;
            if (service.PublishGames(games))
            {
                publishedGames = games;
                nextPublish = now + PublishInterval;
            }
        }

        /// <summary>
        /// A searcher's match (inferred, see the class notes): a game of the ticked kinds on their landscape, enabled,
        /// joinable, with someone waiting in it (or running, with In Progress ticked), and not the one they are in. The
        /// most populated first. True if one was found (state 1 sent, the player queued for it).
        /// </summary>
        private static bool TryMatch(WorldSession session, Searcher searcher)
        {
            Game match;
            lock (sync)
                match = gamesByClaim.Values
                    .Where(g => !g.Disabled && g.CanJoin && g.Zone == session.ZoneId && g.Find(session) == null && OfKind(g, searcher.Flags)
                        && (g.State == StateRunning ? (searcher.Flags & FlagInProgress) != 0 : g.State < StateRunning && g.Players.Count > 0))
                    .OrderByDescending(g => g.Players.Count).FirstOrDefault();
            if (match == null)
                return false;
            lock (sync)
                searchers.Remove(session);
            // Out of the shared count too (its SearchState 3 answer is dropped: the player did not ask to stop).
            if (searcher.Remote)
                SocialServices.Matchmaking?.Search(session.PlayerGuid, searcher.Flags, false);
            SendMatchmaking(session, searcher.HasRemoteCounts ? searcher.LastSent : FinderCounts(searcher.Flags), SearchMatchFound, KindFlags(match));
            Enqueue(match, session);
            log.Info($"Game Finder: {session.CharacterName} matched {Title(match)} on site {match.ClaimId}.");
            return true;
        }

        /// <summary>
        /// Players searching, games with a round on, games waiting for more players, games nobody is in — of the kinds
        /// the searcher ticked (<see cref="OfKind"/>).
        /// </summary>
        private static (uint Players, uint Active, uint Waiting, uint Inactive) FinderCounts(ulong flags)
        {
            lock (sync)
            {
                var games = gamesByClaim.Values.Where(g => !g.Disabled && OfKind(g, flags)).ToList();
                uint active = (uint)games.Count(g => g.State >= StateStarted && g.State <= StateFinishing);
                uint waiting = (uint)games.Count(g => g.State < StateStarted && (g.Players.Count > 0 || g.Queue.Count > 0));
                uint inactive = (uint)games.Count(g => g.State < StateStarted && g.Players.Count == 0 && g.Queue.Count == 0);
                return ((uint)searchers.Count, active, waiting, inactive);
            }
        }

        /// <summary>Every sync: drops searchers who left and sends the others the counts when they changed.</summary>
        private static void TickFinder()
        {
            TickArrivals();
            IMatchmakingService service = SocialServices.Matchmaking;
            if (service != null)
                PublishFinder(service);
            List<(WorldSession Session, Searcher Searcher)> list;
            List<(WorldSession Session, Searcher Searcher)> gone;
            lock (sync)
            {
                gone = searchers.Where(s => s.Key.Disconnected).Select(s => (s.Key, s.Value)).ToList();
                foreach ((WorldSession session, _) in gone)
                    searchers.Remove(session);
                stopping.RemoveWhere(s => s.Disconnected);
                list = searchers.Select(s => (s.Key, s.Value)).ToList();
            }
            // The service also ends a search when the character leaves the world; this covers a session dropped without it.
            foreach ((WorldSession session, Searcher searcher) in gone)
                if (searcher.Remote)
                    service?.Search(session.PlayerGuid, searcher.Flags, false);
            if (list.Count == 0)
                return;
            foreach ((WorldSession session, Searcher searcher) in list)
            {
                if (session.RezoneTarget != null || TryMatch(session, searcher))
                    continue;
                // With the service the counts come from it, on change (FinderServiceUpdate).
                if (searcher.Remote)
                    continue;
                var counts = FinderCounts(searcher.Flags);
                if (searcher.LastSent == counts)
                    continue;
                searcher.LastSent = counts;
                SendMatchmaking(session, counts, SearchSearching, searcher.Flags);
            }
        }

        private static void SendMatchmaking(WorldSession session, (uint, uint, uint, uint) counts, uint state, ulong flags)
        {
            var output = new List<byte>();
            var w = Begin(output, SubMatchmakingUpdate);
            w.Write(session.PlayerGuid);
            w.Write(counts.Item1);
            w.Write(counts.Item2);
            w.Write(counts.Item3);
            w.Write(counts.Item4);
            w.Write(state);
            w.Write(flags);
            session.EnqueueMessage(ZoneMessageOpcode.InstancedGameBase, output.ToArray());
        }
    }
}
