# Playtest PT4 — Scout → Herbalist chain

**Phase:** Content pack + docs.  
**NPCs:** Fungus Scout (~28, 22), Spire Herbalist (~36, −18). Facilitator: `npc goto herbalist`.

## Setup

- Both NPCs loaded. Use `npc reset` for a cold start on the tester.

## Tasks


| #   | Task                                                                                         | Pass if                                   | Pass? |
| --- | -------------------------------------------------------------------------------------------- | ----------------------------------------- | ----- |
| 1   | Complete Scout quest (PT2 loop)                                                              | Turn-in succeeds                          | PASS  |
| 2   | Find the Herbalist (`npc goto herbalist` or walk east/south of Spire)                        | Spawned, named, interactable              | PASS  |
| 3   | On an alt / before Scout completion: talk to Herbalist                                       | Locked / teaser line only                 |       |
| 4   | After Scout turn-in: talk to Herbalist; accept the gift                                      | New dialogue; gift / Lumen step completes | PASS  |
| 5   | Interact again                                                                               | Done / epilogue line                      | PASS  |
| 6   | Facilitator: change one string in `Resources/Npcs/fungus_scout.json`, `npc reload`, interact | New text appears in-game                  |       |


## Feedback

- Was the chain discoverable?
- Did locked dialogue make sense?
- Want more quests like this?
- Priority next: more NPCs, better Journal text, map markers, moods, claim Links?

## Exit criteria

Chain completable from playtest sheets alone; doc mini-recipe (`npc reload`) verified once.