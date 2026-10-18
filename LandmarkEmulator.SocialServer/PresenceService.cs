using LandmarkEmulator.Database.Social.Model;
using LandmarkEmulator.Internal.Social;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// A character the service counts as online: where it is connected now (world link + connection id), or, during
    /// the grace after it left, nowhere (<see cref="World"/> null, <see cref="LeftAt"/> set) while friends still see it
    /// online.
    /// </summary>
    public sealed class OnlineCharacter
    {
        public ulong Guid { get; init; }
        public string Name { get; set; } = "";
        public ulong AccountId { get; set; }
        /// <summary>The world link the character is connected through; null while it is in the leave grace.</summary>
        public WorldLink World { get; set; }
        public uint WorldId { get; set; }
        public ulong ConnectionId { get; set; }
        public uint ZoneId { get; set; }
        /// <summary>Name text ids of its landscape and world (guild roster Location).</summary>
        public uint ZoneNameText { get; set; }
        public uint WorldNameText { get; set; }
        public string ClientBuild { get; set; } = "";
        /// <summary>When it left (UTC); null while connected.</summary>
        public DateTime? LeftAt { get; set; }

        public bool Connected => World != null && LeftAt == null;

        public override string ToString() => $"{Name} ({Guid:X})";
    }

    /// <summary>
    /// Presence for every world (social-service.md §5.1 item 1, §5.5): one record per character guid, fed by the worlds'
    /// connection events (CharacterEnteredWorld / CharacterLeftWorld, PresenceSnapshot on every (re)connect,
    /// ZoneChanged). Rules:
    /// <list type="bullet">
    /// <item>Newest connection wins: a Left for a connection that is no longer the character's current one (a relog's
    /// old session, or the old world after a world change) is ignored.</item>
    /// <item>Leave grace (ours, PresenceGraceSeconds, 30 s): a character who left keeps counting as online; an Entered
    /// within the grace (world change = Left on world A, Entered on world B) sends friends no offline / online note —
    /// the P1679 / P4907 behaviour, with the client's own debounce (FUN_143490ae0 / FUN_143493150) as a second line.
    /// Only when the grace runs out do watchers get FriendOffline. The location update a world change should send
    /// (FriendStatus 0x4D/9) waits for its fields to be traced (L66).</item>
    /// <item>On Entered: the player's friends list (0x4D/1) and ignore list (0x72/1); to its world IgnoreListSync and
    /// SocialStateForZone (§5.4); FriendOnline (0x4D/2) to everyone online who lists the character.</item>
    /// <item>A link that drops puts its characters into the grace; a PresenceSnapshot on the reconnect within the grace
    /// takes them back without notes (a social restart likewise: §5.6) and re-sends every bootstrap; characters of that
    /// world missing from the snapshot have left.</item>
    /// <item>Last world / zone go to social_character on entry, last online on leave (friends' "last online").</item>
    /// </list>
    /// </summary>
    public sealed class PresenceService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private readonly SocialHub hub;
        private readonly TimeSpan grace;
        private readonly Dictionary<ulong, OnlineCharacter> online = new();

        /// <summary>Characters online on any world (work loop; the admin page's status).</summary>
        public int OnlineCount => online.Count;

        public PresenceService(SocialHub hub, TimeSpan grace)
        {
            this.hub = hub;
            this.grace = grace;
        }

        /// <summary>The character's presence (connected or in its leave grace), or null.</summary>
        public OnlineCharacter Find(ulong guid) => online.TryGetValue(guid, out OnlineCharacter c) ? c : null;

        /// <summary>The character connected through this world link with this connection id, or null (stale / unknown).</summary>
        public OnlineCharacter FindConnected(WorldLink world, ulong guid, ulong connectionId)
            => online.TryGetValue(guid, out OnlineCharacter c) && c.Connected && c.World == world && c.ConnectionId == connectionId ? c : null;

        /// <summary>Everyone connected now, on every world (public chat channels).</summary>
        public List<OnlineCharacter> Everyone() => online.Values.Where(c => c.Connected).ToList();

        /// <summary>Everyone online (connected) who has <paramref name="guid"/> on their friends list.</summary>
        public List<OnlineCharacter> Watchers(ulong guid)
            => hub.Store.GetFriendOf(guid).Where(w => w != guid).Select(Find).Where(c => c is { Connected: true }).ToList();

        /// <summary>CharacterEnteredWorld.</summary>
        public void Entered(WorldLink world, CharacterPresence entry)
        {
            if (world.WorldId == 0)
                world.WorldId = entry.WorldId;
            bool known = Register(world, entry);
            OnlineCharacter character = online[entry.CharacterGuid];
            if (!known)
                hub.SendTo(Watchers(character.Guid), ClientPackets.FriendOnline(character.Name, character.Guid));
            Bootstrap(character, announce: !known);
            log.Info($"{character} entered world {entry.WorldId} (zone {entry.ZoneId}){(known ? ", already online: no note to friends" : "")}.");
        }

        /// <summary>
        /// PresenceSnapshot: the link's world id and everyone on it. Characters of the world missing from it left;
        /// everyone in it is (re)registered without notes and gets the bootstrap again; friends on other worlds of
        /// characters the service did not have online get their list re-sent, so a restarted service shows them right
        /// without a burst of "is online" lines.
        /// </summary>
        public void Snapshot(WorldLink world, PresenceSnapshot snapshot)
        {
            world.WorldId = snapshot.WorldId;
            var present = snapshot.Characters.Select(c => c.CharacterGuid).ToHashSet();
            foreach (OnlineCharacter gone in online.Values.Where(c => c.WorldId == snapshot.WorldId && c.LeftAt == null
                         && !present.Contains(c.Guid)).ToList())
                BeginLeave(gone);

            var newcomers = new List<ulong>();
            foreach (CharacterPresence entry in snapshot.Characters)
                if (!Register(world, entry))
                    newcomers.Add(entry.CharacterGuid);
            foreach (CharacterPresence entry in snapshot.Characters)
                Bootstrap(online[entry.CharacterGuid]);

            var refresh = new HashSet<OnlineCharacter>();
            foreach (ulong guid in newcomers)
                foreach (OnlineCharacter watcher in Watchers(guid))
                    if (!present.Contains(watcher.Guid))
                        refresh.Add(watcher);
            foreach (OnlineCharacter watcher in refresh)
                hub.SendTo(watcher, hub.Friends.ListPacket(watcher.Guid));
            log.Info($"Presence snapshot from world {snapshot.WorldId}: {snapshot.Characters.Count} online, {newcomers.Count} new, "
                + $"{refresh.Count} friends lists refreshed elsewhere.");
        }

        /// <summary>
        /// Puts a character into the presence table as connected through this link; true when it already counted as
        /// online (connected elsewhere, or in its leave grace), so no online note is due.
        /// </summary>
        private bool Register(WorldLink world, CharacterPresence entry)
        {
            bool known = online.TryGetValue(entry.CharacterGuid, out OnlineCharacter character);
            if (!known)
                online[entry.CharacterGuid] = character = new OnlineCharacter { Guid = entry.CharacterGuid };
            character.Name = entry.Name;
            character.AccountId = entry.AccountId;
            character.World = world;
            character.WorldId = entry.WorldId;
            character.ConnectionId = entry.ConnectionId;
            character.ZoneId = entry.ZoneId;
            character.ZoneNameText = entry.ZoneNameText;
            character.WorldNameText = entry.WorldNameText;
            character.ClientBuild = entry.ClientBuild ?? "";
            character.LeftAt = null;
            hub.Store.UpsertCharacter(entry.CharacterGuid, entry.AccountId, entry.Name);
            hub.Store.RecordLastSeen(entry.CharacterGuid, entry.WorldId, entry.ZoneId, null, entry.ZoneNameText, entry.WorldNameText);
            return known;
        }

        /// <summary>
        /// What a character gets on entering (§5.5), in one batch before anything else for the connection (§7
        /// "Ordering"): friends list, ignore list, its guild (and an open guild invitation); and its world's zone caches.
        /// <paramref name="announce"/>: it wasn't online before, so guildmates get the online note.
        /// </summary>
        private void Bootstrap(OnlineCharacter character, bool announce = false)
        {
            hub.SendTo(character, hub.Friends.ListPacket(character.Guid));
            hub.Ignores.SendList(character);
            hub.Guilds.Entered(character, announce);
            hub.Friends.PushStateForZone(character.Guid);
            // The character's group (§10): its world learns it, a new connection gets the party window again.
            hub.Groups.Entered(character);
        }

        /// <summary>CharacterLeftWorld: the grace starts (ignored for a connection that is no longer current).</summary>
        public void Left(WorldLink world, CharacterLeftWorld left)
        {
            OnlineCharacter character = FindConnected(world, left.CharacterGuid, left.ConnectionId);
            if (character == null)
            {
                log.Debug($"Left for {left.CharacterGuid:X} connection {left.ConnectionId:X} on {world}: not its current connection, ignored.");
                return;
            }
            BeginLeave(character);
            log.Info($"{character} left world {character.WorldId} ({left.Reason}).");
        }

        /// <summary>A world link dropped: its characters go into the grace (a reconnect's snapshot takes them back).</summary>
        public void LinkLost(WorldLink world)
        {
            foreach (OnlineCharacter character in online.Values.Where(c => c.World == world && c.LeftAt == null).ToList())
                BeginLeave(character);
        }

        private void BeginLeave(OnlineCharacter character)
        {
            DateTime now = DateTime.UtcNow;
            character.World = null;
            character.LeftAt = now;
            hub.Store.RecordLastSeen(character.Guid, character.WorldId, character.ZoneId, now, character.ZoneNameText, character.WorldNameText);
            // Group members see them linkdead during the grace (§10); a group survives a world change.
            hub.Groups.Linkdead(character);
        }

        /// <summary>Once a second: characters whose grace ran out are offline; friends are told (FriendOffline 0x4D/3).</summary>
        public void Tick()
        {
            DateTime now = DateTime.UtcNow;
            foreach (OnlineCharacter character in online.Values.Where(c => c.LeftAt is DateTime left && now - left >= grace).ToList())
            {
                online.Remove(character.Guid);
                hub.SendTo(Watchers(character.Guid), ClientPackets.FriendOffline(character.Guid));
                hub.Guilds.WentOffline(character.Guid);
                hub.Chat.WentOffline(character.Guid);
                hub.Groups.WentOffline(character.Guid);
                log.Info($"{character} is offline.");
            }
        }

        /// <summary>ZoneChanged: the character is on another landscape.</summary>
        public void ZoneChanged(CharacterZoneChanged change)
        {
            if (online.TryGetValue(change.CharacterGuid, out OnlineCharacter character))
            {
                character.ZoneId = change.ZoneId;
                character.WorldId = change.WorldId;
                character.ZoneNameText = change.ZoneNameText;
                character.WorldNameText = change.WorldNameText;
            }
            hub.Store.RecordLastSeen(change.CharacterGuid, change.WorldId, change.ZoneId, null, change.ZoneNameText, change.WorldNameText);
        }

        /// <summary>PresenceQuery: where a character is (teleport to friend, cross-world travel).</summary>
        public void Query(WorldLink world, PresenceQuery query)
        {
            OnlineCharacter character = Find(query.CharacterGuid);
            SocialCharacterModel known = character == null ? hub.Store.GetCharacter(query.CharacterGuid) : null;
            hub.SendService(world, new PresenceReply
            {
                RequestId = query.RequestId,
                CharacterGuid = query.CharacterGuid,
                Online = character != null,
                Name = character?.Name ?? known?.Name ?? "",
                WorldId = character?.WorldId ?? 0,
                ZoneId = character?.ZoneId ?? 0
            });
        }
    }
}
