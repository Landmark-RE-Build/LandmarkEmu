using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Game;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// Dev commands (Game/DevCommands, DevCommands.Usage.cs): every command has a usage line, and a command missing a
    /// required argument, or given one that doesn't parse, answers "usage: …" instead of throwing.
    /// </summary>
    public class DevCommandUsageTests
    {
        private static readonly DevCommands.Source Console = new("test");

        /// <summary>Every top-level case label of the command switch, as the wiki generator reads them (24-space indent).</summary>
        private static List<string> CaseLabels()
            => Regex.Matches(Repo.Read("LandmarkEmulator.WorldServer", "Game", "DevCommands.cs"), "(?m)^ {24}case \"([^\"]+)\":")
                .Select(m => m.Groups[1].Value).ToList();

        [Fact]
        public void EveryCommand_HasAUsageLine_AndEveryUsageLineACommand()
        {
            List<string> labels = CaseLabels();
            Assert.True(labels.Count > 200, $"{labels.Count} case labels read");
            Assert.Empty(labels.Where(l => DevCommands.UsageOf(l) == null), "commands without a usage line");
            Assert.Empty(DevCommands.Usages.Keys.Where(k => !labels.Contains(k)), "usage lines without a command");
            Assert.Empty(DevCommands.Usages.Where(u => !u.Value.Contains(u.Key)).Select(u => u.Key), "usage lines that don't name their command");
        }

        [Fact]
        public void RequiredArguments_AreReadFromTheUsageLine()
        {
            Assert.Equal(2, DevCommands.RequiredArguments("give"));
            Assert.Equal(0, DevCommands.RequiredArguments("dig"));
            Assert.Equal(1, DevCommands.RequiredArguments("shutdown"));        // "<seconds> [message] | cancel"
            Assert.Equal(1, DevCommands.RequiredArguments("broadcast"));       // the shortest alternative
            Assert.Equal(1, DevCommands.RequiredArguments("terminal"));        // "<hub|c<index>|hex node>" is one group
            Assert.Equal(2, DevCommands.RequiredArguments("storyfaction"));    // "<0 enemy|1 neutral|2 ally>" is one group
            Assert.Equal(0, DevCommands.RequiredArguments("kick"));            // "@Name kick [seconds] …"
            Assert.Equal(0, DevCommands.RequiredArguments("tree"));            // nested optional groups
            Assert.Equal(10, DevCommands.RequiredArguments("blockcell"));
        }

        [Fact]
        public void EveryCommandWithRequiredArguments_AnswersItsUsage_OnEmptyArguments()
        {
            var noPlayers = new List<WorldSession>();
            var problems = new List<string>();
            foreach (string command in DevCommands.Usages.Keys.Where(c => DevCommands.RequiredArguments(c) > 0))
                foreach (string line in new[] { command, "@Nobody " + command })
                {
                    List<string> replies;
                    try
                    {
                        replies = DevCommands.Run(new[] { (line, Console) }, noPlayers);
                    }
                    catch (System.Exception e)
                    {
                        problems.Add($"{line}: threw {e.GetType().Name}: {e.Message}");
                        continue;
                    }
                    if (replies.Count != 1 || replies[0] != "usage: " + DevCommands.UsageOf(command))
                        problems.Add($"{line}: replied [{string.Join(" / ", replies)}]");
                }
            Assert.Empty(problems);
        }

        [Fact]
        public void ABadArgument_AnswersTheUsage()
        {
            List<string> replies = DevCommands.Run(new[] { ("timeofday noon", Console) }, new List<WorldSession>());
            Assert.Equal(1, replies.Count);
            Assert.True(replies[0].StartsWith("usage: timeofday <hour 0-24> (FormatException"), replies[0]);
        }
    }
}
