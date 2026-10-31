# System: entities, movement, character state (PlayerUpdate 0x26, ClientUpdate 0x29, position updates)

> 0.695 rebase (2026-10-26): addresses below are 0.589 unless marked 0.695; their 0.695 partners and changes: trace/0695/address-map-589-695.md, trace/0695/rebase-695.md

Status: [~] PlayerUpdate mapped (trace/playerupdate_subs.md: sub → message, dispatcher PlayerUpdate_Dispatch
FUN_142c45fa0, case = sub − 1); per-message server status in families/0026_PlayerUpdate.md. ClientUpdate and
movement packets still to write up.

## PlayerUpdate server messages by purpose (✔ = server sends it today)

Entity lifecycle: 0x1 RemovePlayer / RemoveGracefully ✔, 0x58 AddLightweightPc ✔, 0x59 AddLightweightNpc ✔,
0x5A AddLightweightVehicle, 0x5B LightweightToFullPc ✔ (case 0x5B named "?" by ctor; it is the Pc one in the
catalogue), 0x5C LightweightToFullNpc, 0x5D LightweightToFullVehicle, 0x62 AddLightweightProp ✔, 0x63
LightweightToFullProp ✔, 0x74 / 0x75 Add / Remove FeatherweightObject (proxied props, ProxiedCharacterManager::
SpawnProxiedProp), 0x2D Destroyed, 0x50 HideCorpse, 0x26 ReplaceBaseModel, 0x8 UpdateScale, 0x64
SourceGeometryOffset, 0x27 SetCollidable, 0x28 UpdateOwner, 0x4D ManagedObject.

Movement / AI: 0x72 MobMovement, 0x2A / 0x2B / 0x2C rails (MoveOnRail, ClearMovementRail, MoveOnRelativeRail),
0x2E / 0x2F SeekTarget / SeekTargetUpdate, 0xD SetLookAt, 0x17 Freeze, 0x11 ExpectedSpeed, 0x2 Knockback,
0x40 Jet, 0x41 Turbo, 0x7 NpcRelevance, 0x14 SetDisposition, 0x24 SetSurpriseState, 0x3F SetSpotted,
0x13 ThoughtBubble, 0x7E DebugAILineOfSight.

Appearance: 0x9 UpdateTemporaryAppearance, 0x1B CustomizationChange, 0x34 CustomizationData, 0x21 CustomizeNpc,
0x25 RemoveNpcCustomization, 0x29 UpdateTintAlias, 0x4E MaterialTypeOverride, 0x16 SlotCompositeEffectOverride.

Effects / combat feedback: 0xB PlayCompositeEffect {u64 entity, u32 effect, u32, u32, bool×3}, 0xC Stop, 0x56
PlayWorldCompositeEffect, 0x19 TriggerEffectPackage, 0x1D / 0x1E add / remove effect-tag composite effect, 0x1F
EffectTagCompositeEffectsEnable ✔, 0x78 DoCombatNotification (floating combat text), 0x31 LaunchProjectile,
0x4F DebrisLaunch, 0x54 AnimationRequest, 0x65 RequestAnimationSelection (named in catalogue), 0x23 SetComboState.

Character state / stats: 0xF UpdateCharacterState (not sent; tried for revive, see gameplay.md), 0x51
UpdateCharacterStateDelta, 0x53 UpdateStat, 0x3 UpdateHitpoints ✔ (creatures), 0x67 CombatResourceEvent ✔,
0x45 SetFactionId, 0x46 SetBattleRank, 0x3E ActivateProfile, 0x35 PlayerMemberStatusUpdate, 0x57 AFK,
0x1C PlayerTitle ✔, 0xE RenamePlayer, 0x5F / 0x60 name change initiate / result, 0x68 UpdateSteamInfo.

Death / revive / heal: 0x39 KnockedOut ✔, 0x3A KnockedOutDamageReport, 0x3C RespawnReply, 0x42 / 0x43
Start / StopRevive, 0x44 ReadyToRevive ✔, 0x47 / 0x48 Start / StopHeal.

