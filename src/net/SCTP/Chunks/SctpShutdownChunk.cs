//-----------------------------------------------------------------------------
// Filename: SctpShutdownChunk.cs
//
// Description: Represents the SCTP SHUTDOWN chunk.
//
// Remarks:
// Defined in section 3.3.8 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.8
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Mar 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// An endpoint in an association MUST use this chunk to initiate a
    /// graceful close of the association with its peer.
    /// </summary>
    /// <remarks>
    /// https://tools.ietf.org/html/rfc4960#section-3.3.8
    /// </remarks>
    public class SctpShutdownChunk : SctpChunk
    {
        public const int FIXED_PARAMETERS_LENGTH = 4;

        /// <summary>
        /// This parameter contains the TSN of the last chunk received in
        /// sequence before any gaps.
        /// </summary>
        public uint? CumulativeTsnAck;

        private SctpShutdownChunk() : base(SctpChunkType.SHUTDOWN)
        { }

        /// <summary>
        /// Creates a new SHUTDOWN chunk.
        /// </summary>
        /// <param name="cumulativeTsnAck">The last TSN that was received from the remote party.</param>
        public SctpShutdownChunk(uint? cumulativeTsnAck) : base(SctpChunkType.SHUTDOWN)
        {
            CumulativeTsnAck = cumulativeTsnAck;
        }

        /// <summary>
        /// Calculates the padded length for the chunk.
        /// </summary>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The padded length of the chunk.</returns>
        public override ushort GetChunkLength(bool padded)
        {
            return SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH;
        }

        /// <summary>
        /// Serialises the SHUTDOWN chunk to a pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public override ushort WriteTo(byte[] buffer, int posn)
        {
            WriteChunkHeader(buffer, posn);
            NetConvert.ToBuffer(CumulativeTsnAck.GetValueOrDefault(), buffer, posn + SCTP_CHUNK_HEADER_LENGTH);
            return GetChunkLength(true);
        }

        /// <summary>
        /// Parses the SHUTDOWN chunk fields.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        public static SctpShutdownChunk ParseChunk(byte[] buffer, int posn)
        {
            var shutdownChunk = new SctpShutdownChunk();
            shutdownChunk.CumulativeTsnAck = NetConvert.ParseUInt32(buffer, posn + SCTP_CHUNK_HEADER_LENGTH);
            return shutdownChunk;
        }
    }
}
