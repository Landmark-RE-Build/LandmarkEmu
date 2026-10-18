using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LandmarkEmulator.WorldServer.Game
{
    /// <summary>
    /// The client's telemetry and external-service reports (TASKS E.7): live's collectors received these and nothing
    /// answered in play, so the server records them — one JSON line per report in Telemetry/&lt;date&gt;.jsonl beside the
    /// server (like Reports/) — decoded where the 0.695 writer is read, else as hex. Per player at most
    /// <see cref="PerMinute"/> reports a minute are kept (the rest are counted), so a chatty client can't fill the disk.
    /// Layouts (0.695 writers, [P]):
    /// <list type="bullet">
    /// <item>PacketClientLuaMetrics 0x8B (FUN_142c43d20 / FUN_142c32cc0): u32 count, count × {u32 key, string name, u32 value}.</item>
    /// <item>WallOfData 0xCE, u8 sub: 2 PlayerKeyboard (FUN_142c459d0: key counters, kept as hex), 4 UIEvent (FUN_1435d5130:
    /// 3 strings), 6 VoiceChatEvent (FUN_142fc5510: u32, u32), 8 Marketplace (FUN_1435d4f90: string, u32, u32, u32, string).</item>
    /// <item>CommerceSessionRequest 0x77 (FUN_142c440e0), SocialShare 0x104 sub 1 provider status {u32, u32} / sub 3
    /// publish {u32, u32, string, string} (FUN_143181bb0 / FUN_143181d50), ItemReview 0x10B OptInAccount {u64, u8, u8}
    /// (FUN_1435ec2c0), Command 0x3F RendererDump (header only), InGamePurchase 0x2A SpiceWebAuthUrlRequest {u32}.</item>
    /// </list>
    /// None of them is answered: the social-share and Spice web-auth replies would point the client at an outside web
    /// service (they open the player's browser), which this server never does.
    /// </summary>
    public static class Telemetry
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public const int PerMinute = 60;
        private static readonly object fileLock = new();
        private static readonly ConcurrentDictionary<ulong, (long Minute, int Count, int Dropped)> budget = new();

        /// <summary>Zone opcodes that are telemetry only (recorded and not answered).</summary>
        public static bool IsTelemetry(ZoneMessageOpcode opcode) => opcode is ZoneMessageOpcode.ClientLuaMetrics
            or ZoneMessageOpcode.WallOfDataBase or ZoneMessageOpcode.CommerceSessionRequest
            or ZoneMessageOpcode.SocialShareBase or ZoneMessageOpcode.ItemReviewBase;

        /// <summary>Records one report (the body after the opcode).</summary>
        public static void Record(WorldSession session, string kind, byte[] body)
        {
            if (!Admit(session))
                return;
            object fields;
            try
            {
                fields = Decode(kind, body);
            }
            catch (Exception)
            {
                fields = null;
            }
            var line = new Dictionary<string, object>
            {
                ["time"] = DateTime.UtcNow.ToString("o"),
                ["player"] = session.CharacterName ?? "",
                ["guid"] = session.PlayerGuid,
                ["kind"] = kind,
                ["fields"] = fields,
                ["hex"] = fields == null ? Convert.ToHexString(body) : null,
            };
            string json = JsonSerializer.Serialize(line, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            try
            {
                string folder = Path.Combine(AppContext.BaseDirectory, "Telemetry");
                lock (fileLock)
                {
                    Directory.CreateDirectory(folder);
                    File.AppendAllText(Path.Combine(folder, $"{DateTime.UtcNow:yyyy-MM-dd}.jsonl"), json + "\n");
                }
            }
            catch (IOException exception)
            {
                log.Warn($"Telemetry not written: {exception.Message}");
            }
            log.Debug($"Telemetry {kind} from {session.CharacterName}: {json.Length} bytes.");
        }

        /// <summary>A zone telemetry packet: names it by opcode (and sub) and records it.</summary>
        public static void RecordZone(WorldSession session, ZoneMessageOpcode opcode, byte[] body)
        {
            string kind = opcode switch
            {
                ZoneMessageOpcode.ClientLuaMetrics => "ClientLuaMetrics",
                ZoneMessageOpcode.WallOfDataBase => body.Length > 0 ? body[0] switch
                {
                    2 => "WallOfData.PlayerKeyboard",
                    4 => "WallOfData.UIEvent",
                    6 => "WallOfData.VoiceChatEvent",
                    8 => "WallOfData.Marketplace",
                    _ => $"WallOfData.{body[0]}"
                } : "WallOfData",
                ZoneMessageOpcode.CommerceSessionRequest => "CommerceSessionRequest",
                ZoneMessageOpcode.SocialShareBase => body.Length > 0 && body[0] == 1 ? "SocialShare.ProviderStatusRequest"
                    : body.Length > 0 && body[0] == 3 ? "SocialShare.PublishRequest" : "SocialShare",
                ZoneMessageOpcode.ItemReviewBase => "ItemReview.OptInAccount",
                _ => opcode.ToString()
            };
            Record(session, kind, body);
        }

        private static bool Admit(WorldSession session)
        {
            long minute = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
            bool admitted = false;
            budget.AddOrUpdate(session.PlayerGuid, _ => { admitted = true; return (minute, 1, 0); }, (_, b) =>
            {
                if (b.Minute != minute)
                {
                    if (b.Dropped > 0)
                        log.Info($"Telemetry from {session.CharacterName}: {b.Dropped} reports over {PerMinute}/min not kept.");
                    admitted = true;
                    return (minute, 1, 0);
                }
                if (b.Count < PerMinute)
                {
                    admitted = true;
                    return (minute, b.Count + 1, b.Dropped);
                }
                return (minute, b.Count, b.Dropped + 1);
            });
            return admitted;
        }

        /// <summary>A session ends: its budget goes.</summary>
        public static void Leave(WorldSession session) => budget.TryRemove(session.PlayerGuid, out _);

        private static object Decode(string kind, byte[] body)
        {
            var r = new GamePacketReader(body);
            switch (kind)
            {
                case "ClientLuaMetrics":
                {
                    uint count = r.ReadUInt();
                    var metrics = new List<object>();
                    for (uint i = 0; i < count && i < 4096; i++)
                    {
                        uint key = r.ReadUInt();
                        string name = r.ReadString();
                        metrics.Add(new { key, name, value = r.ReadUInt() });
                    }
                    return metrics;
                }
                case "WallOfData.UIEvent":
                    r.ReadByte();
                    return new { a = r.ReadString(), b = r.ReadString(), c = r.ReadString() };
                case "WallOfData.VoiceChatEvent":
                    r.ReadByte();
                    return new { a = r.ReadUInt(), b = r.ReadUInt() };
                case "WallOfData.Marketplace":
                    r.ReadByte();
                    return new { name = r.ReadString(), a = r.ReadUInt(), b = r.ReadUInt(), c = r.ReadUInt(), text = r.ReadString() };
                case "SocialShare.ProviderStatusRequest":
                    r.ReadByte();
                    return new { a = r.ReadUInt(), b = r.ReadUInt() };
                case "SocialShare.PublishRequest":
                    r.ReadByte();
                    return new { a = r.ReadUInt(), b = r.ReadUInt(), title = r.ReadString(), text = r.ReadString() };
                case "ItemReview.OptInAccount":
                    return new { account = r.ReadULong(), a = r.ReadByte(), b = r.ReadByte() };
                case "SpiceWebAuthUrlRequest":
                    return new { a = r.ReadUInt() };
                case "RendererDump":
                    return new { };
                default:
                    return null;
            }
        }
    }
}
