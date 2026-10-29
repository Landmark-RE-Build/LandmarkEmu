using LandmarkEmulator.PatchPacks;

namespace LandmarkEmulator.ClientLaunch
{
    /// <summary>Knobs for starting Landmark64 (in-memory patches applied while the process is suspended).</summary>
    public sealed class ClientLaunchOptions
    {
        /// <summary>
        /// Experimental: expand the thread-allocator slot table from 64 to 127 in memory (0.695 / 0.699 only).
        /// Default Off. Auto applies when the machine has 16 or more logical processors.
        /// </summary>
        public AllocatorSlotExpand AllocatorSlotExpand { get; set; } = AllocatorSlotExpand.Off;

        /// <summary>
        /// Experimental: raise contour / Contouring Job workers 3→5. Default Off. Only applied when allocator
        /// expand actually succeeds this launch (setting must be Auto/On and allocator Auto/On).
        /// </summary>
        public ContourThreadExpand ContourThreadExpand { get; set; } = ContourThreadExpand.Off;
    }

    /// <summary>Allocator + optional contour patch outcomes for one game start.</summary>
    public sealed class ClientLaunchPatches
    {
        public AllocatorPatchResult Allocator { get; init; } = AllocatorPatchResult.Skipped;
        public AllocatorPatchResult Contour { get; init; } = AllocatorPatchResult.Skipped;
    }

    /// <summary>Outcome of the optional allocator-slot patch for one game start.</summary>
    public enum AllocatorPatchStatus
    {
        /// <summary>Setting was Off, or Auto and the CPU count is below the threshold.</summary>
        Skipped = 0,
        /// <summary>All sites matched and the rewrite was applied before ResumeThread.</summary>
        Applied = 1,
        /// <summary>Sites did not match (or a write failed); the game was resumed unpatched.</summary>
        Failed = 2,
    }

    /// <summary>Result of <see cref="AllocatorSlotPatch.TryApply"/> / <see cref="GameSession.Start"/> for UI alerts.</summary>
    public sealed class AllocatorPatchResult
    {
        public AllocatorPatchStatus Status { get; init; } = AllocatorPatchStatus.Skipped;
        /// <summary>Short technical reason (which site failed); empty when Skipped/Applied.</summary>
        public string Reason { get; init; } = "";
        /// <summary>Player-facing alert when <see cref="Status"/> is Failed; empty otherwise.</summary>
        public string UserMessage { get; init; } = "";

        public static AllocatorPatchResult Skipped { get; } = new() { Status = AllocatorPatchStatus.Skipped };

        public static AllocatorPatchResult Applied(string? detail = null) => new()
        {
            Status = AllocatorPatchStatus.Applied,
            Reason = detail ?? "",
        };

        public static AllocatorPatchResult Failed(string reason) => new()
        {
            Status = AllocatorPatchStatus.Failed,
            Reason = reason,
            UserMessage =
                "Allocator slot expand could not be applied: this Landmark64.exe does not match the expected " +
                "0.695 / 0.699 code at the patch sites. The game will start without the experimental fix." +
                (string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"),
        };
    }
}
