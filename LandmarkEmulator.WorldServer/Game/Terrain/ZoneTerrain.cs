using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// The zone's side of terrain (trace/architecture/terrain-service.md §3.1, §3.3): the zone server is the only sender
    /// of terrain packets to a player, so everything that decides who gets what — full cell data within range or a
    /// 21-byte manifest note, the builder left out of their own edit, the zone-in manifest, Voxel Vision replies with the
    /// builders' names — and the permission check against the player's claims lives here. The voxel data itself comes
    /// from the Environment Server (<see cref="EnvironmentServer.Instance"/>).
    /// </summary>
    public static class ZoneTerrain
    {
        /// <summary>The Voxel Vision reply keeps the logger it had in VoxelOwnership (same name in the logs).</summary>
        private static readonly ILogger ownershipLog = LogManager.GetLogger(typeof(VoxelOwnership).FullName);
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static IEnvironmentServer Env => EnvironmentServer.Instance;

        private static uint Home => TerrainStore.HomeContinent;

        // ---- permissions -----------------------------------------------------------------------------------------------

        /// <summary>May a player build every voxel an edit changes in this cell (claims checked once per cell)?</summary>
        public static bool MayApply(ulong id, byte[] changed, ulong playerGuid, uint zone)
        {
            (uint level, uint cx, uint cy, uint cz) = TerrainStore.Decode(id);
            if (level != TerrainEdits.Level)
                return false;
            double size = TerrainEdits.CellSize;
            double ox = cx * size, oy = cy * size, oz = cz * size;
            if (!Spire.MayEdit(playerGuid, zone, (ox, oy, oz), (ox + size, oy + size, oz + size)))
                return false;
            var check = Claims.BuildCheckIn(playerGuid, zone, (ox, oy, oz), (ox + size, oy + size, oz + size));
            return check == null || TerrainEdits.MayApply(id, changed, check);
        }

        /// <summary>
        /// Per voxel of a cell (index y fastest): may the player build it? The same rules as <see cref="MayApply"/>, for
        /// optional voxels of an edit that are left out rather than refusing it (a paste's vertex-only surface voxels just
        /// outside the site).
        /// </summary>
        public static Func<int, bool> VoxelPermission(ulong id, ulong playerGuid, uint zone)
        {
            (uint level, uint cx, uint cy, uint cz) = TerrainStore.Decode(id);
            if (level != TerrainEdits.Level)
                return _ => false;
            double size = TerrainEdits.CellSize, v = TerrainEdits.VoxelSize;
            double ox = cx * size, oy = cy * size, oz = cz * size;
            if (!Spire.MayEdit(playerGuid, zone, (ox, oy, oz), (ox + size, oy + size, oz + size)))
                return _ => false;
            var check = Claims.BuildCheckIn(playerGuid, zone, (ox, oy, oz), (ox + size, oy + size, oz + size));
            if (check == null)
                return _ => true;
            return i =>
            {
                int y = i % BlockData.Size, x = i / BlockData.Size % BlockData.Size, z = i / (BlockData.Size * BlockData.Size);
                return check(ox + (x + 0.5) * v, oy + (y + 0.5) * v, oz + (z + 0.5) * v);
            };
        }

        // ---- sending cells -----------------------------------------------------------------------------------------------

        /// <summary>Sends one player the server's version of a cell (undoes a refused local edit).</summary>
        public static void Resend(WorldSession session, ulong id)
        {
            uint zone = session.ZoneId;
            // Every layer is sent (empty where the server has nothing), so this undoes the edit even in a new cell.
            byte[] update = Env.BuildCellsUpdate(id, zone, always: true);
            if (update != null)
                session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, update);
        }

        /// <summary>
        /// New or changed vein cells (layer 2): every player on the landscape gets a 21-byte
        /// ModifiedCellManifestUpdate and their client fetches the cell when it draws it (no voxels pushed).
        /// </summary>
        public static void Announce(List<ulong> cellIds, uint zone, WorldSession except = null)
        {
            var entries = new List<(ulong Id, uint Version)>();
            foreach (ulong id in cellIds)
                entries.Add((id, Env.LayerVersions(id, zone)[TerrainEdits.DigLayer]));
            foreach (WorldSession session in PlayerManager.InZone(zone == 0 ? Home : zone))
                if (session != except)
                    foreach ((ulong id, uint version) in entries)
                        SendManifestEntry(session, id, TerrainEdits.DigLayer, version, 0);
        }

        /// <summary>Players within this many metres of a changed cell get its voxels straight away.</summary>
        private const float FullUpdateRange = 256f;

        /// <summary>
        /// Sends changed cells out. Nearby players get the voxel data (they can see it); everyone else a 21-byte
        /// ModifiedCellManifestUpdate per layer the server holds, so their client fetches the cell when it comes into
        /// view. The builder is left out (<paramref name="except"/>): their client already holds the edit in layer 1
        /// and updated its own manifest when the edit was confirmed (HandleCellUpdateResponse FUN_143f52b60); data
        /// sent for a cell they are still editing makes it refuse new edits.
        /// </summary>
        public static void Broadcast(IEnumerable<ulong> cellIds, uint zone = 0) => Broadcast(cellIds, null, zone);

        public static void Broadcast(IEnumerable<ulong> cellIds, WorldSession except, uint zone = 0)
        {
            List<ulong> ids = cellIds.ToList();
            // Every stored-voxel change passes here: the creature navigation's site grids meeting these cells are due a
            // rebuild (Game/Pathing, OPEN-LEADS L133; server-internal, nothing is sent for it).
            Pathing.CellsChanged(zone, ids);
            // Nobody else on the landscape (building alone): no cell data to build and compress.
            var viewers = PlayerManager.InZone(zone == 0 ? Home : zone).Where(s => s != except).ToList();
            if (viewers.Count == 0)
                return;
            var changed = new List<(ulong Id, Vector3 Centre, uint[] Versions, byte[] Update)>();
            // One fetch for all of them from a remote Environment Server (in-process: the same loop as before).
            IReadOnlyList<byte[]> updates = Env.BuildCellsUpdates(ids, zone, always: true);
            for (int i = 0; i < ids.Count; i++)
            {
                (Vector3 min, Vector3 max) = TerrainStore.LocalBounds(ids[i]);
                changed.Add((ids[i], (min + max) / 2, Env.LayerVersions(ids[i], zone), updates[i]));
            }
            foreach (WorldSession session in viewers)
                foreach ((ulong id, Vector3 centre, uint[] versions, byte[] update) in changed)
                {
                    // No body (a remote service away, terrain-service.md §6): a note, so the client fetches it later.
                    if (update != null && Vector3.Distance(session.Position, centre) <= FullUpdateRange)
                        session.EnqueueMessage(ZoneMessageOpcode.TerrainCellCellsUpdate, update);
                    else
                        for (uint layer = 0; layer < TerrainEdits.LayerCount; layer++)
                            if (versions[layer] != 0)
                                SendManifestEntry(session, id, layer, versions[layer], 0);
                }
        }

        // ---- manifests -----------------------------------------------------------------------------------------------

        /// <summary>
        /// TerrainCell 0x0B ModifiedCellManifestUpdate (reader FUN_14309c1b0): u64 cell, u32 layer code (1-4 = set 0
        /// layers 0-3, 5-8 = set 1), u32 version, u8 flag. Tells clients in the world a cell changed so they fetch it.
        /// This one lists every layer of a cell of the captured store (dev mesh tests).
        /// </summary>
        public static void SendManifestUpdate(WorldSession session, ulong cellId)
        {
            TerrainStore.Cell cell = TerrainStore.Get(cellId);
            if (cell == null)
                return;
            foreach ((uint layerId, TerrainStore.Layer layer) in cell.Layers)
                SendManifestEntry(session, cellId, layerId, layer.Version, layer.Flag);
        }

        /// <summary>One ModifiedCellManifestUpdate entry (code = layer + 1 for set 0).</summary>
        public static void SendManifestEntry(WorldSession session, ulong cellId, uint layer, uint version, byte flag)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.WriteBytes(new byte[2]);
            writer.Write(cellId);
            writer.Write(layer + 1);
            writer.Write(version);
            writer.Write(flag);
            session.EnqueueMessage(ZoneMessageOpcode.TerrainCellModifiedCellManifestUpdate, output.ToArray());
        }

        /// <summary>
        /// Sends the manifest of cells this server changed (TerrainCell 0x0A); the client then requests those cells.
        /// </summary>
        public static void SendManifest(WorldSession session)
        {
            // The ore veins around the player first, so their cells are listed here (see OreVeins.EnsureAround); the
            // other players on the landscape get a note for the new ones.
            List<ulong> made = Env.EnsureVeinsAround(session.ZoneId, session.Position);
            if (made.Count > 0)
                Announce(made, session.ZoneId == 0 ? Home : session.ZoneId, session);
            session.EnqueueMessage(ZoneMessageOpcode.TerrainCellModifiedCellManifest, Env.BuildManifest(session.ZoneId));
        }

        /// <summary>
        /// Coarse copies changed (Environment Server, after a refresh; background thread): every player on the
        /// landscape gets a manifest note per copy.
        /// </summary>
        public static void LodCopiesChanged(uint zone, uint layer, IReadOnlyList<(ulong Cell, uint Version)> copies)
        {
            foreach (WorldSession viewer in PlayerManager.InZone(zone))
                foreach ((ulong id, uint version) in copies)
                    SendManifestEntry(viewer, id, layer, version, 0);
        }

        /// <summary>
        /// A build site was placed (global dm box): the veins touching it move elsewhere (OreVeins.ClearSite) and every
        /// player on the landscape gets a note for the changed ore cells.
        /// </summary>
        public static void ClearVeinsInSite(uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            List<ulong> changed = Env.ClearVeinsInSite(zone, min, max);
            if (changed.Count > 0)
                Announce(changed, zone == 0 ? Home : zone);
        }

        // ---- world tick ----------------------------------------------------------------------------------------------

        /// <summary>The heal (TerrainEdits.Regenerate): cells that grew back go to the players in range.</summary>
        public static void Regenerate()
        {
            foreach ((uint zone, List<ulong> cells) in Env.Regenerate())
            {
                HealedCells += cells.Count;
                LastHeal = DateTime.UtcNow;
                Broadcast(cells, zone);
            }
        }

        /// <summary>Admin monitor (/admin/monitor): cells the heal has given back since start, and when it last did.</summary>
        public static long HealedCells { get; private set; }
        public static DateTime? LastHeal { get; private set; }

        /// <summary>Ore veins around the players (OreVeins.Update): new or moved ore cells are announced.</summary>
        public static void UpdateVeins()
        {
            foreach ((uint zone, List<ulong> cells) in Env.UpdateVeins(playerPositions))
                Announce(cells, zone);
        }

        /// <summary>
        /// Every player's landscape and position, read only when the vein update runs (one iterator for every tick: the
        /// update is due every 2 s, the tick runs far more often).
        /// </summary>
        private static readonly IEnumerable<(uint Zone, Vector3 Position)> playerPositions = PlayerPositions();

        private static IEnumerable<(uint Zone, Vector3 Position)> PlayerPositions()
        {
            foreach (WorldSession session in PlayerManager.Snapshot())
                yield return (session.ZoneId, session.Position);
        }

        // ---- a remote Environment Server away (terrain-service.md §6) ----------------------------------------------------

        /// <summary>
        /// Cells a player was answered "up to date" for although the server holds them (the service was away and the body
        /// wasn't cached): when it is back each gets a ModifiedCellManifestUpdate per held layer, so the client fetches it
        /// (spec §6 "the zone remembers (player, cell)").
        /// </summary>
        private static readonly Dictionary<WorldSession, HashSet<(uint Zone, ulong Cell)>> owed = new();

        public static void OweRefetch(WorldSession session, ulong cell)
        {
            lock (owed)
            {
                if (!owed.TryGetValue(session, out var cells))
                    owed[session] = cells = new HashSet<(uint, ulong)>();
                cells.Add((session.ZoneId, cell));
            }
        }

        /// <summary>The service is back (world thread): the owed notes go out to the players still on that landscape.</summary>
        public static void PayOwedNotes()
        {
            List<KeyValuePair<WorldSession, HashSet<(uint Zone, ulong Cell)>>> due;
            lock (owed)
            {
                due = owed.ToList();
                owed.Clear();
            }
            int notes = 0;
            foreach ((WorldSession session, HashSet<(uint Zone, ulong Cell)> cells) in due)
            {
                if (session.Disconnected)
                    continue;
                foreach ((uint zone, ulong cell) in cells)
                {
                    if (zone != session.ZoneId)
                        continue;
                    uint[] versions = Env.LayerVersions(cell, zone);
                    for (uint layer = 0; layer < versions.Length; layer++)
                        if (versions[layer] != 0)
                        {
                            SendManifestEntry(session, cell, layer, versions[layer], 0);
                            notes++;
                        }
                }
            }
            if (notes > 0)
                log.Info($"Environment Server back: {notes} manifest note(s) for cells answered while it was away.");
        }

        /// <summary>Client text 562155 "Unable to process the request. Connection to the Build SiteServer is unavailable."</summary>
        public const uint UnavailableText = 562155;

        /// <summary>
        /// A build request was refused because a remote Environment Server is away (spec §6): the player is told with
        /// <b>ClaimsPacketLostClaimServerConnection 0xF9/0x12</b> (<see cref="Claims.SendLostServerConnection"/>, at most every
        /// 10 s per player). Text 562155 is the client's own text for that packet — CodeStringMappings.txt:97 maps
        /// "Claim.LostClaimServerConnection" to it and the Claims dispatcher shows it on 0x12 (OPEN-LEADS L142) — so this
        /// is how live showed it [P for the mapping; I that live sent it for build edits too, the claim server holding
        /// build permission]. It replaces VoxelConstruction 0x1B ZoneDisplayNotification {u8 sub, u32 text, bool}
        /// (reader 0.695 FUN_14315b9d0, handler FUN_1431614a0: ShowSystemMessage with the bool set), which carried the
        /// same text before; ZoneDisplayNotification's live texts stay open (L126).
        /// </summary>
        public static void SendUnavailable(WorldSession session) => Claims.SendLostServerConnection(session);

        // ---- Voxel Vision ----------------------------------------------------------------------------------------------

        /// <summary>
        /// ClientOwnershipRequest (C→S 0xF4 sub 4): answers every listed cell — up to date when the client's cached
        /// version matches, otherwise the cell's grid (an empty one where nobody built). Formats: VoxelOwnership.
        /// </summary>
        public static void HandleOwnershipRequest(WorldSession session, byte[] body)
        {
            var cells = new List<(ulong Cell, uint Version)>();
            try
            {
                int p = 0;
                int n = BitConverter.ToInt32(body, p);
                p += 4;
                if (n < 0 || n > 20000 || p + n * 12 > body.Length)
                {
                    ownershipLog.Warn($"{session.CharacterName}: voxel ownership request with {n} cell(s) in {body.Length} bytes: {BitConverter.ToString(body, 0, Math.Min(body.Length, 48))}");
                    return;
                }
                for (int i = 0; i < n; i++, p += 12)
                    cells.Add((BitConverter.ToUInt64(body, p), BitConverter.ToUInt32(body, p + 8)));
            }
            catch (ArgumentException)
            {
                return;
            }

            (List<ulong> upToDate, var data) = Env.OwnershipQuery(session.ZoneId, cells);

            var names = DatabaseManager.Instance.CharacterDatabase.GetCharacterNames(data.SelectMany(d => d.Owners).Distinct());
            if (upToDate.Count > 0)
            {
                var output = new List<byte>();
                var writer = new GamePacketWriter(output);
                writer.Write(VoxelOwnership.UpToDateSub);
                writer.Write(upToDate.Count);
                foreach (ulong cell in upToDate)
                    writer.Write(cell);
                session.EnqueueMessage(ZoneMessageOpcode.VoxelConstructionBase, output.ToArray());
            }
            foreach ((ulong cell, uint version, byte[] raw, List<ulong> owners) in data)
                session.EnqueueMessage(ZoneMessageOpcode.VoxelConstructionBase, BuildOwnershipData(cell, version, raw, owners, names));
            ownershipLog.Debug($"{session.CharacterName}: voxel ownership of {cells.Count} cell(s): {upToDate.Count} up to date, " +
                $"{data.Count} sent ({data.Count(d => d.Raw != null)} with builders).");
        }

        /// <summary>One cell's EnvOwnershipDataResponse (sub 0x17).</summary>
        private static byte[] BuildOwnershipData(ulong cell, uint version, byte[] owner, List<ulong> owners, Dictionary<ulong, string> names)
        {
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(VoxelOwnership.DataSub);
            writer.Write(0ul);
            writer.Write(cell);
            byte[] blob = owner == null ? Array.Empty<byte>() : TerrainStore.Pack(VoxelOwnership.GridBytes(cell, owner));
            writer.Write(blob.Length);
            if (blob.Length > 0)
                writer.WriteBytes(blob);
            // Index 0 is "nobody" (never a voxel's owner).
            writer.Write(owners.Count + 1);
            writer.Write("");
            foreach (ulong guid in owners)
                writer.Write(names.GetValueOrDefault(guid, ""));
            writer.Write(owners.Count + 1);
            writer.Write(0ul);
            foreach (ulong guid in owners)
                writer.Write(guid);
            writer.Write(0);
            writer.Write(version);
            return output.ToArray();
        }
    }
}
