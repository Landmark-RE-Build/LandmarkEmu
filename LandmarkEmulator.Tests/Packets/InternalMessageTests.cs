using LandmarkEmulator.Internal;
using LandmarkEmulator.Tests.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Packets
{
    /// <summary>
    /// The internal protocol between the world, the gateway and the services (LandmarkEmulator.Internal): every message
    /// with a <c>Write(InternalWriter)</c> and a static <c>Read(InternalReader)</c> round-trips write → read → write with a
    /// distinct value in every field, and its reader consumes exactly what was written. A field written but not read (or
    /// read in another order) shows here; the two ends are often different processes (world ↔ social / commerce /
    /// environment / world-channel services), so such a slip would only show at run time across containers.
    /// </summary>
    public class InternalMessageTests
    {
        public static IEnumerable<object[]> Messages()
            => typeof(InternalWriter).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.ContainsGenericParameters
                    && t.GetMethod("Write", BindingFlags.Instance | BindingFlags.Public, new[] { typeof(InternalWriter) }) != null
                    && Reader(t) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => new object[] { t });

        private static MethodInfo Reader(Type t)
            => t.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Read" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(InternalReader)
                    && t.IsAssignableFrom(m.ReturnType));

        private static byte[] Write(object message)
        {
            var w = new InternalWriter();
            message.GetType().GetMethod("Write", new[] { typeof(InternalWriter) })!.Invoke(message, new object[] { w });
            return w.ToArray();
        }

        [Theory]
        [MemberData(nameof(Messages))]
        public void WriteReadWrite_GivesTheSameBytes(Type type)
        {
            object first = new Populator().Instantiate(type);
            Assert.NotNull(first);
            byte[] written = Write(first);
            var reader = new InternalReader(written);
            object second = Reader(type).Invoke(null, new object[] { reader });
            Assert.Equal(0, reader.Remaining, $"{type.Name}: {reader.Remaining} of {written.Length} bytes left unread");
            Assert.Equal(written, Write(second), $"{type.Name}: the second write differs");
        }

        [Fact]
        public void TheSetIsNotEmpty() => Assert.True(Messages().Count() > 60, $"{Messages().Count()} internal messages");
    }
}
