//-----------------------------------------------------------------------------
// Filename: H265Packetiser.cs
//
// Description: The H265Packetiser class provides methods to packetize H265 Network Abstraction Layer (NAL) units
// for transmission over RTP (Real-time Transport Protocol). It includes functionalities to parse
// H265 NAL units from an Annex B bitstream, construct RTP headers for H265 NAL units, and create
// aggregated NAL units for efficient RTP payloads. The class supports single NAL unit packets,
// fragmentation units as well as aggregation packets.
//
// Author(s):
// Morten Palner Drescher (mdr@milestone.dk)
//
// History:
// 04 Mar 2025    Morten Palner Drescher	Created, Copenhagen, Denmark.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using SIPSorcery.Net;

namespace SIPSorcery.net.RTP.Packetisation
{
    public class H265Packetiser
    {
        public const int H265_RTP_HEADER_LENGTH = 2;

        /// <summary>
        /// The length of the fragmentation unit header written by <see cref="GetH265RtpHeader"/>.
        /// </summary>
        internal const int H265_FU_HEADER_LENGTH = 3;

        /// <summary>
        /// The length of the H265 NAL unit header. A shorter NAL cannot be sent, so the parser drops it.
        /// </summary>
        internal const int H265_NAL_HEADER_LENGTH = 2;

        /// <summary>
        /// The length of the size field in front of each NAL in an aggregation packet (RFC 7798 section 4.4.2).
        /// </summary>
        private const int H265_AP_NAL_SIZE_LENGTH = 2;

        /// <summary>
        /// The payload header type of an aggregation packet.
        /// </summary>
        private const int H265_AP_TYPE = 48;

        public struct H265Nal
        {
            public byte[] NAL { get; }
            public bool IsLast { get; }

            public H265Nal(byte[] nal, bool isLast)
            {
                NAL = nal;
                IsLast = isLast;
            }
        }

        /// <summary>
        /// Splits an Annex B access unit into its NAL units, without their start codes.
        /// </summary>
        /// <remarks>
        /// Zero bytes in front of a start code (trailing_zero_8bits) and at the end of the access unit are padding and
        /// are not included in any NAL. A NAL shorter than the two byte NAL header cannot be sent and is dropped, and the
        /// last NAL returned is always flagged as last, even when the access unit ends with a start code or with such a
        /// NAL.
        /// </remarks>
        public static IEnumerable<H265Nal> ParseNals(byte[] accessUnit)
        {
            if (accessUnit == null)
            {
                throw new ArgumentNullException(nameof(accessUnit));
            }

            // Parse NALs from H265 access unit, encoded as an Annex B bitstream.
            // NALs are delimited by 0x000001 or 0x00000001.
            var scanner = new AnnexBNalScanner(H265_NAL_HEADER_LENGTH);

            while (scanner.TryGetNext(accessUnit, out int offset, out int length, out bool isLast))
            {
                yield return new H265Nal(accessUnit.AsSpan(offset, length).ToArray(), isLast);
            }
        }

