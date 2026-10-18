using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Friend = LandmarkEmulator.AuthServer.Network.Message.Model.CharacterSelectInfoReply.Character.CharacterPayload.Friend;

namespace LandmarkEmulator.AuthServer
{
    /// <summary>
    /// The friends list in each character of the character list (CharacterSelectInfoReply payload, Friends::Friend
    /// records, see <see cref="Friend"/>). The client keeps the list per character and, for the selected one, counts each
    /// world's online friends (FUN_1430c43e0 → server list column FriendsOnline); live sent it [I: the client is built to
    /// read it; no live capture of the character list]. AuthServer.json CharacterSelectFriends (Docker
    /// CHARACTER_SELECT_FRIENDS, default true) turns it off.
    /// <para>Where the lists come from:</para>
    /// <list type="bullet">
    /// <item>With the shared social service (AuthServer.json SocialAddress = the worlds' SOCIAL_ADDRESS): asked of the
    /// service over its internal port (<see cref="SocialFriendsClient"/>, CharacterSelectFriendsQuery), which holds the
    /// friends lists (lmemu_social) and who is online on which world and landscape. No answer in 3 s: the lists go out
    /// empty (logged) rather than stale.</item>
    /// <item>Without it the worlds keep the friends lists themselves in lmemu_character (character_friend, the one the
    /// login server reads its characters from) and each world knows only its own players online: the lists come from
    /// that table with every friend offline (the login server has no presence of its own).</item>
    /// </list>
    /// Online friends carry the Status XML with the world they are on (<see cref="Friend.StatusXml"/>); offline ones an
    /// empty status (the client never parses it for them).
    /// </summary>
    public static class CharacterSelectFriends
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static SocialFriendsClient social;

        /// <summary>At start: dial the social service when an address is set.</summary>
        public static void Initialise(AuthServerConfiguration config)
        {
            string address = config?.SocialAddress?.Trim();
            if (string.IsNullOrEmpty(address) || config?.CharacterSelectFriends == false)
                return;
            social = new SocialFriendsClient(address, text => log.Info(text));
            log.Info($"Character select friends from the social service at {address}.");
        }

        public static void Shutdown()
        {
            social?.Dispose();
            social = null;
        }

        /// <summary>Each character's friends records (an empty list for one without friends, or with the feature off).</summary>
        public static async Task<IReadOnlyDictionary<ulong, List<Friend>>> ForCharacters(IReadOnlyCollection<ulong> characters)
        {
            var result = characters.Distinct().ToDictionary(c => c, _ => new List<Friend>());
            if (ConfigurationManager<AuthServerConfiguration>.Instance.Config?.CharacterSelectFriends == false || result.Count == 0)
                return result;

            if (social != null)
            {
                IReadOnlyDictionary<ulong, IReadOnlyList<CharacterSelectFriend>> answer = await social.GetFriends(result.Keys);
                if (answer != null)
                    foreach ((ulong character, IReadOnlyList<CharacterSelectFriend> friends) in answer)
                        if (result.TryGetValue(character, out List<Friend> list))
                            list.AddRange(friends.Select((f, i) => ToRecord(f, (uint)i)));
                return result;
            }

            try
            {
                await Task.Run(() =>
                {
                    foreach ((ulong character, List<Friend> list) in result)
                    {
                        List<CharacterModel> friends = DatabaseManager.Instance.CharacterDatabase.GetFriends(character);
                        list.AddRange(friends.Take(CharacterSelectFriendsReply.MaxFriends)
                            .Select((f, i) => ToRecord(new CharacterSelectFriend(f.Id, f.Name, false, 0, 0), (uint)i)));
                    }
                });
            }
            catch (Exception e)
            {
                // The character list still goes out (without friends) when the friends table can't be read.
                log.Error(e, "Character select friends: reading character_friend failed.");
                foreach (List<Friend> list in result.Values)
                    list.Clear();
            }
            return result;
        }

        /// <summary>The client record for one friend: numbered key, name, guid, online, and the Status XML when online.</summary>
        public static Friend ToRecord(CharacterSelectFriend friend, uint key) => new()
        {
            Key = key,
            Name = friend.Name ?? "",
            CharacterGuid = friend.Guid,
            Online = friend.Online,
            Status = friend.Online ? Friend.StatusXml(friend.WorldId, friend.ZoneId) : ""
        };
    }
}