Gathering: 0x37 StartHarvest / 0x38 StopHarvest (casting bar for timed harvests; also EffectEnd 0x27/0xD harvest
results), 0x15 LootEvent.

Notifications / HUD: 0x5 AddNotifications / 0x6 RemoveNotifications (MiniMap:KillIndicator — map markers), 0x33
HudMessagePacket, 0x36 SetCurrentAdventure.

Currency: 0x49 PlayerCurrencyChange ✔, 0x4A PlayerRewardCurrency.

Props (story tools): 0x76 FlashTemplateInformation (design author / name / date, "BaseClient::ShowSystemMessage"),
0x79 DisplayPropText ("[%s] says: %s", HudStateManager:OnOpenTextDisplay), 0x7A SetPropTint, 0x7C UpdatePropName,
0x7D UpdateEditProperties.

Client → server PlayerUpdate (all handled): 0xA RemoveTemporaryAppearance, 0x18 RequestStripEffect, 0x1A
PreferredLanguages, 0x3B Respawn, 0x3D ReadyToReviveResponse, 0x4C SafeEject, 0x5E FullCharacterDataRequest,
0x69 InteractionInfoRequest, 0x6C ClientLoading, 0x71 BindingCustomization, 0x73 SetEnabled, 0x77 EmoteStringById.

## Gaps that matter for the live game (to confirm per system)

MobMovement 0x72 (creature movement — we use Position updates instead: compare), HideCorpse, DoCombatNotification,
PlayCompositeEffect family (hit effects, ambient weapon effects u17, level-up / loot effects), Knockback,
StartHarvest / StopHarvest, LootEvent, UpdateStat, AnimationRequest, AFK, notifications (map markers), RenamePlayer /
name change, prop text / tint / name updates for story tools, Destroyed, SetLookAt / SeekTarget for creature AI.

## ClientUpdate 0x29 (own-player updates; families/0029_ClientUpdate.md)

Sent today: 0x1 Hitpoints, 0x2 ItemAdd (item definitions cache, ItemDefinitions.log), 0x3 ItemUpdate, 0x4 ItemDelete,
0x6 UpdateStat, 0xC TeleportToLocation, 0x13 CoinCount, 0x1B DoneSendingPreloadCharacters, 0x1F ZonePopulation,
0x20 RespawnLocations, 0x26 MovementVersion, 0x2C UpdateCalculatedStats.
Never sent: 0x5 ItemDeleteFailed, 0x7-0xA Collection Start / Remove / AddEntry / RemoveEntry (collections system),
0xB UpdateLocation (teleport variant that waits for the location), 0xD Mana (CombatGroupHandler:SetPower — energy
bar), 0xF / 0x18 Add / UpdateProfileAbilitySetApl, 0x10 / 0x11 Add / RemoveEffectTag, 0x14 DeleteProfile, 0x15
ActivateProfile, 0x16 AddAbility, 0x17 NotifyPlayer, 0x1D PhaseChange (terrain / world phases — see
TerrainCalendarDefinitions), 0x1E DamageInfo, 0x21-0x23 ModifyMovementSpeed / TurnRate / StrafeSpeed (slows, speed
buffs), 0x24 UpdateManagedLocation, 0x25 ScreenEffect (ScreenEffects.txt), 0x27 ManagedMovementVersion, 0x28
UpdateWeaponAddClips, 0x2B LoyaltyPointsUpdate, 0x2E UpdateAttributes (AttributeDefinitions / AttributeEntryDefinitions).

## Client constants (trace/client-constants.md)
Movement: JumpHeight 1.5, StepHeight 0.3, WalkModeSpeed 2, SprintStrafeMultiplier 0.8, jump penalty (×0.9 per jump,
clears after 1.5 s, recovers 0.66 after 1000 ms), SnaredSpeed 3 / ×0.25. PlayerPhysics: capsule radius 0.4, stand
height 3.2, crouch 0.7, MaxSlope 70°, Traction 125. NPC default capsule 2.7 × 0.3. StatId.* = the stat ids for
movement / breath / currency caps (MaxHp 1, MaxMovementSpeed 2, Sprint 5, Swim 6, JumpHeight 38 …) — use these ids
when sending stats.
