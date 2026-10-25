"""Attach hook_placeoffset.js to the running Landmark64.exe (L199 paste preview offset).

Usage (from repo root or this folder):
  tools/paste/.venv/Scripts/python.exe tools/paste/run_placeoffset.py

Then in game: Designs → place a piece, move/rotate the preview, click paste once.
Watch placeoffset.log (and the console). Close the game before stopping this script
(Ctrl+C) — detaching Frida from a live client can crash it.
"""
from __future__ import annotations

import json
import os
import sys
import time

import frida

HERE = os.path.dirname(os.path.abspath(__file__))
LOG = os.path.join(HERE, "placeoffset.log")
HOOK = os.path.join(HERE, "hook_placeoffset.js")


def main() -> int:
    procs = [p for p in frida.get_local_device().enumerate_processes() if p.name.lower() == "landmark64.exe"]
    if not procs:
        print("Landmark64.exe is not running", file=sys.stderr)
        return 1
    if len(procs) > 1:
        print(f"several Landmark64.exe: using pid {procs[0].pid}", file=sys.stderr)
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
    print("Move the paste preview / rotate / click paste. Close the game, then Ctrl+C here.", flush=True)
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nCtrl+C — waiting; close Landmark64.exe first if it is still up, then detach…", flush=True)
        # Prefer detach after the process has exited.
        for _ in range(30):
            still = any(p.pid == pid for p in frida.get_local_device().enumerate_processes())
            if not still:
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
