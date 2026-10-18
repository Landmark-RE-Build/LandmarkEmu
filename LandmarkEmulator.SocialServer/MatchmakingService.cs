using LandmarkEmulator.Internal.Social;
using NLog;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// The cross-world Game Finder (patch notes 14 Oct 2016: the Game Finder works across worlds;
    /// world-channel-service.md §6.8, social-service.md §9): one list of searchers and one list of site games for every
    /// world, so the window's counts ("Players in matchmaking", active / waiting / inactive games) cover all worlds.
    /// <list type="bullet">
    /// <item>Each zone publishes its games (<see cref="MatchmakingGames"/>, whole per world); a world's games and
    /// searchers are dropped when its link closes.</item>
    /// <item>A search (<see cref="MatchmakingRequest"/>) is answered at once (SearchState 0 on start, 3 on stop); while
    /// it runs, the searcher gets a new <see cref="MatchmakingUpdate"/> whenever their counts change (checked once a
    /// second on the work loop and only when something changed: the client keeps the last values, so nothing is sent
    /// without a change — bandwidth rule).</item>
    /// <item>Matching: the searcher's own zone matches games on its world itself (landscape, already-in-it and the join
    /// are zone checks, InstancedGames.Finder.cs). Here only games on <b>other</b> worlds are looked at: one that would
    /// match is named in the update (OtherWorld / OtherGame, with its landscape, kind and name); the searcher's zone
    /// offers it ("Match found!" and a Game Action Queue "Join Now") and, when taken, hands the player over to that world,
    /// which joins them (L31; the move itself is ours: the client carries no world in its Game Finder packets). The zone
    /// ends the search here when it makes the offer. SearchState 1 is written by the zone, never sent from here.</item>
    /// </list>
    /// </summary>
    public sealed class MatchmakingService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private sealed class Search
        {
            public WorldLink Link;
            public uint WorldId;
            public ulong Character;
            public ulong Flags;
            public (uint, uint, uint, uint) LastCounts;
            public (uint World, ulong Game) LastOther;
        }

        private readonly SocialHub hub;
        /// <summary>Published games by world link (a reconnected world is a new link; the old one's close can't remove the new list).</summary>
        private readonly Dictionary<WorldLink, (uint WorldId, IReadOnlyList<MatchmakingGame> Games)> games = new();
        private readonly Dictionary<(uint World, ulong Character), Search> searches = new();
        private bool dirty;

        public MatchmakingService(SocialHub hub) => this.hub = hub;

        /// <summary>A zone's search start / stop.</summary>
        public void Request(WorldLink world, MatchmakingRequest request)
        {
            var key = (request.WorldId, request.CharacterGuid);
            if (request.Searching)
            {
                var search = new Search { Link = world, WorldId = request.WorldId, Character = request.CharacterGuid, Flags = request.Flags };
                searches[key] = search;
                dirty = true;
                Send(search, MatchmakingMessages.SearchSearching);
                log.Info($"Game Finder: character {request.CharacterGuid:X} on world {request.WorldId} searches (flags 0x{request.Flags:X}); {searches.Count} searching.");
            }
            else
            {
                searches.Remove(key, out Search was);
                dirty = true;
                Send(was ?? new Search { Link = world, WorldId = request.WorldId, Character = request.CharacterGuid, Flags = request.Flags },
                    MatchmakingMessages.SearchStopped);
                log.Info($"Game Finder: character {request.CharacterGuid:X} on world {request.WorldId} stopped searching.");
            }
        }

        /// <summary>A zone's games (whole list for its world).</summary>
        public void Games(WorldLink world, MatchmakingGames published)
        {
            games[world] = (published.WorldId, published.Games);
            dirty = true;
            log.Debug($"Game Finder: world {published.WorldId} has {published.Games.Count} game(s).");
        }

        /// <summary>A character left its world: its search ends (no reply; the client is gone).</summary>
        public void Left(WorldLink world, ulong character)
        {
            foreach (var key in searches.Where(s => s.Value.Link == world && s.Value.Character == character).Select(s => s.Key).ToList())
            {
                searches.Remove(key);
                dirty = true;
            }
        }

        /// <summary>A world's link closed: its games and searchers are gone (the zone re-sends both after reconnecting).</summary>
        public void LinkLost(WorldLink world)
        {
            bool had = games.Remove(world);
            int before = searches.Count;
            foreach (var key in searches.Where(s => s.Value.Link == world).Select(s => s.Key).ToList())
                searches.Remove(key);
            if (had || before != searches.Count)
            {
                dirty = true;
                log.Info($"Game Finder: {world} gone; its games and {before - searches.Count} searcher(s) dropped.");
            }
        }

        /// <summary>Work loop, once a second: searchers whose counts (or other-world match) changed get an update.</summary>
        public void Tick()
        {
            if (!dirty)
                return;
            dirty = false;
            foreach (Search search in searches.Values.ToList())
            {
                var counts = CountsFor(search);
                var other = OtherWorldMatch(search);
                if (counts == search.LastCounts && (other.World, other.Game.Game) == search.LastOther)
                    continue;
                Send(search, MatchmakingMessages.SearchSearching, counts, other);
            }
        }

        private void Send(Search search, uint state)
            => Send(search, state, CountsFor(search), state == MatchmakingMessages.SearchSearching ? OtherWorldMatch(search) : default);

        private void Send(Search search, uint state, (uint, uint, uint, uint) counts, (uint World, MatchmakingGame Game) other)
        {
            search.LastCounts = counts;
            search.LastOther = (other.World, other.Game.Game);
            hub.SendService(search.Link, new MatchmakingUpdate
            {
                CharacterGuid = search.Character, Players = counts.Item1, Active = counts.Item2, Waiting = counts.Item3, Inactive = counts.Item4,
                SearchState = state, Flags = search.Flags, OtherWorld = other.World, OtherGame = other.Game.Game,
                OtherZone = other.Game.Zone, OtherFlags = other.Game.Flags & MatchmakingMessages.ClientKinds,
                OtherNameId = other.Game.NameId, OtherName = other.Game.Name ?? ""
            });
        }

        /// <summary>Players searching on every world, and the game counts of every world for this searcher's kinds.</summary>
        private (uint, uint, uint, uint) CountsFor(Search search)
        {
            (uint active, uint waiting, uint inactive) = MatchmakingMessages.Counts(games.Values.SelectMany(g => g.Games), search.Flags);
            return ((uint)searches.Count, active, waiting, inactive);
        }

        /// <summary>The most populated game on another world that would match the search, or (0, default).</summary>
        private (uint World, MatchmakingGame Game) OtherWorldMatch(Search search)
        {
            var best = games.Values.Where(w => w.WorldId != search.WorldId)
                .SelectMany(w => w.Games.Where(g => MatchmakingMessages.Matches(g, search.Flags)).Select(g => (w.WorldId, Game: g)))
                .OrderByDescending(x => x.Game.Players).ThenBy(x => x.WorldId).ThenBy(x => x.Game.Game)
                .FirstOrDefault();
            return best.WorldId != 0 ? best : default;
        }
    }
}
