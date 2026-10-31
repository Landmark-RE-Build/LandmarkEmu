using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// PlayerUpdate requests that concern entities: respawning after death, and the client asking for the full data of an
    /// entity it only knows in lightweight form.
    /// </summary>
    public class PlayerHandler
    {
        private static readonly NLog.ILogger log = NLog.LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The death window's respawn choice (PlayerUpdate 0x3B {u32 type}); see <see cref="Game.Health.HandleRespawn"/>.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateRespawnWire)]
        public static void HandleRespawn(WorldSession session, PlayerUpdateRespawn request)
            => Game.Health.HandleRespawn(session, request.Type);

        /// <summary>
        /// RequestStripEffect: the player drops one of their own effects (same as the buff bar's cancel button). The body
        /// is the effect id (u32; a u64 key form is accepted too).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateRequestStripEffectWire)]
        public static void HandleRequestStripEffect(WorldSession session, PlayerUpdateRequestStripEffect request)
        {
            if (request.Body.Length >= 4)
                Game.Buffs.Cancel(session, System.BitConverter.ToUInt32(request.Body, 0));
        }

        /// <summary>PreferredLanguages: kept on the session (chat language), logged.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdatePreferredLanguagesWire)]
        public static void HandlePreferredLanguages(WorldSession session, PlayerUpdatePreferredLanguages request)
        {
            if (request.Body.Length >= 12)
                session.PreferredLanguages = System.BitConverter.ToUInt32(request.Body, 8);
            log.Info($"{session.CharacterName} prefers languages 0x{session.PreferredLanguages:X}.");
        }

        /// <summary>
        /// RemoveTemporaryAppearance: the player drops a temporary look (an admin's /setmodel, Game.TemporaryAppearance).
        /// Everyone on the landscape is told (S→C 0x0A {u64 character}; 0.695 case 9 → FUN_142eca480 restores the normal
        /// appearance) and later arrivals no longer get it.
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateRemoveTemporaryAppearanceWire)]
        public static void HandleRemoveTemporaryAppearance(WorldSession session, PlayerUpdateRemoveTemporaryAppearanceRequest request)
            => Game.TemporaryAppearance.Clear(session);

        /// <summary>SafeEject: the client was launched (see <see cref="Game.SafeEject"/>); bit 26 is cleared when it lands.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateSafeEjectWire)]
        public static void HandleSafeEject(WorldSession session, PlayerUpdateSafeEjectRequest request)
            => Game.SafeEject.Report(session, request.Flag);

        /// <summary>SetEnabled and EmoteStringById: informational; logged.</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateSetEnabledWire)]
        public static void HandleSetEnabled(WorldSession session, PlayerUpdateSetEnabledRequest request)
            => log.Info($"{session.CharacterName} SetEnabled {System.BitConverter.ToString(request.Body)}");

        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateEmoteStringByIdWire)]
        public static void HandleEmoteStringById(WorldSession session, PlayerUpdateEmoteStringById request)
        {
            log.Info($"{session.CharacterName} EmoteStringById {System.BitConverter.ToString(request.Body)}");
            Game.Emotes.RelayEmoteText(session, request.Body);
        }

        /// <summary>The dead player's answer to ReadyToRevive (see <see cref="Game.Health.HandleReadyToReviveResponse"/>).</summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateReadyToReviveResponseWire)]
        public static void HandleReadyToReviveResponse(WorldSession session, PlayerUpdateReadyToReviveResponse request)
            => Game.Health.HandleReadyToReviveResponse(session, request.Body);

        /// <summary>
        /// FullCharacterDataRequest (0x26/0x5E {u64 guid}): the client wants the full form of a lightweight entity. Props (placed and
        /// the Spire's) get LightweightToFullProp (collision, interaction; needed before a placed prop can be edited); creatures get
        /// LightweightToFullNpc; other players get LightweightToFullPc (their appearance, stats and equipment).
        /// </summary>
        [ZoneMessageHandler(ZoneMessageOpcode.PlayerUpdateFullCharacterDataRequestWire)]
        public static void HandleFullCharacterDataRequest(WorldSession session, PlayerUpdateFullCharacterDataRequest request)
        {
            if (Game.Props.SendFull(session, request.Guid) || Game.Spire.SendFull(session, request.Guid)
                || Game.WarpNetwork.SendFull(session, request.Guid) || Game.Outposts.SendFull(session, request.Guid)
                || Game.LandmarksOfLandmark.SendFull(session, request.Guid)
                || Game.LootChests.SendFull(session, request.Guid) || Game.Graveyards.SendFull(session, request.Guid)
                || Game.Gathering.SendFull(session, request.Guid) || Game.Creatures.SendFull(session, request.Guid))
                return;

            WorldSession other = PlayerManager.FindByGuid(request.Guid);
            if (other == null || other == session)
            {
                log.Debug($"{session.CharacterName}: full data request for 0x{request.Guid:X} not answered.");
                return;
            }

            session.EnqueueMessage(other.BuildLightweightToFullPc());
            // A stance the player is in (kneel, play dead…), after the full character so the client has the entity; a
            // client sit travels in the LightweightToFullPc's movement itself (Game.Emotes.IntroducePose).
            Game.Emotes.IntroducePose(session, other);
        }
    }
}
