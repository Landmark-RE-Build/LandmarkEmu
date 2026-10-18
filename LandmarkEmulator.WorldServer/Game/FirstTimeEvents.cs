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
            InventoryOpen = 25, InventoryOverflow = 26, Mailbox = 27, DailyGoals = 34, TweakMode = 35,
            AppearancePurchase = 43, MeshTabHelp = 48;

        /// <summary>
        /// Chaos Cavern events (FirstTimeEvents.txt 36 ChaosCavern, 37 Outpost, 38 LostCrystal; P5732, 7 Jun 2016: first-time
        /// help for Chaos Caverns, Outposts and Lost Crystals; audit-outposts-695.md F7): shown on arriving in a cavern, on
        /// coming near an outpost (where its name shows) and on the first use of a Lost Crystal.
        /// </summary>
        public const uint ChaosCavern = 36, Outpost = 37, LostCrystal = 38;

        /// <summary>
        /// Story-tool event ids (FirstTimeEvents.txt rows ScriptObj*): 28 Logic, 29 Path (Travel Point), 30 Probability
        /// (0.589 "ScriptObjRandom"; in 0.695 row 30 is renamed and the Probability Filter has its own row 46), 32 Text
        /// (Story Node), 33 Timer, 39 Damage, 40 Focus, 41 Reset; 0.695 only: 44 Respawn, 45 Combat, 46 Probability,
        /// 47 Server Time.
        /// </summary>
        public const uint ScriptObjLogic = 28, ScriptObjPath = 29, ScriptObjRandom589 = 30, ScriptObjText = 32,
            ScriptObjTimer = 33, ScriptObjDamage = 39, ScriptObjFocus = 40, ScriptObjReset = 41, ScriptObjRespawn = 44,
            ScriptObjCombat = 45, ScriptObjProbability = 46, ScriptObjServerTime = 47;

        /// <summary>
        /// The help page each event shows (HelpPages/&lt;page&gt;.html) and where: ShowHelpWindow sizes "Large" 900×700,
        /// anything else 640×400; positions TopL/Top/TopR/CenterL/Center/CenterR/BotL/Bot/BotR. The live server's
        /// choices aren't in the client, so the help sits beside the window that triggered it rather than over it.
        /// Events without a page here (the recipe and combat-prototype timers) are only marked seen.
        /// The 0.695 pages added with the Crimson Parlor and the Mesh tab open "Large": both are two-column pages with
        /// screenshots (361 px and 145 + 257 px wide) that don't fit 640×400, and the one size the 0.695 client itself
        /// names for a help page (PropHelpWindow.txt: ScriptObj_Damage_FTE, Large, CenterR) is Large.
        /// </summary>
        private static readonly Dictionary<uint, (string Page, string Position, string Size)> pages = new()
        {
            [BuildModeHelp] = ("Design_FTE", "TopR", "Small"),
            [TeleportMenuHelp] = ("Teleport_Help_Small", "CenterR", "Small"),
            [TintsTabHelp] = ("Tints_FTE", "TopR", "Small"),
            [BlocksTabHelp] = ("Blocks_FTE", "TopR", "Small"),
            [PropsTabHelp] = ("Props_Help_Small", "TopR", "Small"),
            [EquipAxe] = ("Axe_FTE", "CenterR", "Small"),
            [EquipPick] = ("Pick_FTE", "CenterR", "Small"),
            [RepairOpen] = ("Repair_Window_FTE", "CenterL", "Small"),
            [ClaimPermissions] = ("Claim_Permissions_FTE", "CenterR", "Small"),
            [FirstWear] = ("Durability_FTE", "CenterR", "Small"),
            [FriendList] = ("Friend_List_FTE", "CenterR", "Small"),
            [Journal] = ("Journal_FTE", "CenterR", "Small"),
            [GuildWindow] = ("Guild_Window_FTE", "CenterR", "Small"),
            [InventoryOpen] = ("Inventory_FTE", "CenterL", "Small"),
            [InventoryOverflow] = ("Inventory_Overflow_FTE", "CenterL", "Small"),
            [Mailbox] = ("Mail_FTE", "CenterR", "Small"),
            [DailyGoals] = ("Daily_Goals_FTE", "CenterR", "Small"),
            [TweakMode] = ("Tweak_Mode_FTE", "CenterR", "Small"),
            [AppearancePurchase] = ("AppearancePurchase_FTE", "CenterR", "Large"),
            [MeshTabHelp] = ("Mesh_FTE", "TopR", "Large"),
            // Chaos Caverns (HelpPages/ChaosCavern_*_FTE.html, 0.695): one-column "small_help" pages like the other Small ones.
            [ChaosCavern] = ("ChaosCavern_1Enter_FTE", "CenterR", "Small"),
            [Outpost] = ("ChaosCavern_2Outpost_FTE", "CenterR", "Small"),
            [LostCrystal] = ("ChaosCavern_3Crystal_FTE", "CenterR", "Small"),
            // Story tools (HelpPages/ScriptObj_*_FTE.html, all present in the 0.695 client). Size and place are the one
            // pair the client itself names for such a page (PropHelpWindow.txt row 1: ScriptObj_Damage_FTE, Large,
            // CenterR); the pages are two-column with screenshots, like the other Large ones.
            [ScriptObjLogic] = ("ScriptObj_Logic_FTE", "CenterR", "Large"),
            [ScriptObjPath] = ("ScriptObj_Path_FTE", "CenterR", "Large"),
            [ScriptObjRandom589] = ("ScriptObj_Random_FTE", "CenterR", "Large"),
            [ScriptObjText] = ("ScriptObj_Text_FTE", "CenterR", "Large"),
            [ScriptObjTimer] = ("ScriptObj_Timer_FTE", "CenterR", "Large"),
            [ScriptObjDamage] = ("ScriptObj_Damage_FTE", "CenterR", "Large"),
            [ScriptObjFocus] = ("ScriptObj_Focus_FTE", "CenterR", "Large"),
            [ScriptObjReset] = ("ScriptObj_Reset_FTE", "CenterR", "Large"),
            [ScriptObjRespawn] = ("ScriptObj_RespawnLoc_FTE", "CenterR", "Large"),
            [ScriptObjCombat] = ("ScriptObj_Combat_FTE", "CenterR", "Large"),
            [ScriptObjProbability] = ("ScriptObj_Probability_FTE", "CenterR", "Large"),
            [ScriptObjServerTime] = ("ScriptObj_Servertime_FTE", "CenterR", "Large")
        };

        /// <summary>
        /// Story-tool prop id → its first-time event (StoryTools prop ids). 0.695 Probability is 46 (0.589 used row 30, and
        /// had no Respawn Location, Combat Node or Server Time Node).
        /// </summary>
        private static readonly Dictionary<uint, uint> propEvents = new()
        {
            [StoryTools.LogicFilterProp] = ScriptObjLogic,          // 2325
            [StoryTools.TravelPointProp] = ScriptObjPath,           // 2330
            [StoryTools.DiceRollProp] = ScriptObjProbability,       // 2345
            [StoryTools.StoryNodeProp] = ScriptObjText,             // 2344
            [StoryTools.TimerProp] = ScriptObjTimer,                // 2367
            [StoryTools.DamageNodeProp] = ScriptObjDamage,          // 2899
            [StoryTools.FocusNodeProp] = ScriptObjFocus,            // 2898
            [StoryTools.ResetNodeProp] = ScriptObjReset,            // 2900
            [StoryTools.RespawnPointProp] = ScriptObjRespawn,       // 2701
            [StoryTools.CombatNodeProp] = ScriptObjCombat,          // 2940
            [StoryTools.ServerTimeNodeProp] = ScriptObjServerTime,  // 2659
        };

        /// <summary>
        /// UI event (Ui.UIEventTriggered, re/out/ui_events.txt) → first-time event: 2 Build Mode opened, 4 Travel menu,
        /// 10/11/12 Build Mode Tint/Prop/Build tabs, 20 repair station, 21 claim Permissions tab, 23 contacts, 24 journal,
        /// 25 guild, 26 inventory, 27 its Overflow tab, 28 inbox, 30 the journal's Daily Goals category, 31 Tweak Mode;
        /// 0.695: 33 the Crimson Parlor's unlock / purchase buttons (BarberShopWidget.as 401, 410), 34 Build Mode's Mesh
        /// tab (BuildModeWidget.as 337).
        /// </summary>
        private static readonly Dictionary<uint, uint> uiEvents = new()
        {
            [2] = BuildModeHelp, [4] = TeleportMenuHelp, [10] = TintsTabHelp, [11] = PropsTabHelp, [12] = BlocksTabHelp,
            [20] = RepairOpen, [21] = ClaimPermissions, [23] = FriendList, [24] = Journal, [25] = GuildWindow,
            [26] = InventoryOpen, [27] = InventoryOverflow, [28] = Mailbox, [30] = DailyGoals, [31] = TweakMode,
            [33] = AppearancePurchase, [34] = MeshTabHelp
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
        /// A prop was placed for the first time (Props.Save, next to Achievements.PropPlaced): a story tool shows its help
        /// page once (trace/0695/plan-storytools-next.md Spec 2, TASKS 2.27).
        /// <b>Inferred</b>: the moment. The 0.695 client has the events (FirstTimeEvents.txt 28-47) and pages
        /// (HelpPages/ScriptObj_*_FTE.html) but no caller of its own — no UIEventTriggered for them in the 0.695 Flash
        /// (re/as3_695) and no ClientTimerDefinitions timer — so the live server triggered them; first placement is the
        /// natural point and is kept as an open lead (L23).
        /// </summary>
        public static void PropPlaced(WorldSession session, uint propId)
        {
            if (!propEvents.TryGetValue(propId, out uint id))
                return;
            Trigger(session, id);
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
                writer.Write($"{page.Size},{page.Page},{page.Position}");
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
