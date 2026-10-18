using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Build site screenshots (Site Management → Screenshots; the gallery shows them). Up to four per site (index 0 =
    /// the primary one).
    /// <list type="bullet">
    /// <item>Lua Claims.TakeScreenshot(site, index) (FUN_143533660) has the client capture a 1366 × 768 shot and a 128 ×
    /// 128 thumbnail (FUN_143148b50, GameEvents:OnClaimScreenshotTaken) and upload them like design shots: MarketplaceImage
    /// 0x107 sub 1 {u64 image id ((character &lt;&lt; 32) | time), u64 site, string folder (empty), string "large" /
    /// "small", u32, u32 index, u32 length + JPEG} (<see cref="Templates.StoreImage"/> hands those here).</item>
    /// <item>Then InGamePurchase 0x45 sub 0x4A ModifyScreenshots (0.589 0x49) {u32 action (1 taken, 2 clear, 3 make
    /// primary), u32 index, u64 site, u64 image id, u32 n × ClaimsImage (FUN_1435e6670, 0x30 bytes each in memory)}
    /// [P, 0.695 serializer FUN_1435e7280 → body FUN_1435e6a30: header FUN_14322b8a0 (u16 0x45, u16 sub), u32 +0x18,
    /// u32 +0x1c, u64 +0x20, u64 +0x28, array +0x30 (FUN_1435e6c40: u32 count, elements); 0.589 FUN_143534a60 the same].
    /// The Lua natives Claims.TakeScreenshot FUN_1435e5500 / ClearScreenshots FUN_1435e3b70 / MakePrimaryScreenshot
    /// FUN_1435e3c60 (re/out/luabind_695.tsv) send actions 1 / 2 / 3 with an empty array; only TakeScreenshot fills the
    /// image id (the upload's (character &lt;&lt; 32) | time) — the other two leave that u64 uninitialised (stack, never
    /// stored: disassembly of FUN_1435e3b70), so the server reads only action, index and site. The window keeps its
    /// close button hidden until ModifyScreenshotsResponse 0x45 sub 0x46 (0.589 0x45) {u64 site, u64, bool success}
    /// (0.589 reader FUN_143183890, 0.695 FUN_14322e350; handler FUN_1431a9a70 → GameEvents:OnScreenshotResponse).</item>
    /// <item>Served at /uploads/lm/images/claims[N]/(small|large)/(site guid as 24 digits, "/" every 3).jpg, as design
    /// shots are (<see cref="WebPages"/>).</item>
    /// </list>
    /// </summary>
    public static class SiteScreenshots
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>0.695 numbers [P] (coverage-695.md §0x45; id table cIdModifyScreenshots 0x4A, cIdCommerceModifyScreenShotsResponse 0x46).</summary>
        public const ushort ModifyRequestSub = 0x4A, ModifyResponseSub = 0x46;
        private const uint ActionTaken = 1, ActionClear = 2, ActionMakePrimary = 3;
        private const int MaxImages = 4, MaxImageBytes = 1024 * 1024;

        private static string Folder => Path.Combine(AppContext.BaseDirectory, "SiteImages");
        private static string FileOf(ulong site, int index, string size) => Path.Combine(Folder, $"{site}_{index}_{size}.jpg");

        /// <summary>An uploaded site shot (folder "images/claims[N]"): kept when the uploader owns the site.</summary>
        public static void Store(WorldSession session, ulong site, string size, uint index, byte[] jpeg)
        {
            if ((size != "large" && size != "small") || index >= MaxImages || jpeg.Length > MaxImageBytes || jpeg.Length < 4
                || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || Claims.OwnerOf(site) != session.PlayerGuid)
            {
                log.Debug($"{session.CharacterName}: refused a {size} shot ({jpeg.Length} bytes) for site {site}.");
                return;
            }
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(FileOf(site, (int)index, size), jpeg);
            log.Info($"{session.CharacterName} uploaded the {size} screenshot {index} of site {site} ({jpeg.Length} bytes).");
        }

        /// <summary>A stored shot for the web route, or null.</summary>
        public static byte[] Image(ulong site, int index, string size)
        {
            string file = FileOf(site, index, size);
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        }

        /// <summary>Whether a shot is stored (the gallery page shows the primary "small" one).</summary>
        public static bool HasImage(ulong site, int index, string size) => File.Exists(FileOf(site, index, size));

        /// <summary>The web path of a shot, as <see cref="WebPages"/> serves it (index 0 = "claims", else "claimsN").</summary>
        public static string ImagePath(ulong site, int index, string size)
            => $"/uploads/lm/images/claims{(index == 0 ? "" : index.ToString())}/{size}/{site}.jpg";

        /// <summary>ModifyScreenshots from Site Management: taken / clear / make primary, then the window's answer.</summary>
        public static void HandleModify(WorldSession session, byte[] body)
        {
            bool ok = false;
            ulong site = 0;
            try
            {
                // {u32 action, u32 index, u64 site, u64 image id, u32 n × ClaimsImage}: the first three are all the
                // server needs (the image id is garbage for actions 2 / 3, and the images arrive as uploads).
                var r = new GamePacketReader(body);
                uint action = r.ReadUInt();
                uint index = r.ReadUInt();
                site = r.ReadULong();
                if (Claims.OwnerOf(site) == session.PlayerGuid && index < MaxImages)
                {
                    switch (action)
                    {
                        case ActionTaken:
                            ok = true;    // the image itself arrives as an upload
                            break;
                        case ActionClear:
                            foreach (string size in new[] { "large", "small" })
                                if (File.Exists(FileOf(site, (int)index, size)))
                                    File.Delete(FileOf(site, (int)index, size));
                            ok = true;
                            break;
                        case ActionMakePrimary:
                            foreach (string size in new[] { "large", "small" })
                                Swap(FileOf(site, 0, size), FileOf(site, (int)index, size));
                            ok = true;
                            break;
                    }
                }
                log.Info($"{session.CharacterName}: site {site} screenshot action {action} index {index}: {(ok ? "done" : "refused")}.");
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable ModifyScreenshots from {session.CharacterName}: {e.Message}");
            }
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.WriteUShort(ModifyResponseSub);
            w.Write(site);
            w.Write(0ul);
            w.Write(ok);
            session.EnqueueMessage(ZoneMessageOpcode.InGamePurchaseBase, output.ToArray());
        }

        private static void Swap(string a, string b)
        {
            if (a == b)
                return;
            string temp = a + ".swap";
            if (File.Exists(a))
                File.Move(a, temp, true);
            if (File.Exists(b))
                File.Move(b, a, true);
            if (File.Exists(temp))
                File.Move(temp, b, true);
        }
    }
}
