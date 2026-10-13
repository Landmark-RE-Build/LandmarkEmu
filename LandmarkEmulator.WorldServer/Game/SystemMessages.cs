using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System.Collections.Generic;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The client's own system lines: PacketShowSystemMessage (0x69, reader FUN_142b8b220, handled in the zone dispatcher
    /// case 0x2f) {u32 text id, string text key, u32 n × {string name, string value, u32 value text id}, u32, u32}.
    /// The client localizes the line itself — the key (CodeStringMappings) or else the id — fills [*name*] placeholders
    /// from the parameters (a parameter's value text id, when set, replaces its value string) and shows it through
    /// BaseClient::ShowSystemMessage (FUN_142cbd1b0 → FUN_143353140), the same sink its own system lines use.
    /// </summary>
    public static class SystemMessages
    {
        /// <summary>Shows client text <paramref name="textId"/> with [*name*] parameters filled from <paramref name="parameters"/>.</summary>
        public static void Show(WorldSession session, uint textId, params (string Name, string Value)[] parameters)
            => Send(session, textId, "", parameters);

        /// <summary>Shows the client text named <paramref name="key"/> (a CodeStringMappings key).</summary>
        public static void ShowKey(WorldSession session, string key, params (string Name, string Value)[] parameters)
            => Send(session, 0, key, parameters);

        private static void Send(WorldSession session, uint textId, string key, (string Name, string Value)[] parameters)
        {
            var output = new List<byte>();
            var w = new GamePacketWriter(output);
            w.Write(textId);
            w.Write(key ?? "");
            w.Write((uint)parameters.Length);
            foreach (var (name, value) in parameters)
            {
                w.Write(name ?? "");
                w.Write(value ?? "");
                w.Write(0u);
            }
            w.Write(0u);
            w.Write(0u);
            session.EnqueueMessage(ZoneMessageOpcode.ShowSystemMessage, output.ToArray());
        }
    }
}
