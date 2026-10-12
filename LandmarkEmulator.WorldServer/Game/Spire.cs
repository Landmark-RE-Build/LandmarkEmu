using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The world's Spire: the teleporter hub "Spire" in the Travel window takes players to (cast 185), and the hub /
    /// graveyard respawn. Built the way the live game built it: the client's own template
    /// CommonData/Voxel/templates/base_portal_spire.otd (<see cref="Terrain.VoxelTemplate"/>) is stamped into the
    /// terrain near the zone origin — its voxel platform — and its props (the Spire pieces, Models.txt 596
    /// Theme_Widgets_Teleporters_PortalSpires01 and the rest) are placed where the template puts them. The stamp is done
    /// once and saved with the terrain edits (marker TerrainEdits/spire_base.txt holds its corner cell); the props are
    /// static entities sent on zone-in. Without the template the Spire falls back to its two models on the ground.
    /// </summary>
    public static class Spire
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>Where the Spire should stand (local metres, horizontal; the ground height is looked up).</summary>
        private const float X = 0f, Z = 0f;
        private const uint BaseModel = 612, SpiresModel = 596;
        private const ulong GuidBase = 0x5350_0000_0000_0000;
        private const string TemplateFile = "base_portal_spire.otd", Marker = "spire_base.txt";

        private static Vector3? position;
        private static List<PlayerUpdateAddLightweightProp> props;

        /// <summary>Spire prop guid → its prop definition (PropDefs), for the ones players can use.</summary>
        private static readonly Dictionary<ulong, uint> propDefinitions = new();

        /// <summary>The Spire's mailbox (PropDefs 489 "Mailbox").</summary>
        private const uint MailboxProp = 489;

        /// <summary>
        /// The Spire's barbershop (PropDefs 2942 "The Crimson Parlor"): clicking it opens the client's Barbershop window
        /// (CharacterCustomization.StartCustomization, run through InteractionRunClientLua).
        /// </summary>
        private const uint BarberProp = 2942;
        private static (uint X, uint Y, uint Z)? corner;
        private static Terrain.VoxelTemplate template;

        /// <summary>
        /// Where players arrive at the Spire (travel, respawn, first entry), from its foot: open platform beside the Portal
        /// Crystal. In front of the Spire (+Z) is inside its pillar, and the client pushed players out under the platform.
        /// </summary>
        private static readonly Vector3 ArrivalOffset = new(7.2f, -0.5f, -6.5f);

        /// <summary>The Spire's arrival point (local metres), or null where it couldn't be placed.</summary>
        public static Vector3? Arrival => Position is Vector3 at ? at + ArrivalOffset : null;

        /// <summary>The Spire's guid (also its id as a respawn site).</summary>
        public static ulong Guid => GuidBase | 1;

        /// <summary>The Spire's foot (local metres), or null where it couldn't be placed.</summary>
        public static Vector3? Position
        {
            get
            {
                if (position == null && template == null)
                    position = Terrain.TerrainHeights.OnGround(X, Z);
                return position;
            }
        }

        /// <summary>
        /// Loads the Spire template and stamps its base into the home continent's terrain the first time (after
        /// TerrainEdits.Load). The corner cell is chosen so the template's Spire lands on the ground at (X, Z).
        /// </summary>
        public static void Load(string gameDirectory, string editsFolder)
        {
            string path = Path.Combine(gameDirectory ?? "", "CommonData", "Voxel", "templates", TemplateFile);
            if (!File.Exists(path))
            {
                log.Warn($"Spire template {path} not found; the Spire stands on the bare ground.");
                return;
            }
            try
            {
                template = Terrain.VoxelTemplate.Load(path);
            }
            catch (Exception e)
            {
                log.Error(e, "Could not read the Spire template.");
                return;
            }
            Terrain.VoxelTemplate.Prop spire = template.Props.FirstOrDefault(p => p.ModelId == SpiresModel) ?? template.Props.FirstOrDefault();
            if (spire == null || Terrain.TerrainHeights.OnGround(X, Z) is not Vector3 ground)
            {
                template = null;
                return;
            }

            string marker = Path.Combine(editsFolder, Marker);
            if (File.Exists(marker) && File.ReadAllText(marker).Split(' ') is { Length: 3 } saved)
                corner = (uint.Parse(saved[0]), uint.Parse(saved[1]), uint.Parse(saved[2]));
            else
            {
                // Corner = the ground point minus the Spire's offset in the template (metres → dm), on the cell grid.
                (double gx, double gy, double gz) = Terrain.TerrainEdits.ToGlobal(ground);
                double size = Terrain.TerrainEdits.CellSize;
                corner = ((uint)Math.Round((gx - spire.Position.X * 10) / size), (uint)Math.Round((gy - spire.Position.Y * 10) / size),
                    (uint)Math.Round((gz - spire.Position.Z * 10) / size));
                List<ulong> cells = template.Stamp(corner.Value.X, corner.Value.Y, corner.Value.Z, Terrain.TerrainStore.HomeContinent);
                Terrain.TerrainEdits.Flush();
                Directory.CreateDirectory(editsFolder);
                File.WriteAllText(marker, $"{corner.Value.X} {corner.Value.Y} {corner.Value.Z}");
                log.Info($"Stamped the Spire base ({cells.Count} cells) at cell {corner}.");
            }
            position = Local(spire.Position);
            Terrain.TerrainEdits.MarkPermanent(template.Cells.Select(c => Terrain.TerrainStore.CellId(Terrain.TerrainEdits.Level,
                (uint)(corner.Value.X + c.X), (uint)(corner.Value.Y + c.Y), (uint)(corner.Value.Z + c.Z))), Terrain.TerrainStore.HomeContinent);
        }

        /// <summary>
        /// Is a point (local metres) on the Spire's platform, on the home continent? Trees and plants are not grown there
        /// (the world generator knows nothing of the stamped template).
        /// </summary>
        public static bool Covers(uint zone, float x, float z)
        {
            if (template == null || corner == null || zone != Terrain.TerrainStore.HomeContinent)
                return false;
            Vector3 min = Local(Vector3.Zero);
            float width = template.Size.X * (float)Terrain.TerrainEdits.VoxelSize / 10, depth = template.Size.Z * (float)Terrain.TerrainEdits.VoxelSize / 10;
            return x >= min.X - 2 && x <= min.X + width + 2 && z >= min.Z - 2 && z <= min.Z + depth + 2;
        }

        /// <summary>A point inside the template (metres from its corner) in local metres.</summary>
        private static Vector3 Local(Vector3 inTemplate)
        {
            double size = Terrain.TerrainEdits.CellSize;
            var origin = Terrain.TerrainStore.ZoneOrigin;
            return new Vector3((float)((corner.Value.X * size + inTemplate.X * 10 - origin.X) / 10),
                (float)((corner.Value.Y * size + inTemplate.Y * 10 - origin.Y) / 10),
                (float)((corner.Value.Z * size + inTemplate.Z * 10 - origin.Z) / 10));
        }

        /// <summary>Sends the Spire's props to a player entering the home continent.</summary>
        public static void Enter(WorldSession session)
        {
            if (Position is not Vector3 at)
                return;
            props ??= BuildProps(at);
            foreach (PlayerUpdateAddLightweightProp prop in props)
                session.EnqueueMessage(prop);
            // The Replicator, Repair Station and Mailbox are usable; flag them so the client asks for their interaction
            // info (Props.SendInteractionInfo) and sends InteractRequest on a click.
            var flagging = new DynamicInteractionFlagging();
            flagging.Guids.AddRange(propDefinitions.Keys.Where(Usable));
            if (flagging.Guids.Count > 0)
                session.EnqueueMessage(flagging);
        }

        /// <summary>
        /// Answers the client's FullCharacterDataRequest for a Spire prop with LightweightToFullProp. Until it has it a
        /// prop stays a lightweight shell: no collision (players walked through the Spire) and no proximity
        /// interaction (the client's reach test uses the collision bounds).
        /// </summary>
        public static bool SendFull(WorldSession session, ulong guid)
        {
            PlayerUpdateAddLightweightProp prop = props?.FirstOrDefault(p => p.Guid == guid);
            if (prop == null)
                return false;
            session.EnqueueMessage(new PlayerUpdateLightweightToFullProp { TransientId = prop.TransientId });
            return true;
        }

        /// <summary>The crafting station a Spire prop works as (the Replicator, the Repair Station), or 0.</summary>
        public static uint StationAt(ulong guid)
            => propDefinitions.TryGetValue(guid, out uint def) ? CraftingStations.StationForProp(def) : 0;

        public static bool IsMailbox(ulong guid) => propDefinitions.TryGetValue(guid, out uint def) && def == MailboxProp;

        public static bool IsBarber(ulong guid) => propDefinitions.TryGetValue(guid, out uint def) && def == BarberProp;

        /// <summary>
        /// The guid of the Spire's "Portal Spire (Hub)" prop (PropDefs 1871): the warp network's hub node (the Local
        /// Travel map picks nodes by the entity under the cursor), 0 before the Spire's props are built.
        /// </summary>
        public static ulong HubGuid
        {
            get
            {
                if (Position is Vector3 at)
                    props ??= BuildProps(at);
                return propDefinitions.FirstOrDefault(p => p.Value == 1871).Key;
            }
        }

        /// <summary>
        /// The Spire's Portal Crystal (PropDefs 575): clicking it is the warp hub's terminal. Only the crystal is usable;
        /// the Spire structure itself (1871, the hub node's entity) stays a plain solid prop.
        /// </summary>
        public static bool IsCrystal(ulong guid) => propDefinitions.TryGetValue(guid, out uint def) && def == 575;

        /// <summary>Can players use this Spire prop?</summary>
        public static bool Usable(ulong guid) => StationAt(guid) != 0 || IsMailbox(guid) || IsBarber(guid) || IsCrystal(guid);

        /// <summary>
        /// The template's props where it puts them; the Spire itself gets index 1 (its guid is the respawn site id).
        /// Without a template, the two Spire models on the ground.
        /// </summary>
        private static List<PlayerUpdateAddLightweightProp> BuildProps(Vector3 at)
        {
            if (template == null || corner == null)
                return new() { Prop(1, SpiresModel, at, Quaternion.Identity, Vector3.One), Prop(2, BaseModel, at, Quaternion.Identity, Vector3.One) };
            var list = new List<PlayerUpdateAddLightweightProp>();
            ulong next = 2;
            foreach (Terrain.VoxelTemplate.Prop p in template.Props)
            {
                if (p.ModelId == 0)
                    continue;
                ulong index = p.ModelId == SpiresModel && list.All(x => x.Guid != Guid) ? 1 : next++;
                PlayerUpdateAddLightweightProp spawn = Prop(index, p.ModelId, Local(p.Position), p.Rotation, p.Scale);
                propDefinitions[spawn.Guid] = p.PropId;
                if (Usable(spawn.Guid))   // named like a placed station (the hover prompt shows it)
                    spawn.NameId = GameTableManager.Instance.PropDefs.GetEntry(p.PropId)?.NameStringId ?? 0;
                list.Add(spawn);
            }
            log.Info($"Spire: {list.Count} template props (prop ids {string.Join(", ", propDefinitions.Values.Distinct().OrderBy(v => v))}).");
            return list;
        }

        /// <summary>A static Spire prop with a fixed guid so every player sees the same entity.</summary>
        private static PlayerUpdateAddLightweightProp Prop(ulong index, uint model, Vector3 at, Quaternion rotation, Vector3 scale) => new()
        {
            Guid = GuidBase | index,
            TransientId = PlayerManager.AllocateTransientId(),
            ModelId = model,
            Position = new Vector4(at, 1f),
            Rotation = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W),
            Scale = new Vector4(scale, 1f)
        };

        /// <summary>
        /// Travel → Spire: arrive on the platform beside the Portal Crystal. The Spire stands on the home continent; from another
        /// landscape the trip re-zones there (a plain teleport would land at that landscape's origin, often open sea).
        /// </summary>
        public static void TravelTo(WorldSession session)
        {
            if (!session.OnHomeContinent)
            {
                Landscapes.TravelTo(session, Terrain.TerrainStore.HomeContinent);
                return;
            }
            if (Arrival is not Vector3 at)
            {
                log.Warn("The Spire has no ground position; travel cancelled.");
                return;
            }
            Teleport.To(session, at);
            log.Info($"{session.CharacterName} travels to the Spire {at}.");
        }
    }
}
