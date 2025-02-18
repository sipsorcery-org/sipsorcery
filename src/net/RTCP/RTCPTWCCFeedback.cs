//-----------------------------------------------------------------------------
// Filename: RTCPFeedback.cs
//
// Description:
//
//        RTCP Feedback Packet
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// header |V=2|P|    RC   |   PT=SR=200   |             length            |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                  SSRC of packet sender                        |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                  SSRC of media source                         |
// info   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        :            Feedback Control Information(FCI)                  :
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
// Author(s):
// TeraBitSoftware
// 
// History:
// 29 Jun 2020  TeraBitSoftware     Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

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
        public int DeltaScale { get; set; } = 250;

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

            // Parse the 32-bit word containing Reference Time (24 bits) and Feedback Packet Count (8 bits).
            uint refTimeAndCount = ReadUInt32(packet, ref offset);
            ReferenceTime = refTimeAndCount >> 8; // top 24 bits
            FeedbackPacketCount = (byte)(refTimeAndCount & 0xFF);

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
                    int delta = (sbyte)packet[offset];
                    offset += 1;
                    deltaValues.Add(delta);
                }
                else if (status == TWCCPacketStatusType.ReceivedLargeDelta)
                {
                    // 2-byte signed delta.
                    if (offset + 2 > packet.Length)
                    {
                        throw new ApplicationException("Not enough data for a large delta.");
                    }
                    short delta = (short)((packet[offset] << 8) | packet[offset + 1]);
                    offset += 2;
                    deltaValues.Add(delta);
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
        /// This example implements the run-length chunk when possible and defaults to two-bit
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
                        throw new ApplicationException("Missing delta for a large delta packet.");
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

/*
6.  Format of RTCP Feedback Messages

   This section defines the format of the low-delay RTCP feedback
   messages.These messages are classified into three categories as
   follows:

   - Transport layer FB messages
   - Payload-specific FB messages
   - Application layer FB messages

   Transport layer FB messages are intended to transmit general purpose
   feedback information, i.e., information independent of the particular
   codec or the application in use.The information is expected to be
   generated and processed at the transport/RTP layer.  Currently, only
   a generic negative acknowledgement (NACK) message is defined.

   Payload-specific FB messages transport information that is specific
   to a certain payload type and will be generated and acted upon at the
   codec "layer".  This document defines a common header to be used in
   conjunction with all payload-specific FB messages.The definition of
   specific messages is left either to RTP payload format specifications
   or to additional feedback format documents.

   Application layer FB messages provide a means to transparently convey
   feedback from the receiver's to the sender's application.  The
   information contained in such a message is not expected to be acted
   upon at the transport/RTP or the codec layer.The data to be
   exchanged between two application instances is usually defined in the
   application protocol specification and thus can be identified by the
   application so that there is no need for additional external
   information.Hence, this document defines only a common header to be
   used along with all application layer FB messages.  From a protocol
   point of view, an application layer FB message is treated as a
   special case of a payload-specific FB message.

      Note: Proper processing of some FB messages at the media sender
      side may require the sender to know which payload type the FB
      message refers to.Most of the time, this knowledge can likely be
      derived from a media stream using only a single payload type.
      However, if several codecs are used simultaneously (e.g., with
      audio and DTMF) or when codec changes occur, the payload type
      information may need to be conveyed explicitly as part of the FB
      message.This applies to all




Ott, et al.Standards Track[Page 31]

RFC 4585                        RTP/AVPF July 2006


      payload-specific as well as application layer FB messages.  It is
      up to the specification of an FB message to define how payload
      type information is transmitted.

   This document defines two transport layer and three (video) payload-
   specific FB messages as well as a single container for application
   layer FB messages.  Additional transport layer and payload-specific
   FB messages MAY be defined in other documents and MUST be registered
   through IANA (see Section 9, "IANA Considerations").

   The general syntax and semantics for the above RTCP FB message types
   are described in the following subsections.

6.1.   Common Packet Format for Feedback Messages

   All FB messages MUST use a common packet format that is depicted in
   Figure 3:

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   |V=2|P|   FMT   |       PT      |          length               |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   |                  SSRC of packet sender                        |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   |                  SSRC of media source                         |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   :            Feedback Control Information(FCI)                  :
   :                                                               :

           Figure 3: Common Packet Format for Feedback Messages

   The fields V, P, SSRC, and length are defined in the RTP
   specification[2], the respective meaning being summarized below:

   version(V) : 2 bits
      This field identifies the RTP version.The current version is 2.

   padding(P) : 1 bit
      If set, the padding bit indicates that the packet contains
      additional padding octets at the end that are not part of the
      control information but are included in the length field.

Ott, et al.Standards Track[Page 32]

RFC 4585                        RTP/AVPF July 2006


   Feedback message type (FMT): 5 bits
      This field identifies the type of the FB message and is
      interpreted relative to the type (transport layer, payload-
      specific, or application layer feedback).  The values for each of
      the three feedback types are defined in the respective sections
      below.

   Payload type (PT): 8 bits
      This is the RTCP packet type that identifies the packet as being
      an RTCP FB message.Two values are defined by the IANA:

            Name   | Value | Brief Description
         ----------+-------+------------------------------------
            RTPFB  |  205  | Transport layer FB message
            PSFB   |  206  | Payload-specific FB message

   Length: 16 bits
      The length of this packet in 32-bit words minus one, including the
      header and any padding.  This is in line with the definition of
      the length field used in RTCP sender and receiver reports[3].

   SSRC of packet sender: 32 bits
      The synchronization source identifier for the originator of this
      packet.

   SSRC of media source: 32 bits
      The synchronization source identifier of the media source that
      this piece of feedback information is related to.

   Feedback Control Information (FCI): variable length
      The following three sections define which additional information
      MAY be included in the FB message for each type of feedback:
      transport layer, payload-specific, or application layer feedback.
      Note that further FCI contents MAY be specified in further
      documents.

   Each RTCP feedback packet MUST contain at least one FB message in the
   FCI field.Sections 6.2 and 6.3 define for each FCI type, whether or
   not multiple FB messages MAY be compressed into a single FCI field.
   If this is the case, they MUST be of the same type, i.e., same FMT.
   If multiple types of feedback messages, i.e., several FMTs, need to
   be conveyed, then several RTCP FB messages MUST be generated and
   SHOULD be concatenated in the same compound RTCP packet.

Ott, et al.                 Standards Track                    [Page 33]

RFC 4585                        RTP/AVPF July 2006


6.2.   Transport Layer Feedback Messages

   Transport layer FB messages are identified by the value RTPFB as RTCP
   message type.

   A single general purpose transport layer FB message is defined in
   this document: Generic NACK.  It is identified by means of the FMT
   parameter as follows:

   0:    unassigned
   1:    Generic NACK
   2-30: unassigned
   31:   reserved for future expansion of the identifier number space

   The following subsection defines the formats of the FCI field for
   this type of FB message.  Further generic feedback messages MAY be
   defined in the future.

6.2.1.  Generic NACK

   The Generic NACK message is identified by PT= RTPFB and FMT = 1.

   The FCI field MUST contain at least one and MAY contain more than one
   Generic NACK.

   The Generic NACK is used to indicate the loss of one or more RTP
   packets.The lost packet(s) are identified by the means of a packet
   identifier and a bit mask.

   Generic NACK feedback SHOULD NOT be used if the underlying transport
   protocol is capable of providing similar feedback information to the
   sender (as may be the case, e.g., with DCCP).

   The Feedback Control Information(FCI) field has the following Syntax
  (Figure 4) :

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   |            PID                |             BLP               |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

               Figure 4: Syntax for the Generic NACK message

   Packet ID(PID): 16 bits
     The PID field is used to specify a lost packet.The PID field

     refers to the RTP sequence number of the lost packet.


Ott, et al.                 Standards Track                    [Page 34]

RFC 4585                        RTP/AVPF July 2006



  bitmask of following lost packets (BLP): 16 bits
     The BLP allows for reporting losses of any of the 16 RTP packets

     immediately following the RTP packet indicated by the PID.The
     BLP's definition is identical to that given in [6].  Denoting the

     BLP's least significant bit as bit 1, and its most significant bit
      as bit 16, then bit i of the bit mask is set to 1 if the receiver

     has not received RTP packet number (PID+i) (modulo 2^16) and
     indicates this packet is lost; bit i is set to 0 otherwise.Note
     that the sender MUST NOT assume that a receiver has received a
     packet because its bit mask was set to 0.  For example, the least

     significant bit of the BLP would be set to 1 if the packet

     corresponding to the PID and the following packet have been lost.
     However, the sender cannot infer that packets PID+2 through PID+16

     have been received simply because bits 2 through 15 of the BLP are
      0; all the sender knows is that the receiver has not reported them
      as lost at this time.

  The length of the FB message MUST be set to 2+n, with n being the

  number of Generic NACKs contained in the FCI field.

  The Generic NACK message implicitly references the payload type
  through the sequence number(s).

6.3.  Payload-Specific Feedback Messages

  Payload-Specific FB messages are identified by the value PT= PSFB as
  RTCP message type.


  Three payload-specific FB messages are defined so far plus an
  application layer FB message.They are identified by means of the
  FMT parameter as follows:

      0:     unassigned
      1:     Picture Loss Indication (PLI)
      2:     Slice Loss Indication (SLI)
      3:     Reference Picture Selection Indication (RPSI)
      4-14:  unassigned
      15:    Application layer FB (AFB) message
      16-30: unassigned
      31:    reserved for future expansion of the sequence number space

  The following subsections define the FCI formats for the payload-

  specific FB messages, Section 6.4 defines FCI format for the
  application layer FB message.


Ott, et al.                 Standards Track                    [Page 35]

RFC 4585                        RTP/AVPF July 2006


6.3.1.  Picture Loss Indication (PLI)

  The PLI FB message is identified by PT= PSFB and FMT = 1.


  There MUST be exactly one PLI contained in the FCI field.

6.3.1.1.  Semantics

  With the Picture Loss Indication message, a decoder informs the

  encoder about the loss of an undefined amount of coded video data

  belonging to one or more pictures.  When used in conjunction with any
  video coding scheme that is based on inter-picture prediction, an
  encoder that receives a PLI becomes aware that the prediction chain

  may be broken.The sender MAY react to a PLI by transmitting an

  intra-picture to achieve resynchronization (making this message
  effectively similar to the FIR message as defined in [6]); however,
   the sender MUST consider congestion control as outlined in Section 7,
   which MAY restrict its ability to send an intra frame.

   Other RTP payload specifications such as RFC 2032 [6]
already define
  a feedback mechanism for some for certain codecs.  An application
  supporting both schemes MUST use the feedback mechanism defined in
   this specification when sending feedback.  For backward compatibility
  reasons, such an application SHOULD also be capable to receive and
  react to the feedback scheme defined in the respective RTP payload
  format, if this is required by that payload format.

6.3.1.2.  Message Format

  PLI does not require parameters.  Therefore, the length field MUST be
   2, and there MUST NOT be any Feedback Control Information.

   The semantics of this FB message is independent of the payload type.

6.3.1.3.  Timing Rules

  The timing follows the rules outlined in Section 3.  In systems that
  employ both PLI and other types of feedback, it may be advisable to
  follow the Regular RTCP RR timing rules for PLI, since PLI is not as
   delay critical as other FB types.

6.3.1.4.  Remarks

  PLI messages typically trigger the sending of full intra-pictures.
   Intra-pictures are several times larger then predicted (inter-)
   pictures.  Their size is independent of the time they are generated.
   In most environments, especially when employing bandwidth-limited
  links, the use of an intra-picture implies an allowed delay that is a



Ott, et al.                 Standards Track [Page 36]

RFC 4585                        RTP/AVPF July 2006


   significant multitude of the typical frame duration.  An example: If
  the sending frame rate is 10 fps, and an intra-picture is assumed to
  be 10 times as big as an inter-picture, then a full second of latency
  has to be accepted.  In such an environment, there is no need for a
  particular short delay in sending the FB message.  Hence, waiting for
   the next possible time slot allowed by RTCP timing rules as per [2]
with Tmin=0 does not have a negative impact on the system
  performance.

6.3.2.  Slice Loss Indication (SLI)

   The SLI FB message is identified by PT=PSFB and FMT=2.

   The FCI field MUST contain at least one and MAY contain more than one
  SLI.

6.3.2.1.  Semantics

  With the Slice Loss Indication, a decoder can inform an encoder that
  it has detected the loss or corruption of one or several consecutive
  macroblock(s) in scan order (see below).  This FB message MUST NOT be
  used for video codecs with non-uniform, dynamically changeable
  macroblock sizes such as H.263 with enabled Annex Q.  In such a case,
   an encoder cannot always identify the corrupted spatial region.

6.3.2.2.  Format

  The Slice Loss Indication uses one additional FCI field, the content
  of which is depicted in Figure 6.  The length of the FB message MUST
  be set to 2 + n, with n being the number of SLIs contained in the FCI
   field.

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
   + -+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   | First | Number | PictureID |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

            Figure 6: Syntax of the Slice Loss Indication(SLI)

   First: 13 bits
      The macroblock(MB) address of the first lost macroblock.  The MB
      numbering is done such that the macroblock in the upper left
      corner of the picture is considered macroblock number 1 and the
      number for each macroblock increases from left to right and then
      from top to bottom in raster - scan order(such that if there is a
       total of N macroblocks in a picture, the bottom right macroblock
       is considered macroblock number N).



Ott, et al.                 Standards Track[Page 37]


RFC 4585                        RTP / AVPF                       July 2006


   Number: 13 bits
      The number of lost macroblocks, in scan order as discussed above.

   PictureID: 6 bits
      The six least significant bits of the codec - specific identifier
      that is used to reference the picture in which the loss of the
      macroblock(s) has occurred.  For many video codecs, the PictureID
      is identical to the Temporal Reference.

   The applicability of this FB message is limited to a small set of
   video codecs; therefore, no explicit payload type information is
   provided.

6.3.2.3.Timing Rules

 The efficiency of algorithms using the Slice Loss Indication is
 reduced greatly when the Indication is not transmitted in a timely
   fashion.Motion compensation propagates corrupted pixels that are
 not reported as being corrupted.Therefore, the use of the algorithm
discussed in Section 3 is highly recommended.

6.3.2.4.Remarks

   The term Slice is defined and used here in the sense of MPEG-1-- a
  consecutive number of macroblocks in scan order.  More recent video
  coding standards sometimes have a different understanding of the term
   Slice.In H.263(1998), for example, a concept known as "rectangular

slice" exists.  The loss of one rectangular slice may lead to the

necessity of sending more than one SLI in order to precisely identify

the region of lost / damaged MBs.


The first field of the FCI defines the first macroblock of a picture
as 1 and not, as one could suspect, as 0.This was done to align

this specification with the comparable mechanism available in ITU - T

Rec.H.245[24].The maximum number of macroblocks in a picture
(2 * *13 or 8192) corresponds to the maximum picture sizes of most of

the ITU - T and ISO / IEC video codecs.If future video codecs offer

larger picture sizes and / or smaller macroblock sizes, then an

additional FB message has to be defined.The six least significant

bits of the Temporal Reference field are deemed to be sufficient to

indicate the picture in which the loss occurred.


The reaction to an SLI is not part of this specification.One

typical way of reacting to an SLI is to use intra refresh for the
affected spatial region.


Ott, et al.Standards Track[Page 38]


RFC 4585                        RTP / AVPF                       July 2006



Algorithms were reported that keep track of the regions affected by

motion compensation, in order to allow for a transmission of Intra

macroblocks to all those areas, regardless of the timing of the FB
(see H.263(2000) Appendix I[17] and[15]).Although the timing of

the FB is less critical when those algorithms are used than if they
   are not, it has to be observed that those algorithms correct large
   parts of the picture and, therefore, have to transmit much higher
   data volume in case of delayed FBs.

6.3.3.Reference Picture Selection Indication(RPSI)

   The RPSI FB message is identified by PT = PSFB and FMT = 3.

   There MUST be exactly one RPSI contained in the FCI field.

6.3.3.1.Semantics

   Modern video coding standards such as MPEG - 4 visual version 2[16] or
    H.263 version 2[17] allow using older reference pictures than the
   most recent one for predictive coding.  Typically, a first -in-first -
   out queue of reference pictures is maintained.If an encoder has
   learned about a loss of encoder - decoder synchronicity, a known -as-
   correct reference picture can be used.As this reference picture is
   temporally further away then usual, the resulting predictively coded
   picture will use more bits.

   Both MPEG - 4 and H.263 define a binary format for the "payload" of an
  
     RPSI message that includes information such as the temporal ID of the
  
     damaged picture and the size of the damaged region.This bit string
     is typically small (a couple of dozen bits), of variable length, and
   self - contained, i.e., contains all information that is necessary to
   perform reference picture selection.

   Both MPEG-4 and H.263 allow the use of RPSI with positive feedback
   information as well.That is, pictures(or Slices) are reported that
were decoded without error.Note that any form of positive feedback
MUST NOT be used when in a multiparty session(reporting positive

feedback about individual reference pictures at RTCP intervals is not
expected to be of much use anyway).


Ott, et al.                 Standards Track[Page 39]


RFC 4585                        RTP / AVPF                       July 2006


6.3.3.2.Format

   The FCI for the RPSI message follows the format depicted in Figure 7:

    0                   1                   2                   3
    0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
   + -+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   | PB | 0 | Payload Type | Native RPSI bit string |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
   | defined per codec...                | Padding(0) |
   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

   Figure 7: Syntax of the Reference Picture Selection Indication(RPSI)

   PB: 8 bits
      The number of unused bits required to pad the length of the RPSI
      message to a multiple of 32 bits.

   0:  1 bit
      MUST be set to zero upon transmission and ignored upon reception.

   Payload Type: 7 bits
      Indicates the RTP payload type in the context of which the native
      RPSI bit string MUST be interpreted.

   Native RPSI bit string: variable length
      The RPSI information as natively defined by the video codec.

   Padding: #PB bits
      A number of bits set to zero to fill up the contents of the RPSI
      message to the next 32 - bit boundary.The number of padding bits
      MUST be indicated by the PB field.

6.3.3.3.Timing Rules

   RPSI is even more critical to delay than algorithms using SLI.This
   is because the older the RPSI message is, the more bits the encoder
   has to spend to re-establish encoder - decoder synchronicity.See[15]
   for some information about the overhead of RPSI for certain bit
   rate / frame rate / loss rate scenarios.

   Therefore, RPSI messages should typically be sent as soon as
   possible, employing the algorithm of Section 3.


*/
