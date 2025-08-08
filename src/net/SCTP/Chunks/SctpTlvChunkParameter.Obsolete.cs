using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class SctpTlvChunkParameter
    {
        /// <summary>
        /// The first 32 bits of all chunk parameters represent the type and length. This method
        /// parses those fields and sets them on the current instance.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk parameter.</param>
        /// <param name="posn">The position in the buffer that indicates the start of the chunk parameter.</param>
        [Obsolete("Use ParseFirstWord(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public ushort ParseFirstWord(byte[] buffer, int posn)
            => ParseFirstWord(buffer.AsSpan(posn));

        /// <summary>
        /// Parses an SCTP Type-Length-Value (TLV) chunk parameter from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised TLV chunk parameter.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP TLV chunk parameter instance.</returns>
        [Obsolete("Use ParseTlvParameter(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpTlvChunkParameter ParseTlvParameter(byte[] buffer, int posn)
            => ParseTlvParameter(buffer.AsSpan(posn));
    }
}
