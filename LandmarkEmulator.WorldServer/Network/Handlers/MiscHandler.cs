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

        /// <summary>AbilityLanding 0x27/0x1D (see <see cref="AbilityLanding"/>): logged only (L7).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityAbilityLanding)]
        public static void HandleAbilityLanding(WorldSession session, AbilityLanding landing)
            => log.Trace($"{session.CharacterName} ability landing: 0x{landing.First:X} 0x{landing.Second:X}");

        /// <summary>The character controller's contact report (see <see cref="AbilityCollisionReport"/>); logged only.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityCollisionReport)]
        public static void HandleCollisionReport(WorldSession session, AbilityCollisionReport report)
            => log.Trace($"{session.CharacterName} collision report: {string.Join(", ", report.Entities.Select(e => $"0x{e:X}"))}");

        /// <summary>The client's periodic keep-alive {u32 game time}. Receiving it is the point (the session's
        /// timeout clock resets on any packet); no reply, to save bandwidth.</summary>
        /// <summary>The client's NPC height corrections (0x114): see Creatures.CorrectHeights.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.CorrectNpcHeights)]
        public static void HandleCorrectNpcHeights(WorldSession session, CorrectNpcHeights heights)
            => Game.Creatures.CorrectHeights(session, heights.Guids, heights.Heights);

        /// <summary>MountBase (0xA1) from the client: DismountRequest at an encounter's start; no mounts in Landmark, no reply.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.MountBase)]
        public static void HandleMount(WorldSession session, MountRequest request)
            => log.Debug($"{session.CharacterName}: mount packet sub {request.SubOpcode} ignored (no mounts).");

        [ZoneMessageHandler(ZoneMessageOpcode.KeepAlive)]
        public static void HandleKeepAlive(WorldSession session, KeepAlive keepAlive)
            => session.NoteClientClock(keepAlive.GameTime);

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
            {
                Network.OpcodeStats.Unanswered("UI", request.SubOpcode);
                log.Debug($"Unhandled UI request 0x{request.SubOpcode:X2}: {BitConverter.ToString(request.Body)}");
            }
            else if (request.Body.Length >= 4)
            {
                uint uiEvent = BitConverter.ToUInt32(request.Body, 0);
                Game.Achievements.UiEvent(session, uiEvent);
                Game.WorldChannel.WorldChannelServices.Commerce.UiEvent(session, uiEvent);
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
        public static void HandleMail(WorldSession session, MailRequest request) => Game.Social.SocialServices.Service.Mail(session, request);

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

        /// <summary>FotomatBase: portrait / headshot requests (see <see cref="Game.Portraits"/>; the commerce back end's).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.FotomatBase)]
        public static void HandleFotomat(WorldSession session, FotomatRequest message)
            => Game.WorldChannel.WorldChannelServices.Commerce.PortraitRequest(session, message.Body);

        /// <summary>AddClientPortraitCrc (0x75 {u32}): the checksum of the portrait the client is about to upload.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AddClientPortraitCrc)]
        public static void HandlePortraitCrc(WorldSession session, AddClientPortraitCrc message)
            => Game.WorldChannel.WorldChannelServices.Commerce.PortraitCrc(session, message.Crc);

        /// <summary>SetLocale (0x5B, World channel): kept by the world-channel back end.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.SetLocale)]
        public static void HandleSetLocale(WorldSession session, SetLocale message)
            => Game.WorldChannel.WorldChannelServices.WorldChannel.SetLocale(session, message.Body);

        /// <summary>
        /// RatingBase 0xA7 (u8 sub). 0x1D RequestContestsRequest (Lua RequestContests, sent often): answered with
        /// 0x1E RequestContestsReply {u64, u32 count, entries} (reader FUN_142d09110, which fills the Contests data
        /// source; see <see cref="Game.Contests"/>; the commerce back end's). 8 AddVote, 0x18 CanVote:
        /// <see cref="Game.Ratings"/> (the world-channel back end's claim votes). Others are logged.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.RatingBase)]
        public static void HandleRating(WorldSession session, RatingRequest message)
        {
            if (message.Body.Length == 0)
                return;
            if (message.Body[0] == 0x1D)
                Game.WorldChannel.WorldChannelServices.Commerce.ContestList(session);
            else if (message.Body[0] == 0x08)
                Game.WorldChannel.WorldChannelServices.WorldChannel.ClaimVote(session, message.Body);
            else if (message.Body[0] == 0x18)
                Game.WorldChannel.WorldChannelServices.WorldChannel.CanVote(session, message.Body);
            else
                log.Info($"Rating request from {session.CharacterName}: {BitConverter.ToString(message.Body)}");
        }

        /// <summary>
        /// PlayerUpdate ClientLoading 0x6C {u8}: GameStateLoading's enter sends 1 (FUN_1436abc90; not at login, where the
        /// connection doesn't exist yet), its exit 0 (FUN_1436abd00) when the loading screen drops [P]. The first 0 of a
        /// login shows this player and the others to each other (<see cref="Game.PlayerManager.LoadingScreenLeft"/>).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateLoading)]
        public static void HandleLoading(WorldSession session, PlayerUpdateLoading message)
        {
            if (!PlayerUpdateLoading.LeftLoading(message.Body) || session.LoadingScreenDown)
                return;
            session.LoadingScreenDown = true;
            Game.PlayerManager.LoadingScreenLeft(session);
        }

        /// <summary>
        /// Zone family 0xDD (<see cref="ClientPcDataRequest"/>): sub 4 PlayerFilterUpdatePacket changes the saved bag
        /// filters (<see cref="Game.InventoryFilters"/>; no reply, the client has none). Subs 1 SetSpeechPack and 2
        /// GetSpeechPackList are logged (not built yet).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClientPcDataBase)]
        public static void HandleClientPcData(WorldSession session, ClientPcDataRequest message)
        {
            if (message.SubOpcode == ClientPcDataRequest.PlayerFilterUpdate)
                Game.InventoryFilters.Handle(session, message.Body);
            else
                log.Debug($"{session.CharacterName}: 0xDD sub {message.SubOpcode} ({message.Body.Length} B) not handled.");
        }

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
                Paused = false
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
        /// <summary>
        /// The end of a re-zone: the flags WaitForConfirmationPacket waits on, then the landscape's players, sites and
        /// props, and the arrival.
        /// </summary>
        /// <summary>How long a re-zone onto a Spire holds the zone-done flags while the deck's bodies build (see ClientIsReady).</summary>
        private const int SpireBodyMs = 6000;

        /// <summary>How long a re-zone into the air over placed props holds the flags (bodies took ≤2.9 s; ours).</summary>
        private const int PropBodyMs = 4000;

        /// <summary>Sideways reach (m) of the props sent ahead of the flags on an arrival over props (64 m down, 16 m up)
        /// [I ours: the structure the player lands on, with margin].</summary>
        private const float EarlyPropReach = 48f;

        /// <summary>
        /// Does the player arrive at least 1.5 m above the landscape's ground with a placed prop under them (within 4 m
        /// sideways, 12 m down)? Then they would stand on that prop, which has no body yet.
        /// </summary>
        private static bool ArrivesOverProps(WorldSession session)
        {
            System.Numerics.Vector3 at = session.Position;
            if (Game.Terrain.TerrainHeights.HeightAt(at.X, at.Z, session.ZoneId) is not float ground || at.Y - ground < 1.5f)
                return false;
            return Game.Props.PlacedInBox(session.ZoneId, at - new System.Numerics.Vector3(4f, 12f, 4f),
                at + new System.Numerics.Vector3(4f, 0.5f, 4f)).Count > 0;
        }

        private static void FinishZoneIn(WorldSession session)
        {
            // The player can drop during an arrival hold (Spire / props, below); a dead session must not re-enter the
            // world (review 2026-10-18 #2).
            if (session.Disconnected)
                return;
            session.EnqueueMessage(new ClientUpdateDoneSendingPreloadCharacters());
            session.EnqueueMessage(new ZoneDoneSendingInitialData());
            // A session re-attached after a zone restart (WorldSession.OnReattach) joins the player list on its first
            // movement update, but an idle client sends none: it then stayed out of the list (invisible to others,
            // skipped by chat and commands) until it moved — seen 2026-10-16 on the split-stack test. Its re-zone
            // finishing here is the point it is back in the world.
            if (session.WorldReady && !Game.PlayerManager.Snapshot().Contains(session))
                Game.PlayerManager.Enter(session);
            Game.PlayerManager.Reintroduce(session);
            Game.Teleport.FinishRezone(session);
        }

        /// <summary>
        /// Runs <see cref="FinishZoneIn"/> after an arrival hold of <paramref name="ms"/>, for this re-zone only: when another
        /// re-zone has started since (<see cref="WorldSession.RezoneSerial"/>), the hold is stale and does nothing — its
        /// zone-done flags and Reintroduce would land in the middle of that load, which can hang the client
        /// (docs/terrain.md), and its FinishRezone would end the newer re-zone early (review 2026-10-18 #3).
        /// </summary>
        private static void HoldZoneIn(WorldSession session, int ms)
        {
            int serial = session.RezoneSerial;
            Game.Teleport.After(session, ms, () =>
            {
                if (session.RezoneSerial == serial)
                    FinishZoneIn(session);
                else
                    log.Info($"{session.CharacterName}: arrival hold of re-zone {serial} dropped (re-zone {session.RezoneSerial} started meanwhile).");
            });
        }

        [ZoneMessageHandler(ZoneMessageOpcode.ClientIsReady)]
        public static void ClientIsReady(WorldSession session, ClientIsReady clientIsReady)
        {
            if (session.ReadySent)
            {
                // Again after a re-zone (Game.Teleport.Rezone): zoning deleted every entity but the player (ClientBeginZoning,
                // FUN_142d1ccf0 case 8 → FUN_142d60340 [P]) and cleared three flags that WaitForConfirmationPacket
                // (FUN_142cc5120) waits on: preload done (ClientUpdateDoneSendingPreloadCharacters), initial zone data
                // complete (ZoneDoneSendingInitialData) and area definitions loaded.
                // Area definitions (SetDefinitions, 0xEA/1): the new landscape's trigger volumes (Linking Area boxes);
                // the client clears its list on it (Game.AreaDefinitions).
                Game.AreaDefinitions.SendAll(session);
                // The new voxel engine only starts streaming once it has the cell manifest (TerrainCell 0x0A sets its
                // "manifest received" flag, core +0x5a75, via FUN_143ed1000), so send it again.
                Game.Terrain.ZoneTerrain.SendManifest(session);
                // Each re-zone starts with no prop on the client (ClientBeginZoning deleted them; Teleport.RezoneNow forgot
                // the stream already): a hold whose FinishZoneIn never ran (Teleport.After drops it for a player who left)
                // must not leave props marked as sent (bandwidth audit R5).
                Game.Props.Forget(session);
                session.SpireSentEarly = false;
                // Arriving on a Spire: its props first, and the flags held SpireBodyMs, so the deck's collision bodies
                // exist before the client lets the player move. Frida, 2026-10-17 (re/hooks/physics, revisit1.jsonl):
                // the Spire props' bodies were built 2.7-4.4 s after the zone load (AddLightweightProp → model load →
                // FUN_1430aab50 / FUN_143d0c110 world add); where the landscape's terrain was already cached the client
                // dropped its loading screen sooner and the player fell through the not-yet-built deck (391), where the
                // terrain took longer the deck was ready (395 / 397 / 398) [P bodies and times; I that the hold covers a
                // slow PC]. The props are not sent again by Reintroduce (a duplicate guid is ignored, FUN_142f8a800).
                if (Game.Spire.Covers(session.ZoneId, session.Position.X, session.Position.Z))
                {
                    Game.Spire.Enter(session);
                    session.SpireSentEarly = true;
                    HoldZoneIn(session, SpireBodyMs);
                    return;
                }
                // The same for an arrival in the air over placed props (a Magical Platform, a walkway of props): the
                // re-zone deleted them and their bodies come back 2.5-2.9 s after the zone load (physics hook, round 71,
                // re/hooks/physics/logs/r71.jsonl: platform 0x20001CF and its docks) [P], while ClientIsReady lets the
                // player move at once, so a player put 1.5 m over a platform fell through it
                // (trace/0695/agent-platform-collision.md §3) [I strong]. The props go first and the flags wait.
                // Only those near the arrival (EarlyPropReach), not the whole landscape (~250 B a prop): the rest follow
                // the flags in Reintroduce, so the burst ahead of the flags can't push the deck's own props back in the
                // reliable queue during the hold (bandwidth audit 2026-10-18 R5).
                if (ArrivesOverProps(session))
                {
                    System.Numerics.Vector3 at = session.Position;
                    var near = Game.Props.PlacedInBox(session.ZoneId,
                        at - new System.Numerics.Vector3(EarlyPropReach, 64f, EarlyPropReach),
                        at + new System.Numerics.Vector3(EarlyPropReach, 16f, EarlyPropReach));
                    Game.Props.SendEarly(session, near);   // StreamIn (Reintroduce) skips them
                    HoldZoneIn(session, PropBodyMs);
                    return;
                }
                FinishZoneIn(session);
                return;
            }

            session.ReadySent = true;
            // Tint definitions: the client replaces its tint table with these lists (FUN_142c6ec20 → FUN_143c113c0) and
            // builds the character-create / barbershop option lists. Empty lists left every tint blank (Game/ArtTints).
            session.EnqueueMessage(ZoneMessageOpcode.ArtDataTintDefinitionManagerData, Game.ArtTints.Body);
            Game.Palettes.Send(session);
            // T-KIRMMIN-WIRE-TIMING (logging only)
            session.MarkClientIsReadyWireTiming();
            session.EnqueueMessage(ZoneMessageOpcode.QuickChatBase, Convert.FromHexString("01-00-00-00-00-00".Replace("-", "")));
            session.EnqueueMessage(new ClientUpdateDoneSendingPreloadCharacters());
            session.SendCalculatedStats();
            // The character stats live sent at login (movement tuning; Game.CharacterStats, L162).
            Game.CharacterStats.SendOwn(session);
            // Saved chat bubble colours (only when not the client's defaults; Game.ChatBubbleColor).
            Game.ChatBubbleColor.SendOwn(session);
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
                string showcase = Game.WorldChannel.WorldChannelServices.Commerce.ShowcaseUrl;
                w.Write($"{showcase}/#!/help");
                w.Write($"{showcase}/#!/marketplace");
                w.Write($"{showcase}/#!/marketplace");
                w.Write(1u);
                w.Write(false);
                session.EnqueueMessage(ZoneMessageOpcode.ClientSettings, settings.ToArray());
            }


            ////session.EnqueueMessage((ZoneMessageOpcode)0x29000600, Convert.FromHexString("25-00-00-00-01-00-00-00-00-08-07-00-00-00-00-00-00-03-00-00-00-01-66-66-26-3F-00-00-00-00-04-00-00-00-01-00-00-80-3E-00-00-00-00-05-00-00-00-01-00-00-80-3F-00-00-00-00-06-00-00-00-01-00-00-80-3F-00-00-00-00-07-00-00-00-01-00-00-40-3F-00-00-00-00-08-00-00-00-01-CD-CC-CC-3D-00-00-00-00-09-00-00-00-01-CD-CC-4C-3E-00-00-00-00-0A-00-00-00-01-CD-CC-CC-3D-00-00-00-00-0B-00-00-00-00-B8-0B-00-00-00-00-00-00-0C-00-00-00-01-00-00-00-40-00-00-00-00-0D-00-00-00-00-FA-00-00-00-00-00-00-00-0E-00-00-00-01-00-00-80-3F-00-00-00-00-0F-00-00-00-01-00-00-00-00-00-00-00-00-10-00-00-00-01-00-00-00-00-00-00-00-00-15-00-00-00-01-CD-CC-4C-3E-00-00-00-00-16-00-00-00-01-9A-99-19-3E-00-00-00-00-17-00-00-00-01-00-00-40-42-00-00-00-00-18-00-00-00-01-CD-CC-4C-3E-00-00-00-00-19-00-00-00-01-CD-CC-4C-3E-00-00-00-00-1A-00-00-00-01-CD-CC-4C-3E-00-00-00-00-1B-00-00-00-01-9A-99-19-3E-00-00-00-00-1C-00-00-00-01-9A-99-19-3E-00-00-00-00-1D-00-00-00-01-9A-99-19-3E-00-00-00-00-20-00-00-00-01-00-00-00-00-00-00-00-00-21-00-00-00-01-00-00-00-00-00-00-00-00-22-00-00-00-01-00-00-80-3F-00-00-00-00-25-00-00-00-01-00-00-80-3F-00-00-00-00-26-00-00-00-01-00-00-00-00-00-00-00-00-27-00-00-00-01-00-00-00-00-00-00-00-00-28-00-00-00-01-00-00-80-3F-00-00-00-00-2A-00-00-00-01-00-00-00-00-00-00-00-00-2B-00-00-00-01-00-00-00-00-00-00-00-00-2C-00-00-00-01-00-00-00-00-00-00-00-00-2D-00-00-00-01-00-00-00-00-00-00-00-00-2E-00-00-00-01-00-00-00-00-00-00-00-00-2F-00-00-00-01-00-00-00-00-00-00-00-00".Replace("-", "")));
            // Our own manifest (cells this server changed); the live capture's cells have no data here.
            Game.Terrain.ZoneTerrain.SendManifest(session);
            // Area definitions (SetDefinitions, 0xEA/1): the landscape's trigger volumes made with the Area Tool (Linking
            // Area boxes, TASKS 2.33); an empty list for older clients, as before (Game.AreaDefinitions).
            Game.AreaDefinitions.SendAll(session);
            // Client preferences (BasePreferencePacket 0x03, Game.Preferences), where the live capture had it: the stored
            // document, else live's default (01000000 8e000000 <Root Version="1.0">…, the captured body this line used to
            // hold commented out). The client loads it and runs ChatManager:RestoreSavedChatWindows; without it the client
            // never saves or sends its chat windows. Once per login: a re-zone keeps the client's own copy.
            Game.Preferences.SendOwn(session);
            ////session.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateSetPlayerBio, Convert.FromHexString("00-00-00-00-00-00".Replace("-", "")));
            ////session.EnqueueMessage(ZoneMessageOpcode.MailBase, Convert.FromHexString("0F-00-00-00-01-00".Replace("-", "")));
            // The prop definition list (PropBase 0xED sub 0x0A, 354 KB, captured from live): u8 sub, u64, bool (1: no
            // u32 / u64 follow), u32 n = 1982, n × 0x68-byte entries {str model, u32 prop id, str internal name, str name,
            // f32, f32, u32 image set, u32, u32 flag, u32 model id} (agent-leads-sweep.md L52). Both clients read it with
            // the same code — 0.695 FUN_142faa9b0 (dispatcher part_0079 :39031, case 10) and 0.589 FUN_142f0bc80
            // (FUN_142f1b980 case 10 under 0xED) — and the dump parses to its last byte with it [P], so it goes to both
            // (it was 0.695-only before, on the wrong belief that 0.589 had no reader). The server reads it for prop models.
            var propBasePath = Path.Combine(AppContext.BaseDirectory, "Resources", "PacketDumps", "145-PropBase");
            session.EnqueueMessage(ZoneMessageOpcode.PropBase, File.ReadAllBytes(propBasePath).AsSpan().Slice(2).ToArray());
            session.EnqueueMessage(ZoneMessageOpcode.ActivityBase, Convert.FromHexString("01-01-02-00-00-00-00-00-00-00".Replace("-", "")));
        }

        /// <summary>
        /// ClientFinishedLoading: WaitForWorldReady is done and the client runs (FUN_142cc3050, run state 0x10 → 0x12), but
        /// its loading screen stays up until the terrain around it is ready, 15-25 s more on the dev world (ClientLoading
        /// 0x6C {0}, <see cref="HandleLoading"/>) [P]. The player enters the world on its next position update; it and the
        /// other players are shown to each other once the screen drops (PlayerManager.LoadingScreenLeft).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.ClientFinishedLoading)]
        public static void ClientFinishedLoading(WorldSession session, ClientFinishedLoading clientFinishedLoading)
        {
            // The player is added on the first position update, so everyone else spawns this player where they
            // actually are.
            session.WorldReady = true;
            // A relog from build mode left the hotbar hidden until Tab: the client syncs its native build mode from
            // SendSelf (+0x286, off) at login (FUN_142c78ac0 -> FUN_1433bec40), which changes nothing when its tool
            // manager (kept across relogs) is already off, so the Lua HUD never hears HudStateManager:OnDisableBuildMode
            // and the ActionBar, resource bars and ability bar its OnEnableBuildMode hid stay hidden. Once per login
            // (not after a re-zone, where the player may still be building) the server runs that function itself; it
            // only shows those windows and hides the build mode widgets (UI/ScriptsBase.bin, re/tools/luadis.py).
            if (!session.BuildHudReset)
            {
                session.BuildHudReset = true;
                Game.ClientLua.RunCode(session, "if HudStateManager and HudStateManager.OnDisableBuildMode then HudStateManager:OnDisableBuildMode() end");
            }
        }

        /// <summary>
        /// Synchronization (0xC3): the client's clock sync request. Answered with one shared server millisecond clock
        /// (<see cref="Game.ServerClock"/>) so movement timestamps mean the same on every client.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.Synchronization)]
        public static void Sychronization(WorldSession session, Synchronization synchronization)
        {
            if (synchronization.IsReply)
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
                session.EnqueueMessage(ZoneMessageOpcode.AbilityCasterInfoReply, CapturedCasterInfo(ability));
        }

        /// <summary>Only 187's caster info again (the friend teleport cooldown changed at run time: dev "friendtpcooldown").</summary>
        public static void SendFriendTeleportCasterInfo(WorldSession session)
            => session.EnqueueMessage(ZoneMessageOpcode.AbilityCasterInfoReply, CapturedCasterInfo(Game.Abilities.FriendTeleportAbility));

        /// <summary>
        /// Where the reply's u32 / f32 +0x70 sits in the body: after u32 id +0x18, u32 kind +0x1c, u32 +0x20, u32 +0x24,
        /// bool +0x28, u32 +0x2c (0.695 reader FUN_142e016b0) [P]. The store FUN_142e187a0 copies it to the ability
        /// record's +0x14c, the cooldown in seconds.
        /// </summary>
        public const int CasterInfoCooldownAt = 21;

        /// <summary>
        /// A captured / derived caster info body. 187 (Travel to a friend) carries the friend teleport cooldown
        /// (Abilities.FriendTeleportCooldownSeconds) in +0x70 instead of the 1098 capture's 60 s: the client's own
        /// Ui.TeleportToFriend (FUN_1435c91f0) refuses while it runs and prints AbilityCastError.OnCooldown 562080
        /// itself (OPEN-LEADS L182; Abilities.FriendTeleportCooldownSeconds for the record fields). Null for an ability
        /// with no captured body.
        /// </summary>
        public static byte[] CapturedCasterInfo(uint ability)
        {
            if (!capturedCasterInfo.TryGetValue(ability, out string hex))
                return null;
            byte[] body = Convert.FromHexString(hex.Replace("-", ""));
            if (ability == Game.Abilities.FriendTeleportAbility)
                BitConverter.GetBytes((float)Game.Abilities.FriendTeleportCooldownSeconds).CopyTo(body, CasterInfoCooldownAt);
            return body;
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
            // The client ends the attack's animation with ActiveTimeEnd (selection 510, STOPS_CURRENT_SELECTION) one
            // cast time after the cast success (measured: 401 ms with 0.4 s, re/test/scenarios/adhoc/castanim). With
            // 0.4 s it stopped the strike ~50-130 ms in. The cast time is long enough for this weapon's latest strike
            // (Release at the selection's AttackTime) plus its motion; the recovery stays short so combos chain.
            reply.CastTime = Math.Max(0.4f, Game.WeaponAttacks.StrikeWindowSeconds(weapon, abilityId));
            reply.RecoveryTime = 0.1f;
            reply.MaxRange = 4f;
            if (Game.Combat.WeaponCombat.SendKeywords)
                reply.Keywords = Game.Combat.WeaponCombat.KeywordsOf(weapon, abilityId);
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
            if (!Game.MovementEffects.Finish(session, finish))
                Game.Combat.WeaponCombat.FinishCharge(session, finish);
        }

        [ZoneMessageHandler(ZoneMessageOpcode.AbilityCancelCast)]
        public static void HandleCancelCast(WorldSession session, AbilityCancelCast cancel)
        {
            log.Debug($"CancelCast from {session.CharacterName}: {BitConverter.ToString(cancel.Body)}");
            // A channel's key let go (sprint, Immediate Delivery, Endless Breeze: MovementEffects.Release), else a charge
            // given up (a leap, then a weapon's charged attack). The packet names no cast (one bool, FUN_142e0f5a0).
            if (!Game.MovementEffects.Release(session, cancel.Interrupted) && !Game.MovementEffects.Finish(session, null))
                Game.Combat.WeaponCombat.CancelCharge(session);
        }

        /// <summary>
        /// ClientTargeterFindTarget 0x27/0x24: a melee swing's client Sweep hit a character (L170); struck when the server's
        /// cone agrees (Game.Combat.WeaponCombat.Targeters). Nothing is sent back: the client waits for no answer [P].
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityClientTargeterFindTarget)]
        public static void HandleClientTargeterFindTarget(WorldSession session, AbilityClientTargeterFindTarget findTarget)
            => Game.Combat.WeaponCombat.SweepFoundTarget(session, findTarget);

        /// <summary>ClientTargeterEnd 0x27/0x23: a client targeter finished; closes its swing (accepted, no answer) [P].</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.AbilityClientTargeterEnd)]
        public static void HandleClientTargeterEnd(WorldSession session, AbilityClientTargeterEnd end)
            => Game.Combat.WeaponCombat.SweepEnded(session, end);

        [ZoneMessageHandler(ZoneMessageOpcode.AbilityCasterInfoRequest)]
        public static void AbilityCasterInfoRequest(WorldSession session, AbilityCasterInfoRequest abilityRequest)
        {
            uint abilityId = abilityRequest.AbilityId;
            if (abilityId == Game.Abilities.HeroicMovementAbility)
            {
                // Shift (input action HeroicMovement) casts 326. The captured reply under that id carried the Lightstone's
                // name and long timings, so the client refused every press with "ability on cooldown": answer it with no
                // cast, recovery or cost (the Energy check is the client's own, see CharacterResources); with the movement
                // effects on, MovementEffects.Describe makes it a channelled cast (kind 0x216b5dcb, held with Shift).
                var heroic = Game.Abilities.BuildItemAbilityInfo(
                    new Game.Inventory.ItemDefinitionManager.ItemDefinition { NameId = Game.Abilities.HeroicMovementName },
                    new Game.Inventory.ItemDefinitionManager.ItemAbility(abilityId, 0, "", ""));
                heroic.CastTime = 0f;
                heroic.RecoveryTime = 0f;
                heroic.Ua0 = 0;
                // The sprint itself: the client runs it from the reply's ClientExecutedEffectData (L160); a channelled cast
                // while Shift is held (L168).
                Game.MovementEffects.Describe(heroic);
                session.EnqueueMessage(heroic);
                return;
            }
            if (CapturedCasterInfo(abilityId) is byte[] captured)
            {
                session.EnqueueMessage(ZoneMessageOpcode.AbilityCasterInfoReply, captured);
                return;
            }

            var source = Game.Inventory.ItemDefinitionManager.FindAbility(abilityId);
            if (source == null && Game.BootAbilities.BootOf(abilityId) is { } boot)
            {
                // A boot's movement ability that no item lists (Dash Soles' directions, Roll Wraps' 1207-1214): instant,
                // like 326, named after the boot (Roll Wraps: 577207 "Roll").
                uint bootName = boot.ItemId == Game.BootAbilities.RollWraps ? Game.BootAbilities.RollName
                    : Game.Inventory.ItemDefinitionManager.Get(boot.ItemId)?.NameId ?? 0;
                var move = Game.Abilities.BuildItemAbilityInfo(
                    new Game.Inventory.ItemDefinitionManager.ItemDefinition { NameId = bootName },
                    new Game.Inventory.ItemDefinitionManager.ItemAbility(abilityId, 0, "", ""));
                move.CastTime = 0f;
                move.RecoveryTime = 0f;
                move.Ua0 = 0;
                // The jump / glide / sprint: ClientExecutedEffectData the client runs; the leaps are charged casts (L160);
                // Immediate Delivery and Endless Breeze channelled; Energy, cooldowns and cast times (L168).
                Game.MovementEffects.Describe(move);
                session.EnqueueMessage(move);
                return;
            }
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
        /// SetPlayerBio (0.695): the player changed their profile text.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateSetPlayerBioWire)]
        public static void PlayerSetPlayerBio(WorldSession session, PlayerUpdateSetPlayerBio playerBio)
        {
            // Saved on the character; it comes back in SendSelfToClient and the Inspect reply. No reply: the client
            // expects none.
            session.SetBio(playerBio.Bio);
            log.Debug($"{session.CharacterName} set a bio of {playerBio.Bio.Length} characters.");
        }

        /// <summary>
        /// PreferencePacketClientXml (0x03): the client's preference document (chat windows, action bar lock), sent on
        /// chat window / tab changes and at logout. Stored per character; no reply (Game.Preferences).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PreferencePacket)]
        public static void PreferencePacketClientXml(WorldSession session, PreferencePacketClientXml preferences)
        {
            Game.Preferences.Handle(session, preferences.Body);
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
