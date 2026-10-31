using LandmarkEmulator.WorldServer.Game;
using System.IO;
using System.Text;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// GuildChangeMemberRank 0x0A as the 0.695 client reads it (FUN_142ee4550; the dispatcher runs the handler only when
    /// the body is read to its end): u64 guild, u64 member, string, u64 changer, string, u32 rank, string, bool, bool, u32
    /// (trace/0695/phaseE3-wire-b.md F1).
    /// </summary>
    public class GuildChangeRankLayoutTests
    {
        private static string ReadString(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes((int)r.ReadUInt32()));

        [Fact]
        public void ChangeRank_IsReadToItsEnd()
        {
            byte[] body = Guilds.BuildChangeRank(0x11, 0x22, "Member", 0x33, "Changer", 4);
            using var r = new BinaryReader(new MemoryStream(body));
            Assert.Equal(Guilds.ChangeMemberRankSub, r.ReadUInt16());
            Assert.Equal(0x11ul, r.ReadUInt64());
            Assert.Equal(0x22ul, r.ReadUInt64());
            Assert.Equal("Member", ReadString(r));
            Assert.Equal(0x33ul, r.ReadUInt64());
            Assert.Equal("Changer", ReadString(r));
            Assert.Equal(4u, r.ReadUInt32());
            Assert.Equal("", ReadString(r));     // +0x68
            Assert.False(r.ReadBoolean());       // +0x80
            Assert.False(r.ReadBoolean());       // +0x81
            Assert.Equal(0u, r.ReadUInt32());    // +0x84
            Assert.Equal(r.BaseStream.Length, r.BaseStream.Position);
        }
    }
}
