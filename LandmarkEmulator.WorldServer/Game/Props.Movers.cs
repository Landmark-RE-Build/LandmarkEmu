using LandmarkEmulator.Shared.GameTable;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Movers and placement packs (TASKS E.1; full trace with proof marks: trace/0695/movers-695.md). A mover is placed as
    /// one spawner prop (PropDefs PLACEMENT_PACK_ID ≠ 0: Translocation Pad 1196, Spring-loaded Flinger 1200, Magical
    /// Platform 1207 …) that the server replaces with its parts (PlacementPackProps.txt: prop, offset, scale, heading) —
    /// the client never reads those offsets (its pack manager only maps a part to its pack for the known-prop lookups,
    /// 0.695 FUN_143535a60). Each part's spawn carries a static area (GlobalAreas id, base +0x260), an area guid (+0x268)
    /// and the pack's group key (+0x270); the client then builds the trigger area from its own tables and runs it:
    /// <list type="bullet">
    /// <item>Translocation Pad (area 7, ClientAreaTeleporter): stepping on a pad sends TeleportPlayEffectRequest 0xFC/9 and
    /// TeleportManagedRequest 0xFC/10; the server moves the player to the pack's other pad (<see cref="ManagedTeleport"/>).</item>
    /// <item>Flinger (area 6, ChainThrustPad) and its target (area 19): the client throws the player at the part of the same
    /// group key; Launcher (area 20, Thrower): by its speed and pitch — client physics, nothing for the server.</item>
    /// <item>Magical Platform (area 14) and docks (area 15): their moving-platform properties (types 27 / 28) have no client
    /// class, so the movement was the server's: PlayerUpdatePosition 0x7F for the platform's transient id
    /// (<see cref="UpdatePlatforms"/>, Props.Platforms.cs; whether riders stay on is the runtime part of L135).</item>
    /// </list>
    /// Which area each part gets is inferred from the areas' properties and shapes (the table below); the settings list in
    /// the spawn (+0x340) is not sent yet (L134), so movers use the EditableProperties defaults.
    /// </summary>
    public static partial class Props
    {
        /// <summary>Static area (GlobalAreas.txt id) per mover part prop [I: matched by the areas' properties and shapes].</summary>
        private static readonly Dictionary<uint, uint> moverAreas = new()
        {
            [1194] = 7,    // Translocation Pad: Teleporter (type 23), sphere r 1.5 at 1.5 m, enable_teleportation
            [1206] = 7,    // Dark Elf Teleporter (pack 4: two of them) — a teleporter pair like the pads
            [1188] = 6,    // Spring-loaded Flinger: ChainThrustPad (24) with the flinger's launch settings
            [1191] = 19,   // Flinger Target: FlingerParamEditProxy (31), trajectory height
            [1233] = 20,   // Spring-loaded Launcher: Thrower (32), launch speed and pitch
            [1193] = 14,   // Magical Platform: SlidingFriction (26) + moving platform (28)
            [1421] = 14,   // Glass Platform
            [1192] = 15,   // Magical Platform Dock: moving object (27) + its edit proxy (29)
            [1422] = 15,   // Glass Platform dock
        };

        /// <summary>Teleporter areas: <see cref="ManagedTeleport"/> moves between parts with these.</summary>
        private const uint TeleporterArea = 7;

        /// <summary>How long the arrival pad is ignored after a Translocation Pad hop (ours; trace/0695/agent-short-teleport.md).</summary>
        private const uint PadArrivalIgnoreMs = 2500;

        /// <summary>
        /// Tags that keep a mover's area guid and group key apart from prop guids and the Area Tool's area guids (both
        /// small counters), so a TeleportManagedRequest naming either one is understood.
        /// </summary>
        private const ulong MoverAreaTag = 0x4D41_0000_0000_0000UL, MoverGroupTag = 0x4D47_0000_0000_0000UL,
            MoverTagMask = 0xFFFF_0000_0000_0000UL;

        /// <summary>A placement pack's part (PlacementPackProps.txt).</summary>
        private sealed record PackPart(uint PropId, uint Index, float OffsetX, float OffsetZ, float Scale, float Heading);

        /// <summary>Pack id → its parts by PROP_INDEX, and → its separation limits (PlacementPacks.txt; metres [I]).</summary>
        private static readonly Lazy<(Dictionary<uint, List<PackPart>> Parts, Dictionary<uint, (float Min, float Max)> Limits)> packs = new(() =>
        {
            var parts = new Dictionary<uint, List<PackPart>>();
            foreach (string[] r in GameDataFiles.Rows("PlacementPackProps.txt"))
            {
                if (!parts.TryGetValue(r.U(1), out var list))
                    parts[r.U(1)] = list = new List<PackPart>();
                list.Add(new PackPart(r.U(2), r.U(3), r.F(4), r.F(5), r.F(6), r.F(7)));
            }
            foreach (var list in parts.Values)
                list.Sort((a, b) => a.Index.CompareTo(b.Index));
            var limits = new Dictionary<uint, (float, float)>();
            foreach (string[] r in GameDataFiles.Rows("PlacementPacks.txt"))
                limits[r.U(0)] = (r.F(2), r.F(4));
            return (parts, limits);
        });

        /// <summary>The placement pack a spawner prop places (0 = an ordinary prop).</summary>
        public static uint PackOf(uint propId) => GameTableManager.Instance.PropDefs.GetEntry(propId)?.PlacementPackId ?? 0;

        /// <summary>The parts of one pack (the group's props, any state).</summary>
        private static List<PlacedProp> PackParts(ulong group)
        {
            lock (sync)
                return props.Where(p => p.PackGroup == group).ToList();
        }

        /// <summary>Gives a mover part its static area, area guid and the pack's group key (the spawn fields above).</summary>
        private static void BindMover(PlacedProp part)
        {
            if (part.PackGroup == 0)
                return;
            part.Spawn.GroupGuid = part.PackGroup;
            if (moverAreas.TryGetValue(part.PropId, out uint area))
            {
                part.Spawn.StaticAreaId = area;
                part.Spawn.AreaGuid = MoverAreaTag | part.Spawn.Guid;
                // A spawn with an area guid and no name is named "area" by the client (FUN_142d54040: area guid != 0 and
                // name length +0x1318 == 0 → FUN_142ec18c0(entity, "area"), its only caller) [P], which floats over the
                // part as a label — meant for the Area Tool's box (AreaDefinitions), not for movers (user 2026-10-18:
                // remove it). The area guid must stay (the static area is created under it), so the part gets a blank
                // name: the client keeps it and draws nothing.
                if (string.IsNullOrEmpty(part.Spawn.Name))
                    part.Spawn.Name = " ";
            }
            BindPlatform(part);
        }

        /// <summary>
        /// The first Save of a spawner prop: its pack's parts take its place at their offsets (turned with it), all paid
        /// by the spawner; the spawner itself goes away. Returns the parts (empty when the pack is unknown).
        /// </summary>
        private static List<PlacedProp> ExpandPack(WorldSession owner, PlacedProp spawner)
        {
            var made = new List<PlacedProp>();
            uint pack = PackOf(spawner.PropId);
            if (!packs.Value.Parts.TryGetValue(pack, out List<PackPart> parts) || parts.Count == 0)
                return made;
            Vector4 p = spawner.Spawn.Position, r = spawner.Spawn.Rotation;
            var origin = new Vector3(p.X, p.Y, p.Z);
            var turn = Quaternion.Normalize(new Quaternion(r.X, r.Y, r.Z, r.W));
            foreach (PackPart part in parts)
            {
                if (!propModels.TryGetValue(part.PropId, out uint model))
                    continue;
                Vector3 at = origin + Vector3.Transform(new Vector3(part.OffsetX, 0f, part.OffsetZ), turn);
                Quaternion heading = turn * Quaternion.CreateFromAxisAngle(Vector3.UnitY, part.Heading * MathF.PI / 180f);
                PlacedProp made1 = Create(owner, part.PropId, model, NameIdOf(part.PropId), at);
                made1.Zone = spawner.Zone;
                made1.Spawn.Rotation = new Vector4(heading.X, heading.Y, heading.Z, heading.W);
                made1.Spawn.Scale = part.Scale > 0f ? new Vector4(part.Scale, part.Scale, part.Scale, 1f) : spawner.Spawn.Scale;
                made1.Placed = true;
                made1.PaidByItem = spawner.PaidByItem;
                made1.PaidByItemGuid = spawner.PaidByItemGuid;
                made1.PackSpawner = spawner.PropId;
                made.Add(made1);
            }
            if (made.Count == 0)
                return made;
            ulong group = MoverGroupTag | made[0].Spawn.Guid;
            foreach (PlacedProp part in made)
            {
                part.PackGroup = group;
                BindMover(part);
                Persist(part);
            }
            // Streamed as one pack: every part is listed in every part's region (Props.Stream.cs), then shown to the
            // players holding any of them.
            lock (sync)
            {
                IndexLocked(made[0]);
                props.Remove(spawner);
                byGuid.Remove(spawner.Spawn.Guid);
            }
            foreach (PlacedProp part in made)
                Show(part);
            SendRemove(owner, spawner);
            log.Info($"{owner.CharacterName}'s {spawner.PropId} became pack {pack}: {string.Join(", ", made.Select(m => $"{m.PropId} 0x{m.Spawn.Guid:X} at {m.Spawn.Position.X:0.##} {m.Spawn.Position.Y:0.##} {m.Spawn.Position.Z:0.##}"))}.");
            return made;
        }

        /// <summary>
        /// May a part of a pack move to <paramref name="to"/>? Every other part must stay within the pack's maximum
        /// separation (PlacementPacks.txt MAX_SEPARATION_DISTANCE: Translocation Pads 298, the rest 46430; metres [I]) —
        /// the 10 Sep 2014 fix Translocation Pads can't teleport to a distant claim any more. Staying on
        /// the owner's build land is <see cref="MayPlaceAt"/>'s check, as for any prop (a Flinger's endpoint can't be
        /// placed outside your claim).
        /// </summary>
        private static bool PackAllows(PlacedProp part, Vector3 to)
        {
            if (part.PackGroup == 0 || !packs.Value.Limits.TryGetValue(PackOf(part.PackSpawner), out var limits) || limits.Max <= 0f)
                return true;
            foreach (PlacedProp other in PackParts(part.PackGroup))
                if (other != part && Vector3.Distance(to, new Vector3(other.Spawn.Position.X, other.Spawn.Position.Y, other.Spawn.Position.Z)) > limits.Max)
                    return false;
            return true;
        }

        /// <summary>
        /// Picking up / deleting one part takes the whole pack: the other parts leave without a refund, and the picked one
        /// refunds as the spawner (<see cref="ReturnOf"/> uses <see cref="PlacedProp.PackSpawner"/>).
        /// </summary>
        private static void RemoveOtherParts(PlacedProp picked)
        {
            if (picked.PackGroup == 0)
                return;
            foreach (PlacedProp other in PackParts(picked.PackGroup))
                if (other != picked)
                    Remove(other, null, Refund.None);
        }

        /// <summary>
        /// Where a mover is (local metres), by any of the ids the client may name it with: the part's prop guid, its area
        /// guid or the pack's group key (the first part). Null when there is no such mover on this server.
        /// </summary>
        public static Vector3? MoverPosition(ulong id)
        {
            PlacedProp part;
            if ((id & MoverTagMask) == MoverGroupTag)
                part = PackParts(id).FirstOrDefault();
            else
                part = Find((id & MoverTagMask) == MoverAreaTag ? id & ~MoverTagMask : id);
            return part == null ? null : new Vector3(part.Spawn.Position.X, part.Spawn.Position.Y, part.Spawn.Position.Z);
        }

        /// <summary>
        /// TeleportPlayEffectRequest (TeleportBase 0xFC sub 9, 0.695 FUN_1431736e0): {u64, u32 composite effect, u32
        /// duration ms}. A teleporter area asks for its departure / arrival effect on the player (PhysicsTeleportCallback
        /// FUN_1436db650 states 1 and 6, the effect ids from AreaProperties INT params); shown to the player and everyone
        /// near [I: who sees it].
        /// </summary>
        public static void TeleportEffect(WorldSession session, byte[] body)
        {
            if (body.Length < 16)
                return;
            uint effect = BitConverter.ToUInt32(body, 8), duration = BitConverter.ToUInt32(body, 12);
            if (effect == 0 || effect > 1_000_000)
                return;
            var viewers = PlayerManager.InZone(session.ZoneId)
                .Where(v => Vector3.Distance(v.Position, session.Position) <= 100f).Append(session).Distinct();
            HitEffects.PlayOn(session.PlayerGuid, effect, viewers, Math.Min(duration, 10_000u));
        }

        /// <summary>
        /// TeleportManagedRequest (0xFC sub 10, 0.695 FUN_143173480): {u64 guid, u8, u32 1, u32 0, u32 −1, u64, u64}, sent
        /// by a teleporter area once its effect has played (FUN_1436db650 state 5). The guid is the pad's (its prop, area
        /// guid or group key — all are accepted): the player goes to the other teleporter of the same pack, on top of it.
        /// One-way pads are the client's own check (the source pad's enable_teleportation, FUN_1436db8b0).
        /// </summary>
        public static void ManagedTeleport(WorldSession session, byte[] body)
        {
            if (body.Length < 8)
                return;
            ulong id = BitConverter.ToUInt64(body, 0);
            ulong group = 0;
            if ((id & MoverTagMask) == MoverGroupTag)
                group = id;
            else
            {
                ulong guid = (id & MoverTagMask) == MoverAreaTag ? id & ~MoverTagMask : id;
                group = Find(guid)?.PackGroup ?? 0;
            }
            List<PlacedProp> pads = group == 0 ? new List<PlacedProp>()
                : PackParts(group).Where(p => p.Placed && p.Zone == session.ZoneId && p.Spawn.StaticAreaId == TeleporterArea).ToList();
            Vector3 Pos(PlacedProp p) => new(p.Spawn.Position.X, p.Spawn.Position.Y, p.Spawn.Position.Z);
            PlacedProp from = pads.OrderBy(p => Vector3.Distance(Pos(p), session.Position)).FirstOrDefault();
            // The player stands on the pad that sent it (its trigger sphere is 1.5 m around a point 1.5 m up).
            if (from == null || pads.Count < 2 || Vector3.Distance(Pos(from), session.Position) > 6f)
            {
                log.Info($"{session.CharacterName}: managed teleport 0x{id:X} refused (no pad pair here).");
                return;
            }
            PlacedProp to = pads.Where(p => p != from).OrderBy(p => p.Spawn.Guid).First();
            // The arrival pad is ignored for a moment, so standing on it doesn't send the player straight back. The key is
            // the pad's prop guid, not its area guid: the teleporter area keeps the spawn's guid (packet +0x18) in its data
            // +0x10 (FUN_142d54040 type 0x17 → vfunc 0xd0 = FUN_1431b6070), and both checks of the ignore set ask with that
            // value (enter FUN_1436db8b0: the entity found by data +0x10, its +0x278 guid; state machine FUN_1436db650
            // case 1: callback +0x28 = data +0x10, FUN_1436db150) [P] (trace/0695/audit-wire-e.md). With the area guid
            // (MoverAreaTag | guid, sent 2026-10-18) the arrival pad was never ignored.
            Teleport.To(session, Pos(to) + new Vector3(0f, 0.25f, 0f), snapToGround: false, loadingScreen: false,
                ignoreTeleporter: to.Spawn.Guid, ignoreMs: PadArrivalIgnoreMs);
            log.Info($"{session.CharacterName} translocated 0x{from.Spawn.Guid:X} → 0x{to.Spawn.Guid:X}.");
        }
    }
}
