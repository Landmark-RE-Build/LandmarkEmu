using LandmarkEmulator.Internal.Social;
using System;
using System.Collections.Generic;

namespace LandmarkEmulator.Internal.Commerce
{
    /// <summary>
    /// Service messages between a world's zone process and the shared commerce service (`commerce`, one for all worlds;
    /// trace/architecture/world-channel-service.md §5.2, §6.1, §6.3; build step 8). They travel as a
    /// <see cref="ServicePacket"/> (u16 family, u32 sub, body) on the zone's link to the service (the zone dials
    /// COMMERCE_ADDRESS, like SOCIAL_ADDRESS); client packets from the service to a player travel on the same link as
    /// <see cref="TunnelToExternal"/> (the 0.695 client's numbering, sent as given; docs/protocol.md §1.6).
    /// <para>
    /// Family and sub: the client's ids where a client request is handed on — InGamePurchaseBase 0x45 (u16 subs:
    /// WalletInfoRequest 0x0B, ServerStatusRequest 0x0D, AccountInfoRequest 0x1A, ProductRemove 0x32, ProductUpdate 0x34,
    /// RequestAccountStore 0x37, GetProductForReview 0x40, SubmitProductReview 0x43 … (0.695 numbers); docs/commerce.md), RatingBase 0xA7
    /// (RequestContestsRequest 0x1D, AddVote 8 category "Contests"), FotomatBase 0x98 (PortraitDataRequest 2),
    /// AddClientPortraitCrc 0x75 (no sub: 0) — and CommerceProductCreate 0x45 / 0x2F (the client's name; spec §6.3). Every
    /// other message is <b>ours</b> (live's internal layouts are not found yet, L48): the family of the client area it
    /// belongs to and a sub from 0x10000 up (spec §6.2), above every client sub, so it is never taken for a client packet.
    /// The ids 0x10010-0x10014 and 0x10020 are the spec's (§6.3; 0x10012 / 0x10013 run zone → commerce → zone here, see
    /// <see cref="CommerceGrantDesignRequest"/>); the others are additions. The spec's CommerceClientReply 0x10000 /
    /// ...List 0x10001 are not used: the service answers a player with <see cref="TunnelToExternal"/> on the zone's link,
    /// as the social service does.
    /// Bodies are ours, little endian, strings u32 length + UTF-8 (<see cref="InternalWriter"/>).
    /// </para>
    /// <para>
    /// <b>Deviation from the spec (recorded there, §6.3):</b> the spec routes the commerce requests World channel → `commerce`
    /// directly (WC relays CommerceClientRequest). Here the zone is the commerce service's client, the way it is the social
    /// service's: the zone already receives every channel-1 commerce request (the world channel hands them on, or the
    /// gateway sends channel 1 to the zone while the world channel is away, §8 step 2), it holds what commerce must check
    /// first (the design's or the build site's owner, the site's tags, the contest-entry limiter) and the session's
    /// account and build, and it keeps the caches the zone itself reads (IsOnMarketplace, the gallery listings). One link
    /// per world, no second relay hop, and no commerce code in the world-channel container.
    /// </para>
    /// <para>
    /// <b>Shared keys:</b> (world id, character guid) everywhere (spec §6.1). A world also names its <b>realm</b> in
    /// <see cref="CommerceHello"/>: the character database it uses. Worlds sharing one character database (the Docker
    /// layout: lmemu_character for every world) share character guids and design (template) ids, so the service matches a
    /// player's own products, reviews, taken designs and portraits on (realm of the row's world, guid); build sites
    /// (claims) are per world and matched on (world, claim).
    /// </para>
    /// </summary>
    public static class CommerceMessages
    {
        /// <summary>InGamePurchaseBase 0x45 (the client's family; u16 subs on the client's wire).</summary>
        public const ushort CommerceFamily = 0x45;
        /// <summary>RatingBase 0xA7 (u8 subs): contest list and contest votes.</summary>
        public const ushort RatingFamily = 0xA7;
        /// <summary>ClaimsBase 0xF9 (u8 subs): contest entries (SubmitContestEntry 0x41 → ContestEntryResults 0x18).</summary>
        public const ushort ClaimsFamily = 0xF9;
        /// <summary>FotomatBase 0x98 (i16 subs): portraits.</summary>
        public const ushort FotomatFamily = 0x98;
        /// <summary>PacketAddClientPortraitCrc 0x75 (no sub).</summary>
        public const ushort PortraitCrcFamily = 0x75;

        // ---- client subs handed on ----------------------------------------------------------------------------------
        /// <summary>CommerceProductCreate 0x45/0x2F (the zone has checked the item; <see cref="CommerceProductCreate"/>).</summary>
        public const uint ProductCreateSub = 0x2F;
        /// <summary>RatingBase RequestContestsRequest 0x1D and AddVote 8 (category "Contests").</summary>
        public const uint RequestContestsSub = 0x1D, AddVoteSub = 8;
        /// <summary>FotomatBase PortraitDataRequest 2.</summary>
        public const uint PortraitDataRequestSub = 2;

        // ---- ours, InGamePurchase family ------------------------------------------------------------------------------
        public const uint HelloSub = 0x10002;
        public const uint MarketplaceStatusSub = 0x10010, GalleryStatusSub = 0x10011, GrantDesignRequestSub = 0x10012,
            GrantDesignReplySub = 0x10013, MarketSnapshotSub = 0x10014;
        public const uint StudioListRequestSub = 0x10015, StudioListReplySub = 0x10016, DesignTakenSub = 0x10017, NoticeSub = 0x10018,
            ResultMailSub = 0x10019, ResultMailSentSub = 0x1001A, AdminRequestSub = 0x1001B, AdminReplySub = 0x1001C,
            ImportRequestSub = 0x1001D, ImportFileSub = 0x1001E, ImportDoneSub = 0x1001F;
        /// <summary>Player Studio sales: the creator's share to mail (<see cref="CommercePayout"/>) and its confirmation.</summary>
        public const uint PayoutSub = 0x10021, PayoutSentSub = 0x10022;
        /// <summary>
        /// Showcase following (added 2026-10-18): the page's list / edits (<see cref="CommerceFollowRequest"/> →
        /// <see cref="CommerceFollowReply"/>) and the daily digest's mail (<see cref="CommerceDigestMail"/> →
        /// <see cref="CommerceDigestMailSent"/>).
        /// </summary>
        public const uint FollowRequestSub = 0x10023, FollowReplySub = 0x10024, DigestMailSub = 0x10025, DigestMailSentSub = 0x10026;
        /// <summary>
        /// A ProductCreate's typed outcome for the zone that took its listing fee (<see cref="CommerceProductCreateResult"/>),
        /// and the zone's question after a lost answer (<see cref="CommerceProductCreateCheck"/>); added 2026-10-18 (review #4).
        /// </summary>
        public const uint ProductCreateResultSub = 0x10027, ProductCreateCheckSub = 0x10028;
        // ---- ours, Claims family / Fotomat family ---------------------------------------------------------------------
        public const uint ContestEntrySubmitSub = 0x10020;
        public const uint PortraitUploadSub = 0x10030, PortraitUploadReplySub = 0x10031;

