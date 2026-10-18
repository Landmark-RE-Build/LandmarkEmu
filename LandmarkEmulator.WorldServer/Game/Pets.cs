using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Game.Terrain;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Cosmetic pets (28 Jul 2016 update; TASKS 3.10, trace/0695/spec-pets.md, OPEN-LEADS L24 / L27). Right-clicking a
    /// figurine summons its pet, which follows its owner; one pet at a time, another figurine swaps it, and the pet goes
    /// when its buff icon is cancelled or the owner logs out. The client's own rules (text 577546): "Figurines have
    /// unlimited uses, but you may only have one active pet at a time." — so a figurine is never used up.
    /// <para>
    /// The 0.695 client has no pet code (no new class in re/out/rtti_new_695.txt, no new packet or handler; the
    /// cPacketIdPetBase 0x39 / ListPets / PetTrick ids are Free Realms leftovers that no handler uses). Live built pets
    /// from systems the client already had, and so does this module (spec §1, §4):
    /// <list type="bullet">
    /// <item>the figurine item (<see cref="ItemDefinitionBuilder"/>, template 3711, no ability), whose right-click sends
    /// Inventory 0x2D sub 0x15 ActivateItem → <see cref="ItemUses.TryActivate"/> → <see cref="Summon"/>;</item>
    /// <item>a permanent buff icon (Abilities 0x0C EffectStart + 0x1C EffectInitialized with duration 0: the
    /// BuffListItemRenderer.as:77-87 countdown only runs when secondsTotal &gt; 0) whose right-click sends Abilities
    /// 0x1E RequestCancelEffect → <see cref="Buffs.Cancel"/> → <see cref="Cancel"/>;</item>
    /// <item>the pet itself: an NPC (PlayerUpdate 0x59 AddLightweightNpc) with the pet's model (Models.txt 1729-1735),
    /// moved by the server with Position 0x7F type-0 movement bodies (<see cref="Creatures.MovementBody"/>), removed
    /// with PlayerUpdate 0x01 RemovePlayer.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Following is server-driven (inference from the patch note P6145 "if the pet is not following you, try summoning
    /// it while NOT on a build site": live's pathing struggled on player-built voxels). The client's own seek controller
    /// (0x2E SeekTarget → ProxiedCharacterSeekMovementController FUN_1435a3710) homes onto its target and snaps onto it
    /// within a fixed distance (FUN_1435a3900, DAT_144692dc0), which suits a projectile or a charge, not a pet at heel,
    /// so it is not used (spec §3.3).
    /// </para>
    /// <para>
    /// Pets are not kept in Creatures' region tables: those belong to a home region and stream by region
    /// (Creatures.ViewersOf), while a pet travels with its owner. Creatures' step / face / viewer helpers are private
    /// and creature-specific (combat statuses, cavern floors), so this module has a small follower of its own that sends
    /// the same packets: the spawn as Creatures.BuildSpawn builds it, <see cref="Creatures.MovementBody"/> (public) for
    /// movement, and RemovePlayer.
    /// </para>
    /// <para>
    /// Interest management (bandwidth rule): a pet is shown to players on its landscape within <see cref="ViewRange"/>
    /// and dropped beyond <see cref="DropRange"/> (the same ranges as creatures), checked once a second; movement goes
    /// only to those viewers and only while the pet moves, plus one zero-velocity update when it stops (the idle pose).
    /// A viewer whose world streams are on hold (<see cref="WorldSession.StreamsOnHold"/>: a teleport arrival or a
    /// re-zone still loading) gets no pet packets at all until it is done (project note "cave teleport hang").
    /// </para>
    /// </summary>
    public static class Pets
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// One pet: the figurine that summons it, its texts and look (spec §2 table, all proven client data). Names /
        /// descriptions are re/out/text_695.tsv rows (new in 0.695: trace/0695/text-new.tsv 577507-577540), icons
        /// ImageSets ids (Pets_*), models Models.txt rows (gameData695/Models.txt). Models 1729-1731 / 1734-1735 are a
        /// new row kind (RACE_ID 12 "Monster", IS_VALID_FOR_PC 1, PATH_BLOCK_RADIUS 0); 1732 / 1733 are small copies of
        /// the Spirit Wolf / Deer creature rigs.
        /// </summary>
        public sealed record PetKind(uint Item, string Name, uint FigurineName, uint FigurineDescription, uint PetName,
            uint Icon, uint Model);

        /// <summary>The figurines (item ids are ours: <see cref="ItemDefinitionBuilder.ShadowWolfFigurine"/> ff.).</summary>
        public static readonly PetKind[] Kinds =
        {
            new(ItemDefinitionBuilder.ShadowWolfFigurine, "Shadow Wolf Pup", 577507, 577508, 577510, 21740, 1732),   // Char_Wolf_SpiritWolf_Entities_WolfPup_00_Chest.adr
            new(ItemDefinitionBuilder.StagFigurine, "Young Stag", 577512, 577513, 577514, 21739, 1733),             // Char_Deer_Deer_Entities_Fawn_00_Chest.adr
            new(ItemDefinitionBuilder.CockatriceFigurine, "Baby Cockatrice", 577515, 577516, 577517, 21738, 1729),  // Char_FlightlessBird_Cockatrice_Body_00.adr
            new(ItemDefinitionBuilder.BunnyFigurine, "Bunny", 577518, 577519, 577520, 21735, 1730),                 // Char_Bunny_Bunny_00_bunny.adr
            new(ItemDefinitionBuilder.BeetleFigurine, "Beetle", 577521, 577522, 577523, 21734, 1731),               // Char_Klicnik_Klicnik_000.adr
            new(ItemDefinitionBuilder.JackalopeFigurine, "Jackalope", 577532, 577533, 577534, 21736, 1734),         // Char_Bunny_Bunny_00_Jackalope.adr
            new(ItemDefinitionBuilder.LopEarBunnyFigurine, "Lop Ear Bunny", 577538, 577539, 577540, 21737, 1735),   // Char_Bunny_Bunny_00_Lopear.adr
        };

        private static readonly Dictionary<uint, PetKind> byItem = Kinds.ToDictionary(k => k.Item);

        /// <summary>The pet a figurine item summons, or null.</summary>
        public static PetKind KindOfItem(uint itemId) => byItem.GetValueOrDefault(itemId);

        /// <summary>Is this item a pet figurine (for <see cref="ItemUses.Handles"/>)?</summary>
        public static bool IsFigurine(uint itemId) => byItem.ContainsKey(itemId);

        /// <summary>
        /// The buff key of the pet icon: one key for every pet, so a player has one icon with a stable effect id
        /// (<see cref="Buffs.EffectId"/>, the icon's GUID that RequestCancelEffect sends back), and a swap replaces it.
        /// </summary>
        public const string IconKey = "Pet";

        /// <summary>
        /// Pet guids: a base of their own (top byte 0x52, unused elsewhere: creatures 0x50 / caverns 0x51, Spire and
        /// templates 0x53, loot chests and outposts 0x54, warp crystals 0x57, gathering 0x40) plus a counter.
        /// </summary>
        private const ulong GuidBase = 0x5250_0000_0000_0000;

        /// <summary>Show / drop distances (m), as Creatures (ViewRange / DropRange): the gap stops flicker at the edge.</summary>
        private const float ViewRange = 120f, DropRange = 160f;

        /// <summary>AI steps per second (as Creatures.AiHz).</summary>
        private const int AiHz = 5;

        /// <summary>Where the pet stands: this far behind its owner and this far to the owner's right (m).</summary>
        private const float FollowBehind = 2f, FollowSide = 1f;

        /// <summary>
        /// A resting pet sets off once its spot is this far away (m); a following pet stops on its spot once the owner
        /// stands still. The gap keeps a turning or shuffling owner from making the pet dance.
        /// </summary>
        private const float StartDistance = 3f;

        /// <summary>
        /// Speeds (m/s): never slower than <see cref="MinSpeed"/> (a walk), never faster than <see cref="MaxSpeed"/>; in
        /// between, the owner's own speed plus <see cref="CatchUpPerMetre"/> for each metre it is behind its spot. Live's
        /// values are unknown (spec §6.6).
        /// </summary>
        private const float MinSpeed = 1.5f, MaxSpeed = 10f, CatchUpPerMetre = 1f;

        /// <summary>Further than this from its owner (m), or on another landscape, the pet is put next to them at once.</summary>
        private const float SnapDistance = 40f;

        /// <summary>
        /// Height: on open ground the pet walks the natural ground (<see cref="TerrainHeights.HeightAt"/>, as creatures
        /// do). When the owner is this far (m) above or below it — in the air, in a cave, in a mine — or above the ground
        /// on a build site (<see cref="OnSiteAbove"/>), it takes the owner's height instead, half the gap each step, so it
        /// doesn't sink into built floors (live's known build-site trouble, P6145).
        /// </summary>
        private const float OffGround = 2.5f, OnSiteAbove = 0.75f, HeightLag = 0.5f;

        private sealed class Pet
        {
            public ulong Guid;
            public uint TransientId;
            public PetKind Kind;
            public WorldSession Owner;
            /// <summary>The landscape the pet is on (its owner's, unless the owner just changed landscape).</summary>
            public uint Zone;
            public Vector3 Position;
            public float Yaw;
            public Vector3 Velocity;
            public bool Moving;
            /// <summary>Moved on the previous step: a pet that stops gets one zero-velocity update (idle pose).</summary>
            public bool WasMoving;
            /// <summary>The owner's position at the previous step (their speed).</summary>
            public Vector3 OwnerLast;
        }

        private static readonly object sync = new();
        /// <summary>The active pet of each player (one at most).</summary>
        private static readonly Dictionary<WorldSession, Pet> pets = new();
        /// <summary>The pets each player's client has been sent (to remove them and not to send them twice).</summary>
        private static readonly Dictionary<WorldSession, HashSet<ulong>> shown = new();
        private static long nextGuid;
        private static DateTime nextTick = DateTime.MinValue, nextStream = DateTime.MinValue;

        // ---- Summon / dismiss -----------------------------------------------------------------------------------------

        /// <summary>
        /// Uses a figurine (from <see cref="ItemUses.TryActivate"/>): summons its pet. The same pet already out stays as
        /// it is; another pet out is dismissed first (patch note P6146: switching is allowed). The figurine is not taken
        /// (text 577546 "Figurines have unlimited uses"). Refused while dead, with the client's generic "can't use"
        /// (<see cref="ItemUses.FailedItemRequirementText"/>; live's answer is unknown). Instanced games are allowed
        /// (live unknown, spec §5.3).
        /// </summary>
        public static void Summon(WorldSession session, PetKind kind)
        {
            if (Health.IsDead(session))
            {
                ItemUses.Refuse(session, ItemUses.FailedItemRequirementText);
                return;
            }
            Pet old;
            lock (sync)
            {
                if (pets.TryGetValue(session, out old) && old.Kind == kind)
                    return;
            }
            if (old != null)
                Despawn(old);
            float yaw = OwnerYaw(session);
            Vector3 spot = SpotBehind(session.Position, yaw);
            var pet = new Pet
            {
                Guid = GuidBase | (ulong)System.Threading.Interlocked.Increment(ref nextGuid),
                TransientId = PlayerManager.AllocateTransientId(),
                Kind = kind,
                Owner = session,
                Zone = session.ZoneId,
                Yaw = yaw,
                OwnerLast = session.Position
            };
            pet.Position = spot with { Y = HeightFor(pet, spot, session.Position, snap: true) };
            lock (sync)
                pets[session] = pet;
            ShowIcon(session, kind);
            foreach (WorldSession viewer in PlayerManager.InZone(pet.Zone))
                if (!viewer.Disconnected && !viewer.StreamsOnHold && Vector3.Distance(viewer.Position, pet.Position) <= ViewRange)
                    SendSpawn(viewer, pet);
            log.Info($"{session.CharacterName} summoned their {kind.Name} (0x{pet.Guid:X}, model {kind.Model}).");
        }

        /// <summary>
        /// Puts the pet icon on the owner's buff bar (<see cref="Buffs.ShowStatus"/>: EffectStart + EffectInitialized)
        /// with duration 0, a permanent icon without a timer. Name = the pet's name text, image set = the figurine's icon.
        /// The tooltip shows the name only (BuffListItemRenderer.as: _tooltipText = name), so no description is sent.
        /// Which texts / icon live used here is unknown (spec §6.3).
        /// </summary>
        private static void ShowIcon(WorldSession session, PetKind kind)
            => Buffs.ShowStatus(session, IconKey, kind.PetName, kind.Icon, TimeSpan.Zero, 1);

        /// <summary>Sends the pet away: gone for every viewer, its icon off the owner's buff bar.</summary>
        public static void Dismiss(WorldSession session)
        {
            Pet pet;
            lock (sync)
                pets.Remove(session, out pet);
            Buffs.HideStatus(session, IconKey);
            if (pet == null)
                return;
            RemoveFromViewers(pet);
            log.Info($"{session.CharacterName} dismissed their {pet.Kind.Name}.");
        }

        /// <summary>A replaced pet: gone for every viewer (the icon stays; the new pet's replaces it).</summary>
        private static void Despawn(Pet pet)
        {
            lock (sync)
                if (pets.TryGetValue(pet.Owner, out Pet current) && current == pet)
                    pets.Remove(pet.Owner);
            RemoveFromViewers(pet);
        }

        private static void RemoveFromViewers(Pet pet)
        {
            List<WorldSession> viewers;
            lock (sync)
                viewers = shown.Where(s => s.Value.Remove(pet.Guid)).Select(s => s.Key).ToList();
            foreach (WorldSession viewer in viewers)
                if (!viewer.Disconnected)
                    viewer.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = pet.Guid });
        }

        /// <summary>
        /// The buff bar's cancel (Abilities 0x1E RequestCancelEffect, via <see cref="Buffs.Cancel"/>): true when the
        /// effect is the pet icon, which dismisses the pet (spec §3.2: "Cancel the pet icon above your health bar to
        /// dismiss it").
        /// </summary>
        public static bool Cancel(WorldSession session, uint effectId)
        {
            if (effectId != Buffs.EffectId(IconKey))
                return false;
            Dismiss(session);
            return true;
        }

        /// <summary>
        /// After a zone load the client has dropped its effects and asks again (AbilityEffectUpdatesRequest →
        /// <see cref="Buffs.Resend"/>): the pet icon is shown again.
        /// </summary>
        public static void ResendIcon(WorldSession session)
        {
            PetKind kind;
            lock (sync)
                kind = pets.TryGetValue(session, out Pet pet) ? pet.Kind : null;
            if (kind != null)
                ShowIcon(session, kind);
        }

        /// <summary>
        /// After a re-zone the client has dropped every entity but its own character (PlayerManager.Reintroduce): it
        /// forgets which pets it was sent, so the next stream (once its streams are off hold) sends the pets near it
        /// again, its own included. The pet itself is put next to its owner by the next AI step when the move was long.
        /// </summary>
        public static void Reintroduce(WorldSession session)
        {
            lock (sync)
                shown.Remove(session);
        }

        /// <summary>
        /// The owner left the world (PlayerManager.Leave): the pet goes with them (buffs are per session, Buffs.cs;
        /// persistence over relog is unknown, spec §6.10), and what this client was shown is forgotten.
        /// </summary>
        public static void Leave(WorldSession session)
        {
            Pet pet;
            lock (sync)
            {
                pets.Remove(session, out pet);
                shown.Remove(session);
            }
            if (pet != null)
                RemoveFromViewers(pet);
        }

        // ---- Follow ---------------------------------------------------------------------------------------------------

        /// <summary>Server tick: streaming once a second, the follow step at <see cref="AiHz"/>.</summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            List<Pet> active;
            lock (sync)
            {
                if (pets.Count == 0 && shown.Count == 0)
                    return;
                active = pets.Values.ToList();
            }
            foreach (Pet gone in active.Where(p => p.Owner.Disconnected))
                Leave(gone.Owner);
            active.RemoveAll(p => p.Owner.Disconnected);
            if (now >= nextStream)
            {
                nextStream = now.AddSeconds(1);
                Stream(active);
            }
            if (now < nextTick)
                return;
            float dt = 1f / AiHz;
            nextTick = now.AddSeconds(dt);
            foreach (Pet pet in active)
                Follow(pet, dt);
        }

        /// <summary>
        /// One follow step. The pet's spot is <see cref="FollowBehind"/> m behind and <see cref="FollowSide"/> m beside
        /// its owner. Far away or on another landscape: put on the spot at once. Resting: sets off once the spot is
        /// <see cref="StartDistance"/> away. Following: walks / runs to the spot at the owner's speed plus a catch-up, and
        /// rests once there with the owner standing still (one zero-velocity update: the client's idle pose; it picks
        /// walk / run / idle from the velocity, Creatures.cs MovementBody).
        /// </summary>
        private static void Follow(Pet pet, float dt)
        {
            WorldSession owner = pet.Owner;
            Vector3 o = owner.Position;
            float ownerYaw = OwnerYaw(owner);
            float ownerSpeed = Flat(o, pet.OwnerLast) / dt;
            pet.OwnerLast = o;
            Vector3 spot = SpotBehind(o, ownerYaw);

            if (owner.ZoneId != pet.Zone || Vector3.Distance(pet.Position, o) > SnapDistance)
            {
                // A teleport, warp, re-zone or landscape change: next to the owner at once. Viewers on the old landscape
                // lose it with the next stream; those still seeing it get one update at the new place, as players are
                // shown after a teleport (PlayerManager.ShowPosition).
                pet.Zone = owner.ZoneId;
                pet.Position = spot with { Y = HeightFor(pet, spot, o, snap: true) };
                pet.Yaw = ownerYaw;
                pet.Velocity = Vector3.Zero;
                pet.Moving = pet.WasMoving = false;
                SendMovement(pet);
                return;
            }

            Vector3 before = pet.Position;
            float distance = Flat(pet.Position, spot);
            if (!pet.Moving && distance > StartDistance)
                pet.Moving = true;
            if (pet.Moving)
            {
                if (distance <= 0.05f)
                {
                    if (ownerSpeed < 0.3f)
                    {
                        pet.Moving = false;
                        pet.Yaw = ownerYaw;   // rests looking the way its owner looks
                    }
                }
                else
                {
                    float speed = Math.Clamp(ownerSpeed + distance * CatchUpPerMetre, MinSpeed, MaxSpeed);
                    float step = MathF.Min(speed * dt, distance);
                    Vector3 flat = new(spot.X - pet.Position.X, 0f, spot.Z - pet.Position.Z);
                    Vector3 next = pet.Position + flat / distance * step;
                    next.Y = HeightFor(pet, next, o, snap: false);
                    pet.Position = next;
                    pet.Yaw = MathF.Atan2(flat.X, flat.Z);
                    if (distance - step <= 0.05f && ownerSpeed < 0.3f)
                        pet.Moving = false;
                }
            }
            bool moved = pet.Position != before;
            pet.Velocity = moved ? (pet.Position - before) / dt : Vector3.Zero;
            if (moved || pet.WasMoving)
                SendMovement(pet);
            pet.WasMoving = moved;
        }

        /// <summary>The pet's spot: behind and beside a position facing <paramref name="yaw"/>.</summary>
        private static Vector3 SpotBehind(Vector3 at, float yaw)
        {
            var forward = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
            var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
            return at - forward * FollowBehind + right * FollowSide;
        }

        /// <summary>
        /// The owner's heading: the yaw of their last reported rotation, in the creatures' convention (rotation
        /// (0, sin(yaw/2), 0, cos(yaw/2)), yaw = atan2(dx, dz)).
        /// </summary>
        private static float OwnerYaw(WorldSession owner)
        {
            Quaternion q = owner.Facing;
            return 2f * MathF.Atan2(q.Y, q.W);
        }

        /// <summary>The pet's height at a point (see <see cref="OffGround"/>).</summary>
        private static float HeightFor(Pet pet, Vector3 at, Vector3 owner, bool snap)
        {
            uint zone = pet.Owner.ZoneId;
            float? ground = TerrainHeights.Has(zone) ? TerrainHeights.HeightAt(at.X, at.Z, zone) : null;
            bool useOwner = ground is not float g
                || MathF.Abs(owner.Y - g) > OffGround
                || (owner.Y - g > OnSiteAbove && Claims.ClaimAt(zone, owner) != null);
            if (!useOwner)
                return ground.Value;
            return snap ? owner.Y : pet.Position.Y + (owner.Y - pet.Position.Y) * HeightLag;
        }

        private static float Flat(Vector3 a, Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        // ---- Streaming ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Interest management, once a second: each player on a pet's landscape within <see cref="ViewRange"/> is sent
        /// the pet, and a player who has it but is beyond <see cref="DropRange"/>, on another landscape, or whose pet is
        /// gone loses it. Players whose streams are on hold are left alone until they are ready.
        /// </summary>
        private static void Stream(List<Pet> active)
        {
            var spawns = new List<(WorldSession, Pet)>();
            var removals = new List<(WorldSession, ulong)>();
            List<WorldSession> players = PlayerManager.Snapshot();
            lock (sync)
            {
                foreach (WorldSession gone in shown.Where(s => s.Key.Disconnected || s.Value.Count == 0).Select(s => s.Key).ToList())
                    shown.Remove(gone);
                Dictionary<ulong, Pet> byGuid = active.ToDictionary(p => p.Guid);
                foreach (WorldSession viewer in players)
                {
                    if (viewer.Disconnected || viewer.StreamsOnHold)
                        continue;
                    shown.TryGetValue(viewer, out HashSet<ulong> have);
                    if (have != null)
                        foreach (ulong guid in have.ToList())
                            if (!byGuid.TryGetValue(guid, out Pet p) || p.Zone != viewer.ZoneId
                                || Vector3.Distance(p.Position, viewer.Position) > DropRange)
                            {
                                have.Remove(guid);
                                removals.Add((viewer, guid));
                            }
                    foreach (Pet p in active)
                        if (p.Zone == viewer.ZoneId && (have == null || !have.Contains(p.Guid))
                            && Vector3.Distance(p.Position, viewer.Position) <= ViewRange)
                            spawns.Add((viewer, p));
                }
            }
            foreach ((WorldSession viewer, ulong guid) in removals)
                viewer.EnqueueMessage(new PlayerUpdateRemovePlayer { Guid = guid });
            foreach ((WorldSession viewer, Pet p) in spawns)
                SendSpawn(viewer, p);
        }

        /// <summary>
        /// Shows a pet to one player: AddLightweightNpc (PlayerUpdateAddLightweightNpc.cs; 0.695 u32 sub-opcode,
        /// coverage-695.md:777) with the pet's model and name text (NameId, used when both name strings are empty), its
        /// position in the local frame and its facing. ControlMode as creatures (<see cref="Creatures.ControlMode"/>,
        /// 2): non-zero or the client leaves the NPC where it spawned (reader +0x19c → entity +0x41a0, FUN_142df5160).
        /// No Life resource is sent, so the client shows no nameplate or health (requirement 21 "NPC Combatant" fails,
        /// see Creatures.SendLife) — fitting profile 379 "Non-Combat Entity" (inference, spec §6.7). No 0x28 UpdateOwner
        /// (its layout past the guid is untraced, spec §6.5).
        /// </summary>
        private static void SendSpawn(WorldSession viewer, Pet pet)
        {
            lock (sync)
            {
                if (!shown.TryGetValue(viewer, out HashSet<ulong> have))
                    shown[viewer] = have = new HashSet<ulong>();
                if (!have.Add(pet.Guid))
                    return;
            }
            viewer.EnqueueMessage(new PlayerUpdateAddLightweightNpc
            {
                ControlMode = Creatures.ControlMode,
                Guid = pet.Guid,
                TransientId = pet.TransientId,
                NameId = pet.Kind.PetName,
                ModelId = pet.Kind.Model,
                Position = new Vector4(pet.Position, 1f),
                Rotation = new Vector4(0f, MathF.Sin(pet.Yaw / 2), 0f, MathF.Cos(pet.Yaw / 2))
            });
        }

        /// <summary>
        /// Position 0x7F with a type-0 movement body (<see cref="Creatures.MovementBody"/>: local frame, heading,
        /// velocity, state 0x401 moving / 0x441 still) to the players who have the pet and are on its landscape, through
        /// <see cref="WorldSession.QueueMovement"/> (only the newest per entity goes out each flush).
        /// </summary>
        private static void SendMovement(Pet pet)
        {
            List<WorldSession> viewers;
            lock (sync)
                viewers = shown.Where(s => s.Value.Contains(pet.Guid)).Select(s => s.Key).ToList();
            byte[] movement = null;
            foreach (WorldSession viewer in viewers)
            {
                if (viewer.Disconnected || viewer.StreamsOnHold || viewer.ZoneId != pet.Zone)
                    continue;
                movement ??= Creatures.MovementBody(pet.Position, pet.Yaw, pet.Velocity);
                Network.MovementFanout.Queue(viewer, pet.TransientId, movement);
            }
        }
    }
}
