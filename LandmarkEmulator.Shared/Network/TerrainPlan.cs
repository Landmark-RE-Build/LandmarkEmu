using System.Collections.Generic;

namespace LandmarkEmulator.Shared.Network
{
    /// <summary>How the edge paces CellsUpdate delivery after the first multi.</summary>
    public enum TerrainTailPolicy
    {
        /// <summary>H16: ack-through first multi, then burst-paced singleton tail.</summary>
        AckFirstThenBurstSingleton = 0,

        /// <summary>H28 (Ghidra 1820 Q3): 1191 one-cell updates, pipelined under SOE window — no megachain overlap.</summary>
        AllSingletonAckThrough = 1,

        /// <summary>H-T8: pack all cells into few Live-sized multis (~1.6 MB), ack-gated between multis only (no singleton tail).</summary>
        LiveShapedLargeMultis = 2,
    }

    /// <summary>One cell payload in request order (zone-built; edge serializes to 0xEC000500).</summary>
    public sealed class TerrainPlanCellEntry
    {
        public ulong CellId { get; set; }
        /// <summary>
        /// The cell's version in CellsUpdate (0xEC sub 5: {u64 cell, u32 version, list of layers}; docs/protocol.md §8):
        /// version 0 = block (voxel) data that the client re-meshes (VoxelCellBlockCacheImpl::HandleCellsUpdate
        /// 0x143f52db0, docs/client-reference.md). Nothing builds a TerrainPlan yet (IZoneSessionChannel only).
        /// </summary>
        public uint Version { get; set; }
        public uint SegmentCount { get; set; } = 1;
        public uint Flags { get; set; } = 2;
        public byte[] DataBlob { get; set; }
    }

    /// <summary>
    /// Zone → edge terrain delivery intent. Zone supplies ordered cell bodies and policy;
    /// the edge terrain delivery scheduler owns SOE ack-window pacing (W2).
    /// </summary>
    public sealed class TerrainPlan
    {
        public TunnelChannel ReplyChannel { get; set; } = TunnelChannel.Zone;

        /// <summary>Cells in the first multi CellsUpdate (Phase 0b default ~818).</summary>
        public int FirstMultiSize { get; set; } = 818;

        public TerrainTailPolicy TailPolicy { get; set; } = TerrainTailPolicy.AckFirstThenBurstSingleton;

        /// <summary>
        /// Unix ms the originating ClientCellRequest was received, for the
        /// request→first-CellsUpdate delta against the client 6s SCENE request clock.
        /// In-process only; not carried over the zone→edge gRPC plan.
        /// </summary>
        public long RequestReceivedUnixMs { get; set; }

        public List<TerrainPlanCellEntry> Entries { get; } = new();
    }
}
