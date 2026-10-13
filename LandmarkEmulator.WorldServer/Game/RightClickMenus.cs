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
            (uint Id, uint Order)[] buttons = menu == 1 ? PropMenu(target)
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
        }

        /// <summary>
        /// The prop menu (RadialMenuButtons.txt menu type 1; sort order in brackets): Script 63 (0) for story-tool props
        /// (Prop.SetupScript), Settings 56 (1) for props with settings (Prop.SetupObject, PropSettings), Light on 73 (2) /
        /// off 70 (3) for props with an on/off state (Prop.SetPropEnable; whichever applies), Reposition 55 (4), Adjust 2
        /// (5), Duplicate 18 (7), Pick up 17 (8), Delete 3 (10). Disassemble 64 (9) is left out: it is pick-up / delete
        /// under another name and live used it for no prop we know of.
        /// </summary>
        private static (uint Id, uint Order)[] PropMenu(ulong guid)
        {
            var buttons = new List<(uint, uint)> { (55u, 4u), (2u, 5u), (18u, 7u), (17u, 8u), (3u, 10u) };
            if (Props.Find(guid) is not Props.PlacedProp prop)
                return buttons.ToArray();
            StoryTools.Kind kind = StoryTools.KindOf(prop.PropId);
            if (kind != null)
                buttons.Add((63u, 0u));
            if (PropSettings.SettingsOf(prop.PropId).Count > 0)
                buttons.Add((56u, 1u));
            if (PropEffects.HasEffect(prop.PropId) || kind?.Toggle == true)
                buttons.Add(prop.Disabled ? (73u, 2u) : (70u, 3u));
            return buttons.OrderBy(b => b.Item2).ToArray();
        }
    }
}
