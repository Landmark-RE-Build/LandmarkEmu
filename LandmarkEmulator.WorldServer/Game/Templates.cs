using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Internal.Commerce;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Game.WorldChannel;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player designs (templates), the Designs tab of build mode. TemplateBase 0xF0, u8 sub (client dispatcher
    /// FUN_1430624b0).
    /// <para>Saving: the Designs tab's "+" (Lua Templates.SaveTemplate → Selection tool → FUN_1430ba7b0) sends the
    /// selection request (VoxelConstruction sub 2) with action 3, the design's name and tags, and the box's voxels like
    /// a copy. The server stores it and answers 3 SaveTemplateResponse {bool ok, TemplateBasicData}; the client adds
    /// it to its list.</para>
    /// <para>Placing: selecting a design asks for its voxels (4 VoxelDataRequest {u64, u64 guid, bool}) unless they
    /// are cached; 5 VoxelDataResponse {u64, TemplateBasicData, block list, image list, bool} fills the cache and the
    /// client builds its paste clipboard from it (FUN_1430671e0). The paste request then carries the design's guid,
    /// and the server places the stored voxels (Building.Paste).</para>
    /// <para>Other subs: 2 EnvironmentListResponse {u32 n × TemplateBasicData, u64} (on zone-in), 6 DeleteTemplate →
    /// 7 {u64, u64 guid, bool ok, bool cannot-delete}, 0xA rename → 0xC UpdateTemplateResponse {TemplateBasicData}
    /// (0xB {u32 error 1 not found / 2 not author / 3 immutable, u64 guid} only on failure).</para>
    /// TemplateBasicData (reader FUN_143058180; Lua names from FUN_143... near "TemplateGUID"): u64 TemplateGUID,
    /// string name, u64, u64 AuthorPlayerGuid, u64 DateCreated, u64 DateAcquired, u32 IsOnMarketplace,
    /// u32 IsDesignerTemplate, 3 × u32 dimensions, material map {i32 n × (u16, u32)} (VoxelCount), PropData list
    /// (PropCount), list of 0x38-byte records, list of 0x68-byte records, bool IsDefault, string AllTags,
    /// bool counts against storage, u32, u32, u32, bool, string AuthorName, u32 TemplateValue, bool ContestWinner, u32.
    /// </summary>
    public static class Templates
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>0x1B ExportToItemRequest {u64 design} (Lua ExportToItem, FUN_14352a680).</summary>
        public const byte ExportToItemSub = 0x1B;

        /// <summary>A player's design name by guid ("" when unknown).</summary>
        public static string NameOf(WorldSession session, ulong guid) => Find(session.PlayerGuid, guid)?.Name ?? "";

        /// <summary>A stored design by its guid and owner (a character, or a Player Design item's guid).</summary>
        public static TemplateModel FindDesign(ulong owner, ulong guid) => Find(owner, guid);

        /// <summary>
        /// Is this design of the player's a copy they took from Player Studio (TASKS 6.45: such designs go only on the
        /// player's own site)? A Player Studio copy (<see cref="Commerce.TakeDesign"/>) is someone else's work (not
        /// <see cref="IsOwnWork"/>) matching, by name, size and materials, the source design of a product the player has
        /// taken (<see cref="Unlocks.StudioDesign"/> rows: product ids). <b>Inferred identification</b>: the stored copy
        /// keeps no product id (live kept "template instances", the client's text Construction.OnlyClaimOwnerMayPlaceOr
        /// CopyTemplateInstances); designs traded as items are not restricted, and a Player Studio copy can't be exported to
        /// an item (DesignItems.Export, audit-wiki-gaps #4).
        /// </summary>
        public static bool IsPlayerStudioCopy(ulong owner, ulong guid)
        {
            if (IsStarter(guid))
                return false;
            TemplateModel design = Find(owner, guid);
            if (design == null || IsOwnWork(design))
                return false;
            var products = Unlocks.ValuesOfAnyone(owner, Unlocks.StudioDesign).Select(p => (ulong)p).ToList();
            if (products.Count == 0)
                return false;
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            var sources = context.PlayerStudioProduct.Where(p => products.Contains(p.Id)).Select(p => p.TemplateId).ToList();
            return context.Template.Any(t => sources.Contains(t.Id) && t.Name == design.Name && t.SizeX == design.SizeX
                && t.SizeY == design.SizeY && t.SizeZ == design.SizeZ && t.Materials == design.Materials);
        }

        /// <summary>
        /// A deleted or lapsed site's design keeps the site's screenshots (hotfix 1 Apr 2016, P4365: designs made
        /// automatically when upkeep runs out keep their screenshots): the Site Management shots
        /// (<see cref="SiteScreenshots"/>, index 0 the primary one) are copied as the design's shots, index by index, while
        /// both sizes exist. Returns how many.
        /// </summary>
        public static int CopySiteImages(ulong site, ulong design)
        {
            int copied = 0;
            for (int index = 0; index < MaxImages; index++)
            {
                byte[] large = SiteScreenshots.Image(site, index, "large"), small = SiteScreenshots.Image(site, index, "small");
                if (large == null || small == null)
                    break;
                System.IO.Directory.CreateDirectory(ImageFolder);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(ImageFolder, design + "_" + index + "_large.jpg"), large);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(ImageFolder, design + "_" + index + "_small.jpg"), small);
                copied++;
            }
            return copied;
        }

        /// <summary>
        /// The client's own "You have learned a new Design" notice with the design's name (Learned.Template; TemplateBase
        /// 0xF0 sub 0x11 EnvironmentAddOwnershipResponse, reader FUN_143058780, handler FUN_143061f90 — the notice only):
        /// {u8 sub, u64, u64, bool, u64, string name}.
        /// </summary>
        public static void SendLearned(WorldSession session, ulong design, string name)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((byte)0x11);
            w.Write(design);
            w.Write(session.PlayerGuid);
            w.Write(true);
            w.Write(0ul);
            w.Write(name ?? "");
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
        }

        /// <summary>The designs held by an owner (a character or an item).</summary>
        public static List<TemplateModel> OwnedBy(ulong owner) => Owned(owner);

        /// <summary>Does the player's design storage have room for <paramref name="voxels"/> more voxels?</summary>
        public static bool HasRoom(WorldSession session, long voxels) => UsedVoxels(session.PlayerGuid) + voxels <= MaxVoxels(session);

        /// <summary>Does the player's design storage have room for this design?</summary>
        public static bool HasRoom(WorldSession session, TemplateModel design)
            => HasRoom(session, Materials(design).Sum(m => (long)m.Count));

        /// <summary>Adds a design to the player's Designs list (3 SaveTemplateResponse, ok).</summary>
        public static void SendAdded(WorldSession session, TemplateModel design)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(SaveResponseSub);
            writer.Write(true);
            WriteBasicData(writer, design);
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
        }

        public const byte ListRequestSub = 1, ListResponseSub = 2, SaveResponseSub = 3, VoxelDataRequestSub = 4, VoxelDataResponseSub = 5,
            DeleteRequestSub = 6, DeleteResponseSub = 7, RenameRequestSub = 0xA, RenameErrorSub = 0xB, UpdateResponseSub = 0xC;

        /// <summary>Templates.MaxVoxelExpandable: the most design storage expanders can bring a player to.</summary>
        public const long MaxVoxelExpandable = 48_000_000;

        /// <summary>
        /// Design storage is counted in voxels, not designs (client FUN_143061750 → Lua Character.Template.Limits
        /// MaxVoxels): Templates.MaxTotalStoredVoxelCount (6,000,000) plus the player's voxel storage expanders, at most
        /// Templates.MaxVoxelExpandable. Over it, a save is refused with Response 0x11
        /// (Construction.ExceedsMaxTotalTemplateVoxelsCount, which offers Voxel Storage Expanders).
        /// </summary>
        public static long MaxVoxels(WorldSession session) => Math.Min(ItemUses.DesignStorageOf(session.PlayerGuid), MaxVoxelExpandable);

        /// <summary>
        /// Voxels the player's designs take up (CurrentVoxels, summed in the client's list builder): the material counts
        /// of every design that CountsAgainstStorageLimit — not the game's own (designer) designs, nor those
        /// <see cref="FreeOfStorage"/> names.
        /// </summary>
        public static long UsedVoxels(ulong owner)
        {
            List<TemplateModel> owned = Owned(owner);
            HashSet<ulong> free = FreeOfStorage(owner, owned);
            return owned.Where(t => !IsDesigner(t) && !free.Contains(t.Id)).Sum(t => Materials(t).Sum(m => (long)m.Count));
        }

        /// <summary>
        /// Do Player Studio designs take no design storage (setting PlayerStudioDesignsFreeStorage, on = live)? Live: the
        /// designs bought on the Marketplace tab of the Showcase don't count toward the limit (wiki Designs, and Build Mode:
        /// "any designs you buy from the Marketplace tab ... do not count toward the limit"), and a design successfully
        /// submitted to Player Studio no longer counts against Voxel Storage (developer, Hotfix Notes 17 Oct 2014, thread
        /// 49423). The Designs window shows the per-design "free" column from the TemplateBasicData storage bool (notes 21
        /// Mar 2016, developer 6 May 2016 thread 57063).
        /// </summary>
        public static bool StudioDesignsFree
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.PlayerStudioDesignsFreeStorage ?? true;

        /// <summary>
        /// Which of <paramref name="designs"/> (all the owner's) take no design storage beyond the designer designs, while
        /// <see cref="StudioDesignsFree"/> is on: those on Player Studio (waiting for review or listed,
        /// <see cref="ICommerceService.IsOnMarketplace"/>) and the copies the owner took from Player Studio (matched as
        /// <see cref="IsPlayerStudioCopy"/> does, with one query for all of them). Empty while the setting is off.
        /// </summary>
        public static HashSet<ulong> FreeOfStorage(ulong owner, IReadOnlyCollection<TemplateModel> designs)
        {
            var free = new HashSet<ulong>();
            if (!StudioDesignsFree || designs.Count == 0)
                return free;
            foreach (TemplateModel design in designs)
                if (design.Id != 0 && !IsDesigner(design) && WorldChannelServices.Commerce.IsOnMarketplace(design.Id))
                    free.Add(design.Id);
            var products = Unlocks.ValuesOfAnyone(owner, Unlocks.StudioDesign).Select(p => (ulong)p).ToList();
            if (products.Count == 0)
                return free;
            var others = designs.Where(d => d.Id != 0 && !IsStarter(d.Id) && !IsDesigner(d) && !free.Contains(d.Id) && !IsOwnWork(d)).ToList();
            if (others.Count == 0)
                return free;
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            var sourceIds = context.PlayerStudioProduct.Where(p => products.Contains(p.Id)).Select(p => p.TemplateId).ToList();
            var sources = context.Template.Where(t => sourceIds.Contains(t.Id))
                .Select(t => new TemplateModel { Name = t.Name, SizeX = t.SizeX, SizeY = t.SizeY, SizeZ = t.SizeZ, Materials = t.Materials })
                .ToList();
            foreach (TemplateModel d in others)
                if (sources.Any(t => IsSameDesign(t, d)))
                    free.Add(d.Id);
            return free;
        }

        /// <summary>A Player Studio copy matches its product's source design by name, size and materials (the stored copy
        /// keeps no product id; <see cref="IsPlayerStudioCopy"/>).</summary>
        internal static bool IsSameDesign(TemplateModel source, TemplateModel copy)
            => source.Name == copy.Name && source.SizeX == copy.SizeX && source.SizeY == copy.SizeY && source.SizeZ == copy.SizeZ
               && source.Materials == copy.Materials;

        /// <summary>Does this one design count against its owner's design storage (TemplateBasicData's storage bool)?</summary>
        private static bool CountsAgainstStorage(TemplateModel design)
            => !IsDesigner(design) && (design.Id == 0 || !FreeOfStorage(design.OwnerGuid, new[] { design }).Contains(design.Id));

        // ---- Starter Castle ------------------------------------------------------------------------------------------

        /// <summary>
        /// The Starter Castle designs (client ExportedTemplateCategoryMap: category 1 "Starter Castle", 28 templates;
        /// their names are texts 574226-574336/576961-576962). Served from the client's own .otd files, not stored per
        /// character. Name → file by name, and by size where the names differ: walls are 12 × 50 (Short 24 = ramparts
        /// short, Medium 72, Long 168, plus the rampart corner and entry), rooms are the 86 m-high family (room v2, the
        /// 4-way and corner "tower" files), towers the 124 m-high family (the only inline piece is a tall tower).
        /// Modular Castle Window with Wall (template 1141, item 4106) has no file (no 26 × 50 × 40 window); if one turns
        /// up, it goes at the end as index 27.
        /// <para><b>The order is fixed</b>: a piece's guid is "ST" + its index here (<see cref="StarterGuidBase"/>), and
        /// the client caches designs by guid.</para>
        /// <para>Per piece (trace/0695/spec-design-categories.md §2):</para>
        /// <list type="bullet">
        /// <item><c>TemplateId</c>: the live template database id, i.e. the ExportedTemplateCategoryMap TEMPLATE_ID the
        /// 0.695 Designs window compares with TemplateBasicData +0xec to file a designer template under "Starter Castle"
        /// (FUN_143755020, called from the palette's count / entry builders FUN_143754540 / FUN_143755d60). It is the
        /// same number the piece's design item carries as its grantId (22 of the 28 rows; the map rows are in name
        /// order, which pins the 6 without an item).</item>
        /// <item><c>ItemId</c>: the design item that unlocks the piece (0: none). 3706/3707/4072-4080/4085/4360/4361 are
        /// the 13 castle pieces in reward bundle 1051 (goal 471 "Your Home Is Your Castle", and again 477, in the
        /// Feb 2017 capture); 4098-4105 the Modular Castle items, rewarded by the tutorial goal Building Block Basics
        /// (Achievements.Tutorial.cs, TASKS 6.57: Modular Thinking, the goal after it, asks the player to place them;
        /// inferred, OPEN-LEADS U-DC3).</item>
        /// <item><c>Default</c>: every character has it. The 6 pieces with no item, which include the 4 that goal 471
        /// asks to place (Courtyard, Wall Entry, Tower - Inline, Wall - Medium; 21 Mar 2016 a few to start with, more
        /// from achievements). The 8 Modular Castle pieces stopped being defaults on
        /// 2026-10-16: the wipe took the Modular Castle designs from every player so the tutorial could hand them out
        /// as intended (7 Jun 2016, P5750), now from Building Block Basics' reward. The
        /// rest come as unlocks of kind <see cref="Unlocks.StarterDesign"/> (<see cref="HasStarterPiece"/>).</item>
        /// </list>
        /// </summary>
        private static readonly (uint NameId, string File, uint TemplateId, uint ItemId, bool Default)[] StarterCastle =
        {
            (574336, "castle_courtyard", 1144, 0, true),                        //  0 Castle Courtyard
            (574256, "castle_hallway_(corner)", 1125, 4074, false),            //  1 Castle Hallway - Corner
            (574284, "castle_hallway_(long)", 1124, 0, true),                   //  2 Castle Hallway - Long
            (576962, "castle_hallway_-_medium", 1324, 4360, false),            //  3 Castle Hallway - Medium
            (574281, "castle_hallway_(short)", 1123, 4072, false),             //  4 Castle Hallway - Short
            (574255, "castle_hallway_(t-junction)", 1126, 4075, false),        //  5 Castle Hallway - T-Junction
            (576961, "castle_hallway_with_entries", 1323, 4361, false),        //  6 Castle Hallway with Side Doors
            (576199, "castle_long_room", 1310, 4085, false),                   //  7 Castle Long Room
            (574226, "castle_room_v2", 1127, 4076, false),                     //  8 Castle Room
            (574294, "castle_tower_(corner)", 1129, 4078, false),              //  9 Castle Room - Corner
            (574295, "castle_tower_(4_way)", 1130, 4077, false),               // 10 Castle Room - Four Way
            (574297, "castle_tall_tower", 1131, 4079, false),                  // 11 Castle Tower
            (574298, "castle_tall_tower_(corner)", 1132, 4080, false),         // 12 Castle Tower - Corner
            (574296, "castle_tall_tower_(inline)", 1128, 0, true),             // 13 Castle Tower - Inline
            (574251, "ramparts_wall_(corner)", 1120, 0, true),                 // 14 Castle Wall - Corner
            (574253, "castle_wall_(long)", 1122, 3706, false),                 // 15 Castle Wall - Long
            (574302, "castle_wall_(medium)", 1143, 0, true),                   // 16 Castle Wall - Medium
            (574252, "ramparts_wall_(short)", 1119, 3707, false),              // 17 Castle Wall - Short
            (574257, "ramparts_wall_(entry)", 1121, 0, true),                  // 18 Castle Wall Entry
            (574310, "modular_castle_entry", 1140, 4098, false),                // 19 Modular Castle Entry
            (574305, "modular_castle_entry_with_wall", 1135, 4099, false),      // 20 Modular Castle Entry with Wall
            (574309, "modular_castle_hall_entry", 1139, 4100, false),           // 21 Modular Castle Hall Entry
            (574304, "modular_castle_room_(long)", 1134, 4101, false),          // 22 Modular Castle Long Room
            (574307, "modular_castle_room_hallway", 1137, 4102, false),         // 23 Modular Castle Room Hallway
            (574303, "modular_castle_tower", 1133, 4104, false),                // 24 Modular Castle Tower
            (574308, "modular_castle_tall_tower_hallway", 1138, 4103, false),   // 25 Modular Castle Tower Hallway
            (574311, "modular_castle_window", 1136, 4105, false)                // 26 Modular Castle Window
        };

        /// <summary>
        /// Characters made before this day had every Starter Castle piece (the list sent all 27 until 2026-10-15); they
        /// keep them as unlocks (<see cref="GrantOldStarterPieces"/>). Compared with the character's createTime column.
        /// </summary>
        private static readonly DateTime StarterUnlockCutoff = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Unspecified);

        /// <summary>Characters already checked for the one-time grant in this run (keyed by character id).</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, byte> starterGrantChecked = new();

        /// <summary>
        /// One-time, non-destructive migration (spec-design-categories.md §5, option 1): a character with no
        /// <see cref="Unlocks.StarterDesign"/> rows yet that was made before <see cref="StarterUnlockCutoff"/> gets every
        /// piece of <see cref="StarterCastle"/> as an unlock, plus the marker row (kind 20, value 0), so nobody loses a
        /// design they had. Idempotent: any kind-20 row (the marker, or a piece learned since) means it has run; it is
        /// checked once per character per run. Owners that are not characters (a Player Design item's guid) find no
        /// character row and are skipped. Nothing is deleted: designer copies of castle pieces stored in the template
        /// table before this change (ItemUses.LearnDesign used to store them) stay.
        /// </summary>
        private static void GrantOldStarterPieces(ulong characterId)
        {
            if (!starterGrantChecked.TryAdd(characterId, 0))
                return;
            try
            {
                if (Unlocks.ValuesOfAnyone(characterId, Unlocks.StarterDesign).Count > 0)
                    return;
                DateTime? created;
                using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
                    created = context.Character.Where(c => c.Id == characterId).Select(c => (DateTime?)c.CreateTime).FirstOrDefault();
                if (created == null || created.Value >= StarterUnlockCutoff)
                    return;
                foreach (var piece in StarterCastle)
                    Unlocks.Add(characterId, Unlocks.StarterDesign, piece.TemplateId);
                Unlocks.Add(characterId, Unlocks.StarterDesign, 0);
                log.Info($"Character {characterId} (made {created.Value:yyyy-MM-dd}) keeps all {StarterCastle.Length} Starter Castle designs as unlocks.");
            }
            catch (Exception e)
            {
                starterGrantChecked.TryRemove(characterId, out _);   // try again next time
                log.Warn($"Starter Castle grant for character {characterId} failed: {e.Message}");
            }
        }

        /// <summary>
        /// Does the character have this Starter Castle piece (index into <see cref="StarterCastle"/>)? Defaults always;
        /// the rest when unlocked (kind <see cref="Unlocks.StarterDesign"/>, value = the piece's TemplateId), from its
        /// design item (ItemUses.LearnDesign) or the one-time grant to older characters.
        /// </summary>
        public static bool HasStarterPiece(ulong owner, int index)
        {
            if (index < 0 || index >= StarterCastle.Length)
                return false;
            if (StarterCastle[index].Default)
                return true;
            GrantOldStarterPieces(owner);
            return Unlocks.ValuesOfAnyone(owner, Unlocks.StarterDesign).Contains(StarterCastle[index].TemplateId);
        }

        /// <summary>The Starter Castle piece a design item unlocks (index into <see cref="StarterCastle"/>), or -1.</summary>
        public static int StarterIndexOfItem(uint itemId)
            => itemId == 0 ? -1 : Array.FindIndex(StarterCastle, s => s.ItemId == itemId);

        /// <summary>
        /// Unlocks a Starter Castle piece for the player (kind <see cref="Unlocks.StarterDesign"/>) and adds it to their
        /// Designs list (3 SaveTemplateResponse, as a save does; the 0.695 client files it under "Starter Castle" by its
        /// +0xec id). No template row is stored: the piece is the client's own .otd, served by guid "ST" + index, and
        /// designer designs take no design storage. Returns the design as sent (null if its file is missing; the unlock
        /// is kept, so it shows once the file is there).
        /// </summary>
        public static TemplateModel UnlockStarterPiece(WorldSession session, int index)
        {
            if (index < 0 || index >= StarterCastle.Length)
                return null;
            Unlocks.Add(session, Unlocks.StarterDesign, StarterCastle[index].TemplateId);
            TemplateModel piece = Starter(index, session.PlayerGuid);
            if (piece != null)
                SendAdded(session, piece);
            return piece;
        }

        /// <summary>Guids of the Starter Castle designs ("ST" + index).</summary>
        private const ulong StarterGuidBase = 0x5354_0000_0000_0000;

        private static readonly Dictionary<int, TemplateModel> starterModels = new();

        public static bool IsStarter(ulong guid) => (guid & 0xFFFF_0000_0000_0000) == StarterGuidBase;

        /// <summary>A Starter Castle design as a template row (built from its .otd once), or null.</summary>
        private static TemplateModel Starter(int index, ulong owner)
        {
            if (index < 0 || index >= StarterCastle.Length)
                return null;
            TemplateModel model;
            lock (starterModels)
            {
                if (!starterModels.TryGetValue(index, out model))
                {
                    model = null;
                    string path = System.IO.Path.Combine(VoxelTemplate.Folder, StarterCastle[index].File + ".otd");
                    try
                    {
                        if (System.IO.File.Exists(path))
                        {
                            VoxelTemplate t = VoxelTemplate.Load(path);
                            var materials = t.Materials.Count > 0 ? t.Materials.Select(m => $"{m.Material}:{m.Count}") : new[] { "0:1" };
                            model = new TemplateModel
                            {
                                Id = StarterGuidBase | (uint)index,
                                AuthorName = DesignerAuthor,
                                Name = LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(StarterCastle[index].NameId) ?? StarterCastle[index].File,
                                SizeX = t.Size.X,
                                SizeY = t.Size.Y,
                                SizeZ = t.Size.Z,
                                Materials = string.Join(";", materials),
                                Blocks = t.RawCells,
                                CreatedAt = new DateTime(2016, 6, 23, 0, 0, 0, DateTimeKind.Utc)
                            };
                        }
                    }
                    catch (Exception e)
                    {
                        log.Warn($"Starter design {path} unreadable ({e.Message}).");
                    }
                    starterModels[index] = model;
                }
            }
            if (model == null)
                return null;
            return new TemplateModel
            {
                Id = model.Id, OwnerGuid = owner, AuthorName = model.AuthorName, Name = model.Name, Tags = model.Tags,
                SizeX = model.SizeX, SizeY = model.SizeY, SizeZ = model.SizeZ, Materials = model.Materials, Blocks = model.Blocks,
                CreatedAt = model.CreatedAt
            };
        }

        /// <summary>The index in <see cref="StarterCastle"/> of the piece with this (shown) name, or -1.</summary>
        private static int StarterIndexOfName(string name)
            => string.IsNullOrEmpty(name) ? -1
                : Array.FindIndex(StarterCastle, s => LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(s.NameId) == name);

        /// <summary>The name text id of a Starter Castle design (by its name), or 0.</summary>
        private static uint StarterNameId(string name)
            => StarterIndexOfName(name) is int i and >= 0 ? StarterCastle[i].NameId : 0;

        /// <summary>
        /// The exported template id of a designer design (TemplateBasicData +0xec, see <see cref="WriteBasicData"/>):
        /// a Starter Castle piece's TemplateId (by its "ST" guid, or by name for a designer copy stored before
        /// 2026-10-16), else the grantId of the design item of the same name (<see cref="ItemUses.DesignGrantId"/>:
        /// old starter shapes 7-30, Starter Homes 599-608, Architecture Tools 1318-1319 …, all in category 3
        /// "Collected"), else 0 (the client's fallback is Collected too).
        /// </summary>
        private static uint ExportedTemplateId(TemplateModel design)
        {
            if (IsStarter(design.Id) && (int)(design.Id & 0xFFFF) is int index && index < StarterCastle.Length)
                return StarterCastle[index].TemplateId;
            if (StarterIndexOfName(design.Name) is int byName and >= 0)
                return StarterCastle[byName].TemplateId;
            return ItemUses.DesignGrantId(design.Name);
        }

        private static List<TemplateModel> Owned(ulong owner)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.Template.Where(t => t.OwnerGuid == owner).OrderBy(t => t.Id).ToList();
        }

        /// <summary>
        /// A design by guid and owner, or null. A Starter Castle guid ("ST" + index) only resolves for an owner who has
        /// that piece (<see cref="HasStarterPiece"/>): every user of a guid the client sends — paste
        /// (BuildingClipboard → <see cref="VoxelsOf"/>), voxel data, rename/delete, ExportToItem (DesignItems.Export),
        /// Player Studio listing (Commerce), design materials — goes through here, so a piece the character hasn't got
        /// can't be used by sending its guid.
        /// </summary>
        private static TemplateModel Find(ulong owner, ulong guid)
        {
            if (IsStarter(guid))
            {
                int index = (int)(guid & 0xFFFF);
                return HasStarterPiece(owner, index) ? Starter(index, owner) : null;
            }
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.Template.FirstOrDefault(t => t.Id == guid && t.OwnerGuid == owner);
        }

        /// <summary>
        /// TemplateBase sub 2: every design the player owns. Sent on zone-in. The Starter Castle pieces are the defaults
        /// plus those the character has unlocked (<see cref="HasStarterPiece"/>; spec-design-categories.md §3.4): a new
        /// character starts with the 6 default pieces (the Modular Castle pieces come from Building Block Basics). The
        /// client cannot show a design it isn't sent: its Designs window walks its own template manager (FUN_142ccb9a0),
        /// which only these packets fill; ExportedTemplateCategoryMap only picks the tab.
        /// </summary>
        public static void SendList(WorldSession session)
        {
            AdoptAccountDesigns(session);
            List<TemplateModel> designs = Owned(session.PlayerGuid);
            HashSet<ulong> free = FreeOfStorage(session.PlayerGuid, designs);
            GrantOldStarterPieces(session.PlayerGuid);
            for (int i = 0; i < StarterCastle.Length; i++)
                if (HasStarterPiece(session.PlayerGuid, i) && Starter(i, session.PlayerGuid) is TemplateModel starter)
                    designs.Add(starter);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(ListResponseSub);
            writer.Write((uint)designs.Count);
            foreach (TemplateModel design in designs)
                WriteBasicData(writer, design, !IsDesigner(design) && !free.Contains(design.Id));
            writer.Write(0ul);
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
        }

        /// <summary>
        /// Do designs follow the account (setting DesignsFollowAccount, on = live)? Live kept the designs of the Designs
        /// window with the account, not the character: "they are tied to your account, not your character. When you create
        /// a new character, the templates for that account will appear on the new character" (developer, Landmark Wipe
        /// Q&amp;A 14 Mar 2016, thread 56195; the same in the Upcoming Wipe Answers of 13 Mar 2015, thread 53904), and the
        /// designs made of a deleted character's sites waited for the player too. Designs given away as items are not the
        /// account's any more and are not kept (the same answer).
        /// </summary>
        public static bool DesignsFollowAccount
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.DesignsFollowAccount ?? true;

        /// <summary>
        /// While <see cref="DesignsFollowAccount"/> is on, at world entry: the designs still held by deleted characters of
        /// the player's account (their saved designs and the designs made of their sites, CharacterDeletion) move to this
        /// character (one indexed query per login when there are none). A design whose author was the deleted character takes this character's
        /// name, so it stays the player's own work (Personal; sellable on Player Studio). Returns how many moved.
        /// </summary>
        public static int AdoptAccountDesigns(WorldSession session)
        {
            if (!DesignsFollowAccount || session.Account == null)
                return 0;
            try
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                ulong account = session.Account.Id, me = session.PlayerGuid;
                var deleted = context.Character.Where(c => c.AccountId == account && c.DeleteTime != null && c.Id != me)
                    .Select(c => new { c.Id, Name = c.OriginalName ?? c.Name ?? "" }).ToList();
                if (deleted.Count == 0)
                    return 0;
                var ids = deleted.Select(c => c.Id).ToList();
                var names = deleted.Select(c => c.Name).Where(n => n.Length > 0).ToList();
                List<TemplateModel> rows = context.Template.Where(t => ids.Contains(t.OwnerGuid)).ToList();
                foreach (TemplateModel row in rows)
                {
                    row.OwnerGuid = me;
                    row.AuthorName = AdoptedAuthor(row.AuthorName, names, session.CharacterName);
                }
                if (rows.Count > 0)
                {
                    context.SaveChanges();
                    ownerNames.TryRemove(me, out _);
                    log.Info($"{session.CharacterName}: {rows.Count} design(s) of deleted character(s) of the account moved here.");
                }
                return rows.Count;
            }
            catch (Exception e)
            {
                log.Warn(e, $"Could not move the account's designs to {session.CharacterName}; trying again at the next login.");
                return 0;
            }
        }

        /// <summary>The author of an adopted design: this character's name when the deleted character made it, else as it was.</summary>
        internal static string AdoptedAuthor(string author, IEnumerable<string> deletedNames, string newName)
            => !string.IsNullOrEmpty(author) && !string.IsNullOrEmpty(newName)
               && deletedNames.Any(n => string.Equals(n, author, StringComparison.OrdinalIgnoreCase)) ? newName : author;

        /// <summary>Solid voxels in a stored material list ("material:count;...").</summary>
        public static long MaterialCount(string materials)
            => Materials(new TemplateModel { Materials = materials ?? "" }).Sum(m => (long)m.Count);

        /// <summary>A design's materials and voxel counts (also for <see cref="DesignMaterials"/>).</summary>
        public static IEnumerable<(ushort Material, uint Count)> MaterialsOf(TemplateModel design) => Materials(design);

        private static IEnumerable<(ushort Material, uint Count)> Materials(TemplateModel design)
        {
            foreach (string entry in design.Materials.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = entry.Split(':');
                if (f.Length == 2 && ushort.TryParse(f[0], out ushort material) && uint.TryParse(f[1], out uint n))
                    yield return (material, n);
            }
        }

        /// <summary>
        /// Re-sends one of the player's designs as UpdateTemplateResponse (0xC), e.g. when it goes on or off the
        /// marketplace and changes category in the Designs window.
        /// </summary>
        public static void SendUpdated(WorldSession session, ulong guid)
        {
            TemplateModel design = Find(session.PlayerGuid, guid);
            if (design == null)
                return;
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(UpdateResponseSub);
            WriteBasicData(writer, design);
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, string> ownerNames = new();

        /// <summary>
        /// Is the design its holder's own work? Designs another player made (bought in Player Studio, or a Player Design
        /// item from someone else) carry their author's name and go to the Designs window's Collected category, as
        /// live; the game's own designs stay where IsDesignerTemplate puts them.
        /// </summary>
        /// <summary>
        /// May the holder list this design on Player Studio? Only their own work (the client's own test: ProductAddItem,
        /// 0.695 FUN_143263990, refuses a design whose own-design flag +0xf4 is false; the Player Studio button is off
        /// for purchased templates, Hotfix Notes 20 Jun 2014) [P], and not one of the game's own designs [I].
        /// </summary>
        public static bool IsSellable(TemplateModel design) => design != null && !IsDesigner(design) && IsOwnWork(design);

        private static bool IsOwnWork(TemplateModel design)
        {
            if (string.IsNullOrEmpty(design.AuthorName) || IsDesigner(design))
                return true;
            string owner = ownerNames.GetOrAdd(design.OwnerGuid, id =>
                DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(new[] { id }).GetValueOrDefault(id) ?? "");
            return owner.Length == 0 || string.Equals(owner, design.AuthorName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// TemplateBasicData (0.695 reader FUN_1430ff530). 0.695 dropped the PropData list from it (0.589 had it after the
        /// materials; it is written in the voxel data reply instead, <see cref="SendVoxelData"/>); the rest is the same
        /// (trace/0695/packets-695.md §B4).
        /// <para>0.695 Designs window category (PaletteCollectionTemplates, FUN_143754540 / FUN_143755d60,
        /// part_0120.c:8670-8716 and 9827-9841): IsOnMarketplace (+0x48) → Player Studio; else IsDesignerTemplate (+0x50)
        /// → the first ExportedTemplateCategory whose ExportedTemplateCategoryMap set holds the u32 at <b>+0xec</b> (the
        /// third u32 after the "counts against storage" bool; FUN_143755020), else Collected; else the own-design bool
        /// (+0xf4) → Personal, false → Collected. Empty categories get no tab (FUN_143755ca0). Before 2026-10-16 the server
        /// sent 0 at +0xec, so every designer design fell back to Collected and "Starter Castle" never showed
        /// (trace/0695/spec-design-categories.md).</para>
        /// </summary>
        private static void WriteBasicData(GamePacketWriter writer, TemplateModel design, bool? countsAgainstStorage = null)
        {
            ulong created = (ulong)new DateTimeOffset(DateTime.SpecifyKind(design.CreatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
            writer.Write(design.Id);
            writer.Write(design.Name);
            writer.Write(0ul);
            writer.Write(design.OwnerGuid);         // AuthorPlayerGuid
            writer.Write(created);                  // DateCreated
            writer.Write(created);                  // DateAcquired
            writer.Write(WorldChannelServices.Commerce.IsOnMarketplace(design.Id) ? 1u : 0u);   // IsOnMarketplace: the Player Studio category
            writer.Write(IsDesigner(design) ? 1u : 0u);   // IsDesignerTemplate: category by the +0xec id below, free of storage
            writer.Write(design.SizeX);
            writer.Write(design.SizeY);
            writer.Write(design.SizeZ);
            var materials = Materials(design).ToList();
            writer.Write((uint)materials.Count);
            foreach ((ushort material, uint n) in materials)
            {
                writer.WriteUShort(material);
                writer.Write(n);
            }
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(false);                    // IsDefault
            writer.Write(design.Tags);
            writer.Write(countsAgainstStorage ?? CountsAgainstStorage(design));   // counts against the storage limit ("free" column)
            // A designer template's shown name is this text id (0.695 +0xe4, 0.589 +0xfc; 0 shows "Daybreak Template
            // (Invalid Name)", tooltip part_0086.c:12124): the piece's or the design item's own name.
            writer.Write(!IsDesigner(design) ? 0u : StarterNameId(design.Name) is uint starterName and not 0 ? starterName : ItemUses.DesignNameId(design.Name));
            writer.Write(0u);                       // +0xe8, meaning unknown (U-DC1)
            // +0xec: the exported template id, read only when IsDesignerTemplate != 0; its ExportedTemplateCategoryMap
            // row picks the Designs tab (1119-1324 → "Starter Castle", others → "Collected"). The same u32 slot in 0.589.
            writer.Write(IsDesigner(design) ? ExportedTemplateId(design) : 0u);
            writer.Write(IsOwnWork(design));        // the player's own design: Personal category (false: Collected)
            writer.Write(design.AuthorName);
            // TemplateValue (+0x110): the design's own Player Studio minimum price; the submission window's ProductAddItem
            // (0.695 FUN_143263990) raises the product's minimum to it (StudioRules.TemplateValue: 1 % of the voxels).
            writer.Write(IsDesigner(design) ? 0u : StudioRules.TemplateValue(MaterialCount(design.Materials)));
            writer.Write(false);                    // ContestWinner
            writer.Write(0u);
        }

        /// <summary>
        /// TemplateBasicData of a design by its owner and guid (a placed Player Studio design's details, 0xF0/0x13,
        /// <see cref="TemplateInstances.SendDetails"/>); only the guid when the owner no longer has it.
        /// </summary>
        public static void WriteBasicDataOf(GamePacketWriter writer, ulong owner, ulong guid)
            => WriteBasicData(writer, Find(owner, guid) ?? new TemplateModel { Id = guid, Name = "", CreatedAt = DateTime.UtcNow });

        /// <summary>PropData list (PropCount; the client sends it back when the design is placed, FUN_1430b9d50).</summary>
        private static void WritePropList(GamePacketWriter writer, TemplateModel design)
        {
            List<Props.DesignProp> props = design.Id != 0 ? PropsOf(design.Id) : new List<Props.DesignProp>();
            writer.Write((uint)props.Count);
            foreach (Props.DesignProp prop in props)
            {
                var bytes = new List<byte>();
                Building.WritePropData(bytes, prop);
                writer.WriteBytes(bytes.ToArray());
            }
        }

        /// <summary>
        /// Saves a design from a selection request with action 3 (called by Building): <paramref name="blocks"/> is the
        /// request's block list as sent, <paramref name="materials"/> its solid voxels per material.
        /// </summary>
        public static void Save(WorldSession session, string name, string tags, uint sizeX, uint sizeY, uint sizeZ,
            byte[] blocks, IDictionary<ushort, uint> materials, List<Props.DesignProp> props = null)
        {
            Store(session, name, tags, sizeX, sizeY, sizeZ, blocks, materials, session.CharacterName ?? "", props);
        }

        /// <summary>A design's props, kept beside the design (TemplateProps/&lt;design id&gt;.json).</summary>
        private static string PropsFile(ulong design) => System.IO.Path.Combine(AppContext.BaseDirectory, "TemplateProps", design + ".json");

        private sealed record StoredProp(uint PropId, float[] Offset, float[] Rotation, float[] Scale, string Tint, string Name, string Text);

        /// <summary>The props saved with a design (offsets from its box's corner), empty if none.</summary>
        public static List<Props.DesignProp> PropsOf(ulong design)
        {
            string path = PropsFile(design);
            if (!System.IO.File.Exists(path))
                return new List<Props.DesignProp>();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<StoredProp>>(System.IO.File.ReadAllText(path))
                    .Select(p => new Props.DesignProp(p.PropId, new System.Numerics.Vector3(p.Offset[0], p.Offset[1], p.Offset[2]),
                        new System.Numerics.Vector4(p.Rotation[0], p.Rotation[1], p.Rotation[2], p.Rotation[3]),
                        new System.Numerics.Vector4(p.Scale[0], p.Scale[1], p.Scale[2], p.Scale[3]), p.Tint, p.Name, p.Text))
                    .ToList();
            }
            catch (Exception e)
            {
                log.Warn($"Unreadable props of design {design}: {e.Message}");
                return new List<Props.DesignProp>();
            }
        }

        private static void SaveProps(ulong design, List<Props.DesignProp> props)
        {
            if (props == null || props.Count == 0)
                return;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PropsFile(design)));
            System.IO.File.WriteAllText(PropsFile(design), System.Text.Json.JsonSerializer.Serialize(props.Select(p => new StoredProp(p.PropId,
                new[] { p.Offset.X, p.Offset.Y, p.Offset.Z }, new[] { p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W },
                new[] { p.Scale.X, p.Scale.Y, p.Scale.Z, p.Scale.W }, p.Tint, p.Name, p.Text))));
        }

        /// <summary>
        /// Author of the designs the game itself hands out (design items, .otd templates): sent with IsDesignerTemplate
        /// set, so they don't count against the storage limit. The 0.695 client picks their tab by the exported template
        /// id at TemplateBasicData +0xec (<see cref="WriteBasicData"/>): "Starter Castle" for the castle pieces, else
        /// "Collected". (0.589's window, FUN_1436966c0 and the list builder after it, filed every designer template under
        /// Template.Category.Daybreak.) A space keeps it apart from any character name.
        /// </summary>
        public const string DesignerAuthor = "Landmark Team";

        private static bool IsDesigner(TemplateModel design) => design.AuthorName is DesignerAuthor or "Landmark";

        /// <summary>
        /// Adds a prefab from the client's templates (a "Design:" item such as the old starter shapes or Starter Homes) to
        /// the player's designs, as if saved: the template's cell list is a design's block list as it stands. Starter
        /// Castle pieces are not stored this way any more (<see cref="UnlockStarterPiece"/>).
        /// </summary>
        public static bool AddFromTemplate(WorldSession session, string name, VoxelTemplate template)
        {
            var materials = template.Materials.ToDictionary(m => m.Material, m => m.Count);
            if (materials.Count == 0)
                materials[0] = 1;   // the map only feeds the window's voxel count; an empty one would refuse the save
            return Store(session, name, "", template.Size.X, template.Size.Y, template.Size.Z, template.RawCells, materials, DesignerAuthor);
        }

        private static bool Store(WorldSession session, string name, string tags, uint sizeX, uint sizeY, uint sizeZ,
            byte[] blocks, IDictionary<ushort, uint> materials, string author, List<Props.DesignProp> props = null)
        {
            name = (name ?? "").Trim();
            if (name.Length > 64)
                name = name[..64];
            tags ??= "";
            if (tags.Length > 500)
                tags = tags[..500];
            bool ok = materials.Count > 0 && name.Length > 0
                && (author == DesignerAuthor || HasRoom(session, materials.Values.Sum(n => (long)n)));
            TemplateModel design = null;
            if (ok)
            {
                design = new TemplateModel
                {
                    OwnerGuid = session.PlayerGuid,
                    AuthorName = author,
                    Name = name,
                    Tags = tags ?? "",
                    SizeX = sizeX,
                    SizeY = sizeY,
                    SizeZ = sizeZ,
                    Materials = string.Join(";", materials.Select(m => $"{m.Key}:{m.Value}")),
                    Blocks = blocks,
                    CreatedAt = DateTime.UtcNow
                };
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                context.Template.Add(design);
                context.SaveChanges();
                SaveProps(design.Id, props);
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(SaveResponseSub);
            writer.Write(ok);
            WriteBasicData(writer, design ?? new TemplateModel { Name = name });
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
            log.Info(ok ? $"{session.CharacterName} saved design {design.Id} \"{name}\" ({sizeX}×{sizeY}×{sizeZ})."
                : $"{session.CharacterName} could not save design \"{name}\" (empty, unnamed or storage full).");
            return ok;
        }

        /// <summary>
        /// The stored voxels of one of the player's designs, for a paste (null if it isn't theirs): every block of its list
        /// (u32 n × {u64 cell, u64, u32 type, u32 length, packed changed mask + format-2 block, u32 more for types 2/3}),
        /// keyed by its cell relative to the lowest one. A design saved in game is one block; a prefab spans many.
        /// </summary>
        /// <summary>
        /// A design's block list with its cells counted from the design's lowest cell (0, 0, 0), as the client needs them.
        /// Its paste clipboard is sized from the design's dimensions and puts each cell's voxels at cell × 40 + voxel with
        /// no offset (FUN_143ad6ff0, called from FUN_1430671e0 when a design is picked in the Designs tab). Game designs
        /// and the client's own saves are already 0-based, but site designs saved before 2026-10-15 kept world cell ids
        /// (159874, 185, 159790...), so the client wrote far outside its buffer and crashed (dump 2026-10-14 08:45:
        /// write fault in FUN_1429dd2f0 under FUN_143ad6ff0).
        /// </summary>
        public static byte[] Rebased(byte[] blocks)
        {
            if (blocks.Length < 4)
                return blocks;
            uint count = BitConverter.ToUInt32(blocks, 0);
            var offsets = new List<int>();
            uint minX = uint.MaxValue, minY = uint.MaxValue, minZ = uint.MaxValue;
            int p = 4;
            for (uint i = 0; i < count && p + 24 <= blocks.Length; i++)
            {
                (_, uint x, uint y, uint z) = TerrainStore.Decode(BitConverter.ToUInt64(blocks, p));
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); minZ = Math.Min(minZ, z);
                offsets.Add(p);
                uint type = BitConverter.ToUInt32(blocks, p + 16);
                p += 24 + BitConverter.ToInt32(blocks, p + 20) + (type is 2 or 3 ? 4 : 0);
            }
            if (offsets.Count == 0 || (minX == 0 && minY == 0 && minZ == 0))
                return blocks;
            byte[] copy = (byte[])blocks.Clone();
            foreach (int at in offsets)
                for (int k = 0; k < 2; k++)   // both ids of the entry (cell, then the same cell again)
                {
                    (uint level, uint x, uint y, uint z) = TerrainStore.Decode(BitConverter.ToUInt64(copy, at + k * 8));
                    BitConverter.GetBytes(TerrainStore.CellId(level, x - minX, y - minY, z - minZ)).CopyTo(copy, at + k * 8);
                }
            return copy;
        }

        public static (Dictionary<(int X, int Y, int Z), BlockData> Cells, uint SizeX, uint SizeY, uint SizeZ)? VoxelsOf(WorldSession session, ulong guid)
        {
            TemplateModel design = Find(session.PlayerGuid, guid);
            if (design == null || design.Blocks.Length < 4)
                return null;
            var read = new List<(uint X, uint Y, uint Z, BlockData Voxels)>();
            int p = 4;
            uint count = BitConverter.ToUInt32(design.Blocks, 0);
            for (uint i = 0; i < count && p + 24 <= design.Blocks.Length; i++)
            {
                ulong id = BitConverter.ToUInt64(design.Blocks, p);
                uint type = BitConverter.ToUInt32(design.Blocks, p + 16);
                int length = BitConverter.ToInt32(design.Blocks, p + 20);
                byte[] raw = TerrainStore.Unpack(design.Blocks, p + 24, length);
                p += 24 + length + (type is 2 or 3 ? 4 : 0);
                (_, uint x, uint y, uint z) = TerrainStore.Decode(id);
                read.Add((x, y, z, raw.Length > BlockData.Count / 8 ? BlockData.Deserialize(raw, BlockData.Count / 8) : new BlockData()));
            }
            if (read.Count == 0)
                return null;
            uint minX = read.Min(r => r.X), minY = read.Min(r => r.Y), minZ = read.Min(r => r.Z);
            var cells = read.ToDictionary(r => ((int)(r.X - minX), (int)(r.Y - minY), (int)(r.Z - minZ)), r => r.Voxels);
            return (cells, design.SizeX, design.SizeY, design.SizeZ);
        }

        public static void Handle(WorldSession session, byte sub, byte[] body)
        {
            switch (sub)
            {
                case ListRequestSub:
                    // ClientListRequest (Lua GetList, FUN_143069140): sent only while the client has no list yet (+0xf8)
                    // and none asked for (+0xf9). The list is pushed at world entry, so this is the fallback.
                    SendList(session);
                    break;
                case VoxelDataRequestSub when body.Length >= 16:
                    SendVoxelData(session, BitConverter.ToUInt64(body, 0), BitConverter.ToUInt64(body, 8),
                        body.Length > 16 && body[16] != 0);
                    break;
                case DeleteRequestSub:
                    Delete(session, body);
                    break;
                case RenameRequestSub:
                    Rename(session, body);
                    break;
                case TemplateInstances.DetailsRequestSub when body.Length >= 8:
                    // Voxel Vision asks about a placed Player Studio design it found in a site's cells (FUN_1431138e0).
                    TemplateInstances.SendDetails(session, BitConverter.ToUInt64(body, 0));
                    break;
                case ExportToItemSub when body.Length >= 8:
                    DesignItems.Export(session, BitConverter.ToUInt64(body, 0));
                    break;
                case DesignMaterials.DataRequestSub when body.Length >= 8:
                    DesignMaterials.SendOffer(session, BitConverter.ToUInt64(body, 0));
                    break;
                case DesignMaterials.PurchaseSub when body.Length >= 12:
                    DesignMaterials.Purchase(session, body);
                    break;
                default:
                    Network.OpcodeStats.Unanswered("Template 0xF0", sub);
                    log.Debug($"Template packet 0x{sub:X2} from {session.CharacterName}: {BitConverter.ToString(body, 0, Math.Min(body.Length, 64))}");
                    break;
            }
        }

        private static void SendVoxelData(WorldSession session, ulong first, ulong guid, bool images)
        {
            TemplateModel design = Find(session.PlayerGuid, guid);
            if (design == null)
            {
                log.Debug($"{session.CharacterName} asked for the voxels of unknown design {guid}.");
                return;
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(VoxelDataResponseSub);
            writer.Write(first);
            WriteBasicData(writer, design);
            writer.WriteBytes(Rebased(design.Blocks));
            // 0.695 (FUN_1430fff20): the design's props follow the cell list.
            WritePropList(writer, design);
            // Screenshots only when asked for (tens of KB each): large/small pairs in index order (TEMPLATE_<n>).
            List<byte[]> shots = images ? Images(design.Id) : new List<byte[]>();
            writer.Write((uint)shots.Count);
            foreach (byte[] shot in shots)
            {
                writer.Write((uint)shot.Length);
                writer.WriteBytes(shot);
            }
            writer.Write(images);
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
        }

        /// <summary>Saved design screenshots: TemplateImages/(design)_(index)_(large|small).jpg.</summary>
        private static string ImageFolder => System.IO.Path.Combine(AppContext.BaseDirectory, "TemplateImages");

        private const int MaxImageBytes = 512 * 1024, MaxImages = 4;

        private static IEnumerable<string> ImageFiles(ulong design)
            => System.IO.Directory.Exists(ImageFolder)
                ? System.IO.Directory.GetFiles(ImageFolder, design + "_*.jpg")
                : Array.Empty<string>();

        /// <summary>A copy of a design keeps its screenshots (Player Studio copies).</summary>
        public static void CopyImages(ulong from, ulong to)
        {
            foreach (string file in ImageFiles(from).ToList())
            {
                string name = System.IO.Path.GetFileName(file);
                System.IO.File.Copy(file, System.IO.Path.Combine(ImageFolder, to + name[from.ToString().Length..]), true);
            }
        }

        /// <summary>Does the design have a first screenshot?</summary>
        public static bool HasImage(ulong design) => Image(design, 0, "small") != null;

        /// <summary>
        /// The web path of a design's screenshot as the client builds it (FUN_143ad69a0): the guid as 24 digits with a
        /// "/" after every 3.
        /// </summary>
        public static string ImagePath(ulong design, string size = "small")
        {
            string digits = design.ToString("D24");
            return $"/uploads/lm/images/templates/{size}/" + string.Join("/", Enumerable.Range(0, 8).Select(i => digits.Substring(i * 3, 3))) + ".jpg";
        }

        /// <summary>One stored screenshot (null if there is none), for the web server.</summary>
        public static byte[] Image(ulong design, int index, string size)
        {
            if (index < 0 || index >= MaxImages || (size != "small" && size != "large"))
                return null;
            string file = System.IO.Path.Combine(ImageFolder, design + "_" + index + "_" + size + ".jpg");
            return System.IO.File.Exists(file) ? System.IO.File.ReadAllBytes(file) : null;
        }

        /// <summary>The design's screenshots as large/small pairs by index.</summary>
        private static List<byte[]> Images(ulong design)
        {
            var shots = new List<byte[]>();
            for (int index = 0; index < MaxImages; index++)
            {
                string large = System.IO.Path.Combine(ImageFolder, design + "_" + index + "_large.jpg");
                string small = System.IO.Path.Combine(ImageFolder, design + "_" + index + "_small.jpg");
                if (!System.IO.File.Exists(large) || !System.IO.File.Exists(small))
                    break;
                shots.Add(System.IO.File.ReadAllBytes(large));
                shots.Add(System.IO.File.ReadAllBytes(small));
            }
            return shots;
        }

        /// <summary>
        /// MarketplaceImageBase 0x107 from the client: sub 1 UploadRequest is stored (<see cref="StoreImage"/>); the
        /// client sends no other sub (coverage-695.md "0x107": only UploadRequest) and expects no answer.
        /// </summary>
        [Network.Message.ZoneMessageHandler(Network.Message.ZoneMessageOpcode.MarketplaceImageBase)]
        public static void HandleImage(WorldSession session, Network.Message.Model.MarketplaceImageRequest request)
        {
            if (request.SubOpcode == 1)
                StoreImage(session, request.Body);
            else
                Network.OpcodeStats.Unanswered("MarketplaceImage 0x107", request.SubOpcode);
        }

        /// <summary>
        /// MarketplaceImageBase 0x107 sub 1 UploadRequest (built in FUN_14314b380 after a save, FUN_143062ce0):
        /// {u64 design, u64 design, string folder ("images/templates"), string size ("large" or "small"), u32, u32 index,
        /// u32 length + JPEG}. Kept for the design's owner (JPEG only, size-capped).
        /// </summary>
        public static void StoreImage(WorldSession session, byte[] body)
        {
            try
            {
                var reader = new GamePacketReader(body);
                ulong guid = reader.ReadULong();
                ulong second = reader.ReadULong();
                string folder = reader.ReadString();
                string size = reader.ReadString();
                reader.ReadUInt();
                uint index = reader.ReadUInt();
                uint length = reader.ReadUInt();
                if (string.IsNullOrEmpty(folder) || folder.StartsWith("images/claims"))
                {
                    // A build site's screenshot (Site Management, Claims.TakeScreenshot): {u64 image id ((character << 32)
                    // | time), u64 site, "" folder, size, u32, u32 index, JPEG}.
                    SiteScreenshots.Store(session, second, size, index, length <= 1024 * 1024 ? reader.ReadBytes(length) : Array.Empty<byte>());
                    return;
                }
                byte[] jpeg = length <= MaxImageBytes ? reader.ReadBytes(length) : Array.Empty<byte>();
                if (folder != "images/templates" || (size != "large" && size != "small") || index >= MaxImages
                    || jpeg.Length != length || length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8
                    || Find(session.PlayerGuid, guid) == null)
                {
                    log.Debug($"{session.CharacterName}: refused a {size} image ({length} bytes) for design {guid} ({second}, folder '{folder}', index {index}).");
                    return;
                }
                System.IO.Directory.CreateDirectory(ImageFolder);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(ImageFolder, guid + "_" + index + "_" + size + ".jpg"), jpeg);
                log.Debug($"{session.CharacterName} uploaded the {size} screenshot {index} of design {guid} ({length} bytes).");
            }
            catch (Exception e)
            {
                log.Debug($"Unreadable image upload from {session.CharacterName}: {e.Message}");
            }
        }

        /// <summary>The first u64 in a request body that names one of the player's designs.</summary>
        private static TemplateModel FindIn(WorldSession session, byte[] body, out int offset)
        {
            for (offset = 0; offset + 8 <= body.Length && offset <= 16; offset += 8)
            {
                TemplateModel design = Find(session.PlayerGuid, BitConverter.ToUInt64(body, offset));
                if (design != null)
                    return design;
            }
            return null;
        }

        private static void Delete(WorldSession session, byte[] body)
        {
            TemplateModel design = FindIn(session, body, out _);
            if (design != null && IsStarter(design.Id))
            {
                // The Starter Castle designs come with the game: "cannot delete".
                var refusal = new List<byte>();
                var w = new GamePacketWriter(refusal);
                w.Write(DeleteResponseSub);
                w.Write(0ul);
                w.Write(design.Id);
                w.Write(false);
                w.Write(true);
                session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, refusal.ToArray());
                return;
            }
            if (design != null)
            {
                foreach (string file in ImageFiles(design.Id))
                    System.IO.File.Delete(file);
                using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
                context.Template.Remove(context.Template.First(t => t.Id == design.Id));
                context.SaveChanges();
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(DeleteResponseSub);
            writer.Write(0ul);
            writer.Write(design?.Id ?? (body.Length >= 8 ? BitConverter.ToUInt64(body, body.Length >= 16 ? 8 : 0) : 0ul));
            writer.Write(design != null);
            writer.Write(design == null);
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
            log.Info(design != null ? $"{session.CharacterName} deleted design {design.Id}." : $"{session.CharacterName}: delete of unknown design.");
        }

        private static void Rename(WorldSession session, byte[] body)
        {
            TemplateModel design = FindIn(session, body, out int offset);
            string name = null;
            if (design != null && offset + 12 <= body.Length)
            {
                int length = BitConverter.ToInt32(body, offset + 8);
                if (length >= 0 && offset + 12 + length <= body.Length)
                    name = System.Text.Encoding.UTF8.GetString(body, offset + 12, length).Trim();
            }
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            if (design == null || string.IsNullOrEmpty(name) || name.Length > 64 || IsDesigner(design))
            {
                writer.Write(RenameErrorSub);
                writer.Write(design != null && IsDesigner(design) ? 3u : 1u);   // 3: immutable (a game design)
                writer.Write(design?.Id ?? 0ul);
                session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
                log.Debug($"{session.CharacterName}: rename refused: {BitConverter.ToString(body)}");
                return;
            }
            using (var context = DatabaseManager.Instance.CharacterDatabase.NewContext())
            {
                TemplateModel row = context.Template.First(t => t.Id == design.Id);
                row.Name = name;
                context.SaveChanges();
                design = row;
            }
            writer.Write(UpdateResponseSub);
            WriteBasicData(writer, design);
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
            log.Info($"{session.CharacterName} renamed design {design.Id} to \"{name}\".");
        }
    }
}
