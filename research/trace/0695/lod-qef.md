# Coarse LOD copies: VoxelFarm::updateBlockLOD ported (why Mesh Shapes went blocky at a distance)

Written 2026-11-01 after the user's report: Mesh Shapes (stacked pyramids, cylinders) look smooth up close, turn blocky /
stepped when the player moves away, and smooth again when coming closer. Fix on branch `local/lod-qef` (worktree
re/wt-lod), files `LandmarkEmulator.EnvServer.Core/Terrain/TerrainLod.cs` and `VoxelFarmQef.cs`; docs/terrain.md
"Coarse levels". Lead: OPEN-LEADS L264.

Marks: [P] proven (read in the decompile / asm, or measured), [I] inferred. Addresses are 0.695 unless marked.

## 1. Cause

- **The client never builds coarse block data [P].** VoxelFarm::updateBlockLOD is FUN_142aadbb0 (profiler string at
  BlockData.cpp line 0x951; 0.589 FUN_142a15480, the address docs and code used to cite). It and
  updateBlockLODDeprecated (FUN_142aac2b0, 0.589 FUN_142a13b80) have no call, LEA or pointer in Landmark64.exe:
  re/tools/refscan.py finds only their .rdata / .pdata entries. ClientCellRequest (FUN_14401a660) asks for any level
  the manifest lists. So beyond the level-2 ring the client draws exactly the server's copies.
- **The server's copies dropped most vertices [P].** The old TerrainLod.Downsample wrote a parent voxel only where its
  corner child (2P) had a material. Otherwise it wrote nothing, and where it did write, the vertex was a mass point. The
  client draws a cube whose voxel has no bit 1 at its centre (ContourEx FUN_142ac4590). On the user's 98 build-layer
  cells (landscape 464, a copy of the pack at <scratch>\texcheck\464):
  - 55 % of the level-3 surface cubes had no vertex;
  - by face: -x 85 %, -z 99 %, underside 96 %, against +x 20 % and +z 1 %. The client stores a cube's vertex at its low
    corner, which is air on faces whose solid side is +axis;
  - result: 0.8 m treads with 2.3 m risers on the pyramids, and 1.6 m blocks at level 4.
- **Where it shows [P].** The scene constructor FUN_143f8aa40 sets the clipmap radius preset to 1 (+0x5a88). The table at
  0x14499d2c0 has presets {4}, {6,6,6}, {12,12,12}, so the radii are {6,6,6} cells for levels 2-4. The setter
  FUN_143f9a6d0 is unreferenced. The level boxes are built by FUN_142b1ced0, called from FUN_142ae6c50: a box along x,
  y and z from the camera's level-2 cell, each coarser one at least a cell around the finer one. Reach: level 2 to
  80-112 m, level 3 to 160-224 m, level 4 to 320-448 m, level 5 to 448-704 m, level 6 to 704-1216 m. The blocky switch
  is the level 2 → 3 change at about 100 m.

## 2. The client's rule (what the port does)

The decompile is part_0052.c:33270-34375, with a copy at <scratch>\texcheck\updateBlockLOD_695.c.

- **Signature [P].** FUN_142aadbb0(pred, store, u64* parentId, x0, y0, z0, x1, y1, z1):
  - pred->vtbl[0](index) says whether a parent voxel is written. If it is false, a voxel next to an already written
    one still runs (the "written" mask, local_fae8).
  - store->vtbl+0x18(id, create) returns a block.
  - The box is in parent voxels, widened by 2 and clamped to 0..39.
  - A level-2 parent returns at once.
  - A full rebuild is pred = true and the whole box. That is what the port does, so the written mask has no effect.
- **Order [P].**
  - Octants: oz outer, ox, oy inner. An octant whose child block is missing is skipped. The parent block is fetched with
    create = 1 at the first child found.
  - Parent voxels: z, x, y over 20 per axis.
  - Children of a parent voxel: dz, dx, dy.
  - Voxel index (z·40 + x)·40 + y. Flags at +0, u16 material at +64000, offsets at +0x2ee00.
