using System;

namespace LandmarkEmulator.Database.Social.Model
{
    /// <summary>
    /// social_guild: a guild, shared by every world (social-service.md §5.2, guilds across worlds: the roster's
    /// "world:zone" Location column). From lmemu_character.guild (<see cref="SocialImport"/>). The ranks are kept as the
    /// world server kept them: a JSON list of {Id, Level, Name, Permissions}.
    /// </summary>
    public class SocialGuildModel
    {
        public ulong Id { get; set; }
        public string Name { get; set; } = "";
        public string Motd { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>The GuildGameData XML (emblem, recruitment, keywords / tag) the client sends with Guild.SetTag.</summary>
        public string AppData { get; set; } = "";
        public ulong LeaderGuid { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>The ranks as JSON; null or empty = the three default ranks.</summary>
        public string Ranks { get; set; }
        /// <summary>The guild window's News: the newest log rows as JSON (as the world server keeps them).</summary>
        public string Log { get; set; }
    }

    /// <summary>social_guild_member: one row per member (a character is in at most one guild), with its rank id.</summary>
    public class SocialGuildMemberModel
    {
        public ulong CharacterGuid { get; set; }
        public ulong GuildId { get; set; }
        public uint RankId { get; set; }
    }
}