        /// <summary>
        /// Constructs the RTP header for an H265 NAL. This method does NOT support
        /// aggregation packets where multiple NALs are sent as a single RTP payload.
        ///</summary>
        ///<remarks>
        ///HEVC maintains the NAL unit concept of H.264 with modifications.
        ///HEVC uses a two-byte NAL unit header, as shown in Figure 1.  The
        ///payload of a NAL unit refers to the NAL unit excluding the NAL unit
        ///header.
        ///
        ///+---------------+---------------+
        ///|0|1|2|3|4|5|6|7|0|1|2|3|4|5|6|7|
        ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///|F|   Type    |  LayerId  | TID |
        ///+-------------+-----------------+
        ///
        ///Figure 1: The Structure of the HEVC NAL Unit Header
        ///
        ///F: 1 bit
        ///forbidden_zero_bit.  Required to be zero in [HEVC].  
        ///In the context of this memo,
        ///the value 1 may be used to indicate a syntax violation
        ///
        ///Type: 6 bits
        ///nal_unit_type.  This field specifies the NAL unit type as defined
        ///in Table 7-1 of [HEVC].  If the most significant bit of this field
        ///of a NAL unit is equal to 0 (i.e., the value of this field is less
        ///than 32), the NAL unit is a VCL NAL unit.  Otherwise, the NAL unit
        ///is a non-VCL NAL unit. 
        ///
        ///LayerId: 6 bits
        ///nuh_layer_id.  Required to be equal to zero in [HEVC].
        ///
        ///TID: 3 bits
        ///nuh_temporal_id_plus1.  This field specifies the temporal
        ///identifier of the NAL unit plus 1.  The value of TemporalId is
        ///equal to TID minus 1.  A TID value of 0 is illegal to ensure that
        ///there is at least one bit in the NAL unit header equal to 1, so to
        ///enable independent considerations of start code emulations in the
        ///NAL unit header and in the NAL unit payload data.
        ///
        ///
        ///
        ///4.2.Payload Header Usage
        ///
        ///The first two bytes of the payload of an RTP packet are referred to
        ///as the payload header.The payload header consists of the same
        ///fields(F, Type, LayerId, and TID) as the NAL unit header as shown in
        ///Section 1.1.4, irrespective of the type of the payload structure.
        ///
        ///
        ///
        ///4.4.Payload Structures
        ///
        ///o  Single NAL unit packet : Contains a single NAL unit in the payload,
        ///and the NAL unit header of the NAL unit also serves as the payload
        ///header.
        ///o  Aggregation Packet(AP) : Contains more than one NAL unit within
        ///one access unit.
        ///o  Fragmentation Unit(FU) : Contains a subset of a single NAL unit.
        ///o  PACI carrying RTP packet : Contains a payload header(that differs
        ///from other payload headers for efficiency), a Payload Header
        ///Extension Structure(PHES), and a PACI payload.
        ///
        ///
        ///
        ///4.4.1.  Single NAL Unit Packets
        ///
        ///A single NAL unit packet contains exactly one NAL unit, and consists
        ///of a payload header (denoted as PayloadHdr), a conditional 16-bit
        ///DONL field (in network byte order), and the NAL unit payload data
        ///(the NAL unit excluding its NAL unit header) of the contained NAL
        ///unit, as shown in Figure 3.
        ///
        /// 0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///|           PayloadHdr          |      DONL (conditional)       |
        ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///|                                                               |
        ///|                  NAL unit payload data                        |
        ///|                                                               |
        ///|                               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///|                               :...OPTIONAL RTP padding        |
        ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///
        ///Figure 3: The Structure of a Single NAL Unit Packet
        ///
        ///
        ///
        /// 4.4.2.  Aggregation Packets (APs)
        ///
        /// 0                   1                   2                   3
       /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
       ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
       ///|                          RTP Header                           |
       ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
       ///|   PayloadHdr(Type= 48)        |         NALU 1 Size           |
       ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
       ///|          NALU 1 HDR           |                               |
       ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+         NALU 1 Data           |
       ///|                   . . .                                       |
       ///|                                                               |
       ///+               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
       ///|  . . .        | NALU 2 Size                   | NALU 2 HDR    |
       ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
       ///| NALU 2 HDR    |                                               |
       ///+-+-+-+-+-+-+-+-+              NALU 2 Data                      |
       ///|                   . . .                                       |
       ///|                               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
       ///|                               :...OPTIONAL RTP padding        |
       ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///
        ///
        ///
        ///4.4.3.  Fragmentation Units
        ///
        ///Fragmentation Units (FUs) are introduced to enable fragmenting a
        ///single NAL unit into multiple RTP packets.  
        ///
        /// 0                   1                   2                   3
        /// 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
        ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///|    PayloadHdr (Type=49)       |   FU header   | DONL (cond)   |
        ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-|
        ///| DONL (cond)   |                                               |
        ///|-+-+-+-+-+-+-+-+                                               |
        ///|                         FU payload                            |
        ///|                                                               |
        ///|                               +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///|                               :...OPTIONAL RTP padding        |
        ///+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
        ///
        ///        Figure 9: The Structure of an FU
        ///
        ///The fields in the payload header are set as follows.  The Type field
        ///MUST be equal to 49.  The fields F, LayerId, and TID MUST be equal to
        ///the fields F, LayerId, and TID, respectively, of the fragmented NAL
        ///unit.
        ///
        ///The FU header consists of an S bit, an E bit, and a 6-bit FuType
        ///field, as shown in Figure 10.
        ///
        ///+---------------+
        ///|0|1|2|3|4|5|6|7|
        ///+-+-+-+-+-+-+-+-+
        ///|S|E|  FuType   |
        ///+---------------+
        ///
        ///Figure 10: The Structure of FU Header
        ///
        ///The semantics of the FU header fields are as follows:
        ///
        ///S: 1 bit
        ///When set to 1, the S bit indicates the start of a fragmented NAL
        ///unit, i.e., the first byte of the FU payload is also the first
        ///byte of the payload of the fragmented NAL unit.  When the FU
        ///payload is not the start of the fragmented NAL unit payload, the S
        ///bit MUST be set to 0.
        ///
        ///E: 1 bit
        ///When set to 1, the E bit indicates the end of a fragmented NAL
        ///unit, i.e., the last byte of the payload is also the last byte of
        ///the fragmented NAL unit.  When the FU payload is not the last
        ///fragment of a fragmented NAL unit, the E bit MUST be set to 0.
        ///
        ///FuType: 6 bits
        ///The field FuType MUST be equal to the field Type of the fragmented
        ///NAL unit.
        /// </remarks>
        public static byte[] GetH265RtpHeader(byte[] nals, bool isFirstPacket, bool isFinalPacket)
        {
            var header = new byte[H265_FU_HEADER_LENGTH];
            WriteH265RtpHeader(nals[0], nals[1], isFirstPacket, isFinalPacket, header);
            return header;
        }

