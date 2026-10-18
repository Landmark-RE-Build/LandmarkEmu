using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Network;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// The zone opcode codec (WorldSession.GetOpcodeBytes / GetOpcode) and the [ZoneMessage] table: docs/protocol.md §1.3
    /// and §1.6 — bases are u16 on the wire, PlayerUpdate 0x26 carries a u32 sub (26 00 ss 00 00 00) both ways.
    /// </summary>
    public class OpcodeCodecTests
    {
        private static IEnumerable<(Type Type, ZoneMessageAttribute Attribute)> Messages()
            => typeof(ZoneMessageOpcode).Assembly.GetTypes()
                .Select(t => (t, t.GetCustomAttribute<ZoneMessageAttribute>()))
                .Where(p => p.Item2 != null)
                .OrderBy(p => p.t.FullName, StringComparer.Ordinal);

        public static IEnumerable<object[]> ClientMessages
            => Messages().Where(m => typeof(IReadable).IsAssignableFrom(m.Type)).Select(m => new object[] { m.Type.Name, m.Attribute.Opcode });

        public static IEnumerable<object[]> ServerMessages
            => Messages().Where(m => typeof(IWritable).IsAssignableFrom(m.Type)).Select(m => new object[] { m.Type.Name, m.Attribute.Opcode });

        public static IEnumerable<object[]> PlayerUpdateValues
            => Enum.GetValues<ZoneMessageOpcode>().Distinct().Where(IsPlayerUpdate).Select(o => new object[] { o });

        private static bool IsPlayerUpdate(ZoneMessageOpcode o) => (uint)o is >= 0x260000 and <= 0x2600FF or >= 0x26000000 and <= 0x2600FFFF;

        [Fact]
        public void PlayerUpdate_IsWrittenWithAU32Sub()
        {
            Assert.Equal(Wire.Hex("26 00 58 00 00 00"), WorldSession.GetOpcodeBytes(ZoneMessageOpcode.PlayerUpdateAddLightweightPcWire));
            Assert.Equal(Wire.Hex("26 00 01 00 00 00"), WorldSession.GetOpcodeBytes(ZoneMessageOpcode.PlayerUpdateRemovePlayerWire));
        }

        [Theory]
        [MemberData(nameof(PlayerUpdateValues))]
        public void PlayerUpdate_EveryValue_IsSixBytes_AndReadsBack(ZoneMessageOpcode opcode)
        {
            byte[] bytes = WorldSession.GetOpcodeBytes(opcode);
            Assert.Equal(6, bytes.Length, $"{opcode} (0x{(uint)opcode:X}) → {Wire.Show(bytes)}");
            Assert.Equal(new byte[] { 0x26, 0x00 }, bytes[..2]);
            Assert.Equal(new byte[] { 0, 0, 0 }, bytes[3..]);
            (ZoneMessageOpcode? read, int length) = WorldSession.GetOpcode(bytes.Concat(new byte[] { 0xAB, 0xCD }).ToArray());
            Assert.Equal(6, length);
            Assert.NotNull(read);
            // The two enum forms of one sub (0x2600ss "Wire" and 0x2600ss00) must name the same sub.
            uint sub = (uint)opcode <= 0x2600FF ? (uint)opcode & 0xFF : ((uint)opcode >> 8) & 0xFF;
            uint readSub = (uint)read.Value <= 0x2600FF ? (uint)read.Value & 0xFF : ((uint)read.Value >> 8) & 0xFF;
            Assert.Equal(sub, readSub);
        }

        [Fact]
        public void Bases_AreTwoBytes()
        {
            Assert.Equal(Wire.Hex("13 00"), WorldSession.GetOpcodeBytes(ZoneMessageOpcode.ChatBase));
            Assert.Equal(Wire.Hex("0D 01"), WorldSession.GetOpcodeBytes(ZoneMessageOpcode.InstancedGameBase));
            Assert.Equal(Wire.Hex("01 00"), WorldSession.GetOpcodeBytes(ZoneMessageOpcode.Notifications));
            Assert.Equal(Wire.Hex("03 00"), WorldSession.GetOpcodeBytes(ZoneMessageOpcode.PreferencePacket));
        }

        /// <summary>
        /// Every enum value is at least the u16 base on the wire: DebugDrawBase 0x0001 is wire bytes 00 01 (the enum's
        /// own comment), so dropping its leading zero byte sent a 1-byte opcode the client cannot read.
        /// </summary>
        [Fact]
        public void EveryOpcode_IsAtLeastTwoBytes()
        {
            var shortOnes = Enum.GetValues<ZoneMessageOpcode>().Distinct()
                .Select(o => (o, b: WorldSession.GetOpcodeBytes(o))).Where(p => p.b.Length < 2)
                .Select(p => $"{p.o} (0x{(uint)p.o:X}) → {Wire.Show(p.b)}");
            Assert.Empty(shortOnes);
        }

        /// <summary>
        /// A client packet must decode to the opcode its model is registered under, or it reaches no factory and is
        /// dropped as "unreadable". The header length must be what GetOpcodeBytes wrote.
        /// </summary>
        [Theory]
        [MemberData(nameof(ClientMessages))]
        public void ClientMessage_OpcodeDecodesToItsModel(string model, ZoneMessageOpcode opcode)
        {
            byte[] header = WorldSession.GetOpcodeBytes(opcode);
            byte[] packet = header.Concat(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }).ToArray();
            (ZoneMessageOpcode? read, int length) = WorldSession.GetOpcode(packet);
            Assert.True(read == opcode, $"{model}: {opcode} (0x{(uint)opcode:X}, bytes {Wire.Show(header)}) decodes as "
                + $"{(read?.ToString() ?? "nothing")}{(read != null ? $" (0x{(uint)read:X})" : "")}");
            Assert.Equal(header.Length, length);
        }

        /// <summary>The same check with a body starting with a non-zero byte (a sub-opcode).</summary>
        [Theory]
        [MemberData(nameof(ClientMessages))]
        public void ClientMessage_OpcodeDecodes_WithAnyBody(string model, ZoneMessageOpcode opcode)
        {
            byte[] header = WorldSession.GetOpcodeBytes(opcode);
            foreach (byte first in new byte[] { 0x01, 0x02, 0x05, 0x10, 0x26, 0x80, 0xFF })
            {
                byte[] packet = header.Concat(new byte[] { first, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }).ToArray();
                (ZoneMessageOpcode? read, int length) = WorldSession.GetOpcode(packet);
                Assert.True(read == opcode && length == header.Length, $"{model}: {opcode} (bytes {Wire.Show(header)}) + body "
                    + $"{first:X2} 01 … decodes as {(read?.ToString() ?? "nothing")} (length {length})");
            }
        }

        [Theory]
        [MemberData(nameof(ServerMessages))]
        public void ServerMessage_HasAWireForm(string model, ZoneMessageOpcode opcode)
        {
            byte[] header = WorldSession.GetOpcodeBytes(opcode);
            Assert.InRange(header.Length, 2, 6, $"{model}: {opcode} → {Wire.Show(header)}");
        }

        /// <summary>The message table builds (a duplicate client opcode or handler throws at server start).</summary>
        [Fact]
        public void MessageManager_Initialises()
        {
            FakeWorld.EnsureMessageTable();
            Assert.True(ZoneMessageManager.Instance.GetOpcode(new LandmarkEmulator.WorldServer.Network.Message.Model.KeepAlive(), out ZoneMessageOpcode op)
                && op == ZoneMessageOpcode.KeepAlive);
        }

        [Fact]
        public void ClientMessageOpcodes_AreUnique()
        {
            var dupes = Messages().Where(m => typeof(IReadable).IsAssignableFrom(m.Type))
                .GroupBy(m => m.Attribute.Opcode).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(m => m.Type.Name))}");
            Assert.Empty(dupes);
        }

        /// <summary>
        /// Two enum names on one value make Enum.IsDefined / ToString ambiguous (logs, OpcodeStats) — allowed only where
        /// listed.
        /// </summary>
        [Fact]
        public void EnumValues_HaveOneName()
        {
            var dupes = Enum.GetNames<ZoneMessageOpcode>()
                .GroupBy(n => (uint)Enum.Parse<ZoneMessageOpcode>(n)).Where(g => g.Count() > 1)
                .Select(g => $"0x{g.Key:X}: {string.Join(", ", g)}");
            Assert.Empty(dupes);
        }

        /// <summary>
        /// A value carrying a sub-opcode is the base's wire bytes then the sub: 3 bytes XX 00 ss / XX 01 ss (u8 sub; 01 = a
        /// two-byte base 0x1XX) or 4 bytes XX 00 ss ss / XX 01 ss ss. Anything else is a typo (0x2900F00 was a digit short).
        /// </summary>
        [Fact]
        public void SubCarryingValues_HaveTheBaseForm()
        {
            var bad = Enum.GetValues<ZoneMessageOpcode>().Distinct().Where(o => (uint)o > 0xFFFF)
                .Where(o => ((uint)o > 0xFFFFFF ? ((uint)o >> 16) & 0xFF : ((uint)o >> 8) & 0xFF) > 1)
                .Select(o => $"{o} = 0x{(uint)o:X}");
            Assert.Empty(bad);
        }

        /// <summary>
        /// Every (base, sub) the enum carries is a packet of the 0.695 client, named alike: checked against the client's
        /// packet-id tables. Data/packet_ids_695.tsv is a copy of the research workspace's re/out/packet_ids_695.tsv
        /// (base, sub, base name, packet class name: structure only, no game content), copied next to the test binary.
        /// </summary>
        [Fact]
        public void SubCarryingValues_MatchThe695IdTable()
        {
            string table = System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "packet_ids_695.tsv");
            var ids = new Dictionary<(uint, uint), string>();
            foreach (string line in System.IO.File.ReadLines(table).Skip(1))
            {
                string[] f = line.Split('	');
                if (f.Length >= 4)
                    ids[(Convert.ToUInt32(f[0], 16), Convert.ToUInt32(f[1], 16))] = f[3];
            }
            // Subs the client builds in its dispatcher without a PacketId constant: PlayerUpdate 0x65
            // RequestAnimationSelection (0.695 FUN_142cdfa60 case 100 sets the class's sub to 0x65 itself).
            ids[(0x26, 0x65)] = "PlayerUpdatePacketRequestAnimationSelection";
            // Families the id tables do not list (no PacketId constants in the client): checked by their own tests.
            var unlisted = new HashSet<uint> { 0x9A, 0x2F };
            var problems = new List<string>();
            foreach (string name in Enum.GetNames<ZoneMessageOpcode>())
            {
                uint v = (uint)Enum.Parse<ZoneMessageOpcode>(name);
                if (v <= 0xFFFF)
                    continue;
                (uint baseId, uint sub) = v > 0xFFFFFF
                    ? ((v >> 24) | ((v >> 16) & 0xFF) << 8, ((v >> 8) & 0xFF) | (v & 0xFF) << 8)
                    : ((v >> 16) | ((v >> 8) & 0xFF) << 8, v & 0xFF);
                if (unlisted.Contains(baseId))
                    continue;
                if (!ids.TryGetValue((baseId, sub), out string packet))
                {
                    problems.Add($"{name} = 0x{v:X}: base 0x{baseId:X} sub 0x{sub:X} is not in the 0.695 id table");
                    continue;
                }
                string key = Normalise(packet);
                string ours = name.Replace("Wire", "").ToLowerInvariant();
                // Client spellings the enum corrects or shortens.
                ours = ours.Replace("dynamicinteraction", "dyanmicinteraction").Replace("playerupdatecombat", "playercombat");
                if (!ours.Contains(key))
                    problems.Add($"{name} = 0x{v:X}: the client calls base 0x{baseId:X} sub 0x{sub:X} {packet}");
            }
            Assert.Empty(problems);
        }

        private static string Normalise(string packet)
        {
            packet = packet.Split("::").Last();
            foreach (string pattern in new[] { @".*PacketId_?", @".*Packet", @"^cPlayerUpdate", @"^cPlayer", @"^c" })
            {
                string n = System.Text.RegularExpressions.Regex.Replace(packet, pattern, "");
                if (n != packet)
                    return n.Replace("_", "").ToLowerInvariant();
            }
            return packet.ToLowerInvariant();
        }

        public static IEnumerable<object[]> Handlers
            => typeof(ZoneMessageOpcode).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Select(m => (m, a: m.GetCustomAttribute<ZoneMessageHandlerAttribute>()))
                .Where(x => x.a != null)
                .OrderBy(x => x.m.DeclaringType!.Name + "." + x.m.Name, StringComparer.Ordinal)
                .Select(x => new object[] { $"{x.m.DeclaringType!.Name}.{x.m.Name}", x.a.Opcode, x.m });

        /// <summary>
        /// A handler runs only for a packet its opcode's model read (WorldSession.HandleZonePacket: no model → "unreadable",
        /// the handler never runs), and the model is cast to the handler's parameter type (a mismatch throws on every packet).
        /// The registration only takes public methods (ZoneMessageManager uses GetMethods()).
        /// </summary>
        [Theory]
        [MemberData(nameof(Handlers))]
        public void Handler_HasAModelOfItsParameterType(string handler, ZoneMessageOpcode opcode, MethodInfo method)
        {
            Assert.True(method.IsPublic, $"{handler} is not public: ZoneMessageManager never registers it");
            Type model = Messages().Where(m => typeof(IReadable).IsAssignableFrom(m.Type) && m.Attribute.Opcode == opcode)
                .Select(m => m.Type).SingleOrDefault();
            Assert.True(model != null, $"{handler}: no IReadable [ZoneMessage({opcode})] model, so the packet is dropped before it");
            ParameterInfo[] parameters = method.GetParameters();
            Type parameter = parameters[^1].ParameterType;
            Assert.True(parameter.IsAssignableFrom(model), $"{handler} takes {parameter.Name}, the model is {model.Name}");
            if (method.IsStatic)
                Assert.True(parameters.Length == 2 && parameters[0].ParameterType.IsAssignableFrom(typeof(WorldSession)),
                    $"{handler}: a static handler takes (WorldSession, message)");
            else
                Assert.True(parameters.Length == 1 && method.DeclaringType!.IsAssignableFrom(typeof(WorldSession)),
                    $"{handler}: an instance handler must be a WorldSession method taking (message)");
        }

        [Fact]
        public void HandlerOpcodes_AreUnique()
        {
            var dupes = Handlers.GroupBy(h => (ZoneMessageOpcode)h[1]).Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(h => h[0]))}");
            Assert.Empty(dupes);
        }
    }
}
