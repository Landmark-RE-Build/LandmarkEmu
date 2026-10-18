using LandmarkEmulator.AuthServer.Network.Message.Static;
using LandmarkEmulator.Shared.Network;

namespace LandmarkEmulator.AuthServer.Network.Message.Model.TunnelData
{
    /// <summary>
    /// Login tunnel type 7, server → client: one string. The name is ours [I] (no client class name); the handling is
    /// proven [P]: the 0.695 tunnel switch FUN_1430b5370 case 7 → FUN_1430acec0 reads {u8 type, string} and hands the
    /// string to FUN_1430b2d50, which passes it to FUN_142ce7050, an empty function (a single <c>ret</c>; 0.589 the same,
    /// FUN_142c4d5f0). The client therefore drops the text: it is never shown or stored. The server no longer sends it
    /// (it used to send a welcome line after LoginInit that no client ever displayed).
    /// </summary>
    [TunnelData(TunnelDataType.LoginMessage)]
    internal class LoginMessage : ITunnelData
    {
        public string Text { get; set; } = "";

        public uint GetSize() => (uint)(4 + Text.Length);

        public void Read(GamePacketReader reader)
        {
            Text = reader.ReadString();
        }

        public void Write(GamePacketWriter writer)
        {
            writer.Write(Text);
        }
    }
}
