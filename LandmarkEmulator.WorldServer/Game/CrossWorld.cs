using LandmarkEmulator.Internal;
using LandmarkEmulator.WorldServer.Network;
using NLog;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Travel to another world from the Landscapes window (TASKS B.10; wiki "Servers and Landscapes": every world was
    /// listed in the teleport window, P0514 / P4400 / P5734). The 0.695 client never reconnects to change world: it has
    /// no reachable CharacterTransferServerRequest sender, and ClientBeginZoning carries the new world id (+0xa8) and is
    /// handled like any other landscape change (OPEN-LEADS L114). So live's gateway moved the connection to the other
    /// world's zone. Here:
    /// <list type="number">
    /// <item>The Landscapes window lists the worlds whose zones register on the same MultiWorld gateway (WorldsReachable,
    /// <see cref="Landscapes.SetReachableWorlds"/>), each with this world's landscapes [I: every world ran the same set].</item>
    /// <item>TeleportRequest {u32 world, u32 zone} for one of them: the target landscape and world (LastServerId) are saved,
    /// the session leaves this world, and HandOver 0x25 with the target world goes to the gateway
    /// (<see cref="GatewayLink.HandOverToWorld"/>).</item>
    /// <item>The gateway attaches the connection to the target world's zone as a transfer (AttachConnection isTransfer);
    /// that zone loads the character and re-zones the client to the landscape's arrival as its own travel does
    /// (<see cref="WorldSession.OnReattach"/> → <see cref="Landscapes.TravelTo"/>; ClientBeginZoning with its world id).</item>
    /// </list>
    /// The same hand-over carries Travel to Friend (ArriveNear) and Game Finder trips (JoinGame, with ReturnTo / ArriveAt
    /// for the way back when FinderReturnHome is on; InstancedGames.Finder.cs, L31), and Lost Crystal / Underground Adventure
    /// trips to another world's caverns (CavernTrip, L131; <see cref="CavernTripsEnabled"/>).
    /// Friends, guildmates and groups see no offline / online in between: the social service's presence grace covers the
    /// leave on one world and the enter on the other (social-service.md §5.5).
    /// </summary>
    public static class CrossWorld
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Travel to Friend when the friend is on another world (TASKS E.13; the social service's presence says where):
        /// the same hand-over, to the friend's landscape, arriving beside them (HandOver / AttachConnection ArriveNear →
        /// <see cref="Abilities.ArriveBesideFriend"/> on the friend's world). False when that world isn't reachable here.
        /// </summary>
        public static bool TravelToFriend(WorldSession session, ulong friendWorld, uint friendZone, ulong friend)
        {
            Landscapes.OtherWorldInfo target = Landscapes.OtherWorlds.FirstOrDefault(o => o.ServerWorld == friendWorld);
            if (target == null)
                return false;
            Travel(session, target, friendZone, friend);
            return true;
        }

        /// <summary>
        /// A Game Finder match on another world taken with Join Now (OPEN-LEADS L31): the same hand-over, to the game's
        /// landscape; the world there re-zones the player to the game's site and joins them (HandOver / AttachConnection
        /// JoinGame → <see cref="InstancedGames.ArriveToJoin"/>). False when that world isn't reachable here.
        /// </summary>
        public static bool JoinGame(WorldSession session, uint serverWorld, uint zoneId, ulong game)
        {
            Landscapes.OtherWorldInfo target = Landscapes.OtherWorlds.FirstOrDefault(o => o.ServerWorld == serverWorld);
            // With FinderReturnHome the player is taken back here, to this spot, once they leave that game (InstancedGames
            // .TickReturns there); off (the default) they stay on that world, as on live as far as the sources show [I].
            WorldSpot home = InstancedGames.ReturnHomeEnabled && GatewayLink.ServerWorldId != 0
                ? new WorldSpot(GatewayLink.ServerWorldId, session.ZoneId, session.Position.X, session.Position.Y, session.Position.Z)
                : default;
            return target != null && Travel(session, target, zoneId, joinGame: game, returnTo: home);
        }

        /// <summary>
        /// The way back from a Game Finder trip (FinderReturnHome, L31): the same hand-over, to the world, landscape and spot
        /// the player left from (AttachConnection ArriveAt there). False when that world isn't reachable from here.
        /// </summary>
        public static bool ReturnHome(WorldSession session, WorldSpot home)
        {
            Landscapes.OtherWorldInfo target = Landscapes.OtherWorlds.FirstOrDefault(o => o.ServerWorld == home.World);
            return target != null && Travel(session, target, home.Zone, arriveAt: home);
        }

        // ---- Chaos Cavern trips to other worlds (OPEN-LEADS L131) ----------------------------------------------------------

        /// <summary>
        /// Setting CavernTripsOtherWorlds (Docker CAVERN_TRIPS_OTHER_WORLDS, on by default = live): a Lost Crystal's trip and
        /// Travel → An Underground Adventure may land in a cavern on another world of this gateway.
        /// <para>
        /// Why live did this [I strong, OPEN-LEADS L131]: each Lost Crystal use may go to another world and biome (7
        /// Jun 2016, P5734), and a world is a server in the game's own words — the wiki's "Servers and Landscapes"
        /// shows servers (worlds in game) on the left and landscapes (zones) on the right, every server with the same
        /// landscapes; players name a place "Emerald Vale on Serenity"
        /// (forum 57596 #114) and "Determination / Silver Shallows" (57065 #224), and Serenity / Determination / Courage are
        /// the client's world name texts 560916 / 560917 / 560911 [P] (24 Jun 2016, P5905: Courage finished, work
        /// started on more worlds); the developers speak of teleports between landscapes or worlds (P4907) and of
        /// changing worlds (P5841). Before 7 Jun players complained that the crystal never left their island (57065
        /// #53); the note answers with other worlds <i>and</i> biomes. 30 Jun
        /// (P5927): the Travel button goes to Chaos Caverns in any zone, as Lost Crystals already did, so the button
        /// rolls the same way [I]. No source shows the client doing anything but its usual
        /// zoning: NextUndergroundReply / RandomUndergroundRequest carry no destination, and a change of world is a
        /// ClientBeginZoning with the new world id (L114), which the hand-over below already uses.
        /// </para>
        /// <para>
        /// The roll [I, ours]: this world and every other world reachable on the gateway are equally likely (every world ran
        /// the same landscapes, so a pick over all of the realm's caverns weights the worlds evenly); the landscape there is
        /// one with Chaos Caverns other than the player's own when it can be; the world it lands on picks the cavern from
        /// its own outposts, lockouts and graveyards (<see cref="ArriveOnCavernTrip"/>). Nobody is brought back: no source
        /// mentions a return, and the next trip, the Landscapes window or a logout / login (LastServerId) moves them on.
        /// With one world (no multi-world gateway) nothing changes.
        /// </para>
        /// </summary>
        public static bool CavernTripsEnabled
            => Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.CavernTripsOtherWorlds != false;

        private static readonly System.Random random = new();

        /// <summary>
        /// The world roll of a cavern trip (L131): one of the other worlds this player can be handed to, or null to stay
        /// here (always null with the setting off or no other world reachable).
        /// </summary>
        public static Landscapes.OtherWorldInfo RollTripWorld(WorldSession session)
        {
            if (!CavernTripsEnabled || !GatewayLink.Enabled || session.Link is not RemoteClientLink)
                return null;
            var worlds = Landscapes.OtherWorlds;   // never this world (SetReachableWorlds leaves it out)
            if (worlds.Count == 0)
                return null;
            int pick = random.Next(worlds.Count + 1);   // + 1: this world
            return pick < worlds.Count ? worlds[pick] : null;
        }

        /// <summary>The landscape a trip to another world is handed to: one of <paramref name="lands"/> (Chaos Cavern
        /// landscapes), not the player's own while others remain; 0 when there is none.</summary>
        public static uint TripLandscape(WorldSession session, System.Collections.Generic.IReadOnlyList<uint> lands)
        {
            var choices = lands.Where(z => Landscapes.Get(z) is { EventIsland: false }).ToList();
            if (choices.Count > 1)
                choices.Remove(session.ZoneId);
            return choices.Count == 0 ? 0 : choices[random.Next(choices.Count)];
        }

        /// <summary>
        /// A cavern trip to another world (L131): the hand-over of <see cref="Travel"/> to <paramref name="zone"/> there,
        /// marked as a cavern trip so that world puts the player in one of its caverns; <paramref name="follow"/>: the group
        /// member whose Underground Adventure they accepted (arrive beside them when they are there). False (nothing done)
        /// when that world or landscape can't be reached from here.
        /// </summary>
        public static bool CavernTrip(WorldSession session, Landscapes.OtherWorldInfo target, uint zone, Internal.CavernTripKind kind,
            ulong follow = 0)
        {
            if (target == null || Landscapes.Get(zone) is not { EventIsland: false } || !CanReach(session, target.ServerWorld))
                return false;
            string name = session.CharacterName;
            System.Threading.Tasks.Task saved = session.SaveForWorldMove(zone, target.ServerWorld);
            if (!GatewayLink.HandOverToWorld(session, target.ServerWorld, zone, saved, arriveNear: follow, cavernTrip: kind))
                return false;
            log.Info($"{name} goes underground on world {target.ServerWorld} (client id {target.ClientWorld}), landscape {zone}: {kind}"
                + (follow != 0 ? $", following 0x{follow:X}." : "."));
            return true;
        }

        /// <summary>
        /// The arrival of a cavern trip from another world (WorldSession's transfer re-zone): a Lost Crystal trip to an
        /// outpost's cavern here (<see cref="Outposts.ArriveFromOtherWorld"/>), an Underground Adventure to a cavern
        /// graveyard here or beside the member it follows (<see cref="Caves.ArriveFromOtherWorld"/>). False when nothing
        /// moved the player (no caverns open here): the caller then uses the landscape's usual arrival.
        /// </summary>
        public static bool ArriveOnCavernTrip(WorldSession session, Internal.CavernTripKind kind, ulong follow)
        {
            switch (kind)
            {
                case Internal.CavernTripKind.LostCrystal:
                    Outposts.ArriveFromOtherWorld(session);
                    break;
                case Internal.CavernTripKind.UndergroundAdventure:
                    Caves.ArriveFromOtherWorld(session, follow);
                    break;
                default:
                    return false;
            }
            return session.RezoneTarget != null;
        }

        /// <summary>
        /// Dev "cavetrip &lt;crystal|adventure&gt; [client world id] [zone]": the cavern trip to another world now, without the
        /// roll or the setting (harness test of L131). The result line for the log.
        /// </summary>
        public static string DevCavernTrip(WorldSession session, string kind, uint? clientWorld, uint? zone)
        {
            Internal.CavernTripKind trip = kind switch
            {
                "crystal" => Internal.CavernTripKind.LostCrystal,
                "adventure" => Internal.CavernTripKind.UndergroundAdventure,
                _ => Internal.CavernTripKind.None
            };
            if (trip == Internal.CavernTripKind.None)
                return "usage: cavetrip <crystal|adventure> [client world id] [zone]";
            Landscapes.OtherWorldInfo target = clientWorld is uint id ? Landscapes.OtherWorld(id) : Landscapes.OtherWorlds.FirstOrDefault();
            if (target == null)
                return $"no other world reachable{(clientWorld is uint w ? $" with client id {w}" : "")} (GATEWAY=1 and a second world?)";
            uint land = zone ?? TripLandscape(session, trip == Internal.CavernTripKind.LostCrystal || Caves.GraveyardLandscapes.Count == 0
                ? Caves.Landscapes : Caves.GraveyardLandscapes);
            return CavernTrip(session, target, land, trip)
                ? $"handed over to world {target.ServerWorld} (client id {target.ClientWorld}), landscape {land}, {trip}."
                : $"world {target.ServerWorld} / landscape {land} not reachable for this player.";
        }

        /// <summary>Can this player be handed over to that world (the world is on this gateway, the player came through it)?</summary>
        public static bool CanReach(WorldSession session, uint serverWorld)
            => GatewayLink.Enabled && session.Link is RemoteClientLink && Landscapes.OtherWorlds.Any(o => o.ServerWorld == serverWorld);

        public static bool Travel(WorldSession session, Landscapes.OtherWorldInfo target, uint zoneId, ulong arriveNear = 0, ulong joinGame = 0,
            WorldSpot returnTo = default, WorldSpot arriveAt = default)
        {
            Landscapes.Zone zone = Landscapes.Get(zoneId);
            if (zone == null || zone.EventIsland)
            {
                Chat.System(session, "That landscape isn't available.");
                return false;
            }
            if (!GatewayLink.Enabled || session.Link is not RemoteClientLink)
            {
                Chat.System(session, "Travel to other worlds isn't open on this server.");
                return false;
            }
            string name = session.CharacterName;
            System.Threading.Tasks.Task saved = session.SaveForWorldMove(zone.Id, target.ServerWorld);
            if (!GatewayLink.HandOverToWorld(session, target.ServerWorld, zone.Id, saved, arriveNear, joinGame, returnTo, arriveAt))
            {
                Chat.System(session, "Travel to other worlds isn't open on this server.");
                return false;
            }
            log.Info($"{name} travels to world {target.ServerWorld} (client id {target.ClientWorld}), landscape {zone.Id}"
                + (joinGame != 0 ? $" to join game 0x{joinGame:X}" + (returnTo.IsSet ? " (and back afterwards)." : ".") : arriveAt.IsSet ? ", back to where they left from." : "."));
            return true;
        }
    }
}
