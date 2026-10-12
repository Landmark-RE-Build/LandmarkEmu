# Systems worklist: every client system working as the live server ran it

Started 2026-10-12 from the user's instruction: all systems and functions, decoded, linked and behaving like the real
server, whether or not the 0.589 UI uses them. Source of truth per family: re/out/inventory.md (open items listed per
packet class; static, so check each against the server before building). Order: foundations first.

Status: [ ] open, [~] in progress, [x] done (decoded + implemented + verified with the probes).

| Status | Base | Family | C2S handled | S2C sent | Open items |
|---|---|---|---|---|---|
| [x] | 0x2C | EncounterParticipant | 0/5 | 2/2 | 5 |
| [ ] | 0x26 | PlayerUpdate | 6/12 | 10/32 | 28 |
| [ ] | 0x29 | ClientUpdate | 0/0 | 11/38 | 27 |
| [ ] | 0x27 | Ability | 6/10 | 1/25 | 28 |
| [ ] | 0x24 | Combat | 0/3 | 1/8 | 10 |
| [ ] | 0x1E | Command | 19/86 | 6/36 | 97 |
| [ ] | 0x2D | Inventory | 8/15 | 2/7 | 12 |
| [ ] | 0x42 | RecipecComponentImageSetIds | 8/16 | 2/13 | 19 |
| [ ] | 0xF9 | Claims | 16/29 | 10/25 | 28 |
| [ ] | 0xF4 | VoxelConstruction | 2/2 | 0/11 | 11 |
| [ ] | 0x32 | UIEventTriggered | 1/5 | 0/15 | 19 |
| [ ] | 0x13 | Chat | 0/2 | 2/15 | 15 |
| [ ] | 0x83 | GuildInvite | 12/17 | 0/11 | 16 |
| [ ] | 0x9C | Social | 0/11 | 0/5 | 16 |
| [ ] | 0xB8 | Raid | 0/10 | 0/3 | 13 |
| [ ] | 0x3E | TradeInvite | 6/6 | 4/9 | 5 |
| [ ] | 0xF8 | PacketMailConsumeAttachmentRequest | 5/5 | 1/10 | 9 |
| [ ] | 0x34 | QuestEnd | 0/2 | 0/13 | 15 |
| [ ] | 0x30 | ObjectiveClientComplete | 0/1 | 0/9 | 10 |
| [ ] | 0xD1 | Mission | 0/2 | 0/8 | 10 |
| [ ] | 0x8D | RepeatingActivityAddCount | 0/5 | 0/5 | 10 |
| [ ] | 0xA8 | ClientActivityLaunch | 0/0 | 0/8 | 8 |
| [ ] | 0x1B | Content | 0/10 | 0/1 | 11 |
| [ ] | 0x10D | InstancedGame | 4/10 | 2/12 | 16 |
| [ ] | 0x111 | ObjectScripting | 11/11 | 2/11 | 9 |
| [ ] | 0x8A | MatchInvitation | 0/1 | 0/7 | 8 |
| [ ] | 0x67 | CreateLobbyGame | 0/5 | 0/2 | 7 |
| [ ] | 0x2A | MiniGamePayload | 0/1 | 0/9 | 10 |
| [ ] | 0xFC | PacketTeleportCancelQueue | 0/8 | 1/3 | 10 |
| [ ] | 0xEA | AreaDefinition | 0/5 | 0/2 | 7 |
| [ ] | 0x10C | WorldEditingBoxUpdate | 0/8 | 0/0 | 8 |
| [ ] | 0x105 | HeroicMovement | 0/5 | 0/0 | 5 |
| [ ] | 0xBC | Skill | 0/5 | 1/1 | 5 |
| [ ] | 0x1 | SkillAdmin | 7/19 | 13/28 | 27 |
| [ ] | 0xBD | Loadout | 0/6 | 0/1 | 7 |
| [ ] | 0xE6 | Item | 0/2 | 0/2 | 4 |
| [ ] | 0x9E | CoinStoreBuyBack | 0/5 | 0/7 | 12 |
| [ ] | 0x45 | PacketInGamePurchaseWalletInfoRequest | 3/21 | 3/32 | 47 |
| [ ] | 0x6E | GetAllAvailableRequisitionItems | 0/3 | 0/3 | 6 |
| [ ] | 0xBA | DestroyNpcProjectile | 0/13 | 0/22 | 35 |
| [ ] | 0xBF | Vehicle | 0/12 | 0/21 | 33 |
| [ ] | 0xA1 | PacketDismountRequest | 0/6 | 0/6 | 12 |
| [ ] | 0x104 | SocialShare | 0/2 | 0/3 | 5 |
| [ ] | 0x100 | PacketDebugDrawSphere | 0/0 | 0/5 | 5 |
| [ ] | 0xCE | WallOfDataMarketplace | 0/4 | 0/0 | 4 |
| [ ] | 0xED | Prop | 13/15 | 5/7 | 4 |
| [ ] | 0x4D | FriendUpdateExtraInfos | 1/1 | 6/10 | 4 |
| [ ] | 0x92 | AchievementTrackerUpdate | 1/1 | 5/9 | 4 |
| [ ] | 0xA7 | Rating | 0/3 | 4/5 | 4 |
| [ ] | 0xFB | LootPinata | 0/2 | 2/4 | 4 |
| [ ] | 0xEE | Projectile | 0/0 | 0/4 | 4 |
| [ ] | 0xA0 | Activity | 0/2 | 2/3 | 3 |
| [ ] | 0x118 | Content | 0/1 | 0/2 | 3 |
| [ ] | 0x9 | Gateway::ExternalPackets::BasePacket | 0/2 | 0/1 | 3 |
| [ ] | 0x4DA | GriefAdmin | 0/3 | 0/0 | 3 |
| [ ] | 0x2 | Inventory | 0/1 | 0/2 | 3 |
| [ ] | 0xC6 | Leaderboard | 0/3 | 0/0 | 3 |
| [ ] | 0xAF | Quiz | 0/2 | 0/1 | 3 |
| [ ] | 0xEC | TerrainCell | 2/3 | 6/8 | 3 |
| [ ] | 0x2B | Group | 15/15 | 2/5 | 3 |
| [ ] | 0xB5 | Target | 2/4 | 2/3 | 3 |
| [ ] | 0xF0 | Template | 5/6 | 5/7 | 3 |
| [ ] | 0x98 | PacketPortraitDataRequest | 0/2 | 1/2 | 3 |
| [ ] | 0xDD | GetSpeechPackList | 0/2 | 0/1 | 3 |
| [ ] | 0xCB | Equipment | 0/0 | 0/3 | 3 |
| [ ] | 0x4B | AcquaintanceOnline | 0/0 | 0/3 | 3 |
| [ ] | 0x11B | InteractionCancel | 0/0 | 0/3 | 3 |
| [ ] | 0xEF | PlacedEffectCreate | 0/0 | 0/3 | 3 |
| [ ] | 0x113 | WarpNetworkClient | 0/1 | 0/1 | 2 |
| [ ] | 0x117 | Tool | 0/1 | 0/1 | 2 |
| [ ] | 0x4E8 | CurrencyAdmin | 0/2 | 0/0 | 2 |
| [ ] | 0x6 | Gateway::ExternalPackets::PacketTunnelPacketFromExternalConnection | 0/1 | 0/1 | 2 |
| [ ] | 0x4E7 | ItemAdmin | 0/2 | 0/0 | 2 |
| [ ] | 0xAD | PacketInviteAndStartMiniGameInvitePacket | 0/2 | 0/0 | 2 |
| [ ] | 0xCA | ProfileStats | 0/2 | 0/0 | 2 |
| [ ] | 0x91 | StopInspect | 0/1 | 0/1 | 2 |
| [ ] | 0x112 | PacketClaimVendorPurchaseItemRequest | 4/4 | 2/4 | 2 |
| [ ] | 0xF5 | Palette | 3/5 | 0/0 | 2 |
| [ ] | 0xBE | Experience | 0/0 | 1/3 | 2 |
| [ ] | 0x5 | Login::ExternalPackets::PacketCharacterCreateRequest | 0/0 | 0/2 | 2 |
| [ ] | 0xB9 | VoiceJoinChannel | 0/0 | 0/2 | 2 |
| [ ] | 0x52 | Command | 0/1 | 0/0 | 1 |
| [ ] | 0x102 | FogOfWarZoneData | 0/1 | 0/0 | 1 |
| [ ] | 0x10B | ItemReview::PacketOptInAccount | 0/1 | 0/0 | 1 |
| [ ] | 0x115 | KnownProp | 0/1 | 1/1 | 1 |
| [ ] | 0x48 | LiveGamerPostItem | 0/1 | 0/0 | 1 |
| [ ] | 0x62 | MiniGameFlashCommandAdmin | 0/1 | 0/0 | 1 |
| [ ] | 0xA5 | PacketClientAreaTimer | 0/1 | 0/0 | 1 |
| [ ] | 0xF1 | PacketClientFacialAnim | 0/1 | 0/0 | 1 |
| [ ] | 0xF3 | PacketClientFacialAnimSettings | 0/1 | 0/0 | 1 |
| [ ] | 0xD0 | PacketClientInGamePurchase | 0/1 | 0/0 | 1 |
| [ ] | 0x8B | PacketClientLuaMetrics | 0/1 | 0/0 | 1 |
| [ ] | 0x77 | PacketCommerceSessionRequest | 0/1 | 0/0 | 1 |
| [ ] | 0xCD | PacketGetRespawnLocations | 0/1 | 0/0 | 1 |
| [ ] | 0xE7 | PlayerUpdate | 0/1 | 0/0 | 1 |
| [ ] | 0xE8 | PlayerUpdate | 0/1 | 0/0 | 1 |
| [ ] | 0xAE | PlayerUpdate | 0/1 | 0/0 | 1 |
| [ ] | 0xC9 | PlayerUpdate | 0/1 | 0/0 | 1 |
| [ ] | 0xE3 | PlayerUpdateVehicleCollision | 0/1 | 0/0 | 1 |
| [ ] | 0x109 | ProceduralDataMonitor | 0/1 | 0/0 | 1 |
| [ ] | 0xBB | PunkBuster | 0/1 | 0/0 | 1 |
| [ ] | 0x47 | Report | 0/1 | 0/0 | 1 |
| [ ] | 0x49A | SkillAdmin | 0/1 | 0/0 | 1 |
| [ ] | 0xCF | ThrustPadPlayerEntered | 0/1 | 0/0 | 1 |
| [ ] | 0xD | PacketClientFinishedLoading | 1/1 | 0/1 | 1 |
| [ ] | 0x11C | CharacterCustomization | 1/1 | 0/1 | 1 |
| [ ] | 0x9A | ActionManager | 4/4 | 0/1 | 1 |
| [ ] | 0x55 | BugSubmission | 0/1 | 0/0 | 1 |
| [ ] | 0x56 | ClaimReportSubmission | 0/1 | 0/0 | 1 |
| [ ] | 0x94 | PlayerTitle | 0/1 | 2/2 | 1 |
| [ ] | 0xDC | HudPackets::PacketRadialMenuRequest | 0/1 | 1/1 | 1 |
| [ ] | 0x23 | PacketClientBeginZoning | 0/0 | 0/1 | 1 |
| [ ] | 0x8F | ActivityProfileList | 0/0 | 0/1 | 1 |
| [ ] | 0xDE | AcquireTimer | 0/0 | 0/1 | 1 |
| [ ] | 0x116 | ArtData | 0/0 | 0/1 | 1 |
| [ ] | 0x97 | BeginJobCustimzation | 0/0 | 0/1 | 1 |
| [ ] | 0xE5 | Currency | 0/0 | 0/1 | 1 |
| [ ] | 0xC2 | Faction | 0/0 | 0/1 | 1 |
| [ ] | 0x103 | FogOfWarZoneData | 0/0 | 0/1 | 1 |
| [ ] | 0x16 | Gateway::InternalPackets::PacketTunnelPacketToExternalConnectionList | 0/0 | 0/1 | 1 |
| [ ] | 0xB7 | GuideStone | 0/0 | 0/1 | 1 |
| [ ] | 0x72 | IgnoreAdd | 0/0 | 0/1 | 1 |
| [ ] | 0x74 | KeyCodeRedemptionNotification | 0/0 | 0/1 | 1 |
| [ ] | 0x68 | LobbyGameDefinition | 0/0 | 0/1 | 1 |
| [ ] | 0xA | Login::ExternalPackets::PacketCharacterDeleteReply | 0/0 | 0/1 | 1 |
| [ ] | 0x8 | Login::ExternalPackets::PacketCharacterLoginReply | 0/0 | 0/1 | 1 |
| [ ] | 0x7 | Login::ExternalPackets::PacketCharacterLoginRequest | 0/0 | 0/1 | 1 |
| [ ] | 0xC | Login::ExternalPackets::PacketCharacterSelectInfoReply | 0/0 | 0/1 | 1 |
| [ ] | 0xB | Login::ExternalPackets::PacketCharacterSelectInfoRequest | 0/0 | 0/1 | 1 |
| [ ] | 0x4 | Login::ExternalPackets::PacketForcedDisconnect | 0/0 | 0/1 | 1 |
| [ ] | 0xE | Login::ExternalPackets::PacketServerListReply | 0/0 | 0/1 | 1 |
| [ ] | 0x10 | Login::ExternalPackets::PacketTunnelAppPacketClientToServer | 0/0 | 0/1 | 1 |
| [ ] | 0x53 | PacketClientKickedFromServer | 0/0 | 0/1 | 1 |
| [ ] | 0x6F | PacketClientLog | 0/0 | 0/1 | 1 |
| [ ] | 0x14 | PacketClientLogout | 0/0 | 0/1 | 1 |
| [ ] | 0xB3 | PacketClientMembershipVipInfo | 0/0 | 0/1 | 1 |
| [ ] | 0xA4 | PacketClientNotifyCoinSpinAvailable | 0/0 | 0/1 | 1 |
| [ ] | 0x114 | PacketCorrectNpcHeights | 0/0 | 0/1 | 1 |
| [ ] | 0xE0 | PacketLoginQueueStatus | 0/0 | 0/1 | 1 |
| [ ] | 0x5A | PacketMOTD | 0/0 | 0/1 | 1 |
| [ ] | 0xFE | PacketPlayerStudioRegistered | 0/0 | 0/1 | 1 |
| [ ] | 0xD6 | PacketSecurity | 0/0 | 0/1 | 1 |
| [ ] | 0x5B | PacketSetLocale | 0/0 | 0/1 | 1 |
| [ ] | 0x54 | PacketUpdateClientSessionData | 0/0 | 0/1 | 1 |
| [ ] | 0x5F | PacketWorldShutdownNotice | 0/0 | 0/1 | 1 |
| [ ] | 0xF2 | PacketZoneProximityFacialAnim | 0/0 | 0/1 | 1 |
| [ ] | 0xC7 | PlayerUpdateManagedPosition | 0/0 | 0/1 | 1 |
| [ ] | 0xDF | PlayerUpdate | 0/0 | 0/1 | 1 |
| [ ] | 0xE4 | PlayerUpdate | 0/0 | 0/1 | 1 |
| [ ] | 0x10A | ProceduralDataMonitor | 0/0 | 0/1 | 1 |
| [ ] | 0x35 | Reward | 0/0 | 0/1 | 1 |

