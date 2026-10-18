using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Landmarks of Landmark (TASKS 3.20): the winning builds of the Landmarks of Landmark building competitions, placed by
    /// the developers beside the Spire of every landscape on every world (Week End Wrap Up 5 Aug 2016: the Landmarks of
    /// Landmark were all placed by the spires; wiki "Landmarks of Landmark": usually close to the spires). Which build
    /// stood where is the developers' list of 3 Aug 2016
    /// (<see cref="table"/>, LandmarksOfLandmark.Table.cs). Each one stood on a claim of its own (P1385, 10 Jul 2014),
    /// marked as a landmark claim with the blue header banner, and only staff could edit it (Domino, forum 57799 #6).
    /// <para>
    /// Built like the Spire and the outposts: the client's own templates (CommonData/Voxel/templates
    /// landmarks_of_landmark_2016_winner_-_*.otd, lm_*.otd for 2014) are stamped into the terrain once per landscape
    /// (marker TerrainEdits/landmarks_of_landmark.txt: landscape, world, template, corner voxel, size), cells kept from the
    /// heal, and the template's props are streamed to players nearby. The site is a server-owned claim record
    /// (<see cref="Claims.AddServerSite"/>) whose admin template guid (record +0x158 → claim entry +0x138) is set, so the
    /// client's walk-on window shows its contest panel (ContestWinnerContent) with the build's name, the competition and
    /// its creators (record strings → ClaimDialogs.WinnerWalkOnText / AdminClaimContributors, client FUN_142dc7540 and
    /// FUN_142db82e0).
    /// </para>
    /// <para>
    /// The server runs one world: the list of the world named by WorldServer.json LandmarksOfLandmarkWorld (Docker
    /// LANDMARKS_WORLD): auto (default) = the live world whose name this server has (WorldNameId; the teleport window's
    /// Liberation when unset), else Courage; or Courage / Determination / Liberation / Rebellion / Serenity / off.
    /// </para>
    /// <para>
    /// Inferred [I] (no source names them): the exact spot (live "tried to choose good locations"; here the nearest dry,
    /// level ground beside the Spire clear of every site), the height (the build's lowest solid voxel on the lowest ground
    /// under it: forum 57799 #7: set on flat ground with the base showing), the row layout of the two entries
    /// "combined on 1 claim", the banner's description and date wording, and the award model beside each claim.
    /// trace/0695/landmarks-of-landmark-695.md; open questions: trace/0695/OPEN-LEADS.md L111.
    /// </para>
    /// </summary>
    public static partial class LandmarksOfLandmark
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>One line of the developers' list.</summary>
        private sealed record Entry(string World, uint Zone, string Title, string[] Creators, int Year, string[] Templates)
        {
            public string ByLine => $"{Title} by {string.Join(" and ", Creators)}";
        }

        /// <summary>The live worlds of the list and their server-list names (client text ids, WorldServerConfiguration).</summary>
        private static readonly Dictionary<uint, string> worldNames = new()
        {
            [560911] = "Courage",
            [560912] = "Liberation",
            [560915] = "Rebellion",
            [560916] = "Serenity",
            [560917] = "Determination"
        };

        private const string MarkerFile = "landmarks_of_landmark.txt";
        /// <summary>Claim ids, owner and prop guids: "LOL" in the top bytes, never a database id or a character.</summary>
        private const ulong IdBase = 0x4C4F_4C00_0000_0000;
        public const ulong OwnerGuid = IdBase;
        /// <summary>PropDefs 1735 "Landmarks of Landmark Winner" (text 566223), Models.txt 1369 Theme_Awards_Showcases_LandmarkOfLandmarks01.</summary>
        private const uint AwardProp = 1735, AwardModel = 1369;
        /// <summary>Default Access of the sites: Play Around (as a new site; no build, mine or plunder bits).</summary>
        private const uint Access = 0x003;
        /// <summary>Dry land (local metres; sea level ~1000) and the largest ground step under a build (ours).</summary>
        private const float DryLand = 1005f, MaxStep = 6f;
        /// <summary>Clear ground kept around the build to any other site and to the Spire's platform, decimetres (ours).</summary>
        private const double Gap = 200;
        /// <summary>Streaming: props sent within this distance of the build's middle, removed beyond the second (metres).</summary>
        private const float ViewRange = 320f, DropRange = 420f;

        /// <summary>One stamped template of a landmark.</summary>
        private sealed record Part(string Template, long X, long Y, long Z, uint SizeX, uint SizeY, uint SizeZ);

        private sealed class Placed
        {
            public uint Zone;
            public Entry Entry;
            public List<Part> Parts = new();
            public ClaimModel Claim;
            /// <summary>The middle of the build at ground level (local metres).</summary>
            public Vector3 Middle;
            /// <summary>The Spire's foot it stands beside (local metres; for the award's side).</summary>
            public Vector3 Spire;
            /// <summary>Template props + the award, built once (null until then; empty when they can't be read).</summary>
            public volatile List<PlayerUpdateAddLightweightProp> Props;
            public bool Building;
        }

        private static readonly object sync = new(), placeLock = new();
        private static readonly Dictionary<uint, Placed> placed = new();
        private static readonly Dictionary<ulong, Placed> byClaim = new();
        private static readonly HashSet<uint> attempted = new();
        private static readonly Dictionary<WorldSession, (uint Zone, bool Sent)> streamed = new();
        private static string editsFolder;
        private static DateTime nextStream;

        /// <summary>The world whose list is used, or null (off).</summary>
        public static string World { get; private set; }

        /// <summary>
        /// Start-up (before Spire.Load): picks the world, then registers the landmarks stamped in earlier runs (their sites
        /// exist from the start: counted, searchable, refused to builders). New ones are stamped when their landscape's
        /// Spire is built (<see cref="Prepare"/>).
        /// </summary>
        public static void Load(string editsFolder)
        {
            LandmarksOfLandmark.editsFolder = editsFolder;
            World = ChooseWorld();
            string marker = Path.Combine(editsFolder, MarkerFile);
            int registered = 0;
            if (File.Exists(marker))
            {
                foreach (var group in File.ReadAllLines(marker).Select(l => l.Split('\t')).Where(f => f.Length == 9)
                             .GroupBy(f => (Zone: uint.Parse(f[0]), World: f[1])))
                {
                    Entry entry = table.FirstOrDefault(e => e.World == group.Key.World && e.Zone == group.Key.Zone);
                    if (entry == null)
                        continue;
                    if (World != null && World != entry.World)
                        log.Warn($"Landscape {entry.Zone} keeps the {entry.World} landmark stamped earlier ({entry.Title}); "
                            + $"the setting now names {World}. Revert its cells to change it.");
                    var p = new Placed { Zone = entry.Zone, Entry = entry };
                    foreach (string[] f in group)
                        p.Parts.Add(new Part(f[2], long.Parse(f[3]), long.Parse(f[4]), long.Parse(f[5]), uint.Parse(f[6]), uint.Parse(f[7]), uint.Parse(f[8])));
                    Register(p);
                    registered++;
                }
            }
            log.Info($"Landmarks of Landmark: world {World ?? "off"}; {registered} placed earlier.");
        }

        /// <summary>WorldServer.json LandmarksOfLandmarkWorld: auto, off or a world of the list.</summary>
        private static string ChooseWorld()
        {
            var config = ConfigurationManager<WorldServerConfiguration>.Instance.Config;
            string setting = (config?.LandmarksOfLandmarkWorld ?? "auto").Trim();
            if (setting.Equals("off", StringComparison.OrdinalIgnoreCase))
                return null;
            string named = table.Select(e => e.World).FirstOrDefault(w => w.Equals(setting, StringComparison.OrdinalIgnoreCase));
            if (named != null)
                return named;
            uint nameId = config?.WorldNameId is uint id and not 0 ? id : Landscapes.WorldNameText;
            return worldNames.TryGetValue(nameId, out string world) ? world : "Courage";
        }

        // ---- Placing ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Called once a landscape's Spire exists (Spire.Ensure): stamps that landscape's landmark the first time (its spot
        /// found beside the Spire), registers its site and remembers it. Nothing when the world is off, the landscape has
        /// no entry, or it was placed already.
        /// </summary>
        public static void Prepare(uint zone)
        {
            if (World == null)
                return;
            lock (sync)
            {
                if (placed.ContainsKey(zone) || !attempted.Add(zone))
                    return;
            }
            Entry entry = table.FirstOrDefault(e => e.World == World && e.Zone == zone);
            if (entry == null)
                return;
            try
            {
                // One at a time: two landscapes' Spires can be built at once from different threads.
                lock (placeLock)
                    Place(entry);
            }
            catch (Exception e)
            {
                log.Error(e, $"Could not place the landmark of landscape {zone} ({entry.Title}).");
            }
        }

        private static void Place(Entry entry)
        {
            uint zone = entry.Zone;
            if (!TerrainHeights.Available(zone))
                return;
            // The Spire it stands beside: its platform box (global dm), or the home Spire's foot without a template.
            (double X, double Y, double Z) spireMin, spireMax;
            if (Spire.PlatformOn(zone, 0) is { } box)
                (spireMin, spireMax) = box;
            else if (zone == TerrainStore.HomeContinent && Spire.Position is Vector3 foot)
            {
                var g = TerrainEdits.ToGlobal(foot);
                (spireMin, spireMax) = ((g.X - 100, g.Y - 200, g.Z - 100), (g.X + 100, g.Y + 200, g.Z + 100));
            }
            else
                return;

            var templates = new List<(string Name, VoxelTemplate Template, int Lowest)>();
            foreach (string name in entry.Templates)
            {
                VoxelTemplate t = LoadTemplate(name, true);
                if (t == null)
                    continue;
                int lowest = LowestSolid(t);
                if (lowest == int.MaxValue)
                {
                    log.Warn($"Landmark template {name} has no solid voxels; left out.");
                    continue;
                }
                TrimBelow(t, lowest);
                templates.Add((name, t, lowest));
            }
            if (templates.Count == 0)
            {
                log.Warn($"Landscape {zone}: no readable template for {entry.ByLine}; no landmark (trace/0695/OPEN-LEADS.md).");
                return;
            }

            // Several templates on one claim stand in a row along +x, each in its own box ([I]).
            double voxel = TerrainEdits.VoxelSize;
            long width = templates.Sum(t => (long)t.Template.Size.X), depth = templates.Max(t => (long)t.Template.Size.Z);
            var origin = TerrainStore.ZoneOrigin;
            Vector3 spireFoot = new((float)(((spireMin.X + spireMax.X) / 2 - origin.X) / 10), (float)((spireMin.Y - origin.Y) / 10),
                (float)(((spireMin.Z + spireMax.Z) / 2 - origin.Z) / 10));
            if (FindSpot(zone, spireMin, spireMax, spireFoot, width * (float)voxel / 10, depth * (float)voxel / 10) is not { } spot)
            {
                log.Warn($"Landscape {zone}: no free level ground beside the Spire for {entry.ByLine}.");
                return;
            }
            (float cx, float cz, float ground) = spot;

            var middle = TerrainEdits.ToGlobal(new Vector3(cx, ground, cz));
            long x0 = (long)Math.Round((middle.X - width * voxel / 2) / voxel), z0 = (long)Math.Round((middle.Z - depth * voxel / 2) / voxel);
            long groundVoxel = (long)Math.Floor(middle.Y / voxel);
            // Building: the streaming tick must not read the props from the files while they are made here.
            var p = new Placed { Zone = zone, Entry = entry, Spire = spireFoot, Building = true };
            var lines = new List<string>();
            long offset = 0;
            foreach ((string name, VoxelTemplate t, int lowest) in templates)
            {
                var part = new Part(name, x0 + offset, groundVoxel - lowest, z0, t.Size.X, t.Size.Y, t.Size.Z);
                offset += t.Size.X;
                (List<ulong> cells, List<ulong> changed) = EnvironmentServer.Instance.StampTemplateAt(t, part.X, part.Y, part.Z, zone);
                EnvironmentServer.Instance.Flush();
                EnvironmentServer.Instance.MarkPermanent(cells, zone);
                EnvironmentServer.Instance.CompactNow(true);
                ZoneTerrain.Broadcast(changed, zone);
                log.Info($"Stamped landmark {name} on landscape {zone} at voxel ({part.X}, {part.Y}, {part.Z}): {VoxelTemplate.LastStampReport}.");
                p.Parts.Add(part);
                lines.Add(string.Join('\t', zone, entry.World, name, part.X, part.Y, part.Z, part.SizeX, part.SizeY, part.SizeZ));
            }
            Directory.CreateDirectory(editsFolder);
            File.AppendAllLines(Path.Combine(editsFolder, MarkerFile), lines);
            Register(p);
            p.Props = BuildProps(p, templates.Select(t => t.Template).ToList());
            p.Building = false;
            templates.Clear();
            ZoneTerrain.ClearVeinsInSite(zone, (p.Claim.MinX, p.Claim.MinY, p.Claim.MinZ), (p.Claim.MaxX, p.Claim.MaxY, p.Claim.MaxZ));
            log.Info($"Landmark of landscape {zone} ({entry.World}): {entry.ByLine}, {entry.Year}, site {p.Claim.Id:X}, {p.Props.Count} props.");
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }

        /// <summary>
        /// The nearest spot to the Spire (rings 10 m apart, out to 1 km) where the build's footprint has dry ground no more
        /// than <see cref="MaxStep"/> uneven (9 samples) and keeps <see cref="Gap"/> from every site and from the Spire's
        /// platform. Returns its middle (local x / z) and the lowest ground under it.
        /// </summary>
        private static (float X, float Z, float Ground)? FindSpot(uint zone, (double X, double Y, double Z) spireMin,
            (double X, double Y, double Z) spireMax, Vector3 spire, float width, float depth)
        {
            float hw = width / 2, hd = depth / 2;
            var samples = new (float, float)[] { (0, 0), (-hw, -hd), (hw, -hd), (-hw, hd), (hw, hd), (0, -hd), (0, hd), (-hw, 0), (hw, 0) };
            for (float radius = 20f; radius <= 1000f; radius += 10f)
            {
                int steps = Math.Max(8, (int)(radius * 2 * MathF.PI / 10f));
                for (int i = 0; i < steps; i++)
                {
                    float angle = i * 2 * MathF.PI / steps;
                    float x = spire.X + radius * MathF.Cos(angle), z = spire.Z + radius * MathF.Sin(angle);
                    var global = TerrainEdits.ToGlobal(new Vector3(x, 0f, z));
                    (double X, double Z) lo = (global.X - hw * 10 - Gap, global.Z - hd * 10 - Gap), hi = (global.X + hw * 10 + Gap, global.Z + hd * 10 + Gap);
                    if (lo.X < spireMax.X && hi.X > spireMin.X && lo.Z < spireMax.Z && hi.Z > spireMin.Z)
                        continue;
                    float low = float.MaxValue, high = float.MinValue;
                    bool dry = true;
                    foreach ((float dx, float dz) in samples)
                    {
                        if (TerrainHeights.HeightAt(x + dx, z + dz, zone) is not float h || h < DryLand)
                        {
                            dry = false;
                            break;
                        }
                        low = MathF.Min(low, h);
                        high = MathF.Max(high, h);
                    }
                    if (!dry || high - low > MaxStep)
                        continue;
                    if (Claims.BoxesIn(zone, (lo.X, double.MinValue, lo.Z), (hi.X, double.MaxValue, hi.Z)).Count > 0)
                        continue;
                    return (x, z, low);
                }
            }
            return null;
        }

        /// <summary>The lowest template layer (template voxels) holding a solid voxel, or int.MaxValue.</summary>
        private static int LowestSolid(VoxelTemplate t)
        {
            int lowest = int.MaxValue;
            foreach (VoxelTemplate.Cell cell in t.Cells.OrderBy(c => c.Y))
            {
                if (cell.Y * BlockData.Size >= lowest)
                    break;
                for (int i = 0; i < BlockData.Count; i++)
                    if ((cell.Changed[i >> 3] & (0x80 >> (i & 7))) != 0 && (cell.Blocks.Flags[i] & 1) != 0
                        && cell.Blocks.Materials[i] is not 0 and not VoxelTemplate.InteriorAir)
                        lowest = Math.Min(lowest, cell.Y * BlockData.Size + i % BlockData.Size);   // index y + 40·(x + 40·z)
            }
            return lowest;
        }

        /// <summary>
        /// Leaves out every template voxel under the build's lowest solid layer: the 2016 entries write air through their
        /// whole 70 × 100 × 70 m contest box, which under the ground would dig a pit around the base.
        /// </summary>
        private static void TrimBelow(VoxelTemplate t, int lowest)
        {
            foreach (VoxelTemplate.Cell cell in t.Cells)
            {
                int bottom = cell.Y * BlockData.Size;
                if (bottom >= lowest)
                    continue;
                for (int i = 0; i < BlockData.Count; i++)
                    if (bottom + i % BlockData.Size < lowest)
                        cell.Changed[i >> 3] &= (byte)~(0x80 >> (i & 7));
            }
        }

        // ---- Sites -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// Makes the landmark's site (the parts' boxes, from 20 m under the ground to the top of the tallest box) and
        /// registers it with Claims.
        /// </summary>
        private static void Register(Placed p)
        {
            double voxel = TerrainEdits.VoxelSize;
            long x0 = p.Parts.Min(t => t.X), z0 = p.Parts.Min(t => t.Z);
            long x1 = p.Parts.Max(t => t.X + t.SizeX), z1 = p.Parts.Max(t => t.Z + t.SizeZ);
            long top = p.Parts.Max(t => t.Y + t.SizeY);
            // From 20 m under the lowest template corner (which lies under the ground by the build's empty bottom layers):
            // made from the marker's corners only, so the box is the same after every restart.
            double groundDm = p.Parts.Min(t => t.Y) * voxel;
            ulong id = IdBase | p.Zone;
            p.Claim = new ClaimModel
            {
                Id = id,
                OwnerGuid = OwnerGuid,
                Name = p.Entry.ByLine,
                Description = Description(p.Entry),
                MinX = x0 * voxel,
                MaxX = x1 * voxel,
                MinY = groundDm - 200,
                MaxY = top * voxel,
                MinZ = z0 * voxel,
                MaxZ = z1 * voxel,
                ZoneId = p.Zone,
                DefaultPermissions = Access
            };
            var origin = TerrainStore.ZoneOrigin;
            float mx = (float)(((p.Claim.MinX + p.Claim.MaxX) / 2 - origin.X) / 10), mz = (float)(((p.Claim.MinZ + p.Claim.MaxZ) / 2 - origin.Z) / 10);
            // (Neither Spire.PlatformOn nor the ground heights here: at start-up this runs before Spire.Load, and asking
            // then would leave that landscape without its Spire. BuildProps asks later, when the award is placed.)
            p.Middle = new Vector3(mx, (float)((groundDm - origin.Y) / 10), mz);   // y: the lowest corner (ground looked up when needed)
            lock (sync)
            {
                placed[p.Zone] = p;
                byClaim[id] = p;
            }
            Claims.AddServerSite(p.Claim);
        }

        private static string Description(Entry e)
            => $"A winner of the {e.Year} Landmarks of Landmark building competition, enshrined here by the developers.";

        /// <summary>
        /// The claim record's admin template guid (record +0x158) for a site: non-zero only for a landmark's site (the
        /// client then shows its contest banner). Claims.WriteRecord.
        /// </summary>
        public static ulong AdminTemplateOf(ulong claimId)
        {
            lock (sync)
                return byClaim.ContainsKey(claimId) ? claimId : 0;
        }

        /// <summary>
        /// The five strings of a landmark site's claim record, as the client's banner reads them (FUN_142dc7540, lengths
        /// as it clamps them): competition name (≤128), build name (≤128), description (≤256), date (≤32), contributors
        /// (FUN_142db82e0 format 0: "0 Name Name ...", one row of ClaimDialogs.AdminClaimContributors per name). Null for
        /// other sites.
        /// </summary>
        public static string[] WinnerTextsOf(ulong claimId)
        {
            Placed p;
            lock (sync)
                if (!byClaim.TryGetValue(claimId, out p))
                    return null;
            Entry e = p.Entry;
            return new[]
            {
                "Landmarks of Landmark",
                e.ByLine,
                Description(e),
                e.Year.ToString(),
                "0 " + string.Join(' ', e.Creators.Select(c => c.Replace(' ', '_')))
            };
        }

        // ---- Props -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// The templates' props where they put them (spawner props left out, as in the outposts) and the award model
        /// beside the site, on the Spire's side ([I]).
        /// </summary>
        private static List<PlayerUpdateAddLightweightProp> BuildProps(Placed p, List<VoxelTemplate> templates)
        {
            var list = new List<PlayerUpdateAddLightweightProp>();
            ulong next = 1;
            var origin = TerrainStore.ZoneOrigin;
            double voxel = TerrainEdits.VoxelSize;
            for (int n = 0; n < p.Parts.Count && n < templates.Count; n++)
            {
                Part part = p.Parts[n];
                Vector3 corner = new((float)((part.X * voxel - origin.X) / 10), (float)((part.Y * voxel - origin.Y) / 10), (float)((part.Z * voxel - origin.Z) / 10));
                foreach (VoxelTemplate.Prop prop in templates[n].Props)
                {
                    if (prop.ModelId == 0 || Creatures.PropName(prop.PropId).Contains("Spawner", StringComparison.OrdinalIgnoreCase))
                        continue;
                    list.Add(Prop(p.Zone, next++, prop.ModelId, corner + prop.Position, prop.Rotation, prop.Scale, 0));
                }
            }
            // The award: 3 m outside the site's edge that faces the Spire, on the ground, facing the Spire.
            ClaimModel c = p.Claim;
            float minX = (float)((c?.MinX ?? 0) - origin.X) / 10, maxX = (float)((c?.MaxX ?? 0) - origin.X) / 10;
            float minZ = (float)((c?.MinZ ?? 0) - origin.Z) / 10, maxZ = (float)((c?.MaxZ ?? 0) - origin.Z) / 10;
            if (c != null)
            {
                if (p.Spire == default && Spire.PlatformOn(p.Zone, 0) is { } box)
                    p.Spire = new Vector3((float)(((box.Min.X + box.Max.X) / 2 - origin.X) / 10), p.Middle.Y,
                        (float)(((box.Min.Z + box.Max.Z) / 2 - origin.Z) / 10));
                Vector3 toSpire = p.Spire - p.Middle;
                float ax, az;
                if (MathF.Abs(toSpire.X) >= MathF.Abs(toSpire.Z))
                    (ax, az) = (toSpire.X >= 0 ? maxX + 3f : minX - 3f, (minZ + maxZ) / 2);
                else
                    (ax, az) = ((minX + maxX) / 2, toSpire.Z >= 0 ? maxZ + 3f : minZ - 3f);
                float ay = TerrainHeights.HeightAt(ax, az, p.Zone) ?? TerrainHeights.HeightAt(p.Middle.X, p.Middle.Z, p.Zone) ?? p.Middle.Y;
                float yaw = MathF.Atan2(p.Spire.X - ax, p.Spire.Z - az);
                uint model = Props.ModelOf(AwardProp) is uint m and not 0 ? m : AwardModel;
                list.Add(Prop(p.Zone, 0xFFFF, model, new Vector3(ax, ay, az), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), Vector3.One,
                    AwardProp));
            }
            return list;
        }

        private static PlayerUpdateAddLightweightProp Prop(uint zone, ulong index, uint model, Vector3 at, Quaternion rotation, Vector3 scale, uint propId) => new()
        {
            Guid = IdBase | ((ulong)zone << 24) | index,
            TransientId = PlayerManager.AllocateTransientId(),
            ModelId = model,
            Position = new Vector4(at, 1f),
            Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
            Scale = new Vector4(scale, 1f),
            NameId = propId == 0 ? 0 : GameTableManager.Instance.PropDefs.GetEntry(propId)?.NameStringId ?? 0
        };

        /// <summary>
        /// Props of a landmark placed in an earlier run: read from its templates (props only, cells skipped) in the
        /// background the first time a player is on its landscape.
        /// </summary>
        private static void BuildPropsLater(Placed p)
        {
            lock (sync)
            {
                if (p.Props != null || p.Building)
                    return;
                p.Building = true;
            }
            Task.Run(() =>
            {
                List<PlayerUpdateAddLightweightProp> props;
                try
                {
                    var templates = p.Parts.Select(t => LoadTemplate(t.Template, false)).ToList();
                    props = templates.All(t => t != null) ? BuildProps(p, templates) : new List<PlayerUpdateAddLightweightProp>();
                }
                catch (Exception e)
                {
                    log.Error(e, $"Could not read the props of the landmark of landscape {p.Zone}.");
                    props = new List<PlayerUpdateAddLightweightProp>();
                }
                p.Props = props;
            });
        }

        // ---- Streaming ---------------------------------------------------------------------------------------------------

        /// <summary>World tick: sends a landmark's props to players who come near it, removes them from players who leave.</summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextStream)
                return;
            nextStream = now.AddSeconds(1);
            Dictionary<uint, Placed> snapshot;
            lock (sync)
            {
                if (placed.Count == 0)
                    return;
                snapshot = new Dictionary<uint, Placed>(placed);
            }
            foreach (WorldSession session in PlayerManager.Snapshot())
            {
                if (session.StreamsOnHold)
                    continue;
                uint zone = session.ZoneId;
                (uint Zone, bool Sent) state;
                lock (sync)
                    state = streamed.TryGetValue(session, out var s) && s.Zone == zone ? s : (zone, false);
                if (!snapshot.TryGetValue(zone, out Placed p))
                {
                    lock (sync)
                        streamed.Remove(session);
                    continue;
                }
                if (p.Props == null)
                {
                    BuildPropsLater(p);
                    continue;
                }
                float d = Vector2.Distance(new Vector2(session.Position.X, session.Position.Z), new Vector2(p.Middle.X, p.Middle.Z));
                if (!state.Sent && d <= ViewRange)
                {
                    foreach (PlayerUpdateAddLightweightProp prop in p.Props)
                        session.EnqueueMessage(prop);
                    state.Sent = true;
                }
                else if (state.Sent && d > DropRange)
                {
                    foreach (PlayerUpdateAddLightweightProp prop in p.Props)
                        session.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = prop.Guid });
                    state.Sent = false;
                }
                lock (sync)
                    streamed[session] = state;
            }
        }

        /// <summary>A player left or re-zoned (the client dropped its entities): props are sent again when near.</summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
                streamed.Remove(session);
        }

        /// <summary>LightweightToFullProp for a landmark prop (collision; FullCharacterDataRequest).</summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            if ((guid & 0xFFFF_FF00_0000_0000) != IdBase)
                return false;
            Placed p;
            lock (sync)
                p = placed.GetValueOrDefault((uint)((guid >> 24) & 0xFFFF));
            PlayerUpdateAddLightweightProp prop = p?.Props?.FirstOrDefault(x => x.Guid == guid);
            if (prop == null)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = prop.TransientId });
            return true;
        }

        // ---- Templates ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Reads a landmark template; a file the client ships damaged by a text-mode copy (every LF written as CR LF:
        /// landmarks_of_landmark_2016_winner_-_aetherion_tower.otd, Courage / Lonely Glacier) is repaired in memory first
        /// (<see cref="UndoTextMode"/>). Null when it can't be read.
        /// </summary>
        private static VoxelTemplate LoadTemplate(string name, bool withCells)
        {
            string path = Path.Combine(VoxelTemplate.Folder, name + ".otd");
            if (!File.Exists(path))
            {
                log.Warn($"Landmark template {path} not found.");
                return null;
            }
            byte[] data = File.ReadAllBytes(path);
            try
            {
                return VoxelTemplate.Load(new MemoryStream(data), withCells);
            }
            catch (Exception e) when (UndoTextMode(data) is byte[] repaired)
            {
                try
                {
                    VoxelTemplate t = VoxelTemplate.Load(new MemoryStream(repaired), withCells);
                    log.Info($"Landmark template {name}: repaired a text-mode copy ({data.Length} → {repaired.Length} bytes; first read: {e.Message}).");
                    return t;
                }
                catch (Exception again)
                {
                    log.Warn($"Landmark template {name} unreadable even after the CR LF repair: {again.Message}");
                    return null;
                }
            }
            catch (Exception e)
            {
                log.Warn($"Landmark template {name} unreadable: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Undoes a text-mode write of an .otd (LF → CR LF everywhere, so no LF stands alone). Each cell's length field is
        /// exact and its blob is a zlib stream with an Adler-32 check, so per blob the few CR LF pairs that were genuine
        /// (about one in 256) are found by trying them until the blob has its length and checks out. Headers and the
        /// trailing size / material / prop sections are read with every CR LF as LF. Null when the file doesn't look like
        /// that or can't be repaired.
        /// </summary>
        private static byte[] UndoTextMode(byte[] d)
        {
            int lf = 0, crlf = 0;
            for (int i = 0; i < d.Length; i++)
                if (d[i] == 0x0A)
                {
                    lf++;
                    if (i > 0 && d[i - 1] == 0x0D)
                        crlf++;
                }
            if (crlf == 0 || crlf != lf || d.Length < 5)
                return null;
            var output = new MemoryStream();
            int p = 1;
            output.WriteByte(d[0]);
            byte[] Take(int n)
            {
                var b = new byte[n];
                for (int k = 0; k < n; k++)
                {
                    if (d[p] == 0x0D && p + 1 < d.Length && d[p + 1] == 0x0A)
                        p++;
                    b[k] = d[p++];
                }
                return b;
            }
            byte[] head = Take(4);
            output.Write(head);
            uint cells = BitConverter.ToUInt32(head);
            for (uint c = 0; c < cells; c++)
            {
                head = Take(24);
                output.Write(head);
                uint type = BitConverter.ToUInt32(head, 16);
                int length = BitConverter.ToInt32(head, 20);
                byte[] blob = TakeBlob(d, ref p, length);
                if (blob == null)
                    return null;
                output.Write(blob);
                if (type is 2 or 3)
                    output.Write(Take(4));
            }
            for (int i = p; i < d.Length; i++)
                if (!(d[i] == 0x0D && i + 1 < d.Length && d[i + 1] == 0x0A))
                    output.WriteByte(d[i]);
            return output.ToArray();
        }

        /// <summary>One cell blob of <paramref name="length"/> real bytes at <paramref name="p"/> (see UndoTextMode).</summary>
        private static byte[] TakeBlob(byte[] d, ref int p, int length)
        {
            for (int genuine = 0; genuine <= 2; genuine++)
            {
                // Physical span P holding `length` real bytes when all but `genuine` of its CR LF pairs lose their CR.
                int span = length, pairs = 0;
                for (int guard = 0; guard < 64; guard++)
                {
                    if (p + span > d.Length)
                        break;
                    pairs = 0;
                    for (int i = p; i + 1 < p + span; i++)
                        if (d[i] == 0x0D && d[i + 1] == 0x0A)
                            pairs++;
                    if (span - (pairs - genuine) == length)
                        break;
                    span = length + pairs - genuine;
                }
                if (p + span > d.Length || span - (pairs - genuine) != length || genuine > pairs)
                    continue;
                var at = new List<int>();
                for (int i = p; i + 1 < p + span; i++)
                    if (d[i] == 0x0D && d[i + 1] == 0x0A)
                        at.Add(i);
                foreach (int[] keep in Choose(at.Count, genuine))
                {
                    var kept = new HashSet<int>(keep.Select(k => at[k]));
                    var blob = new byte[length];
                    int n = 0;
                    for (int i = p; i < p + span && n < length; i++)
                    {
                        if (d[i] == 0x0D && i + 1 < p + span && d[i + 1] == 0x0A && !kept.Contains(i))
                            continue;
                        blob[n++] = d[i];
                    }
                    if (n == length && BlobChecks(blob))
                    {
                        p += span;
                        return blob;
                    }
                }
            }
            return null;
        }

        private static IEnumerable<int[]> Choose(int n, int k)
        {
            if (k == 0)
                yield return Array.Empty<int>();
            else if (k == 1)
                for (int a = 0; a < n; a++)
                    yield return new[] { a };
            else
                for (int a = 0; a < n; a++)
                    for (int b = a + 1; b < n; b++)
                        yield return new[] { a, b };
        }

        /// <summary>The block codec around a zlib stream whose output has the stated size and Adler-32.</summary>
        private static bool BlobChecks(byte[] blob)
        {
            if (blob.Length < 14 || blob[0] != 0xA1 || blob[1] != 0xB2 || blob[2] != 0xC3 || blob[3] != 0xD4)
                return false;
            int size = blob[4] << 24 | blob[5] << 16 | blob[6] << 8 | blob[7];
            if (size <= 0 || size > 1 << 22)
                return false;
            try
            {
                using var zlib = new ZLibStream(new MemoryStream(blob, 8, blob.Length - 8), CompressionMode.Decompress);
                var raw = new byte[size + 1];
                int read = 0, r;
                while (read < raw.Length && (r = zlib.Read(raw, read, raw.Length - read)) > 0)
                    read += r;
                if (read != size)
                    return false;
                uint a = 1, b = 0;
                for (int i = 0; i < size; i++)
                {
                    a = (a + raw[i]) % 65521;
                    b = (b + a) % 65521;
                }
                uint stored = (uint)(blob[^4] << 24 | blob[^3] << 16 | blob[^2] << 8 | blob[^1]);
                return (b << 16 | a) == stored;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
    }
}
