using System;
using System.Runtime.CompilerServices;

namespace SIPSorcery.Sys
{
#if NET6_0_OR_GREATER
    internal ref partial struct ValueStringBuilder
    {
        /// <summary>
        /// Appends a value that implements ISpanFormattable to the string builder using span-based formatting.
        /// If span formatting fails, falls back to regular string formatting.
        /// </summary>
        /// <typeparam name="T">The type of the value to format. Must implement ISpanFormattable.</typeparam>
        /// <param name="value">The value to append.</param>
        /// <param name="format">A format string that defines the formatting to apply, or null to use default formatting.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information, or null to use default formatting.</param>
        internal void AppendSpanFormattable<T>(T value, string? format = null, IFormatProvider? provider = null) where T : ISpanFormattable
        {
            if (value.TryFormat(_chars.Slice(_pos), out int charsWritten, format, provider))
            {
                _pos += charsWritten;
            }
            else
            {
                Append(value.ToString(format, provider));
            }
        }
    }
#endif
}
