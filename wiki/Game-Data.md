# Game Data

LandmarkEmu ships **no game files**. The servers read what they need from **your own Landmark folder**, and nothing
in that folder is ever changed (Docker mounts it read-only).

## Which Landmark folder

`GAME_DIR` in `.env` is your Landmark folder: the one with `Landmark64.exe` and `Resources`, usually
`C:/Program Files (x86)/Steam/steamapps/common/Landmark` (use forward slashes, also on Windows).

**It must be build 0.695.1.132647**, the build your players use. The server's data tables (items, recipes, props,
prices, site-game rules, texts) must match the client's, and 57 of them changed between 0.589 and 0.695. Steam installs
0.589, so update your own copy first with [LaunchPad](LaunchPad#the-update-to-0695) (from an `.lmpatch` file), exactly as
a player would. LaunchPad's records of the update live in a `LaunchPadEmu` folder inside the game folder; the server
ignores it.

On a server PC without the game (a Linux machine, for example), copy a 0.695 Landmark folder there unchanged, file
names and case as they are, and point `GAME_DIR` at it.

## What the server reads

**Copied at every start** by the `setup` container into `server-data/gamedata` (the servers read this copy):

| From | What | Used for |
|---|---|---|
| `GAME_DIR/Resources/*.txt` | The client's data tables | Items, inventory and equipment slots, recipes and crafting stations, props and their costs, voxel materials and biomes, ore veins, site-game rules, abilities, goals and more |
| `GAME_DIR/Locale/en_us_data.dat` | The English texts | Every name and message the server sends or checks (without it, names are missing in game) |
| the project's `data/` | Small tables derived from the client | e.g. weapon animation classes |
| `LandmarkEmu-terrain-heights.zip` (optional, in the project folder) | Ground-height grids, `TerrainHeights<zone>.bin` | Tree placement, creature footing, starting build sites. Unpacked once, and again only when the zip changes |

**Read in place** (the world server and the Environment Server mount `GAME_DIR` read-only as `/game`): the terrain and
world data under `CommonData/` (biome maps, ore-vein data, caves and cave pieces, voxel templates for the Spire, ruins,
graveyards and slabs), and `Resources/Models.txt` for model sizes.

The `setup` step refuses to go on if `GAME_DIR` has no `Landmark64.exe` or `Resources` folder
(`docker compose logs setup` shows it). `server-data/gamedata` is safe to delete: it is made again at the next start.

## The terrain heights

`LandmarkEmu-terrain-heights.zip` is a release asset of the project, not part of the source. Put it in the project
folder next to `docker-compose.yml`, **still zipped**. Without it trees, ground checks and starting build sites stay
off.

The grids are sampled from the running game client itself; the scripts that do it are in `tools/terrain/` (they need
Python and Frida, and a client logged in to the server). You only need them to make the grids yourself.

## Using another folder for the tables

`GAME_DATA_DIR` in `.env` (in the Advanced section, commented out) replaces `server-data/gamedata` with a folder of your
own. It must hold the same files the setup step copies (the 0.695 `Resources/*.txt` tables, `en_us_data.dat`, the
project's `data/` files and the terrain heights) from the same build as your players' client. Leave it unset unless you
have a reason: the setup step keeps the default folder up to date by itself.

## What the server never reads or sends

Game art, models, sounds and UI files stay on the players' PCs; the server only tells the client which of its own
assets to show.
