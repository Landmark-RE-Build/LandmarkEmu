# Client 0.589 → 0.695: what changed

How each part was found, and the tool that regenerates it. The function-level trace (every new / changed function,
what it activates, what it needs from the server) follows once the 0.695 decompile is done (re/decomp_all_695).

Builds: 0.589.1.128895 (exe 2016-06-04, the Steam build) → 0.695.1.132647 (exe 4 Jan 2017; the 14 Feb 2017 update
changed only the exe and the English text). 1,094 files: 583 new, 511 changed (update-0589-to-0695-files.csv).

## 1. Classes (RTTI)

`LANDMARK_DIR=<0.695> RTTI_CACHE=re/out/rtti_695.tsv python re/tools/rtti.py` → re/out/rtti_695.tsv; diff in
re/out/rtti_new_695.txt (143 new) and rtti_gone_695.txt (28 gone: only container / lambda instantiations, no
feature removed).

New packet and definition classes, by feature:

| Feature | Classes |
|---|---|
| Bio and Inspect | PlayerUpdatePacketSetPlayerBio, RequestInspectPlayerPacket |
| Buy a Design's missing materials | TemplatePacketPurchaseMissingTemplateMaterials, TemplatePacketPurchaseTemplateMaterialsDataRequest / Response (RecipeCraft arrays) |
| Story Node response buttons | PropPacketDialogResponse, Hud::HudIndicatorDefinitionNpcDialogResponse, NpcDialogResponseIndicator |
| Game Table rework, Game Finder | InstancedGameMatchmakingRequest / Update, InstancedGameRequestJoinTeamPacket, InstancedGameGetConfigurationRequestPacket, InstancedGameAddOrRemoveRuleRequestPacket, ClientRequirementInstancedGameSettingValue, InstancedGames::RuleSetScriptPropDefinition, InstancedGames::RuleRequirementFromGeneralSettingDefinition |
| Play Mode Rules | ClaimsPacketSetAdventureFlags, ClientRequirementCanUseBootAbility |
| Build sites without flags | ClaimsPacketStartClaimPlacement |
| Build site entry | ClaimsPacketPlayerEnteredClaim |
| Crimson Parlor gender / size | CharacterCustomizationPacketEquippedAttachmentRequest / Reply, CharacterCreateOptionsManager option tables |
| Tint unlockers (Parlor packs, hats) | ArtTintGroupUnlockDefinition(+Manager), ArtTintGroupUnlockTintDefinition |
| Item use refused | InventoryPacketShowActivationFailure |
| World-edit effects | WorldEditingPlayEffectPacket |
| Player Studio | PacketInGamePurchaseCommerceTemplateProductCreate |
| Daybreak Design categories | ExportedTemplateDefinition(+Manager, SideLoader), ExportedTemplateCategoryDefinition, ExportedTemplateCategoryMappingDefinition, ExportedTemplateSideDefinition, Internal::ExportedTemplateInternalLoaderHandler |
| Mesh Shapes tab | MeshCategoryDefinition, MeshCategoryGroupDefinition, MeshCategoryMeshMapDefinition, MeshPaletteEntry, PaletteCollectionMeshes |
| Prop help windows | PropHelpWindowDefinition(+Manager), PropHelpWindowMappingDefinition |
| Prop settings editor | LuaPropertyEditor::PropertyTextLine (text fields: team names, victory messages) |
| Paste / preview progress | VoxelFarm::Internal::PasteThread, PreviewGenerationThread |

## 2. Text (Locale/en_us_data.dat)

`python re/tools/textdiff.py` → re/out/text_589.tsv, text_695.tsv and trace/0695/text-new.tsv: **531 new** and
**1,372 changed** strings (ids 577069-577746 for the new ones). They name every new feature and give the exact link
sentences, settings and messages:

