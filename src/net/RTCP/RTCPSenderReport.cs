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
using System.Buffers.Binary;
using System.Collections.Generic;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

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
public partial class RTCPSenderReport : IByteSerializable
{
    public const int SENDER_PAYLOAD_SIZE = 20;
    public const int MIN_PACKET_SIZE = RTCPHeader.HEADER_BYTES_LENGTH + 4 + SENDER_PAYLOAD_SIZE;

    public RTCPHeader Header;
    public uint SSRC;
    public ulong NtpTimestamp;
    public uint RtpTimestamp;
    public uint PacketCount;
    public uint OctetCount;
    public List<ReceptionReportSample>? ReceptionReports;

    public RTCPSenderReport(uint ssrc, ulong ntpTimestamp, uint rtpTimestamp, uint packetCount, uint octetCount, List<ReceptionReportSample>? receptionReports)
    {
        Header = new RTCPHeader(RTCPReportTypesEnum.SR, (receptionReports is { }) ? receptionReports.Count : 0);
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
    public RTCPSenderReport(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < MIN_PACKET_SIZE)
        {
            throw new SipSorceryException("The packet did not contain the minimum number of bytes for an RTCPSenderReport packet.");
        }

        Header = new RTCPHeader(packet);
        ReceptionReports = new List<ReceptionReportSample>();

        SSRC = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(RTCPHeader.HEADER_BYTES_LENGTH, 4));
        NtpTimestamp = BinaryPrimitives.ReadUInt64BigEndian(packet.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 4, 8));
        RtpTimestamp = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 12, 4));
        PacketCount = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 16, 4));
        OctetCount = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 20, 4));

        var rrIndex = 28;
        for (var i = 0; i < Header.ReceptionReportCount; i++)
        {
            var pkt = packet.Slice(rrIndex + i * ReceptionReportSample.PAYLOAD_SIZE);
            if (pkt.Length >= ReceptionReportSample.PAYLOAD_SIZE)
            {
                var rr = new ReceptionReportSample(pkt);
                ReceptionReports.Add(rr);
            }
        }
    }

    /// <inheritdoc/>
    public int GetByteCount() => RTCPHeader.HEADER_BYTES_LENGTH + 4 + SENDER_PAYLOAD_SIZE + (ReceptionReports?.Count).GetValueOrDefault() * ReceptionReportSample.PAYLOAD_SIZE;

    /// <inheritdoc/>
    public int WriteBytes(Span<byte> buffer)
    {
        var size = GetByteCount();

        if (buffer.Length < size)
        {
            throw new ArgumentOutOfRangeException($"The buffer should have at least {size} bytes and had only {buffer.Length}.");
        }

        WriteBytesCore(buffer.Slice(0, size));

        return size;
    }

    private void WriteBytesCore(Span<byte> buffer)
    {
        Header.SetLength((ushort)(buffer.Length / 4 - 1));
        _ = Header.WriteBytes(buffer);

        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH), SSRC);
        BinaryPrimitives.WriteUInt64BigEndian(buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 4), NtpTimestamp);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 12), RtpTimestamp);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 16), PacketCount);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 20), OctetCount);

        if (ReceptionReports is { Count: > 0 } receptionReports)
        {
            buffer = buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 24);
            for (var i = 0; i < receptionReports.Count; i++)
            {
                _ = receptionReports[i].WriteBytes(buffer);
                buffer = buffer.Slice(ReceptionReportSample.PAYLOAD_SIZE);
            }
        }
    }
}
