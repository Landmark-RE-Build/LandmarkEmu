# Tunable content (AGENTS #22)

Server-authored loot / rate tables. Shipped defaults live here under `loot/` (lowercase — Linux images are case-sensitive).

**Live overrides** (edit without rebuilding the image):

`server-data/worlds/<WORLD_KEY>/content/` → container `/data/content/`

Precedence: baked (this tree) &lt; host override. On first start the world seeds empty
`/data/content/loot/*.json` from these defaults without overwriting host edits.

Reload in-game / admin console without a world restart:

- `loot reload`
- `content reload` (alias)

A bad or missing override keeps the previous good table and logs. Provenance stays in
each file’s `_meta` (`[P]` / `[I]` / ours). Quiver / creature weapons stay out of loot.
