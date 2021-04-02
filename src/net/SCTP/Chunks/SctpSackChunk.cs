//-----------------------------------------------------------------------------
// Filename: SctpSackChunk.cs
//
// Description: Represents the SCTP Selective Acknowledgement (SACK) chunk.
//
// Remarks:
// Defined in section 3.3.4 of RFC4960:
// https://tools.ietf.org/html/rfc4960#section-3.3.4
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
    /// This chunk is sent to the peer endpoint to acknowledge received DATA
    /// chunks and to inform the peer endpoint of gaps in the received
    /// sub-sequences of DATA chunks as represented by their Transmission
    /// Sequence Numbers (TSN).
    /// </summary>
    public class SctpSackChunk : SctpChunk
    {
        public const int FIXED_PARAMETERS_LENGTH = 12;

        /// <summary>
        /// This parameter contains the TSN of the last chunk received in
        /// sequence before any gaps.
        /// </summary>
        public uint CumulativeTsnAck;

        /// <summary>
        /// Advertised Receiver Window Credit. This field indicates the updated 
        /// receive buffer space in bytes of the sender of this SACK
        /// </summary>
        public uint ARwnd;

        /// <summary>
        /// Indicates the number of Gap Ack Blocks included in this SACK.
        /// </summary>
        public ushort NumberGapAckBlocks;

        /// <summary>
        /// This field contains the number of duplicate TSNs the endpoint has
        /// received.Each duplicate TSN is listed following the Gap Ack
        /// Block list.
        /// </summary>
        public ushort NumberDuplicateTSNs;

        public ushort ReportsLength;

        private SctpSackChunk() : base(SctpChunkType.SACK)
        { }

        /// <summary>
        /// Creates a new SACK chunk.
        /// </summary>
        /// <param name="cumulativeTsnAck">The last TSN that was received from the remote party.</param>
        /// <param name="arwnd">The current Advertised Receiver Window Credit.</param>
        public SctpSackChunk(uint cumulativeTsnAck, uint arwnd) : base(SctpChunkType.SACK)
        {
            CumulativeTsnAck = cumulativeTsnAck;
            ARwnd = arwnd;
        }

        /// <summary>
        /// Calculates the padded length for the chunk.
        /// </summary>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The length of the chunk.</returns>
        public override ushort GetChunkLength(bool padded)
        {
            var len = (ushort)(SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH + ReportsLength);
            return (padded) ? SctpPadding.PadTo4ByteBoundary(len) : len;
        }

        /// <summary>
        /// Serialises the SACK chunk to a pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public override ushort WriteTo(byte[] buffer, int posn)
        {
            WriteChunkHeader(buffer, posn);

            ushort startPosn = (ushort)(posn + SCTP_CHUNK_HEADER_LENGTH);

            NetConvert.ToBuffer(CumulativeTsnAck, buffer, startPosn);
            NetConvert.ToBuffer(ARwnd, buffer, startPosn + 4);
            NetConvert.ToBuffer(NumberGapAckBlocks, buffer, startPosn + 8);
            NetConvert.ToBuffer(NumberDuplicateTSNs, buffer, startPosn + 10);

            return GetChunkLength(true);
        }

        /// <summary>
        /// Parses the SACK chunk fields.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        public static SctpSackChunk ParseChunk(byte[] buffer, int posn)
        {
            var sackChunk = new SctpSackChunk();
            ushort chunkLen = sackChunk.ParseFirstWord(buffer, posn);

            ushort startPosn = (ushort)(posn + SCTP_CHUNK_HEADER_LENGTH);

            sackChunk.CumulativeTsnAck = NetConvert.ParseUInt32(buffer, startPosn);
            sackChunk.ARwnd = NetConvert.ParseUInt32(buffer, startPosn + 4);
            sackChunk.NumberGapAckBlocks = NetConvert.ParseUInt16(buffer, startPosn + 8);
            sackChunk.NumberDuplicateTSNs = NetConvert.ParseUInt16(buffer, startPosn + 10);

            sackChunk.ReportsLength = (ushort)(chunkLen - SCTP_CHUNK_HEADER_LENGTH - FIXED_PARAMETERS_LENGTH);

            // TODO: Parse reports.

            return sackChunk;
        }
    }
}
