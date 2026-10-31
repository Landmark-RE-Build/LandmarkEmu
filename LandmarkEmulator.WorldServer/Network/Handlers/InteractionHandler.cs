using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Requests about interacting with the world and other players: entity interaction info, Command 0x1E (interact with a
    /// station, add/remove friends, use an item), and the routing of friend, group, guild, notification, building,
    /// loot and in-game-store packets to their systems.
    /// </summary>
    public static class InteractionHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>CommandPacketHexPermissions (Command 0x1E sub 0x34, 0.695 FUN_142d10ad0) [P].</summary>
        private const ushort HexPermissionsSub = 0x34;

        /// <summary>
        /// InteractionInfoRequest (0x26/0x69): the client asks what it can do with flagged entities; answered by
        /// <see cref="Game.Props.SendInteractionInfo"/> (crafting stations are interactable within range).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateInteractionInfoRequestWire)]
        public static void HandleInteractionInfoRequest(WorldSession session, InteractionInfoRequest request)
        {
            Game.Props.SendInteractionInfo(session, request.Guids);
        }

        /// <summary>
        /// Command 0x1E (u16 sub): 9 InteractRequest {u64 guid} opens a crafting station's window, 0x10/0x12 add/remove a
        /// friend, 0x4C InteractionItem {u64 item guid} uses an item (the Build Site Flag starts site placement).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.CommandBase)]
        public static void HandleCommand(WorldSession session, CommandRequest request)
        {
            switch (request.SubOpcode)
            {
                case CommandRequest.InteractRequest:
                {
                    ulong guid = new GamePacketReader(request.Body).ReadULong();
                    uint station = Game.Props.StationAt(guid);
                    if (Game.RewardChests.Click(session, guid))
                        break;                           // a reward chest: looted in Play Mode by visitors (before site access)
                    if (!Game.Props.MayUse(session, guid))
                    {
                        // "No Touching": visitors may look around and talk with creatures but use nothing.
                        log.Info($"{session.CharacterName} may not use prop 0x{guid:X} (site access).");
                        break;
                    }
                    if (station != 0)
                        Game.CraftingStations.OpenWindow(session, station, guid);
                    else if (Game.Chests.IsChest(guid))
                        Game.Chests.Interact(session, guid);
                    else if (Game.Vendors.IsVendor(guid))
                        Game.Vendors.Open(session, guid);
                    else if (Game.Vaults.IsVault(guid))
                        Game.Vaults.Open(session, guid);
                    else if (Game.Spire.IsMailbox(guid) || Game.Props.IsMailbox(guid))
                        Game.Mail.OpenFor(session);
                    else if (Game.Spire.IsBarber(guid) || Game.Props.IsParlor(guid))
                        Game.ClientLua.Run(session, "CharacterCustomization.StartCustomization");
                    else if (Game.Spire.IsResupplyChest(guid))
                        Game.Spire.UseResupplyChest(session);
                    else if (Game.Spire.IsCrystal(guid))
                        // The Spire's Portal Crystal: the player stands at the hub (Spire menu: World / Local Travel).
                        Game.WarpNetwork.ActivateTerminal(session, Game.WarpNetwork.HubNodeOn(session.ZoneId), false);
                    else if (Game.LootChests.IsChest(guid))
                        Game.LootChests.OpenChest(session, guid);
                    else if (Game.Outposts.IsChest(guid))
                        Game.Outposts.OpenChest(session, guid);
                    else if (Game.Outposts.IsCrystal(guid))
                        Game.Outposts.UseCrystal(session, guid);
                    else if (Game.WarpNetwork.NodeOf(guid) is ulong node and not 0)
                        // An underground portal crystal: straight to the Local Travel map from there.
                        Game.WarpNetwork.ActivateTerminal(session, node, true);
                    else if (Game.Props.IsReadable(guid))
                        // A sign / book / scroll with text: its text window for the reader.
                        Game.Props.Read(session, guid);
                    else if (Game.StoryTools.Click(session, guid))
                    {
                        // A story-tool switch (flips in Play Mode).
                    }
                    else if (Game.Npc.WorldNpcs.Click(session, guid))
                    {
                        // A world NPC (Game.Npc.WorldNpcs): dialogue / quest offer.
                    }
                    else if (Game.StoryTools.ClickCreature(session, guid))
                    {
                        // A story creature: its spawner's "clicked" Links.
                    }
                    else
                        log.Debug($"{session.CharacterName} interacted with 0x{guid:X}, which has no interaction.");
                    break;
                }
                case CommandRequest.AddFriendRequest:
                    Game.Social.SocialServices.Service.AddFriend(session, request.Body);
                    break;
                case Game.Friends.ConfirmResponseSub:
                    // ConfirmFriendResponse: Accept / Refuse on the client's "Friend Invite" popup (Game/Friends.cs).
                    Game.Social.SocialServices.Service.ConfirmFriend(session, request.Body);
                    break;
                case CommandRequest.RemoveFriendRequest:
                    Game.Social.SocialServices.Service.RemoveFriend(session, request.Body);
                    break;
                case CommandRequest.SelectPlayer:
                    break;
                case CommandRequest.BuilderMode:
                    // CommandPacketBuilderMode {u8 on} (FUN_1433bec40): the player entered or left build mode. The
                    // client wants no answer; the held item is put away and brought back (Game.BuildModeHolster).
                    if (request.Body.Length >= 1)
                        Game.BuildModeHolster.BuildMode(session, request.Body[0] != 0);
                    break;
                case CommandRequest.IgnoreRequest:
                    Game.Social.SocialServices.Service.Ignore(session, request.Body);
                    break;
                case CommandRequest.FreeInteractionNpc:
                    // CommandPacketFreeInteractionNpc: the client let go of the entity it was interacting with (window
                    // closed, walked away). No reply; the server keeps no interaction lock.
                    log.Debug($"{session.CharacterName} ended an interaction ({BitConverter.ToString(request.Body)}).");
                    break;
                case CommandRequest.ClientCommand:
                    Game.Emotes.HandleCommand(session, request.Body);
                    break;
                case CommandRequest.ExecuteCommand:
                    // A command added by AddWorldCommand / AddZoneCommand (0x45 / 0x46), sent on channel 1 or 0.
                    Game.DynamicCommands.HandleExecute(session, request.Body);
                    break;
                case CommandRequest.ItemDefinitionRequest:
                    // The client lacks a definition (FUN_14335a2e0: "Item definition not found or pending, will issue
                    // new request"): answer with that one definition (0x4A), which drops it from its pending list.
                    Game.Inventory.ItemDefinitionManager.SendDefinition(session, new GamePacketReader(request.Body).ReadUInt());
                    break;
                case CommandRequest.InteractionItem:
                    UseItem(session, new GamePacketReader(request.Body).ReadULong());
                    break;
                case Game.PublicChannels.ChannelOnSub:
                case Game.PublicChannels.ChannelOffSub:
                    // CommandPacketChatChannelOn / Off {string} (Lua Ui.ChannelOn / Ui.ChannelOff): join / leave General.
                    Game.Social.SocialServices.Service.ChatChannel(session, request.SubOpcode, request.Body);
                    break;
                case Game.ChatBubbleColor.Sub:
                    // CommandPacketSetChatBubbleColor (Lua Ui.SetChatColorFgBgSz): see Game.ChatBubbleColor.
                    Game.ChatBubbleColor.Handle(session, request.Body);
                    break;
                case CommandRequest.AfkRequest:
                    // CommandPacketAFKRequest (/afk): see Game.Afk.
                    Game.Afk.Toggle(session);
                    break;
                case Game.FriendPositions.RequestSub:
                    // CommandPacketFriendsPositionRequest (map): friends' positions, FriendBase sub 5.
                    Game.FriendPositions.Handle(session);
                    break;
                case Game.PlayerPositions.RequestSub:
                    // CommandPacketRequestPlayerPositions (Lua Ui.RequestPlayerPositions): answered with 0x25, which the
                    // client reads and drops; see Game.PlayerPositions.
                    Game.PlayerPositions.Handle(session, request.Body);
                    break;
                case Game.RandomRoll.Sub:
                    // CommandPacketRandom (/random): the server rolls; see Game.RandomRoll.
                    Game.RandomRoll.Handle(session, request.Body);
                    break;
                // Admin slash commands of the retail client (unlocked by SendSelf's admin-player flag): /goto 0x42E,
                // /goto waypoint 0x430, /target 0x4B5, /hideme 0x458, /runspeed 0x4D8, /servertime & /zonetime 0x429,
                // /clienttime 0x42A, /prop 0x4EA / 0x4ED, /vehicle 0x49C, /setmodel 0x4F0, /ammo 0x499, /weaponset 0x4EE,
                // /placedeffect 0x4EF, free-fly hide 0x4F1, /friend forceadd|remove 0x4CF / 0x4D0, /profile 0x3EC.
                // Gated again server-side by Game.Admin.IsAdmin (trace/0695/admin-commands-695.md).
                case Game.AdminCommands.GotoSub:
                case Game.AdminCommands.GotoWaypointSub:
                case Game.AdminCommands.VerifyTargetSub:
                case Game.AdminCommands.HideMeSub:
                case Game.AdminCommands.RunspeedSub:
                case Game.AdminCommands.GameTimeSub:
                case Game.AdminCommands.ClientTimeSub:
                case Game.AdminCommands.SpawnPropSub:
                case Game.AdminCommands.DeleteAllPropsSub:
                case Game.AdminCommands.SpawnVehicleSub:
                case Game.AdminCommands.SetModelSub:
                case Game.AdminCommands.AddAmmoSub:
                case Game.AdminCommands.WeaponSetSub:
                case Game.AdminCommands.PlacedEffectSub:
                case Game.AdminCommands.FreeFlyHideSub:
                case Game.AdminCommands.ForceAddFriendSub:
                case Game.AdminCommands.ForceRemoveFriendSub:
                case Game.AdminCommands.ProfileSub:
                // /item add | delete | undelete | list | check | random | vault | collect | overflow | buyback … (AdminCommands.Items.cs)
                case Game.AdminCommands.AddItemSub:
                case Game.AdminCommands.DeleteItemSub:
                case Game.AdminCommands.DeleteItemByIdSub:
                case Game.AdminCommands.GetItemListSub:
                case Game.AdminCommands.GetItemInfoSub:
                case Game.AdminCommands.VaultItemSub:
                case Game.AdminCommands.RandomItemSub:
                case Game.AdminCommands.CollectionItemSub:
                case Game.AdminCommands.VaultCollectionItemSub:
                case Game.AdminCommands.OverflowItemSub:
                case Game.AdminCommands.BuybackItemSub:
                case Game.AdminCommands.GriefAdminSub:
                // Admin monitor requests (log only) and the PathFindVisualize tool's path query (AdminCommands.Monitor.cs).
                case Game.AdminCommands.DebugPathQuerySub:
                case Game.AdminCommands.PathingDataMonitorSub:
                case Game.AdminCommands.ZonePlayerCountSub:
                case Game.AdminCommands.StorybricksDiagnosticsSub:
                    Game.AdminCommands.Handle(session, request.SubOpcode, request.Body);
                    break;
                case 0x3F:
                    // CommandPacketRendererDump (header only): a renderer report; recorded (Game.Telemetry).
                    Game.Telemetry.Record(session, "RendererDump", request.Body);
                    break;
                case Game.TeleportRequests.ToLocationSub:
                case Game.TeleportRequests.ToLocationExSub:
                case Game.TeleportRequests.ManagedToLocationSub:
                    // TeleportToLocation / …Ex / TeleportManagedToLocation (FUN_1433c80a0): the Escape menu's "Evac to
                    // Safety", /location, /teleporttocamera, the admin map teleport. The client stops moving until a
                    // TeleportToLocation answers it, so every one is answered (Game.TeleportRequests).
                    Game.TeleportRequests.Handle(session, request.SubOpcode, request.Body);
                    break;
                case HexPermissionsSub:
                    // CommandPacketHexPermissions {4-byte bitset} (FUN_142d10ad0, at zone-in / teleport): sent only by a
                    // non-admin client whose config string (global 0x1451f5a20, from a client-config field) is non-empty,
                    // which the retail config leaves empty; PlanetSide 2 hex territory, nothing in Landmark. Accepted, no
                    // reply (agent-l140.md B4).
                    log.Debug($"{session.CharacterName}: HexPermissions {BitConverter.ToString(request.Body)} (ignored).");
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Command", request.SubOpcode);
                    log.Debug($"Unhandled command 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
                    break;
            }
        }

        /// <summary>
        /// FriendBase 0x4D from the client: only the window-settings report (sub 0x0B) arrives here; it is logged.
        /// Friend changes come as Command subs (see <see cref="HandleCommand"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.FriendBase)]
        public static void HandleFriend(WorldSession session, FriendRequest request)
        {
            log.Debug($"Friend packet 0x{request.SubOpcode:X2} from {session.CharacterName}: {BitConverter.ToString(request.Body)}");
        }

        /// <summary>
        /// GroupsBase 0x2B: invites, leave, kick, disband (see <see cref="Game.Groups"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.GroupsBase)]
        public static void HandleGroup(WorldSession session, GroupRequest request)
        {
            Game.Groups.Handle(session, request.SubOpcode, request.Body);
        }

        /// <summary>
        /// Notifications (bytes 01 00) from the client: u32 sub 3 = UpdateNotification {u64 notification id, u32 state}
        /// (FUN_14332f4b0 / FUN_143338890). State 4 means the Game Action Queue entry is finished: it is sent after
        /// Accept, after Decline (GameActionQueue.Respond → FUN_143338690, entry slot 7 FUN_14332f940) and on Remove,
        /// so it is no answer by itself; each popup answers through its own packet (group invite: GroupsBase Join,
        /// guild invite: GuildBase InviteReply). Logged only.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.Notifications)]
        public static void HandleNotification(WorldSession session, NotificationRequest request)
        {
            log.Debug($"Notification 0x{request.SubOpcode:X} from {session.CharacterName}: {BitConverter.ToString(request.Body)}");
        }

        /// <summary>
        /// GuildBase 0x83 (u16 sub): create, invite, invite reply, MOTD, quit (see <see cref="Game.Guilds"/>).
        /// </summary>
        /// <summary>
        /// Encounter requests (entrance, exit, invitation / duel answers; docs/encounters.md). All of them come on the
        /// World channel: the world-channel back end runs the encounter lifecycle (<see cref="Game.WorldChannel.IWorldChannelService"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.EncounterBase)]
        public static void HandleEncounter(WorldSession session, EncounterRequest request)
            => Game.WorldChannel.WorldChannelServices.WorldChannel.EncounterRequest(session, request.SubOpcode, request.Body);

        [ZoneMessageHandler(ZoneMessageOpcode.GuildBase)]
        public static void HandleGuild(WorldSession session, GuildRequest request)
        {
            Game.Social.SocialServices.Service.Guild(session, request.SubOpcode, request.Body);
        }

        /// <summary>
        /// VoxelConstructionBase 0xF4: building edits (see <see cref="Game.Building"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.VoxelConstructionBase)]
        public static void HandleConstruction(WorldSession session, ConstructionRequest request)
        {
            Game.Building.Handle(session, request.SubOpcode, request.Body);
        }

        /// <summary>
        /// TemplateBase 0xF0: designs (see <see cref="Game.Templates"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TemplateBase)]
        public static void HandleTemplate(WorldSession session, TemplateRequest request)
        {
            Game.Templates.Handle(session, request.SubOpcode, request.Body);
        }

        /// <summary>
        /// TradeBase 0x3E (u8 sub): player trades (see <see cref="Game.Trades"/>). The invitation subs come on the World
        /// channel and go to the world-channel trade broker (<see cref="Game.Trades.IsWorldChannelSub"/>); the exchange
        /// (offer, coins, accept) comes on Zone and stays with the zone, which holds the inventories. With the
        /// world-channel container running the invitation subs are answered there and never arrive here
        /// (world-channel-service.md §6.4; Game/WorldChannel/WorldChannelLink).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TradeBase)]
        public static void HandleTrade(WorldSession session, TradeRequest request)
        {
            if (Game.Trades.IsWorldChannelSub(request.SubOpcode))
                Game.WorldChannel.WorldChannelServices.WorldChannel.TradeRequest(session, request.SubOpcode, request.Body);
            else
                Game.Trades.Handle(session, request.SubOpcode, request.Body);
        }

        /// <summary>
        /// TargetBase 0xB5 (u16 sub): the client's target slots (a tool's harvest target, a hostile...). The client only
        /// adopts a target the server confirms (TargetPacketSetTarget handler FUN_14313c120 needs the success flag):
        /// 2 RequestSetTarget {u64 target, u32 slot} → 6 SetTarget {u64, u32, bool ok}; 3 RequestRemoveTarget {u32 slot}
        /// → 7 RemoveTarget {u32 slot, bool ok}; 4 RequestRemoveAllTargets. Readers FUN_143138df0 / FUN_143138d30.
        /// The client adopts the slot the answer carries (FUN_1431e6b40), so a gathering node is put in the harvest slot 9
        /// whatever slot was asked, with its health sent first (Game/Gathering.Health.cs: the tree's health bar).
        /// </summary>
        private const uint MaxTargetSlots = 16;

        [ZoneMessageHandler(ZoneMessageOpcode.TargetBase)]
        public static void HandleTarget(WorldSession session, TargetRequest request)
        {
            // The client's slots are small numbers (3 hostile, 8 ally, 9 harvest); others are answered but not kept.
            var reader = new GamePacketReader(request.Body);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            switch (request.SubOpcode)
            {
                case 2 when request.Body.Length >= 12:
                {
                    ulong target = reader.ReadULong();
                    uint slot = reader.ReadUInt();
                    if (Game.Gathering.Targeted(session, target))
                        slot = Game.Gathering.HarvestTargetSlot;
                    else if (slot == Game.Gathering.HarvestTargetSlot)
                        Game.Gathering.Untargeted(session);   // slot 9 now holds a neutral creature, not the tree
                    session.CurrentTarget = target;
                    if (slot < MaxTargetSlots)
                        session.TargetSlots[slot] = target;
                    writer.WriteUShort(6);
                    writer.Write(target);
                    writer.Write(slot);
                    writer.Write(true);
                    break;
                }
                case 3 when request.Body.Length >= 4:
                {
                    uint slot = reader.ReadUInt();
                    if (slot == Game.Gathering.HarvestTargetSlot)
                        Game.Gathering.Untargeted(session);
                    // Only the slot's own entity stops being the current target: a felled tree leaving slot 9 hands it to
                    // the creature still in slot 3 (or the ally in 8). The client repeats the request every frame until
                    // the answer arrives (CheckTargets FUN_1431e57e0) [P], so a slot already gone changes nothing unless
                    // the current target sits in no slot at all.
                    if (session.TargetSlots.Remove(slot, out ulong removed))
                    {
                        if (removed == session.CurrentTarget)
                            session.CurrentTarget = new uint[] { 3, 8, 9 }.Select(s => session.TargetSlots.GetValueOrDefault(s))
                                .FirstOrDefault(g => g != 0);
                    }
                    else if (!session.TargetSlots.ContainsValue(session.CurrentTarget))
                        session.CurrentTarget = 0;
                    writer.WriteUShort(7);
                    writer.Write(slot);
                    writer.Write(true);
                    break;
                }
                default:
                    log.Debug($"Target request {request.SubOpcode} from {session.CharacterName}: {BitConverter.ToString(request.Body)}");
                    return;
            }
            session.EnqueueMessage(ZoneMessageOpcode.TargetBase, output.ToArray());
        }

        /// <summary>
        /// ChestBase 0xFF (u32 sub): chest contents requests (see <see cref="Game.Chests"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ChestBase)]
        public static void HandleChest(WorldSession session, ChestRequest request)
        {
            Game.Chests.Handle(session, request.SubOpcode, request.Body);
        }

        /// <summary>PlayerTitleBase 0x94: choosing the title to show (see <see cref="Game.Titles"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerTitleBase)]
        public static void HandlePlayerTitle(WorldSession session, PlayerTitleRequest request)
            => Game.Titles.Handle(session, request.SubOpcode, request.Body);

        /// <summary>
        /// LootPinataBase 0xFB: picking up mined chunks (see <see cref="Game.LootPinatas"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.LootPinataBase)]
        public static void HandleLootPinata(WorldSession session, LootPinataRequest request)
        {
            Game.LootPinatas.Handle(session, request.SubOpcode, request.Body);
        }

        /// <summary>
        /// In-game store (cash shop) and Player Studio, InGamePurchase 0x45: build site upkeep (PointOfNeed type 1) and
        /// the zone's own subs (<see cref="Game.Commerce.IsZoneSub"/>) are answered here; the rest (wallet, account,
        /// products, reviews, account store) goes to the shared commerce back end
        /// (<see cref="Game.WorldChannel.ICommerceService"/>).
        /// </summary>
        /// <summary>
        /// PointOfNeedRequest (0.695 0x47, 0.589 0x46; reader FUN_143235ff0; id table cIdPointOfNeedRequest 0x47) [P].
        /// </summary>
        private const ushort PointOfNeedRequestSub = 0x47;

        [ZoneMessageHandler(ZoneMessageOpcode.InGamePurchaseBase)]
        public static void HandleInGamePurchase(WorldSession session, InGamePurchaseRequest request)
        {
            // 0.695 numbers as sent (TemplateProductCreate 0x30 taken as ProductCreate 0x2F: Game.Commerce.FromWire).
            ushort sub = Game.Commerce.FromWire(session, request.SubOpcode);
            if (sub == 0x2A)
            {
                // SpiceWebAuthUrlRequest: the reply would send the client to an outside web service; recorded only.
                Game.Telemetry.Record(session, "SpiceWebAuthUrlRequest", request.Body);
                return;
            }
            if (sub == PointOfNeedRequestSub && request.Body.Length >= 9 && BitConverter.ToUInt32(request.Body, 0) == 1)
            {
                // PointOfNeedRequest type 1: build site upkeep {u32 1, u8 flag, u32 days, u64, u64}.
                Game.Claims.PayUpkeep(session, BitConverter.ToUInt32(request.Body, 5));
                return;
            }
            if (Game.Commerce.IsZoneSub(sub))
            {
                Game.Commerce.HandleZone(session, sub, request.Body);
                return;
            }
            Game.WorldChannel.WorldChannelServices.Commerce.InGamePurchaseRequest(session, sub, request.Body);
        }

        /// <summary>
        /// PacketClientInGamePurchase 0xD0 (Lua InGamePurchaseStoreScreen.PurchaseBundleWithInGameCurrency): a bundle of the
        /// client's store bought with in-game currency — Lumens here, through the Showcase store's purchase (see
        /// <see cref="Game.StoreBundles.HandlePurchase"/>). Zone channel, in both commerce modes (the Lumens are this world's).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.InGamePurchase)]
        public static void HandleClientInGamePurchase(WorldSession session, ClientInGamePurchase request)
            => Game.StoreBundles.HandlePurchase(session, request.First, request.Second, request.Third);

        /// <summary>
        /// Command InteractionItem: using an item through its interaction (right-click). Only the Build Site Flag does
        /// something today: it starts build-site placement.
        /// </summary>
        private static void UseItem(WorldSession session, ulong itemGuid)
        {
            var item = session.Inventory.Get(itemGuid);
            if (item == null)
            {
                log.Debug($"{session.CharacterName} used item 0x{itemGuid:X}, which they don't have.");
                return;
            }
            if (item.ItemDefinitionId == Game.Claims.BuildSiteFlagItem)
                Game.Claims.BeginPlacement(session);
            else
                log.Debug($"{session.CharacterName} used item {item.ItemDefinitionId}, which has no use yet.");
        }
    }
}
