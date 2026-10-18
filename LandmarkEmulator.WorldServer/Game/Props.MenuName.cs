using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.GameTable.Text;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The prop's name over it while its build-mode right-click menu is open (user request 2026-10-18; setting
    /// <see cref="WorldServerConfiguration.BuildMenuPropName"/>, Docker BUILD_MENU_PROP_NAME, default on). Only the player
    /// who opened the menu sees it; everyone else's view is untouched.
    /// <para><b>How the client draws it [P].</b> A prop entity gets the "Prop" HUD indicator (HudIndicators.txt row 12,
    /// code 18) under client requirement 13 "(TargetIsProp &amp;&amp; ShowProxyName) || …" (HUDIndicatorSettings.txt
    /// 2^18^13 / 13^18^13); ShowProxyName is requirement 73 = ClientRequirementProxyNameHidden with operator 2, which reads
    /// entity +0x1454 bit 0x40 (FUN_1437119c0) and is clear on our props. The indicator shows the entity's name
    /// (+0x1308): props spawn with no name (spawn NameId 0, <see cref="Network.Message.Model.PlayerUpdateAddLightweightProp.ShowName"/>)
    /// and show nothing; with a name they show it (runtime: the NameId labels removed in a56d4aa, renamed props since
    /// 2026-10-13, the movers' "area" labels removed in 97b5728).</para>
    /// <para><b>Changing it without a respawn [P].</b> PlayerUpdate 0x7C UpdatePropName {u64 prop, string name} (reader
    /// FUN_142c4de60: u16 opcode, u32 sub, u64 guid +0x18, string +0x20; 0.695 dispatcher FUN_142cdfa60 case 0x7b) finds
    /// the entity, sets its name through FUN_142ec5150 (entity +0x1308, and +0x1453 |= 4) and stores the string as the
    /// prop data's custom name (+0x4e80, FUN_142d50310; the Prop Settings window shows it, FUN_142fb71f0). An empty string
    /// is replaced by the text of the prop data's name id +0x4e98 (FUN_142cd0980), which is the spawn's NameId (packet
    /// +0x58, FUN_142cde1c0) — 0 on our props — so an empty name can't be used to clear a label. A single space can: the
    /// movers' parts carry " " and show no label (runtime 2026-10-18, r75). So the label goes back with " " (or with the
    /// name the prop really carries, see <see cref="RestingName"/>), and no respawn is needed: movers and platforms keep
    /// running.</para>
    /// <para><b>Open and close signals [P].</b> Opening: the right-click sends HudManagerBase 0xDC sub 1 RadialMenuRequest
    /// {u64 target, u32 type 1} (FUN_14372f510) and the menu appears when RadialMenuResponse arrives (FUN_14372e1c0) — the
    /// server answers it in <see cref="RightClickMenus.Handle"/>, which calls <see cref="ShowMenuName"/> for the prop menu.
    /// The menu's own title can't carry the name: the context menu indicator takes its labels from the scripting
    /// controller's state (FUN_143446870 → FUN_142fed440 → state vtable +0x18), a no-op in EditingStateIdle
    /// (FUN_142fed470), the state the prop menu runs in. Closing sends nothing: the X button runs Lua
    /// RadialMenu.ExitScriptingState (FUN_1435fd800 → FUN_14343a640, a controller state change) and the menu's reset
    /// FUN_14372d730 sends no packet; Escape / clicking away likewise. So the label goes when the next thing the player does
    /// reaches the server (<see cref="HideMenuName"/>):
    /// <list type="bullet">
    /// <item>a button that sends something: Reposition / Adjust (PropBase 1 Access, 3 Save, 0x0C), Duplicate 0x0F, Send to
    /// Inventory / Recycle 5, Delete 7 (after its confirmation), Settings 0x1B, Light on / off 0x1A (luabind_695 "sends"
    /// column; PropHandler), Script (ObjectScripting 0x111 sub 0x0F, FUN_1435fafc0 → FUN_142f489e0);</item>
    /// <item>any other prop request (tint, text, name, placing from the palette); PropProcessorStateNotification 0x0C
    /// alone does not count, as the prop tool sends it on its own state changes (FUN_142fc0df0);</item>
    /// <item>another right-click (any RadialMenuRequest), leaving build mode (Command 0x5A BuilderMode 0), a zone change,
    /// logging out;</item>
    /// <item>else a timeout, <see cref="MenuNameTimeout"/> after the menu opened [I]: Help (button 74, Lua only), a
    /// cancelled Delete, the X, Escape and clicking away send nothing at all.</item>
    /// </list></para>
    /// <para>Left alone: props whose client name is "area" (an area guid and no name: the Area Tool's Linking Areas; the
    /// client names them at spawn, FUN_142d54040 → FUN_142ec18c0, and that name can't be given back through 0x7C), and
    /// props with no name text. Mover parts get their name while the menu is open on them and their blank " " back after.</para>
    /// </summary>
    public static partial class Props
    {
        /// <summary>
        /// How long a menu's name label stays up when nothing else closes it [I]: the client reports no close, so this only
        /// bounds a label left over by Escape / the X / Help / a click away. Long enough to read the menu and pick a button.
        /// </summary>
        private static readonly TimeSpan MenuNameTimeout = TimeSpan.FromSeconds(10);

        /// <summary>A shown label: the prop, the landscape it was shown on, and when it goes at the latest.</summary>
        private sealed record MenuNameShown(ulong Guid, uint Zone, DateTime Until);

        private static readonly object menuNameSync = new();
        private static readonly Dictionary<WorldSession, MenuNameShown> menuNames = new();

        private static bool MenuNameEnabled =>
            ConfigurationManager<WorldServerConfiguration>.Instance.Config?.BuildMenuPropName != false;

        /// <summary>
        /// The prop menu was just sent to this player for this prop (<see cref="RightClickMenus.Handle"/>): its name goes
        /// over it for this player only. A label still up on another prop goes first.
        /// </summary>
        public static void ShowMenuName(WorldSession session, ulong guid)
        {
            MenuNameShown previous;
            lock (menuNameSync)
                menuNames.Remove(session, out previous);
            if (previous != null && previous.Guid != guid)
                RestoreMenuName(session, previous);
            if (!MenuNameEnabled || Find(guid) is not PlacedProp prop || !prop.Placed || prop.Zone != session.ZoneId)
                return;
            // An "area" prop (client-named at spawn) is left as it is: 0x7C can't give that name back.
            if (prop.Spawn.AreaGuid != 0 && string.IsNullOrEmpty(prop.Spawn.Name))
                return;
            string name = MenuName(prop);
            // Nothing to show, or already showing it (a renamed prop carries its name for everyone).
            if (string.IsNullOrWhiteSpace(name) || name == RestingName(prop))
                return;
            session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdatePropName, PropNameUpdate(guid, name));
            lock (menuNameSync)
                menuNames[session] = new MenuNameShown(guid, prop.Zone, DateTime.UtcNow + MenuNameTimeout);
        }

        /// <summary>
        /// The player did something that closes the prop menu (see the class notes): the label shown for it, if any, goes.
        /// Cheap when none is up (it is called for every prop request, including the gizmo's Saves).
        /// </summary>
        public static void HideMenuName(WorldSession session)
        {
            MenuNameShown shown;
            lock (menuNameSync)
            {
                if (menuNames.Count == 0 || !menuNames.Remove(session, out shown))
                    return;
            }
            RestoreMenuName(session, shown);
        }

        /// <summary>The player left the world: nothing to send, the state goes.</summary>
        public static void ForgetMenuName(WorldSession session)
        {
            lock (menuNameSync)
                menuNames.Remove(session);
        }

        /// <summary>Labels left up past <see cref="MenuNameTimeout"/> (from <see cref="Update"/>).</summary>
        private static void UpdateMenuNames()
        {
            List<(WorldSession Session, MenuNameShown Shown)> expired = null;
            DateTime now = DateTime.UtcNow;
            lock (menuNameSync)
            {
                if (menuNames.Count == 0)
                    return;
                foreach (var (session, shown) in menuNames)
                    if (now >= shown.Until)
                        (expired ??= new()).Add((session, shown));
                if (expired == null)
                    return;
                foreach (var (session, _) in expired)
                    menuNames.Remove(session);
            }
            foreach (var (session, shown) in expired)
                RestoreMenuName(session, shown);
        }

        /// <summary>
        /// Puts back the name the player's client had for the prop. Skipped when the prop is gone or the player is on
        /// another landscape (the client dropped the entity; a fresh spawn carries its own name).
        /// </summary>
        private static void RestoreMenuName(WorldSession session, MenuNameShown shown)
        {
            if (Find(shown.Guid) is not PlacedProp prop || prop.Zone != shown.Zone || session.ZoneId != shown.Zone)
                return;
            session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdatePropName, PropNameUpdate(shown.Guid, RestingName(prop)));
        }

        /// <summary>The name shown while the menu is open: the owner's own name for it, else its definition's name text.</summary>
        private static string MenuName(PlacedProp prop)
        {
            if (!string.IsNullOrWhiteSpace(prop.CustomName))
                return prop.CustomName;
            uint nameId = NameIdOf(prop.PropId);
            return nameId != 0 ? TextManager.Instance.GetTextForId(nameId) ?? "" : "";
        }

        /// <summary>
        /// The name the prop carries for every viewer when no menu is open: its custom name (sent at spawn and on rename,
        /// <see cref="SendSpawns"/> / <see cref="SetName"/>), else its spawn's literal name (a mover part's " "), else " "
        /// (no label; the spawn's empty name can't be sent back, see the class notes).
        /// </summary>
        private static string RestingName(PlacedProp prop)
        {
            if (!string.IsNullOrEmpty(prop.CustomName))
                return prop.CustomName;
            if (!string.IsNullOrEmpty(prop.Spawn.Name))
                return prop.Spawn.Name;
            return " ";
        }
    }
}
