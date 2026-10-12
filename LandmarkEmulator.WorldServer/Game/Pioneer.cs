using LandmarkEmulator.WorldServer.Storage;
using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using NLog;
using System;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// New players start on a Pioneer landscape (zones 417-421), on a build site already placed for them. From the
    /// March 2016 design notes (design/game-rules.md): Pioneer lands are flat, kept for these automatically placed
    /// sites (players can't place their own there, <see cref="Claims"/>), and the rest of the world is reached through
    /// the Landscapes window.
    /// </summary>
    public static class Pioneer
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Distance between candidate site centres, local metres: a 210 m site may not come within 285 m of another
        /// site's centre (Claims.IsFree), so centres 390 m apart are the closest a grid can pack them.
        /// </summary>
        private const float Spacing = 390f;
        /// <summary>Dry land at least this high (sea level is about 1000 m local).</summary>
        private const float DryLand = 1005f;
        /// <summary>Steepest ground (rise per metre) a starting site may sit on.</summary>
        private const float MaxSlope = 0.25f;
        /// <summary>Largest height difference between a site's middle and its corners, metres.</summary>
        private const float MaxStep = 6f;
        /// <summary>How far from a landscape's centre sites are looked for, local metres.</summary>
        private const float SearchRadius = 3600f;

        /// <summary>
        /// A brand-new character (never in the world: no saved position, no site) is given a site on the Pioneer land
        /// with the fewest sites and placed in its middle. Only landscapes with baked heights are used (the ground has
        /// to be known); without one the character starts at the Spire as before. Changes <paramref name="character"/>
        /// before the zone details are sent, so the client loads the Pioneer land directly.
        /// </summary>
        public static void Settle(CharacterModel character)
        {
            if (character.PositionX != 0f || character.PositionY != 0f || character.PositionZ != 0f
                || Claims.Owns(character.Id))
                return;
            if (Place(character.Id, character.Name) is not (uint zone, Vector3 at))
                return;
            character.ZoneId = zone;
            (character.PositionX, character.PositionY, character.PositionZ) = (at.X, at.Y, at.Z);
            ulong id = character.Id;
            DatabaseManager.Instance.CharacterDatabase.Save(c =>
            {
                var model = new CharacterModel { Id = id };
                c.Character.Attach(model);
                model.ZoneId = zone;
                model.PositionX = at.X;
                model.PositionY = at.Y;
                model.PositionZ = at.Z;
                c.MarkModified(model, nameof(model.ZoneId), nameof(model.PositionX), nameof(model.PositionY), nameof(model.PositionZ));
            });
            log.Info($"{character.Name} starts on Pioneer land {zone} at {at}.");
        }

        /// <summary>
        /// Grants a site on the least-crowded Pioneer land; returns the landscape and the local spot (1 m above the
        /// site's middle), or null when no Pioneer land has baked heights or room.
        /// </summary>
        public static (uint Zone, Vector3 At)? Place(ulong ownerGuid, string ownerName)
        {
            var lands = Landscapes.Zones.Values.Where(z => z.Pioneer && Terrain.TerrainHeights.Available(z.Id))
                .OrderBy(z => Claims.CountOn(z.Id)).ThenBy(z => z.Id).ToList();
            // Only the chosen land's grid is loaded (FindSpot); the others are just checked on disk.
            foreach (Landscapes.Zone land in lands)
                if (FindSpot(land.Id) is Vector3 spot)
                {
                    var global = Terrain.TerrainEdits.ToGlobal(spot);
                    Claims.Grant(ownerGuid, ownerName, land.Id, global.X, global.Y, global.Z);
                    return (land.Id, Gathering.ClearOfTrees(spot + new Vector3(0f, 1f, 0f), land.Id));
                }
            log.Warn($"No Pioneer land has room for {ownerName}'s starting site.");
            return null;
        }

        /// <summary>
        /// The free spot nearest a landscape's centre on a <see cref="Spacing"/> grid (so sites line up in rows) with dry,
        /// gentle ground under the whole site (its middle and four points 90 m out diagonally).
        /// </summary>
        private static Vector3? FindSpot(uint zone)
        {
            int rings = (int)(SearchRadius / Spacing);
            for (int ring = 0; ring <= rings; ring++)
                for (int i = -ring; i <= ring; i++)
                    for (int j = -ring; j <= ring; j++)
                    {
                        if (Math.Max(Math.Abs(i), Math.Abs(j)) != ring)
                            continue;   // only the square ring's edge: inner cells were tried already
                        float x = i * Spacing, z = j * Spacing;
                        if (Ground(zone, x, z) is not float y || !Flat(zone, x, z, y))
                            continue;
                        var global = Terrain.TerrainEdits.ToGlobal(new Vector3(x, y, z));
                        if (Claims.IsFree(zone, global.X, global.Z))
                            return new Vector3(x, y, z);
                    }
            return null;
        }

        private static bool Flat(uint zone, float x, float z, float y)
        {
            foreach ((float dx, float dz) in new[] { (-90f, -90f), (90f, -90f), (-90f, 90f), (90f, 90f) })
                if (Ground(zone, x + dx, z + dz) is not float corner || MathF.Abs(corner - y) > MaxStep)
                    return false;
            return true;
        }

        private static float? Ground(uint zone, float x, float z) =>
            Terrain.TerrainHeights.HeightAt(x, z, zone) is float y && y >= DryLand
                && Terrain.TerrainHeights.Slope(x, z, 2f, zone) <= MaxSlope ? y : null;
    }
}
