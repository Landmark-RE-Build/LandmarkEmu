using LandmarkEmulator.WorldServer.Network;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Where a dead player can come back: the death window's "graveyard" choice (respawn type 11) takes them to the
    /// respawn site nearest to where they died. Sites:
    /// - the landscape's own graveyards (<see cref="Graveyards"/> partial Graveyards.Landscape.cs): one in every Chaos
    ///   Cavern (the client's Graveyard01 cave pieces) and, on the surface, the red circles of the map;
    /// - placed Respawn Points (item 1331 "Respawn at this point.", prop 1225) and graveyards (Graveyard Centerpiece
    ///   1948 / prop 1518, Updated Graveyard Prop 2606 / prop 2108) on the landscape: anyone may use them;
    /// - the Spire of the landscape (the hub; every landscape has one since TASKS 6.49);
    /// - the player's own build site.
    /// The client's respawn request carries only the type (PlayerUpdate 0x3B {u32 type}), so the server picks the site;
    /// all of them are listed in RespawnLocations for the death window.
    /// <para>
    /// Layers (FEATURES #47): dying below the first layer revives you at one of the graveyards spread over the
    /// underground layers (17 Dec 2014, P2296) and a fix for always going to the same graveyard of a layer instead of
    /// the nearest (5 Feb 2015, P2718): a player who dies in
    /// the Chaos Caverns flees to the nearest cavern graveyard of that landscape, one on the surface to the nearest
    /// surface site. Every Graveyard01 piece of the shipped placements is on cave tier 1 (VoxelLayerPropertyGroups), so the
    /// launch caverns are one layer; which layer a player is on is decided by depth (<see cref="IsUnderground"/>).
    /// </para>
    /// </summary>
    public static partial class Graveyards
    {
        /// <summary>Props that are respawn sites: Respawn Point 1225 (item 1331), graveyards 1518 / 2108.</summary>
        private static readonly HashSet<uint> siteProps = new() { 1225, 1518, 2108 };

        /// <summary>A respawn site; <paramref name="ClaimId"/> is the build site it belongs to (0 = open land).</summary>
        /// <param name="Facing">Which way a player arriving here faces: a Starting Location's rotation (P5825), else null.</param>
        /// <param name="Underground">A Chaos Cavern site: the move must not let the client snap the player to the
        /// terrain above (Teleport.ToZone snapToGround false, as the warp network's crystals).</param>
        public sealed record Site(ulong Guid, string Name, Vector3 Position, uint Zone, ulong ClaimId = 0, Quaternion? Facing = null,
            bool Underground = false);

        /// <summary>
        /// Every respawn site open to a player on their current landscape (plus their build site): the landscape's own
        /// graveyards of the layer <paramref name="from"/> is on (the player's position when not given), respawn points and
        /// placed graveyards, every site's Starting Location (wiki Starting Location: it marks where players arrive on
        /// a site when they teleport there or revive there), the Spire, and the player's own site.
        /// </summary>
        public static List<Site> SitesFor(WorldSession session, Vector3? from = null)
        {
            uint zone = session.ZoneId;
            var sites = new List<Site>();
            bool underground = IsUnderground(zone, from ?? session.Position);
            foreach (Yard yard in YardsOf(zone, underground))
                sites.Add(new Site(yard.Guid, "Graveyard", yard.Arrival, zone, 0, null, yard.Cavern));
            foreach ((ulong guid, uint propId, Vector3 at, string name) in Props.PlacedOf(zone, siteProps))
                sites.Add(new Site(guid, name.Length > 0 ? name : propId == 1225 ? "Respawn Point" : "Graveyard",
                    at + new Vector3(0f, 0.5f, 1.5f), zone, Claims.ClaimAt(zone, at) ?? 0));
            foreach ((ulong guid, _, Vector3 at, _) in Props.PlacedOf(zone, new HashSet<uint> { StoryTools.StartingLocationProp }))
                if (Claims.ClaimAt(zone, at) is ulong claim)
                    sites.Add(new Site(guid, "Build Site", at + new Vector3(0f, 0.5f, 0f), zone, claim, Props.RotationOf(guid)));
            // The landscape's Spire (every landscape has one, TASKS 6.49).
            if (Spire.ArrivalOn(zone) is Vector3 spire)
                sites.Add(new Site(Spire.GuidOn(zone), "Spire", spire, zone));
            if (Claims.ArrivalOf(session.PlayerGuid) is (Vector3 site, uint siteZone, ulong ownClaim))
                sites.Add(new Site(session.PlayerGuid, "Build Site", site, siteZone, ownClaim, Claims.ArrivalFacingOf(session.PlayerGuid)));
            return sites;
        }

        /// <summary>
        /// Where "Flee" takes a player who died at <paramref name="from"/>: inside a build site, that site's nearest
        /// Respawn Point or its Starting Location (wiki Respawn Point: a player who dies on a site revives at its
        /// nearest respawn location; the Starting Location can't be turned off); in a Chaos Cavern the nearest cavern
        /// graveyard of that landscape (the layer rule, see the class notes); elsewhere the nearest open-land site
        /// (graveyard, respawn point, the Spire, their own site). Null when there is none.
        /// </summary>
        public static Site Nearest(WorldSession session, Vector3 from)
        {
            List<Site> sites = SitesFor(session, from);
            ulong diedOn = Claims.ClaimAt(session.ZoneId, from) ?? 0;
            if (diedOn != 0 && sites.Where(s => s.ClaimId == diedOn && s.Zone == session.ZoneId)
                    .OrderBy(s => Vector3.Distance(s.Position, from)).FirstOrDefault() is Site onSite)
                return onSite;
            // Below the surface: the closest graveyard on that layer (P2718).
            if (sites.Where(s => s.Underground && s.Zone == session.ZoneId)
                    .OrderBy(s => Vector3.Distance(s.Position, from)).FirstOrDefault() is Site cavern)
                return cavern;
            var open = sites.Where(s => s.ClaimId == 0 || s.Guid == session.PlayerGuid).ToList();
            return open.Where(s => s.Zone == session.ZoneId).OrderBy(s => Vector3.Distance(s.Position, from)).FirstOrDefault()
                ?? open.FirstOrDefault() ?? sites.FirstOrDefault();
        }
    }
}
