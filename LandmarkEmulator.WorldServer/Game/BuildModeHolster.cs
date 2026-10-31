using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Tab puts the held item away and brings it back (requested behaviour; the 0.589 client itself keeps holding it).
    /// The client reports build mode with CommandPacketBuilderMode {u8 on} (Command 0x1E sub 0x5A, FUN_1433bec40). What
    /// a hotbar press does is move the item between the bag and the main hand (TransferItem; the hotbar window's
    /// DoAction(slot) → equipment slot 2 to hold it, again → back to the bag), so build mode does the same: on, the
    /// main-hand item goes to the bag and is remembered; off, it is equipped again if it is still in the bag.
    /// <para>
    /// <b>Placing a prop from the bag opens Build Mode</b> (<see cref="OpenForPlacement"/>; user request 2026-10-18,
    /// setting <c>PlaceOpensBuildMode</c> / Docker <c>PLACE_OPENS_BUILD_MODE</c>, <b>default on although live did not
    /// do it</b> — the user asked for it; off = live). Evidence (0.695 client; docs/gameplay.md "Props"):
    /// <list type="bullet">
    /// <item>[P] Live's client never enters build mode by itself for a placement: the only function that switches it,
    /// FUN_14346cc90 (sets ToolManager +0xF8, sends CommandPacketBuilderMode, calls HudStateManager:OnEnable/
    /// OnDisableBuildMode through FUN_143736da0, swaps the "BuildMode" / "HotKey" input contexts), is called with "on"
    /// only from the Lua natives Voxel.ToggleBuildingMode / SetEnableBuildingMode (FUN_1435fe740 / FUN_1435fe780), the
    /// key command (FUN_1435feaa0) and the zone-in restore from SendSelf +0x286 (FUN_142d12200, no packet); the bag's
    /// ClientInventory.ActivateItem (FUN_1435dd400 → FUN_143457220) and the placement start (PropBase sub 2) never call
    /// it. No server packet switches build mode on mid-session (the instanced-game state handler FUN_1434ec8b0 only
    /// switches it off).</item>
    /// <item>[P] Live's sources say nothing of it either: props that stay items are still placed by right-clicking them
    /// in the inventory (update notes 26 Mar 2015); the wiki's Build Mode is opened with Tab
    /// or the tools icon only.</item>
    /// <item>So the server runs the client's own native through client Lua (<see cref="ClientLua.RunCode"/>), guarded
    /// exactly as Tab is (HudStateManager:OnToggleBuildMode checks Voxel.AllowBuildingMode()): build mode opens only
    /// where Tab would open it. The client then reports it with CommandPacketBuilderMode {1} as for Tab, so the held
    /// item is put away as usual. The build widget is shown on its Props page: ClientFixes' remembered page
    /// (LmemuBuildPage.view = 3) is applied when the widget loads (OnSetTab "3" + Ui.SetActivePaletteCategory(3)).
    /// [P] Neither touches the placement: SetActivePaletteCategory (FUN_1435d2d70 → FUN_1434cfaf0) calls the category
    /// collection's vtable slot 1, which is empty (FUN_143748730) for PaletteCollectionProps and every other
    /// collection but the tints (FUN_14374ca20: AvailableTintsRequest).</item>
    /// <item>When that placement is <b>cancelled</b> (the unplaced prop removed — Escape before the first Save, refusal,
    /// re-zone) build mode is closed again the same way, unless the player switched it off and on themselves meanwhile
    /// or has started another placement (a palette click). A successful first Save does <b>not</b> close it: the client
    /// keeps sending Saves while the placement gizmo is open, and closing build mode mid-gizmo tears the ghost down
    /// (fix/bag-placement-gizmo-timeout). The player Tabs out when done. Off a build site the placement is refused
    /// before it starts (Props.BeginPlacement), so nothing opens.</item>
    /// <item>Only a build mode the placement really opened is closed: the open is guarded client-side, so the client may
    /// do nothing. The player is marked "open pending" when the code is sent and counts as opened by the placement
    /// only when the client's CommandPacketBuilderMode {1} arrives within <see cref="OpenConfirmWindow"/>; any other
    /// build-mode-on report is the player's own (Tab) and is never closed (review 2026-10-18b #7). The close runs on the
    /// world tick (<see cref="Update"/>), not on a pool thread, and skips a session that has gone (#6).</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class BuildModeHolster
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>EquipmentSlotDefinitions: the main hand.</summary>
        private const uint MainHand = 2;

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, ulong> holstered = new();
        private static readonly HashSet<WorldSession> building = new();

        /// <summary>
        /// Players whose current build mode a bag placement opened, confirmed by the client (cleared when the client
        /// reports it off).
        /// </summary>
        private static readonly HashSet<WorldSession> openedByPlacement = new();

        /// <summary>Players sent <see cref="OpenCode"/> whose client has not reported build mode on yet, and when.</summary>
        private static readonly Dictionary<WorldSession, DateTime> openPending = new();

        /// <summary>
        /// How long after <see cref="OpenCode"/> went out a build-mode-on report counts as its answer. [I] Ours: the code
        /// runs at the client's next Lua dispatch and the report goes straight back, well within this; a later report is
        /// the player's own Tab.
        /// </summary>
        internal static readonly TimeSpan OpenConfirmWindow = TimeSpan.FromSeconds(2);

        /// <summary>The bag placement (prop guid) each of those players is making.</summary>
        private static readonly Dictionary<WorldSession, ulong> placing = new();

        /// <summary>Build modes to close (<see cref="Update"/>), and when.</summary>
        private static readonly Dictionary<WorldSession, DateTime> closeDue = new();

        /// <summary>
        /// How long after a cancelled bag placement build mode is closed. [I] Ours: long enough for a palette click that
        /// replaces the placement (cancel, then PlacePropById) to start its own one, which keeps build mode open.
        /// </summary>
        internal static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Opens build mode on the Props page, as Tab would (same guard), only when it is off; sends
        /// CommandPacketBuilderMode {1} back. No commas, so ClientLua sends it as is.
        /// </summary>
        private const string OpenCode =
            "if Voxel and Voxel.AllowBuildingMode() and not Voxel.GetBuildingModeEnabled() then "
            + "if LmemuBuildPage then LmemuBuildPage.view = 3 end "
            + "Voxel.SetEnableBuildingMode(true) end";

        /// <summary>Closes build mode if it is still on; sends CommandPacketBuilderMode {0} back.</summary>
        private const string CloseCode =
            "if Voxel and Voxel.GetBuildingModeEnabled() then Voxel.SetEnableBuildingMode(false) end";

        private static bool PlaceOpensBuildMode
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config.PlaceOpensBuildMode;

        /// <summary>
        /// A prop placement from a bag item has started (<see cref="Props.BeginPlacement"/> returned it): open build mode
        /// for it unless the player is already building (Tab), and remember the placement so its end closes it again.
        /// </summary>
        public static void OpenForPlacement(WorldSession session, Props.PlacedProp prop)
        {
            if (prop == null || !PlaceOpensBuildMode)
                return;
            lock (sync)
            {
                // The player's own build mode (Tab) is left alone, before and after.
                if (building.Contains(session) && !openedByPlacement.Contains(session))
                    return;
                placing[session] = prop.Spawn.Guid;
                closeDue.Remove(session);   // a new placement keeps it open
                // Already opened by the last placement (confirmed), or the open just sent is still unanswered.
                if (openedByPlacement.Contains(session)
                    || openPending.TryGetValue(session, out DateTime sent) && DateTime.UtcNow - sent <= OpenConfirmWindow)
                    return;
                openPending[session] = DateTime.UtcNow;
            }
            ClientLua.RunCode(session, OpenCode);
            log.Info($"{session.CharacterName}: placing prop {prop.PropId} from the bag opens build mode.");
        }

        /// <summary>
        /// The bag placement's first Save settled the prop; the gizmo may still be open for fine-tuning. Clears the
        /// placement mark only — does not close build mode (see class remarks; fix/bag-placement-gizmo-timeout).
        /// </summary>
        public static void PlacementSettled(ulong propGuid) => ClearPlacing(propGuid, scheduleClose: false);

        /// <summary>
        /// A bag placement was cancelled (its unplaced prop removed). If it opened build mode, that mode is closed
        /// <see cref="CloseDelay"/> later on the world tick (<see cref="Update"/>).
        /// </summary>
        public static void PlacementEnded(ulong propGuid) => ClearPlacing(propGuid, scheduleClose: true);

        private static void ClearPlacing(ulong propGuid, bool scheduleClose)
        {
            WorldSession session = null;
            lock (sync)
            {
                foreach (var entry in placing)
                    if (entry.Value == propGuid)
                    {
                        session = entry.Key;
                        break;
                    }
                if (session == null)
                    return;
                placing.Remove(session);
                // Opened by it, or its open still unanswered (the answer may come before the close is due).
                if (scheduleClose && (openedByPlacement.Contains(session) || openPending.ContainsKey(session)))
                    closeDue[session] = DateTime.UtcNow + CloseDelay;
                // Settled: the build mode the placement opened stays open on purpose and is the player's from now on.
                // Left marked, a later bag placement's cancel (Escape) closed it under a player who had been building in
                // it for minutes (trace/0695/phaseE4-logic.md F3).
                if (!scheduleClose)
                {
                    openedByPlacement.Remove(session);
                    openPending.Remove(session);
                }
            }
        }

        /// <summary>
        /// World tick: closes the build modes whose placement ended <see cref="CloseDelay"/> ago (the same pattern as
        /// Abilities.Later and Consumables.UpdateRepairBots; until 2026-10-18 a pool thread did this after a
        /// Task.Delay, beside the tick, review 2026-10-18b #6). Also drops open marks the client never answered.
        /// </summary>
        public static void Update()
        {
            List<WorldSession> due = null;
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                if (closeDue.Count == 0 && openPending.Count == 0)
                    return;
                foreach (var (session, at) in closeDue)
                    if (now >= at)
                        (due ??= new List<WorldSession>()).Add(session);
                if (due != null)
                    foreach (WorldSession session in due)
                        closeDue.Remove(session);
                List<WorldSession> unanswered = null;
                foreach (var (session, sent) in openPending)
                    if (now - sent > OpenConfirmWindow && !closeDue.ContainsKey(session) && !placing.ContainsKey(session))
                        (unanswered ??= new List<WorldSession>()).Add(session);
                if (unanswered != null)
                    foreach (WorldSession session in unanswered)
                        openPending.Remove(session);
            }
            if (due == null)
                return;
            foreach (WorldSession session in due)
                if (!session.Disconnected)
                    CloseAfterPlacement(session);
        }

        private static void CloseAfterPlacement(WorldSession session)
        {
            lock (sync)
            {
                // The client never opened build mode for the placement (Voxel.AllowBuildingMode() said no): nothing to close.
                openPending.Remove(session);
                if (!openedByPlacement.Contains(session) || placing.ContainsKey(session))
                    return;
            }
            // A placement the player started meanwhile from the open palette keeps build mode open (theirs from now on).
            if (Props.HasPendingPlacement(session))
                return;
            lock (sync)
                if (!openedByPlacement.Remove(session))
                    return;
            ClientLua.RunCode(session, CloseCode);
            log.Info($"{session.CharacterName}: bag placement over, build mode closed again.");
        }

        /// <summary>Is the player in build mode (Tab)? The prop right-click menu is offered only then.</summary>
        public static bool InBuildMode(WorldSession session)
        {
            lock (sync)
                return building.Contains(session);
        }

        public static void BuildMode(WorldSession session, bool on)
        {
            bool changed;
            lock (sync)
            {
                changed = on ? building.Add(session) : building.Remove(session);
                // Switched on: the answer to a placement's open sent within OpenConfirmWindow makes it the placement's;
                // any other report on is the player's own (Tab), and clears the pending mark either way (#7).
                if (on && openPending.Remove(session, out DateTime sent) && DateTime.UtcNow - sent <= OpenConfirmWindow)
                    openedByPlacement.Add(session);
                // Switched off (Tab, the tools icon, a site game starting): any later build mode is the player's own.
                if (!on)
                {
                    openedByPlacement.Remove(session);
                    openPending.Remove(session);
                    closeDue.Remove(session);
                }
            }
            if (changed)
                log.Info($"{session.CharacterName} {(on ? "entered" : "left")} build mode.");
            // A playing site's Starting Location is shown only in build mode (wiki Starting Location; L20).
            if (changed)
                Props.BuildModeVisibilityChanged(session, on);
            // Leaving build mode closes the prop menu: its name label goes (Props.MenuName.cs).
            if (!on)
                Props.HideMenuName(session);
            Props.RefreshEditable(session);
            if (on)
            {
                uint? slot = Inventory.Inventory.EquippedSlotFor(MainHand);
                CharacterItemModel held = slot == null ? null : session.Inventory.At(Inventory.Inventory.EquippedContainer, slot.Value);
                if (held == null)
                    return;
                lock (sync)
                    holstered[session] = held.Id;
                Network.Handlers.InventoryHandler.Unequip(session, MainHand);
                log.Debug($"{session.CharacterName}: build mode, put away {held.ItemDefinitionId}.");
                return;
            }
            ulong guid;
            lock (sync)
                if (!holstered.Remove(session, out guid))
                    return;
            CharacterItemModel item = session.Inventory.Get(guid);
            if (item == null || item.Container != Inventory.Inventory.BagContainer)
                return;
            Network.Handlers.InventoryHandler.Equip(session, guid, MainHand);
            log.Debug($"{session.CharacterName}: left build mode, holding {item.ItemDefinitionId} again.");
        }

        public static void Forget(WorldSession session)
        {
            Props.ForgetMenuName(session);
            lock (sync)
            {
                holstered.Remove(session);
                building.Remove(session);
                openedByPlacement.Remove(session);
                openPending.Remove(session);
                closeDue.Remove(session);
                placing.Remove(session);
            }
        }
    }
}
