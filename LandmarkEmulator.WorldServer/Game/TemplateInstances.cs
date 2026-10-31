using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Placed Player Studio designs — live's "template instances" (trace/0695/audit-wiki-gaps.md #5). The client names the
    /// rule in one text, result 0x1A of the construction Response: 563373 Construction.OnlyClaimOwnerMayPlaceOrCopy
    /// TemplateInstances "Only the Build Site owner may place, copy or create Designs using Designs purchased from the
    /// marketplace." (0.589 FUN_1430b6560, 0.695 part_0087.c:32798) [P]; the wiki adds visitors can't copy purchased
    /// designs on your claim [W]. The place half is BuildingClipboard's paste check (TASKS 6.45: a Player
    /// Studio copy goes only on the player's own sites). The copy half: every successful placement of a Player Studio
    /// copy is recorded here (site, box), and a copy or Save Design by anyone but the site's current owner whose selection
    /// overlaps a recorded box is refused with the same 0x1A — even with Get Inspired.
    /// <para>
    /// [I] How live forgot an instance is not known. Here a record lasts while its build site exists (a deleted or lapsed
    /// site's records are dropped when next looked at); undoing or recycling the placed blocks does not clear it (the dev
    /// command <c>psinstances clear</c> clears the records on the player's current site). World data: the world's own
    /// database when it has one (<c>site_template_instance</c>).
    /// </para>
    /// <para>
    /// <b>Voxel Vision</b> (the site window's "Templates" list, 0.695): every record is also an instance the client's
    /// terrain knows. The landscape's records go to the Environment Server (<see cref="Push"/> →
    /// IEnvironmentServer.SetTemplateInstances), which writes them into layer 1 of their cells as the client's
    /// VoxelTemplateLookup (<see cref="TemplateInstanceLookup"/>: the format and the client path). The widget then asks
    /// GetInstanceDetailsRequest (0xF0 sub 0x12 {u64 instance, u64 DAT_145452aa8 (never set: 0)}, FUN_1431138e0) for each
    /// instance it finds in the site's cells, answered by <see cref="SendDetails"/>. Until 2026-10-26 nothing carried the
    /// instances, so the list stayed empty and 0x12 was never sent (trace/0695/OPEN-LEADS.md NL2).
    /// </para>
    /// </summary>
    public static class TemplateInstances
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly object sync = new();
        private static List<SiteTemplateInstanceModel> instances;

        private static List<SiteTemplateInstanceModel> Loaded()
        {
            if (instances != null)
                return instances;
            try
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewWorldContext();
                instances = context.SiteTemplateInstance.ToList();
                log.Info($"Loaded {instances.Count} placed Player Studio design(s).");
            }
            catch (Exception e)
            {
                log.Error(e, "Failed to load placed Player Studio designs.");
                instances = new List<SiteTemplateInstanceModel>();
            }
            return instances;
        }

        private static uint Landscape(uint zone) => zone == 0 ? Terrain.TerrainStore.HomeContinent : zone;

        /// <summary>
        /// A Player Studio copy was placed (BuildingClipboard paste): remember its box (world units, as build sites) on the
        /// site under the box's centre.
        /// </summary>
        public static void Record(WorldSession session, uint zone, ulong design, (double X, double Y, double Z) min,
            (double X, double Y, double Z) max)
        {
            zone = Landscape(zone);
            double cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2, cz = (min.Z + max.Z) / 2;
            ClaimModel site = Claims.All().FirstOrDefault(c => c.ZoneId == zone && cx >= c.MinX && cx <= c.MaxX
                && cy >= c.MinY && cy <= c.MaxY && cz >= c.MinZ && cz <= c.MaxZ);
            if (site == null)
                return;   // only on a build site (the paste check allows no other place for a Player Studio copy)
            var row = new SiteTemplateInstanceModel
            {
                ZoneId = zone, SiteGuid = site.Id, TemplateGuid = design, PlacedBy = session.PlayerGuid,
                MinX = min.X, MinY = min.Y, MinZ = min.Z, MaxX = max.X, MaxY = max.Y, MaxZ = max.Z, PlacedAt = DateTime.UtcNow
            };
            // The database gets a copy: EF writes the generated Id into the object it saved, on the save's thread. The
            // in-memory row takes that Id when the save is done (under the lock); a Forget before then deletes after the
            // save, by the copy's Id. Until 2026-10-18 the same object went to both, and a Forget right after the
            // placement (psinstances clear, the site removed) deleted Id 0, so the row came back at the next start
            // (review 2026-10-18b #13).
            var copy = new SiteTemplateInstanceModel
            {
                ZoneId = row.ZoneId, SiteGuid = row.SiteGuid, TemplateGuid = row.TemplateGuid, PlacedBy = row.PlacedBy,
                MinX = row.MinX, MinY = row.MinY, MinZ = row.MinZ, MaxX = row.MaxX, MaxY = row.MaxY, MaxZ = row.MaxZ,
                PlacedAt = row.PlacedAt
            };
            Task<ulong> saved = DatabaseManager.Instance.CharacterDatabase.SaveWorld(c => c.SiteTemplateInstance.Add(copy))
                .ContinueWith(t =>
                {
                    if (!t.IsCompletedSuccessfully)
                    {
                        log.Error(t.Exception, "Failed to save a placed Player Studio design.");
                        return 0ul;
                    }
                    return copy.Id;
                }, TaskScheduler.Default);
            lock (sync)
            {
                Loaded().Add(row);
                saving[row] = saved;
            }
            // Attached after the entry is in: it always finds it.
            saved.ContinueWith(t =>
            {
                lock (sync)
                {
                    row.Id = t.Result;
                    saving.Remove(row);
                }
            }, TaskScheduler.Default);
            log.Info($"{session.CharacterName} placed Player Studio design {design} on site {site.Id}: copying it is the owner's only.");
            // The paste's own cell updates (sent after this) carry the new instance; nothing extra to send.
            Push(zone, broadcast: false);
        }

        /// <summary>GetInstanceDetailsRequest / EnvironmentInstanceDetailsResponse (TemplateBase 0xF0).</summary>
        public const byte DetailsRequestSub = 0x12, DetailsResponseSub = 0x13;

        /// <summary>
        /// The instance guid the client's terrain and 0x13 know a record by: a hash of what the record holds (landscape,
        /// site, design, placer, box), so it stays the same across restarts without a stored id. The top byte 0x7C keeps it
        /// apart from character, item and design guids [ours].
        /// </summary>
        public static ulong GuidOf(SiteTemplateInstanceModel row)
        {
            ulong hash = 14695981039346656037UL;
            void Mix(ulong value)
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (value >> (8 * i)) & 0xFF;
                    hash *= 1099511628211UL;
                }
            }
            Mix(row.ZoneId);
            Mix(row.SiteGuid);
            Mix(row.TemplateGuid);
            Mix(row.PlacedBy);
            foreach (double d in new[] { row.MinX, row.MinY, row.MinZ, row.MaxX, row.MaxY, row.MaxZ })
                Mix((ulong)BitConverter.DoubleToInt64Bits(Math.Round(d, 3)));
            return (hash & 0x00FF_FFFF_FFFF_FFFFUL) | 0x7C00_0000_0000_0000UL;
        }

        private static TemplateInstanceLookup.Instance AsInstance(SiteTemplateInstanceModel row)
            => new(GuidOf(row), row.PlacedBy, new VoxelBox(row.MinX, row.MinY, row.MinZ, row.MaxX, row.MaxY, row.MaxZ));

        /// <summary>
        /// Sends a landscape's records (those whose site still exists) to the Environment Server; with
        /// <paramref name="broadcast"/>, the cells whose building layer changed go out to the players again.
        /// </summary>
        public static void Push(uint zone, bool broadcast = true, bool announce = true)
        {
            zone = Landscape(zone);
            IEnvironmentServer env = Terrain.EnvironmentServer.Instance;
            if (env == null)
                return;
            List<TemplateInstanceLookup.Instance> list;
            lock (sync)
                list = Loaded().Where(i => i.ZoneId == zone && Claims.Get(i.SiteGuid) != null).Select(AsInstance).ToList();
            List<ulong> cells = env.SetTemplateInstances(zone, list, announce);
            if (broadcast && cells.Count > 0)
                ZoneTerrain.Broadcast(cells, zone);
        }

        /// <summary>At start-up, after the Environment Server has loaded: every landscape's records.</summary>
        public static void PushAll()
        {
            List<uint> zones;
            lock (sync)
                zones = Loaded().Select(i => i.ZoneId).Distinct().ToList();
            // Without new versions: the clients' copies were built from the same records before the restart, and new
            // versions every start re-sent every cell under every placed design (phaseE4-logic.md F10).
            foreach (uint zone in zones)
                Push(zone, broadcast: false, announce: false);
            if (zones.Count > 0)
                log.Info($"Placed Player Studio designs sent to the terrain on {zones.Count} landscape(s).");
        }

        /// <summary>
        /// EnvironmentInstanceDetailsResponse (0xF0 sub 0x13; 0.695 dispatcher FUN_14310bbf0 case 0x11, consumer
        /// FUN_14310b940) [P layout]: u8 sub, u64 (+0x18, not read by the consumer: 0), the instance (FUN_1430ff920: u64
        /// instance guid, u64 template guid (the key FUN_1431109c0 finds the design by), u64, region (FUN_1430ff0c0: u64
        /// cell, u32 × 3 voxel offset, u32 × 3 size; FUN_143537cc0 lists its cells to count and light up the voxels), u32,
        /// u64, string, bool), TemplateBasicData (FUN_1430ff530: the "Templates" group row's name, FUN_1435382e0), u32 n ×
        /// u64 cells (FUN_143101860), i32 n × PropData (none). The consumer files the design in its template cache (+0x28,
        /// not the player's Designs list +0x58) and adds the rows. [I] fields the client reads but nothing shows: the third
        /// u64 = the placer, u32 0, u64 = placed at (Unix seconds), string "", bool false.
        /// An unknown guid is not answered (the client asks each guid once, FUN_1431138e0's +0xb8 set).
        /// </summary>
        public static void SendDetails(WorldSession session, ulong instance)
        {
            SiteTemplateInstanceModel row;
            lock (sync)
                row = Loaded().FirstOrDefault(i => GuidOf(i) == instance);
            if (row == null || Claims.Get(row.SiteGuid) == null)
            {
                log.Debug($"{session.CharacterName} asked about unknown placed design {instance:X16}.");
                return;
            }
            session.EnqueueMessage(ZoneMessageOpcode.TemplateBase,
                DetailsBody(row, w => Templates.WriteBasicDataOf(w, row.PlacedBy, row.TemplateGuid)));
        }

        /// <summary>The 0x13 body of a record, <paramref name="basicData"/> writing the design's TemplateBasicData (<see cref="SendDetails"/>; tests).</summary>
        public static byte[] DetailsBody(SiteTemplateInstanceModel row, Action<GamePacketWriter> basicData)
        {
            var box = new VoxelBox(row.MinX, row.MinY, row.MinZ, row.MaxX, row.MaxY, row.MaxZ);
            (ulong cell, uint x, uint y, uint z, uint sx, uint sy, uint sz) = TemplateInstanceLookup.Region(box);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(DetailsResponseSub);
            writer.Write(0ul);
            writer.Write(GuidOf(row));
            writer.Write(row.TemplateGuid);
            writer.Write(row.PlacedBy);
            writer.Write(cell);
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
            writer.Write(sx);
            writer.Write(sy);
            writer.Write(sz);
            writer.Write(0u);
            writer.Write((ulong)Math.Max(0, new DateTimeOffset(DateTime.SpecifyKind(row.PlacedAt, DateTimeKind.Utc)).ToUnixTimeSeconds()));
            writer.Write("");
            writer.Write(false);
            basicData(writer);
            List<ulong> cells = TemplateInstanceLookup.CellsOf(box).ToList();
            writer.Write((uint)cells.Count);
            foreach (ulong c in cells)
                writer.Write(c);
            writer.Write(0u);
            return output.ToArray();
        }

        /// <summary>
        /// Does a placed Player Studio design overlap this box (world units) on a site <paramref name="player"/> does not
        /// own? Then the player may not copy or save a design from it.
        /// </summary>
        public static bool Protects(ulong player, uint zone, (double X, double Y, double Z) min, (double X, double Y, double Z) max)
        {
            zone = Landscape(zone);
            List<SiteTemplateInstanceModel> stale = new();
            bool protects = false;
            lock (sync)
                foreach (SiteTemplateInstanceModel i in Loaded().Where(i => i.ZoneId == zone && i.MinX <= max.X && i.MaxX >= min.X
                    && i.MinY <= max.Y && i.MaxY >= min.Y && i.MinZ <= max.Z && i.MaxZ >= min.Z))
                {
                    ClaimModel site = Claims.Get(i.SiteGuid);
                    if (site == null)
                        stale.Add(i);
                    else if (site.OwnerGuid != player)
                        protects = true;
                }
            Forget(stale);
            return protects;
        }

        /// <summary>The records on a site (dev: <c>psinstances</c>).</summary>
        public static List<SiteTemplateInstanceModel> OnSite(ulong site)
        {
            lock (sync)
                return Loaded().Where(i => i.SiteGuid == site).ToList();
        }

        /// <summary>Drops records (a site that is gone; dev <c>psinstances clear</c>).</summary>
        public static void Forget(List<SiteTemplateInstanceModel> rows)
        {
            if (rows.Count == 0)
                return;
            List<uint> zones = rows.Select(r => r.ZoneId).Distinct().ToList();
            var ids = new List<ulong>();
            var stillSaving = new List<Task<ulong>>();
            lock (sync)
                foreach (SiteTemplateInstanceModel row in rows)
                {
                    Loaded().Remove(row);
                    if (saving.Remove(row, out Task<ulong> save))
                        stillSaving.Add(save);   // its Id is not known yet: deleted once the insert is done
                    else if (row.Id != 0)
                        ids.Add(row.Id);
                }
            Delete(ids);
            // Gone from the client's terrain too (Voxel Vision).
            foreach (uint zone in zones)
                Push(zone);
            foreach (Task<ulong> save in stillSaving)
                save.ContinueWith(t =>
                {
                    if (t.Result != 0)
                        Delete(new List<ulong> { t.Result });
                }, TaskScheduler.Default);
        }

        /// <summary>A placed Player Studio copy's row whose insert is still running, with the task that gives its Id.</summary>
        private static readonly Dictionary<SiteTemplateInstanceModel, Task<ulong>> saving = new();

        private static void Delete(List<ulong> ids)
        {
            if (ids.Count == 0)
                return;
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c => c.SiteTemplateInstance.RemoveRange(
                    c.SiteTemplateInstance.Where(r => ids.Contains(r.Id))))
                .ContinueWith(t => log.Error(t.Exception, "Failed to delete placed Player Studio design records."),
                    TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
