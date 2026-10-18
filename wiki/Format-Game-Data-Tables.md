# Format: Game Data Tables

The servers read the game's own data from the server owner's Landmark install: the client's data tables and texts
(copied into the game data folder) and some terrain files (read in place). Nothing of it is in the repository.

Back to [Asset Documentation](Asset-Documentation).

## Where the files come from

With Docker, the `setup` service copies, on every start (a second when nothing changed):

- `Resources/*.txt` from `GAME_DIR` (the data tables);
- `Locale/en_us_data.dat` (the English texts);
- the project's `data/` folder (small tables derived by this project);
- the ground-height grids from `LandmarkEmu-terrain-heights.zip`, if it is in the project folder.

into `server-data/gamedata` (`GAME_DATA_DIR`), mounted read-only at `/gamedata` (`GameDataPath`). The Landmark folder
itself is mounted read-only at `/game` (`GameDirectory`) for the files read in place. The tables must be those of the
players' client build (0.695). See [Server Installation](Server-Installation).

## Data tables (`Resources/*.txt`)

Plain text, one row per line, fields separated by **`^`**, the first line a header. Numbers are written the invariant
way (`2.8`), whatever the PC's language. Most tables have an id in the first column. The server maps the columns it
needs onto row classes (`LandmarkEmulator.Shared/GameTable/Model/`) or reads them directly where it uses one or two.

Tables the server reads include (by system):

| System | Tables |
|---|---|
| Characters | `ProfileDefinitions`, `CharacterCreate*`, `CharCustomization*`, `CharacterStatDefinitions`, `CalculatedStatDefinitions`, `BlackListEntries` (name filter) |
| Inventory and items | `InventoryDefinition`, `ActionSlotDefinitions`, `DurabilitySet`, `RefItemQuality`, `LootPinata` |
| Crafting | `Recipes`, `RecipeComponents`, `RecipeRecipeComponentMaps`, `CraftingStations`, `CraftingStationRecipeMappings`, `CraftingCategoryGroupMapDefinitions` |
| Props | `PropDefs`, `PropRecipeComponentMaps`, `PropCategory*`, `PropHelpWindow*`, `PlacementPacks`, `PlacementPackProps`, `EditableProperties`, `Models` |
| Terrain and gathering | `VoxelMaterials`, `VoxelBiomes`, `VoxelBiomeMaterials`, `VoxelProceduralMaterials`, `VoxelProceduralMaterialMaterials`, `VoxelLayerPropertyGroups`, `VoxelCaveTileDefinitions`, `ResourceVeins`, `ResourceVeinGroupVeins`, `Resources`, `ProceduralPlacement*` |
| Combat and animation | `AbilityParameterDefinitions`, `ProjectileDefinitions`, `AnimationSelection`, `AnimationSelectionVariables`, `InteractableCompositeEffect*`, `InputActionGroupEntries` |
| Site games | `IgRules`, `IgRuleSets`, `IgRuleTypes`, `IgRuleParameters`, `IgRuleTypeParameters`, `IgPropRoles`, `IgPropRoleProps`, `IgRuleSetPlacementProps`, `IgGeneralSetting`, `RefIgGeneralSettingsTypes` |
| World | `StaticZoneData`, `TagDefinitions` |

`VoxelMaterials` has two numbers per material: the **ID** column and the **ARRAY_INDEX** column. Voxel data on the wire
and in templates uses ARRAY_INDEX.

## Texts (`Locale/en_us_data.dat`)

Tab-separated lines: a text hash, a second field, the text. The client looks a text up by the **Jenkins lookup2** hash
of `Global.Text.<id>`, so the server hashes text ids the same way to find a name (`GameTable/Text/TextManager.cs`).
Many settings and packets name texts by id (the world names in `WORLD_NAME_ID`, for example).

## Client files read in place (from `GameDirectory`)

| File | Used for | Format notes |
|---|---|---|
| `CommonData/Voxel/materials/textures/Continent_BiomeIds_<continent>.bmp` | Which biome a spot is (materials under the pick, tree lists) | 1250 × 1250, the blue channel is the `VoxelBiomes` id, 6.4 m per pixel, the zone origin at the centre, z running down |
| `CommonData/Voxel/veinData/ContinentVeinData<continent>.vdt` | Which cells are surface or cavern biome, for ore veins | from offset 460: sections of `{u32 groups, groups × (u32 layer, u32 biome, u32 1, u32 n, n × u64 sorted level-2 cell ids)}` |
| Cave pieces (`.cav`, `.cpa`) | Where the Chaos Caverns are air, graveyard and outpost spots | see `docs/terrain.md`, "Caves" |
| `CommonData/Voxel/templates/*.otd`, `CommonData/Collision/*.cdt` | Stamps and the Spire's collision | [Format: Voxel Templates](Format-Voxel-Templates) |
| `CommonData/Voxel/surfaceCache/*.csc` | Checking generated landscapes (tools only) | [Format: Surface Cache](Format-Surface-Cache) |

Vein shapes: the client generates them itself and that generator is not decoded yet; the server places veins with its
own rule meanwhile (*not live data*). More: `docs/client-reference.md`, "Client data the server reads", and
`docs/terrain.md` in the repository.
