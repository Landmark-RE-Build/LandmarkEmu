# Live zone-in / prop traffic: what recorded live data exists (agent "live-captures", 2026-10-17)

Question: what did the LIVE server send around a zone-in / landscape change (order and timing of ClientBeginZoning,
SendZoneDetails, ClientIsReady, DoneSendingPreloadCharacters, ZoneDoneSendingInitialData, AddLightweightProp /
LightweightToFullProp), and what bytes were in live AddLightweightProp bodies (trailing prop part, base +0x298 / +0x29a)?
Read-only pass. **[P]** = proven by a file / line on disk, **[I]** = inferred (reason given).

## 1. Result in one line

**No live packet capture of a zone-in, a landscape change or any prop spawn is on disk.** No live AddLightweightProp,
LightweightToFullProp, FullCharacterDataRequest, ClientIsReady, DoneSendingPreloadCharacters or ZoneDone body exists,
so the trailing prop-part values and the +0x298 / +0x29a flag bytes live used cannot be read from a recording. [P]
What does survive is two timing numbers from the upstream author's official capture, the design of that author's
timeline tool, and the order of the inherited login replay. All three are indirect evidence (§3).

## 2. Search done (all negative for prop / zone-in captures)

| Where | Found | Verdict |
|---|---|---|
| `*.pcap`, `*.pcapng`, `*.cap`, `*.har`, `*capture*`, `*Timeline*`, `*AgentShare*`, `*kirmmin*` over all of Z: (minus Steam library / recycle bin) | only the Parser sources (`CaptureSessionParser.cs`, `PcapUdpReader.cs`, `ZoneTimelineMarkdown.cs`, …) | no capture file [P] |
| user folders | nothing relevant | no capture [P] |
| `LandmarkEmulator.WorldServer/Resources/PacketDumps/` (and the bin/ copies) | `145-PropBase`, `39-…ItemCategoryDefinitions`, `41-…ProfileDefinitions`, `44-CommandBase`, `71-AchievementBase`, `SendSelf.bin`, `TerrainCellManifest.bin`, `PropTints.bin` | live bodies, but none is 0x26/0x62, 0x63, 0x5E, 0x29/0x1B, 0x10 or 0x11 [P]. `145-PropBase` is the prop *definition* list (0xED sub 0x0A, MiscHandler.cs:317-324); its 0x68-byte entries have no collision field |
| Captured hex bodies in code (`MiscHandler.cs` :251-325, `WorldSession.cs` :509-516, :647, `ActivityHandler.cs:22`, `capturedCasterInfo`) | InitializationParameters "live", ClientGameSettings, ZoneDetails (OpenBetaGeometry_406), ZoneCoordinateInfo, Experience, Loadouts, QuickChat, ActivityManager/ActivityBase, CasterInfoReply ×4, commented-out stats/bio/mail/preference bodies | no entity / prop / zone-done body [P]. Same inventory as `agent-leads2-a.md:7-9` |
| Client log folders: `testclient/Logs`, `<0.695 install>\Logs`, `<0.695 test copy>\Logs`, `lp-test/Landmark/Logs`, `Steam library/.../Landmark/Logs` | every file dated 2026-10 (our emulator runs) | no live-era client log [P] |
| `<0.695 install>\*.zip` (`files-still-needed`, `landmark-0695-files-not-in-0589`, `testclient`) | Resources/*.txt only | no logs, no captures [P] |
| `re/` (`frida_live*.txt`, `hook_live.js`, `live_stats.txt`) | "live" means an unrequested terrain-cell push / the live stat table, from our own server | not captures of the live server [P] |
| `trace/families/0026_PlayerUpdate.md:663-671` | 0x62 / 0x63 entries list only our own send counts (opcode_stats) | no live data [P] |
| `LandmarkEmu/docs/protocol.md:266-284` (§4.2) | prop part documented as `u64 owner; u8 mode, u32 flags, u8, …; list, u32, u64, bool` — from the client reader, not from a capture | [P] |

Not checked (rules: no git): the `LandmarkEmu/.git` history. Upstream wrote its timeline to `AgentShare/ZoneEntryTimeline.md`
(`Parser.cs:604` usage text); if that file was ever committed, `git log --all --stat -- '*AgentShare*' '*ZoneEntryTimeline*' '*.pcap*'`
would recover the real live order. **This is the one remaining on-disk lead** (for OPEN-LEADS).

## 3. Indirect evidence of what live sent

### 3.1 The two live timings [P that they exist; I that they are live measurements]

`WorldSession.cs:1410-1427` (region "T-KIRMMIN-WIRE-TIMING", upstream debug code) logs our timings next to two
constants: `liveReadyToZoneDone=1263` and `liveZoneDoneToA00=7286`. "A00" is TerrainCellModifiedCellManifest
(0xEC sub 0x0A, `ZoneMessageOpcode.cs:525`; the region logs exactly ZoneDone, ModifiedCellManifest and ActivityBase).
`ZoneTimelineMarkdown.cs:49` titles the tool's output "Official capture zone-entry timeline", so the constants come from a
live capture parsed with it [I]. The capture is a first login (t=0 is the first SendSelfToClient), not a landscape change.
- Live: ClientIsReady (C→S) → ZoneDoneSendingInitialData **1263 ms**.
- Live: ZoneDoneSendingInitialData → ModifiedCellManifest **7286 ms**.
- Ours (`server-data/worlds/socialtest/console.log`, WIRE-TIMING lines): the manifest goes in the same flush as ZoneDone.

### 3.2 What the upstream timeline tool shows about the live prop stream [I]

`ZoneTimelineMarkdown.cs:15-29` collapses opcodes that were bursts in the official capture: **AddLightweightProp,
AddLightweightNpc, LightweightToFullProp, FullCharacterDataRequest (C→S), InteractionInfoRequest (C→S),
AddFeatherweightObject**, cell request/update, ClientLog. Its phases (:122-134) put the lightweight / featherweight
stream in phase E, after "C. ClientIsReady → ZoneDone (< 2 s)". Read together:
- Live sent props as a stream **after ClientIsReady**, not as part of the initial data [I: phase layout].
- **The live client pulled the full conversions**: FullCharacterDataRequest was high-volume C→S, and LightweightToFullProp
  was high-volume S→C [I: both listed as bursts]. This matches what our own client does
  (MODLOG.md:179 "Client then asks FullCharacterDataRequest (0x26/0x5E) for it"; Spire fix MODLOG.md:1172). Pushing
  LightweightToFullProp without a request (Spire.cs:767-785) is ours; no live evidence for it either way.
- The anchor "first AddLightweightProp, ms vs ClientIsReady" (`ZoneTimelineMarkdown.cs:104`) was computed, but the number
  is not on disk.

### 3.3 The inherited login order [I]

`MiscHandler.ClientIsReady` (:244-325) is the upstream replay of the captured login, still in capture order (comment
:279 "Zone Details are sent a second time for some reason in logs"): QuickChat → **DoneSendingPreloadCharacters** →
stats / equipment / achievements / activity manager / effect tags / loadouts / emotes / inventory 0x18 →
**ZoneDoneSendingInitialData** → SendZoneDetails (again) → ZonePopulation → (LoadWelcomeScreen) → Experience →
ZoneCoordinateInfo → WorldDisplayInfo → MovementVersion → ClientSettings → (manifest) → PropBase list → ActivityBase.
No entity spawn is inside this run. Whatever preload characters live sent came before ClientIsReady (phase B,
SendSelf → ClientIsReady) or not at all; the capture that would say which is the missing one.

DoneSendingPreloadCharacters' bool: the upstream model sent an **empty** body (`export/public/.../ClientUpdateDoneSendingPreloadCharacters.cs:7`
"S→C, empty"). The current model sends `WaitForLoad = false`. The live value is unknown [P: no body on disk].

### 3.4 Live players' reports (design/forum, weak) [I]

- 2014-09-16 update notes: "additional work on teleporting … you might fall through the ground as you load"
  (`design/forum/all/2014-09-16-48275-…md`). So live had arrival fall-throughs and fixed them, but the notes don't say how.
- 2016-06-29 thread: "Running off the claim out of range and then running back causes NPC's to fall through"
  (`2016-06-29-57596-…md:461`). Entities that come back into range fell through prop floors on live too. This hints
  that live also gave props their collision late, when they streamed in.

## 4. Our AddLightweightProp vs the reader (no live bytes to compare)

0.589 base reader FUN_142b8cfc0 (`re/decomp_all`, read with fn.py) mapped onto
`PlayerUpdateAddLightweightProp.cs:95-105`. Assumed the same in 0.695; agent-prop-collision checks this.

| Writer line | Reader target | We send | Live |
|---|---|---|---|
| :95 u32 "spawn flags" | +0x190 | 0 | unknown |
| :96-97 u32, u32 | +0x194, +0x198 | 0, 0 | unknown |
| :98 i8 | +0x19c | 0 | unknown |
| :99 u32 | +0x1a0 | 0 | unknown |
| :100 bool | +0x1a4 | false | unknown |
| **:101 `WriteBytes(new byte[3])`** | **+0x298, +0x299, +0x29a** (3-byte loop) [P] | **00 00 00** | unknown |
| :102 u8 | +0x1a5 | 0 | unknown |
| :103 u32, :104 u64 | +0x1a8, +0x1b0 | 0, 0 | unknown |
| :105 u64 | attachment block FUN_142b890f0 → +0x1c0 | 0 | unknown |
| prop part :128 u64 owner | +0x2a0 | owner | unknown |
| :129-132 u8 1, u32 0, u8 0, u32 0 | FUN_142ba65c0 → +0x2b0 (movement-body block, MODLOG.md:698-701) | mode 1, all 0 | unknown |
| :133 u32 list | FUN_142ba1d20 → +0x340 | empty | unknown |
| :134 u32, :135 u64, :136 bool | +0x480, +0x488, +0x490 | 0, 0, false | unknown |

So the "flag bytes" the client copies from base +0x298 / +0x29a are the three zero bytes at writer line 101 [P].
What live put there, and what any non-zero value would do, can only come from the client trace.

## 5. What this means for the server

1. **The recordings support no byte-level change.** Nothing on disk shows a live value for the prop part or for
   +0x298 / +0x29a. Any change to those bytes has to be justified by `agent-prop-collision.md` (client code), not by a
   capture.
2. **Order of the flags vs the props** [I, the one change the evidence points to]: the packet's name and its
   reader (DoneSendingPreloadCharacters, `WaitForLoad` marks *every entity the client already holds* as required:
   FUN_142cd9980 case 0x1A → FUN_142f86f80, entity +0x1458 bit 4, checked by WaitForWorldReady FUN_142f86ee0) mean the
   preload entities go out *before* the flag. Our re-zone end `MiscHandler.FinishZoneIn` (:211-223) does the opposite.
   It sends DoneSendingPreloadCharacters(false) and ZoneDoneSendingInitialData first, then `PlayerManager.Reintroduce`
   (the landscape's props and players). Proposed change, if the client trace confirms that "required / loaded"
   includes collision setup: in FinishZoneIn, first send the arrival area's props (at least the Spire set:
   AddLightweightProp, plus LightweightToFullProp if the trace shows full is needed for physics), then
   `DoneSendingPreloadCharacters { WaitForLoad = true }`, then ZoneDoneSendingInitialData. The loading screen then stays
   up until those entities report loaded (vtable +0x4f8). Keep the rest of the landscape streaming after, as live did
   (§3.2).
3. Keep answering FullCharacterDataRequest with LightweightToFullProp (the live pattern, §3.2). The unrequested push in
   Spire.cs is harmless but not live-evidenced.
4. OPEN-LEADS entry to add: "Live zone-in timeline: upstream's `AgentShare/ZoneEntryTimeline.md` (official capture
   parsed by LandmarkEmulator.Parser `timeline`) is the only live record of prop / flag order. Try the
   LandmarkEmu git history (all branches) for it, or for the capture itself."
