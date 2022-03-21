//-----------------------------------------------------------------------------
// Filename: SctpForwardCumulativeTSNChunk.cs
//
// Description: Represents the SCTP Forward TSN chunk.
//
// Remarks:
// Defined in section 3.2 of RFC3758:
// https://datatracker.ietf.org/doc/html/rfc3758#section-3.2
//
// Author(s):
// Cam Newnham (camnewnham@gmail.com)
// 
// History:
// 21 Mar 2022	Cam Newnham
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// This chunk shall be used by the data sender to inform the data
    /// receiver to adjust its cumulative received TSN point forward because
    /// some missing TSNs are associated with data chunks that SHOULD NOT be
    /// transmitted or retransmitted by the sender.
    /// </summary>
    public class SctpForwardCumulativeTSNChunk : SctpChunk
    {
        private const int STREAM_SEQUENCE_ASSOCIATION_LENGTH = 4;
        private const int NEW_CUMULATIVE_TSN_LENGTH = 4;

        /// <summary>
        /// This indicates the new cumulative TSN to the data receiver.Upon
        /// the reception of this value, the data receiver MUST consider
        /// any missing TSNs earlier than or equal to this value as received,
        /// and stop reporting them as gaps in any subsequent SACKs.
        /// </summary>
        public uint NewCumulativeTSN;

        /// <summary>
        /// Pairings between stream number and stream sequence number. 
        /// Key: This field holds a stream number that was skipped by this FWD-TSN.
        /// Value: This field holds the sequence number associated with the stream
        /// that was skipped.The stream sequence field holds the largest
        /// stream sequence number in this stream being skipped.The receiver
        /// of the FWD-TSN's can use the Stream-N and Stream Sequence-N fields
        /// to enable delivery of any stranded TSN's that remain on the stream
        /// re-ordering queues.This field MUST NOT report TSN's corresponding
        /// to DATA chunks that are marked as unordered.For ordered DATA
        /// chunks this field MUST be filled in.
        /// </summary>
        public Dictionary<ushort,ushort> StreamSequenceAssociations = new Dictionary<ushort,ushort>();

        private SctpForwardCumulativeTSNChunk() : base(SctpChunkType.FORWARDTSN)
        { }

        /// <summary>
        /// Creates a new Forward Cumulative TSN (FORWARD TSN) chunk.
        /// </summary>
        /// <param name="newCumulativeTsn">The new cumulative TSN. The receiver will stop reporting missing TSNs prior.</param>
        public SctpForwardCumulativeTSNChunk(uint newCumulativeTsn) : base(SctpChunkType.FORWARDTSN)
        {
            NewCumulativeTSN = newCumulativeTsn;
        }

        /// <summary>
        /// Calculates the padded length for the chunk.
        /// </summary>
        /// <param name="padded">If true the length field will be padded to a 4 byte boundary.</param>
        /// <returns>The length of the chunk.</returns>
        public override ushort GetChunkLength(bool padded)
        {
            var len = (ushort)(SCTP_CHUNK_HEADER_LENGTH +
                NEW_CUMULATIVE_TSN_LENGTH +
                 StreamSequenceAssociations.Count * STREAM_SEQUENCE_ASSOCIATION_LENGTH);

            // Guaranteed to be in a 4 byte boundary so no need to pad.
            return len;
        }

        /// <summary>
        /// Serialises the FORWARD TSN chunk to a pre-allocated buffer.
        /// </summary>
        /// <param name="buffer">The buffer to write the serialised chunk bytes to. It
        /// must have the required space already allocated.</param>
        /// <param name="posn">The position in the buffer to write to.</param>
        /// <returns>The number of bytes, including padding, written to the buffer.</returns>
        public override ushort WriteTo(byte[] buffer, int posn)
        {
            WriteChunkHeader(buffer, posn);

            ushort startPosn = (ushort)(posn + SCTP_CHUNK_HEADER_LENGTH);

            NetConvert.ToBuffer(NewCumulativeTSN, buffer, startPosn);

            int reportPosn = startPosn + NEW_CUMULATIVE_TSN_LENGTH;

            foreach (var seqPair in StreamSequenceAssociations)
            {
                NetConvert.ToBuffer(seqPair.Key, buffer, reportPosn);
                NetConvert.ToBuffer(seqPair.Value, buffer, reportPosn + 2);
                reportPosn += STREAM_SEQUENCE_ASSOCIATION_LENGTH;
            }

            return GetChunkLength(true);
        }

        /// <summary>
        /// Parses the FORWARD TSN chunk fields.
        /// </summary>
        /// <param name="buffer">The buffer holding the serialised chunk.</param>
        /// <param name="posn">The position to start parsing at.</param>
        public static SctpForwardCumulativeTSNChunk ParseChunk(byte[] buffer, int posn)
        {
            var fwdTsnChunk = new SctpForwardCumulativeTSNChunk();

            ushort chunkLen = fwdTsnChunk.ParseFirstWord(buffer, posn);

            ushort startPosn = (ushort)(posn + SCTP_CHUNK_HEADER_LENGTH);

            fwdTsnChunk.NewCumulativeTSN = NetConvert.ParseUInt32(buffer, startPosn);

            int reportPosn = startPosn + NEW_CUMULATIVE_TSN_LENGTH;

            int numStreamSeqAssoc = chunkLen - reportPosn; 

            for (int i = 0; i < numStreamSeqAssoc; i++)
            {
                ushort streamNum = NetConvert.ParseUInt16(buffer, reportPosn);
                ushort seqNum = NetConvert.ParseUInt16(buffer, reportPosn + 2);
                fwdTsnChunk.StreamSequenceAssociations.Add(streamNum, seqNum);
                reportPosn += STREAM_SEQUENCE_ASSOCIATION_LENGTH;
            }

            return fwdTsnChunk;
        }
    }
}
