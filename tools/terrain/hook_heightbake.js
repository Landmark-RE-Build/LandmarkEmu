// Read-only bake: sample the client's own heightmap terrain function (FUN_143fc3e00, ContinentMapHeightmapTerrain
// vtable +0x58) on a regular grid and stream heights + surface material ids to the runner. Samples are taken inside
// the sampler's own calls (on the terrain worker threads), a chunk at a time, so it never runs on a foreign thread.
const mod = Process.getModuleByName("Landmark64.exe");
const rva = a => mod.base.add(ptr(a).sub(ptr("0x140000000")));
const target = rva("0x143fc3e00");
const sig = ['pointer', 'double', 'double', 'double', 'pointer', 'pointer', 'pointer', 'pointer', 'int', 'double'];
const orig = new NativeFunction(target, 'double', sig);
const OX = 25584000, OZ = 25564500, OY = 20000;
const STEP = 20;            // dm (2 m)
const N = 4000;             // samples per side, centred on the zone origin
const ROWS_PER_CHUNK = 2;
const p5 = Memory.alloc(0x400), p6 = Memory.alloc(16), p7 = Memory.alloc(16), p8 = Memory.alloc(16);
let row = N, busy = false, started = Date.now();   // idle until start()
Interceptor.replace(target, new NativeCallback(function (t, x, z, scale, a5, a6, a7, a8, lod, p10) {
  const h = orig(t, x, z, scale, a5, a6, a7, a8, lod, p10);
  if (!busy && row < N) {
    busy = true;
    const rows = Math.min(ROWS_PER_CHUNK, N - row);
    const heights = new Float32Array(rows * N), mats = new Uint16Array(rows * N);
    for (let r = 0; r < rows; r++) {
      const gz = OZ + ((row + r) - N / 2) * STEP;
      for (let c = 0; c < N; c++) {
        const gx = OX + (c - N / 2) * STEP;
        p6.writeU64(0); p7.writeU32(0);
        const y = orig(t, gx, gz, 40, p5, p6, p7, p8, 2, 1.7976931348623157e308);
        heights[r * N + c] = p6.readU8() ? (y - OY) / 10 : NaN;
        mats[r * N + c] = p7.readU32();
      }
    }
    send({ row, rows }, heights.buffer);
    send({ row, rows, mats: true }, mats.buffer);
    row += rows;
    if (row % 200 == 0 || row >= N) send({ progress: row, secs: (Date.now() - started) / 1000 });
    busy = false;
  }
  return h;
}, 'double', sig));
// rpc start(): sample the grid again (after travelling to another continent, whose terrain the hooked object now is).
rpc.exports = { start() { row = 0; started = Date.now(); return true; } };
send({ ready: true });
