// L199 placeoffset: log the client's paste-area origin vs clipboard size / turn while the
// PasteVoxel tool is up. Read-only — never changes game state.
//
// Attach with run_placeoffset.py while Landmark64.exe is in-world with a design/clipboard ready.
// Move the paste cursor (and optionally rotate); click paste once. Detach only after closing the
// game (detaching hooks from a live client can crash it — same rule as tools/terrain).
//
// Addresses are Ghidra absolute (image base 0x140000000). Verified on stamp 0x58a38fa2 (Feb 2017
// 0.695): FUN_1436cd920 prologue matches. FUN_1436cd920 reads clipboard size at cursor+0x240/244/248,
// applies dec (−1) on each axis, then writes a ConstructionArea-style origin through arg2.

"use strict";

const mod = Process.getModuleByName("Landmark64.exe");
const IMAGE = ptr("0x140000000");
const rva = (abs) => mod.base.add(ptr(abs).sub(IMAGE));

const stamp = mod.base.add(mod.base.add(0x3c).readU32() + 8).readU32();

function prologue(abs, n) {
  try {
    return Array.from(new Uint8Array(rva(abs).readByteArray(n || 16)))
      .map((b) => b.toString(16).padStart(2, "0"))
      .join(" ");
  } catch (e) {
    return String(e);
  }
}

// Expected first bytes of FUN_1436cd920 (mov rax,rsp; push rbp; …).
const AREA_PROLOGUE = "48 8b c4 55 53 56 57 41 54 41 55 41 56 41 57 48";
const AREA_ABS = "0x1436cd920";
const PASTE_SEND_ABS = "0x1436d00b0";   // paste cursor commit → on-grid / off-grid send
const ON_GRID_ABS = "0x1431642e0";      // builds ClientSelectionAreaRequest (paste)
const BOUNDS_ABS = "0x14315feb0";       // world = pos + l·R (clipboard-local → world)

if (prologue(AREA_ABS) !== AREA_PROLOGUE) {
  send({
    error: "placeoffset: FUN_1436cd920 prologue mismatch — wrong client build?",
    stamp: "0x" + stamp.toString(16),
    got: prologue(AREA_ABS),
    want: AREA_PROLOGUE,
  });
  throw new Error("FUN_1436cd920 prologue mismatch");
}

function u32(p, off) {
  return p.add(off).readU32();
}
function i32(p, off) {
  return p.add(off).readS32();
}
function f32(p, off) {
  return p.add(off).readFloat();
}
function f32x4(p, off) {
  return [0, 1, 2, 3].map((i) => f32(p, off + i * 4));
}

function safe(fn, fallback) {
  try {
    return fn();
  } catch (e) {
    return fallback !== undefined ? fallback : String(e);
  }
}

let lastAreaKey = "";
let lastAreaAt = 0;
const AREA_MIN_MS = 150;

function readCursorSizes(cursor) {
  // FUN_1436cd920: movsd/mov from +0x240 (x,y as qword) and dword +0x248 (z), then dec each.
  return {
    sizeX: u32(cursor, 0x240),
    sizeY: u32(cursor, 0x244),
    sizeZ: u32(cursor, 0x248),
    sizeMinus1: [u32(cursor, 0x240) - 1, u32(cursor, 0x244) - 1, u32(cursor, 0x248) - 1],
  };
}

function readCursorMatrix(cursor) {
  // Rows at +0x270, +0x280, +0x290, +0x2a0 (4 × vec4) — same loads as FUN_1436cd920.
  return {
    row0: f32x4(cursor, 0x270),
    row1: f32x4(cursor, 0x280),
    row2: f32x4(cursor, 0x290),
    row3: f32x4(cursor, 0x2a0),
  };
}

function matrixInts(m) {
  // Nearest int of the 3×3 turn (rows); matches what the paste request stores as i32.
  const round = (v) => Math.round(v);
  return [
    round(m.row0[0]), round(m.row0[1]), round(m.row0[2]),
    round(m.row1[0]), round(m.row1[1]), round(m.row1[2]),
    round(m.row2[0]), round(m.row2[1]), round(m.row2[2]),
  ];
}

