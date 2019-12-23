//-----------------------------------------------------------------------------
// Filename: RTCPReportPacket.cs
//
// Description:
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
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//  
// History:
// 23 Feb 2007	Aaron Clauson	Created, Hobart, Australia.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.Net
{
    public class RTCPReportPacket
    {
        public RTCPReportHeader Header;                               // 32 bits.
        public byte[] Report;

        public RTCPReportPacket(RTCPReportTypesEnum reportType, byte[] report)
        {
            Header = new RTCPReportHeader(reportType, Convert.ToUInt16(report.Length));
            Report = report;
        }

        public RTCPReportPacket(byte[] packet)
        {
            Header = new RTCPReportHeader(packet);
            Report = new byte[Header.Length];
            Array.Copy(packet, RTCPReportHeader.HEADER_BYTES_LENGTH, Report, 0, Report.Length);
        }

        public byte[] GetBytes()
        {
            byte[] packet = new byte[RTCPReportHeader.HEADER_BYTES_LENGTH + Report.Length];
            byte[] headerBytes = Header.GetBytes();

            Array.Copy(headerBytes, packet, headerBytes.Length);
            Array.Copy(Report, 0, packet, headerBytes.Length, Report.Length);

            return packet;
        }
    }
}
