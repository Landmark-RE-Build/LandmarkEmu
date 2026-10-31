# PlayerUpdate 0x26 subs (PlayerUpdate_Dispatch FUN_142c45fa0; case value + 1 = sub; names from ctor vtables, re/tools/ctorname.py)

> 0.695 rebase (2026-10-26): addresses below are 0.589 unless marked 0.695; their 0.695 partners and changes: trace/0695/address-map-589-695.md, trace/0695/rebase-695.md

| Sub | Message | Case functions |
|---|---|---|
| 0x1 | PlayerUpdatePacketRemovePlayerGracefully | FUN_142bb3650 FUN_142bce9c0 FUN_142bb36c0 |
| 0x2 | PlayerUpdatePacketKnockback | FUN_142bcdb60 FUN_142bb2920 |
| 0x3 | ? | FUN_142bb4450 |
| 0x5 | PlayerUpdatePacketAddNotifications | FUN_142bcd5b0 FUN_142bb1bf0 FUN_142c125a0 |
| 0x6 | PlayerUpdatePacketRemoveNotifications | FUN_142bce8d0 FUN_142bb3270 FUN_142c124a0 |
| 0x7 | PlayerUpdatePacketNpcRelevance | FUN_142bce710 FUN_142bb2db0 FUN_142c12490 |
| 0x8 | ? | FUN_142bb4770 |
| 0x9 | ? | FUN_142bb4970 |
| 0xA | ? | FUN_142bb3730 |
| 0xB | PlayerUpdatePacketPlayCompositeEffect | FUN_142bce750 FUN_142bb2f10 FUN_142be4330 |
| 0xC | PlayerUpdatePacketStopCompositeEffect | FUN_142bcf030 FUN_142bb4220 |
| 0xD | PlayerUpdatePacketSetLookAt | FUN_142bcedc0 FUN_142bb3cc0 |
| 0xE | PlayerUpdatePacketRenamePlayer | FUN_142bcea60 FUN_142bb37f0 FUN_142be3530 |
| 0xF | PlayerUpdatePacketUpdateCharacterState | FUN_142bcf180 FUN_142bb6320 |
| 0x11 | ? | FUN_142bb25d0 |
| 0x13 | PlayerUpdatePacketThoughtBubble | FUN_142a871b0 FUN_142bcf110 FUN_142bb6240 |
| 0x14 | PlayerUpdatePacketSetDisposition | FUN_142a871b0 FUN_142bced30 FUN_142bb6160 |
| 0x15 | PlayerUpdatePacketLootEvent | FUN_142a871b0 FUN_142bce550 FUN_142bb5ec0 |
| 0x16 | ? | FUN_142bb61d0 |
| 0x17 | PlayerUpdatePacketFreeze | FUN_142bcda60 FUN_142c36620 FUN_142bb5da0 |
| 0x19 | PlayerUpdateTriggerEffectPackagePacket | FUN_142bcf510 FUN_142bb6400 FUN_142c339f0 |
| 0x1A | PlayerUpdatePacketPreferredLanguages | FUN_142bce800 FUN_142bb3060 FUN_142a871b0 |
| 0x1B | PlayerUpdatePacketCustomizationChange | FUN_142bcd720 FUN_142bb1f10 FUN_142be3b00 |
| 0x1C | ? | FUN_142bb2ff0 |
| 0x1D | PlayerUpdatePacketAddEffectTagCompositeEffect | FUN_142bccfc0 FUN_142bb1950 FUN_142a871b0 |
| 0x1E | PlayerUpdatePacketRemoveEffectTagCompositeEffect | FUN_142bce880 FUN_142bb3140 FUN_142a871b0 |
| 0x1F | PlayerUpdatePacketEffectTagCompositeEffectsEnable | FUN_142bcd9b0 FUN_142bb2520 FUN_142c36620 |
| 0x21 | PlayerUpdatePacketCustomizeNpc | FUN_142bcd7c0 FUN_142bb2040 FUN_142a871b0 |
| 0x22 | ? | FUN_142bb3e50 |
| 0x23 | PlayerUpdatePacketSetComboState | FUN_142bceca0 FUN_142bb3b00 FUN_142a871b0 |
| 0x24 | PlayerUpdatePacketSetSurpriseState | FUN_142bceea0 FUN_142bb3f30 FUN_142a871b0 |
| 0x25 | PlayerUpdatePacketRemoveNpcCustomization | FUN_142bce910 FUN_142bb3440 FUN_142a871b0 |
| 0x26 | PlayerUpdatePacketReplaceBaseModel | FUN_142bcead0 FUN_142bb38d0 FUN_142a871b0 |
| 0x27 | PlayerUpdatePacketSetCollidable | FUN_142bcec70 FUN_142bb3a90 FUN_142a871b0 |
| 0x28 | PlayerUpdatePacketUpdateOwner | FUN_142bcf2a0 FUN_142bb44c0 FUN_142a871b0 |
| 0x29 | PlayerUpdatePacketUpdateTintAlias | FUN_142bcf480 FUN_142bb49e0 FUN_142a871b0 |
| 0x2A | PlayerUpdatePacketMoveOnRail | FUN_142bce690 FUN_142bb5fa0 FUN_142c31d30 |
| 0x2B | PlayerUpdatePacketClearMovementRail | FUN_142a871b0 FUN_142bcd690 FUN_142bb5b50 |
| 0x2C | PlayerUpdatePacketMoveOnRelativeRail | FUN_142bce6d0 FUN_142bb6010 FUN_142c31d40 |
| 0x2D | PlayerUpdatePacketDestroyed | FUN_142bcd870 FUN_142bb5c10 FUN_142c349d0 |
| 0x2E | PlayerUpdatePacketSeekTarget | FUN_142bcebb0 FUN_142bb6080 FUN_142c349e0 |
| 0x2F | PlayerUpdatePacketSeekTargetUpdate | FUN_142bcec00 FUN_142bb60f0 FUN_142c349f0 |
| 0x31 | PlayerUpdateLaunchProjectilePacket | FUN_142bccdd0 FUN_142bb5ae0 FUN_142c38250 |
| 0x33 | HudMessagePacket | FUN_142bc8770 FUN_142bb5770 FUN_142a883b0 |
| 0x34 | PlayerUpdatePacketCustomizationData | FUN_142bcd770 FUN_142bb1f80 FUN_143abf5f0 |
| 0x35 | PlayerMemberStatusUpdatePacket | FUN_142bccc90 FUN_142bb5a00 FUN_142c37df0 |
| 0x36 | PlayerUpdatePacketSetCurrentAdventure | FUN_142bcecd0 FUN_142bb3b70 FUN_142be45a0 |
| 0x37 | ? | FUN_142bb4010 |
| 0x38 | ? | FUN_142bb4290 |
| 0x39 | PlayerUpdatePacketKnockedOut | FUN_142bcdba0 FUN_142bb2990 FUN_142be3da0 |
| 0x3A | PlayerUpdatePacketKnockedOutDamageReport | FUN_142bcdcb0 FUN_142bb2a00 FUN_142be3de0 |
| 0x3C | PlayerUpdatePacketRespawnReply | FUN_142bceb80 FUN_142bb39b0 |
| 0x3E | PlayerUpdatePacketActivateProfile | FUN_142bccf10 FUN_142bb18e0 FUN_142be3760 |
| 0x3F | PlayerUpdatePacketSetSpotted | FUN_142bcee60 FUN_142bb3ec0 FUN_142be46d0 |
| 0x40 | PlayerUpdatePacketJet | FUN_142a871b0 FUN_142bcdb30 FUN_142bb5e50 |
| 0x41 | PlayerUpdatePacketTurbo | FUN_142a871b0 FUN_142bcf150 FUN_142bb62b0 |
| 0x42 | PlayerUpdatePacketStartRevive | FUN_142bcefc0 FUN_142bb40f0 |
| 0x43 | PlayerUpdatePacketStopRevive | FUN_142bcf0d0 FUN_142bb4370 |
| 0x44 | PlayerUpdatePacketReadyToRevive | FUN_142bce830 FUN_142bb30d0 |
| 0x45 | PlayerUpdatePacketSetFactionId | FUN_142bc1230 FUN_142a871b0 FUN_142bced90 |
| 0x46 | PlayerUpdatePacketSetBattleRank | FUN_142a871b0 FUN_142bcec40 FUN_142bb3a20 |
| 0x47 | PlayerUpdatePacketStartHeal | FUN_142bcef80 FUN_142bb4080 |
| 0x48 | PlayerUpdatePacketStopHeal | FUN_142bcf090 FUN_142bb4300 |
| 0x49 | PacketPlayerCurrencyChange | FUN_142a871b0 FUN_142bcc170 FUN_142bb0d50 |
| 0x4A | PacketPlayerRewardCurrency | FUN_142a871b0 FUN_142bcc220 FUN_142bb0f60 |
| 0x4B | PlayerInGamePurchaseResult (named by REA, 2026-10-13) | FUN_142bccaf0 FUN_142bb1790 |
| 0x4D | PlayerUpdatePacketManagedObject | FUN_142c43990 |
| 0x4E | PlayerUpdatePacketMaterialTypeOverride | FUN_142bce5e0 FUN_142bb5f30 FUN_142c31d20 |
| 0x4F | PlayerUpdateDebrisLaunchPacket | FUN_142bccd90 FUN_142bb5a70 FUN_142c31d00 |
| 0x50 | ? | FUN_142bb2740 |
| 0x51 | PlayerUpdatePacketUpdateCharacterStateDelta | FUN_142bcf1b0 FUN_142bb6390 |
| 0x52 | ? | FUN_142ab5fa0 FUN_142f34460 |
| 0x53 | PlayerUpdatePacketUpdateStat | FUN_142bcf410 FUN_142bb47e0 FUN_142be4970 |
| 0x54 | PlayerUpdatePacketAnimationRequest | FUN_142bcd5f0 FUN_142bb1d60 |
| 0x55 | PlayerUpdateNonPriorityTargetCharacters (named by REA, 2026-10-13) | FUN_142bccea0 FUN_142bb1800 FUN_142be36e0 |
| 0x56 | PlayerUpdatePacketPlayWorldCompositeEffect | FUN_142bce7a0 FUN_142bb2f80 FUN_142bc1600 |
| 0x57 | PlayerUpdatePacketAFK | FUN_142bccee0 FUN_142bb1870 |
| 0x58 | PlayerUpdatePacketAddLightweightPc | FUN_142bcd2d0 FUN_142b8d680 FUN_142bcfe10 |
| 0x59 | PlayerUpdatePacketAddLightweightNpc | FUN_142bcd080 FUN_142b8cfc0 FUN_142c43fe0 |
| 0x5A | PlayerUpdatePacketAddLightweightVehicle | FUN_142bcd4d0 FUN_142b8dd20 FUN_142c44df0 |
| 0x5B | ? | FUN_142b8fb40 FUN_142b88bc0 FUN_142b93270 |
| 0x5C | PlayerUpdatePacketLightweightToFullNpc | FUN_142bcdf40 FUN_142b8f230 FUN_142c45940 |
| 0x5D | PlayerUpdatePacketLightweightToFullVehicle | FUN_142bce2b0 FUN_142b8fc70 FUN_142c45aa0 |
| 0x5F | PacketPlayerInitiateNameChange | FUN_142a871b0 FUN_142bcc1c0 FUN_142bb0e30 |
| 0x60 | PacketPlayerNameChangeResult | FUN_142a871b0 FUN_142bcc1f0 FUN_142bb0ef0 |
| 0x62 | PlayerUpdatePacketAddLightweightProp | FUN_142bcd3f0 FUN_142b8dc00 FUN_142c44700 |
| 0x63 | PlayerUpdatePacketLightweightToFullProp | FUN_142bce240 FUN_142bb2ae0 FUN_142be4080 |
| 0x64 | PlayerUpdatePacketSourceGeometryOffset | FUN_142bcef00 FUN_142bb3fa0 |
| 0x65 | ? | FUN_142bb3940 |
| 0x66 | ? | FUN_142bb0000 |
| 0x67 | ? | FUN_142bb1ea0 |
| 0x72 | ? | FUN_142b90140 FUN_142c45c90 |
| 0x74 | PlayerUpdatePacketAddFeatherweightObject | FUN_142bcd010 FUN_142b8cd60 FUN_142c43c50 |
| 0x75 | ? | FUN_142bb31b0 |
| 0x76 | PlayerUpdatePacketFlashTemplateInformation | FUN_142bcda00 FUN_142bb5d30 FUN_142c45380 |
| 0x78 | PlayerUpdatePacketDoCombatNotification | FUN_142bcd940 FUN_142bb5c80 FUN_142c30fa0 |
| 0x79 | PlayerUpdatePacketDisplayPropText | FUN_142bcd8f0 FUN_142bb23d0 FUN_142c30fa0 |
| 0x7A | PlayerUpdatePacketSetPropTint | FUN_142bcede0 FUN_142bb3d30 FUN_142c30fa0 |
| 0x7C | PlayerUpdatePacketUpdatePropName | FUN_142bcf390 FUN_142bb4650 FUN_142be4910 |
| 0x7D | PlayerUpdatePacketUpdateEditProperties | FUN_142bcf1f0 FUN_142bb43e0 FUN_142c38260 |
| 0x7E | PlayerUpdatePacketDebugAILineOfSight | FUN_142bcd820 FUN_142bb2250 FUN_142b1ee30 |
| 0x7F | ? | FUN_142b1ee30 FUN_142c2ab90 FUN_142b69870 |
