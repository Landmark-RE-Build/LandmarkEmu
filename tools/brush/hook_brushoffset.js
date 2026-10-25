// Brush preview vs ApplyDiff (L199 follow-up). Read-only.
//
// Hooks:
//   - FUN_14315acb0 (0.695 ApplyDiff writer = 0.589 FUN_1430b0510): log brush aim (+0x78) / size (+0x84) from the
//     request object when the packet is built.
//   - FUN_142fdf050 is the Box *reader*; the send path is FUN_14362a210 → box sender. We hook the common box
//     writer FUN_142fe5fe0 / related if the prologue matches; otherwise we only rely on ApplyDiff + server Info.
//
// Pair with world log lines: "brush aim X,Y,Z … → written min … min−aim …"
// Close the game before Ctrl+C (same detach rule as tools/paste and tools/terrain).

"use strict";

const mod = Process.getModuleByName("Landmark64.exe");
const IMAGE = ptr("0x140000000");
const rva = (abs) => mod.base.add(ptr(abs).sub(IMAGE));
const stamp = mod.base.add(mod.base.add(0x3c).readU32() + 8).readU32();

function prologue(abs) {
  try {
    return Array.from(new Uint8Array(rva(abs).readByteArray(16)))
      .map((b) => b.toString(16).padStart(2, "0"))
      .join(" ");
  } catch (e) {
    return String(e);
  }
}

function u32(p, o) {
  return p.add(o).readU32();
}
function i32(p, o) {
  return p.add(o).readS32();
}
function u64(p, o) {
  return p.add(o).readU64().toString();
}
function f32x4(p, o) {
  return [0, 1, 2, 3].map((i) => p.add(o + i * 4).readFloat());
}

function safe(fn, fb) {
  try {
    return fn();
  } catch (e) {
    return fb !== undefined ? fb : String(e);
  }
}

// 0.695 ApplyDiff writer (terrain-695.md). Fall back to 0.589 address if needed.
const APPLY_CANDIDATES = ["0x14315acb0", "0x1430b0510"];
const applyAbs = APPLY_CANDIDATES.find((a) => {
  const p = prologue(a);
  return p.startsWith("48 8b c4") || p.startsWith("40 55") || p.startsWith("48 89") || p.startsWith("55 48");
});
if (!applyAbs) {
  send({ error: "ApplyDiff writer prologue not found", stamp: "0x" + stamp.toString(16), tried: APPLY_CANDIDATES.map((a) => [a, prologue(a)]) });
  throw new Error("ApplyDiff writer not found");
}

// Request object layout (building.md): ApplyDiff fields live on the construction request at manager+0x38.
// Writer FUN_1430b0510 reads +0x70 cell, +0x78 pos, +0x84 size from that object. First arg is often the manager
// or the request — try both offsets from args[0].
function readAimFrom(base) {
  return safe(() => {
    const cell = u64(base, 0x70);
    const x = i32(base, 0x78), y = i32(base, 0x7c), z = i32(base, 0x80);
    const sx = i32(base, 0x84), sy = i32(base, 0x88), sz = i32(base, 0x8c);
    // Sanity: brush size typically small; voxel coords are large globals or cell-local.
    return { cell, x, y, z, sx, sy, sz };
  }, null);
}

Interceptor.attach(rva(applyAbs), {
  onEnter(args) {
    this.a0 = args[0];
    this.a1 = args[1];
  },
  onLeave() {
    // Prefer a0+0x38 (manager → request) then a0 as the request itself.
    const fromMgr = this.a0.isNull() ? null : readAimFrom(this.a0.add(0x38));
    const direct = readAimFrom(this.a0);
    const alt = this.a1.isNull() ? null : readAimFrom(this.a1);
    send({
      kind: "applyDiffWrite",
      abs: applyAbs,
      fromManagerPlus38: fromMgr,
      fromA0: direct,
      fromA1: alt,
    });
  },
});

// WorldEditing box send helpers (optional): log min/max if we hit a known writer.
const BOX_CANDIDATES = ["0x142fe5fe0", "0x142fe6340", "0x142fe64b0", "0x14362a210"];
for (const abs of BOX_CANDIDATES) {
  const p = prologue(abs);
  if (!(p.startsWith("48 8b c4") || p.startsWith("40 55") || p.startsWith("48 89") || p.startsWith("55 "))) {
    send({ kind: "skip", name: "boxish", abs, prologue: p });
    continue;
  }
  Interceptor.attach(rva(abs), {
    onEnter(args) {
      // Best-effort: many of these take min/max as xmm or pointed vec4s — dump a1/a2 floats.
      send({
        kind: "boxish",
        abs,
        a0: args[0].toString(),
        maybeMin: safe(() => f32x4(args[1], 0), null),
        maybeMax: safe(() => f32x4(args[2], 0), null),
      });
    },
  });
  send({ kind: "hooked", name: "boxish", abs, prologue: p });
}

send({
  ready: true,
  stamp: "0x" + stamp.toString(16),
  applyDiff: applyAbs,
  applyPrologue: prologue(applyAbs),
  note: "Place a few Add-tool voxels; compare applyDiffWrite aim to server 'brush aim … min−aim' Info lines.",
});