## Other items found

- [ ] Zones: ZoneCoordinateInfo and the server's terrain origin (TerrainStore.ZoneOrigin) per zone from StaticZoneData
      (GEOMETRY_OFFSET × 10, ZONE_EXTENTS). Landscapes 400-423 all share the captured values; the world-launched seamless
      continent 430 (zones 440-455, a 4 × 4 grid at 2546400-2570400 / 2544450-2568450) and SeamlessZone1-4 (436-439,
      continent 429) differ. Needed before those zones can be offered.

- [ ] Game Action Queue popups the server never creates: notification types 3 GroupApproval, 4 FriendInvite,
      9 MarketplaceItemReviewRequest (2 GroupInvite, 6 GuildInvite, 7 TradeInvite exist), and the popups made by other
      packets: GaqItemClaimExpiring, GaqItemPendingMail, GaqItemSwapSeatRequest, GaqItemTeleportToPlayer,
      GaqItemTeleportToZoneRequest (FUN_143337930 maps the notification types).
- [x] Encounters 0x2C: protocol decoded and built, duels (docs/encounters.md, 0ce255a). Complete/ObjectiveState with real rewards wait for content.
- [~] Revive another player: client path decoded, server side built; the live "dead" state signal for other clients is open (docs/gameplay.md).

- [~] PlayerUpdate 0x26 (decoded 2026-10-12; client handler = PlayerUpdate_Dispatch case sub-1):
      - [x] KnockedOut 0x39 sent on death; ReadyToRevive 0x44 / ReadyToReviveResponse 0x3D (revive flow); client
            requests 0x0A / 0x18 / 0x1A / 0x73 / 0x77 handled; full data for gathering nodes (LightweightToFullProp).
      - [ ] StartHarvest 0x37 / StopHarvest 0x38: a harvest casting bar ("CastingBar:SetupBar" type 2) for the local
            player; live gathering was timed, ours is instant. Needs harvest durations (not in client data).
      - [ ] MobMovement 0x72 {u64 entity, u32, vec4 ×…}: creature steering (FUN_142c45c90). Ours use 0x7F bodies (work,
            smaller). ExpectedSpeed 0x11 (entity +0x740) for remote smoothing.
      - [ ] HideCorpse 0x50 {u64, bool} (entity +0x145d bit 8): creature corpses (ours vanish at death).
      - [ ] LaunchProjectile 0x31: ranged visuals for others. SetSpawnerActivationEffect 0x22: story spawner effect.
      - [ ] KnockedOutDamageReport 0x3A: death recap (damage report block decoded, 94 B).
      - [ ] CustomizationChange 0x1B / CustomizationData 0x34: live appearance change (we re-show the player).
      - [ ] UpdateScale 0x08, UpdateTemporaryAppearance 0x09, StopCompositeEffect 0x0C, SlotCompositeEffectOverride
            0x16, ManagedObject 0x4D, UpdateStat 0x53, RequestAnimationSelection 0x65, NpcCharacterPersistStateUpdate
            0x66, RemoveFeatherweightObject 0x75; UpdateSteamInfo 0x68 has no client case.

