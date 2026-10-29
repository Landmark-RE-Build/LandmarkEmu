# Allocator slots Frida probes (0.695)

Research tools for the 64-slot hang. Does **not** patch `Landmark64.exe` on disk.

## Observe (attach to a running client)

1. PLAY with `DirectThreadCount=10` (LaunchPad advanced Custom 10 — already set for `D:\Landmark\Game`).
2. Then:

```powershell
python tools\allocator-slots\run_observe.py
```

Watch `used` / `free`. Full table = `used: 64, free: 0`.

## P4 — move table to 127 (spawn suspended)

Close Landmark first. Uses LaunchPad’s **selected** server + remembered SRP key (`%AppData%\LandmarkEmu\launchpad.json`), forces `DirectThreadCount=10`, mints a fresh `SessionId`, `frida.spawn`s, patches LEAs while frozen, resumes.

```powershell
python tools\allocator-slots\run_spawn.py
```

Success: first message `{"kind":"p4","ok":true,...}`; later `slots` with `capacity: 127` and `used` able to go **above 64**.

Observe-only spawn (auth + DTC=10, no patches):

```powershell
python tools\allocator-slots\run_spawn.py --observe
```

Needs `dotnet` (builds `SessionKey` once) and Frida on your Python.
