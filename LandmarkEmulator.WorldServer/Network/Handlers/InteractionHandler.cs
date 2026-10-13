using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;

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
                    else if (Game.Spire.IsBarber(guid))
                        Game.ClientLua.Run(session, "CharacterCustomization.StartCustomization");
                    else if (Game.Spire.IsCrystal(guid))
                        // The Spire's Portal Crystal: the player stands at the hub (Spire menu: World / Local Travel).
                        Game.WarpNetwork.ActivateTerminal(session, Game.WarpNetwork.HubNode, false);
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
                    else if (Game.StoryTools.ClickCreature(session, guid))
                    {
                        // A story creature: its spawner's "clicked" Links.
                    }
                    else
                        log.Debug($"{session.CharacterName} interacted with 0x{guid:X}, which has no interaction.");
                    break;
                }
                case CommandRequest.AddFriendRequest:
                    Game.Friends.HandleAdd(session, request.Body);
                    break;
                case CommandRequest.RemoveFriendRequest:
                    Game.Friends.HandleRemove(session, request.Body);
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
                    Game.Ignores.Handle(session, request.Body);
                    break;
                case CommandRequest.FreeInteractionNpc:
                    // CommandPacketFreeInteractionNpc: the client let go of the entity it was interacting with (window
                    // closed, walked away). No reply; the server keeps no interaction lock.
                    log.Debug($"{session.CharacterName} ended an interaction ({BitConverter.ToString(request.Body)}).");
                    break;
                case CommandRequest.ClientCommand:
                    Game.Emotes.HandleCommand(session, request.Body);
                    break;
                case CommandRequest.InteractionItem:
                    UseItem(session, new GamePacketReader(request.Body).ReadULong());
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
        /// <summary>Encounter requests (entrance, exit, invitation / duel answers; docs/encounters.md).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.EncounterBase)]
        public static void HandleEncounter(WorldSession session, EncounterRequest request)
            => Game.Encounters.Handle(session, request.SubOpcode, request.Body);

        [ZoneMessageHandler(ZoneMessageOpcode.GuildBase)]
        public static void HandleGuild(WorldSession session, GuildRequest request)
        {
            Game.Guilds.Handle(session, request.SubOpcode, request.Body);
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
        /// TradeBase 0x3E (u8 sub): player trades (see <see cref="Game.Trades"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TradeBase)]
        public static void HandleTrade(WorldSession session, TradeRequest request)
        {
            Game.Trades.Handle(session, request.SubOpcode, request.Body);
        }

        /// <summary>
        /// TargetBase 0xB5 (u16 sub): the client's target slots (a tool's harvest target, a hostile...). The client only
        /// adopts a target the server confirms (TargetPacketSetTarget handler FUN_14313c120 needs the success flag):
        /// 2 RequestSetTarget {u64 target, u32 slot} → 6 SetTarget {u64, u32, bool ok}; 3 RequestRemoveTarget {u32 slot}
        /// → 7 RemoveTarget {u32 slot, bool ok}; 4 RequestRemoveAllTargets. Readers FUN_143138df0 / FUN_143138d30.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TargetBase)]
        public static void HandleTarget(WorldSession session, TargetRequest request)
        {
            var reader = new GamePacketReader(request.Body);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            switch (request.SubOpcode)
            {
                case 2 when request.Body.Length >= 12:
                {
                    ulong target = reader.ReadULong();
                    uint slot = reader.ReadUInt();
                    session.CurrentTarget = target;
                    writer.WriteUShort(6);
                    writer.Write(target);
                    writer.Write(slot);
                    writer.Write(true);
                    break;
                }
                case 3 when request.Body.Length >= 4:
                    session.CurrentTarget = 0;
                    writer.WriteUShort(7);
                    writer.Write(reader.ReadUInt());
                    writer.Write(true);
                    break;
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
        /// In-game store (cash shop). 0x0B WalletInfoRequest → 0x0C WalletInfoResponse {u32 result (1 ok), wallet
        /// {u8, u32 balance, u32, string, string, u8}} (client FUN_143185620): an empty wallet.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.InGamePurchaseBase)]
        public static void HandleInGamePurchase(WorldSession session, InGamePurchaseRequest request)
        {
            if (request.SubOpcode == 0x46 && request.Body.Length >= 9 && BitConverter.ToUInt32(request.Body, 0) == 1)
            {
                // PointOfNeedRequest type 1: build site upkeep {u32 1, u8 flag, u32 days, u64, u64}.
                Game.Claims.PayUpkeep(session, BitConverter.ToUInt32(request.Body, 5));
                return;
            }
            if (request.SubOpcode != 0x0B)
            {
                Game.Commerce.Handle(session, request.SubOpcode, request.Body);
                return;
            }
            var output = new System.Collections.Generic.List<byte>();
            var w = new GamePacketWriter(output);
            // WalletInfoResponse (0x0C; reader FUN_143185620, handler FUN_1431adaf0): u32 result, bool, u32 balance
            // (manager +0x22bc10, checked against the Player Studio listing fee), u32, string, string, bool "zip code on
            // file" (manager +0x22bc18): with it a US account may create Player Studio products (FUN_1431b4c10).
            w.WriteUShort(0x0C);
            w.Write(1u);
            w.Write((byte)0);
            w.Write(0u);
            w.Write(0u);
            w.Write("");
            w.Write("");
            w.Write((byte)1);
            session.EnqueueMessage(ZoneMessageOpcode.InGamePurchaseBase, output.ToArray());
        }

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
