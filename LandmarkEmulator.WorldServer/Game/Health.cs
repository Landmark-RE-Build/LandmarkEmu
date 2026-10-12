using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Player hit points, death and respawn (the server owns them; the client only shows them).
    /// - ClientUpdate 0x01 Hitpoints {u32 current, u32 max} drives the health bar (client FUN_142b83000).
    /// - At 0 the server sends EncounterBase 0x2C {i16 0x7D, u32 pending, u32 encounter} (Encounters.SendDeathWindow) → "You have died." window (RespawnHandler:Show);
    ///   {u16 0x7E} hides it.
    /// - The window answers with PlayerUpdate 0x3B Respawn {u32 type, ...}: 10 = revive here (costs
    ///   <see cref="ReviveCost"/> Lumen, item 1579, the client checks the count first), 11/12 = nearest safe point / hub.
    /// </summary>
    public static class Health
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Maximum hit points: the player's health resource (Resources.txt id 4, type 1 Health, MAX_VALUE 900), which is
        /// what the HUD bar shows.
        /// </summary>
        public const uint MaxHealth = 900;
        private const uint HealthResourceId = 4, HealthResourceType = 1;
        public const uint LumenItem = 1579, ReviveCost = 15;

        /// <summary>
        /// Reviving another player (player right-click "Revive Player", Lua Ui.RevivePlayer FUN_14351ace0): the client's
        /// action manager casts Abilities.ReviveInteractionAbilityId (ResourceConstants 246) at the dead player, but only
        /// when its copy of that player holds RevivePoints (resource type 12; Resources.txt id 29, 50 / 50) above 0
        /// (FUN_1433abc00 → FUN_143ad04e0(entity resources, 12)). So a death tells every client on the landscape the dead
        /// player's RevivePoints, and the cast revives them in place. 0.589 has no accept prompt (UI.DoYouAcceptRevive
        /// is only a text mapping, no code uses it) and no point cost is defined, so the points are not spent.
        /// </summary>
        public const uint ReviveInteractionAbility = 246, RevivePointsId = 29, RevivePointsType = 12, RevivePoints = 50;
        /// <summary>How close the reviver must stand (metres).</summary>
        private const float ReviveRange = 8f;
        public const uint RespawnHere = 10;
        /// <summary>Death window "graveyard" choice (free): respawn at the nearest respawn site (Graveyards).</summary>
        public const uint RespawnSafePoint = 11;
        /// <summary>
        /// Death window "Flee" (Lua ReviveFlee). The window shows its free respawn button when a type-11 location is
        /// listed (FUN_1433878f0), labelled Flee; the click requests type 12 and the client then loops until it finds a
        /// type-12 location (FUN_143387ee0): without one it hung for good. Both go to the nearest respawn site.
        /// </summary>
        public const uint RespawnFlee = 12;

        private static readonly object sync = new();
        private static readonly Dictionary<WorldSession, uint> current = new();
        private static readonly HashSet<WorldSession> dead = new();

        /// <summary>
        /// A player's current hit points (full when not tracked yet).
        /// </summary>
        public static uint Current(WorldSession session)
        {
            lock (sync)
                return current.TryGetValue(session, out uint hp) ? hp : MaxHealth;
        }

        /// <summary>
        /// Is the player dead (death window shown, waiting to respawn)?
        /// </summary>
        public static bool IsDead(WorldSession session)
        {
            lock (sync)
                return dead.Contains(session);
        }

        /// <summary>Sends the health bar on world entry.</summary>
        public static void SendInitial(WorldSession session) => SendHitpoints(session, Current(session));

        /// <summary>
        /// Drops a player's health state when they leave; they come back with full health.
        /// </summary>
        public static void Forget(WorldSession session)
        {
            lock (sync)
            {
                current.Remove(session);
                dead.Remove(session);
                reviveOnArrival.Remove(session);
                CharacterResources.Forget(session);
                WeaponAttacks.Forget(session);
                lastHurt.Remove(session);
                lastEvacuation.Remove(session);
            }
        }

        /// <summary>
        /// Takes hit points away; at 0 the player dies. <paramref name="attacker"/> is the player who dealt the damage
        /// (a site game's PvP hit), null for creatures, falls and the like; <paramref name="sourceGuid"/> is the
        /// creature that dealt it (0 when none). With a source the victim is told who hit them (LastDamageTime), which
        /// is what draws the client's damage-direction arrows round the reticle.
        /// </summary>
        /// <summary>Dev / tests: players who take no damage (dev "god on").</summary>
        public static readonly HashSet<WorldSession> Invulnerable = new();

        /// <summary>Dev / tests: alive again with full health where they stand, death window closed.</summary>
        public static void DevRevive(WorldSession session)
        {
            if (!IsDead(session))
                return;
            Revive(session);
            Encounters.SendDeathWindow(session, false);
        }

        public static void Damage(WorldSession session, uint amount, WorldSession attacker = null, ulong sourceGuid = 0)
        {
            uint hp, lost;
            bool died;
            lock (sync)
            {
                if (dead.Contains(session) || Invulnerable.Contains(session))
                    return;
                hp = Current(session);
                lost = Math.Min(amount, hp);
                hp -= lost;
                lastHurt[session] = DateTime.UtcNow;
                current[session] = hp;
                died = hp == 0;
                if (died)
                    dead.Add(session);
            }
            SendHitpoints(session, hp);
            ulong source = attacker?.PlayerGuid ?? sourceGuid;
            if (source != 0 && source != session.PlayerGuid)
                SendLastDamage(session, source);
            if (attacker != null && attacker != session)
                HitConfirmed(attacker);
            if (lost > 0)
                Outposts.Interrupt(session);
            Durability.Damaged(session, lost, died);
            if (died)
            {
                // A player in a running site game respawns at the game's spawn points instead (no death window).
                if (InstancedGames.Died(session, attacker))
                    return;
                SendRespawnLocations(session);
                Encounters.SendDeathWindow(session, true);
                byte[] knockedOut = BuildKnockedOut(session, attacker);
                foreach (WorldSession viewer in PlayerManager.InZone(session.ZoneId))
                {
                    CharacterResources.SendFor(viewer, session.PlayerGuid, RevivePointsId, RevivePointsType, RevivePoints);
                    viewer.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateKnockedOutWire, knockedOut);
                }
                log.Info($"{session.CharacterName} died.");
            }
        }

        /// <summary>
        /// Combat LastDamageTime (CombatBase 0x24 → bytes 24 00, i16 sub 0x0D; reader FUN_14331e310, the combat
        /// dispatcher FUN_14331da00 case 0x0C): {u64 attacker}. The client (FUN_142e248b0) stamps
        /// GrapplingHook.LastDamageTime with the time of the hit and hands the attacker to the ReticleManager
        /// (FUN_143156ad0): when the attacker is the player's current target the reticle flashes its hit marker
        /// (GameEvents:ShowReticleHitMarker), otherwise it works out the attacker's direction from the camera and puts it
        /// in the next of the three Reticle.HitIndicator1-3 rows ("Value" = degrees), which the HUD's reticle shows as an
        /// arrow fading over Reticle.HitIndicatorFadeTime (1000 ms). Live sent it with every hit a player took, and to
        /// the player who landed a hit with their own guid (see <see cref="HitConfirmed"/>).
        /// </summary>
        private static void SendLastDamage(WorldSession session, ulong attacker)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write((ushort)0x0D);
            w.Write(attacker);
            session.EnqueueMessage(ZoneMessageOpcode.CombatBase, output.ToArray());
        }

        /// <summary>
        /// A player's attack landed (on a creature or another player): LastDamageTime with the player's own guid, which
        /// the client's ReticleManager (FUN_143156ad0) turns into GameEvents:ShowReticleHitMarker — the Combat HUD's
        /// reticle flashes its hit marker (events.lua ShowReticleHitMarker → CombatHudWidget.showReticleHitMarker).
        /// </summary>
        public static void HitConfirmed(WorldSession attacker) => SendLastDamage(attacker, attacker.PlayerGuid);

        /// <summary>Was the player hurt within the last <paramref name="span"/> (in combat)?</summary>
        public static bool HurtWithin(WorldSession session, TimeSpan span)
        {
            lock (sync)
                return lastHurt.TryGetValue(session, out DateTime at) && DateTime.UtcNow - at < span;
        }

        /// <summary>Regeneration of the health resource (Resources.txt REGEN_PER_MS 0.05 → 50 per second).</summary>
        private const uint RegenPerSecond = 50;
        /// <summary>No regeneration this long after taking damage (out of combat only).</summary>
        private static readonly TimeSpan RegenDelay = TimeSpan.FromSeconds(8);
        private static readonly Dictionary<WorldSession, DateTime> lastHurt = new();
        private static DateTime nextRegen = DateTime.MinValue;

        /// <summary>Server tick: once a second, wounded living players regenerate (one small packet each).</summary>
        public static void Update()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextRegen)
                return;
            nextRegen = now.AddSeconds(1);
            List<WorldSession> wounded;
            lock (sync)
                wounded = current.Where(p => p.Value < MaxHealth && !dead.Contains(p.Key)
                    && (!lastHurt.TryGetValue(p.Key, out DateTime hurt) || now - hurt > RegenDelay)).Select(p => p.Key).ToList();
            foreach (WorldSession session in wounded)
                Heal(session, RegenPerSecond);
        }

        /// <summary>
        /// Restores hit points (regeneration), up to the maximum; nothing for the dead, and nothing sent when already at
        /// full health.
        /// </summary>
        public static void Heal(WorldSession session, uint amount)
        {
            uint hp;
            lock (sync)
            {
                if (dead.Contains(session) || Current(session) >= MaxHealth)
                    return;
                hp = Math.Min(MaxHealth, Current(session) + amount);
                current[session] = hp;
            }
            SendHitpoints(session, hp);
        }

        /// <summary>
        /// Brings a dead player back with full health where they are (a site game's respawn moves them afterwards).
        /// </summary>
        public static void Revive(WorldSession session)
        {
            lock (sync)
            {
                dead.Remove(session);
                current[session] = MaxHealth;
            }
            SendHitpoints(session, MaxHealth);
        }

        /// <summary>A newcomer sees a dead player's RevivePoints too (PlayerManager.Introduce).</summary>
        public static void IntroduceDead(WorldSession viewer, WorldSession subject)
        {
            if (IsDead(subject))
                CharacterResources.SendFor(viewer, subject.PlayerGuid, RevivePointsId, RevivePointsType, RevivePoints);
        }

        /// <summary>Who is reviving whom: the dead player and the reviver, until the dead client answers ReadyToRevive.</summary>
        private static readonly Dictionary<WorldSession, WorldSession> reviving = new();

        /// <summary>
        /// The revive cast (246) from <paramref name="reviver"/> at a dead player nearby. As the live server: the dead
        /// player's client gets ReadyToRevive (PlayerUpdate 0x44 {u64 dead, u64 reviver, u64, bool, u32}; client case
        /// sub 0x44 in FUN_142c45fa0 checks the entity is down, records the reviver, and for the local player answers
        /// ReadyToReviveResponse(1) by itself, so 0.589 shows no prompt), and the answer revives them.
        /// </summary>
        public static void ReviveBy(WorldSession reviver, WorldSession target)
        {
            if (target == null || target == reviver || !IsDead(target) || IsDead(reviver) || target.ZoneId != reviver.ZoneId
                || System.Numerics.Vector3.Distance(target.Position, reviver.Position) > ReviveRange)
            {
                Chat.System(reviver, "There is no one here to revive.");
                return;
            }
            lock (sync)
                reviving[target] = reviver;
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(target.PlayerGuid);
            w.Write(reviver.PlayerGuid);
            w.Write(0ul);
            w.Write(false);
            w.Write(0u);
            target.EnqueueMessage(ZoneMessageOpcode.PlayerUpdateReadyToReviveWire, output.ToArray());
            log.Info($"{reviver.CharacterName} is reviving {target.CharacterName}.");
        }

        /// <summary>ReadyToReviveResponse (0x3D {u8 accept, u64 own guid}): accepted, the player is revived in place.</summary>
        public static void HandleReadyToReviveResponse(WorldSession session, byte[] body)
        {
            bool accept = body.Length > 0 && body[0] != 0;
            WorldSession reviver;
            lock (sync)
                reviving.Remove(session, out reviver);
            log.Info($"{session.CharacterName} answered the revive: {(accept ? "accept" : "decline")} ({BitConverter.ToString(body)}).");
            if (reviver == null || !IsDead(session))
                return;
            if (!accept)
            {
                Chat.System(reviver, $"{session.CharacterName} declined the revive.");
                return;
            }
            Revive(session);
            Encounters.SendDeathWindow(session, false);
            // The other clients still hold the knocked-out entity: they meet the player again, alive.
            PlayerManager.Reshow(session);
            Chat.System(session, $"{reviver.CharacterName} revived you.");
            Chat.System(reviver, $"You revived {session.CharacterName}.");
            log.Info($"{reviver.CharacterName} revived {session.CharacterName}.");
        }

        /// <summary>
        /// KnockedOut (PlayerUpdate 0x39; reader FUN_142b8efc0): what every client on the landscape is told when a
        /// player goes down: u64 victim, victim info, a stat block, killer info, a stat block, a damage report, u32.
        /// The client adds a kill-feed line and runs the victim's own knocked-out handling (entity vtable +0x3f0), which
        /// is what makes the body revivable on other screens.
        /// Character info (FUN_142b8bda0): string name, u64 guid, u32 x 4, u8, u32 x 9, {list of 3-u32 entries, u32,
        /// string, u32, u32}, u32 x 7, u64, u32 x 2, u32 x 3, u32. Stat block (FUN_142b96d60): u64, u32 x 12. Damage
        /// report (FUN_142b84fd0): u8 x 2, u32, u64 x 3, u32 x 8, u32 x 4, u32 x 4.
        /// </summary>
        private static byte[] BuildKnockedOut(WorldSession victim, WorldSession killer)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            void Info(string name, ulong guid)
            {
                w.Write(name ?? "");
                w.Write(guid);
                for (int i = 0; i < 4; i++) w.Write(0u);
                w.Write((byte)0);
                for (int i = 0; i < 9; i++) w.Write(0u);
                w.Write(0u);
                w.Write(0u);
                w.Write("");
                w.Write(0u); w.Write(0u);
                for (int i = 0; i < 7; i++) w.Write(0u);
                w.Write(0ul);
                w.Write(0u); w.Write(0u);
                for (int i = 0; i < 3; i++) w.Write(0u);
                w.Write(0u);
            }
            void Stats(ulong guid)
            {
                w.Write(guid);
                for (int i = 0; i < 12; i++) w.Write(0u);
            }
            w.Write(victim.PlayerGuid);
            Info(victim.CharacterName, victim.PlayerGuid);
            Stats(victim.PlayerGuid);
            Info(killer?.CharacterName ?? "", killer?.PlayerGuid ?? 0ul);
            Stats(killer?.PlayerGuid ?? 0ul);
            w.Write((byte)0); w.Write((byte)0);
            w.Write(0u);
            w.Write(killer?.PlayerGuid ?? 0ul);
            w.Write(victim.PlayerGuid);
            w.Write(0ul);
            for (int i = 0; i < 16; i++) w.Write(0u);
            w.Write(0u);
            return output.ToArray();
        }

        private static readonly HashSet<WorldSession> reviveOnArrival = new();

        /// <summary>A re-zone finished (Teleport.FinishRezone): a respawn that moved the player shows them alive now.</summary>
        public static void Arrived(WorldSession session)
        {
            bool revive;
            lock (sync)
                revive = reviveOnArrival.Remove(session);
            if (!revive)
                return;
            SendHitpoints(session, Current(session));
            Encounters.SendDeathWindow(session, false);
        }

        /// <summary>The death window's choice. Reviving in place costs Lumen; the other choices are free.</summary>
        /// <summary>When each player last asked to be evacuated (at most once every 10 s).</summary>
        private static readonly Dictionary<WorldSession, DateTime> lastEvacuation = new();

        /// <summary>
        /// ZoneSafeTeleportRequest: the client found its player outside the zone (fell out of the world) or asked for its
        /// safe point. Moves them to the nearest respawn site (<see cref="Graveyards"/>), as the death window's free
        /// respawn does; with none, back onto the ground where they are. Throttled: the client may repeat it every frame
        /// until the move arrives.
        /// </summary>
        public static void EvacuateToSafePoint(WorldSession session)
        {
            DateTime now = DateTime.UtcNow;
            lock (sync)
            {
                if (lastEvacuation.TryGetValue(session, out DateTime last) && now - last < TimeSpan.FromSeconds(10))
                    return;
                lastEvacuation[session] = now;
            }
            if (session.RezoneTarget != null)
                return;
            Graveyards.Site site = Graveyards.Nearest(session, session.Position);
            if (site != null)
                Teleport.ToZone(session, site.Position, site.Zone);
            else
                Teleport.To(session, session.Position, snapToGround: true);
            log.Info($"{session.CharacterName} evacuated to {site?.Name ?? "the ground below"} (safe point request).");
        }

        public static void HandleRespawn(WorldSession session, uint type)
        {
            if (!IsDead(session))
                return;
            if (type == RespawnHere && !session.TakeItem(LumenItem, ReviveCost))
            {
                Chat.System(session, $"You need {ReviveCost} Lumen to revive here.");
                return;
            }
            lock (sync)
            {
                dead.Remove(session);
                current[session] = MaxHealth;
            }
            // The graveyard choice: the respawn site nearest to where the player fell.
            Graveyards.Site site = type != RespawnHere ? Graveyards.Nearest(session, session.Position) : null;
            if (site != null)
            {
                // Back at a site's Starting Location: its "When a Luminary respawns at this Build Site..." Links run.
                if (site.Name == "Build Site" && site.ClaimId != 0)
                    StoryTools.ExpectArrival(session, site.ClaimId, respawn: true);
                Teleport.ToZone(session, site.Position, site.Zone);
            }
            log.Info($"{session.CharacterName} respawned (type {type}) at {site?.Name ?? "the spot they fell"}.");
            // Moved: the hit points and the window's close wait until the re-zone has finished (Arrived). Sent in the
            // same burst as ClientBeginZoning, hit points could hang the client (docs/terrain.md, long teleports).
            if (session.RezoneTarget != null)
            {
                lock (sync)
                    reviveOnArrival.Add(session);
                return;
            }
            SendHitpoints(session, MaxHealth);
            Encounters.SendDeathWindow(session, false);
        }

        /// <summary>
        /// Offers the free respawn next to "Revive Here" (which needs Lumen): the nearest respawn site
        /// (<see cref="Graveyards"/>; where the player fell when there is none), listed twice: as type 12 first (the
        /// Flee click looks for that type and hangs the client without it, see <see cref="RespawnFlee"/>) and as type 11
        /// (which makes the window show the button).
        /// </summary>
        private static void SendRespawnLocations(WorldSession session)
        {
            Graveyards.Site site = Graveyards.Nearest(session, session.Position);
            Vector3 at = site?.Position ?? session.Position;
            string name = site?.Name ?? "Respawn";
            var message = new Network.Message.Model.ClientUpdateRespawnLocations();
            foreach (uint type in new[] { RespawnFlee, RespawnSafePoint })
                message.RespawnLocations.Add(new()
                {
                    Guid = session.PlayerGuid,
                    Type = (byte)type,
                    Position = new Vector4(at, 1f),
                    Name = name
                });
            session.EnqueueMessage(message);
        }

        /// <summary>
        /// Updates the player's health: the HUD bar (the Health resource) and ClientUpdate Hitpoints {u32 current, u32 max}.
        /// </summary>
        private static void SendHitpoints(WorldSession session, uint hp)
        {
            SendHealthResource(session, hp);
            var output = new List<byte>();
            var writer = new GamePacketWriter(output);
            writer.Write(hp);
            writer.Write(MaxHealth);
            session.EnqueueMessage(ZoneMessageOpcode.ClientUpdateHitpoints, output.ToArray());
        }

        /// <summary>The Life resource on the HUD (see <see cref="CharacterResources"/>).</summary>
        private static void SendHealthResource(WorldSession session, uint hp)
            => CharacterResources.Send(session, HealthResourceId, HealthResourceType, hp);

    }
}
