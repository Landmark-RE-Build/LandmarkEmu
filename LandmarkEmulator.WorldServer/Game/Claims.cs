using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
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
    /// (sub 6), the player confirms (PlaceClaimConfirm, sub 0x1B) and the server creates the claim, takes a Build
    /// Site Flag and sends PlaceClaimComplete (sub 7) plus the claim to everyone nearby.
    /// </summary>
    public static class Claims
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Item used up to place a claim.</summary>
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
        /// Default access of a new site: Play Around (0x003) — the May 25 2016 update "assured that 'Play Around' was the
        /// default security setting" (wiki Game Updates 2016): visitors may use props, nothing more.
        /// </summary>
        private const uint DefaultAccess = 0x003;
        /// <summary>Most root sites any player could have (wiki: "The maximum slots for Build Sites is 8").</summary>
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
            lock (sync)
            {
                claims.AddRange(stored);
                foreach (ClaimPermissionModel p in DatabaseManager.Instance.CharacterDatabase.GetClaimPermissions())
                {
                    if (!special.TryGetValue(p.ClaimId, out var map))
                        special[p.ClaimId] = map = new Dictionary<ulong, uint>();
                    map[p.CharacterId] = p.Mask;
                }
                foreach ((ulong id, string name) in DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(
                    special.Values.SelectMany(m => m.Keys).Distinct()))
                    specialNames[id] = name;
                nextId = stored.Count == 0 ? 1 : (long)stored.Max(c => c.Id);
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
                DatabaseManager.Instance.CharacterDatabase.Save(db =>
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

        /// <summary>
        /// Refusals that don't depend on the spot: on a Pioneer landscape (28), no flag (6), already at the site limit (10).
        /// </summary>
        private static uint? OwnerRefusal(WorldSession session)
        {
            if (Landscapes.Get(session.ZoneId) is { Pioneer: true })
                return ResultPioneer;
            if (session.Inventory.CountOf(BuildSiteFlagItem) == 0)
                return ResultNoFlag;
            lock (sync)
                if (claims.Count(c => c.OwnerGuid == session.PlayerGuid) >= MaxClaimsPerOwner)
                    return ResultMaxOwned;
            return null;
        }

        /// <summary>
        /// Routes a ClaimsBase request (u8 sub) from the client: placement (0x1A request, 0x1B confirm), Site Management
        /// (0x1C management data, 0x1E edit data, 0x20 upkeep, renames, descriptions, permissions, delete), and the requests
        /// the client makes when windows open (0x27 sites by owner, 0x35 prop exclusion areas). 0x19 is deliberately left
        /// unanswered: see docs/protocol.md, section 10.
        /// </summary>
        public static void HandleRequest(WorldSession session, byte sub, byte[] body)
        {
            var reader = new GamePacketReader(body);
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
                    ulong id = reader.ReadULong();
                    SetDefaultPermissions(session, id, reader.ReadUInt());
                    break;
                }
                case 0x2E:
                {
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
                        Contests.Submit(session, body);
                    break;
                case 0x44:
                    // OnClientRateClaim {u64 claim}: the client reports a vote it just had confirmed (RatingBase
                    // VoteReply, built in FUN_142d1f470). The vote itself is counted by Ratings; nothing to answer.
                    break;
                case 0x35:
                    // RequestPropExclusionArea: RecievePropExclusionArea (0x11) {u32, empty list}.
                    SendSimple(session, 0x11, w => { w.Write(0u); w.Write(0u); });
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Claims 0xF9", sub);
                    log.Debug($"Unhandled claims request 0x{sub:X2}: {BitConverter.ToString(body)}");
                    break;
            }
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
        /// shows: Pioneer landscape, no flag, site limit (<see cref="OwnerRefusal"/>), unknown landscape (3), or too close
        /// to another site (26, <see cref="IsFree(uint, double, double, double)"/>).
        /// </summary>
        private static uint CanPlace(WorldSession session, double x, double y, double z)
        {
            if (Landscapes.Get(session.ZoneId) == null)
                return ResultInvalidLocation;
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
            return IsFree(session.ZoneId, x, y, z) ? ResultConfirm : ResultBufferOverlap;
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
            if (result != ResultConfirm || !session.TakeItem(BuildSiteFlagItem, 1))
            {
                SendComplete(session, result == ResultConfirm ? ResultNoFlag : result, null);
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
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.Claim.Add(claim))
                .ContinueWith(t => log.Error(t.Exception, $"Failed to save claim {claim.Id}."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);

            SendComplete(session, ResultPlaced, claim);
            byte[] add = BuildAoiUpdate(false, new[] { claim }, Array.Empty<ulong>());
            // The placer too: PlaceClaimComplete alone doesn't make the client treat the new site as one it stands in
            // (props couldn't be placed in it until the next login, when the full list arrives).
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, add);
            log.Info($"{session.CharacterName} placed claim {claim.Id}.");
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
                writer.Write(Landscapes.WorldId);
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
        /// Point-of-Need upkeep (Lua Claims.PayUpkeepPoN(days, flag) from ClaimPoN.lua → InGamePurchase 0x46
        /// PointOfNeedRequest type 1 {u32 1, u8 flag, u32 days, u64, u64}). Upkeep is free here and never runs out while
        /// the owner plays, so it is simply confirmed and the timer re-sent.
        /// </summary>
        public static void PayUpkeep(WorldSession session, uint days)
        {
            Chat.System(session, $"Upkeep paid for {days} day(s): free on this server.");
            SendUpkeepData(session);
            log.Info($"{session.CharacterName} paid upkeep for {days} day(s) (free).");
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
            writer.Write((uint)MaxClaimsPerOwner);       // PlayerMaxRootClaims: this player's slots
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
            List<ClaimModel> expired = all.Where(c => !online.Contains(c.OwnerGuid)
                && lastOnline.TryGetValue(c.OwnerGuid, out DateTime? last) && last is DateTime seen
                && now - DateTime.SpecifyKind(seen, DateTimeKind.Utc) > TimeSpan.FromDays(SiteUpkeep.DaysOf(c.OwnerGuid))).ToList();
            if (expired.Count == 0)
                return;
            Remove(expired, "upkeep expired");
            foreach (ClaimModel claim in expired)
                SiteContents.Pack(claim);
        }

        /// <summary>
        /// Writes a ClaimsBase packet: the u8 sub-opcode, then the body.
        /// </summary>
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
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                model.Tags = tags;
                c.MarkModified(model, nameof(model.Tags));
            });
            byte[] update = BuildInfoUpdate(claim, InfoTags);
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId).Where(o => o != session))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            if (session.ZoneId != claim.ZoneId)
                session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            log.Info($"{session.CharacterName} set site {id} tags to \"{tags}\".");
        }

        /// <summary>The site's time-of-day settings (Site Management), saved and sent to everyone who sees the site.</summary>
        private static void SetTime(WorldSession session, ulong claimId, int hour, float rate, int visitorHour, float visitorRate)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null)
                return;
            static int Hour(int h) => h < 0 || h > 23 ? -1 : h;
            static float Rate(float r) => float.IsFinite(r) && r >= 0f && r <= 100f ? r : 1f;
            (claim.TimeHour, claim.TimeRate) = (Hour(hour), Rate(rate));
            (claim.VisitorTimeHour, claim.VisitorTimeRate) = (Hour(visitorHour), Rate(visitorRate));
            ulong id = claim.Id;
            (int h1, float r1, int h2, float r2) = (claim.TimeHour, claim.TimeRate, claim.VisitorTimeHour, claim.VisitorTimeRate);
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                (model.TimeHour, model.TimeRate, model.VisitorTimeHour, model.VisitorTimeRate) = (h1, r1, h2, r2);
                c.MarkModified(model, nameof(model.TimeHour), nameof(model.TimeRate), nameof(model.VisitorTimeHour), nameof(model.VisitorTimeRate));
            });
            log.Info($"{session.CharacterName} set site {id} time: owner {h1} h ×{r1}, visitors {h2} h ×{r2}.");
            // Only the time block changes: the small info update instead of the whole site record.
            byte[] update = BuildInfoUpdate(claim, InfoTime);
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
        }

        /// <summary>
        /// Claim record (client FUN_142d01c40). Only id, owner, name and bounds are filled; the rest (members,
        /// permissions lists, upkeep, contest data) is empty.
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
            // Default access for everyone / friends / guild mates (client claim +0x2d0, +0x2d4, +0x2d8).
            for (int i = 0; i < 3; i++)
                writer.Write(claim.DefaultPermissions);
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
            writer.Write(0ul);
            writer.Write(false);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);                           // empty list
            for (int i = 0; i < 5; i++)
                writer.Write("");
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
        /// and fresh management data.
        /// </summary>
        private static void Delete(WorldSession session, ulong claimId)
        {
            ClaimModel claim;
            lock (sync)
                claim = claims.FirstOrDefault(c => c.Id == claimId && c.OwnerGuid == session.PlayerGuid);
            uint result = claim == null ? DeleteFailed
                : claim.ZoneId != session.ZoneId ? DeleteForeignLandscape
                : InstancedGames.HasGame(claim.Id) ? DeleteHasGame
                : DeleteDone;
            if (result != DeleteDone)
                claim = null;
            if (claim != null)
            {
                Remove(new List<ClaimModel> { claim }, $"deleted by {session.CharacterName}");
                // "A copy of your current Build Site will be created and stored in your Designs list" (the dialog).
                var (_, built) = SiteDesigns.Save(claim);
                int refunded = SiteDesigns.Refund(session, built);
                List<ulong> reverted = Terrain.TerrainEdits.RevertBox(claim.MinX, claim.MinY, claim.MinZ,
                    claim.MaxX, claim.MaxY, claim.MaxZ, claim.ZoneId);
                Terrain.TerrainEdits.Broadcast(reverted, claim.ZoneId);
                var origin = Terrain.TerrainStore.ZoneOrigin;
                int returned = Props.ReturnInBox(session,
                    new Vector3((float)((claim.MinX - origin.X) / 10), (float)((claim.MinY - origin.Y) / 10), (float)((claim.MinZ - origin.Z) / 10)),
                    new Vector3((float)((claim.MaxX - origin.X) / 10), (float)((claim.MaxY - origin.Y) / 10), (float)((claim.MaxZ - origin.Z) / 10)), claim.ZoneId);
                session.GiveItem(BuildSiteFlagItem, 1);
                log.Info($"Deleted site {claim.Id}: {reverted.Count} cell(s) reverted, {returned} prop(s) and {refunded} material item(s) returned.");
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

        /// <summary>ClaimInfoUpdate kinds (sub 5, client FUN_142d1ff50): the site's game / the site was deleted / its time settings / its music changed.</summary>
        private const uint InfoTags = 4, InfoGame = 0x11, InfoMode = 0x13, InfoDeleted = 0x14, InfoTime = 0x15, InfoMusic = 0x16;
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
            byte[] update = BuildInfoUpdate(claim, InfoGame);
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
            byte[] update = BuildInfoUpdate(claim, InfoMusic);
            foreach (WorldSession other in PlayerManager.InZone(claim.ZoneId))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            log.Info($"{session.CharacterName} set site {claim.Id} music to {music}.");
        }

        /// <summary>
        /// ClaimInfoUpdate (sub 5, reader FUN_142d029f0): u64 claim, u32 kind, then one fixed record the client reads
        /// fields from by kind. Kind 0x14 removes the site from the owner's own list as well (FUN_142d18090 mode 2;
        /// an AOI removal is only "left the area" and the site stays in My Sites); 0x15 applies the time block; 0x16 the
        /// site music (+0x278).
        /// Layout: string, u32, 3 x u32, u64, 4 x u32, u64, 5 strings, u64, the FUN_142cffe50 struct, u64, string,
        /// empty list, time block (owner hour, rate, visitor hour, rate), u32 music, u32.
        /// </summary>
        private static byte[] BuildInfoUpdate(ClaimModel claim, uint kind)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)5);
            w.Write(claim.Id);
            w.Write(kind);
            // The first string is the name, and the tags for kind 4.
            w.Write((kind == InfoTags ? claim.Tags : claim.Name) ?? "");
            w.Write(0u);
            for (int i = 0; i < 3; i++) w.Write(0u);
            w.Write(0ul);
            for (int i = 0; i < 4; i++) w.Write(0u);
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
            w.Write(MusicOf(claim));                    // +0x278: kind 0x16 applies it to the site (client +0xf4)
            w.Write(ModeOf(claim.Id));                  // kind 0x13 applies it (2 = Play Mode)
            return output.ToArray();
        }

        /// <summary>Deletes claims, from the database and from every player's view.</summary>
        private static void Remove(List<ClaimModel> gone, string reason)
        {
            lock (sync)
                claims.RemoveAll(c => gone.Any(g => g.Id == c.Id));
            // Owners online drop it from their own sites (My Sites) too, not just from the area.
            foreach (ClaimModel claim in gone)
                PlayerManager.FindByGuid(claim.OwnerGuid)?.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(claim, InfoDeleted));
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.Claim.RemoveRange(gone))
                .ContinueWith(t => log.Error(t.Exception, "Failed to delete claims."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            foreach (var group in gone.GroupBy(c => c.ZoneId))
            {
                byte[] update = BuildAoiUpdate(false, Array.Empty<ClaimModel>(), group.Select(c => c.Id).ToList());
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
        public static (Vector3 Position, uint Zone, ulong ClaimId)? ArrivalOf(ulong ownerGuid)
        {
            ClaimModel claim;
            lock (sync)
                claim = claims.FirstOrDefault(c => c.OwnerGuid == ownerGuid);
            if (claim == null || CentreOf(ownerGuid) is not (Vector3 centre, uint zone))
                return null;
            var start = Props.PlacedOf(zone, new HashSet<uint> { StoryTools.StartingLocationProp })
                .Where(p => ClaimAt(zone, p.Position) == claim.Id).Select(p => (Vector3?)p.Position).FirstOrDefault();
            return (start is Vector3 s ? s + new Vector3(0f, 0.5f, 0f) : centre, zone, claim.Id);
        }

        /// <summary>
        /// Every site has one Starting Location: wiki "On newly claimed Build Sites, the Starting Location prop will be
        /// placed by default in the northwest corner" (free, one per site; where visitors arrive and revive,
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
        /// The build check for one terrain cell (global box), done once per cell instead of per voxel: null when every
        /// site overlapping the box lets the player build (or none does), otherwise a point test against only the
        /// sites that refuse them. Same answer as <see cref="MayEdit"/> for points in the box; a 26-cell edit used to
        /// run ~200,000 locked scans of all sites (2026-10-13: the remove tool stalled between clicks).
        /// </summary>
        public static Func<double, double, double, bool> BuildCheckIn(ulong playerGuid, uint zone,
            (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            zone = zone == 0 ? Terrain.TerrainStore.HomeContinent : zone;
            List<(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)> refused;
            lock (sync)
                refused = claims.Where(c => c.ZoneId == zone && c.MinX <= max.X && c.MaxX >= min.X && c.MinY <= max.Y
                        && c.MaxY >= min.Y && c.MinZ <= max.Z && c.MaxZ >= min.Z
                        && (PermissionsFor(c, playerGuid) & PermissionBuild) != PermissionBuild)
                    .Select(c => ((double)c.MinX, (double)c.MinY, (double)c.MinZ, (double)c.MaxX, (double)c.MaxY, (double)c.MaxZ)).ToList();
            if (refused.Count == 0)
                return null;
            return (x, y, z) => !refused.Exists(b => x >= b.MinX && x <= b.MaxX && y >= b.MinY && y <= b.MaxY && z >= b.MinZ && z <= b.MaxZ);
        }

        /// <summary>
        /// A player's permission mask in one site: everything for the owner, their Special Access entry if any, else the
        /// site's Default Access.
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

        /// <summary>SetItemBasicPermissions (0x2D: u64 claim, u32 mask, u64, bool) — "Default Access".</summary>
        private static void SetDefaultPermissions(WorldSession session, ulong claimId, uint mask)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null)
                return;
            claim.DefaultPermissions = mask & OwnerPermissions;
            ulong id = claim.Id;
            uint value = claim.DefaultPermissions;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new ClaimModel { Id = id };
                c.Claim.Attach(model);
                model.DefaultPermissions = value;
                c.MarkModified(model, nameof(model.DefaultPermissions));
            });
            log.Info($"{session.CharacterName} set default access of claim {id} to 0x{value:X}.");
            Resend(claim);
        }

        /// <summary>ResultResponse result: no player or guild by that name.</summary>
        private const uint NameNotFound = 0x17;

        /// <summary>Dev / tests: Special Access on the player's own (first) site, as Site Management sets it.</summary>
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
        /// SetItemIndividualPermission (0x2E: u64 claim, u8, string player name, u8, u32 mask, bool) — "Special
        /// Access". Mask 0 removes the player's entry.
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
            mask &= OwnerPermissions;
            lock (sync)
            {
                if (!special.TryGetValue(id, out var map))
                    special[id] = map = new Dictionary<ulong, uint>();
                if (mask == 0)
                    map.Remove(characterId);
                else
                    map[characterId] = mask;
                specialNames[characterId] = target.Name;
            }
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                ClaimPermissionModel row = c.ClaimPermission.Find(id, characterId);
                if (mask == 0)
                {
                    if (row != null)
                        c.ClaimPermission.Remove(row);
                }
                else if (row == null)
                    c.ClaimPermission.Add(new ClaimPermissionModel { ClaimId = id, CharacterId = characterId, Mask = mask });
                else
                    row.Mask = mask;
            });
            log.Info($"{session.CharacterName} gave {target.Name} access 0x{mask:X} on claim {id}.");
            Resend(claim);
            // The owner's open Site Management dialog lists Special Access from the edit data: refresh it.
            SendEditData(session, id);
        }

        /// <summary>Build sites on a landscape (the Landscapes window's claim count).</summary>
        public static int CountOn(uint zone)
        {
            lock (sync)
                return claims.Count(c => c.ZoneId == zone);
        }

        /// <summary>The build site containing a local-space point on a landscape, or null.</summary>
        /// <summary>The owner of a build site (0 when there is no such site).</summary>
        public static ulong OwnerOf(ulong claimId)
        {
            lock (sync)
                return claims.FirstOrDefault(c => c.Id == claimId)?.OwnerGuid ?? 0;
        }

        public static ulong? ClaimAt(uint zone, Vector3 local)
        {
            var p = Terrain.TerrainEdits.ToGlobal(local);
            lock (sync)
                return claims.FirstOrDefault(c => c.ZoneId == zone && p.X >= c.MinX && p.X <= c.MaxX && p.Y >= c.MinY
                    && p.Y <= c.MaxY && p.Z >= c.MinZ && p.Z <= c.MaxZ)?.Id;
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
            DatabaseManager.Instance.CharacterDatabase.Save(c => c.Claim.Add(claim))
                .ContinueWith(t => log.Error(t.Exception, $"Failed to save claim {claim.Id}."),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            byte[] add = BuildAoiUpdate(false, new[] { claim }, Array.Empty<ulong>());
            foreach (WorldSession other in PlayerManager.InZone(zone))
                other.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, add);
            log.Info($"Granted {ownerName} site {claim.Id} on landscape {zone}.");
            return claim;
        }

        /// <summary>Dev/testing: claims a site at the player's position through the normal request/confirm path.</summary>
        public static void DevPlaceHere(WorldSession session)
        {
            var origin = Terrain.TerrainStore.ZoneOrigin;
            Vector3 p = session.Position;
            session.GiveItem(BuildSiteFlagItem, 1);
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
        /// <summary>Sites in Play mode (F5); everything else is in Edit mode. Not persisted: sites start in Edit.</summary>
        private static readonly HashSet<ulong> playing = new();

        /// <summary>
        /// The owner switched the site's mode (F5 / the Play Mode button). Everyone on the landscape learns it from
        /// ClaimInfoUpdate kind 0x13 (FUN_142d1ff50 → claim entry vtable +0x28, FUN_142d198a0), the mode being the
        /// record's last u32; the requester's client waits for it and shows "Claim.EnableNpcs.Failure" when the answer
        /// isn't Play while it asked for Play. Story tools (Links, creature spawners) start or stop.
        /// </summary>
        public static void SetAdventureStatus(WorldSession session, ulong claimId, uint status)
        {
            ClaimModel claim = OwnedClaim(session, claimId);
            if (claim == null)
            {
                if (Get(claimId) is ClaimModel other)
                    session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, BuildInfoUpdate(other, InfoMode));
                return;
            }
            ApplyMode(claim, status == PlayMode);
            log.Info($"{session.CharacterName} switched site {claim.Id} to {(status == PlayMode ? "Play" : "Edit")} mode.");
            Achievements.SiteModeChanged(session, status == PlayMode);
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
            byte[] update = BuildInfoUpdate(claim, InfoMode);
            foreach (WorldSession viewer in PlayerManager.InZone(claim.ZoneId))
                if (viewer.RezoneTarget == null)
                    viewer.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, update);
            if (was != play || (play && restart))
                StoryTools.PlayModeChanged(claim, play);
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
            name = (name ?? "").Trim();
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
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
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
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
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
            byte[] update = BuildAoiUpdate(false, new[] { claim }, Array.Empty<ulong>());
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
