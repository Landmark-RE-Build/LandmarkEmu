using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System.Runtime.CompilerServices;

namespace LandmarkEmulator.WorldServer.Network.Handlers
{
    /// <summary>
    /// Voice chat (0xB9; trace/0695/audit-wiki-gaps.md #13). Live ran in-game voice through Vivox: the wiki's Local
    /// (proximity) and Guild channels with push-to-talk [W wiki Chat], the client's VivoxWrapper / PlanetsideClientVoice and
    /// the VoiceChatWidget (data sources BaseClient.VivoxRooms / VivoxParticipants, filled only by the client's own Vivox
    /// session) [P]. The server's part on live was to hand the client its Vivox login and channels, S→C only:
    /// <list type="bullet">
    /// <item>sub 0 VoiceLogin (reader FUN_1430cb780): four strings, read into +0x48, +0x60, +0x18, +0x30 [P layout]. Their
    /// roles from the Vivox calls (trace/0695/agent-leadsC.md L181) [P offsets; I the Vivox SDK names]: 1 account name and
    /// 2 account password (BeginLogin FUN_143691770 copies login +0x48 / +0x60 into the request's +0x38 / +0x40, the
    /// vx_req_account_login layout), 3 the account-management server URL (FUN_1430ce900 formats "%s/api2/" from login
    /// +0x18 for BeginConnect FUN_143691370; the client's own debug default host is "enlp.vivox.com"), 4 unused (no
    /// reader of +0x30 found) [P negative]. The login is read only while voice is enabled (voice +0x188);</item>
    /// <item>sub 1 VoiceJoinChannel (reader FUN_1430cada0): u8 channel type (+0x18), then the channel URI (+0x20 → the
    /// BeginSession FUN_143691930 request's +0x40; the client's debug form is "sip:confctl-2@%s"), its password (+0x38 →
    /// +0x48, set only when not empty) and a display name (+0x50: a custom room's name, "user." prefix stripped) [P
    /// offsets; I names]. The dispatcher FUN_1430cfad0 adds the channel and drops an older one of the same type (type 7
    /// can repeat). Channel types (FUN_143c6dff0) [P]: 0 Echo, 1 Proximity (the wiki's Local: only type 1 gets the
    /// positional setup FUN_14369b6b0), 2 Faction, 3 Group, 4 GroupLeader, 5 Raid, 6 Guild, 7 Custom;</item>
    /// <item>sub 2 VoiceLeaveChannel: u8 channel type (every channel of that type goes, then the channels re-sync).</item>
    /// </list>
    /// This server has no voice service, so it never sends VoiceLogin: the client's voice then never connects, which is the
    /// same state as a player with voice switched off — no window waits on the server [P: without a login (voice +0x38 ==
    /// 0) the update FUN_1430cf6b0 never connects, and with voice off it shuts Vivox down; no client code waits for a voice
    /// packet; the voice widget only lists the rooms its own Vivox session has]. This is the clean "voice unavailable"
    /// state: a VoiceLogin with empty strings would start a connect to an empty URL (FUN_1430ce900 → BeginConnect) [I]. The one thing the client sends,
    /// VoiceRequestCustomChannel (sub 6, a player asking for a custom room), gets a system line saying voice isn't available
    /// (once per session [I: live had no such reply; the client has no "voice unavailable" text]); nothing is answered on
    /// 0xB9, since the family has no refusal packet.
    /// </summary>
    public static class VoiceHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Sessions already told that voice isn't available.</summary>
        private static readonly ConditionalWeakTable<WorldSession, object> told = new();

        [ZoneMessageHandler(ZoneMessageOpcode.VoiceBase)]
        public static void HandleVoice(WorldSession session, VoiceRequest request)
        {
            if (request.SubOpcode == VoiceRequest.RequestCustomChannel)
            {
                log.Info($"{session.CharacterName} asked for voice room \"{request.RoomName}\": no voice service, not joined.");
                if (!told.TryGetValue(session, out _))
                {
                    told.AddOrUpdate(session, new object());
                    Game.Chat.System(session, "Voice chat is not available on this server.");
                }
                return;
            }
            Network.OpcodeStats.Unanswered("Voice 0xB9", request.SubOpcode);
            log.Debug($"{session.CharacterName}: voice packet sub {request.SubOpcode} ({request.Body?.Length ?? 0} bytes), not handled.");
        }
    }
}
