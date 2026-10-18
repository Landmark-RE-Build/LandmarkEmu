using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Xunit;

namespace LandmarkEmulator.Tests.Framework
{
    /// <summary>
    /// The console runner for the xUnit-style tests in this assembly: every public class with [Fact] / [Theory] methods, a
    /// new instance per test (and per data row), Dispose after. Arguments are name filters (substring of
    /// "Class.Method"); none = everything. Exit code 0 when nothing failed.
    /// </summary>
    public static class TestRunner
    {
        public static int Main(string[] args)
        {
            // The tests compare invariant text and floats; the runner never depends on the machine's culture.
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            bool verbose = args.Contains("-v");
            // The server logs through NLog (its nlog.config is copied beside the tests): silent unless asked for ("-log").
            if (!args.Contains("-log"))
                NLog.LogManager.Configuration = new NLog.Config.LoggingConfiguration();
            string[] filters = args.Where(a => a != "-v" && a != "-log").ToArray();

            int passed = 0, failed = 0, skipped = 0;
            var failures = new List<string>();
            var skips = new List<string>();
            var clock = Stopwatch.StartNew();

            IEnumerable<Type> classes = typeof(TestRunner).Assembly.GetTypes()
                .Where(t => t.IsClass && t.IsPublic && !t.IsAbstract || t.IsClass && t.IsPublic && t.IsSealed && t.IsAbstract)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);
            foreach (Type type in classes)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.GetCustomAttribute<FactAttribute>() != null).OrderBy(m => m.MetadataToken))
                {
                    string name = $"{type.Name}.{method.Name}";
                    if (filters.Length > 0 && !filters.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)
                        || (type.FullName ?? "").Contains(f, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    FactAttribute fact = method.GetCustomAttribute<FactAttribute>();
                    if (!string.IsNullOrEmpty(fact.Skip))
                    {
                        skipped++;
                        skips.Add($"{name}: {fact.Skip}");
                        continue;
                    }

                    List<object[]> rows;
                    if (fact is TheoryAttribute)
                    {
                        rows = new List<object[]>();
                        try
                        {
                            foreach (InlineDataAttribute inline in method.GetCustomAttributes<InlineDataAttribute>())
                                rows.Add(inline.Data);
                            foreach (MemberDataAttribute member in method.GetCustomAttributes<MemberDataAttribute>())
                                rows.AddRange(member.GetData(method));
                        }
                        catch (Exception e)
                        {
                            failed++;
                            failures.Add($"{name} (data): {Describe(e)}");
                            continue;
                        }
                        if (rows.Count == 0)
                        {
                            failed++;
                            failures.Add($"{name}: no data rows.");
                            continue;
                        }
                    }
                    else
                        rows = new List<object[]> { Array.Empty<object>() };

                    foreach (object[] row in rows)
                    {
                        string label = rows.Count == 1 && row.Length == 0 ? name : $"{name}({string.Join(", ", row.Select(Format))})";
                        try
                        {
                            object instance = method.IsStatic ? null : Activator.CreateInstance(type);
                            try
                            {
                                object result = method.Invoke(instance, Convert(row, method.GetParameters()));
                                if (result is System.Threading.Tasks.Task task)
                                    task.GetAwaiter().GetResult();
                            }
                            finally
                            {
                                (instance as IDisposable)?.Dispose();
                            }
                            passed++;
                            if (verbose)
                                Console.WriteLine($"  ok    {label}");
                        }
                        catch (Exception e) when (Unwrap(e) is SkipException skip)
                        {
                            skipped++;
                            skips.Add($"{label}: {skip.Message}");
                        }
                        catch (Exception e)
                        {
                            failed++;
                            failures.Add($"{label}: {Describe(e)}");
                            if (verbose)
                                Console.WriteLine($"  FAIL  {label}");
                        }
                    }
                }
            }

            foreach (string failure in failures)
                Console.WriteLine($"[FAIL] {failure}\n");
            foreach (string skip in skips)
                Console.WriteLine($"[SKIP] {skip}");
            Console.WriteLine($"Tests: {passed + failed + skipped} total, {passed} passed, {failed} failed, {skipped} skipped "
                + $"({clock.Elapsed.TotalSeconds:0.0} s).");
            return failed == 0 ? 0 : 1;
        }

        private static object[] Convert(object[] row, ParameterInfo[] parameters)
        {
            if (row.Length != parameters.Length)
                throw new XunitException($"Data row has {row.Length} value(s), the test takes {parameters.Length}.");
            var args = new object[row.Length];
            for (int i = 0; i < row.Length; i++)
            {
                Type target = parameters[i].ParameterType;
                object value = row[i];
                if (value != null && !target.IsInstanceOfType(value) && value is IConvertible)
                    value = target.IsEnum ? Enum.ToObject(target, value) : System.Convert.ChangeType(value, target,
                        System.Globalization.CultureInfo.InvariantCulture);
                args[i] = value;
            }
            return args;
        }

        private static Exception Unwrap(Exception e)
        {
            while (e is TargetInvocationException { InnerException: not null } t)
                e = t.InnerException;
            return e;
        }

        private static string Describe(Exception e)
        {
            e = Unwrap(e);
            if (e is XunitException)
                return e.Message + "\n    " + FirstFrames(e);
            return $"{e.GetType().Name}: {e.Message}\n    {FirstFrames(e)}";
        }

        private static string FirstFrames(Exception e)
            => string.Join("\n    ", (e.StackTrace ?? "").Split('\n').Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.Contains("Xunit.Assert") && !l.Contains("System.Reflection") && !l.Contains("System.RuntimeMethodHandle"))
                .Take(6));

        private static string Format(object value) => value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            Type t => t.Name,
            float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}