- [~] Player Studio + in-game commerce (InGamePurchase 0x45), reachable in 0.589: Designs window > Submit (needs a
      screenshot) → InGamePurchaseStoreScreen.ProductAddItem(guid, 2) + PlayerStudioWindow (name, headline, description,
      price, tags) → CommerceProductCreate 0x2F → CreateResponse 0x30; Player Studio registration (PacketPlayerStudioRegistered,
      IsPlayerStudioRegistered); peer review (GAQ MarketplaceItemReviewRequest type 9 → GetProductForReview 0x3F → 0x40 →
      SubmitProductReview 0x42); accepted/rejected mail (PlayerStudio.AcceptedItem / RejectedItem); design IsOnMarketplace /
      MarketplacePath; Designs "Player Studio" tab (Template.Category.PlayerStudio); search 0x3D → 0x3E, account store
      0x36 → 0x38, recommendations 0x37, follow 0x34/0x35, product update/remove 0x33/0x31, screenshots 0x49 → 0x45,
      preview/place order 0x01/0x02/0x04, server status 0x0D → 0x0E, configuration 0x2E. Prices: free / coins (user
      decision: store free). Steps: decode → registration + product create → storage → review → store listing + buying.
      Done: registration, create (656c993), peer review + result mail (4ea5527), docs/commerce.md. Next: design
      IsOnMarketplace, my products 0x36/0x38, update/remove, search/listing, buying (free), screenshots.

