# Paste preview vs place origin (L199)

Frida hooks for the 1–2 voxel offset between a design’s placement preview and where
the server lands the paste.

## Setup

```text
py -3.12 -m venv tools/paste/.venv
tools/paste/.venv/Scripts/python.exe -m pip install frida frida-tools
```

(Already done if `.venv` exists under this folder.)

## Run

1. Log in with **one** `Landmark64.exe` (paste tool / Designs tab ready).
2. From the repo root:

```text
tools/paste/.venv/Scripts/python.exe tools/paste/run_placeoffset.py
```

3. Move the preview, try identity and a yaw turn, click **paste** once or twice.
4. **Close the game**, then Ctrl+C the runner (detaching while the client is live can crash it — same as `tools/terrain`).

Lines go to the console and `tools/paste/placeoffset.log`.

## What to look for

| `kind` | Meaning |
|--------|---------|
| `area` | `FUN_1436cd920`: clipboard `sizes`, `sizeMinus1` (after the three `dec`s), `areaVoxels` (origin written for the request), `turn` 3×3 |
| `pasteSend` | Paste commit from the cursor (`FUN_1436d00b0`) |
| `onGridPaste` | On-grid send (`FUN_1431642e0`) — correlate with the world server’s Info paste line |
| `bounds` | Best-effort dump from `FUN_14315feb0` (preview world transform helper) |

Compare `areaVoxels` + `turn` to the server log:
`pasted … at X,Y,Z … area−copy … first voxel at …`.

## Build check

Hook refuses to attach if `FUN_1436cd920`’s prologue does not match stamp `0x58a38fa2` /
known 0.695 bytes. Other builds need a new absolute address (see `tools/terrain/hook_heightbake.js`).
