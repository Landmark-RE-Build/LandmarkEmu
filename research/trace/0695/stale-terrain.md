# Sections of a brush stroke that stay behind: stale layer versions (2026-10-31)

Player report (screenshot): a sphere Remove in natural terrain left a straight-edged section standing. Workflow
wf_9aff1753-514 (client versions, server repro, log timeline, skeptical review; scratch in scratchpad/healstale and
scratchpad/review47). [P] proven, [I] inferred.

## Cause [P]
- The client keeps each cell's layer versions in its CellBlocks (+0x10 + 4·layer; layer 1 at +0x14). A CellsUpdate
  installs the packet's versions (FUN_144020b50 → FUN_143fdf3a0); a local commit re-stamps layer 1 with
  CompressBlockData's version (FUN_144049a00): CRC32 of A1 B2 C3 D4 + big-endian raw length + classic zlib level 6 of
  the raw layer, 0 → 1, 0x7FFFFFFF → 0x7FFFFFFE, an empty layer (no flagged voxel, FUN_14404aa60) 0.
- ApplyDiffRequest's base for a cell = the old block's +0x14 at the cell's first diff in the stroke (FUN_143163bb0 →
  FUN_1431658b0); new = the current block's +0x14 at flush (FUN_143161c90).
- The server stamps layers it writes itself with a counter (TerrainEdits.Write: Version++ — heal ClearCells, undo /
  redo WriteCells, Delete / Cut, paste); only an applied edit adopts the client's NewVersion (Commit).
- The client re-stamps such a layer with its CRC (or 0 when empty) on a later local commit without a change — typically
  a cell inside the next stroke's brush box but outside the sphere [I strong, 23 of 42 matched]. Its next edit there
  carries that base; the stale check refused it ("not applied"), re-sent the cell, and the removed part came back
  bounded by the cell's faces.
- console_47.log: all 42 refusals had server content = exactly the client's copy: 38 base 0 on an empty server layer
  (after undo 23 / heal 8 / re-send 7), 4 non-zero bases = the CRC of the server's content, reproduced exactly with
  native classic zlib (5 of 5 vectors; .NET ZLibStream and SharpZipLib give other bytes).

## Built
TerrainEdits.HoldsClientCopy (LocalEnvironmentServer.ApplyEdit's stale check): base 0 is the same copy when the layer is
empty; any other base when ClientLayerCodec.Version(the server's layer bytes) equals it (native libz.so.1 / zlib1.dll;
without it only the empty rule). A real conflict still differs. Tests: ClientLayerVersionTests (vectors in
LandmarkEmulator.Tests/Data/ClientLayerVersions).

## Open (OPEN-LEADS L261)
13 refusals without a logged overlapping stroke (what re-stamped them); heal re-sends 0 of 11 matched (installed?);
cells with layer-2 data skip the re-stamp (FUN_144020fa0) so layer 1 may keep version 0 while non-empty; Windows hosts
without zlib1.dll (LandmarkLocal) get only the empty rule; one Response per ApplyDiffRequest (mixed heal + edit requests
send two; harmless per FUN_143162830).