        /// <summary><see cref="CommerceImportRequest.What"/>: the world's Contests/ files (contests.json, entries.json, shots/).</summary>
        public const uint ImportContests = 1;
        /// <summary><see cref="CommerceImportRequest.What"/>: the world's Portraits/ files (&lt;character&gt;_portrait.png / _headshot.png).</summary>
        public const uint ImportPortraits = 2;

        /// <summary><see cref="CommerceGrantDesignReply.Result"/> values.</summary>
        public const byte GrantOk = 0, GrantNotListed = 1, GrantOwnDesign = 2, GrantGone = 3, GrantAlreadyHave = 4;

        /// <summary>The commerce message in a service packet, or null when it isn't one of these.</summary>
        public static ISocialMessage Parse(ServicePacket packet)
        {
            var r = new InternalReader(packet.Body);
            return (packet.Family, packet.Sub) switch
            {
                (CommerceFamily, ProductCreateSub) => CommerceProductCreate.Read(r),
                (CommerceFamily, HelloSub) => CommerceHello.Read(r),
                (CommerceFamily, MarketplaceStatusSub) => CommerceMarketplaceStatus.Read(r),
                (CommerceFamily, GalleryStatusSub) => CommerceGalleryStatus.Read(r),
                (CommerceFamily, GrantDesignRequestSub) => CommerceGrantDesignRequest.Read(r),
                (CommerceFamily, GrantDesignReplySub) => CommerceGrantDesignReply.Read(r),
                (CommerceFamily, MarketSnapshotSub) => CommerceMarketSnapshot.Read(r),
                (CommerceFamily, StudioListRequestSub) => CommerceStudioListRequest.Read(r),
                (CommerceFamily, StudioListReplySub) => CommerceStudioListReply.Read(r),
                (CommerceFamily, DesignTakenSub) => CommerceDesignTaken.Read(r),
                (CommerceFamily, NoticeSub) => CommerceNotice.Read(r),
                (CommerceFamily, ResultMailSub) => CommerceResultMail.Read(r),
                (CommerceFamily, ResultMailSentSub) => CommerceResultMailSent.Read(r),
                (CommerceFamily, AdminRequestSub) => CommerceAdminRequest.Read(r),
                (CommerceFamily, AdminReplySub) => CommerceAdminReply.Read(r),
                (CommerceFamily, ImportRequestSub) => CommerceImportRequest.Read(r),
                (CommerceFamily, ImportFileSub) => CommerceImportFile.Read(r),
                (CommerceFamily, ImportDoneSub) => CommerceImportDone.Read(r),
                (CommerceFamily, PayoutSub) => CommercePayout.Read(r),
                (CommerceFamily, PayoutSentSub) => CommercePayoutSent.Read(r),
                (CommerceFamily, FollowRequestSub) => CommerceFollowRequest.Read(r),
                (CommerceFamily, FollowReplySub) => CommerceFollowReply.Read(r),
                (CommerceFamily, DigestMailSub) => CommerceDigestMail.Read(r),
                (CommerceFamily, DigestMailSentSub) => CommerceDigestMailSent.Read(r),
                (CommerceFamily, ProductCreateResultSub) => CommerceProductCreateResult.Read(r),
                (CommerceFamily, ProductCreateCheckSub) => CommerceProductCreateCheck.Read(r),
                (CommerceFamily, < 0x10000) => CommerceClientRequest.Read(packet.Family, packet.Sub, r),
                (RatingFamily, RequestContestsSub or AddVoteSub) => CommerceClientRequest.Read(packet.Family, packet.Sub, r),
                (FotomatFamily, PortraitDataRequestSub) => CommerceClientRequest.Read(packet.Family, packet.Sub, r),
                (PortraitCrcFamily, 0) => CommerceClientRequest.Read(packet.Family, packet.Sub, r),
                (ClaimsFamily, ContestEntrySubmitSub) => CommerceContestEntrySubmit.Read(r),
                (FotomatFamily, PortraitUploadSub) => CommercePortraitUpload.Read(r),
                (FotomatFamily, PortraitUploadReplySub) => CommercePortraitUploadReply.Read(r),
                _ => null
            };
        }

        internal static byte[] ReadBlob(InternalReader r, uint max)
        {
            uint n = r.U32();
            if (n > max)
                throw new System.IO.InvalidDataException($"{n}-byte blob refused.");
            return r.Bytes((int)n);
        }

        internal static void WriteBlob(InternalWriter w, byte[] data)
        {
            data ??= Array.Empty<byte>();
            w.U32((uint)data.Length).Bytes(data);
        }

        /// <summary>Largest image or design block list carried (the link's frame limit is 16 MB).</summary>
        internal const uint MaxBlob = 12 * 1024 * 1024;
    }

    /// <summary>
    /// Who sent a request (ours; the spec's CommerceClientRequest header, §6.3, without the locale, which the zone does not
    /// keep — SetLocale ends in the world channel — and with the character's name for the service's records): u64
    /// connection (the zone's id for the player's session, which the service's <see cref="TunnelToExternal"/> answers
    /// address), u64 character, u64 account, u32 world, string client build ("0.589" / "0.695" …), string character name.
    /// </summary>
    public sealed record CommerceCaller(ulong ConnectionId, ulong Character, ulong Account, uint World, string ClientBuild, string Name)
    {
        public void Write(InternalWriter w) => w.U64(ConnectionId).U64(Character).U64(Account).U32(World).Str(ClientBuild).Str(Name);
        public static CommerceCaller Read(InternalReader r) => new(r.U64(), r.U64(), r.U64(), r.U32(), r.Str(), r.Str());
    }

