using LandmarkEmulator.WorldServer.Network;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Gaps in the client's own UI scripts (0.589 and 0.695: the 0.695 UI\ScriptsBase.bin still calls
    /// ClaimVendorScreen.BuyItem and registers the natives as LuaClaimVendor), patched at run time with client Lua
    /// (<see cref="ClientLua.RunCode"/>) once the player is in the world: nothing on disk changes.
    /// <list type="bullet">
    /// <item>Build site vendors: the client's ClaimVendorHandler.lua (in UI\ScriptsBase.bin) calls the table
    /// ClaimVendorScreen (OnVendorOpened, OnVendorClosed, BuyItem), but this build registers the natives as
    /// LuaClaimVendor (CanBuyItem, BuyItem, SelectItem, ListItem, SetItemPriceByGuid, SetItemPriceBySlot,
    /// FUN_14353d440), so opening a vendor failed with "attempt to index a nil value (global 'ClaimVendorScreen')"
    /// and Buy did nothing. ClaimVendorScreen becomes LuaClaimVendor with the two missing callbacks, and BuyItem
    /// passes only (item, quantity): the native (FUN_14353cdf0) refuses any other argument count, while the handler
    /// passes (item, quantity, tint).</item>
    /// <item>Build mode page: Tab re-creates the build mode widget (HudStateManager:OnEnableBuildMode → OnShowBuildMode →
    /// LoadWidget), and BuildModeWidget.as always opens on Designs (handleAddedToStage: currentView = TEMPLATE_VIEW)
    /// while the client's active build category (Ui.SetActivePaletteCategory, set when a tab is clicked) stays on the
    /// last one — the panel showed Designs while the prop or voxel tool was still active. The page clicked last
    /// (2 Blocks, 3 Props, 4 Designs, 5 Tints) is remembered and restored when the widget loads (OnWidgetLoaded →
    /// OnSetTab, the event HudStateManager:OnSwitchTo*Tab use), with the category set to match.</item>
    /// </list>
    /// </summary>
    public static class ClientFixes
    {
        private const string VendorScreen =
            "if ClaimVendorScreen == nil and LuaClaimVendor ~= nil then "
            + "ClaimVendorScreen = setmetatable({ OnVendorOpened = function() end, OnVendorClosed = function() end, "
            + "BuyItem = function(item, quantity) return LuaClaimVendor.BuyItem(item, quantity) end }, { __index = LuaClaimVendor }) end";

        private const string BuildModePage =
            "if LmemuBuildPage == nil and Ui ~= nil and Ui.SetActivePaletteCategory ~= nil then "
            + "LmemuBuildPage = {} "
            + "local setCategory = Ui.SetActivePaletteCategory "
            + "Ui.SetActivePaletteCategory = function(...) local a = {...} LmemuBuildPage.view = tonumber(a[#a]) return setCategory(...) end "
            + "LmemuBuildPage.OnWidgetLoaded = function(self, widgetId) "
            + "local w = HudStateManager and HudStateManager.BuildMode "
            + "if w and widgetId == w.WIDGET_BUILD_MODE and self.view and self.view ~= 4 then "
            + "w:DispatchEventToFlash('OnSetTab', tostring(self.view)) setCategory(self.view) end end "
            + "LuaEventDispatcher:addListener('OnWidgetLoaded', LmemuBuildPage, 'OnWidgetLoaded') end";

        /// <summary>
        /// A crafting window left over from an earlier connection. The client's Lua state outlives a reconnect (the same
        /// game process signs in again after a server restart), and hudstate.lua HudStateManager:OnOpenCraftingWindow only
        /// opens the window when HudStateManager.CraftingWindow is nil: with a window object left there it takes the
        /// "already open" branch, whose code is all commented out, so every later Replicator / station window stayed shut
        /// until the game was restarted (2026-10-21: the server restarted while the crafting window was open at 18:02;
        /// CraftingWidget.gfx never loaded again, while the Repair Station, a separate window, still opened). The handle
        /// is cleared only by the window's own OnClose. So at the first sign-in of each connection a window left there is
        /// closed through its own Close (its OnClose clears the handle and tells the server, CraftWindowEnableRequest 6)
        /// and the handle cleared. A fresh game has none: nothing happens.
        /// </summary>
        private const string StaleCraftingWindow =
            "if HudStateManager and HudStateManager.CraftingWindow then "
            + "local w = HudStateManager.CraftingWindow "
            + "HudStateManager.CraftingWindow = nil "
            + "pcall(function() w:Close() end) end";

        /// <summary>Connections whose stale crafting window was already cleared (once each: the sign-in can repeat).</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldSession, object> craftingWindowCleared = new();

        /// <summary>Sent with the Showcase sign-in (<see cref="Store"/>), after the client's UI scripts are loaded.</summary>
        public static void Apply(WorldSession session)
        {
            ClientLua.RunCode(session, VendorScreen);
            ClientLua.RunCode(session, BuildModePage);
            if (craftingWindowCleared.TryAdd(session, new object()))
                ClientLua.RunCode(session, StaleCraftingWindow);
        }
    }
}
