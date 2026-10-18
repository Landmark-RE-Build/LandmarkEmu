# ContinentSurfaceCache<continent>.csc (client CommonData/Voxel/surfaceCache; reader FUN_14407b070 -> FUN_14407b810 in
# 0.695, FUN_143fb73c0 in 0.589) against the server's baked ground heights (gameData*/TerrainHeights<zone>.bin).
#
# Format [P layout from the reader; I meaning from the fit below, docs/terrain.md, Surface cache]:
#   u8 version 1, u32 x, u32 y, u32 z, u32 w, u32 h, then 9 LOD grids of w*h u16, row-major with z as the row; each
#   next LOD halves x, y, z and has w' = (w + 1 + (x & 1)) / 2, h' = (h + 1 + (z & 1)) / 2.
#   LOD 0 texel (row r, col c) is the 16 m column at global cell (x + c, z + r); cells are 16 m * 2^lod, so global
#   metres = cell * 16 (zone origin 2558400 / 2000 / 2556450 -> local x 16c - 4000, z 16r - 4002 for the islands).
#   u16 = (surface y cell - y) in bits 0-9 | (number of further cells the surface spans) << 10: the column's ground lies in
#   cells [y + low, y + low + count] (local metres (y + low) * 16 - 2000 ...). It is a range, not a height.
#
#   csc.py info <continent>                     header and value ranges
#   csc.py fit <zone> [continent]               how well the csc reproduces a baked grid (default continent = zone)
#   csc.py pick <zone> <continent> [...]        which surface cache a fresh bake (e.g. zone 460) was generated like:
#                                               exact-match rate of the rule below against every listed continent's
#                                               csc, overall and on the texels where the candidates differ
# Rule (406: 97.1 % of LOD 0 texels exact): low = floor((min of the bake over the texel's 16 m square widened by one
# 2 m sample) - (y * 16 - 2000) - 1) / 16); the rest are 2 m bake samples missing the 0.4 m voxel extremes.
# Paths: the environment variables GAME_DIR (the Landmark 0.695 install) and GAME_DATA_DIR (the server's game data
# folder with the TerrainHeights files), as in docker/worlds/*.env.
import os, struct, sys, zlib
import numpy as np

CSC_DIR = os.path.join(os.environ.get('GAME_DIR', '.'), 'CommonData', 'Voxel', 'surfaceCache')
GAME_DATA = os.environ.get('GAME_DATA_DIR', 'gameData')


def read_csc(continent, lod=0):
    d = open(os.path.join(CSC_DIR, f'ContinentSurfaceCache{continent}.csc'), 'rb').read()
    v, x, y, z, w, h = struct.unpack_from('<BIIIII', d, 0)
    off = 21
    for _ in range(lod):
        off += 2 * w * h
        w = (w + 1 + (x & 1)) // 2; h = (h + 1 + (z & 1)) // 2; x //= 2; y //= 2; z //= 2
    return (x, y, z, w, h), np.frombuffer(d, np.uint16, w * h, off).reshape(h, w)


def read_bake(zone):
    d = open(os.path.join(GAME_DATA, f'TerrainHeights{zone}.bin'), 'rb').read()
    assert d[:4] == b'LMH1', 'not a TerrainHeights grid'
    n, step, minx, minz, base, unit = struct.unpack_from('<ifffff', d, 4)
    off = 28
    blocks = []
    for _ in range(2):
        size = struct.unpack_from('<i', d, off)[0]; off += 4
        blocks.append(np.frombuffer(zlib.decompress(d[off:off + size], -15), np.uint16).reshape(n, n)); off += size
    q = blocks[0]
    return (n, step, minx, minz), np.where(q == 0xFFFF, np.nan, base + q.astype(np.float64) * unit)


def predicted_low(heights, hdr, rows, cols, origin=(2558400, 2000, 2556450)):
    """Rule above: the predicted low field (cells above the csc's y) for each LOD 0 texel, NaN where the bake has holes."""
    (n, step, minx, minz), (x, y, z, w, h) = heights[0], hdr
    hh = heights[1]
    out = np.full((rows, cols), np.nan)
    per = int(round(16 / step))
    for r in range(rows):
        z0 = int(round(((z + r) * 16 - origin[2] - minz) / step)) - 1
        if z0 < 0 or z0 + per + 2 > n: continue
        band = hh[z0:z0 + per + 2]
        for c in range(cols):
            x0 = int(round(((x + c) * 16 - origin[0] - minx) / step)) - 1
            if x0 < 0 or x0 + per + 2 > n: continue
            m = band[:, x0:x0 + per + 2].min()
            out[r, c] = np.floor((m - (y * 16 - origin[1]) - 1) / 16)
    return out


def main(argv):
    if len(argv) >= 2 and argv[0] == 'info':
        hdr, a = read_csc(int(argv[1]))
        lo, cnt = a & 0x3FF, a >> 10
        print(f'header x,y,z,w,h {hdr}; low {lo.min()}..{lo.max()}; count {cnt.min()}..{cnt.max()}; '
              f'bits 8-9 set: {int(((a >> 8) & 3).astype(bool).sum())}')
    elif len(argv) >= 2 and argv[0] in ('fit', 'pick'):
        zone = int(argv[1])
        conts = [int(c) for c in argv[2:]] or [zone]
        bake = read_bake(zone)
        lows = {}
        for c in conts:
            hdr, a = read_csc(c)
            pred = predicted_low(bake, hdr, *a.shape)
            ok = np.isfinite(pred)
            lows[c] = (a & 0x3FF).astype(float), pred, ok
            print(f'bake {zone} vs csc {c}: rule matches {np.mean(pred[ok] == lows[c][0][ok]) * 100:.2f}% of {ok.sum()} texels')
        if argv[0] == 'pick' and len(conts) > 1:
            for i, c1 in enumerate(conts):
                for c2 in conts[i + 1:]:
                    l1, pred, ok = lows[c1]; l2 = lows[c2][0]
                    diff = ok & (l1 != l2)
                    if not diff.any():
                        print(f'  {c1} / {c2}: identical low fields'); continue
                    print(f'  {c1} / {c2}: {diff.sum()} texels differ; bake agrees with {c1} on '
                          f'{np.mean(pred[diff] == l1[diff]) * 100:.1f}%, with {c2} on {np.mean(pred[diff] == l2[diff]) * 100:.1f}%')
    else:
        print(__doc__ or open(__file__).read().split('import os')[0])


if __name__ == '__main__':
    main(sys.argv[1:])
