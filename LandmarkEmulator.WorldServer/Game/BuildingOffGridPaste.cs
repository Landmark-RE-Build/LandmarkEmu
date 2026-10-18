using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The off-grid paste: VoxelConstruction 0xF4 sub 5 <b>OffGridPasteClientRequest</b> (registered "cVoxelConstructionPacketId
    /// OffGridPasteClientRequest" 0x0500F400 next to OffGridPasteZoneRequest 0x1300F400, part_0107; RTTI vtable
    /// 0x144715408). The paste cursor FUN_1436d00b0 sends the on-grid ClientSelectionAreaRequest (FUN_1431642e0) when its
    /// +0x2e8 flag is clear, else this request (FUN_1431645b0), with the same paste mode (cursor +0x2e4) and Remove-voxels
    /// flag (cursor +0x2e0) arguments [P]. FUN_1431645b0 fills the packet (constructor FUN_14315e5a0, copy FUN_14315e3e0),
    /// wraps it in a <c>std::function&lt;void(ConstructionArea, HashList&lt;int&gt;)&gt;</c> (FUN_143159fe0), and the
    /// call slot FUN_14315f270 fills the cell list from the ConstructionArea (FUN_143161c90), copies the area into the
    /// packet, fills the material map and sends it on channel 0 = Zone (FUN_14315cea0(gateway, packet, 0, …))
    /// (agent-leads-sweep.md L77) [P].
    /// <para>
    /// <b>Body</b> (writer FUN_14315b130; every sub-writer expanded) [P]:
    /// <code>
    /// u8 layer (+0x18, constructor 0xFF)
    /// f64 × 3 position (+0x20; FUN_142da2f20; the cursor's corner as FUN_142e9d500 computes it: cell × cell size +
    ///        voxel × voxel size — global terrain units, the server's TerrainStore.CellOrigin units)
    /// f32 × 16 transform (+0x40..+0x7c; four FUN_142c32fb0 rows, from the clipboard object +0xb0)
    /// u32 paste mode (+0x80, constructor default 1; cursor +0x2e4: 2 Paste Air, 3 Paste Interior Air)
    /// u8 Remove voxels (+0x84; cursor +0x2e0)
    /// u8 × 3 (+0x85; clipboard +0x12c..+0x12e; meaning not found yet)
    /// PropData list (+0x88; FUN_143100870: u32 n × FUN_1430fe1b0, the TemplateCommon::PropData writer)
    /// u64 design guid (+0xa0; clipboard +0x20, 0 for the clipboard — as the on-grid request)
    /// CellIdAndPermanentVersion list (+0xa8; FUN_14315bea0: u32 n × {u64 cell, u32, u32})
    /// material map (+0xc0; FUN_1431005d0: u32 n × {u16 material, i32 voxels})
    /// ConstructionArea (+0xf0): u64 level-2 cell, 3 × u32 voxel origin (+0xf8), 3 × u32 size (+0x104)
    /// u8 (+0x110; clipboard +0x12f; meaning not found yet)
    /// </code>
    /// </para>
    /// <para>
    /// <b>Where the voxels go</b> [P from the client's own bounds FUN_14315feb0]: a clipboard point l (metres-free: voxel
    /// index × voxel size, x with transform row 0, y row 1, z row 2) lands at position + l·R + T, R the transform's
    /// 3 × 3 rows and T its fourth row (world.x = l.x·M[0] + l.y·M[4] + l.z·M[8] + M[12] + pos.x). The server inverts
    /// that: every voxel inside the turned box takes the clipboard voxel its centre falls in (nearest voxel), its
    /// in-voxel offset turned with R [I: resampling rule ours — the client's own placement code was not traced].
    /// </para>
    /// <para>
    /// Live forwarded the request to the Environment Server as OffGridPasteZoneRequest 0x13 (a registry name; no client
    /// reader). Here, like the on-grid paste, the zone resolves the voxels and the Environment Server writes them in one
    /// operation (<see cref="IEnvironmentServer.WriteCells"/>, job type 11 "Construction Paste Template/Clipboard"), with
    /// the same rules: Player Studio designs only on the player's own site, build permission per cell, the material cost
    /// checked and charged, one undoable edit, props placed, Response (sub 7) with request type 5, the listed cells
    /// answered, the changed cells to everyone.
    /// </para>
    /// </summary>
    public static partial class Building
    {
        public const byte OffGridPasteSub = 5;

        /// <summary>Most voxels one off-grid paste may test (the turned box; ~30 cells of 40³).</summary>
        private const long MaxOffGridVoxels = 2_000_000;

        private sealed class OffGridPasteRequest
        {
            public Vector3D Position;
            public float[] Transform = new float[16];
            public uint PasteMode;
            public bool AsDelete;
            public ulong TemplateGuid;
            public List<ulong> Cells = new();
            public ulong AreaCell;
            public uint AreaX, AreaY, AreaZ, AreaSizeX, AreaSizeY, AreaSizeZ;
        }

        /// <summary>A double-precision point (global terrain units).</summary>
        private readonly record struct Vector3D(double X, double Y, double Z);

        private static OffGridPasteRequest ReadOffGridPaste(byte[] body, OffGridPasteRequest r)
        {
            int p = 1;                                                   // u8 layer
            r.Position = new Vector3D(BitConverter.ToDouble(body, p), BitConverter.ToDouble(body, p + 8), BitConverter.ToDouble(body, p + 16));
            p += 24;
            for (int i = 0; i < 16; i++, p += 4)
                r.Transform[i] = BitConverter.ToSingle(body, p);
            r.PasteMode = BitConverter.ToUInt32(body, p);
            r.AsDelete = body[p + 4] != 0;
            p += 4 + 1 + 3;
            uint props = BitConverter.ToUInt32(body, p);
            p += 4;
            for (uint i = 0; i < props; i++)
                p = SkipPropData(body, p);
            r.TemplateGuid = BitConverter.ToUInt64(body, p);
            p += 8;
            uint cells = BitConverter.ToUInt32(body, p);
            p += 4;
            if (cells > MaxCellsPerEdit)
                throw new System.IO.InvalidDataException($"{cells} cells");
            for (uint i = 0; i < cells; i++, p += 16)
                r.Cells.Add(BitConverter.ToUInt64(body, p));
            p += 4 + (int)BitConverter.ToUInt32(body, p) * 6;           // material map (the client's own count)
            r.AreaCell = BitConverter.ToUInt64(body, p);
            r.AreaX = BitConverter.ToUInt32(body, p + 8);
            r.AreaY = BitConverter.ToUInt32(body, p + 12);
            r.AreaZ = BitConverter.ToUInt32(body, p + 16);
            r.AreaSizeX = BitConverter.ToUInt32(body, p + 20);
            r.AreaSizeY = BitConverter.ToUInt32(body, p + 24);
            r.AreaSizeZ = BitConverter.ToUInt32(body, p + 28);
            p += 32 + 1;
            if (p != body.Length)
                throw new System.IO.InvalidDataException($"parsed to {p} of {body.Length} bytes");
            return r;
        }

        /// <summary>OffGridPasteClientRequest (0xF4 sub 5): see the class summary.</summary>
        private static void HandleOffGridPaste(WorldSession session, byte[] body)
        {
            var request = new OffGridPasteRequest();
            try
            {
                ReadOffGridPaste(body, request);
            }
            catch (Exception e)
            {
                log.Warn($"Unreadable off-grid paste from {session.CharacterName}: {e.Message} ({body.Length} bytes)");
                SendResponse(session, OffGridPasteSub, Failed);
                SendCellResponses(session, request.Cells);
                return;
            }
            List<ulong> changed = OffGridPaste(session, request);
            // The client holds the listed cells as pending until it hears back (as for the on-grid paste).
            SendCellResponses(session, request.Cells);
            if (changed != null)
                ZoneTerrain.Broadcast(changed, session.ZoneId);
        }

        private static List<ulong> OffGridPaste(WorldSession session, OffGridPasteRequest request)
        {
            Clipboard clipboard = null;
            if (request.TemplateGuid != 0)
            {
                if (Templates.VoxelsOf(session, request.TemplateGuid) is { } design)
                    clipboard = new Clipboard(design.Cells, design.SizeX, design.SizeY, design.SizeZ)
                    {
                        Props = Templates.PropsOf(request.TemplateGuid)
                    };
            }
            else
                lock (sync)
                    clipboards.TryGetValue(session.PlayerGuid, out clipboard);
            if (clipboard == null)
            {
                log.Debug($"{session.CharacterName} pasted off-grid with an empty clipboard.");
                SendResponse(session, OffGridPasteSub, Failed);
                return null;
            }

            float[] t = request.Transform;
            var rotation = new Matrix4x4(t[0], t[1], t[2], 0, t[4], t[5], t[6], 0, t[8], t[9], t[10], 0, 0, 0, 0, 1);
            if (t.Any(f => !float.IsFinite(f)) || !double.IsFinite(request.Position.X) || !double.IsFinite(request.Position.Y)
                || !double.IsFinite(request.Position.Z) || !Matrix4x4.Invert(rotation, out Matrix4x4 inverse)
                || Math.Abs(rotation.GetDeterminant()) < 0.01f || Math.Abs(rotation.GetDeterminant()) > 100f)
            {
                log.Info($"{session.CharacterName}: off-grid paste with an unusable transform [{string.Join(" ", t)}] at {request.Position}.");
                SendResponse(session, OffGridPasteSub, Failed);
                return null;
            }

            double v = TerrainEdits.VoxelSize;
            // The box's origin in global units: the cursor's position plus the transform's own translation row.
            double ox = request.Position.X + t[12], oy = request.Position.Y + t[13], oz = request.Position.Z + t[14];
            double[] extent = { (clipboard.SizeX + 1) * v, (clipboard.SizeY + 1) * v, (clipboard.SizeZ + 1) * v };
            double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue }, hi = { double.MinValue, double.MinValue, double.MinValue };
            for (int corner = 0; corner < 8; corner++)
            {
                double a = (corner & 1) != 0 ? extent[0] : 0, b = (corner & 2) != 0 ? extent[1] : 0, c = (corner & 4) != 0 ? extent[2] : 0;
                double[] w =
                {
                    ox + a * t[0] + b * t[4] + c * t[8],
                    oy + a * t[1] + b * t[5] + c * t[9],
                    oz + a * t[2] + b * t[6] + c * t[10]
                };
                for (int i = 0; i < 3; i++)
                {
                    lo[i] = Math.Min(lo[i], w[i]);
                    hi[i] = Math.Max(hi[i], w[i]);
                }
            }
            long x0 = (long)Math.Floor(lo[0] / v), y0 = (long)Math.Floor(lo[1] / v), z0 = (long)Math.Floor(lo[2] / v);
            long x1 = (long)Math.Ceiling(hi[0] / v), y1 = (long)Math.Ceiling(hi[1] / v), z1 = (long)Math.Ceiling(hi[2] / v);
            if (x0 < 0 || y0 < 0 || z0 < 0 || (x1 - x0) * (y1 - y0) * (z1 - z0) > MaxOffGridVoxels)
            {
                log.Info($"{session.CharacterName}: off-grid paste box {x1 - x0}×{y1 - y0}×{z1 - z0} at ({x0}, {y0}, {z0}) refused (too large or outside the world).");
                SendResponse(session, OffGridPasteSub, Failed);
                return null;
            }

            // Every target voxel takes the clipboard voxel its centre falls in: l = (w − origin)·R⁻¹ (row vectors).
            var byCell = new Dictionary<ulong, List<TerrainEdits.VoxelValue>>();
            for (long gz = z0; gz < z1; gz++)
                for (long gx = x0; gx < x1; gx++)
                    for (long gy = y0; gy < y1; gy++)
                    {
                        float wx = (float)((gx + 0.5) * v - ox), wy = (float)((gy + 0.5) * v - oy), wz = (float)((gz + 0.5) * v - oz);
                        Vector3 l = Vector3.Transform(new Vector3(wx, wy, wz), inverse);
                        int sx = (int)Math.Floor(l.X / v), sy = (int)Math.Floor(l.Y / v), sz = (int)Math.Floor(l.Z / v);
                        if (sx < 0 || sy < 0 || sz < 0 || sx > clipboard.SizeX || sy > clipboard.SizeY || sz > clipboard.SizeZ)
                            continue;
                        if (clipboard.At(sx, sy, sz) is not (BlockData source, int from)
                            || (source.Flags[from] & 1) == 0 || source.Materials[from] == 0)
                            continue;
                        ulong cell = TerrainStore.CellId(TerrainEdits.Level, (uint)(gx / BlockData.Size), (uint)(gy / BlockData.Size), (uint)(gz / BlockData.Size));
                        int to = TerrainEdits.VoxelIndex((int)(gx % BlockData.Size), (int)(gy % BlockData.Size), (int)(gz % BlockData.Size));
                        var o = new Vector3(source.Offsets[from * 3] - BlockData.CentreOffset, source.Offsets[from * 3 + 1] - BlockData.CentreOffset,
                            source.Offsets[from * 3 + 2] - BlockData.CentreOffset);
                        Vector3 turned = Vector3.Transform(o, rotation);
                        if (!byCell.TryGetValue(cell, out var values))
                            byCell[cell] = values = new List<TerrainEdits.VoxelValue>();
                        values.Add(new TerrainEdits.VoxelValue(to, source.Flags[from], source.Materials[from],
                            OffsetByte(turned.X), OffsetByte(turned.Y), OffsetByte(turned.Z)));
                    }
            if ((byCell.Count == 0 && clipboard.Props.Count == 0) || byCell.Count > (request.TemplateGuid != 0 ? MaxCellsPerDesign : MaxCellsPerEdit))
            {
                SendResponse(session, OffGridPasteSub, Failed);
                return null;
            }

            uint zone = session.ZoneId;
            // Player Studio designs only on the player's own site (as the on-grid paste; result 0x1A).
            if (request.TemplateGuid != 0 && Templates.IsPlayerStudioCopy(session.PlayerGuid, request.TemplateGuid)
                && !Claims.OwnsEverySiteIn(session.PlayerGuid, zone, (x0 * v, y0 * v, z0 * v), (x1 * v, y1 * v, z1 * v)))
            {
                log.Info($"{session.CharacterName} tried to place Player Studio design {request.TemplateGuid} off-grid on a site they don't own.");
                SendResponse(session, OffGridPasteSub, OnlyClaimOwnerTemplateInstances);
                return null;
            }
            foreach ((ulong cell, List<TerrainEdits.VoxelValue> values) in byCell)
            {
                var mask = new byte[BlockData.Count / 8];
                foreach (TerrainEdits.VoxelValue value in values)
                    mask[value.Index >> 3] |= (byte)(1 << (7 - (value.Index & 7)));
                if (!ZoneTerrain.MayApply(cell, mask, session.PlayerGuid, zone))
                {
                    log.Info($"{session.CharacterName} tried to paste off-grid into cell {cell:X} without permission.");
                    SendResponse(session, OffGridPasteSub, InsufficientPermissions);
                    return null;
                }
            }
            WriteResult written = Env.WriteCells(zone, session.PlayerGuid, byCell.Select(c => (c.Key, c.Value)).ToList(), Budget(session), true);
            if (written.Unavailable)
            {
                SendResponse(session, OffGridPasteSub, Failed);
                ZoneTerrain.SendUnavailable(session);
                return null;
            }
            if (written.Result == InsufficientMaterials)
            {
                SendResponse(session, OffGridPasteSub, InsufficientMaterials);
                return null;
            }
            if (!PayFor(session, written.Delta))
                log.Warn($"{session.CharacterName}'s off-grid paste was written but its materials could not be taken any more.");

            var record = new Edit(zone, request.TemplateGuid != 0 ? "Place design" : "Paste");
            record.Cells.AddRange(written.Cells);
            var origin = TerrainStore.ZoneOrigin;
            var row0 = Vector3.Normalize(new Vector3(t[0], t[1], t[2]));
            var row1 = Vector3.Normalize(new Vector3(t[4], t[5], t[6]));
            var row2 = Vector3.Normalize(new Vector3(t[8], t[9], t[10]));
            Quaternion turn = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                row0.X, row0.Y, row0.Z, 0, row1.X, row1.Y, row1.Z, 0, row2.X, row2.Y, row2.Z, 0, 0, 0, 0, 1)));
            PlaceProps(session, clipboard.Props, prop =>
            {
                // The prop's offset from the box corner (metres → global units), carried by the same transform.
                double a = prop.Offset.X * 10, b = prop.Offset.Y * 10, c = prop.Offset.Z * 10;
                double wx = ox + a * t[0] + b * t[4] + c * t[8], wy = oy + a * t[1] + b * t[5] + c * t[9], wz = oz + a * t[2] + b * t[6] + c * t[10];
                return (new Vector3((float)((wx - origin.X) / 10), (float)((wy - origin.Y) / 10), (float)((wz - origin.Z) / 10)), turn);
            }, record);
            Remember(session, record);
            string designName = request.TemplateGuid != 0 ? Templates.NameOf(session, request.TemplateGuid) : "";
            if (request.TemplateGuid != 0)
                Achievements.DesignPlaced(session, designName);
            Achievements.Pasted(session, designName, request.PasteMode, request.AsDelete);   // tutorial goals
            SendResponse(session, OffGridPasteSub, 0);
            List<ulong> changed = byCell.Keys.Concat(request.Cells.Where(c => Env.Has(c, zone))).Distinct().ToList();
            log.Debug($"{session.CharacterName} pasted {byCell.Values.Sum(x => x.Count)} voxels off-grid into {byCell.Count} cell(s) at "
                + $"({request.Position.X:0.##}, {request.Position.Y:0.##}, {request.Position.Z:0.##}), mode {request.PasteMode}.");
            return changed;
        }

        /// <summary>
        /// Test hook (dev "offgridpaste"): the request the client would send for the clipboard (or a design) with its
        /// corner at a local point, turned <paramref name="yawDegrees"/> about the vertical axis, through the same path as
        /// OffGridPasteClientRequest (the retail cursor's off-grid switch, +0x2e8, was not found yet).
        /// </summary>
        public static bool DevOffGridPaste(WorldSession session, ulong design, Vector3 at, float yawDegrees)
        {
            (double gx, double gy, double gz) = TerrainEdits.ToGlobal(at);
            float c = MathF.Cos(yawDegrees * MathF.PI / 180f), sn = MathF.Sin(yawDegrees * MathF.PI / 180f);
            var request = new OffGridPasteRequest
            {
                Position = new Vector3D(gx, gy, gz),
                // Row vectors (world = l·R): x turns to (cos, 0, −sin), z to (sin, 0, cos); y stays up.
                Transform = new[] { c, 0f, -sn, 0f, 0f, 1f, 0f, 0f, sn, 0f, c, 0f, 0f, 0f, 0f, 1f },
                PasteMode = 1,
                TemplateGuid = design
            };
            List<ulong> changed = OffGridPaste(session, request);
            if (changed == null)
                return false;
            ZoneTerrain.Broadcast(changed, session.ZoneId);
            return true;
        }

        private static byte OffsetByte(float centred) => (byte)Math.Clamp((int)MathF.Round(BlockData.CentreOffset + centred), 0, 255);
    }
}