        /// <summary>
        /// Writes the fragmentation unit payload header and FU header (RFC 7798 section 4.4.3) for one fragment of the
        /// NAL whose two byte header is <paramref name="nal0"/>, <paramref name="nal1"/>, into
        /// <paramref name="destination"/> instead of a new array.
        /// </summary>
        /// <returns>The number of bytes written, <see cref="H265_FU_HEADER_LENGTH"/>.</returns>
        internal static int WriteH265RtpHeader(byte nal0, byte nal1, bool isFirstPacket, bool isFinalPacket, Span<byte> destination)
        {
            // The NAL type, carried in every fragment's FU header.
            byte nalType = (byte)((nal0 >> 1) & 0x3F);

            byte fuHeader = nalType;
            if (isFirstPacket)
            {
                fuHeader |= 0x80;
            }
            else if (isFinalPacket)
            {
                fuHeader |= 0x40;
            }

            // The payload header is the NAL header with its type changed to FU (49), keeping the F bit, layer id and TID.
            destination[0] = (byte)((nal0 & 0x81) | (49 << 1));
            destination[1] = nal1;
            destination[2] = fuHeader;
            return H265_FU_HEADER_LENGTH;
        }

        /// <summary>
        /// Puts consecutive NALs that are small enough to share a packet into aggregation packets (RFC 7798 section
        /// 4.4.2), and returns the other NALs unchanged, to be sent on their own or fragmented.
        /// </summary>
        /// <remarks>
        /// An aggregation packet holds at least two NALs, and with its payload header and the size field in front of
        /// each NAL it is no longer than <paramref name="RTP_MAX_PAYLOAD"/>. Its payload header is built from the
        /// headers of all the NALs it holds, as section 4.4.2 requires, and it is flagged as last when its last NAL is.
        /// </remarks>
        public static IEnumerable<H265Nal> CreateAggregated(IEnumerable<H265Nal> nals, int RTP_MAX_PAYLOAD)
        {
            var newNalList = new List<H265Nal>();
            var group = new List<H265Nal>();
            int groupLength = 0;

            foreach (var nal in nals)
            {
                if (group.Count > 0 && FitsInAggregation(groupLength, nal.NAL.Length, RTP_MAX_PAYLOAD))
                {
                    group.Add(nal);
                    groupLength = AggregatedLength(groupLength, nal.NAL.Length);
                    continue;
                }

                // This NAL does not fit with the ones before it, so they are complete.
                AddGroup(newNalList, group, groupLength);
                group.Clear();
                groupLength = 0;

                if (FitsInAggregation(0, nal.NAL.Length, RTP_MAX_PAYLOAD))
                {
                    group.Add(nal);
                    groupLength = AggregatedLength(0, nal.NAL.Length);
                }
                else
                {
                    newNalList.Add(nal);
                }
            }

            AddGroup(newNalList, group, groupLength);

            return newNalList;
        }

