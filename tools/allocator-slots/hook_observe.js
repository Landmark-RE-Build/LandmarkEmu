// Experiment A — read-only allocator slot counts (no patches).
// Addresses: January 2017 0.695, image base 0x140000000 (no ASLR).

"use strict";

const mod = Process.getModuleByName("Landmark64.exe");
const IMAGE = ptr("0x140000000");
const rva = (abs) => mod.base.add(ptr(abs).sub(IMAGE));

const USED = rva("0x145426dd8");
const FREE = rva("0x145426de8");
const OFF = rva("0x14542698b");
const TABLE = rva("0x145427450");
const STRIDE = 0x258;

const stamp = mod.base.add(mod.base.add(0x3c).readU32() + 8).readU32();
send({ kind: "hello", stamp: "0x" + stamp.toString(16), base: mod.base.toString() });

function sample() {
  const used = USED.readU32();
  const free = FREE.readU32();
  const off = OFF.readU8();
  let owners = 0;
  for (let i = 0; i < 64; i++) {
    if (TABLE.add(i * STRIDE).readU32() !== 0) owners++;
  }
  send({ kind: "slots", used, free, off, owners, t: Date.now() });
}

sample();
setInterval(sample, 2000);
