//-----------------------------------------------------------------------------
// Filename: TypeExtensions.cs
//
// Description: Helper methods.
//
// Author(s):
// Aaron Clauson
//
// History:
// ??	Aaron Clauson	Created.
// 21 Jan 2020  Aaron Clauson   Added HexStr and ParseHexStr (borrowed from
//                              Bitcoin Core source).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SIPSorcery.Sys
{
    public static class TypeExtensions
    {
        // The Trim method only trims 0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0085, 0x2028, and 0x2029.
        // This array adds in control characters.
        public static readonly char[] WhiteSpaceChars = new char[] { (char)0x00, (char)0x01, (char)0x02, (char)0x03, (char)0x04, (char)0x05,
        (char)0x06, (char)0x07, (char)0x08, (char)0x09, (char)0x0a, (char)0x0b, (char)0x0c, (char)0x0d, (char)0x0e, (char)0x0f,
        (char)0x10, (char)0x11, (char)0x12, (char)0x13, (char)0x14, (char)0x15, (char)0x16, (char)0x17, (char)0x18, (char)0x19, (char)0x20,
        (char)0x1a, (char)0x1b, (char)0x1c, (char)0x1d, (char)0x1e, (char)0x1f, (char)0x7f, (char)0x85, (char)0x2028, (char)0x2029 };

        private static readonly sbyte[] _hexDigits =
            { -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          0,1,2,3,4,5,6,7,8,9,-1,-1,-1,-1,-1,-1,
          -1,0xa,0xb,0xc,0xd,0xe,0xf,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,0xa,0xb,0xc,0xd,0xe,0xf,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,
          -1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, };

        private static readonly char[] hexmap = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        /// <summary>    
        /// Gets a value that indicates whether or not the string is empty.    
        /// </summary>    
        public static bool IsNullOrBlank(this string s)
        {
            if (s == null || s.AsSpan().Trim(WhiteSpaceChars).Length == 0)
            {
                return true;
            }

            return false;
        }

        public static bool NotNullOrBlank(this string s)
        {
            if (s == null || s.AsSpan().Trim(WhiteSpaceChars).Length == 0)
            {
                return false;
            }

            return true;
        }

        [Obsolete("Use ToUnixTime.")]
        public static long GetEpoch(this DateTime dateTime)
        {
            var unixTime = dateTime.ToUniversalTime() -
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            return Convert.ToInt64(unixTime.TotalSeconds);
        }

        public static long ToUnixTime(this DateTime dateTime)
        {
            return new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeSeconds();
        }

        /// <summary>
        /// Returns a slice from a string that is delimited by the first instance of a 
        /// start and end character. The delimiting characters are not included.
        /// 
        /// <code>
        /// "sip:127.0.0.1:5060;connid=1234".slice(':', ';') => "127.0.0.1:5060"
        /// </code>
        /// </summary>
        /// <param name="s">The input string to extract the slice from.</param>
        /// <param name="startDelimiter">The character to start the slice from. The first instance of the character found is used.</param>
        /// <param name="endDelimeter">The character to end the slice on. The first instance of the character found is used.</param>
        /// <returns>A slice of the input string or null if the slice is not possible.</returns>
        public static string Slice(this string s, char startDelimiter, char endDelimeter)
        {
            if (String.IsNullOrEmpty(s))
            {
                return null;
            }
            else
            {
                int startPosn = s.IndexOf(startDelimiter);
                int endPosn = s.IndexOf(endDelimeter) - 1;

                if (endPosn > startPosn)
                {
                    return s.Substring(startPosn + 1, endPosn - startPosn);
                }
                else
                {
                    return null;
                }
            }
        }

        public static string HexStr(this byte[] buffer, char? separator = null)
        {
            return HexStr(buffer.AsSpan(), separator: separator, lowercase: false);
        }

        public static string HexStr(this byte[] buffer, int length, char? separator = null)
        {
            return HexStr(buffer.AsSpan(0, buffer.Length), separator: separator, lowercase: false);
        }

        public static string HexStr(this byte[] buffer, int length, char? separator = null, bool lowercase = false)
        {
            return HexStr(buffer.AsSpan(0, length), separator: separator, lowercase: lowercase);
        }

        public static string HexStr(this Span<byte> buffer, char? separator = null, bool lowercase = false)
        {
            return HexStr((ReadOnlySpan<byte>)buffer, separator: separator, lowercase: lowercase);
        }

        public static string HexStr(this ReadOnlySpan<byte> buffer, char? separator = null, bool lowercase = false)
        {
            using var sb = new ValueStringBuilder(stackalloc char[256]);
            sb.Append(buffer, separator, lowercase);
            return sb.ToString();
        }

        public static byte[] ParseHexStr(string hexStr)
        {
            List<byte> buffer = new List<byte>();
            var chars = hexStr.ToCharArray();
            int posn = 0;
            while (posn < hexStr.Length)
            {
                while (char.IsWhiteSpace(chars[posn]))
                {
                    posn++;
                }
                sbyte c = _hexDigits[chars[posn++]];
                if (c == -1)
                {
                    break;
                }
                sbyte n = (sbyte)(c << 4);
                c = _hexDigits[chars[posn++]];
                if (c == -1)
                {
                    break;
                }
                n |= c;
                buffer.Add((byte)n);
            }
            return buffer.ToArray();
        }

        //#if NET472 || NETSTANDARD2_0
        public static void Deconstruct<T1, T2>(this KeyValuePair<T1, T2> tuple, out T1 key, out T2 value)
        {
            key = tuple.Key;
            value = tuple.Value;
        }
        //#endif

        public static bool IsPrivate(this IPAddress address)
        {
            return IPSocket.IsPrivateAddress(address.ToString());
        }


        /// <summary>
        /// Purpose of this extension is to allow deconstruction of a list into a fixed size tuple.
        /// </summary>
        /// <example>
        /// (var field0, var field1) = "a b c".Split();
        /// </example>
        public static void Deconstruct<T>(this IList<T> list, out T first, out T second)
        {
            first = list.Count > 0 ? list[0] : default(T);
            second = list.Count > 1 ? list[1] : default(T);
        }

        /// <summary>
        /// Purpose of this extension is to allow deconstruction of a list into a fixed size tuple.
        /// </summary>
        /// <example>
        /// (var field0, var field1, var field2) = "a b c".Split();
        /// </example>
        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out T third)
        {
            first = list.Count > 0 ? list[0] : default(T);
            second = list.Count > 1 ? list[1] : default(T);
            third = list.Count > 2 ? list[2] : default(T);
        }

        /// <summary>
        /// Purpose of this extension is to allow deconstruction of a list into a fixed size tuple.
        /// </summary>
        /// <example>
        /// (var field0, var field1, var field2, var field3) = "a b c d".Split();
        /// </example>
        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out T third, out T fourth)
        {
            first = list.Count > 0 ? list[0] : default(T);
            second = list.Count > 1 ? list[1] : default(T);
            third = list.Count > 2 ? list[2] : default(T);
            fourth = list.Count > 3 ? list[3] : default(T);
        }

