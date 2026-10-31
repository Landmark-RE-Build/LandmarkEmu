using LandmarkEmulator.Database.Character.Model;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Who may set a site's time (ClaimsPacketSetClaimTime 0xF9/0x40): the 0.695 client sends it when the player has bit
    /// 0x10 (Ultimate Power) on the site (FUN_142dd15d0), so the owner and Ultimate Power builders are accepted; anyone
    /// else gets Claim.SetTime.Error and the site's current time back (trace/0695/phaseE3-text.md F1).
    /// </summary>
    public class SiteTimeRuleTests : IDisposable
    {
        private const ulong Owner = 0x7B00_0001, Builder = 0x7B00_0002, Visitor = 0x7B00_0003, Site = 0x7B00_0100;
        private readonly FakeWorld world = new();
        private readonly ClaimModel site = new() { Id = Site, ZoneId = 935, OwnerGuid = Owner, TimeHour = -1, TimeRate = 1f,
            VisitorTimeHour = -1, VisitorTimeRate = 1f };
        private readonly List<ClaimModel> saved;
        private static List<ClaimModel> Claims_ => (List<ClaimModel>)Private.Field(typeof(Claims), "claims");
        private static Dictionary<ulong, Dictionary<ulong, uint>> Special => (Dictionary<ulong, Dictionary<ulong, uint>>)Private.Field(typeof(Claims), "special");

        public SiteTimeRuleTests()
        {
            lock (Private.Field(typeof(Claims), "sync"))
            {
                saved = Claims_.ToList();
                Claims_.Add(site);
                Special[Site] = new Dictionary<ulong, uint> { [Builder] = Claims.PermissionBuild | Claims.PermissionDecorate | Claims.PermissionUse };
            }
        }

        public void Dispose()
        {
            lock (Private.Field(typeof(Claims), "sync"))
            {
                Claims_.Clear();
                Claims_.AddRange(saved);
                Special.Remove(Site);
            }
            world.Dispose();
        }

        [Fact]
        public void OwnerAndUltimatePowerBuilder_MaySetTheTime()
        {
            // (SetTime itself writes the site to the database and builds the site's game info, which these tests
            // don't have: the rule it applies is checked.)
            Assert.True(Claims.MaySetTime(site, Owner));
            Assert.True(Claims.MaySetTime(site, Builder));
            Assert.False(Claims.MaySetTime(site, Visitor));
        }
    }
}
