"""Attach hook_observe.js to a running Landmark64.exe (slot counts only).

  python tools/allocator-slots/run_observe.py

Start the game first (LaunchPad PLAY; keep DirectThreadCount=10). Close the game
before Ctrl+C — detaching Frida from a live client can crash it.
"""
from __future__ import annotations

import json
import os
import sys
import time

import frida

HERE = os.path.dirname(os.path.abspath(__file__))
HOOK = os.path.join(HERE, "hook_observe.js")
LOG = os.path.join(HERE, "observe.log")


def main() -> int:
    procs = [p for p in frida.get_local_device().enumerate_processes() if p.name.lower() == "landmark64.exe"]
    if not procs:
        print("Landmark64.exe is not running — PLAY first, then re-run this.", file=sys.stderr)
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
    print("Enter world / load a heavy scene. Close Landmark, then Ctrl+C here.", flush=True)
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("\nCtrl+C — waiting for Landmark64.exe to exit, then detach…", flush=True)
        for _ in range(60):
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
