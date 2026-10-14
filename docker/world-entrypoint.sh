#!/bin/sh
# The world server keeps its saved state as files beside its program (AppContext.BaseDirectory). Link each of those
# paths to the persistent /data volume so they survive rebuilding the container.
set -e
mkdir -p /data
for dir in TerrainEdits Contests Portraits SiteImages TemplateImages TemplateProps Reports; do
    mkdir -p "/data/$dir"
    rm -rf "/app/$dir"
    ln -s "/data/$dir" "/app/$dir"
done
for file in felled_trees.txt gathered_nodes.txt dev_commands.txt opcode_stats.tsv; do
    touch "/data/$file"
    rm -f "/app/$file"
    ln -s "/data/$file" "/app/$file"
done
# The log goes with the saved files too (on a host folder it can be read without docker exec).
sed -i 's#fileName="console.log"#fileName="/data/console.log"#' /app/nlog.config
cd /app
exec dotnet LandmarkEmulator.WorldServer.dll "$@"
