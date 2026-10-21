using LandmarkEmulator.WorldServer.Game;
using Xunit;

namespace LandmarkEmulator.Tests.Rules
{
    /// <summary>
    /// ClientLua.CommaSafe: the code the server runs in the client travels as one comma-separated argument, so commas are
    /// written as the Lua decimal escape \044 inside load("...")(). A Lua decimal escape takes up to three digits, so the
    /// escape must always be three digits long or a digit after the comma runs into it.
    /// </summary>
    public class ClientLuaTests
    {
        [Fact]
        public void CodeWithoutCommas_IsSentUnchanged()
            => Assert.Equal("print('hi')", ClientLua.CommaSafe("print('hi')"));

        [Fact]
        public void CommaBeforeADigit_UsesTheThreeDigitEscape()
        {
            string safe = ClientLua.CommaSafe("string.sub(s,1441)");
            Assert.Equal("load(\"string.sub(s\\0441441)\")()", safe);
            Assert.False(safe.Contains(","));
        }

        [Fact]
        public void EveryCommaAndLineBreak_IsEscaped()
        {
            string safe = ClientLua.CommaSafe("f(1,2,3)\nprint(\"a\\b\")");
            Assert.Equal("load(\"f(1\\0442\\0443)\\nprint(\\\"a\\\\b\\\")\")()", safe);
            Assert.False(safe.Contains(","));
        }

        [Fact]
        public void DevCode_NativeAndFileLoaders_AreRefused()
        {
            Assert.Equal("loadlib", ClientLua.ExternalBrowserCall("package.loadlib('x.dll', 'f')()"));
            Assert.Equal("require(", ClientLua.ExternalBrowserCall("local m = require('socket')"));
            Assert.Equal("dofile", ClientLua.ExternalBrowserCall("dofile('C:/x.lua')"));
            Assert.Equal("loadfile", ClientLua.ExternalBrowserCall("loadfile('x.lua')()"));
            Assert.Null(ClientLua.ExternalBrowserCall("print('this is required')"));
        }
    }
}
