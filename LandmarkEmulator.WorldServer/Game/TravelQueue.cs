using LandmarkEmulator.Shared.Configuration;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The travel queue: a landscape with a player limit (<see cref="Capacity"/>) makes Travel-window arrivals wait
    /// (OPEN-LEADS L60, TASKS E.8). The client side is the 0.695 client's own, traced end to end:
    /// <list type="number">
    /// <item>The Landscapes window's TeleportRequest 0xFC/4 {u32 world, u32 zone} (Lua Teleport.TeleportToLocation,
    /// FUN_1435e7ff0 → FUN_143173750) first stores world and zone in the client's teleport manager (DAT_145431448
    /// +0x30 / +0x34). Nothing else writes them; TeleportToQueuedLocation and TeleportCancelQueue send them back and clear
    /// them.</item>
    /// <item>Queued: zone packet PacketLoginQueueStatus 0xE0 (reader FUN_14316d6d0, handler FUN_143171ae0 →
    /// FUN_14368afa0 with state 2) fills the queue data source and, the first time, runs GameEvents:OnQueueShow(1), which
    /// opens LoginQueueDialog (LoginQueueDialog.swf: Type 2 shows "Ui.LocationFullQueueMessage", 561206 "This location is
    /// currently full...", the place and the estimate; Cancel → Dialog.lua OnCancelTeleportQueueButtonClicked →
    /// Teleport.CancelQueue; the second button only closes the window). Wire after the opcode, as the login tunnel's
    /// type 3 ([P], auth LoginQueue): u32 place (0-based, shown + 1), u32 members in queue, u32 non-members in queue, u32
    /// member seconds per place, u32 non-member seconds per place, u8 is member, then four u32 the handler doesn't
    /// use.</item>
    /// <item>The player's turn: PacketTeleportPostQueueNotify 0xFC/5 {u32, u32 text, u32, u32 text2} (reader
    /// FUN_14316daa0; handler FUN_1431726c0 takes text2 when non-zero, else text). With no Lua answer to
    /// GameEvents:OnPlayerTeleportPostQueueNotify (none in the shipped UI or ScriptsBase.bin) it looks the text up and
    /// adds a Game Action Queue item GaqItemTeleportToZoneRequest ("GAQTeleportToZone", FUN_1433db960 → FUN_1433db970 →
    /// ctor FUN_1433d8c20; vtable 144767230).</item>
    /// <item>That item's Accept (slot 4, FUN_1433da3b0 → FUN_1433fcec0(1)) sends TeleportToQueuedLocation 0xFC/6 {u32
    /// world, u32 zone} from +0x30 / +0x34 and closes the queue dialog (FUN_14368acd0, OnQueueShow(0)); Decline (slot 3,
    /// FUN_1433dc5b0) sends TeleportCancelQueue 0xFC/7 {same}, which also closes the dialog. Those are the only ways the
    /// client leaves a travel queue: live's queue ended with PostQueueNotify and the player's Accept.</item>
    /// </list>
    /// Ours: the limit counts the players on the landscape plus the places offered and not yet taken; members wait ahead
    /// of everyone without a membership (as the login queue, TASKS 3.12) [I]; an offered place is held for
    /// <see cref="PassLifetime"/> [I]; an Accept after that (or without an offer) is a new request. Only this world's
    /// landscapes are limited: a landscape of another world is the other world's to limit, and it is handed over at once
    /// (L60 note). The notify's text is <see cref="ReadyText"/> [I: live's text id is not in the client].
    /// </summary>
    public static class TravelQueue
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private const byte PostQueueNotifySub = 5, ToQueuedLocationSub = 6, CancelQueueSub = 7;

        /// <summary>A limit set at run time (dev command "landscapecap"), else the configuration's.</summary>
        public static int? Override { get; set; }

        /// <summary>Dev "landscapecap full": every Travel-window arrival waits (single-client tests of the queue).</summary>
        public const int Full = -1;

        /// <summary>Players allowed on one landscape at once (WorldServer "LandscapeCapacity"); 0 = no limit.</summary>
        public static int Capacity => Override ?? ConfigurationManager<WorldServerConfiguration>.Instance.Config?.LandscapeCapacity ?? 0;

        /// <summary>
        /// The notify's text (the Game Action Queue item's message): 565560 "Teleport" [I]. The client's candidates with a
        /// zone tag (62065 "Warp to [*zone*]?", 559442 "Teleport to [*zone*]?") would show the tag as written: the notify
        /// carries no zone to fill it in. Dev "landscapecap text &lt;id&gt;" changes it for tests.
        /// </summary>
        public static uint ReadyText { get; set; } = 565560;

        /// <summary>Seconds per place for the client's estimate [I: live's figure is not known; as the login queue].</summary>
        private const uint SecondsPerPlace = 60;
        private static readonly TimeSpan StatusEvery = TimeSpan.FromSeconds(10);
        /// <summary>How long an offered place is kept for the player's Accept [I].</summary>
        private static readonly TimeSpan PassLifetime = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(1);

        private sealed class Waiting
        {
            public WorldSession Session;
            public uint World;
            public uint Zone;
            public DateTime Since;
            public bool IsMember;
        }

        private sealed record Pass(uint World, uint Zone, DateTime Until);

        private static readonly object sync = new();
        private static readonly List<Waiting> waiting = new();
        private static readonly Dictionary<WorldSession, Pass> passes = new();
        private static DateTime lastStatus = DateTime.MinValue, nextTick = DateTime.MinValue;

        /// <summary>A landscape's line in admission order: members first, then by arrival.</summary>
        private static IEnumerable<Waiting> InOrder(IEnumerable<Waiting> line) => line.OrderByDescending(w => w.IsMember).ThenBy(w => w.Since);

        /// <summary>Players on the landscape plus places offered there and not yet taken (with the lock held).</summary>
        private static int Taken(uint zone, WorldSession except = null)
            => PlayerManager.InZone(zone).Count(p => !p.Disconnected && p != except)
               + passes.Count(p => p.Key != except && p.Value.Zone == zone && p.Value.Until > DateTime.UtcNow);

        /// <summary>
        /// TeleportRequest (0xFC/4) for a landscape of this world: true when the player has to wait (queued, the dialog
        /// opened); false to travel now. A new request replaces the player's earlier wait; a place offered for this
        /// landscape is used up by it.
        /// </summary>
        public static bool Request(WorldSession session, uint world, uint zone)
        {
            int cap = Capacity;
            DateTime now = DateTime.UtcNow;
            bool left, queued = false;
            lock (sync)
            {
                left = waiting.RemoveAll(w => w.Session == session) > 0;
                bool held = passes.Remove(session, out Pass pass) && pass.Zone == zone && pass.Until > now;
                if (!held && cap != 0 && zone != session.ZoneId
                    && (cap == Full || waiting.Any(w => w.Zone == zone) || Taken(zone, session) >= cap))
                {
                    waiting.Add(new Waiting { Session = session, World = world, Zone = zone, Since = now, IsMember = Membership.IsMember(session) });
                    queued = true;
                    log.Info($"{session.CharacterName}: landscape {zone} full ({Taken(zone, session)} / {(cap == Full ? "full" : cap.ToString())}), "
                        + $"queued at {waiting.Count(w => w.Zone == zone)}.");
                }
            }
            if (left || queued)
                SendStatus();
            return queued;
        }

        /// <summary>
        /// TeleportToQueuedLocation 0xFC/6 and TeleportCancelQueue 0xFC/7 {u32 world, u32 zone} (the client's teleport
        /// manager +0x30 / +0x34, i.e. its last TeleportRequest). True when handled.
        /// </summary>
        public static bool Handle(WorldSession session, byte sub, byte[] body)
        {
            if (sub != ToQueuedLocationSub && sub != CancelQueueSub)
                return false;
            uint world = body.Length >= 4 ? BitConverter.ToUInt32(body, 0) : 0;
            uint zone = body.Length >= 8 ? BitConverter.ToUInt32(body, 4) : 0;
            Pass pass;
            bool wasWaiting;
            lock (sync)
            {
                passes.Remove(session, out pass);
                wasWaiting = waiting.RemoveAll(w => w.Session == session) > 0;
            }
            if (wasWaiting)
                SendStatus();
            if (sub == CancelQueueSub)
            {
                log.Info($"{session.CharacterName} left the travel queue{(zone != 0 ? $" for landscape {zone}" : "")}.");
                return true;
            }
            if (pass != null && pass.Zone == zone && pass.Until > DateTime.UtcNow)
            {
                log.Info($"{session.CharacterName} takes the offered place on landscape {zone}.");
                Go(session, world, zone);
            }
            else if (zone != 0)
            {
                // No place held (never offered, or kept too long): the same as asking again.
                if (!Request(session, world, zone))
                    Go(session, world, zone);
            }
            return true;
        }

        private static void Go(WorldSession session, uint world, uint zone)
        {
            if (world != 0 && world != Landscapes.WorldId && Landscapes.OtherWorld(world) is Landscapes.OtherWorldInfo other)
                CrossWorld.Travel(session, other, zone);
            else
                Landscapes.TravelTo(session, zone);
        }

        /// <summary>
        /// Server tick (once a second): drops players who left, expires places not taken, offers the places a landscape
        /// has free to the head of its line (PostQueueNotify), and keeps everyone's place current (on change and every
        /// <see cref="StatusEvery"/>).
        /// </summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextTick)
                return;
            nextTick = now + TickEvery;
            int cap = Capacity;
            var admit = new List<Waiting>();
            bool changed = false;
            lock (sync)
            {
                foreach (WorldSession expired in passes.Where(p => p.Value.Until <= now || p.Key.Disconnected).Select(p => p.Key).ToList())
                    passes.Remove(expired);
                if (waiting.Count == 0)
                    return;
                changed |= waiting.RemoveAll(w => w.Session.Disconnected) > 0;
                foreach (var line in waiting.GroupBy(w => w.Zone).ToList())
                {
                    long room = cap == 0 ? long.MaxValue : cap == Full ? 0 : (long)cap - Taken(line.Key);
                    foreach (Waiting w in InOrder(line))
                    {
                        if (room <= 0)
                            break;
                        // A player still loading another move gets the offer once it is over (one thing at a time).
                        if (w.Session.RezoneTarget != null)
                            continue;
                        room--;
                        admit.Add(w);
                    }
                }
                foreach (Waiting w in admit)
                {
                    waiting.Remove(w);
                    passes[w.Session] = new Pass(w.World, w.Zone, now + PassLifetime);
                    changed = true;
                }
            }
            foreach (Waiting w in admit)
            {
                SendPostQueueNotify(w.Session, w.Zone);
                log.Info($"{w.Session.CharacterName}: a place on landscape {w.Zone} is free; offered.");
            }
            if (changed || now - lastStatus >= StatusEvery)
                SendStatus();
        }

        /// <summary>
        /// PacketTeleportPostQueueNotify 0xFC/5: u32 (unread), u32 text, u32 (unread), u32 text2 (0: the handler takes
        /// text). The client adds its "teleport to zone" Game Action Queue item with that text; Accept sends 0xFC/6.
        /// </summary>
        private static void SendPostQueueNotify(WorldSession session, uint zone)
        {
            // The client shows the item as "Teleport to <text>?" (seen in the 2026-10-17 harness test with 565560:
            // "Teleport to Teleport?"), so the text is the destination: the landscape's own name text, else ReadyText.
            uint text = Landscapes.Get(zone)?.NameText is uint name && name != 0 ? name : ReadyText;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(PostQueueNotifySub);
            w.Write(0u);
            w.Write(text);
            w.Write(0u);
            w.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.TeleportBase, output.ToArray());
        }

        /// <summary>PacketLoginQueueStatus 0xE0 to everyone waiting: their place in their landscape's line.</summary>
        private static void SendStatus()
        {
            List<Waiting> snapshot;
            lock (sync)
                snapshot = waiting.ToList();
            lastStatus = DateTime.UtcNow;
            foreach (var group in snapshot.GroupBy(w => w.Zone))
            {
                List<Waiting> line = InOrder(group).ToList();
                uint members = (uint)line.Count(w => w.IsMember);
                for (int i = 0; i < line.Count; i++)
                {
                    if (line[i].Session.RezoneTarget != null || line[i].Session.Disconnected)
                        continue;
                    var output = new List<byte>();
                    var w = new GamePacketWriter(output);
                    w.Write((uint)i);                       // place, 0-based (members first in the line)
                    w.Write(members);                       // members in queue
                    w.Write((uint)line.Count - members);    // non-members in queue
                    w.Write(SecondsPerPlace);               // member seconds per place
                    w.Write(SecondsPerPlace);               // non-member seconds per place
                    w.Write(line[i].IsMember);              // is member
                    w.Write(0u);
                    w.Write(0u);
                    w.Write(0u);
                    w.Write(0u);
                    line[i].Session.EnqueueMessage(ZoneMessageOpcode.LoginQueueStatus, output.ToArray());
                }
            }
        }

        /// <summary>Dev "landscapecap": the limit, the lines and the places held.</summary>
        public static string Describe()
        {
            lock (sync)
            {
                string lines = string.Join("; ", waiting.GroupBy(w => w.Zone).Select(g =>
                    $"landscape {g.Key}: {string.Join(", ", InOrder(g).Select(w => w.Session.CharacterName))}"));
                string held = string.Join(", ", passes.Select(p => $"{p.Key.CharacterName} → {p.Value.Zone}"));
                int cap = Capacity;
                return $"Landscape capacity {(cap == Full ? "full" : cap == 0 ? "none" : cap.ToString())}; "
                    + $"waiting: {(lines.Length > 0 ? lines : "nobody")}; places held: {(held.Length > 0 ? held : "none")}.";
            }
        }
    }
}
