using LandmarkEmulator.Shared.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// The client packets the commerce service writes, byte for byte what the in-process code wrote (Game/Commerce.cs,
    /// Contests.cs, Portraits.cs, Ratings.cs), in the 0.695 client's numbering: the zone hands them to the session,
    /// which sends them as given (docs/protocol.md §1.6; InGamePurchase subs ≥ 0x30 are the 0.589 ones + 1,
    /// trace/0695/coverage-695.md §0x45: 33 renumbered, bodies "same"). Every packet starts with the family's two opcode bytes (the zone's
    /// WorldSession.GetOpcode reads them back), then the family's sub as the client reads it (u16 for 0x45, u8 for 0xA7 /
    /// 0xF9, i16 for 0x98).
    /// </summary>
    public static class ClientPackets
    {
        // InGamePurchase subs, 0.695 numbers (as Game/Commerce.cs; docs/commerce.md; 0.589 number in brackets).
        public const ushort SubWalletInfoRequest = 0x0B, SubWalletInfoResponse = 0x0C, SubServerStatusRequest = 0x0D,
            SubServerStatusResponse = 0x0E, SubAccountInfoRequest = 0x1A, SubAccountInfoResponse = 0x1B,
            SubProductCreateResponse = 0x31 /* 0x30 */, SubProductRemove = 0x32 /* 0x31 */, SubProductUpdate = 0x34 /* 0x33 */,
            SubRequestAccountStore = 0x37 /* 0x36 */, SubAccountStoreResponse = 0x39 /* 0x38 */,
            SubGetProductForReview = 0x40 /* 0x3F */, SubReviewProductResponse = 0x41 /* 0x40 */,
            SubSubmitProductReview = 0x43 /* 0x42 */, SubSubmitProductReviewResponse = 0x44 /* 0x43 */,
            SubFollowCreator = 0x35 /* 0x34 */, SubUnFollowCreator = 0x36 /* 0x35 */, SubAddFollowing = 0x3A /* 0x39 */,
            SubRemoveFollowing = 0x3B /* 0x3A */;

        /// <summary>An InGamePurchaseBase 0x45 packet: bytes 45 00, u16 sub, body.</summary>
        public static byte[] InGamePurchase(ushort sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte> { 0x45, 0x00 };
            var w = new GamePacketWriter(output);
            w.WriteUShort(sub);
            body?.Invoke(w);
            return output.ToArray();
        }

        /// <summary>A RatingBase 0xA7 packet: bytes A7 00, u8 sub, body.</summary>
        public static byte[] Rating(byte sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte> { 0xA7, 0x00 };
            var w = new GamePacketWriter(output);
            w.Write(sub);
            body(w);
            return output.ToArray();
        }

        /// <summary>A ClaimsBase 0xF9 packet: bytes F9 00, u8 sub, body.</summary>
        public static byte[] Claims(byte sub, Action<GamePacketWriter> body)
        {
            var output = new List<byte> { 0xF9, 0x00 };
            var w = new GamePacketWriter(output);
            w.Write(sub);
            body(w);
            return output.ToArray();
        }

        /// <summary>
        /// WalletInfoResponse 0x0C (reader FUN_143185620, handler FUN_1431adaf0): u32 result 1, u8, u32 balance 0, u32,
        /// string, string, u8 "zip code on file" 1 — the empty wallet of Commerce.SendWalletInfo.
        /// </summary>
        public static byte[] WalletInfo() => InGamePurchase(SubWalletInfoResponse, w =>
        {
            w.Write(1u);
            w.Write((byte)0);
            w.Write(0u);
            w.Write(0u);
            w.Write("");
            w.Write("");
            w.Write((byte)1);
        });

        /// <summary>
        /// ServerStatusResponse 0x0E {bool} (handler FUN_1431ab880: the byte, nothing after it, goes to Lua
        /// OnReceiveServerStatus; "same" in 0.695, FUN_143256750). True from the service (and from in-process Game/Commerce); spec §7: false from the zone
        /// while the service is unreachable [I: the bool read as "the store service is up"].
        /// </summary>
        public static byte[] ServerStatus(bool up) => InGamePurchase(SubServerStatusResponse, w => w.Write(up));

        /// <summary>AccountInfoResponse 0x1B (FUN_1431a67b0): u32 1, string "US", string "USD", bool zip code, string.</summary>
        public static byte[] AccountInfo() => InGamePurchase(SubAccountInfoResponse, w =>
        {
            w.Write(1u);
            w.Write("US");     // Player Studio sellers had to be US accounts (FUN_1431b4c10)
            w.Write("USD");
            w.Write(true);     // zip code on file
            w.Write("");
        });

        /// <summary>CommerceProductCreateResponse 0x31 {u32 status, string} (0.589 0x30, FUN_1431a7430 → FUN_14319fc60).</summary>
        public static byte[] CreateResponse(uint status) => InGamePurchase(SubProductCreateResponse, w =>
        {
            w.Write(status);
            w.Write("");
        });

        /// <summary>
        /// AccountStore 0x39 (0.589 0x38, reader FUN_143182f10): the followings (u32 n × {u64 creator id, string name}, 0.695
        /// FUN_1432333c0), the other store-side lists empty, then u32 n × Product — the player's own products
        /// (Commerce.SendAccountStore).
        /// </summary>
        public static byte[] AccountStore(IReadOnlyList<ProductRow> mine, string creator, IReadOnlyList<(ulong Id, string Name)> followings = null)
            => InGamePurchase(SubAccountStoreResponse, w =>
        {
            w.Write(0ul);
            w.Write(false);
            w.Write((uint)(followings?.Count ?? 0));
            foreach ((ulong id, string name) in followings ?? Array.Empty<(ulong, string)>())
            {
                w.Write(id);
                w.Write(name ?? "");
            }
            w.Write(0u);
            w.Write(0u);
            w.Write(0ul);
            w.Write(0ul);
            w.Write(0ul);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write(0u);
            w.Write((uint)mine.Count);
            foreach (ProductRow p in mine)
                WriteProduct(w, p, creator);
        });

        /// <summary>ReviewProductResponse 0x41 (0.589 0x40, FUN_1431a7fc0, reader FUN_1431834e0): a Product, then u32 products waiting.</summary>
        public static byte[] ReviewProduct(ProductRow product, string creator, int waiting) => InGamePurchase(SubReviewProductResponse, w =>
        {
            WriteProduct(w, product, creator);
            w.Write((uint)waiting);
        });

        /// <summary>
        /// AddFollowing 0x3A (0.589 0x39; 0.695 reader FUN_14322da80): u64 FollowingId, bool result, string FollowingName
        /// (Commerce.Follow).
        /// </summary>
        public static byte[] AddFollowing(ulong creator, bool ok, string name) => InGamePurchase(SubAddFollowing, w =>
        {
            w.Write(creator);
            w.Write(ok);
            w.Write(name ?? "");
        });

        /// <summary>RemoveFollowing 0x3B (0.589 0x3A; 0.695 reader FUN_14322de50): u64 FollowingId, bool result.</summary>
        public static byte[] RemoveFollowing(ulong creator, bool ok) => InGamePurchase(SubRemoveFollowing, w =>
        {
            w.Write(creator);
            w.Write(ok);
        });

        /// <summary>SubmitProductReviewResponse 0x44 {bool} (0.589 0x43, FUN_1431a8620, Lua GameEvents:OnReviewComplete).</summary>
        public static byte[] ReviewDone(bool ok) => InGamePurchase(SubSubmitProductReviewResponse, w => w.Write(ok));

        /// <summary>
        /// A Product (reader FUN_143184930), as Commerce.WriteProduct: u64 ProductId, u64, u32 BundleId (= the id, never
        /// 0), u32 Quantity, u32 Price, u32, u32 Rating (approvals), string Name, string Description, u64 Created (Unix s),
        /// u64 LastPurchased, u32 Status, string SearchTags, u32 n × ProductItem (u64 item, u32 type, u32 quantity, u64,
        /// bool, string: a design's TemplateData XML with CreatorName). Null = the empty product (id 0).
        /// </summary>
        public static void WriteProduct(GamePacketWriter w, ProductRow p, string creator)
        {
            w.Write(p?.Id ?? 0ul);
            w.Write(0ul);
            w.Write((uint)(p?.Id ?? 0));
            w.Write(p == null ? 0u : 1u);
            w.Write(p?.Price ?? 0u);
            w.Write(0u);
            w.Write(p?.Approvals ?? 0u);
            w.Write(p?.Name ?? "");
            w.Write(p?.Description ?? "");
            w.Write(p == null ? 0ul : (ulong)new DateTimeOffset(DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds());
            w.Write(0ul);
            w.Write((uint)(p?.Status ?? 0));
            w.Write(p?.Tags ?? "");
            if (p == null)
            {
                w.Write(0u);
                return;
            }
            w.Write(1u);
            w.Write(p.ItemId);
            w.Write(p.ItemType);
            w.Write(1u);
            w.Write(0ul);
            w.Write(false);
            w.Write(p.ItemType == CommerceStore.ItemDesign ? TemplateDataXml(creator) : "");
        }

        /// <summary>The review window's TemplateData XML (CreatorName as attribute and element, FUN_142b79310's two modes).</summary>
        private static string TemplateDataXml(string creator)
        {
            string name = System.Security.SecurityElement.Escape(creator ?? "");
            return $"<TemplateData CreatorName=\"{name}\"><CreatorName>{name}</CreatorName></TemplateData>";
        }

        /// <summary>
        /// RatingBase VoteReply 0x12 (reader FUN_142d02880): u64 voter, string item id (as the request sent it), bool
        /// counted, u64 the item's vote total — the contest vote's answer (Ratings.HandleAddVote, category "Contests").
        /// </summary>
        public static byte[] VoteReply(ulong voter, string itemId, bool counted, ulong total) => Rating(0x12, w =>
        {
            w.Write(voter);
            w.Write(itemId ?? "");
            w.Write(counted);
            w.Write(total);
        });

        /// <summary>
        /// RequestContestsReply 0x1E (FUN_142d09110): u64 player, u32 n × {string key, string name, u32 id, u32 phase,
        /// u64 submissions start, u64 voting start, u64 judging start} (Contests.SendList).
        /// </summary>
        public static byte[] ContestList(ulong player, IReadOnlyList<ContestRow> contests, Func<ContestRow, uint> phase) => Rating(0x1E, w =>
        {
            w.Write(player);
            w.Write((uint)contests.Count);
            foreach (ContestRow c in contests)
            {
                w.Write(c.Key);
                w.Write(c.Name);
                w.Write(c.Id);
                w.Write(phase(c));
                w.Write((ulong)c.SubmissionsStart);
                w.Write((ulong)c.VotingStart);
                w.Write((ulong)c.JudgingStart);
            }
        });

        /// <summary>ContestEntryResults 0xF9/0x18 (FUN_142d08640): bool ok, string site name, string contest name.</summary>
        public static byte[] ContestEntryResults(bool ok, string site, string contest) => Claims(0x18, w =>
        {
            w.Write(ok);
            w.Write(site ?? "");
            w.Write(contest ?? "");
        });

        /// <summary>
        /// FotomatBase PlayerImageData (sub 3; reader FUN_142ebab00: u16 opcode, <b>i16</b> sub, u64 character, string kind,
        /// bool, appearance block FUN_142eba940, then the image FUN_142b9ab00): bytes 98 00, 03 00, u64 character, string
        /// kind, bool, an empty appearance block, u32 n + the PNG (Portraits.Build). The sub is written as two bytes: a
        /// <c>short</c> passed to GamePacketWriter.Write picks the <c>int</c> overload (4 bytes), which shifted every
        /// field after it (fixed in Portraits.Build too, 2026-10-16).
        /// </summary>
        public static byte[] PlayerImageData(ulong character, string kind, byte[] png)
        {
            var output = new List<byte> { 0x98, 0x00 };
            var w = new GamePacketWriter(output);
            w.WriteUShort(3);
            w.Write(character);
            w.Write(kind);
            w.Write(true);
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

        /// <summary>CRC-32 as the client computes it (FUN_142ac1800: the standard table, start ~0, final ~), as Portraits.Crc32.</summary>
        public static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in data)
                c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return ~c;
        }

        private static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();
    }

    /// <summary>
    /// The gallery tag rules the commerce side needs (copied from the zone's Game/Gallery.cs, trace/0695/gallery-695.md):
    /// the client's "PlayMode" word (FUN_142d99b70) and the "contest_..." markers Claims.EnterContest puts in a listing's
    /// tags. The three drop-down checks (TagDefinitions.txt) stay in the zone, which has the game tables.
    /// </summary>
    public static class GalleryTags
    {
        public const string PlayModeWord = "PlayMode";
        /// <summary>Contest tag words start with this (Contests.TagPrefix; the client refuses them in site tags).</summary>
        public const string ContestPrefix = "contest_";

        private static string[] Words(string tags)
            => (tags ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool IsContestTag(string word) => word.StartsWith(ContestPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>A tag string without the client's "PlayMode" word and contest markers.</summary>
        public static string CleanTags(string tags)
            => string.Join(' ', Words(tags).Where(w => !string.Equals(w, PlayModeWord, StringComparison.OrdinalIgnoreCase) && !IsContestTag(w)));

        /// <summary>The contest markers of a listing's tags, space-separated ("" for none).</summary>
        public static string ContestTagsOf(string tags) => string.Join(' ', Words(tags).Where(IsContestTag));

        /// <summary><paramref name="tags"/> (markers dropped) with <paramref name="contestTags"/> appended (null = none).</summary>
        public static string WithContestTags(string tags, string contestTags)
        {
            var words = Words(tags).Where(w => !IsContestTag(w)).ToList();
            foreach (string marker in Words(contestTags))
                if (IsContestTag(marker) && !words.Contains(marker, StringComparer.OrdinalIgnoreCase))
                    words.Add(marker);
            return string.Join(' ', words);
        }
    }
}
