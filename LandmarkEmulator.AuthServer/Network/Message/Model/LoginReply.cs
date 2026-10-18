using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.AuthServer.Network.Message.Model
{
    /// <summary>
    /// LoginReply (0x02, login protocol LoginUdp_10: the 0.589 and 0.695 clients). Binary reader FUN_143a54c80 (0.695;
    /// 0.589 FUN_14399cda0, same code), handler FUN_143a51800; the XML reader FUN_143a57cb0 names the fields [P]:
    /// bool LoggedIn, u32 Status, u32 ResultCode, bool IsMember, bool IsInternal, string Namespace, then
    /// <see cref="AccountFeatures"/> (FUN_143a55800), <see cref="ApplicationPayload"/> (FUN_142c34680: u32 n + n bytes) and
    /// <see cref="ErrorDetails"/> (FUN_143a55a90). The server sends the three trailing fields empty.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-18 Write sent three u64 zeros after Namespace (24 bytes) for the three fields: the client reads the
    /// three empty counts (12 bytes) and ignores the rest of the frame (FUN_143a51800 only checks for an overrun), so
    /// writing exactly the three fields changes nothing the client sees.
    /// </remarks>
    [AuthMessage(AuthMessageOpcode.LoginReply, ProtocolVersion.LoginUdp_10)]
    public class LoginReply : IWritable, IReadable
    {
        /// <summary>
        /// One AccountFeatures entry (HashListMap keyed by a u32 "Key"; record FUN_143a54370, XML FUN_143a576e0 [P]):
        /// u32 Key (the map key), then u32 Id, bool Active, u32 RemainingCount, string RawData.
        /// </summary>
        public class AccountFeature : IWritable, IReadable
        {
            public uint Key { get; set; }
            public uint Id { get; set; }
            public bool Active { get; set; }
            public uint RemainingCount { get; set; }
            public string RawData { get; set; } = "";

            public void Read(GamePacketReader reader)
            {
                Key = reader.ReadUInt();
                Id = reader.ReadUInt();
                Active = reader.ReadBool();
                RemainingCount = reader.ReadUInt();
                RawData = reader.ReadString();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Key);
                writer.Write(Id);
                writer.Write(Active);
                writer.Write(RemainingCount);
                writer.Write(RawData);
            }
        }

        /// <summary>One ErrorDetails entry (FUN_143a55a90: two strings, XML "name" / "value" FUN_143a568b0 [P]).</summary>
        public class ErrorDetail : IWritable, IReadable
        {
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";

            public void Read(GamePacketReader reader)
            {
                Name = reader.ReadString();
                Value = reader.ReadString();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(Name);
                writer.Write(Value);
            }
        }

        public bool LoggedIn { get; set; }
        public uint Status { get; set; }
        /// <summary>"ResultCode" in the client's XML reader.</summary>
        public uint Result { get; set; }
        public bool IsMember { get; set; }
        public bool IsInternal { get; set; }
        public string Namespace { get; set; } = "soe";
        public List<AccountFeature> AccountFeatures { get; set; } = new();
        /// <summary>"ApplicationPayload": u32 length + bytes (empty).</summary>
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();
        public List<ErrorDetail> ErrorDetails { get; set; } = new();

        /// <summary>A forged count never makes Read allocate more entries than the frame could hold.</summary>
        private static uint Bounded(GamePacketReader reader, uint count, uint minBytes)
            => count > reader.BytesRemaining / minBytes ? throw new System.IO.InvalidDataException($"{count} entries refused.") : count;

        public void Read(GamePacketReader reader)
        {
            LoggedIn = reader.ReadBool();
            Status = reader.ReadUInt();
            Result = reader.ReadUInt();
            IsMember = reader.ReadBool();
            IsInternal = reader.ReadBool();
            Namespace = reader.ReadString();

            AccountFeatures = new();
            uint features = Bounded(reader, reader.ReadUInt(), 17u);
            for (uint i = 0; i < features; i++)
            {
                var feature = new AccountFeature();
                feature.Read(reader);
                AccountFeatures.Add(feature);
            }

            uint length = Bounded(reader, reader.ReadUInt(), 1u);
            Payload = reader.ReadBytes(length);

            ErrorDetails = new();
            uint details = Bounded(reader, reader.ReadUInt(), 8u);
            for (uint i = 0; i < details; i++)
            {
                var detail = new ErrorDetail();
                detail.Read(reader);
                ErrorDetails.Add(detail);
            }
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(LoggedIn);
            writer.Write(Status);
            writer.Write(Result);
            writer.Write(IsMember);
            writer.Write(IsInternal);
            writer.Write(Namespace);

            writer.Write((uint)AccountFeatures.Count);
            AccountFeatures.ForEach(f => f.Write(writer));

            byte[] payload = Payload ?? System.Array.Empty<byte>();
            writer.Write((uint)payload.Length);
            writer.WriteBytes(payload);

            writer.Write((uint)ErrorDetails.Count);
            ErrorDetails.ForEach(d => d.Write(writer));
        }
    }

    /// <summary>
    /// LoginReply for login protocol LoginUdp_9 (an older client than the two in our decompiles, which both speak
    /// LoginUdp_10): as <see cref="LoginReply"/> without ResultCode and with one byte array, the payload [I: this
    /// model's layout from before the decompiles; no LoginUdp_9 reader to check it against].
    /// </summary>
    [AuthMessage(AuthMessageOpcode.LoginReply, ProtocolVersion.LoginUdp_9)]
    public class LoginReply_9 : IWritable, IReadable
    {
        public bool LoggedIn { get; set; }
        public uint Status { get; set; }
        public bool IsMember { get; set; }
        public bool IsInternal { get; set; }
        public string Namespace { get; set; } = "soe";
        /// <summary>u32 length + bytes (empty).</summary>
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();

        public void Read(GamePacketReader reader)
        {
            LoggedIn = reader.ReadBool();
            Status = reader.ReadUInt();
            IsMember = reader.ReadBool();
            IsInternal = reader.ReadBool();
            Namespace = reader.ReadString();
            uint length = reader.ReadUInt();
            if (length > reader.BytesRemaining)
                throw new System.IO.InvalidDataException($"Payload of {length} bytes refused.");
            Payload = reader.ReadBytes(length);
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(LoggedIn);
            writer.Write(Status);
            writer.Write(IsMember);
            writer.Write(IsInternal);
            writer.Write(Namespace);
            byte[] payload = Payload ?? System.Array.Empty<byte>();
            writer.Write((uint)payload.Length);
            writer.WriteBytes(payload);
        }
    }
}