- **Per child [P]** (m = material if bit 0, else 0xFFFE; the child at d = 0 sets the starting material):
  - No bit 1: code = bit 0. Record it if nothing is chosen and best ≤ code; ties go to the later child.
  - Bit 1: code |= 2, and FUN_142aa7820 gives the cube's quads (§3).
    - No quads: record, with the vertex at the centre (0.5).
    - Quads: v = (byte·(1/85) − 1)·0.5 + d·0.5 per axis. The reciprocal is computed first:
      DAT_144c41178 / DAT_144c401b8 at :433. Each quad (t0, t1, t2) is appended as two triangles (v, s(t0), s(t1)) and
      (v, s(t1), s(t2)), with s(t) = (float)((double)(t·0.5f) + d·0.5).
    - Vertex-only child (m = 0xFFFE): skipped once something is chosen, else code 6 and record.
    - Solid child: a candidate if something is chosen, else if FUN_142aa9a00 returns 0 (thin). If it is not a
      candidate: code 7 and record.
    - Candidate: the consistency check over o ∈ {−1,0}³ \ 0 (order oz, ox, oy, inside the box). It fails where
      hasVertex[P+o] is set and the parent's bit 0 at P+o differs from bit 0 of child c+o (a missing block is
      skipped). If an earlier choice was consistent and this one is not, the child is skipped. Otherwise it is chosen:
      material = m, best = 3, best vertex = v, best child = c. Consistency is remembered.
- **Correction to the first trace [P].** The second agent's reading, "the offset bit is set only if a child produced
  triangles", is wrong. A child with bit 1 but no quads keeps code (f&1)|2 (:422, then the jump to the shared record step at
  0x142aae827 (:432) with the code unchanged). So the parent gets bit 1 whenever any child has bit 1, and its vertex is the centre when no child
  had quads.
- **Vertex [P].**
  - It is solved only for 0 < triangles < 64 (asm 0x142aae90d: rows = n/3, `cmp edx, 0x40; jge`). Otherwise the best
    child's vertex is used, unclamped.
  - Rows: float normal n = cross(v1 − v0, v2 − v0), in the client's component formula. The length is
    sqrtf((ny² + nx²) + nz²) (asm 0x142aaea44). The normal is normalised (1.0f / len) when len > 1e-7f.
  - b = float((nx·(v0x − 0.5f) + ny·(v0y − 0.5f)) + nz·(v0z − 0.5f)) (asm 0x142aaeb60).
  - SVD solve (§4), then p = x + 0.5.
  - If any axis is in [−0.7, 0) or (1, 1.3], take the corner c = (p > 0.5 ? 1f : 0f) per axis, set
    b' = float(n · (v0 − c)), solve again on a fresh copy of A, and p = c + x' with no [0, 1] clamp. Otherwise clamp
    each axis to [0, 1] (NaN → 1).
  - Then clamp to [−1, 2] (NaN → 2).
- **Writes [P].**
  1. hasVertex[P] = chosen.
  2. If chosen: each lower neighbour Q = P + o inside the box, if the choice was consistent or hasVertex[Q] is 0, gets
     hasVertex[Q] = 1. Its vertex comes from n = bestC + o (u = byte·(1/85) − 1 with bit 1, else 0.5). Per axis it is
     (u + (bestC & 1))·0.5 for o = 0. For o = −1 it is (u + 1)·0.5 when bestC is even and 1.0 when it is odd. That is
     n's vertex in Q's frame, clamped at Q's far face. Only the offset and bit 1 are written.
  3. The voxel's own write. Material: 0xFFFE gives material 0 with bit 0 cleared, otherwise the material with bit 0 set.
     Offset: if best & 2 is clear, 0,0,0 with bit 1 cleared; otherwise Quant(p) with bit 1 set.
     Quant(v) = floor((clamp(v, −1, 2) + 1)·85 + 0.5).
  - Every writer of a voxel comes after its own write. Its writers are P + {0,1}³, which come later in the
    z, x, y / octant order. So the last neighbour write wins.
