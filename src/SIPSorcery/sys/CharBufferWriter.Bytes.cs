namespace System.Buffers;

internal static partial class CharBufferWriter
{
    private const string UpperHexMap = "0123456789ABCDEF";
    private const string LowerHexMap = "0123456789abcdef";

    extension(IBufferWriter<char> writer)
    {
        public IBufferWriter<char> Write(byte[] bytes, char? separator = null)
        {
            if (bytes is { Length: > 0 })
            {
                writer.Write(bytes.AsSpan(), separator);
            }

            return writer;
        }

        public IBufferWriter<char> Write(scoped ReadOnlySpan<byte> bytes, char? separator = null, bool lowercase = false)
        {
            if (bytes.IsEmpty)
            {
                return writer;
            }

            var hexMap = lowercase ? LowerHexMap : UpperHexMap;
            var separatorCount = separator is null ? 0 : bytes.Length - 1;
            var length = checked((bytes.Length * 2) + separatorCount);
            var destination = writer.GetSpan(length).Slice(0, length);
            var position = 0;

            for (var i = 0; i < bytes.Length; i++)
            {
                var value = bytes[i];
                destination[position++] = hexMap[value >> 4];
                destination[position++] = hexMap[value & 0b1111];

                if (separator is { } actualSeparator && i < bytes.Length - 1)
                {
                    destination[position++] = actualSeparator;
                }
            }

            writer.Advance(length);
            return writer;
        }
    }
}