    /// <summary>
    /// A client request handed on (zone → commerce; spec CommerceClientRequest): family / sub = the client's, body = the
    /// <see cref="CommerceCaller"/>, then the client's body after its sub, unchanged — except RatingBase AddVote 8, whose
    /// body is ours (string item id as the client sent it, u64 site): the zone has already read the category ("Contests")
    /// and the site id (Ratings.HandleAddVote) and the claim votes go elsewhere (the world channel).
    /// </summary>
    public sealed class CommerceClientRequest : ISocialMessage
    {
        public ushort Family { get; init; } = CommerceMessages.CommerceFamily;
        public uint Sub { get; init; }
        public CommerceCaller Caller { get; init; }
        public byte[] Body { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w)
        {
            Caller.Write(w);
            w.Bytes(Body);
        }

        public static CommerceClientRequest Read(ushort family, uint sub, InternalReader r)
            => new() { Family = family, Sub = sub, Caller = CommerceCaller.Read(r), Body = r.Rest() };
    }

    /// <summary>
    /// A design as it was saved (ours; spec "template snapshot", §6.1, §9 "Template data across worlds"): what a player on
    /// another world needs to take a copy of a listed design. string name, string tags, string author, u32 size x / y / z,
    /// string materials ("material:count;..."), u32 n + the block list exactly as the client sent it (TemplateModel.Blocks).
    /// </summary>
    public sealed record TemplateSnapshot(string Name, string Tags, string Author, uint SizeX, uint SizeY, uint SizeZ, string Materials, byte[] Blocks)
    {
        public void Write(InternalWriter w)
        {
            w.Str(Name).Str(Tags).Str(Author).U32(SizeX).U32(SizeY).U32(SizeZ).Str(Materials);
            CommerceMessages.WriteBlob(w, Blocks);
        }

        public static TemplateSnapshot Read(InternalReader r)
            => new(r.Str(), r.Str(), r.Str(), r.U32(), r.U32(), r.U32(), r.Str(), CommerceMessages.ReadBlob(r, CommerceMessages.MaxBlob));
    }

    /// <summary>
    /// CommerceProductCreate (zone → commerce, 0x45/0x2F; spec §6.3): a Player Studio product or gallery listing the zone
    /// has checked — the item exists, belongs to the caller, a site has its three gallery tags, a design costs at least
    /// 25 (Commerce.CreateProduct's checks, which need the zone's templates and claims). The zone answers the client's
    /// refusals (0x30 status 9 / 0xB / 0xC) itself and hands on only the creates that pass; the service does the duplicate
    /// check (0xF), stores the product and answers 0x30. Body: caller, u32 item type (2 design, 3 build site), u64 item
    /// (the design's template id / the site's claim id on the caller's world), u32 price, string name, string description,
    /// string tags (a site's: the "PlayMode" word and contest markers removed, Gallery.CleanTags), string headline,
    /// u32 voxels (a design's solid voxel count, for the Showcase), bool snapshot, then the design's
    /// <see cref="TemplateSnapshot"/>, then (since 2026-10-18) u64 create id: when not 0 the service also answers the zone
    /// with <see cref="CommerceProductCreateResult"/> for that id, which settles the listing fee (review #4). Trailing and
    /// read only when present, so a service or world built before it still reads the rest.
    /// </summary>
    public sealed class CommerceProductCreate : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.ProductCreateSub;
        public CommerceCaller Caller { get; init; }
        public uint ItemType { get; init; }
        public ulong ItemId { get; init; }
        public uint Price { get; init; }
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Tags { get; init; } = "";
        public string Headline { get; init; } = "";
        public uint Voxels { get; init; }
        public TemplateSnapshot Snapshot { get; init; }
        /// <summary>The zone's id for this create (0: no typed result wanted, e.g. a world built before 2026-10-18).</summary>
        public ulong CreateId { get; init; }

        public void Write(InternalWriter w)
        {
            Caller.Write(w);
            w.U32(ItemType).U64(ItemId).U32(Price).Str(Name).Str(Description).Str(Tags).Str(Headline).U32(Voxels).Bool(Snapshot != null);
            Snapshot?.Write(w);
            w.U64(CreateId);
        }

