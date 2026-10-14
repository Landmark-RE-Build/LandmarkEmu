#!/bin/sh
# The "setup" service (docker-compose.yml): fills server-data/gamedata, the data the servers read, before they start.
#   - from your Landmark folder (/game, read-only): the data tables (Resources/*.txt) and the English texts
#     (Locale/en_us_data.dat);
#   - from this project (/project): the tables in data/, derived from the client;
#   - LandmarkEmu-terrain-heights.zip, if it is in the project folder: the ground heights of each landscape (trees,
#     creature footing, starting build sites). Unpacked once, and again only when the zip changes.
# It runs on every "docker compose up" and takes a second when nothing changed.
set -e

if [ ! -f /game/Landmark64.exe ] || [ ! -d /game/Resources ]; then
    echo "GAME_DIR in .env is not your Landmark folder: Landmark64.exe and Resources were not found there."
    echo "Set it to the folder with Landmark64.exe, e.g. C:/Program Files (x86)/Steam/steamapps/common/Landmark"
    exit 1
fi

cp /game/Resources/*.txt /gamedata/
if [ -f /game/Locale/en_us_data.dat ]; then
    cp /game/Locale/en_us_data.dat /gamedata/
else
    echo "Locale/en_us_data.dat was not found in the Landmark folder: names will be missing in game."
fi
cp -r /project/data/. /gamedata/

heights=/project/LandmarkEmu-terrain-heights.zip
if [ -f "$heights" ]; then
    stamp=$(stat -c '%s-%Y' "$heights")
    if [ "$(cat /gamedata/.terrain-heights 2>/dev/null)" != "$stamp" ]; then
        echo "Unpacking the terrain heights (once; about a minute)..."
        unzip -o -q "$heights" -d /gamedata
        echo "$stamp" > /gamedata/.terrain-heights
    fi
elif ! ls /gamedata/TerrainHeights*.bin >/dev/null 2>&1; then
    echo "No LandmarkEmu-terrain-heights.zip in the project folder: trees, ground checks and starting build sites stay off (see README)."
fi

echo "Game data ready: $(ls /gamedata | wc -l) files."
