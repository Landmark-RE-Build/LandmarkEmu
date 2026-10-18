using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Saved bag filters (docs/protocol.md §14 "Bag filters"): the player's named Inventory window filters survive a relog, as on
    /// live, where the server kept them and sent them back at login.
    /// <list type="bullet">
    /// <item><b>Client side</b>: InventoryFilters::ClientInventoryFilterManager at player +0x7ae60 (vtable 1446dd6f8:
    /// slot 1 AddFilter FUN_142efd5e0, 2 RemoveFilter FUN_142efd330, 3 ResetFilters FUN_142efd4f0), driven by the Lua
    /// natives ClientInventory.AddFilter(id, name, filterString) / RemoveFilter(id) / ResetFilters() (FUN_1435df160 /
    /// FUN_1435df100 / FUN_1435df270, luabind_695.tsv). Each change is mirrored in the data source
    /// Character.InventoryFilters {FilterId, FilterName, FilterString} (FUN_142efd930) and reported to the server with
    /// PlayerFilterUpdatePacket 0xDD sub 4 (<see cref="PlayerFilterUpdate"/>) [P]. No shipped 0.695 script or window calls
    /// the three natives (UI/ScriptsBase.bin, UI/Default/*.lua, Inventory.swf: the AS3 InventoryFilter class and
    /// ItemContainerBase.setFilter exist but nothing calls them), so only a Lua call (dev command luacode) makes one.</item>
    /// <item><b>At login</b>: SendSelfToClient's InventoryFilterManager (reader FUN_142c3d470: the filter list, then the
    /// manager's NextFilterId at +0x7ae68) [P]. The client gives a new filter NextFilterId and counts it up without
    /// checking for a filter that already has that id, so the server sends one past the highest stored id (1 with none,
    /// the live capture's value); [I] live's own counter is not on any wire we have, but this can never collide.</item>
    /// <item><b>Server rules</b> (<see cref="Apply"/>): op 0 adds or replaces the filter with that id, op 1 removes it, op 2
    /// removes all. The client never sends id 0 or an empty name / string with op 0 (it removes instead), so such a packet
    /// is dropped. The client has no length limit of its own (Lua strings into SoeUtil::IString; the only bound is its
    /// 8 KB send buffer, SoeUtil::Array&lt;unsigned char,8192&gt; in FUN_142fc5850) [P]; the server keeps at most
    /// <see cref="MaxFilters"/> filters, names up to <see cref="MaxNameBytes"/> and filter strings up to
    /// <see cref="MaxFilterStringBytes"/> bytes [I ours: keeps SendSelf small; a filter string is a short comma-separated
    /// list of item Type values]. A dropped change stays in the client's window until the next login.</item>
    /// <item><b>No reply</b>: the client's 0xDD reader (FUN_142fcf8b0) handles only subs 1 and 3 [P].</item>
    /// </list>
    /// Stored per character (character_inventory_filter). Saving is coalesced per character like Game.Preferences: the
    /// whole set is written in one transaction, and while a write runs newer sets replace each other, so a burst (a reset
    /// and new filters) costs at most two writes.
    /// </summary>
    public static class InventoryFilters
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Most filters kept per character [I ours].</summary>
        public const int MaxFilters = 64;
        /// <summary>Longest name kept, in UTF-8 bytes (the varchar(255) column) [I ours].</summary>
        public const int MaxNameBytes = 255;
        /// <summary>Longest filter string kept, in UTF-8 bytes (the varchar(2048) column) [I ours].</summary>
        public const int MaxFilterStringBytes = 2048;

        /// <summary>What <see cref="Apply"/> did.</summary>
        public enum Outcome { Added, Updated, Removed, Cleared, Unchanged, Rejected }

        /// <summary>
        /// Applies one client change to <paramref name="filters"/> (in place). <paramref name="why"/> says why a change
        /// was rejected (null otherwise). Only Added / Updated / Removed / Cleared change the list.
        /// </summary>
        public static Outcome Apply(List<CharacterInventoryFilterModel> filters, PlayerFilterUpdate update, out string why)
        {
            why = null;
            switch (update.Op)
            {
                case PlayerFilterUpdate.AddOrUpdate:
                {
                    if (update.FilterId == 0)
                        return Reject("id 0 (the client always gives a new filter its NextFilterId)", out why);
                    if (update.FilterNameBytes == 0 || update.FilterStringBytes == 0)
                        return Reject("an empty name or filter string (the client removes the filter instead)", out why);
                    if (update.FilterNameBytes > MaxNameBytes)
                        return Reject($"a {update.FilterNameBytes}-byte name (most {MaxNameBytes})", out why);
                    if (update.FilterStringBytes > MaxFilterStringBytes)
                        return Reject($"a {update.FilterStringBytes}-byte filter string (most {MaxFilterStringBytes})", out why);
                    CharacterInventoryFilterModel existing = filters.FirstOrDefault(f => f.FilterId == update.FilterId);
                    if (existing != null)
                    {
                        if (existing.Name == update.FilterName && existing.FilterString == update.FilterString)
                            return Outcome.Unchanged;
                        existing.Name = update.FilterName;
                        existing.FilterString = update.FilterString;
                        return Outcome.Updated;
                    }
                    if (filters.Count >= MaxFilters)
                        return Reject($"a new filter past the limit of {MaxFilters}", out why);
                    filters.Add(new CharacterInventoryFilterModel
                    {
                        FilterId = update.FilterId,
                        Name = update.FilterName,
                        FilterString = update.FilterString
                    });
                    return Outcome.Added;
                }
                case PlayerFilterUpdate.Remove:
                    if (update.FilterId == 0)
                        return Reject("removing id 0 (the client never does)", out why);
                    return filters.RemoveAll(f => f.FilterId == update.FilterId) > 0 ? Outcome.Removed : Outcome.Unchanged;
                case PlayerFilterUpdate.Reset:
                    if (filters.Count == 0)
                        return Outcome.Unchanged;
                    filters.Clear();
                    return Outcome.Cleared;
                default:
                    return Reject($"unknown op {update.Op}", out why);
            }
        }

        private static Outcome Reject(string reason, out string why)
        {
            why = reason;
            return Outcome.Rejected;
        }

        /// <summary>The NextFilterId to send: one past the highest id, 1 with none (the live value and the client's default).</summary>
        public static uint NextFilterId(IEnumerable<CharacterInventoryFilterModel> filters)
        {
            uint highest = 0;
            foreach (CharacterInventoryFilterModel filter in filters)
                highest = Math.Max(highest, filter.FilterId);
            return highest == uint.MaxValue ? uint.MaxValue : highest + 1;
        }

        /// <summary>SendSelfToClient's InventoryFilterManager for these filters (map key = filter id).</summary>
        public static SendSelfToClient.InventoryFilterManager SelfData(IReadOnlyList<CharacterInventoryFilterModel> filters)
        {
            var data = new SendSelfToClient.InventoryFilterManager();
            if (filters == null)
                return data;
            lock (filters)
            {
                foreach (CharacterInventoryFilterModel filter in filters)
                    data.Filters.Add(new SendSelfToClient.InventoryFilterManager.PlayerInventoryFilter
                    {
                        Key = filter.FilterId,
                        FilterId = filter.FilterId,
                        FilterName = filter.Name,
                        FilterString = filter.FilterString
                    });
                data.NextFilterId = NextFilterId(filters);
            }
            return data;
        }

        /// <summary>
        /// The character's filters at login: the newest set this server still has to write, else the database's (an empty
        /// list when that fails; logged).
        /// </summary>
        public static List<CharacterInventoryFilterModel> Load(ulong characterId)
        {
            lock (gate)
            {
                if (pending.TryGetValue(characterId, out List<CharacterInventoryFilterModel> newest)
                    || (writing.TryGetValue(characterId, out newest) && newest != null))
                    return Copy(newest);
            }
            try
            {
                return DatabaseManager.Instance.CharacterDatabase.GetInventoryFilters(characterId);
            }
            catch (Exception e)
            {
                log.Error(e, $"Loading the bag filters of character {characterId:X} failed; none are sent.");
                return new List<CharacterInventoryFilterModel>();
            }
        }

        /// <summary>PlayerFilterUpdatePacket from the client (the body after the sub byte): applied, kept on the session and saved.</summary>
        public static void Handle(WorldSession session, byte[] body)
        {
            ulong characterId = session.CharacterId;
            List<CharacterInventoryFilterModel> filters = session.InventoryFilters;
            if (characterId == 0 || filters == null)
                return;
            if (!PlayerFilterUpdate.TryParse(body, out PlayerFilterUpdate update))
            {
                log.Debug($"Unreadable filter update from {session.CharacterName}: {Convert.ToHexString(body ?? Array.Empty<byte>())}");
                return;
            }
            List<CharacterInventoryFilterModel> snapshot;
            lock (filters)
            {
                Outcome outcome = Apply(filters, update, out string why);
                if (outcome == Outcome.Rejected)
                {
                    log.Info($"{session.CharacterName}: bag filter change dropped ({why}).");
                    return;
                }
                if (outcome == Outcome.Unchanged)
                    return;
                log.Debug($"{session.CharacterName}: bag filter {update.FilterId} {outcome} ({filters.Count} kept).");
                snapshot = Copy(filters);
            }
            Save(characterId, snapshot);
        }

        private static List<CharacterInventoryFilterModel> Copy(List<CharacterInventoryFilterModel> filters)
            => filters.Select(f => new CharacterInventoryFilterModel
            {
                CharacterId = f.CharacterId, FilterId = f.FilterId, Name = f.Name, FilterString = f.FilterString
            }).ToList();

        private static readonly object gate = new();
        /// <summary>Per character: the newest set not yet handed to a write.</summary>
        private static readonly Dictionary<ulong, List<CharacterInventoryFilterModel>> pending = new();
        /// <summary>Per character: the set a running write is storing (also marks that a writer runs).</summary>
        private static readonly Dictionary<ulong, List<CharacterInventoryFilterModel>> writing = new();

        private static void Save(ulong characterId, List<CharacterInventoryFilterModel> filters)
        {
            lock (gate)
            {
                pending[characterId] = filters;
                if (writing.ContainsKey(characterId))
                    return;          // the running writer takes it next
                writing[characterId] = null;
            }
            _ = Task.Run(() => Drain(characterId));
        }

        /// <summary>Writes the character's newest set until none is left (one writer per character, so in order).</summary>
        private static async Task Drain(ulong characterId)
        {
            while (true)
            {
                List<CharacterInventoryFilterModel> next;
                lock (gate)
                {
                    if (!pending.Remove(characterId, out next))
                    {
                        writing.Remove(characterId);
                        return;
                    }
                    writing[characterId] = next;
                }
                try
                {
                    await DatabaseManager.Instance.CharacterDatabase.ReplaceInventoryFilters(characterId, next);
                }
                catch (Exception e)
                {
                    // ReplaceInventoryFilters logs database failures; this also covers a missing database (tests).
                    log.Debug(e, $"Bag filters of character {characterId:X} not saved.");
                }
            }
        }
    }
}
