#if NET6_0_OR_GREATER
namespace System.Buffers;

internal static partial class CharBufferWriter
{
    extension(IBufferWriter<char> writer)
    {
        internal IBufferWriter<char> WriteSpanFormattable<T>(T value, string format = null, IFormatProvider provider = null)
        where T : ISpanFormattable
        {
            var destination = writer.GetSpan();
            if (value.TryFormat(destination, out var charsWritten, format, provider))
            {
                writer.Advance(charsWritten);
            }
            else
            {
                writer.Write(value.ToString(format, provider));
            }

            return writer;
        }
    }
}
#endif