- **Neighbour step FUN_142a75720 [P]** (part_0051.c:176396). A local coordinate outside 0..39 moves to the next cell
  (floor division by 40). A negative cell coordinate gives the invalid id DAT_145eb4710, a runtime global in .bss; the
  port treats it as "no block".

## 3. Quads and the thin test

- **FUN_142aa7820 (param_8 = 1) [P]** (part_0052.c:29551, copy aa7820.c):
  - The default material def is 0 when a voxel of c + {0,1}³ inside c's own cell has bit 0 and a material other than 0.
    The check is only `< 0x28`, with no wrap. Otherwise def is 0xFFFE.
  - Each end of an edge has its material if bit 0 is set, else def. A missing block also gives def.
  - The edge counts when ma ≠ mb and one of them is def.
  - For a counted edge, the 3 other cubes around it (table 0x144a5ffc0) each give offset + u, with u = byte·(1/85) − 1
    with bit 1, else 0.5. All three blocks must exist.
  - The triangle is (V0, V2, V1) when ma = def, else (V0, V1, V2).
  - Edge table 0x144a60170 and cube table 0x144a5ffc0 were read from the exe and match the trace.
  - The port reads the cube's 8 corners once and returns early when all 8 agree; the values are the same.
- **FUN_142aa9a00 / FUN_142aa9830 [P]** (copies aa9a00.c, part_0052.c:30846).
  - aa9a00 returns 1 ("thick") when some q within ±2 has material m and aa9830(q) = 0. In both functions unset voxels
    and missing blocks count as 0.
  - aa9830(q) = 1 when a voxel of the cube q..q+1 has w ≠ m with w = 0 or m = 0.
  - The asm uses the 4-argument wrap at 0x142aa9a60.

## 4. The SVD (VoxelFarmQef.cs)

- Structure of Ronen Tzur's dual-contouring qef.cpp [P]:
  - factorize FUN_142aa2900, with the Householder vector FUN_142aa2e70 (LAPACK-style scaled norm);
  - unpack FUN_142aa3140;
  - diagonalize FUN_142aa37f0: chop 1e-5 (DAT_144c41090), at most 1000 passes;
  - qrstep FUN_142aa3e50: 2 columns go to FUN_142aa4aa0, a zero on the diagonal to FUN_142aa4480, a zero at the end to
    FUN_142aa4800; the shift is FUN_142aa4a20;
  - singularize FUN_142aa53d0: negative values flip V's column, then sort decreasing;
  - back-substitution FUN_142aa5700: w starts from a static zero vector DAT_145eb4b68 in .bss.
  - The caller sets singular values below 0.1 (DAT_144c41130) to 0.
- **Things the decompile hides [P asm, capstone on the exe]:**
  - The Givens rotations inlined into the QR code compute the tangent as (−1/a)·b (0x142aa4026: divsd of −1.0, then
    mulsd), not −b/a. The standalone FUN_142aa52a0 (−(a/b)) has no caller.
  - The shift's product term is (u1·v1)·(u1·v1) (0x142aa4a45 / 0x142aa4a59). Ghidra prints it as u1·v1·u1·v1.
  - Dot products accumulate one term at a time from the left (0x142aa2a42), and so does x = V·w (0x142aa58cd).
  - The Householder update is ((t·ssq)·t) + 1 (0x142aa2f07).
  - The row update in factorize is (f·tau)·ptr (0x142aa2cc9).
  - The code is SSE2 with no FMA. .NET does not fuse either, so the same order gives the same doubles [I: bit-exact
    agreement with the client was not executed, §7].
- **SolveSvd keeps w_j unscaled when d_j = 0 [P]** (part_0052.c:28340-28348: divide only if d ≠ 0, no zeroing). With the 0.1 cut
  that adds U_jᵀb along a cut direction. For planar data U_jᵀb is about 0.
- **Accuracy [P measured].** With the chop near 0 the decomposition is an exact SVD: U and V orthonormal and
  A = U·D·Vᵀ to 1e-12 over 180 random matrices, rows 1-63, including rank 1 and 2. The least-squares solution matches
  the normal equations to 1e-9. With the client's 1e-5 chop it is good to about 1e-5 (tests use 1e-4). That is the
  client's own precision.

