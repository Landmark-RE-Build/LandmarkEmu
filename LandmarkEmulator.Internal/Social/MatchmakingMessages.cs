using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.Internal.Social
{
    /// <summary>
    /// The cross-world Game Finder between the zones and the shared social service (patch notes 14 Oct 2016: the Game
    /// Finder works across worlds; trace/architecture/world-channel-service.md §6.8, social-service.md §9). All
    /// three are <b>ours</b> (live's server-side matchmaking messages are not named in the client's id table, L48): family
    /// InstancedGameBase 0x10D (the client's Game Finder family, packets-695.md §5), subs from 0x10100 up.
    /// <list type="bullet">
    /// <item><see cref="MatchmakingRequest"/> 0x10D / 0x10100, zone → social: the zone that received the client's
    /// InstancedGameMatchmakingRequest (0x10D / 0x19) hands the search on.</item>
    /// <item><see cref="MatchmakingGames"/> 0x10D / 0x10101, zone → social: the zone's site games, whole (replaces what
    /// the service had for that world); sent on change, at most once every few seconds, and in full after every
    /// (re)connect. The service forgets a world's games when its link closes.</item>
    /// <item><see cref="MatchmakingUpdate"/> 0x10D / 0x10102, social → zone: the counts and search state for one
    /// searcher; the zone writes the client's InstancedGameMatchmakingUpdate (0x10D / 0x1A) from it with its own writer
    /// (0.695 numbers), so the client sees the same packet as without the
    /// service.</item>
    /// </list>
    /// The counting and matching rules (<see cref="OfKind"/>, <see cref="Counts"/>, <see cref="Matches"/>) are the
    /// zone's local Game Finder rules (InstancedGames.Finder.cs), applied to the games of every world.
    /// </summary>
    public static class MatchmakingMessages
    {
        /// <summary>InstancedGameBase (the client's Game Finder family).</summary>
        public const ushort InstancedGameFamily = 0x10D;
        public const uint RequestSub = 0x10100, GamesSub = 0x10101, UpdateSub = 0x10102;

        /// <summary>Game Finder kind bits (GameFinder.GameFlags rows, FUN_1434ed2e0): FFA 2, Teams 4, PvE 8, PvP 0x10, In Progress 0x40.</summary>
        public const ulong FlagFfa = 2, FlagTeams = 4, FlagPvE = 8, FlagPvP = 0x10, FlagInProgress = 0x40;

        /// <summary>The client's kind bits (what a client's MatchmakingUpdate may carry).</summary>
        public const ulong ClientKinds = FlagFfa | FlagTeams | FlagPvE | FlagPvP | FlagInProgress;

        /// <summary>
        /// Ours, above every client bit, in a published game's flags only (never sent to a client): the game can take a
        /// player now (enabled, not on its scoreboard, not full), and someone is in its queue.
        /// </summary>
        public const ulong FlagOpen = 0x1_0000_0000, FlagQueued = 0x2_0000_0000;

        /// <summary>
        /// Game states as the zone publishes them: the client's own state numbers (state switch FUN_14343ff60,
        /// InstancedGames.StateXxx): 3 Started, 6 Running, 8 Finishing.
        /// </summary>
        public const uint StateStarted = 3, StateRunning = 6, StateFinishing = 8;

        /// <summary>Client SearchState values (handler FUN_1434e9a50): 0 searching, 1 match found, 3 removed from the queue.</summary>
        public const uint SearchSearching = 0, SearchMatchFound = 1, SearchStopped = 3;

        /// <summary>The matchmaking message in a service packet of family 0x10D, or null.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            var r = new InternalReader(packet.Body);
            return packet.Sub switch
            {
                RequestSub => MatchmakingRequest.Read(r),
                GamesSub => MatchmakingGames.Read(r),
                UpdateSub => MatchmakingUpdate.Read(r),
                _ => null
            };
        }

        /// <summary>Is a game with these kind bits of a kind the searcher ticked (FFA / Teams, PvE / PvP; an unticked pair = either)?</summary>
        public static bool OfKind(ulong kind, ulong search)
        {
            ulong teams = search & (FlagFfa | FlagTeams), pvp = search & (FlagPvE | FlagPvP);
            return (teams == 0 || (teams & kind) != 0) && (pvp == 0 || (pvp & kind) != 0);
        }

        /// <summary>
        /// The window's three game counts over <paramref name="games"/> for a searcher's ticks: "Currently Active games"
        /// (a round on: Started..Finishing), "Games awaiting more players" (no round yet, someone in it or queued),
        /// "Inactive games" (no round, nobody in it or queued). Same rules as the zone's FinderCounts.
        /// </summary>
        public static (uint Active, uint Waiting, uint Inactive) Counts(IEnumerable<MatchmakingGame> games, ulong search)
        {
            uint active = 0, waiting = 0, inactive = 0;
            foreach (MatchmakingGame g in games)
            {
                if (!OfKind(g.Flags, search))
                    continue;
                if (g.State >= StateStarted && g.State <= StateFinishing)
                    active++;
                else if (g.State < StateStarted)
                {
                    if (g.Players > 0 || (g.Flags & FlagQueued) != 0)
                        waiting++;
                    else
                        inactive++;
                }
            }
            return (active, waiting, inactive);
        }

        /// <summary>
        /// Would a searcher with these ticks be matched with this game (the zone's TryMatch rule without the landscape /
        /// already-in-it checks, which only the game's own zone can make): open, of the ticked kinds, and either someone
        /// waiting in it before its round, or its round running with In Progress ticked.
        /// </summary>
        public static bool Matches(MatchmakingGame g, ulong search)
            => (g.Flags & FlagOpen) != 0 && OfKind(g.Flags, search)
               && (g.State == StateRunning ? (search & FlagInProgress) != 0 : g.State < StateRunning && g.Players > 0);
    }

    /// <summary>
    /// MatchmakingRequest (ours, 0x10D / 0x10100, zone → social; world-channel-service.md §6.8): u32 world, u64
    /// character, u64 flag mask, bool searching. A start carries the client's Game Finder flag mask; a stop carries
    /// the mask the search was started with (the client leaves the field unset on stop, packets-695.md §5a). The service
    /// answers a start with SearchState 0 and a stop with SearchState 3 (<see cref="MatchmakingUpdate"/>).
    /// </summary>
    public sealed class MatchmakingRequest : ISocialMessage
    {
        public ushort Family => MatchmakingMessages.InstancedGameFamily;
        public uint Sub => MatchmakingMessages.RequestSub;
        public uint WorldId { get; init; }
        public ulong CharacterGuid { get; init; }
        public ulong Flags { get; init; }
        public bool Searching { get; init; }

        public void Write(InternalWriter w) => w.U32(WorldId).U64(CharacterGuid).U64(Flags).Bool(Searching);
        public static MatchmakingRequest Read(InternalReader r) => new()
        {
            WorldId = r.U32(), CharacterGuid = r.U64(), Flags = r.U64(), Searching = r.Bool()
        };
    }

    /// <summary>
    /// One site game as a zone publishes it: u64 game (the Game Table's prop guid), u32 zone (landscape), u64 flags (the
    /// kind bits FFA 2 / Teams 4 / PvE 8 / PvP 0x10 / In Progress 0x40, plus ours <see cref="MatchmakingMessages.FlagOpen"/>
    /// and <see cref="MatchmakingMessages.FlagQueued"/>), u32 players in it, u32 state (the client's game state number).
    /// Games switched off by their owner are not published. <see cref="NameId"/> / <see cref="Name"/> (the rule set's
    /// name text and the owner's game name, as the client's queue entry shows them) travel in the list's trailing names
    /// section (<see cref="MatchmakingGames"/>), so a zone can offer a game of another world (L31).
    /// </summary>
    public readonly record struct MatchmakingGame(ulong Game, uint Zone, ulong Flags, uint Players, uint State)
    {
        public uint NameId { get; init; }
        public string Name { get; init; }

        public void Write(InternalWriter w) => w.U64(Game).U32(Zone).U64(Flags).U32(Players).U32(State);
        public static MatchmakingGame Read(InternalReader r) => new(r.U64(), r.U32(), r.U64(), r.U32(), r.U32());
    }

    /// <summary>
    /// MatchmakingGames (ours, 0x10D / 0x10101, zone → social): u32 world, u32 n, n × <see cref="MatchmakingGame"/> —
    /// every published game of the world (a whole list: it replaces the previous one) — then, optional (added for L31),
    /// u32 n, n × (u32 name text, str game name) in the same order.
    /// </summary>
    public sealed class MatchmakingGames : ISocialMessage
    {
        public ushort Family => MatchmakingMessages.InstancedGameFamily;
        public uint Sub => MatchmakingMessages.GamesSub;
        public uint WorldId { get; init; }
        public IReadOnlyList<MatchmakingGame> Games { get; init; } = Array.Empty<MatchmakingGame>();

        public void Write(InternalWriter w)
        {
            w.U32(WorldId).U32((uint)Games.Count);
            foreach (MatchmakingGame g in Games)
                g.Write(w);
            w.U32((uint)Games.Count);
            foreach (MatchmakingGame g in Games)
                w.U32(g.NameId).Str(g.Name ?? "");
        }

        public static MatchmakingGames Read(InternalReader r)
        {
            uint world = r.U32(), n = r.U32();
            SocialMessages.CheckCount(n, 100_000, "matchmaking games");
            var list = new List<MatchmakingGame>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add(MatchmakingGame.Read(r));
            if (r.Remaining >= 4 && r.U32() == n)
                for (int i = 0; i < list.Count; i++)
                    list[i] = list[i] with { NameId = r.U32(), Name = r.Str() };
            return new() { WorldId = world, Games = list };
        }

        /// <summary>Same list (order included)? Used by the zone to publish only on change.</summary>
        public static bool Same(IReadOnlyList<MatchmakingGame> a, IReadOnlyList<MatchmakingGame> b)
            => a != null && b != null && a.SequenceEqual(b);
    }

    /// <summary>
    /// MatchmakingUpdate (ours, 0x10D / 0x10102, social → zone): u64 character, u32 players searching (every world),
    /// u32 active, u32 waiting, u32 inactive games (every world, of the searcher's kinds), u32 SearchState (0 / 3), u64
    /// flags (the searcher's mask, as the client's update carries it), u32 otherWorld + u64 otherGame: a game on another
    /// world that would match the search (0 / 0 = none); then, optional (added for L31), that game's u32 zone, u64 kind
    /// flags (client bits only), u32 name text and str name. The searcher's zone offers it as a Game Action Queue "Join
    /// Now" and hands the player over to that world when they take it (InstancedGames.Finder.cs); matches on its own
    /// world it finds itself.
    /// </summary>
    public sealed class MatchmakingUpdate : ISocialMessage
    {
        public ushort Family => MatchmakingMessages.InstancedGameFamily;
        public uint Sub => MatchmakingMessages.UpdateSub;
        public ulong CharacterGuid { get; init; }
        public uint Players { get; init; }
        public uint Active { get; init; }
        public uint Waiting { get; init; }
        public uint Inactive { get; init; }
        public uint SearchState { get; init; }
        public ulong Flags { get; init; }
        public uint OtherWorld { get; init; }
        public ulong OtherGame { get; init; }
        public uint OtherZone { get; init; }
        public ulong OtherFlags { get; init; }
        public uint OtherNameId { get; init; }
        public string OtherName { get; init; } = "";

        public void Write(InternalWriter w) => w.U64(CharacterGuid).U32(Players).U32(Active).U32(Waiting).U32(Inactive)
            .U32(SearchState).U64(Flags).U32(OtherWorld).U64(OtherGame).U32(OtherZone).U64(OtherFlags).U32(OtherNameId).Str(OtherName ?? "");
        public static MatchmakingUpdate Read(InternalReader r)
        {
            ulong character = r.U64();
            uint players = r.U32(), active = r.U32(), waiting = r.U32(), inactive = r.U32(), state = r.U32();
            ulong flags = r.U64();
            uint world = r.U32();
            ulong game = r.U64();
            bool more = r.Remaining >= 16;
            return new()
            {
                CharacterGuid = character, Players = players, Active = active, Waiting = waiting, Inactive = inactive,
                SearchState = state, Flags = flags, OtherWorld = world, OtherGame = game,
                OtherZone = more ? r.U32() : 0, OtherFlags = more ? r.U64() : 0, OtherNameId = more ? r.U32() : 0,
                OtherName = more && r.Remaining > 0 ? r.Str() : ""
            };
        }
    }
}
