using LandmarkEmulator.Internal;
using LandmarkEmulator.Internal.Environment;
using System;
using System.Collections.Generic;
using System.IO;

namespace LandmarkEmulator.WorldServer.Game.Terrain
{
    /// <summary>
    /// A coarse solidity grid of the edited voxels in a box (OPEN-LEADS L133, trace/0695/agent-leads4.md B1): what the
    /// creature navigation (Game/Pathing) lays over its walkable columns on a build site, so story creatures walk around
    /// player walls and through their doorways, and (optionally) what players dug into a Chaos Cavern outpost.
    /// <para>
    /// <b>Evidence.</b> Live creatures pathed around player builds: 2016 players saw NPCs walk along the outer walls to
    /// the next pathing node, found a 45° doorway had to be as wide as a straight one, and saw pathing stop after
    /// repainting the floor under a path (forum 56713, hotfix thread 1 Apr 2016; agent-leads2-d2.md
    /// L133) [P behaviour]. Live's navigation mesh was server-only (no class, no library, no reader in either client,
    /// OPEN-LEADS L132), so this grid is ours [I mechanism].
    /// </para>
    /// <para>
    /// <b>The rule</b> is Sight.AddRuin's (Game/Sight.cs) for the ruin templates: a block is <i>written</i> when any of its
    /// voxels is stored (flag bit 0, a material override) and <i>solid</i> when at least half of its written voxels have a
    /// material other than 0 / InteriorAir. The blocks are 3 × 1 × 3 voxels (1.2 m across, one navigation column, and one
    /// voxel = 0.4 m tall), not the ruins' 3 × 3 × 3: a ruin writes its doorways' air, a player's doorway is just unwritten
    /// air, so a 1.2 m tall block under a lintel turns solid and a 2.4 m door kept only 1.6 m of headroom (below the 2 m a
    /// creature needs) in the first test of this grid (2026-10-17); with one-voxel rows lintels, floors and stair steps keep
    /// their real heights to 0.4 m. Horizontally a thin (one-voxel) wall still fills its whole 1.2 m column, as in the ruins.
    /// The voxels are the composite the client draws: the layers 0 (server stamps), 1 (player building) and 2 (digging,
    /// temporary destruction) of every level-2 cell meeting the box, a later layer's written voxel over an earlier one's
    /// (a dig or a blast carves building voxels in layer 2, TerrainEdits.Sphere carveBuilds). Unwritten blocks keep the
    /// generated ground.
    /// </para>
    /// <para>
    /// <b>Cost.</b> Only the stored cells are read (their compact copies decompressed into a scratch buffer, flags and
    /// materials only, outside the store's lock, so nothing is expanded or kept); the grid covers the stored cells' part of
    /// the box. A whole 210 × 180 × 210 m site is at most 175 × 450 × 175 blocks = two 1.7 MB bit sets (a site built over only
    /// a part of its box: that part); a remote service sends them deflated (one answer, no voxel bodies on the wire).
    /// </para>
    /// </summary>
    public sealed class SolidityGrid
    {
        /// <summary>Block edge across (x, z) in voxels (Sight.AddRuin's, one 1.2 m navigation column) and its height (one voxel).</summary>
        public const int Block = 3, BlockY = 1;
        /// <summary>Most blocks one grid may hold (a whole site is 13.8 M).</summary>
        public const long MaxBlocks = 16_000_000;
        /// <summary>Most level-2 cells one request reads (a whole site box meets about 2,900).</summary>
        public const int MaxCells = 4096;

        /// <summary>The global voxel (0.4 m) of block (0, 0, 0)'s low corner.</summary>
        public long CornerX, CornerY, CornerZ;
        public int NX, NY, NZ;
        /// <summary>Bit sets, bit b = bx + NX (by + NY bz), LSB first (Sight.Grid's layout).</summary>
        public byte[] Written = Array.Empty<byte>(), Solid = Array.Empty<byte>();
        /// <summary>Stored cells read, written and solid blocks (for the log).</summary>
        public int Cells, WrittenBlocks, SolidBlocks;

        public bool IsEmpty => NX == 0 || NY == 0 || NZ == 0 || WrittenBlocks == 0;