        /// <summary>
        /// Adds a group of consecutive small NALs to <paramref name="output"/>: a single NAL unchanged, and two or more
        /// as one aggregation packet.
        /// </summary>
        private static void AddGroup(List<H265Nal> output, List<H265Nal> group, int groupLength)
        {
            if (group.Count == 1)
            {
                output.Add(group[0]);
            }
            else if (group.Count > 1)
            {
                var aggregation = new byte[groupLength];
                int length = 0;

                foreach (var nal in group)
                {
                    length = AppendToAggregation(aggregation, length, nal.NAL);
                }

                WriteAggregationHeader(aggregation);
                output.Add(new H265Nal(aggregation, group[group.Count - 1].IsLast));
            }
        }

        /// <summary>
        /// The length of an aggregation packet of <paramref name="aggregationLength"/> bytes, or 0 for an empty one,
        /// once a NAL of <paramref name="nalLength"/> bytes is added: the payload header, then each NAL with its size
        /// field.
        /// </summary>
        internal static int AggregatedLength(int aggregationLength, int nalLength)
        {
            return (aggregationLength == 0 ? H265_RTP_HEADER_LENGTH : aggregationLength) + H265_AP_NAL_SIZE_LENGTH + nalLength;
        }

        /// <summary>
        /// Whether a NAL of <paramref name="nalLength"/> bytes can be added to an aggregation packet of
        /// <paramref name="aggregationLength"/> bytes, or 0 for an empty one, without it growing past
        /// <paramref name="maxPayload"/>. A NAL without a full NAL header is never aggregated, since the payload header
        /// is built from the NAL headers.
        /// </summary>
        internal static bool FitsInAggregation(int aggregationLength, int nalLength, int maxPayload)
        {
            return nalLength >= H265_NAL_HEADER_LENGTH && AggregatedLength(aggregationLength, nalLength) <= maxPayload;
        }

        /// <summary>
        /// Writes <paramref name="nal"/>, preceded by its size field, at the end of the aggregation packet being built
        /// in <paramref name="destination"/>, leaving room for the payload header in front of the first NAL.
        /// </summary>
        /// <returns>The new length of the aggregation packet.</returns>
        internal static int AppendToAggregation(Span<byte> destination, int aggregationLength, ReadOnlySpan<byte> nal)
        {
            int position = (aggregationLength == 0) ? H265_RTP_HEADER_LENGTH : aggregationLength;
            destination[position] = (byte)(nal.Length >> 8);
            destination[position + 1] = (byte)nal.Length;
            nal.CopyTo(destination.Slice(position + H265_AP_NAL_SIZE_LENGTH));
            return position + H265_AP_NAL_SIZE_LENGTH + nal.Length;
        }

        /// <summary>
        /// Writes the payload header of a complete aggregation packet from the headers of the NALs it holds (RFC 7798
        /// section 4.4.2): the F bit is set if any NAL has it set, the type is 48, and LayerId and TID are the lowest
        /// of any NAL.
        /// </summary>
        /// <param name="aggregation">The aggregation packet, exactly as long as the NALs written into it.</param>
        internal static void WriteAggregationHeader(Span<byte> aggregation)
        {
            int forbiddenBit = 0;
            int layerId = int.MaxValue;
            int tid = int.MaxValue;

            for (int position = H265_RTP_HEADER_LENGTH; position < aggregation.Length;)
            {
                int nalLength = (aggregation[position] << 8) | aggregation[position + 1];
                byte nal0 = aggregation[position + H265_AP_NAL_SIZE_LENGTH];
                byte nal1 = aggregation[position + H265_AP_NAL_SIZE_LENGTH + 1];

                forbiddenBit |= nal0 & 0x80;
                layerId = Math.Min(layerId, ((nal0 & 0x01) << 5) | (nal1 >> 3));
                tid = Math.Min(tid, nal1 & 0x07);

                position += H265_AP_NAL_SIZE_LENGTH + nalLength;
            }

            aggregation[0] = (byte)(forbiddenBit | (H265_AP_TYPE << 1) | (layerId >> 5));
            aggregation[1] = (byte)(((layerId & 0x1F) << 3) | tid);
        }
    }
}
