# Playtest PT1 — World NPC dialogue (Fungus Scout)

**Phase:** Runtime + interact dialogue (no quest Journal yet required).  
**Gate:** Pass before starting Phase 2 work for feedback, or note solo-gate below.

## Setup

- World server with `Resources/Npcs/fungus_scout.json` loaded (normal world start).
- Tester on a normal character (admin not required for tasks).
- Facilitator (admin): `npc goto fungus_scout` to teleport; `npc reset` to clear flags between runs; `npc talk fungus_scout` forces the greet without clicking.

**Location:** Home continent, near the Spire (~28, ground, 22). Nameplate **Fungus Scout** (small Patroller kobold).  
**Not PT1:** Spire Herbalist (shaman, farther south-east) — until the Scout quest is done it only chat-lines with **no** Yes/No.

## Tasks


| #   | Task                                                                 | Pass if                                                                | Pass? |
| --- | -------------------------------------------------------------------- | ---------------------------------------------------------------------- | ----- |
| 1   | Travel to the Scout (or facilitator `npc goto fungus_scout`)         | NPC visible, named **Fungus Scout**, not hostile                       | PASS  |
| 2   | Interact once (or `npc talk fungus_scout`); choose **No** / **Yes**  | Yes and No appear on **your** bubble; decline or accept line; no crash | PASS  |
| 3   | Interact again                                                       | Reminder / re-offer line (not the identical first greeting alone)      | PASS  |
| 4   | Facilitator runs `npc reset` on the tester; interact; choose **Yes** | Accept / thank-you line                                                | PASS  |
| 5   | Interact again                                                       | Idle / accepted line (not the first greeting)                          | PASS  |
| 6   | Second character (or alt) interacts for the first time               | Full first greeting / Yes-No again                                     |       |


## Feedback (copy into chat / issue)

- Was the NPC easy to find?
- Was Yes/No obvious?
- Did lines feel repeated or clear?
- Any bubble/chat missing for you or a friend nearby?
- What should we change next?

## Exit criteria

All tasks pass for one facilitator character + one other character (or two sessions). Feedback noted here or in the issue.

**Solo gate (if no second player):** run tasks 1–5 twice with `npc reset` between; skip task 6 and note “solo gate” here: _______________