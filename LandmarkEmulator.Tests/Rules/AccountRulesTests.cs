using LandmarkEmulator.WebAPI;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// The sign-up rules every account-creating path applies (WebAPI AccountRules: /srp/register, /create, the login
    /// server console's account create), the same as LaunchPad's and the website's client-side checks.
    /// </summary>
    public class AccountRulesTests
    {
        [Theory]
        [InlineData("Alice", true)]
        [InlineData("al_1", true)]
        [InlineData("ab", false)]
        [InlineData("has space", false)]
        [InlineData("dash-name", false)]
        [InlineData("abcdefghijklmnopqrstuvwxyz0123456", false)]
        [InlineData(null, false)]
        public void Names(string name, bool ok) => Assert.Equal(ok, AccountRules.CheckName(name) == null);

        [Theory]
        [InlineData("123456", true)]
        [InlineData("12345", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Passwords(string password, bool ok) => Assert.Equal(ok, AccountRules.CheckPassword(password) == null);

        [Fact]
        public void Check_ReportsTheNameFirst()
            => Assert.Equal(AccountRules.CheckName("x"), AccountRules.Check("x", "1"));
    }
}
