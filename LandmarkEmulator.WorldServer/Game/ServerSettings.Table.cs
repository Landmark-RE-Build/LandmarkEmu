using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>Can a setting be changed on the settings page?</summary>
    public enum SettingAccess
    {
        /// <summary>Changed on the page (stored in the overrides file).</summary>
        Editable,
        /// <summary>Shown, changed only in .env / WorldServer.json (network, addresses, ids, folders, the admin list…).</summary>
        ReadOnly,
        /// <summary>Never shown, only "set" / "not set" (passwords, connection strings, keys).</summary>
        Secret,
    }

    /// <summary>When a saved change takes effect (checked against the code that reads it; see the table).</summary>
    public enum SettingApply
    {
        /// <summary>The code reads the setting each time it is used: the change counts at once.</summary>
        Now,
        /// <summary>Read at use time, but what it changes is sent to a player when they enter the world.</summary>
        NextLogin,
        /// <summary>Read once (at start-up or first use): the change counts after the world restarts.</summary>
        Restart,
    }

    /// <summary>
    /// What the settings page knows about one setting beyond its name and type: who may change it, when a change counts,
    /// how live Landmark behaved, and the values it accepts. The help text and the group come from .env.example
    /// (<see cref="ServerSettingsDocs"/>); <see cref="Note"/> adds to it (or stands in for settings .env.example doesn't
    /// list).
    /// </summary>
    public sealed record SettingRule(string Key, SettingAccess Access, SettingApply Apply)
    {
        /// <summary>The value that is live Landmark's behaviour (null = not known, or not a gameplay choice).</summary>
        public string Live { get; init; }
        /// <summary>The only values accepted (shown as a drop-down), compared ignoring case; stored as written here.</summary>
        public string[] Choices { get; init; }
        public double? Min { get; init; }
        public double? Max { get; init; }
        /// <summary>A text value must match this (whole value); <see cref="FormatHint"/> says how.</summary>
        public Regex Format { get; init; }
        public string FormatHint { get; init; }
        /// <summary>Extra validation (null = valid, else the reason).</summary>
        public Func<string, string> Check { get; init; }
        /// <summary>Longest text accepted.</summary>
        public int MaxLength { get; init; } = 1000;
        /// <summary>Extra text shown with the help (why it is read-only, what "applies now" means for it…).</summary>
        public string Note { get; init; }
        /// <summary>For a setting the code reads straight from the configuration (no WorldServerConfiguration property).</summary>
        public Type RawType { get; init; }
        /// <summary>The default of a raw setting (as the code's GetValue default; as text).</summary>
        public string RawDefault { get; init; }
        /// <summary>Read from the process environment only (not through the configuration): applied by <see cref="Applier"/>.</summary>
        public bool EnvironmentOnly { get; init; }
        /// <summary>.env variables that feed the setting other than through the world's environment block (volumes).</summary>
        public string[] EnvHints { get; init; }
        /// <summary>Runs on the world thread after a change that counts now (and at start-up for environment-only settings).</summary>
        public Action<string> Applier { get; init; }
    }

    public static partial class ServerSettings
    {
        private static SettingRule Edit(string key, SettingApply apply, string live = null) => new(key, SettingAccess.Editable, apply) { Live = live };
        private static SettingRule Fixed(string key, string why) => new(key, SettingAccess.ReadOnly, SettingApply.Restart) { Note = why };
        private static SettingRule Hidden(string key, string why) => new(key, SettingAccess.Secret, SettingApply.Restart) { Note = why };

        private const string NetworkNote = "Network, addresses and ports belong to the Docker layout: change them in .env (they must match the "
            + "port mappings and the other containers).";
        private const string FolderNote = "A folder or file of the container layout: change it in .env.";
        private const string SharedNote = "Read by the world and the Environment Server alike from one .env line, so the two always agree: "
            + "change it in .env (an override here would make them disagree).";
        private const string CommerceNote = "With COMMERCE_ADDRESS set, the shared commerce service's own value counts (set it in .env).";
        private const string SocialNote = "With SOCIAL_ADDRESS set, the shared social service's own value counts (set it in .env).";
        private const string StoreCacheNote = "The store's catalogue and prices are built once, at the first store visit after a start.";

        private static readonly Regex Ids = new(@"^\d+(\s*,\s*\d+)*$");

        private static string RestartScheduleCheck(string value)
            => value.Trim().Length == 0 || RestartSchedule.Parse(value).Count > 0 ? null
                : "Write days and a time, e.g. \"Mon,Sat 03:00\" or \"Daily 05:00\" (empty = no scheduled restarts).";

        private static string TimeZoneCheck(string value)
        {
            if (value.Trim().Length == 0)
                return null;
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(value.Trim());
                return null;
            }
            catch (Exception)
            {
                return $"Unknown time zone \"{value}\" (e.g. America/Los_Angeles, Europe/London, UTC).";
            }
        }

        private static void ReconfigureRestarts(string _)
        {
            WorldServerConfiguration c = Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            if (c != null)
                RestartSchedule.Configure(c.RestartSchedule, c.RestartTimeZone, c.RestartWarningSeconds, c.RestartMessage);
        }

        private static void ApplyClientTimeout(string _)
        {
            WorldServerConfiguration c = Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            if (c != null)
                Shared.Network.SocketHeartbeat.DefaultTimeoutSeconds = c.ClientTimeoutSeconds;
        }

        /// <summary>
        /// One rule per setting: every WorldServerConfiguration property (nested classes by their top-level name), every
        /// setting the code reads straight from the configuration, and the environment-only ones (a test checks the list
        /// is complete). "Now" / "NextLogin" are only given where the reading code was checked to read the setting at use
        /// time (the file and member are named); everything read once is "Restart".
        /// </summary>
        public static readonly IReadOnlyList<SettingRule> Rules = new List<SettingRule>
        {
            // ---- Read-only: network, addresses, ids, folders, secrets, admins --------------------------------------------
            Fixed("Network", NetworkNote),
            Hidden("Database", "Database connections carry the database password: never shown, change them in .env (DB_PASSWORD)."),
            Fixed("GameDataPath", FolderNote) with { EnvHints = new[] { "GAME_DATA_DIR" } },
            Fixed("GameDirectory", FolderNote) with { EnvHints = new[] { "GAME_DIR" } },
            Fixed("HealthFile", FolderNote),
            Fixed("SettingsOverridesFile", "Where this page keeps its changes (in the world's data folder, WORLD_DATA_DIR in Docker).")
                with { EnvHints = new[] { "WORLD_DATA_DIR" }, RawType = typeof(string), RawDefault = "" },
            Fixed("ZoneServerId", "The world's id in the server list and in item / guild ids: never change it on a running world."),
            Fixed("ClientWorldId", "Worlds on one gateway need different ids; it belongs to the Docker layout."),
            Fixed("PublicAddress", NetworkNote),
            Fixed("WebPagesPort", NetworkNote) with { RawType = typeof(int), RawDefault = "19001" },
            Fixed("WebPagesHost", NetworkNote) with { RawType = typeof(string), RawDefault = "" },
            Fixed("WebPagesUrl", NetworkNote) with { RawType = typeof(string), RawDefault = "" },
            Fixed("SocialAddress", NetworkNote),
            Fixed("CommerceAddress", NetworkNote),
            Fixed("EnvServerAddress", NetworkNote),
            Fixed("GatewayInternalAddress", NetworkNote),
            Fixed("ZONE_INTERNAL_PORT", NetworkNote) with { EnvironmentOnly = true, RawType = typeof(string), RawDefault = "" },
            Fixed("Continent", SharedNote),
            Fixed("TerrainLodCopies", SharedNote),
            Fixed("KeepVertexOnlyVoxels", SharedNote),
            Fixed("PioneerSurfaceVeins", SharedNote + " Read when the vein layer loads a landscape (start-up). Live showed Pioneer surface ore as "
                + "flush patches, which needs the exact ground the server lacks there; buried is the closest (trace/0695/pioneer-veins.md).")
                with { Choices = new[] { "buried", "off" } },
            Fixed("PioneerSurfaceIron", SharedNote + " Read when the vein layer loads a landscape (start-up).") with { Live = "false" },
            Fixed("Admins", "Bootstrap only (.env). Day-to-day admins live in the auth DB admin table (SQL); an admin who could change either list here could make anyone an admin."),
            Fixed("AdminList", "Bootstrap only (.env). Day-to-day admins live in the auth DB admin table (SQL); an admin who could change either list here could make anyone an admin."),
            Fixed("DevClientCodeOnOthers", "Stays in .env: switched on, any admin could run code in every player's game client (and open their desktop browser); only the server owner decides that."),
            Fixed("StoreLumenPrices", "The WorldServer.json dictionary form of STORE_PRICES; edit STORE_PRICES (below) instead."),

            // ---- Your world -----------------------------------------------------------------------------------------------
            Edit("WorldNameId", SettingApply.Restart) with { Min = 0, Max = uint.MaxValue,
                Note = "Written to the server list at start-up (HostedService.RegisterInServerList)." },
            Edit("ClientWorldNameText", SettingApply.Restart, "0") with { Min = 0, Max = uint.MaxValue, Note = "Read once (Landscapes.WorldNameText)." },
            Edit("MaxPlayers", SettingApply.Now) with { Min = 0, Max = 100000, Note = "Read with every heartbeat (LoginQueue.MaxPlayers)." },
            Edit("MotdSubject", SettingApply.NextLogin) with { MaxLength = 255 },
            Edit("MotdBody", SettingApply.NextLogin) with { MaxLength = 2000 },
            Edit("RestartSchedule", SettingApply.Now, "Mon,Sat 03:00") with { Check = RestartScheduleCheck, Applier = ReconfigureRestarts,
                Note = "Live restarted Mon,Sat 03:00 Pacific; the schedule is the owner's choice. A change re-plans the next restart at once." },
            Edit("RestartTimeZone", SettingApply.Now, "America/Los_Angeles") with { Check = TimeZoneCheck, Applier = ReconfigureRestarts },
            Edit("UpkeepTimeZone", SettingApply.Now, "America/Los_Angeles") with { Check = TimeZoneCheck,
                Note = "Read at each world entry (SiteUpkeep.DayZone): the day a login counts for." },
            Edit("RestartWarningSeconds", SettingApply.Now) with { Min = 0, Max = 86400, Applier = ReconfigureRestarts },
            Edit("RestartMessage", SettingApply.Now) with { MaxLength = 255, Applier = ReconfigureRestarts },
            Edit("ShutdownBroadcasts", SettingApply.Now, "false") with { RawType = typeof(bool), RawDefault = "false",
                Note = "Read at each warning (Broadcasts.MirrorShutdownWarnings)." },
            Edit("PendingKickPacket", SettingApply.Now) with { RawType = typeof(bool), RawDefault = "true", Note = "Read at each warning (Kicks.SendPendingPacket)." },
            Edit("TargetNotOnlinePacket", SettingApply.Now) with { RawType = typeof(bool), RawDefault = "false", Note = "Read each time (TargetNotOnline.UsePacket)." },
            Edit("CraftingWindowCloseAway", SettingApply.Restart) with { RawType = typeof(bool), RawDefault = "true", Note = "Read at first use (Crafting.CloseAwayEnabled)." },
            Edit("WatchdogSeconds", SettingApply.Restart) with { Min = 0, Max = 86400, Note = "Read at start-up (Watchdog.Start)." },
            Edit("ClientTimeoutSeconds", SettingApply.Now) with { Min = 0, Max = 86400,
                Applier = ApplyClientTimeout,
                Note = "Read at each check (SocketHeartbeat.DefaultTimeoutSeconds), for this world's in-process gateway. With a separate gateway "
                    + "(GATEWAY_INTERNAL_ADDRESS) the gateway container's value counts: change CLIENT_TIMEOUT_SECONDS in .env and restart the gateway. "
                    + "60 = two of the client's 29 s keep-alives; below 30 counts as 30; 0 = never. Live's server value is not known." },
            Edit("FreeBuild", SettingApply.NextLogin, "false") with {
                Note = "The server checks it at once (Props.FreeBuild); the game client gets it with the player's data at login, so players relog." },
            Edit("MovementEffects", SettingApply.Now, "true") with { Note = "Read at each cast (MovementEffects.Enabled; a dev command override wins)." },
            Edit("MeleeTargeters", SettingApply.Now, "true") with { Note = "Read at each swing (WeaponCombat.MeleeTargeters; a dev command override wins)." },
            Edit("BlockStanceVisible", SettingApply.Now, "true") with { Note = "Read at each stance (BlockStance.Visible)." },
            Edit("DeadBodiesVisible", SettingApply.Now, "true") with { Note = "Read at each death (DeadBodies.Visible)." },
            Edit("SafeEjectReset", SettingApply.Now, "true") with { Note = "Read at each landing (SafeEject.Reset)." },
            Edit("LiveCharacterStats", SettingApply.NextLogin, "true") with { Note = "Sent when a player enters the world (CharacterStats.Enabled)." },
            Edit("PropStreamRange", SettingApply.Now) with { Min = 0, Max = 100000,
                Note = "Read as players move (Props.StreamRange; the dev command \"propstream\" wins until the restart)." },
            Edit("TreeStreamRange", SettingApply.Now) with { Min = 64, Max = 1000,
                Note = "Read as players move (Gathering.StreamRange; the dev command \"treestream\" wins until the restart)." },
            Edit("StreamHoldUntilLoaded", SettingApply.Now) with {
                Note = "Read at each world entry and re-zone (PlayerManager.StreamHoldEnabled); a hold already running finishes as it started." },
            Edit("StreamHoldLimitSeconds", SettingApply.Now) with { Min = 10, Max = 600,
                Note = "Read when a hold starts or its re-zone reaches its zone-in (PlayerManager.StreamHoldLimit)." },
            Edit("PlayerTeleportRequests", SettingApply.Restart, "true") with { RawType = typeof(bool), RawDefault = "true",
                Note = "Read at first use (TeleportRequests.Enabled)." },

            // ---- Landscapes, caverns, creatures ---------------------------------------------------------------------------
            Edit("LandmarksOfLandmarkWorld", SettingApply.Restart, "auto") with {
                Choices = new[] { "auto", "Courage", "Determination", "Liberation", "Rebellion", "Serenity", "off" } },
            Edit("CavernLandscapes", SettingApply.Restart, "all") with { Format = new Regex(@"^(all|home|\d+(\s*,\s*\d+)*)$", RegexOptions.IgnoreCase),
                FormatHint = "all, home, or landscape ids separated by commas (e.g. 406,391,393)" },
            Edit("OutpostPreload", SettingApply.Restart),
            Edit("OutpostRotationHours", SettingApply.Restart) with { Min = 0, Max = 100000 },
            Edit("OutpostRotationShare", SettingApply.Restart) with { Min = 0, Max = 1 },
            Edit("CreaturePathing", SettingApply.Restart, "true") with { Note = "Read at first use (Pathing.Enabled; dev \"pathing on|off\" until the restart)." },
            Edit("SitePathing", SettingApply.Restart, "true") with { Note = "Read at first use (Pathing.SitesEnabled)." },
            Edit("CreatureWeapons", SettingApply.Now, "true") with { Note = "Read each time a creature is sent to a player (CreatureWeapons.Enabled): creatures already seen keep theirs." },
            Edit("SurfaceGraveyards", SettingApply.Restart, "true"),
            Edit("SpireArea", SettingApply.Restart, "true"),
            Edit("CreatureLifeRange", SettingApply.Restart, "100") with { Min = 0, Max = 1000,
                Note = "Live's range is inferred from footage (bars out to about where names fade); 0 = every streamed creature." },
            Edit("CavernGraveyardStamps", SettingApply.Restart, "true"),
            Edit("CavernArrivalRescue", SettingApply.Now, "true") with { Note = "Read at each cavern trip (CavernArrivalGuard.Enabled)." },
            Edit("ProceduralRespawn", SettingApply.Restart, "minmax") with { Choices = new[] { "minmax", "inplace" }, RawType = typeof(string), RawDefault = "minmax" },
            Edit("ProceduralRespawnMinShare", SettingApply.Restart) with { Min = 0, Max = 1, RawType = typeof(double), RawDefault = "0.67" },
            Edit("TreeHealth", SettingApply.Restart, "health") with { Choices = new[] { "health", "hits" }, RawType = typeof(string), RawDefault = "health" },
            Edit("ProceduralPlacement", SettingApply.Restart) with { RawType = typeof(bool), RawDefault = "false" },
            Edit("ProceduralPlacementDecor", SettingApply.Restart) with { RawType = typeof(bool), RawDefault = "false" },
            Edit("ProceduralPlacementMasks", SettingApply.Restart) with { RawType = typeof(bool), RawDefault = "true" },
            Edit("EventIsland", SettingApply.Restart, "auto") with { Choices = new[] { "auto", "halloween", "winter", "judgement", "off" } },
            Edit("EventIslandExpireOnClose", SettingApply.Restart, "true") with { Note = "Deletes players' builds on the island that closed: back up first." },
            Edit("MoveOldPioneerSites", SettingApply.Restart),
            Edit("PioneerSlab", SettingApply.Now, "site") with { Choices = new[] { "site", "claim3x3", "claim1x1", "off" },
                Note = "Read for each new Pioneer site (Pioneer.QueueSlab). site = live's full-site slab (2016 videos); its exact height is inferred." },
            Edit("PioneerSlabMaterial", SettingApply.Now) with { Min = 0, Max = 65535 },
            Edit("LANDMARK_SEASON", SettingApply.Now, "auto") with { Choices = new[] { "auto", "halloween", "winter", "off" }, EnvironmentOnly = true,
                RawType = typeof(string), RawDefault = "auto", Applier = v => RareDrops.Setting = RareDrops.ParseSeason(v),
                Note = "Read at each drop (RareDrops.Setting)." },
            Edit("HAT_DROPS", SettingApply.Now, "season") with { Choices = new[] { "season", "always", "off" }, EnvironmentOnly = true,
                RawType = typeof(string), RawDefault = "season", Applier = v => RareDrops.HatDrops = RareDrops.ParseHatMode(v),
                Note = "Read at each drop (RareDrops.HatDrops)." },

            // ---- Store, Player Studio, membership -------------------------------------------------------------------------
            Edit("FreeStore", SettingApply.Restart, "false") with { Note = StoreCacheNote },
            Edit("LumensPerDaybreakCash", SettingApply.Restart, "1.3") with { Min = 0.01, Max = 1000, Note = StoreCacheNote },
            Edit("StorePrices", SettingApply.Restart, "") with { Format = new Regex(@"^(\d+\s*=\s*\d+)(\s*,\s*\d+\s*=\s*\d+)*$"),
                FormatHint = "item id=Lumens pairs separated by commas, e.g. 900001=500,2551=1300 (empty = live's prices only)", Note = StoreCacheNote },
            Edit("PlayerStudioCreatorSharePercent", SettingApply.Now, "40") with { Min = 0, Max = 100, Note = CommerceNote },
            Edit("PlayerStudioListingFee", SettingApply.Now, "true"),
            Edit("PlayerStudioApprovals", SettingApply.Now) with { Min = 1, Max = 1000, Note = CommerceNote },
            Edit("PlayerStudioRejections", SettingApply.Now) with { Min = 1, Max = 1000, Note = CommerceNote },
            Edit("PlayerStudioReviewerMinSpend", SettingApply.Now, "1000") with { Min = 0, Max = 10000000 },
            Edit("PlayerStudioReviewsPerDay", SettingApply.Now, "2") with { Min = 0, Max = 10000, Note = CommerceNote },
            Edit("PlayerStudioStaffReview", SettingApply.Now, "true") with { Note = CommerceNote },
            Edit("PlayerStudioReviewerOverrules", SettingApply.Now, "1") with { Min = 0, Max = 1000, Note = CommerceNote },
            Edit("DesignsFollowAccount", SettingApply.NextLogin, "true") with {
                Note = "Read at each world entry (Templates.AdoptAccountDesigns): the designs of the account's deleted characters move then." },
            Edit("PlayerStudioDesignsFreeStorage", SettingApply.Now, "true") with {
                Note = "Read at each storage check and Designs list (Templates.StudioDesignsFree); the \"free\" column of designs already "
                    + "shown changes when the list is sent again (next zone-in)." },
            Edit("FounderEntitlements", SettingApply.NextLogin, "false") with {
                Note = "Live gave these items only to buyers, survivors and winners; on for everyone is this server's choice. Granted at login." },
            Edit("EntitlementPackages", SettingApply.NextLogin) with { Format = new Regex(@"^((founder|yearzero|promo)(\s*,\s*(founder|yearzero|promo))*)?$", RegexOptions.IgnoreCase),
                FormatHint = "founder, yearzero, promo, comma separated" },
            Edit("EntitlementDelivery", SettingApply.NextLogin, "auto") with {
                Format = new Regex(@"^(auto|mail|bag|(founder|yearzero|promo)\s*=\s*(auto|mail|bag)(\s*,\s*(founder|yearzero|promo)\s*=\s*(auto|mail|bag))*)$", RegexOptions.IgnoreCase),
                FormatHint = "auto, mail, bag, or per package, e.g. founder=bag,promo=mail" },
            Edit("LandscapeCapacity", SettingApply.Now) with { Min = 0, Max = 100000, Note = "Read at each arrival (TravelQueue.Capacity)." },
            Edit("MembershipPriceLumens", SettingApply.Restart) with { Min = 0, Max = 100000000, Note = StoreCacheNote + " (its membership row too)" },
            Edit("MembershipDays", SettingApply.Restart) with { Min = 0, Max = 3650, Note = StoreCacheNote + " (its membership row too)" },
            Edit("TrialKeys", SettingApply.Now, "true"),
            Edit("TrialKeyDays", SettingApply.Now, "10") with { Min = 0, Max = 3650 },
            Edit("VendorShops", SettingApply.Restart, "false") with { Note = "Stock left in booths while it is off goes back to the owners at start-up." },
            Edit("MemberMonthlyLumens", SettingApply.Now, "0") with { Min = 0, Max = 100000000 },
            Edit("MemberStoreDiscountPercent", SettingApply.Now, "0") with { Min = 0, Max = 100 },
            Edit("StoreTradeWaitHours", SettingApply.Now, "12") with { Min = 0, Max = 100000 },
            Edit("OverflowExpiryHours", SettingApply.Now, "24") with { Min = 0, Max = 100000 },
            Edit("MailMaxStack", SettingApply.Now, "9000") with { Min = 0, Max = uint.MaxValue },
            Edit("LumenTrading", SettingApply.Now, "false") with { Note = "Read at each trade coin offer, trade completion and mail compose "
                + "(Trades.LumenTradingAllowed); a trade already holding a Lumen offer fails at completion once it is off." },
            Edit("ItemAutoResupply", SettingApply.Now, "true"),
            Edit("ShowcaseFollowDigest", SettingApply.Now, "true") with { Note = CommerceNote },
            Edit("ShowcaseDigestHourUtc", SettingApply.Now) with { Min = 0, Max = 23, Note = CommerceNote },

            // ---- Building, play, social -----------------------------------------------------------------------------------
            Edit("BuildMenuPropName", SettingApply.Now) with { Note = "No source shows live doing it; on is this server's choice." },
            Edit("ChestAccountCap", SettingApply.Now, "0") with { Min = 0, Max = 100000 },
            Edit("FriendTeleportCooldownSeconds", SettingApply.Now, "0") with { Min = 0, Max = 86400 },
            Edit("TravelCooldownSeconds", SettingApply.Now, "0") with { Min = 0, Max = 86400,
                Note = "Spire / My Site: the client takes a new value with its next caster info (relog)." },
            Edit("FriendRequestsMutual", SettingApply.Now, "true") with { Note = SocialNote + " Read at each accepted request (Friends.HandleResponse); "
                + "friends already made stay as they are." },
            Edit("StoryPosePersists", SettingApply.Now, "true"),
            Edit("CreaturePropsHideInPlayMode", SettingApply.Restart, "true") with {
                Note = "Read once: a site that is playing must keep one rule, or its hidden props would not come back in Edit Mode." },
            Edit("PlaceOpensBuildMode", SettingApply.Now, "false") with { Note = "On is this server's choice (asked for by the user); live never opened Build Mode for it. Cancel closes it; a successful place leaves it open." },
            Edit("EmotesPutAwayHeldItem", SettingApply.Now, "false") with { Note = "Off = live: the client never puts a held item away for "
                + "an emote, so live's emotes needed empty hands. On plays the tool's put-away clip first (trace/0695/emotes-client.md)." },
            Edit("YellIsEmote", SettingApply.Now, "true") with { Note = "The command list goes out at zone-in: a player sees the change "
                + "after a re-zone or relog. Off leaves /yell to the client's /shout alias." },
            Edit("DayCycleSpeed", SettingApply.Restart, "8") with { Min = 0.01, Max = 10000, Note = "Read at start-up (WorldClock.DefaultCycleSpeed)." },
            Edit("FinderReturnHome", SettingApply.Now),
            Edit("CavernTripsOtherWorlds", SettingApply.Now, "true"),
            Edit("ShortTeleportDistance", SettingApply.Restart) with { Min = 0, Max = 1000, RawType = typeof(float), RawDefault = "60",
                Note = "Read at first use (Teleport.LoadingScreenDistance)." },
            Edit("ShortTeleportWaitScreen", SettingApply.Restart) with { RawType = typeof(bool), RawDefault = "false" },
            Edit("ItemDefinitionChunkSize", SettingApply.Restart, "0") with { Min = 0, Max = 100000, RawType = typeof(int), RawDefault = "100",
                Note = "The login list is cut once at start-up (ItemDefinitionManager.Load); 0 is the live dump's one packet." },
            Edit("GriefAwards", SettingApply.Now, "true"),
            Edit("GriefDamagePerPoint", SettingApply.Now) with { Min = 1, Max = 1000000 },
            Edit("GriefLevelPoints", SettingApply.Now) with { Format = new Regex(@"^\d+(\s*,\s*\d+){0,4}$"), FormatHint = "up to five numbers, comma separated (levels 1-5)" },
            Edit("GriefDecayPerHour", SettingApply.Now) with { Min = 0, Max = 1000000 },
            Edit("GriefSuspensionMinutes", SettingApply.Now) with { Min = 1, Max = 525600 },

            // ---- Operations -----------------------------------------------------------------------------------------------
            Edit("AdminMonitorPush", SettingApply.Restart) with { RawType = typeof(bool), RawDefault = "false", Note = "Read at first use (AdminMonitor.PushEnabled)." },
            Edit("EnvServerStartupWaitSeconds", SettingApply.Restart) with { Min = 0, Max = 3600 },
        };

        private static Dictionary<string, SettingRule> rulesByKey;

        /// <summary>The rule for a key: its own, or its top-level parent's ("Database:Auth:ConnectionString" → "Database").</summary>
        public static SettingRule RuleFor(string key)
        {
            rulesByKey ??= Rules.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
            for (string k = key; k != null; k = k.Contains(':') ? k[..k.LastIndexOf(':')] : null)
                if (rulesByKey.TryGetValue(k, out SettingRule rule))
                    return rule;
            return null;
        }

        /// <summary>A name that holds a secret, whatever its rule says (defence in depth).</summary>
        public static bool LooksSecret(string key)
            => Regex.IsMatch(key, @"(ConnectionString|Password|Secret|Token|ApiKey|PrivateKey)$", RegexOptions.IgnoreCase)
               || Regex.IsMatch(key, @"(^|[:_])(PASSWORD|SECRET|TOKEN)($|_)", RegexOptions.IgnoreCase);

        internal static string Invariant(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    }
}
