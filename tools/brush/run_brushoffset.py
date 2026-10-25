"""Attach hook_brushoffset.js to Landmark64.exe (brush aim vs ApplyDiff).

  tools/paste/.venv/Scripts/python.exe tools/brush/run_brushoffset.py

Rebuild world first so Info lines include: brush aim X,Y,Z … → written min … min−aim …
Close the game before Ctrl+C.
"""
from __future__ import annotations

import json
import os
import sys
import time

import frida

HERE = os.path.dirname(os.path.abspath(__file__))
# Reuse the paste venv's frida (same machine).
LOG = os.path.join(HERE, "brushoffset.log")
HOOK = os.path.join(HERE, "hook_brushoffset.js")


def main() -> int:
    procs = [p for p in frida.get_local_device().enumerate_processes() if p.name.lower() == "landmark64.exe"]
    if not procs:
        print("Landmark64.exe is not running", file=sys.stderr)
        return 1
    pid = procs[0].pid
    session = frida.attach(pid)
    script = session.create_script(open(HOOK, encoding="utf-8").read())
    log = open(LOG, "a", encoding="utf-8", buffering=1)
    log.write(f"\n--- attach pid={pid} at {time.strftime('%Y-%m-%d %H:%M:%S')} ---\n")

    def on_message(message, data):
        if message["type"] == "send":
            line = json.dumps(message["payload"], ensure_ascii=False)
        elif message["type"] == "error":
            line = "ERROR " + json.dumps(message, ensure_ascii=False)
        else:
            line = str(message)
        print(line, flush=True)
        log.write(line + "\n")

    script.on("message", on_message)
    script.load()
    print(f"attached {pid}; logging to {LOG}", flush=True)
    print("Add-tool a few voxels (and a small box if you like). Close game, then Ctrl+C.", flush=True)
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nCtrl+C — close Landmark64.exe first if still up…", flush=True)
        for _ in range(30):
            if not any(p.pid == pid for p in frida.get_local_device().enumerate_processes()):
                break
            time.sleep(1)
        try:
            session.detach()
        except Exception as e:
            print(f"detach: {e}", file=sys.stderr)
        log.write("--- detach ---\n")
        log.close()
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
