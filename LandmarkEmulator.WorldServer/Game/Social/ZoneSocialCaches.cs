using LandmarkEmulator.WorldServer.Network;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// Ignore checks the zone makes itself (say / shout / emote / party lines, emotes, trade and group invitations):
    /// the receiving player's ignore list. Once the social service is remote this is a cache fed by IgnoreListSync
    /// (spec §5.4, pushed on world entry and on every change).
    /// </summary>
    public interface IIgnoreCache
    {
        /// <summary>Does <paramref name="listener"/> ignore <paramref name="speaker"/>?</summary>
        bool Ignoring(WorldSession listener, WorldSession speaker);

        /// <summary>
        /// For invitations: true (and the inviter is told "X is not accepting invitations from you.") when the invited
        /// player ignores the inviter.
        /// </summary>
        bool Refuses(WorldSession target, WorldSession inviter);
    }

    /// <summary>
    /// Guild membership and names for the zone (claim guild masks and Special Access by guild name, guild tag,
    /// dev commands). Remote: SocialStateForZone (guild id per character) and GuildNameToId (spec §5.4).
    /// </summary>
    public interface IGuildCache
    {
        /// <summary>The guild a character is in (0 = none).</summary>
        ulong GuildIdOf(ulong character);

        /// <summary>The guild with this name (case-insensitive; 0 = none).</summary>
        ulong GuildIdByName(string name);

        /// <summary>A guild's name (null = no such guild).</summary>
        string GuildName(ulong guildId);

        /// <summary>Guild chat (channel 5): the sender's guildmates online in this world (the sender included).</summary>
        List<WorldSession> MembersOf(WorldSession session);

        /// <summary>Officer chat (channel 6): the guildmates online here whose rank has JoinOfficerChat; nobody when the sender can't use it.</summary>
        List<WorldSession> OfficersOf(WorldSession session);

        /// <summary>Is the member muted on guild channel 5 / 6?</summary>
        bool IsMuted(WorldSession session, ushort channel);
    }

    /// <summary>
    /// Who lists a character as a friend (claim "friends" default mask, client claim +0x2d4). Remote: the friend-of
    /// list in SocialStateForZone (spec §5.4).
    /// </summary>
    public interface IFriendOfCache
    {
        /// <summary>The characters who have <paramref name="character"/> on their friends list.</summary>
        IReadOnlyCollection<ulong> FriendOf(ulong character);
    }

    /// <summary>
    /// Where an online character is (teleport to a friend, cross-world travel). Remote: PresenceQuery (spec §5.4),
    /// answered from the service's presence for every world.
    /// </summary>
    public interface IPresenceLookup
    {
        /// <summary>The character's presence, or null when they are offline.</summary>
        SocialPresence Find(ulong character);
    }

    /// <summary>An online character: name, world (WorldServerConfiguration.ZoneServerId) and landscape (zone id).</summary>
    public sealed record SocialPresence(ulong Character, string Name, ulong WorldId, uint ZoneId);
}
