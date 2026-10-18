using LandmarkEmulator.Shared.Database;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// What happens in the world when a character is deleted (TASKS 6.42). The client deletes from character select:
    /// the login server (AuthServer LoginHandler.HandleCharacterDeleteRequest) only marks the row (DeleteTime, Name
    /// cleared, OriginalName kept) and has no link to the worlds or the social service. So each world sweeps the
    /// character database for deleted characters — every one once at start, then every <see cref="Interval"/> those
    /// deleted since the last look — and cleans up after them. Every step is idempotent, so a restart, or several
    /// worlds sharing one social service, only repeat no-ops.
    ///
    /// Live rules, all from the patch notes:
    /// <list type="bullet">
    /// <item>P4624 (13 Apr 2016) "Deleted characters will now be correctly removed from friend and guild lists" and
    /// P5311 (11 May) "Officer and Guild Chat channels should no longer remain after deleting and recreating a
    /// character": <see cref="Social.ISocialService.CharacterDeleted"/> (local: Friends / Guilds; remote: the social
    /// service, told by CharacterDirectoryUpdate Deleted, does it in FriendService / GuildService).</item>
    /// <item>P5312 (11 May), Guild News keeps a deleted player's name: neither guild
    /// implementation keeps guild news yet; the RemoveMember both send carries the name the member had.</item>
    /// <item>P4958 (26 Apr), deleting a character saves each of its build sites on that world as a design, and P5412
    /// (11 May), deleted characters leave the sites' Special Access lists: need the site store (Claims: its site list
    /// and Special Access maps are
    /// private). NOT DONE HERE YET — see <see cref="SitesPending"/>; reported to the Claims owner.</item>
    /// </list>
    /// Runs on the world thread (Social.SocialServices.Update); the database read runs in the background.
    /// </summary>
    public static class CharacterDeletion
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>How often deleted characters are looked for (ours: deleting is rare; a minute is soon enough).</summary>
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
        /// <summary>Overlap between looks, so a delete saved while a look ran is not missed (clock / commit skew).</summary>
        private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);

        private static DateTime nextLook = DateTime.MinValue;
        /// <summary>Null until the first (full) look; after it, the start of the last successful look.</summary>
        private static DateTime? lastLook;
        private static DateTime lookStarted;
        private static Task<List<(ulong Id, ulong AccountId, string Name, DateTime DeleteTime)>> pending;
        /// <summary>Characters whose clean-up is done (this run of the server).</summary>
        private static readonly HashSet<ulong> done = new();

        /// <summary>
        /// The site steps (P4958 design of each site, P5412 Special Access) wait for a Claims entry point: something like
        /// <c>Claims.CharacterDeleted(ulong guid, string name)</c> that saves each of the character's sites as a design
        /// (SiteDesigns.Save), removes it (Claims.Remove: database, clients, terrain revert as Claims.Delete does) and drops
        /// the character from every site's Special Access map and specialNames (and its claim_permission rows, then
        /// resends the owners' Site Management data). False until that exists; logged once per deleted character.
        /// </summary>
        public static bool SitesPending => false;

        /// <summary>World tick: starts a look when due, and works its result when it is back.</summary>
        public static void Update()
        {
            if (pending != null)
            {
                if (!pending.IsCompleted)
                    return;
                Task<List<(ulong Id, ulong AccountId, string Name, DateTime DeleteTime)>> finished = pending;
                pending = null;
                if (finished.IsFaulted)
                {
                    log.Warn(finished.Exception?.GetBaseException(), "Could not read deleted characters; trying again later.");
                    return;
                }
                bool allDone = true;
                foreach (var character in finished.Result)
                    allDone &= CleanUp(character.Id, character.AccountId, character.Name);
                // Only move the window on when everything in it went through (a remote service that was down gets
                // the same characters again next time).
                if (allDone)
                    lastLook = lookStarted;
                return;
            }
            DateTime now = DateTime.UtcNow;
            if (now < nextLook)
                return;
            nextLook = now + Interval;
            lookStarted = now;
            DateTime? since = lastLook?.Subtract(Overlap);
            pending = Task.Run(() => DatabaseManager.Instance.CharacterDatabase.GetDeletedCharacters(since));
        }

        /// <summary>Cleans up after one deleted character; true when every step that can run went through.</summary>
        private static bool CleanUp(ulong id, ulong accountId, string name)
        {
            if (done.Contains(id))
                return true;
            if (!Social.SocialServices.Service.CharacterDeleted(id, accountId, name))
                return false;
            // Its sites become designs and go; its Special Access rows go (P4958, P5412).
            Claims.CharacterDeleted(id, name);
            done.Add(id);
            log.Debug($"Deleted character {name} ({id:X}) cleaned up.");
            return true;
        }
    }
}
