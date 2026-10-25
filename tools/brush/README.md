# Brush aim vs written voxels

Follow-up to paste L199: someone reported individual voxels landing offset from the
preview. ApplyDiff is **client-authored** — the server stores the packed cell as sent.
If the builder sees preview ≠ result, that is often **client-local** (they are not sent
their own CellsUpdate). This harness checks whether the packet’s brush aim (`+0x78`)
agrees with the voxels in the cell blobs.

## Server (after rebuild)

Each ApplyDiff logs at Info:

```text
… brush aim X,Y,Z size Sx×Sy×Sz (cell …) → written min … max … (n=…, min−aim dx,dy,dz, kind …)
```

- `min−aim 0,0,0` (and max ≈ aim+size−1 for a filled box) → aim and blob agree; server is not shifting.
- A constant `min−aim` ≠ 0 → client aim field vs blob disagree (Frida next).

## Frida

Uses the same venv as paste:

```text
tools/paste/.venv/Scripts/python.exe tools/brush/run_brushoffset.py
```

Then place a few Add-tool voxels. Compare `applyDiffWrite` JSON to the world Info lines.
Close the game before Ctrl+C.

## Important

Do **not** “fix” brush placement by shifting ApplyDiff on the server: the builder already
kept the local edit; shifting would only move what everyone else sees.
