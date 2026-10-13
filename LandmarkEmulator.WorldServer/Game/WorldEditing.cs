using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Other players' build cursors: WorldEditingBase 0x10C. While a player builds, the client sends the shape of its
    /// tool cursor every time it changes (FUN_14362a210 → box 0x1 / sphere 0x2 / shape 0x3 senders; selection 0x4 from
    /// FUN_143636770; template preview 0x5 / 0x6; fill 0x7) and a Clear 0x0 when it goes away (FUN_142fe6140). Each
    /// packet ends its body with the builder's own character guid (BaseClient +0x32508, the "Character.%llu" id).
    /// <para>
    /// The client also has the receiving side: the zone dispatcher hands incoming 0x10C to
    /// ClientWorldEditingEffectManager (FUN_142fe3ae0), which keeps one effect per guid and draws the box / sphere /
    /// shape / selection / preview at the other player's cursor, and to FUN_1433b9530 (same per-guid map at +0xe0).
    /// Neither skips packets about the local player, so the live server sent them to the other players only. A Clear with
    /// flag 0 removes everything shown for that guid (FUN_142fe11b0); flag 1 removes only the selection.
    /// </para>
    /// <para>
    /// Relay rules (bandwidth): only the newest packet per builder is kept and sent at most every
    /// <see cref="MinInterval"/>; a packet identical to the last one sent goes only to players who came into range; a
    /// builder's cursor is cleared for players who leave <see cref="ViewRange"/> and for everyone when the builder leaves.
    /// A packet whose guid is not the sender's own is dropped.
    /// </para>
    /// </summary>
    public static class WorldEditing
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const float ViewRange = 160f;
        private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan ViewCheck = TimeSpan.FromSeconds(1);

        /// <summary>Wire header: u16 opcode 0x010C, u32 sub, u32 (FUN_142c38cc0's id), then the sub's body.</summary>
        private const int HeaderLength = 10;
        private const uint ClearSub = 0, PreviewSetupSub = 5;

        private sealed class Editor
        {
            public byte[] Pending;
            public byte[] LastSent;
            public DateTime LastSentAt;
            public DateTime LastViewCheck;
            public HashSet<WorldSession> Viewers = new();
        }

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, Editor> editors = new();

        /// <summary>A 0x10C packet from a client (raw, starting with the opcode bytes).</summary>
        public static void Received(WorldSession session, byte[] raw)
        {
            int key = KeyOffset(raw, session.PlayerGuid);
            if (key < 0 || BitConverter.ToUInt64(raw, key) != session.PlayerGuid)
            {
                log.Debug($"{session.CharacterName}: world-editing packet dropped (unreadable or not about the sender).");
                return;
            }
            lock (sync)
            {
                if (!editors.TryGetValue(session, out Editor editor))
                    editors[session] = editor = new Editor();
                editor.Pending = raw;
            }
        }

        /// <summary>Sends due cursor updates and keeps each builder's viewers in step with who is in range.</summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            var sends = new List<(WorldSession To, byte[] Packet)>();
            lock (sync)
            {
                foreach (var (builder, editor) in editors)
                {
                    bool due = editor.Pending != null && now - editor.LastSentAt >= MinInterval;
                    bool check = now - editor.LastViewCheck >= ViewCheck;
                    if (!due && !check)
                        continue;
                    editor.LastViewCheck = now;
                    var inRange = PlayerManager.InZone(builder.ZoneId)
                        .Where(p => p != builder && !p.Disconnected
                            && Vector3.Distance(p.Position, builder.Position) <= ViewRange)
                        .ToHashSet();

                    if (due)
                    {
                        byte[] packet = editor.Pending;
                        editor.Pending = null;
                        bool same = editor.LastSent != null && packet.AsSpan().SequenceEqual(editor.LastSent);
                        foreach (WorldSession viewer in inRange)
                            if (!same || !editor.Viewers.Contains(viewer))
                                sends.Add((viewer, packet));
                        editor.LastSent = packet;
                        editor.LastSentAt = now;
                    }
                    else if (editor.LastSent != null && !IsClearAll(editor.LastSent))
                        foreach (WorldSession viewer in inRange)
                            if (!editor.Viewers.Contains(viewer))
                                sends.Add((viewer, editor.LastSent));

                    if (editor.LastSent != null && !IsClearAll(editor.LastSent))
                        foreach (WorldSession gone in editor.Viewers)
                            if (!inRange.Contains(gone))
                                sends.Add((gone, ClearAll(builder, editor.LastSent)));
                    editor.Viewers = inRange;
                }
            }
            foreach (var (to, packet) in sends)
                to.SendRaw(packet);
        }

        /// <summary>The builder left the world: their cursor goes away for everyone who saw it.</summary>
        public static void Forget(WorldSession session)
        {
            Editor editor;
            lock (sync)
                if (!editors.Remove(session, out editor))
                    return;
            if (editor.LastSent == null || IsClearAll(editor.LastSent))
                return;
            byte[] clear = ClearAll(session, editor.LastSent);
            foreach (WorldSession viewer in editor.Viewers)
                viewer.SendRaw(clear);
        }

        /// <summary>A Clear (sub 0, flag 0: remove everything shown for the guid) for <paramref name="builder"/>.</summary>
        private static byte[] ClearAll(WorldSession builder, byte[] last)
        {
            byte[] clear = new byte[HeaderLength + 1 + 8];
            Array.Copy(last, 0, clear, 0, 2);                       // opcode
            BitConverter.GetBytes(ClearSub).CopyTo(clear, 2);
            Array.Copy(last, 6, clear, 6, 4);                       // the client's id field, as the builder sent it
            clear[HeaderLength] = 0;
            BitConverter.GetBytes(builder.PlayerGuid).CopyTo(clear, HeaderLength + 1);
            return clear;
        }

        private static bool IsClearAll(byte[] packet)
            => BitConverter.ToUInt32(packet, 2) == ClearSub && packet.Length > HeaderLength && packet[HeaderLength] == 0;

        /// <summary>
        /// Where the builder guid sits in a 0x10C packet, from the client readers: Clear {u8 flag, u64}; Box (FUN_142fdf050)
        /// {vec4 min, vec4 max, string, u64}; Sphere (FUN_142fdc1c0) {vec3, f32 radius, string, u64}; Shape (FUN_142fdc110)
        /// {4×vec4 transform, u32, string, u64}; Selection (FUN_142fdc040) {u32, 2×vec4, string, u64, u32}; PreviewUpdate
        /// (FUN_142fdbf40) {vec4, 4×vec4, bool, string, u64}; Fill (FUN_142ba2ce0 list) {u32 n × vec4, string, u64};
        /// PreviewSetup (FUN_142fdbdd0) has nested template records before the guid and a byte array after it, so its guid
        /// is found by value. -1 when the packet is too short.
        /// </summary>
        private static int KeyOffset(byte[] p, ulong guid)
        {
            if (p.Length < HeaderLength + 8)
                return -1;
            uint sub = BitConverter.ToUInt32(p, 2);
            int o = HeaderLength;
            switch (sub)
            {
                case 0: o += 1; break;
                case 1: o = SkipString(p, o + 32); break;
                case 2: o = SkipString(p, o + 16); break;
                case 3: o = SkipString(p, o + 64 + 4); break;
                case 4: o = SkipString(p, o + 4 + 32); break;
                case 6: o = SkipString(p, o + 16 + 64 + 1); break;
                case 7:
                    if (o + 4 > p.Length)
                        return -1;
                    long n = BitConverter.ToUInt32(p, o);
                    if (n > (p.Length - o) / 16)
                        return -1;
                    o = SkipString(p, o + 4 + (int)n * 16);
                    break;
                case PreviewSetupSub:
                    return p.AsSpan(HeaderLength).IndexOf(BitConverter.GetBytes(guid)) is int at and >= 0 ? HeaderLength + at : -1;
                default:
                    return -1;
            }
            return o >= 0 && o + 8 <= p.Length ? o : -1;
        }

        /// <summary>Skips a string {u32 length, bytes}; -1 when it runs past the end.</summary>
        private static int SkipString(byte[] p, int o)
        {
            if (o < 0 || o + 4 > p.Length)
                return -1;
            long length = BitConverter.ToUInt32(p, o);
            return length > p.Length - o - 4 ? -1 : o + 4 + (int)length;
        }
    }
}
