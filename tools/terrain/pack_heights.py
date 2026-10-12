# Packs heightbake/heights.f32 + mats.u16 (from run_heightbake.py) into <gameData>/TerrainHeights<continent>.bin
# for the world server (Game/Terrain/TerrainHeights.cs). Usage: python pack_heights.py <gameData dir> <continent>
import numpy as np, struct, zlib, sys
N, STEP = 4000, 2.0
h = np.fromfile("heightbake/heights.f32", np.float32).reshape(N, N)
m = np.fromfile("heightbake/mats.u16", np.uint16).reshape(N, N)
valid = np.isfinite(h)
lo, hi = float(np.nanmin(h)), float(np.nanmax(h))
unit = 0.05
assert (hi - lo) / unit < 65535, (lo, hi)
q = np.full((N, N), 0xFFFF, np.uint16)
q[valid] = np.round((h[valid] - lo) / unit).astype(np.uint16)
def block(a):
    c = zlib.compressobj(9, zlib.DEFLATED, -15)
    d = c.compress(a.tobytes()) + c.flush()
    return struct.pack("<i", len(d)) + d
out = b"LMH1" + struct.pack("<ifffff", N, STEP, -N / 2 * STEP, -N / 2 * STEP, lo, unit) + block(q) + block(m)
path = f"{sys.argv[1]}/TerrainHeights{sys.argv[2]}.bin"
open(path, "wb").write(out)
print(f"{path}: {len(out)} bytes, y {lo:.1f}..{hi:.1f} m, valid {valid.mean()*100:.1f}%, materials {len(np.unique(m))}")
