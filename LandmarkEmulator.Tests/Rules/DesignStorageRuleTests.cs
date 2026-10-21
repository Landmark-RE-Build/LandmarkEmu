using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Design storage (Game/Templates.cs): Player Studio designs take no storage while PlayerStudioDesignsFreeStorage is on
    /// (live: wiki Designs, bought designs don't count toward the limit; developers' Hotfix Notes 17 Oct 2014, designs
    /// submitted to Player Studio no longer count against Voxel Storage). The database is never reached: the marketplace
    /// set and the owner's unlocks are filled in memory.
    /// </summary>
    public class DesignStorageRuleTests : IDisposable
    {
        private const ulong Owner = 0x7E5_0001, Listed = 0x501, Plain = 0x502;
        private readonly FakeWorld world = new();
        private static readonly FieldInfo MarketField = typeof(Commerce).GetField("marketDesigns", BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly FieldInfo GalleryField = typeof(Commerce).GetField("galleryClaims", BindingFlags.NonPublic | BindingFlags.Static)!;
        private readonly object previousMarket, previousGallery;

        public DesignStorageRuleTests()
        {
            previousMarket = MarketField.GetValue(null);
            previousGallery = GalleryField.GetValue(null);
            MarketField.SetValue(null, new HashSet<ulong> { Listed });
            GalleryField.SetValue(null, new HashSet<ulong>());
            // The owner's unlocks as loaded (none): no Player Studio purchases, so no copy lookup in the database.
            var cache = (ConcurrentDictionary<ulong, HashSet<(uint Kind, uint Value)>>)Private.Field(typeof(Unlocks), "cache");
            cache.TryAdd(Owner, new HashSet<(uint Kind, uint Value)>());
        }

        public void Dispose()
        {
            MarketField.SetValue(null, previousMarket);
            GalleryField.SetValue(null, previousGallery);
            world.Dispose();
        }

        private static TemplateModel Design(ulong id) => new()
        {
            Id = id, OwnerGuid = Owner, Name = "Tower", SizeX = 4, SizeY = 8, SizeZ = 4, Materials = "12:100"
        };

        [Fact]
        public void DesignOnPlayerStudio_IsFree_WhileTheRuleIsOn()
        {
            HashSet<ulong> free = Templates.FreeOfStorage(Owner, new[] { Design(Listed), Design(Plain) });
            Assert.Equal(new HashSet<ulong> { Listed }, free);
            Assert.False(Private.Call<bool>(typeof(Templates), "CountsAgainstStorage", Design(Listed)));
            Assert.True(Private.Call<bool>(typeof(Templates), "CountsAgainstStorage", Design(Plain)));
        }

        [Fact]
        public void RuleOff_EveryPlayerDesignCounts()
        {
            world.Config.PlayerStudioDesignsFreeStorage = false;
            Assert.Empty(Templates.FreeOfStorage(Owner, new[] { Design(Listed), Design(Plain) }));
            Assert.True(Private.Call<bool>(typeof(Templates), "CountsAgainstStorage", Design(Listed)));
        }

        [Fact]
        public void DesignerDesigns_NeverCount()
        {
            TemplateModel daybreak = Design(Plain);
            daybreak.AuthorName = "Landmark";
            Assert.False(Private.Call<bool>(typeof(Templates), "CountsAgainstStorage", daybreak));
        }

        /// <summary>
        /// A deleted character's designs follow the account (live: Wipe Q&amp;A 14 Mar 2016): the new character becomes the
        /// author of those the deleted one made; designs by others keep their author.
        /// </summary>
        [Theory]
        [InlineData("OldMe", "NewMe")]
        [InlineData("oldme", "NewMe")]
        [InlineData("Someone", "Someone")]
        [InlineData("", "")]
        public void AdoptedDesign_TakesTheNewCharacterAsAuthor_WhenTheDeletedOneMadeIt(string author, string expected)
            => Assert.Equal(expected, Templates.AdoptedAuthor(author, new[] { "OldMe" }, "NewMe"));

        [Fact]
        public void AccountDesigns_NotMovedWhileTheRuleIsOff()
        {
            world.Config.DesignsFollowAccount = false;
            var session = world.Session(Owner, "NewMe");
            Assert.Equal(0, Templates.AdoptAccountDesigns(session));
        }

        /// <summary>A bought copy is recognised by its product's source design: same name, size and materials.</summary>
        [Fact]
        public void PlayerStudioCopy_MatchesItsSourceByNameSizeAndMaterials()
        {
            TemplateModel source = Design(0x900), copy = Design(Plain);
            Assert.True(Templates.IsSameDesign(source, copy));
            copy.Materials = "12:101";
            Assert.False(Templates.IsSameDesign(source, copy));
            copy = Design(Plain);
            copy.SizeY = 9;
            Assert.False(Templates.IsSameDesign(source, copy));
        }
    }
}
