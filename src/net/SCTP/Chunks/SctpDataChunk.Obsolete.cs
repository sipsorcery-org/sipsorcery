using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class SctpDataChunk
    {
        /// <summary>
        /// Parses the DATA chunk fields
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        [Obsolete("Use Parse(ParseChunk<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpDataChunk ParseChunk(byte[] buffer, int posn)
            => ParseChunk(buffer.AsSpan(posn));
    }
}
