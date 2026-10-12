# Runs hook_heightbake.js against the single running Landmark64.exe and writes the grid files.
# Output: heightbake/heights.f32 (N*N float32 local metres, NaN = invalid), heightbake/mats.u16, progress.log
import frida, sys, os, time, json
os.makedirs("heightbake", exist_ok=True)
N = 4000
hf = open("heightbake/heights.f32", "wb"); hf.truncate(N * N * 4)
mf = open("heightbake/mats.u16", "wb"); mf.truncate(N * N * 2)
log = open("heightbake/progress.log", "w", buffering=1)
done = {"rows": 0}
def on_message(m, data):
    if m["type"] != "send":
        log.write(str(m) + "\n"); return
    p = m["payload"]
    if "row" in p:
        if p.get("mats"):
            mf.seek(p["row"] * N * 2); mf.write(data); mf.flush()
        else:
            hf.seek(p["row"] * N * 4); hf.write(data); hf.flush()
            done["rows"] = p["row"] + p["rows"]
    else:
        log.write(json.dumps(p) + "\n")
pid = [p.pid for p in frida.get_local_device().enumerate_processes() if p.name.lower() == "landmark64.exe"][0]
session = frida.attach(pid)
script = session.create_script(open("hook_heightbake.js").read())
script.on("message", on_message)
script.load()
log.write(f"attached {pid}\n")
while True:
    time.sleep(1)
