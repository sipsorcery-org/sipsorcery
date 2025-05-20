/*
* Filename: RTCPTWCCFeedback.cs
*
* Description:
* Transport Wide Congestion Control (TWCC) Feedback Packet
*         0                   1                   2                   3
*         0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
*        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
* header |V=2|P| FMT=15  |    PT=205     |             length            |
*        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
*        |                     SSRC of packet sender                     |
*        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
*        |                     SSRC of media source                      |
*        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
* TWCC   |           Base Sequence Number         | Packet Status Count  |
* header +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
*        |                  Reference Time (24 bits)   | Fbk pkt cnt (8 bits)|
*        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
*        |                                                               |
*        |             Packet Status Chunks (variable)                 |
*        |                                                               |
*        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
*        |                                                               |
*        |               Receive Delta(s) (variable)                     |
*        |                                                               |
*        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
*
*
* Author:        Sean Tearney
* Date:          2025 - 02 - 22
*
* License:       BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
* 
* Change Log:
*   2025-02-20  Initial creation.
*/
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum TWCCPacketStatusType
    {
        NotReceived = 0,
        ReceivedSmallDelta = 1,
        ReceivedLargeDelta = 2,
        Reserved = 3
    }

    /// <summary>
    /// Represents the status of a single RTP packet in a TWCC feedback message.
    /// </summary>
    public class TWCCPacketStatus
    {
        /// <summary>
        /// The RTP sequence number for this packet.
        /// </summary>
        public ushort SequenceNumber { get; set; }
        /// <summary>
        /// The reception status.
        /// </summary>
        public TWCCPacketStatusType Status { get; set; }
        /// <summary>
        /// The receive time delta in (raw) units (typically 250 µs per unit). Null if not received.
        /// </summary>
        public int? Delta { get; set; }
    }

    /// <summary>
    /// Parser and serializer for RTCP TWCC feedback messages as per RFC 8888.
    /// 
    /// Format:
    /// 
    ///   0                   1                   2                   3
    ///   0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///  |V=2|P|   FMT=15  |       PT=205      |          length             |
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///  |                      SSRC of packet sender                    |
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///  |                      SSRC of media source                     |
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///  |      Base Sequence Number     |    Packet Status Count        |
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///  |                      Reference Time                           |
    ///  |                 (24 bits)       | FB Packet Count (8 bits)      |
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///  |                                                               |
    ///  |             Packet Status Chunks (variable length)            |
    ///  |                                                               |
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    ///  |                                                               |
    ///  |                Receive Delta Values (variable length)         |
    ///  |                                                               |
    ///  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    /// 
    /// Packet Status Chunks are 16-bit fields and come in two flavors:
    /// 
    /// 1. **Run Length Chunk** (when the two MSB are 00):
    ///    - Bits 15–14: Type (00)
    ///    - Bits 13–12: Packet Status Symbol (0–3)
    ///    - Bits 11–0 : Run Length (number of consecutive packets with that symbol)
    /// 
    /// 2. **Status Vector Chunk** (when the two MSB are 10 or 11):
    ///    - Bits 15–14: Type (10 for two-bit symbols, 11 for one-bit symbols)
    ///    - Bits 13–0 : For two-bit mode: seven 2-bit symbols; for one-bit mode: fourteen 1-bit symbols.
    ///      In one-bit mode, a bit value of 0 means packet not received; a value of 1 means received (assumed small delta).
    /// 
    /// For every packet marked as received (i.e. status of ReceivedSmallDelta or ReceivedLargeDelta),
    /// a delta field is present in the delta section. For small delta the field is 1 byte (signed),
    /// for large delta it is 2 bytes (signed, network order).
    /// </summary>
    public class RTCPTWCCFeedback
    {
        public RTCPHeader Header { get; private set; }
        public uint SenderSSRC { get; private set; }
        public uint MediaSSRC { get; private set; }

        /// <summary>
        /// The first (base) sequence number covered by this feedback.
        /// </summary>
        public ushort BaseSequenceNumber { get; private set; }

        /// <summary>
        /// Total number of packet statuses described.
        /// </summary>
        public ushort PacketStatusCount { get; private set; }

        /// <summary>
        /// 24-bit reference time (in 1/64 seconds) from the top 24 bits of this 32-bit word.
        /// </summary>
        public uint ReferenceTime { get; private set; }

        /// <summary>
        /// Feedback packet count (the lower 8 bits of the 32-bit word containing ReferenceTime).
        /// </summary>
        public byte FeedbackPacketCount { get; private set; }

        /// <summary>
        /// The list of per-packet statuses (in order from BaseSequenceNumber).
        /// </summary>
        public List<TWCCPacketStatus> PacketStatuses { get; private set; } = new List<TWCCPacketStatus>();

        /// <summary>
        /// The resolution multiplier for delta values (e.g. 250 µs per unit).
        /// </summary>
        public int DeltaScale { get; set; } = 250;

        /// <summary>
        /// Constructs a TWCC feedback message from the raw RTCP packet.
        /// </summary>
        /// <param name="packet">The complete RTCP TWCC feedback packet.</param>
        /// <summary>
        /// Parses a TWCC feedback packet from the given byte array.
        /// </summary>
        [Obsolete("Use RTCPSDesReport(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPTWCCFeedback(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
        }

        /// <summary>
        /// Constructs a TWCC feedback message from the raw RTCP packet.
        /// </summary>
        /// <param name="packet">The complete RTCP TWCC feedback packet.</param>
        /// <summary>
        /// Parses a TWCC feedback packet from the given byte array.
        /// </summary>
        public RTCPTWCCFeedback(ReadOnlySpan<byte> packet)
        {
            ValidatePacket(packet);

            // Parse the RTCP header.
            Header = new RTCPHeader(packet);
            packet = packet.Slice(RTCPHeader.HEADER_BYTES_LENGTH);

            // Parse sender and media SSRCs
            SenderSSRC = BinaryOperations.ReadUInt32BigEndian(ref packet);
            MediaSSRC = BinaryOperations.ReadUInt32BigEndian(ref packet);

            // Parse Base Sequence Number and Packet Status Count
            BaseSequenceNumber = BinaryOperations.ReadUInt16BigEndian(ref packet);
            PacketStatusCount = BinaryOperations.ReadUInt16BigEndian(ref packet);

            // Parse Reference Time and Feedback Packet Count
            ReferenceTime = ParseReferenceTime(ref packet, out byte fbCount);
            FeedbackPacketCount = fbCount;

            // Parse status chunks
            var statusSymbols = ParseStatusChunks(ref packet);

            // Parse delta values with validation
            var deltaValues = ParseDeltaValues(ref packet, statusSymbols);

            // Build final packet status list
            BuildPacketStatusList(statusSymbols, deltaValues);
            
        }

        private void ParseRunLengthChunk(ushort chunk, List<TWCCPacketStatusType> statusSymbols, ref int remainingStatuses)
        {
            // The status bits might be reversed from what we expect
            int statusBits = (chunk >> 12) & 0x3;
            TWCCPacketStatusType symbol;

            switch (statusBits)
            {
                case 0: // 00
                    symbol = TWCCPacketStatusType.NotReceived;
                    break;
                case 1: // 01
                    symbol = TWCCPacketStatusType.ReceivedSmallDelta;
                    break;
                case 2: // 10
                    symbol = TWCCPacketStatusType.ReceivedSmallDelta; // Changed from Large to Small
                    break;
                case 3: // 11
                    symbol = TWCCPacketStatusType.ReceivedLargeDelta;
                    break;
                default:
                    throw new ArgumentException($"Invalid status bits: {statusBits}");
            }

            ushort runLength = (ushort)(chunk & 0x0FFF);
            
            runLength = (ushort)Math.Min(runLength, remainingStatuses);
            for (int i = 0; i < runLength; i++)
            {
                statusSymbols.Add(symbol);
            }
            remainingStatuses -= runLength;
        }

        [Obsolete("Use ValidatePacket(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        private void ValidatePacket(byte[] packet)
        {
            if (packet == null)
            {
                throw new ArgumentNullException(nameof(packet));
            }

            ValidatePacket(new ReadOnlySpan<byte>(packet));
        }

        private void ValidatePacket(ReadOnlySpan<byte> packet)
        {
            if (packet.Length < (RTCPHeader.HEADER_BYTES_LENGTH + 12))
            {
                throw new ArgumentException("Packet too short to be a valid TWCC feedback message.");
            }
        }

        private uint ParseReferenceTime(ref ReadOnlySpan<byte> packet, out byte fbCount)
        {
            if (packet.Length < 4)
            {
                throw new ArgumentException("Packet truncated at reference time.");
            }

            var b1 = packet[0];
            var b2 = packet[1];
            var b3 = packet[2];
            fbCount = packet[3];
            packet = packet.Slice(4);
            return (uint)((b1 << 16) | (b2 << 8) | b3);
        }

        private List<TWCCPacketStatusType> ParseStatusChunks(ref ReadOnlySpan<byte> packet)
        {
            var statusSymbols = new List<TWCCPacketStatusType>();
            int remainingStatuses = PacketStatusCount;

            while (remainingStatuses > 0)
            {
                if (packet.Length < 2)
                {
                    throw new ArgumentException($"Packet truncated during status chunk parsing. Expected {remainingStatuses} more statuses.");
                }

                var chunk = BinaryOperations.ReadUInt16BigEndian(ref packet);
                var chunkType = chunk >> 14;

                switch (chunkType)
                {
                    case 0: // Run Length Chunk
                        ParseRunLengthChunk(chunk, statusSymbols, ref remainingStatuses);
                        break;
                    case 2: // Two-bit Status Vector
                        ParseTwoBitStatusVector(chunk, statusSymbols, ref remainingStatuses);
                        break;
                    case 3: // One-bit Status Vector
                        ParseOneBitStatusVector(chunk, statusSymbols, ref remainingStatuses);
                        break;
                }
            }

            return statusSymbols;
        }


        private void ParseTwoBitStatusVector(ushort chunk, List<TWCCPacketStatusType> statusSymbols, ref int remainingStatuses)
        {
            int symbolsToRead = Math.Min(7, remainingStatuses);
            for (int i = 0; i < symbolsToRead; i++)
            {
                int shift = 12 - (2 * i);
                int symVal = (chunk >> shift) & 0x3;
                statusSymbols.Add((TWCCPacketStatusType)symVal);
            }
            remainingStatuses -= symbolsToRead;
        }

        private void ParseOneBitStatusVector(ushort chunk, List<TWCCPacketStatusType> statusSymbols, ref int remainingStatuses)
        {
            int symbolsToRead = Math.Min(14, remainingStatuses);
            for (int i = 0; i < symbolsToRead; i++)
            {
                int shift = 13 - i;
                int bit = (chunk >> shift) & 0x1;
                statusSymbols.Add(bit == 0 ? TWCCPacketStatusType.NotReceived : TWCCPacketStatusType.ReceivedSmallDelta);
            }
            remainingStatuses -= symbolsToRead;
        }

        private (List<int> deltaValues, int lastOffset) ParseDeltaValues(byte[] packet, int offset, List<TWCCPacketStatusType> statusSymbols)
        {
            var deltaValues = new List<int>();
            int expectedDeltaCount = statusSymbols.Count(s =>
                s == TWCCPacketStatusType.ReceivedSmallDelta ||
                s == TWCCPacketStatusType.ReceivedLargeDelta);

            foreach (var status in statusSymbols)
            {
                if (status == TWCCPacketStatusType.NotReceived || status == TWCCPacketStatusType.Reserved)
                {
                    deltaValues.Add(0);
                    continue;
                }

                // Check if we have enough data for the delta
                int deltaSize = status == TWCCPacketStatusType.ReceivedSmallDelta ? 1 : 2;
                if (offset + deltaSize > packet.Length)
                {
                    // Instead of throwing, we'll add a special value to indicate truncation
                    deltaValues.Add(int.MinValue);
                    break;
                }

                if (status == TWCCPacketStatusType.ReceivedSmallDelta)
                {
                    deltaValues.Add((sbyte)packet[offset] * DeltaScale);
                    offset += 1;
                }
                else // ReceivedLargeDelta
                {
                    short rawDelta = (short)((packet[offset] << 8) | packet[offset + 1]);
                    deltaValues.Add(rawDelta * DeltaScale);
                    offset += 2;
                }
            }

            return (deltaValues, offset);
        }

        private List<int> ParseDeltaValues(ref ReadOnlySpan<byte> packet, List<TWCCPacketStatusType> statusSymbols)
        {
            var deltaValues = new List<int>();
            int expectedDeltaCount = statusSymbols.Count(s => s is TWCCPacketStatusType.ReceivedSmallDelta or TWCCPacketStatusType.ReceivedLargeDelta);

            foreach (var status in statusSymbols)
            {
                if (status is TWCCPacketStatusType.NotReceived or TWCCPacketStatusType.Reserved)
                {
                    deltaValues.Add(0);
                    continue;
                }

                // Check if we have enough data for the delta
                var deltaSize = status == TWCCPacketStatusType.ReceivedSmallDelta ? 1 : 2;
                if (deltaSize > packet.Length)
                {
                    // Instead of throwing, we'll add a special value to indicate truncation
                    deltaValues.Add(int.MinValue);
                    break;
                }

                if (status == TWCCPacketStatusType.ReceivedSmallDelta)
                {
                    deltaValues.Add((sbyte)packet[0] * DeltaScale);
                    packet = packet.Slice(1);
                }
                else // ReceivedLargeDelta
                {
                    var rawDelta = (short)((packet[0] << 8) | packet[1]);
                    deltaValues.Add(rawDelta * DeltaScale);
                    packet = packet.Slice(2);
                }
            }

            return deltaValues;
        }

        private void BuildPacketStatusList(List<TWCCPacketStatusType> statusSymbols, List<int> deltaValues)
        {
            PacketStatuses = new List<TWCCPacketStatus>();
            ushort seq = BaseSequenceNumber;

            for (int i = 0; i < statusSymbols.Count; i++)
            {
                int? delta = deltaValues[i] == int.MinValue ? null :
                            (statusSymbols[i] == TWCCPacketStatusType.NotReceived ||
                             statusSymbols[i] == TWCCPacketStatusType.Reserved) ? null : deltaValues[i];

                PacketStatuses.Add(new TWCCPacketStatus
                {
                    SequenceNumber = seq++,
                    Status = statusSymbols[i],
                    Delta = delta
                });
            }
        }

        public int GetPacketSize()
        {
            var length = RTCPHeader.HEADER_BYTES_LENGTH + 4 + 4 + 2 + 2 + 4;

            var packageStatusesCount = PacketStatuses.Count;
            for (var i = 0; i < packageStatusesCount;)
            {
                // Try to use run-length chunk: count how many consecutive statuses are identical.
                var runLength = 1;
                var current = PacketStatuses[i].Status;
                while (i + runLength < packageStatusesCount && PacketStatuses[i + runLength].Status == current && runLength < 0x0FFF)
                {
                    runLength++;
                }
                if (runLength >= 2)
                {
                    // Build run-length chunk.
                    // Currently:
                    // ushort chunk = (ushort)(((int)current & 0x3) << 12);

                    // Need to modify to use correct status bit mapping:
                    ushort statusBits;
                    switch (current)
                    {
                        case TWCCPacketStatusType.NotReceived:
                            statusBits = 0; // 00
                            break;
                        case TWCCPacketStatusType.ReceivedSmallDelta:
                            statusBits = 1; // 01 for small delta
                                            // Note: status 10 (2) also means small delta
                            break;
                        case TWCCPacketStatusType.ReceivedLargeDelta:
                            statusBits = 3; // 11 for large delta
                            break;
                        default:
                            statusBits = 0;
                            break;
                    }

                    var chunk = (ushort)(statusBits << 12);
                    chunk |= (ushort)(runLength & 0x0FFF);
                    length += 2;
                    i += runLength;
                }
                else
                {
                    // Otherwise, pack into a two-bit status vector chunk.
                    var count = Math.Min(7, packageStatusesCount - i);
                    ushort chunk = 0x8000; // Set top bits to 10 for vector chunk

                    for (var j = 0; j < count; j++)
                    {
                        // Convert status to correct bit pattern
                        ushort statusBits;
                        switch (PacketStatuses[i + j].Status)
                        {
                            case TWCCPacketStatusType.NotReceived:
                                statusBits = 0;
                                break;
                            case TWCCPacketStatusType.ReceivedSmallDelta:
                                statusBits = 1;
                                break;
                            case TWCCPacketStatusType.ReceivedLargeDelta:
                                statusBits = 3;
                                break;
                            default:
                                statusBits = 0;
                                break;
                        }

                        chunk |= (ushort)(statusBits << (12 - 2 * j));
                    }
                    length += 2;
                    i += count;
                }
            }

            foreach (var ps in PacketStatuses)
            {
                length += GetDeltaLength(ps);
            }

            var check = GetBytes().Length;

            Debug.Assert(check == length);

            return check;

            static int GetDeltaLength(TWCCPacketStatus ps)
            {
                if (ps.Status == TWCCPacketStatusType.ReceivedSmallDelta)
                {
                    return 1;
                }
                
                if (ps.Status == TWCCPacketStatusType.ReceivedLargeDelta)
                {
                    return 2;
                }

                return 0;
            }
        }

        /// <summary>
        /// Serializes this TWCC feedback message to a byte array.
        /// Note: The serialization logic rebuilds the packet status chunks from the PacketStatuses list.
        /// This implements the run-length chunk when possible and defaults to two-bit
        /// status vector chunks if a run-length encoding isn’t efficient.
        /// </summary>
        /// <returns>The serialized RTCP TWCC feedback packet.</returns>
        public byte[] GetBytes()
                    {
            var buffer = new byte[GetPacketSize()];

            WriteBytesCore(buffer);

            return buffer;
                    }

        public int WriteBytes(Span<byte> buffer)
        {
            var size = GetPacketSize();

            if (buffer.Length < size)
            {
                throw new ArgumentOutOfRangeException($"The buffer should have at least {size} bytes and had only {buffer.Length}.");
                }

            WriteBytesCore(buffer.Slice(0, size));

            return size;
            }

        private void WriteBytesCore(Span<byte> buffer)
        {
            // Update the header length (in 32-bit words minus one).
            Header.SetLength((ushort)(buffer.Length / 4 - 1));
            _ = Header.WriteBytes(buffer);
            buffer = buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH);

            // Write Sender and Media SSRC.
            BinaryOperations.WriteUInt32BigEndian(ref buffer, SenderSSRC);
            BinaryOperations.WriteUInt32BigEndian(ref buffer, MediaSSRC);

            // Write Base Sequence Number and Packet Status Count.
            BinaryOperations.WriteUInt16BigEndian(ref buffer, BaseSequenceNumber);
            BinaryOperations.WriteUInt16BigEndian(ref buffer, PacketStatusCount);

            // Build the 32-bit word for ReferenceTime and FeedbackPacketCount.
            BinaryOperations.WriteUInt32BigEndian(ref buffer, (ReferenceTime << 8) | FeedbackPacketCount);

            for (var i = 0; i < PacketStatuses.Count;)
            {
                // Try to use run-length chunk: count how many consecutive statuses are identical.
                var runLength = 1;
                var current = PacketStatuses[i].Status;
                while (i + runLength < PacketStatuses.Count && PacketStatuses[i + runLength].Status == current && runLength < 0x0FFF)
            {
                    runLength++;
                }
                if (runLength >= 2)
                {
                    // Build run-length chunk.
                    // Currently:
                    // ushort chunk = (ushort)(((int)current & 0x3) << 12);

                    // Need to modify to use correct status bit mapping:
                    ushort statusBits;
                    switch (current)
                    {
                        case TWCCPacketStatusType.NotReceived:
                            statusBits = 0; // 00
                            break;
                        case TWCCPacketStatusType.ReceivedSmallDelta:
                            statusBits = 1; // 01 for small delta
                                            // Note: status 10 (2) also means small delta
                            break;
                        case TWCCPacketStatusType.ReceivedLargeDelta:
                            statusBits = 3; // 11 for large delta
                            break;
                        default:
                            statusBits = 0;
                            break;
                    }

                    var chunk = (ushort)(statusBits << 12);
                    chunk |= (ushort)(runLength & 0x0FFF);
                    BinaryOperations.WriteUInt16BigEndian(ref buffer, chunk);
                    i += runLength;
            }
                else
                {
                    // Otherwise, pack into a two-bit status vector chunk.
                    var count = Math.Min(7, PacketStatuses.Count - i);
                    ushort chunk = 0x8000; // Set top bits to 10 for vector chunk

                    for (var j = 0; j < count; j++)
                    {
                        // Convert status to correct bit pattern
                        ushort statusBits;
                        switch (PacketStatuses[i + j].Status)
            {
                            case TWCCPacketStatusType.NotReceived:
                                statusBits = 0;
                                break;
                            case TWCCPacketStatusType.ReceivedSmallDelta:
                                statusBits = 1;
                                break;
                            case TWCCPacketStatusType.ReceivedLargeDelta:
                                statusBits = 3;
                                break;
                            default:
                                statusBits = 0;
                                break;
            }

                        chunk |= (ushort)(statusBits << (12 - 2 * j));
                    }
                    BinaryOperations.WriteUInt16BigEndian(ref buffer, chunk);
                    i += count;
                }
            }

            foreach (var ps in PacketStatuses)
            {
                if (ps.Status == TWCCPacketStatusType.ReceivedSmallDelta)
                {
                    // Delta was stored already scaled; convert back to raw units.
                    var delta = (sbyte)(ps.Delta.GetValueOrDefault() / DeltaScale);
                    buffer[0] = (byte)delta;
                    buffer = buffer.Slice(1);
                }
                else if (ps.Status == TWCCPacketStatusType.ReceivedLargeDelta)
                {
                    var delta = (short)(ps.Delta.GetValueOrDefault() / DeltaScale);
                    var high = (byte)(delta >> 8);
                    var low = (byte)(delta & 0xFF);
                    buffer[0] = high;
                    buffer[1] = low;
                    buffer = buffer.Slice(2);
                }
                // For not received or reserved, no delta bytes are added.
            }
        }

        public override string ToString()
        {
            var packetStatusInfo = string.Join(", ", PacketStatuses.Select(ps =>
                $"Seq:{ps.SequenceNumber}({ps.Status}{(ps.Delta.HasValue ? $",Δ:{ps.Delta.Value}" : "")})"));

            return $"TWCC Feedback: SenderSSRC={SenderSSRC}, MediaSSRC={MediaSSRC}, BaseSeq={BaseSequenceNumber}, " +
                   $"StatusCount={PacketStatusCount}, RefTime={ReferenceTime} (1/64 sec), " +
                   $"FbkPktCount={FeedbackPacketCount}, PacketStatuses=[{packetStatusInfo}]";
        }
    }
}
