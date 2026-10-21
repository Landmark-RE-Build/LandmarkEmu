# The strong hash list LaunchPad and the patch host trust (LandmarkEmulator.PatchPacks/Data/landmark-0695-files-sha256.csv):
# RelativePath,Size,CRC32,SHA256 for every file of the known 0.695 builds (Patching.Targets).
#
# Why: Daybreak's own list of the final build (landmark-final-2017-file-list.csv) has only size + CRC32 per file, and a
# CRC32 can be forged (anyone can make a harmful Landmark64.exe with the right size and CRC32). A SHA-256 can't. So the
# SHA-256 of each genuine file is taken here, once, from local copies, and the launcher holds every file it writes to it.
#
# A file's SHA-256 is only taken from a copy whose size AND CRC32 match Daybreak's list for that build (or the January
# 2017 variants below): a copy that was edited (the test copy's patched exe, a rewritten ClientConfig.ini) never gets in.
# Sources are tried in the order given; zips are read without unpacking (their stored CRC32 picks the candidate, the
# CRC32 is then recomputed from the bytes). For folders, <path>.orig / .org / .bak are tried as well (a copy whose file
# was edited may keep the original that way). Sources are only read, never written. Hashes are of the exact bytes: no
# file is ever "normalised" (no debug ids, PE timestamps or checksums stripped), so each build's own file has its own row.
#
# Rows whose file was found nowhere keep an empty SHA256: the launcher then can't verify that build's file and refuses
# a pack that needs it (nothing is ever trusted on CRC32 alone).
#
# The output holds names, sizes and checksums only: no game bytes.
#
# Usage:
#   python strong_hash_list.py <final list csv> <out csv> <source> [<source> ...]
# where each source is a .zip of game files or a game folder. Example (2026-10-18):
#   python tools/strong_hash_list.py LandmarkEmulator.PatchPacks/Data/landmark-final-2017-file-list.csv \
#       LandmarkEmulator.PatchPacks/Data/landmark-0695-files-sha256.csv \
#       "<0.695 install>-0695-files-not-in-0589.zip" "<0.695 install>/files-still-needed.zip" \
#       "<Landmark folder>" "<0.695 install>"
# Result (2026-10-18): 34,138 of 34,141 rows: 1,008 + 86 from the two zips, 33,044 from the Steam 0.589 install (files
# 0.695 kept unchanged); not found then: the final (Feb 2017) build's Landmark64.exe ED231C3C and Locale\en_us_data.dat
# 10E110DF / .dir 7C48670B. Those three SHA-256s were filled 2026-10-20 from genuine copies (size + CRC32 match); the
# CSV is now complete (34,141 / 34,141). Re-running this script without those sources as inputs will blank them again.
import csv, hashlib, os, sys, zipfile, zlib

BS = chr(92)

# The January 2017 build (LaunchPad Patching.Targets): Daybreak's final list with these three files from before their
# last update (14 Feb 2017).
JANUARY = {
    'Landmark64.exe': (91222528, '2FF380AE'),
    'Locale' + BS + 'en_us_data.dat': (1394253, 'FFF22327'),
    'Locale' + BS + 'en_us_data.dir': (636370, '6166AEA1'),
}


def norm(path):
    return path.replace('/', BS).lstrip(BS)


def hashes(stream):
    crc, sha = 0, hashlib.sha256()
    while True:
        block = stream.read(1 << 22)
        if not block:
            break
        crc = zlib.crc32(block, crc)
        sha.update(block)
    return '%08X' % (crc & 0xFFFFFFFF), sha.hexdigest().upper()


def main():
    final_path, out_path, sources = sys.argv[1], sys.argv[2], sys.argv[3:]
    wanted = []                                     # (path, size, crc), in list order
    for r in csv.DictReader(open(final_path, encoding='utf-8-sig')):
        wanted.append((norm(r['RelativePath']), int(r['Size']), r['CRC32'].strip().upper()))
    for path, (size, crc) in JANUARY.items():
        wanted.append((path, size, crc))

    zips, folders = [], []
    for s in sources:
        if s.lower().endswith('.zip'):
            z = zipfile.ZipFile(s)
            index = {}
            for i in z.infolist():
                if not i.filename.endswith('/'):
                    index.setdefault((norm(i.filename).lower(), i.file_size, '%08X' % i.CRC), i)
            zips.append((s, z, index))
        elif os.path.isdir(s):
            folders.append(s)
        else:
            sys.exit('No such source: ' + s)

    folder_cache = {}                               # full path -> (crc, sha)
    found, rows, by_source = 0, [], {}
    for n, (path, size, crc) in enumerate(wanted, 1):
        sha, source = '', ''
        for name, z, index in zips:
            info = index.get((path.lower(), size, crc))
            if info is None:
                continue
            with z.open(info) as f:
                c, h = hashes(f)
            if c == crc:
                sha, source = h, name
                break
        if not sha:
            for folder in folders:
                for suffix in ('', '.orig', '.org', '.bak'):
                    full = os.path.join(folder, path) + suffix
                    try:
                        if os.path.getsize(full) != size:
                            continue
                    except OSError:
                        continue
                    if full not in folder_cache:
                        with open(full, 'rb') as f:
                            folder_cache[full] = hashes(f)
                    c, h = folder_cache[full]
                    if c == crc:
                        sha, source = h, folder
                        break
                if sha:
                    break
        if sha:
            found += 1
            by_source[source] = by_source.get(source, 0) + 1
        rows.append((path, size, crc, sha))
        if n % 1000 == 0:
            print(f'  {n:,} of {len(wanted):,} ({found:,} found)', flush=True)

    with open(out_path, 'w', newline='', encoding='utf-8') as f:
        w = csv.writer(f, lineterminator='\n')
        w.writerow(['RelativePath', 'Size', 'CRC32', 'SHA256'])
        # Sorted, one row per (path, size, CRC32): a path with two builds' versions has two rows.
        seen = set()
        for r in sorted(rows, key=lambda r: (r[0].lower(), r[2])):
            key = (r[0].lower(), r[1], r[2])
            if key not in seen:
                seen.add(key)
                w.writerow(r)
    print(f'{found:,} of {len(wanted):,} files have a SHA-256.')
    for s, k in by_source.items():
        print(f'  {k:,} from {s}')
    for path, size, crc, sha in rows:
        if not sha:
            print(f'  NOT FOUND: {path} {size} {crc}')


if __name__ == '__main__':
    main()