- **Story tools:** Combat Node (tactics Standard / Stationary / Do Nothing, Ability 1-6, Enable Combat AI Movement,
  weapon visuals Equipped / Sheathed, Spawn With Weapons Equipped), Hour Node (= Server Time Node: Amount of
  Listeners, "When game time in slot 1..6 is reached"), Counting Node and Scoreboard (count events / actions), IF Gate
  (If / Else, Current Value, Blue Light / Yellow Light, Controlling Team, Is Alive, "Choose the prop to use in the IF
  condition"), Random Link Filter ("Assign potential action", "Trigger Random Action"), Respawn Location (activate /
  deactivate), Story Node ("When the text on this node expires", "Is Question? (Chat Bubble)", "When a Luminary
  accepts / denies"), Linking Area ("Destroy / Heal Voxels in this area", "Allow Voxel Destruction: Site-wide
  setting / Allowed", "Damage all Luminaries [and creatures] in this area", **"For every object in this area when
  parsed... / ...parse every entity in this area"**), Damage Node ("Damage % Multiplier"), fireworks ("Launch this
  firework").
- **Play Mode Rules messages:** "Grappling is disabled on this Build Site.", "Voxel destruction is disabled...",
  "Boot abilities are disabled...", "You cannot change the Time Settings on this Build Site.", "You cannot use a
  grappling hook while gliding!".
- **Site games:** Allow team switching, Team 1-4 Name / Allow Players / Victory Message, Score Based Victory, Victory
  Points Required, Players needed to start, Time Limit, Score On Enemy Kill, Player / Team Score Victory, Control
  Points, Neutral / Team Spawners, TeamSpawner Blue / Red / Yellow / Green, Score Node (Point Amount, "Add points to
  the triggering Luminary's score", "Add points to Team 1-4"), Victory Node ("Declare Victory for Team 1-4 / the
  Triggering Team"), score chat lines, Control Point ("When control point is captured", "Attempt to control"), Game
  Finder.
- **Reward Chests:** Common, Stonedelver's, Orefinder's, Gemseeker's, Superb, Legendary (descriptions say what each
  holds); "Player looting cooldown"; "Owners cannot loot their own chests"; "There are chests on this build site that
  you have a lockout timer for"; "Reward Chest Expiration!" mail; **Thank You Token** and the goals "No Good Deed...",
  "To Serve Luminaries", "They Like You. They Really Like You!" with the title "Champion of the People".
- **Pets:** Shadow Wolf, Stag, Cockatrice, Bunny, Lop Ear Bunny, Beetle, Jackalope figurines; "Pet",
  "Non-Combat Entity", "Cosmetic Pets".
- **Fireworks:** 22 consumables, 22 emitters and their recipes, packs (Pack of Fireworks / Fancy / Streamers / Triple
  Fan / Twinkle / Whirly, Pack of Unstable Fireworks "leaves behind 50 safe fireworks").
- **Crimson Parlor:** Fantasy Parlor Pack (faces with pointy ears / horns / tusks, cat eyes, skintones in 6 colours),
  Cowboy Hat / Ninja Mask / Samurai Helmet unlockers (the Halloween hats).
- **Not in any patch note:** **Roll Wraps** boots ("Execute an acrobatic roll in any direction"), the **playdead**
  emote, **rank 2 and 3 of every weapon ability** (Poison Blade 2/3 ... Darkfire 2/3), **Terrogaunt**, Strafe Left /
  Right (Neutral), titles for events (Hero of Landmark, Landmark Explorer, Holiday Hero, Creator of Winter
  Wonderlands, Icy Architect, The Lumerian Lens, Curator, "giveaway title for sunset players"), holiday sweaters
  (Green, Blue), Colonist's Flight Suits (Merry Red, Forest Green, Festive Blue), "Invite your friends!" (trial key
  mail), Chat Window Focus / Fade Out Opacity, Ambient Volume, Allow Site-Wide Level of Detail, Smoothing Enabled.
- **The 1,372 changed strings:** 1,188 only change capitalisation ("build site" → "Build Site"), 183 change wording
  (gray → grey, typo fixes, "Skeletal Bodysuit" → "(White)", "Strafe Left" → "(Mouse)", the flag's description:
  "This item is no longer required to place Build Sites..."), none only change numbers. **Some 0.589 ids were reused
  for new sentences:** 557290 "MAX" → "When this node's count reaches its maximum...", 565055 "Mine" → "When this
  node's count reaches 0...", 560924 "Victory" → "Victory Node", 564156 "Fireball" → "Fireball 1" (ability ranks).
  The server's Link vocabulary (StoryTools.cs uses text ids) must use the 0.695 meaning of such ids for 0.695
  clients.

## 3. Data tables (Resources/*.txt)

`python re/tools/tablediff.py` → trace/0695/tables-diff.md (one row per table); `tablediff.py <Table>` shows the
differing rows. Highlights:

