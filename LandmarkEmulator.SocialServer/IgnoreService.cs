using LandmarkEmulator.Database.Social.Model;
using LandmarkEmulator.Internal.Social;
using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.SocialServer
{
    /// <summary>
    /// Ignore lists (the client's Blocked list), moved out of the world server's Game/Ignores.cs with the same client
    /// behaviour (social-service.md §5.1 item 2):
    /// <list type="bullet">
    /// <item>IgnoreRequest (Command 0x1E/0x20 {u64 guid (0 = by name), name block {u32 × 3, string first, string last},
    /// u8 1 ignore / 0 stop}; captured from the client, re/test/scenarios/adhoc/ignoreprobe.json; Ui.IgnorePlayer…
    /// FUN_14351fcc0 / FUN_143520cd0 / Block FUN_143521d80 / Unblock FUN_143513270).</item>
    /// <item>Answers IgnoreBase 0x72 Add (2) / Remove (3); the list (1) on world entry. The client keeps the list (it
    /// also feeds ClientRequirementIsOnIgnoreList) and prints IgnoreAdded / IgnoreRemoved itself. Limit 100 (ours).</item>
    /// <item>The zone enforces it for what it delivers itself (chat by range, emotes, trade / group / guild invitations,
    /// mail) from IgnoreListSync, pushed on entry and after every change (§5.4).</item>
    /// </list>
    /// </summary>
    public sealed class IgnoreService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const int MaxIgnored = 100;

        private readonly SocialHub hub;

        public IgnoreService(SocialHub hub) => this.hub = hub;

        /// <summary>Each character's ignored guids, read once and kept up to date: chat checks them per recipient and line.</summary>
        private readonly Dictionary<ulong, HashSet<ulong>> cache = new();

        /// <summary>Does <paramref name="listener"/> ignore <paramref name="speaker"/>?</summary>
        public bool Ignoring(ulong listener, ulong speaker)
        {
            if (!cache.TryGetValue(listener, out HashSet<ulong> ignored))
                cache[listener] = ignored = hub.Store.GetIgnores(listener).Keys.ToHashSet();
            return ignored.Contains(speaker);
        }

        /// <summary>World entry: the list to the client (0x72/1) and to its zone (IgnoreListSync).</summary>
        public void SendList(OnlineCharacter character)
        {
            IReadOnlyDictionary<ulong, string> list = hub.Store.GetIgnores(character.Guid);
            hub.SendTo(character, ClientPackets.IgnoreList(list));
            Sync(character, list);
        }

        private void Sync(OnlineCharacter character, IReadOnlyDictionary<ulong, string> list)
        {
            if (character is not { Connected: true })
                return;
            hub.SendService(character.World, new IgnoreListSync
            {
                CharacterGuid = character.Guid,
                Ignored = list.Select(kv => (kv.Key, kv.Value ?? "")).ToList()
            });
        }

        /// <summary>IgnoreRequest, handed on by the zone.</summary>
        public void Handle(WorldLink world, ClientRequestForward request)
        {
            OnlineCharacter player = hub.Presence.FindConnected(world, request.CharacterGuid, request.ConnectionId);
            if (player == null)
            {
                log.Warn($"IgnoreRequest for {request.CharacterGuid:X} on {world} without a current connection: dropped.");
                return;
            }
            ulong guid;
            string name;
            bool add;
            try
            {
                var r = new GamePacketReader(request.Body);
                guid = r.ReadULong();
                r.ReadUInt(); r.ReadUInt(); r.ReadUInt();
                name = (r.ReadString() ?? "").Trim();
                string last = (r.ReadString() ?? "").Trim();
                if (last.Length > 0)
                    name = (name + " " + last).Trim();
                add = r.ReadByte() != 0;
            }
            catch (Exception)
            {
                log.Warn($"Unreadable ignore request from {player}: {BitConverter.ToString(request.Body)}");
                return;
            }

            SocialCharacterModel target = guid != 0 ? hub.Store.GetCharacter(guid)
                : name.Length > 0 ? hub.Store.FindCharacterByName(name) : null;
            if (target?.DeletedAt != null)
                target = null;
            if (target == null)
            {
                hub.SendTo(player, ClientPackets.SystemLine($"There is no player named {name}."));
                return;
            }

            IReadOnlyDictionary<ulong, string> list = hub.Store.GetIgnores(player.Guid);
            if (add)
            {
                if (target.Guid == player.Guid)
                {
                    hub.SendTo(player, ClientPackets.SystemLine("You can't ignore yourself."));
                    return;
                }
                if (list.ContainsKey(target.Guid))
                    return;
                if (list.Count >= MaxIgnored)
                {
                    hub.SendTo(player, ClientPackets.SystemLine("Your ignore list is full."));
                    return;
                }
                hub.Store.AddIgnore(player.Guid, target.Guid, target.Name);
                hub.SendTo(player, ClientPackets.IgnoreAdd(target.Guid, target.Name));
                log.Info($"{player} ignores {target.Name}.");
            }
            else
            {
                if (!hub.Store.RemoveIgnore(player.Guid, target.Guid))
                    return;
                hub.SendTo(player, ClientPackets.IgnoreRemove(target.Guid));
                log.Info($"{player} stopped ignoring {target.Name}.");
            }
            IReadOnlyDictionary<ulong, string> now = hub.Store.GetIgnores(player.Guid);
            cache[player.Guid] = now.Keys.ToHashSet();
            Sync(player, now);
        }
    }
}
