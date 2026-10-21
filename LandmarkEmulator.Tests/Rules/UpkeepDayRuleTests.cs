using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using System;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Build site upkeep login days (Game/SiteUpkeep.cs): live counted a day from midnight to midnight Pacific time
    /// ("12:00:00AM to 11:59:59 PM Pacific Time", developers, Upcoming Claim Upkeep Changes, 17 Mar 2015); setting
    /// UpkeepTimeZone.
    /// </summary>
    public class UpkeepDayRuleTests : IDisposable
    {
        private readonly FakeWorld world = new();

        public void Dispose() => world.Dispose();

        private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        [Fact]
        public void EveningInCalifornia_IsStillThatDay_ThoughUtcHasMovedOn()
        {
            // 21 Oct 2026 03:00 UTC is 20 Oct 20:00 in California (PDT, UTC-7).
            DateTime utc = new(2026, 10, 21, 3, 0, 0, DateTimeKind.Utc);
            uint utcDay = SiteUpkeep.DayNumber(utc, TimeZoneInfo.Utc), pacificDay = SiteUpkeep.DayNumber(utc, Pacific);
            Assert.Equal(utcDay - 1, pacificDay);
            Assert.Equal(pacificDay, SiteUpkeep.DayNumber(new DateTime(2026, 10, 20, 7, 0, 0, DateTimeKind.Utc), Pacific));
            Assert.Equal(pacificDay + 1, SiteUpkeep.DayNumber(new DateTime(2026, 10, 21, 7, 0, 0, DateTimeKind.Utc), Pacific));
        }

        [Fact]
        public void Setting_PicksTheZone_UnknownMeansUtc()
        {
            world.Config.UpkeepTimeZone = "America/Los_Angeles";
            Assert.Equal(Pacific.BaseUtcOffset, SiteUpkeep.DayZone.BaseUtcOffset);
            world.Config.UpkeepTimeZone = "Nowhere/Nothing";
            Assert.Equal(TimeZoneInfo.Utc, SiteUpkeep.DayZone);
            world.Config.UpkeepTimeZone = "";
            Assert.Equal(TimeZoneInfo.Utc, SiteUpkeep.DayZone);
        }
    }
}
