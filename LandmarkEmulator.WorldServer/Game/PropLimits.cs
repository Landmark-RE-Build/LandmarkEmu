using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// A build site's prop category limits as the Build Mode widget shows them ("12/50 Chests", BuildModeWidget.as reads
    /// data source ClaimDialogs.PropLimits: CurrentCount / MaxCount / Name). The client keeps them per site (claim record
    /// +0x388, filled by FUN_142d1f070 for the site the player is on) from ClaimsBase sub 0x5F UpdatePropCategoryCount
    /// (reader FUN_142d08c00, handler case in FUN_142d1ff50): {u64 site, u32 n × {u32 category name text, u32 current,
    /// u32 max}}. The one limited category known for launch is storage: "up to 50 chests per build site" (update notes,
    /// 21 March 2016; PropCategory 93 "Chests", text 573121); placing past it is refused with the client's own chest-limit
    /// error (<see cref="Props"/>).
    /// <para>
    /// Sent when a player walks onto a site (once per site entered) and to everyone on a site when its chest count
    /// changes — nothing while the count stays the same.
    /// </para>
    /// </summary>
    public static class PropLimits
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte UpdatePropCategoryCountSub = 0x5F;
        private const uint ChestsName = 573121;

        private static readonly object sync = new();
        /// <summary>The site each player was last seen on (null: open land).</summary>
        private static readonly Dictionary<WorldSession, ulong?> siteOf = new();

        /// <summary>Player moved: entering a site sends its counts.</summary>
        public static void Moved(WorldSession session, Vector3 position)
        {
            ulong? site = Claims.ClaimAt(session.ZoneId, position);
            lock (sync)
            {
                if (siteOf.TryGetValue(session, out ulong? last) && last == site)
                    return;
                siteOf[session] = site;
            }
            if (site is ulong id)
                Send(session, id);
        }

        /// <summary>A chest was placed or picked up at a point: everyone on that site gets the new count.</summary>
        public static void Changed(uint zone, Vector3 position)
        {
            if (Claims.ClaimAt(zone, position) is not ulong site)
                return;
            List<WorldSession> on;
            lock (sync)
                on = siteOf.Where(p => p.Value == site && p.Key.ZoneId == zone && !p.Key.Disconnected).Select(p => p.Key).ToList();
            foreach (WorldSession session in on)
                Send(session, site);
        }

        public static void Forget(WorldSession session)
        {
            lock (sync)
                siteOf.Remove(session);
        }

        private static void Send(WorldSession session, ulong site)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(UpdatePropCategoryCountSub);
            w.Write(site);
            w.Write(1u);
            int chests = Props.ChestsOn(session.ZoneId, site);
            w.Write(ChestsName);
            w.Write((uint)chests);
            w.Write((uint)Chests.MaxPerSite);
            session.EnqueueMessage(ZoneMessageOpcode.ClaimsBase, output.ToArray());
            log.Debug($"{session.CharacterName}: site {site} prop limits sent (chests {chests}/{Chests.MaxPerSite}).");
        }
    }
}
