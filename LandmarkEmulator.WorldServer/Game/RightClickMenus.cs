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
    /// Right-click menus are built by the server. A right-click on a player, a friend, a group or guild member, a prop
    /// or a chat tab sends HudManagerBase 0xDC sub 1 RadialMenuRequest {u64 target guid, u32 request type}; the client
    /// shows only the buttons the server lists in sub 2 RadialMenuResponse (reader FUN_143670ee0):
    /// {u64 target guid (must match the request), u32 menu type (RadialMenuTypes.txt), u32 n, n × {u32 sort order, u32 button id,
    /// u8 selectable}, u32 request type (echoed)}.
    /// Button ids are rows of the client's Resources/RadialMenuButtons.txt; the client takes each button's label and
    /// the Lua function it runs (Ui.TeleportToFriend, Groups.GroupInviteByGuid, ...) from that table. Request types
    /// 2-4 and 6 are drawn by the social windows' own context menu (FUN_143494590), the others as a radial menu.
    /// </summary>
    public static class RightClickMenus
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte ResponseSub = 2;

        // (button id, sort order) per request type, from RadialMenuButtons.txt (sort order = its BUTTON column).
        private static readonly Dictionary<uint, (uint Id, uint Order)[]> menus = new()
        {
            // 1: a prop in the world (request type 1 with a prop's guid): built per prop by PropMenu.
            // 2: a name in the Friends window. Group invite 24, remove friend 16, travel to 23, guild invite 58, tell 65.
            [2] = new[] { (24u, 0u), (16u, 1u), (23u, 2u), (58u, 6u), (65u, 7u) },
            // 3: another player (request type 1 with a player's guid). Group invite 21, trade 19, add friend 15,
            // guild invite 57.
            [3] = new[] { (21u, 0u), (19u, 1u), (15u, 2u), (57u, 6u) },
            // 5: a member in the Party window. Leave 36, kick 35, promote 37, add friend 38, disband 39.
            [5] = new[] { (36u, 0u), (35u, 1u), (37u, 2u), (38u, 3u), (39u, 4u) },
            // 6: a guild member. Group invite 47, chat 52, travel to 68, mail 48, ignore 49, promote 42, demote 46, kick 44.
            [6] = new[] { (47u, 0u), (52u, 1u), (68u, 2u), (48u, 3u), (49u, 4u), (42u, 6u), (46u, 7u), (44u, 8u) },
            // 7: a chat tab. Close 67.
            [7] = new[] { (67u, 0u) },
        };

        /// <summary>
        /// HudManagerBase requests. Sub 1 RadialMenuRequest is answered with the buttons for that kind of target; an
        /// unknown request type gets an empty menu (the client then shows nothing).
        /// </summary>
        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            if (sub != 1 || body.Length < 12)
            {
                log.Debug($"HudManager request 0x{sub:X2}: {BitConverter.ToString(body)}");
                return;
            }
            ulong target = BitConverter.ToUInt64(body, 0);
            uint type = BitConverter.ToUInt32(body, 8);
            // Request type 1 is any entity in the world: the menu depends on what it is. Another player gets the player
            // menu; a placed prop the player may edit gets the prop menu, but only in build mode (editing tools belong
            // to build mode; user request 2026-10-13); anything else (trees, plants, creatures, the Spire, someone
            // else's props) gets none, as on live, where world objects had no right-click menu.
            uint menu = type;
            if (type == 1)
                menu = PlayerManager.FindByGuid(target) is WorldSession other && other != session ? 3u
                    : BuildModeHolster.InBuildMode(session) && Props.CanEdit(session, target) ? 1u : 0u;
            (uint Id, uint Order)[] buttons = menu == 1 ? PropMenu(session, target)
                : menus.TryGetValue(menu, out var fixedButtons) ? fixedButtons : Array.Empty<(uint, uint)>();

            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(ResponseSub);
            w.Write(target);
            w.Write(menu);
            w.Write((uint)buttons.Length);
            foreach ((uint id, uint order) in buttons)
            {
                w.Write(order);
                w.Write(id);
                w.Write(true);
            }
            w.Write(type);
            session.EnqueueMessage(ZoneMessageOpcode.HudManagerBase, output.ToArray());
            // The prop's name over it while its menu is open, for this player only (Props.MenuName.cs); any other
            // right-click takes a label still up away (the client reports no menu close).
            if (menu == 1 && buttons.Length > 0)
                Props.ShowMenuName(session, target);
            else
                Props.HideMenuName(session);
            log.Debug($"{session.CharacterName}: radial menu {menu} for 0x{target:X}: buttons {string.Join(",", buttons.Select(b => b.Id))}.");
        }

        /// <summary>
        /// The prop menu (RadialMenuButtons.txt menu type 1; sort order in brackets): Script 63 (0) for story-tool props
        /// (Prop.SetupScript), Settings 56 (1) for props with settings (Prop.SetupObject, PropSettings), Light on 73 (2) /
        /// off 70 (3) for props with an on/off state (Prop.SetPropEnable; whichever applies), Reposition 55 (4), Adjust 2
        /// (5), Duplicate 18 (7), "Send to Inventory" 17 (8, text 559201, Prop.PickUpObject: PickUp u8 0) for props that
        /// come back as an item, "Recycle" 64 (9, text 569162, Prop.DisassembleObject: PickUp u8 1) for props with a recipe
        /// except chests (<see cref="Props.OffersSendToInventory"/> / <see cref="Props.OffersRecycle"/>, TASKS 6.38 /
        /// 6.41), Delete 3 (10), and (0.695 only) Help 74 (11) for props with a help page (<see cref="HasHelpWindow"/>).
        /// </summary>
        private static (uint Id, uint Order)[] PropMenu(WorldSession session, ulong guid)
        {
            var buttons = new List<(uint, uint)> { (55u, 4u), (2u, 5u), (18u, 7u) };
            // Taking a prop away (Delete, Send to Inventory, Recycle) is the owner's or Ultimate Power's; "Let's Decorate"
            // only moves props (Props.CanRemove, bughunt-build B2), so a decorator's menu leaves them out.
            bool removable = Props.CanRemove(session, guid);
            if (removable)
                buttons.Add((3u, 10u));
            if (removable && Props.OffersSendToInventory(guid))
                buttons.Add((17u, 8u));
            if (removable && Props.OffersRecycle(guid))
                buttons.Add((RecycleButton, 9u));
            if (Props.Find(guid) is not Props.PlacedProp prop)
                return buttons.OrderBy(b => b.Item2).ToArray();
            StoryTools.Kind kind = StoryTools.KindOf(prop.PropId);
            // Links on every prop: each has at least "When a Luminary clicks this object..." (P6432).
            buttons.Add((63u, 0u));
            if (PropSettings.SettingsOf(prop.PropId).Count > 0)
                buttons.Add((56u, 1u));
            if (PropEffects.HasEffect(prop.PropId) || kind?.Toggle == true)
                buttons.Add(prop.Disabled ? (73u, 2u) : (70u, 3u));
            // 0.695 RadialMenuButtons row 74: menu 1, BUTTON 11, text 577204, Lua Prop.OpenHelpWindow (FUN_1435fbc80, which
            // finds the prop's PropHelpWindowMapping row and opens the page itself). The 0.589 table has no row 74.
            if (HasHelpWindow(prop.PropId))
                buttons.Add((HelpButton, 11u));
            return buttons.OrderBy(b => b.Item2).ToArray();
        }

        /// <summary>RadialMenuButtons row 74 (0.695): "Help" on a prop, Lua Prop.OpenHelpWindow.</summary>
        private const uint HelpButton = 74;

        /// <summary>RadialMenuButtons row 64 (both builds): "Recycle" (569162), Lua Prop.DisassembleObject.</summary>
        private const uint RecycleButton = 64;

        private static HashSet<uint> helpProps;

        /// <summary>
        /// Does the prop have a help page? The 0.695 client tables PropHelpWindowMapping.txt (ID^DESCRIPTION^WINDOW_ID^PROP_ID)
        /// and PropHelpWindow.txt (ID^DESCRIPTION^WINDOW_STRING^SIZE_STRING^ALIGN_STRING) from the game data folder: a prop
        /// whose mapping row names an existing window row gets the Help button. In 0.695 that is one prop: mapping row 1 →
        /// window 1 (ScriptObj_Damage_FTE, Large, CenterR) → prop 2899 (Damage Node). Read once; a missing table means no
        /// Help buttons (trace/0695/plan-storytools-next.md Spec 2, TASKS 2.27).
        /// </summary>
        public static bool HasHelpWindow(uint propId)
        {
            HashSet<uint> props = helpProps;
            if (props == null)
            {
                var windows = GameDataFiles.Rows("PropHelpWindow.txt").Select(r => r.U(0)).Where(id => id != 0).ToHashSet();
                props = GameDataFiles.Rows("PropHelpWindowMapping.txt")
                    .Where(r => windows.Contains(r.U(2)) && r.U(3) != 0)
                    .Select(r => r.U(3))
                    .ToHashSet();
                helpProps = props;
                log.Info($"Prop help windows: {props.Count} prop(s) with a Help button.");
            }
            return props.Contains(propId);
        }
    }
}
