using LandmarkEmulator.WorldServer.Game.Terrain;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The Selection tool's box: where the player sees it, which voxels and props each action takes, and where a copy's
    /// grid starts (trace/0695/selection-offset.md; workflow wf_a6205152-f54, 0.695 client traced, constants read from
    /// Landmark64.exe, 2026-11-01).
    /// <para>
    /// A ClientSelectionAreaRequest names its box as a level-2 cell, an origin X / Y / Z (voxels in that cell) and a size
    /// SizeX / Y / Z (voxels). Per axis, with G = cell · 40 + origin (the global grid point of the origin), S the size and
    /// v the voxel (4 dm): a grid point g sits at g·v, the centre of its block [(g − ½)v, (g + ½)v] (the client's mesher
    /// samples materials at grid points and draws a lone solid voxel as the cube between the centres of the eight cubes
    /// around it, docs/terrain.md "How the client draws a dig").
    /// </para>
    /// <list type="bullet">
    /// <item><b>The visible box</b> [P] is [(G − ½)v, (G + S − ½)v]: its faces sit on block faces and it holds exactly the
    /// grid points G .. G + S − 1. The selection cursor (FUN_143794700, FUN_1436eec30) turns the dragged box into the
    /// request with FUN_143c1a8a0: origin = floor((min + 0.75v) / v + 0.5), size = floor((max − min) / v + 0.75)
    /// (<see cref="VisibleBox(ulong, uint, uint, uint, uint, uint, uint)"/>).</item>
    /// <item><b>Delete / Cut voxels</b>: exactly those grid points (<see cref="DeleteMasks"/>). The client's own local
    /// erase is one layer short on the + faces (BuildingDelete.cs); the server's resend corrects it.</item>
    /// <item><b>Copy / Cut / Save Design voxels</b> [P]: the client's own copy, an (S + 1)-point grid starting at G − 1
    /// (FUN_143160530 → FUN_143c18800: origin − 1, size + 1), so clipboard grid point 0 is global grid point G − 1
    /// (<see cref="ClipboardGridZero(ulong, uint, uint, uint)"/>).</item>
    /// <item><b>Props</b>: the ones that stand in the visible box (<see cref="HoldsProp"/>: the point 2 cm up the prop's
    /// own up axis from its pivot, so a prop on a face goes with the side its body is on), the same box for Delete, Cut,
    /// Copy and Save Design; a copied prop keeps its offset from clipboard grid point 0, so a paste puts it back on the
    /// voxel it stood on (BuildingClipboard.cs <c>PastedPropAt</c>).</item>
    /// </list>
    /// <para>
    /// Until 2026-11-01 the props boxes were half a voxel to 1.5 voxels off the visible box: Delete took [G·v, (G + S)·v],
    /// Copy, Cut, Save Design and the Get Inspired / placed-design checks used [G·v, (G + S + 1)·v] (props up to 1.5 voxels
    /// past the + faces went with the selection, props in its − half-layer stayed), and copied props were measured from
    /// G·v instead of grid point 0, so a paste put them one voxel low and towards −x / −z, two voxels off on the axes a
    /// turned paste flips.
    /// </para>
    /// </summary>
    public static partial class Building
    {
        /// <summary>
        /// How close (metres) a tested point may be to a face of the selection box and still count as on it
        /// (<see cref="HoldsPivot"/>). Prop positions are floats in local metres: their step is under 1 mm within ~8 km
        /// of the landscape origin, and a prop snapped onto a block face can sit a few steps off it; 1 cm (1/40 voxel)
        /// leaves room for that and for the box itself being rounded to floats, and is far below anything a player places
        /// by hand on purpose.
        /// </summary>
        internal const float PivotTieTolerance = 0.01f;

        /// <summary>
        /// The visible selection box in global decimetres (TerrainStore.CellOrigin units; see the class summary [P]):
        /// [(G − ½)v, (G + S − ½)v] per axis.
        /// </summary>
        internal static ((double X, double Y, double Z) Min, (double X, double Y, double Z) Max) VisibleBox(ulong cell, uint x, uint y,
            uint z, uint sizeX, uint sizeY, uint sizeZ)
        {
            (double ox, double oy, double oz) = TerrainStore.CellOrigin(cell);
            double v = TerrainEdits.VoxelSize;
            var min = (ox + ((double)x - 0.5) * v, oy + ((double)y - 0.5) * v, oz + ((double)z - 0.5) * v);
            var max = (ox + ((double)x + sizeX - 0.5) * v, oy + ((double)y + sizeY - 0.5) * v, oz + ((double)z + sizeZ - 0.5) * v);
            return (min, max);
        }

        /// <summary>The visible selection box in client-local metres ((global − zone origin) / 10, as prop positions are).</summary>
        internal static (Vector3 Min, Vector3 Max) VisibleBoxLocal(ulong cell, uint x, uint y, uint z, uint sizeX, uint sizeY, uint sizeZ)
        {
            ((double X, double Y, double Z) min, (double X, double Y, double Z) max) = VisibleBox(cell, x, y, z, sizeX, sizeY, sizeZ);
            return (ToLocal(min), ToLocal(max));
        }

        /// <summary>
        /// Clipboard grid point 0 of a copy, in client-local metres: global grid point G − 1 at (G − 1)·v, where the
        /// client's copy grid starts (FUN_143c18800 [P]). Copied props are measured from here (Props.InBox).
        /// </summary>
        internal static Vector3 ClipboardGridZero(ulong cell, uint x, uint y, uint z)
        {
            (double ox, double oy, double oz) = TerrainStore.CellOrigin(cell);
            double v = TerrainEdits.VoxelSize;
            return ToLocal((ox + ((double)x - 1) * v, oy + ((double)y - 1) * v, oz + ((double)z - 1) * v));
        }

        private static Vector3 ToLocal((double X, double Y, double Z) global)
        {
            var origin = TerrainStore.ZoneOrigin;
            return new Vector3((float)((global.X - origin.X) / 10), (float)((global.Y - origin.Y) / 10), (float)((global.Z - origin.Z) / 10));
        }

        /// <summary>
        /// How far (metres) along a prop's own up axis its pivot is moved before the box tests it
        /// (<see cref="PropBodyPoint"/>): twice <see cref="PivotTieTolerance"/>, so a pivot up to 1 cm either side of a
        /// face still goes with the side the prop's body is on.
        /// </summary>
        internal const float PropBodyNudge = 2 * PivotTieTolerance;

        /// <summary>
        /// Does a point (local metres) lie in the selection box [min, max]? Each axis is half-open, [min − ε, max − ε) with
        /// ε = <see cref="PivotTieTolerance"/>, so two boxes that share a face never both take a point and never both
        /// leave it [I: rule ours; live's own rule for a prop on a face is not known, OPEN-LEADS L250]. Props are tested
        /// at their body point (<see cref="HoldsProp"/>), not at their pivot.
        /// </summary>
        internal static bool HoldsPivot(Vector3 min, Vector3 max, Vector3 at)
            => at.X >= min.X - PivotTieTolerance && at.X < max.X - PivotTieTolerance
                && at.Y >= min.Y - PivotTieTolerance && at.Y < max.Y - PivotTieTolerance
                && at.Z >= min.Z - PivotTieTolerance && at.Z < max.Z - PivotTieTolerance;

        /// <summary>
        /// Does a placed prop (pivot and rotation, local metres) stand in the selection box [min, max]? Its body point
        /// (<see cref="PropBodyPoint"/>) by the half-open rule of <see cref="HoldsPivot"/>. Props.StandingIn, so the copy
        /// (Copy, Cut, Save Design) and the take-down (Delete, Cut) use the same test.
        /// </summary>
        internal static bool HoldsProp(Vector3 min, Vector3 max, Vector3 pivot, Vector4 rotation)
            => HoldsPivot(min, max, PropBodyPoint(pivot, rotation));

        /// <summary>
        /// The point of a placed prop the selection box tests: its pivot moved <see cref="PropBodyNudge"/> (2 cm) along
        /// the prop's own up axis, rotation · +Y (the quaternion applied as System.Numerics does, as everywhere in Props; a
        /// zero quaternion is the identity). A prop stands on its pivot (its base), so the point lies just inside the prop's
        /// body, and a pivot on a face of the box goes with the side the body is on [I: rule ours, L250]:
        /// <list type="bullet">
        /// <item>an upright prop (up +Y): standing on the box's floor (the block under the selection) it is in, standing on
        /// the top of its highest layer it is out, as with the pivot itself; on a side face the half-open split of
        /// <see cref="HoldsPivot"/> still decides (in on a − face, out on a + face);</item>
        /// <item>a prop on a wall or hanging from a ceiling, if its up axis is that surface's normal (Align to Surface, the
        /// placement mode once called Pitch Plant, P2580 / P5508 [I: the client's placement code and the models' pivots
        /// are not traced]): the signs on all of a building's outer walls are left by a box drawn flush with those walls,
        /// as the props on its roof are; the torches on all of a room's inner walls and a lamp under its ceiling go with a
        /// box drawn flush with the room's interior.</item>
        /// </list>
        /// A prop sunk up to 3 cm into the block it stands on still counts as standing on it. It is one point per prop, so
        /// two boxes that share a face still split the props between them. Until the review of 2026-11-01 the pivot itself
        /// was tested: a pivot on a − face was always taken and one on a + face always left, whichever side the body was on
        /// (with a box flush with a building's walls, the signs on its −x / −z walls went, while those on +x / +z stayed and
        /// were left floating once the walls were deleted), and a floor prop sunk more than 1 cm into the floor was left out.
        /// </summary>
        internal static Vector3 PropBodyPoint(Vector3 pivot, Vector4 rotation)
        {
            var q = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
            Vector3 up = Vector3.Transform(Vector3.UnitY, q.LengthSquared() > 0.0001f ? Quaternion.Normalize(q) : Quaternion.Identity);
            return pivot + up * PropBodyNudge;
        }

        private static ((double X, double Y, double Z) Min, (double X, double Y, double Z) Max) VisibleBox(SelectionRequest r)
            => VisibleBox(r.Cell, r.X, r.Y, r.Z, r.SizeX, r.SizeY, r.SizeZ);

        private static (Vector3 Min, Vector3 Max) VisibleBoxLocal(SelectionRequest r)
            => VisibleBoxLocal(r.Cell, r.X, r.Y, r.Z, r.SizeX, r.SizeY, r.SizeZ);
    }
}
