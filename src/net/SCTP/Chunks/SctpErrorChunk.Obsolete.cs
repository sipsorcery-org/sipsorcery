using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    /// <summary>
    /// An endpoint sends this chunk to its peer endpoint to notify it of
    /// certain error conditions. It contains one or more error causes. An
    /// Operation Error is not considered fatal in and of itself, but may be
    /// used with an ABORT chunk to report a fatal condition.
    /// </summary>
    partial class SctpErrorChunk : SctpChunk
    {
        /// <summary>
        /// Parses the ERROR chunk fields.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        [Obsolete("Use Parse(ParseChunk<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpErrorChunk ParseChunk(byte[] buffer, int posn, bool isAbort)
            => ParseChunk(buffer.AsSpan(posn), isAbort);
    }
}
