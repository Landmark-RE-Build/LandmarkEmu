// Experiment E — move allocator slot table 64 → 127 (P4). Apply on frida.spawn before resume.
// Verified LEA/imm sites on stamp 0x58a38fa2 (same as Jan 2017 map in allocator-slots-options.md).

"use strict";

const mod = Process.getModuleByName("Landmark64.exe");
const IMAGE = ptr("0x140000000");
const rva = (abs) => mod.base.add(ptr(abs).sub(IMAGE));

const OLD_TABLE = ptr("0x145427450");
const SLOT = 0x258;
const NEW_COUNT = 127;
const NEW_BYTES = NEW_COUNT * SLOT; // 0x129A8
const PREFERRED = ptr("0x1461B0000");

const LEAS = [
  { abs: "0x142c050c5", add: 0 },
  { abs: "0x142c05a19", add: 0 },
  { abs: "0x142c05c5c", add: 0 },
  { abs: "0x142c070b7", add: 0 },
  { abs: "0x142c0712e", add: 0 },
  { abs: "0x142c076e0", add: 0 },
  { abs: "0x142c088fb", add: 0x238 },
  { abs: "0x142c09112", add: 0 },
];

const CAP_CMP = "0x142c050f3"; // 48 83 f9 40  → imm8 at +3
const SCAN_CMP = "0x142c05145"; // 48 81 ff 00 96 00 00 → imm32 at +3

const USED = rva("0x145426dd8");
const FREE = rva("0x145426de8");
const OFF = rva("0x14542698b");

const stamp = mod.base.add(mod.base.add(0x3c).readU32() + 8).readU32();

function hexBytes(p, n) {
  return Array.from(new Uint8Array(p.readByteArray(n)))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join(" ");
}

function leaTarget(abs) {
  const insn = rva(abs);
  const b = new Uint8Array(insn.readByteArray(7));
  // REX.W lea r??, [rip+disp32]
  if (!(b[0] === 0x48 || b[0] === 0x4c) || b[1] !== 0x8d) {
    throw new Error("not lea at " + abs + ": " + hexBytes(insn, 7));
  }
  const disp = insn.add(3).readS32();
  return insn.add(7).add(disp);
}

function patchLea(abs, newTarget) {
  const insn = rva(abs);
  const rip = insn.add(7);
  const disp = newTarget.sub(rip).toInt32();
  Memory.protect(insn, 7, "rwx");
  insn.add(3).writeS32(disp);
  Memory.protect(insn, 7, "r-x");
}

function allocTable() {
  // Frida 17+: Module.getExportByName(dll, name) is gone — use the Module instance.
  const k32 = Process.getModuleByName("kernel32.dll");
  const VirtualAlloc = new NativeFunction(k32.getExportByName("VirtualAlloc"), "pointer", [
    "pointer",
    "size_t",
    "uint32",
    "uint32",
  ]);
  const MEM_COMMIT = 0x1000;
  const MEM_RESERVE = 0x2000;
  const PAGE_READWRITE = 0x04;

  let p = VirtualAlloc(PREFERRED, NEW_BYTES, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
  if (p.isNull()) {
    for (let i = 1; i <= 64 && p.isNull(); i++) {
      p = VirtualAlloc(PREFERRED.add(i * 0x10000), NEW_BYTES, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    }
  }
  if (p.isNull()) {
    throw new Error("VirtualAlloc failed near image — need a RIP-reachable page");
  }
  for (let off = 0; off < NEW_BYTES; off += 8) {
    p.add(off).writeU64(0);
  }
  return p;
}

function applyP4() {
  // Prove every LEA still aims at the old table (or table+0x238).
  for (const L of LEAS) {
    const got = leaTarget(L.abs);
    const want = OLD_TABLE.add(L.add);
    if (!got.equals(want)) {
      throw new Error(
        "LEA " + L.abs + " targets " + got + ", expected " + want + " — wrong build?"
      );
    }
  }

  const cap = rva(CAP_CMP);
  if (hexBytes(cap, 5) !== "48 83 f9 40 73") {
    throw new Error("capacity cmp mismatch: " + hexBytes(cap, 5));
  }
  const scan = rva(SCAN_CMP);
  if (hexBytes(scan, 7) !== "48 81 ff 00 96 00 00") {
    throw new Error("scan cmp mismatch: " + hexBytes(scan, 7));
  }

  const neu = allocTable();

  for (const L of LEAS) {
    patchLea(L.abs, neu.add(L.add));
  }

  Memory.protect(cap, 5, "rwx");
  cap.add(3).writeU8(NEW_COUNT); // 0x7F
  Memory.protect(cap, 5, "r-x");

  Memory.protect(scan, 7, "rwx");
  scan.add(3).writeU32(NEW_BYTES); // 0x129A8
  Memory.protect(scan, 7, "r-x");

  // Re-check LEAs
  for (const L of LEAS) {
    const got = leaTarget(L.abs);
    const want = neu.add(L.add);
    if (!got.equals(want)) {
      throw new Error("post-patch LEA " + L.abs + " = " + got + " want " + want);
    }
  }

  send({
    kind: "p4",
    ok: true,
    stamp: "0x" + stamp.toString(16),
    oldTable: OLD_TABLE.toString(),
    newTable: neu.toString(),
    capacity: NEW_COUNT,
    capBytes: hexBytes(cap, 5),
    scanBytes: hexBytes(scan, 7),
  });

  return neu;
}

let newTable = null;
try {
  newTable = applyP4();
} catch (e) {
  send({ kind: "p4", ok: false, error: String(e), stamp: "0x" + stamp.toString(16) });
  throw e;
}

function sample() {
  const used = USED.readU32();
  const free = FREE.readU32();
  const off = OFF.readU8();
  let owners = 0;
  const base = newTable || rva("0x145427450");
  const limit = newTable ? NEW_COUNT : 64;
  for (let i = 0; i < limit; i++) {
    if (base.add(i * SLOT).readU32() !== 0) owners++;
  }
  send({ kind: "slots", used, free, off, owners, capacity: limit, t: Date.now() });
}

sample();
setInterval(sample, 2000);