| Table | Change | Means |
|---|---|---|
| PropDefs | +82, 15 changed | New props: Combat Node 2940, Hour Node 2945, IF Gate 2976, Scoreboard 3000, Victory Node 3010, Score Node 3011, TeamSpawner Yellow 3012 / Green 3013, Fireworks Emitters 2949-2970, Reward Chests 3003-3008, creature props (Ebonguard set 2902-2928 and Enforcer 2996; Slaug, Abomination, Chomper, Fiery Fungus, Proximity Mine, The Outcast, Target Dummy, Vine Trap, Highfall Catapult 2978-2995). Changed: Random Link Filter 2657, Server Time Node 2659, Respawn Point 2701, Counting Node 2901 already existed in 0.589 (dormant) and got new models / recipes; Magical Platforms scalable; Game Table settings |
| VoxelShapes | +30 | The Mesh Shapes (cones, cogs, tubes, cylinder, polyhedra, prisms, pyramids) |
| MeshCategory / MeshCategoryMeshMap | new (4 / 14) | The Mesh Shapes tab's categories |
| IgGeneralSetting, RefIgGeneralSettingsTypes, IgRuleTypes, IgRules, IgRuleParameters, IgPropRoleProps, IgRuleSets | reworked | The Game Table rework (header changes too) |
| CharacterCreateOptions / Actors, CharCreateOptionEqualities, CharCustOptionMult, CharCustomizationSlotCost | +129 / +1086 / +110 | Crimson Parlor: gender, size, fantasy options and their prices |
| ArtTints (new, 1,119), ArtTintGroupUnlock (6), ArtTintGroupUnlockTint (45), ArtTintTypeTints, ArtSourceActors (+136), ArtSourceActorTintTypes (+215) | | Tint unlocks and new art (outfits, pets, creatures) |
| EditableProperties | +16, header changed | New prop settings (counting values, IF Gate, Combat Node, Reward Chest cooldown...) |
| Recipes, RecipeComponents, RecipeRecipeComponentMaps, CraftingStationRecipeMappings | +33 / +10 / +36 / +32 | Fireworks, Reward Chests, story tools at the Lumen Station |
| PropCategoryPropMap | +58 | New props in the build palette |
| InteractableCompositeEffects / Sets | +44 / +22 | Effects of the new props (fireworks, chests, nodes) |
| FirstTimeEvents | +6 | First-time pop-ups for the new story tools |
| PropHelpWindow / Mapping | new (1 / 1) | A help window for a prop |
| Requirements, RequirementExpressions | +8 / +8 | New requirements (boot ability allowed, instanced-game setting value) |
| HudIndicators | +1 | The NPC dialog response indicator |
| RadialMenuButtons | +2 | New right-click entries (Inspect?) |
| AbilityParameterDefinitions, AnimationSelection(+Variables), InputActionGroupEntries, DynamicInputActionSystems | small | Roll ability, strafing, new animations |
| Models | +19 | Pets, chests, new props |
| VoxelContinentLayers / ToLayerMapping, VoxelLayerBmpToPropGroups, VoxelBiomes, VoxelMaterials | +1 / +3 / 6 / 2 changed | A new continent layer (holiday islands?), biome changes |
| StringHashToValue, CodeStringMappings, KeywordDefinitions, TagDefinitions, TriggerFXMap / TriggerFxTag, ProfileDefinitions, ModalTool* | small | Code strings, tags and effects of the above |

## 4. UI, scripts and help pages

From update-0589-to-0695-files.csv and a diff of the plain-text Lua files.

