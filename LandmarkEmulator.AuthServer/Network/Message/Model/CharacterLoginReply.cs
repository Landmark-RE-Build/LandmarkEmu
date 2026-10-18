using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;

namespace LandmarkEmulator.AuthServer.Network.Message.Model
{
    [AuthMessage(AuthMessageOpcode.CharacterLoginReply, ProtocolVersion.LOGIN_ALL)]
    public class CharacterLoginReply : IWritable, IReadable
    {
        public class ServerInfo : IWritable, IReadable, ISize
        {
            public string ServerAddress { get; set; } = "";
            public string ServerTicket { get; set; } = "";
            public byte[] EncryptionKey { get; set; } = new byte[0];
            public ulong CharacterId { get; set; }
            public ulong Guid { get; set; }
            public string AccountName { get; set; } = "";
            public string CharacterName { get; set; } = "";
            /// <summary>
            /// The URL the client opens when the game closes [P]. The reply's payload reader FUN_1430ad6e0 stores it at
            /// login state +0x170 and FUN_1430b4ad0 copies it to game +0x325a8; when the game closes (game state 0x18 in
            /// FUN_142cd4e60) FUN_142d56760 opens a non-empty one in the player's web browser (ShellExecuteA "open",
            /// FUN_143b0e840), then exits the process. Name from the client's own term [I]: the reward entry
            /// RewardBundleEntryClientExitUrl prints "ClientExitUrl=%s" (FUN_143b84e60). Kept empty: the server never
            /// opens the player's browser.
            /// </summary>
            public string ClientExitUrl { get; set; } = "";

            public uint GetSize()
            {
                return (uint)(4u + ServerAddress.Length +
                    4u + ServerTicket.Length +
                    4u + EncryptionKey.Length +
                    8u +
                    8u +
                    4u + AccountName.Length +
                    4u + CharacterName.Length +
                    4u + ClientExitUrl.Length);
            }

            public void Read(GamePacketReader reader)
            {
                reader.ReadUInt(); // Size
                ServerAddress = reader.ReadString();
                ServerTicket = reader.ReadString();

                uint keyCount = reader.ReadUInt();
                EncryptionKey = new byte[keyCount];
                for (int i = 0; i < keyCount; i++)
                    EncryptionKey[i] = reader.ReadByte();

                CharacterId = reader.ReadULong();
                Guid = reader.ReadULong();
                AccountName = reader.ReadString();
                CharacterName = reader.ReadString();
                ClientExitUrl = reader.ReadString();
            }

            public void Write(GamePacketWriter writer)
            {
                writer.Write(GetSize());
                writer.Write(ServerAddress);
                writer.Write(ServerTicket);
                writer.Write((uint)EncryptionKey.Length);
                foreach (byte key in EncryptionKey)
                    writer.Write(key);
                writer.Write(CharacterId);
                writer.Write(Guid);
                writer.Write(AccountName);
                writer.Write(CharacterName);
                writer.Write(ClientExitUrl);
            }
        }

        public ulong CharacterId { get; set; }
        public ulong ServerId { get; set; }
        public CharacterLoginResult Result { get; set; }
        public ServerInfo Server { get; set; } = new();

        public void Read(GamePacketReader reader)
        {
            CharacterId = reader.ReadULong();
            ServerId = reader.ReadULong();
            Result = (CharacterLoginResult)reader.ReadUInt();
            Server.Read(reader);
        }
        
        public void Write(GamePacketWriter writer)
        {
            writer.Write(CharacterId);
            writer.Write(ServerId);
            writer.Write((uint)Result);
            Server.Write(writer);
        }
    }
}
