using LandmarkEmulator.Shared.Configuration;

namespace LandmarkEmulator.CommerceServer
{
    /// <summary>
    /// CommerceServer.json, overridable by environment variables with the same names (Network__Port, Database, ImportFrom
    /// ...), like the other servers.
    /// </summary>
    public class CommerceServerConfiguration
    {
        /// <summary>
        /// Where the worlds' zone processes reach the service: Host / Port of its internal TCP listener
        /// (internal-transport.md framing). Default 20400 (Docker: COMMERCE_ADDRESS=commerce:20400). Never published outside
        /// the Docker network.
        /// </summary>
        public NetworkConfig Network { get; set; } = new() { Host = "0.0.0.0", Port = 20400 };

        /// <summary>
        /// The service's own database (world-channel-service.md §6.1): a MySQL / MariaDB connection string naming schema
        /// lmemu_commerce, created at start when missing, with its tables (CommerceStore). Required.
        /// </summary>
        public string Database { get; set; } = "";

        /// <summary>
        /// The character database the Player Studio products, peer reviews and taken designs of the in-process commerce
        /// code were kept in (player_studio_product, player_studio_review, character_unlock kind 16, with template and
        /// character for the design snapshots and the makers' names): copied once, on the first start with an empty
        /// product table. Normally lmemu_character; empty = no copy.
        /// </summary>
        public string ImportFrom { get; set; } = "";

        /// <summary>
        /// The world those rows belong to (its zone_server id): the in-process code ran in one world, whose build sites the
        /// gallery listings name. Default 1 (the first world).
        /// </summary>
        public uint ImportWorldId { get; set; } = 1;

        /// <summary>
        /// The realm (character database) of <see cref="ImportWorldId"/> until that world links and names its own; normally
        /// the same database as <see cref="ImportFrom"/>.
        /// </summary>
        public string ImportRealm { get; set; } = "";

        /// <summary>
        /// Folder for the files the service keeps: contest screenshots (Contests/shots) and portraits (Portraits). Docker:
        /// the commerce-data volume at /data. Relative paths are next to the program.
        /// </summary>
        public string DataPath { get; set; } = "Data";

        /// <summary>
        /// Player Studio peer review: approvals that list a product, and rejections that turn it down (were
        /// WorldServer.json PlayerStudioApprovals / PlayerStudioRejections; the live server's numbers are not in the client).
        /// </summary>
        public uint PlayerStudioApprovals { get; set; } = 3;
        public uint PlayerStudioRejections { get; set; } = 3;

        /// <summary>
        /// Products one player may review in 24 hours (0 = no limit); 2 = live, at most two review offers per player a
        /// day (Update Notes 18 Jun 2014, P1209). Docker: PLAYER_STUDIO_REVIEWS_PER_DAY.
        /// Who may review at all (the spend rule) is checked by the world before it hands the request on.
        /// </summary>
        public uint PlayerStudioReviewsPerDay { get; set; } = 2;

        /// <summary>
        /// On (live) = a product the peers reject waits for staff (P1210: rejected items went to a Customer Service
        /// queue;
        /// the worlds' admin page lists them, ProductService.StaffQueue, and "psreview" decides); off = rejected at once.
        /// Docker: PLAYER_STUDIO_STAFF_REVIEW.
        /// </summary>
        public bool PlayerStudioStaffReview { get; set; } = true;

        /// <summary>
        /// A reviewer whose rejections staff overturned this many times is offered no more reviews (wiki Player Studio: such
        /// a reviewer "will stop getting items to review"; StudioRules.MayStillReview); 0 = never held back. Default 1
        /// [I count]. Docker: PLAYER_STUDIO_REVIEWER_OVERRULES (world and commerce alike).
        /// </summary>
        public uint PlayerStudioReviewerOverrules { get; set; } = 1;

        /// <summary>
        /// The Player Studio creator's share of the Lumens a buyer paid for their design, in percent (ProductService.Taken;
        /// the same rule as WorldServer.json PlayerStudioCreatorSharePercent for in-process commerce). 40 = live's
        /// 60/40 split of each sale's net revenue (Update Notes 18 Jun 2014), the creator's side being the 40 [W].
        /// Mailed to the creator
        /// through a world of their realm (CommercePayout) [I]. Docker: PLAYER_STUDIO_CREATOR_SHARE.
        /// </summary>
        public uint PlayerStudioCreatorSharePercent { get; set; } = 40;

        /// <summary>
        /// The daily Showcase following mail for the worlds that use this service (FollowService; the worlds' in-process
        /// setting of the same name does the same for a world without COMMERCE_ADDRESS): once a day an in-game mail to every
        /// player who follows creators or tags, listing what was listed since the last one; nothing when nothing is
        /// new. On = live: once a day, a mail listing what the followed creators and tags had newly on the Marketplace
        /// or Gallery (Update Notes 27 Aug 2014). Off = no digest mail (following itself stays). Docker:
        /// SHOWCASE_FOLLOW_DIGEST.
        /// </summary>
        public bool ShowcaseFollowDigest { get; set; } = true;

        /// <summary>
        /// The UTC hour (0-23) the daily following mail goes out (default 10, as the worlds' setting; live's hour is not
        /// known [I]); a service that was down at that hour runs it when it is back the same day. Docker:
        /// SHOWCASE_DIGEST_HOUR_UTC.
        /// </summary>
        public uint ShowcaseDigestHourUtc { get; set; } = 10;
    }
}
