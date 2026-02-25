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

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace SIPSorcery.Net;

/// <summary>
/// This chunk is sent to the peer endpoint to acknowledge received DATA
/// chunks and to inform the peer endpoint of gaps in the received
/// sub-sequences of DATA chunks as represented by their Transmission
/// Sequence Numbers (TSN).
/// </summary>
public partial class SctpSackChunk : SctpChunk
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
    public override ushort GetByteCount(bool padded)
    {
        var len = (ushort)(SCTP_CHUNK_HEADER_LENGTH +
            FIXED_PARAMETERS_LENGTH +
            GapAckBlocks.Count * GAP_REPORT_LENGTH +
            DuplicateTSN.Count * DUPLICATE_TSN_LENGTH);

        // Guaranteed to be in a 4 byte boundary so no need to pad.
        return len;
    }

    /// <inheritdoc/>
    public override ushort WriteTo(IBufferWriter<byte> writer)
    {
        var byteCount = GetByteCount(true);
        var buffer = writer.GetSpan(byteCount);

        var bytesWritten = WriteChunkHeader(buffer);

        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH), CumulativeTsnAck);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 4), ARwnd);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 8), (ushort)GapAckBlocks.Count);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(SCTP_CHUNK_HEADER_LENGTH + 10), (ushort)DuplicateTSN.Count);

        var reportPosn = SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH;

        foreach (var gapBlock in GapAckBlocks)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(reportPosn), gapBlock.Start);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(reportPosn + 2), gapBlock.End);
            reportPosn += GAP_REPORT_LENGTH;
        }

        foreach (var dupTSN in DuplicateTSN)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(reportPosn), dupTSN);
            reportPosn += DUPLICATE_TSN_LENGTH;
        }

        writer.Advance(byteCount);

        return (ushort)byteCount;
    }

    /// <summary>
    /// Parses the SACK chunk fields.
    /// </summary>
    /// <param name="buffer">The buffer holding the serialised chunk.</param>
    public static SctpSackChunk ParseChunk(ReadOnlySpan<byte> buffer)
    {
        var sackChunk = new SctpSackChunk();
        var chunkLen = sackChunk.ParseFirstWord(buffer);

        // The chunk must be long enough to hold the fixed parameters before they are read. The caller
        // only guarantees the chunk length is at least an SCTP chunk header and that the chunk fits in
        // the buffer, so anything shorter than this would read bytes belonging to whatever follows.
        if (chunkLen < SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH)
        {
            throw new SipSorceryException($"The SCTP SACK chunk was too short. The minimum length is {SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH} bytes but the chunk specified {chunkLen} bytes.");
        }

        sackChunk.CumulativeTsnAck = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice((ushort)(SCTP_CHUNK_HEADER_LENGTH)));
        sackChunk.ARwnd = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(((ushort)(SCTP_CHUNK_HEADER_LENGTH)) + 4));
        var numGapAckBlocks = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(((ushort)(SCTP_CHUNK_HEADER_LENGTH)) + 8));
        var numDuplicateTSNs = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(((ushort)(SCTP_CHUNK_HEADER_LENGTH)) + 10));

        // The gap ack block and duplicate TSN counts are supplied by the remote party and each allows
        // up to 65535 entries, so they must be checked against the length the chunk actually declared.
        // Without this the loops below read whatever follows the chunk in the receive buffer: far
        // enough past it and the read leaves the buffer entirely, and the resulting
        // IndexOutOfRangeException is not one of the recoverable parse failures the SCTP receive loop
        // expects, so it terminates the receive thread and with it the association. Short of that the
        // reads stay in bounds and quietly turn stale bytes left by earlier packets into gap ack
        // blocks and duplicate TSNs, corrupting the sender's retransmission state instead.
        var requiredLen = SCTP_CHUNK_HEADER_LENGTH + FIXED_PARAMETERS_LENGTH
            + numGapAckBlocks * GAP_REPORT_LENGTH
            + numDuplicateTSNs * DUPLICATE_TSN_LENGTH;

        if (requiredLen > chunkLen)
        {
            throw new SipSorceryException($"The SCTP SACK chunk was too short for the gap ack block and duplicate TSN counts it specified. Required {requiredLen} bytes but the chunk specified {chunkLen} bytes.");
        }

        var reportPosn = ((ushort)(SCTP_CHUNK_HEADER_LENGTH)) + FIXED_PARAMETERS_LENGTH;

        for (var i = 0; i < numGapAckBlocks; i++)
        {
            var start = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(reportPosn));
            var end = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(reportPosn + 2));
            sackChunk.GapAckBlocks.Add(new SctpTsnGapBlock { Start = start, End = end });
            reportPosn += GAP_REPORT_LENGTH;
        }

        for (var j = 0; j < numDuplicateTSNs; j++)
        {
            sackChunk.DuplicateTSN.Add(BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(reportPosn)));
            reportPosn += DUPLICATE_TSN_LENGTH;
        }

        return sackChunk;
    }
}
