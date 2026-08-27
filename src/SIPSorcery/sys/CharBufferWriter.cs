using System.Runtime.CompilerServices;

namespace System.Buffers;

internal static partial class CharBufferWriter
{
    extension(IBufferWriter<char> writer)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBufferWriter<char> Write(bool value) => writer.Write(value ? "true" : "false");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBufferWriter<char> Write(char value)
        {
            writer.GetSpan(1)[0] = value;
            writer.Advance(1);
            return writer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBufferWriter<char> Write(string value)
        {
            if (value is not null)
            {
                writer.Write(value.AsSpan());
            }

            return writer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBufferWriter<char> WriteLine() => writer.Write(Environment.NewLine);

        public IBufferWriter<char> Write(char value, int count)
        {
            var destination = writer.GetSpan(count).Slice(0, count);
            destination.Fill(value);
            writer.Advance(count);
            return writer;
        }

        public IBufferWriter<char> Write(scoped ReadOnlySpan<char> value)
        {
            value.CopyTo(writer.GetSpan(value.Length));
            writer.Advance(value.Length);
            return writer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<char> WriteSpan(int length)
        {
            var span = writer.GetSpan(length).Slice(0, length);
            writer.Advance(length);
            return span;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public IBufferWriter<char> Write(int value, string format = null, IFormatProvider provider = null) => writer.WriteSpanFormattable(value, format, provider);
#else
        public IBufferWriter<char> Write(int value, string format = null, IFormatProvider provider = null) => writer.Write(value.ToString(format, provider));
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public IBufferWriter<char> Write(uint value, string format = null, IFormatProvider provider = null) => writer.WriteSpanFormattable(value, format, provider);
#else
        public IBufferWriter<char> Write(uint value, string format = null, IFormatProvider provider = null) => writer.Write(value.ToString(format, provider));
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public IBufferWriter<char> Write(ushort value, string format = null, IFormatProvider provider = null) => writer.WriteSpanFormattable(value, format, provider);
#else
        public IBufferWriter<char> Write(ushort value, string format = null, IFormatProvider provider = null) => writer.Write(value.ToString(format, provider));
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBufferWriter<char> Write(ushort? value, string format = null, IFormatProvider provider = null)
        {
            if (value is { } actualValue)
            {
#if NET6_0_OR_GREATER
                writer.WriteSpanFormattable(actualValue, format, provider);
#else
                writer.Write(actualValue.ToString(format, provider));
#endif
            }

            return writer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public IBufferWriter<char> Write(long value, string format = null, IFormatProvider provider = null) => writer.WriteSpanFormattable(value, format, provider);
#else
        public IBufferWriter<char> Write(long value, string format = null, IFormatProvider provider = null) => writer.Write(value.ToString(format, provider));
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public IBufferWriter<char> Write(ulong value, string format = null, IFormatProvider provider = null) => writer.WriteSpanFormattable(value, format, provider);
#else
        public IBufferWriter<char> Write(ulong value, string format = null, IFormatProvider provider = null) => writer.Write(value.ToString(format, provider));
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public IBufferWriter<char> Write(float value, string format = null, IFormatProvider provider = null) => writer.WriteSpanFormattable(value, format, provider);
#else
        public IBufferWriter<char> Write(float value, string format = null, IFormatProvider provider = null) => writer.Write(value.ToString(format, provider));
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public IBufferWriter<char> Write(double value, string format = null, IFormatProvider provider = null) => writer.WriteSpanFormattable(value, format, provider);
#else
        public IBufferWriter<char> Write(double value, string format = null, IFormatProvider provider = null) => writer.Write(value.ToString(format, provider));
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET6_0_OR_GREATER
        public IBufferWriter<char> Write(decimal value, string format = null, IFormatProvider provider = null) => writer.WriteSpanFormattable(value, format, provider);
#else
        public IBufferWriter<char> Write(decimal value, string format = null, IFormatProvider provider = null) => writer.Write(value.ToString(format, provider));
#endif
    }
}
