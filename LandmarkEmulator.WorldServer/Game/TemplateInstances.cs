using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.WorldServer.Network;
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
