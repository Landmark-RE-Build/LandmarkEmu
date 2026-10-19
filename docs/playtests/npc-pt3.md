# Playtest PT3 — Patrol and proximity hail

**Phase:** Routines + proximity.  
**Depends on:** PT2 happy path still works.

## Setup

- Same Scout. Prefer a fresh / `npc reset` character for hail tests.
- Watch from ~20 m so you can see the patrol.

## Tasks

| # | Task | Pass if | Pass? |
|---|---|---|---|
| 1 | Watch the Scout ~30 s without interacting | Walks between points / returns toward home | |
| 2 | Walk into hail range on a fresh/reset character | One hail bubble; no spam while standing still | |
| 3 | Leave and re-enter range | No repeat hail | |
| 4 | Interact during a walk | Dialogue; NPC stops or finishes the step sensibly | |
| 5 | Short PT2 path: accept → kill at least one → reminder | Quest still works with routines on | |

## Feedback

- Did patrol look intentional or broken?
- Was the hail welcome or annoying?
- Did talking while they walked feel OK?

## Exit criteria

PT3 tasks pass; no regression on PT2 accept/progress.
