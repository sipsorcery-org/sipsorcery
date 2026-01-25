//-----------------------------------------------------------------------------
// Filename: RTCPReceiverReport.cs
//
// Description:
//
//        RTCP Receiver Report Packet
//  0                   1                   2                   3
//         0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// header |V=2|P|    RC   |   PT=RR=201   |             length            |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//        |                     SSRC of packet sender                     |
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
// report |                 SSRC_2(SSRC of second source)                 |
// block  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  2     :                               ...                             :
//        +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//        |                  profile-specific extensions                  |
//        +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//
//  An empty RR packet (RC = 0) MUST be put at the head of a compound
//  RTCP packet when there is no data transmission or reception to
//  report.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public partial class RTCPReceiverReport : IByteSerializable
{
    public const int MIN_PACKET_SIZE = RTCPHeader.HEADER_BYTES_LENGTH + 4;

    public RTCPHeader Header;
    public uint SSRC;
    public List<ReceptionReportSample>? ReceptionReports;

    /// <summary>
    /// Creates a new RTCP Reception Report payload.
    /// </summary>
    /// <param name="ssrc">The synchronisation source of the RTP packet being sent. Can be zero
    /// if there are none being sent.</param>
    /// <param name="receptionReports">A list of the reception reports to include. Can be empty.</param>
    public RTCPReceiverReport(uint ssrc, List<ReceptionReportSample>? receptionReports)
    {
        Header = new RTCPHeader(RTCPReportTypesEnum.RR, receptionReports is { } ? receptionReports.Count : 0);
        SSRC = ssrc;
        ReceptionReports = receptionReports;
    }

    /// <summary>
    /// Create a new RTCP Receiver Report from a serialised byte array.
    /// </summary>
    /// <param name="packet">The byte array holding the serialised receiver report.</param>
    public RTCPReceiverReport(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < MIN_PACKET_SIZE)
        {
            throw new ArgumentException("The packet did not contain the minimum number of bytes for an RTCPReceiverReport packet.", nameof(packet));
        }

        Header = new RTCPHeader(packet);
        ReceptionReports = new List<ReceptionReportSample>();

        SSRC = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4, 4));

        var rrIndex = 8;
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
    public int GetByteCount() => RTCPHeader.HEADER_BYTES_LENGTH + 4 + (ReceptionReports?.Count).GetValueOrDefault() * ReceptionReportSample.PAYLOAD_SIZE;

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

        if (ReceptionReports is { Count: > 0 } receptionReports)
        {
            buffer = buffer.Slice(RTCPHeader.HEADER_BYTES_LENGTH + 4);
            for (var i = 0; i < receptionReports.Count; i++)
            {
                _ = receptionReports[i].WriteBytes(buffer);
                buffer = buffer.Slice(ReceptionReportSample.PAYLOAD_SIZE);
            }
        }
    }
}