- **New windows:** `widgets/InspectWidget.swf` (Inspect, HUD widget 218), `widgets/TemplateConfirmationWidget.swf`
  (buy a Design's missing materials, 219), the modular build windows `PropWindow` (220), `TintsWindow` (221),
  `MaterialsWindow` (222), `MeshesWindow` (223), `DesignsWindow` (224), `ShortcutsWindow` (225), `ToolsWindow`
  (226), `GameFinder.xml/.lua` (/gamefinder), `DropdownDialog.xml/.lua` (a generic dropdown dialog: team choice?).
- **Changed windows (20 .swf):** CharacterCreateWindow, Chat, ClaimManagementWindow (place sites from the window),
  GuildWindow, InstancePropertyEditor (prop settings), Inventory (Bio), LoadingScreen (contest winners),
  NotificationWindow, RulesetObjectSettings (Game Table), SelectedTemplate, BarberShopWidget (gender / size),
  BuildModeHUDWidget, BuildModeTimeWidget, BuildModeWidget, ClaimWalkOnTextWindow, CraftingWidget,
  HUDScoreboardWidget, SpeechBubbleWidget (response buttons), TeleportSpireWidget, TeleportWidget. To export with
  JPEXS like 0.589 (re/as3) and diff the ActionScript.
- **Lua:** `global.lua` adds `/gamefinder` → ShowGameFinder(), OnInspectPlayer() → HudStateManager:OnOpenInspect(),
  CloseEndGameScoreboard(), and checks `InstancedGame.IsInGame()`; `admin.lua` adds `/barberrace`, `/barbergender`
  (CharacterCustomization.SetRace / SetGender: new client natives); `UIObjects.lua` adds UICheckbox;
  `SelectedTemplate.lua` adds GetResources (the missing-materials confirmation); `hudstate.lua` no longer checks
  `Voxel.AllowBuildingMode()` before restoring the combat HUD; `CharacterSelection.lua` resets the create window.
- **Help pages:** new AppearancePurchase_FTE, Mesh_FTE, ScriptObj_Combat_FTE, ScriptObj_Probability_FTE,
  ScriptObj_RespawnLoc_FTE, ScriptObj_Servertime_FTE; changed ScriptObj_Large, ScriptObj_Logic_FTE,
  ScriptObj_Random_FTE, Social_Media_FTE (the First Time Events of FirstTimeEvents +6).
- **Terrain data:** CommonData/Voxel 5 new + 32 changed .otd, 3 new .ocm / .csc (VoxelContinentLayers +1: a new
  continent layer, the holiday islands?).
- **Art:** Resources/Assets 255 changed .pack, Images 358 new .dds, Sound 20 new .fsb, CommonData/Collision 11 new
  .cdt (Respawn prop, pets, chests...).
- **InputProfile_Default.xml:** new key bindings (roll? help?).

## 4b. Lua bindings (natives the UI and scripts call)

`LUABIND_EXE=<0.695 exe> DECOMP=decomp_all_695 LUABIND_OUT=luabind_695.tsv LUABIND_SENDER=DAT_145431088
LUABIND_SETGLOBAL=1439d0060 LUABIND_REGISTER=1439dc760 python re/tools/luabind.py` → re/out/luabind_695.tsv (2,115
bindings; 36 new — the six SocialShare.* existed in 0.589 under a garbled name, ui-trace-695.md). New ones that send packets:

| Binding | Sends | Feature |
|---|---|---|
| Claims.SetClaimAdventureFlag | ClaimsPacketSetAdventureFlags | Play Mode Rules |
| Claims.StartClaimPlacement | ClaimsPacketStartClaimPlacement | place a site from the window |
| InstancedGame.SwitchTeam | InstancedGameRequestJoinTeamPacket | Game Table teams |
| InstancedGame.StartMatchmaking / StopMatchmaking | (tunnel; InstancedGameMatchmakingRequest — resolve) | Game Finder |
| RulesetObject.UpdateValueBool | InstancedGameAddOrRemoveRuleRequestPacket | Game Table rules |
| Prop.OnConfirmNpcDialog / OnDenyNpcDialog | PropPacketDialogResponse | Story Node response buttons |
| Templates.RequestPurchaseTemplateMaterialsData / PurchaseMissingTemplateMaterials | TemplatePacketPurchaseTemplateMaterialsDataRequest / PurchaseMissingTemplateMaterials | buy a Design's missing materials |
| Ui.InspectPlayer | RequestInspectPlayerPacket | Inspect |
| Mesh.PlaceMesh / Mesh.SelectMesh | **ToolPacketActivate** (existing packet) | Mesh Shapes reuse the tool activation: the server must accept the new shape ids there |

Client-only new bindings: CharacterSelect Get/SetEyeSlot*, Claims Get/SetAllowClaimWideLOD, GameSettings / Ui
Get/SetAmbientVolume, InstancedGame IsMatchmaking / SetGameFinderFlag / ShowGameFinder, Mesh.Populate / Search /
SelectCategory / Filter*, Prop.OpenHelpWindow, RulesetObject.UpdateDirtySliders / UpdateValueFloat / UpdateValueString
(check whether these send through a deeper path), SocialShare.*, Tool Get/ToggleCursorSmoothing (Fill "Smoothing
Enabled"), Ui.SetBio (no direct send found at depth 2 → trace deeper: PlayerUpdatePacketSetPlayerBio is sent by
FUN_142fd4290).

## 5. Functions

- Decompiled 2026-10-15: re/Landmark695 (re/analyze695.py, 237,022 functions, 30 min) → re/decomp_all_695 (237 parts).
- `python re/tools/funcdiff.py` → trace/0695/functions.tsv (matching by normalised code, strings, call graph and
  address order), then `python re/tools/codecompare.py` (machine code compared with every address masked; column
  `code`): **228,056 unchanged, 5,238 changed, 3,013 new**; 1,245 0.589 functions without a partner. The text-only
  status column overcounts changes (decompiler typing); use `code`.
- To do: trace every new function and every really-changed one: what it reads, shows, sends, and what the server
  must answer.