Interceptor.attach(rva(AREA_ABS), {
  onEnter(args) {
    this.cursor = args[0];
    this.outA = args[1]; // becomes r14 — often the cell-id / area receiver
    this.outB = args[2]; // becomes rbx — voxel xyz written here (3 × i32/u32)
    this.outC = args[3];
    this.t0 = Date.now();
  },
  onLeave(retval) {
    const now = Date.now();
    const sizes = safe(() => readCursorSizes(this.cursor), null);
    const matrix = safe(() => readCursorMatrix(this.cursor), null);
    const voxels = safe(
      () => ({
        x: i32(this.outB, 0),
        y: i32(this.outB, 4),
        z: i32(this.outB, 8),
      }),
      null
    );
    const cellWords = safe(
      () => [u32(this.outA, 0), u32(this.outA, 4), u32(this.outA, 8), u32(this.outA, 12)],
      null
    );
    const key = JSON.stringify({ sizes, voxels, turn: matrix ? matrixInts(matrix) : null });
    if (key === lastAreaKey && now - lastAreaAt < AREA_MIN_MS) return;
    lastAreaKey = key;
    lastAreaAt = now;
    send({
      kind: "area",
      ms: now - this.t0,
      sizes,
      // What the server sees as request origin is built from these voxel coords + cell.
      areaVoxels: voxels,
      areaCellWords: cellWords,
      turn: matrix ? matrixInts(matrix) : null,
      row3: matrix ? matrix.row3 : null,
      note: "sizeMinus1 = values after the three decs at FUN_1436cd920+0x72",
    });
  },
});

function hookOptional(abs, name, handlers) {
  const p = prologue(abs);
  // Accept common function prologues; skip obvious mid-function bytes.
  if (!(p.startsWith("40 ") || p.startsWith("48 8b c4") || p.startsWith("48 89") || p.startsWith("55 "))) {
    send({ kind: "skip", name, abs, prologue: p });
    return;
  }
  Interceptor.attach(rva(abs), handlers);
  send({ kind: "hooked", name, abs, prologue: p });
}

hookOptional(PASTE_SEND_ABS, "pasteSend", {
  onEnter(args) {
    const cursor = args[0];
    const sizes = safe(() => readCursorSizes(cursor), null);
    const matrix = safe(() => readCursorMatrix(cursor), null);
    send({
      kind: "pasteSend",
      sizes,
      turn: matrix ? matrixInts(matrix) : null,
      row3: matrix ? matrix.row3 : null,
      flags: safe(
        () => ({
          asDelete: cursor.add(0x2e0).readU8(),
          mode: cursor.add(0x2e4).readU32(),
          offGrid: cursor.add(0x2e8).readU8(),
        }),
        null
      ),
    });
  },
});

hookOptional(ON_GRID_ABS, "onGridPaste", {
  onEnter(args) {
    // Log a few pointer args; layout varies. Correlate with the server's Info paste line.
    send({
      kind: "onGridPaste",
      a0: args[0].toString(),
      a1: args[1].toString(),
      a2: args[2].toString(),
      a3: args[3].toString(),
    });
  },
});

let lastBoundsAt = 0;
hookOptional(BOUNDS_ABS, "previewBounds", {
  onEnter(args) {
    this.a0 = args[0];
    this.a1 = args[1];
    this.a2 = args[2];
    this.a3 = args[3];
  },
  onLeave(retval) {
    const now = Date.now();
    if (now - lastBoundsAt < 300) return;
    lastBoundsAt = now;
    // Best-effort: dump first floats at a1/a2 if they look like vec3/vec4 outputs.
    send({
      kind: "bounds",
      a0: this.a0.toString(),
      out1: safe(() => f32x4(this.a1, 0), null),
      out2: safe(() => f32x4(this.a2, 0), null),
      out3: safe(() => f32x4(this.a3, 0), null),
    });
  },
});

send({
  ready: true,
  stamp: "0x" + stamp.toString(16),
  base: mod.base.toString(),
  area: AREA_ABS,
  areaPrologue: prologue(AREA_ABS),
});
