namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// Calculated stats every player starts with, taken from a live-server ClientUpdateUpdateCalculatedStats capture
    /// (names from CalculatedStatDefinitions.txt).
    /// </summary>
    public static class DefaultCalculatedStats
    {
        public static readonly (uint StatId, float Value)[] Player =
        {
            (1u, 1800f), // cStatTypeHealthBonus
            (2u, 0f), // cStatTypeCombatCrit
            (3u, 0f), // cStatTypeHarvestingArea
            (4u, 0f), // cStatTypeLightRadius
            (5u, 12f), // cStatTypeMovementSpeed (run speed; 0 stops running entirely)
            (6u, 0f), // cStatTypeJumpHeight
            (7u, 0f), // cStatTypeDiscovery
            (8u, 0f), // cStatTypeTreasureFind
            (9u, 0f), // cStatTypeArmor
            (10u, 70f), // cStatTypeDamage
            (11u, 0f), // cStatTypeMiningHarvestTier
            (12u, 0f), // cStatTypeHarvestingDamage
            (13u, 0f), // cStatTypeHarvestSpeed
            (14u, 0f), // cStatTypeStaminaBonus
            (15u, 0f), // cStatTypeReach
            (16u, 0f), // cStatTypeCraftingQuality
            (17u, 0f), // cStatTypeHarvestBonus
            (18u, 30f), // cStatTypeGrappleReinSpeed
            (19u, 60f), // cStatTypeGrappleMaxDistance
            (20u, 40f), // cStatTypeGrappleShootSpeed
            (21u, 2f), // cStatTypeGrappleCooldown
            (22u, 6f), // cStatTypeHealthRegenRate
            (23u, 0f), // cStatTypeStaminaRegenRate
            (24u, 160f), // cStatTypeManaBonus
            (25u, 0f), // cStatTypeManaRegenRate
            (26u, 0f), // cStatTypePropHarvestTier
            (27u, 0f), // cStatTypeDoubleJump
            (28u, 1f), // cStatTypeGlide
            (29u, 0f), // cStatTypeFly
            (30u, 0f), // cStatTypeDestruction
            (35u, 0f), // cStatTypeHarvestCrit
            (36u, 0f), // cStatTypeGathering
            (37u, 0f), // cStatTypeHaste
            (38u, 0f), // cStatTypeToughness
            (39u, 0f), // cStatTypeEffectPower
            (40u, 0f), // cStatTypeLifeSteal
            (41u, 0f), // cStatTypeEndurance
            (42u, 0f), // cStatTypeEnergyEfficiency
            (43u, 0f), // cStatTypeArmorRegen
            (44u, 0f), // cStatTypeArmorSteal
            (52u, 0f), // cStatTypePlantHarvestTier
            (53u, 0f), // cStatTeleportCooldown
            (54u, 50f), // cStatGuardBreak
            (55u, 0f), // cStatGuardStability
            (57u, 0f), // cStatTypeStrikethrough
            (60u, 0f), // cStatTypeStability
            (62u, 0f), // cstatTypeStability
        };
    }
}