## Client data files the server does not read yet

Rows = data lines in testclient/Resources/<name>.txt. Presentation-only files (art, animation, audio, UI) left out. Some may
already be covered through other files; check before linking. Empty files (143, shared-engine leftovers) listed last.

| Status | File | Rows |
|---|---|---|
| [ ] | AttributeEntryDefinitions | 1737 |
| [ ] | AbilityParameterDefinitions | 747 |
| [ ] | VoxelShaderParameters | 715 |
| [ ] | ItemClassGroups | 713 |
| [ ] | TagDefinitions | 560 |
| [ ] | ProceduralPlacementSetsSetEntities | 536 |
| [ ] | ProceduralPlacementSetMaterials | 368 |
| [ ] | TriggerFXMap | 314 |
| [ ] | TriggerFxTag | 308 |
| [ ] | VoxelWorldSettingBiomes | 275 |
| [ ] | VoxelCaveTileMaterials | 241 |
| [ ] | VoxelContinentWorld | 233 |
| [ ] | VoxelMaterialFloras | 209 |
| [ ] | VoxelBiomeCaveMaterials | 197 |
| [ ] | VoxelMaterialRemaps | 187 |
| [ ] | VoxelFloras | 178 |
| [ ] | ExportedTemplateCategoryMap | 177 |
| [ ] | SlotDefinition | 168 |
| [ ] | VoxelMatComponents | 149 |
| [ ] | DesignBasedStateToStateTransitionMap | 148 |
| [ ] | DesignBasedStateMachineMap | 147 |
| [ ] | VoxelMaterialsMatComponents | 137 |
| [ ] | VoxelCaveTileSetMappings | 137 |
| [ ] | DesignBasedStateDefinition | 137 |
| [ ] | SimpleAnimationFrames | 136 |
| [ ] | DesignBasedStatePropertiesDefinition | 133 |
| [ ] | PropRecipes | 129 |
| [ ] | DesignBasedTransitionRuleDefinition | 128 |
| [ ] | DesignBasedTransitionGroupRuleMap | 122 |
| [ ] | ProjectileDefinitions | 118 |
| [ ] | ProceduralPlacementSets | 109 |
| [ ] | DesignBasedTransitionGroupDefinition | 108 |
| [ ] | ProceduralPlacementSetEntities | 105 |
| [ ] | VoxelCaveTileToCavernMappingDefinitions | 89 |
| [ ] | VoxelLayerBmpToPropGroups | 86 |
| [ ] | VoxelResourceGeology | 85 |
| [ ] | KeywordDefinitions | 81 |
| [ ] | ModalToolOptionMappingDefinitions | 77 |
| [ ] | DynamicInputActionSystems | 75 |
| [ ] | DesignBasedStateMachineDefinition | 75 |
| [ ] | VoxelBiomeCaveTileSetMapping | 69 |
| [ ] | Requirements | 67 |
| [ ] | PropTintGroupMappings | 60 |
| [ ] | AreaProperties | 60 |
| [ ] | VoxelHeightLayers | 54 |
| [ ] | VoxelShapes | 53 |
| [ ] | VoxelLayerPropertyGroups | 53 |
| [ ] | VoxelWorldSettings | 48 |
| [ ] | CharacterStatDefinitions | 48 |
| [ ] | SimpleAnimation | 46 |
| [ ] | AchievementSubcategories | 46 |
| [ ] | RequirementGroups | 44 |
| [ ] | Tips | 43 |
| [ ] | VoxelMeshProceduralMaterialMaterials | 42 |
| [ ] | RadialMenuButtons | 41 |
| [ ] | ExportedTemplates | 41 |
| [ ] | VoxelPeriodicTileSets | 40 |
| [ ] | RequirementExpressions | 38 |
| [ ] | ItemGroupMappings | 37 |
| [ ] | ModalToolBrushOptionDefinitions | 34 |
| [ ] | FirstTimeEvents | 34 |
| [ ] | VoxelContinentToLayerMapping | 31 |
| [ ] | VoxelContinentLayers | 31 |
| [ ] | VoxelCategories | 31 |
| [ ] | VoxelSystemTemplates | 30 |
| [ ] | AbilityTellDefinitions | 30 |
| [ ] | VoxelCategoryGroup | 29 |
| [ ] | VoxelMeshProceduralMaterials | 27 |
| [ ] | Currency | 27 |
| [ ] | VoxelTierBmpToBiomes | 26 |
| [ ] | CharacterPresetToSelectionMappingDefinitions | 25 |
| [ ] | RequirementSets | 23 |
| [ ] | RefInputProfileActions | 23 |
| [ ] | ModalToolBrushCategoryModeMappingDefinitions | 23 |
| [ ] | VoxelCavernToTileSetMappingDefinitions | 22 |
| [ ] | SimpleAnimationSets | 22 |
| [ ] | VoxelMiningResources | 21 |
| [ ] | ProfileSkillSet | 21 |
| [ ] | IgRuleTypes | 21 |
| [ ] | GlobalAreaShapes | 21 |
| [ ] | ContentPacks | 21 |
| [ ] | ClaimPermissionAccessLevelMappings | 21 |
| [ ] | PlacementPackProps | 20 |
| [ ] | TriggerSet | 19 |
| [ ] | RaceDefinitions | 19 |
| [ ] | AchievementCategories | 19 |
| [ ] | ToolTypes | 17 |
| [ ] | LoadoutEquipSlots | 15 |
| [ ] | HeroicMovement | 15 |
| [ ] | ChatChannels | 15 |
| [ ] | VoxelCaveTileSets | 14 |
| [ ] | Reticles | 14 |
| [ ] | ProfileTypes | 14 |
| [ ] | Notifications | 14 |
| [ ] | AchievementQuality | 14 |
| [ ] | ModalToolBrushDefinitions | 13 |
| [ ] | LoadoutSlots | 13 |
| [ ] | GlobalAreas | 13 |
| [ ] | GameActionQueues | 13 |
| [ ] | CombatSpam | 13 |
| [ ] | ClaimPermissionStringMappings | 13 |
| [ ] | NotificationEntries | 12 |
| [ ] | VoxelMatComponentsFlora | 11 |
| [ ] | RefDisplayCategoryTypes | 11 |
| [ ] | CraftingCategoryGroupDefinitions | 11 |
| [ ] | VoxelTiers | 10 |
| [ ] | ToolStateTypes | 10 |
| [ ] | ResourceTypes | 10 |
| [ ] | ItemGroups | 10 |
| [ ] | Chests | 10 |
| [ ] | BrushType | 10 |
| [ ] | AttributeDefinitions | 10 |
| [ ] | VoxelContinentToTierMapping | 9 |
| [ ] | VoxelCavernDefinitions | 9 |
| [ ] | ProfileSkillSets | 9 |
| [ ] | PlacementPacks | 9 |
| [ ] | ItemCategoryStatMappings | 9 |
| [ ] | FacilityZoneMap | 9 |
| [ ] | EquipmentSlotDefinitions | 9 |
| [ ] | CombatNotifications | 9 |
| [ ] | ResourceVeinGroups | 8 |
| [ ] | BiomeTypes | 8 |
| [ ] | VehicleArmorMappings | 7 |
| [ ] | RadialMenuTypes | 7 |
| [ ] | PrebuiltPalettes | 7 |
| [ ] | PiecewiseLinearCurves | 7 |
| [ ] | OnScreenHints | 7 |
| [ ] | LootPinataAnimation | 7 |
| [ ] | ClaimPermissionDefinitions | 7 |
| [ ] | AbilityLineMembers | 7 |
| [ ] | AbilityClasses | 7 |
| [ ] | LootParticles | 6 |
| [ ] | ClientTimerDefinitions | 6 |
| [ ] | VoxelWaters | 5 |
| [ ] | VoxelTransparentTypes | 5 |
| [ ] | Skills | 5 |
| [ ] | ExportedTemplateCategory | 5 |
| [ ] | ClaimMusic | 5 |
| [ ] | WeaponSetDefinitions | 4 |
| [ ] | ToolAxisTypes | 4 |
| [ ] | Factions | 4 |
| [ ] | Faction | 4 |
| [ ] | DeployableTypes | 4 |
| [ ] | CharacterPersistStateDefinitions | 4 |
| [ ] | Waypoints | 3 |
| [ ] | VoxelCursorShape | 3 |
| [ ] | VoxelCaveRoomGapSideDefinitions | 3 |
| [ ] | SelfLight | 3 |
| [ ] | ModalToolBrushCategoryModeDefinitions | 3 |
| [ ] | MerchantSets | 3 |
| [ ] | LootPinataAnimationSet | 3 |
| [ ] | LoadoutConstants | 3 |
| [ ] | ItemSelfLightMap | 3 |
| [ ] | DamageTables | 3 |
| [ ] | CharacterPersistStateAnimSetMapDefinitions | 3 |
| [ ] | CharacterPersistStateAnimClearMapDefinitions | 3 |
| [ ] | Areas | 3 |
| [ ] | ActionBarDefinitions | 3 |
| [ ] | AbilitySets | 3 |
| [ ] | AbilityLines | 3 |
| [ ] | VoxelMaterialRequirements | 2 |
| [ ] | VoxelCornerTileSets | 2 |
| [ ] | VoxelCaveRoomGapSizeDefinitions | 2 |
| [ ] | StatCategoryDefinitions | 2 |
| [ ] | SkillSets | 2 |
| [ ] | SkillLines | 2 |
| [ ] | SkillCategories | 2 |
| [ ] | PropTintGroups | 2 |
| [ ] | PropTagPropMap | 2 |
| [ ] | PropTag | 2 |
| [ ] | Loadouts | 2 |
| [ ] | ItemUiOptions | 2 |
| [ ] | FireModes | 2 |
| [ ] | FireModeEffectGroups | 2 |
| [ ] | FireGroups | 2 |
| [ ] | CraftingStationCatalystMappings | 2 |
| [ ] | AbilityEx | 2 |

