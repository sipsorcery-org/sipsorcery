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

using System.Collections.Generic;
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
        private const int GAP_REPORT_LENGTH = 4;
        private const int DUPLICATE_TSN_LENGTH = 4;

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
        /// The gap ACK blocks. Each entry represents a gap in the forward out of order
        /// TSNs received.
        /// </summary>
        public List<SctpTsnGapBlock> GapAckBlocks = new List<SctpTsnGapBlock>();

        /// <summary>
        /// Indicates the number of times a TSN was received in duplicate
        /// since the last SACK was sent.
        /// </summary>
        public List<uint> DuplicateTSN = new List<uint>();

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
            var len = (ushort)(SCTP_CHUNK_HEADER_LENGTH + 
                FIXED_PARAMETERS_LENGTH +
                GapAckBlocks.Count * GAP_REPORT_LENGTH +
                DuplicateTSN.Count * DUPLICATE_TSN_LENGTH);

            // Guaranteed to be in a 4 byte boundary so no need to pad.
            return len;
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
            NetConvert.ToBuffer((ushort)GapAckBlocks.Count, buffer, startPosn + 8);
            NetConvert.ToBuffer((ushort)DuplicateTSN.Count, buffer, startPosn + 10);

            int reportPosn = startPosn + FIXED_PARAMETERS_LENGTH;

            foreach (var gapBlock in GapAckBlocks)
            {
                NetConvert.ToBuffer(gapBlock.Start, buffer, reportPosn);
                NetConvert.ToBuffer(gapBlock.End, buffer, reportPosn + 2);
                reportPosn += GAP_REPORT_LENGTH;
            }

            foreach(var dupTSN in DuplicateTSN)
            {
                NetConvert.ToBuffer(dupTSN, buffer, reportPosn);
                reportPosn += DUPLICATE_TSN_LENGTH;
            }

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
            ushort numGapAckBlocks = NetConvert.ParseUInt16(buffer, startPosn + 8);
            ushort numDuplicateTSNs = NetConvert.ParseUInt16(buffer, startPosn + 10);

            int reportPosn = startPosn + FIXED_PARAMETERS_LENGTH;

            for (int i=0; i < numGapAckBlocks; i++)
            {
                ushort start = NetConvert.ParseUInt16(buffer, reportPosn);
                ushort end = NetConvert.ParseUInt16(buffer, reportPosn + 2);
                sackChunk.GapAckBlocks.Add(new SctpTsnGapBlock { Start = start, End = end });
                reportPosn += GAP_REPORT_LENGTH;
            }

            for(int j=0; j < numDuplicateTSNs; j++)
            {
                sackChunk.DuplicateTSN.Add(NetConvert.ParseUInt32(buffer, reportPosn));
                reportPosn += DUPLICATE_TSN_LENGTH;
            }

            return sackChunk;
        }
    }
}