        /// <summary>
        /// Builds the grid of a global dm box (voxels whose centre lies inside, as <see cref="VoxelBox.HoldsVoxel"/>) from the
        /// stored layers (<see cref="TerrainEdits.Solidity"/>). Any thread. An empty grid when nothing is stored there.
        /// </summary>
        public static SolidityGrid Build(uint zone, VoxelBox box)
        {
            double v = TerrainEdits.VoxelSize;
            const int S = BlockData.Size;
            long vx0 = (long)Math.Ceiling(box.MinX / v - 0.5), vy0 = (long)Math.Ceiling(box.MinY / v - 0.5), vz0 = (long)Math.Ceiling(box.MinZ / v - 0.5);
            long vx1 = (long)Math.Floor(box.MaxX / v - 0.5), vy1 = (long)Math.Floor(box.MaxY / v - 0.5), vz1 = (long)Math.Floor(box.MaxZ / v - 0.5);
            var grid = new SolidityGrid { CornerX = vx0, CornerY = vy0, CornerZ = vz0 };
            if (vx1 < vx0 || vy1 < vy0 || vz1 < vz0 || vx0 < 0 || vy0 < 0 || vz0 < 0)
                return grid;
            // The stored cells meeting the box, and the block range they cover.
            var stored = new List<(uint X, uint Y, uint Z)>();
            long bx0 = long.MaxValue, by0 = long.MaxValue, bz0 = long.MaxValue, bx1 = -1, by1 = -1, bz1 = -1;
            for (long cz = vz0 / S; cz <= vz1 / S; cz++)
                for (long cy = vy0 / S; cy <= vy1 / S; cy++)
                    for (long cx = vx0 / S; cx <= vx1 / S; cx++)
                    {
                        ulong cell = TerrainStore.CellId(TerrainEdits.Level, (uint)cx, (uint)cy, (uint)cz);
                        if (!TerrainEdits.HasLayers(cell, zone))
                            continue;
                        if (stored.Count >= MaxCells)
                            break;
                        stored.Add(((uint)cx, (uint)cy, (uint)cz));
                        bx0 = Math.Min(bx0, (Math.Max(cx * S, vx0) - vx0) / Block);
                        by0 = Math.Min(by0, (Math.Max(cy * S, vy0) - vy0) / BlockY);
                        bz0 = Math.Min(bz0, (Math.Max(cz * S, vz0) - vz0) / Block);
                        bx1 = Math.Max(bx1, (Math.Min(cx * S + S - 1, vx1) - vx0) / Block);
                        by1 = Math.Max(by1, (Math.Min(cy * S + S - 1, vy1) - vy0) / BlockY);
                        bz1 = Math.Max(bz1, (Math.Min(cz * S + S - 1, vz1) - vz0) / Block);
                    }
            if (stored.Count == 0)
                return grid;
            int nx = (int)(bx1 - bx0 + 1), ny = (int)(by1 - by0 + 1), nz = (int)(bz1 - bz0 + 1);
            long blocks = (long)nx * ny * nz;
            if (blocks <= 0 || blocks > MaxBlocks)
                return grid;
            // The grid starts at the first covered block (still on the box's block lattice).
            long gx0 = vx0 + bx0 * Block, gy0 = vy0 + by0 * BlockY, gz0 = vz0 + bz0 * Block;
            var written = new byte[blocks];
            var solid = new byte[blocks];
            var state = new byte[BlockData.Count];
            var flags = new byte[BlockData.Count];
            var materials = new ushort[BlockData.Count];
            foreach ((uint cx, uint cy, uint cz) in stored)
            {
                ulong cell = TerrainStore.CellId(TerrainEdits.Level, cx, cy, cz);
                if (!TerrainEdits.Solidity(cell, zone, state, flags, materials))
                    continue;
                grid.Cells++;
                for (int i = 0; i < BlockData.Count; i++)
                {
                    byte s = state[i];
                    if (s == 0)
                        continue;
                    // y fastest (BlockData.Index).
                    int y = i % S, x = i / S % S, z = i / (S * S);
                    long gvx = (long)cx * S + x, gvy = (long)cy * S + y, gvz = (long)cz * S + z;
                    if (gvx < vx0 || gvx > vx1 || gvy < vy0 || gvy > vy1 || gvz < vz0 || gvz > vz1)
                        continue;
                    long b = (gvx - gx0) / Block + nx * ((gvy - gy0) / BlockY + (long)ny * ((gvz - gz0) / Block));
                    if (b < 0 || b >= blocks)
                        continue;
                    if (written[b] < 255)
                        written[b]++;
                    if (s == 2 && solid[b] < 255)
                        solid[b]++;
                }
            }
            grid.CornerX = gx0;
            grid.CornerY = gy0;
            grid.CornerZ = gz0;
            grid.NX = nx;
            grid.NY = ny;
            grid.NZ = nz;
            grid.Written = new byte[(blocks + 7) / 8];
            grid.Solid = new byte[(blocks + 7) / 8];
            for (long b = 0; b < blocks; b++)
            {
                if (written[b] == 0)
                    continue;
                grid.Written[b >> 3] |= (byte)(1 << (int)(b & 7));
                grid.WrittenBlocks++;
                if (solid[b] * 2 >= written[b])
                {
                    grid.Solid[b >> 3] |= (byte)(1 << (int)(b & 7));
                    grid.SolidBlocks++;
                }
            }
            return grid;
        }

        /// <summary>The Environment Server answer's payload (ours): corner (3 × u64), NX NY NZ, counts, the two bit sets deflated.</summary>
        public void Write(InternalWriter w)
        {
            w.U64((ulong)CornerX).U64((ulong)CornerY).U64((ulong)CornerZ).U32((uint)NX).U32((uint)NY).U32((uint)NZ)
                .U32((uint)Cells).U32((uint)WrittenBlocks).U32((uint)SolidBlocks);
            EnvWire.Packed(w, Written);
            EnvWire.Packed(w, Solid);
        }

        public static SolidityGrid Read(InternalReader r)
        {
            var g = new SolidityGrid
            {
                CornerX = (long)r.U64(), CornerY = (long)r.U64(), CornerZ = (long)r.U64(),
                NX = (int)r.U32(), NY = (int)r.U32(), NZ = (int)r.U32(),
                Cells = (int)r.U32(), WrittenBlocks = (int)r.U32(), SolidBlocks = (int)r.U32()
            };
            g.Written = EnvWire.Packed(r);
            g.Solid = EnvWire.Packed(r);
            long blocks = (long)g.NX * g.NY * g.NZ;
            if (g.NX < 0 || g.NY < 0 || g.NZ < 0 || blocks > MaxBlocks || g.Written.Length < (blocks + 7) / 8 || g.Solid.Length < (blocks + 7) / 8)
                throw new InvalidDataException($"Solidity grid {g.NX}×{g.NY}×{g.NZ} with {g.Written.Length} / {g.Solid.Length} bytes refused.");
            return g;
        }
    }
}
