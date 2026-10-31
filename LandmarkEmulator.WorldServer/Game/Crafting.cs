using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Model;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Crafting at stations: checks the request against the recipe tables (never trusting the client), takes the
    /// components up front, and hands over the output once the recipe's casting time has passed.
    /// <para>Closing a station window from the server (OPEN-LEADS L177): RecipePacketZoneCraftingWindow 0x42/0x24
    /// {u32 sub, u32 (+0x18, ctor default 0xFFFFFFFF, not read by the handler), bool (+0x1c)} (0.695 reader FUN_142f300e0,
    /// Crafting dispatcher case 0x24) [P]: bool 0 → FUN_142f381b0 sends CraftWindowEnableRequest 6 {u64 station, bool 0}
    /// (handled as <see cref="StationClosed"/>) and closes the window with GameEvents:OnCloseCraftingWindow, or
    /// HudStateManager:OnCloseRepairWindow for a repair station (type 6); bool 1 rebuilds it (FUN_142f3c630(…, 1)). The
    /// client never closes a station window by distance itself (FUN_142f3c630 / FUN_142f381b0 are reached only from this
    /// packet, the window-open response and the Lua window toggle), so a window stayed open as its player walked away,
    /// and the next craft failed with "You are too far from the station." The server closes it when the player is more
    /// than <see cref="MaxStationDistance"/> from the station or the station prop is gone (checked twice a second;
    /// setting CraftingWindowCloseAway, default true = live: Update Notes 02.20.14, "The crafting window will now close
    /// automatically when you move away from the crafting station." (design/wiki/pages/Update Notes 02.20.14.wiki) [P]).</para>
    /// <para>Recipe cooldowns (PlayerRecipeDataUpdatePacket 0x42/0x1E; the crafting list's CooldownRemaining /
    /// CooldownTotal, FUN_143613ab0: recipe +0x44 minus the time since the player's last craft) need nothing: every
    /// recipe's COOLDOWN_TIME is 0 in the client's own Resources/Recipes.txt (0.695: 2,363 rows; 0.589: 2,331) [P].</para>
    /// </summary>
    public static class Crafting
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private class Job
        {
            public WorldSession Session { get; init; }
            public RecipeEntry Recipe { get; init; }
            public uint Count { get; init; }
            public DateTime Done { get; init; }
            /// <summary>What the craft cost (item, count), handed back if it is cancelled; empty when not refundable.</summary>
            public List<(uint Item, uint Count)> Paid { get; init; } = new();
        }

        private static readonly object sync = new();
        private static readonly List<Job> jobs = new();
        private static readonly Dictionary<WorldSession, (uint Station, ulong PropGuid)> openStations = new();

        /// <summary>
        /// Repair windows open, with where the player stood: the repair window tells the server nothing when it closes,
        /// so walking away from where it was opened counts as closing it.
        /// </summary>
        private static readonly Dictionary<WorldSession, System.Numerics.Vector3> openRepairWindows = new();

        /// <summary>CraftingStationTypes: 6 = repair station (its window is the Repair widget).</summary>
        private const uint RepairStationType = 6;

        /// <summary>How far from the station a player may be when starting a craft (a little over its use range).</summary>
        private const float MaxStationDistance = 8f;

        /// <summary>Most crafts one request may ask for.</summary>
        private const uint MaxCount = 100;

        /// <summary>RecipeBase sub 0x24 RecipePacketZoneCraftingWindow.</summary>
        private const uint ZoneCraftingWindowSub = 0x24;

        /// <summary>CraftingWindowCloseAway: the server closes a station window its player walked away from (true).</summary>
        private static bool CloseAwayEnabled =>
            closeAway ??= LandmarkEmulator.Shared.Configuration.SharedConfiguration.Configuration?.GetValue("CraftingWindowCloseAway", true) ?? true;
        private static bool? closeAway;

        private static DateTime lastAwayCheck = DateTime.MinValue;

        /// <summary>RecipePacketZoneCraftingWindow body: u32 sub 0x24, u32 station [I], bool refresh (false closes the window).</summary>
        public static byte[] CraftingWindowBody(uint station, bool refresh)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(ZoneCraftingWindowSub);
            w.Write(station);
            w.Write(refresh);
            return output.ToArray();
        }

        /// <summary>Closes the player's station window (the client answers with CraftWindowEnableRequest 6).</summary>
        public static void CloseWindow(WorldSession session, string why = null)
        {
            uint station;
            lock (sync)
            {
                if (!openStations.Remove(session, out var open))
                    return;
                openRepairWindows.Remove(session);
                openReach.Remove(session);
                station = open.Station;
            }
            session.EnqueueMessage(ZoneMessageOpcode.RecipeBase, CraftingWindowBody(station, false));
            ResetCursor(session);
            log.Info($"{session.CharacterName}: station {station} window closed by the server{(why != null ? " (" + why + ")" : "")}.");
        }

        /// <summary>
        /// Twice a second: station windows whose player walked off (or whose station prop is gone) are closed. World
        /// stations by their prop's position, the repair window (no prop kept) by where it was opened; UI stations (55)
        /// and dev-opened windows stay.
        /// </summary>
        private static void CloseWindowsAway()
        {
            if (!CloseAwayEnabled || (DateTime.UtcNow - lastAwayCheck).TotalMilliseconds < 500)
                return;
            lastAwayCheck = DateTime.UtcNow;
            List<(WorldSession Session, ulong Prop, System.Numerics.Vector3? OpenedAt)> open;
            lock (sync)
            {
                if (openStations.Count == 0)
                    return;
                open = openStations.Where(o => !o.Key.Disconnected && (o.Value.PropGuid != 0 || openRepairWindows.ContainsKey(o.Key)))
                    .Select(o => (o.Key, o.Value.PropGuid, openRepairWindows.TryGetValue(o.Key, out var at) ? at : (System.Numerics.Vector3?)null))
                    .ToList();
            }
            foreach (var (session, prop, openedAt) in open)
            {
                System.Numerics.Vector3? station = prop != 0 ? StationPosition(prop) ?? openedAt : openedAt;
                float limit;
                lock (sync)
                    limit = Math.Max(MaxStationDistance, (openReach.TryGetValue(session, out float reach) ? reach : 0f) + ReachMargin);
                float distance = station is System.Numerics.Vector3 at ? System.Numerics.Vector3.Distance(at, session.Position) : float.NaN;
                if (distance <= limit)
                    continue;
                CloseWindow(session, float.IsNaN(distance) ? "its station is gone" : $"{distance:0.0} m from it, limit {limit:0.0} m");
            }
        }

        /// <summary>
        /// Where a station prop stands: a placed prop (<see cref="Props.PositionOf"/>) or one of a Spire's own stations
        /// (<see cref="Spire.PositionOf"/>: its Replicators and Repair Station are template props, not placed ones); null
        /// when it is gone. Until 2026-10-21 only placed props were looked up, so every Spire station window was closed
        /// as "gone" on the next tick, half a second after it opened.
        /// </summary>
        private static System.Numerics.Vector3? StationPosition(ulong prop) => Props.PositionOf(prop) ?? Spire.PositionOf(prop);

        /// <summary>
        /// Is the player too far from their open station to craft? The walk-away close's own station and range: the station's
        /// position among placed props or the Spire's (<see cref="StationPosition"/>), within max(8 m, its reach when opened
        /// + 3 m).
        /// </summary>
        internal static bool StationTooFar(WorldSession session, ulong propGuid, uint recipeId = 0)
        {
            float limit;
            lock (sync)
                limit = Math.Max(MaxStationDistance, (openReach.TryGetValue(session, out float reach) ? reach : 0f) + ReachMargin);
            if (StationPosition(propGuid) is not System.Numerics.Vector3 stationPosition)
                return true;
            float distance = System.Numerics.Vector3.Distance(stationPosition, session.Position);
            if (distance <= limit)
                return false;
            log.Info($"{session.CharacterName}: recipe {recipeId} refused, {distance:0.0} m from station 0x{propGuid:X} (limit {limit:0.0} m).");
            return true;
        }

        /// <summary>
        /// Players' distance from the station prop's pivot when they opened it: a big station's pivot can be further than
        /// <see cref="MaxStationDistance"/> from where it can be clicked, so the window closes only once the player is
        /// <see cref="ReachMargin"/> further away than that.
        /// </summary>
        private static readonly Dictionary<WorldSession, float> openReach = new();

        /// <summary>How much further than at the open a player may walk before the window closes, metres.</summary>
        private const float ReachMargin = 3f;

        /// <summary>
        /// Remembers which station a player opened, and its prop (0 for a dev-opened window, which skips the
        /// distance check).
        /// </summary>
        public static void StationOpened(WorldSession session, uint station, ulong propGuid)
        {
            float? reach = propGuid != 0 && StationPosition(propGuid) is System.Numerics.Vector3 at
                ? System.Numerics.Vector3.Distance(at, session.Position) : null;
            lock (sync)
            {
                openStations[session] = (station, propGuid);
                if (reach is float r)
                    openReach[session] = r;
                else
                    openReach.Remove(session);
                if (GameTableManager.Instance.CraftingStations?.GetEntry(station)?.CraftingStationTypeId == RepairStationType)
                    openRepairWindows[session] = session.Position;
            }
        }

        /// <summary>
        /// The crafting and repair windows leave the cursor overridden: their widgets call
        /// HudStateManager:OnSetCursor("RepairWindow", 2) on mouse over and on mouse out (CraftingWindowWidget /
        /// RepairWidget), and nothing in the UI scripts resets it, so the hover cursor stays an arrow. After the window
        /// closes the server resets it (OnSetCursor with cursor 0) through InteractionRunClientLua.
        /// </summary>
        private static void ResetCursor(WorldSession session) => ClientLua.Run(session, "HudStateManager:OnSetCursor", 0f, 0f);

        /// <summary>Stations whose window the client opens from its own UI, with no station prop in the world (55: the
        /// Lumen shop, behind the "+" by the coin counter).</summary>
        private static readonly HashSet<uint> UiStations = new() { 55 };

        /// <summary>
        /// RecipeBase 0x23 PlayerOpenCraftingWindow {u32 station} (FUN_142ea0fe0): the client opened a station window
        /// itself. Only UI stations count; a world station's window is opened by the server (InteractRequest).
        /// </summary>
        public static void UiStationOpened(WorldSession session, uint station)
        {
            if (!UiStations.Contains(station))
                return;
            StationOpened(session, station, 0);
            if (station == 55)
                Achievements.Progress(session, Achievements.OpenLumenStation);
        }

        /// <summary>RecipeBase 6 CraftWindowEnableRequest {u64 station prop, bool false} (FUN_142e99610): the window closed.</summary>
        public static void StationClosed(WorldSession session)
        {
            lock (sync)
            {
                openStations.Remove(session);
                openRepairWindows.Remove(session);
                openReach.Remove(session);
            }
            ResetCursor(session);
        }

        /// <summary>
        /// RecipeBase 1 StartRequest: starts crafting <paramref name="count"/> of a recipe at the station whose window is
        /// open, or reports why not (StartResponse, sub 2).
        /// </summary>
        public static void HandleStart(WorldSession session, uint recipeId, uint count)
        {
            string error = TryStart(session, recipeId, count);
            if (error != null)
                log.Info($"{session.CharacterName} could not craft recipe {recipeId} x{count}: {error}");
            SendResponse(session, 2, error == null, error ?? "");
        }

        /// <summary>
        /// Checks the request (known recipe, count, an open station that can make it, the components in the inventory),
        /// takes the components and queues the job. Returns an error message, or null when crafting started.
        /// </summary>
        private static string TryStart(WorldSession session, uint recipeId, uint count)
        {
            if (count == 0 || count > MaxCount)
                return "Invalid count.";
            RecipeEntry recipe = GameTableManager.Instance.Recipes.GetEntry(recipeId);
            if (recipe == null)
                return "Unknown recipe.";
            if (!recipe.DefaultLearned && !session.LearnedRecipes.Contains(recipeId))
                return "You don't know that recipe.";

            (uint Station, ulong PropGuid) open;
            lock (sync)
            {
                if (jobs.Any(j => j.Session == session))
                    return "You are already crafting.";
                openStations.TryGetValue(session, out open);
            }
            uint station = open.Station;
            // The same station and range as the walk-away close (CloseWindowsAway): the Spire's own stations are template
            // props, not placed ones, so Props.PositionOf alone missed them and every craft at a Spire Replicator was
            // refused as too far (player report 2026-10-31; trace/0695/craft-a-client.md, craft-b-server.md); and a big
            // station's pivot can lie further than 8 m from where it is clicked.
            if (open.PropGuid != 0 && StationTooFar(session, open.PropGuid, recipeId))
                return "You are too far from the station.";
            if (!recipe.CanCraftWithoutStation && !GameTableManager.Instance.CraftingStationRecipeMappings.Entries
                    .Any(m => m.CraftingStationId == station && m.RecipeId == recipeId))
                return "That recipe needs a different station.";
            // A one-time Lumen Station unlock the player already owns (L43, user decision 2026-10-17): refused before
            // anything is taken.
            if (recipe.VisibilityRequirementId != 0 && UnlockRefusal(session, recipe, count) is uint refusal)
                return TextManager.Instance.GetTextForId(refusal) ?? "You already have that.";

            List<RecipeComponentEntry> components = GameTableManager.Instance.ComponentsOf(recipeId).ToList();
            // Type 1 = an item. The other types (item category / category group / keywords) only occur in salvaging
            // recipes, which Salvaging handles with the player's chosen items.
            if (components.Any(c => c.ComponentType != 1))
                return "That recipe isn't supported yet.";
            // One need per item: 18 recipes list the same item in two component rows (the RECIPE_ID rows of
            // RecipeComponents.txt next to the RecipeRecipeComponentMaps rows, e.g. the forge's recipe 28: ore 108 × 600
            // and × 150). Checked row by row, a player holding only the larger row passed, the second TakeItem failed
            // unnoticed and the job still recorded both rows as paid, so Cancel handed back more than was taken (an item
            // duplication loop) and the craft itself cost less than the recipe.
            var needed = components.GroupBy(c => c.ComponentId)
                .Select(g => (Item: g.Key, Count: (ulong)g.Sum(c => (long)c.ComponentCount) * count)).ToList();
            if (needed.Any(n => n.Count > uint.MaxValue || session.Inventory.CountOf(n.Item) < n.Count))
                return "Missing components.";
            var paid = new List<(uint, uint)>();
            foreach ((uint item, ulong amount) in needed)
            {
                if (!session.TakeItem(item, (uint)amount))
                {
                    // Can't happen after the check above (one thread); never keep a partial payment.
                    foreach ((uint back, uint backCount) in paid)
                        session.GiveItem(back, backCount);
                    return "Missing components.";
                }
                paid.Add((item, (uint)amount));
            }

            Queue(session, recipe, count, paid);
            log.Info($"{session.CharacterName} started crafting recipe {recipeId} x{count}.");
            return null;
        }

        /// <summary>
        /// The Lumen Station's one-time unlock purchases: the 581 recipes with a VISIBILITY_REQUIREMENT_ID (0.695
        /// Recipes.txt, all CAN_CRAFT_WITHOUT_STATION 1): the 555 "Recipe:" items, the 22 "Recipe: Fireworks Emitter (…)"
        /// and Recipe: Highfall Catapult, the Dungeon Builder's Starter Set (MAX_QUEUE 1 each) and the Vault / Inventory /
        /// Design Expansions (2386-2388, MAX_QUEUE 3 / 3 / 7). Repeatable purchases (outfits, cut stone) have none
        /// (trace/0695/agent-leads3-b.md L43) [P data]. The id is a server Requirements row the client cannot evaluate (its
        /// Requirements.txt has rows 3-76 only; no reader of recipe +0x40 found yet), read as "the player doesn't have this
        /// yet", which hid the purchase [I strong]. So the server refuses one that would add nothing (the text id, or null
        /// to allow):
        /// <list type="bullet">
        /// <item>a storage expansion past its most (counting the unused copies held; <see cref="ItemUses.ExpansionUsesLeft"/>);</item>
        /// <item>a recipe item whose use would be refused (<see cref="RecipeBook.TeachesNew"/>: recipe or prop known), one
        /// already held unused, or more than one at a time (MAX_QUEUE 1);</item>
        /// <item>a Starter Set while an unopened one is held [I: opening it is not recorded, so a set opened earlier can be
        /// bought again].</item>
        /// </list>
        /// The texts are the client's own, as for the item uses (<see cref="ItemUses"/>): 568756 "You've already learned
        /// this prop!" for a prop recipe item whose prop is known, else 560768 "Error: Failed item requirement" (the client
        /// has no "recipe known" / "storage at its most" text) [I: that live answered a crafting start with these]. They go
        /// out as the StartResponse string, which the client prints to chat (0.695 FUN_142f39150 copies it, DAT_145431a10
        /// +0x18 the chat-print path) and then resets its crafting state (FUN_142f37fe0 on failure) [P].
        /// </summary>
        private static uint? UnlockRefusal(WorldSession session, RecipeEntry recipe, uint count)
        {
            uint item = recipe.ItemDefinitionId;
            if (ItemUses.ExpansionUsesLeft(session, item) is int left)
                return count * Math.Max(1, recipe.ItemCount) > left ? ItemUses.FailedItemRequirementText : null;
            uint held = session.Inventory.CountOf(item);
            if (RecipeBook.TeachesNew(session, item) is bool teaches)
            {
                if (!teaches)
                    return RecipeBook.IsPropRecipeItem(item) ? ItemUses.PropKnownText : ItemUses.FailedItemRequirementText;
                return held > 0 || count > 1 ? ItemUses.FailedItemRequirementText : null;
            }
            if (item == ItemUses.DungeonStarterSet)
                return held > 0 || count > 1 ? ItemUses.FailedItemRequirementText : null;
            // Any other unlock: at most MAX_QUEUE at a time.
            return recipe.MaxQueue > 0 && count > recipe.MaxQueue ? ItemUses.FailedItemRequirementText : null;
        }

        /// <summary>Queues a craft whose components were already taken; it completes after the recipe's casting time.</summary>
        public static void Queue(WorldSession session, RecipeEntry recipe, uint count, List<(uint, uint)> paid = null)
        {
            lock (sync)
                jobs.Add(new Job
                {
                    Session = session,
                    Recipe = recipe,
                    Count = count,
                    Paid = paid ?? new(),
                    Done = DateTime.UtcNow.AddSeconds(Math.Max(0.25, recipe.CastingTime) * count)
                });
        }

        /// <summary>
        /// RecipeBase 3 CancelRequest (no body; Lua Recipe.CancelRecipe → FUN_142e9f790, which also stops the client's
        /// progress at once): drops the player's craft and hands back its components (a job is paid out only when it
        /// completes). Answered with 4 CancelResponse {bool, string} (handler in FUN_142e9acd0 prints the text and resets
        /// the crafting state again).
        /// </summary>
        public static void Cancel(WorldSession session)
        {
            Job job;
            lock (sync)
            {
                job = jobs.FirstOrDefault(j => j.Session == session);
                if (job != null)
                    jobs.Remove(job);
            }
            if (job == null)
            {
                SendResponse(session, 4, false, "");
                return;
            }
            foreach ((uint item, uint count) in job.Paid)
                session.GiveItem(item, count);
            SendResponse(session, 4, true, "Crafting cancelled.");
            log.Info($"{session.CharacterName} cancelled recipe {job.Recipe.Id} x{job.Count}; refunded {job.Paid.Count} component kinds.");
        }

        /// <summary>The crafting station whose window the player has open (0 if none).</summary>
        public static uint OpenStation(WorldSession session)
        {
            lock (sync)
                return openStations.TryGetValue(session, out var open) ? open.Station : 0;
        }

        /// <summary>Is the player free to start a craft?</summary>
        public static bool Idle(WorldSession session)
        {
            lock (sync)
                return !jobs.Any(j => j.Session == session);
        }

        /// <summary>StartResponse (sub 2): started, or why not.</summary>
        public static void SendStartResponse(WorldSession session, string error)
            => SendResponse(session, 2, error == null, error ?? "");

        /// <summary>
        /// The player left the world (PlayerManager.Leave): a craft or salvage still running is finished now, its output
        /// put in the inventory the session still holds (saved like any other change). Before, the job stayed queued for
        /// the old session and <see cref="Update"/> dropped it when it came due, so the components (taken at the start) and
        /// the output were both lost to a logout or disconnect mid-craft. Every recipe takes at most 1 s per item
        /// (Recipes.txt CASTING_TIME 0 / 0.25 / 1, at most 100 per job), so finishing early saves at most 100 s.
        /// When the character has already logged in again (a fast or crash relog: the new session's inventory came from
        /// the database before this leave), the output goes to the new session instead (<see cref="Network.ZoneSessions.ReplacementOf"/>):
        /// added to the old in-memory inventory it was overwritten by the new session's next save of that stack, or took
        /// a slot the new session also filled (review 2026-10-18b #4).
        /// </summary>
        public static void Forget(WorldSession session)
        {
            List<Job> mine;
            lock (sync)
            {
                mine = jobs.Where(j => j.Session == session).ToList();
                foreach (Job job in mine)
                    jobs.Remove(job);
                openStations.Remove(session);
                openRepairWindows.Remove(session);
                openReach.Remove(session);
            }
            if (mine.Count == 0)
                return;
            WorldSession replacement = Network.ZoneSessions.ReplacementOf(session);
            if (replacement == null && session.Inventory == null)
                return;
            foreach (Job job in mine)
            {
                if (replacement != null)
                {
                    replacement.GiveItem(job.Recipe.ItemDefinitionId, job.Recipe.ItemCount * job.Count);
                    log.Info($"{session.CharacterName} left while crafting recipe {job.Recipe.Id} x{job.Count}: finished, "
                        + "given to the session that replaced it.");
                    continue;
                }
                session.Inventory.Add(job.Recipe.ItemDefinitionId, job.Recipe.ItemCount * job.Count);
                log.Info($"{session.CharacterName} left while crafting recipe {job.Recipe.Id} x{job.Count}: finished at logout.");
            }
        }

        /// <summary>
        /// Finishes crafts that are due (server tick). A job whose player left was finished by <see cref="Forget"/>.
        /// </summary>
        public static void Update()
        {
            List<Job> due;
            lock (sync)
            {
                if (jobs.Count == 0 && openStations.Count == 0)
                    return;
                foreach ((WorldSession player, System.Numerics.Vector3 at) in openRepairWindows.ToList())
                    if (player.Disconnected)
                        openRepairWindows.Remove(player);
                    else if (!CloseAwayEnabled && System.Numerics.Vector3.Distance(player.Position, at) > MaxStationDistance)
                    {
                        openRepairWindows.Remove(player);
                        ResetCursor(player);
                    }
                DateTime now = DateTime.UtcNow;
                due = jobs.Where(j => j.Done <= now).ToList();
                foreach (Job job in due)
                    jobs.Remove(job);
                foreach (WorldSession gone in openStations.Keys.Where(s => s.Disconnected).ToList())
                    openStations.Remove(gone);
                foreach (WorldSession gone in openReach.Keys.Where(s => s.Disconnected).ToList())
                    openReach.Remove(gone);
            }
            CloseWindowsAway();
            foreach (Job job in due)
            {
                if (job.Session.Disconnected)
                    continue;
                job.Session.GiveItem(job.Recipe.ItemDefinitionId, job.Recipe.ItemCount * job.Count);
                Achievements.Crafted(job.Session, job.Recipe.ItemDefinitionId, job.Count);
                SendResponse(job.Session, 5, true, "", job.Recipe.Id);
                log.Info($"{job.Session.CharacterName} crafted recipe {job.Recipe.Id} x{job.Count}.");
            }
        }

        /// <summary>
        /// RecipeBase 2 StartResponse / 4 CancelResponse (bool success, string message) or 5 CompleteResponse (bool, u32 kept at
        /// manager +0xac, string).
        /// </summary>
        private static void SendResponse(WorldSession session, uint sub, bool success, string message, uint value = 0)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(sub);
            writer.Write(success);
            if (sub == 5)
                writer.Write(value);
            writer.Write(message);
            session.EnqueueMessage(ZoneMessageOpcode.RecipeBase, output.ToArray());
        }
    }
}
