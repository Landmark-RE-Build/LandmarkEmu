using LandmarkEmulator.WorldServer.Network.Message;
﻿using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.WorldServer.Game.Inventory;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message.Model;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Abilities that come from items: their definitions for the client, and casting them (tool swings).
    /// </summary>
    public static class Abilities
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        // Item ability types the client treats as tool uses: 3 = primary (mining with a pick), and the logging and
        // reaping slots (FUN_143abe650 / FUN_143abe730 read the other two).
        private const uint PrimaryUse = 3;

        /// <summary>
        /// Reach of non-tool item abilities (consumables), in metres. Not known from captures; the lightstone's uses 30.
        /// </summary>
        private const float ToolRange = 30f;

        /// <summary>
        /// Base reach of gathering tools, metres (CasterInfo MaxRange). The client draws the mining ring in its
        /// "invalid" colour beyond it. The Reach stat (15, "harvest things that are further away") adds to it.
        /// </summary>
        public const float BaseToolReach = 6f;

        /// <summary>Extra the server allows over the reach: the player moves between the client's check and the swing.</summary>
        private const float ReachSlack = 2.5f;

        /// <summary>How far this player's gathering tools reach (base + Reach stat), metres.</summary>
        public static float ToolReach(WorldSession session) => BaseToolReach + Buffs.Apply(session, 15, 0f);

        /// <summary>
        /// Builds the CasterInfoReply for an ability an item gives (a tool's swing, a consumable's use): the item's name and
        /// description, the tool range, and flags that let the client switch tool mode and skip resource costs (no
        /// resource pools are sent yet).
        /// </summary>
        public static AbilityCasterInfoReply BuildItemAbilityInfo(ItemDefinitionManager.ItemDefinition item,
            ItemDefinitionManager.ItemAbility ability)
        {
            var reply = new AbilityCasterInfoReply
            {
                AbilityId = ability.AbilityId,
                NameId = item.NameId,
                DescriptionId = item.DescriptionId,
                // Lets the client re-pick its tool mode (mining for a pick) now that it knows the ability.
                UpdatesToolMode = true,
                MaxRange = ToolRange,
                // No resource pools are sent yet, so the ability must not require one.
                IgnoreCostPool = true,
                Ua0 = ability.AbilityId,
                AnimationId = 245,
                // About one swing a second, so holding the button doesn't flood the server with casts.
                CastTime = 0.75f,
                RecoveryTime = 0.25f,
                Bc4 = true,
                // The ability's own cooldown (struct +0x70 → record +0x14c, FUN_142d79400): potions.
                U70 = Consumables.CooldownOf(ability.AbilityId)
            };
            if (IsHarvestAbility(ability.AbilityId))
            {
                reply.MaxRange = BaseToolReach;
                // Harvest tools repeat while the button is held, so they must not start a global cooldown: any cast or
                // recovery time lands in it (+0x9c/+0xa0 → player +0x75620) and every held retry or quick click then
                // fails with "ability on cooldown" (tried a 1 s cast time 2026-10-11: one swing, then only cooldowns).
                // The pace is kept server-side instead (TooSoon), and the swing animation is sent by the server
                // (Emotes.ShowSwing to the swinger too). U68 set: recast = 50 / stat 13 + base (FUN_142d77f20).
                reply.CastTime = 0f;
                reply.RecoveryTime = 0f;
                reply.U68 = 1;
                // Ua0 lands in the ability's cost-resource id (+0x7c). The held-button repeat (ActionStateMining update
                // FUN_143677b40 → ClientActionManager +0x28 → FUN_142d80f30) only fires again while the player can
                // afford the cost in that resource; naming a resource that doesn't exist (the ability id) stopped it
                // after the first swing.
                reply.Ua0 = 0;
            }
            if (ReapingAbilities.Contains(ability.AbilityId))
            {
                // A held sickle draws its plant-harvest wedge (ActionStateReaping → PlantHarvestDecal) from this
                // ability's arc: ArcDegrees is the angle, MinRange/MaxRange the inner/outer reach in metres
                // (FUN_1433abd90 → reaping SetParam 9/10/11). With no angle the wedge has no width and isn't seen.
                reply.ArcDegrees = SickleArcDegrees;
                reply.MinRange = 0f;
                reply.MaxRange = SickleReach;
            }
            return reply;
        }

        /// <summary>Width of the sickle's harvest wedge, degrees (not known from captures).</summary>
        private const float SickleArcDegrees = 90f;

        /// <summary>Outer reach of the sickle's harvest wedge, metres; the server accepts plants a little further out.</summary>
        private const float SickleReach = 5f;

        /// <summary>
        /// A cast from the client. Item abilities are allowed when the player has an item that gives them: tools
        /// must be held, usable items (potions, gadgets, dynamite, campfire) go to Consumables.
        /// </summary>
        public static void HandleCast(WorldSession session, AbilityCastAttempt attempt)
        {
            // Any cast shows the client is not stuck in a channel it let go of (MovementEffects.Release).
            MovementEffects.NoteCast(session);
            log.Debug($"Cast {attempt.AbilityId} by {session.CharacterName} at <{attempt.TargetX:F2}, {attempt.TargetY:F2}, "
                + $"{attempt.TargetZ:F2}> ring {attempt.RingPoint?.ToString("F2") ?? "none"} tail {BitConverter.ToString(attempt.Tail ?? Array.Empty<byte>())}");
            if (TravelAbilities.Contains(attempt.AbilityId))
            {
                Travel(session, attempt);
                return;
            }
            if (attempt.AbilityId == Health.ReviveInteractionAbility)
            {
                Answer(session, attempt);
                Health.ReviveBy(session, PlayerManager.FindByGuid(TargetGuid(attempt, session)) ?? FindPlayerIn(attempt.Tail, session));
                return;
            }
            CharacterItemModel source = session.Inventory.Items.FirstOrDefault(i =>
                ItemDefinitionManager.Get(i.ItemDefinitionId)?.Abilities.Any(a => a.AbilityId == attempt.AbilityId) == true
                && (i.Container == Inventory.Inventory.EquippedContainer || IsConsumable(i)));
            if (source == null)
            {
                // Weapon combo abilities aren't listed on items: with a weapon held, the cast is a melee swing.
                // Shift's Heroic Movement (326) is cast with a weapon in hand too: it is not an attack.
                if (HoldsWeapon(session) && attempt.AbilityId != HeroicMovementAbility && !BootAbilities.IsBootAbility(attempt.AbilityId))
                {
                    // Which ids are the basic / power attack isn't decoded yet (the weapon set picks them); logged so a
                    // real fight shows them (goals 1273 basic attack, 1274 power attack).
                    // Ranged weapons aim with the reticle: the cast's point is the player's own position and the creature
                    // under the reticle comes in the tail (or as the player's target), so strike that first.
                    ulong target = TargetGuid(attempt, session);
                    var ray = attempt.AimRay(session.Position);
                    if (Grief.EquipmentLocked(session))
                    {
                        // Grief equipment lock: the client itself fires no weapon then (Grief.EquipmentLocked).
                        log.Info($"{session.CharacterName} weapon cast {attempt.AbilityId}: refused, grief level {Grief.LevelOf(session)}.");
                        Refuse(session, attempt);
                        return;
                    }
                    if (!WeaponAttacks.PayEnergy(session, attempt.AbilityId))
                    {
                        log.Debug($"{session.CharacterName} weapon cast {attempt.AbilityId}: not enough energy.");
                        Refuse(session, attempt);
                        return;
                    }
                    uint damage = AttackDamage(session, attempt.AbilityId);
                    AttackGoal(session, attempt.AbilityId);
                    var facing = ray is var (o, d) ? o + d * 10f : new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ);
                    int stage = WeaponAttacks.NextStage(session, attempt.AbilityId);
                    WeaponAttacks.Animation? swing = WeaponAttacks.AttackAnimation(HeldWeapon(session), attempt.AbilityId, stage);
                    if (swing is { } a)
                        Emotes.ShowWeaponAttack(session, a.Enter, a.Release, a.ReleaseMs, facing);
                    else
                        Emotes.ShowAttack(session, facing);
                    var aimPoint = new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ);
                    // A charged attack ("Charge a powerful …"): the client charges until the button is let go, then
                    // sends FinishChargedCast; the attack and the cast's answer wait for that (an answer now would end
                    // the charge: CastSuccess clears the client's charging state, FUN_142d7a370).
                    if (Combat.WeaponCombat.BeginCharge(session, attempt, HeldWeapon(session), damage, swing, facing))
                        return;
                    // Site games and encounters (players as targets) first; then the held weapon's own attack
                    // (Combat.WeaponCombat: its shape, affinity properties and armour effects); a creature under the
                    // reticle that the attack's shape missed still takes a plain hit - unless the swing waits for the
                    // client's Sweep targeter (WeaponCombat.Targeters, L170), which is then the one authority for its hits.
                    Combat.WeaponCombat.CheckWeapon(session);
                    // A blocking stance (Molten Rebuke, Corrupting Revenge) hits nothing when raised: only Use runs for it
                    // (it puts the Block state on), and its false is no miss, so neither a duel / site-game opponent nor
                    // the targeted creature takes a plain hit (before, raising the block with a creature targeted dealt it a
                    // Power Attack's damage from up to 30 m; bughunt-combat.md B2). A dead player's cast hits nothing.
                    bool noHit = Combat.WeaponCombat.IsBlock(HeldWeapon(session), attempt.AbilityId) || Health.IsDead(session);
                    bool hit = false;
                    try
                    {
                        hit = (!noHit && InstancedGames.Hit(session, target, ray, aimPoint, damage, CombatRange))
                            || (!noHit && Encounters.Hit(session, target, ray, aimPoint, damage, CombatRange))
                            || Combat.WeaponCombat.Use(session, attempt, HeldWeapon(session), attempt.AbilityId, damage)
                            || (!noHit && target != 0 && target != session.PlayerGuid && !Combat.WeaponCombat.AwaitingSweep(session, attempt)
                                && Creatures.Strike(session, target, damage, CombatRange));
                    }
                    catch (Exception e)
                    {
                        // An attack rule that throws must not cost the cast its answer: the client waits for every cast it
                        // sent and refuses all further ones until it hears back (stuck "casting"; bughunt-combat.md B1).
                        log.Error(e, $"{session.CharacterName} weapon cast {attempt.AbilityId}: the attack failed; the cast is still answered.");
                    }
                    log.Info($"{session.CharacterName} weapon cast {attempt.AbilityId} ({WeaponAttacks.KindOf(attempt.AbilityId)} stage {stage}, {damage}, animation {swing?.Enter ?? 0}/{swing?.Release ?? 0}) at 0x{target:X}: {(hit ? "hit" : Combat.WeaponCombat.AwaitingSweep(session, attempt) ? "waiting for the client's sweep" : "missed")}.");
                    Answer(session, attempt, animation: swing);
                    return;
                }
                if (attempt.AbilityId == HeroicMovementAbility)
                {
                    // Heroic movement (sprint): the movement itself is the client's. With the movement effects on it is
                    // a channelled cast that lasts while Shift is held (MovementEffects); otherwise the cast just needs
                    // its answer.
                    if (!MovementEffects.HandleCast(session, attempt))
                        Answer(session, attempt);
                    return;
                }
                // A boot's movement ability (Dash Soles' directions, Roll Wraps' rolls, ...: the boot's input group,
                // BootAbilities): like 326, the movement is the client's; answered while the boots are worn.
                if (BootAbilities.BootOf(attempt.AbilityId) is { } boot && session.Inventory.Items.Any(i =>
                        i.ItemDefinitionId == boot.ItemId && i.Container == Inventory.Inventory.EquippedContainer))
                {
                    // MovementEffects: Energy and cooldown, the channels (held until the key is let go), the charged
                    // leaps (answered at release: a CastSuccess now would end the charge) and the instant ones.
                    if (!MovementEffects.HandleCast(session, attempt))
                        Answer(session, attempt);
                    return;
                }
                // Still answer: the client waits for every cast it sent ("cast pending") and refuses all further
                // casts as "on cooldown" until it hears back (e.g. a sickle swing sent just as the tool changed).
                log.Debug($"{session.CharacterName} cast ability {attempt.AbilityId} without an item that gives it.");
                Refuse(session, attempt);
                return;
            }

            // Combat gear (weapons in the hands, class armour, accessories): its abilities are attacks on the target,
            // or a melee swing at the aim point when nothing is targeted. Ability effects aren't in the client data
            // (AbilityEx.txt is empty), so every gear ability deals the base weapon damage for now.
            if (IsCombatGear(source))
            {
                ulong target = TargetGuid(attempt, session);
                var aim = new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ);
                if (Grief.EquipmentLocked(session))
                {
                    log.Info($"{session.CharacterName} gear ability {attempt.AbilityId}: refused, grief level {Grief.LevelOf(session)}.");
                    Refuse(session, attempt);
                    return;
                }
                if (!WeaponAttacks.PayEnergy(session, attempt.AbilityId))
                {
                    Refuse(session, attempt);
                    return;
                }
                uint damage = AttackDamage(session, attempt.AbilityId);
                Emotes.ShowAttack(session, attempt.AimRay(session.Position) is var (ao, ad) ? ao + ad * 10f : aim);
                bool hit = InstancedGames.Hit(session, target, attempt.AimRay(session.Position), aim, damage, CombatRange)
                    || Encounters.Hit(session, target, attempt.AimRay(session.Position), aim, damage, CombatRange)
                    || (target != 0 && target != session.PlayerGuid && Creatures.Strike(session, target, damage, CombatRange))
                    || (attempt.AimRay(session.Position) is var (origin, dir) && Creatures.Shoot(session, origin, dir, damage, CombatRange))
                    || Creatures.Swing(session, aim, damage);
                log.Debug($"{session.CharacterName} gear ability {attempt.AbilityId} ({source.ItemDefinitionId}): {(hit ? "hit" : "missed")}.");
                Answer(session, attempt);
                return;
            }

            // Usable items with an effect of their own (potions, gadgets, beacons): Consumables decides whether one is used up.
            if (Consumables.Handles(attempt.AbilityId))
            {
                // A refused use (full health, already active, nowhere to put it) answers CastFail, which also takes
                // back the hotbar cooldown the client started for it; a use answers CastSuccess and the cooldown runs.
                if (Consumables.TryUse(session, source, attempt.AbilityId,
                        attempt.RingPoint ?? new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ)))
                    Answer(session, attempt);
                else
                    Refuse(session, attempt);
                return;
            }

            // Harvest swings keep the tool's pace: the held-button client repeat sends casts far faster (about four a
            // second, two per swing), so swings that come sooner than the tool's interval are answered but do nothing.
            if (IsHarvestAbility(attempt.AbilityId) && TooSoon(session, source.ItemDefinitionId))
            {
                Answer(session, attempt);
                return;
            }

            bool used = !IsConsumable(source) || session.TakeItem(source.ItemDefinitionId, 1);

            if (used && IsPulverizer(attempt.AbilityId))
                Mine(session, source.ItemDefinitionId, attempt.RingPoint ?? new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ), collect: false);
            // Others see the swing (their clients don't animate someone else's tool use by themselves).
            var swingAt = new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ);
            if (used && (MiningAbilities.Contains(attempt.AbilityId) || IsPulverizer(attempt.AbilityId)))
                Emotes.ShowSwing(session, 226, swingAt);
            else if (used && LoggingAbilities.Contains(attempt.AbilityId))
                Emotes.ShowSwing(session, 254, swingAt);
            else if (used && ReapingAbilities.Contains(attempt.AbilityId))
                Emotes.ShowSwing(session, 256, swingAt);

            if (used && MiningAbilities.Contains(attempt.AbilityId))
                Mine(session, source.ItemDefinitionId, attempt.RingPoint ?? new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ));
            // For axes and sickles the cast's target point is the player's own position; where the swing (the sickle's
            // cone) is aimed comes as the ring point, so a node is looked for there.
            var aimedAt = attempt.RingPoint ?? new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ);
            if (used && LoggingAbilities.Contains(attempt.AbilityId))
                Gathering.Chop(session, TargetGuid(attempt, session), aimedAt, source.ItemDefinitionId);   // the axe's own perks (P4131)
            if (used && ReapingAbilities.Contains(attempt.AbilityId))
                Gathering.Reap(session, TargetGuid(attempt, session), aimedAt, source.ItemDefinitionId);
            if (used && ExtractorAbilities.Contains(attempt.AbilityId))
            {
                ulong node = TargetGuid(attempt, session);
                var at = new System.Numerics.Vector3(attempt.TargetX, attempt.TargetY, attempt.TargetZ);
                bool gathered = Gathering.IsNode(node) && (Gathering.Chop(session, node, at, source.ItemDefinitionId)
                    || Gathering.Reap(session, node, at, source.ItemDefinitionId));
                if (!gathered)
                    Mine(session, source.ItemDefinitionId, attempt.RingPoint ?? at);
                Emotes.ShowSwing(session, 226, at);
            }

            Answer(session, attempt, swung: used && IsHarvestAbility(attempt.AbilityId));
        }

        /// <summary>
        /// Answers a cast: AbilityCastSuccess now, then AbilityEnd once the cast time is over. The end clears the
        /// client's current ability (FUN_142d798a0 → +0x2ac) and its pending cast; without it the client never casts
        /// again while the button is held (holding a pick or axe swung once).
        /// </summary>
        /// <summary>
        /// The swing animation a harvest cast confirms (CastSuccess +0x3c → FUN_142d5dfa0 → FUN_143cd5e90 plays an
        /// animation selection on the caster: the same ids PlayAnim uses, mining 226, logging 254, reaping 256; 245, the
        /// CasterInfo AnimationId, played nothing). It animates the player's own character without turning it, unlike a
        /// PlayAnim with a facing point (which pulled the moving player and camera towards each swing).
        /// </summary>
        private static uint SwingAnimation(uint ability)
            => LoggingAbilities.Contains(ability) ? 254u : ReapingAbilities.Contains(ability) ? 256u : 226u;

        /// <summary>Ends a charged cast: one CastSuccess carrying the attack's Release (the strike), or none.</summary>
        public static void AnswerCharged(WorldSession session, AbilityCastAttempt attempt, uint release)
        {
            session.EnqueueMessage(new AbilityCastSuccess
            {
                CasterGuid = session.PlayerGuid,
                CastGuid = attempt.CastGuid,
                CastHash = attempt.CastHash,
                AbilityId = attempt.AbilityId,
                AnimationId = release
            });
        }

        /// <summary>
        /// A refused cast (not enough Energy, no item that gives the ability, a consumable that can't be used now):
        /// CastFail, the client's own answer for it (see <see cref="AbilityCastFail"/>) — it ends the cast and gives the
        /// hotbar slot its cooldown back.
        /// </summary>
        public static void Refuse(WorldSession session, AbilityCastAttempt attempt)
        {
            log.Info($"{session.CharacterName}: cast {attempt.AbilityId} refused (CastFail).");
            session.EnqueueMessage(new AbilityCastFail
            {
                CasterGuid = session.PlayerGuid,
                CastGuid = attempt.CastGuid,
                CastHash = attempt.CastHash,
                AbilityId = attempt.AbilityId
            });
        }

        public static void Answer(WorldSession session, AbilityCastAttempt attempt, bool swung = false,
            WeaponAttacks.Animation? animation = null)
        {
            session.EnqueueMessage(new AbilityCastSuccess
            {
                CasterGuid = session.PlayerGuid,
                CastGuid = attempt.CastGuid,
                CastHash = attempt.CastHash,
                AbilityId = attempt.AbilityId,
                AnimationId = animation?.Enter ?? (swung ? SwingAnimation(attempt.AbilityId) : 0)
            });
            ulong caster = session.PlayerGuid, castGuid = attempt.CastGuid;
            uint castHash = attempt.CastHash, ability = attempt.AbilityId;
            // A weapon attack's Enter is followed by its Release (the strike) on the caster's own client too. Not as a
            // second "own cast" success: with Force (+0x48) the client's own-cast branch (FUN_142d7a370) played
            // ActiveTimeStart 509 again and scheduled another ActiveTimeEnd 510, whose STOPS_CURRENT_SELECTION cut the
            // strike short (measured 2026-10-14, re/test/scenarios/adhoc/castanim). Without it only the selection plays.
            if (animation is { Release: not 0 } weaponSwing)
                System.Threading.Tasks.Task.Delay(weaponSwing.ReleaseMs).ContinueWith(_ =>
                {
                    if (!session.Disconnected)
                        session.EnqueueMessage(new AbilityCastSuccess
                        {
                            CasterGuid = caster, CastGuid = castGuid, CastHash = castHash, AbilityId = ability,
                            AnimationId = weaponSwing.Release, Force = false
                        });
                });
            // The swing is an Enter/Release pair (MODLOG: pick 226/227, axe 254/255, sickle 256/257); the confirm
            // carries the Enter, a second confirm 350 ms later the Release (without it the character froze in the
            // wind-up until the next swing).
            if (swung)
                System.Threading.Tasks.Task.Delay(350).ContinueWith(_ =>
                {
                    if (!session.Disconnected)
                        session.EnqueueMessage(new AbilityCastSuccess
                        {
                            CasterGuid = caster, CastGuid = castGuid, CastHash = castHash, AbilityId = ability,
                            AnimationId = SwingAnimation(ability) + 1
                        });
                });
            System.Threading.Tasks.Task.Delay(AbilityEndDelay).ContinueWith(_ =>
            {
                if (!session.Disconnected)
                    SendAbilityEnd(session, castGuid, castHash, ability);
            });
        }

        /// <summary>
        /// AbilityEnd (Abilities 0x27 sub 5; 0.589 reader FUN_142d62d00, 0.695 reader FUN_142e00d20 / handler
        /// FUN_142e17870): header, u64 cast guid, u32 cast hash, u32 ability, u64 caster. The client clears its current
        /// ability (+0x2ac) when it is this one.
        /// </summary>
        public static void SendAbilityEnd(WorldSession session, ulong castGuid, uint castHash, uint ability)
        {
            var output = new List<byte>();
            var writer = new LandmarkEmulator.Shared.Network.GamePacketWriter(output);
            writer.Write((byte)5);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(castGuid);
            writer.Write(castHash);
            writer.Write(ability);
            writer.Write(session.PlayerGuid);
            session.EnqueueMessage(ZoneMessageOpcode.AbilityAbilityEnd, output.ToArray());
        }

        /// <summary>When the ability ends after its success: the tool casts' cast time.</summary>
        private static readonly TimeSpan AbilityEndDelay = TimeSpan.FromSeconds(0.75);

        /// <summary>
        /// Travel window casts: 186 (picking an entry under My Sites), 185 (Spire) and 187 (a friend). Answered with the cast success;
        /// 186 then teleports to the player's build site.
        /// </summary>
        public static readonly HashSet<uint> TravelAbilities = new() { 185, 186, 187 };

        /// <summary>The ability Shift casts (input action HeroicMovement) and its name text ("Heroic Movement").</summary>
        public const uint HeroicMovementAbility = 326, HeroicMovementName = 573238;
        private static readonly TimeSpan TravelCastTime = TimeSpan.FromSeconds(4);

        /// <summary>
        /// Work due later on the world thread: the travel casts' moves once their cast animation is over. Until 2026-10-18
        /// these were Task.Delay continuations, so Teleport.ToZone / Spire.TravelTo / CrossWorld.TravelToFriend ran on a
        /// thread-pool thread and changed the session's re-zone state, the streams and PlayerManager's views while the world
        /// thread used them (bughunt-combat.md B7; MovementEffects moved its timers to the tick for the same reason).
        /// </summary>
        private static readonly List<(DateTime Due, Action Do)> timers = new();

        private static void Later(TimeSpan delay, Action action)
        {
            lock (timers)
                timers.Add((DateTime.UtcNow + delay, action));
        }

        /// <summary>World tick (HostedService "ability timers"): runs the timers that are due.</summary>
        public static void Update()
        {
            List<Action> due;
            DateTime now = DateTime.UtcNow;
            lock (timers)
            {
                if (timers.Count == 0)
                    return;
                due = timers.Where(t => t.Due <= now).Select(t => t.Do).ToList();
                if (due.Count == 0)
                    return;
                timers.RemoveAll(t => t.Due <= now);
            }
            foreach (Action action in due)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    log.Error(e, "Ability timer failed.");
                }
            }
        }

        /// <summary>
        /// Travel window casts. Answers the cast, then, once the 4-second cast animation is over (the client ignores a
        /// teleport while it plays), moves the player: 186 "My Sites" to their build site, 185 "Spire" to the Spire.
        /// The cast only carries the caster's own position, so the destination is decided here.
        /// </summary>
        private static void Travel(WorldSession session, AbilityCastAttempt attempt)
        {
            if (attempt.AbilityId == 187 && FriendTeleportCooldownLeft(session) is TimeSpan left)
            {
                // On cooldown (owner setting, off as live): CastFail ends the cast and gives the slot back.
                Refuse(session, attempt);
                string format = Shared.GameTable.Text.TextManager.Instance.GetTextForId(FriendTeleportCooldownText)
                    ?? "This ability is on cooldown for another %d minutes and %d seconds.";
                Chat.System(session, ReplaceFirst(ReplaceFirst(format, "%d", ((int)left.TotalMinutes).ToString()), "%d", left.Seconds.ToString()));
                return;
            }
            Answer(session, attempt);
            // The cast carries the caster's own position, not the destination (no destination packet is sent):
            // 186 from "My Sites" goes to the player's build site.
            if (attempt.AbilityId == 186 && Claims.ArrivalOf(session.PlayerGuid) is (System.Numerics.Vector3 site, uint siteZone, ulong siteId))
            {
                var ground = Terrain.TerrainHeights.OnGround(site.X, site.Z, siteZone) ?? site;
                StoryTools.ExpectArrival(session, siteId, respawn: false);
                // The client ignores a teleport while its travel cast is still playing, so wait for it to end.
                Later(TravelCastTime, () =>
                {
                    if (!session.Disconnected)
                        Teleport.ToZone(session, ground + new System.Numerics.Vector3(0, 1, 0), siteZone, Claims.ArrivalFacingOf(session.PlayerGuid));
                });
                log.Info($"{session.CharacterName} travels to their build site {ground}.");
                return;
            }
            if (attempt.AbilityId == 187)
            {
                // Travel to a friend (StringHashToValue Teleport.FriendAbilityId = 187; the client's Lua TeleportToFriend
                // casts it at the friend). The friend's guid should be the cast's target; failing that, any online
                // player's guid found in the cast tail.
                ulong friendGuid = TargetGuid(attempt, session);
                WorldSession friend = PlayerManager.FindByGuid(friendGuid) ?? FindPlayerIn(attempt.Tail, session);
                if (friend == null && friendGuid != 0 && friendGuid != session.PlayerGuid)
                {
                    // Not on this world: ask the social service where they are (its presence lookup waits up to a second,
                    // so off the tick), then travel to their world beside them (TASKS E.13, features audit #63).
                    System.Threading.Tasks.Task.Run(() =>
                    {
                        Social.SocialPresence where = Social.SocialServices.Presence.Find(friendGuid);
                        // Offline: the client's own "[player] is not online at this." with the friend's name (L175; the
                        // name lookup stays off the world thread).
                        string offline = where == null ? TargetNotOnline.NameOf(friendGuid) : null;
                        // The travel itself on the world thread (Later), as every other travel cast's.
                        Later(TravelCastTime, () =>
                        {
                            if (session.Disconnected)
                                return;
                            if (where == null)
                            {
                                if (offline == null || !TargetNotOnline.Tell(session, friendGuid, offline))
                                    Chat.System(session, "Your friend isn't online.");
                            }
                            else if (!CrossWorld.TravelToFriend(session, where.WorldId, where.ZoneId, friendGuid))
                                Chat.System(session, "Your friend is on a world you can't travel to from here.");
                            else
                            {
                                StartFriendTeleportCooldown(session);
                                log.Info($"{session.CharacterName} travels to {where.Name} on world {where.WorldId}.");
                            }
                        });
                    });
                    return;
                }
                if (friend == null || friend == session)
                {
                    Chat.System(session, "Your friend isn't online.");
                    return;
                }
                Later(TravelCastTime, () =>
                {
                    if (session.Disconnected || friend.Disconnected)
                        return;
                    // Beside the friend, never below the ground there (a stale or underground friend position would
                    // otherwise leave the traveller under the terrain).
                    var at = friend.Position + new System.Numerics.Vector3(1.5f, 0f, 1.5f);
                    if (Terrain.TerrainHeights.HeightAt(at.X, at.Z, friend.ZoneId) is float ground && ground > at.Y)
                        at.Y = ground;
                    Teleport.ToZone(session, at + new System.Numerics.Vector3(0f, 1f, 0f), friend.ZoneId);
                });
                StartFriendTeleportCooldown(session);
                log.Info($"{session.CharacterName} travels to {friend.CharacterName}.");
                return;
            }
            if (attempt.AbilityId == 185)
            {
                Later(TravelCastTime, () =>
                {
                    if (!session.Disconnected)
                        Spire.TravelTo(session);
                });
                return;
            }
            log.Debug($"{session.CharacterName} travel cast {attempt.AbilityId} has no destination yet.");
        }

        /// <summary>
        /// Teleport to Friend's cooldown (trace/0695/audit-wiki-gaps.md #15). Live's history [P developer notes]: 2 May
        /// 2014: the friend teleport keeps its 30 minute cooldown (design/forum/patches/2014-05-02-34889); 17 Sep 2014:
        /// it shows an error while on cooldown (2014-09-16-48275); 5 May 2015 (the big wipe): the cooldown for
        /// teleporting to a friend from the contact list is gone (2015-05-05-54485), and no later note brings it back.
        /// So the 2016 game had none: the setting
        /// FriendTeleportCooldownSeconds (Docker FRIEND_TELEPORT_COOLDOWN_SECONDS) is 0 by default; 1800 = the 2014 alpha's
        /// 30 minutes. The wiki's "cool down period" (Chests page) describes the 2014 game.
        /// <para>
        /// The client refuses by itself [P, 0.695]: Lua Ui.TeleportToFriend (FUN_1435c91f0) reads Teleport.FriendAbilityId
        /// (StringHashToValue 0xD84CF278 = 187) and computes cooldown − elapsed: the cooldown is the ability record's +0x14c
        /// seconds (FUN_142e173d0; less the entity's +0x4a74 share when record +0x150 is set), the elapsed time the player's
        /// timer keyed by record +0x7c (FUN_142e16440 → FUN_142fcd170, player +0x75420; a timer never started counts as
        /// the full cooldown). Above 0 it shows AbilityCastError.OnCooldown = 562080 "This ability is on cooldown for
        /// another %d minutes and %d seconds." itself (FUN_142b24220 with secs / 60, secs % 60) and does not cast.
        /// The record comes from 187's caster info (store FUN_142e187a0: reply +0x70 → record +0x14c, +0xa0 → +0x7c,
        /// bool +0xa4 → +0x80, bool +0xca → +0x150), and the timer starts when a CastSuccess for the player's own cast
        /// arrives while record +0x80 is set (FUN_142e18340 → FUN_142fd1cb0). So the cooldown goes into 187's caster info
        /// (MiscHandler.CapturedCasterInfo: +0x70 = <see cref="FriendTeleportCooldownSeconds"/>; the body's +0xa0 = 187 and
        /// +0xa4 = 1 already). Before L182 it carried the 1098 capture's 60 s, a client-side cooldown live 2016 did not
        /// have. The server keeps its own check as the fallback: CastFail (the client ends the cast and hands the slot
        /// back) and the same text as a system line. Stat 53 cStatTeleportCooldown has no client reader [P negative,
        /// agent-leadsC.md L182]. Per character, kept in memory (a restart clears it).
        /// </para>
        /// </summary>
        private const uint FriendTeleportCooldownText = 562080;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, DateTime> friendTeleportUsed = new();

        /// <summary>Travel to a friend: StringHashToValue Teleport.FriendAbilityId (0xD84CF278).</summary>
        public const uint FriendTeleportAbility = 187;

        /// <summary>The setting FriendTeleportCooldownSeconds (0 = none, live 2016); also sent in 187's caster info (+0x70).</summary>
        public static int FriendTeleportCooldownSeconds => Math.Max(0,
            Shared.Configuration.ConfigurationManager<WorldServerConfiguration>.Instance.Config?.FriendTeleportCooldownSeconds ?? 0);

        private static TimeSpan FriendTeleportCooldown => TimeSpan.FromSeconds(FriendTeleportCooldownSeconds);

        /// <summary>Time left on the player's Teleport to Friend cooldown; null when they may teleport.</summary>
        private static TimeSpan? FriendTeleportCooldownLeft(WorldSession session)
        {
            TimeSpan cooldown = FriendTeleportCooldown;
            if (cooldown <= TimeSpan.Zero || !friendTeleportUsed.TryGetValue(session.PlayerGuid, out DateTime used))
                return null;
            TimeSpan left = used + cooldown - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : null;
        }

        private static void StartFriendTeleportCooldown(WorldSession session)
        {
            if (FriendTeleportCooldown > TimeSpan.Zero)
                friendTeleportUsed[session.PlayerGuid] = DateTime.UtcNow;
        }

        private static string ReplaceFirst(string text, string what, string with)
        {
            int at = text.IndexOf(what, StringComparison.Ordinal);
            return at < 0 ? text : text.Substring(0, at) + with + text.Substring(at + what.Length);
        }

        /// <summary>The first online player (other than the caster) whose guid appears anywhere in a cast's tail.</summary>
        /// <summary>
        /// The arrival of Travel to Friend from another world (WorldSession's transfer re-zone, TASKS E.13): beside the
        /// friend if they are on this world, as the same-world travel places them. False when they aren't here (the
        /// traveller then gets the landscape's usual arrival).
        /// </summary>
        public static bool ArriveBesideFriend(WorldSession session, ulong friendGuid)
        {
            if (PlayerManager.FindByGuid(friendGuid) is not WorldSession friend || friend.Disconnected)
                return false;
            var at = friend.Position + new System.Numerics.Vector3(1.5f, 0f, 1.5f);
            if (Terrain.TerrainHeights.HeightAt(at.X, at.Z, friend.ZoneId) is float ground && ground > at.Y)
                at.Y = ground;
            Teleport.ToZone(session, at + new System.Numerics.Vector3(0f, 1f, 0f), friend.ZoneId);
            log.Info($"{session.CharacterName} arrives from another world beside {friend.CharacterName}.");
            return true;
        }

        private static WorldSession FindPlayerIn(byte[] tail, WorldSession caster)
        {
            if (tail == null)
                return null;
            for (int i = 0; i + 8 <= tail.Length; i++)
            {
                ulong value = BitConverter.ToUInt64(tail, i);
                if (value != 0 && value != caster.PlayerGuid && PlayerManager.FindByGuid(value) is WorldSession found)
                    return found;
            }
            return null;
        }

        /// <summary>Melee damage per hit (the Damage stat's default, 70).</summary>
        private const uint WeaponDamage = 70;

        /// <summary>Getting Started goals for the first basic / power attack.</summary>
        private static void AttackGoal(WorldSession session, uint ability)
        {
            switch (WeaponAttacks.KindOf(ability))
            {
                case WeaponAttacks.Kind.Basic or WeaponAttacks.Kind.BasicCombo:
                    Achievements.Progress(session, Achievements.BasicAttackUsed);
                    break;
                case WeaponAttacks.Kind.Power or WeaponAttacks.Kind.PowerCombo:
                    Achievements.Progress(session, Achievements.PowerAttackUsed);
                    break;
            }
        }

        /// <summary>
        /// A weapon cast's damage: the base weapon damage × the attack's multiplier, which includes the held weapon's own
        /// Basic / Power factor from the 26 April and 25 May 2016 balance passes (<see cref="WeaponAttacks.Balance"/>,
        /// TASKS 6.9).
        /// </summary>
        private static uint AttackDamage(WorldSession session, uint ability)
            => (uint)Math.Round(DamageOf(session) * WeaponAttacks.DamageMultiplier(HeldWeapon(session), ability));

        /// <summary>The player's base weapon damage (revenge damage, shield bashes and bursts are shares of it).</summary>
        public static uint WeaponDamageFor(WorldSession session) => DamageOf(session);

        /// <summary>
        /// Base weapon damage: 70 (the Damage stat's default) × the worn armour's damage factor (light ×1.30, medium
        /// ×1.20, heavy ×1.10, none ×1; Combat.GearStats, wiki "Armor Weight"), halved while the wielded weapon is at
        /// 0 % condition (<see cref="Durability"/>). Every player hit (weapon kits, PvP, bursts, revenge) scales from this.
        /// </summary>
        private static uint DamageOf(WorldSession session)
            => (uint)Math.Round(WeaponDamage * Combat.GearStats.DamageFactor(session) * WeaponCondition(session)
                * MovementEffects.DamageFactor(session));

        /// <summary>
        /// The wielded weapon's condition factor on its damage: 0.5 at 0 % condition, else 1 (<see cref="Durability.Strength"/>).
        /// </summary>
        public static float WeaponCondition(WorldSession session)
        {
            var weapon = session.Inventory.Items.FirstOrDefault(i => i.Container == Inventory.Inventory.EquippedContainer
                && ItemDefinitionManager.Get(i.ItemDefinitionId) is { DurabilitySet: 2 });
            return Durability.Strength(weapon);
        }

        /// <summary>Is the player wielding something that isn't a gathering tool (pick, axe, sickle)?</summary>
        private static bool HoldsWeapon(WorldSession session) => HeldWeapon(session) != null;

        /// <summary>
        /// The held item if it is a weapon: in the main hand (equipment slot 2) and not a gathering tool. Many weapons
        /// list no abilities of their own (their attacks come from the input table), so the slot decides; chest armour
        /// and accessories carry combat abilities too (1062 …) but are never the weapon.
        /// </summary>
        public static ItemDefinitionManager.ItemDefinition HeldWeapon(WorldSession session)
            => session.Inventory.Items.Where(i => i.Container == Inventory.Inventory.EquippedContainer)
                .Select(i => ItemDefinitionManager.Get(i.ItemDefinitionId))
                .Where(d => d != null && d.EquipSlotId == MainHandSlot && !d.Abilities.Any(a => IsHarvestAbility(a.AbilityId))
                    && (d.Abilities.Count > 0 || WeaponAttacks.ClassOf(d) != 0))
                .FirstOrDefault();

        /// <summary>EquipmentSlotDefinitions: the main hand.</summary>
        private const uint MainHandSlot = 2;

        /// <summary>The entity under the crosshair when the cast was made (cast tail +36), or 0.</summary>
        /// <summary>
        /// The entity a tool swing is aimed at: the guid in the cast (axes carry it), else the target the client set
        /// for the player (TargetBase RequestSetTarget; a sickle cast carries none but the plant under the cursor is
        /// targeted).
        /// </summary>
        private static ulong TargetGuid(AbilityCastAttempt attempt, WorldSession session)
        {
            ulong inCast = attempt.Tail is { Length: >= 44 } tail ? BitConverter.ToUInt64(tail, 36) : 0;
            return inCast != 0 ? inCast : session.CurrentTarget;
        }

        /// <summary>Primary swing of every pick (Adventurer's/Charmed/... 1018, Hewer's 136 → Overseer's 142, Pickaxes 169).</summary>
        public static readonly HashSet<uint> MiningAbilities = new() { 1018, 136, 138, 140, 142, 169 };

        /// <summary>Primary swing of every axe (Adventurer's/Charmed/Goliath/... 1020, the crafted axes 176).</summary>
        public static readonly HashSet<uint> LoggingAbilities = new() { 1020, 176 };

        /// <summary>Primary swing of every sickle (Weaver's/Adventurer's 153, Gatherer's 155, Alchemist's 156, Shaman's 157).</summary>
        public static readonly HashSet<uint> ReapingAbilities = new() { 153, 155, 156, 157 };

        /// <summary>
        /// NovaTech Extractor / Holoseeker swings (1036 primary, 1037): "rapidly harvests trees as well as dirt, stone,
        /// ore, and gems": a tree or plant under the crosshair is chopped or reaped, anything else is dug.
        /// </summary>
        public static readonly HashSet<uint> ExtractorAbilities = new() { 1036, 1037 };

        /// <summary>
        /// The player's calculated stats: the defaults plus the held tool's harvesting stats and the worn armour's life
        /// and damage. The client sizes the mining ring from cStatTypeHarvestingArea (stat 3, metres; FUN_142dbf9c0 →
        /// ActionStateMining SetParam 0), so a pick needs a non-zero area for the ring to show. Stat 1 (text "Life",
        /// hidden from the UI) carries the maximum life and stat 10 (Damage) 70 × the armour factor: the client's attack
        /// parameters multiply stat 10 (AbilityParameterDefinitions StatMultiplier, ParameterCalculator FUN_1435079e0),
        /// so its attack numbers follow the armour as the server's damage does (Combat.GearStats).
        /// </summary>
        public static IEnumerable<(uint StatId, float Value)> CalculatedStats(WorldSession session)
        {
            var held = session.Inventory.Items.FirstOrDefault(i => i.Container == Inventory.Inventory.EquippedContainer
                && ItemDefinitionManager.Get(i.ItemDefinitionId)?.Abilities.Any(a => MiningAbilities.Contains(a.AbilityId)
                    || IsPulverizer(a.AbilityId) || ExtractorAbilities.Contains(a.AbilityId)) == true);
            double radius = held != null ? DigRadius(held.ItemDefinitionId) : 0;
            // Swing pace comes from whichever gathering tool is held (pick, axe, sickle…).
            var tool = session.Inventory.Items.FirstOrDefault(i => i.Container == Inventory.Inventory.EquippedContainer
                && ItemDefinitionManager.Get(i.ItemDefinitionId)?.Abilities.Any(a => IsHarvestAbility(a.AbilityId)) == true);
            float speed = tool != null ? StatsOf(tool.ItemDefinitionId).Speed : DefaultTool.Speed;
            int tier = held == null ? 0 : (int)Math.Round((radius - 10) + 1);
            foreach ((uint id, float value) in DefaultCalculatedStats.Player)
                yield return (id, Buffs.Apply(session, id, Consumables.Apply(session, id, BaseStat(id, value))));

            // The stat before buffs: defaults, plus the held tool's harvesting stats.
            float BaseStat(uint id, float value) => id switch
                {
                    // Harvesting area is a volume: the client's ring radius = 0.233 × area^(1/3) (measured: 1.2→0.248,
                    // 4.8→0.394, 9.6→0.496), so send the area whose ring matches the dig radius (m).
                    3 when held != null => (float)Math.Pow(radius / 10 * HarvestAreaScale / 0.233, 3),
                    5 when AdminCommands.RunSpeedOf(session) is float adminSpeed => adminSpeed,   // admin /runspeed

                    11 when held != null => tier,                      // mining harvest tier
                    12 when held != null => HarvestDamagePerSwing,     // harvesting damage
                    13 => speed,                                       // harvest speed: held-button swing interval = 50 / speed s (FUN_142d77f20)
                    Combat.GearStats.LifeStat => Combat.GearStats.MaxHealth(session),     // maximum life (900 + armour)
                    Combat.GearStats.DamageStatId => Combat.GearStats.DamageStat(session), // 70 × armour damage factor
                    _ => value
                };
        }

        /// <summary>
        /// Mining ring size = dig radius × this (dev "ringscale"). 1 matches the dig; at 2 the ring was too wide to show
        /// on steep ground (it is projected straight down with a limited depth).
        /// </summary>
        public static float HarvestAreaScale = 1f;

        /// <summary>Health a voxel loses per pick swing, percent.</summary>
        public const float HarvestDamagePerSwing = 34f;

        /// <summary>
        /// A gathering tool's stats: dig radius in voxel-sized steps (decimetres; picks, pulverizers, extractors) and
        /// harvest speed (stat 13: held-button swing interval = 50 / speed s, so 50 = one swing a second).
        /// The client data has no tool stats (the live game rolled them per crafted tool on the server), so these follow
        /// what each tool is known for (archived wiki): the Adventurer's gear is the plain starter, Whirlwind / Hotfoot
        /// swing faster, Goliath tools strike wider but slower, the crafted lines get better by tier.
        /// </summary>
        private sealed record ToolStats(double Radius, float Speed);

        private static readonly ToolStats DefaultTool = new(10, 50);

        private static readonly Dictionary<uint, ToolStats> toolStats = new()
        {
            // Picks
            [3253] = new(10, 50),                         // Adventurer's
            [3254] = new(12, 50),                         // Charmed (finds ore more often)
            [3255] = new(15, 40),                         // Goliath: wide, slow
            [3256] = new(11, 80),                         // Whirlwind: fast
            [3257] = new(13, 50),                         // Farseer (shows veins)
            [1919] = new(11, 55),                         // Hewer's
            [1920] = new(12, 55), [2569] = new(12, 60),   // Miner's, Explorer's
            [1921] = new(13, 60), [2570] = new(13, 65),   // Foreman's, Trailblazer's
            [1922] = new(14, 65),                         // Overseer's
            [2568] = new(11, 55),                         // Settler's
            // Pulverizers dig wide and collect nothing
            [212] = new(18, 50), [214] = new(22, 55), [216] = new(28, 60), [218] = new(34, 65),
            // NovaTech Extractor, Holoseeker, Harvestron (more per swing, Gathering.ToolYieldBonus), Velocitizer (a
            // speed burst after use, Gathering.AfterUse)
            [3702] = new(14, 60), [3704] = new(14, 60), [3703] = new(14, 60), [3705] = new(14, 60),
            // Axes
            [3259] = new(10, 50),                         // Adventurer's
            [1925] = new(10, 55), [1927] = new(10, 60), [1926] = new(10, 65), [1928] = new(10, 70),
            [3263] = new(10, 80),                         // Hotfoot: fast
            [3261] = new(10, 40),                         // Goliath
            [3260] = new(10, 50),                         // Charmed
        };

        private static ToolStats StatsOf(uint tool) => toolStats.GetValueOrDefault(tool, DefaultTool);

        /// <summary>
        /// A gathering tool's stats as its item record shows them (ItemRandom stat list, see Inventory.FillDefinition):
        /// harvesting area (stat 3), harvest speed (13), harvest damage (12). The live Adventurer's Pick and Axe showed
        /// 100 / 50 / 80; area scales with the dig radius (10 dm = 100), speed is the tool's, damage the live 80. Null
        /// for items that aren't in the tool table.
        /// </summary>
        public static (float Area, float Speed, float Damage)? ToolDisplayStats(uint item)
            => toolStats.TryGetValue(item, out ToolStats t) ? ((float)(t.Radius * 10), t.Speed, 80f) : null;

        /// <summary>When each player's last harvest swing landed.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<WorldSession, DateTime> lastSwing = new();

        /// <summary>
        /// A player left (Combat.WeaponCombat.Forget, from PlayerManager.Leave): their harvest pace goes. It was never
        /// removed, so every session that swung a tool once stayed in memory with its inventory (bughunt-combat.md B9).
        /// </summary>
        public static void Forget(WorldSession session) => lastSwing.TryRemove(session, out _);

        /// <summary>
        /// Is this swing sooner than the tool allows? Interval = 50 / harvest speed seconds (the tool's speed raised by
        /// buffs such as the Potion of Nimble Harvesting), with a little slack for network jitter. Records the swing
        /// when it's allowed.
        /// </summary>
        private static bool TooSoon(WorldSession session, uint tool)
        {
            float speed = Buffs.Apply(session, 13, StatsOf(tool).Speed);
            var interval = TimeSpan.FromSeconds(50.0 / Math.Max(1f, speed) * 0.85);
            DateTime now = DateTime.UtcNow;
            if (lastSwing.TryGetValue(session, out DateTime last) && now - last < interval)
                return true;
            lastSwing[session] = now;
            return false;
        }

        /// <summary>The gathering tool the player holds (main hand), or 0.</summary>
        private static uint HeldToolOf(WorldSession session)
            => session.Inventory.Items.Where(i => i.Container == Inventory.Inventory.EquippedContainer)
                .Select(i => ItemDefinitionManager.Get(i.ItemDefinitionId))
                .FirstOrDefault(d => d != null && d.Abilities.Any(a => IsHarvestAbility(a.AbilityId)))?.Id ?? 0;

        /// <summary>Dig radius of a pick (decimetres).</summary>
        private static double DigRadius(uint pickItem) => StatsOf(pickItem).Radius;

        /// <summary>The Charmed Pick: "chance to find recipes, special resources": ore turns up more often.</summary>
        private static double OreChance(uint pickItem) => pickItem == 3254 ? 0.5 : 0.35;


        /// <summary>
        /// A pick swing: carve a sphere of air at the target, push the changed cells to everyone and give the player
        /// what was dug: the biome's surface materials (Terrain.TerrainMaterials), Stone when deep, one item per four
        /// voxels removed.
        /// </summary>
        public static void Mine(WorldSession session, uint pickItem, System.Numerics.Vector3 target, bool collect = true)
            => Excavate(session, target, DigRadius(pickItem), collect, digOn: true, ToolReach(session) + ReachSlack, OreChance(pickItem));

        /// <summary>
        /// Digs a sphere out of the terrain for a player (picks, pulverizers, dynamite): checks reach and build-site
        /// rights, carves <paramref name="radius"/> (decimetres) at the target, pushes the changed cells to everyone and,
        /// when <paramref name="collect"/>, gives what was dug (raised by the Harvest Bonus stat) with chunks flying to
        /// the player. <paramref name="digOn"/>: a swing into air digs on along the aim (held-button mining).
        /// <para>
        /// <paramref name="explosive"/> (dynamite): a temporary destruction tool (0.695, TASKS 2.1 / 2.6;
        /// <see cref="VoxelDestruction"/>). It works on a build site in Play Mode unless the site's Play Mode Rules (bit 4)
        /// or a Linking Area (row 102) block it there, carving the owner's building too (in the destruction layer, so it
        /// comes back when it heals) and collecting nothing on the site; refused at the target it answers EffectEnd
        /// result 9 (8 on a site in Edit Mode, inferred). It heals after <paramref name="healDelay"/> (dynamite 100 s,
        /// P6037). Every other tool stays a gathering tool: refused on every site and kept out of sites at their edges.
        /// </para>
        /// Returns the voxels removed, or -1 when a site refused the destruction (the client was answered already).
        /// </summary>
        public static int Excavate(WorldSession session, System.Numerics.Vector3 target, double radius, bool collect, bool digOn, float reach,
            double oreChance = 0.35, bool explosive = false, TimeSpan? healDelay = null)
        {
            uint zone = session.ZoneId;
            if (System.Numerics.Vector3.Distance(target, session.Position) > reach)
            {
                log.Debug($"{session.CharacterName} swung at terrain {target} too far away.");
                return 0;
            }
            var global = Terrain.TerrainEdits.ToGlobal(target);
            if (explosive)
            {
                // Temporary destruction: decided at the target by the site's mode, Play Mode Rules and Linking Areas.
                VoxelDestruction.Verdict verdict = VoxelDestruction.At(zone, global.X, global.Y, global.Z);
                if (verdict is VoxelDestruction.Verdict.EditMode or VoxelDestruction.Verdict.Blocked)
                {
                    VoxelDestruction.Refuse(session, verdict);
                    return -1;
                }
                // "This change does not impact the ability to harvest resources on your Site. (Still not possible.)"
                if (verdict == VoxelDestruction.Verdict.Allowed)
                    collect = false;
            }
            // No gathering inside any build site, the player's own included (wiki Gathering: nothing can be gathered
            // inside any build site's boundary; the pick's ring turns red there). Building
            // tools change site terrain instead. The live server's text: Harvest.Fail.InClaim (562568).
            else if (Claims.IsInsideAny(session.ZoneId, global.X, global.Y, global.Z))
            {
                log.Info($"{session.CharacterName} tried to mine inside a build site.");
                // The client's own on-screen harvest failure, as for every refused harvest: "You cannot harvest here."
                Gathering.HarvestFail(session, Gathering.HarvestNotAllowed);
                return 0;
            }
            // Not beside the Portal Spire (P4425; text 560944).
            if (!Spire.MayEdit(session.PlayerGuid, zone, (global.X - radius, global.Y - radius, global.Z - radius),
                    (global.X + radius, global.Y + radius, global.Z + radius)))
                return 0;
            // Which voxels of the sphere may go (global dm voxel centres): dynamite stops where a site does not allow
            // destruction; gathering tools stop at every site boundary. Null: no site near, no filter. The dig-on steps
            // below move the (local, metre) target up to 3 × radius × 0.7 m with radius in dm, so the box (dm) covers
            // 3 × 0.7 × 10 = 21 radii beyond the sphere.
            double span = radius * (digOn ? 22.1 : 1.1);
            var near = (global.X - span, global.Y - span, global.Z - span);
            var far = (global.X + span, global.Y + span, global.Z + span);
            Terrain.VoxelFilter allowed = explosive ? VoxelDestruction.MaskFor(zone, near, far)
                : VoxelDestruction.OutsideSites(zone, near, far);
            uint expected = Terrain.TerrainMaterials.YieldAt(target, session.Position, random, zone);
            // Hole walls in the local biome's material for what is dug (its sand, dirt or rock); the forest set
            // where the biome tables have none.
            ushort shell = Terrain.TerrainMaterials.WallMaterialAt(target, expected, zone)
                ?? (ushort)(expected switch { 46 => 289, 45 => 287, _ => 297 });
            var oreMined = new Dictionary<ushort, int>();
            var oreCells = new HashSet<ulong>();
            Terrain.IEnvironmentServer env = Terrain.EnvironmentServer.Instance;
            (List<ulong> cells, int voxels) = env.Sphere(target, radius, 0, zone, oreMined, oreCells, shell, allowed, explosive, healDelay);
            // A swing right after the last one still aims at the old surface (the client hasn't re-cast its ring onto
            // the new hole yet): dig on along the swing instead of hitting air. Every swing must change the terrain,
            // because the client only re-checks a held mouse button when its cursor target changes.
            var along = System.Numerics.Vector3.Normalize(target - (session.Position + new System.Numerics.Vector3(0, 1.6f, 0)));
            for (int step = 1; digOn && voxels == 0 && step <= 3 && !float.IsNaN(along.X); step++)
            {
                target += along * (float)(radius * 0.7);
                (cells, voxels) = env.Sphere(target, radius, 0, zone, oreMined, oreCells, shell, allowed, explosive, healDelay);
            }
            if (cells.Count == 0 || voxels == 0)
                return 0;
            Terrain.ZoneTerrain.Broadcast(cells, zone);
            // Digging into a Chaos Cavern outpost: its creatures' navigation gets the holes (Pathing.Dug, OPEN-LEADS L133).
            Pathing.Dug(zone, cells);
            if (!collect)
            {
                log.Debug($"{session.CharacterName} pulverized {voxels} voxels in {cells.Count} cell(s).");
                return voxels;
            }
            // One item per voxel dug, the rate building costs (BuildMaterials.VoxelsPerItem, the client's own count).
            // Harvest Bonus (stat 17, e.g. Potion of Minor Yield +10): that many percent more; the Harvestron more again.
            uint yield = (uint)Math.Max(1, voxels / Terrain.BuildMaterials.VoxelsPerItem * (1f + Buffs.Apply(session, 17, 0f) / 100f)
                * Gathering.ToolYieldBonus(HeldToolOf(session)) * Durability.ToolStrength(session));   // a broken pick: half (6.53, as axes)
            uint item = expected;
            // Ore: the vein voxels the swing dug out (OreVeins), one item per VoxelsPerOre voxels (at least one), and
            // the rest of the swing's yield in the ground's own material.
            int oreVoxels = oreMined.Values.Sum();
            if (oreVoxels > 0)
            {
                env.DisturbVeins(oreCells, zone);
                foreach (var byItem in oreMined.GroupBy(o => env.OreItemFor(o.Key)).Where(g => g.Key != 0))
                {
                    int n = byItem.Sum(o => o.Value);
                    uint oreYield = (uint)Math.Max(1, n / Terrain.OreVeins.VoxelsPerOre * (1f + Buffs.Apply(session, 17, 0f) / 100f));
                    Gathering.GiveHarvest(session, byItem.Key, oreYield);
                    Achievements.Harvested(session, byItem.Key, oreYield);
                    item = byItem.Key;
                }
                yield = (uint)Math.Max(0, (voxels - oreVoxels) / 4);
            }
            if (yield > 0)
            {
                Gathering.GiveHarvest(session, expected, yield);
                Achievements.Harvested(session, expected, yield);
            }
            if (collect)
            {
                Gathering.RollDrops(session, rich: oreVoxels > 0, HeldToolOf(session));
                Gathering.AfterUse(session, HeldToolOf(session));
            }
            // The resource chunks that pop out of the hole and fly into the player (one per few items, at most 3).
            uint chunks = Math.Clamp(yield / 5, 1, 3);
            for (int i = 0; i < chunks; i++)
                LootPinatas.SpawnChunk(session, target + new System.Numerics.Vector3(0, 0.5f, 0), item, Math.Max(1, yield / chunks));
            log.Debug($"{session.CharacterName} dug {voxels} voxels in {cells.Count} cell(s) "
                + $"({Terrain.TerrainMaterials.BiomeName(Terrain.TerrainMaterials.BiomeAt(target, zone))}) for {yield} × item {expected}"
                + (oreVoxels > 0 ? $" and ore {string.Join(", ", oreMined.Select(o => $"{o.Value} voxels of {o.Key} (item {env.OreItemFor(o.Key)})"))}." : "."));
            return voxels;
        }

        private static readonly Random random = new();

        /// <summary>How far gear abilities reach a targeted creature, metres.</summary>
        private const float CombatRange = 30f;

        /// <summary>Equipped gear whose abilities are combat ones: not a gathering tool, not a grappling hook or glider.</summary>
        private static bool IsCombatGear(CharacterItemModel item)
            => item.Container == Inventory.Inventory.EquippedContainer
               && ItemDefinitionManager.Get(item.ItemDefinitionId) is { } d && d.EquipSlotId is 2 or 3 or 8
               && !d.Abilities.Any(a => IsHarvestAbility(a.AbilityId));

        /// <summary>A gathering tool's swing (pick, axe, sickle, pulverizer, extractor), not a combat ability.</summary>
        public static bool IsHarvestAbility(uint ability)
            => MiningAbilities.Contains(ability) || LoggingAbilities.Contains(ability) || ReapingAbilities.Contains(ability)
               || IsPulverizer(ability) || ExtractorAbilities.Contains(ability);

        /// <summary>Pulverizers' drill abilities (Mechanized 162, Powered 164, Laser-Assisted 166, Atomic 168).</summary>
        public static bool IsPulverizer(uint ability) => ability is 162 or 164 or 166 or 168;

        /// <summary>
        /// Items used up by their ability: anything that isn't equipment.
        /// </summary>
        private static bool IsConsumable(CharacterItemModel item)
            => ItemDefinitionManager.Get(item.ItemDefinitionId) is { EquipSlotId: 0 };
    }
}
