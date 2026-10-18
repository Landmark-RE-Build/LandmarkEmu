#!/bin/sh
# The world server keeps its saved state as files beside its program (AppContext.BaseDirectory). Link each of those
# paths to the persistent /data volume so they survive rebuilding the container.
set -e
mkdir -p /data
for dir in TerrainEdits Contests Portraits SiteImages TemplateImages TemplateProps Reports Telemetry; do
    mkdir -p "/data/$dir"
    rm -rf "/app/$dir"
    ln -s "/data/$dir" "/app/$dir"
done
for file in felled_trees.txt gathered_nodes.txt dev_commands.txt opcode_stats.tsv ItemAudit.txt CombatGear.txt; do
    touch "/data/$file"
    rm -f "/app/$file"
    ln -s "/data/$file" "/app/$file"
done
# The log goes with the saved files too (on a host folder it can be read without docker exec). The previous runs' logs
# are kept as console.0.log (newest) .. console.4.log, so a restart or a recreated container never loses what led up
# to it (2026-10-18: a recreated world wiped the log of the session that dropped).
sed -i 's#Name="console\.#Name="/data/console.#g' /app/nlog.config
cd /app
exec dotnet LandmarkEmulator.WorldServer.dll "$@"
