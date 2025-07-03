using System;
using System.ComponentModel;

namespace SIPSorcery.Net
{
    partial class SctpChunk
    {
        /// <summary>
        /// The first 32 bits of all chunks represent the same 3 fields. This method
        /// parses those fields and sets them on the current instance.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position in the buffer that indicates the start of the chunk.</param>
        /// <returns>The chunk length value.</returns>
        [Obsolete("Use ParseFirstWord(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public ushort ParseFirstWord(byte[] buffer, int posn)
            => ParseFirstWord(buffer.AsSpan(posn));

        /// <summary>
        /// Parses a simple chunk and does not attempt to process any chunk value.
        /// This method is suitable when:
        ///  - the chunk type consists only of the 4 byte header and has 
        ///    no fixed or variable parameters set.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP chunk instance.</returns>
        [Obsolete("Use ParseBaseChunk(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpChunk ParseBaseChunk(byte[] buffer, int posn)
            => ParseBaseChunk(buffer.AsSpan(posn));

        /// <summary>
        /// Parses an SCTP chunk from a buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        /// <returns>An SCTP chunk instance.</returns>
        [Obsolete("Use Parse(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static SctpChunk Parse(byte[] buffer, int posn)
            => Parse(buffer.AsSpan(posn));

        /// <summary>
        /// Extracts the padded length field from a serialised chunk buffer.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The start position of the serialised chunk.</param>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The padded length of the serialised chunk.</returns>
        [Obsolete("Use Parse(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static uint GetChunkLengthFromHeader(byte[] buffer, int posn, bool padded)
            => GetChunkLengthFromHeader(buffer.AsSpan(posn), padded);

        /// <summary>
        /// Copies an unrecognised chunk to a byte buffer and returns it. This method is
        /// used to assist in reporting unrecognised chunk types.
        /// </summary>
        /// <param name="buffer">The buffer containing the chunk.</param>
        /// <param name="posn">The position in the buffer that the unrecognised chunk starts.</param>
        /// <returns>A new buffer containing a copy of the chunk.</returns>
        [Obsolete("Use Parse(ReadOnlySpan<byte>) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static byte[] CopyUnrecognisedChunk(byte[] buffer, int posn)
            => CopyUnrecognisedChunk(buffer.AsSpan(posn));
    }
}