#if !NET8_0_OR_GREATER
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source) where TKey : notnull
            => global::System.Linq.Enumerable.ToDictionary(source, kvp => kvp.Key, kvp => kvp.Value);
#endif

#if !NETsTANDARD2_1_OR_GREATER && !NETCOREAPP2_0_OR_GREATER
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> source, TKey key, TValue value)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.ContainsKey(key))
            {
                return false;
            }

            source.Add(key, value);

            return true;
        }
#endif

#if !NET8_0_OR_GREATER
        public static void AddRange<T>(this List<T> list, ReadOnlySpan<T> source)
        {
            if (list is null)
            {
                throw new ArgumentNullException(nameof(list));
            }

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
#endif
    }

#if !(NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER)
    internal static class UInt32
    {
        public static bool TryParse(ReadOnlySpan<char> s, out uint result)
            => uint.TryParse(s.ToString(), out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out uint result)
            => uint.TryParse(s.ToString(), style, provider, out result);
    }
    internal static class Int32
    {
        public static bool TryParse(ReadOnlySpan<char> s, out int result)
            => int.TryParse(s.ToString(), out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out int result)
            => int.TryParse(s.ToString(), style, provider, out result);
    }
    internal static class Int64
    {
        public static bool TryParse(ReadOnlySpan<char> s, out long result)
            => long.TryParse(s.ToString(), out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out long result)
            => long.TryParse(s.ToString(), style, provider, out result);
    }
    internal static class UInt64
    {
        public static bool TryParse(ReadOnlySpan<char> s, out ulong result)
            => ulong.TryParse(s.ToString(), out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out ulong result)
            => ulong.TryParse(s.ToString(), style, provider, out result);
    }
    internal static class UInt16
    {
        public static bool TryParse(ReadOnlySpan<char> s, out ushort result)
            => ushort.TryParse(s.ToString(), out result);
        public static ushort Parse(ReadOnlySpan<char> s)
            => ushort.Parse(s.ToString());
    }
    internal static class Decimal
    {
        public static bool TryParse(ReadOnlySpan<char> s, out decimal result)
            => decimal.TryParse(s.ToString(), out result);
        public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out decimal result)
            => decimal.TryParse(s.ToString(), style, provider, out result);
    }
