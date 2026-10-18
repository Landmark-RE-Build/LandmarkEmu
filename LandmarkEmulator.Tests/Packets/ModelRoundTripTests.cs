using LandmarkEmulator.Shared.Network;
using LandmarkEmulator.Shared.Network.Message;
using LandmarkEmulator.Tests.Support;
using LandmarkEmulator.WorldServer.Network.Message;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// Data-driven checks over every packet model (IReadable / IWritable) in the world server and the shared library:
    /// write → read → write gives the same bytes and consumes them exactly (both-way models), a populated model writes
    /// (S→C models), and a client model never hangs or allocates without bound on hostile input (C→S models).
    /// </summary>
    public class ModelRoundTripTests
    {
        private static readonly Assembly[] Assemblies =
        {
            typeof(ZoneMessageOpcode).Assembly,           // LandmarkEmulator.WorldServer
            typeof(GamePacketReader).Assembly,            // LandmarkEmulator.Shared
            typeof(LandmarkEmulator.Gateway.Network.Message.ClientProtocol).Assembly,   // LandmarkEmulator.Gateway
            typeof(LandmarkEmulator.AuthServer.Network.Message.AuthMessageAttribute).Assembly,   // LandmarkEmulator.AuthServer (login)
        };

        private static IEnumerable<Type> Models(Func<Type, bool> filter)
            => Assemblies.SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters
                    && t.GetConstructor(Type.EmptyTypes) != null && filter(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal);

        private static bool IsReadable(Type t) => typeof(IReadable).IsAssignableFrom(t);
        private static bool IsWritable(Type t) => typeof(IWritable).IsAssignableFrom(t);

        public static IEnumerable<object[]> BothWays => Models(t => IsReadable(t) && IsWritable(t)).Select(t => new object[] { t });
        public static IEnumerable<object[]> WriteOnly => Models(t => IsWritable(t) && !IsReadable(t)).Select(t => new object[] { t });
        public static IEnumerable<object[]> ReadOnly => Models(t => IsReadable(t) && !IsWritable(t)).Select(t => new object[] { t });
        public static IEnumerable<object[]> AllReadable => Models(IsReadable).Select(t => new object[] { t });

        /// <summary>
        /// Models whose read and write may not be mirror images, with the reason. Empty since 2026-10-18 (every model the
        /// client has is modelled both ways: the character select friends list, the quest / palette / acquaintance /
        /// mission lists of SendSelfToClient, the login replies; capture-only parsing lives in SendSelfToClient.ReadCapture
        /// and LandmarkEmulator.Parser CaptureReading). A model added here skips the round trips: build it instead.
        /// </summary>
        private static readonly Dictionary<string, string> NotSymmetric = new();

        /// <summary>Fields with a fixed element count on the wire, put back after the populator filled them.</summary>
        private static void FixUp(object model)
        {
            // Twelve containers, always (the reader reads 12 {u32, bool}; WorldSession.SendSelf sets entries by index).
            if (model is WorldServer.Network.Message.Model.SendSelfToClient.InventoryData inventory)
                inventory.Containers = new WorldServer.Network.Message.Model.SendSelfToClient.InventoryData().Containers;
            // The same block inside the whole SendSelfToClient.
            if (model is WorldServer.Network.Message.Model.SendSelfToClient sendSelf)
                FixUp(sendSelf.Inventory);
        }

        [Theory]
        [MemberData(nameof(BothWays))]
        public void WriteReadWrite_GivesTheSameBytes(Type type)
        {
            Skip.If(NotSymmetric.TryGetValue(type.Name, out string why), why);
            var first = (IWritable)new Populator().Fill(Activator.CreateInstance(type));
            FixUp(first);
            byte[] written = Wire.Write(first);

            var second = (IReadable)Activator.CreateInstance(type);
            var reader = new GamePacketReader(written);
            second.Read(reader);
            Assert.Equal(0u, reader.BytesRemaining, $"{type.Name}: read left {reader.BytesRemaining} of {written.Length} bytes "
                + $"({Wire.Show(written)})");

            byte[] again = Wire.Write((IWritable)second);
            Assert.Equal(written, again, $"{type.Name}: the second write differs");
        }

        [Theory]
        [MemberData(nameof(BothWays))]
        public void DefaultInstance_RoundTrips(Type type)
        {
            Skip.If(NotSymmetric.TryGetValue(type.Name, out string why), why);
            var first = (IWritable)Activator.CreateInstance(type);
            byte[] written;
            try
            {
                written = Wire.Write(first);
            }
            catch (Exception e) when (e is NullReferenceException or ArgumentNullException)
            {
                // A model whose fields must be set first (strings default null): nothing to round-trip.
                return;
            }
            var second = (IReadable)Activator.CreateInstance(type);
            var reader = new GamePacketReader(written);
            second.Read(reader);
            Assert.Equal(0u, reader.BytesRemaining, $"{type.Name}: read left {reader.BytesRemaining} of {written.Length} bytes");
            Assert.Equal(written, Wire.Write((IWritable)second), $"{type.Name}: the second write differs");
        }

        [Theory]
        [MemberData(nameof(WriteOnly))]
        public void PopulatedServerModel_Writes(Type type)
        {
            Skip.If(NotSymmetric.TryGetValue(type.Name, out string why), why);
            var model = (IWritable)new Populator().Fill(Activator.CreateInstance(type));
            byte[] written = Wire.Write(model);
            Assert.True(written.Length >= 0);
        }

        /// <summary>
        /// Hostile input: random bytes, all-0xFF (every count 4 billion) and nothing at all. A client packet may fail to
        /// read (the dispatcher logs it), but it must not hang the world thread or allocate without bound.
        /// </summary>
        [Theory]
        [MemberData(nameof(AllReadable))]
        public void ClientModel_SurvivesHostileInput(Type type)
        {
            var inputs = new List<byte[]> { Array.Empty<byte>(), Enumerable.Repeat((byte)0xFF, 64).ToArray(),
                Enumerable.Repeat((byte)0x7F, 64).ToArray() };
            var random = new Random(type.FullName!.Length * 7919);
            for (int i = 0; i < 6; i++)
            {
                var data = new byte[random.Next(1, 96)];
                random.NextBytes(data);
                inputs.Add(data);
            }
            foreach (byte[] input in inputs)
            {
                Task<(Exception Error, long Allocated)> run = Task.Run(() =>
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    try
                    {
                        var model = (IReadable)Activator.CreateInstance(type);
                        model.Read(new GamePacketReader(input));
                        return ((Exception)null, GC.GetAllocatedBytesForCurrentThread() - before);
                    }
                    catch (Exception e)
                    {
                        return (e, GC.GetAllocatedBytesForCurrentThread() - before);
                    }
                });
                Assert.True(run.Wait(TimeSpan.FromSeconds(5)), $"{type.Name}: Read hung on {Wire.Show(input)}");
                // Any exception is caught by WorldSession.HandleZonePacket and the packet dropped; what must not happen is
                // a hang or an allocation driven by a forged count.
                Assert.True(run.Result.Allocated < 16 << 20, $"{type.Name}: Read allocated {run.Result.Allocated:N0} bytes "
                    + $"from {input.Length} input bytes ({Wire.Show(input)})");
                Assert.False(run.Result.Error is OutOfMemoryException, $"{type.Name}: out of memory on {Wire.Show(input)}");
            }
        }
    }
}
