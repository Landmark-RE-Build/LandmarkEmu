using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Shared.Game;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Character select dresses from the last equipped inventory via ItemAppearances (dump + Cosmetics outfits).
    /// </summary>
    public class CharacterSelectAppearanceTests
    {
        public CharacterSelectAppearanceTests()
        {
            // No dump file in unit tests: register built outfits only.
            ItemAppearances.RegisterBuiltClothing();
        }

        [Fact]
        public void BuiltOutfit_IsKnownToItemAppearances()
        {
            var look = ItemAppearances.Get(Cosmetics.SweaterGreenTan);
            Assert.NotNull(look);
            Assert.Equal(Cosmetics.OutfitEquipSlot, look.Value.EquipSlot);
            Assert.True(look.Value.Model.StartsWith("Char_Biped_[RACE][GENDER]_Entities_Holiday_Light_001"));
            Assert.Equal("Holiday_Light_00", look.Value.TintAlias);
        }

        [Fact]
        public void BodyFromEquipped_UsesLastEquippedCharClothing()
        {
            var items = new List<CharacterItemModel>
            {
                new() { ItemDefinitionId = Cosmetics.SweaterGreenTan, Container = ItemAppearances.EquippedContainer, Slot = 1 },
                new() { ItemDefinitionId = 3253, Container = ItemAppearances.EquippedContainer, Slot = 2 }, // mining pick: not Char_
                new() { ItemDefinitionId = Cosmetics.SweaterGreenTan, Container = 0, Slot = 0 }, // bag: ignored
            };

            List<ItemAppearances.Appearance> worn = ItemAppearances.BodyFromEquipped(items).ToList();
            Assert.Single(worn);
            Assert.Equal(Cosmetics.OutfitEquipSlot, worn[0].EquipSlot);
            Assert.Equal("Holiday_Light_00", worn[0].TintAlias);
        }

        [Fact]
        public void EveryCosmeticsOutfit_RegistersACharModel()
        {
            foreach (var outfit in Cosmetics.Outfits)
            {
                var look = ItemAppearances.Get(outfit.Id);
                Assert.True(look.HasValue, $"missing appearance for {outfit.Id} {outfit.Name}");
                Assert.Equal(Cosmetics.OutfitEquipSlot, look.Value.EquipSlot);
                Assert.True(look.Value.Model.StartsWith("Char_"));
                Assert.Equal(outfit.Tint, look.Value.TintAlias);
            }
        }
    }
}
