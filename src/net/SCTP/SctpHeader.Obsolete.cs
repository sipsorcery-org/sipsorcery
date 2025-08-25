using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial struct SctpHeader
    {
        /// <summary>
        /// Parses the an SCTP header from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer to parse the SCTP header from.</param>
        /// <param name="posn">The position in the buffer to start parsing the header from.</param>
        /// <returns>A new SCTPHeaer instance.</returns>
        [Obsolete("Use Parse(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpHeader Parse(byte[] buffer, int posn)
            => Parse(buffer.AsSpan(posn));
    }
}
