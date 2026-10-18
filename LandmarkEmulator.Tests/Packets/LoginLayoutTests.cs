using LandmarkEmulator.AuthServer.Network.Message.Model;
using LandmarkEmulator.AuthServer.Network.Message.Model.Shared;
using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using System.Linq;
using Xunit;
using Friend = LandmarkEmulator.AuthServer.Network.Message.Model.CharacterSelectInfoReply.Character.CharacterPayload.Friend;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// Login server bodies against the 0.695 client's readers ([P] layouts in the models' comments): the character
    /// payload's friends list (FUN_1430ae3a0 / FUN_1430ad9e0), its Status XML (FUN_1430b85f0), LoginReply's three trailing
    /// fields (FUN_143a54c80) and the login tunnel's payload framing.
    /// </summary>
    public class LoginLayoutTests
    {
        /// <summary>FUN_1430ae3a0 / FUN_1430ad9e0: u32 key, string, string, string, u64, bool, string.</summary>
        [Fact]
        public void Friend_KeyThreeStringsGuidOnlineStatus()
        {
            var friend = new Friend { Key = 3, Name = "Ann", Text40 = "b", Text64 = "", CharacterGuid = 0x1122334455667788, Online = true, Status = "x" };
            Assert.Equal(new Layout().U32(3).Str("Ann").Str("b").Str("").U64(0x1122334455667788).Bool(true).Str("x").ToArray(),
                Wire.Write(friend));
        }

        /// <summary>FUN_1430b85f0 reads element Status with these five attributes; FUN_1430c43e0 compares WorldId.</summary>
        [Fact]
        public void FriendStatus_IsTheXmlTheClientParses()
            => Assert.Equal("<Status StatusId=\"0\" MiniGameType=\"0\" MiniGameNameId=\"0\" ZoneId=\"406\" WorldId=\"93\"/>",
                Friend.StatusXml(93, 406));

        /// <summary>
        /// The payload is a byte array (u32 length + bytes, Login::EntityDetails FUN_143a544a0) and FUN_1430ad7c0 ends with
        /// the friends list and a u64: the length counts UTF-8 bytes, and the friends come right before the last u64.
        /// </summary>
        [Fact]
        public void CharacterPayload_LengthPrefixAndFriendsBeforeTheLastU64()
        {
            var payload = new CharacterSelectInfoReply.Character.CharacterPayload { Name = "Zoë", ServerId = 0xAABBCCDDEEFF0011 };
            payload.Friends.Add(new Friend { Key = 0, Name = "F", CharacterGuid = 5, Online = false });
            byte[] written = Wire.Write(payload);
            uint length = System.BitConverter.ToUInt32(written, 0);
            Assert.Equal((uint)written.Length - 4u, length);
            Assert.Equal(length, payload.GetSize());
            byte[] friendThenQ = new Layout().U32(1).U32(0).Str("F").Str("").Str("").U64(5).Bool(false).Str("")
                .U64(0xAABBCCDDEEFF0011).ToArray();
            Assert.Equal(friendThenQ, written.Skip(written.Length - friendThenQ.Length).ToArray());
            Assert.Equal(new Layout().U32(4).Raw(System.Text.Encoding.UTF8.GetBytes("Zoë")).ToArray(), written.Skip(4).Take(8).ToArray());

            var (read, left) = Wire.Read<CharacterSelectInfoReply.Character.CharacterPayload>(written);
            Assert.Equal(0u, left);
            Assert.Equal("F", read.Friends.Single().Name);
        }

        /// <summary>The smallest friend records (29 bytes each: every string empty) read back, however many.</summary>
        [Fact]
        public void CharacterPayload_ManySmallestFriends_ReadBack()
        {
            var payload = new CharacterSelectInfoReply.Character.CharacterPayload { Name = "A", ServerId = 7 };
            for (uint i = 0; i < 5; i++)
                payload.Friends.Add(new Friend { Key = i, Name = "", Text40 = "", Text64 = "", CharacterGuid = i, Status = "" });
            Assert.Equal(29u, payload.Friends[0].GetSize());
            var (read, left) = Wire.Read<CharacterSelectInfoReply.Character.CharacterPayload>(Wire.Write(payload));
            Assert.Equal(0u, left);
            Assert.Equal(5, read.Friends.Count);
            Assert.Equal(7ul, read.ServerId);
        }

        /// <summary>FUN_143a54c80: after Namespace, AccountFeatures (u32 n), ApplicationPayload (u32 length), ErrorDetails (u32 n).</summary>
        [Fact]
        public void LoginReply_EndsWithThreeEmptyFields()
        {
            byte[] written = Wire.Write(new LoginReply { LoggedIn = true, Status = 1, Namespace = "soe" });
            Assert.Equal(new Layout().Bool(true).U32(1).U32(0).Bool(false).Bool(false).Str("soe").U32(0).U32(0).U32(0).ToArray(), written);
        }

        /// <summary>The tunnel: u64 server id, then the payload byte array whose first byte is the type.</summary>
        [Fact]
        public void TunnelPacket_PayloadCountsTypeAndBody()
        {
            var packet = new TunnelPacketServerToClient { ServerId = 9, Type = (TunnelDataType)0x7F, Raw = new byte[] { 1, 2, 3 } };
            byte[] written = Wire.Write(packet);
            Assert.Equal(new Layout().U64(9).U32(4).U8(0x7F).Raw(1, 2, 3).ToArray(), written);

            var (read, left) = Wire.Read<TunnelPacketServerToClient>(written);
            Assert.Equal(0u, left);
            Assert.Null(read.Data);
            Assert.Equal(new byte[] { 1, 2, 3 }, read.Raw);
        }

        /// <summary>A known type is parsed into its model and written back from it.</summary>
        [Fact]
        public void TunnelPacket_KnownTypeParsesItsModel()
        {
            byte[] body = new Layout().Str("hello").ToArray();
            byte[] written = new Layout().U64(0).U32((uint)body.Length + 1).U8((byte)TunnelDataType.LoginMessage).Raw(body).ToArray();
            var (read, left) = Wire.Read<TunnelPacketServerToClient>(written);
            Assert.Equal(0u, left);
            Assert.NotNull(read.Data);
            Assert.Equal(written, Wire.Write(read));
        }
    }
}
