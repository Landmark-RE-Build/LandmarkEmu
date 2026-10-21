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
    /// the Lua function it runs (Ui.TeleportToFriend, Groups.GroupInviteByGuid, ...) from that table.
    /// <para>
    /// Request types, per sender (0.695; 0.589 the same) [P]: 1 an entity in the world (the radial input, FUN_14372ebc0);
    /// 2 the Friends list (Lua ClientFriends.HandleRightClick FUN_1435e2d20); 3 the party window (Groups.HandleRightClick
    /// FUN_1435f1880, GroupWidget.as); 4 the guild roster (Guild.HandleRightClick FUN_1435b98e0, RosterView.as /
    /// ContactListWidget.as); 6 a chat tab (Chat.HandleRightClick FUN_1435ae1d0, ChatDocumentClass.as, deletable tabs
    /// only). The response handler FUN_14372e1c0 draws types 2-4 and 6 as the social windows' context menu
    /// (FUN_143544280 → ContextMenu.swf, which calls the button's Lua function with the target guid alone; "Send Tell"
    /// also gets the name) and everything else as the radial menu (FUN_14372d940: guid, then the target actor's name,
    /// then any ",value" from LUA_CALL). Each request type answers with its RadialMenuTypes menu: 2 → 4 (friend),
    /// 3 → 5 (party member), 4 → 6 (guild member), 6 → 7 (chat tab); 1 → 3 (another player) or 1 (an editable prop).
    /// Before 2026-10-21 the request type was used as the menu type, so the party window got the player menu, the guild
    /// roster none and a chat tab the guild member menu (docs/request-variants.md).
    /// </para>
    /// <para>
    /// Which of a menu's rows are listed depends on the target (<see cref="Buttons"/>); live's exact rules are not in the
    /// client [I]: a button is left out when it can't apply (Revive on a living player, Kick / Make Party Leader for
    /// someone outside the party or when not its leader, Add Friend for a friend, Ignore vs Stop Ignoring).
    /// </para>
    /// </summary>
    public static class RightClickMenus
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte ResponseSub = 2;

        /// <summary>RadialMenuRequest request types (the sender's u32, echoed in the response).</summary>
        public const uint RequestWorld = 1, RequestFriend = 2, RequestParty = 3, RequestGuild = 4, RequestChatTab = 6;

        /// <summary>RadialMenuTypes.txt menu types (RadialMenuButtons.txt MENU_TYPE_ID).</summary>
        public const uint MenuNone = 0, MenuProp = 1, MenuPlayer = 3, MenuFriend = 4, MenuParty = 5, MenuGuild = 6, MenuChatTab = 7;

        /// <summary>The menu a social window's request type answers with (request 1 is decided per target).</summary>
        public static uint MenuFor(uint requestType) => requestType switch
        {
            RequestFriend => MenuFriend,
            RequestParty => MenuParty,
            RequestGuild => MenuGuild,
            RequestChatTab => MenuChatTab,
            _ => MenuNone,
        };

        /// <summary>
        /// What the menu needs to know about its target: is it the player themselves, in the player's party (and does the
        /// player lead it), on the player's friends list, dead, on the player's ignore list.
        /// </summary>
        public readonly record struct Target(bool Self, bool InMyParty, bool IAmLeader, bool Friend, bool Dead, bool Ignored);

        /// <summary>
        /// The buttons (RadialMenuButtons row, sort order = its BUTTON column) of a menu type for this target:
        /// <list type="bullet">
        /// <item>3 another player: Invite to Party 21 (0) outside the party, or Kick from Party 25 (3) and Make Party Leader
        /// 27 (4) for a member while leading it; Start Trade 19 (1); Add Friend 15 (2) unless a friend; Revive 41 (5) when
        /// dead; Invite to Guild 57 (6); Inspect 75 (7, 0.695).</item>
        /// <item>4 a friend: Invite to Party 24 (0) / Kick 28 (3) and Make Leader 30 (4) as above; Remove Friend 16 (1);
        /// Teleport to Friend 23 (2); Invite to Guild 58 (6); Send Tell 65 (7). Row 34 Add Friend is not offered (a
        /// friend already).</item>
        /// <item>5 a party member: for the player themselves Leave Party 36 (0) and, as leader, Disband Party 39 (4); for
        /// another member Kick 35 (1) and Make Leader 37 (2) as leader, Add Friend 38 (3) unless a friend.</item>
        /// <item>6 a guild member (never the player: RosterView.as skips the own row): Invite to Party 47 (0) outside the
        /// party, Send Tell 52 (1), Teleport to Guildmate 68 (2), Send Mail 48 (3), Ignore 49 (4) or Stop Ignoring 50
        /// (5), Promote 42 (6), Demote 46 (7), Kick 44 (8) — the client checks the rank permission for the last three
        /// itself (Guild.InsufficientRank).</item>
        /// <item>7 a chat tab: Close 67 (0).</item>
        /// </list>
        /// </summary>
        public static (uint Id, uint Order)[] Buttons(uint menu, Target target)
        {
            var buttons = new List<(uint, uint)>();
            void PartyButtons(uint invite, uint kick, uint leader)
            {
                if (!target.InMyParty)
                    buttons.Add((invite, 0u));
                else if (target.IAmLeader)
                {
                    buttons.Add((kick, 3u));
                    buttons.Add((leader, 4u));
                }
            }
            switch (menu)
            {
                case MenuPlayer when !target.Self:
                    PartyButtons(21, 25, 27);
                    buttons.Add((19u, 1u));
                    if (!target.Friend)
                        buttons.Add((15u, 2u));
                    if (target.Dead)
                        buttons.Add((41u, 5u));
                    buttons.Add((57u, 6u));
                    buttons.Add((75u, 7u));
                    break;
                case MenuFriend when !target.Self:
                    PartyButtons(24, 28, 30);
                    buttons.Add((16u, 1u));
                    buttons.Add((23u, 2u));
                    buttons.Add((58u, 6u));
                    buttons.Add((65u, 7u));
                    break;
                case MenuParty when target.Self:
                    buttons.Add((36u, 0u));
                    if (target.IAmLeader)
                        buttons.Add((39u, 4u));
                    break;
                case MenuParty:
                    if (target.InMyParty && target.IAmLeader)
                    {
                        buttons.Add((35u, 1u));
                        buttons.Add((37u, 2u));
                    }
                    if (!target.Friend)
                        buttons.Add((38u, 3u));
                    break;
                case MenuGuild when !target.Self:
                    if (!target.InMyParty)
                        buttons.Add((47u, 0u));
                    buttons.Add((52u, 1u));
                    buttons.Add((68u, 2u));
                    buttons.Add((48u, 3u));
                    buttons.Add(target.Ignored ? (50u, 5u) : (49u, 4u));
                    buttons.Add((42u, 6u));
                    buttons.Add((46u, 7u));
                    buttons.Add((44u, 8u));
                    break;
                case MenuChatTab:
                    buttons.Add((67u, 0u));
                    break;
            }
            return buttons.OrderBy(b => b.Item2).ToArray();
        }

        /// <summary>The menu's view of a character target (<see cref="Target"/>), from this world's state.</summary>
        private static Target Describe(WorldSession session, ulong guid)
        {
            bool self = guid == session.PlayerGuid;
            var party = Groups.StateOf(session);
            bool inParty = party is { } p && p.Members.Contains(guid);
            bool leader = party is { } q && q.Leader == session.PlayerGuid;
            bool friend = !self && Social.SocialServices.FriendCache.FriendOf(guid)?.Contains(session.PlayerGuid) == true;
            bool dead = PlayerManager.FindByGuid(guid) is WorldSession other && Health.IsDead(other);
            return new Target(self, inParty, leader, friend, dead, Ignores.IsIgnoring(session.PlayerGuid, guid));
        }

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
            uint menu = MenuFor(type);
            if (type == RequestWorld)
                menu = PlayerManager.FindByGuid(target) is WorldSession other && other != session ? MenuPlayer
                    : BuildModeHolster.InBuildMode(session) && Props.CanEdit(session, target) ? MenuProp : MenuNone;
            (uint Id, uint Order)[] buttons = menu == MenuProp ? PropMenu(session, target)
                : menu == MenuChatTab ? Buttons(menu, default)
                : menu == MenuNone ? Array.Empty<(uint, uint)>()
                : Buttons(menu, Describe(session, target));

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
            if (menu == MenuProp && buttons.Length > 0)
                Props.ShowMenuName(session, target);
            else
                Props.HideMenuName(session);
            log.Debug($"{session.CharacterName}: radial menu {menu} (request {type}) for 0x{target:X}: buttons {string.Join(",", buttons.Select(b => b.Id))}.");
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
