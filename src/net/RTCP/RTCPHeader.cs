//-----------------------------------------------------------------------------
// Filename: RTCPHeader.cs
//
// Description: RTCP Header as defined in RFC3550.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com
//
// History:
// 22 Feb 2007	Aaron Clauson	Created, Hobart, Australia.
//
// Notes:
//
//      RTCP Header
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |V=2|P|    RC   |   PT=SR=200   |             Length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         Payload                               |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// (V)ersion (2 bits) = 2
// (P)adding (1 bit) = Inidcates whether the packet contains additional padding octets.
// Reception Report Count (RC) (5 bits) = The number of reception report blocks contained in this packet. A
//      value of zero is valid.
// Packet Type (PT) (8 bits) = Contains the constant 200 to identify this as an RTCP SR packet.
// Length (16 bits) = The length of this RTCP packet in 32-bit words minus one, including the header and any padding.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// The different types of RTCP packets as defined in RFC3550.
    /// </summary>
    public enum RTCPReportTypesEnum : ushort
    {
        SR = 200,     // Send Report.
        RR = 201,     // Receiver Report.
        SDES = 202,   // Session Description.
        BYE = 203,    // Goodbye.
        APP = 204     // Application-defined.
    }

    public class RTCPReportTypes
    {
        public static RTCPReportTypesEnum GetRTCPReportTypeForId(ushort rtcpReportTypeId)
        {
            return (RTCPReportTypesEnum)Enum.Parse(typeof(RTCPReportTypesEnum), rtcpReportTypeId.ToString(), true);
        }
    }

    /// <summary>
    /// RTCP Header as defined in RFC3550.
    /// </summary>
    public class RTCPHeader
    {
        public const int HEADER_BYTES_LENGTH = 4;
        public const int MAX_RECEPTIONREPORT_COUNT = 32;
        public const int RTCP_VERSION = 2;

        public int Version = RTCP_VERSION;         // 2 bits.
        public int PaddingFlag = 0;                 // 1 bit.
        public int ReceptionReportCount = 0;        // 5 bits.
        public UInt16 PacketType;                   // 8 bits.
        public UInt16 Length;                       // 16 bits.

        public RTCPHeader(RTCPReportTypesEnum packetType, int reportCount)
        {
            PacketType = (ushort)packetType;
            ReceptionReportCount = reportCount;
        }

        /// <summary>
        /// Extract and load the RTCP header from an RTCP packet.
        /// </summary>
        /// <param name="packet"></param>
        public RTCPHeader(byte[] packet)
        {
            if (packet.Length < HEADER_BYTES_LENGTH)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP header packet.");
            }

            UInt16 firstWord = BitConverter.ToUInt16(packet, 0);

            if (BitConverter.IsLittleEndian)
            {
                firstWord = NetConvert.DoReverseEndian(firstWord);
                Length = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
            }
            else
            {
                Length = BitConverter.ToUInt16(packet, 2);
            }

            Version = Convert.ToInt32(firstWord >> 14);
            PaddingFlag = Convert.ToInt32((firstWord >> 13) & 0x1);
            ReceptionReportCount = Convert.ToInt32((firstWord >> 8) & 0x1f);
            PacketType = Convert.ToUInt16(firstWord & 0x00ff);
        }

        public byte[] GetHeader(int receptionReportCount, UInt16 length)
        {
            if (receptionReportCount > MAX_RECEPTIONREPORT_COUNT)
            {
                throw new ApplicationException("The Reception Report Count value cannot be larger than " + MAX_RECEPTIONREPORT_COUNT + ".");
            }

            ReceptionReportCount = receptionReportCount;
            Length = length;

            return GetBytes();
        }

        public byte[] GetBytes()
        {
            byte[] header = new byte[4];

            UInt32 firstWord = Convert.ToUInt32(Version * Math.Pow(2, 30) + PaddingFlag * Math.Pow(2, 29) + ReceptionReportCount * Math.Pow(2, 24) + PacketType * Math.Pow(2, 16) + Length);

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(firstWord)), 0, header, 0, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(firstWord), 0, header, 0, 4);
            }

            return header;
        }
    }
}
