using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Build site claims. Clients only allow building and prop placement inside a claim they have permission on
    /// (client FUN_142d15900), so every claim near a player is sent to them (ClaimsBase AOIUpdate).
    /// Placing one: the client sends PlaceClaimRequest (sub 0x1A, centre), the server answers PlaceClaimResponse
    /// (sub 6), the player confirms (PlaceClaimConfirm, sub 0x1B) and the server creates the claim and sends
    /// PlaceClaimComplete (sub 7) plus the claim to everyone nearby.
    /// </summary>
    public static class Claims
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>The Build Site Flag: starts placement when used, but is no longer needed or used up (see OwnerRefusal).</summary>
        public const uint BuildSiteFlagItem = 105;
        /// <summary>
        /// Half the site's width/depth and height, world units (dm). A launch build site is 210 × 180 × 210 m (525 × 450 ×
        /// 525 blocks of 0.4 m): client constant ClaimCursor.ClaimSize "210 180 210" (StringHashToValue.txt), which sizes
        /// the placement cursor (measured at runtime: x -395..-185, y 925.4..1105.4 local m). ToolClaimPlacement's
        /// "70 100 70" (FUN_1435aef40) is only the fallback when the constant is missing. The site is centred on the
        /// point the client requests (the cursor box's midpoint).
        /// </summary>
        private const double HalfSize = 1050, HalfHeight = 900;
        /// <summary>
        /// Buffer around every root site, world units: ClaimsManager.BufferDistanceToNonfriendlyClaim 285 m (client
        /// FUN_142d22e00, clamped 100-400). The client indexes each root site by its centre (FUN_142d134f0 → FUN_142d142c0)
        /// and refuses a new site whose box comes within 285 m of a centre on any axis (FUN_142d28700 → result 1,
        /// "Claims.InvalidClaimLocationBufferOverlap"); measured: a 210 m site needs its centre at least 390 m away. The
        /// server applies the same rule.
        /// </summary>
        private const double BufferFromCentre = 2850;
        /// <summary>Root sites a character may own (launch: 1 for new players, more bought as site slots).</summary>
        private const int MaxClaimsPerOwner = 1;
        /// <summary>
        /// Default access of a new site: Play Around (0x003) — the 25 May 2016 update made Play Around the default
        /// (wiki Game Updates 2016): visitors may use props, nothing more.
        /// </summary>
        private const uint DefaultAccess = 0x003;
        /// <summary>Most root sites any player could have (wiki: at most 8 build site slots).</summary>
        private const int MaxRootClaimsAllowed = 8;

        /// <summary>
        /// PlaceClaimResponse / PlaceClaimComplete / InteractionClaim results (client FUN_142d16710 picks the text): 1 asks
        /// for confirmation, 2 placed; refusals show the matching Claims.* text (trace/systems/claims.md).
        /// </summary>
        private const uint ResultConfirm = 1, ResultPlaced = 2, ResultInvalidLocation = 3, ResultNoFlag = 6,
            ResultMaxOwned = 10, ResultBufferOverlap = 26, ResultPioneer = 28;
        /// <summary>DeleteClaimItemReply results (client FUN_142d29200): 2 deleted; others show their text.</summary>
        private const uint DeleteDone = 2, DeleteForeignLandscape = 0x12, DeleteHasGame = 0x14, DeleteFailed = 1;
        private static readonly TimeSpan ExpiryCheckInterval = TimeSpan.FromHours(1);
        private static DateTime nextExpiryCheck = DateTime.MinValue;

        private static readonly object sync = new();
        private static readonly List<ClaimModel> claims = new();
        /// <summary>Special Access per claim: character id → permission mask.</summary>
        private static readonly Dictionary<ulong, Dictionary<ulong, uint>> special = new();
        /// <summary>Names of the characters with Special Access somewhere (Site Management lists them by name).</summary>
        private static readonly Dictionary<ulong, string> specialNames = new();

        /// <summary>
        /// Client permission bits (masks the Site Management levels send: Play Around 0x003, Pillage &amp; Plunder
        /// 0x007, Get Inspired 0x107, Let's Decorate! 0x10F, Ultimate Power 0x11F; owners always 0x11F, client
        /// FUN_143b5b480).
        /// </summary>
        public const uint PermissionUse = 0x01, PermissionPlunder = 0x04, PermissionDecorate = 0x08, PermissionBuild = 0x10,
            PermissionCopy = 0x100, OwnerPermissions = 0x11F;
        private static readonly Dictionary<WorldSession, (double X, double Y, double Z)> pending = new();
        private static long nextId;

        /// <summary>
        /// Loads every build site and its Special Access entries from the database at server start.
        /// </summary>
        public static void Load()
        {
            List<ClaimModel> stored = DatabaseManager.Instance.CharacterDatabase.GetClaims();
            var orphans = new HashSet<ulong>();
            lock (sync)
            {
                claims.AddRange(stored);
                var siteIds = stored.Select(c => c.Id).ToHashSet();
                foreach (ClaimPermissionModel p in DatabaseManager.Instance.CharacterDatabase.GetClaimPermissions())
                {
                    // Entries of sites removed before 2026-10-18 (Remove left them behind): never given to a new site that
                    // gets the old id (bughunt-build B3); the rows themselves are dropped below.
                    if (!siteIds.Contains(p.ClaimId))
                    {
                        orphans.Add(p.ClaimId);
                        continue;
                    }
                    if (!special.TryGetValue(p.ClaimId, out var map))
                        special[p.ClaimId] = map = new Dictionary<ulong, uint>();
                    map[p.CharacterId] = p.Mask;
                }
                foreach ((ulong id, string name) in DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(
                    special.Values.SelectMany(m => m.Keys).Distinct()))
                    specialNames[id] = name;
                nextId = stored.Count == 0 ? 1 : (long)stored.Max(c => c.Id);
            }
            if (orphans.Count > 0)
            {
                DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
                    c.ClaimPermission.RemoveRange(c.ClaimPermission.Where(p => orphans.Contains(p.ClaimId))))
                    .ContinueWith(t => log.Error(t.Exception, "Failed to drop the Special Access of removed sites."),
                        System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                log.Info($"Dropped the Special Access entries of {orphans.Count} removed site(s).");
            }
            GrowToLaunchSize();
            log.Info($"Loaded {stored.Count} claims.");
        }

        /// <summary>
        /// Sites made before the launch size was known (70 × 100 × 70 m) grow to 210 × 180 × 210 m around their centre, as
        /// live's sites grew in March 2016 — unless the bigger box would overlap another site's box (logged, left as is).
        /// </summary>
        private static void GrowToLaunchSize()
        {
            List<ClaimModel> grown = new();
            lock (sync)
            {
                foreach (ClaimModel c in claims)
                {
                    if (c.MaxX - c.MinX >= HalfSize * 2 - 1 && c.MaxY - c.MinY >= HalfHeight * 2 - 1)
                        continue;
                    double cx = (c.MinX + c.MaxX) / 2, cy = (c.MinY + c.MaxY) / 2, cz = (c.MinZ + c.MaxZ) / 2;
                    bool blocked = claims.Any(o => o != c && o.ZoneId == c.ZoneId
                        && cx - HalfSize < o.MaxX && cx + HalfSize > o.MinX && cz - HalfSize < o.MaxZ && cz + HalfSize > o.MinZ
                        && cy - HalfHeight < o.MaxY && cy + HalfHeight > o.MinY);
                    if (blocked)
                    {
                        log.Warn($"Site {c.Id} kept its old size: the launch size would overlap another site.");
                        continue;
                    }
                    (c.MinX, c.MaxX, c.MinY, c.MaxY, c.MinZ, c.MaxZ) =
                        (cx - HalfSize, cx + HalfSize, cy - HalfHeight, cy + HalfHeight, cz - HalfSize, cz + HalfSize);
                    grown.Add(c);
                }
            }
            foreach (ClaimModel c in grown)
            {
                ulong id = c.Id;
                (double a, double b, double d, double e, double f, double g) = (c.MinX, c.MaxX, c.MinY, c.MaxY, c.MinZ, c.MaxZ);
                DatabaseManager.Instance.CharacterDatabase.SaveWorld(db =>
                {
                    var model = new ClaimModel { Id = id };
                    db.Claim.Attach(model);
                    (model.MinX, model.MaxX, model.MinY, model.MaxY, model.MinZ, model.MaxZ) = (a, b, d, e, f, g);
                    db.MarkModified(model, nameof(model.MinX), nameof(model.MaxX), nameof(model.MinY), nameof(model.MaxY),
                        nameof(model.MinZ), nameof(model.MaxZ));
                });
            }
            if (grown.Count > 0)
                log.Info($"Grew {grown.Count} site(s) to the launch size (210 × 180 × 210 m).");
        }

        /// <summary>
        /// Sites on a landscape whose name or tags contain every word of <paramref name="query"/> (case ignored) — the
        /// world map's site search (Commerce CommerceSearch).
        /// </summary>
        public static List<ulong> Search(uint zone, string query)
        {
            string[] words = (query ?? "").ToLowerInvariant().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            lock (sync)
                return claims.Where(c => c.ZoneId == zone && words.Length > 0
                        && words.All(w => (c.Name ?? "").ToLowerInvariant().Contains(w) || (c.Tags ?? "").ToLowerInvariant().Contains(w)))
                    .Select(c => c.Id).ToList();
        }

        /// <summary>Sends every claim to a player who just entered the world (a full update).</summary>
        public static void SendAll(WorldSession session)
        {
            List<ClaimModel> all;
            uint zone = session.ZoneId;
            lock (sync)
                all = claims.Where(c => c.ZoneId == zone).ToList();
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildAoiUpdate(true, all, Array.Empty<ulong>()));
            // The full update replaced the client's list: the outposts' banner sites (client-only records) are sent again.
            OutpostSites.Reset(session);
            // Site music isn't part of the site record: one info update per site that has some.
            foreach (ClaimModel claim in all.Where(c => MusicOf(c) != 0))
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(claim, InfoMusic));
            // Nor are the tags (the client fills them only from ClaimInfoUpdate kind 4).
            foreach (ClaimModel claim in all.Where(c => !string.IsNullOrEmpty(c.Tags)))
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(claim, InfoTags));
        }

        /// <summary>
        /// Using a Build Site Flag: Command 0x4D InteractionClaim (client FUN_143311590), body u32 result. 2 puts the client
        /// into build-site placement (ControllerClaimPlacement); any other value is shown as that claim error instead
        /// (FUN_142d29240), so a player who can't place a site is told why before placement starts (otherwise each
        /// refused placement puts them straight back into placement mode: the "stuck flag" of 2026-10-12).
        /// </summary>
        public static void BeginPlacement(WorldSession session)
        {
            uint result = OwnerRefusal(session) ?? ResultPlaced;
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteUShort(0x4D);
            writer.Write(result);
            session.EnqueueMessage(ZoneMessageOpcode.CommandBase, output.ToArray());
        }

        // No Build Site Flag is taken or given back. From 14 Jul 2016 (patch notes P6081-P6085; the 0.695 flag text: "no
        // longer required to place Build Sites") sites are slots placed from the Site Management window ("Place this build
        // site", StartClaimPlacementSub); old flags could still be used or simply deleted, so a flag starts placement
        // but is neither needed nor used up, and deleting or losing a site gives no flag back. (0.589 took one flag per
        // site and refused Pioneer landscapes, results 6 / 28: the frozen client-0.589 branch's Claims.UsesFlags.)

        /// <summary>
        /// ClaimsPacketStartClaimPlacement (0.695 only, 0xF9/0x46, no body; Lua Claims.StartClaimPlacement from the Place
        /// row) [P]: id table cClaimsPacketIdStartClaimPlacement 0x46 (re/out/packet_ids_695.tsv, packets-695.md §7).
        /// </summary>
        private const byte StartClaimPlacementSub = 0x46;

        /// <summary>
        /// PlayerEnteredClaim (0.695 only, wire 0xF9 / 0x63 {u64 claim}): sent by the client's site-entry path after the
        /// claim music / time and Lua OnEnterClaim, through FUN_142da8230 (trace/0695/leads-L33-L35-L36.md, L33). No
        /// reply is read. [P] id table cClaimsPacketIdPlayerEnteredClaim 0x63 (packets-695.md §8).
        /// </summary>
        private const byte PlayerEnteredClaimSub = 0x63;

        /// <summary>
        /// ClaimsPacketSetAdventureFlags (0.695 only, 0xF9 / 0x45; id table cClaimsPacketIdUpdateAdventureFlags 0x45,
        /// packets-695.md §6): the Play Mode Rules window's
        /// toggles. Lua Claims.SetClaimAdventureFlag(claim, bit, on) (FUN_1435e6470; claim 0 = the manager's current
        /// claim at +0x348, the one the player stands on, L55) → FUN_142dd08e0, which looks the claim up in the client's
        /// own claim table, takes its current mask (claim entry +0x380) with the bit set or cleared, and writes
        /// {u64 claim, u64 new mask, u8 1} (FUN_142da6bd0). The whole mask comes back, so the server just stores it
        /// (<see cref="SetAdventureFlags"/>). The client does not change its own entry until ClaimInfoUpdate kind 0x17.
        /// </summary>
        private const byte SetAdventureFlagsSub = 0x45;

        /// <summary>
        /// Refusals that don't depend on the spot: already at the site limit (10). Pioneer landscapes are allowed
        /// (sites on Pioneer islands since 29 Sep 2016, P6500) and no flag is needed (see above).
        /// </summary>
        private static uint? OwnerRefusal(WorldSession session)
        {
            lock (sync)
                if (claims.Count(c => c.OwnerGuid == session.PlayerGuid) >= SlotsOf(session.PlayerGuid))
                    return ResultMaxOwned;
            return null;
        }

        /// <summary>
        /// Routes a ClaimsBase request (u8 sub) from the client: placement (0x1A request, 0x1B confirm), Site Management
        /// (0x1C management data, 0x1E edit data, 0x20 upkeep, renames, descriptions, permissions, delete), and the requests
        /// the client makes when windows open (0x27 sites by owner, 0x35 prop exclusion areas). 0x19 is deliberately left
        /// unanswered: see docs/protocol.md, section 10.
        /// </summary>
        /// <summary>Unhandled claims subs already logged at Info this run.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<byte, bool> unknownSubs = new();
        public static void HandleRequest(WorldSession session, byte sub, byte[] body)
        {
            var reader = new GamePacketReader(body);
            if (sub == StartClaimPlacementSub)
            {
                // The Site Management window's "Place this build site": the same start as using a flag (Command 0x4D).
                BeginPlacement(session);
                return;
            }
            if (sub == PlayerEnteredClaimSub)
            {
                Entered(session, reader.ReadULong());
                return;
            }
            if (sub == SetAdventureFlagsSub)
            {
                if (body.Length < 16)
                {
                    log.Debug($"{session.CharacterName}: short SetAdventureFlags ({body.Length} bytes).");
                    return;
                }
                ulong id = reader.ReadULong();
                SetAdventureFlags(session, id, reader.ReadULong());   // the trailing u8 (always 1) is not needed
                return;
            }
            switch (sub)
            {
                case 0x1A:
                    PlaceRequest(session, reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
                    break;
                case 0x1B:
                    PlaceConfirm(session, reader.ReadBool());
                    break;
                case 0x1C:
                    SendManagementData(session);
                    SendUpkeepData(session);
                    Achievements.Progress(session, Achievements.OpenSiteManagement);
                    break;
                case 0x20:
                    SendUpkeepData(session);
                    break;
                case 0x2F:
                    Delete(session, reader.ReadULong());
                    break;
                case 0x43:
                {
                    // SetAdventureStatus (F5 = Lua Claims.ToggleAdventureStatus, client FUN_142d33110):
                    // {u64 claim, u32 status (2 play, 1 edit), u8 1}. The client keeps its own play flag (+0x840).
                    ulong id = reader.ReadULong();
                    SetAdventureStatus(session, id, reader.ReadUInt());
                    break;
                }
                case 0x2A:
                {
                    // SetItemName (client writer FUN_142d073e0): {u64 claim, string name, u8 1}.
                    ulong id = reader.ReadULong();
                    string name = reader.ReadString();
                    AfterTyping(id, false, () => SetName(session, id, name));
                    break;
                }
                case 0x2B:
                {
                    // SetItemDescription (Lua Claims.SetClaimWelcomeMessage, writer FUN_142d331a0): {u64 claim, string}.
                    ulong id = reader.ReadULong();
                    string text = reader.ReadString();
                    AfterTyping(id, true, () => SetDescription(session, id, text));
                    break;
                }
                case 0x2C:
                {
                    // SetTagString (Lua Claims.SetClaimTags, FUN_1435318e0; packet built in FUN_142d33950's caller):
                    // {u64 claim, string tags}. The client has already applied them to its own record.
                    ulong id = reader.ReadULong();
                    SetTags(session, id, reader.ReadString());
                    break;
                }
                case 0x2D:
                {
                    // SetItemBasicPermissions (Lua Claims.SetBasicSecuritySettings FUN_1435e4180, writer FUN_1435e6760):
                    // {u64 claim, u32 public, u32 group, u32 guild, u8 1}. Each mask is Lua args 2-4 turned from a level
                    // name into a mask by FUN_142dbd940 (nil / "" → 0).
                    ulong id = reader.ReadULong();
                    uint everyone = reader.ReadUInt(), group = reader.ReadUInt(), guild = reader.ReadUInt();
                    SetDefaultPermissions(session, id, everyone, group, guild);
                    break;
                }
                case 0x2E:
                {
                    // SetItemIndividualPermission (Lua Claims.SetAdvancedSecuritySettings FUN_1435e42a0 /
                    // DeleteAdvancedSecuritySetting FUN_1435e4400, writer FUN_1435e68c0): {u64 claim, u8 (Lua arg 4 /
                    // 3 as a bool; the shipped window passes false), string name, u8 0, u32 mask, u8 1}. Delete sends
                    // the mask 0x80000000.
                    ulong id = reader.ReadULong();
                    reader.ReadByte();
                    string name = reader.ReadString();
                    reader.ReadByte();
                    SetSpecialPermissions(session, id, name, reader.ReadUInt());
                    break;
                }
                case 0x19:
                    // RequestAllClaimBasicInfo: answered with SendAllClaimBasicInfo (sub 1), the light list of every
                    // site on the landscape. (Answering with the full AOI list instead made the client re-request
                    // 0x19/0x27/0x35 in a tight loop.)
                    SendBasicInfo(session);
                    break;
                case 0x1E:
                    // RequestEditClaimItemData {u64 claim id}: EditClaimItemData (9) {u64 id, bool, claim record};
                    // the client applies it to the selected site (Build Site Management, Special Access list).
                    SendEditData(session, body.Length >= 8 ? reader.ReadULong() : 0);
                    break;
                case 0x27:
                    SendClaimsByOwner(session);
                    break;
                case 0x40:
                {
                    // SetClaimTime (ClaimsPacketSetClaimTime, built by FUN_142d33d30 / FUN_142d33f90 for Lua
                    // SetClaimTimeHour / SetClaimTimeRate): {u64 claim, i32 owner hour, f32 owner rate, i32 visitor
                    // hour, f32 visitor rate, u8}.
                    ulong id = reader.ReadULong();
                    SetTime(session, id, (int)reader.ReadUInt(), reader.ReadSingle(), (int)reader.ReadUInt(), reader.ReadSingle());
                    break;
                }
                case 0x42:
                    // SetMusicOverrideId (Lua Claims.SetClaimMusicOverrideId, FUN_142d334c0): {u64 claim, u32 music, u8 1}.
                    if (body.Length >= 12)
                        SetMusic(session, reader.ReadULong(), reader.ReadUInt());
                    break;
                case 0x41:
                    // SubmitContestEntry (Lua EnterContest from the Contest window): see Contests.
                    if (body.Length >= 12)
                        WorldChannelServices.Commerce.ContestSubmit(session, body);
                    break;
                case 0x44:
                    // OnClientRateClaim {u64 claim}: the client reports a vote it just had confirmed (RatingBase
                    // VoteReply, built in FUN_142d1f470). The vote itself is counted by Ratings; nothing to answer.
                    break;
                case ClaimInventory.DebugAddItemSub:
                    // DebugAddItemClaim (/item claimAddItem, admins): an item into the site's inventory (L146).
                    ClaimInventory.HandleDebugAdd(session, body);
                    break;
                case ClaimInventory.MoveItemToPlayerSub:
                    // ClaimRequestMoveItemToPlayer (/item claimItemToInventory, admins): a site item to a player (L146).
                    ClaimInventory.HandleMoveToPlayer(session, body);
                    break;
                case 0x35:
                    // RequestPropExclusionArea: RecievePropExclusionArea (0x11) {u32, two empty lists} (reader FUN_142da0c00:
                    // u32 + two list counts; one count was missing until the 2026-10-17 wire audit) [P].
                    SendSimple(session, 0x11, w => { w.Write(0u); w.Write(0u); w.Write(0u); });
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Claims 0xF9", sub);
                    // Once per sub at Info. (The six 0xE3 in the dev world's opcode_stats were PlayerEnteredClaim 0x63 as
                    // the 15 Oct 2026 gateway translation renumbered it, 0x80 | 0x63, before this class handled it: OPEN-LEADS
                    // L202. No 0.695 client builder sends 0xE3.)
                    if (unknownSubs.TryAdd(sub, true))
                        log.Info($"Unhandled claims request 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body, 0, Math.Min(body.Length, 64))}");
                    else
                        log.Debug($"Unhandled claims request 0x{sub:X2}: {BitConverter.ToString(body)}");
                    break;
            }
        }

        /// <summary>
        /// A 0.695 client says it walked onto a site. Trusted only when the server agrees the player stands on it (the
        /// client may send it for a site whose record arrived first); then the visit counts (Build Site Tourist, the
        /// visit achievements: once per site a day, deduplicated with the position check of 0.589 clients, which never
        /// send it). Nothing is answered. Reward-chest lockout notices hook in here once chests exist (TASKS 3.11).
        /// </summary>
        /// <summary>How far (m) outside a site a player may stand when their client reports entering it (ours).</summary>
        private const float EnterMargin = 4f;

        /// <summary>Is a local-space point inside the site's box grown by <paramref name="margin"/> metres?</summary>
        private static bool Near(uint zone, Vector3 local, ulong claimId, float margin)
        {
            var p = Terrain.TerrainEdits.ToGlobal(local);
            double m = margin * 10;   // the boxes are in global decimetres
            lock (sync)
                return claims.Any(c => c.Id == claimId && c.ZoneId == zone && p.X >= c.MinX - m && p.X <= c.MaxX + m
                    && p.Y >= c.MinY - m && p.Y <= c.MaxY + m && p.Z >= c.MinZ - m && p.Z <= c.MaxZ + m);
        }

        private static void Entered(WorldSession session, ulong claim)
        {
            // The server's position can be one movement update behind the client's when it crosses the border, so a
            // report for a site within EnterMargin of that position is trusted too (agent-e3-platform.md §1).
            if (ClaimAt(session.ZoneId, session.Position) != claim && !Near(session.ZoneId, session.Position, claim, EnterMargin))
            {
                log.Debug($"{session.CharacterName} reports entering site {claim}, but stands elsewhere: ignored.");
                return;
            }
            DailyGoals.Moved(session, session.Position);
            // Reward chests: the site-level lockout notice "ClaimPlayerLoot.ChestsYouCantLoot" (L33; spec U1).
            RewardChests.Entered(session, claim);
        }

        /// <summary>
        /// SendAllClaimBasicInfo (sub 1; reader FUN_142d08ce0, handler FUN_142d34900): u64, u32 n, then n
        /// ClientClaimBasicData (FUN_142d019f0): u64 id, u64 parent group (0 = root site), u64 owner, u32 × 3, f32 min x/y/z, f32 max x/y/z (local
        /// metres), u32, bool, u32, u32. The client keeps them in its site index (claims manager +0x1c0, FUN_142d134f0)
        /// and shows them on the map (FUN_142cd78a0).
        /// </summary>
        public static void DevSendBasicInfo(WorldSession session) => SendBasicInfo(session);

        private static void SendBasicInfo(WorldSession session)
        {
            List<ClaimModel> all;
            uint zone = session.ZoneId;
            lock (sync)
                all = claims.Where(c => c.ZoneId == zone).ToList();
            var origin = Terrain.TerrainStore.ZoneOrigin;
            SendSimple(session, 1, w =>
            {
                w.Write(0ul);
                w.Write((uint)all.Count);
                foreach (ClaimModel c in all)
                {
                    w.Write(c.Id);
                    w.Write(0ul);                       // parent group: none (a root site)
                    w.Write(c.OwnerGuid);
                    w.Write(0u);
                    w.Write(0u);
                    w.Write(0u);
                    w.Write((float)((c.MinX - origin.X) / 10));
                    w.Write((float)((c.MinY - origin.Y) / 10));
                    w.Write((float)((c.MinZ - origin.Z) / 10));
                    w.Write((float)((c.MaxX - origin.X) / 10));
                    w.Write((float)((c.MaxY - origin.Y) / 10));
                    w.Write((float)((c.MaxZ - origin.Z) / 10));
                    w.Write(0u);
                    w.Write(false);
                    w.Write(0u);
                    w.Write(0u);
                }
            });
        }

        /// <summary>
        /// PlaceClaimRequest (0x1A {f64 x, f64 y, f64 z global centre, ...}): checks the spot and remembers it until the
        /// player confirms. PlaceClaimResponse result 1 shows the client's confirmation dialog; a refusal code shows its
        /// text and puts the client back into placement (FUN_142fbc270).
        /// </summary>
        private static void PlaceRequest(WorldSession session, double x, double y, double z)
        {
            uint result = CanPlace(session, x, y, z);
            if (result == ResultConfirm)
                lock (sync)
                    pending[session] = (x, y, z);
            log.Info($"{session.CharacterName} asks to claim at ({x:F0}, {y:F0}, {z:F0}): result {result}.");

            // PlaceClaimResponse (reader FUN_142d01130, shown by FUN_142d199b0 in ClaimDialogs.ClaimConfirmDialog):
            // u32 result (1 = ok → confirmation UI), u64, u32, u32 MaxDaysUpkeep, u32, u32, u64 expiry (Unix
            // seconds; the dialog shows its local time). Zeros showed "0 days".
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)6);
            writer.Write(result);
            writer.Write(0ul);
            writer.Write(0u);
            uint days = SiteUpkeep.DaysOf(session.PlayerGuid);
            writer.Write(days);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((ulong)DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds());
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
        }

        /// <summary>
        /// Can the player place a site centred on a global point? <see cref="ResultConfirm"/>, or the refusal the client
        /// shows: Pioneer landscape, no flag, site limit (<see cref="OwnerRefusal"/>), unknown landscape (3), too close
        /// to another site (26, <see cref="IsFree(uint, double, double, double)"/>), or on a graveyard (3
        /// "Claims.InvalidClaimLocation", as the client's own exclusion-sphere check FUN_142dc6870 answers;
        /// <see cref="Graveyards.BlocksSite"/>; graveyards can't be claimed, P3548).
        /// </summary>
        private static uint CanPlace(WorldSession session, double x, double y, double z)
        {
            if (Landscapes.Get(session.ZoneId) == null)
                return ResultInvalidLocation;
            if (HeightRefusal(session.ZoneId, x, y, z) is uint tooFar)
                return tooFar;
            if (OwnerRefusal(session) is uint refusal)
            {
                if (refusal == ResultMaxOwned)
                {
                    // The client's text says how to get more sites; also say where the existing one is, which is easy to
                    // forget when it is on another landscape.
                    ClaimModel owned;
                    lock (sync)
                        owned = claims.FirstOrDefault(c => c.OwnerGuid == session.PlayerGuid);
                    uint text = owned == null ? 0 : Landscapes.Get(owned.ZoneId)?.NameText ?? 0;
                    string where = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(text) ?? $"landscape {owned?.ZoneId}";
                    if (owned != null)
                        Chat.System(session, $"Your Build Site \"{owned.Name}\" is on {where}.");
                }
                return refusal;
            }
            if (!IsFree(session.ZoneId, x, y, z))
                return ResultBufferOverlap;
            // The Spire area (90 m round the Hub, P1832 "You can no longer place a claim in this area") and the graveyards: the
            // client refuses both itself (code 3), except for a player with FreeBuild or a site expansion, so the server too.
            if (Spire.BlocksSite(session.ZoneId, x, y, z, HalfSize, HalfHeight))
                return ResultInvalidLocation;
            return Graveyards.BlocksSite(session.ZoneId, x, y, z, HalfSize, HalfHeight) ? ResultInvalidLocation : ResultConfirm;
        }

        /// <summary>
        /// The client's test of a build site against a ClaimPlacementExclusionZoneSphere (trace/0695/spire-claims.md, verified):
        /// the sphere's octree box is a column (FUN_1436dd1a0: ±r on x and z, unbounded in y), so a site whose box overlaps it on
        /// x and z (|dx|, |dz| &lt; half size + r) and whose middle is closer than r + its half diagonal (FUN_1436ddd80) is
        /// refused. Local metres for the centres, decimetres for the half sizes.
        /// </summary>
        internal static bool InExclusionSphere(Vector3 centre, float radius, Vector3 middle, double halfSize, double halfHeight)
        {
            float half = (float)(halfSize / 10), diagonal = (float)(Math.Sqrt(2 * halfSize * halfSize + halfHeight * halfHeight) / 10);
            return Math.Abs(middle.X - centre.X) < half + radius && Math.Abs(middle.Z - centre.Z) < half + radius
                && Vector3.Distance(middle, centre) < radius + diagonal;
        }

        /// <summary>
        /// Is a point (global dm) inside any build site's box on a landscape, within <paramref name="marginXZ"/> dm on x / z? Pure
        /// geometry, no permissions: trees and plants keep out of sites whoever may build there (Claims.MayEdit(0, …) let
        /// them grow in sites whose Default Access includes Build).
        /// </summary>
        public static bool CoversPoint(uint zone, double x, double y, double z, double marginXZ = 0)
        {
            lock (sync)
                return claims.Any(c => c.ZoneId == zone && x >= c.MinX - marginXZ && x <= c.MaxX + marginXZ && y >= c.MinY - 10
                    && y <= c.MaxY && z >= c.MinZ - marginXZ && z <= c.MaxZ + marginXZ);
        }

        /// <summary>
        /// How far a site's centre may sit from the ground below it, metres (client constants, StringHashToValue.txt,
        /// same in 0.589 and 0.695): ClaimPlacement.TerrainFollowHeightOffsetMin -75 and ...Max 150. The placement tool
        /// (0.695 FUN_143662a00 reads them into DAT_14528f6d4 / DAT_14528f6d0; 0.589 FUN_1435afc20, DAT_1451b4d04 /
        /// DAT_1451b4d00) keeps its height offset (tool +0x88, moved by Ctrl+wheel) between them (0.695 FUN_143662f30 end;
        /// reset into the range on start, FUN_143663900) and puts the box's bottom at ground + offset − half the box
        /// height
        /// (FUN_143664280; 0.589 FUN_1435b0150 / FUN_1435b0b20 / FUN_1435b14a0: the ground from FUN_1430a8040 at the box's
        /// centre x / z, half height = ClaimSize y × 0.5), so the requested centre is ground + offset. With the 180 m box
        /// that is: at most 15 m of the box above ground when sunk, at most 60 m of air under it when floated — as the
        /// notes put it, a site may sink a little into the ground or float a little above it (21 Mar 2016, P4135), and
        /// none may sit far from the surface underground (P4298). The 0.695 tool code and its defaults (100 / -100) are the
        /// same as 0.589's [P] (trace/0695/phaseE4-constants.md F3).
        /// </summary>
        private const float TerrainFollowHeightOffsetMin = -75f, TerrainFollowHeightOffsetMax = 150f;

        /// <summary>
        /// Slack on the height limits, metres (<b>ours</b>): our ground (TerrainHeights, sampled from the client's own
        /// heightmap function on a 2 m grid, bilinear) can differ a little from the client's sample, and the cursor box is
        /// snapped to the voxel grid (FUN_1435b0ef0).
        /// </summary>
        private const float HeightSlack = 2f;

        /// <summary>
        /// TASKS 6.47: refuses a site centre (global dm) further from the generated ground than the client's placement tool
        /// allows (<see cref="TerrainFollowHeightOffsetMin"/>): result 3 "Claims.InvalidClaimLocation" ("The Build Site
        /// location selected is invalid."). The client can't ask for such a centre itself; this stops a crafted request.
        /// No ground known there (no height grid): no check. Null when allowed.
        /// </summary>
        private static uint? HeightRefusal(uint zone, double x, double y, double z)
        {
            var origin = Terrain.TerrainStore.ZoneOrigin;
            float lx = (float)((x - origin.X) / 10), ly = (float)((y - origin.Y) / 10), lz = (float)((z - origin.Z) / 10);
            if (Terrain.TerrainHeights.HeightAt(lx, lz, zone) is not float ground)
                return null;
            float offset = ly - ground;
            if (offset >= TerrainFollowHeightOffsetMin - HeightSlack && offset <= TerrainFollowHeightOffsetMax + HeightSlack)
                return null;
            log.Info($"Site centre {offset:F0} m from the ground at ({lx:F0}, {lz:F0}): outside {TerrainFollowHeightOffsetMin}..{TerrainFollowHeightOffsetMax} m.");
            return ResultInvalidLocation;
        }

        /// <summary>
        /// PlaceClaimConfirm (0x1B {bool}): creates the site at the remembered centre, takes the flag, saves it, answers
        /// PlaceClaimComplete and tells everyone about the new site.
        /// </summary>
        private static void PlaceConfirm(WorldSession session, bool accept)
        {
            (double X, double Y, double Z) centre;
            lock (sync)
            {
                if (!pending.Remove(session, out centre))
                    return;
            }
            if (!accept)
                return;
            uint result = CanPlace(session, centre.X, centre.Y, centre.Z);
            if (result != ResultConfirm)
            {
                SendComplete(session, result, null);
                return;
            }

            var claim = new ClaimModel
            {
                Id = (ulong)Interlocked.Increment(ref nextId),
                OwnerGuid = session.PlayerGuid,
                Name = $"{session.CharacterName}'s Claim",
                MinX = centre.X - HalfSize,
                MaxX = centre.X + HalfSize,
                MinY = centre.Y - HalfHeight,
                MaxY = centre.Y + HalfHeight,
                MinZ = centre.Z - HalfSize,
                MaxZ = centre.Z + HalfSize,
                ZoneId = session.ZoneId,
                DefaultPermissions = DefaultAccess
            };
            lock (sync)
                claims.Add(claim);
            Terrain.ZoneTerrain.ClearVeinsInSite(claim.ZoneId, (claim.MinX, claim.MinY, claim.MinZ), (claim.MaxX, claim.MaxY, claim.MaxZ));
            Gathering.SitePlaced(claim.ZoneId, (claim.MinX, claim.MinY, claim.MinZ), (claim.MaxX, claim.MaxY, claim.MaxZ));   // the trees in it go
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c => c.Claim.Add(claim))
                .ContinueWith(t => log.Error(t.Exception, $"Failed to save claim {claim.Id}."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);

            SendComplete(session, ResultPlaced, claim);
            var add = BuildAoiUpdate(false, new[] { claim }, Array.Empty<ulong>());
            // The placer too: PlaceClaimComplete alone doesn't make the client treat the new site as one it stands in
            // (props couldn't be placed in it until the next login, when the full list arrives).
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, add);
            log.Info($"{session.CharacterName} placed claim {claim.Id}.");
            Achievements.ClaimPlaced(session, claim.ZoneId);   // "Adventure landscape Build Site claimed" (511, TASKS 5.10)
            SendSiteRows(session);
            SendClaimsByOwner(session);
            EnsureStartingLocation(session);
        }

        /// <summary>
        /// ClaimManagmentData (sub 8, reader FUN_142d00990): u64 UpkeepSecondsLeftAtRate, then the player's sites as
        /// ClientClaimManagmentItem {bool IsGroup, u64 id, string Name, bool HasOverridingSettings, u64 ParentGroup,
        /// string CategoryName, u32 world, u32 zone} (FUN_142d02180).
        /// </summary>
        private static void SendManagementData(WorldSession session)
        {
            List<ClaimModel> mine;
            lock (sync)
                mine = claims.Where(c => c.OwnerGuid == session.PlayerGuid).ToList();
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)8);
            writer.Write((ulong)TimeSpan.FromDays(SiteUpkeep.DaysOf(session.PlayerGuid)).TotalSeconds);
            writer.Write((uint)mine.Count);
            foreach (ClaimModel claim in mine)
            {
                writer.Write(false);
                writer.Write(claim.Id);
                writer.Write(claim.Name ?? "");
                writer.Write(false);
                writer.Write(0ul);
                writer.Write("");
                // The site's world and landscape (item +0x50, +0x54). The window compares them with the player's own
                // (game +0x378cc world, +0x378d0 zone) and, when they differ, adds "(landscape name)" — "(Zone 0)"
                // with zeros.
                writer.Write(Landscapes.WorldOf(claim.ZoneId));
                writer.Write(claim.ZoneId);
            }
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
        }

        /// <summary>
        /// UpkeepManagmentData (sub 10, reader FUN_142d014e0, shown by FUN_142d1b1a0): u64 expiry (Unix seconds),
        /// u32 RootClaimsOwned, AttachedClaimsOwned, PlayerMaxRootClaims, PlayerMaxAttachedClaims,
        /// MaxRootClaimsAllowed, MaxAttachedClaimsAllowed, u32 warning (1 → GameEvents:OnUpkeepWarning), MaxDaysUpkeep,
        /// then three u32 kept by the client (+0x48, +0x40, +0x44).
        /// The player is in the world, so their timer is full: <see cref="SiteUpkeep.DaysOf"/> from now. The Site Management
        /// window shows MaxDaysUpkeep as the days left, "Your Max Possible Build Sites" (PlayerMaxRootClaims) and "Build
        /// Site Unlocks Available" (MaxRootClaimsAllowed minus that), with a Buy button for "Site Expansion" products.
        /// </summary>
        /// <summary>
        /// Point-of-Need upkeep (Lua Claims.PayUpkeepPoN(days, flag) from ClaimPoN.lua → InGamePurchase 0x47
        /// PointOfNeedRequest type 1 {u32 1, u8 flag, u32 days, u64, u64}). Upkeep is free here and never runs out while
        /// the owner plays, so it is simply confirmed and the timer re-sent.
        /// </summary>
        public static void PayUpkeep(WorldSession session, uint days)
        {
            Chat.System(session, $"Upkeep paid for {days} day(s): free on this server.");
            SendUpkeepData(session);
            log.Info($"{session.CharacterName} paid upkeep for {days} day(s) (free).");
        }

        /// <summary>
        /// The Site Management window's rows after the player's site count changed (TASKS 3.26). The 0.695 window lists one
        /// row per site from the management data (claimsListDS, sub 8), then PlayerMaxRootClaims − that count "Place this
        /// build site" rows and a Buy row while below MaxRootClaimsAllowed, both from the upkeep data (sub 10); it re-lists
        /// when either data source changes (ClaimManagementWindowDocumentClass.as 113-129, 281-334), so both are sent.
        /// </summary>
        private static void SendSiteRows(WorldSession session)
        {
            SendManagementData(session);
            SendUpkeepData(session);
        }

        /// <summary>
        /// A player's build site slots: one, plus one per Build Site Expansion used, up to <see cref="MaxRootClaimsAllowed"/>
        /// ("increase by one the number of Build Sites it is possible for you to place, up to a maximum of eight", 571247).
        /// </summary>
        public static uint SlotsOf(ulong characterId)
            => (uint)Math.Min(MaxRootClaimsAllowed, MaxClaimsPerOwner + Unlocks.ValuesOfAnyone(characterId, Unlocks.SiteExpansions).Count);

        /// <summary>
        /// Using a Build Site Expansion: one more slot, then the Site Management rows are sent again. At eight slots the
        /// use is refused with the client's 571174 "You already have the maximum number of Build Site Expansions!"
        /// (<see cref="ItemUses.Refuse"/>: InventoryPacketShowActivationFailure 0x2D / 0x1E, handler FUN_142f202e0 case
        /// 0x1E, an "Activation Failure" box) and the item is kept.
        /// </summary>
        public static bool UseExpansion(WorldSession session)
        {
            uint slots = SlotsOf(session.PlayerGuid);
            if (slots >= MaxRootClaimsAllowed)
            {
                ItemUses.Refuse(session, ItemUses.MaxSiteExpansionsText);
                return false;
            }
            if (!session.TakeItem(Inventory.ItemDefinitionBuilder.BuildSiteExpansion, 1))
                return false;
            Unlocks.Add(session, Unlocks.SiteExpansions, slots + 1);
            SendSiteRows(session);
            Chat.System(session, $"You can now place {slots + 1} Build Sites.");
            log.Info($"{session.CharacterName} used a Build Site Expansion: {slots + 1} slots.");
            return true;
        }

        private static void SendUpkeepData(WorldSession session)
        {
            int owned;
            lock (sync)
                owned = claims.Count(c => c.OwnerGuid == session.PlayerGuid);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)10);
            uint days = SiteUpkeep.DaysOf(session.PlayerGuid);
            writer.Write((ulong)DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeSeconds());
            writer.Write((uint)owned);
            writer.Write(0u);
            writer.Write(SlotsOf(session.PlayerGuid));   // PlayerMaxRootClaims: this player's slots
            writer.Write(0u);
            writer.Write((uint)MaxRootClaimsAllowed);    // MaxRootClaimsAllowed: the most anyone can have
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(days);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
        }

        /// <summary>
        /// Removes build sites whose owner hasn't been in the world for their timer (<see cref="SiteUpkeep.DaysOf"/>;
        /// checked hourly): packed, and the owner mailed (<see cref="SiteContents.Pack"/>).
        /// </summary>
        public static void Update()
        {
            if (!modesRestored)
                RestoreModes();
            DateTime now = DateTime.UtcNow;
            if (now < nextExpiryCheck)
                return;
            nextExpiryCheck = now + ExpiryCheckInterval;
            List<ClaimModel> all;
            lock (sync)
                all = claims.ToList();
            if (all.Count == 0)
                return;
            HashSet<ulong> online = PlayerManager.Snapshot().Select(p => p.PlayerGuid).ToHashSet();
            Dictionary<ulong, DateTime?> lastOnline = DatabaseManager.Instance.CharacterDatabase
                .GetLastOnline(all.Select(c => c.OwnerGuid));
            // Time each absent owner has left: their timer minus the time since they were last in the world.
            var left = new Dictionary<ulong, (TimeSpan Left, DateTime Seen)>();
            foreach (ulong owner in all.Select(c => c.OwnerGuid).Distinct())
                if (!online.Contains(owner) && lastOnline.TryGetValue(owner, out DateTime? last) && last is DateTime seen)
                {
                    DateTime utc = DateTime.SpecifyKind(seen, DateTimeKind.Utc);
                    left[owner] = (TimeSpan.FromDays(SiteUpkeep.DaysOf(owner)) - (now - utc), utc);
                }
            foreach (var (owner, (remaining, seen)) in left)
                if (remaining > TimeSpan.Zero && remaining <= SiteUpkeep.WarningTime)
                {
                    string name = DatabaseManager.Instance.CharacterDatabase.GetCharacterById(owner).GetAwaiter().GetResult()?.Name ?? "";
                    SiteUpkeep.MailWarning(owner, name, seen);
                }
            List<ClaimModel> expired = all.Where(c => left.TryGetValue(c.OwnerGuid, out var l) && l.Left < TimeSpan.Zero).ToList();
            if (expired.Count == 0)
                return;
            // A lapsed site's design is saved from the terrain before it is reverted: with a remote Environment Server away
            // (terrain-service.md §6) that waits for the next hourly check rather than saving an empty design.
            if (!Terrain.EnvironmentServer.Instance.Available)
            {
                log.Warn($"{expired.Count} lapsed site(s) wait: the Environment Server is unreachable.");
                return;
            }
            Remove(expired, "upkeep expired");
            foreach (ClaimModel claim in expired)
                SiteContents.Pack(claim);
        }

        /// <summary>
        /// Writes a ClaimsBase packet: the u8 sub-opcode, then the body.
        /// </summary>
        /// <summary>ClaimsPacketLostClaimServerConnection (S→C, no body).</summary>
        public const byte LostClaimServerConnectionSub = 0x12;

        private static readonly Dictionary<WorldSession, DateTime> lastLostNotice = new();

        /// <summary>
        /// ClaimsPacketLostClaimServerConnection (sub 0x12, header only; OPEN-LEADS L142) [P]: the Claims dispatcher's case
        /// 0x11 (part_0069 ~14596) reads only the header (FUN_142da8e50) and calls FUN_142db7970 with the key
        /// "Claim.LostClaimServerConnection" → CodeStringMappings.txt:97 → text <b>562155</b> "Unable to process the
        /// request. Connection to the Build SiteServer is unavailable.", shown as the Lua SystemMessage (FUN_1433f98e0 on
        /// game +0x32488) and as a chat line of type 7 (FUN_142ca7900). The client blocks nothing. Sent when a site
        /// request is refused because the service holding the sites' terrain (the remote Environment Server, terrain-service.md
        /// §6) is away — the request's own refusal goes out as well. At most every 10 s per player [I: like
        /// <see cref="Terrain.ZoneTerrain.SendUnavailable"/>, so a repeated click does not stack messages].
        /// </summary>
        public static void SendLostServerConnection(WorldSession session)
        {
            DateTime now = DateTime.UtcNow;
            lock (lastLostNotice)
            {
                if (lastLostNotice.TryGetValue(session, out DateTime last) && now - last < TimeSpan.FromSeconds(10))
                    return;
                lastLostNotice[session] = now;
                foreach (WorldSession gone in lastLostNotice.Keys.Where(s => s.Disconnected).ToList())
                    lastLostNotice.Remove(gone);
            }
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, new[] { LostClaimServerConnectionSub });
            log.Info($"{session.CharacterName}: site request refused, Environment Server unreachable (LostClaimServerConnection).");
        }

        private static void SendSimple(WorldSession session, byte sub, Action<GamePacketWriter> write)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(sub);
            write(writer);
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
        }

        /// <summary>
        /// EditClaimItemData (sub 9 {u64 id, bool, claim record}) for the named site, or the player's own site when the id is
        /// unknown. The client applies it to the site selected in Site Management.
        /// </summary>
        private static void SendEditData(WorldSession session, ulong id)
        {
            ClaimModel claim;
            lock (sync)
                claim = claims.FirstOrDefault(c => c.Id == id) ?? claims.FirstOrDefault(c => c.OwnerGuid == session.PlayerGuid);
            if (claim == null)
                return;
            // {u64 id, bool is group, record}: true would send it to the group dialog and the site's Info tab would stay
            // on its defaults ("The Claim or Group", "Welcome!").
            SendSimple(session, 9, w => { w.Write(claim.Id); w.Write(false); WriteRecord(w, claim); });
        }

        /// <summary>PlaceClaimComplete (sub 7, reader FUN_142d08a50): u32 result (2 = placed), claim record.</summary>
        private static void SendComplete(WorldSession session, uint result, ClaimModel claim)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)7);
            writer.Write(result);
            WriteRecord(writer, claim ?? new ClaimModel());
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
        }

        /// <summary>
        /// AOIUpdate (sub 4 — the client dispatcher switches on sub-1; reader FUN_142d00720): bool full update,
        /// list of claim records added, list of u64 claim ids removed.
        /// </summary>
        private static byte[] BuildAoiUpdate(bool full, IReadOnlyCollection<ClaimModel> added, IReadOnlyCollection<ulong> removed)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)4);
            writer.Write(full);
            writer.Write((uint)added.Count);
            foreach (ClaimModel claim in added)
                WriteRecord(writer, claim);
            writer.Write((uint)removed.Count);
            foreach (ulong id in removed)
                writer.Write(id);
            return output.ToArray();
        }

        /// <summary>
        /// SendClaimsByOwner (0x0F, reader FUN_142d08870; handled in FUN_142d1ff50): {u64 owner, u32 n, n records
        /// (FUN_142cffaa0)}. The client keeps these as the player's own sites (claim manager +0x380), which the
        /// Travel window's My Sites lists, so sites on other landscapes (not in the AOI list) show up too.
        /// Record: u64 owner, string name, u64 id, 3 strings, f64 min x/y/z, f64 max x/y/z, u32, u32 landscape
        /// (compared with ZoneDetails' world id), u32 landscape, f32, u32, u32, empty list, u64, 5 strings, u64,
        /// two empty lists.
        /// </summary>
        private static void SendClaimsByOwner(WorldSession session)
        {
            List<ClaimModel> mine;
            lock (sync)
                mine = claims.Where(c => c.OwnerGuid == session.PlayerGuid).ToList();
            SendSimple(session, 0x0F, w =>
            {
                w.Write(session.PlayerGuid);
                w.Write((uint)mine.Count);
                foreach (ClaimModel claim in mine)
                {
                    w.Write(claim.OwnerGuid);
                    w.Write(claim.Name ?? "");
                    w.Write(claim.Id);
                    w.Write("");
                    w.Write("");
                    w.Write("");
                    w.Write(claim.MinX);
                    w.Write(claim.MinY);
                    w.Write(claim.MinZ);
                    w.Write(claim.MaxX);
                    w.Write(claim.MaxY);
                    w.Write(claim.MaxZ);
                    w.Write(0u);
                    w.Write(claim.ZoneId);
                    w.Write(claim.ZoneId);
                    w.Write(0f);
                    w.Write(0u);
                    w.Write(0u);
                    w.Write(0u);                        // empty list
                    w.Write(0ul);
                    for (int i = 0; i < 5; i++)
                        w.Write("");
                    w.Write(0ul);
                    w.Write(0u);                        // empty list
                    w.Write(0u);                        // empty list
                }
            });
            // Tags of the player's sites on other landscapes (SendAll covers this one).
            foreach (ClaimModel claim in mine.Where(c => c.ZoneId != session.ZoneId && !string.IsNullOrEmpty(c.Tags)))
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(claim, InfoTags));
        }

        /// <summary>
        /// The site's tags (Build Site Management): saved and sent to everyone on the landscape as ClaimInfoUpdate kind 4,
        /// which puts the record's first string into the site's tags (client claim +0x3e8) and has the client re-ask
        /// whether it may vote for contests ("Claims;Contests"). Tags starting with "contest_" are the client's own
        /// contest markers. A site published to the gallery keeps its listing's tags in step (Commerce).
        /// </summary>
        private static void SetTags(WorldSession session, ulong claimId, string tags)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null)
                return;
            tags = (tags ?? "").Trim();
            if (tags.Length > MaxTags)
                tags = tags[..MaxTags];
            claim.Tags = tags;
            ulong id = claim.Id;
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                model.Tags = tags;
                c.MarkModified(model, nameof(model.Tags));
            });
            var update = BuildInfoUpdate(claim, InfoTags);
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId).Where(o => o != session))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            if (session.ZoneId != claim.ZoneId)
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            log.Info($"{session.CharacterName} set site {id} tags to \"{tags}\".");
        }

        /// <summary>
        /// The site's time-of-day settings (Site Management, the build-mode time panel), saved and sent to everyone who
        /// sees the site. Who may: the 0.695 client sends ClaimsPacketSetClaimTime only when the player has bit 0x10 on the
        /// site (FUN_142dd15d0: FUN_142d98cc0(claim, player, 0x10), Ultimate Power, <see cref="PermissionBuild"/>; the
        /// 0.589 FUN_142d33d30 had no check) and applies the change on its own screen first, so the server accepts the owner
        /// and Ultimate Power builders alike. Anyone else gets the client's Claim.SetTime.Error (577525 "You cannot change
        /// the Time Settings on this Build Site.", a key no client code names) and the site's current time back. Until
        /// 2026-10-26 only the owner was accepted and a builder's change was dropped silently (trace/0695/phaseE3-text.md F1).
        /// </summary>
        internal static bool MaySetTime(ClaimModel claim, ulong player) => (PermissionsFor(claim, player) & PermissionBuild) == PermissionBuild;

        private static void SetTime(WorldSession session, ulong claimId, int hour, float rate, int visitorHour, float visitorRate)
        {
            ClaimModel claim = claimId != 0 ? Get(claimId)
                : ClaimAt(session.ZoneId, session.Position) is ulong here ? Get(here) : OwnedClaim(session, 0);
            if (claim == null)
                return;
            if (!MaySetTime(claim, session.PlayerGuid))
            {
                log.Info($"{session.CharacterName} may not set the time of site {claim.Id}: refused.");
                SystemMessages.ShowKey(session, "Claim.SetTime.Error");
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(claim, InfoTime));
                return;
            }
            // Hours as the client applies them on walking in (FUN_143d939c0): -1 = the world clock ("Game Time" / "Custom
            // Speed" in the build-mode time panel), -2 = the player's own PC clock (FUN_143d93bc0; no menu entry in 0.695,
            // Lua only), 0-23 = time stopped at that hour. Anything else is treated as the world clock.
            static int Hour(int h) => h is >= -2 and <= 23 ? h : -1;
            // Speeds as the client clamps them (FUN_143d93b90: 0.25 - 1000; the panel's slider runs 1 - 1000, BuildModeTimeWidget
            // container.as; dev post "Claim Time Controls": time can run up to 1000 times faster). Until 2026-10-18 the
            // server reset anything over 100 to 1 and sent that back at once (ClaimInfoUpdate 0x15), so Custom Speed above
            // 100 never took.
            static float Rate(float r) => float.IsFinite(r) ? Math.Clamp(r, 0.25f, 1000f) : 1f;
            (claim.TimeHour, claim.TimeRate) = (Hour(hour), Rate(rate));
            (claim.VisitorTimeHour, claim.VisitorTimeRate) = (Hour(visitorHour), Rate(visitorRate));
            ulong id = claim.Id;
            (int h1, float r1, int h2, float r2) = (claim.TimeHour, claim.TimeRate, claim.VisitorTimeHour, claim.VisitorTimeRate);
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                (model.TimeHour, model.TimeRate, model.VisitorTimeHour, model.VisitorTimeRate) = (h1, r1, h2, r2);
                c.MarkModified(model, nameof(model.TimeHour), nameof(model.TimeRate), nameof(model.VisitorTimeHour), nameof(model.VisitorTimeRate));
            });
            log.Info($"{session.CharacterName} set site {id} time: owner {h1} h ×{r1}, visitors {h2} h ×{r2}.");
            // Only the time block changes: the small info update instead of the whole site record.
            var update = BuildInfoUpdate(claim, InfoTime);
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
        }

        /// <summary>
        /// Claim record (client FUN_142d01c40; 0.695 FUN_142da1a50 adds a u32 to the site-game struct and a trailing u64).
        /// Only id, owner, name and bounds are filled; the rest (members, permissions lists, upkeep, contest data) is empty.
        /// </summary>
        private static void WriteRecord(GamePacketWriter writer, ClaimModel claim)
        {
            writer.Write(claim.Id);
            // Parent group, then owner. A non-zero parent makes the client file the site under that group
            // (FUN_142d16cf0 → FUN_143b5b220) instead of treating it as a root site (which also gets the buffer).
            writer.Write(0ul);
            writer.Write(claim.OwnerGuid);
            writer.Write(claim.Name);
            writer.Write(0ul);
            // The Info tab (FUN_142d19b90) shows record +0x38 as the site's name and +0x50 as its welcome message.
            writer.Write(claim.Name);
            writer.Write(claim.Description ?? "");
            writer.Write("");
            writer.Write(0u);
            writer.Write(claim.MinX);
            writer.Write(claim.MinY);
            writer.Write(claim.MinZ);
            writer.Write(claim.MaxX);
            writer.Write(claim.MaxY);
            writer.Write(claim.MaxZ);
            // Default Access: public, group, guild (record +0xb8/+0xbc/+0xc0 → 0.695 claim entry +0x2d8/+0x2dc/+0x2e0 by
            // FUN_142d9bad0; 0.589 entry +0x2d0/+0x2d4/+0x2d8). Site Management's data source names them PermissionsPublic,
            // PermissionsGroup and PermissionsGuild (FUN_142db8ab0). Each as the owner last sent it (0x2D).
            writer.Write(claim.DefaultPermissions);
            writer.Write(claim.GroupPermissions);
            writer.Write(claim.GuildPermissions);
            // Special access: players {u64 guid, u32 mask} (FUN_142d044a0: the client's permission checks), guilds
            // (same, FUN_142d04a20), then the named list Site Management shows (record +0x130, elements FUN_142d022d0:
            // {u64 guid, string name, u32 access, bool}; FUN_142d19b90 → ClaimEditDialog.ClaimItemAdvPermissions rows,
            // "Unknown" without a name).
            Dictionary<ulong, uint> players;
            Dictionary<ulong, string> names;
            lock (sync)
            {
                players = special.TryGetValue(claim.Id, out var map) ? new Dictionary<ulong, uint>(map) : new();
                names = players.Keys.ToDictionary(k => k, k => specialNames.GetValueOrDefault(k, ""));
            }
            writer.Write((uint)players.Count);
            foreach ((ulong guid, uint mask) in players)
            {
                writer.Write(guid);
                writer.Write(mask);
            }
            writer.Write(0u);
            writer.Write((uint)players.Count);
            foreach ((ulong guid, uint mask) in players)
            {
                writer.Write(guid);
                writer.Write(names[guid]);
                writer.Write(mask);
                writer.Write(false);
            }
            writer.Write(0u);
            // Record +0x158 → claim entry +0x138 (0.695 FUN_142db5ca0): the "admin template" (ClaimsPacketSetAsAdminClaim).
            // Non-zero makes the walk-on window show the contest-winner banner (WalkOnText AdminTemplateGuid, AS3
            // getIsAdminClaim): the Landmarks of Landmark sites (LandmarksOfLandmark.cs, TASKS 3.20). 0 elsewhere.
            writer.Write(LandmarksOfLandmark.AdminTemplateOf(claim.Id));
            writer.Write(false);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);                           // empty list
            // The five "supplemental" strings → claim entry +0x48 / +0x60 / +0x78 / +0x90 / +0xa8, which an admin site's
            // banner shows (FUN_142dc7540, ClaimDialogs.WinnerWalkOnText: WinnerCompetitionName, WinnerClaimName, WinnerCompetitionDescription,
            // WinnerCompetitionDate, contributors list), then the feedback recipient guid (+0x1e0 → entry +0xc0).
            string[] winner = LandmarksOfLandmark.WinnerTextsOf(claim.Id);
            for (int i = 0; i < 5; i++)
                writer.Write(winner?[i] ?? "");
            writer.Write(0ul);
            // The site's game (FUN_142cffe50 → claim entry +0x140, shown by the walk-on window).
            InstancedGames.WriteInfo(writer, claim.Id);
            writer.Write(0ul);
            writer.Write("");
            writer.Write(0u);                           // empty list
            // Time of day inside the site (FUN_142d01940 → client claim +0xc8..+0xd4, applied on walking in by
            // FUN_142d29390): owner hour (-1 = world clock) and rate, then the same for visitors. Zeros would force
            // midnight with the clock stopped, so the sky changed on every entry.
            writer.Write(claim.TimeHour);
            writer.Write(claim.TimeRate);
            writer.Write(claim.VisitorTimeHour);
            writer.Write(claim.VisitorTimeRate);
            writer.Write(0u);
            writer.Write(ModeOf(claim.Id));             // record +0x348 → claim entry +0x370 (2 = Play Mode)
            writer.Write(0u);                           // empty list
            // 0.695 (FUN_142da1a50): u64 Play Mode Rules flags (record +0x3e0 → claim entry +0x380), as stored by
            // ClaimsPacketSetAdventureFlags (<see cref="SetAdventureFlags"/>; trace/0695/packets-695.md §6).
            writer.Write(claim.AdventureFlags & AdventureFlagMask);
        }

        /// <summary>Dev "lapsesite": the player's site lapses now, as if its upkeep ran out (then Build Site Contents).</summary>
        public static void DevLapse(WorldSession session)
        {
            List<ClaimModel> mine;
            lock (sync)
                mine = claims.Where(c => c.OwnerGuid == session.PlayerGuid).ToList();
            if (mine.Count == 0)
                return;
            Remove(mine, $"lapsed (dev) for {session.CharacterName}");
            foreach (ClaimModel claim in mine)
                SiteContents.Pack(claim);
            SiteContents.Deliver(session);
        }

        /// <summary>Gives up every claim the player owns (dev command for now; later the claim management UI).</summary>
        public static void Abandon(WorldSession session)
        {
            List<ClaimModel> mine;
            lock (sync)
                mine = claims.Where(c => c.OwnerGuid == session.PlayerGuid).ToList();
            if (mine.Count > 0)
                Remove(mine, $"abandoned by {session.CharacterName}");
        }

        /// <summary>
        /// DeleteClaimItem (sub 0x2F: u64 claim id, bool, bool) from the Site Management window. As the client's dialog
        /// promises: the site goes, its terrain returns to its natural state and the Build Site Flag comes back. Answered
        /// with DeleteClaimItemReply (sub 0x30, reader FUN_142d00b70: u64 id, u32 result; client FUN_142d29200: 2 = done,
        /// 0x12 "must be on the same Landscape", 0x14 "site has a game object", others "Unable to recover build site")
        /// and fresh management data. The site's inventory (<see cref="ClaimInventory"/>, L146) comes back with it: its
        /// props as materials (P4870, launch rule) and its unplaced items as themselves. P0079's 2014 rule ("You can no
        /// longer delete a claim unless you have room in your inventory for all the items on it and the claim flag")
        /// predates the overflow tab (P0862): returned items that do not fit the bag wait in overflow, so a delete is not
        /// refused for room [I: that the overflow superseded the check].
        /// </summary>
        private static void Delete(WorldSession session, ulong claimId)
        {
            ClaimModel claim;
            lock (sync)
                claim = claims.FirstOrDefault(c => c.Id == claimId && c.OwnerGuid == session.PlayerGuid);
            // Remote Environment Server away (terrain-service.md §6): the site's design could not be saved nor its terrain
            // reverted, so the delete is refused ("Unable to recover build site") and can be tried again.
            uint result = claim == null ? DeleteFailed
                : claim.ZoneId != session.ZoneId ? DeleteForeignLandscape
                : InstancedGames.HasGame(claim.Id) ? DeleteHasGame
                : !Terrain.EnvironmentServer.Instance.Available ? DeleteFailed
                : DeleteDone;
            if (result == DeleteFailed && claim != null && !Terrain.EnvironmentServer.Instance.Available)
                SendLostServerConnection(session);
            if (result != DeleteDone)
                claim = null;
            if (claim != null)
            {
                Remove(new List<ClaimModel> { claim }, $"deleted by {session.CharacterName}", sendUpkeep: false);
                // "A copy of your current Build Site will be created and stored in your Designs list" (the dialog), with
                // the site's screenshots (SiteDesigns.Save).
                var (_, built) = SiteDesigns.Save(claim);
                int refunded = SiteDesigns.Refund(session, built);
                List<ulong> reverted = Terrain.EnvironmentServer.Instance.RevertBox(claim.MinX, claim.MinY, claim.MinZ,
                    claim.MaxX, claim.MaxY, claim.MaxZ, claim.ZoneId);
                Terrain.ZoneTerrain.Broadcast(reverted, claim.ZoneId);
                var origin = Terrain.TerrainStore.ZoneOrigin;
                // Props come back as materials (hotfix 26 Apr 2016, P4870; TASKS 6.39), chests' contents to overflow.
                var propMaterials = new List<(uint Item, uint Count)>();
                int returned = Props.ReturnInBox(session,
                    new Vector3((float)((claim.MinX - origin.X) / 10), (float)((claim.MinY - origin.Y) / 10), (float)((claim.MinZ - origin.Z) / 10)),
                    new Vector3((float)((claim.MaxX - origin.X) / 10), (float)((claim.MaxY - origin.Y) / 10), (float)((claim.MaxZ - origin.Z) / 10)), claim.ZoneId,
                    propMaterials);
                // The site's unplaced inventory items (L146; ClaimInventory) come back too — bag, then overflow (P0862).
                int unplaced = ClaimInventory.ReturnUnplaced(session, claim);
                log.Info($"Deleted site {claim.Id}: {reverted.Count} cell(s) reverted, {returned} prop(s) returned as "
                    + $"{propMaterials.Sum(m => (long)m.Count)} material item(s), {refunded} voxel material item(s) returned, "
                    + $"{unplaced} inventory item stack(s) returned.");
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write((byte)0x30);
            writer.Write(claimId);
            writer.Write(result);
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
            SendManagementData(session);
            SendUpkeepData(session);
            SendClaimsByOwner(session);
        }

        /// <summary>
        /// ClaimInfoUpdate kinds (sub 5, client FUN_142d1ff50): the site's game / the site was deleted / its time settings /
        /// its music changed; 0.695 adds 0x17, the Play Mode Rules mask (FUN_142da2840 +0x288 → claim entry +0x380).
        /// 5 = the Default Access masks changed, 6 = one player's Special Access changed (0.695 FUN_142dbecb0 case 4).
        /// </summary>
        private const uint InfoTags = 4, InfoPermissions = 5, InfoPlayerAccess = 6, InfoGame = 0x11, InfoMode = 0x13,
            InfoDeleted = 0x14, InfoTime = 0x15, InfoMusic = 0x16, InfoAdventureFlags = 0x17;
        /// <summary>The client keeps at most 128 characters of a site's tags (FUN_142d33950).</summary>
        private const int MaxTags = 128;

        /// <summary>A site's mode for the client (record +0x348 / ClaimInfoUpdate 0x13 → claim entry +0x370).</summary>
        private static uint ModeOf(ulong claimId) => IsPlaying(claimId) ? PlayMode : EditMode;



        /// <summary>Every build site (a copy of the list).</summary>
        public static List<ClaimModel> All()
        {
            lock (sync)
                return claims.ToList();
        }

        /// <summary>A build site by id, or null.</summary>
        public static ClaimModel Get(ulong claimId)
        {
            lock (sync)
                return claims.FirstOrDefault(c => c.Id == claimId);
        }

        /// <summary>
        /// The site's game changed (created, removed, state or player count): ClaimInfoUpdate kind 0x11 to everyone on
        /// the landscape, which refreshes the walk-on window of players standing on the site. Players in the middle of a
        /// re-zone are skipped: a site update right behind ClientBeginZoning hung the client in QuerySimpleVisibility
        /// (2026-10-11); the site games send it to their own players once they have arrived (<see cref="SendGameInfoTo"/>).
        /// </summary>
        public static void SendGameInfo(ulong claimId)
        {
            ClaimModel claim = Get(claimId);
            if (claim == null)
                return;
            var update = BuildInfoUpdate(claim, InfoGame);
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                if (other.RezoneTarget == null)
                    other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
        }

        /// <summary>The site's game info (ClaimInfoUpdate 0x11) to one player.</summary>
        public static void SendGameInfoTo(WorldSession session, ulong claimId)
        {
            ClaimModel claim = Get(claimId);
            if (claim != null)
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(claim, InfoGame));
        }

        /// <summary>
        /// The music a site plays for players walking in (client claim +0xf4, played by FUN_142d29390 through
        /// FUN_14347e590): chosen by the owner in Build Site Management, kept as the owner's <see cref="Unlocks.SiteMusic"/>
        /// (one site per player). 0 = the area's own music.
        /// </summary>
        private static uint MusicOf(ClaimModel claim)
            => Unlocks.ValuesOfAnyone(claim.OwnerGuid, Unlocks.SiteMusic).FirstOrDefault();

        /// <summary>The owner picked the site's music: save it and tell everyone on the landscape (ClaimInfoUpdate 0x16).</summary>
        public static void SetMusic(WorldSession session, ulong claimId, uint music)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null)
                return;
            Unlocks.Set(session, Unlocks.SiteMusic, music);
            var update = BuildInfoUpdate(claim, InfoMusic);
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            log.Info($"{session.CharacterName} set site {claim.Id} music to {music}.");
        }

        /// <summary>
        /// ClaimInfoUpdate (sub 5, reader FUN_142d029f0): u64 claim, u32 kind, then one fixed record the client reads
        /// fields from by kind. Kind 0x14 removes the site from the owner's own list as well (FUN_142d18090 mode 2;
        /// an AOI removal is only "left the area" and the site stays in My Sites); 0x15 applies the time block; 0x16 the
        /// site music (+0x280; 0.589 +0x278). Kind 5 applies the three Default Access masks (FUN_142d9bad0) and kind 6 one player's
        /// Special Access (u64 guid + first u32: FUN_143c17b00, 0x80000000 removes it); after kinds 5-7 and 0x11 the
        /// client recomputes the access of the site it stands in (0.695 FUN_142dbecb0 → FUN_143c17590, FUN_142dd2e10;
        /// 0.589 FUN_142d1ff50 the same).
        /// Layout: string, u32, 3 x u32 (Default Access), u64 (guid), 4 x u32 (value first), u64, 5 strings, u64, the
        /// FUN_142cffe50 struct, u64, string, empty list, time block (owner hour, rate, visitor hour, rate), u32 music, u32.
        /// </summary>
        private static byte[] BuildInfoUpdate(ClaimModel claim, uint kind, ulong guid = 0, uint value = 0)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)5);
            w.Write(claim.Id);
            w.Write(kind);
            // The first string is the name, and the tags for kind 4.
            w.Write((kind == InfoTags ? claim.Tags : claim.Name) ?? "");
            w.Write(0u);
            // +0x2c: the Default Access masks (read for kind 5 only; always the site's own, never misleading).
            w.Write(claim.DefaultPermissions);
            w.Write(claim.GroupPermissions);
            w.Write(claim.GuildPermissions);
            // +0x38 guid, +0x40 value: kind 6 (player Special Access) / 7 (guild Special Access).
            w.Write(guid);
            w.Write(value);
            for (int i = 0; i < 3; i++) w.Write(0u);
            w.Write(0ul);
            for (int i = 0; i < 5; i++) w.Write("");
            w.Write(0ul);
            // The site's game (FUN_142cffe50; kind 0x11 applies it, FUN_1434868f0).
            InstancedGames.WriteInfo(w, claim.Id);
            w.Write(0ul);
            w.Write("");
            w.Write(0u);                                // empty list
            w.Write(claim.TimeHour);
            w.Write(claim.TimeRate);
            w.Write(claim.VisitorTimeHour);
            w.Write(claim.VisitorTimeRate);
            w.Write(MusicOf(claim));                    // +0x280 (0.589 +0x278): kind 0x16 applies it to the site (client +0xf4)
            w.Write(ModeOf(claim.Id));                  // kind 0x13 applies it (2 = Play Mode)
            // 0.695 (FUN_142da2840): u64 Play Mode Rules flags (+0x288; new kind 0x17 applies them to claim entry +0x380).
            w.Write(claim.AdventureFlags & AdventureFlagMask);
            return output.ToArray();
        }

        /// <summary>
        /// Deletes claims, from the database and from every player's view. Owners online get fresh Site Management rows
        /// (<see cref="SendSiteRows"/>) unless the caller sends them itself (<paramref name="sendUpkeep"/> false).
        /// </summary>
        private static void Remove(List<ClaimModel> gone, string reason, bool sendUpkeep = true)
        {
            // The site's Special Access entries go with it (memory and the claim_permission rows, which have no foreign
            // key): ids restart from the highest stored one at the next start (Load), so the newest site's id is handed
            // out again after a restart, and its old entries gave the new owner's site to the old site's guests
            // (bughunt-build B3).
            List<ulong> ids = gone.Select(c => c.Id).ToList();
            lock (sync)
            {
                claims.RemoveAll(c => gone.Any(g => g.Id == c.Id));
                foreach (ulong id in ids)
                    special.Remove(id);
            }
            // Owners online drop it from their own sites (My Sites) too, not just from the area.
            foreach (ClaimModel claim in gone)
                PlayerManager.FindByGuid(claim.OwnerGuid)?.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(claim, InfoDeleted));
            if (sendUpkeep)
                foreach (ulong ownerGuid in gone.Select(c => c.OwnerGuid).Distinct())
                    if (PlayerManager.FindByGuid(ownerGuid) is WorldSession owner)
                        SendSiteRows(owner);
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
                {
                    c.Claim.RemoveRange(gone);
                    c.ClaimPermission.RemoveRange(c.ClaimPermission.Where(p => ids.Contains(p.ClaimId)));
                })
                .ContinueWith(t => log.Error(t.Exception, "Failed to delete claims."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            foreach (var group in gone.GroupBy(c => c.ZoneId))
            {
                var update = BuildAoiUpdate(false, Array.Empty<ClaimModel>(), group.Select(c => c.Id).ToList());
                foreach (WorldSession other in PlayerManager.InZone(group.Key))
                    other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            }
            log.Info($"Removed {gone.Count} claim(s): {reason}.");
        }

        /// <summary>
        /// Where players arrive at a player's build site (travel, respawn): its Starting Location prop when it has one
        /// (story tools: "lets you specify where Luminaries go when they teleport to or revive at your Site"), else the
        /// centre. Also the site's id.
        /// </summary>
        /// <summary>
        /// Which way a visitor of this owner's site faces on arrival: its Starting Location's rotation (P5825: turning
        /// the prop sets the arrival direction; <see cref="Teleport.Rezone"/>), null without one.
        /// </summary>
        public static Quaternion? ArrivalFacingOf(ulong ownerGuid)
        {
            ClaimModel claim;
            lock (sync)
                claim = claims.FirstOrDefault(c => c.OwnerGuid == ownerGuid);
            return claim != null && Props.StartingLocationOn(claim.ZoneId, claim.Id) is Props.PlacedProp sl
                ? Props.RotationOf(sl.Spawn.Guid) : null;
        }

        public static (Vector3 Position, uint Zone, ulong ClaimId)? ArrivalOf(ulong ownerGuid)
        {
            ClaimModel claim;
            lock (sync)
                claim = claims.FirstOrDefault(c => c.OwnerGuid == ownerGuid);
            if (claim == null || CentreOf(ownerGuid) is not (Vector3 centre, uint zone))
                return null;
            // One per site since TASKS 6.37 (a second placement moves the first); sites that got two before take the newest.
            Vector3? start = Props.StartingLocationOn(zone, claim.Id) is Props.PlacedProp sl
                ? new Vector3(sl.Spawn.Position.X, sl.Spawn.Position.Y, sl.Spawn.Position.Z) : null;
            return (start is Vector3 s ? s + new Vector3(0f, 0.5f, 0f) : centre, zone, claim.Id);
        }

        /// <summary>
        /// Every site has one Starting Location: wiki: a new site gets its Starting Location prop in the north-west
        /// corner (free, one per site; where visitors arrive and revive,
        /// <see cref="ArrivalOf"/>). Placed for the owner's sites on the landscape they are on when one is missing: right
        /// after a site is placed, and at world entry (sites granted while the owner was away, older sites).
        /// North-west is taken as the site's -x / +z corner, 10 m in.
        /// </summary>
        public static void EnsureStartingLocation(WorldSession session)
        {
            uint zone = session.ZoneId;
            List<ClaimModel> mine;
            lock (sync)
                mine = claims.Where(c => c.OwnerGuid == session.PlayerGuid && c.ZoneId == zone).ToList();
            if (mine.Count == 0)
                return;
            var starts = Props.PlacedOf(zone, new HashSet<uint> { StoryTools.StartingLocationProp });
            var origin = Terrain.TerrainStore.ZoneOrigin;
            foreach (ClaimModel claim in mine)
            {
                if (starts.Any(p => ClaimAt(zone, p.Position) == claim.Id))
                    continue;
                float x = (float)((claim.MinX - origin.X) / 10) + 10f, z = (float)((claim.MaxZ - origin.Z) / 10) - 10f;
                float minY = (float)((claim.MinY - origin.Y) / 10), maxY = (float)((claim.MaxY - origin.Y) / 10);
                float y = Math.Clamp(Terrain.TerrainHeights.HeightAt(x, z, zone) ?? (minY + maxY) / 2, minY + 1f, maxY - 1f);
                ulong guid = Props.PlaceAt(session, StoryTools.StartingLocationProp, new Vector3(x, y, z));
                log.Info($"Placed site {claim.Id}'s Starting Location (prop 0x{guid:X}).");
            }
        }

        /// <summary>Local-space centre (on the ground) of a player's build site, if they have one.</summary>
        public static (Vector3 Position, uint Zone)? CentreOf(ulong ownerGuid)
        {
            ClaimModel claim;
            lock (sync)
                claim = claims.FirstOrDefault(c => c.OwnerGuid == ownerGuid);
            if (claim == null)
                return null;
            var origin = Terrain.TerrainStore.ZoneOrigin;
            float x = (float)(((claim.MinX + claim.MaxX) / 2 - origin.X) / 10), z = (float)(((claim.MinZ + claim.MaxZ) / 2 - origin.Z) / 10);
            float y = Terrain.TerrainHeights.HeightAt(x, z, claim.ZoneId) ?? (float)(((claim.MinY + claim.MaxY) / 2 - origin.Y) / 10);
            return (new Vector3(x, y + 1f, z), claim.ZoneId);
        }

        /// <summary>
        /// May this player change terrain or props at a global point? Used by mining, building and props.
        /// </summary>
        public static bool MayEdit(ulong playerGuid, double x, double y, double z, uint zone = 0)
            => Allows(playerGuid, x, y, z, PermissionBuild, zone);

        /// <summary>
        /// Does every claim covering a global point grant this player the permission bits? Outside claims: yes.
        /// Owners have everything; others get their Special Access, else the claim's Default Access.
        /// </summary>
        public static bool Allows(ulong playerGuid, double x, double y, double z, uint permission, uint zone = 0)
        {
            zone = zone == 0 ? Terrain.TerrainStore.HomeContinent : zone;
            lock (sync)
                return claims.Where(c => c.ZoneId == zone && x >= c.MinX && x <= c.MaxX && y >= c.MinY && y <= c.MaxY && z >= c.MinZ && z <= c.MaxZ)
                    .All(c => (PermissionsFor(c, playerGuid) & permission) == permission);
        }

        /// <summary>
        /// The build check for one terrain cell (global box), done once per cell instead of per voxel (a 26-cell edit
        /// used to run ~200,000 locked scans of all sites; 2026-10-13: the remove tool stalled between clicks). Used by
        /// the build tools only (ZoneTerrain.MayApply: Building ApplyDiff / Heal / undo, BuildingClipboard paste) —
        /// mining and harvesting check <see cref="MayEdit"/>, which still allows the open world.
        /// <para>
        /// Building happens only on a site where the player may build (TASKS 6.46): Build Mode only works on a site
        /// where the player has build access (21 Mar 2016, P4141); pasting outside your own site was not intended (25
        /// May 2016, P5651); wiki Building Tools: only on your own sites or where the owner gave build access. A point
        /// is allowed when it lies inside a site granting <see cref="PermissionBuild"/>
        /// and inside no site refusing it. Returns null when the whole box is allowed (one granting site covers it, none
        /// refuses), else the point test. Refused edits get Response result 0xE ("Construction.InsufficentPermissions",
        /// "You do not have permission to build that here.").
        /// </para>
        /// </summary>
        public static Func<double, double, double, bool> BuildCheckIn(ulong playerGuid, uint zone,
            (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            zone = zone == 0 ? Terrain.TerrainStore.HomeContinent : zone;
            List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)> refused, granted;
            lock (sync)
            {
                var touching = claims.Where(c => c.ZoneId == zone && c.MinX <= max.X && c.MaxX >= min.X && c.MinY <= max.Y
                        && c.MaxY >= min.Y && c.MinZ <= max.Z && c.MaxZ >= min.Z).ToList();
                refused = touching.Where(c => (PermissionsFor(c, playerGuid) & PermissionBuild) != PermissionBuild)
                    .Select(c => ((double)c.MinX, (double)c.MinY, (double)c.MinZ, (double)c.MaxX, (double)c.MaxY, (double)c.MaxZ)).ToList();
                granted = touching.Where(c => (PermissionsFor(c, playerGuid) & PermissionBuild) == PermissionBuild)
                    .Select(c => ((double)c.MinX, (double)c.MinY, (double)c.MinZ, (double)c.MaxX, (double)c.MaxY, (double)c.MaxZ)).ToList();
            }
            if (refused.Count == 0 && granted.Exists(b => min.X >= b.MinX && max.X <= b.MaxX && min.Y >= b.MinY && max.Y <= b.MaxY
                    && min.Z >= b.MinZ && max.Z <= b.MaxZ))
                return null;
            if (granted.Count == 0)
                return (_, _, _) => false;
            return (x, y, z) => granted.Exists(b => x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY && z >= b.MinZ && z <= b.MaxZ)
                && !refused.Exists(b => x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY && z >= b.MinZ && z <= b.MaxZ);
        }

        /// <summary>
        /// May the player copy (or save as a design) what lies in a global box? Every site overlapping it must grant them
        /// Get Inspired (<see cref="PermissionCopy"/> 0x100, Site Management's "Get Inspired" permission, text 576061);
        /// open land needs none, and the owner has it. Features audit #93 (2026-10-17): the permission was kept but
        /// never checked, so anyone could copy any site.
        /// </summary>
        public static bool MayCopyIn(ulong playerGuid, uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            zone = zone == 0 ? Terrain.TerrainStore.HomeContinent : zone;
            lock (sync)
                return claims.Where(c => c.ZoneId == zone && c.MinX <= max.X && c.MaxX >= min.X && c.MinY <= max.Y
                        && c.MaxY >= min.Y && c.MinZ <= max.Z && c.MaxZ >= min.Z)
                    .All(c => (PermissionsFor(c, playerGuid) & PermissionCopy) == PermissionCopy);
        }

        /// <summary>
        /// Does the player own every site that overlaps a global box (true when none does)? Player Studio designs go only
        /// on the player's own site (TASKS 6.45, BuildingClipboard paste).
        /// </summary>
        public static bool OwnsEverySiteIn(ulong playerGuid, uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            zone = zone == 0 ? Terrain.TerrainStore.HomeContinent : zone;
            lock (sync)
                return claims.Where(c => c.ZoneId == zone && c.MinX <= max.X && c.MaxX >= min.X && c.MinY <= max.Y
                        && c.MaxY >= min.Y && c.MinZ <= max.Z && c.MaxZ >= min.Z)
                    .All(c => c.OwnerGuid == playerGuid);
        }

        /// <summary>
        /// A player's permission mask in one site: everything for the owner, their Special Access entry if any (a 0 entry
        /// is "No Touching" and wins over Default Access), else the site's public Default Access.
        /// <para>
        /// This is the client's own rule [P]: 0.695 FUN_143c17590 (0.589 FUN_143b5b480) takes (player, guild guid, bool
        /// guild, bool group) and returns 0x11F for the owner (entry +0xd8), else the player's Special Access entry
        /// (FUN_143c17680), else the guild's (FUN_143c17520), else +0x2dc if the group flag is set, +0x2e0 if the guild
        /// flag is set, else +0x2d8 (public). Every caller passes both flags false and a constant invalid guild guid —
        /// the build / prop / chest pre-check FUN_142d98cc0 (callers FUN_142dc7540, FUN_14346d4a0, FUN_142dd15d0 with
        /// 0x10; FUN_142fa55e0 props), the access shown on entering a site (FUN_142db5ca0, FUN_142dbecb0), free camera
        /// (FUN_1435e4530, FUN_143607950) and the ability requirement ClientRequirementCasterOnClaim (FUN_14370f6e0, RTTI
        /// vtable slot). So in the client the group and guild masks never apply. The server does the same: granting group / guild mates more here would still be
        /// refused by their own client's pre-check, and granting less would let their client try what the server
        /// refuses. Guild Special Access entries (the record's second list) are not kept either: the shipped window
        /// only names players (Lua arg 4 of SetAdvancedSecuritySettings is always false).
        /// </para>
        /// </summary>
        private static uint PermissionsFor(ClaimModel claim, ulong playerGuid)
        {
            if (claim.OwnerGuid == playerGuid)
                return OwnerPermissions;
            if (special.TryGetValue(claim.Id, out var map) && map.TryGetValue(playerGuid, out uint mask))
                return mask;
            return claim.DefaultPermissions;
        }

        /// <summary>The claim a Site Management request is about: the id it names, or (0) the player's own site.</summary>
        private static ClaimModel OwnedClaim(WorldSession session, ulong claimId)
        {
            lock (sync)
                return claims.FirstOrDefault(c => c.OwnerGuid == session.PlayerGuid && (claimId == 0 || c.Id == claimId));
        }

        /// <summary>
        /// SetItemBasicPermissions (0xF9/0x2D {u64 claim, u32 public, u32 group, u32 guild, u8 1}; 0.695 Lua
        /// Claims.SetBasicSecuritySettings FUN_1435e4180 → writer FUN_1435e6760) — "Default Access". All three masks
        /// are kept as sent and go back in the claim record and in ClaimInfoUpdate kind 5, so every client's claim entry
        /// holds exactly what the owner set (trace/0695/audit-wire-b.md #4: only the first used to be kept and was
        /// echoed into all three slots).
        /// <para>
        /// What the three are [P]: Site Management's data source names them PermissionsPublic / PermissionsGroup /
        /// PermissionsGuild (FUN_142db8ab0, from record +0xb8 / +0xbc / +0xc0). The shipped window (SecurityPanel.as, the
        /// same in 0.589 and 0.695) fills and wires only PublicSecurityDropdown and calls
        /// ClaimsWrapper.SetBasicSecuritySettings(guid, level) with Lua args 3 and 4 nil, which FUN_142dbd940 turns into
        /// 0: group and guild always arrive as 0. Its GroupSecurityDropdown / GuildSecurityDropdown are declared but
        /// never filled or listened to. Which access they would give is decided in <see cref="PermissionsFor"/>.
        /// </para>
        /// </summary>
        private static void SetDefaultPermissions(WorldSession session, ulong claimId, uint everyone, uint group, uint guild)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null)
                return;
            claim.DefaultPermissions = everyone & OwnerPermissions;
            claim.GroupPermissions = group & OwnerPermissions;
            claim.GuildPermissions = guild & OwnerPermissions;
            ulong id = claim.Id;
            uint e = claim.DefaultPermissions, g = claim.GroupPermissions, u = claim.GuildPermissions;
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                model.DefaultPermissions = e;
                model.GroupPermissions = g;
                model.GuildPermissions = u;
                c.MarkModified(model, nameof(model.DefaultPermissions));
                c.MarkModified(model, nameof(model.GroupPermissions));
                c.MarkModified(model, nameof(model.GuildPermissions));
            });
            log.Info($"{session.CharacterName} set default access of claim {id} to 0x{e:X} (group 0x{g:X}, guild 0x{u:X}).");
            // ClaimInfoUpdate kind 5 instead of the whole record: the client sets the three masks on its claim entries
            // and recomputes the access of the site it stands in (FUN_142dbecb0 case 4, kinds 5-7).
            SendInfoToZone(claim, BuildInfoUpdate(claim, InfoPermissions));
        }

        /// <summary>
        /// A ClaimInfoUpdate to everyone on the site's landscape, except players in the middle of a re-zone (an update
        /// right behind ClientBeginZoning hung the client, <see cref="SendGameInfo"/>; they get the whole record with
        /// the area's sites when they arrive).
        /// </summary>
        private static void SendInfoToZone(ClaimModel claim, byte[] update)
        {
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                if (other.RezoneTarget == null)
                    other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
        }

        /// <summary>ResultResponse result: no player or guild by that name.</summary>
        private const uint NameNotFound = 0x17;

        /// <summary>
        /// Dev / tests: Special Access on the player's own (first) site, as Site Management sets it (0 = No Touching,
        /// 0x80000000 removes the entry).
        /// </summary>
        public static bool DevGrant(WorldSession session, string name, uint mask)
        {
            ulong? id;
            lock (sync)
                id = claims.FirstOrDefault(c => c.OwnerGuid == session.PlayerGuid)?.Id;
            if (id == null)
                return false;
            SetSpecialPermissions(session, id.Value, name, mask);
            return true;
        }

        /// <summary>
        /// The mask Lua Claims.DeleteAdvancedSecuritySetting sends (0.695 FUN_1435e4400 sets 0x80000000) and the client's
        /// own entry update treats as "remove" (FUN_143c17b00 / FUN_143c17a50) [P].
        /// </summary>
        private const uint RemoveAccess = 0x80000000;

        /// <summary>
        /// SetItemIndividualPermission (0xF9/0x2E {u64 claim, u8, string player name, u8, u32 mask, u8}) — "Special
        /// Access". <see cref="RemoveAccess"/> removes the player's entry; any other mask, 0 included, is stored: the
        /// client's "No Touching" level maps to no bits (ClaimPermissionDefinitions 1 has no AccessLevelMappings rows, so
        /// FUN_143c0d900 sums to 0) and an entry of 0 still wins over Default Access in the client's rule
        /// (FUN_143c17590 returns the entry's value whenever FUN_143c17680 finds it). Until 2026-10-17 a 0 mask removed
        /// the entry, so a player set to No Touching kept the site's public access on the server.
        /// </summary>
        private static void SetSpecialPermissions(WorldSession session, ulong claimId, string name, uint mask)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null || string.IsNullOrWhiteSpace(name))
                return;
            CharacterModel target = DatabaseManager.Instance.CharacterDatabase.GetCharacterByName(name.Trim()).GetAwaiter().GetResult();
            if (target == null)
            {
                // ClaimsBase 0x10 ResultResponse {u32 result, string name, u64} (reader FUN_142d012c0, handled in
                // FUN_142d1ff50): result 0x17 prints the client's own "Claim Operation Result: ERROR: Player or Guild
                // could not be found under the name [%s]!".
                var output = new List<byte>();
                var writer = new GamePacketWriter(output);
                writer.Write((byte)0x10);
                writer.Write(NameNotFound);
                writer.Write(name.Trim());
                writer.Write(claimId);
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
                return;
            }
            ulong id = claim.Id, characterId = target.Id;
            bool remove = mask == RemoveAccess;
            mask &= OwnerPermissions;
            lock (sync)
            {
                if (!special.TryGetValue(id, out var map))
                    special[id] = map = new Dictionary<ulong, uint>();
                if (remove)
                    map.Remove(characterId);
                else
                    map[characterId] = mask;
                specialNames[characterId] = target.Name;
            }
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                ClaimPermissionModel row = c.ClaimPermission.Find(id, characterId);
                if (remove)
                {
                    if (row != null)
                        c.ClaimPermission.Remove(row);
                }
                else if (row == null)
                    c.ClaimPermission.Add(new ClaimPermissionModel { ClaimId = id, CharacterId = characterId, Mask = mask });
                else
                    row.Mask = mask;
            });
            log.Info(remove ? $"{session.CharacterName} removed {target.Name}'s access on claim {id}."
                : $"{session.CharacterName} gave {target.Name} access 0x{mask:X} on claim {id}.");
            // ClaimInfoUpdate kind 6 {guid, value} instead of the whole record: every client on the landscape updates the
            // player's entry (or removes it with 0x80000000) and recomputes the access of the site it stands in.
            SendInfoToZone(claim, BuildInfoUpdate(claim, InfoPlayerAccess, characterId, remove ? RemoveAccess : mask));
            // The owner's open Site Management dialog lists Special Access from the edit data (the named list, which
            // kind 6 does not touch): refresh it.
            SendEditData(session, id);
        }

        /// <summary>
        /// A character was deleted (CharacterDeletion's sweep). Deleting a character saves each of its build sites on
        /// that world as a design (26 Apr 2016, P4958): each of its sites is saved as a design,
        /// packed and removed as an expired site is (<see cref="SiteContents.Pack"/>: design, terrain back, props and
        /// resources to the owner's mail — which nobody reads any more, kept for the record), and "Names of deleted
        /// characters no longer remain in the Special Access list" (11 May 2016, P5412): its access rows go from every site,
        /// and the owners' open Site Management data is refreshed.
        /// </summary>
        public static void CharacterDeleted(ulong characterId, string name)
        {
            List<ClaimModel> owned;
            List<ulong> accessOn;
            lock (sync)
            {
                owned = claims.Where(c => c.OwnerGuid == characterId).ToList();
                accessOn = special.Where(s => s.Value.Remove(characterId)).Select(s => s.Key).ToList();
                specialNames.Remove(characterId);
            }
            if (owned.Count > 0)
            {
                Remove(owned, $"owner {name} deleted");
                foreach (ClaimModel claim in owned)
                    SiteContents.Pack(claim);
            }
            if (accessOn.Count > 0)
            {
                DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
                {
                    foreach (ulong claimId in accessOn)
                        if (c.ClaimPermission.Find(claimId, characterId) is ClaimPermissionModel row)
                            c.ClaimPermission.Remove(row);
                });
                foreach (ulong claimId in accessOn)
                    if (Get(claimId) is ClaimModel site)
                    {
                        Resend(site);
                        if (PlayerManager.FindByGuid(site.OwnerGuid) is WorldSession owner)
                            SendEditData(owner, site.Id);
                    }
            }
            if (owned.Count > 0 || accessOn.Count > 0)
                log.Info($"Deleted character {name}: {owned.Count} site(s) saved as designs and removed, Special Access left on {accessOn.Count} site(s).");
        }

        /// <summary>Build sites on a landscape (the Landscapes window's claim count).</summary>
        public static int CountOn(uint zone)
        {
            lock (sync)
                return claims.Count(c => c.ZoneId == zone);
        }

        /// <summary>The build sites on a landscape (a copy; event island expiry and its dry run).</summary>
        public static List<ClaimModel> On(uint zone)
        {
            lock (sync)
                return claims.Where(c => c.ZoneId == zone).OrderBy(c => c.Id).ToList();
        }

        /// <summary>
        /// Expires sites the way <see cref="Update"/> expires lapsed ones: removed (database, every player's view, the
        /// owners' Site Management rows), then each packed (<see cref="SiteContents.Pack"/>: design saved, terrain reverted,
        /// props and resources to the owner's mail) with <paramref name="letter"/> as the letter carrying the Build Site
        /// Contents item (null = the upkeep one). The caller checks that the Environment Server is reachable first.
        /// </summary>
        public static void Expire(List<ClaimModel> sites, string reason, Action<ulong, string> letter = null)
        {
            if (sites.Count == 0)
                return;
            Remove(sites, reason);
            foreach (ClaimModel claim in sites)
                SiteContents.Pack(claim, letter);
        }

        /// <summary>The owner of a build site (0 when there is no such site).</summary>
        public static ulong OwnerOf(ulong claimId)
        {
            lock (sync)
                return claims.FirstOrDefault(c => c.Id == claimId)?.OwnerGuid ?? 0;
        }

        /// <summary>The build site containing a local-space point on a landscape, or null.</summary>
        public static ulong? ClaimAt(uint zone, Vector3 local)
        {
            var p = Terrain.TerrainEdits.ToGlobal(local);
            lock (sync)
                return claims.FirstOrDefault(c => c.ZoneId == zone && p.X >= c.MinX && p.X <= c.MaxX && p.Y >= c.MinY
                    && p.Y <= c.MaxY && p.Z >= c.MinZ && p.Z <= c.MaxZ)?.Id;
        }

        /// <summary>The height range (client local metres) of the build site containing a local-space point, or null.</summary>
        public static (float MinY, float MaxY)? SiteHeightAt(uint zone, Vector3 local)
        {
            var p = Terrain.TerrainEdits.ToGlobal(local);
            ClaimModel c;
            lock (sync)
                c = claims.FirstOrDefault(c => c.ZoneId == zone && p.X >= c.MinX && p.X <= c.MaxX && p.Y >= c.MinY
                    && p.Y <= c.MaxY && p.Z >= c.MinZ && p.Z <= c.MaxZ);
            if (c == null)
                return null;
            double originY = Terrain.TerrainStore.ZoneOrigin.Y;
            return ((float)((c.MinY - originY) / 10), (float)((c.MaxY - originY) / 10));
        }

        /// <summary>
        /// The box of the build site containing a local-space point, in client local metres (centre and the half length of
        /// its 3-D diagonal), or null. For the Escape menu's "Evac to Safety" on a site (TeleportRequests): the client's
        /// target is the site box's centre plus that half diagonal + 10 m along the player's heading (FUN_1435fdb70).
        /// </summary>
        public static (Vector3 Centre, float HalfDiagonal)? SiteBoxAt(uint zone, Vector3 local)
        {
            var p = Terrain.TerrainEdits.ToGlobal(local);
            ClaimModel c;
            lock (sync)
                c = claims.FirstOrDefault(c => c.ZoneId == zone && p.X >= c.MinX && p.X <= c.MaxX && p.Y >= c.MinY
                    && p.Y <= c.MaxY && p.Z >= c.MinZ && p.Z <= c.MaxZ);
            if (c == null)
                return null;
            var origin = Terrain.TerrainStore.ZoneOrigin;
            var centre = new Vector3((float)(((c.MinX + c.MaxX) / 2 - origin.X) / 10), (float)(((c.MinY + c.MaxY) / 2 - origin.Y) / 10),
                (float)(((c.MinZ + c.MaxZ) / 2 - origin.Z) / 10));
            double dx = c.MaxX - c.MinX, dy = c.MaxY - c.MinY, dz = c.MaxZ - c.MinZ;
            return (centre, (float)(Math.Sqrt(dx * dx + dy * dy + dz * dz) / 2 / 10));
        }

        /// <summary>Build-site boxes (global dm) on a landscape that touch a box.</summary>
        public static List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)> BoxesIn(uint zone,
            (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            lock (sync)
                return claims.Where(c => c.ZoneId == zone && c.MinX <= max.X && c.MaxX >= min.X && c.MinY <= max.Y
                        && c.MaxY >= min.Y && c.MinZ <= max.Z && c.MaxZ >= min.Z)
                    .Select(c => ((double)c.MinX, (double)c.MinY, (double)c.MinZ, (double)c.MaxX, (double)c.MaxY, (double)c.MaxZ)).ToList();
        }

        /// <summary>
        /// Every build site's box (global dm) with its landscape (the claim's ZoneId, as <see cref="BoxesIn"/> keys it): what
        /// a remote Environment Server is sent instead of asking <see cref="BoxesIn"/> (terrain-service.md §5.2 / §5.5,
        /// Terrain.RemoteEnvironmentServer).
        /// </summary>
        public static List<(uint Zone, double[] Box)> AllBoxes()
        {
            lock (sync)
                return claims.Select(c => (c.ZoneId, new double[] { c.MinX, c.MinY, c.MinZ, c.MaxX, c.MaxY, c.MaxZ })).ToList();
        }

        /// <summary>
        /// A build site as voxel destruction sees it (<see cref="VoxelDestruction"/>): its box (global dm), whether it is in
        /// Play Mode and its Play Mode Rules mask (<see cref="ClaimModel.AdventureFlags"/>).
        /// </summary>
        public readonly record struct DestructionSite(ulong Id, double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ,
            bool Playing, ulong AdventureFlags)
        {
            public bool Contains(double x, double y, double z)
                => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY && z >= MinZ && z <= MaxZ;

            /// <summary>Play Mode Rules bit 4 "Block Voxel Destruction" (default off = destruction allowed, P5969).</summary>
            public bool RuleBlocks => (AdventureFlags & AdventureBlockVoxelDestruction) != 0;
        }

        /// <summary>The build sites on a landscape touching a box (global dm), with their mode and Play Mode Rules.</summary>
        public static List<DestructionSite> DestructionSitesIn(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            lock (sync)
                return claims.Where(c => c.ZoneId == zone && c.MinX <= max.X && c.MaxX >= min.X && c.MinY <= max.Y
                        && c.MaxY >= min.Y && c.MinZ <= max.Z && c.MaxZ >= min.Z)
                    .Select(c => new DestructionSite(c.Id, c.MinX, c.MinY, c.MinZ, c.MaxX, c.MaxY, c.MaxZ, playing.Contains(c.Id), c.AdventureFlags))
                    .ToList();
        }

        /// <summary>Is a global point inside any build site on a landscape?</summary>
        public static bool IsInsideAny(uint zone, double x, double y, double z)
        {
            lock (sync)
                return claims.Any(c => c.ZoneId == zone && x >= c.MinX && x <= c.MaxX && y >= c.MinY && y <= c.MaxY
                    && z >= c.MinZ && z <= c.MaxZ);
        }

        /// <summary>Is the player standing inside one of their own build sites?</summary>
        public static bool IsOnOwnSite(WorldSession session)
        {
            var p = Terrain.TerrainEdits.ToGlobal(session.Position);
            uint zone = session.ZoneId;
            lock (sync)
                return claims.Any(c => c.OwnerGuid == session.PlayerGuid && c.ZoneId == zone
                    && p.X >= c.MinX && p.X <= c.MaxX && p.Y >= c.MinY && p.Y <= c.MaxY && p.Z >= c.MinZ && p.Z <= c.MaxZ);
        }

        /// <summary>Does a character own a build site anywhere?</summary>
        public static bool Owns(ulong ownerGuid)
        {
            lock (sync)
                return claims.Any(c => c.OwnerGuid == ownerGuid);
        }

        /// <summary>
        /// Would a site centred on a global point keep out of every other site's buffer? As the client checks it
        /// (FUN_142d28700 on the root-site index): the new box may not come within <see cref="BufferFromCentre"/> of another
        /// site's centre on any axis (x, y and z).
        /// </summary>
        public static bool IsFree(uint zone, double x, double y, double z)
        {
            lock (sync)
                return !claims.Any(c => c.ZoneId == zone
                    && Math.Abs(x - (c.MinX + c.MaxX) / 2) < HalfSize + BufferFromCentre
                    && Math.Abs(y - (c.MinY + c.MaxY) / 2) < HalfHeight + BufferFromCentre
                    && Math.Abs(z - (c.MinZ + c.MaxZ) / 2) < HalfSize + BufferFromCentre);
        }

        /// <summary>The same, ignoring height (for picking a spot before the ground height is known).</summary>
        public static bool IsFree(uint zone, double x, double z)
        {
            lock (sync)
                return !claims.Any(c => c.ZoneId == zone
                    && Math.Abs(x - (c.MinX + c.MaxX) / 2) < HalfSize + BufferFromCentre
                    && Math.Abs(z - (c.MinZ + c.MaxZ) / 2) < HalfSize + BufferFromCentre);
        }

        /// <summary>
        /// Gives a character a build site without a flag (the Pioneer start: new players begin on a site of their own).
        /// Centre in global units. Players on that landscape are sent it.
        /// </summary>
        public static ClaimModel Grant(ulong ownerGuid, string ownerName, uint zone, double x, double y, double z)
        {
            var claim = new ClaimModel
            {
                Id = (ulong)Interlocked.Increment(ref nextId),
                OwnerGuid = ownerGuid,
                Name = $"{ownerName}'s Claim",
                MinX = x - HalfSize,
                MaxX = x + HalfSize,
                MinY = y - HalfHeight,
                MaxY = y + HalfHeight,
                MinZ = z - HalfSize,
                MaxZ = z + HalfSize,
                ZoneId = zone,
                DefaultPermissions = DefaultAccess
            };
            lock (sync)
                claims.Add(claim);
            Terrain.ZoneTerrain.ClearVeinsInSite(claim.ZoneId, (claim.MinX, claim.MinY, claim.MinZ), (claim.MaxX, claim.MaxY, claim.MaxZ));
            Gathering.SitePlaced(claim.ZoneId, (claim.MinX, claim.MinY, claim.MinZ), (claim.MaxX, claim.MaxY, claim.MaxZ));   // the trees in it go
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c => c.Claim.Add(claim))
                .ContinueWith(t => log.Error(t.Exception, $"Failed to save claim {claim.Id}."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            var add = BuildAoiUpdate(false, new[] { claim }, Array.Empty<ulong>());
            foreach (WorldSession other in PlayerManager.InZone(zone))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, add);
            if (PlayerManager.FindByGuid(ownerGuid) is WorldSession owner)
                SendSiteRows(owner);
            log.Info($"Granted {ownerName} site {claim.Id} on landscape {zone}.");
            return claim;
        }

        /// <summary>
        /// Moves a site to another landscape / spot (Pioneer.MoveOldSites: the old Pioneer lands 417-421), centred on a
        /// global point at the launch size, keeping its id, owner, name, tags, access and settings (everything keyed by the
        /// claim id). Only the site record moves: the voxels and props on the old spot stay there. The new box is kept clear
        /// of ore veins as a granted site's is; players on either landscape get the AOI change, an online owner their rows.
        /// False when there is no such site.
        /// </summary>
        public static bool Relocate(ulong claimId, uint zone, double x, double y, double z)
        {
            ClaimModel claim;
            uint from;
            lock (sync)
            {
                claim = claims.FirstOrDefault(c => c.Id == claimId);
                if (claim == null)
                    return false;
                from = claim.ZoneId;
                claim.ZoneId = zone;
                (claim.MinX, claim.MaxX, claim.MinY, claim.MaxY, claim.MinZ, claim.MaxZ) =
                    (x - HalfSize, x + HalfSize, y - HalfHeight, y + HalfHeight, z - HalfSize, z + HalfSize);
            }
            Terrain.ZoneTerrain.ClearVeinsInSite(zone, (claim.MinX, claim.MinY, claim.MinZ), (claim.MaxX, claim.MaxY, claim.MaxZ));
            Gathering.SitePlaced(zone, (claim.MinX, claim.MinY, claim.MinZ), (claim.MaxX, claim.MaxY, claim.MaxZ));   // the trees at the new spot go
            (double a, double b, double d, double e, double f, double g) = (claim.MinX, claim.MaxX, claim.MinY, claim.MaxY, claim.MinZ, claim.MaxZ);
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(db =>
            {
                var model = new ClaimModel { Id = claimId };
                db.Claim.Attach(model);
                model.ZoneId = zone;
                (model.MinX, model.MaxX, model.MinY, model.MaxY, model.MinZ, model.MaxZ) = (a, b, d, e, f, g);
                db.MarkModified(model, nameof(model.ZoneId), nameof(model.MinX), nameof(model.MaxX), nameof(model.MinY),
                    nameof(model.MaxY), nameof(model.MinZ), nameof(model.MaxZ));
            }).ContinueWith(t => log.Error(t.Exception, $"Failed to save the move of claim {claimId}."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            var removed = BuildAoiUpdate(false, Array.Empty<ClaimModel>(), new[] { claimId });
            foreach (WorldSession other in PlayerManager.InZone(from))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, removed);
            var added = BuildAoiUpdate(false, new[] { claim }, Array.Empty<ulong>());
            foreach (WorldSession other in PlayerManager.InZone(zone))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, added);
            if (PlayerManager.FindByGuid(claim.OwnerGuid) is WorldSession owner)
                SendSiteRows(owner);
            return true;
        }

        /// <summary>
        /// A site the server itself places (the Landmarks of Landmark, <see cref="LandmarksOfLandmark"/>): kept in memory
        /// only (registered again at every start, never saved), owned by a guid no character has, so its Default Access
        /// applies to every player (no building, mining or props: only staff could edit them, Domino, forum 57799 #6)
        /// and it never expires. Players on its landscape are sent it.
        /// </summary>
        public static void AddServerSite(ClaimModel claim)
        {
            lock (sync)
            {
                if (claims.Any(c => c.Id == claim.Id))
                    return;
                claims.Add(claim);
            }
            Gathering.SitePlaced(claim.ZoneId, (claim.MinX, claim.MinY, claim.MinZ), (claim.MaxX, claim.MaxY, claim.MaxZ));
            var add = BuildAoiUpdate(false, new[] { claim }, Array.Empty<ulong>());
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, add);
        }

        /// <summary>
        /// Site records that exist on the client only (<see cref="OutpostSites"/>: the outposts' walk-on banners): added to
        /// and removed from one client's list (AOIUpdate, not a full update), never kept in the server's own list, so they
        /// play no part in the server's permissions, searches or placement rules.
        /// </summary>
        public static void SendDetachedSites(WorldSession session, IReadOnlyCollection<ClaimModel> added, IReadOnlyCollection<ulong> removed)
            => session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildAoiUpdate(false, added, removed));

        /// <summary>Dev/testing: claims a site at the player's position through the normal request/confirm path.</summary>
        public static void DevPlaceHere(WorldSession session)
        {
            var origin = Terrain.TerrainStore.ZoneOrigin;
            Vector3 p = session.Position;
            PlaceRequest(session, origin.X + p.X * 10, origin.Y + p.Y * 10, origin.Z + p.Z * 10);
            PlaceConfirm(session, true);
        }

        /// <summary>Dev/testing: renames the player's first site through <see cref="SetName"/>.</summary>
        public static void DevRename(WorldSession session, string name)
        {
            ClaimModel claim;
            lock (sync)
                claim = claims.FirstOrDefault(c => c.OwnerGuid == session.PlayerGuid);
            if (claim != null)
                SetName(session, claim.Id, name);
        }

        public const uint EditMode = 1, PlayMode = 2;
        /// <summary>
        /// Sites in Play mode (F5); everything else is in Edit mode. The owner's choice is saved on the site
        /// (ClaimModel.PlayMode) and restored on the first tick after a restart (<see cref="RestoreModes"/>); Adventure
        /// rounds switch it without saving.
        /// </summary>
        private static readonly HashSet<ulong> playing = new();
        private static bool modesRestored;

        /// <summary>
        /// Starts the story of every site its owner left in Play Mode (launch notes P4248 / P4351: after a server reset
        /// pathing creatures stopped until Play Mode was toggled; fixed 1 Apr 2016). Runs once, on the first tick, when
        /// props, creatures and story tools are loaded.
        /// </summary>
        private static void RestoreModes()
        {
            modesRestored = true;
            List<ClaimModel> saved;
            lock (sync)
                saved = claims.Where(c => c.PlayMode).ToList();
            foreach (ClaimModel claim in saved)
                ApplyMode(claim, true);
            if (saved.Count > 0)
                log.Info($"Restored Play Mode on {saved.Count} site(s).");
        }

        /// <summary>
        /// The owner, or a builder with full access, switched the site's mode (F5 / the Play Mode button). Everyone on the
        /// landscape learns it from ClaimInfoUpdate kind 0x13 (FUN_142d1ff50 → claim entry vtable +0x28, FUN_142d198a0),
        /// the mode being the record's last u32; the requester's client waits for it and shows "Claim.EnableNpcs.Failure"
        /// when the answer isn't Play while it asked for Play. Story tools (Links, creature spawners) start or stop.
        /// <para>
        /// Who may (TASKS 6.44): builders with full (Ultimate Power) access to someone else's site may switch Play /
        /// Edit mode (hotfix 11 May 2016, P5343) — the owner, or a player whose access there is Ultimate
        /// Power (<see cref="OwnerPermissions"/> 0x11F, every bit). The client asks for the site it stands in: Lua
        /// Claims.ToggleAdventureStatus (0.695 FUN_1435e62a0) takes the current claim (manager +0x348) and its mode
        /// (+0x378) and sends ClaimsPacketSetAdventureStatus 0xF9 / 0x43 {u64 claim, u32 status, u8 1} (FUN_142dd09b0),
        /// with no ownership test of its own. Claim 0 still means the requester's own site (<see cref="OwnedClaim"/>).
        /// </para>
        /// </summary>
        public static void SetAdventureStatus(WorldSession session, ulong claimId, uint status)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null && claimId != 0 && Get(claimId) is ClaimModel shared
                && (PermissionsFor(shared, session.PlayerGuid) & OwnerPermissions) == OwnerPermissions)
                claim = shared;
            if (claim == null)
            {
                if (Get(claimId) is ClaimModel other)
                    session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(other, InfoMode));
                return;
            }
            bool wasPlaying = IsPlaying(claim.Id);
            ApplyMode(claim, status == PlayMode);
            bool play = status == PlayMode;
            // Edit → Play saves the site's story state (P5276): "The story on this Build Site has been saved!" (L22).
            if (play && !wasPlaying)
                StoryTools.AnnounceSaved(claim);
            if (claim.PlayMode != play)
            {
                claim.PlayMode = play;
                ulong id = claim.Id;
                DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
                {
                    var model = new ClaimModel { Id = id };
                    c.Claim.Attach(model);
                    model.PlayMode = play;
                    c.MarkModified(model, nameof(model.PlayMode));
                });
            }
            log.Info($"{session.CharacterName} switched site {claim.Id} to {(status == PlayMode ? "Play" : "Edit")} mode.");
            Achievements.SiteModeChanged(session, status == PlayMode);
        }

        /// <summary>
        /// Play Mode Rules bits (data source Claim.AdventureFlags, FUN_142dd2480: Value = bit): 1 Block Grappling Hook,
        /// 2 Block Boot Abilities, 4 Block Voxel Destruction, 8 Allow Free Camera, 0x10 Allow Flying Character Camera,
        /// 0x20 Allow Orbit Camera. Anything above 0x3F is dropped.
        /// </summary>
        public const ulong AdventureBlockGrapple = 0x1, AdventureBlockBootAbilities = 0x2, AdventureBlockVoxelDestruction = 0x4,
            AdventureAllowFreeCamera = 0x8, AdventureAllowFlyingCamera = 0x10, AdventureAllowOrbitCamera = 0x20,
            AdventureFlagMask = 0x3F;

        /// <summary>
        /// ClaimsPacketSetAdventureFlags (0.695 0xF9 / 0x45 {u64 claim, u64 mask, u8 1}; client FUN_142dd08e0 → writer
        /// FUN_142da6bd0): the owner changed the site's Play Mode Rules. Claim 0 (or -1: the client's "current claim"
        /// sentinel DAT_145476ff0 is normally substituted by the client itself, L55) means the site the player stands on.
        /// Only the owner may change them (as <see cref="SetAdventureStatus"/>; <b>inferred</b>: live may also have let
        /// builders with a manage permission do it — L-note in trace/0695/plan-storytools-next.md Spec 10). The mask is
        /// kept to bits 0x3F, saved on the claim (<see cref="ClaimModel.AdventureFlags"/>) and sent to every 0.695 client
        /// on the landscape as ClaimInfoUpdate kind 0x17 (FUN_142da2840 trailing u64 +0x288 → claim entry +0x380); the
        /// claim record carries it as well (FUN_142da1a50 +0x3e0), so it survives a relog.
        /// <para>
        /// Enforcement: bits 1, 2, 8, 0x10, 0x20 are applied by the client itself once its claim entry has the mask
        /// (grapple FUN_142e93540 "Grapple.AdventureBlocked"; boot abilities FUN_14370f590; cameras FUN_142dd5540,
        /// FUN_143607950, FUN_1435e4530) while the site is in Play Mode (entry +0x378 == 2).
        /// </para>
        /// <para>
        /// Bit 4 (Block Voxel Destruction) is a server rule (the 0.695 client has no check of its own): the server refuses
        /// voxel destruction (dynamite) on a Play Mode site with the bit set and answers with Abilities EffectEnd result 9
        /// (FUN_142e18c70 → "ClaimDestruction.Fail.NotAllowed"); Linking Areas can override it locally (row 102). Built
        /// 2026-10-16 in <see cref="VoxelDestruction"/> (TASKS 2.1 / 2.6 / C.6).
        /// </para>
        /// </summary>
        public static void SetAdventureFlags(WorldSession session, ulong claimId, ulong mask)
        {
            if (claimId == 0 || claimId == ulong.MaxValue)
                claimId = ClaimAt(session.ZoneId, session.Position) ?? 0;
            ClaimModel claim;
            lock (sync)
                claim = claimId == 0 ? null : claims.FirstOrDefault(c => c.Id == claimId);
            if (claim == null || claim.OwnerGuid != session.PlayerGuid)
            {
                // The client does not change its own entry before kind 0x17 arrives, so a refusal needs no correction.
                log.Info($"{session.CharacterName} may not set the Play Mode Rules of site {claimId} (not the owner).");
                return;
            }
            ulong flags = mask & AdventureFlagMask;
            if (claim.AdventureFlags != flags)
            {
                claim.AdventureFlags = flags;
                ulong id = claim.Id;
                DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
                {
                    var model = new ClaimModel { Id = id };
                    c.Claim.Attach(model);
                    model.AdventureFlags = flags;
                    c.MarkModified(model, nameof(model.AdventureFlags));
                });
            }
            // Kind 0x17 (0.695 dispatcher only); players mid re-zone get it in the claim record instead.
            var update = BuildInfoUpdate(claim, InfoAdventureFlags);
            foreach (WorldSession viewer in PlayerManager.InZone(claim.ZoneId))
                if (viewer.RezoneTarget == null)
                    viewer.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            log.Info($"{session.CharacterName} set site {claim.Id} adventure flags 0x{flags:X}.");
        }

        /// <summary>
        /// Puts a site in or out of Play Mode (the owner's F5, or an Adventure round starting / ending): everyone on the
        /// landscape gets ClaimInfoUpdate 0x13, and the story tools start or stop. <paramref name="restart"/> restarts a
        /// site that is already playing (a fresh story for a new round).
        /// </summary>
        public static void ApplyMode(ClaimModel claim, bool play, bool restart = false)
        {
            bool was = IsPlaying(claim.Id);
            lock (sync)
            {
                if (play)
                    playing.Add(claim.Id);
                else
                    playing.Remove(claim.Id);
            }
            var update = BuildInfoUpdate(claim, InfoMode);
            foreach (WorldSession viewer in PlayerManager.InZone(claim.ZoneId))
                if (viewer.RezoneTarget == null)
                    viewer.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            if (was != play || (play && restart))
                StoryTools.PlayModeChanged(claim, play);
            // Story tools hide in Play Mode and come back in Edit Mode (server-side, the client has no rule: L20). After
            // the story stopped, so a counter coming back shows its Edit value (StoryTools.SendStateTo).
            if (was != play)
                Props.PlayModeVisibilityChanged(claim, play);
        }

        public static bool IsPlaying(ulong claimId)
        {
            lock (sync)
                return playing.Contains(claimId);
        }

        /// <summary>Longest site name the client keeps (it truncates to 128 characters itself).</summary>
        private const int MaxNameLength = 128;

        /// <summary>
        /// SetItemName (0x2A) from Build Site Management: the owner renames a site. The client has already shown the new
        /// name; the server checks it (owner, length, control characters, the client's word blacklist), saves it and
        /// sends the site to everyone. A refused name is put back with the same AOI update.
        /// </summary>
        private static void SetName(WorldSession session, ulong claimId, string name)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null)
                return;
            // Site names show in other players' notification lines: typed rich-text tokens made harmless (L54).
            name = Shared.Game.RichTextTokens.Neutralize((name ?? "").Trim());
            string problem = name.Length == 0 ? "A build site needs a name."
                : name.Length > MaxNameLength ? $"Build site names can be at most {MaxNameLength} characters."
                : name.Any(char.IsControl) ? "That name has characters that aren't allowed."
                : IsBlacklisted(name) ? "That name isn't allowed." : null;
            if (problem != null)
            {
                Chat.System(session, problem);
                Resend(claim);
                return;
            }
            claim.Name = name;
            ulong id = claim.Id;
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                model.Name = name;
                c.MarkModified(model, nameof(model.Name));
            });
            log.Info($"{session.CharacterName} renamed claim {id} to \"{name}\".");
            Resend(claim);
            Achievements.Progress(session, Achievements.CustomizeSiteName);
        }

        /// <summary>
        /// The site name and welcome message boxes send their text on every keystroke. Each change is applied (checked,
        /// saved, sent to everyone) only once typing has paused for <see cref="TypingPause"/>, so a half-typed name isn't
        /// refused and a sentence isn't broadcast letter by letter.
        /// </summary>
        private static void AfterTyping(ulong claimId, bool description, Action apply)
        {
            long stamp = typingStamps.AddOrUpdate((claimId, description), 1, (_, v) => v + 1);
            System.Threading.Tasks.Task.Delay(TypingPause).ContinueWith(_ =>
            {
                if (typingStamps.TryGetValue((claimId, description), out long latest) && latest == stamp)
                    apply();
            });
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ulong, bool), long> typingStamps = new();
        private static readonly TimeSpan TypingPause = TimeSpan.FromSeconds(1.5);

        /// <summary>Longest welcome message kept (the column is 512 characters).</summary>
        private const int MaxDescriptionLength = 512;

        /// <summary>
        /// SetItemDescription (0x2B): the owner's welcome message ("Your Build Site's Epic Story"). Checked like names
        /// (line breaks allowed), saved and sent to everyone with the site.
        /// </summary>
        private static void SetDescription(WorldSession session, ulong claimId, string text)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null)
                return;
            text = (text ?? "").Trim();
            string problem = text.Length > MaxDescriptionLength ? $"Welcome messages can be at most {MaxDescriptionLength} characters."
                : text.Any(c => char.IsControl(c) && c != '\n' && c != '\r') ? "That message has characters that aren't allowed."
                : IsBlacklisted(text) ? "That message isn't allowed." : null;
            if (problem != null)
            {
                Chat.System(session, problem);
                Resend(claim);
                return;
            }
            claim.Description = text;
            ulong id = claim.Id;
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                model.Description = text;
                c.MarkModified(model, nameof(model.Description));
            });
            log.Info($"{session.CharacterName} set claim {id}'s welcome message.");
            Resend(claim);
        }

        /// <summary>The client's word blacklist (BlackListEntries.txt), as used for character names.</summary>
        private static bool IsBlacklisted(string text) =>
            LandmarkEmulator.Shared.GameTable.GameTableManager.Instance.BlackList?.Entries.Any(e =>
                e.RequiresExactMatch ? string.Equals(text, e.Word, StringComparison.OrdinalIgnoreCase)
                    : text.Contains(e.Word, StringComparison.OrdinalIgnoreCase)) ?? false;

        /// <summary>Sends a changed claim to everyone (an AOI update re-adding it).</summary>
        private static void Resend(ClaimModel claim)
        {
            var update = BuildAoiUpdate(false, new[] { claim }, Array.Empty<ulong>());
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
        }

        /// <summary>
        /// Drops a half-finished placement when the player leaves.
        /// </summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                pending.Remove(session);
        }
    }
}
