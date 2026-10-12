# Bakes TerrainHeights<zone>.bin for every landscape, one client session:
#   for each zone: dev command "landscape <zone>" (server travels the logged-in test client there), wait until its
#   loading screen has dropped, re-arm the sampler (hook_heightbake.js rpc start), collect the 4000x4000 grid, pack it.
# Attaches once and never detaches while the game runs (detaching a replaced function can crash the client): close
# the game when it reports "all done", then the runner exits by itself.
# Usage: python bake_all.py <gameData dir> <dev_commands.txt> <client Logs dir> zone [zone ...]
import frida, sys, os, time, subprocess
game_data, dev_file, logs = sys.argv[1], sys.argv[2], sys.argv[3]
zones = [int(z) for z in sys.argv[4:]]
N = 4000
os.makedirs("heightbake", exist_ok=True)
log = open("heightbake/bake_all.log", "a", buffering=1)
state = {"rows": 0}
hf = mf = None
def on_message(m, data):
    if m["type"] != "send":
        log.write(str(m) + "\n"); return
    p = m["payload"]
    if "row" in p:
        f = mf if p.get("mats") else hf
        f.seek(p["row"] * N * (2 if p.get("mats") else 4)); f.write(data)
        if not p.get("mats"):
            state["rows"] = p["row"] + p["rows"]
def loading_hidden_since(t0):
    try:
        last = open(os.path.join(logs, "LoadingScreen.txt"), encoding="utf-8", errors="replace").read().splitlines()[-1]
    except Exception:
        return False
    return "now hidden" in last and os.path.getmtime(os.path.join(logs, "LoadingScreen.txt")) > t0
dev = frida.get_local_device()
pid = [p.pid for p in dev.enumerate_processes() if p.name.lower() == "landmark64.exe"][0]
script = dev.attach(pid).create_script(open("hook_heightbake.js").read())
script.on("message", on_message); script.load()
log.write(f"attached {pid}\n")
for zone in zones:
    t0 = time.time()
    open(dev_file, "w").write(f"landscape {zone}\n")
    while not loading_hidden_since(t0) and time.time() - t0 < 180:
        time.sleep(2)
    time.sleep(8)
    hf = open("heightbake/heights.f32", "wb"); hf.truncate(N * N * 4)
    mf = open("heightbake/mats.u16", "wb"); mf.truncate(N * N * 2)
    state["rows"] = 0
    script.exports_sync.start()
    t1 = time.time()
    while state["rows"] < N and time.time() - t1 < 600:
        time.sleep(1)
    hf.close(); mf.close()
    if state["rows"] < N:
        log.write(f"zone {zone}: stalled at row {state['rows']}\n"); continue
    out = subprocess.run([sys.executable, "pack_heights.py", game_data, str(zone)], capture_output=True, text=True)
    log.write(f"zone {zone}: {time.time() - t1:.0f}s {out.stdout.strip()} {out.stderr.strip()[-300:]}\n")
log.write("all done; close the game to end the runner\n")
while any(p.pid == pid for p in dev.enumerate_processes()):
    time.sleep(3)
log.write("game closed\n")
