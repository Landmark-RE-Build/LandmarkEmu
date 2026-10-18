using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The admin-monitor requests and the path-query tool of the 0.695 client (trace/0695/agent-e-leads.md L4 and L145).
    /// All four arrive as Command 0x1E subs on the zone channel (every sender passes channel 0 to its tunnel writer:
    /// FUN_14321ccd0 / FUN_143204600 / FUN_1432040a0 / FUN_14379adb0 called with (gateway, packet, 0, …)) [P].
    /// <para>
    /// <b>Reachability on retail</b> [P]: none of them can be sent by the unmodified client. The three monitor requests
    /// come from the paged admin monitor view that nothing in the retail exe turns on (+0x342A8, OPEN-LEADS L3) or from
    /// the Storybricks debug manager whose sb_* command bodies are unreferenced; the path query comes from the build tool
    /// "PathFindVisualize" (ToolTypes row 15, SHOW_IN_PALETTE 0, KEYBINDING_ID 0, no script activates it). They are
    /// handled anyway, gated by <see cref="Admin.IsAdmin"/> like every admin sub, so a dev-modified client (Frida page hook,
    /// palette entry) gets a sensible server: the three monitor requests are logged and get no answer (their replies have
    /// no reader in 0.695 — sending one would only be dropped), and the path query runs the server's own
    /// <see cref="Pathing"/> and answers in chat (0x4A8 DebugPathQueryResponse is a registry name with no reader, so it is
    /// not sent) [P for "no reader"; the chat answer is ours].
    /// </para>
    /// </summary>
    public static partial class AdminCommands
    {
        public const ushort DebugPathQuerySub = 0x4A7;          // AdminCommandPacketDebugPathQueryRequest (writer FUN_14379adb0)
        public const ushort PathingDataMonitorSub = 0x4A9;      // AdminCommandPacketPathingDataMonitorUpdateRequest (FUN_143204600)
        public const ushort ZonePlayerCountSub = 0x4CC;         // AdminCommandPacketZonePlayerCount (FUN_14321ccd0)
        public const ushort StorybricksDiagnosticsSub = 0x4FC;  // AdminCommandPacketStorybricksRequestDiagnostics (FUN_1432040a0)

        /// <summary>Is this one of the monitor / path-query subs handled here?</summary>
        private static bool IsMonitorSub(ushort sub)
            => sub is DebugPathQuerySub or PathingDataMonitorSub or ZonePlayerCountSub or StorybricksDiagnosticsSub;

        /// <summary>Per (player, sub) the last time a repeating monitor request was logged (they come every 0.2 s / 5 s).</summary>
        private static readonly Dictionary<(ulong, ushort), DateTime> monitorLogged = new();

        private static void HandleMonitor(WorldSession session, ushort sub, byte[] body)
        {
            switch (sub)
            {
                case PathingDataMonitorSub:
                    // Header only, sent every 0.2 s by the monitor's page-32 update FUN_14321c770 [P]. The reply
                    // PathingDataMonitorUpdateResponse 0x4AA has no class / reader in 0.695 (architecture-leads.md L4):
                    // nothing is sent. The same numbers are on the server's /admin/monitor page (AdminMonitor, Pathing.Monitor).
                    LogMonitor(session, sub, "PathingDataMonitorUpdateRequest (page 32; no reply has a reader)", body);
                    break;
                case ZonePlayerCountSub:
                    // u32 0 at +0x20 (builder FUN_1432262a0, only from the monitor draw FUN_14321e590 behind +0x342A8) [P].
                    // Its reply would fill monitor +0x34288 ("Zone Pop: %i" / "Pop:%4d"), which nothing in the exe writes:
                    // no reader, so no reply [P]. The implied body would be a u32 count [I].
                    LogMonitor(session, sub, $"ZonePlayerCount ({PlayerManager.Snapshot().Count} online here; no reply has a reader)", body);
                    break;
                case StorybricksDiagnosticsSub:
                {
                    // u64 admin target guid (+0x20), u8 flag (+0x28): 0 from monitor page 30/31 (FUN_14321c900), 1 from
                    // ClientStorybricksDebugManager's 5 s update FUN_14352d160 [P]. Its consumer FUN_14352d770 →
                    // FUN_14352f060 ("DebugData_AI_SelectedCharacterInfo") is unreferenced, so no reply is read [P].
                    var reader = new GamePacketReader(body);
                    ulong target = body.Length >= 8 ? reader.ReadULong() : 0;
                    byte flag = body.Length >= 9 ? reader.ReadByte() : (byte)0;
                    LogMonitor(session, sub, $"StorybricksRequestDiagnostics target 0x{target:X} flag {flag} (no reply has a reader)", body);
                    break;
                }
                case DebugPathQuerySub:
                    DebugPathQuery(session, body);
                    break;
            }
        }

        /// <summary>Logs a monitor request: the first one per player and sub, then at most once a minute.</summary>
        private static void LogMonitor(WorldSession session, ushort sub, string what, byte[] body)
        {
            DateTime now = DateTime.UtcNow;
            lock (monitorLogged)
            {
                if (monitorLogged.TryGetValue((session.PlayerGuid, sub), out DateTime last) && now - last < TimeSpan.FromMinutes(1))
                    return;
                monitorLogged[(session.PlayerGuid, sub)] = now;
            }
            log.Info($"Admin {session.CharacterName} admin monitor 0x{sub:X}: {what} [{BitConverter.ToString(body)}].");
        }

        // ------------------------------------------------------------------------------------------------------------
        // DebugPathQueryRequest 0x4A7

        /// <summary>A path query from the PathFindVisualize tool waiting for the pathing thread.</summary>
        private sealed class PendingPathQuery
        {
            public WorldSession Admin;
            public Pathing.PathRequest Request;
            public Pathing.LineResult Line;
            public byte Flag;
        }

        private static readonly List<PendingPathQuery> pendingPathQueries = new();

        /// <summary>How long a path query may wait for the pathing thread before it is reported as unanswered.</summary>
        private static readonly TimeSpan PathQueryTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// DebugPathQueryRequest 0x1E / 0x4A7 — the "PathFindVisualize" build tool's cursor VoxelPathFindVisualizeCursor
        /// (vtable 0x1447e9400; click handler FUN_14379b600): the first click stores the start (+0x230 ← cursor point
        /// +0x1B0), the second the end (+0x240) and sends, writer FUN_14379adb0 [P]: 4 × f32 start (+0x20), 4 × f32 end
        /// (+0x30), u8 flag (+0x40 ← cursor +0x268, toggled by FUN_14379b790; its meaning is not found yet). The
        /// server runs its own <see cref="Pathing"/> between the two points on the admin's landscape — first the straight
        /// walk (<see cref="Pathing.Line"/>), then an A* query (<see cref="Pathing.RequestPath"/>) on the background
        /// thread — and reports the result in chat when it is done (<see cref="Update"/>): straight-line verdict, reached /
        /// partial / none, waypoints, walked length, nodes expanded. The grid kind follows the dev command "pathing"
        /// (<see cref="Creatures.DevPathing"/>): cave on a landscape with caverns when the start is more than 40 m below
        /// the generated ground, else surface. Live's reply 0x4A8 has no reader in 0.695, so it is not sent.
        /// </summary>
        public static void DebugPathQuery(WorldSession session, byte[] body)
        {
            if (body.Length < 33)
            {
                log.Info($"Admin {session.CharacterName} DebugPathQuery too short ({body.Length} bytes).");
                return;
            }
            var reader = new GamePacketReader(body);
            var start = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            reader.ReadSingle();                                   // w of the vec4 (the cursor point's 4th float)
            var end = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            reader.ReadSingle();
            byte flag = reader.ReadByte();
            if (!IsFinite(start) || !IsFinite(end))
            {
                Chat.System(session, "Path query: the points are not numbers.");
                return;
            }
            uint zone = session.ZoneId;
            Pathing.Mode mode = Caves.HasCaverns(zone) && TerrainHeights.DepthBelowGround(start, zone) > 40f
                ? Pathing.Mode.Cave : Pathing.Mode.Surface;
            Pathing.LineResult line = Pathing.Line(zone, mode, start, end);
            Pathing.PathRequest request = Pathing.RequestPath(zone, mode, start, end);
            log.Info($"Admin {session.CharacterName} DebugPathQuery {mode} on {zone} from {start} to {end} flag {flag}: line {line}.");
            if (request == null)
            {
                Chat.System(session, $"Path query ({mode}): straight line {line}; the pathing queue is full, try again.");
                return;
            }
            lock (pendingPathQueries)
                pendingPathQueries.Add(new PendingPathQuery { Admin = session, Request = request, Line = line, Flag = flag });
        }

        private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        /// <summary>World tick: reports finished (or timed-out) path queries to their admins.</summary>
        public static void Update()
        {
            List<PendingPathQuery> finished = null;
            lock (pendingPathQueries)
            {
                if (pendingPathQueries.Count == 0)
                    return;
                for (int i = pendingPathQueries.Count - 1; i >= 0; i--)
                {
                    PendingPathQuery query = pendingPathQueries[i];
                    if (query.Request.Done || query.Admin.Disconnected || DateTime.UtcNow - query.Request.Created > PathQueryTimeout)
                    {
                        (finished ??= new()).Add(query);
                        pendingPathQueries.RemoveAt(i);
                    }
                }
            }
            if (finished == null)
                return;
            foreach (PendingPathQuery query in finished)
            {
                if (query.Admin.Disconnected)
                    continue;
                Pathing.PathRequest r = query.Request;
                string verdict;
                if (!r.Done)
                    verdict = "no answer from the pathing thread within 10 s";
                else if (r.Points == null || r.Points.Count == 0)
                    verdict = $"no way found ({r.Nodes} nodes expanded)";
                else
                {
                    float length = 0f;
                    Vector3 at = r.From;
                    foreach (Vector3 p in r.Points)
                    {
                        length += Vector3.Distance(at, p);
                        at = p;
                    }
                    Vector3 last = r.Points[^1];
                    verdict = $"{(r.Reached ? "reached" : "partial (nearest point reached)")}: {r.Points.Count} waypoints, "
                        + $"{length:0.0} m walked (straight {Vector3.Distance(r.From, r.To):0.0} m), {r.Nodes} nodes, "
                        + $"ends at ({last.X:0.#}, {last.Y:0.#}, {last.Z:0.#})";
                }
                Chat.System(query.Admin, $"Path query ({r.Mode}, landscape {r.Zone}): straight line {query.Line}; path {verdict}"
                    + $"{(Pathing.Enabled ? "" : " (creature pathing is off)")}.");
                log.Info($"Admin {query.Admin.CharacterName} DebugPathQuery result: line {query.Line}; {verdict}.");
            }
        }
    }
}
