#pragma warning disable
#nullable enable annotations
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

static partial class PolyfillExtensions
{
#if NETFRAMEWORK || NETSTANDARD2_0 || NETSTANDARD2_1 || NETCOREAPP3_1 || NET5_0 || NET6_0
    extension(global::System.ArgumentOutOfRangeException)
    {
        public static void ThrowIfNegative(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} ('{value}') must not be netagive.");
        }
    }
#endif

#if NETFRAMEWORK || NETSTANDARD2_0
    extension(ushort)
    {
        public static ushort Parse(ReadOnlySpan<char> s)
            => ushort.Parse(s);
        public static bool TryParse(ReadOnlySpan<char> s, out ushort result)
            => ushort.TryParse(s.ToString(), out result);
    }

    extension(int)
    {
        public static bool TryParse(ReadOnlySpan<char> s, out int result)
            => int.TryParse(s.ToString(), out result);
    }

    extension(uint)
    {
        public static bool TryParse(ReadOnlySpan<char> s, out uint result)
            => uint.TryParse(s.ToString(), out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out uint result)
            => uint.TryParse(s.ToString(), style, provider, out result);
    }

    extension(long)
    {
        public static bool TryParse(ReadOnlySpan<char> s, out long result)
            => long.TryParse(s.ToString(), out result);
    }

    extension(ulong)
    {
        public static bool TryParse(ReadOnlySpan<char> s, out ulong result)
            => ulong.TryParse(s.ToString(), out result);
    }
#endif

#if !NET8_0_OR_GREATER
    extension(decimal)
    {
        public static bool TryParse(ReadOnlySpan<char> s, out decimal result)
            => decimal.TryParse(s.ToString(), out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out decimal result)
            => decimal.TryParse(s.ToString(), style, provider, out result);
    }
#endif

#if !NETSTANDARD2_1_OR_GREATER && !NETCOREAPP2_0_OR_GREATER
    extension<TKey, TValue>(global::System.Collections.Generic.Dictionary<TKey, TValue> source)
    {
        public bool TryAdd(TKey key, TValue value)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (source.ContainsKey(key))
            {
                return false;
            }

            source.Add(key, value);

            return true;
        }
    }
#endif

#if !NET8_0_OR_GREATER
    extension<T>(global::System.Collections.Generic.List<T> list)
    {
        public void AddRange(ReadOnlySpan<T> source)
        {
            ArgumentNullException.ThrowIfNull(list);

            if (!source.IsEmpty)
            {
                if (list.Capacity < list.Count + source.Length)
                {
                    list.Capacity = list.Count + source.Length;

                    foreach (var item in source)
                    {
                        list.Add(item);
                    }
                }
            }
        }
    }
#endif
}
