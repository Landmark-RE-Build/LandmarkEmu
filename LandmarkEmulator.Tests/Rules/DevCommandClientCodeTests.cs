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
    /// The dev commands that run code in a game client (lua, luacode, luas, send; DevCommands.ClientCode.cs) reach only the
    /// caller and admins and never name a desktop-browser native, unless DevClientCodeOnOthers allows it.
    /// </summary>
    public class DevCommandClientCodeTests
    {
        private static readonly DevCommands.Source Console = new("test");

        // ---- Client code -------------------------------------------------------------------------------------------

        private static int ScriptPackets(WorldSession session)
            => FakeWorld.Outgoing(session).Count(o => o.Opcode == ZoneMessageOpcode.FirstTimeEventBase);

        [Fact]
        public void ClientCode_ReachesOnlyAdmins_ByDefault()
        {
            using var world = new FakeWorld(c => c.Admins = new List<string> { "character:Boss" });
            WorldSession boss = world.Session(0x100, "Boss"), pleb = world.Session(0x101, "Pleb");
            List<string> replies = DevCommands.Run(new[] { ("luacode print('hi')", Console) }, new List<WorldSession> { boss, pleb });
            Assert.Equal(2, ScriptPackets(boss));     // BindCommandToLua + Ui.ProcessChatCommand
            Assert.Equal(0, ScriptPackets(pleb));
            Assert.Equal(1, replies.Count);
            Assert.True(replies[0].StartsWith("luacode: refused for Pleb"), replies[0]);
        }

        [Fact]
        public void ClientCode_ReachesTheCaller_EvenWhenNotAnAdmin()
        {
            using var world = new FakeWorld();
            WorldSession pleb = world.Session(0x101, "Pleb");
            List<string> replies = DevCommands.Run(new[] { ("@Pleb luas Ui.DoCommand x", new DevCommands.Source("test", pleb)) },
                new List<WorldSession> { pleb });
            Assert.Equal(1, ScriptPackets(pleb));
            Assert.Empty(replies);
        }

        [Fact]
        public void ClientCode_NamingADesktopBrowserNative_IsRefusedForEveryone()
        {
            using var world = new FakeWorld(c => c.Admins = new List<string> { "character:Boss" });
            WorldSession boss = world.Session(0x100, "Boss");
            foreach (string line in new[] { "luacode Ui.OpenSupportUrl()", "luas BrowseTo http://example.invalid", "lua Ui.LaunchBrowser",
                         "luacode InGameBrowser:showhelppage()" })
            {
                List<string> replies = DevCommands.Run(new[] { (line, Console) }, new List<WorldSession> { boss });
                Assert.Equal(1, replies.Count, line);
                Assert.True(replies[0].Contains("desktop web browser"), replies[0]);
            }
            var sent = FakeWorld.Outgoing(boss);
            Assert.Equal(0, sent.Count(o => o.Opcode == ZoneMessageOpcode.FirstTimeEventBase || o.Opcode == ZoneMessageOpcode.CommandBase));
        }

        [Fact]
        public void TheSetting_LiftsBothChecks()
        {
            using var world = new FakeWorld(c => c.DevClientCodeOnOthers = true);
            WorldSession pleb = world.Session(0x101, "Pleb");
            List<string> replies = DevCommands.Run(new[] { ("luacode Ui.OpenSupportUrl()", Console) }, new List<WorldSession> { pleb });
            Assert.Empty(replies);
            Assert.Equal(2, ScriptPackets(pleb));
        }

        [Fact]
        public void TheDevClientLuaEntryPoints_RefuseBrowserNatives_TheServersOwnDoNot()
        {
            using var world = new FakeWorld();
            WorldSession pleb = world.Session(0x101, "Pleb");
            Assert.False(ClientLua.DevRunCode(pleb, "Ui.OpenSupportUrl()"));
            Assert.False(ClientLua.DevRunWithArgs(pleb, "BindCommandToLua", "x", "Ui.LaunchBrowser()"));
            Assert.Equal(0, ScriptPackets(pleb));
            Assert.True(ClientLua.DevRunCode(pleb, "OpenMarketplaceTo('http://127.0.0.1:19001/')"));   // the in-game browser
            Assert.Equal(2, ScriptPackets(pleb));
        }
    }
}
