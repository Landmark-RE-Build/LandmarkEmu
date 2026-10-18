using LandmarkEmulator.Internal.Environment;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LandmarkEmulator.EnvServer
{
    /// <summary>
    /// The Environment Server's job counters in the client's layout (ClientEnvironmentServerMetrics; monitor page
    /// FUN_14320ea00 / FUN_143163fe0 prints, for job type 0..18, the name from FUN_143c74ef0 / FUN_143bb4ef0, Pending at
    /// +0x20 + 4·i and Active at +0x6C + 4·i, then "Total Jobs" = the two current totals at +0xB8 / +0xBC (corrected
    /// 2026-10-17, agent-leads2-d2.md L128; finished counts stay in Summary as our own extra). Active = being
    /// worked now (the service works one request at a time, so 0 or 1, plus LOD refreshes running in the background),
    /// Pending = received and waiting in the work queue.
    /// <para>
    /// Which of our operations counts under which type is [I] for 4, 5, 6 and 17 (the names only; L76 / L128): digs and
    /// heals → 4 "Cell Temporary Modifications"; cell fetches → 6 "Cell Composite Update Lod" (a CellsUpdate composes the
    /// layers and the ore); ownership lists and censuses → 17 "Misc Cell Ownership Reusable"; the Heal tool and the
    /// dev reads / writes → 7 "Construction Apply Diff Cell". Type 5 "Cell Generation" stays 0 (no server-side generation,
    /// spec §3.7 / §4.4).
    /// </para>
    /// </summary>
    public sealed class JobCounters
    {
        public static readonly string[] Names =
        {
            "Cell Poi Lod", "Cell Permanent Lod", "Cell Timed Persistent Lod", "Cell Transient Lod", "Cell Temporary Modifications",
            "Cell Generation", "Cell Composite Update Lod", "Construction Apply Diff Cell", "Construction Template",
            "Construction Undo Redo", "Construction Gather Ownership Info", "Construction Paste Template/Clipboard",
            "Construction Paste Template/Clipboard Off-grid", "Construction Clear Exported Template", "Construction Area Iterator",
            "Vein Spawn Vein", "Vein Validate Cells", "Misc Cell Ownership Reusable", "Misc Manifest Compression"
        };

        public const int TemporaryModifications = 4, CompositeUpdate = 6, ApplyDiff = 7, Template = 8, UndoRedo = 9, Ownership = 10,
            Paste = 11, AreaIterator = 14, SpawnVein = 15, ValidateVeins = 16, OwnershipReusable = 17, Manifest = 18;

        private readonly int[] active = new int[EnvMetrics.JobTypes], pending = new int[EnvMetrics.JobTypes];
        private readonly long[] done = new long[EnvMetrics.JobTypes];

        /// <summary>The job type of a request (by family / sub), or -1 when it is not a job (status, admin text).</summary>
        public static int TypeOf(ushort family, uint sub) => (family, sub) switch
        {
            (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.CellFetchSub) => CompositeUpdate,
            (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.HelloSub or EnvironmentMessages.VersionIndexSub) => Manifest,
            (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.SphereSub) => TemporaryModifications,
            (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.DestroyAreaSub or EnvironmentMessages.HealAreaSub
                or EnvironmentMessages.ClearCellSub) => AreaIterator,
            (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.EnsureVeinsSub) => SpawnVein,
            (EnvironmentMessages.TerrainCellFamily, EnvironmentMessages.DisturbVeinsSub or EnvironmentMessages.ClearVeinsSub) => ValidateVeins,
            (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.ApplyDiffSub or EnvironmentMessages.ClearCellsSub
                or EnvironmentMessages.ReadSub or EnvironmentMessages.WriteSub or EnvironmentMessages.MaterialDeltaSub) => ApplyDiff,
            (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.WriteCellsSub) => UndoRedo,   // paste: the request says 11
            (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.OwnershipSub) => Ownership,
            (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.OwnedBySub or EnvironmentMessages.CensusSub) => OwnershipReusable,
            (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.SpawnTemplateSub) => Template,
            (EnvironmentMessages.ClaimsFamily, EnvironmentMessages.RevertTerrainSub) => AreaIterator,
            // The creature navigation's solidity grid of a site box (ours; it iterates the box's stored cells) [I].
            (EnvironmentMessages.ConstructionFamily, EnvironmentMessages.SolidityOfSub) => AreaIterator,
            _ => -1
        };

        public void Queued(int type)
        {
            if (type >= 0)
                Interlocked.Increment(ref pending[type]);
        }

        public void Started(int type, bool wasQueued = true)
        {
            if (type < 0)
                return;
            if (wasQueued)
                Interlocked.Decrement(ref pending[type]);
            Interlocked.Increment(ref active[type]);
        }

        /// <summary>A job ended; <paramref name="countAs"/> moves it to another type at the end (a paste reported as undo when queued).</summary>
        public void Finished(int type, int countAs = -1)
        {
            if (type < 0)
                return;
            Interlocked.Decrement(ref active[type]);
            Interlocked.Increment(ref done[countAs >= 0 ? countAs : type]);
        }

        /// <summary>A job done in one go outside the queue (a heal pass, a LOD refresh).</summary>
        public void Count(int type, int jobs = 1)
        {
            if (type >= 0 && jobs > 0)
                Interlocked.Add(ref done[type], jobs);
        }

        public EnvMetrics Metrics(List<(uint Desired, uint Active, uint Failed, int LastId)> ruinLayers) => new()
        {
            Active = active.Select(v => (uint)System.Math.Max(0, v)).ToArray(),
            Pending = pending.Select(v => (uint)System.Math.Max(0, v)).ToArray(),
            RuinLayers = ruinLayers
        };

        /// <summary>One line for the admin page: the job types with work done or waiting.</summary>
        public string Summary()
        {
            var parts = new List<string>();
            for (int i = 0; i < Names.Length; i++)
                if (done[i] > 0 || pending[i] > 0 || active[i] > 0)
                    parts.Add($"{Names[i]} {done[i]}{(pending[i] > 0 ? $" (+{pending[i]} pending)" : "")}{(active[i] > 0 ? " *" : "")}");
            return parts.Count == 0 ? "none yet" : string.Join(", ", parts);
        }
    }
}
