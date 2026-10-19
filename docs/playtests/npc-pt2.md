# Playtest PT2 — Scout's Errand (Journal quest)

**Phase:** Achievement-backed quest offer / credit / turn-in.  
**Depends on:** PT1 behaviours still working.

## Setup

- Same as PT1. Facilitator: `npc reset` (clears flags **and** Scout quest progress, refreshes Journal).
- Fiery Fungus: a pocket is seeded ~30–70 m around the Fungus Scout on world start / `npc reload`. Random surface fungus elsewhere is sparse. Admin shortcut: `creature fungus`.

## Tasks

| # | Task | Pass if | Pass? |
|---|---|---|---|
| 1 | Fresh / reset character; accept the Scout’s ask (Yes) | Goal appears in Journal / Goals tracker | |
| 2 | Open Journal; note the objective text | Mentions explode-y mushrooms (matches NPC ask) | |
| 3 | Kill Fiery Fungus until the tracker moves | Count advances in Journal (not only chat) | |
| 4 | Return and interact before finishing | Reminder dialogue; goal still active | |
| 5 | Finish three kills; return; interact | Turn-in line; goal completes; Lumen reward | |
| 6 | Interact again | Epilogue / done line; no second reward | |
| 7 | `npc reset`; accept again | Goal can be re-offered | |

## Feedback

- Did you understand what to kill and how many?
- Did the Journal help, or did you only use the NPC?
- Was the reward clear / satisfying?
- Did it feel like a Landmark goal or a quest?

## Exit criteria

Full accept → kill → turn-in once without admin flag edits; reset loop works.
