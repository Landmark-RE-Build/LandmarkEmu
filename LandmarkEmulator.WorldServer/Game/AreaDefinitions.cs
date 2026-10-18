using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Database;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Trigger volumes ("areas", family 0xEA AreaDefinition) made with the Area Tool (ToolTypes row 4 "TriggerVolume",
    /// client class ToolTriggerVolume) — the boxes of Linking Areas (TASKS 2.33). Full trace with proof marks:
    /// trace/0695/area-definitions-695.md.
    /// <para>
    /// How the parts fit (0.695 client):
    /// <list type="bullet">
    /// <item>The tool sends CreateTriggerAreaRequest (sub 7) with the box the player drew and a pending id, and places
    /// nothing itself (FUN_143739100). The server makes the area and a Linking Area prop (1590 — the server-side setting
    /// <c>GameScripting.PropNpcDefinitionId</c>, which the client never reads) at the box centre, scaled to the box size,
    /// whose spawn carries the area guid (<see cref="PlayerUpdateAddLightweightProp.AreaGuid"/>), and answers
    /// CreateTriggerAreaResponse (sub 8). Links use the prop guid.</item>
    /// <item>The tool edits a box by moving / scaling that prop and saving it with an ordinary PropSave (FUN_143739440);
    /// <see cref="PropSaved"/> makes the area follow.</item>
    /// <item>The old area window's natives (Lua table <c>Area</c>: SetName, AddProperty, Rotate, Group, Ungroup, Remove)
    /// send UpdateDefinition (3), TransferShape (2) and RemoveDefinition (4). Each sets a client "busy" flag that only
    /// RequestResponse (6) clears, so every one of them is answered.</item>
    /// <item>The client keeps the landscape's areas from SetDefinitions (1, sent at login and every re-zone, which clears
    /// its list first) and the UpdateDefinition / RemoveDefinition the server sends when they change.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Story Tools ask <see cref="BoxesOf"/> for a Linking Area's real shape ("in this area"); a Linking Area without an
    /// area (placed before this, or copied in a design) keeps the old radius.
    /// </para>
    /// <para>
    /// Bandwidth: one AreaDefinition with one box and no properties is 135 bytes; the landscape's list goes once per
    /// login / re-zone and single records only when an area changes. Areas carry no property: the tool's own local copy
    /// has a Blank (19) one, which does nothing in the client (no ClientAreaProperty for it), so it is left out.
    /// </para>
    /// </summary>
    public static class AreaDefinitions
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>RequestResponse error 1: "...the desired DataSet could not be found on the zone!" (sent for an unknown area).</summary>
        private const uint ErrorDataSetNotFound = 1;
        /// <summary>RequestResponse error 2: "We have failed to save your trigger volume due to invalid values!".</summary>
        private const uint ErrorInvalidValues = 2;
        /// <summary>RequestResponse error without a client text (FUN_1432f9c10 shows nothing for other codes).</summary>
        private const uint ErrorSilent = 0;

        /// <summary>
        /// Longest box side accepted, in metres. A build site is at most a few dozen metres across; a larger box is a
        /// broken or forged request (answered with <see cref="ErrorInvalidValues"/>).
        /// </summary>
        private const double MaxSide = 256;
        /// <summary>Smallest box side kept when a prop is scaled almost flat (keeps the box valid for the client).</summary>
        private const float MinSide = 0.05f;

        /// <summary>One area: its record as the client reads it, where it is, who made it and the prop that stands for it.</summary>
        private sealed class Area
        {
            public AreaDefinitionRecord Record;
            public uint Zone;
            public ulong Owner;
            /// <summary>The Linking Area prop carrying this area's guid (0 = none).</summary>
            public ulong PropGuid;
            public ulong Guid => Record.Guid;
        }

        /// <summary>
        /// A shape ready for "is this point inside" tests: centre, half sizes and the inverse of its rotation
        /// (landscape-local metres, the frame of player and prop positions).
        /// </summary>
        public readonly struct Box
        {
            public Vector3 Centre { get; init; }
            public Vector3 Half { get; init; }
            /// <summary>Turns a world offset into the box's own axes.</summary>
            public Quaternion Inverse { get; init; }
            /// <summary>A sphere shape (type 1): the sphere inscribed in the box.</summary>
            public bool Sphere { get; init; }

            /// <summary>Inside or on the surface.</summary>
            public bool Contains(Vector3 at)
            {
                Vector3 local = Vector3.Transform(at - Centre, Inverse);
                if (Sphere)
                {
                    float r = MathF.Min(Half.X, MathF.Min(Half.Y, Half.Z));
                    return local.LengthSquared() <= r * r;
                }
                return MathF.Abs(local.X) <= Half.X && MathF.Abs(local.Y) <= Half.Y && MathF.Abs(local.Z) <= Half.Z;
            }
        }

        private static readonly object sync = new();
        private static readonly Dictionary<ulong, Area> areas = new();
        /// <summary>Prop guid → its area's shapes as boxes; read lock-free by Story Tools every tick.</summary>
        private static readonly ConcurrentDictionary<ulong, Box[]> boxesByProp = new();
        /// <summary>Area and shape guids share one counter (any non-zero value; 0 means "none" to the client).</summary>
        private static long nextGuid;
        private static bool loaded;

        #region loading and queries

        /// <summary>
        /// Reads the stored areas (called by <see cref="Props.LoadWorld"/> once the props are in memory) and gives each
        /// Linking Area prop its area guid and box size back: the prop table keeps a single scale.
        /// </summary>
        public static void Load()
        {
            List<WorldAreaModel> rows;
            try
            {
                using var context = DatabaseManager.Instance.CharacterDatabase.NewWorldContext();
                rows = context.WorldArea.ToList();
            }
            catch (Exception e)
            {
                log.Error(e, "Area: could not read the stored areas.");
                rows = new();
            }
            int linked = 0;
            lock (sync)
            {
                areas.Clear();
                boxesByProp.Clear();
                foreach (WorldAreaModel row in rows)
                {
                    AreaDefinitionRecord record;
                    try
                    {
                        record = AreaDefinitionRecord.FromBytes(row.Data);
                    }
                    catch (Exception e)
                    {
                        log.Warn(e, $"Area: stored area 0x{row.Id:X} is unreadable, skipped.");
                        continue;
                    }
                    record.Guid = row.Id;
                    var area = new Area { Record = record, Zone = row.ZoneId, Owner = row.OwnerId, PropGuid = row.PropId };
                    areas[area.Guid] = area;
                    Bump(area.Guid);
                    foreach (AreaShapeRecord s in record.Shapes)
                        Bump(s.Guid);
                    if (area.PropGuid != 0 && Props.Find(area.PropGuid) is Props.PlacedProp prop)
                    {
                        prop.Spawn.AreaGuid = area.Guid;
                        if (record.Shapes.Count > 0)
                        {
                            AreaShapeRecord s = record.Shapes[0];
                            prop.Spawn.Scale = new Vector4((float)(s.MaxX - s.MinX), (float)(s.MaxY - s.MinY), (float)(s.MaxZ - s.MinZ), 1f);
                        }
                        linked++;
                    }
                    else if (area.PropGuid != 0)
                    {
                        log.Warn($"Area 0x{area.Guid:X}: its Linking Area prop 0x{area.PropGuid:X} is gone; kept without a prop.");
                        area.PropGuid = 0;
                    }
                    Index(area);
                }
                loaded = true;
            }
            log.Info($"Loaded {areas.Count} area definitions ({linked} with a Linking Area prop).");
        }

        private static void EnsureLoaded()
        {
            if (!loaded)
                Load();
        }

        private static void Bump(ulong guid)
        {
            if ((long)guid > Interlocked.Read(ref nextGuid))
                Interlocked.Exchange(ref nextGuid, (long)guid);
        }

        private static ulong NewGuid() => (ulong)Interlocked.Increment(ref nextGuid);

        /// <summary>
        /// The shapes of the area a Linking Area prop stands for, or null when the prop has no area (Story Tools then use
        /// the old radius). Lock-free.
        /// </summary>
        public static Box[] BoxesOf(ulong propGuid) => boxesByProp.TryGetValue(propGuid, out Box[] boxes) ? boxes : null;

        /// <summary>Refreshes <see cref="boxesByProp"/> for an area (call under <see cref="sync"/>).</summary>
        private static void Index(Area area)
        {
            if (area.PropGuid == 0)
                return;
            boxesByProp[area.PropGuid] = area.Record.Shapes.Select(ToBox).ToArray();
        }

        private static Box ToBox(AreaShapeRecord s) => new()
        {
            Centre = new Vector3((float)((s.MinX + s.MaxX) / 2), (float)((s.MinY + s.MaxY) / 2), (float)((s.MinZ + s.MaxZ) / 2)),
            Half = new Vector3((float)((s.MaxX - s.MinX) / 2), (float)((s.MaxY - s.MinY) / 2), (float)((s.MaxZ - s.MinZ) / 2)),
            Inverse = Quaternion.Conjugate(Rotation(s)),
            Sphere = s.Type == AreaShapeRecord.TypeSphere
        };

        /// <summary>
        /// A shape's rotation as a quaternion. The tool's Euler split (FUN_142e550d0: yaw = atan2(r2.x, r2.z), pitch =
        /// asin(−r2.y), roll = atan2(r0.y, r1.y)) is exactly the inverse of System.Numerics' CreateFromYawPitchRoll.
        /// </summary>
        private static Quaternion Rotation(AreaShapeRecord s)
            => s.Pitch == 0 && s.Yaw == 0 && s.Roll == 0 ? Quaternion.Identity : Quaternion.CreateFromYawPitchRoll(s.Yaw, s.Pitch, s.Roll);

        /// <summary>A prop rotation (quaternion x, y, z, w) split the way the tool fills a shape's +0x50.</summary>
        private static (float Pitch, float Yaw, float Roll) Euler(Vector4 rotation)
        {
            var q = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
            if (q.LengthSquared() < 1e-6f)
                return (0, 0, 0);
            Matrix4x4 m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q));
            return (MathF.Asin(Math.Clamp(-m.M32, -1f, 1f)), MathF.Atan2(m.M31, m.M33), MathF.Atan2(m.M12, m.M22));
        }

        #endregion

        #region sending

        /// <summary>
        /// SetDefinitions (sub 1) with every area of the player's landscape, at login and after every re-zone (the client
        /// clears its list on it, FUN_143d5ff60, and zoning clears the "area definitions loaded" flag the loading screen
        /// waits on).
        /// </summary>
        public static void SendAll(WorldSession session)
        {
            EnsureLoaded();
            List<AreaDefinitionRecord> records;
            lock (sync)
                records = areas.Values.Where(a => a.Zone == session.ZoneId).Select(a => a.Record).ToList();
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            AreaDefinitionHeader.Write(w);
            w.Write((uint)records.Count);
            foreach (AreaDefinitionRecord r in records)
                r.Write(w);
            session.EnqueueMessage(ZoneMessageOpcode.AreaDefinitionSetDefinitions, output.ToArray());
        }

        /// <summary>UpdateDefinition (sub 3) body: the area and "apply" = true (FUN_143d60310 ignores it otherwise).</summary>
        private static byte[] UpdateBody(AreaDefinitionRecord record)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            AreaDefinitionHeader.Write(w);
            record.Write(w);
            w.Write(true);
            return output.ToArray();
        }

        /// <summary>RemoveDefinition (sub 4) body: an otherwise empty record with the area guid (FUN_143d5ff40 reads only that).</summary>
        private static byte[] RemoveBody(ulong guid)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            AreaDefinitionHeader.Write(w);
            new AreaDefinitionRecord { Guid = guid }.Write(w);
            return output.ToArray();
        }

        /// <summary>Sends a message to the players on a landscape.</summary>
        private static void ToZone(uint zone, ZoneMessageOpcode opcode, byte[] body)
        {
            foreach (WorldSession s in PlayerManager.InZone(zone))
                s.EnqueueMessage(opcode, body);
        }

        /// <summary>
        /// RequestResponse (sub 6; reader FUN_1432f8e60): u64 area guid, bool success, u32 error. Clears the client's busy
        /// flag; on failure error 1 / 2 show their texts, others nothing.
        /// </summary>
        private static void Respond(WorldSession session, ulong areaGuid, bool success, uint error = 0)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            AreaDefinitionHeader.Write(w);
            w.Write(areaGuid);
            w.Write(success);
            w.Write(success ? 0u : error);
            session.EnqueueMessage(ZoneMessageOpcode.AreaDefinitionRequestResponse, output.ToArray());
        }

        /// <summary>
        /// CreateTriggerAreaResponse (sub 8; reader FUN_1432f78d0, handler FUN_1431b0a70 → the tool's callback): bool
        /// success, u32 the client's pending request id, u64 the new area guid.
        /// </summary>
        private static void RespondCreate(WorldSession session, uint requestId, bool success, ulong areaGuid)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            AreaDefinitionHeader.Write(w);
            w.Write(success);
            w.Write(requestId);
            w.Write(success ? areaGuid : 0ul);
            session.EnqueueMessage(ZoneMessageOpcode.AreaDefinitionCreateTriggerAreaResponse, output.ToArray());
        }

        #endregion

        #region persistence

        private static void Persist(Area area)
        {
            var model = new WorldAreaModel
            {
                Id = area.Guid,
                ZoneId = area.Zone,
                OwnerId = area.Owner,
                PropId = area.PropGuid,
                Data = area.Record.ToBytes()
            };
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                if (c.WorldArea.Any(a => a.Id == model.Id))
                    c.WorldArea.Update(model);
                else
                    c.WorldArea.Add(model);
            }).ContinueWith(t => log.Error(t.Exception, $"Area: failed to save area 0x{model.Id:X}."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        private static void Unpersist(ulong guid)
        {
            DatabaseManager.Instance.CharacterDatabase.SaveWorld(c =>
            {
                WorldAreaModel stored = c.WorldArea.FirstOrDefault(a => a.Id == guid);
                if (stored != null)
                    c.WorldArea.Remove(stored);
            }).ContinueWith(t => log.Error(t.Exception, $"Area: failed to delete area 0x{guid:X}."),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        #endregion

        #region permission

        /// <summary>The centre of an area's first shape (landscape-local), or null for an area without shapes.</summary>
        private static Vector3? CentreOf(AreaDefinitionRecord record)
        {
            if (record.Shapes.Count == 0)
                return null;
            AreaShapeRecord s = record.Shapes[0];
            return new Vector3((float)((s.MinX + s.MaxX) / 2), (float)((s.MinY + s.MaxY) / 2), (float)((s.MinZ + s.MaxZ) / 2));
        }

        /// <summary>
        /// May the player make or change an area at a landscape-local point? As for placing a prop: the point is on a
        /// build site, the player has build access there, and the site is not in Play Mode. A refusal shows
        /// "Construction.InsufficentPermissions" (561237, "You do not have permission to build that here.").
        /// </summary>
        private static bool MayBuildAt(WorldSession session, Vector3 local, uint zone, bool tell = true)
        {
            ulong? site = Claims.ClaimAt(zone, local);
            var global = Terrain.TerrainEdits.ToGlobal(local);
            bool ok = site is ulong id && !Claims.IsPlaying(id) && Claims.MayEdit(session.PlayerGuid, global.X, global.Y, global.Z, zone);
            if (!ok && tell)
                SystemMessages.ShowKey(session, "Construction.InsufficentPermissions");
            return ok;
        }

        /// <summary>May the player change this area (its owner, or a builder where it stands)?</summary>
        private static bool MayChange(WorldSession session, Area area)
        {
            if (area.Zone != session.ZoneId)
                return false;
            Vector3? centre = CentreOf(area.Record);
            if (centre == null)
                return area.Owner == session.PlayerGuid;
            return MayBuildAt(session, centre.Value, area.Zone);
        }

        #endregion

        #region client requests

        /// <summary>
        /// CreateTriggerAreaRequest (sub 7): the Area Tool's new box. Makes the area (one box shape, the drawn corners, a
        /// new shape guid), shows it to the landscape, places its Linking Area prop and answers the tool.
        /// </summary>
        public static void Create(WorldSession session, AreaDefinitionCreateTriggerAreaRequest request)
        {
            EnsureLoaded();
            AreaShapeRecord shape = request.Shape.Clone();
            shape.Frame = 0;
            if (shape.Type != AreaShapeRecord.TypeBox || !shape.IsValid(MaxSide))
            {
                log.Info($"Area: {session.CharacterName} asked for an invalid box ({Describe(shape)}).");
                RespondCreate(session, request.RequestId, false, 0);
                return;
            }
            Vector3 centre = new((float)((shape.MinX + shape.MaxX) / 2), (float)((shape.MinY + shape.MaxY) / 2), (float)((shape.MinZ + shape.MaxZ) / 2));
            if (!MayBuildAt(session, centre, session.ZoneId))
            {
                RespondCreate(session, request.RequestId, false, 0);
                return;
            }

            shape.Guid = NewGuid();
            var area = new Area
            {
                Record = new AreaDefinitionRecord { Guid = NewGuid(), Shapes = { shape } },
                Zone = session.ZoneId,
                Owner = session.PlayerGuid
            };
            lock (sync)
                areas[area.Guid] = area;
            // The area first, so the client knows it when the prop and the tool's answer arrive.
            ToZone(area.Zone, ZoneMessageOpcode.AreaDefinitionUpdateDefinition, UpdateBody(area.Record));

            var size = new Vector3((float)(shape.MaxX - shape.MinX), (float)(shape.MaxY - shape.MinY), (float)(shape.MaxZ - shape.MinZ));
            Quaternion turn = Rotation(shape);
            Props.PlacedProp prop = Props.PlaceLinkingArea(session, centre, new Vector4(turn.X, turn.Y, turn.Z, turn.W), size, area.Guid);
            lock (sync)
            {
                area.PropGuid = prop?.Spawn.Guid ?? 0;
                Index(area);
            }
            Persist(area);
            RespondCreate(session, request.RequestId, true, area.Guid);
            log.Info($"Area: {session.CharacterName} made area 0x{area.Guid:X} ({Describe(shape)}), Linking Area prop "
                + (prop != null ? $"0x{prop.Spawn.Guid:X}" : "none (no model for prop 1590)") + ".");
        }

        /// <summary>
        /// UpdateDefinition (sub 3): the old area window changed an area (name, properties, a shape's rotation, a removed
        /// shape). Stored as sent (the guid stays the server's), shown to the landscape; a Linking Area prop follows its
        /// first shape.
        /// </summary>
        public static void Update(WorldSession session, AreaDefinitionUpdateRequest request)
        {
            EnsureLoaded();
            AreaDefinitionRecord incoming = request.Area;
            Area area;
            lock (sync)
                areas.TryGetValue(incoming.Guid, out area);
            if (area == null)
            {
                Respond(session, incoming.Guid, false, ErrorDataSetNotFound);
                return;
            }
            if (!MayChange(session, area))
            {
                Respond(session, area.Guid, false, ErrorSilent);
                return;
            }
            if (incoming.Shapes.Count == 0 || incoming.Shapes.Any(s => !s.IsValid(MaxSide)))
            {
                Respond(session, area.Guid, false, ErrorInvalidValues);
                return;
            }
            var seen = new HashSet<ulong>();
            foreach (AreaShapeRecord s in incoming.Shapes)
            {
                s.Frame = 0;
                if (s.Guid == 0 || !seen.Add(s.Guid))
                    s.Guid = NewGuid();
                seen.Add(s.Guid);
            }
            // Every shape must still be somewhere the player may build. Only the first one's centre was checked, so an
            // update carrying more shapes (the record holds up to MaxShapes) put trigger volumes over any other player's
            // site, where their Links fired on that site's visitors (bughunt-build B5). A shape's side is at most MaxSide
            // (256 m) and sites stand 390 m apart centre to centre (Claims.BufferFromCentre), so a shape centred on a
            // site the player may build on cannot reach another one.
            foreach (AreaShapeRecord s in incoming.Shapes)
            {
                var centre = new Vector3((float)((s.MinX + s.MaxX) / 2), (float)((s.MinY + s.MaxY) / 2), (float)((s.MinZ + s.MaxZ) / 2));
                if (!MayBuildAt(session, centre, area.Zone))
                {
                    Respond(session, area.Guid, false, ErrorSilent);
                    return;
                }
            }
            Props.PlacedProp prop = null;
            lock (sync)
            {
                bool firstChanged = !SameShape(area.Record.Shapes.FirstOrDefault(), incoming.Shapes[0]);
                area.Record = incoming;
                if (firstChanged && area.PropGuid != 0)
                    prop = Props.Find(area.PropGuid);
                Index(area);
            }
            if (prop != null)
            {
                AreaShapeRecord s = incoming.Shapes[0];
                Quaternion turn = Rotation(s);
                prop.Spawn.Position = new Vector4((float)((s.MinX + s.MaxX) / 2), (float)((s.MinY + s.MaxY) / 2), (float)((s.MinZ + s.MaxZ) / 2), 1f);
                prop.Spawn.Rotation = new Vector4(turn.X, turn.Y, turn.Z, turn.W);
                prop.Spawn.Scale = new Vector4((float)(s.MaxX - s.MinX), (float)(s.MaxY - s.MinY), (float)(s.MaxZ - s.MinZ), 1f);
                Props.RespawnLinkingArea(prop);
            }
            Persist(area);
            ToZone(area.Zone, ZoneMessageOpcode.AreaDefinitionUpdateDefinition, UpdateBody(area.Record));
            Respond(session, area.Guid, true);
            log.Info($"Area: {session.CharacterName} changed area 0x{area.Guid:X} (\"{area.Record.Name}\", "
                + $"{area.Record.Shapes.Count} shape(s), {area.Record.Properties.Count} propert{(area.Record.Properties.Count == 1 ? "y" : "ies")}).");
        }

        /// <summary>
        /// RemoveDefinition (sub 4): the area's last shape was removed. The area goes, and with it its Linking Area prop
        /// (which stands for nothing any more).
        /// </summary>
        public static void Remove(WorldSession session, AreaDefinitionRemoveRequest request)
        {
            EnsureLoaded();
            ulong guid = request.Area.Guid;
            Area area;
            lock (sync)
                areas.TryGetValue(guid, out area);
            if (area == null)
            {
                Respond(session, guid, false, ErrorDataSetNotFound);
                return;
            }
            if (!MayChange(session, area))
            {
                Respond(session, guid, false, ErrorSilent);
                return;
            }
            Forget(area);
            Respond(session, guid, true);
            if (area.PropGuid != 0)
                Props.Despawn(area.PropGuid);
            log.Info($"Area: {session.CharacterName} removed area 0x{guid:X}.");
        }

        /// <summary>
        /// TransferShape (sub 2, Group / Ungroup): moves one shape from area B to area A. B goes when it has no shape
        /// left (its Linking Area prop with it). Both are shown to the landscape as they now are.
        /// </summary>
        public static void TransferShape(WorldSession session, AreaDefinitionTransferShapeRequest request)
        {
            EnsureLoaded();
            Area to, from;
            lock (sync)
            {
                areas.TryGetValue(request.To.Guid, out to);
                areas.TryGetValue(request.From.Guid, out from);
            }
            if (to == null || from == null || to == from)
            {
                Respond(session, request.To.Guid, false, ErrorDataSetNotFound);
                return;
            }
            if (to.Zone != from.Zone || !MayChange(session, to) || !MayChange(session, from))
            {
                Respond(session, to.Guid, false, ErrorSilent);
                return;
            }
            bool emptied;
            lock (sync)
            {
                AreaShapeRecord shape = from.Record.Shapes.FirstOrDefault(s => s.Guid == request.ShapeGuid);
                if (shape == null || to.Record.Shapes.Count >= AreaDefinitionRecord.MaxShapes)
                    shape = null;
                else
                {
                    from.Record.Shapes.Remove(shape);
                    to.Record.Shapes.Add(shape);
                    Index(to);
                    Index(from);
                }
                if (shape == null)
                {
                    Respond(session, to.Guid, false, ErrorInvalidValues);
                    return;
                }
                emptied = from.Record.Shapes.Count == 0;
            }
            Persist(to);
            ToZone(to.Zone, ZoneMessageOpcode.AreaDefinitionUpdateDefinition, UpdateBody(to.Record));
            if (emptied)
            {
                Forget(from);
                if (from.PropGuid != 0)
                    Props.Despawn(from.PropGuid);
            }
            else
            {
                Persist(from);
                ToZone(from.Zone, ZoneMessageOpcode.AreaDefinitionUpdateDefinition, UpdateBody(from.Record));
            }
            Respond(session, to.Guid, true);
            log.Info($"Area: {session.CharacterName} moved shape 0x{request.ShapeGuid:X} from area 0x{from.Guid:X} to 0x{to.Guid:X}.");
        }

        #endregion

        #region prop hooks

        /// <summary>
        /// A Linking Area prop was saved somewhere new (the Area Tool's edit, or an ordinary prop move): its area's first
        /// shape becomes the prop's box — centre = position, size = scale, rotation = the prop's.
        /// </summary>
        public static void PropSaved(Props.PlacedProp prop)
        {
            EnsureLoaded();
            ulong propGuid = prop.Spawn.Guid;
            Area area;
            lock (sync)
                area = areas.Values.FirstOrDefault(a => a.PropGuid == propGuid);
            if (area == null)
                return;
            Vector4 p = prop.Spawn.Position, scale = prop.Spawn.Scale;
            float sx = MathF.Max(MinSide, MathF.Abs(scale.X)), sy = MathF.Max(MinSide, MathF.Abs(scale.Y)), sz = MathF.Max(MinSide, MathF.Abs(scale.Z));
            (float pitch, float yaw, float roll) = Euler(prop.Spawn.Rotation);
            byte[] body;
            lock (sync)
            {
                if (area.Record.Shapes.Count == 0)
                    area.Record.Shapes.Add(new AreaShapeRecord { Guid = NewGuid() });
                AreaShapeRecord s = area.Record.Shapes[0];
                var updated = s.Clone();
                updated.Type = AreaShapeRecord.TypeBox;
                updated.Frame = 0;
                updated.MinX = p.X - sx / 2; updated.MaxX = p.X + sx / 2;
                updated.MinY = p.Y - sy / 2; updated.MaxY = p.Y + sy / 2;
                updated.MinZ = p.Z - sz / 2; updated.MaxZ = p.Z + sz / 2;
                updated.Pitch = pitch; updated.Yaw = yaw; updated.Roll = roll;
                if (SameShape(s, updated))
                    return;
                area.Record.Shapes[0] = updated;
                Index(area);
                body = UpdateBody(area.Record);
            }
            Persist(area);
            ToZone(area.Zone, ZoneMessageOpcode.AreaDefinitionUpdateDefinition, body);
            log.Debug($"Area 0x{area.Guid:X} follows its prop 0x{propGuid:X}: {Describe(area.Record.Shapes[0])}.");
        }

        /// <summary>A Linking Area prop was picked up or deleted: its area goes too (RemoveDefinition to the landscape).</summary>
        public static void PropRemoved(ulong propGuid)
        {
            EnsureLoaded();
            Area area;
            lock (sync)
                area = areas.Values.FirstOrDefault(a => a.PropGuid == propGuid);
            if (area == null)
                return;
            area.PropGuid = 0;
            boxesByProp.TryRemove(propGuid, out _);
            Forget(area);
            log.Info($"Area 0x{area.Guid:X} removed with its Linking Area prop 0x{propGuid:X}.");
        }

        /// <summary>Drops an area everywhere: memory, Story Tools' boxes, the database and the landscape's clients.</summary>
        private static void Forget(Area area)
        {
            lock (sync)
            {
                if (!areas.Remove(area.Guid))
                    return;
                if (area.PropGuid != 0)
                    boxesByProp.TryRemove(area.PropGuid, out _);
            }
            Unpersist(area.Guid);
            ToZone(area.Zone, ZoneMessageOpcode.AreaDefinitionRemoveDefinition, RemoveBody(area.Guid));
        }

        #endregion

        private static bool SameShape(AreaShapeRecord a, AreaShapeRecord b)
            => a != null && b != null && a.Type == b.Type
               && Near(a.MinX, b.MinX) && Near(a.MinY, b.MinY) && Near(a.MinZ, b.MinZ)
               && Near(a.MaxX, b.MaxX) && Near(a.MaxY, b.MaxY) && Near(a.MaxZ, b.MaxZ)
               && Near(a.Pitch, b.Pitch) && Near(a.Yaw, b.Yaw) && Near(a.Roll, b.Roll);

        private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-3;

        private static string Describe(AreaShapeRecord s)
            => $"type {s.Type}, min ({s.MinX:0.##}, {s.MinY:0.##}, {s.MinZ:0.##}), max ({s.MaxX:0.##}, {s.MaxY:0.##}, {s.MaxZ:0.##}), "
               + $"rotation ({s.Pitch:0.###}, {s.Yaw:0.###}, {s.Roll:0.###})";
    }
}
