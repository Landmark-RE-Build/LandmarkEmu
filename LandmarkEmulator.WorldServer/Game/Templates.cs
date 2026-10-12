using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Terrain;
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

        /// <summary>Does the player have room for one more design (game designs don't count)?</summary>
        public static bool HasRoom(WorldSession session) => Owned(session.PlayerGuid).Count(t => !IsDesigner(t)) < Limit(session);

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

        public const byte ListResponseSub = 2, SaveResponseSub = 3, VoxelDataRequestSub = 4, VoxelDataResponseSub = 5,
            DeleteRequestSub = 6, DeleteResponseSub = 7, RenameRequestSub = 0xA, RenameErrorSub = 0xB, UpdateResponseSub = 0xC;

        /// <summary>Designs a player may keep.</summary>
        public const int StorageLimit = 100;

        /// <summary>Designs a player may keep: <see cref="StorageLimit"/> per 6,000,000 voxels of design storage.</summary>
        public static int Limit(WorldSession session)
            => (int)(StorageLimit * (long)ItemUses.DesignStorageOf(session.PlayerGuid) / ItemUses.DefaultDesignStorage);

        // ---- Starter Castle ------------------------------------------------------------------------------------------

        /// <summary>
        /// The Starter Castle designs every player has (client ExportedTemplateCategoryMap: category 1 "Starter Castle",
        /// 28 templates; their names are texts 574226-574336/576961-576962; the Getting Started goal "Your Home Is Your
        /// Castle" asks to place Castle Courtyard, Castle Wall Entry, Castle Tower - Inline and Castle Wall - Medium,
        /// none of which is an item). Served from the client's own .otd files, not stored per character. Name → file
        /// by name, and by size where the names differ: walls are 12 × 50 (Short 24 = ramparts short, Medium 72, Long
        /// 168, plus the rampart corner and entry), rooms are the 86 m-high family (room v2, the 4-way and corner
        /// "tower" files), towers the 124 m-high family (the only inline piece is a tall tower). Modular Castle Window
        /// with Wall has no file (no 26 × 50 × 40 window).
        /// </summary>
        private static readonly (uint NameId, string File)[] StarterCastle =
        {
            (574336, "castle_courtyard"), (574256, "castle_hallway_(corner)"), (574284, "castle_hallway_(long)"),
            (576962, "castle_hallway_-_medium"), (574281, "castle_hallway_(short)"), (574255, "castle_hallway_(t-junction)"),
            (576961, "castle_hallway_with_entries"), (576199, "castle_long_room"), (574226, "castle_room_v2"),
            (574294, "castle_tower_(corner)"), (574295, "castle_tower_(4_way)"), (574297, "castle_tall_tower"),
            (574298, "castle_tall_tower_(corner)"), (574296, "castle_tall_tower_(inline)"), (574251, "ramparts_wall_(corner)"),
            (574253, "castle_wall_(long)"), (574302, "castle_wall_(medium)"), (574252, "ramparts_wall_(short)"),
            (574257, "ramparts_wall_(entry)"), (574310, "modular_castle_entry"), (574305, "modular_castle_entry_with_wall"),
            (574309, "modular_castle_hall_entry"), (574304, "modular_castle_room_(long)"), (574307, "modular_castle_room_hallway"),
            (574303, "modular_castle_tower"), (574308, "modular_castle_tall_tower_hallway"), (574311, "modular_castle_window")
        };

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

        /// <summary>The name text id of a Starter Castle design (by its name), or 0.</summary>
        private static uint StarterNameId(string name)
            => StarterCastle.Select(s => s.NameId).FirstOrDefault(id => LandmarkEmulator.Shared.GameTable.Text.TextManager.Instance.GetTextForId(id) == name);

        private static List<TemplateModel> Owned(ulong owner)
        {
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.Template.Where(t => t.OwnerGuid == owner).OrderBy(t => t.Id).ToList();
        }

        private static TemplateModel Find(ulong owner, ulong guid)
        {
            if (IsStarter(guid))
                return Starter((int)(guid & 0xFFFF), owner);
            using var context = DatabaseManager.Instance.CharacterDatabase.NewContext();
            return context.Template.FirstOrDefault(t => t.Id == guid && t.OwnerGuid == owner);
        }

        /// <summary>TemplateBase sub 2: every design the player owns. Sent on zone-in.</summary>
        public static void SendList(WorldSession session)
        {
            List<TemplateModel> designs = Owned(session.PlayerGuid);
            for (int i = 0; i < StarterCastle.Length; i++)
                if (Starter(i, session.PlayerGuid) is TemplateModel starter)
                    designs.Add(starter);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(ListResponseSub);
            writer.Write((uint)designs.Count);
            foreach (TemplateModel design in designs)
                WriteBasicData(writer, design);
            writer.Write(0ul);
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase, output.ToArray());
        }

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

        private static void WriteBasicData(GamePacketWriter writer, TemplateModel design)
        {
            ulong created = (ulong)new DateTimeOffset(DateTime.SpecifyKind(design.CreatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
            writer.Write(design.Id);
            writer.Write(design.Name);
            writer.Write(0ul);
            writer.Write(design.OwnerGuid);         // AuthorPlayerGuid
            writer.Write(created);                  // DateCreated
            writer.Write(created);                  // DateAcquired
            writer.Write(Commerce.IsOnMarketplace(design.Id) ? 1u : 0u);   // IsOnMarketplace: the Player Studio category
            writer.Write(IsDesigner(design) ? 1u : 0u);   // IsDesignerTemplate: the "Starter Castle" category, free of storage
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
            writer.Write(0u);                       // props
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(false);                    // IsDefault
            writer.Write(design.Tags);
            writer.Write(!IsDesigner(design));      // counts against the storage limit
            // A designer template's shown name is this text id (+0xfc; 0 shows "Daybreak Template (Invalid Name)"):
            // the design item's own name.
            writer.Write(!IsDesigner(design) ? 0u : StarterNameId(design.Name) is uint starterName and not 0 ? starterName : ItemUses.DesignNameId(design.Name));
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(true);                     // the player's own design: Personal category (false: Collected)
            writer.Write(design.AuthorName);
            writer.Write(0u);                       // TemplateValue
            writer.Write(false);                    // ContestWinner
            writer.Write(0u);
        }

        /// <summary>
        /// Saves a design from a selection request with action 3 (called by Building): <paramref name="blocks"/> is the
        /// request's block list as sent, <paramref name="materials"/> its solid voxels per material.
        /// </summary>
        public static void Save(WorldSession session, string name, string tags, uint sizeX, uint sizeY, uint sizeZ,
            byte[] blocks, IDictionary<ushort, uint> materials)
        {
            Store(session, name, tags, sizeX, sizeY, sizeZ, blocks, materials, session.CharacterName ?? "");
        }

        /// <summary>
        /// Adds a prefab from the client's templates (a "Design:" item such as the castle pieces) to the player's designs,
        /// as if saved: the template's cell list is a design's block list as it stands.
        /// </summary>
        /// <summary>
        /// Author of the designs the game itself hands out (design items, .otd templates). The client files these under
        /// "Starter Castle" (Template.Category.Daybreak): its Designs window (FUN_1436966c0 and the list builder after it)
        /// puts a design in Player Studio when IsOnMarketplace is set, else in Starter Castle when IsDesignerTemplate is
        /// set, else in Personal or Collected; such designs don't count against the storage limit. A space keeps it apart
        /// from any character name.
        /// </summary>
        public const string DesignerAuthor = "Landmark Team";

        private static bool IsDesigner(TemplateModel design) => design.AuthorName is DesignerAuthor or "Landmark";

        public static bool AddFromTemplate(WorldSession session, string name, VoxelTemplate template)
        {
            var materials = template.Materials.ToDictionary(m => m.Material, m => m.Count);
            if (materials.Count == 0)
                materials[0] = 1;   // the map only feeds the window's voxel count; an empty one would refuse the save
            return Store(session, name, "", template.Size.X, template.Size.Y, template.Size.Z, template.RawCells, materials, DesignerAuthor);
        }

        private static bool Store(WorldSession session, string name, string tags, uint sizeX, uint sizeY, uint sizeZ,
            byte[] blocks, IDictionary<ushort, uint> materials, string author)
        {
            name = (name ?? "").Trim();
            if (name.Length > 64)
                name = name[..64];
            tags ??= "";
            if (tags.Length > 500)
                tags = tags[..500];
            bool ok = materials.Count > 0 && name.Length > 0 && Owned(session.PlayerGuid).Count(t => !IsDesigner(t)) < Limit(session);
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
                case ExportToItemSub when body.Length >= 8:
                    DesignItems.Export(session, BitConverter.ToUInt64(body, 0));
                    break;
                default:
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
            writer.WriteBytes(design.Blocks);
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