#endif

#if !NET8_0_OR_GREATER
    internal static class ArgumentExceptionExtensions
    {
        public static void ThrowIfNullOrEmpty(string? argument, string? paramName = default)
        {
            if (string.IsNullOrEmpty(argument))
            {
                ThrowArgumentException(paramName);
                return;

                static void ThrowArgumentException(string paramName)
                {
                    throw new System.ArgumentException("Argument cannot be null or empty.", paramName);
                }
            }
        }

        public static void ThrowIfNullOrWhiteSpace(string? argument, string? paramName = default)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                ThrowArgumentException(paramName);

                static void ThrowArgumentException(string paramName)
                {
                    throw new System.ArgumentException("Argument cannot be null or whitespace.", paramName);
                }
            }
        }
    }

    internal static class ArgumentNullExceptionExtensions
    {
        public static void ThrowIfNull([System.Diagnostics.CodeAnalysis.NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
            {
                ThrowArgumentNullException(paramName);

                static void ThrowArgumentNullException(string paramName)
                {
                    throw new System.ArgumentNullException("Argument cannot be null or whitespace.", paramName);
                }
            }
        }
    }

    internal static class ArgumentOutOfRangeExceptionExtensions
    {
        public static void ThrowIfNegative(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} ('{value}') must be a non-negative value.");
            }
        }

        public static void ThrowIfGreaterThan<T>(T value, T other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where T : IComparable<T>
        {
            if (value.CompareTo(other) > 0)
            {
                throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} ('{value}') cannot be greater than {other}.");
            }
        }
    }

    internal static class ObjectDisposedExceptionExtensions
    {
        public static void ThrowIf(bool condition, object instance)
        {
            if (condition)
            {
                throw new ObjectDisposedException(instance?.GetType().FullName);
            }
        }
    }
#endif
}

#if !NET8_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>Specifies that a type has required members or that a member is required.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }

    /// <summary>
    /// Indicates that compiler support for a particular feature is required for the location where this attribute is applied.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName)
        {
            FeatureName = featureName;
        }

        /// <summary>
        /// The name of the compiler feature.
        /// </summary>
        public string FeatureName { get; }

        /// <summary>
        /// If true, the compiler can choose to allow access to the location where this attribute is applied if it does not understand <see cref="FeatureName"/>.
        /// </summary>
        public bool IsOptional { get; set; }

        /// <summary>
        /// The <see cref="FeatureName"/> used for the ref structs C# feature.
        /// </summary>
        public const string RefStructs = nameof(RefStructs);

        /// <summary>
        /// The <see cref="FeatureName"/> used for the required members C# feature.
        /// </summary>
        public const string RequiredMembers = nameof(RequiredMembers);
    }

#if !NETCOREAPP3_0_OR_GREATER
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName)
        {
            ParameterName = parameterName;
        }

        public string ParameterName { get; }
    }
#endif

    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif

namespace System.Diagnostics.CodeAnalysis
{
#if !NETCOREAPP3_0_OR_GREATER && !NETSTANDARD2_1_OR_GREATER
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;
        public bool ReturnValue { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;
        public bool ReturnValue { get; }
    }

    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class NotNullAttribute : Attribute
    {
    }
#endif
}
