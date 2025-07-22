using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class STUNHeader
    {
        [Obsolete("Use ParseSTUNHeader(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static STUNHeader ParseSTUNHeader(byte[] buffer)
        {
            return ParseSTUNHeader(buffer.AsSpan());
        }

        [Obsolete("Use ParseSTUNHeader(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static STUNHeader ParseSTUNHeader(ArraySegment<byte> bufferSegment)
        {
            return ParseSTUNHeader(bufferSegment.AsSpan());
        }
    }
}
