//-----------------------------------------------------------------------------
// Filename: RTCPSenderReport.cs
//
// Description:
//
//        RTCP Sender Report Packet
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// header |V=2|P|    RC   |   PT=SR=200   |             length            |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                         SSRC of sender                        |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// sender |              NTP timestamp, most significant word             |
// info   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |             NTP timestamp, least significant word             |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                         RTP timestamp                         |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                     sender's packet count                     |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                      sender's octet count                     |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
// report |                 SSRC_1(SSRC of first source)                  |
// block  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  1     | fraction lost |       cumulative number of packets lost       |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |           extended highest sequence number received           |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                      interarrival jitter                      |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                         last SR(LSR)                          |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                   delay since last SR(DLSR)                   |
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Aug 2019  Aaron Clauson   Created, Montreux, Switzerland.
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
    /// <summary>
    /// An RTCP sender report is for use by active RTP senders. 
    /// </summary>
    /// <remarks>
    /// From https://tools.ietf.org/html/rfc3550#section-6.4:
    /// "The only difference between the
    /// sender report(SR) and receiver report(RR) forms, besides the packet
    /// type code, is that the sender report includes a 20-byte sender
    /// information section for use by active senders.The SR is issued if a
    /// site has sent any data packets during the interval since issuing the
    /// last report or the previous one, otherwise the RR is issued."
    /// </remarks>
    public class RTCPSenderReport
    {
        public const int SENDER_PAYLOAD_SIZE = 20;
        public const int MIN_PACKET_SIZE = RTCPHeader.HEADER_BYTES_LENGTH + 4 + SENDER_PAYLOAD_SIZE;

        public RTCPHeader Header;
        public uint SSRC;
        public ulong NtpTimestamp;
        public uint RtpTimestamp;
        public uint PacketCount;
        public uint OctetCount;
        public List<ReceptionReportSample> ReceptionReports;

        public RTCPSenderReport(uint ssrc, ulong ntpTimestamp, uint rtpTimestamp, uint packetCount, uint octetCount, List<ReceptionReportSample> receptionReports)
        {
            Header = new RTCPHeader(RTCPReportTypesEnum.SR, (receptionReports != null) ? receptionReports.Count : 0);
            SSRC = ssrc;
            NtpTimestamp = ntpTimestamp;
            RtpTimestamp = rtpTimestamp;
            PacketCount = packetCount;
            OctetCount = octetCount;
            ReceptionReports = receptionReports;
        }

        /// <summary>
        /// Create a new RTCP Sender Report from a serialised byte array.
        /// </summary>
        /// <param name="packet">The byte array holding the serialised sender report.</param>
        public RTCPSenderReport(byte[] packet)
        {
            if (packet.Length < MIN_PACKET_SIZE)
            {
                throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCPSenderReport packet.");
            }

            Header = new RTCPHeader(packet);
            ReceptionReports = new List<ReceptionReportSample>();

            if (BitConverter.IsLittleEndian)
            {
                SSRC = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 4));
                NtpTimestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt64(packet, 8));
                RtpTimestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 16));
                PacketCount = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 20));
                OctetCount = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 24));
            }
            else
            {
                SSRC = BitConverter.ToUInt32(packet, 4);
                NtpTimestamp = BitConverter.ToUInt64(packet, 8);
                RtpTimestamp = BitConverter.ToUInt32(packet, 16);
                PacketCount = BitConverter.ToUInt32(packet, 20);
                OctetCount = BitConverter.ToUInt32(packet, 24);
            }

            int rrIndex = 28;
            for (int i = 0; i < Header.ReceptionReportCount; i++)
            {
                var rr = new ReceptionReportSample(packet.Skip(rrIndex + i * ReceptionReportSample.PAYLOAD_SIZE).ToArray());
                ReceptionReports.Add(rr);
            }
        }

        public byte[] GetBytes()
        {
            int rrCount = (ReceptionReports != null) ? ReceptionReports.Count : 0;
            byte[] buffer = new byte[RTCPHeader.HEADER_BYTES_LENGTH + 4 + SENDER_PAYLOAD_SIZE + rrCount * ReceptionReportSample.PAYLOAD_SIZE];
            Header.SetLength((ushort)(buffer.Length / 4 - 1));

            Buffer.BlockCopy(Header.GetBytes(), 0, buffer, 0, RTCPHeader.HEADER_BYTES_LENGTH);
            int payloadIndex = RTCPHeader.HEADER_BYTES_LENGTH;

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SSRC)), 0, buffer, payloadIndex, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(NtpTimestamp)), 0, buffer, payloadIndex + 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(RtpTimestamp)), 0, buffer, payloadIndex + 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(PacketCount)), 0, buffer, payloadIndex + 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(OctetCount)), 0, buffer, payloadIndex + 20, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SSRC), 0, buffer, payloadIndex, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NtpTimestamp), 0, buffer, payloadIndex + 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(RtpTimestamp), 0, buffer, payloadIndex + 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(PacketCount), 0, buffer, payloadIndex + 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(OctetCount), 0, buffer, payloadIndex + 20, 4);
            }

            int bufferIndex = payloadIndex + 24;
            for (int i = 0; i < rrCount; i++)
            {
                var receptionReportBytes = ReceptionReports[i].GetBytes();
                Buffer.BlockCopy(receptionReportBytes, 0, buffer, bufferIndex, ReceptionReportSample.PAYLOAD_SIZE);
                bufferIndex += ReceptionReportSample.PAYLOAD_SIZE;
            }

            return buffer;
        }
    }
}
