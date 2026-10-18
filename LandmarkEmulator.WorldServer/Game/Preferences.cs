using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Client preferences round-trip (zone opcode 0x03; trace/0695/agent-s2c-sweep.md §5 #1): the chat window comes back
    /// where the player left it, at the size they left it, and the action bar lock state is kept.
    /// <list type="bullet">
    /// <item><b>C→S PreferencePacketClientXml</b> {u32 kind = 1, u32 len, char[len] xml}: writer FUN_142c13420 (u16 opcode
    /// 3, u32 kind from +0x10, then FUN_142d4b510: u32 length + the characters, no terminator) [P]. The document is built
    /// from the client's preference store (PlayClient +0x32280): &lt;Root Version&gt;&lt;World/&gt;&lt;Zone/&gt;&lt;Client&gt;&lt;UI&gt;
    /// &lt;Chat&gt;&lt;ChatWindows IsList="1"&gt; one &lt;ChatWindow&gt; per window (Top / Left / Width / Height, FUN_142c50430)
    /// &lt;/ChatWindows&gt;&lt;/Chat&gt;&lt;ActionBar AreActionBarsLocked/&gt;… (FUN_142d4a390, FUN_142c50360) [P]. Senders:
    /// FUN_142d4a390, run by Ui.Logout (FUN_1435ccd00 → FUN_142d086a0: send, then the store is deleted, +0x32280 = 0) and
    /// the Exit path FUN_142d59850; and FUN_14336b4c0 / FUN_1433630b0, the chat module's timed push
    /// (PushPreferencesToWorldEvent, rtti_695) that Lua Chat.SaveChatTabs (native FUN_1435ad9b0 → FUN_14335fd60) arms:
    /// 30 s after a plain save, 1 s after an "insta" save [P]. SaveChatTabs copies every chat window's Top / Left / Width
    /// / Height into the store; ChatManager:SaveChatWindows calls it when a chat window is added or removed and when a
    /// tab is registered or removed (instant). Moving or resizing a window alone saves nothing until the next such call,
    /// and the save in ChatManager:CloseChatWindows (character select) finds the store already deleted by the logout, so
    /// a move is kept only if a window / tab change followed it [P: ChatManager.lua, no other SaveChatWindows / SaveChatTabs
    /// caller in the 0.695 scripts, UI files or native code]. Every sender first checks that the store exists.</item>
    /// <item><b>S→C BasePreferencePacket</b>, same layout: zone dispatcher FUN_142d1ccf0 case 1, reader FUN_142c4e700. The
    /// packet creates the store if there is none (param_1[0x6450] = +0x32280); only kind 1 is loaded (FUN_142c50880, root
    /// element "Root") and then Lua ChatManager:RestoreSavedChatWindows runs, which reads Chat.GetChatTabs and moves and
    /// sizes the main chat window [P].</item>
    /// </list>
    /// So the server must send one every login, also with nothing stored: without it the client has no store, never runs
    /// RestoreSavedChatWindows (so ChatManager.SavedWindowsRestored stays false and SaveChatWindows saves nothing), and
    /// never sends its preferences. With nothing stored the live default is sent, as captured from live (the old
    /// commented line in MiscHandler): <see cref="LiveDefault"/>. Character select resets SavedWindowsRestored
    /// (ChatManager:CloseChatWindows), so each login needs it again; a re-zone does not (the client keeps its store and
    /// the windows), and neither does a re-attach or an arrival from another world.
    /// <para>Stored per character (character_preference, kind 1), overwritten, byte for byte. Saving is coalesced per
    /// character: while one write runs, newer documents replace each other and only the newest is written next, so a
    /// burst of pushes costs at most two writes. A document equal to the stored one is not written.</para>
    /// </summary>
    public static class Preferences
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The only kind the client applies (FUN_142d1ccf0 case 1: <c>local_f498 == 1</c>) and the one it sends.</summary>
        public const uint Kind = 1;
        /// <summary>Largest document stored (a few windows' four numbers each is well under 1 KB; this leaves room).</summary>
        public const int MaxLength = 16 * 1024;

        /// <summary>
        /// The live server's document for a character with no saved preferences (live capture: BasePreferencePacket
        /// 01000000 8e000000 + these 142 characters) [P].
        /// </summary>
        public static readonly byte[] LiveDefault = Encoding.ASCII.GetBytes(
            "<Root Version=\"1.0\"><World/><Zone/><Client><UI><Chat><ChatWindows IsList=\"1\"/></Chat>" +
            "<ActionBar AreActionBarsLocked=\"0\"/></UI></Client></Root>");

        private static readonly byte[] RootPrefix = Encoding.ASCII.GetBytes("<Root");

        /// <summary>The character's stored document (null for none): the newest one this server still has to write, else the database's.</summary>
        public static byte[] Load(ulong characterId)
        {
            lock (gate)
            {
                if (pending.TryGetValue(characterId, out byte[] newest))
                    return newest;
                if (writing.TryGetValue(characterId, out byte[] inFlight) && inFlight != null)
                    return inFlight;
            }
            try
            {
                return DatabaseManager.Instance.CharacterDatabase.GetPreference(characterId, Kind);
            }
            catch (Exception e)
            {
                log.Error(e, $"Loading preferences of character {characterId:X} failed; the live default is sent.");
                return null;
            }
        }

        /// <summary>
        /// At the first ClientIsReady of a login: the stored document, else the live default. The client loads it and
        /// restores the chat windows (ChatManager:RestoreSavedChatWindows). ~150 B for the default.
        /// </summary>
        public static void SendOwn(WorldSession session)
        {
            byte[] xml = session.PreferenceXml ?? LiveDefault;
            var body = new List<byte>(8 + xml.Length);
            var writer = new GamePacketWriter(body);
            writer.Write(Kind);
            writer.Write((uint)xml.Length);
            body.AddRange(xml);
            session.EnqueueMessage(ZoneMessageOpcode.PreferencePacket, body.ToArray());
        }

        /// <summary>PreferencePacketClientXml from the client: checked, kept on the session and saved. No reply.</summary>
        public static void Handle(WorldSession session, byte[] body)
        {
            ulong characterId = session.CharacterId;
            if (characterId == 0)
                return;
            if (body == null || body.Length < 8)
            {
                log.Debug($"Short preference packet from {session.CharacterName}: {Convert.ToHexString(body ?? Array.Empty<byte>())}");
                return;
            }
            uint kind = BitConverter.ToUInt32(body, 0);
            int length = BitConverter.ToInt32(body, 4);
            if (kind != Kind || length < RootPrefix.Length || length > MaxLength || length != body.Length - 8
                || !body.AsSpan(8, RootPrefix.Length).SequenceEqual(RootPrefix))
            {
                log.Debug($"Preference packet from {session.CharacterName} dropped: kind {kind}, length {length}, body {body.Length} B.");
                return;
            }
            byte[] xml = body.AsSpan(8, length).ToArray();
            if (session.PreferenceXml != null && xml.AsSpan().SequenceEqual(session.PreferenceXml))
                return;
            session.PreferenceXml = xml;
            log.Info($"{session.CharacterName} sent preferences ({length} B).");
            Save(characterId, xml);
        }

        private static readonly object gate = new();
        /// <summary>Per character: the newest document not yet handed to a write.</summary>
        private static readonly Dictionary<ulong, byte[]> pending = new();
        /// <summary>Per character: the document a running write is storing (also marks that a writer runs).</summary>
        private static readonly Dictionary<ulong, byte[]> writing = new();

        private static void Save(ulong characterId, byte[] xml)
        {
            lock (gate)
            {
                pending[characterId] = xml;
                if (writing.ContainsKey(characterId))
                    return;          // the running writer takes it next
                writing[characterId] = null;
            }
            _ = Task.Run(() => Drain(characterId));
        }

        /// <summary>Writes the character's newest document until none is left (one writer per character, so in order).</summary>
        private static async Task Drain(ulong characterId)
        {
            while (true)
            {
                byte[] next;
                lock (gate)
                {
                    if (!pending.Remove(characterId, out next))
                    {
                        writing.Remove(characterId);
                        return;
                    }
                    writing[characterId] = next;
                }
                try
                {
                    await DatabaseManager.Instance.CharacterDatabase.SavePreference(characterId, Kind, next);
                }
                catch (Exception)
                {
                    // Logged by SavePreference; the next push from the client tries again.
                }
            }
        }
    }
}
