using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace LandmarkEmulator.Tests.Support
{
    /// <summary>
    /// Fills an object's settable properties (public or private setters, nested objects, lists, arrays) with distinct,
    /// deterministic values, so a write → read → write round trip shows a swapped, mis-sized or missing field: every
    /// field gets a different value, and every list two elements.
    /// </summary>
    public sealed class Populator
    {
        private int counter;
        private readonly int maxDepth;

        public Populator(int seed = 1, int maxDepth = 5)
        {
            counter = seed;
            this.maxDepth = maxDepth;
        }

        private int Next() => ++counter;

        public T Fill<T>(T target) => (T)Fill(target, typeof(T) == typeof(object) ? target.GetType() : target.GetType(), 0);

        public object Create(Type type) => Value(type, null, 0, null);

        /// <summary>
        /// A new, filled instance: through the parameterless constructor, else the longest one (positional records) with a
        /// distinct value for every argument; then every settable property is filled too.
        /// </summary>
        public object Instantiate(Type type)
        {
            ConstructorInfo empty = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
            object instance;
            if (empty != null || type.IsValueType)
                instance = Activator.CreateInstance(type, true);
            else
            {
                ConstructorInfo ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor == null)
                    return null;
                object[] args = ctor.GetParameters().Select(p => Value(p.ParameterType, null, 1, p.Name)).ToArray();
                instance = ctor.Invoke(args);
            }
            return Fill(instance, type, 0);
        }

        private object Fill(object target, Type type, int depth)
        {
            if (target == null || depth > maxDepth)
                return target;
            foreach (PropertyInfo property in Properties(type))
            {
                object current;
                try
                {
                    current = property.GetValue(target);
                }
                catch
                {
                    continue;
                }
                MethodInfo setter = property.GetSetMethod(true);
                if (setter == null)
                {
                    // Get-only: fill a collection or nested object in place.
                    if (current is IList list && !list.IsFixedSize && !list.IsReadOnly)
                        AddElements(list, ElementType(property.PropertyType), depth);
                    else if (current is IDictionary dictionary && !dictionary.IsReadOnly)
                        AddEntries(dictionary, property.PropertyType, depth);
                    else if (current is Array array)
                        FillArray(array, depth);
                    else if (current != null && IsComposite(current.GetType()))
                        Fill(current, current.GetType(), depth + 1);
                    continue;
                }
                object value = Value(property.PropertyType, current, depth, property.Name);
                try
                {
                    setter.Invoke(target, new[] { value });
                }
                catch (TargetInvocationException)
                {
                    // A setter that validates; keep the default.
                }
            }
            return target;
        }

        private static IEnumerable<PropertyInfo> Properties(Type type)
        {
            var seen = new HashSet<string>();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead).OrderBy(p => p.MetadataToken))
                    if (seen.Add(p.Name))
                        yield return p;
        }

        private object Value(Type type, object current, int depth, string name)
        {
            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                return Value(underlying, null, depth, name);
            int n = Next();
            if (type == typeof(bool)) return n % 2 == 0;
            if (type == typeof(byte)) return (byte)(n % 200 + 1);
            if (type == typeof(sbyte)) return (sbyte)(n % 100 + 1);
            if (type == typeof(short)) return (short)(n * 7 + 1);
            if (type == typeof(ushort)) return (ushort)(n * 7 + 1);
            if (type == typeof(int)) return n * 1000 + 3;
            if (type == typeof(uint)) return (uint)(n * 1000 + 3);
            if (type == typeof(long)) return (long)n * 1_000_000_007L + 5;
            if (type == typeof(ulong)) return (ulong)n * 1_000_000_007UL + 5;
            if (type == typeof(float)) return n + 0.5f;
            if (type == typeof(double)) return n + 0.25;
            if (type == typeof(string)) return "s" + n;
            if (type == typeof(char)) return (char)('a' + n % 26);
            if (type == typeof(DateTime)) return new DateTime(2016, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n);
            if (type == typeof(Guid)) return new Guid(n, 0, 0, new byte[8]);
            if (type == typeof(Vector2)) return new Vector2(n + 0.5f, n + 1.5f);
            if (type == typeof(Vector3)) return new Vector3(n + 0.5f, n + 1.5f, n + 2.5f);
            if (type == typeof(Vector4)) return new Vector4(n + 0.5f, n + 1.5f, n + 2.5f, n + 3.5f);
            if (type == typeof(Quaternion)) return new Quaternion(n + 0.5f, n + 1.5f, n + 2.5f, n + 3.5f);
            if (type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
            {
                // Past the depth limit the elements are made empty, not left null: a (uint, Model) tuple the writer
                // dereferences must not hold a null model.
                ConstructorInfo tuple = type.GetConstructors().Single();
                return tuple.Invoke(tuple.GetParameters()
                    .Select(p => depth < maxDepth ? Value(p.ParameterType, null, depth + 1, null) : Empty(p.ParameterType)).ToArray());
            }
            if (type.IsEnum)
            {
                Array values = Enum.GetValues(type);
                return values.Length == 0 ? Activator.CreateInstance(type) : values.GetValue(n % values.Length);
            }
            if (type == typeof(byte[]))
                return new[] { (byte)(n % 250 + 1), (byte)(n % 250 + 2), (byte)(n % 250 + 3) };
            if (type.IsArray)
            {
                // Keep a fixed-size array's length (float[4] position); else six elements (a box's min / max corners,
                // double[6], is the one array the internal protocol writes without a count).
                int length = current is Array existing && existing.Length > 0 ? existing.Length : 6;
                Array array = Array.CreateInstance(type.GetElementType(), length);
                FillArray(array, depth);
                return array;
            }
            if (depth >= maxDepth)
                return current != null ? NoNullStrings(current, 0) : Empty(type);
            if (typeof(IList).IsAssignableFrom(type) && type.IsGenericType)
            {
                var list = (IList)(current ?? Activator.CreateInstance(type));
                list.Clear();
                AddElements(list, ElementType(type), depth);
                return list;
            }
            if (typeof(IDictionary).IsAssignableFrom(type) && type.IsGenericType)
            {
                var dictionary = (IDictionary)(current ?? Activator.CreateInstance(type));
                dictionary.Clear();
                AddEntries(dictionary, type, depth);
                return dictionary;
            }
            if (type.IsInterface && type.IsGenericType && current == null)
            {
                // IReadOnlyList<T>, IList<T>, IEnumerable<T>, IReadOnlyCollection<T> → List<T>; IReadOnlyDictionary → Dictionary.
                Type[] args = type.GetGenericArguments();
                Type concrete = args.Length == 1 ? typeof(List<>).MakeGenericType(args)
                    : args.Length == 2 ? typeof(Dictionary<,>).MakeGenericType(args) : null;
                if (concrete != null && type.IsAssignableFrom(concrete))
                    return Value(concrete, null, depth, name);
            }
            if (type.IsInterface || type.IsAbstract)
                return current;
            if (IsComposite(type))
            {
                if (current != null)
                    return Fill(current, type, depth + 1);
                if (type.IsValueType || type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) != null)
                    return Fill(Activator.CreateInstance(type, true), type, depth + 1);
                ConstructorInfo ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor == null)
                    return null;
                object made = ctor.Invoke(ctor.GetParameters().Select(p => Value(p.ParameterType, null, depth + 1, p.Name)).ToArray());
                return Fill(made, type, depth + 1);
            }
            return current;
        }

        /// <summary>
        /// A value with nothing filled in, for past the depth limit: numbers 0, strings "", an empty list / array, a model
        /// from its parameterless constructor (its own defaults); null only for what can't be made that way.
        /// </summary>
        private static object Empty(Type type)
        {
            if (type == typeof(string))
                return "";
            if (type.IsValueType)
                return Activator.CreateInstance(type);
            if (type.IsArray)
                return Array.CreateInstance(type.GetElementType(), 0);
            if (type.IsInterface || type.IsAbstract)
                return null;
            if (type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) == null)
                return null;
            return NoNullStrings(Activator.CreateInstance(type, true), 0);
        }

        /// <summary>A model's own defaults, except strings left null (a writer can't write null): "", in nested models too.</summary>
        private static object NoNullStrings(object model, int level)
        {
            if (model == null || level > 8 || !IsComposite(model.GetType()) || model.GetType().IsValueType)
                return model;
            foreach (PropertyInfo p in Properties(model.GetType()))
            {
                object value;
                try
                {
                    value = p.GetValue(model);
                }
                catch
                {
                    continue;
                }
                if (p.PropertyType == typeof(string))
                {
                    if (value == null && p.GetSetMethod(true) != null)
                        p.GetSetMethod(true).Invoke(model, new object[] { "" });
                }
                else if (value != null && IsComposite(value.GetType()))
                    NoNullStrings(value, level + 1);
            }
            return model;
        }

        private void FillArray(Array array, int depth)
        {
            for (int i = 0; i < array.Length; i++)
                array.SetValue(Value(array.GetType().GetElementType(), array.GetValue(i), depth, null), i);
        }

        private void AddElements(IList list, Type element, int depth)
        {
            if (element == null)
                return;
            for (int i = 0; i < 2; i++)
                if (Value(element, null, depth + 1, null) is object value)
                    list.Add(value);
        }

        private void AddEntries(IDictionary dictionary, Type type, int depth)
        {
            Type[] args = type.GetGenericArguments();
            if (args.Length != 2)
                return;
            for (int i = 0; i < 2; i++)
            {
                object key = Value(args[0], null, depth + 1, null);
                object value = Value(args[1], null, depth + 1, null);
                if (key != null && value != null && !dictionary.Contains(key))
                    dictionary.Add(key, value);
            }
        }

        private static Type ElementType(Type type)
            => type.IsArray ? type.GetElementType()
                : type.GetInterfaces().Concat(new[] { type }).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>))
                    ?.GetGenericArguments()[0];

        private static bool IsComposite(Type type)
            => !type.IsPrimitive && !type.IsEnum && type != typeof(string) && type != typeof(decimal)
               && (type.IsClass || type.IsValueType && !type.IsPrimitive) && type.Namespace?.StartsWith("LandmarkEmulator") == true;
    }
}
