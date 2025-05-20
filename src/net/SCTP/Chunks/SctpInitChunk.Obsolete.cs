using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class SctpInitChunk
    {
        /// <summary>
        /// Parses the INIT or INIT ACK chunk fields
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        [Obsolete("Use ParseChunk(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpInitChunk ParseChunk(byte[] buffer, int posn)
            => ParseChunk(buffer.AsSpan(posn));
    }
}
