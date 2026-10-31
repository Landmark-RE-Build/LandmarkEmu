using LandmarkEmulator.WorldServer.Game;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The 0.695 text file cut the two upkeep letters to 255 characters; the server gives them their endings back and
    /// leaves any other text alone (trace/0695/phaseE3-text.md F3).
    /// </summary>
    public class UpkeepLetterRuleTests
    {
        private static string Cut(string start, string end) => start + new string('x', 255 - start.Length - end.Length) + end;

        [Fact]
        public void CutLetters_GetTheirEndingsBack()
        {
            string expired = SiteUpkeep.Completed(562150, Cut("Dear [*player*],", "you will find thes"));
            Assert.True(expired.EndsWith("Thank you,<br>Build Site Management"));
            Assert.Contains("these in your Designs list.", expired);
            string extended = SiteUpkeep.Completed(564968, Cut("Dear [*Player*],", "will remain in the "));
            Assert.True(extended.EndsWith("to a maximum of 90 days.<br><br>Thank you,<br>Build Site Management"));
        }

        [Fact]
        public void OtherTexts_StayAsTheyAre()
        {
            Assert.Equal("A whole letter.", SiteUpkeep.Completed(562150, "A whole letter."));
            string otherCut = Cut("Lieber [*player*],", "something else");
            Assert.Equal(otherCut, SiteUpkeep.Completed(562150, otherCut));
            Assert.Null(SiteUpkeep.Completed(562150, null));
            string sameEndOtherId = Cut("Dear", "you will find thes");
            Assert.Equal(sameEndOtherId, SiteUpkeep.Completed(562148, sameEndOtherId));
        }
    }
}
