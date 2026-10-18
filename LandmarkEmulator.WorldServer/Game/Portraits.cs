using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Character portraits and headshots (the client's FotomatPortraitDataManager / ScreenshotManager). FotomatBase
    /// 0x98 (wire bytes 98 00), i16 sub:
    /// <list type="bullet">
    /// <item>C→S 1 GeneratePortraitRequest (FUN_142ebf000, Lua RequestPortrait, at most once a minute): the requester's
    /// appearance block (~7 KB) for a portrait service to render.</item>
    /// <item>C→S 2 PortraitDataRequest (FUN_142ebee10, once per player and kind): u64 character, string kind
    /// ("Headshot" or "Portrait"). Sent for party members and other players shown with a face.</item>
    /// <item>S→C 3 PlayerImageData (reader FUN_142ebab00, handler FUN_142ebd850): u64 character, string kind, bool,
    /// appearance block (FUN_142eba940: u32, u32, u64, u32, u32 n + list, {u32 n + n × {u32, u32, u32}, u32, string, u32,
    /// u32}, u32 × 4, string), then the image as u32 length + PNG bytes. A non-empty image goes into the client's
    /// portrait / headshot cache for that character.</item>
    /// </list>
    /// The official portraits came from a rendering service. The client can also render its own: ScreenshotManager
    /// captures a portrait and headshot of the local character and posts them (multipart: imageType "portrait",
    /// characterId, thumbnailFile headshot.png, imageFile portrait.png) to ClientConfig [Portrait] UploadUrl, after
    /// telling the server their checksum (AddClientPortraitCrc 0x75 {u32 CRC-32 of portrait.png}). The server keeps
    /// those uploads (accepted only when the checksum came from that character's own session) and answers requests
    /// with them; until a character has one, requests stay unanswered and the client shows its default face.
    /// </summary>
    public static class Portraits
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const short GenerateSub = 1, DataRequestSub = 2, ImageDataSub = 3;
        private const int MaxImageBytes = 2 * 1024 * 1024;

        private static string Folder => Path.Combine(AppContext.BaseDirectory, "Portraits");
        private static string FileOf(ulong character, string kind) => Path.Combine(Folder, $"{character}_{kind.ToLowerInvariant()}.png");

        /// <summary>Checksums announced per character (AddClientPortraitCrc), for checking uploads.</summary>
        private static readonly Dictionary<ulong, uint> announced = new();

        /// <summary>FotomatBase from the client.</summary>
        public static void Handle(WorldSession session, byte[] data)
        {
            if (data.Length < 2)
                return;
            short sub = BitConverter.ToInt16(data, 0);
            var reader = new GamePacketReader(data.AsSpan(2).ToArray());
            if (sub == DataRequestSub && data.Length >= 10)
            {
                ulong character = reader.ReadULong();
                string kind = reader.BytesRemaining >= 4 ? reader.ReadString() ?? "" : "";
                if (kind is "Headshot" or "Portrait")
                    Send(session, character, kind);
            }
            else if (sub == GenerateSub)
                log.Debug($"{session.CharacterName} asked for a rendered portrait ({data.Length} bytes of appearance).");
        }

        /// <summary>AddClientPortraitCrc (0x75 {u32 crc}): the checksum of the portrait about to be uploaded.</summary>
        public static void Announce(WorldSession session, uint crc)
        {
            lock (announced)
                announced[session.PlayerGuid] = crc;
            log.Info($"{session.CharacterName} announced a portrait (CRC {crc:X8}).");
        }

        /// <summary>Sends a stored image (if any) as PlayerImageData.</summary>
        public static bool Send(WorldSession session, ulong character, string kind)
        {
            string file = FileOf(character, kind);
            if (!File.Exists(file))
                return false;
            session.EnqueueMessage(ZoneMessageOpcode.FotomatBase, Build(character, kind, File.ReadAllBytes(file)));
            return true;
        }

        public static byte[] Build(ulong character, string kind, byte[] png)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            // The sub is an i16 (reader FUN_142ebab00: u16 opcode, i16 sub, u64 character, …). Write(short) has no overload
            // and picked Write(int) — 4 bytes, which shifted the character and every later field (fixed 2026-10-16).
            w.WriteUShort((ushort)ImageDataSub);
            w.Write(character);
            w.Write(kind);
            w.Write(true);
            // Appearance block, empty.
            w.Write(0u);
            w.Write(0u);
            w.Write(0ul);
            w.Write(0u);
            w.Write(0u);          // list
            w.Write(0u);          // {list,
            w.Write(0u);          //  u32,
            w.Write("");          //  string,
            w.Write(0u);          //  u32,
            w.Write(0u);          //  u32}
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write("");
            w.Write((uint)png.Length);
            output.AddRange(png);
            return output.ToArray();
        }

        /// <summary>
        /// An upload from the client's own capture (web page /portrait/upload): accepted when the portrait's CRC-32 is
        /// what that character announced over the game. Returns a message for the log.
        /// </summary>
        public static (bool Ok, string Message) Upload(ulong character, byte[] portrait, byte[] headshot)
        {
            if (portrait == null || portrait.Length == 0 || portrait.Length > MaxImageBytes || (headshot?.Length ?? 0) > MaxImageBytes)
                return (false, "bad image");
            uint crc = Crc32(portrait);
            lock (announced)
                if (!announced.TryGetValue(character, out uint expected) || expected != crc)
                    return (false, "checksum not announced by that character");
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(FileOf(character, "Portrait"), portrait);
            if (headshot?.Length > 0)
                File.WriteAllBytes(FileOf(character, "Headshot"), headshot);
            log.Info($"Portrait stored for character {character} ({portrait.Length} + {headshot?.Length ?? 0} bytes).");
            return (true, "stored");
        }

        /// <summary>Stores an image for a character (dev / tests).</summary>
        public static void Store(ulong character, string kind, byte[] png)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(FileOf(character, kind), png);
        }

        private static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        /// <summary>CRC-32 as the client computes it (FUN_142ac1800: the standard table, start ~0, final ~).</summary>
        public static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in data)
                c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return ~c;
        }
    }
}
