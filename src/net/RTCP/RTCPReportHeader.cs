//-----------------------------------------------------------------------------
// Filename: RTCPReportHeader.cs
//
// Description: Header for custom RTCP reports.
//
//      Custom RTCP Report Packet
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |      Report Type              |             Length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         Report                                |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// Header contains two fields each a 16 bit unisgned integer:
// - Report Type: Type of report the report data contains,
//      - 0 RTCP
//      - 1 Network Test Descriptor.
// - Length: Length of the data in the report.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 22 Feb 2007	Aaron Clauson	Created, Hobart, Australia.
// 28 Dec 2019  Aaron Clauson   Added missing RTCP report types.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public enum RTCPReportTypesEnum
    {
        SR = 0,     // Send Report.
        RR = 1,     // Receiver Report.
        SDES = 2,   // Session Description.
        BYE = 3,    // Goodbye.
    }

    public class RTCPReportTypes
    {
        public static RTCPReportTypesEnum GetRTCPReportTypeForId(ushort rtcpReportTypeId)
        {
            return (RTCPReportTypesEnum)Enum.Parse(typeof(RTCPReportTypesEnum), rtcpReportTypeId.ToString(), true);
        }
    }

    public class RTCPReportHeader
    {
        public const int HEADER_BYTES_LENGTH = 4;

        public RTCPReportTypesEnum ReportType;      // 16 bits.
        public UInt16 Length;                       // 16 bits.

        public RTCPReportHeader(RTCPReportTypesEnum reportType, ushort payloadLength)
        {
            ReportType = reportType;
            Length = payloadLength;
        }

        /// <summary>
        /// Extract and load the RTCPReportHeader from packet.
        /// </summary>
        /// <param name="packet">The raw RTCP packet to parse.</param>
        public RTCPReportHeader(byte[] packet)
        {
            if (packet.Length < HEADER_BYTES_LENGTH)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP Report Header packet.");
            }

            if (BitConverter.IsLittleEndian)
            {
                ReportType = RTCPReportTypes.GetRTCPReportTypeForId(NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 0)));
                Length = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
            }
            else
            {
                ReportType = RTCPReportTypes.GetRTCPReportTypeForId(BitConverter.ToUInt16(packet, 0));
                Length = BitConverter.ToUInt16(packet, 2);
            }
        }

        public byte[] GetBytes()
        {
            byte[] rtcpReportHeader = new byte[HEADER_BYTES_LENGTH];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((ushort)ReportType)), 0, rtcpReportHeader, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(Length)), 0, rtcpReportHeader, 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)ReportType), 0, rtcpReportHeader, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Length), 0, rtcpReportHeader, 2, 2);
            }

            return rtcpReportHeader;
        }
    }
}
