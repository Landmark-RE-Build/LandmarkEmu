"""Spawn Landmark64 suspended, apply P4 (or observe-only), resume with a fresh SessionId.

Close any running Landmark64 first.

  # P4 table move (default) — uses LaunchPad's selected server + remembered SRP key
  python tools/allocator-slots/run_spawn.py

  # Observe only (no patches) still via spawn+SessionId
  python tools/allocator-slots/run_spawn.py --observe

Requires: frida, and `dotnet` to build/run SessionKey (ClientLaunch SRP).
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import time

import frida

HERE = os.path.dirname(os.path.abspath(__file__))
LOG = os.path.join(HERE, "spawn.log")
SESSION_PROJ = os.path.join(HERE, "SessionKey", "SessionKey.csproj")


def mint_session(extra: list[str]) -> dict:
    cmd = ["dotnet", "run", "--project", SESSION_PROJ, "-c", "Release", "--no-launch-profile", "--", *extra]
    print("minting SessionId via ClientLaunch SRP…", flush=True)
    out = subprocess.check_output(cmd, text=True, stderr=subprocess.PIPE)
    # Last non-empty line is JSON (dotnet may print build noise on older SDKs to stdout — prefer last {…})
    lines = [ln.strip() for ln in out.splitlines() if ln.strip()]
    raw = next((ln for ln in reversed(lines) if ln.startswith("{")), None)
    if not raw:
        raise RuntimeError("SessionKey produced no JSON:\n" + out)
    return json.loads(raw)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--observe", action="store_true", help="spawn+auth only; no P4 patches")
    ap.add_argument("--folder", default=None, help="Landmark folder (default: LaunchPad / D:\\Landmark\\Game)")
    ap.add_argument("--direct-thread-count", type=int, default=10)
    args, passthrough = ap.parse_known_args()

    hook_name = "hook_observe.js" if args.observe else "hook_p4_move.js"
    hook_path = os.path.join(HERE, hook_name)

    extra = ["--direct-thread-count", str(args.direct_thread_count), *passthrough]
    if args.folder:
        extra = ["--folder", args.folder, *extra]

    running = [p for p in frida.get_local_device().enumerate_processes() if p.name.lower() == "landmark64.exe"]
    if running:
        print(f"Landmark64.exe already running (pid {running[0].pid}). Close it first.", file=sys.stderr)
        return 1

    try:
        meta = mint_session(extra)
    except subprocess.CalledProcessError as e:
        print(e.stderr or e, file=sys.stderr)
        return 1

    exe = meta["exe"]
    folder = meta["folder"]
    argv = [
        exe,
        f"SessionId={meta['sessionKey']}",
        "CasSessionId=0",
        "STEAM_ENABLED=0",
    ]
    locale = (meta.get("locale") or "").strip()
    if locale:
        argv.append(f"Internationalization:Locale={locale}")

    print(
        f"spawn {exe}\n  server={meta.get('server')} login={meta.get('login')} "
        f"DTC={meta.get('directThreadCount')} hook={hook_name}",
        flush=True,
    )

    device = frida.get_local_device()
    # Drop Steam ids so the client stays STEAM_ENABLED=0 clean.
    env = {k: v for k, v in os.environ.items() if k not in ("SteamAppId", "SteamGameId", "SteamPath")}
    pid = device.spawn(argv, cwd=folder, env=env, stdio="pipe")
    session = device.attach(pid)
    script = session.create_script(open(hook_path, encoding="utf-8").read())

    log = open(LOG, "a", encoding="utf-8", buffering=1)
    log.write(f"\n--- spawn pid={pid} hook={hook_name} at {time.strftime('%Y-%m-%d %H:%M:%S')} ---\n")
    log.write(json.dumps({k: meta[k] for k in meta if k != "sessionKey"}) + "\n")

    failed = {"p4": False}

    def on_message(message, data):
        if message["type"] == "send":
            payload = message["payload"]
            line = json.dumps(payload, ensure_ascii=False)
            if isinstance(payload, dict) and payload.get("kind") == "p4" and not payload.get("ok"):
                failed["p4"] = True
        elif message["type"] == "error":
            line = "ERROR " + json.dumps(message, ensure_ascii=False)
            failed["p4"] = True
        else:
            line = str(message)
        print(line, flush=True)
        log.write(line + "\n")

    script.on("message", on_message)
    try:
        script.load()
    except Exception as e:
        print(f"script load failed: {e}", file=sys.stderr)
        try:
            device.kill(pid)
        except Exception:
            pass
        return 1

    if failed["p4"] and not args.observe:
        print("P4 patch failed — killing spawned process.", file=sys.stderr)
        try:
            device.kill(pid)
        except Exception:
            pass
        return 1

    device.resume(pid)
    print(f"resumed pid={pid}; enter world. Close Landmark, then Ctrl+C here.", flush=True)

    try:
        while True:
            time.sleep(1)
            if not any(p.pid == pid for p in device.enumerate_processes()):
                print("Landmark exited.", flush=True)
                break
    except KeyboardInterrupt:
        print("\nCtrl+C — waiting for exit, then detach…", flush=True)
        for _ in range(60):
            if not any(p.pid == pid for p in device.enumerate_processes()):
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
