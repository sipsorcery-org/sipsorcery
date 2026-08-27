using System.Net;
using System.Runtime.CompilerServices;

namespace System.Buffers;

internal static partial class CharBufferWriter
{
    extension(IBufferWriter<char> writer)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IBufferWriter<char> Write(IPAddress value)
        {
#if NET8_0_OR_GREATER
            return writer.WriteSpanFormattable(value, null, null);
#else
            return writer.Write(value.ToString());
#endif
        }
    }
}
