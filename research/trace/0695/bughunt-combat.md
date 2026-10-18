# Bug hunt: combat, abilities, health, creatures, gathering, goals (2026-10-18)

Branch `local/bugcombat` (worktree `<workspace>\wt-bugcombat`), from `client-0.695` at 9280c50. Found by reading the
code paths; packet layouts were not changed. Line numbers are as of the last commit (4d362c3). Whole solution builds,
0 errors, no new warnings.

Read: Combat/* (WeaponCombat, Targeters, BlockStance, Statuses, Gear, CreatureAbilities), WeaponAttacks, Abilities,
MovementEffects, Buffs, Consumables, Health, Creatures (all of it), Encounters, Grief, Gathering + Gathering.Respawn,
OreVeins, Achievements (progress / harvest / kill paths), DailyGoals, Pets, LootPinatas, Outposts (elite / crystal / chest),
HitEffects, InstancedGames.Hit.

## Fixed (ranked by severity)

| # | Sev | Where | Scenario | Fix |
|---|-----|-------|----------|-----|
| B1 | High | Combat/WeaponCombat.cs:352 (Cascade After), Abilities.cs:176 ff | Wand of Boundless Light's Cascade kills everything it hits. `s.Hits.Select(PositionOfGuid).OfType<Vector3>().First()` throws, because PositionOfGuid is null for the dead. The exception leaves Abilities.HandleCast before `Answer()`. The client never gets CastSuccess and refuses every further cast until relog (stuck "casting"). | db00270: fall back to the aimed point; the weapon cast's hit resolution is in try/catch, so the cast is always answered |
| B11 | Med-High | HitEffects.cs:63 (AffinityOf) | The weapon affinity for the Daily "<Affinity> Wielder" goals, "Kills with a(n) <Element> weapon" and the impact effects was guessed from words in the weapon's name. Checked against the item records (description 574424…574442; dumped with the built ItemDefinitionManager), 10 of the 26 launch weapons were wrong or empty. Falchion of the Inner Spark ("Spark") counted as Fire. Blight-Warden's Pain ("Light" in "Blight") counted as Radiance. Lightbringer's Justice ("Ice" in "Justice") counted as Water. The three Earth weapons, Spear of Piercing Judgment, Desperado's, Frenzied Slayer's and Katana of Crashing Waves had none. No Earth weapon could advance Earth Wielder. | 4d362c3: WeaponCombat.AffinityOf (kit / description) first, Earth = "Physical"; the word guess only as a fallback |
| B4 | Medium | Creatures.cs:231 (RegionKey), :3313 (SpawnStory) | ViewersOf, RemoveFromRegion and DevKillNear worked out a creature's region from `Home`. A story walk moves `Home` to the Travel Point. Once that point is in another 128 m region, three things go wrong. (1) The creature is never removed on death or despawn and stays as a dead ghost in its old region: FindStory still finds it, and it holds one of the 56 story guid indices. (2) Movement, HP and animations go to the wrong viewers. (3) With the indices used up, SpawnStory's `.First()` throws. | 262f66e: `Creature.RegionKey` is fixed at creation and used by those three; SpawnStory returns 0 (logged) when the region is full |
| B2 | Medium | Abilities.cs:176 | Molten Rebuke and Corrupting Revenge (Shape.Block) make `WeaponCombat.Use` return false. HandleCast read that as a miss and fell back to `Creatures.Strike(target)`, so raising the block with a creature targeted dealt it Power Attack damage from up to 30 m. In a duel or site game, the opponent within 3.5 m also took a hit. | ee186a5: `WeaponCombat.IsBlock`; a block cast (or a dead player's cast) runs only Use |
| B3 | Medium | Achievements.cs:605 | Harvested counted the mapped objective by id (1130-1133, 1138-1142, 1148-1150) and then again by text, `"<item name> harvested"`. Stone counted twice for 1133 "Stone harvested", Plain Wood twice for 1148, and so on: the goals ran at double speed. | c8a6823: `ProgressByText(..., except:)` skips the objective already counted by id |
| B10 | Medium | Creatures.cs:551 (Update), Combat/Statuses.cs Update / Tick, Combat/WeaponCombat.cs area loop | Creature AI steps and Burn/Rot ticks run story Links synchronously (CreatureCombat / CreatureArrived / CreatureDied). A Link that throws ended the whole loop. Every later creature froze for that tick, and BlockStance / WeaponCombat timers (channel hits, sweeps, lingering areas) were skipped. This repeated on every tick for as long as the cause remained. | 3767147: each creature step, state tick or end, and lingering area runs in its own try/catch (creature failures are logged at most once every 30 s with a running count) |
| B7 | Med-Low | Consumables.cs (SummonRepairBot / UpdateRepairBots :234), Abilities.cs:442 (Travel) | Pool-thread mutations: (1) the Repair Bot's 5-minute `Task.Delay → Props.Despawn` ran Props.Remove (prop lists, streams, StoryTools, PropSettings, BuildModeHolster) on a pool thread; (2) the travel casts' 4 s `Task.Delay → Teleport.ToZone / Spire.TravelTo / CrossWorld.TravelToFriend` changed re-zone state and views on a pool thread. Both raced the world thread. | 3470abe: bots despawn in the gadget tick. 23b648c: Abilities timer list run by a new "ability timers" world-tick step (HostedService) |
| B5 | Low-Med | Creatures.cs:1836 (Respawn) | A revenge armour (Frost Lord's Guard, Exo-Suit, Poncho, Plate of Vengeance, Embodiment of Stone) hits back at an exploding fungus while it is still alive. That re-lights its fuse, so it dies with `FuseAt = now`. Respawn didn't reset `FuseAt`, so 3 minutes later the fungus blew up on its first step, with ownerless loot for the "trigger". | 7ec6db3: Respawn clears FuseAt, Dud and Pending |
| B6 | Low-Med | Encounters.cs:453 (Answer) | A challenge stays open for 1 minute. Scenario: A challenges B, C challenges A, A accepts C, then B accepts A. Start moved A out of the A–C duel without ending it, leaving C alone in a "running" duel. Nothing ends it (the range check needs two), and every new challenge is refused "already in an encounter". | dcc0d01: Answer re-checks that neither player is in an encounter and that both are still online |
| B9 | Low | WeaponCombat.cs:1686, Abilities.Forget, Gathering.Forget, Consumables.Update | Maps keyed by WorldSession were never cleared: leftHandNext (Six-Shooters), Abilities.lastSwing (every tool user), Gathering.lastWrongTool, Consumables.flying. Each kept a whole session (inventory, queues) alive after logout, growing with every login. | a12dbbf |
| B8 | Low | Creatures.cs:1544 (StatusDamage) | A Burn or Rot from a player who has logged out keeps ticking with the gone session as the attacker. That session got aggro, combat text, kill credit and goals, and a loot bag. Story "dies" Links also ran with it as the triggering player. | ce4e8a0: a disconnected source counts as no attacker (damage still lands) |

## Checked, not changed

- **Chain bonus hits the struck target too** (WeaponCombat.Chain: Arcing Strike 0.5, Debilitating Shiv 0.5, Brilliance 1.0):
  every second Brilliance hit deals the primary target its damage again. The texts ("chains the damage to all nearby
  opponents", 574465) read as *other* opponents [I], but this is a design / balance choice, so it is left for the
  coordinator.
- **Creatures.Find rebuilds the whole guid index on every miss.** Misses come from player or pet guids in
  FullCharacterDataRequest (Creatures.SendFull is the last in the chain), from InSweep for player targets, and from
  lookups of dead creatures. This costs performance only; results are correct. A cheap fix is to return null for a guid
  whose top byte isn't 0x50 / 0x51.
- **A player's combat states are not cleared at death** (Block, Immune, Illuminate, Harden run out on their own,
  ≤ 10 s). Live is unknown, and the effect is minor.
- **Potion cooldowns (Buffs) and movement-ability cooldowns are per session**, so a relog resets them. This is the
  documented design ("a relog clears them"); live is unknown.
- **Channel ticks (WeaponCombat.Later)** check only for disconnect, death and running out of Energy. After a re-zone
  mid-channel, the remaining 1-4 hits land at the new position. Minor.
- **CloseSweep's fallback** Resolve doesn't check that the player is still alive. This needs death within the sweep window
  plus no client sweep. Very rare.
- **Encounters.Hit** shows the full damage number even when the duel-ending blow applies hp − 1. Cosmetic.
- These looked sound:
  - MovementEffects (charges, channels, cooldowns; already on tick timers)
  - Grief decay and suspension
  - Gathering min/max respawn pools (Felled / Install / SpawnElsewhere bookkeeping)
  - OreVeins disturbed-vein respawn (seconds, as RESPAWN_TIME_DIRTY)
  - LootPinatas ownership
  - Pets
  - Outposts elite / crystal / chest timers
  - BlockStance
  - WeaponAttacks energy (cost per run, Surge, Flow cap)
  - Health regen / revive / respawn

## For the coordinator

- **Settings added:** none. **Dev commands added:** none.
- **New world-tick step:** "ability timers" (`Game.Abilities.Update`, HostedService), which runs the travel casts' delayed
  moves.
- **In-game checks:**
  1. **B1:** wield the Wand of Boundless Light (3767). Power Attack (Cascade) a creature at low HP so the hit kills it.
     Then cast again. Expected: the next cast goes off (before the fix, every cast was refused until relog). Also check
     that the Exalt buff icon still appears.
  2. **B2:** wield the Flame Bound Bastion (3030). Target a creature (click it), stand 10-20 m away and right-click
     (Molten Rebuke). Expected: no damage number on the creature. Then let it hit you from the front: the block still
     catches and counters.
  3. **B3:** note "Stone harvested" / "Plain Wood harvested" in the Journal (This Stuff is EVERYWHERE / What About
     Teak?), dig some stone and chop a tree. Expected: the counter rises by the amount gathered, not twice that.
  4. **B11:** with the Falchion of the Inner Spark, kill a creature. Expected: Lightning Wielder counts if assigned,
     Fire Wielder doesn't. With the Gunslinger's Six-Shooters, Earth Wielder counts. Hit sparks on the Falchion show the
     lightning impact.
  5. **B4:** on a site in Play Mode, link a spawner's creature to a Travel Point on the other side of a creature-region
     boundary (local x or z crossing a multiple of 128). Let the creature walk there, kill it, then end Play Mode. Expected: the server log
     has no ghost creature; `@TestPlayer creatures` (Creatures.Describe, in the log) no longer lists it near the old spot.
  6. **B7:** use a Repair Bot Beacon; after 5 minutes the bot disappears. Travel to My Sites, the Spire and a friend
     works as before (4 s after the cast).
- **MODLOG / TESTING-NEEDED:** the 11 entries above.
- **OPEN-LEADS:** none new. The Chain target question is a design note, not a lead.
