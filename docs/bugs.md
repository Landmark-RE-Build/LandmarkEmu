# Known bugs

Living list of bugs found while running or developing the emulator. One entry per problem. When fixed, move the entry
to **Fixed** with the date and a one-line how. Prefer a GitHub issue for anything that needs discussion or a patch from
someone else ([wiki/Bug-Submission.md](../wiki/Bug-Submission.md)); keep short working notes here so they are not lost
between sessions.

## Open

### 2026-10-19 — World NPC greet bubble had no Yes / No (reopened)

- **Where:** Fungus Scout interact (`Game/Npc/WorldNpcs.cs` + DisplayPropText).
- **What:** Speech bubble / chat text appeared; Yes/No (HudIndicators NpcDialogResponse) did not.
- **Cause (revised):** (1) Playtest after the first fix hit **Spire Herbalist** while chain-locked (`q=0`, no buttons by design) — both were Kobolds near the Spire. (2) Server `FullNpcs` is set when `SendFull` is *queued*, so question packets spoke as the creature before the client had a full actor; bubble can show, question HUD cannot (L30).
- **Status:** questions use interactor bubble (mode 4) + `Chat.NpcSay` as the NPC (so chat is not "[you] says"); needs rebuild + confirm Yes/No still appear.

### 2026-10-19 — World NPC question lines showed as the player in chat

- **Where:** Fungus Scout greet / greet_again (mode 5 DisplayPropText).
- **What:** Chat showed `[Pheebe] says:` for the Scout’s ask lines; accept/decline correctly showed `[Fungus Scout]`.
- **Cause:** Question packets used the player as speaker so Yes/No would attach; mode 5 also emits a say line from that speaker.
- **Fix:** Questions → bubble-only (mode 4) on the player + separate `Chat.NpcSay` under the NPC name.

### 2026-10-19 — No Fiery Fungus near the Spire for Scout's Errand

- **Where:** Surface creature seeding (`Creatures.Region`) vs Fungus Scout quest.
- **What:** Playtester saw none around the Scout / Spire.
- **Cause:** Only ~20% of 128 m regions get 1–2 fungus; claims and graveyards skip spots; Spire neighbourhood often empty.
- **Fix:** Seed 5 Fiery Fungus 30–70 m from the Scout on world NPC spawn; admin `creature fungus` still works.

### 2026-10-19 — World NPCs used absolute coords while Spire seating can move

- **Where:** `WorldNpcs` homes vs `Spire.SpotFor` / merge seating; also `WorldNpcs.Init` ran *before* `Spire.Load`.
- **What:** Scout/Herbalist stayed near old (28,22) / (70,−55) while the hub can seat elsewhere; fungus pocket followed the stranded Scout.
- **Fix:** Init after Spire load; `home`/`waypoints` are Spire-arrival offsets by default (`relativeToSpire`).

### 2026-10-19 — Fungus Scout walks through the Spire and vanishes

- **Where:** Scout patrol waypoints (Spire-relative) vs Spire deck footprint (~±48 m).
- **What:** Patrol cut across / under the Hub model on landscape height; NPC disappeared from view inside the Spire.
- **Fix:** Keep home/waypoints off `Spire.Covers` (push outward); Scout route moved to a NE arc outside the deck (~58–70 m offsets).

## Fixed

### 2026-10-18 — Admin panel: Find (item by name) locks the page up

- **Where:** `/admin` → *Give item* row → *Find (server log)* (`Admin.cs` button → `finditem` in `DevCommands.cs`).
- **What:** Clicking Find made the admin page (and often the game) stop responding for a long time.
- **Cause:** `finditem` logged every match on the world tick. An empty needle matched every name (`Contains("")`);
  short needles matched hundreds. Thousands of log lines stalled the world thread; the in-game browser froze with it.
- **Fix:** refuse an empty needle; log at most 40 matches; reply with the match count (chat + log). The admin button
  also refuses a blank field before queueing.
