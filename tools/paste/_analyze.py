import json

path = r"d:\Landmark\LandMarkEmuTheLaw\tools\paste\placeoffset.log"
rows = []
for line in open(path, encoding="utf-8"):
    line = line.strip()
    if not line.startswith("{"):
        continue
    try:
        rows.append(json.loads(line))
    except json.JSONDecodeError:
        pass

server = [
    ("2,21,30", "120x124x92", "120x124x92", [1, 0, 0, 0, 1, 0, 0, 0, 1]),
    ("11,18,3", "35x57x36", "36x57x35", [0, 0, -1, 0, 1, 0, 1, 0, 0]),
    ("22,17,22", "35x57x36", "36x57x35", [0, 0, 1, 0, 1, 0, -1, 0, 0]),
    ("15,23,27", "36x57x35", "36x57x35", [1, 0, 0, 0, 1, 0, 0, 0, 1]),
]

sends = [r for r in rows if r.get("kind") == "pasteSend"]
print(f"pasteSend count={len(sends)} server lines={len(server)}")
for (orig, area_s, copy_s, turn_s), send in zip(server, sends):
    idx = rows.index(send)
    area_ev = None
    for j in range(idx - 1, -1, -1):
        if rows[j].get("kind") == "area":
            area_ev = rows[j]
            break
    av = area_ev["areaVoxels"] if area_ev else None
    sm1 = area_ev["sizes"]["sizeMinus1"] if area_ev else None
    print("---")
    print(f"server at {orig}  clientArea {area_s}  copy {copy_s}  turn {turn_s}")
    print(f"frida  send.turn {send['turn']}  send.sizeMinus1 {send['sizes']['sizeMinus1']}")
    print(f"frida  areaVoxels {av}  area.sizeMinus1 {sm1}  area.turn {area_ev.get('turn') if area_ev else None}")
    if av:
        sx, sy, sz = map(int, orig.split(","))
        print(f"delta server_origin - frida_areaVoxels = ({sx - av['x']}, {sy - av['y']}, {sz - av['z']})")
