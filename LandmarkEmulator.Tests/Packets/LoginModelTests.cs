using LandmarkEmulator.AuthServer.Network.Message.Model;
using LandmarkEmulator.AuthServer.Network.Message.Model.TunnelData;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Tests.Support;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// Login-side models whose fields were named from the 0.695 client on 2026-10-18 (docs/unknowns.md "Login and
    /// auth"): the layouts are built from the client's readers / writers, not from the server's code.
    /// </summary>
    public class LoginModelTests
    {
        /// <summary>
        /// SHA-256 of the tint definitions body as the server sent it before the fields were named (735,806 bytes, from
        /// the old Unknown-keyed ArtTints.json). Renaming the JSON keys and models must not change one byte.
        /// </summary>
        private const string ArtDataSha256 = "a4b6e8e7c39e4062f1a8f1dce779f85e12df896d072a32d8995203a9f579870d";

        private static byte[] Write(Action<GamePacketWriter> write) => Wire.Write(write);

        private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

        [Fact]
        public void ArtData_LoginAndZoneWritersSendTheCapturedBytes()
        {
            var artData = JsonConvert.DeserializeObject<ArtData>(Repo.Read("LandmarkEmulator.AuthServer", "Resources", "ArtTints.json"));
            byte[] login = Write(artData.Write);
            Assert.Equal(735_806, login.Length);
            Assert.Equal(ArtDataSha256, Sha(login));
            Assert.Equal((uint)login.Length, artData.GetSize());

            // The zone's ArtData 0x116 body is built from the same file by its own reader of the JSON.
            Assert.Equal(login, WorldServer.Game.ArtTints.Body);

            var again = new ArtData();
            var reader = new GamePacketReader(login);
            again.Read(reader);
            Assert.Equal(0u, reader.BytesRemaining);
            Assert.Equal(login, Write(again.Write));
        }

        /// <summary>The alias table invariants the client relies on (FUN_143cd0ef0 builds them the same way).</summary>
        [Fact]
        public void ArtData_KeysMatchTheirHashes()
        {
            var artData = JsonConvert.DeserializeObject<ArtData>(Repo.Read("LandmarkEmulator.AuthServer", "Resources", "ArtTints.json"));
            Assert.All(artData.Aliases, a => Assert.Equal(a.Key, a.AliasHash));
            Assert.All(artData.Aliases, a => Assert.Equal(ClientHash(a.AliasName, false), a.AliasHash));
            Assert.All(artData.Aliases, a => Assert.Equal(ClientHash(a.Slot, false), a.SlotHash));
            Assert.All(artData.Aliases, a => Assert.All(a.Semantics, t =>
            {
                Assert.Equal(ClientHash(t.SemanticGroup, true), t.SemanticGroupHash);
                Assert.Equal(ClientHash(t.StringParam, true), t.StringParamHash);
            }));
            Assert.All(artData.OverrideGroups, g => Assert.Equal(g.Key, g.GroupId));
            Assert.All(artData.ActorTintTypes, t => Assert.Equal(((long)t.ActorModelHash << 32) + t.ShaderNameHash, t.Key));
            Assert.All(artData.AliasNames, n => Assert.Equal(ClientHash(n.AliasName, false), n.AliasHash));
            Assert.True(artData.EditTypes.Exists(e => ClientHash(e, true) == artData.Aliases[0].Semantics[0].EditTypeHash));
        }

        /// <summary>The client's string hash (signed shifts; FUN_1437ebf3c upper-cases when <paramref name="upper"/>).</summary>
        private static int ClientHash(string text, bool upper)
        {
            int h = 0;
            foreach (char c in text)
            {
                h = unchecked((h + (upper ? char.ToUpperInvariant(c) : c)) * 0x401);
                h ^= h >> 6;
            }
            h = unchecked(h * 9);
            h ^= h >> 11;
            return unchecked(h * 0x8001);
        }

        /// <summary>ServerClaimBasicData, reader FUN_1430addc0: the record is read whole (Read used to skip it).</summary>
        [Fact]
        public void ClaimData_RecordLayout()
        {
            var data = new ClaimData();
            data.Claims.Add(new ClaimData.Claim { ClaimId = 1, OwnerCharacterId = 2, Field10 = 3, ServerId = 4, Name = "Site",
                Min = new[] { 1d, 2d, 3d }, Max = new[] { 4d, 5d, 6d }, Field74 = 9 });
            byte[] written = Write(data.Write);
            Assert.Equal(new Layout().U32(1).U64(1).U64(2).U64(3).U32(4).U32(0).U32(0).Bool(false).Str("Site")
                .F64(1).F64(2).F64(3).F64(4).F64(5).F64(6).U32(0).U32(9).ToArray(), written);
            Assert.Equal((uint)written.Length, data.GetSize());

            var again = new ClaimData();
            var reader = new GamePacketReader(written);
            again.Read(reader);
            Assert.Equal(0u, reader.BytesRemaining);
            Assert.Equal(written, Write(again.Write));
        }

        /// <summary>
        /// FUN_143a5aac0 / FUN_1430ad2f0 / FUN_142f59020: {u64 server, u32 payload length, u8 empire, u32 profile, u32
        /// gender, string name, slots, u32 skin tint, string, u32 race, u32 gender, u32 gear set}.
        /// </summary>
        [Fact]
        public void CharacterCreateRequest_ClientLayout()
        {
            byte[] payload = new Layout().U8(1).U32(5).U32(2).Str("Ann").U32(1).U32(2).U32(3).U32(4).U32(7).Str("Mat")
                .U32(1).U32(2).U32(11).ToArray();
            byte[] body = new Layout().U64(93).U32((uint)payload.Length).Raw(payload).ToArray();

            var (request, left) = Wire.Read<CharacterCreateRequest>(body);
            Assert.Equal(0u, left);
            Assert.Equal((uint)payload.Length, request.PayloadLength);
            Assert.Equal(93ul, request.ServerId);
            Assert.Equal(2u, request.SelectedGender);
            Assert.Equal("Ann", request.Name);
            Assert.Equal((2u, 3u, 4u), request.CustomisationOptions[0]);
            Assert.Equal(7u, request.SkinTint);
            Assert.Equal("Mat", request.MaterialOverride);
            Assert.Equal(11u, request.StartingOutfit);
        }

        /// <summary>FUN_1430b3cb0 / FUN_143a53e90: the u32 after the two ids counts the {locale, locale id, gateway id} bytes.</summary>
        [Fact]
        public void CharacterLoginRequest_PayloadLengthCountsTheTail()
        {
            byte[] payload = new Layout().Str("en_US").U32(1).U32(0).ToArray();
            var (request, left) = Wire.Read<CharacterLoginRequest>(new Layout().U64(5).U64(93).U32((uint)payload.Length).Raw(payload).ToArray());
            Assert.Equal(0u, left);
            Assert.Equal((uint)payload.Length, request.PayloadLength);
            Assert.Equal("en_US", request.Locale);
        }

        /// <summary>The exit URL must stay empty: a non-empty one opens the player's browser when the game closes.</summary>
        [Fact]
        public void CharacterLoginReply_ExitUrlEmptyByDefault()
            => Assert.Equal("", new CharacterLoginReply().Server.ClientExitUrl);
    }
}