Empty in 0.589: ZoneFactionAbilityLines WheelInfo WelcomeTips Weapons WeaponMounts WeaponAmmoSlots VoxelSurfaceCaveBiomeRemapDefinitions VoxelLayerBmpToBiomes VoxelGapSizeToSideMapDefinitions VoxelForas Vehicles VehicleSpawnMappings VehicleSkillSets VehicleSets VehicleSeatMappings VehicleSeatDisallowMappings VehicleResourceMappings VehicleResistMappings VehicleMoveInfoMappings VehicleLoadouts VehicleLoadoutTabs VehicleLoadoutSlots VehicleLoadoutSlotTintItemClasses VehicleLoadoutSlotItemClasses VehicleLoadoutGuildTintItemClasses VehicleLoadoutDecalItemClasses VehicleLoadoutBodyTintItemClasses VehicleItemClasses VehicleFactionAbilityLines VehicleCargoMappings VehicleAbilityLines TireInfo TerrainObjectDpos SuspensionInfo StartingZoneLoad SpeechPacks SpeechContexts SpawnInfo SocialWallTagStrings SocialWallTagIcons SocialWallEntryDefinitions SocialProfileReputationDefinitions SocialProfileLikesDefinitions SkinTones SkillPointRates SkillConstants SeatInfo ScreenEffects ResourceInfo ResourceConstants ResistInfo Rails RailPoints RailCameras RaceSets RaceSetIds ProfileStatAttributeMap ProfileAbilityLines PlayerStateProperties PlatformStringMappings NotificationImages MoveInfo Missions MissionConstants MissionComponents MissionComponentMap LoadoutTabs LoadoutSlotTintItemClasses LoadoutSlotItemClasses LoadoutItemAttachments LoadoutGuildTintItemClasses LoadoutDecalItemClasses LoadoutBodyTintItemClasses LoadoutAttachments LoadoutAttachmentGroupMap LoadoutAttachmentClasses LeadershipSkillSets Leadership ItemSortTypes ItemLines ItemLineMembers ItemGroupPromos ItemFiltersToClassMap ItemFilters ItemFilterOptions ItemFilterOptionMap ItemClassSets Implants ImplantSlots ImplantSlotItemClasses ImplantConstants HousingPlacementAssociations HousingNames HouseRatingKeywords HouseNames GameTutorialData FireModeProjectileMappings FireGroupMappings FilterToItemClass FactionSkillSets FactionImageSets FactionAbilityLines FacilityZoomMap FacilityPcBenefits FacilityNpcBenefits FacilityFactionAbilityLines FacilityBenefitImmunities FacilityBenefitGroups Experience ErrorCodeMappings EngineInfo DynamicsInfo DiplomacyTitles DiplomacyRanks DestroyedPartMappings DestroyedPart DestroyedInfoMappings DestroyedInfo Deployables DeployableTypeMappings DeployableCostMappings DeploySpawnMappings DeployInfo DamageMaterialMappings DamageLevelMappings DamageLevelInfo CollectionCategories ClientItemDatasheetData ClientDatasheets CharacterStatSetMappings CargoInfo CargoAllowedMappings BugSeverity BugCategoryMappings BugCategories ArmorInfo ActivityPortalSections ActivityCategories AchievementTrophies AbilityEffectTypeSettings credits VoxelContinentCaveTileMapping Projectiles 