## 5. Server integration

- **Inputs.** A parent reads its 8 children and their neighbours at the child level: up to 3 voxels deep (−2..+3 for the
  thin test). That is a 4 × 4 × 4 grid of child cells, 2P − 1 .. 2P + 2 per axis (`TerrainLod.Grid`).
  - A changed child-level cell c is read by the parents of c − 1, c and c + 1 per axis: 8 parents (`TerrainLod.Readers`,
    used for the dirty set at every level).
  - Level 3 is built from the level-2 overlays: `TerrainEdits.CopyCell` copies the arrays, or decompresses the compact
    bytes after the lock, into a reused block.
  - Level 4 and up are built from our level-3 (and up) copies [I: live built each level from the one below too; one
    store per layer, as the live manifest carried per-layer coarse versions].
- **Unchanged copies.** A rebuilt copy that is byte-identical to the old one (compact form) keeps its version, sends no
  note and dirties nothing above it. Without this every neighbour edit would announce 8 parents.
- **Versions.** From the content: 0x40000000 | (CRC-32 of the compact form & 0x1FFFFFFF), bit 0 flipped if that equals
  the version it replaces (TerrainLod.ContentVersion). Copies are in memory only and rebuilt by `BuildAll` at every
  start, and a copy reads its children's neighbours, so the children's sum (first version of the port) could come back
  after a restart for different content and the client would keep its old copy ("up to date"). The corner rule's
  versions (sum | 0x20000000) all have bit 29 set and content versions have it clear, so every client fetches each copy
  once more; sum | 0x40000000 would have equalled sum | 0x20000000 whenever bits 29 and 30 were both set (about 1 in 4
  CRC-versioned build cells). Empty copies (kept so the client gets an empty layer) read as missing one level up, as
  after a restart.
- **QEF buffers.** A 2-row solve (one quad) reads U[2][2] without writing it (FUN_142aa3140). The client keeps two
  buffers in updateBlockLOD's frame (auStack_200e8 first solve, auStack_1fae8 corner re-solve, part_0052.c:33454-33455),
  uninitialised per call. The port keeps two (Scratch.Qef / QefCorner) and zeroes rows 1-2 of both per parent
  (Scratch.BeginParent): 0 is the minimum-norm answer, and a copy no longer depends on the worker or chunk split.
- **BuildAll order.** Layer 1 (building) and layer 2 (digging) first, then layer 0 (stamps: by far the most cells).
- **Threads.** `TerrainLod.Workers` = a quarter of the cores, 1 to 4. They are dedicated BelowNormal threads, each
  taking runs of 32 parents in Morton order. Results are identical on 1 and 3 threads (hash of every copy).
- **Memory.** Each worker has an LRU of 72 child cells (more than one grid of 64, so a reused block is never one the
  current grid holds) and one parent block. That is about 27 MB per worker.
  - The first version allocated a fresh 384 KB block per read and per parent, and peaked at a 9.7 GB working set on
    landscape 391 (large-object heap). With reuse it peaks at about 260 MB in the harness, with 1.7 GB allocated over
    the build.
- **[server] InteriorAir.** 0xFFFC is read as material 0 everywhere in the port. VoxelFarm has no such rule; the guard
  keeps a stray 0xFFFC out of the copies. It is no longer stored (TerrainEdits.RepairInteriorAir).

## 6. Results and cost [P measured]

- **Landscape 464, the user's 98 build-layer cells** (harness <scratch>\texcheck\lodcheck2, the real
  `UpdateBlockLod`):

  | | old rule | port |
  |---|---|---|
  | level 3: surface cubes without a vertex | 16,938 of 30,980 (55 %) | 638 of 31,024 (2 %) |
  | level 4: surface cubes without a vertex | 4,405 of 7,410 (59 %) | 14 of 7,517 (0.2 %) |
  | level 3 faces lost (-x / -z / +x / +z / top) | 85 / 99 / 20 / 1 / 42 % | 0 / 0 / 0 / 0 / 14 % |

  - Level 2 itself has 2.5 % of its surface cubes without a vertex.
  - The remaining level-3 losses are underside and top cubes against unset voxels the harness counts as air (the
    generated ground is not loaded).
  - Real vertices are 0.00 m (median) and 0.01 m (p90) from the level-2 surface at level 3, and 0.01 / 0.04 m at level 4.
