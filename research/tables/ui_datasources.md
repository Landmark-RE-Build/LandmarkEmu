# UI data sources (generated from re/as3)

What each widget reads; filled natively from packets. Checklist for the rework.

| Data source | Fields read | Widgets |
|---|---|---|
| Achievement.Achievements |  | Journal |
| Achievement.Categories |  | Journal |
| Achievement.FilterTypes |  | Journal |
| Achievement.GroceryList |  | WatchListWidget |
| Achievement.Info |  | Journal |
| Achievement.SortTypes |  | Journal |
| Achievement.TrackedAchievements |  | WatchListWidget |
| AdminData.Templates.Folders |  | ContestWidget |
| AdminData.Templates.List |  | ContestWidget |
| Character.AbilityEffects |  | BuffsWidget |
| Character.ActiveChatBubbles |  | SpeechBubbleWidget |
| Character.Currencies | Name, Quantity | BarberShopWidget, HudMenuWidget |
| Character.FriendsList |  | ContactWidget, TeleportWidget |
| Character.IgnoreList |  | BlockedWidget |
| Character.MaterialPalette.MaterialBreadCrumb |  | MaterialMenuWidgetDocked, MaterialMenuWidgetUndocked |
| Character.MaterialPalette.MaterialCategoryListings |  | MaterialMenuWidgetDocked, MaterialMenuWidgetUndocked |
| Character.MaterialPalette.SelectedMaterial |  | MaterialMenuWidgetDocked, MaterialMenuWidgetUndocked |
| Character.MegaPalette.Materials.Categories |  | BuildModePaletteWidget, BuildModeWidget |
| Character.MegaPalette.Materials.Entries |  | BuildModePaletteWidget, BuildModeWidget |
| Character.MegaPalette.Materials.SortOptions |  | BuildModePaletteWidget |
| Character.MegaPalette.Prop.Categories |  | BuildModePaletteWidget, BuildModeWidget |
| Character.MegaPalette.Prop.Entries |  | BuildModePaletteWidget, BuildModeWidget |
| Character.MegaPalette.Prop.SortOptions |  | BuildModePaletteWidget |
| Character.MegaPalette.PropTint.Categories |  | BuildModeWidget |
| Character.MegaPalette.PropTint.Entries |  | BuildModeWidget |
| Character.MegaPalette.Shapes.Categories |  | BuildModePaletteWidget |
| Character.MegaPalette.Shapes.Entries |  | BuildModePaletteWidget, BuildModeWidget |
| Character.MegaPalette.Shapes.SortOptions |  | BuildModePaletteWidget |
| Character.MegaPalette.Template.Categories |  | BuildModePaletteWidget, BuildModeWidget |
| Character.MegaPalette.Template.Entries |  | BuildModePaletteWidget, BuildModeWidget |
| Character.MegaPalette.Template.SortOptions |  | BuildModePaletteWidget |
| Character.MegaPalette.Tools.Entries |  | BuildModeWidget |
| Character.PlayerTarget |  | CombatHudTargetWidget |
| Character.PropPalette.BreadCrumb |  | PropMenuWidgetDocked, PropMenuWidgetUndocked |
| Character.PropPalette.CategoryListings |  | PropMenuWidgetDocked, PropMenuWidgetUndocked |
| Character.PropPalette.PropTooltip |  | BuildModeWidget, PropMenuWidgetDocked, PropMenuWidgetUndocked |
| Character.PropPalette.SelectedProp |  | PropMenuWidgetDocked, PropMenuWidgetUndocked |
| Character.RespawnInfo |  | RespawnWidget |
| Character.Template.Tooltip.MaterialItems |  | BuildModeWidget |
| Character.Template.Tooltip.Props |  | BuildModeWidget |
| Character.Template.Tooltip.TemplateData |  | BuildModeWidget |
| CharacterCustomization.ActiveSelections | Name, NumOptions, OptionSlot, OptionType, SelectedIndex | BarberShopWidget |
| CharacterCustomization.CurrentCost | TotalCost | BarberShopWidget |
| ClaimDialogs.AdminClaimContributors |  | ClaimWalkOnTextWindow |
| ClaimDialogs.PropLimits |  | BuildModeWidget |
| ClaimDialogs.WinnerWalkOnText |  | ClaimWalkOnTextWindow |
| ContactWindow.TabButtons |  | ContactWidget |
| Content.ActivePackages |  | WorldEditModeLoadWidget |
| Content.ActiveSockets |  | WorldEditModeModalBarWidget |
| Content.AllPackages |  | WorldEditModeModalBarWidget |
| Content.PlayerIn |  | WorldEditModeModalBarWidget |
| Crafting.Repair.All |  | RepairWidget |
| Crafting.Repair.Items |  | RepairWidget |
| Crafting.Repair.Selected |  | RepairWidget |
| Crafting.Salvaging.Components |  | CraftingWidget |
| GameData.Achievement.AchievementDetails |  | JournalDetailsWidget |
| GameData.Achievement.AllAchievements |  | JournalCompleteWidget |
| GameData.Achievement.Objectives |  | JournalDetailsWidget |
| GameData.BaseClient.VivoxParticipants |  | VoiceChatWidget |
| GameData.BaseClient.VivoxRooms |  | VoiceChatWidget |
| GameData.BaseClient.VoiceRecentParticipants |  | VoiceChatRecentWidget |
| GameData.Character.ActionBars.EquippedItem |  | CombatHudCastingWidget |
| GameData.Character.ActionBars.ModalBar |  | BuildModeModalBarWidget, BuildModeWidget |
| GameData.Character.Durability |  | RepairIconWidget |
| GameData.Character.GuildMembers |  | ContactWidget |
| GameData.Character.ItemTooltip | Attribute_ImageSetId, Attribute_Name, Categories, CategoryImageSetId, Condition, Description, ExpirationTime, Flags, Flavor, HasSecondaryChestTooltip, ImageSetId, ItemDefId, ItemName, Keyword_Image_Sets, MiningResourceRating, Modifications, Name, PlantResourceRating, PropResourceRating, Quality, RightClickContext, Stats, TotalCoolDown, Upgrades | ToolTipWidget |
| GameData.Character.PlayerTarget |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudWidget |
| GameData.Character.Templates |  | BuildModePaletteWidget |
| GameData.Character.Tooltip.ChestItems | CurrentChestInventorySize, ItemDefId, MaxChestInventorySize, Name, Quality, Quantity | ToolTipWidget |
| GameData.ClaimDialogs.WalkOnText | GUID | BuildModeHUDWidget, BuildModeTimeWidget, ClaimWalkOnTextWindow, VoxelVisionWidget |
| GameData.Controller.Active |  | BuildModeCameraWidget, BuildModeTimeWidget |
| GameData.Crafting.PoNUrl | Url | CraftingWidget |
| GameData.Crafting.Recipes | CategoryName, ItemDefinitionId | CraftingWidget |
| GameData.Crafting.Refining.Output |  | CraftingWidget |
| GameData.Crafting.Refining.Queue |  | CraftingWidget |
| GameData.Crafting.Refining.State | FuelCap, FuelPreviewCount, FuelPreviewGuid, FuelPreviewIcon, FuelPreviewTime, FuelRequired, FuelTime, ReadOnly, State | CraftingWidget |
| GameData.Crafting.SelectedRecipe |  | CraftingWidget |
| GameData.Crafting.SelectedRecipeComponents |  | CraftingWidget |
| GameData.Crafting.Upgrade.Components | CountRequired, MyCount | CraftingWidget |
| GameData.Crafting.Upgrade.Item | CurrentUpgrades, ItemGuid, MaxUpgrades | CraftingWidget |
| GameData.Crafting.Upgrade.Stat0 | ItemGuid, ModifiedValue | CraftingWidget |
| GameData.Crafting.Upgrade.Stat1 | ItemGuid | CraftingWidget |
| GameData.Crafting.Upgrade.Stat2 | ItemGuid | CraftingWidget |
| GameData.Crafting.Upgrade.Stat3 | ItemGuid | CraftingWidget |
| GameData.Crafting.Upgrade.Tooltip | Categories, CategoryImageSetId, Cost, Description, ExpirationTime, FraudTimer, ImageSetId, MiningResourceRating, Name, PropResourceRating, Quality, Stats, Upgrades | CraftingWidget |
| GameData.GameSettings.General |  | ClockWidget |
| GameData.Groups.PlayerGroup |  | GroupWidget |
| GameData.HelpMenu |  | HelpMenuWidget |
| GameData.InstancedGameScoreHUD.KillFeed |  | KillFeedWidget |
| GameData.Revive |  | CombatHudArmorWidget, CombatHudCastingWidget, CombatHudEnergyWidget, CombatHudReviveWidget, CombatHudWidget |
| GameData.SocialShare | Enabled | SocialShareWidget |
| GameData.StartMenu | HotKey, Name | HudMenuWidget |
| GameData.Time |  | BuildModeTimeWidget, Journal |
| GameData.Voxel.BrushInfo | SelectedShape | ShapeMenuWidgetDocked, ShapeMenuWidgetUndocked |
| GameData.Voxel.Shapes |  | ShapeMenuWidgetDocked, ShapeMenuWidgetUndocked |
| LocalPlayer.Resources.Armor |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudWidget |
| LocalPlayer.Resources.Health |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudResourceBarsWidget, CombatHudWidget |
| LocalPlayer.Resources.Mana |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudResourceBarsWidget, CombatHudWidget |
| LocalPlayer.Resources.Repairable |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudWidget |
| LocalPlayer.Resources.Shield |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudResourceBarsWidget, CombatHudWidget |
| LocalPlayer.Resources.Stagger |  | CombatHudArmorWidget |
| Notifications |  | NotificationsWidget |
| Reticle.CastTime |  | CombatHudArmorWidget, CombatHudCastingWidget, CombatHudEnergyWidget, CombatHudWidget |
| Reticle.HitIndicator1 |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudWidget |
| Reticle.HitIndicator2 |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudWidget |
| Reticle.HitIndicator3 |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudWidget |
| Reticle.Status |  | CombatHudArmorWidget, CombatHudEnergyWidget, CombatHudWidget |
| Teleport.Confirmation |  | TeleportConfirmationWidget |
| Teleport.Filters |  | TeleportSpireWidget |
| Teleport.Menu |  | TeleportMenuWidget |
| Teleport.Targets |  | TeleportShardWidget |
| Teleport.Worlds |  | TeleportSpireWidget |
| Teleport.Zones |  | TeleportSpireWidget |
| Tool.GlobalSettings |  | BuildModeHUDWidget |
| VoxelOwnership.Ownership |  | VoxelVisionWidget |
| VoxelOwnership.PurchasedTemplates |  | VoxelVisionWidget |
| WorldMap.ClaimFiltersSearchBar |  | ClaimFiltersWidget |
| WorldMap.DepthInfo |  | MapDepthWidget |