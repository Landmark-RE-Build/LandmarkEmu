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
    /// build; the model with every field's client evidence is AuthServer Model/TunnelData/ArtData.cs). The client
    /// reads the lists in this order (reader 0.695 FUN_1430af1e0 / zone FUN_142bae270):
    /// <list type="number">
    /// <item>AliasNames {i32 alias hash, string alias name} (1114) → manager +0x878</item>
    /// <item>AliasIds {i32 ArtTints.txt id, i32 alias hash} (1114) → +0x18e8</item>
    /// <item>OverrideGroups {i32 key = group id, i32 art source actor id, i32 group id, list {i32 tint type, i32 tint id}} → +0x39c8</item>
    /// <item>ActorTintTypes {i64 (model hash &lt;&lt; 32) + shader hash, i32 tint type, i32 model hash, i32 shader hash, i32 actor id} → +0x39f8</item>
    /// <item>SemanticNames, EditTypes: {string} ×2 lists → +0x230 / +0x550</item>
    /// <item>Aliases (the alias table, FUN_142c30030): {i32 key = alias hash, string alias, string slot, list {string
    /// semantic group, i32 group hash, i32 semantic name hash, i32 edit type hash, string param, i32 param hash, f32 ×4},
    /// u32 alias hash +0x268, u32 slot hash +0x26c} → +0x8</item>
    /// <item>PlayerTintOptions {i32 tint type, i32 tint id} → +0x3a28</item>
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

            void F(JsonElement e) => w.Write(e.GetSingle());
            void Strings(string name)
            {
                JsonElement list = root.GetProperty(name);
                w.Write((uint)list.GetArrayLength());
                foreach (JsonElement e in list.EnumerateArray())
                    w.Write(e.GetString() ?? "");
            }

            List("AliasNames", e => { I(e, "AliasHash"); S(e, "AliasName"); });
            List("AliasIds", e => { I(e, "AliasId"); I(e, "AliasHash"); });
            List("OverrideGroups", e =>
            {
                I(e, "Key"); I(e, "ArtSourceActorId"); I(e, "GroupId");
                JsonElement entries = e.GetProperty("Entries");
                w.Write((uint)entries.GetArrayLength());
                foreach (JsonElement o in entries.EnumerateArray())
                {
                    I(o, "ArtTintTypeId"); I(o, "ArtTintId");
                }
            });
            List("ActorTintTypes", e =>
            {
                w.Write(e.GetProperty("Key").GetInt64());
                I(e, "ArtTintTypeId"); I(e, "ActorModelHash"); I(e, "ShaderNameHash"); I(e, "ArtSourceActorId");
            });
            Strings("SemanticNames");
            Strings("EditTypes");
            List("Aliases", e =>
            {
                I(e, "Key"); S(e, "AliasName"); S(e, "Slot");
                JsonElement semantics = e.GetProperty("Semantics");
                w.Write((uint)semantics.GetArrayLength());
                foreach (JsonElement t in semantics.EnumerateArray())
                {
                    S(t, "SemanticGroup"); I(t, "SemanticGroupHash"); I(t, "SemanticNameHash"); I(t, "EditTypeHash");
                    S(t, "StringParam"); I(t, "StringParamHash");
                    foreach (JsonElement v in t.GetProperty("Values").EnumerateArray())
                        F(v);
                }
                I(e, "AliasHash"); I(e, "SlotHash");
            });
            List("PlayerTintOptions", e => { I(e, "ArtTintTypeId"); I(e, "ArtTintId"); });

            log.Info($"Tint definitions: {root.GetProperty("Aliases").GetArrayLength()} aliases, {output.Count} bytes.");
            return output.ToArray();
        }
    }
}
