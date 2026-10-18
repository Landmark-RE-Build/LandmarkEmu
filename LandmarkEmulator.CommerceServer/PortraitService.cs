using LandmarkEmulator.Internal.Commerce;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// Portraits and headshots in the commerce service (were Game/Portraits.cs in each world; spec §5.1: live's portraits
    /// came from an external rendering service, Fotomat, shared by every world). The client side is unchanged
    /// (docs/showcase.md, Portraits.cs):
    /// <list type="bullet">
    /// <item>FotomatBase 0x98 sub 2 PortraitDataRequest {u64 character, string "Headshot" / "Portrait"} (World channel,
    /// FUN_142ebee10; "same" in 0.695) → sub 3 PlayerImageData with the stored PNG; nothing while the character has
    /// none (the client keeps its default face). Sub 1 GeneratePortraitRequest is a Zone-channel message and stays in the
    /// zone (logged; no renderer).</item>
    /// <item>AddClientPortraitCrc 0x75 {u32} (World channel): the CRC-32 of the portrait the client is about to post to
    /// ClientConfig [Portrait] UploadUrl; the world's web pages hand the upload on (<see cref="CommercePortraitUpload"/>),
    /// accepted only when the portrait's CRC is what that character announced.</item>
    /// </list>
    /// Files: data folder Portraits/&lt;world&gt;_&lt;character&gt;_&lt;kind&gt;.png, indexed in table portrait. A character
    /// is the same on every world of its realm (shared character database), so a request finds an image uploaded on any of
    /// them; announced checksums are kept per (realm, character) in memory, as in-process (lost on restart: the client
    /// announces again before its next upload).
    /// </summary>
    public sealed class PortraitService
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const int MaxImageBytes = 2 * 1024 * 1024;
        private static readonly Regex OldFile = new(@"^(\d+)_(portrait|headshot)\.png$", RegexOptions.IgnoreCase);

        private readonly CommerceHub hub;
        private readonly Dictionary<(string Realm, ulong Character), uint> announced = new();

        private string Folder => Path.Combine(hub.DataPath, "Portraits");

        public PortraitService(CommerceHub hub) => this.hub = hub;

        /// <summary>"Portrait" / "Headshot" in the client's spelling, or null.</summary>
        private static string Kind(string kind)
            => string.Equals(kind, "Portrait", StringComparison.OrdinalIgnoreCase) ? "Portrait"
                : string.Equals(kind, "Headshot", StringComparison.OrdinalIgnoreCase) ? "Headshot" : null;

        /// <summary>PortraitDataRequest (client body after the sub: u64 character, string kind): the stored image, if any.</summary>
        public void Request(WorldLink world, CommerceClientRequest request)
        {
            ulong character;
            string kind;
            try
            {
                var r = new Shared.Network.GamePacketReader(request.Body);
                character = r.ReadULong();
                kind = r.BytesRemaining >= 4 ? r.ReadString() ?? "" : "";
            }
            catch (Exception)
            {
                return;
            }
            if (kind is not ("Headshot" or "Portrait"))
                return;
            Send(world, request.Caller, character, kind);
        }

        /// <summary>Sends a stored image (if any) as PlayerImageData.</summary>
        public bool Send(WorldLink world, CommerceCaller caller, ulong character, string kind)
        {
            string file = hub.Store.FindPortrait(world.Realm, character, kind);
            string path = file == null ? null : Path.Combine(Folder, file);
            if (path == null || !File.Exists(path))
                return false;
            hub.SendClient(world, caller, ClientPackets.PlayerImageData(character, kind, File.ReadAllBytes(path)));
            return true;
        }

        /// <summary>AddClientPortraitCrc (0x75 {u32 crc}): the checksum of the portrait about to be uploaded.</summary>
        public void Announce(WorldLink world, CommerceClientRequest request)
        {
            if (request.Body.Length < 4)
                return;
            uint crc = BitConverter.ToUInt32(request.Body, 0);
            announced[(world.Realm, request.Caller.Character)] = crc;
            log.Info($"{request.Caller.Name} (world {request.Caller.World}) announced a portrait (CRC {crc:X8}).");
        }

        /// <summary>
        /// An upload handed on by a world's web pages (Portraits.Upload): accepted when the portrait's CRC-32 is what that
        /// character announced over the game; the dev command's images (trusted) are stored as they are.
        /// </summary>
        public CommercePortraitUploadReply Upload(WorldLink world, CommercePortraitUpload upload)
        {
            CommercePortraitUploadReply Reply(bool ok, string message) => new() { RequestId = upload.RequestId, Ok = ok, Message = message };
            if (upload.Trusted && Kind(upload.Kind) is string single)
            {
                Store(upload.World, upload.Character, single, upload.Portrait);
                return Reply(true, "stored");
            }
            byte[] portrait = upload.Portrait, headshot = upload.Headshot;
            if (portrait == null || portrait.Length == 0 || portrait.Length > MaxImageBytes || (headshot?.Length ?? 0) > MaxImageBytes)
                return Reply(false, "bad image");
            uint crc = ClientPackets.Crc32(portrait);
            if (!upload.Trusted && (!announced.TryGetValue((world.Realm, upload.Character), out uint expected) || expected != crc))
                return Reply(false, "checksum not announced by that character");
            Store(upload.World, upload.Character, "Portrait", portrait);
            if (headshot?.Length > 0)
                Store(upload.World, upload.Character, "Headshot", headshot);
            log.Info($"Portrait stored for character {upload.Character} of world {upload.World} ({portrait.Length} + {headshot?.Length ?? 0} bytes).");
            return Reply(true, "stored");
        }

        private void Store(uint world, ulong character, string kind, byte[] png)
        {
            Directory.CreateDirectory(Folder);
            string file = $"{world}_{character}_{kind.ToLowerInvariant()}.png";
            File.WriteAllBytes(Path.Combine(Folder, file), png);
            hub.Store.SavePortrait(world, character, kind, file, png.Length);
        }

        /// <summary>
        /// One file of a world's Portraits/ folder (the one-time import): &lt;character&gt;_portrait.png /
        /// _headshot.png, stored as that world's.
        /// </summary>
        public bool ImportFile(uint world, CommerceImportFile file)
        {
            Match m = OldFile.Match(Path.GetFileName(file.Name));
            if (!m.Success || !ulong.TryParse(m.Groups[1].Value, out ulong character) || file.Data.Length == 0 || file.Data.Length > MaxImageBytes)
                return false;
            Store(world, character, Kind(m.Groups[2].Value), file.Data);
            return true;
        }
    }
}
