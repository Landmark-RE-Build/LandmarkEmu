using LandmarkEmulator.Internal.Social;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// The shared Game Finder as the zone sees it (world-channel-service.md §6.8, social-service.md §9): only there when
    /// the social service is remote (SOCIAL_ADDRESS, <see cref="SocialServices.Matchmaking"/>); in-process the zone's
    /// own Game Finder (InstancedGames.Finder.cs) counts this world alone, exactly as before. Implemented by
    /// <see cref="RemoteSocialService"/>; the service's answers (MatchmakingUpdate) come back through
    /// InstancedGames.FinderServiceUpdate on the world thread.
    /// </summary>
    public interface IMatchmakingService
    {
        /// <summary>Is the link to the service up now?</summary>
        bool IsConnected { get; }

        /// <summary>MatchmakingRequest (0x10D / 0x10100): a search started or stopped; false (nothing sent) while unreachable.</summary>
        bool Search(ulong character, ulong flags, bool searching);

        /// <summary>MatchmakingGames (0x10D / 0x10101): this world's games, whole; false while unreachable.</summary>
        bool PublishGames(IReadOnlyList<MatchmakingGame> games);

        /// <summary>
        /// True once after every (re)connect: the zone then sends its games in full and its searchers again (the service
        /// forgot this world's when the old link closed).
        /// </summary>
        bool TakeResync();
    }
}
