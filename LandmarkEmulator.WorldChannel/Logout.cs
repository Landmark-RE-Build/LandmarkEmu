using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Login;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldChannel
{
    /// <summary>
    /// Logging out to character select (world-channel-service.md §6.7, §8 step 4): PacketCharacterSelectSessionRequest
    /// 0xF6 (Lua Ui.Logout, channel 1). The world channel gets a login session key from the login service — made with
    /// the zone's relog tag (ZoneRelogTag), so the relogin keeps the client's cached item list — tells the zone
    /// (NoteRelogin) and answers CharacterSelectSessionResponse 0xF7 {bool ok, string session} (reader FUN_142bb57e0;
    /// ok → the client sends ClientLogout 0x14 on channel 0 and the zone closes the connection, MODLOG 10-09; not ok →
    /// run state 0x17). Without the zone's tag or a login service the request goes on to the zone, which still does it
    /// the old way.
    /// <para>
    /// <b>The 60 s deadline</b> (agent-leads-sweep.md L61) [P]: the client's sender FUN_142d57e60 sets game +0x324a0 to
    /// now + 60000 ms when it sends 0xF6; its run state 0x15 WaitingForReloginSession (FUN_142cd4e60) logs "Timed out
    /// while waiting for a relogin session." once that passes and goes to 0x17 cClientRunStateShuttingDown — the client
    /// quits. So every 0xF6 must be answered well inside 60 s whatever is down: the login service has its own 10 s limit
    /// (LoginClient); when it fails the request is handed on to the zone, which answers from the auth database
    /// (LocalWorldChannelService.CharacterSelectSession); when neither the login service nor the zone can answer, the
    /// client is told "not ok" at once (it quits either way, but now, not after a minute of a frozen screen).
    /// </para>
    /// </summary>
    public static class Logout
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The login service (LoginInternalAddress), when configured.</summary>
        public static LoginClient Login { get; set; }

        /// <summary>The zone's relog tag, once it sent it.</summary>
        public static string RelogTag { get; set; }

        public static bool CanHandle => Login != null && !string.IsNullOrEmpty(RelogTag);

        /// <summary>
        /// 0xF6 from a client: answered here (asynchronously). <paramref name="handOn"/> passes the request on to the zone
        /// unchanged (false when the zone link is down) — the fallback when the login service gives no session.
        /// </summary>
        public static void Request(TunnelFromExternal request, Func<IInternalMessage, bool> toGateway, Func<IInternalMessage, bool> toZone,
            Func<TunnelFromExternal, bool> handOn)
        {
            // The same form as the zone's own keys (ItemDefinitionManager.NewRelogKey): tag + random hex, 32 characters.
            // Without the zone's tag (the zone has not connected yet) the key is still valid for the login server; only
            // the client's item-definition cache is not kept on the way back.
            string key = ((RelogTag ?? "") + Convert.ToHexString(Shared.Network.Cryptography.RandomProvider.GetBytes(16u)))[..32];
            _ = Task.Run(async () =>
            {
                RelogSessionReply reply = null;
                try
                {
                    reply = await Login.Relog(0, request.CharacterGuid, key);
                }
                catch (Exception e)
                {
                    log.Warn(e, $"Character {request.CharacterGuid}: relog session request failed.");
                }
                if (reply != null)
                {
                    toZone(Internal.Social.SocialMessages.ToPacket(new NoteRelogin { AccountId = reply.AccountId, SessionKey = reply.SessionKey }));
                    log.Info($"Account {reply.AccountId} returning to character select.");
                }
                else if (handOn(request))
                {
                    // The zone answers from the auth database (its own CharacterSelectSession), well inside the deadline.
                    log.Warn($"Character {request.CharacterGuid}: no relog session from the login service; handed on to the zone.");
                    return;
                }
                else
                    log.Warn($"Character {request.CharacterGuid}: no relog session and no zone; the client is told \"not ok\" (it shuts down).");
                Answer(request.ConnectionId, reply?.SessionKey, toGateway);
            });
        }

        /// <summary>
        /// CharacterSelectSessionResponse 0xF7 {bool ok, string session} to the client on channel 0 (Zone) through the
        /// gateway; <paramref name="sessionKey"/> null = not ok.
        /// </summary>
        public static bool Answer(ulong connectionId, string sessionKey, Func<IInternalMessage, bool> toGateway)
        {
            var packet = new List<byte> { 0xF7, 0x00, sessionKey != null ? (byte)1 : (byte)0 };
            byte[] session = Encoding.ASCII.GetBytes(sessionKey ?? "");
            packet.AddRange(BitConverter.GetBytes(session.Length));
            packet.AddRange(session);
            return toGateway(new TunnelToExternal
            {
                Destination = new ExternalDestination(connectionId, GatewayChannel.Zone, ExternalDestination.Reliable),
                Packet = packet.ToArray()
            });
        }
    }
}
