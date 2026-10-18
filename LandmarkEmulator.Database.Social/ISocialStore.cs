using LandmarkEmulator.Database.Social.Model;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Database.Social
{
    /// <summary>
    /// What the social service keeps on disk for presence, friends and ignore (social-service.md §5.2). The service
    /// calls it from its one work loop, so implementations need no locking of their own. <see cref="SocialDatabase"/>
    /// is the MySQL / MariaDB one; a test can supply an in-memory one.
    /// </summary>
    public interface ISocialStore
    {
        /// <summary>A character by guid (deleted ones too), or null.</summary>
        SocialCharacterModel GetCharacter(ulong guid);

        /// <summary>The characters with these guids that the directory knows (any order).</summary>
        IReadOnlyList<SocialCharacterModel> GetCharacters(IReadOnlyCollection<ulong> guids);

        /// <summary>A character that isn't deleted, by name (case-insensitive), or null.</summary>
        SocialCharacterModel FindCharacterByName(string name);

        /// <summary>
        /// A character was seen (entered a world) or created: adds it to the directory, or updates its account and
        /// name (a rename keeps the first name in original_name). <paramref name="deleted"/> marks the character deleted
        /// (now); false leaves an existing mark as it is.
        /// </summary>
        void UpsertCharacter(ulong guid, ulong accountId, string name, bool deleted = false);

        /// <summary>
        /// Where the character is or was: world and landscape, and (when given) when it last left the game and the name
        /// text ids of that landscape / world (0 = keep the stored ones).
        /// </summary>
        void RecordLastSeen(ulong guid, ulong world, uint zone, DateTime? lastOnline, uint zoneNameText = 0, uint worldNameText = 0);

        /// <summary>The guids on the character's friends list.</summary>
        IReadOnlyList<ulong> GetFriends(ulong character);

        /// <summary>The characters who have <paramref name="character"/> on their friends list.</summary>
        IReadOnlyList<ulong> GetFriendOf(ulong character);

        /// <summary>Adds a friend; false when already listed.</summary>
        bool AddFriend(ulong character, ulong friend);

        /// <summary>Removes a friend; false when not listed.</summary>
        bool RemoveFriend(ulong character, ulong friend);

        /// <summary>The character's ignore list: ignored guid → the name stored with it.</summary>
        IReadOnlyDictionary<ulong, string> GetIgnores(ulong character);

        /// <summary>Adds to the ignore list; false when already there.</summary>
        bool AddIgnore(ulong character, ulong ignored, string name);

        /// <summary>Removes from the ignore list; false when not there.</summary>
        bool RemoveIgnore(ulong character, ulong ignored);

        /// <summary>Every guild (loaded once at start; the service keeps them in memory).</summary>
        IReadOnlyList<SocialGuildModel> GetGuilds();

        /// <summary>Every guild membership.</summary>
        IReadOnlyList<SocialGuildMemberModel> GetGuildMembers();

        /// <summary>Adds or replaces a guild row (all its fields).</summary>
        void SaveGuild(SocialGuildModel guild);

        /// <summary>Removes a guild and its memberships.</summary>
        void DeleteGuild(ulong guildId);

        /// <summary>Puts a character in a guild with a rank (adds or replaces its membership).</summary>
        void SetGuildMember(ulong character, ulong guildId, uint rankId);

        /// <summary>Takes a character out of its guild.</summary>
        void RemoveGuildMember(ulong character);

        /// <summary>The newest <paramref name="max"/> messages of an inbox, newest first, with their attachments.</summary>
        IReadOnlyList<SocialMailModel> GetInbox(ulong recipient, int max);

        /// <summary>One message of an inbox with its attachments, or null.</summary>
        SocialMailModel GetMail(ulong id, ulong recipient);

        /// <summary>How many messages an inbox holds.</summary>
        int CountInbox(ulong recipient);

        /// <summary>Is a message with this compose / system-mail token stored already?</summary>
        bool HasMailToken(ulong token);

        /// <summary>Stores messages (with their attachments) in one transaction; their ids are set.</summary>
        void AddMail(IReadOnlyCollection<SocialMailModel> mail);

        /// <summary>Marks a message read.</summary>
        void MarkMailRead(ulong id);

        /// <summary>Deletes a message of an inbox; false when it isn't there.</summary>
        bool DeleteMail(ulong id, ulong recipient);

        /// <summary>Deletes the inbox's messages sent before <paramref name="cutoff"/> that hold nothing still to take.</summary>
        int DeleteExpiredMail(ulong recipient, DateTime cutoff);

        /// <summary>Marks attachment slots of a message taken.</summary>
        void SetMailAttachmentsTaken(ulong mailId, IReadOnlyCollection<uint> slots);

        /// <summary>Does the character have a message still holding an item of this definition?</summary>
        bool HasWaitingMailItem(ulong recipient, uint itemDefinition);
    }
}
