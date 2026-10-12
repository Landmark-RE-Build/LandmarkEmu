using System.Collections.Generic;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using Newtonsoft.Json;
using NLog;
using System;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Zone housekeeping and session-wide requests: client logs, keep-alive, the clocks (GameTimeSync, Synchronization),
    /// zone entry (<see cref="ClientIsReady"/> sends the rest of the initial world), ability info and casts, UI events,
    /// mail.
    /// </summary>
    public class MiscHandler
    {
        protected static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// ClientLog: a line from the client's own log, written to ours at debug level.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClientLog)]
        public static void HandleClientLog(WorldSession session, ClientLog clientLog)
        {
            log.Debug(JsonConvert.SerializeObject(clientLog, Formatting.Indented));
        }

        /// <summary>The player asked to be moved to the zone's safe point (see <see cref="Game.Health.EvacuateToSafePoint"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ZoneSafeTeleportRequest)]
        public static void ZoneSafeTeleport(WorldSession session, ZoneSafeTeleportRequest request)
            => Game.Health.EvacuateToSafePoint(session);

        /// <summary>The character controller's contact report (see <see cref="AbilityCollisionReport"/>); logged only.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityCollisionReport)]
        public static void HandleCollisionReport(WorldSession session, AbilityCollisionReport report)
            => log.Trace($"{session.CharacterName} collision report: {string.Join(", ", report.Entities.Select(e => $"0x{e:X}"))}");

        /// <summary>The client's periodic keep-alive {u32 game time}. Receiving it is the point (the session's
        /// timeout clock resets on any packet); no reply, to save bandwidth.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.KeepAlive)]
        public static void HandleKeepAlive(WorldSession session, KeepAlive keepAlive)
        {
        }

        /// <summary>Key binding customisation the client reports; the client keeps its own bindings locally, so
        /// nothing to store yet.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateBindingCustomization)]
        public static void HandleBindingCustomization(WorldSession session, PlayerUpdateBindingCustomization binding)
        {
        }

        /// <summary>Effect list request on every zone load: the player's active buffs are shown again.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityEffectUpdatesRequest)]
        public static void HandleEffectUpdates(WorldSession session, AbilityEffectUpdatesRequest request)
            => Game.Buffs.Resend(session);

        /// <summary>Reticle ability aim data, already taken from the cast.</summary>
        /// <summary>The buff bar's cancel button (see <see cref="Game.Buffs.Cancel"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityRequestCancelEffect)]
        public static void HandleCancelEffect(WorldSession session, AbilityRequestCancelEffect request)
            => Game.Buffs.Cancel(session, request.EffectId);

        [ZoneMessageHandler(ZoneMessageOpcode.AbilityDelayedTargetInfo)]
        public static void HandleDelayedTargetInfo(WorldSession session, AbilityDelayedTargetInfo info)
        {
        }

        /// <summary>UI events the client reports (<see cref="UiRequest"/>): goal progress and first-time help.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.UiBase)]
        public static void HandleUi(WorldSession session, UiRequest request)
        {
            if (request.SubOpcode != UiRequest.EventTriggered)
                log.Debug($"Unhandled UI request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
            else if (request.Body.Length >= 4)
            {
                uint uiEvent = BitConverter.ToUInt32(request.Body, 0);
                Game.Achievements.UiEvent(session, uiEvent);
                Game.Store.UiEvent(session, uiEvent);
                Game.FirstTimeEvents.UiEvent(session, uiEvent);
            }
        }

        /// <summary>FirstTimeEventBase 0x6D: first-time help events (see <see cref="Game.FirstTimeEvents"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.FirstTimeEventBase)]
        public static void HandleFirstTimeEvent(WorldSession session, FirstTimeEventRequest request)
            => Game.FirstTimeEvents.Handle(session, request);

        /// <summary>Positions of client-driven entities (a held prop); not relayed, others see the saved prop.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateManagedPosition)]
        public static void HandleManagedPosition(WorldSession session, PlayerUpdateManagedPosition update)
        {
        }

        /// <summary>
        /// MailBase 0xF8: the inbox (see <see cref="Game.Mail"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.MailBase)]
        public static void HandleMail(WorldSession session, MailRequest request) => Game.Mail.Handle(session, request);

        /// <summary>HudManagerBase 0xDC: right-click menus (see <see cref="Game.RightClickMenus"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.HudManagerBase)]
        public static void HandleHud(WorldSession session, HudRequest request)
            => Game.RightClickMenus.Handle(session, request.SubOpcode, request.Body);

        /// <summary>WarpNetwork 0x113 sub 4: the Spire's Local Travel network and warps (see <see cref="Game.WarpNetwork"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.WarpNetworkWire)]
        public static void HandleWarpNetwork(WorldSession session, WarpNetworkRequest request)
        {
            if (request.SubOpcode == Game.WarpNetwork.RequestSub)
                Game.WarpNetwork.Handle(session, request.Body);
        }

        /// <summary>TeleportBase 0xFC: Travel window requests (see <see cref="Game.Travel"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.TeleportBase)]
        public static void HandleTeleport(WorldSession session, TeleportRequest request) => Game.Travel.Handle(session, request);

        /// <summary>Reports from the client that need no answer (metrics, locale, PC details, loading state...).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClientMetrics)]
        public static void HandleClientMetrics(WorldSession session, ClientMetrics message) { }

        /// <summary>FotomatBase: portrait / headshot requests (see <see cref="Game.Portraits"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.FotomatBase)]
        public static void HandleFotomat(WorldSession session, FotomatRequest message) => Game.Portraits.Handle(session, message.Body);

        /// <summary>AddClientPortraitCrc (0x75 {u32}): the checksum of the portrait the client is about to upload.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AddClientPortraitCrc)]
        public static void HandlePortraitCrc(WorldSession session, AddClientPortraitCrc message) => Game.Portraits.Announce(session, message.Crc);

        [ZoneMessageHandler(ZoneMessageOpcode.SetLocale)]
        public static void HandleSetLocale(WorldSession session, SetLocale message) { }

        /// <summary>
        /// RatingBase 0xA7 (u8 sub). 0x1D RequestContestsRequest (Lua RequestContests, sent often): answered with
        /// 0x1E RequestContestsReply {u64, u32 count, entries} (reader FUN_142d09110, which fills the Contests data
        /// source; see <see cref="Game.Contests"/>). 8 AddVote: <see cref="Game.Ratings"/>. Others
        /// (CanVote 0x18) are logged.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.RatingBase)]
        public static void HandleRating(WorldSession session, RatingRequest message)
        {
            if (message.Body.Length == 0)
                return;
            if (message.Body[0] == 0x1D)
                Game.Contests.SendList(session);
            else if (message.Body[0] == 0x08)
                Game.Ratings.HandleAddVote(session, message.Body);
            else if (message.Body[0] == 0x18)
                Game.Ratings.HandleCanVote(session, message.Body);
            else
                log.Info($"Rating request from {session.CharacterName}: {BitConverter.ToString(message.Body)}");
        }

        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateLoading)]
        public static void HandleLoading(WorldSession session, PlayerUpdateLoading message) { }

        [ZoneMessageHandler(ZoneMessageOpcode.ClientPcDataBase)]
        public static void HandleClientPcData(WorldSession session, ClientPcData message) { }

        [ZoneMessageHandler(ZoneMessageOpcode.ClientInitializationDetails)]
        public static void HandleClientInitializationDetails(WorldSession session, ClientInitializationDetails message) { }

        public static double LastClientTime;

        /// <summary>Answers the client's clock sync with the world clock (<see cref="Game.WorldClock"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.GameTimeSync)]
        public static void GameTimeSync(WorldSession session, GameTimeSync gameTimeSync)
        {
            LastClientTime = gameTimeSync.Time;
            session.EnqueueMessage(new GameTimeSync
            {
                Time = Game.WorldClock.SyncTime(gameTimeSync.Time),
                CycleSpeed = Game.WorldClock.CycleSpeed,
                Unknown0 = false
            });
        }

        /// <summary>
        /// ClientIsReady: the client has loaded SendSelf and is ready for the rest of the zone. Sent once per session:
        /// quick chat, "done sending preload characters", calculated stats and visible equipment, achievements and the
        /// activity list (captured from a live server), the health/resource records, the default loadout, the emote list,
        /// ZoneDoneSendingInitialData, zone details again (continent 406, its biome/shape/height-layer bitmaps), zone
        /// population, the welcome screen, experience, the prop tint list, ZoneCoordinateInfo (the zone origin), the
        /// character's default chest attachment, the movement version (1), client settings (help/shop URLs), the terrain
        /// manifest of cells this server changed (<see cref="Game.Terrain.TerrainStore.SendManifest"/>) and area
        /// definitions. Most of these bodies are captures that the client accepts as they are; they are replaced with real
        /// data as each system is built.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClientIsReady)]
        public static void ClientIsReady(WorldSession session, ClientIsReady clientIsReady)
        {
            if (session.ReadySent)
            {
                // Again after a re-zone (Game.Teleport.Rezone): the client keeps its entities and terrain, but zoning
                // clears three flags that WaitForConfirmationPacket (FUN_142cc5120) waits on: preload done
                // (ClientUpdateDoneSendingPreloadCharacters), initial zone data complete (ZoneDoneSendingInitialData)
                // and area definitions loaded. 21 bytes in all.
                session.EnqueueMessage(new ClientUpdateDoneSendingPreloadCharacters());
                session.EnqueueMessage(new ZoneDoneSendingInitialData());
                session.EnqueueMessage(ZoneMessageOpcode.AreaDefinitionSetDefinitions, Convert.FromHexString("000000000000000000000000000000"));
                // The new voxel engine only starts streaming once it has the cell manifest (TerrainCell 0x0A sets its
                // "manifest received" flag, core +0x5a75, via FUN_143ed1000), so send it again.
                Game.Terrain.TerrainStore.SendManifest(session);
                Game.PlayerManager.Reintroduce(session);
                Game.Teleport.FinishRezone(session);
                return;
            }

            session.ReadySent = true;
            // Tint definitions: the client builds its tint pickers and the character-create / barbershop option lists
            // when this arrives (FUN_142c6ec20); the lists come from its own files, so eight empty lists will do.
            session.EnqueueMessage(ZoneMessageOpcode.ArtDataTintDefinitionManagerData, new byte[8 * 4]);
            Game.Palettes.Send(session);
            // T-KIRMMIN-WIRE-TIMING (logging only)
            session.MarkClientIsReadyWireTiming();
            session.EnqueueMessage(ZoneMessageOpcode.QuickChatBase, Convert.FromHexString("01-00-00-00-00-00".Replace("-", "")));
            session.EnqueueMessage(new ClientUpdateDoneSendingPreloadCharacters());
            session.SendCalculatedStats();
            SendTravelCasterInfo(session);
            foreach ((uint equipSlot, var item) in session.VisibleEquipment())
                session.EnqueueMessage(session.BuildEquipmentSlot(equipSlot, item));
            // Achievement
            // Achievement
            Game.Achievements.SendInitial(session);
            session.EnqueueMessage(ZoneMessageOpcode.ActivityManagerBase, Convert.FromHexString("01-00-00-00-00".Replace("-", "")));
            // EffectTagCompositeEffectsEnable (PlayerUpdate 0x1F, reader FUN_142bb2520: one bool after the sub byte; the
            // message must be consumed exactly, so the old 4-byte opcode + captured "00 00 01" was dropped).
            session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateEffectTagCompositeEffectsEnable, new byte[] { 1 });
            //session.EnqueueMessage((ZoneMessageOpcode)0x29002E00, Convert.FromHexString("91-06-20-72-AE-1B-56-4B-09-00-00-00-01-00-00-00-00-00-00-00-01-00-00-00-02-00-00-00-00-00-00-00-02-00-00-00-03-00-00-00-00-00-00-00-B3-B1-08-00-04-00-00-00-00-00-00-00-58-B2-08-00-05-00-00-00-00-00-00-00-59-B2-08-00-06-00-00-00-00-00-00-00-5A-B2-08-00-07-00-00-00-00-00-00-00-5B-B2-08-00-08-00-00-00-00-00-00-00-5C-B2-08-00-09-00-00-00-00-00-00-00-5D-B2-08-00".Replace("-", "")));
            //session.EnqueueMessage(ZoneMessageOpcode.ClientUpdateUpdateCalculatedStats, Convert.FromHexString("91-06-20-72-AE-1B-56-4B-2F-00-00-00-01-00-00-00-00-00-E1-44-02-00-00-00-00-00-00-00-03-00-00-00-00-00-00-00-04-00-00-00-00-00-00-00-05-00-00-00-00-00-40-41-06-00-00-00-00-00-00-00-07-00-00-00-00-00-00-00-08-00-00-00-00-00-00-00-09-00-00-00-00-00-00-00-0A-00-00-00-00-00-8C-42-0B-00-00-00-00-00-00-00-0C-00-00-00-00-00-00-00-0D-00-00-00-00-00-00-00-0E-00-00-00-00-00-00-00-0F-00-00-00-00-00-00-00-10-00-00-00-00-00-00-00-11-00-00-00-00-00-00-00-12-00-00-00-00-00-F0-41-13-00-00-00-00-00-70-42-14-00-00-00-00-00-20-42-15-00-00-00-00-00-00-40-16-00-00-00-00-00-C0-40-17-00-00-00-00-00-00-00-18-00-00-00-00-00-20-43-19-00-00-00-00-00-00-00-1A-00-00-00-00-00-00-00-1B-00-00-00-00-00-00-00-1C-00-00-00-00-00-80-3F-1D-00-00-00-00-00-00-00-1E-00-00-00-00-00-00-00-23-00-00-00-00-00-00-00-24-00-00-00-00-00-00-00-25-00-00-00-00-00-00-00-26-00-00-00-00-00-00-00-27-00-00-00-00-00-00-00-28-00-00-00-00-00-00-00-29-00-00-00-00-00-00-00-2A-00-00-00-00-00-00-00-2B-00-00-00-00-00-00-00-2C-00-00-00-00-00-00-00-34-00-00-00-00-00-00-00-35-00-00-00-00-00-00-00-36-00-00-00-00-00-48-42-37-00-00-00-00-00-00-00-39-00-00-00-00-00-00-00-3C-00-00-00-00-00-00-00-3E-00-00-00-00-00-00-00".Replace("-", "")));
            //session.EnqueueMessage(ZoneMessageOpcode.ClientUpdateUpdateStat, Convert.FromHexString("25-00-00-00-01-00-00-00-00-08-07-00-00-00-00-00-00-03-00-00-00-01-66-66-26-3F-00-00-00-00-04-00-00-00-01-00-00-80-3E-00-00-00-00-05-00-00-00-01-00-00-80-3F-00-00-00-00-06-00-00-00-01-00-00-80-3F-00-00-00-00-07-00-00-00-01-00-00-40-3F-00-00-00-00-08-00-00-00-01-CD-CC-CC-3D-00-00-00-00-09-00-00-00-01-CD-CC-4C-3E-00-00-00-00-0A-00-00-00-01-CD-CC-CC-3D-00-00-00-00-0B-00-00-00-00-B8-0B-00-00-00-00-00-00-0C-00-00-00-01-00-00-00-40-00-00-00-00-0D-00-00-00-00-FA-00-00-00-00-00-00-00-0E-00-00-00-01-00-00-80-3F-00-00-00-00-0F-00-00-00-01-00-00-00-00-00-00-00-00-10-00-00-00-01-00-00-00-00-00-00-00-00-15-00-00-00-01-CD-CC-4C-3E-00-00-00-00-16-00-00-00-01-9A-99-19-3E-00-00-00-00-17-00-00-00-01-00-00-40-42-00-00-00-00-18-00-00-00-01-CD-CC-4C-3E-00-00-00-00-19-00-00-00-01-CD-CC-4C-3E-00-00-00-00-1A-00-00-00-01-CD-CC-4C-3E-00-00-00-00-1B-00-00-00-01-9A-99-19-3E-00-00-00-00-1C-00-00-00-01-9A-99-19-3E-00-00-00-00-1D-00-00-00-01-9A-99-19-3E-00-00-00-00-20-00-00-00-01-00-00-00-00-00-00-00-00-21-00-00-00-01-00-00-00-00-00-00-00-00-22-00-00-00-01-00-00-80-3F-00-00-00-00-25-00-00-00-01-00-00-80-3F-00-00-00-00-26-00-00-00-01-00-00-00-00-00-00-00-00-27-00-00-00-01-00-00-00-00-00-00-00-00-28-00-00-00-01-00-00-80-3F-00-00-00-00-2A-00-00-00-01-00-00-00-00-00-00-00-00-2B-00-00-00-01-00-00-00-00-00-00-00-00-2C-00-00-00-01-00-00-00-00-00-00-00-00-2D-00-00-00-01-00-00-00-00-00-00-00-00-2E-00-00-00-01-00-00-00-00-00-00-00-00-2F-00-00-00-01-00-00-00-00-00-00-00-00".Replace("-", "")));
            //session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateUpdateCharacterStateDelta, Convert.FromHexString("00-00-91-06-20-72-AE-1B-56-4B-00-00-00-00-00-00-00-00-01-00-00-00-00-10-00-00-00-00-00-00-00-00-00-00-B7-5D-D4-BB".Replace("-", "")));
            // Removed (2026-10-11): captured ResourcesBase sub 3 / sub 1 and EquipmentBase sub 1 bodies. Each names the
            // live capture's character (guid 0x4B561BAE72200691); the client applies them only to that entity / the local
            // player (FUN_143568b00, FUN_143568e40, FUN_142da4b10), so they were ignored. Real values come from
            // CharacterResources (sub 2) and the equipment slot updates.
            session.EnqueueMessage(ZoneMessageOpcode.LoadoutsBase, Convert.FromHexString("01-03-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-2E-00-00-00-0F-00-00-00-44-65-66-61-75-6C-74-20-4C-6F-61-64-6F-75-74-00-00-00-00-04-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-05-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-10-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00".Replace("-", "")));
            Game.Emotes.SendCommandList(session);
            session.EnqueueMessage(ZoneMessageOpcode.InventoryBase, Convert.FromHexString("18-00-01".Replace("-","")));

            session.EnqueueMessage(new ZoneDoneSendingInitialData());
            // Zone Details are sent a second time for some reason in logs.
            session.EnqueueMessage(ZoneMessageOpcode.SendZoneDetails, Game.Landscapes.ZoneDetails(session.ZoneId));
            session.EnqueueMessage(new ClientUpdateZonePopulation());


            // (A captured "LoadWelcomeScreen" 0x60 body was sent here. 0.589 has no handler for 0x60: it falls past the
            // zone dispatcher's table to the fallback listeners, which all refuse it (probe secdispatch.js, 2026-10-12).
            // Dropped to save the bytes.)
            session.EnqueueMessage(ZoneMessageOpcode.ExperienceBase, Convert.FromHexString("02-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-0F-0D-01-00-00-00-00-00-24-07-00-00-00-00-00-00-73-0D-01-00-00-00-00-00-5C-06-00-00-00-00-00-00-AB-0C-01-00-00-00-00-00-C0-06-00-00-00-00-00-00".Replace("-", "")));
            Game.Props.SendTintList(session);
            session.EnqueueMessage(ZoneMessageOpcode.ZoneCoordinateInfo, Convert.FromHexString("00-00-00-00-18-66-78-41-00-00-00-00-00-88-D3-40-00-00-00-40-55-61-78-41-00-40-9C-47-00-40-1C-47-00-40-9C-47-00-00-00-00-00-00-00-00-00-00-00-00-00-00-7A-C5-00-00-FA-C4-00-00-7A-C5-00-00-80-3F-00-00-7A-45-00-00-FA-44-00-00-7A-45-00-00-80-3F".Replace("-","")));
            session.EnqueueMessage(ZoneMessageOpcode.WorldDisplayInfo, Convert.FromHexString("00-00-00-00".Replace("-","")));
            session.EnqueueMessage(ZoneMessageOpcode.ClientUpdateMovementVersion, Convert.FromHexString("01".Replace("-","")));
            // ClientSettings (0xE9, reader FUN_142bb06d0): string help URL (+0x10, Lua Ui.OpenSupportUrl), string shop URL
            // (+0x28), string shop URL (+0x40), u32 1, bool. The capture this replaced still pointed at PlanetSide 2's
            // site; they now lead to this server's own pages (Game.Store / WebPages).
            {
                var settings = new List<byte>();
                var w = new GamePacketWriter(settings);
                w.Write($"{Game.Store.BaseUrl}/#!/help");
                w.Write($"{Game.Store.BaseUrl}/#!/marketplace");
                w.Write($"{Game.Store.BaseUrl}/#!/marketplace");
                w.Write(1u);
                w.Write(false);
                session.EnqueueMessage(ZoneMessageOpcode.ClientSettings, settings.ToArray());
            }


            ////session.EnqueueMessage((ZoneMessageOpcode)0x29000600, Convert.FromHexString("25-00-00-00-01-00-00-00-00-08-07-00-00-00-00-00-00-03-00-00-00-01-66-66-26-3F-00-00-00-00-04-00-00-00-01-00-00-80-3E-00-00-00-00-05-00-00-00-01-00-00-80-3F-00-00-00-00-06-00-00-00-01-00-00-80-3F-00-00-00-00-07-00-00-00-01-00-00-40-3F-00-00-00-00-08-00-00-00-01-CD-CC-CC-3D-00-00-00-00-09-00-00-00-01-CD-CC-4C-3E-00-00-00-00-0A-00-00-00-01-CD-CC-CC-3D-00-00-00-00-0B-00-00-00-00-B8-0B-00-00-00-00-00-00-0C-00-00-00-01-00-00-00-40-00-00-00-00-0D-00-00-00-00-FA-00-00-00-00-00-00-00-0E-00-00-00-01-00-00-80-3F-00-00-00-00-0F-00-00-00-01-00-00-00-00-00-00-00-00-10-00-00-00-01-00-00-00-00-00-00-00-00-15-00-00-00-01-CD-CC-4C-3E-00-00-00-00-16-00-00-00-01-9A-99-19-3E-00-00-00-00-17-00-00-00-01-00-00-40-42-00-00-00-00-18-00-00-00-01-CD-CC-4C-3E-00-00-00-00-19-00-00-00-01-CD-CC-4C-3E-00-00-00-00-1A-00-00-00-01-CD-CC-4C-3E-00-00-00-00-1B-00-00-00-01-9A-99-19-3E-00-00-00-00-1C-00-00-00-01-9A-99-19-3E-00-00-00-00-1D-00-00-00-01-9A-99-19-3E-00-00-00-00-20-00-00-00-01-00-00-00-00-00-00-00-00-21-00-00-00-01-00-00-00-00-00-00-00-00-22-00-00-00-01-00-00-80-3F-00-00-00-00-25-00-00-00-01-00-00-80-3F-00-00-00-00-26-00-00-00-01-00-00-00-00-00-00-00-00-27-00-00-00-01-00-00-00-00-00-00-00-00-28-00-00-00-01-00-00-80-3F-00-00-00-00-2A-00-00-00-01-00-00-00-00-00-00-00-00-2B-00-00-00-01-00-00-00-00-00-00-00-00-2C-00-00-00-01-00-00-00-00-00-00-00-00-2D-00-00-00-01-00-00-00-00-00-00-00-00-2E-00-00-00-01-00-00-00-00-00-00-00-00-2F-00-00-00-01-00-00-00-00-00-00-00-00".Replace("-", "")));
            // Our own manifest (cells this server changed); the live capture's cells have no data here.
            Game.Terrain.TerrainStore.SendManifest(session);
            session.EnqueueMessage(ZoneMessageOpcode.AreaDefinitionSetDefinitions, Convert.FromHexString("00-00-00-00-00-00-00-00-00-00-00-00-00-00-00".Replace("-", "")));
            ////session.EnqueueMessage(ZoneMessageOpcode.PreferencePacket, Convert.FromHexString("01-00-00-00-8E-00-00-00-3C-52-6F-6F-74-20-56-65-72-73-69-6F-6E-3D-22-31-2E-30-22-3E-3C-57-6F-72-6C-64-2F-3E-3C-5A-6F-6E-65-2F-3E-3C-43-6C-69-65-6E-74-3E-3C-55-49-3E-3C-43-68-61-74-3E-3C-43-68-61-74-57-69-6E-64-6F-77-73-20-49-73-4C-69-73-74-3D-22-31-22-2F-3E-3C-2F-43-68-61-74-3E-3C-41-63-74-69-6F-6E-42-61-72-20-41-72-65-41-63-74-69-6F-6E-42-61-72-73-4C-6F-63-6B-65-64-3D-22-30-22-2F-3E-3C-2F-55-49-3E-3C-2F-43-6C-69-65-6E-74-3E-3C-2F-52-6F-6F-74-3E".Replace("-", "")));
            ////session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateSetPlayerBio, Convert.FromHexString("00-00-00-00-00-00".Replace("-", "")));
            ////session.EnqueueMessage(ZoneMessageOpcode.MailBase, Convert.FromHexString("0F-00-00-00-01-00".Replace("-", "")));
            // The 0.695 prop definition list (354 KB). The 0.589 client has no reader for it (its PropBase 0x0A is
            // a different packet), so it is only sent to newer clients; the server reads it for prop models.
            if (!ZoneOpcodeTranslator.Uses589Opcodes(session.ClientBuild))
            {
                var propBasePath = Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "145-PropBase");
                session.EnqueueMessage(ZoneMessageOpcode.PropBase, File.ReadAllBytes(propBasePath).AsSpan().Slice(2).ToArray());
            }
            session.EnqueueMessage(ZoneMessageOpcode.ActivityBase, Convert.FromHexString("01-01-02-00-00-00-00-00-00-00".Replace("-", "")));
        }

        /// <summary>
        /// ClientFinishedLoading: the loading screen is gone. Other players see this one from its next position update.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClientFinishedLoading)]
        public static void ClientFinishedLoading(WorldSession session, ClientFinishedLoading clientFinishedLoading)
        {
            // Only now is the client's world ready to show other players. They're added on the first position update,
            // so everyone else spawns this player where they actually are.
            session.WorldReady = true;
        }

        /// <summary>
        /// Synchronization (0xC3): the client's clock sync request. Answered with one shared server millisecond clock
        /// (<see cref="Game.ServerClock"/>) so movement timestamps mean the same on every client.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.Synchronization)]
        public static void Sychronization(WorldSession session, Synchronization synchronization)
        {
            if (synchronization.Unknown0)
                return;

            // One server clock for everyone, so movement timestamps from one client mean the same to the others.
            ulong now = Game.ServerClock.Milliseconds;
            session.EnqueueMessage(new Synchronization
            {
                ClientSendTime = synchronization.ClientSendTime,
                Time2 = synchronization.Time2,
                ClientTime = synchronization.ClientTime,
                ServerTime = now,
                ServerTime2 = now,
                Time3 = synchronization.ClientTime
            });
        }

        // Live-captured CasterInfoReply bodies (lightstone 221 and others); other abilities are built from item data.
        private static readonly Dictionary<uint, string> capturedCasterInfo = new()
        {
            [221] = "DD-00-00-00-5F-5E-B0-DF-02-00-00-00-00-00-00-00-00-00-00-00-00-00-00-F0-41-00-00-00-00-00-00-00-00-00-00-00-00-F7-89-08-00-00-00-00-F0-41-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-DD-00-00-00-01-01-00-00-00-00-F5-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-4B-98-08-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00",
            [1098] = "4A-04-00-00-5F-5E-B0-DF-02-00-00-00-00-00-00-00-01-00-00-00-00-00-00-70-42-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-4A-04-00-00-01-01-00-00-80-3F-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00",
            [326] = "46-01-00-00-5F-5E-B0-DF-02-00-00-00-00-00-00-00-00-00-00-00-00-00-00-F0-41-00-00-00-00-00-00-00-00-00-00-00-00-F7-89-08-00-00-00-00-F0-41-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-DD-00-00-00-01-01-00-00-00-00-F5-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-4B-98-08-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00",
            [1122] = "62-04-00-00-5F-5E-B0-DF-02-00-00-00-00-00-00-00-00-00-00-00-00-00-00-F0-41-00-00-00-00-00-00-00-00-00-00-00-00-F7-89-08-00-00-00-00-F0-41-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-DD-00-00-00-01-01-00-00-00-00-F5-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-4B-98-08-00-01-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00",
        };

        static MiscHandler()
        {
            // 186: the build-mode tool's default ability (asked for every second inside a claim). Not in the client's
            // tables; answered with the 1098 layout (60 m range) until its real values are known.
            string template = capturedCasterInfo[1098];
            capturedCasterInfo[186] = "BA-00-00-00" + template.Substring(11).Replace("4A-04-00-00", "BA-00-00-00");
            // 185: Travel → Spire, same layout.
            capturedCasterInfo[185] = "B9-00-00-00" + template.Substring(11).Replace("4A-04-00-00", "B9-00-00-00");
            // 187: Travel → My Friends.
            capturedCasterInfo[187] = "BB-00-00-00" + template.Substring(11).Replace("4A-04-00-00", "BB-00-00-00");
        }

        /// <summary>
        /// CasterInfoRequest (Abilities 0x27 sub 2 {u32 ability}): the client won't use an ability (tools, weapons, travel)
        /// until it knows its range, cast time and costs. Answered from captured bodies (lightstone, campfire, travel),
        /// from the held item's ability data, for weapon combos with a short melee template, or with the generic layout.
        /// The client keeps asking until it gets an answer.
        /// </summary>
        /// <summary>
        /// Sends the travel abilities' caster info unasked (185 Spire, 186 own site, 187 a friend). The client asks for
        /// 185/186 itself from the Travel window, but the friends right-click menu's "Teleport to Friend"
        /// (Lua Ui.TeleportToFriend) only casts 187 when its caster info is already known, and never requests it.
        /// </summary>
        public static void SendTravelCasterInfo(WorldSession session)
        {
            foreach (uint ability in Game.Abilities.TravelAbilities)
                session.EnqueueMessage(ZoneMessageOpcode.AbilityCasterInfoReply,
                    Convert.FromHexString(capturedCasterInfo[ability].Replace("-", "")));
        }

        /// <summary>
        /// Names for weapon combo abilities, which no item lists and the client's ability tables don't name (tooltips said
        /// "Ability Name Not Found"), by the button they're cast with (<see cref="Game.WeaponAttacks.KindOf"/>, from
        /// InputActionGroupEntries): basic and its combo stages (760, 816...) "Basic Attack" 565568, power and its stages
        /// (761, 1181, 1182...) "Power Attack" 565569, the special / E attack "Tertiary" 565570 (the client's attack-slot
        /// names next to them in the text table).
        /// </summary>
        private static uint ComboName(uint ability) => Game.WeaponAttacks.KindOf(ability) switch
        {
            Game.WeaponAttacks.Kind.Basic or Game.WeaponAttacks.Kind.BasicCombo => 565568,
            Game.WeaponAttacks.Kind.Power or Game.WeaponAttacks.Kind.PowerCombo => 565569,
            Game.WeaponAttacks.Kind.Special => 565570,
            _ => 0
        };

        /// <summary>
        /// A weapon cast id's caster info for the held weapon: short cast and recovery so combos chain, the attack's
        /// Energy cost (resource type 2, Mana: the client checks it itself before casting, "AbilityCastError.NoMana",
        /// code 3), and the weapon's own attack name and description (Combat.WeaponCombat.Describe: e.g. "Basic Attack:
        /// Shatter" for the Staff of Frozen Sorrow; Frenzy shortens the cast, Supercharge raises the cost). The client
        /// overwrites its cached entry with every reply (FUN_142d7a7d0), so this is sent again when the weapon or those
        /// states change.
        /// </summary>
        public static AbilityCasterInfoReply WeaponAttackInfo(WorldSession session, Game.Inventory.ItemDefinitionManager.ItemDefinition weapon,
            uint abilityId)
        {
            var reply = Game.Abilities.BuildItemAbilityInfo(weapon,
                new Game.Inventory.ItemDefinitionManager.ItemAbility(abilityId, 0, "", ""));
            reply.CastTime = 0.4f;
            reply.RecoveryTime = 0.1f;
            reply.MaxRange = 4f;
            uint cost = Game.WeaponAttacks.EnergyCost(abilityId);
            if (cost > 0)
            {
                reply.CostResource = 2;
                reply.CostAmount = cost;
                reply.IgnoreCostPool = false;
            }
            if (ComboName(abilityId) is uint comboName and not 0)
            {
                reply.NameId = comboName;
                reply.DescriptionId = 0;
            }
            Game.Combat.WeaponCombat.Describe(session, weapon, abilityId, reply);
            return reply;
        }

        [ZoneMessageHandler(ZoneMessageOpcode.AbilityFinishChargedCast)]
        public static void HandleFinishChargedCast(WorldSession session, AbilityFinishChargedCast finish)
        {
            log.Debug($"FinishChargedCast from {session.CharacterName}: {BitConverter.ToString(finish.Body)}");
            Game.Combat.WeaponCombat.FinishCharge(session, finish);
        }

        [ZoneMessageHandler(ZoneMessageOpcode.AbilityCancelCast)]
        public static void HandleCancelCast(WorldSession session, AbilityCancelCast cancel)
        {
            log.Debug($"CancelCast from {session.CharacterName}: {BitConverter.ToString(cancel.Body)}");
            Game.Combat.WeaponCombat.CancelCharge(session);
        }

        [ZoneMessageHandler(ZoneMessageOpcode.AbilityCasterInfoRequest)]
        public static void AbilityCasterInfoRequest(WorldSession session, AbilityCasterInfoRequest abilityRequest)
        {
            uint abilityId = abilityRequest.Unknown0;
            if (abilityId == Game.Abilities.HeroicMovementAbility)
            {
                // Shift (input action HeroicMovement) casts 326. The captured reply under that id carried the Lightstone's
                // name and long timings, so the client refused every press with "ability on cooldown": answer it as an
                // instant ability with no cast, recovery or cost (the Energy check is the client's own, see
                // CharacterResources).
                var heroic = Game.Abilities.BuildItemAbilityInfo(
                    new Game.Inventory.ItemDefinitionManager.ItemDefinition { NameId = Game.Abilities.HeroicMovementName },
                    new Game.Inventory.ItemDefinitionManager.ItemAbility(abilityId, 0, "", ""));
                heroic.CastTime = 0f;
                heroic.RecoveryTime = 0f;
                heroic.Ua0 = 0;
                session.EnqueueMessage(heroic);
                return;
            }
            if (capturedCasterInfo.TryGetValue(abilityId, out string hex))
            {
                session.EnqueueMessage(ZoneMessageOpcode.AbilityCasterInfoReply, Convert.FromHexString(hex.Replace("-", "")));
                return;
            }

            var source = Game.Inventory.ItemDefinitionManager.FindAbility(abilityId);
            if (source == null)
            {
                // Weapon combo abilities (e.g. 760/761/816/1181/1182 for axes) aren't listed on items and the client's
                // AbilityEx table is empty (server data). With a weapon held, answer like an item ability of that
                // weapon (short cast, quick recovery, so a combo can chain); otherwise the 1098 layout under the id.
                if (Game.Abilities.HeldWeapon(session) is { } weapon)
                {
                    session.EnqueueMessage(WeaponAttackInfo(session, weapon, abilityId));
                    return;
                }
                if (ComboName(abilityId) is uint name and not 0)
                {
                    // Hovering a weapon in the bag: the tooltip lists its combo abilities by these names.
                    var named = Game.Abilities.BuildItemAbilityInfo(new Game.Inventory.ItemDefinitionManager.ItemDefinition { NameId = name },
                        new Game.Inventory.ItemDefinitionManager.ItemAbility(abilityId, 0, "", ""));
                    named.CastTime = 0.4f;
                    named.RecoveryTime = 0.1f;
                    named.MaxRange = 4f;
                    session.EnqueueMessage(named);
                    return;
                }
                string id = BitConverter.ToString(BitConverter.GetBytes(abilityId));
                string generic = id + capturedCasterInfo[1098].Substring(11).Replace("4A-04-00-00", id);
                session.EnqueueMessage(ZoneMessageOpcode.AbilityCasterInfoReply, Convert.FromHexString(generic.Replace("-", "")));
                log.Info($"Caster info for ability {abilityId} answered with the generic layout.");
                return;
            }
            session.EnqueueMessage(Game.Abilities.BuildItemAbilityInfo(source.Value.Item, source.Value.Ability));
        }

        /// <summary>
        /// AbilityCastAttempt (Abilities sub 1): a tool swing, a consumable use, a travel cast or a weapon attack. Every cast
        /// must be answered (see <see cref="Game.Abilities.HandleCast"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityCastAttempt)]
        public static void AbilityCastAttempt(WorldSession session, AbilityCastAttempt attempt)
        {
            Game.Abilities.HandleCast(session, attempt);
        }

        /// <summary>
        /// SetPlayerBio: answered with an empty bio.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateSetPlayerBio)]
        public static void PlayerSetPlayerBio(WorldSession session, PlayerUpdateSetPlayerBio playerBio)
        {
            session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateSetPlayerBio, Convert.FromHexString("00-00-00-00-00-00".Replace("-", "")));
        }

        /// <summary>
        /// AbilityDefaultInfoRequest: answered with an empty list.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityDefaultInfoRequest)]
        public static void AbilityDefaultInfoRequest(WorldSession session, AbilityDefaultInfoRequest abilityDefaultRequest)
        {
            session.EnqueueMessage(ZoneMessageOpcode.AbilityDefaultInfoReply, Convert.FromHexString("00-00-00-00".Replace("-", "")));
        }
    }
}
