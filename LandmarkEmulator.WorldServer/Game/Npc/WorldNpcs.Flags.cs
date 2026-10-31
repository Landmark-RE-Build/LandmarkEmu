using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.Npc
{
    public static partial class WorldNpcs
    {
        /// <summary>character → npcId → key → value</summary>
        private static readonly Dictionary<ulong, Dictionary<string, Dictionary<string, int>>> flags = new();
        private static readonly object flagSync = new();

        /// <summary>
        /// Flag writes and clears, one after another in the order they were made (each used to be its own Task.Run, so a
        /// reset could land before an earlier write and the flag came back, and a relog could read before "accepted" /
        /// "turned_in" was stored and offer the quest again; trace/0695/phaseE4-logic.md F7).
        /// </summary>
        private static Task flagWrites = Task.CompletedTask;
        private static readonly object flagWritesSync = new();

        private static void Write(Action write)
        {
            lock (flagWritesSync)
                flagWrites = flagWrites.ContinueWith(_ =>
                {
                    try { write(); }
                    catch (Exception e) { log.Error(e, "Saving NPC flags failed."); }
                }, TaskScheduler.Default);
        }

        /// <summary>Loads a character's NPC flags at zone-in (with achievements), after the writes already queued.</summary>
        public static void LoadCharacter(ulong character)
        {
            Task pending;
            lock (flagWritesSync)
                pending = flagWrites;
            if (!pending.Wait(TimeSpan.FromSeconds(5)))
                log.Warn("NPC flag writes still running at a character load; reading what is stored.");
            List<CharacterNpcFlagModel> rows = DatabaseManager.Instance.CharacterDatabase.GetNpcFlags(character);
            lock (flagSync)
            {
                var mine = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                foreach (CharacterNpcFlagModel row in rows)
                {
                    if (!mine.TryGetValue(row.NpcId, out Dictionary<string, int> npc))
                        mine[row.NpcId] = npc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    npc[row.Key] = row.Value;
                }
                flags[character] = mine;
            }
        }

        public static int Flag(ulong character, string npcId, string key)
        {
            lock (flagSync)
                return flags.TryGetValue(character, out var mine)
                    && mine.TryGetValue(npcId, out var npc)
                    && npc.TryGetValue(key, out int v)
                    ? v : 0;
        }

        public static bool HasFlag(ulong character, string npcId, string key) => Flag(character, npcId, key) != 0;

        public static void SetFlag(WorldSession session, string npcId, string key, int value)
        {
            ulong character = session.PlayerGuid;
            lock (flagSync)
            {
                if (!flags.TryGetValue(character, out var mine))
                    flags[character] = mine = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
                if (!mine.TryGetValue(npcId, out var npc))
                    mine[npcId] = npc = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                npc[key] = value;
            }
            var row = new CharacterNpcFlagModel
            {
                CharacterId = character,
                NpcId = npcId,
                Key = key,
                Value = value
            };
            Write(() => DatabaseManager.Instance.CharacterDatabase.SaveNpcFlag(row));
        }

        /// <summary>Clears in-memory and DB flags for this character (all NPCs or one).</summary>
        public static void ClearFlags(ulong character, string npcId = null)
        {
            lock (flagSync)
            {
                if (npcId == null)
                    flags.Remove(character);
                else if (flags.TryGetValue(character, out var mine))
                    mine.Remove(npcId);
            }
            Write(() => DatabaseManager.Instance.CharacterDatabase.ClearNpcFlags(character, npcId));
        }
    }
}
