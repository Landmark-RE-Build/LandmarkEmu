using LandmarkEmulator.WorldServer.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The fall guard of the cavern trips (bug 2026-10-18, "teleporting to an underground adventure put me under the map";
    /// Graveyards.Arrivals.cs has the evidence). An Underground Adventure (and a group member following it) arrives on a
    /// cavern graveyard, where no cave floor within 40 m lies more than 5 m below the arrival on any of the 1,944 island
    /// graveyards (Graveyards.CheckArrivals) and the stamped box's bottom is 5.4 m below it. A player reported more than
    /// <see cref="FallDepth"/> below the arrival, within <see cref="Reach"/> of it, in the first <see cref="Window"/> after
    /// the loading screen dropped has therefore fallen through the world: the client had no collision there yet (the logged
    /// fall: movement word 0x441 → 0x29 at the drop, −101 m/s two seconds later with the "fell off" bit 0x1000000). The
    /// player is re-zoned back to the arrival (a loading screen again, during which the client holds the player until its
    /// cells are there, FUN_143324910); a second fall goes to the landscape's shallowest graveyard open to trips instead,
    /// a third is left alone (logged). [I ours: what live did after such a fall is not known; the client's own way out of
    /// a stuck spot is the Escape menu's Evac to Safety.] Positions are only judged once the re-zone has loaded and the
    /// player is shown (PlayerManager hold released), so the loading screen's stale reports never count.
    /// Setting <see cref="WorldServerConfiguration.CavernArrivalRescue"/> (Docker CAVERN_ARRIVAL_RESCUE, default true).
    /// </summary>
    public static class CavernArrivalGuard
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>How far below the arrival a player is taken to have fallen through (metres) [I ours; see the class notes].</summary>
        public const float FallDepth = 12f;
        /// <summary>How far from the arrival (sideways, metres) a fall is still the arrival's.</summary>
        public const float Reach = 60f;
        /// <summary>How long after the loading screen dropped the player is watched.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
        /// <summary>A watch whose loading screen never dropped ends after this long.</summary>
        public static readonly TimeSpan Limit = TimeSpan.FromMinutes(4);
        /// <summary>Falls brought back per trip; after that the player is left where they are.</summary>
        public const int MaxRescues = 2;

        /// <summary>The setting CavernArrivalRescue (read at each trip).</summary>
        public static bool Enabled
            => LandmarkEmulator.Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.CavernArrivalRescue ?? true;

        private sealed class Watch
        {
            public Vector3 At;
            public uint Zone;
            public DateTime Armed;
            public DateTime? Dropped;
            public int Rescues;
        }

        private static readonly Dictionary<WorldSession, Watch> watches = new();

        /// <summary>A cavern trip to <paramref name="at"/> on <paramref name="zone"/> has been sent: watch its first seconds.</summary>
        public static void Arm(WorldSession session, Vector3 at, uint zone)
        {
            if (!Enabled)
                return;
            lock (watches)
                watches[session] = new Watch { At = at, Zone = zone == 0 ? Terrain.TerrainStore.HomeContinent : zone, Armed = DateTime.UtcNow };
        }

        /// <summary>Is the player being watched (dev / tests)?</summary>
        public static bool IsWatching(WorldSession session)
        {
            lock (watches)
                return watches.ContainsKey(session);
        }

        public static void Forget(WorldSession session)
        {
            lock (watches)
                watches.Remove(session);
        }

        /// <summary>Is a position a fall through the world from an arrival (see the class notes)?</summary>
        public static bool FellThrough(Vector3 arrival, Vector3 position)
            => position.Y < arrival.Y - FallDepth
            && Vector2.Distance(new Vector2(arrival.X, arrival.Z), new Vector2(position.X, position.Z)) <= Reach;

        /// <summary>
        /// What to do with a watched player's position: <c>Wait</c> (not in the world at the spot yet), <c>Keep</c> (watching),
        /// <c>Done</c> (the watch is over), <c>Rescue</c> (fallen through). <paramref name="shown"/>: the re-zone has loaded
        /// and the loading screen dropped.
        /// </summary>
        public enum Verdict { Wait, Keep, Done, Rescue }

        /// <summary>The decision of <see cref="Moved"/>, without side effects other than the watch's own times.</summary>
        private static Verdict Judge(Watch w, uint zone, bool shown, Vector3 position, DateTime now)
        {
            if (now - w.Armed > Limit)
                return Verdict.Done;
            if (!shown)
                return Verdict.Wait;
            if (zone != w.Zone)
                return Verdict.Done;   // left the landscape (or the trip went elsewhere)
            w.Dropped ??= now;
            if (now - w.Dropped > Window)
                return Verdict.Done;
            return FellThrough(w.At, position) ? Verdict.Rescue : Verdict.Keep;
        }

        /// <summary>
        /// Pure form of the decision for tests: a watch armed at <paramref name="armed"/> for <paramref name="at"/>, its screen
        /// dropped at <paramref name="dropped"/> (null: not yet), judged at <paramref name="now"/>.
        /// </summary>
        public static Verdict JudgeFor(Vector3 at, uint watchZone, DateTime armed, DateTime? dropped, uint zone, bool shown, Vector3 position, DateTime now)
            => Judge(new Watch { At = at, Zone = watchZone, Armed = armed, Dropped = dropped }, zone, shown, position, now);

        /// <summary>From <see cref="WorldSession"/> after each position update of a player (world thread).</summary>
        public static void Moved(WorldSession session, Vector3 position)
        {
            Watch w;
            Verdict verdict;
            lock (watches)
            {
                if (!watches.TryGetValue(session, out w))
                    return;
                bool shown = !Teleport.RezoneLoading(session) && !session.IsArriving && !PlayerManager.IsHeld(session);
                verdict = Judge(w, session.ZoneId, shown, position, DateTime.UtcNow);
                if (verdict == Verdict.Done || (verdict == Verdict.Rescue && w.Rescues >= MaxRescues))
                    watches.Remove(session);
            }
            if (verdict != Verdict.Rescue)
                return;
            if (w.Rescues >= MaxRescues)
            {
                log.Warn($"{session.CharacterName} fell through the world again at {position} (arrival {w.At} on landscape {w.Zone}); "
                    + $"{MaxRescues} rescues done, left there.");
                return;
            }
            w.Rescues++;
            Vector3 from = w.At;
            if (w.Rescues == MaxRescues && Graveyards.ShallowestTripArrival(w.Zone, w.At) is Vector3 other)
                w.At = other;
            w.Dropped = null;
            w.Armed = DateTime.UtcNow;
            log.Warn($"{session.CharacterName} fell through the world after a cavern trip: at {position}, {from.Y - position.Y:0} m below the "
                + $"arrival {from} on landscape {w.Zone}; re-zoned to {w.At} (rescue {w.Rescues} of {MaxRescues}).");
            Teleport.To(session, w.At, snapToGround: false, loadingScreen: true);
        }
    }
}
