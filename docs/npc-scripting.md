# World NPC scripting

Server-authored world NPCs (not claim Story Tools Links). They spawn on the home continent, speak with `DisplayPropText` bubbles, keep **per-player** flags, and can offer Journal goals via the achievements bus.

## Layout

| Piece | Where |
|---|---|
| Runtime | `LandmarkEmulator.WorldServer/Game/Npc/WorldNpcs*.cs` |
| Definitions | `LandmarkEmulator.WorldServer/Resources/Npcs/*.json` |
| Flags DB | `character_npc_flag` |
| Quest goals | `Achievements.NpcQuests.cs` (ids 9101+) |
| Playtests | [docs/playtests/npc-pt1.md](playtests/npc-pt1.md) … pt4 |

## Definition schema (JSON)

```json
{
  "id": "fungus_scout",
  "displayName": "Fungus Scout",
  "spawnerName": "Spawner: Kobold",
  "zone": 0,
  "relativeToSpire": true,
  "home": [ 28, 0, 22 ],
  "yaw": 0,
  "hailRange": 14,
  "dwellSeconds": 4,
  "waypoints": [ [ 28, 0, 22 ], [ 34, 0, 28 ] ],
  "questAchievementId": 9101,
  "questObjectiveId": 9111,
  "questRequired": 3,
  "requiresNpcTurnedIn": null,
  "lines": {
    "greet": { "text": "...", "mode": 5, "seconds": 12, "question": true }
  }
}
```

- **zone** `0` = home continent at load.
- **relativeToSpire** (default true): `home` / `waypoints` X/Z are offsets from the landscape Spire arrival (so NPCs follow when Spire seating moves). Set false for absolute local metres.
- **mode:** 3 chat, 4 bubble, 5 bubble + chat.
- **question:** Yes/No bubble; answer sets `met` / `accepted` (and may offer the quest).
- **requiresNpcTurnedIn:** other NPC id whose `turned_in` flag must be set (chain lock).
- **questRequired ≤ 1:** gift completes on Yes (Herbalist).

### Flag conventions

| Key | Meaning |
|---|---|
| `hailed` | Proximity hail already shown |
| `met` | Saw the greeting (Yes or No) |
| `accepted` | Accepted the ask / quest |
| `turned_in` | Finished the quest / gift |

## Admin commands

```
npc reload
npc goto <id>
npc talk <id>
npc reset
npc flag <id> <key> [value]
```

(`npc <model> [dx dz]` still spawns a visual test model.)

`npc goto` re-zones onto the NPC’s landscape when needed (default: home continent / Spire land). A same-land teleport alone will not find NPCs if you are on a Pioneer or other landscape.

`npc talk <id>` runs that NPC’s dialogue for you (useful when two NPCs look similar). Question lines put Yes/No on **your** bubble (your client always has you as a full actor).

## Add a line (mini-recipe)

1. Edit `Resources/Npcs/fungus_scout.json` — change a `lines.*.text` string.
2. In-game (admin): `npc reload`.
3. `npc goto fungus_scout`, interact — new text should show.

## Add a quest goal

1. Reserve achievement / objective ids in `Achievements.NpcQuests.cs` and register them in `NpcQuests`.
2. Point the JSON `questAchievementId` / `questObjectiveId` / `questRequired` at those ids.
3. Kill credit for fungus text is shared with the live “Dispatch explode-y mushrooms” objective text (ProgressByText).

## Non-goals (for now)

- Claim Story Tools Link integration
- QuestBase (0x34) packets
- Storybricks core-values AI
- Client quest UI / Lua QuestHandler
