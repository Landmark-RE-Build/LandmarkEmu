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
    /// </list>
    /// </summary>
    public static class ClientFixes
    {
        private const string VendorScreen =
            "if ClaimVendorScreen == nil and LuaClaimVendor ~= nil then "
            + "ClaimVendorScreen = setmetatable({ OnVendorOpened = function() end, OnVendorClosed = function() end, "
            + "BuyItem = function(item, quantity) return LuaClaimVendor.BuyItem(item, quantity) end }, { __index = LuaClaimVendor }) end";

        /// <summary>Sent with the Showcase sign-in (<see cref="Store"/>), after the client's UI scripts are loaded.</summary>
        public static void Apply(WorldSession session)
        {
            ClientLua.RunCode(session, VendorScreen);
        }
    }
}
