using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// An endpoint in an association MUST use this chunk to initiate a
    /// graceful close of the association with its peer.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.8
    /// </remarks>
    partial class SctpShutdownChunk
    {
        /// <summary>
        /// Parses the SHUTDOWN chunk fields.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        [Obsolete("Use ParseChunk(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpShutdownChunk ParseChunk(byte[] buffer, int posn)
            => ParseChunk(buffer.AsSpan(posn));
    }
}
