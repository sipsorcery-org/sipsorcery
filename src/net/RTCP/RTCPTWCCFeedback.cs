using System;
using System.Collections.Generic;
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
        public int DeltaScale { get; set; } = 1; //spec says 250 but it seems to be pre-scaled on all major browsers

        /// <summary>
        /// Constructs a TWCC feedback message from the raw RTCP packet.
        /// </summary>
        /// <param name="packet">The complete RTCP TWCC feedback packet.</param>
        /// <summary>
        /// Parses a TWCC feedback packet from the given byte array.
        /// </summary>
        public RTCPTWCCFeedback(byte[] packet)
        {
            if (packet == null || packet.Length < (RTCPHeader.HEADER_BYTES_LENGTH + 12))
            {
                throw new ArgumentException("Packet too short to be a valid TWCC feedback message.");
            }

            // Parse the RTCP header.
            Header = new RTCPHeader(packet);
            int offset = RTCPHeader.HEADER_BYTES_LENGTH;

            // Parse sender and media SSRCs (each 4 bytes).
            SenderSSRC = ReadUInt32(packet, ref offset);
            MediaSSRC = ReadUInt32(packet, ref offset);

            // Parse Base Sequence Number (2 bytes) and Packet Status Count (2 bytes).
            BaseSequenceNumber = ReadUInt16(packet, ref offset);
            PacketStatusCount = ReadUInt16(packet, ref offset);

            // --- Parse the 24-bit Reference Time and 8-bit Feedback Packet Count ---
            // Do not use a 32-bit read since the field is 24+8 bits.
            byte b1 = packet[offset++];
            byte b2 = packet[offset++];
            byte b3 = packet[offset++];
            byte fbCount = packet[offset++];
            uint referenceTime24 = (uint)((b1 << 16) | (b2 << 8) | b3);
            ReferenceTime = referenceTime24;
            FeedbackPacketCount = fbCount;
            double refTimeSeconds = ReferenceTime / 64.0;

            // --- Parse Packet Status Chunks ---
            // The TWCC packet uses 16-bit chunks to encode the status of a number of packets.
            // We keep reading chunks until we have gathered PacketStatusCount statuses.
            List<TWCCPacketStatusType> statusSymbols = new List<TWCCPacketStatusType>();
            while (statusSymbols.Count < PacketStatusCount)
            {
                if (offset + 2 > packet.Length)
                {
                    throw new ApplicationException("Not enough data for packet status chunks.");
                }
                ushort chunk = ReadUInt16(packet, ref offset);

                int chunkType = chunk >> 14; // top 2 bits determine the chunk type
                if (chunkType == 0)
                {
                    // Run-Length Chunk:
                    // Bits 15-14: 00
                    // Bits 13-12: Packet status symbol (2 bits)
                    // Bits 11-0: Run length (number of packets)
                    TWCCPacketStatusType symbol = (TWCCPacketStatusType)((chunk >> 12) & 0x3);
                    ushort runLength = (ushort)(chunk & 0x0FFF);
                    for (int i = 0; i < runLength; i++)
                    {
                        statusSymbols.Add(symbol);
                    }
                }
                else if (chunkType == 2)
                {
                    // Status Vector Chunk (two-bit symbols)
                    // Bits 15-14: 10, remaining 14 bits represent seven 2-bit statuses.
                    for (int i = 0; i < 7; i++)
                    {
                        int shift = 14 - 2 * (i + 1);
                        int symVal = (chunk >> shift) & 0x3;
                        statusSymbols.Add((TWCCPacketStatusType)symVal);
                    }
                }
                else if (chunkType == 3)
                {
                    // Status Vector Chunk (one-bit symbols)
                    // Bits 15-14: 11, remaining 14 bits represent fourteen 1-bit statuses.
                    // In one-bit mode: 0 = not received, 1 = received (assumed small delta).
                    for (int i = 0; i < 14; i++)
                    {
                        int shift = 14 - (i + 1);
                        int bit = (chunk >> shift) & 0x1;
                        TWCCPacketStatusType symbol = (bit == 0) ? TWCCPacketStatusType.NotReceived : TWCCPacketStatusType.ReceivedSmallDelta;
                        statusSymbols.Add(symbol);
                    }
                }
                else
                {
                    throw new ApplicationException($"Unsupported packet status chunk type: {chunkType}");
                }
            }

            // Ensure we only have as many statuses as specified.
            if (statusSymbols.Count > PacketStatusCount)
            {
                statusSymbols = statusSymbols.Take(PacketStatusCount).ToList();
            }

            // --- Parse Delta Values ---
            // For every packet marked as received (small or large delta), there is an associated delta value.
            List<int> deltaValues = new List<int>();
            int deltaIndex = 0;
            while (offset < packet.Length && deltaIndex < statusSymbols.Count)
            {
                TWCCPacketStatusType status = statusSymbols[deltaIndex];
                if (status == TWCCPacketStatusType.ReceivedSmallDelta)
                {
                    // 1-byte signed delta.
                    int rawDelta = (sbyte)packet[offset];
                    offset += 1;
                    deltaValues.Add(rawDelta);
                }
                else if (status == TWCCPacketStatusType.ReceivedLargeDelta)
                {
                    // 2-byte signed delta.
                    if (offset + 2 > packet.Length)
                    {
                        //TODO - figure out why there is sometimes missing data in these packets
                        //throw new ApplicationException("Not enough data for a large delta.");
                        break;
                        
                    }
                    short rawDelta = (short)((packet[offset] << 8) | packet[offset + 1]);
                    offset += 2;
                    deltaValues.Add(rawDelta);
                }
                else
                {
                    // For NotReceived or Reserved, no delta value.
                    deltaValues.Add(0);
                }
                deltaIndex++;
            }

            // --- Combine statuses and delta values into PacketStatuses list ---
            ushort seq = BaseSequenceNumber;
            int deltaValueIndex = 0;
            foreach (var sym in statusSymbols)
            {
                int? deltaUs = null;
                if (sym == TWCCPacketStatusType.ReceivedSmallDelta || sym == TWCCPacketStatusType.ReceivedLargeDelta)
                {
                    if (deltaValueIndex < deltaValues.Count)
                    {
                        // Multiply the raw delta by DeltaScale to get microseconds.
                        deltaUs = deltaValues[deltaValueIndex++] * DeltaScale;
                    }
                }
                PacketStatuses.Add(new TWCCPacketStatus
                {
                    SequenceNumber = seq,
                    Status = sym,
                    Delta = deltaUs
                });
                seq++; // Note: proper sequence number wrapping might be needed in production.
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
            // Build a list of TWCCPacketStatusType from PacketStatuses.
            List<TWCCPacketStatusType> symbols = PacketStatuses.Select(ps => ps.Status).ToList();

            // Reconstruct packet status chunks.
            List<ushort> chunks = new List<ushort>();
            int i = 0;
            while (i < symbols.Count)
            {
                // Try to use run-length chunk: count how many consecutive statuses are identical.
                int runLength = 1;
                TWCCPacketStatusType current = symbols[i];
                while (i + runLength < symbols.Count && symbols[i + runLength] == current && runLength < 0x0FFF)
                {
                    runLength++;
                }
                if (runLength >= 2)
                {
                    // Build run-length chunk.
                    // Top 2 bits: 00, next 2 bits: symbol, last 12 bits: run length.
                    ushort chunk = (ushort)(((int)current & 0x3) << 12);
                    chunk |= (ushort)(runLength & 0x0FFF);
                    chunks.Add(chunk);
                    i += runLength;
                }
                else
                {
                    // Otherwise, pack into a two-bit status vector chunk.
                    // We pack up to 7 statuses.
                    int count = Math.Min(7, symbols.Count - i);
                    ushort chunk = 0;
                    // Set the top two bits to 10 (binary) to indicate a 2-bit vector.
                    chunk |= 0x8000; // 10xx xxxx xxxx xxxx
                    for (int j = 0; j < count; j++)
                    {
                        chunk |= (ushort)(((int)symbols[i + j] & 0x3) << (14 - 2 * (j + 1)));
                    }
                    chunks.Add(chunk);
                    i += count;
                }
            }

            // Build the delta values array.
            List<byte> deltaBytes = new List<byte>();
            foreach (var ps in PacketStatuses)
            {
                if (ps.Status == TWCCPacketStatusType.ReceivedSmallDelta)
                {
                    // Delta was stored already scaled; convert back to raw units.
                    sbyte delta = (sbyte)(ps.Delta.HasValue ? ps.Delta.Value / DeltaScale : 0);
                    deltaBytes.Add((byte)delta);
                }
                else if (ps.Status == TWCCPacketStatusType.ReceivedLargeDelta)
                {
                    if (!ps.Delta.HasValue)
                    {
                        ps.Delta = 0;
                        //throw new ApplicationException("Missing delta for a large delta packet.");
                    }
                    short delta = (short)(ps.Delta.Value / DeltaScale);
                    byte high = (byte)(delta >> 8);
                    byte low = (byte)(delta & 0xFF);
                    deltaBytes.Add(high);
                    deltaBytes.Add(low);
                }
                // For not received or reserved, no delta bytes are added.
            }

            // Calculate fixed part length.
            int fixedPart = RTCPHeader.HEADER_BYTES_LENGTH + 4 + 4 + 2 + 2 + 4; // header, two SSRCs, Base Seq, Status Count, RefTime+FbkCnt
            int chunksPart = chunks.Count * 2;
            int deltasPart = deltaBytes.Count;
            int totalLength = fixedPart + chunksPart + deltasPart;
            byte[] buffer = new byte[totalLength];

            // Write header (we update length later).
            Buffer.BlockCopy(Header.GetBytes(), 0, buffer, 0, RTCPHeader.HEADER_BYTES_LENGTH);
            int offset = RTCPHeader.HEADER_BYTES_LENGTH;

            // Write Sender and Media SSRC.
            WriteUInt32(buffer, ref offset, SenderSSRC);
            WriteUInt32(buffer, ref offset, MediaSSRC);

            // Write Base Sequence Number and Packet Status Count.
            WriteUInt16(buffer, ref offset, BaseSequenceNumber);
            WriteUInt16(buffer, ref offset, PacketStatusCount);

            // Build the 32-bit word for ReferenceTime and FeedbackPacketCount.
            uint refTimeAndCount = (ReferenceTime << 8) | FeedbackPacketCount;
            WriteUInt32(buffer, ref offset, refTimeAndCount);

            // Write packet status chunks.
            foreach (ushort chunk in chunks)
            {
                WriteUInt16(buffer, ref offset, chunk);
            }

            // Write delta values.
            foreach (byte b in deltaBytes)
            {
                buffer[offset++] = b;
            }

            // Update the header length (in 32-bit words minus one).
            Header.SetLength((ushort)(totalLength / 4 - 1));
            Buffer.BlockCopy(Header.GetBytes(), 0, buffer, 0, RTCPHeader.HEADER_BYTES_LENGTH);

            return buffer;
        }

        public override string ToString()
        {
            var packetStatusInfo = string.Join(", ", PacketStatuses.Select(ps =>
                $"Seq:{ps.SequenceNumber}({ps.Status}{(ps.Delta.HasValue ? $",Δ:{ps.Delta.Value}" : "")})"));

            return $"TWCC Feedback: SenderSSRC={SenderSSRC}, MediaSSRC={MediaSSRC}, BaseSeq={BaseSequenceNumber}, " +
                   $"StatusCount={PacketStatusCount}, RefTime={ReferenceTime} (1/64 sec), " +
                   $"FbkPktCount={FeedbackPacketCount}, PacketStatuses=[{packetStatusInfo}]";
        }

        #region Helper Methods

        private uint ReadUInt32(byte[] buffer, ref int offset)
        {
            uint value = BitConverter.ToUInt32(buffer, offset);
            if (BitConverter.IsLittleEndian)
            {
                value = NetConvert.DoReverseEndian(value);
            }
            offset += 4;
            return value;
        }

        private ushort ReadUInt16(byte[] buffer, ref int offset)
        {
            ushort value = BitConverter.ToUInt16(buffer, offset);
            if (BitConverter.IsLittleEndian)
            {
                value = NetConvert.DoReverseEndian(value);
            }
            offset += 2;
            return value;
        }

        private void WriteUInt32(byte[] buffer, ref int offset, uint value)
        {
            if (BitConverter.IsLittleEndian)
            {
                value = NetConvert.DoReverseEndian(value);
            }
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, buffer, offset, 4);
            offset += 4;
        }

        private void WriteUInt16(byte[] buffer, ref int offset, ushort value)
        {
            if (BitConverter.IsLittleEndian)
            {
                value = NetConvert.DoReverseEndian(value);
            }
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, buffer, offset, 2);
            offset += 2;
        }

        #endregion
    }
}
