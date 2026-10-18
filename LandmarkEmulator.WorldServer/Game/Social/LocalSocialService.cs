using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game.Social
{
    /// <summary>
    /// The social service inside this world server: today's code (<see cref="Friends"/>, <see cref="Ignores"/>,
    /// <see cref="Guilds"/>, the routed part of <see cref="Chat"/>, <see cref="ChatRooms"/>, <see cref="Mail"/>) behind
    /// <see cref="ISocialService"/> and the zone caches, unchanged. Data stays in the shared `lmemu_character` tables and
    /// "who is online" is this process's <see cref="PlayerManager"/>, so nothing social crosses worlds yet
    /// (social-service.md §4); the remote service (§6 steps 2-5) changes that without touching the callers.
    /// </summary>
    public sealed class LocalSocialService : ISocialService, IIgnoreCache, IGuildCache, IFriendOfCache, IPresenceLookup
    {
        public void PlayerEntered(WorldSession session)
        {
            Friends.Enter(session);
            Ignores.Enter(session);
            Guilds.Enter(session);
        }

        public void PlayerLeft(WorldSession session)
        {
            ChatRooms.Forget(session);
            PublicChannels.Forget(session);
            Friends.Leave(session);
            Ignores.Leave(session);
            Guilds.Leave(session);
        }

        public void PlayerChangedZone(WorldSession session)
        {
        }

        public bool CharacterDeleted(ulong characterGuid, ulong accountId, string name)
        {
            Friends.CharacterDeleted(characterGuid);
            Guilds.CharacterDeleted(characterGuid, name);
            return true;
        }

        public void AddFriend(WorldSession session, byte[] body) => Friends.HandleAdd(session, body);
        public void ConfirmFriend(WorldSession session, byte[] body) => Friends.HandleResponse(session, body);
        public void RemoveFriend(WorldSession session, byte[] body) => Friends.HandleRemove(session, body);
        public void AdminFriend(WorldSession admin, ushort sub, byte[] body) => Friends.HandleAdminForce(admin, sub, body);
        public void Ignore(WorldSession session, byte[] body) => Ignores.Handle(session, body);
        public void Guild(WorldSession session, ushort sub, byte[] body) => Guilds.Handle(session, sub, body);
        public void ChatRoom(WorldSession session, ushort sub, byte[] body) => ChatRooms.Handle(session, sub, body);
        public void ChatChannel(WorldSession session, ushort sub, byte[] body) => PublicChannels.Handle(session, sub, body);
        public void RouteChat(WorldSession sender, ChatLine line) => Chat.Route(sender, line);
        public void Mail(WorldSession session, MailRequest request) => Game.Mail.Handle(session, request);

        public void SendSystemMail(ulong recipient, string from, string subject, string body, uint coins,
            IEnumerable<(uint Item, uint Count)> items = null, uint kind = 0)
            => Game.Mail.SendSystemMail(recipient, from, subject, body, coins, items, kind);

        public bool HasMailAttachment(ulong recipient, uint itemDefinition) => Game.Mail.HasAttachment(recipient, itemDefinition);

        public HashSet<uint> MailAttachmentKinds(ulong recipient, IReadOnlyCollection<uint> itemDefinitions)
            => Game.Mail.AttachmentKinds(recipient, itemDefinitions);

        // Zone caches: answered straight from the service's own state.

        public bool Ignoring(WorldSession listener, WorldSession speaker) => Ignores.Ignoring(listener, speaker);
        public bool Refuses(WorldSession target, WorldSession inviter) => Ignores.Refuses(target, inviter);

        public ulong GuildIdOf(ulong character) => Guilds.GuildIdOf(character);
        public ulong GuildIdByName(string name) => Guilds.GuildIdByName(name);
        public string GuildName(ulong guildId) => Guilds.GuildName(guildId);
        public List<WorldSession> MembersOf(WorldSession session) => Guilds.MembersOf(session);
        public List<WorldSession> OfficersOf(WorldSession session) => Guilds.OfficersOf(session);
        public bool IsMuted(WorldSession session, ushort channel) => Guilds.IsMuted(session, channel);

        public IReadOnlyCollection<ulong> FriendOf(ulong character)
            => DatabaseManager.Instance.CharacterDatabase.GetFriendOf(character);

        public SocialPresence Find(ulong character)
            => PlayerManager.FindByGuid(character) is WorldSession s
                ? new SocialPresence(s.PlayerGuid, s.CharacterName, ConfigurationManager<WorldServerConfiguration>.Instance.Config.ZoneServerId, s.ZoneId)
                : null;
    }
}
