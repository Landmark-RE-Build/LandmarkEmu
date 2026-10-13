using LandmarkEmulator.Shared.Network;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The tint definitions: ArtData 0x116 TintDefinitionManagerData (wire 16 01, reader FUN_142bae270: eight lists).
    /// Its handler (FUN_142c6ec20 → FUN_143c113c0) REPLACES the client's TintDefinitionManager (DAT_145351d90) with the
    /// packet's lists, and every tint the client draws is looked up there by alias hash (FUN_143c10110: key +0x268, type
    /// +0x26c or "ANY"): prop tints (PlayerUpdate 0x7A SetPropTint), item and character default tints. Eight empty lists
    /// therefore left the table empty (0 entries, read in the running client 2026-10-13) and no tint ever showed.
    /// The content is the same ArtData the login server tunnels (AuthServer Resources/ArtTints.json, linked into this
    /// build); the client reads the lists in the same order:
    /// <list type="number">
    /// <item>{i32, string} (1114)</item>
    /// <item>{i32, i32} (1114)</item>
    /// <item>override groups {i32, i32, i32, list {i32, i32}}</item>
    /// <item>{i64, i32, i32, i32, i32}</item>
    /// <item>{string} ×2 lists</item>
    /// <item>tint groups (the alias table, FUN_142b96540): {i32 id, string alias, string semantic group, list {string
    /// semantic, i32, i32, i32, string edit type, i32, i32 r, g, b, a}, u32 alias hash +0x268, u32 type hash +0x26c}</item>
    /// <item>{i32, i32}</item>
    /// </list>
    /// About 736 KB raw (compressed per fragment on the wire), sent once per login.
    /// </summary>
    public static class ArtTints
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        private static readonly Lazy<byte[]> body = new(Build);

        /// <summary>The packet body (after the opcode); eight empty lists if the file is missing.</summary>
        public static byte[] Body => body.Value;

        private static byte[] Build()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Resources", "ArtTints.json");
            if (!File.Exists(path))
            {
                log.Warn($"{path} is missing: tints will not show in the client.");
                return new byte[8 * 4];
            }
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = doc.RootElement;
            var output = new List<byte>(800_000);
            var w = new GamePacketWriter(output);

            void I(JsonElement e, string name) => w.Write(unchecked((uint)e.GetProperty(name).GetInt64()));
            void S(JsonElement e, string name) => w.Write(e.GetProperty(name).GetString() ?? "");
            void List(string name, Action<JsonElement> item)
            {
                JsonElement list = root.GetProperty(name);
                w.Write((uint)list.GetArrayLength());
                foreach (JsonElement e in list.EnumerateArray())
                    item(e);
            }

            List("Unknown0", e => { I(e, "Unknown0"); S(e, "Unknown1"); });
            List("Unknown1", e => { I(e, "Unknown0"); I(e, "Unknown1"); });
            List("ArtTintOverrideGroups", e =>
            {
                I(e, "Unknown0"); I(e, "Unknown1"); I(e, "Unknown2");
                JsonElement entries = e.GetProperty("Unknown3");
                w.Write((uint)entries.GetArrayLength());
                foreach (JsonElement o in entries.EnumerateArray())
                {
                    I(o, "Unknown0"); I(o, "Unknown1");
                }
            });
            List("Unknown3", e =>
            {
                w.Write(e.GetProperty("Unknown0").GetInt64());
                I(e, "Unknown1"); I(e, "Unknown2"); I(e, "Unknown3"); I(e, "Unknown4");
            });
            List("Unknown4", e => S(e, "Unknown0"));
            List("Unknown5", e => S(e, "Unknown0"));
            List("ArtTintGroups", e =>
            {
                I(e, "Id"); S(e, "AliasName"); S(e, "SemanticGroup");
                JsonElement tints = e.GetProperty("ArtTints");
                w.Write((uint)tints.GetArrayLength());
                foreach (JsonElement t in tints.EnumerateArray())
                {
                    S(t, "SemanticName"); I(t, "Unknown1"); I(t, "Unknown2"); I(t, "Unknown3"); S(t, "EditType");
                    I(t, "Unknown4"); I(t, "R"); I(t, "G"); I(t, "B"); I(t, "A");
                }
                I(e, "Unknown3"); I(e, "Unknown4");
            });
            List("Unknown7", e => { I(e, "Unknown0"); I(e, "Unknown1"); });

            log.Info($"Tint definitions: {root.GetProperty("ArtTintGroups").GetArrayLength()} aliases, {output.Count} bytes.");
            return output.ToArray();
        }
    }
}