        public static CommerceProductCreate Read(InternalReader r)
        {
            CommerceCaller caller = CommerceCaller.Read(r);
            uint type = r.U32();
            ulong item = r.U64();
            uint price = r.U32();
            string name = r.Str(), description = r.Str(), tags = r.Str(), headline = r.Str();
            uint voxels = r.U32();
            TemplateSnapshot snapshot = r.Bool() ? TemplateSnapshot.Read(r) : null;
            ulong createId = r.Remaining >= 8 ? r.U64() : 0;
            return new()
            {
                Caller = caller, ItemType = type, ItemId = item, Price = price, Name = name, Description = description, Tags = tags,
                Headline = headline, Voxels = voxels, Snapshot = snapshot, CreateId = createId
            };
        }
    }

    /// <summary>
    /// CommerceProductCreateResult (ours, commerce → zone, 0x45/0x10027): u64 create id (<see cref="CommerceProductCreate.CreateId"/>),
    /// u32 status (the client's CreateResponse 0x31 status: 1 stored, anything else not stored), u64 product (0 when not
    /// stored). Sent next to the tunnelled 0x31, and as the answer to a <see cref="CommerceProductCreateCheck"/>. The zone
    /// settles the create's listing fee by the id, not by the order of the answers (review 2026-10-18 #4).
    /// </summary>
    public sealed class CommerceProductCreateResult : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.ProductCreateResultSub;
        public ulong CreateId { get; init; }
        public uint Status { get; init; }
        public ulong Product { get; init; }

        public void Write(InternalWriter w) => w.U64(CreateId).U32(Status).U64(Product);
        public static CommerceProductCreateResult Read(InternalReader r) => new() { CreateId = r.U64(), Status = r.U32(), Product = r.U64() };
    }

    /// <summary>
    /// CommerceProductCreateCheck (ours, zone → commerce, 0x45/0x10028): u64 create id, caller, u32 item type, u64 item. A
    /// create whose result did not come (timeout, the link lost): the service answers <see cref="CommerceProductCreateResult"/>
    /// for that id from its recent results, else from its store — status 1 when the caller has a live product for that
    /// item, 0 when nothing was stored.
    /// </summary>
    public sealed class CommerceProductCreateCheck : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.ProductCreateCheckSub;
        public ulong CreateId { get; init; }
        public CommerceCaller Caller { get; init; }
        public uint ItemType { get; init; }
        public ulong ItemId { get; init; }

        public void Write(InternalWriter w)
        {
            w.U64(CreateId);
            Caller.Write(w);
            w.U32(ItemType).U64(ItemId);
        }

        public static CommerceProductCreateCheck Read(InternalReader r)
        {
            ulong id = r.U64();
            CommerceCaller caller = CommerceCaller.Read(r);
            return new() { CreateId = id, Caller = caller, ItemType = r.U32(), ItemId = r.U64() };
        }
    }

    /// <summary>
    /// CommerceHello (ours, zone → commerce, 0x45/0x10002): the first message on every link — u32 world, string realm (the
    /// world's character database, see <see cref="CommerceMessages"/>), u32 world name text, u32 local data (bit 1: the
    /// world has Contests/ files, bit 2: Portraits/ files, for the one-time import). The service answers with
    /// <see cref="CommerceMarketSnapshot"/> (the spec's "the zone asks for every live product of its world", §6.3), sends
    /// the result mails still owed to that realm (<see cref="CommerceResultMail"/>) and, once per world, asks for its files
    /// (<see cref="CommerceImportRequest"/>).
    /// </summary>
    public sealed class CommerceHello : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.HelloSub;
        public uint WorldId { get; init; }
        public string Realm { get; init; } = "";
        public uint WorldNameText { get; init; }
        public uint LocalData { get; init; }

        public void Write(InternalWriter w) => w.U32(WorldId).Str(Realm).U32(WorldNameText).U32(LocalData);
        public static CommerceHello Read(InternalReader r) => new() { WorldId = r.U32(), Realm = r.Str(), WorldNameText = r.U32(), LocalData = r.U32() };
    }

    /// <summary>
    /// A build site's gallery listing as the zone keeps it (ours): u64 site (claim on the receiving world), u64 listed at
    /// (Unix seconds; "rising stars" = newest listings, P4670), string contest markers of the listing ("contest_..."), u32 n
    /// × string the names of the contests the site is entered in (the Showcase gallery entry).
    /// </summary>
    public sealed record GalleryListing(ulong Claim, long ListedAt, string ContestTags, IReadOnlyList<string> ContestNames)
    {
        public void Write(InternalWriter w)
        {
            w.U64(Claim).U64((ulong)ListedAt).Str(ContestTags).U32((uint)ContestNames.Count);
            foreach (string name in ContestNames)
                w.Str(name);
        }

        public static GalleryListing Read(InternalReader r)
        {
            ulong claim = r.U64();
            long at = (long)r.U64();
            string tags = r.Str();
            uint n = r.U32();
            SocialMessages.CheckCount(n, 1000, "contest names");
            var names = new List<string>((int)n);
            for (uint i = 0; i < n; i++)
                names.Add(r.Str());
            return new GalleryListing(claim, at, tags, names);
        }
    }

    /// <summary>
    /// CommerceMarketplaceStatus (commerce → zone, 0x45/0x10010; spec §6.3): u64 template, u64 owner, bool on the market
    /// (a product waiting for review or listed). Replaces Commerce.IsOnMarketplace: the zone caches it and re-sends the
    /// design's TemplateBasicData (Templates.SendUpdated) to its owner when online there, so the Designs window files it
    /// under Player Studio. Sent to every linked world of the product's realm (template ids are per character database).
    /// </summary>
    public sealed class CommerceMarketplaceStatus : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.MarketplaceStatusSub;
        public ulong Template { get; init; }
        public ulong Owner { get; init; }
        public bool OnMarket { get; init; }

        public void Write(InternalWriter w) => w.U64(Template).U64(Owner).Bool(OnMarket);
        public static CommerceMarketplaceStatus Read(InternalReader r) => new() { Template = r.U64(), Owner = r.U64(), OnMarket = r.Bool() };
    }

    /// <summary>
    /// CommerceGalleryStatus (commerce → zone, 0x45/0x10011; spec §6.3 "u64 claim, bool listed"): bool listed, then the
    /// listing (<see cref="GalleryListing"/>: when, contest markers, contest names) — the spec's body plus what the
    /// Showcase gallery and the like rule read (Commerce.IsOnGallery / Listings, Contests.NamesOf). Sent to the site's
    /// world when its listing or its contest entries change.
    /// </summary>
    public sealed class CommerceGalleryStatus : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.GalleryStatusSub;
        public bool Listed { get; init; }
        public GalleryListing Listing { get; init; }

        public void Write(InternalWriter w)
        {
            w.Bool(Listed);
            Listing.Write(w);
        }

        public static CommerceGalleryStatus Read(InternalReader r) => new() { Listed = r.Bool(), Listing = GalleryListing.Read(r) };
    }

    /// <summary>
    /// CommerceMarketSnapshot (commerce → zone, 0x45/0x10014; spec §6.3): the whole market as one world needs it, after
    /// every <see cref="CommerceHello"/> — u32 n × u64 the realm's designs on the market, u32 n × <see cref="GalleryListing"/>
    /// this world's listed build sites, u32 n × <see cref="GalleryListing"/> this world's sites in contests but not listed
    /// (their contest names only; a site taken off the gallery loses its entries, so normally none).
    /// </summary>
    public sealed class CommerceMarketSnapshot : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.MarketSnapshotSub;
        public IReadOnlyList<ulong> Designs { get; init; } = Array.Empty<ulong>();
        public IReadOnlyList<GalleryListing> Listings { get; init; } = Array.Empty<GalleryListing>();
        public IReadOnlyList<GalleryListing> Unlisted { get; init; } = Array.Empty<GalleryListing>();

        public void Write(InternalWriter w)
        {
            w.U32((uint)Designs.Count);
            foreach (ulong design in Designs)
                w.U64(design);
            w.U32((uint)Listings.Count);
            foreach (GalleryListing listing in Listings)
                listing.Write(w);
            w.U32((uint)Unlisted.Count);
            foreach (GalleryListing listing in Unlisted)
                listing.Write(w);
        }

        public static CommerceMarketSnapshot Read(InternalReader r)
        {
            uint n = r.U32();
            SocialMessages.CheckCount(n, 1_000_000, "designs");
            var designs = new List<ulong>((int)n);
            for (uint i = 0; i < n; i++)
                designs.Add(r.U64());
            return new() { Designs = designs, Listings = ReadListings(r), Unlisted = ReadListings(r) };
        }

        private static List<GalleryListing> ReadListings(InternalReader r)
        {
            uint n = r.U32();
            SocialMessages.CheckCount(n, 1_000_000, "listings");
            var list = new List<GalleryListing>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add(GalleryListing.Read(r));
            return list;
        }
    }

    /// <summary>
    /// CommerceGrantDesignRequest (ours, zone → commerce, 0x45/0x10012): u32 request, caller, u64 product — the Showcase's
    /// Player Studio page took a listed design (Commerce.TakeDesign). The spec's CommerceGrantDesign (§6.3) runs from
    /// commerce to the zone because it puts the web pages in the service; the pages stay on the world here (§6.6
    /// option), so the zone asks and the service answers with what to grant (<see cref="CommerceGrantDesignReply"/>).
    /// </summary>
    public sealed class CommerceGrantDesignRequest : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.GrantDesignRequestSub;
        public uint RequestId { get; init; }
        public CommerceCaller Caller { get; init; }
        public ulong Product { get; init; }

        public void Write(InternalWriter w)
        {
            w.U32(RequestId);
            Caller.Write(w);
            w.U64(Product);
        }

        public static CommerceGrantDesignRequest Read(InternalReader r) => new() { RequestId = r.U32(), Caller = CommerceCaller.Read(r), Product = r.U64() };
    }

    /// <summary>
    /// CommerceGrantDesignReply (commerce → zone, 0x45/0x10013; spec "CommerceGrantDesign … reply: bool ok, string
    /// message"): u32 request, u8 result (<see cref="CommerceMessages.GrantOk"/> …), string message for the page (refusals),
    /// u64 product, string maker, u32 maker's world, u64 maker, u64 the design's template id on the maker's world, string
    /// that world's realm, bool snapshot + <see cref="TemplateSnapshot"/>, u32 the creator's price (as set in the client's
    /// submission window: Daybreak Cash on live; added 2026-10-17, read only when present). The zone charges the buyer
    /// (Commerce.StudioPrice: the price in Lumens, nothing with FreeStore on), copies the design from its own character
    /// database when the realm is its own and the template still exists (as the in-process code did), else from the
    /// snapshot, then reports <see cref="CommerceDesignTaken"/> with what it took.
    /// </summary>
    public sealed class CommerceGrantDesignReply : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.GrantDesignReplySub;
        public uint RequestId { get; init; }
        public byte Result { get; init; }
        public string Message { get; init; } = "";
        public ulong Product { get; init; }
        public string Maker { get; init; } = "";
        public uint MakerWorld { get; init; }
        public ulong MakerGuid { get; init; }
        public ulong SourceTemplate { get; init; }
        public string SourceRealm { get; init; } = "";
        public TemplateSnapshot Snapshot { get; init; }

        public uint Price { get; init; }

        public void Write(InternalWriter w)
        {
            w.U32(RequestId).U8(Result).Str(Message).U64(Product).Str(Maker).U32(MakerWorld).U64(MakerGuid).U64(SourceTemplate)
                .Str(SourceRealm).Bool(Snapshot != null);
            Snapshot?.Write(w);
            w.U32(Price);
        }

        public static CommerceGrantDesignReply Read(InternalReader r)
        {
            uint request = r.U32();
            byte result = r.U8();
            string message = r.Str();
            ulong product = r.U64();
            string maker = r.Str();
            uint makerWorld = r.U32();
            ulong makerGuid = r.U64();
            ulong source = r.U64();
            string realm = r.Str();
            TemplateSnapshot snapshot = r.Bool() ? TemplateSnapshot.Read(r) : null;
            uint price = r.Remaining >= 4 ? r.U32() : 0;
            return new()
            {
                RequestId = request, Result = result, Message = message, Product = product, Maker = maker, MakerWorld = makerWorld,
                MakerGuid = makerGuid, SourceTemplate = source, SourceRealm = realm, Snapshot = snapshot, Price = price
            };
        }
    }

    /// <summary>
    /// CommerceDesignTaken (ours, zone → commerce, 0x45/0x10017): caller, u64 product, u64 the copy's template id, string
    /// design name, u32 Lumens the buyer paid (0 = free; added 2026-10-17, read only when present) — the zone made the copy.
    /// The service records it (taken_design: once per player and product), owes the creator their share of what was paid
    /// (<see cref="CommercePayout"/>) and tells the maker (<see cref="CommerceNotice"/>, the in-process sale line).
    /// </summary>
    public sealed class CommerceDesignTaken : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.DesignTakenSub;
        public CommerceCaller Caller { get; init; }
        public ulong Product { get; init; }
        public ulong Copy { get; init; }
        public string DesignName { get; init; } = "";
        public uint Paid { get; init; }

        public void Write(InternalWriter w)
        {
            Caller.Write(w);
            w.U64(Product).U64(Copy).Str(DesignName).U32(Paid);
        }

        public static CommerceDesignTaken Read(InternalReader r)
        {
            CommerceCaller caller = CommerceCaller.Read(r);
            ulong product = r.U64(), copy = r.U64();
            string name = r.Str();
            return new() { Caller = caller, Product = product, Copy = copy, DesignName = name, Paid = r.Remaining >= 4 ? r.U32() : 0 };
        }
    }

    /// <summary>
    /// CommercePayout (ours, commerce → zone, 0x45/0x10021): u64 payout id, u64 creator (character on the zone's realm),
    /// string listing name, string buyer, u32 Lumens paid, u32 the creator's share — a Player Studio sale's share to mail
    /// (Commerce.MailCreatorShare: a system mail with the Lumens attached). Sent to one linked world of the creator's realm
    /// (their own world first), which answers <see cref="CommercePayoutSent"/>; a share with no world of that realm linked
    /// waits in the service's studio_payout table until one links, as the result mails do.
    /// </summary>
    public sealed class CommercePayout : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.PayoutSub;
        public ulong Payout { get; init; }
        public ulong Creator { get; init; }
        public string DesignName { get; init; } = "";
        public string Buyer { get; init; } = "";
        public uint Paid { get; init; }
        public uint Share { get; init; }

        public void Write(InternalWriter w) => w.U64(Payout).U64(Creator).Str(DesignName).Str(Buyer).U32(Paid).U32(Share);
        public static CommercePayout Read(InternalReader r)
            => new() { Payout = r.U64(), Creator = r.U64(), DesignName = r.Str(), Buyer = r.Str(), Paid = r.U32(), Share = r.U32() };
    }

    /// <summary>CommercePayoutSent (ours, zone → commerce, 0x45/0x10022): u64 payout id — its mail went out.</summary>
    public sealed class CommercePayoutSent : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.PayoutSentSub;
        public ulong Payout { get; init; }

        public void Write(InternalWriter w) => w.U64(Payout);
        public static CommercePayoutSent Read(InternalReader r) => new() { Payout = r.U64() };
    }

    /// <summary>
    /// CommerceFollowRequest (ours, zone → commerce, 0x45/0x10023): u32 request, caller, u8 action
    /// (<see cref="List"/>, <see cref="AddCreator"/>, <see cref="AddTags"/>, <see cref="RemoveCreator"/>,
    /// <see cref="RemoveTags"/>), u64 creator (the character guid, on the caller's realm), string text (the creator's
    /// name for an add — the zone resolved a typed name in its own character database, which is the realm's — or the tag
    /// group). The Showcase page's Following tab (WebPages /api/follows, /api/follow, /api/unfollow) on a world whose
    /// commerce is the service; answered with <see cref="CommerceFollowReply"/> (the list after the change).
    /// </summary>
    public sealed class CommerceFollowRequest : ISocialMessage
    {
        public const byte List = 0, AddCreator = 1, AddTags = 2, RemoveCreator = 3, RemoveTags = 4;

        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.FollowRequestSub;
        public uint RequestId { get; init; }
        public CommerceCaller Caller { get; init; }
        public byte Action { get; init; }
        public ulong Creator { get; init; }
        public string Text { get; init; } = "";

        public void Write(InternalWriter w)
        {
            w.U32(RequestId);
            Caller.Write(w);
            w.U8(Action).U64(Creator).Str(Text);
        }

        public static CommerceFollowRequest Read(InternalReader r)
            => new() { RequestId = r.U32(), Caller = CommerceCaller.Read(r), Action = r.U8(), Creator = r.U64(), Text = r.Str() };
    }

    /// <summary>
    /// CommerceFollowReply (ours, commerce → zone, 0x45/0x10024): u32 request, bool ok, string message (for the page),
    /// bool digest on (the service's SHOWCASE_FOLLOW_DIGEST: it runs the digest for its worlds), u32 n × {u64 creator,
    /// string name} the creators followed, u32 n × string the tag groups followed (both oldest first, across the caller's
    /// realm).
    /// </summary>
    public sealed class CommerceFollowReply : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.FollowReplySub;
        public uint RequestId { get; init; }
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public bool DigestOn { get; init; }
        public IReadOnlyList<(ulong Id, string Name)> Creators { get; init; } = Array.Empty<(ulong, string)>();
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

        public void Write(InternalWriter w)
        {
            w.U32(RequestId).Bool(Ok).Str(Message).Bool(DigestOn).U32((uint)Creators.Count);
            foreach ((ulong id, string name) in Creators)
                w.U64(id).Str(name ?? "");
            w.U32((uint)Tags.Count);
            foreach (string tags in Tags)
                w.Str(tags ?? "");
        }

        public static CommerceFollowReply Read(InternalReader r)
        {
            uint request = r.U32();
            bool ok = r.Bool();
            string message = r.Str();
            bool digest = r.Bool();
            uint n = r.U32();
            SocialMessages.CheckCount(n, 10_000, "followed creators");
            var creators = new List<(ulong, string)>((int)n);
            for (uint i = 0; i < n; i++)
                creators.Add((r.U64(), r.Str()));
            n = r.U32();
            SocialMessages.CheckCount(n, 10_000, "followed tag groups");
            var tags = new List<string>((int)n);
            for (uint i = 0; i < n; i++)
                tags.Add(r.Str());
            return new() { RequestId = request, Ok = ok, Message = message, DigestOn = digest, Creators = creators, Tags = tags };
        }
    }

    /// <summary>
    /// CommerceDigestMail (ours, commerce → zone, 0x45/0x10025): u64 mail id, u64 recipient (character on the zone's
    /// realm), string the recipient's name when they last followed (a fallback: the zone greets them by their current
    /// name from its character database), string subject, string body (without the greeting, FollowDigest.Greeting) — one
    /// follower's daily Showcase following mail (FollowService). Modelled on <see cref="CommercePayout"/>: sent to one
    /// linked world of the follower's realm (the world they followed from first), which mails it through its social seam
    /// (sender: the client's Player Studio text 563379) and answers <see cref="CommerceDigestMailSent"/>; a mail with no
    /// world of that realm linked waits in the service's digest_mail table until one links.
    /// </summary>
    public sealed class CommerceDigestMail : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.DigestMailSub;
        public ulong Mail { get; init; }
        public ulong Recipient { get; init; }
        public string RecipientName { get; init; } = "";
        public string Subject { get; init; } = "";
        public string Body { get; init; } = "";

        public void Write(InternalWriter w) => w.U64(Mail).U64(Recipient).Str(RecipientName).Str(Subject).Str(Body);
        public static CommerceDigestMail Read(InternalReader r)
            => new() { Mail = r.U64(), Recipient = r.U64(), RecipientName = r.Str(), Subject = r.Str(), Body = r.Str() };
    }

    /// <summary>CommerceDigestMailSent (ours, zone → commerce, 0x45/0x10026): u64 mail id — it went out (or its recipient is gone).</summary>
    public sealed class CommerceDigestMailSent : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.DigestMailSentSub;
        public ulong Mail { get; init; }

        public void Write(InternalWriter w) => w.U64(Mail);
        public static CommerceDigestMailSent Read(InternalReader r) => new() { Mail = r.U64() };
    }

    /// <summary>
    /// CommerceNotice (ours, commerce → zone, 0x45/0x10018): u64 character, string text — a system chat line for a character
    /// if it is online on that world (Chat.System), sent to every linked world of the character's realm.
    /// </summary>
    public sealed class CommerceNotice : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.NoticeSub;
        public ulong Character { get; init; }
        public string Text { get; init; } = "";

        public void Write(InternalWriter w) => w.U64(Character).Str(Text);
        public static CommerceNotice Read(InternalReader r) => new() { Character = r.U64(), Text = r.Str() };
    }

    /// <summary>
    /// CommerceResultMail (ours, commerce → zone, 0x45/0x10019; spec "SystemMail (to #12)"): u64 product, u64 owner, string
    /// product name, u32 mail kind (0x10 accepted, 0x20 rejected: the client writes sender, subject and body from the kind,
    /// FUN_143586ba0). The zone sends it through its social seam (SocialServices.Service.SendSystemMail: the social
    /// service when SOCIAL_ADDRESS is set, else its own mail), so commerce needs no link to `social`; it answers
    /// <see cref="CommerceResultMailSent"/>. A mail with no world of the owner's realm linked waits in the service's
    /// table (product.mail_pending) until one links.
    /// </summary>
    public sealed class CommerceResultMail : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.ResultMailSub;
        public ulong Product { get; init; }
        public ulong Owner { get; init; }
        public string ProductName { get; init; } = "";
        public uint Kind { get; init; }

        public void Write(InternalWriter w) => w.U64(Product).U64(Owner).Str(ProductName).U32(Kind);
        public static CommerceResultMail Read(InternalReader r) => new() { Product = r.U64(), Owner = r.U64(), ProductName = r.Str(), Kind = r.U32() };
    }

    /// <summary>CommerceResultMailSent (ours, zone → commerce, 0x45/0x1001A): u64 product — its result mail went out.</summary>
    public sealed class CommerceResultMailSent : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.ResultMailSentSub;
        public ulong Product { get; init; }

        public void Write(InternalWriter w) => w.U64(Product);
        public static CommerceResultMailSent Read(InternalReader r) => new() { Product = r.U64() };
    }

    /// <summary>
    /// CommerceStudioListRequest (ours, zone → commerce, 0x45/0x10015): u32 request — the Showcase's Player Studio page
    /// (WebPages /api/studio, Commerce.ListedDesigns).
    /// </summary>
    public sealed class CommerceStudioListRequest : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.StudioListRequestSub;
        public uint RequestId { get; init; }

        public void Write(InternalWriter w) => w.U32(RequestId);
        public static CommerceStudioListRequest Read(InternalReader r) => new() { RequestId = r.U32() };
    }

    /// <summary>
    /// One listed design for the Showcase (ours): u64 product, name, description, tags, author, u32 voxels, u64 template
    /// (on the maker's world), u32 the maker's world, string that world's realm (a template id names a design image only
    /// in its own realm's world data), u32 the creator's price (added 2026-10-17: world and commerce images are built
    /// together).
    /// </summary>
    public sealed record StudioListEntry(ulong Product, string Name, string Description, string Tags, string Author, uint Voxels, ulong Template,
        uint World, string Realm, uint Price)
    {
        public void Write(InternalWriter w) => w.U64(Product).Str(Name).Str(Description).Str(Tags).Str(Author).U32(Voxels).U64(Template).U32(World).Str(Realm).U32(Price);
        public static StudioListEntry Read(InternalReader r) => new(r.U64(), r.Str(), r.Str(), r.Str(), r.Str(), r.U32(), r.U64(), r.U32(), r.Str(), r.U32());
    }

    /// <summary>CommerceStudioListReply (ours, commerce → zone, 0x45/0x10016): u32 request, u32 n × <see cref="StudioListEntry"/>, newest first.</summary>
    public sealed class CommerceStudioListReply : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.StudioListReplySub;
        public uint RequestId { get; init; }
        public IReadOnlyList<StudioListEntry> Designs { get; init; } = Array.Empty<StudioListEntry>();

        public void Write(InternalWriter w)
        {
            w.U32(RequestId).U32((uint)Designs.Count);
            foreach (StudioListEntry design in Designs)
                design.Write(w);
        }

        public static CommerceStudioListReply Read(InternalReader r)
        {
            uint request = r.U32();
            uint n = r.U32();
            SocialMessages.CheckCount(n, 100_000, "designs");
            var list = new List<StudioListEntry>((int)n);
            for (uint i = 0; i < n; i++)
                list.Add(StudioListEntry.Read(r));
            return new() { RequestId = request, Designs = list };
        }
    }

    /// <summary>
    /// CommerceAdminRequest (ours, zone → commerce, 0x45/0x1001B): u32 request, string command, string arguments — the dev
    /// / admin tools that reach commerce data: "psreview" (&lt;product&gt; approve|reject; live: "appgamecommerce product
    /// approve|reject"), "contestphase" (&lt;id&gt; &lt;phase&gt;), "contests", "followdigest" ([dry] [hours]: the Showcase
    /// following digest for the asking world's realm now, FollowService). Answered with <see cref="CommerceAdminReply"/>.
    /// </summary>
    public sealed class CommerceAdminRequest : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.AdminRequestSub;
        public uint RequestId { get; init; }
        public string Command { get; init; } = "";
        public string Arguments { get; init; } = "";

        public void Write(InternalWriter w) => w.U32(RequestId).Str(Command).Str(Arguments);
        public static CommerceAdminRequest Read(InternalReader r) => new() { RequestId = r.U32(), Command = r.Str(), Arguments = r.Str() };
    }

    /// <summary>CommerceAdminReply (ours, commerce → zone, 0x45/0x1001C): u32 request, bool ok, string text.</summary>
    public sealed class CommerceAdminReply : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.AdminReplySub;
        public uint RequestId { get; init; }
        public bool Ok { get; init; }
        public string Text { get; init; } = "";

        public void Write(InternalWriter w) => w.U32(RequestId).Bool(Ok).Str(Text);
        public static CommerceAdminReply Read(InternalReader r) => new() { RequestId = r.U32(), Ok = r.Bool(), Text = r.Str() };
    }

    /// <summary>
    /// CommerceImportRequest (ours, commerce → zone, 0x45/0x1001D): u32 what (<see cref="CommerceMessages.ImportContests"/> /
    /// <see cref="CommerceMessages.ImportPortraits"/>). The one-time import of the files a world kept before the service
    /// existed (spec §8 step 8: "tables to lmemu_commerce (migration with world id)"); the database rows (products,
    /// reviews, taken designs) are copied by the service itself from the character database (CommerceServer.json
    /// ImportFrom). Asked once per world and kind; the zone answers with <see cref="CommerceImportFile"/> messages and
    /// <see cref="CommerceImportDone"/>. The world's files are only read.
    /// </summary>
    public sealed class CommerceImportRequest : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.ImportRequestSub;
        public uint What { get; init; }

        public void Write(InternalWriter w) => w.U32(What);
        public static CommerceImportRequest Read(InternalReader r) => new() { What = r.U32() };
    }

    /// <summary>
    /// CommerceImportFile (ours, zone → commerce, 0x45/0x1001E): u32 what, string the file's path inside its folder
    /// ("contests.json", "entries.json", "shots/1_42_0.bin"; "12345_portrait.png"), u32 n + the bytes.
    /// </summary>
    public sealed class CommerceImportFile : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.ImportFileSub;
        public uint What { get; init; }
        public string Name { get; init; } = "";
        public byte[] Data { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w)
        {
            w.U32(What).Str(Name);
            CommerceMessages.WriteBlob(w, Data);
        }

        public static CommerceImportFile Read(InternalReader r) => new() { What = r.U32(), Name = r.Str(), Data = CommerceMessages.ReadBlob(r, CommerceMessages.MaxBlob) };
    }

    /// <summary>CommerceImportDone (ours, zone → commerce, 0x45/0x1001F): u32 what, u32 files sent — the import of that kind is complete.</summary>
    public sealed class CommerceImportDone : ISocialMessage
    {
        public ushort Family => CommerceMessages.CommerceFamily;
        public uint Sub => CommerceMessages.ImportDoneSub;
        public uint What { get; init; }
        public uint Files { get; init; }

        public void Write(InternalWriter w) => w.U32(What).U32(Files);
        public static CommerceImportDone Read(InternalReader r) => new() { What = r.U32(), Files = r.U32() };
    }

    /// <summary>
    /// ContestEntrySubmit (zone → commerce, 0xF9/0x10020; spec §6.3 "world, site, owner, contest key, screenshots"): the
    /// client's ClaimsBase 0x41 SubmitContestEntry (FUN_142d30a60 → FUN_142d07b90; "same" in 0.695) after the zone's
    /// checks — caller, u64 site, string contest (name or key as the client sent it), bool allowed (the session's
    /// entry limiter), bool own site (the site exists on this world and the caller owns it), string site name, u32 n × (u32
    /// length + bytes) screenshots (at most 8). The service answers the client itself with ContestEntryResults 0xF9/0x18
    /// {bool ok, string site name, string contest name} (FUN_142d08640), so the spec's ContestEntryResult 0x10021 is not
    /// needed.
    /// </summary>
    public sealed class CommerceContestEntrySubmit : ISocialMessage
    {
        public ushort Family => CommerceMessages.ClaimsFamily;
        public uint Sub => CommerceMessages.ContestEntrySubmitSub;
        public CommerceCaller Caller { get; init; }
        public ulong Site { get; init; }
        public string Contest { get; init; } = "";
        public bool Allowed { get; init; }
        public bool OwnSite { get; init; }
        public string SiteName { get; init; } = "";
        public IReadOnlyList<byte[]> Screenshots { get; init; } = Array.Empty<byte[]>();

        public void Write(InternalWriter w)
        {
            Caller.Write(w);
            w.U64(Site).Str(Contest).Bool(Allowed).Bool(OwnSite).Str(SiteName).U32((uint)Screenshots.Count);
            foreach (byte[] shot in Screenshots)
                CommerceMessages.WriteBlob(w, shot);
        }

        public static CommerceContestEntrySubmit Read(InternalReader r)
        {
            CommerceCaller caller = CommerceCaller.Read(r);
            ulong site = r.U64();
            string contest = r.Str();
            bool allowed = r.Bool(), own = r.Bool();
            string siteName = r.Str();
            uint n = r.U32();
            SocialMessages.CheckCount(n, 8, "screenshots");
            var shots = new List<byte[]>((int)n);
            for (uint i = 0; i < n; i++)
                shots.Add(CommerceMessages.ReadBlob(r, 4 * 1024 * 1024));
            return new() { Caller = caller, Site = site, Contest = contest, Allowed = allowed, OwnSite = own, SiteName = siteName, Screenshots = shots };
        }
    }

    /// <summary>
    /// CommercePortraitUpload (ours, zone → commerce, 0x98/0x10030): u32 request, u32 world, u64 character, bool trusted
    /// (the dev command "portraittest": no checksum check), string kind ("" = an upload of both images; "Portrait" /
    /// "Headshot" = that one, trusted only), u32 n + portrait PNG, u32 n + headshot PNG. The client's own capture is posted
    /// to ClientConfig [Portrait] UploadUrl — the world's web pages, which hand it on here; the service accepts it when
    /// the portrait's CRC-32 is the one that character announced (AddClientPortraitCrc 0x75), as Portraits.Upload did.
    /// </summary>
    public sealed class CommercePortraitUpload : ISocialMessage
    {
        public ushort Family => CommerceMessages.FotomatFamily;
        public uint Sub => CommerceMessages.PortraitUploadSub;
        public uint RequestId { get; init; }
        public uint World { get; init; }
        public ulong Character { get; init; }
        public bool Trusted { get; init; }
        public string Kind { get; init; } = "";
        public byte[] Portrait { get; init; } = Array.Empty<byte>();
        public byte[] Headshot { get; init; } = Array.Empty<byte>();

        public void Write(InternalWriter w)
        {
            w.U32(RequestId).U32(World).U64(Character).Bool(Trusted).Str(Kind);
            CommerceMessages.WriteBlob(w, Portrait);
            CommerceMessages.WriteBlob(w, Headshot);
        }

        public static CommercePortraitUpload Read(InternalReader r) => new()
        {
            RequestId = r.U32(), World = r.U32(), Character = r.U64(), Trusted = r.Bool(), Kind = r.Str(),
            Portrait = CommerceMessages.ReadBlob(r, 4 * 1024 * 1024), Headshot = CommerceMessages.ReadBlob(r, 4 * 1024 * 1024)
        };
    }

    /// <summary>CommercePortraitUploadReply (ours, commerce → zone, 0x98/0x10031): u32 request, bool ok, string message (for the web log).</summary>
    public sealed class CommercePortraitUploadReply : ISocialMessage
    {
        public ushort Family => CommerceMessages.FotomatFamily;
        public uint Sub => CommerceMessages.PortraitUploadReplySub;
        public uint RequestId { get; init; }
        public bool Ok { get; init; }
        public string Message { get; init; } = "";

        public void Write(InternalWriter w) => w.U32(RequestId).Bool(Ok).Str(Message);
        public static CommercePortraitUploadReply Read(InternalReader r) => new() { RequestId = r.U32(), Ok = r.Bool(), Message = r.Str() };
    }
}
