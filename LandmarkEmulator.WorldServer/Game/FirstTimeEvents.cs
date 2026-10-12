using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// First-time events: the one-off help pages a player sees the first time they open the inventory, equip a pick,
    /// visit the mailbox... (FirstTimeEvents.txt names them; the pages are the client's HelpPages/*_FTE.html).
    ///
    /// Client side (ClientFirstTimeEventManager at ClientPcData +0x75088): a byte "enabled", then the set of events
    /// already seen (+0x75090). Both come in SendSelfToClient (SendFirstTimeEvents, FirstTimeEventsSeen).
    /// FirstTimeEventBase 0x6D, u8 sub:
    ///   client → 1 TriggerRequest {u32 event}: sent by Ui.TriggerFirstTimeEvent (the ClientTimerDefinitions.txt timers:
    ///            GrapplingHookRecipe, PulverizerRecipe, CombatPrototypePopup) when enabled and not seen yet (FUN_143580bf0).
    ///   client → 3 ClearRequest {u32 event, -1 = all}, 4 EnablePacket {bool}: the client's debug FTE commands.
    ///   server → 5 ScriptPacket {string function, string params, u32 event, bool clear} (FUN_14357f000 / FUN_143580670):
    ///            runs the Lua function with the comma-separated params ("Table.Method" calls Table:Method), then marks
    ///            the event seen (clear = false) or unseen (FUN_1435802c0). While loading the client queues it.
    ///   server → 2 StatePacket {u32 event, bool clear}: only marks; clear with event -1 forgets every event.
    /// The page opens through the client's own ShowHelpWindow(size, page, position) (Scripts/global.lua).
    ///
    /// Most events are noticed by the server: UI events the Flash windows report (UIEventTriggered, see
    /// <see cref="Achievements.UiEvent"/>), equipping a pick or axe, the first wear on gear. Seen events are kept as
    /// <see cref="Unlocks.FirstTimeEventSeen"/>; <see cref="Unlocks.FirstTimeEventsOff"/> records a player who turned
    /// them off.
    /// </summary>
    public static class FirstTimeEvents
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte TriggerRequest = 1, StatePacket = 2, ClearRequest = 3, EnablePacket = 4, ScriptPacket = 5;

        /// <summary>Event ids (FirstTimeEvents.txt).</summary>
        public const uint GrapplingHookRecipe = 8, PulverizerRecipe = 9, CombatPrototypePopup = 10, BuildModeHelp = 11,
            TeleportMenuHelp = 12, TintsTabHelp = 13, BlocksTabHelp = 14, PropsTabHelp = 15, EquipAxe = 16, EquipPick = 17,
            RepairOpen = 18, ClaimPermissions = 19, FirstWear = 21, FriendList = 22, Journal = 23, GuildWindow = 24,
            InventoryOpen = 25, InventoryOverflow = 26, Mailbox = 27, DailyGoals = 34, TweakMode = 35;

        /// <summary>
        /// The help page each event shows (HelpPages/&lt;page&gt;.html) and where: ShowHelpWindow sizes "Large" 900×700,
        /// anything else 640×400; positions TopL/Top/TopR/CenterL/Center/CenterR/BotL/Bot/BotR. The live server's
        /// choices aren't in the client, so the help sits beside the window that triggered it rather than over it.
        /// Events without a page here (the recipe and combat-prototype timers) are only marked seen.
        /// </summary>
        private static readonly Dictionary<uint, (string Page, string Position)> pages = new()
        {
            [BuildModeHelp] = ("Design_FTE", "TopR"),
            [TeleportMenuHelp] = ("Teleport_Help_Small", "CenterR"),
            [TintsTabHelp] = ("Tints_FTE", "TopR"),
            [BlocksTabHelp] = ("Blocks_FTE", "TopR"),
            [PropsTabHelp] = ("Props_Help_Small", "TopR"),
            [EquipAxe] = ("Axe_FTE", "CenterR"),
            [EquipPick] = ("Pick_FTE", "CenterR"),
            [RepairOpen] = ("Repair_Window_FTE", "CenterL"),
            [ClaimPermissions] = ("Claim_Permissions_FTE", "CenterR"),
            [FirstWear] = ("Durability_FTE", "CenterR"),
            [FriendList] = ("Friend_List_FTE", "CenterR"),
            [Journal] = ("Journal_FTE", "CenterR"),
            [GuildWindow] = ("Guild_Window_FTE", "CenterR"),
            [InventoryOpen] = ("Inventory_FTE", "CenterL"),
            [InventoryOverflow] = ("Inventory_Overflow_FTE", "CenterL"),
            [Mailbox] = ("Mail_FTE", "CenterR"),
            [DailyGoals] = ("Daily_Goals_FTE", "CenterR"),
            [TweakMode] = ("Tweak_Mode_FTE", "CenterR")
        };

        /// <summary>
        /// UI event (Ui.UIEventTriggered, re/out/ui_events.txt) → first-time event: 2 Build Mode opened, 4 Travel menu,
        /// 10/11/12 Build Mode Tint/Prop/Build tabs, 20 repair station, 21 claim Permissions tab, 23 contacts, 24 journal,
        /// 25 guild, 26 inventory, 27 its Overflow tab, 28 inbox, 30 the journal's Daily Goals category, 31 Tweak Mode.
        /// </summary>
        private static readonly Dictionary<uint, uint> uiEvents = new()
        {
            [2] = BuildModeHelp, [4] = TeleportMenuHelp, [10] = TintsTabHelp, [11] = PropsTabHelp, [12] = BlocksTabHelp,
            [20] = RepairOpen, [21] = ClaimPermissions, [23] = FriendList, [24] = Journal, [25] = GuildWindow,
            [26] = InventoryOpen, [27] = InventoryOverflow, [28] = Mailbox, [30] = DailyGoals, [31] = TweakMode
        };

        /// <summary>SendSelfToClient: is the client's first-time event manager on?</summary>
        public static bool Enabled(ulong characterId) => !Unlocks.Values(characterId, Unlocks.FirstTimeEventsOff).Any();

        /// <summary>SendSelfToClient: the events this character has seen.</summary>
        public static List<uint> Seen(ulong characterId) => Unlocks.Values(characterId, Unlocks.FirstTimeEventSeen);

        /// <summary>The client reported a UI event: show its first-time help if it has one.</summary>
        public static void UiEvent(WorldSession session, uint uiEvent)
        {
            if (uiEvents.TryGetValue(uiEvent, out uint id))
                Trigger(session, id);
        }

        /// <summary>A tool was equipped: the pick and axe help pages.</summary>
        public static void Equipped(WorldSession session, uint itemDefinitionId)
        {
            var abilities = Inventory.ItemDefinitionManager.Get(itemDefinitionId)?.Abilities;
            if (abilities == null)
                return;
            if (abilities.Any(a => Abilities.MiningAbilities.Contains(a.AbilityId)))
                Trigger(session, EquipPick);
            else if (abilities.Any(a => Abilities.LoggingAbilities.Contains(a.AbilityId)))
                Trigger(session, EquipAxe);
        }

        /// <summary>
        /// Shows a first-time event once: the help page (if it has one) and the client's "seen" mark, both in one
        /// ScriptPacket; the seen set is saved. Nothing happens when already seen or the player turned them off.
        /// </summary>
        public static void Trigger(WorldSession session, uint id)
        {
            if (!Enabled(session.PlayerGuid) || !Unlocks.Add(session, Unlocks.FirstTimeEventSeen, id))
                return;
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            if (pages.TryGetValue(id, out var page))
            {
                writer.Write(ScriptPacket);
                writer.Write("ShowHelpWindow");
                writer.Write($"Small,{page.Page},{page.Position}");
                writer.Write(id);
                writer.Write(false);
            }
            else
            {
                writer.Write(StatePacket);
                writer.Write(id);
                writer.Write(false);
            }
            session.EnqueueMessage(ZoneMessageOpcode.FirstTimeEventBase, output.ToArray());
            log.Debug($"{session.CharacterName}: first-time event {id} ({page.Page ?? "no page"}).");
        }

        /// <summary>FirstTimeEventBase from the client: trigger, clear (debug) and enable (debug) requests.</summary>
        public static void Handle(WorldSession session, FirstTimeEventRequest request)
        {
            var reader = new GamePacketReader(request.Body);
            switch (request.SubOpcode)
            {
                case TriggerRequest when request.Body.Length >= 4:
                    Trigger(session, reader.ReadUInt());
                    break;
                case ClearRequest when request.Body.Length >= 4:
                {
                    uint id = reader.ReadUInt();
                    if (id == uint.MaxValue)
                        Unlocks.Clear(session, Unlocks.FirstTimeEventSeen);
                    else
                        Unlocks.Replace(session, Unlocks.FirstTimeEventSeen, Seen(session.PlayerGuid).Where(e => e != id));
                    var output = new List<byte>();
                    var writer = new GamePacketWriter(output);
                    writer.Write(StatePacket);
                    writer.Write(id);
                    writer.Write(true);
                    session.EnqueueMessage(ZoneMessageOpcode.FirstTimeEventBase, output.ToArray());
                    break;
                }
                case EnablePacket when request.Body.Length >= 1:
                    if (reader.ReadBool())
                        Unlocks.Clear(session, Unlocks.FirstTimeEventsOff);
                    else
                        Unlocks.Set(session, Unlocks.FirstTimeEventsOff, 1);
                    break;
                default:
                    log.Debug($"First-time event request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }
    }
}
