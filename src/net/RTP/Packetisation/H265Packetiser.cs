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
using System.Collections.Generic;
using System.Linq;

namespace SIPSorcery.net.RTP.Packetisation
{
    public class H265Packetiser
    {
        public const int H265_RTP_HEADER_LENGTH = 2;

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

        public static IEnumerable<H265Nal> ParseNals(byte[] accessUnit)
        {
            int zeroes = 0;

            // Parse NALs from H265 access unit, encoded as an Annex B bitstream.
            // NALs are delimited by 0x000001 or 0x00000001.
            int currPosn = 0;
            for (int i = 0; i < accessUnit.Length; i++)
            {
                if (accessUnit[i] == 0x00)
                {
                    zeroes++;
                }
                else if (accessUnit[i] == 0x01 && zeroes >= 2)
                {
                    // This is a NAL start sequence.
                    int nalStart = i + 1;
                    if (nalStart - currPosn > 4)
                    {
                        int endPosn = nalStart - ((zeroes == 2) ? 3 : 4);
                        int nalSize = endPosn - currPosn;
                        bool isLast = currPosn + nalSize == accessUnit.Length;

                        yield return new H265Nal(accessUnit.Skip(currPosn).Take(nalSize).ToArray(), isLast);
                    }

                    currPosn = nalStart;
                    zeroes = 0;
                }
                else
                {
                    zeroes = 0;
                }
            }

            if (currPosn < accessUnit.Length)
            {
                yield return new H265Nal(accessUnit.Skip(currPosn).ToArray(), true);
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
            // get the type
            byte nalType = (byte)((nals[0] >> 1) & 0x3F);
            // turn into FU (type=49)
            nals[0] = (byte)((nals[0] & 0x81) | (49 << 1));
            // make FU header with previous naltype
            byte fuHeader = (byte)(nalType & 0x3f);
            if (isFirstPacket)
            {
                fuHeader |= 0x80;
            }
            else if (isFinalPacket)
            {
                fuHeader |= 0x40;
            }
            return new byte[] { nals[0], nals[1], fuHeader };
        }

        public static IEnumerable<H265Nal> CreateAggregated(IEnumerable<H265Nal> nals, int RTP_MAX_PAYLOAD)
        {
            var newNalList = new List<H265Nal>();
            var aggregated = new List<byte>();
            var aggregatedLast = false;
            var nalCount = 0;
            foreach (var nal in nals)
            {
                if (nal.NAL.Length + aggregated.Count <= RTP_MAX_PAYLOAD)
                {
                    if (nalCount == 0)
                    {
                        aggregated.Add((byte)((nal.NAL[0] & 0x81) | (48 << 1)));
                        aggregated.Add(nal.NAL[1]);
                    }
                    var length = nal.NAL.Length;
                    byte[] nalSize = new byte[2];
                    nalSize[0] = (byte)(length >> 8);
                    nalSize[1] = (byte)(length & 0xFF);
                    aggregated.AddRange(nalSize);
                    aggregated.AddRange(nal.NAL);
                    aggregatedLast = nal.IsLast;
                    nalCount++;
                }
                // this NAL is beyond aggregation 
                else
                {
                    // add the previously aggregated NAL
                    if (nalCount > 0)
                    {
                        newNalList.Add(new H265Nal(aggregated.ToArray(), aggregatedLast));
                        aggregated.Clear();
                        aggregatedLast = false;
                        nalCount = 0;
                    }

                    // add this NAL
                    newNalList.Add(nal);
                }
            }

            // add the previously aggregated NAL even if it is the last NAL
            if (nalCount > 0)
            {
                newNalList.Add(new H265Nal(aggregated.ToArray(), aggregatedLast));
            }

            return newNalList;
        }
    }
}
