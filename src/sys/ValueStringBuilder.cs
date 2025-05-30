// Based on System.Text.ValueStringBuilder - System.Console

using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SIPSorcery.Sys
{
    /// <summary>
    /// A ref struct that provides a low-allocation way to build strings.
    /// Similar to StringBuilder but stackalloc-based for better performance.
    /// </summary>
    internal ref partial struct ValueStringBuilder
    {
        /// <summary>The array to return to the array pool, if one was rented.</summary>
        private char[]? _arrayToReturnToPool;
        /// <summary>The span containing the characters written so far.</summary>
        private Span<char> _chars;
        /// <summary>The current position within the span.</summary>
        private int _pos;

        /// <summary>
        /// Initializes a new instance of ValueStringBuilder with a provided character buffer.
        /// </summary>
        /// <param name="initialBuffer">The initial buffer to use for storing characters.</param>
        public ValueStringBuilder(Span<char> initialBuffer)
        {
            _arrayToReturnToPool = null;
            _chars = initialBuffer;
            _pos = 0;
        }

        /// <summary>
        /// Initializes a new instance of ValueStringBuilder with a specified initial capacity.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity of the internal buffer.</param>
        public ValueStringBuilder(int initialCapacity)
        {
            _arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
            _chars = _arrayToReturnToPool;
            _pos = 0;
        }

        /// <summary>
        /// Gets or sets the length of the current builder's content.
        /// </summary>
        public int Length
        {
            get => _pos;
            set
            {
                Debug.Assert(value >= 0);
                Debug.Assert(value <= _chars.Length);
                _pos = value;
            }
        }

        /// <summary>
        /// Gets the total capacity of the builder's buffer.
        /// </summary>
        public int Capacity => _chars.Length;

        /// <summary>
        /// Gets a read-only span containing the builder's characters.
        /// </summary>
        public ReadOnlySpan<char> Chars => _chars;

        /// <summary>
        /// Ensures the builder has enough capacity to accommodate a specified total number of characters.
        /// </summary>
        /// <param name="capacity">The minimum capacity needed.</param>
        public void EnsureCapacity(int capacity)
        {
            // This is not expected to be called this with negative capacity
            Debug.Assert(capacity >= 0);

            // If the caller has a bug and calls this with negative capacity, make sure to call Grow to throw an exception.
            if ((uint)capacity > (uint)_chars.Length)
            {
                Grow(capacity - _pos);
            }
        }

        /// <summary>
        /// Get a pinnable reference to the builder.
        /// Does not ensure there is a null char after <see cref="Length"/>
        /// This overload is pattern matched in the C# 7.3+ compiler so you can omit
        /// the explicit method call, and write eg "fixed (char* c = builder)"
        /// </summary>
        /// <returns>A reference to the underlying characters.</returns>
        public ref char GetPinnableReference() => ref MemoryMarshal.GetReference(_chars);

        /// <summary>
        /// Get a pinnable reference to the builder.
        /// </summary>
        /// <param name="terminate">If <see langword="true"/>, ensures that the builder has a null char after <see cref="Length"/></param>
        /// <returns>A reference to the underlying characters.</returns>
        public ref char GetPinnableReference(bool terminate)
        {
            if (terminate)
            {
                EnsureCapacity(Length + 1);
                _chars[Length] = '\0';
            }
            return ref MemoryMarshal.GetReference(_chars);
        }

        /// <summary>
        /// Gets a reference to the character at the specified position.
        /// </summary>
        /// <param name="index">The zero-based index of the character to get.</param>
        /// <returns>A reference to the character at the specified position.</returns>
        public ref char this[int index]
        {
            get
            {
                Debug.Assert(index < _pos);
                return ref _chars[index];
            }
        }

        /// <summary>
        /// Returns the built string and disposes the builder.
        /// </summary>
        /// <returns>The final string.</returns>
        public new string ToString()
        {
            var s = _chars.Slice(0, _pos).ToString();
            Dispose();
            return s;
        }

        /// <summary>
        /// Returns the underlying storage of the builder.
        /// </summary>
        public Span<char> RawChars => _chars;

        /// <summary>
        /// Returns a span around the contents of the builder.
        /// </summary>
        /// <param name="terminate">If <see langword="true"/>, ensures that the builder has a null char after <see cref="Length"/></param>
        /// <returns>A read-only span of the builder's content.</returns>
        public ReadOnlySpan<char> AsSpan(bool terminate)
        {
            if (terminate)
            {
                EnsureCapacity(Length + 1);
                _chars[Length] = '\0';
            }
            return _chars.Slice(0, _pos);
        }

        /// <summary>Returns a read-only span of the builder's content.</summary>
        public ReadOnlySpan<char> AsSpan() => _chars.Slice(0, _pos);

        /// <summary>Returns a read-only span starting at the specified index.</summary>
        /// <param name="start">The starting index.</param>
        public ReadOnlySpan<char> AsSpan(int start) => _chars.Slice(start, _pos - start);

        /// <summary>Returns a read-only span of the specified length starting at the specified index.</summary>
        /// <param name="start">The starting index.</param>
        /// <param name="length">The length of the span.</param>
        public ReadOnlySpan<char> AsSpan(int start, int length) => _chars.Slice(start, length);

        /// <summary>
        /// Attempts to copy the builder's contents to a destination span.
        /// </summary>
        /// <param name="destination">The destination span.</param>
        /// <param name="charsWritten">When this method returns, contains the number of characters that were copied.</param>
        /// <returns><see langword="true"/> if the copy was successful; otherwise, <see langword="false"/>.</returns>
        public bool TryCopyTo(Span<char> destination, out int charsWritten)
        {
            if (_chars.Slice(0, _pos).TryCopyTo(destination))
            {
                charsWritten = _pos;
                Dispose();
                return true;
            }
            else
            {
                charsWritten = 0;
                Dispose();
                return false;
            }
        }

        /// <summary>
        /// Inserts a repeated character at the specified position.
        /// </summary>
        /// <param name="index">The position to insert at.</param>
        /// <param name="value">The character to insert.</param>
        /// <param name="count">The number of times to insert the character.</param>
        public void Insert(int index, char value, int count)
        {
            if (_pos > _chars.Length - count)
            {
                Grow(count);
            }

            var remaining = _pos - index;
            _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
            _chars.Slice(index, count).Fill(value);
            _pos += count;
        }

        /// <summary>
        /// Inserts a string at the specified position.
        /// </summary>
        /// <param name="index">The position to insert at.</param>
        /// <param name="s">The string to insert.</param>
        public void Insert(int index, string? s)
        {
            if (s == null)
            {
                return;
            }

            var count = s.Length;

            if (_pos > (_chars.Length - count))
            {
                Grow(count);
            }

            var remaining = _pos - index;
            _chars.Slice(index, remaining).CopyTo(_chars.Slice(index + count));
            s.AsSpan().CopyTo(_chars.Slice(index));
            _pos += count;
        }

        /// <summary>
        /// Appends a boolean value as its string representation.
        /// </summary>
        /// <param name="value">The value to append.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(bool value) => Append(value ? "true" : "false");

        /// <summary>
        /// Appends a character to the builder.
        /// </summary>
        /// <param name="c">The character to append.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(char c)
        {
            var pos = _pos;
            var chars = _chars;
            if ((uint)pos < (uint)chars.Length)
            {
                chars[pos] = c;
                _pos = pos + 1;
            }
            else
            {
                GrowAndAppend(c);
            }
        }

        /// <summary>
        /// Appends a string to the builder.
        /// </summary>
        /// <param name="s">The string to append.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(string? s)
        {
            if (s == null)
            {
                return;
            }

            var pos = _pos;
            if (s.Length == 1 && (uint)pos < (uint)_chars.Length) // very common case, e.g. appending strings from NumberFormatInfo like separators, percent symbols, etc.
            {
                _chars[pos] = s[0];
                _pos = pos + 1;
            }
            else
            {
                AppendSlow(s);
            }
        }

        /// <summary>
        /// Slow path for appending a string when the fast path isn't applicable.
        /// </summary>
        private void AppendSlow(string s)
        {
            var pos = _pos;
            if (pos > _chars.Length - s.Length)
            {
                Grow(s.Length);
            }

            s.AsSpan().CopyTo(_chars.Slice(pos));
            _pos += s.Length;
        }

        /// <summary>
        /// Appends a character multiple times to the builder.
        /// </summary>
        /// <param name="c">The character to append.</param>
        /// <param name="count">The number of times to append the character.</param>
        public void Append(char c, int count)
        {
            if (_pos > _chars.Length - count)
            {
                Grow(count);
            }

            var dst = _chars.Slice(_pos, count);
            for (var i = 0; i < dst.Length; i++)
            {
                dst[i] = c;
            }
            _pos += count;
        }

        /// <summary>
        /// Appends a span of characters to the builder.
        /// </summary>
        /// <param name="value">The span to append.</param>
        public void Append(scoped ReadOnlySpan<char> value)
        {
            var pos = _pos;
            if (pos > _chars.Length - value.Length)
            {
                Grow(value.Length);
            }

            value.CopyTo(_chars.Slice(_pos));
            _pos += value.Length;
        }

        /// <summary>
        /// Reserves space for a span of characters and returns a span that can be written to.
        /// </summary>
        /// <param name="length">The number of characters to reserve space for.</param>
        /// <returns>A span that can be written to.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<char> AppendSpan(int length)
        {
            var origPos = _pos;
            if (origPos > _chars.Length - length)
            {
                Grow(length);
            }

            _pos = origPos + length;
            return _chars.Slice(origPos, length);
        }

        /// <summary>
        /// Appends an <see langword="int"/> value to the builder.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="format">An optional format string that guides the formatting, or null to use default formatting.</param>
        /// <param name="provider">An optional object that provides culture-specific formatting services, or null to use default formatting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public void Append(int value, string? format = null, IFormatProvider? provider = null) => AppendSpanFormattable(value, format, provider);
#else
        public void Append(int value, string? format = null, IFormatProvider? provider = null) => Append(value.ToString(format, provider));
#endif

        /// <summary>
        /// Appends an <see langword="uint"/> value to the builder.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="format">An optional format string that guides the formatting, or null to use default formatting.</param>
        /// <param name="provider">An optional object that provides culture-specific formatting services, or null to use default formatting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public void Append(uint value, string? format = null, IFormatProvider? provider = null) => AppendSpanFormattable(value, format, provider);
#else
        public void Append(uint value, string? format = null, IFormatProvider? provider = null) => Append(value.ToString(format, provider));
#endif

        /// <summary>
        /// Appends an <see langword="ushort"/> value to the builder.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="format">An optional format string that guides the formatting, or null to use default formatting.</param>
        /// <param name="provider">An optional object that provides culture-specific formatting services, or null to use default formatting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public void Append(ushort value, string? format = null, IFormatProvider? provider = null) => AppendSpanFormattable(value, format, provider);
#else
        public void Append(ushort value, string? format = null, IFormatProvider? provider = null) => Append(value.ToString(format, provider));
#endif

        /// <summary>
        /// Appends an <see langword="ushort?"/> value to the builder.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="format">An optional format string that guides the formatting, or null to use default formatting.</param>
        /// <param name="provider">An optional object that provides culture-specific formatting services, or null to use default formatting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public void Append(ushort? value, string? format = null, IFormatProvider? provider = null)
        {
            if (value is { } v)
            {
                AppendSpanFormattable(v, format, provider);
            }
        }
#else
        public void Append(ushort? value, string? format = null, IFormatProvider? provider = null)
        {
            if (value is { } v)
            {
                Append(v.ToString(format, provider));
            }
        }
#endif

        /// <summary>
        /// Appends an <see langword="long"/> value to the builder.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="format">An optional format string that guides the formatting, or null to use default formatting.</param>
        /// <param name="provider">An optional object that provides culture-specific formatting services, or null to use default formatting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public void Append(long value, string? format = null, IFormatProvider? provider = null) => AppendSpanFormattable(value, format, provider);
#else
        public void Append(long value, string? format = null, IFormatProvider? provider = null) => Append(value.ToString(format, provider));
#endif

        /// <summary>
        /// Appends an <see langword="float"/> value to the builder.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="format">An optional format string that guides the formatting, or null to use default formatting.</param>
        /// <param name="provider">An optional object that provides culture-specific formatting services, or null to use default formatting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public void Append(float value, string? format = null, IFormatProvider? provider = null) => AppendSpanFormattable(value, format, provider);
#else
        public void Append(float value, string? format = null, IFormatProvider? provider = null) => Append(value.ToString(format, provider));
#endif

        /// <summary>
        /// Appends an <see langword="double"/> value to the builder.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="format">An optional format string that guides the formatting, or null to use default formatting.</param>
        /// <param name="provider">An optional object that provides culture-specific formatting services, or null to use default formatting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public void Append(double value, string? format = null, IFormatProvider? provider = null) => AppendSpanFormattable(value, format, provider);
#else
        public void Append(double value, string? format = null, IFormatProvider? provider = null) => Append(value.ToString(format, provider));
#endif

        /// <summary>
        /// Appends an <see langword="decimal"/> value to the builder.
        /// </summary>
        /// <param name="value">The value to append.</param>
        /// <param name="format">An optional format string that guides the formatting, or null to use default formatting.</param>
        /// <param name="provider">An optional object that provides culture-specific formatting services, or null to use default formatting.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public void Append(decimal value, string? format = null, IFormatProvider? provider = null) => AppendSpanFormattable(value, format, provider);
#else
        public void Append(decimal value, string? format = null, IFormatProvider? provider = null) => Append(value.ToString(format, provider));
#endif

        /// <summary>
        /// Grows the buffer and appends a single character.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowAndAppend(char c)
        {
            Grow(1);
            Append(c);
        }

        /// <summary>
        /// Resize the internal buffer either by doubling current buffer size or
        /// by adding <paramref name="additionalCapacityBeyondPos"/> to
        /// <see cref="_pos"/> whichever is greater.
        /// </summary>
        /// <param name="additionalCapacityBeyondPos">
        /// Number of chars requested beyond current position.
        /// </param>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow(int additionalCapacityBeyondPos)
        {
            Debug.Assert(additionalCapacityBeyondPos > 0);
            Debug.Assert(_pos > _chars.Length - additionalCapacityBeyondPos, "Grow called incorrectly, no resize is needed.");

            const uint ArrayMaxLength = 0x7FFFFFC7; // same as Array.MaxLength

            // Increase to at least the required size (_pos + additionalCapacityBeyondPos), but try
            // to double the size if possible, bounding the doubling to not go beyond the max array length.
            var newCapacity = (int)Math.Max(
                (uint)(_pos + additionalCapacityBeyondPos),
                Math.Min((uint)_chars.Length * 2, ArrayMaxLength));

            // Make sure to let Rent throw an exception if the caller has a bug and the desired capacity is negative.
            // This could also go negative if the actual required length wraps around.
            var poolArray = ArrayPool<char>.Shared.Rent(newCapacity);

            _chars.Slice(0, _pos).CopyTo(poolArray);

            var toReturn = _arrayToReturnToPool;
            _chars = _arrayToReturnToPool = poolArray;
            if (toReturn != null)
            {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }

        /// <summary>
        /// Disposes the builder, returning any rented array to the pool.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            var toReturn = _arrayToReturnToPool;
            this = default; // for safety, to avoid using pooled array if this instance is erroneously appended to again
            if (toReturn != null)
            {
                ArrayPool<char>.Shared.Return(toReturn);
            }
        }
    }
}
