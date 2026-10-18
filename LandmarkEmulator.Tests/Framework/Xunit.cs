using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// A small xUnit-compatible surface (the attributes and Assert methods these tests use), so the test files read as plain
// xUnit and can move to the real packages unchanged. Run by Framework/TestRunner.cs. See the .csproj for why.
namespace Xunit
{
    /// <summary>A test. <see cref="Skip"/> set: reported as skipped with that reason, never run.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class FactAttribute : Attribute
    {
        public string Skip { get; set; }
        public string DisplayName { get; set; }
    }

    /// <summary>A test run once per data row (<see cref="InlineDataAttribute"/>, <see cref="MemberDataAttribute"/>).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class TheoryAttribute : FactAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class InlineDataAttribute : Attribute
    {
        public object[] Data { get; }
        public InlineDataAttribute(params object[] data) => Data = data ?? new object[] { null };
    }

    /// <summary>Rows from a public static property, field or method of the test class (or <see cref="MemberType"/>).</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class MemberDataAttribute : Attribute
    {
        public string MemberName { get; }
        public object[] Parameters { get; }
        public Type MemberType { get; set; }
        public MemberDataAttribute(string memberName, params object[] parameters)
        {
            MemberName = memberName;
            Parameters = parameters;
        }

        public IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            Type type = MemberType ?? testMethod.DeclaringType;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            object value = type.GetProperty(MemberName, flags)?.GetValue(null)
                ?? type.GetField(MemberName, flags)?.GetValue(null)
                ?? type.GetMethod(MemberName, flags)?.Invoke(null, Parameters is { Length: > 0 } ? Parameters : null)
                ?? throw new InvalidOperationException($"MemberData {MemberName} not found on {type.Name}.");
            foreach (object row in (IEnumerable)value)
                yield return row as object[] ?? new[] { row };
        }
    }

    /// <summary>A failed assertion.</summary>
    public class XunitException : Exception
    {
        public XunitException(string message) : base(message) { }
    }

    /// <summary>Thrown by <see cref="Skip.If"/>: the test is reported as skipped at run time.</summary>
    public class SkipException : Exception
    {
        public SkipException(string reason) : base(reason) { }
    }

    public static class Skip
    {
        public static void If(bool condition, string reason)
        {
            if (condition)
                throw new SkipException(reason);
        }
    }

    public static class Assert
    {
        public static void True(bool condition, string userMessage = null)
        {
            if (!condition)
                throw new XunitException(userMessage ?? "Assert.True() failure.");
        }

        public static void False(bool condition, string userMessage = null)
        {
            if (condition)
                throw new XunitException(userMessage ?? "Assert.False() failure.");
        }

        public static void Null(object value, string userMessage = null)
        {
            if (value != null)
                throw new XunitException(userMessage ?? $"Assert.Null() failure: {Show(value)}.");
        }

        public static T NotNull<T>(T value, string userMessage = null) where T : class
        {
            if (value == null)
                throw new XunitException(userMessage ?? "Assert.NotNull() failure.");
            return value;
        }

        public static void NotNull(object value) => NotNull<object>(value);

        public static void Equal(double expected, double actual, double tolerance)
        {
            if (!(Math.Abs(expected - actual) <= tolerance) && !(double.IsNaN(expected) && double.IsNaN(actual)))
                throw new XunitException($"Assert.Equal() failure: expected {expected} ± {tolerance}, actual {actual}.");
        }

        public static void Equal(float expected, float actual, float tolerance)
            => Equal((double)expected, actual, tolerance);

        public static void Equal<T>(T expected, T actual, string userMessage = null)
        {
            if (!AreEqual(expected, actual))
                throw new XunitException((userMessage == null ? "" : userMessage + "\n")
                    + $"Assert.Equal() failure.\n  Expected: {Show(expected)}\n  Actual:   {Show(actual)}");
        }

        public static void NotEqual<T>(T expected, T actual, string userMessage = null)
        {
            if (AreEqual(expected, actual))
                throw new XunitException(userMessage ?? $"Assert.NotEqual() failure: both {Show(actual)}.");
        }

        public static void Empty(IEnumerable collection, string userMessage = null)
        {
            var items = collection.Cast<object>().ToList();
            if (items.Count != 0)
                throw new XunitException((userMessage == null ? "" : userMessage + "\n")
                    + $"Assert.Empty() failure: {items.Count} item(s):\n  " + string.Join("\n  ", items.Take(60).Select(Show))
                    + (items.Count > 60 ? "\n  …" : ""));
        }

        public static void NotEmpty(IEnumerable collection, string userMessage = null)
        {
            if (!collection.Cast<object>().Any())
                throw new XunitException(userMessage ?? "Assert.NotEmpty() failure.");
        }

        public static T Single<T>(IEnumerable<T> collection)
        {
            var items = collection.ToList();
            if (items.Count != 1)
                throw new XunitException($"Assert.Single() failure: {items.Count} items.");
            return items[0];
        }

        public static void Contains<T>(T expected, IEnumerable<T> collection, string userMessage = null)
        {
            if (!collection.Any(i => AreEqual(expected, i)))
                throw new XunitException(userMessage ?? $"Assert.Contains() failure: {Show(expected)} not found.");
        }

        public static void Contains<T>(IEnumerable<T> collection, Predicate<T> filter)
        {
            var items = collection.ToList();
            if (!items.Any(i => filter(i)))
                throw new XunitException($"Assert.Contains() failure: no item matches. Items: {string.Join(" | ", items.Take(10).Select(i => Show(i)))}");
        }

        public static void Same(object expected, object actual)
        {
            if (!ReferenceEquals(expected, actual))
                throw new XunitException("Assert.Same() failure: not the same object.");
        }

        public static void Contains(string expected, string actual)
        {
            if (actual == null || !actual.Contains(expected, StringComparison.Ordinal))
                throw new XunitException($"Assert.Contains() failure: \"{expected}\" not in \"{actual}\".");
        }

        public static void DoesNotContain<T>(T expected, IEnumerable<T> collection, string userMessage = null)
        {
            if (collection.Any(i => AreEqual(expected, i)))
                throw new XunitException(userMessage ?? $"Assert.DoesNotContain() failure: {Show(expected)} found.");
        }

        public static void InRange<T>(T actual, T low, T high, string userMessage = null) where T : IComparable<T>
        {
            if (actual.CompareTo(low) < 0 || actual.CompareTo(high) > 0)
                throw new XunitException(userMessage ?? $"Assert.InRange() failure: {actual} not in [{low}, {high}].");
        }

        public static T IsType<T>(object value)
        {
            if (value == null || value.GetType() != typeof(T))
                throw new XunitException($"Assert.IsType() failure: expected {typeof(T).Name}, actual {value?.GetType().Name ?? "null"}.");
            return (T)value;
        }

        public static T Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                if (e.GetType() == typeof(T))
                    return (T)e;
                throw new XunitException($"Assert.Throws() failure: expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}");
            }
            throw new XunitException($"Assert.Throws() failure: expected {typeof(T).Name}, nothing was thrown.");
        }

        public static void Fail(string message) => throw new XunitException(message);

        public static void All<T>(IEnumerable<T> collection, Action<T> action)
        {
            int index = 0;
            foreach (T item in collection)
            {
                try
                {
                    action(item);
                }
                catch (XunitException e)
                {
                    throw new XunitException($"Assert.All() failure at item {index} ({Show(item)}): {e.Message}");
                }
                index++;
            }
        }

        private static bool AreEqual<T>(T expected, T actual)
        {
            if (expected == null || actual == null)
                return expected == null && actual == null;
            if (expected is string || expected is not IEnumerable)
                return EqualityComparer<T>.Default.Equals(expected, actual);
            var a = ((IEnumerable)expected).Cast<object>().ToList();
            var b = ((IEnumerable)(object)actual).Cast<object>().ToList();
            return a.Count == b.Count && a.Zip(b).All(p => Equals(p.First, p.Second));
        }

        private static string Show(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return $"\"{s}\"";
                case byte[] bytes: return bytes.Length == 0 ? "(empty)" : BitConverter.ToString(bytes).Replace('-', ' ');
                case IEnumerable e: return "[" + string.Join(", ", e.Cast<object>().Take(64).Select(Show)) + "]";
                default: return value.ToString();
            }
        }
    }
}
