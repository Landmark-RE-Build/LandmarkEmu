using LandmarkEmulator.WorldServer;
using LandmarkEmulator.WorldServer.Game;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Admin entries (Game/Admin.cs, review 2026-10-18b #2): <c>account:</c> / <c>character:</c> entries are decided by
    /// their own namespace only; a bare entry matches either and goes through the contest rule (needs the databases, so
    /// only its match is tested here).
    /// </summary>
    public class AdminRuleTests
    {
        [Theory]
        [InlineData("account:Robin", "Account", "Robin")]
        [InlineData("ACCOUNT: Robin ", "Account", "Robin")]
        [InlineData("character:RobinHood", "Character", "RobinHood")]
        [InlineData("Character:RobinHood", "Character", "RobinHood")]
        [InlineData("RobinHood", "Either", "RobinHood")]
        [InlineData(" tester2 ", "Either", "tester2")]
        [InlineData("account:", "Account", "")]
        public void ParseEntry_ReadsThePrefix(string entry, string kind, string name)
        {
            (Admin.EntryKind k, string n) = Admin.ParseEntry(entry);
            Assert.Equal(kind, k.ToString());
            Assert.Equal(name, n);
        }

        [Theory]
        // account: only the account username counts; a character of that name (an impostor's) does not.
        [InlineData("account:Robin", "robin", "Anyone", "Yes")]
        [InlineData("account:Robin", "impostor", "Robin", "No")]
        // character: only the character name counts; an account of that name (an impostor's) does not.
        [InlineData("character:RobinHood", "Robin", "ROBINHOOD", "Yes")]
        [InlineData("character:RobinHood", "RobinHood", "Other", "No")]
        // A bare name matches either, subject to the contest rule.
        [InlineData("RobinHood", "Robin", "RobinHood", "IfUncontested")]
        [InlineData("Robin", "Robin", "Someone", "IfUncontested")]
        [InlineData("Robin", "Other", "Someone", "No")]
        // An empty name names nobody.
        [InlineData("account:", "", "", "No")]
        [InlineData("character:", null, null, "No")]
        public void Match_PrefixedEntriesUseOneNamespace(string entry, string account, string character, string expected)
        {
            (Admin.EntryKind kind, string name) = Admin.ParseEntry(entry);
            Assert.Equal(expected, Admin.Match(kind, name, account, character).ToString());
        }

        [Fact]
        public void Entries_JoinsBothSettings()
        {
            var config = new WorldServerConfiguration
            {
                Admins = new List<string> { "account:tester2", "", "  character:RobinHood " },
                AdminList = "account:Bob, ,character:Carol",
            };
            Assert.Equal(new[] { "account:tester2", "character:RobinHood", "account:Bob", "character:Carol" },
                Admin.Entries(config).ToArray());
        }
    }
}
