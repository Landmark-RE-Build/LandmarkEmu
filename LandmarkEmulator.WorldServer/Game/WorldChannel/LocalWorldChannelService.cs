using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Game.Events;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game.WorldChannel
{
    /// <summary>
    /// The world-channel back end in the world server's own process: today's code (<see cref="Trades"/>,
    /// <see cref="Encounters"/>, <see cref="Ratings"/>, the character-select session), unchanged. Each call runs on the
    /// caller's thread exactly as the direct call did.
    /// </summary>
    public class LocalWorldChannelService : IWorldChannelService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // ---- trade broker (in-process; the world-channel container's TradeBroker uses WorldChannelLink instead) ------

        public void TradeRequest(WorldSession session, byte sub, byte[] body) => Trades.Handle(session, sub, body);

        public void TradePartyLeft(WorldSession session) => Trades.Forget(session);

        public void TradePartyMoved(WorldSession session, bool intoGame) => Trades.Moved(session, intoGame);

        // ---- encounters ---------------------------------------------------------------------------------------------

        public void EncounterRequest(WorldSession session, ushort sub, byte[] body) => Encounters.Handle(session, sub, body);

        public void DuelChallenge(WorldSession session, string name) => Encounters.Challenge(session, name);

        public void DuelAnswer(WorldSession session, ulong inviterGuid, bool accept) => Encounters.Answer(session, inviterGuid, accept);

        public void EncounterLeave(WorldSession session, string why) => Encounters.Leave(session, why);

        // ---- claim votes --------------------------------------------------------------------------------------------

        public void ClaimVote(WorldSession session, byte[] body) => Ratings.HandleAddVote(session, body);

        public void CanVote(WorldSession session, byte[] body) => Ratings.HandleCanVote(session, body);

        public ulong ClaimVoteTotal(ulong claimId) => Ratings.Total(claimId);

        // ---- session ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Logging out to character select: issue a fresh login session key (as the WebAPI's /authenticate does) so the
        /// client can log back in to the login server without the launcher. The answer is CharacterSelectSessionResponse
        /// 0xF7 {bool, string session} (reader FUN_142bb57e0; ok → run state 0x16 → ClientLogout 0x14 on Zone). The key
        /// lives in the login service's table (spec ReloginSessionRequest); the zone notes the relog so the client's
        /// item-definition cache is kept (spec NoteRelogin, MODLOG 2026-10-14).
        /// <para>
        /// Always answered, and in time (agent-leads-sweep.md L61) [P]: the client gives up 60 s after sending 0xF6
        /// (FUN_142d57e60 sets the deadline game +0x324a0 = now + 60000; run state 0x15 FUN_142cd4e60 then logs "Timed out
        /// while waiting for a relogin session." and shuts down). So the key is only sent once the database has really
        /// saved it (a failed save used to answer "ok" with a key the login server would refuse), and a save that fails
        /// or takes longer than <see cref="ReloginSaveLimit"/> is answered "not ok" (the client's own failure path,
        /// run state 0x17) rather than left to the client's timeout.
        /// </para>
        /// </summary>
        public void CharacterSelectSession(WorldSession session)
        {
            if (session.Account == null)
            {
                session.EnqueueMessage(new CharacterSelectSessionResponse { Success = false });
                return;
            }

            string sessionKey = Inventory.ItemDefinitionManager.NewRelogKey();
            session.EnqueueEvent(new TaskGenericEvent<bool>(SaveSessionKey(session.Account, sessionKey), saved =>
            {
                if (!saved)
                {
                    log.Warn($"Account {session.Account.Id}: relog session key not saved within {ReloginSaveLimit.TotalSeconds:0} s; \"not ok\" to the client.");
                    session.EnqueueMessage(new CharacterSelectSessionResponse { Success = false });
                    return;
                }
                log.Info($"Account {session.Account.Id} returning to character select.");
                Inventory.ItemDefinitionManager.NoteRelogin(session.Account.Id, sessionKey);
                session.EnqueueMessage(new CharacterSelectSessionResponse
                {
                    Success = true,
                    SessionId = sessionKey
                });
            }));
        }

        /// <summary>How long the relog key's save may take: well inside the client's 60 s, leaving time for the answer.</summary>
        private static readonly TimeSpan ReloginSaveLimit = TimeSpan.FromSeconds(30);

        /// <summary>Saves the relog key as the account's session key; false when it failed or took too long.</summary>
        private static async Task<bool> SaveSessionKey(LandmarkEmulator.Database.Auth.Model.AccountModel account, string sessionKey)
        {
            try
            {
                Task save = DatabaseManager.Instance.AuthDatabase.UpdateAccountSessionKey(account, sessionKey);
                if (await Task.WhenAny(save, Task.Delay(ReloginSaveLimit)) != save)
                    return false;
                await save;
                return true;
            }
            catch (Exception e)
            {
                log.Warn(e.GetBaseException(), $"Account {account.Id}: relog session key not saved.");
                return false;
            }
        }

        /// <summary>Not used yet: the locale was ignored before the split, and still is.</summary>
        public void SetLocale(WorldSession session, byte[] body) { }

        /// <summary>The travel queue's Accept / Cancel (<see cref="TravelQueue"/>, L60).</summary>
        public bool TeleportQueueRequest(WorldSession session, byte sub, byte[] body) => TravelQueue.Handle(session, sub, body);

        /// <summary>A sink: telemetry needs no answer.</summary>
        public void ClientTelemetry(WorldSession session, ZoneMessageOpcode opcode, byte[] body) => Telemetry.RecordZone(session, opcode, body);

        // ---- tick ---------------------------------------------------------------------------------------------------

        public void Update() => Encounters.Update();
    }
}
