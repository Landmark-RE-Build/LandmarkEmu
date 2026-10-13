using LandmarkEmulator.WorldServer.Network;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Gaps in the 0.589 client's own UI scripts, patched at run time with client Lua (<see cref="ClientLua.RunCode"/>)
    /// once the player is in the world: nothing on disk changes.
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

        /// <summary>Sent with the Showcase sign-in (<see cref="Store"/>), after the client's UI scripts are loaded.</summary>
        public static void Apply(WorldSession session)
        {
            ClientLua.RunCode(session, VendorScreen);
            ClientLua.RunCode(session, BuildModePage);
        }
    }
}
