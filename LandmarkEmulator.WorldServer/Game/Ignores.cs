using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Ignore lists (the client's Blocked list: BlockedWidget, player right-click Ignore / Stop ignoring, Lua
    /// Ui.IgnorePlayer / IgnorePlayerByGuid / StopIgnoringPlayerByGuid). Decoded 2026-10-12:
    /// <list type="bullet">
    /// <item>C→S Command 0x20 IgnoreRequest: u64 guid (0 when ignoring by name), the name {u32 × 3, string first name,
    /// string last name}, u8 1 = ignore / 0 = stop (captured from the client, re/test/scenarios/adhoc/ignoreprobe.json).</item>
    /// <item>S→C IgnoreBase 0x72 (u8 sub; client dispatcher FUN_143494180): 1 List {u32 n, n × {u64 guid, string
    /// name}}, 2 Add {u64 guid, string name}, 3 Remove {u64 guid}. The client keeps the list (it also feeds the
    /// requirement ClientRequirementIsOnIgnoreList) and prints IgnoreAdded / IgnoreRemoved.</item>
    /// </list>
    /// The server also enforces it: chat from an ignored player doesn't reach the ignoring player, and their trade,
    /// group and guild invitations are refused.
    /// Part of the social service: callers outside it use <see cref="Social.SocialServices"/> (LocalSocialService runs
    /// this code; social-service.md §6 step 1).
    /// </summary>
    public static class Ignores
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private const byte ListSub = 1, AddSub = 2, RemoveSub = 3;
        private const int MaxIgnored = 100;

        private static readonly object sync = new();
        /// <summary>Per online player: ignored character id → name.</summary>
        private static readonly Dictionary<ulong, Dictionary<ulong, string>> lists = new();

        /// <summary>World entry: load the list and send it.</summary>
        public static void Enter(WorldSession session)
        {
            Dictionary<ulong, string> list;
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
                list = context.CharacterIgnore.Where(i => i.CharacterId == session.PlayerGuid)
                    .ToDictionary(i => i.IgnoredId, i => i.Name);
            lock (sync)
                lists[session.PlayerGuid] = list;
            session.EnqueueMessage(ZoneMessageOpcode.IgnoreBase, Build(w =>
            {
                w.Write(ListSub);
                w.Write((uint)list.Count);
                foreach ((ulong id, string name) in list)
                {
                    w.Write(id);
                    w.Write(name ?? "");
                }
            }));
        }

        public static void Leave(WorldSession session)
        {
            lock (sync)
                lists.Remove(session.PlayerGuid);
        }

        /// <summary>
        /// IgnoreListSync from the shared social service (Social/RemoteSocialService, social-service.md §5.4): replaces
        /// this zone's copy of a character's list, which <see cref="Ignoring"/> / <see cref="Refuses"/> read for
        /// everything the zone delivers itself. With the service remote the list lives there and this table is only
        /// the zone-side cache; requests to change it go to the service.
        /// </summary>
        public static void Replace(ulong character, IEnumerable<(ulong Guid, string Name)> list)
        {
            var copy = new Dictionary<ulong, string>();
            foreach ((ulong guid, string name) in list)
                copy[guid] = name ?? "";
            lock (sync)
                lists[character] = copy;
        }

        /// <summary>Does <paramref name="listener"/> ignore <paramref name="speaker"/>?</summary>
        public static bool Ignoring(WorldSession listener, WorldSession speaker)
        {
            if (listener == null || speaker == null)
                return false;
            lock (sync)
                return lists.TryGetValue(listener.PlayerGuid, out var list) && list.ContainsKey(speaker.PlayerGuid);
        }

        /// <summary>
        /// For invitations: true (and the inviter is told) when the invited player ignores the inviter.
        /// </summary>
        public static bool Refuses(WorldSession target, WorldSession inviter)
        {
            if (!Ignoring(target, inviter))
                return false;
            Chat.System(inviter, $"{target.CharacterName} is not accepting invitations from you.");
            log.Info($"{target.CharacterName} ignores {inviter.CharacterName}: invitation refused.");
            return true;
        }

        /// <summary>Command 0x20 IgnoreRequest.</summary>
        public static void Handle(WorldSession session, byte[] body)
        {
            ulong guid;
            string name;
            bool add;
            try
            {
                var r = new GamePacketReader(body);
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
                log.Warn($"Unreadable ignore request from {session.CharacterName}: {BitConverter.ToString(body)}");
                return;
            }

            var database = DatabaseManager.Instance.CharacterDatabase;
            CharacterModel target = guid != 0 ? database.GetCharacterById(guid).GetAwaiter().GetResult()
                : name.Length > 0 ? database.FindCharacterByName(name) : null;
            if (target?.DeleteTime != null)
                target = null;
            if (target == null)
            {
                Chat.System(session, $"There is no player named {name}.");
                return;
            }
            ulong me = session.PlayerGuid, other = target.Id;
            Dictionary<ulong, string> list;
            lock (sync)
            {
                if (!lists.TryGetValue(me, out list))
                    lists[me] = list = new Dictionary<ulong, string>();
            }

            if (add)
            {
                if (other == me)
                {
                    Chat.System(session, "You can't ignore yourself.");
                    return;
                }
                lock (sync)
                {
                    if (list.ContainsKey(other))
                        return;
                    if (list.Count >= MaxIgnored)
                    {
                        Chat.System(session, "Your ignore list is full.");
                        return;
                    }
                    list[other] = target.Name;
                }
                string ignoredName = target.Name;
                database.Save(c => c.CharacterIgnore.Add(new CharacterIgnoreModel { CharacterId = me, IgnoredId = other, Name = ignoredName }));
                session.EnqueueMessage(ZoneMessageOpcode.IgnoreBase, Build(w => { w.Write(AddSub); w.Write(other); w.Write(ignoredName); }));
                log.Info($"{session.CharacterName} ignores {target.Name}.");
            }
            else
            {
                lock (sync)
                {
                    if (!list.Remove(other))
                        return;
                }
                database.Save(c => c.CharacterIgnore.Remove(new CharacterIgnoreModel { CharacterId = me, IgnoredId = other }));
                session.EnqueueMessage(ZoneMessageOpcode.IgnoreBase, Build(w => { w.Write(RemoveSub); w.Write(other); }));
                log.Info($"{session.CharacterName} stopped ignoring {target.Name}.");
            }
        }

        private static byte[] Build(Action<GamePacketWriter> body)
        {
            var output = new List<byte>();
            body(new GamePacketWriter(output));
            return output.ToArray();
        }
    }
}
