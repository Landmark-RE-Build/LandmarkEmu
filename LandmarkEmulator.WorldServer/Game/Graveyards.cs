using LandmarkEmulator.WorldServer.Network;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Where a dead player can come back: the death window's "graveyard" choice (respawn type 11) takes them to the
    /// respawn site nearest to where they died. Sites:
    /// - placed Respawn Points (item 1331 "Respawn at this point.", prop 1225) and graveyards (Graveyard Centerpiece
    ///   1948 / prop 1518, Updated Graveyard Prop 2606 / prop 2108) on the landscape: anyone may use them;
    /// - the Spire, on the home continent (the world hub);
    /// - the player's own build site.
    /// The client's respawn request carries only the type (PlayerUpdate 0x3B {u32 type}), so the server picks the site;
    /// all of them are listed in RespawnLocations for the death window.
    /// </summary>
    public static class Graveyards
    {
        /// <summary>Props that are respawn sites: Respawn Point 1225 (item 1331), graveyards 1518 / 2108.</summary>
        private static readonly HashSet<uint> siteProps = new() { 1225, 1518, 2108 };

        /// <summary>A respawn site; <paramref name="ClaimId"/> is the build site it belongs to (0 = open land).</summary>
        public sealed record Site(ulong Guid, string Name, Vector3 Position, uint Zone, ulong ClaimId = 0);

        /// <summary>
        /// Every respawn site open to a player on their current landscape (plus their build site): respawn points and
        /// graveyards, every site's Starting Location (wiki Starting Location: "designate where on a Build Site players
        /// will be placed when they teleport to or revive on that Site"), the Spire, and the player's own site.
        /// </summary>
        public static List<Site> SitesFor(WorldSession session)
        {
            uint zone = session.ZoneId;
            var sites = new List<Site>();
            foreach ((ulong guid, uint propId, Vector3 at, string name) in Props.PlacedOf(zone, siteProps))
                sites.Add(new Site(guid, name.Length > 0 ? name : propId == 1225 ? "Respawn Point" : "Graveyard",
                    at + new Vector3(0f, 0.5f, 1.5f), zone, Claims.ClaimAt(zone, at) ?? 0));
            foreach ((ulong guid, _, Vector3 at, _) in Props.PlacedOf(zone, new HashSet<uint> { StoryTools.StartingLocationProp }))
                if (Claims.ClaimAt(zone, at) is ulong claim)
                    sites.Add(new Site(guid, "Build Site", at + new Vector3(0f, 0.5f, 0f), zone, claim));
            if (session.OnHomeContinent && Spire.Arrival is Vector3 spire)
                sites.Add(new Site(Spire.Guid, "Spire", spire, zone));
            if (Claims.ArrivalOf(session.PlayerGuid) is (Vector3 site, uint siteZone, ulong ownClaim))
                sites.Add(new Site(session.PlayerGuid, "Build Site", site, siteZone, ownClaim));
            return sites;
        }

        /// <summary>
        /// Where "Flee" takes a player who died at <paramref name="from"/>: inside a build site, that site's nearest
        /// Respawn Point or its Starting Location (wiki Respawn Point: "When a player dies on a build site ... revives at
        /// the nearest respawn location"; the Starting Location can't be turned off); elsewhere the nearest open-land
        /// site (graveyard, respawn point, the Spire, their own site). Null when there is none.
        /// </summary>
        public static Site Nearest(WorldSession session, Vector3 from)
        {
            List<Site> sites = SitesFor(session);
            ulong diedOn = Claims.ClaimAt(session.ZoneId, from) ?? 0;
            if (diedOn != 0 && sites.Where(s => s.ClaimId == diedOn && s.Zone == session.ZoneId)
                    .OrderBy(s => Vector3.Distance(s.Position, from)).FirstOrDefault() is Site onSite)
                return onSite;
            var open = sites.Where(s => s.ClaimId == 0 || s.Guid == session.PlayerGuid).ToList();
            return open.Where(s => s.Zone == session.ZoneId).OrderBy(s => Vector3.Distance(s.Position, from)).FirstOrDefault()
                ?? open.FirstOrDefault() ?? sites.FirstOrDefault();
        }
    }
}
