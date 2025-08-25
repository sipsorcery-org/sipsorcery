using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This chunk is sent to the peer endpoint to acknowledge received DATA
    /// chunks and to inform the peer endpoint of gaps in the received
    /// sub-sequences of DATA chunks as represented by their Transmission
    /// Sequence Numbers (TSN).
    /// </summary>
    partial class SctpSackChunk
    {
        /// <summary>
        /// Parses the SACK chunk fields.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        [Obsolete("Use ParseChunk(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpSackChunk ParseChunk(byte[] buffer, int posn)
            => ParseChunk(buffer.AsSpan(posn));
    }
}