- **Ray-cast profiles** (out_rays_pyramid.txt, out_rays_cyl.txt):
  - The pyramid's top along x at level 3 matches level 2 within 0.02 m (old rule: 0.8 m treads). At level 4 it is
    within about 0.1 m, except the apex: 28.9-30.1 m against 29.6-30.7 m.
  - The cylinder's −x / +x walls match within 0.02 m at level 3 and 0.1 m at level 4 (old: 0.8 m steps, 1.6 m blocks).
    The top is flat at 10.80 m.
- **Real cell in the tests.** Parent 301385405E01380E (the cylinder): 848 level-3 surface cubes, all with a vertex,
  including all 524 whose low corner is air (the old rule gave those none). 553 parents are vertex-only.
- **Cost.**
  - User's shapes: 3.9 ms median and 14 ms max per level-3 parent.
  - Landscape 391 layer 0 (24,014 stamped cells, 8,218 copies): 62 s on one thread, 22.6 s on three, 60k child cells
    read. The corner rule took about 14 s.
  - Per 1,200 parents of 391: 42.6 M child cube tests (9.2 M with quads), 2.4 M QEF solves of about 30 rows
    (1.2 µs each, 29 % of the time), 7 M thin tests (8.9 voxels each, 7 %).
  - The two exact optimisations (corners read once, the in-cell fast path) cut the build by about a third. Their
    results hash identical to the plain port on 391 / 464 / 460.

## 7. Uncertainties

- **[I] Bit-exact agreement with the client is not proven by running the client's code.** The function never runs in
  the client, so Frida cannot capture it. An emulator run of FUN_142aadbb0 from the exe would settle it; unicorn is not
  installed and was not downloaded. What is proven:
  - the decompile was read line by line, with the asm checked at every associativity question;
  - the SVD is mathematically exact (tests);
  - five hand-worked parent cases (floor, ceiling, thin plate with neighbour writes, thin child material, empty) give
    the values derived from the decompile.
- **[I] Live's predicate and box.** A server probably called it with a dirty-voxel predicate and box after edits. A full
  rebuild (ours) may differ from a chain of incremental updates where the written mask and old parent contents matter.
- **[P port, I live] Block existence matters.** A quad needs all three cubes' blocks, so a neighbour cell that exists but
  is empty adds quads at a face where a missing one adds none. What live's store returned for unedited neighbours
  decides the copies at the edges of edited areas.
- **[P] Thin features move their neighbours' vertices.** In the thin-plate test the interior plate parents end at
  (170, 98, 170), the +x/+z cube corner in x and z, and the cubes under the plate become vertex-only at
  (170, 149, 170). That is the decompile's rule; nobody has looked at it in game yet.
- The side question (the triangle texture on a fresh cylinder) is lead L264.

## 8. Files

- Code and tests: TerrainLod.cs, VoxelFarmQef.cs, TerrainEdits.CopyCell; LandmarkEmulator.Tests/Rules/TerrainLodQefTests.cs.
  Test data: Data/TerrainLod/cylinder-301385405E01380E.cells, 8 level-2 cells of our own dev-world pack.
- Harnesses (scratch, not in the repo):
  - <scratch>\texcheck\lodcheck2 (Program.cs; out_all.txt, out_rays_pyramid.txt, out_rays_cyl.txt);
  - <scratch>\lodperf\run (Program.cs, Hash.cs, Prof.cs, Extract.cs, on copies of the 391 / 460 / 464 packs in
    <scratch>\lodperf\TerrainEdits).
- Disassembly helpers: <user folder>\AppData\Local\Temp\claude\<workspace>\28d489f2-5945-4c82-ba0b-9796b72f5283\scratchpad\lod
  (cdis.py, rd.py, lod.asm, svd_2900.asm, qr.asm, hh.asm).
