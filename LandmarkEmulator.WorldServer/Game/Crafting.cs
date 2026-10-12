using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.Shared.GameTable.Model;
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
    /// Crafting at stations: checks the request against the recipe tables (never trusting the client), takes the
    /// components up front, and hands over the output once the recipe's casting time has passed.
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

        /// <summary>
        /// Remembers which station a player opened, and its prop (0 for a dev-opened window, which skips the
        /// distance check).
        /// </summary>
        public static void StationOpened(WorldSession session, uint station, ulong propGuid)
        {
            lock (sync)
            {
                openStations[session] = (station, propGuid);
                if (GameTableManager.Instance.CraftingStations.GetEntry(station)?.CraftingStationTypeId == RepairStationType)
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
            if (open.PropGuid != 0 && (Props.PositionOf(open.PropGuid) is not System.Numerics.Vector3 stationPosition
                    || System.Numerics.Vector3.Distance(stationPosition, session.Position) > MaxStationDistance))
                return "You are too far from the station.";
            if (!recipe.CanCraftWithoutStation && !GameTableManager.Instance.CraftingStationRecipeMappings.Entries
                    .Any(m => m.CraftingStationId == station && m.RecipeId == recipeId))
                return "That recipe needs a different station.";

            List<RecipeComponentEntry> components = GameTableManager.Instance.ComponentsOf(recipeId).ToList();
            // Type 1 = an item. The other types (item category / category group / keywords) only occur in salvaging
            // recipes, which Salvaging handles with the player's chosen items.
            if (components.Any(c => c.ComponentType != 1))
                return "That recipe isn't supported yet.";
            if (components.Any(c => session.Inventory.CountOf(c.ComponentId) < c.ComponentCount * count))
                return "Missing components.";
            var paid = new List<(uint, uint)>();
            foreach (RecipeComponentEntry component in components)
            {
                session.TakeItem(component.ComponentId, component.ComponentCount * count);
                paid.Add((component.ComponentId, component.ComponentCount * count));
            }

            Queue(session, recipe, count, paid);
            log.Info($"{session.CharacterName} started crafting recipe {recipeId} x{count}.");
            return null;
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
        /// Finishes crafts that are due (server tick). A player who logged out loses that craft; its components were
        /// already spent.
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
                    else if (System.Numerics.Vector3.Distance(player.Position, at) > MaxStationDistance)
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
            }
            foreach (Job job in due)
            {
                if (job.Session.Disconnected)
                    continue;
                job.Session.GiveItem(job.Recipe.ItemDefinitionId, job.Recipe.ItemCount * job.Count);
                Achievements.Crafted(job.Session, job.Recipe.ItemDefinitionId);
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
