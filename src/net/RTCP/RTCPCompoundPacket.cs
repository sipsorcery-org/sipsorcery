//-----------------------------------------------------------------------------
// Filename: RTCPCompoundPacket.cs
//
// Description: Represents an RTCP compound packet consisting of 1 or more
// RTCP packets combined together in a single buffer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 30 Dec 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

/// <summary>
/// Represents an RTCP compound packet consisting of 1 or more
/// RTCP packets combined together in a single buffer. According to RFC3550 RTCP 
/// transmissions should always have at least 2 RTCP packets (a sender/receiver report
/// and an SDES report). This implementation does not enforce that constraint for
/// received reports but does for sends.
/// </summary>
public partial class RTCPCompoundPacket : IByteSerializable
{
    private static ILogger logger = Log.Logger;

    public RTCPSenderReport? SenderReport { get; private set; }
    public RTCPReceiverReport? ReceiverReport { get; private set; }
    public RTCPSDesReport? SDesReport { get; private set; }
    public RTCPBye? Bye { get; set; }
    public RTCPFeedback? Feedback { get; set; }
    public RTCPTWCCFeedback? TWCCFeedback { get; set; }

    protected internal RTCPCompoundPacket()
    {
    }

    public RTCPCompoundPacket(RTCPSenderReport senderReport, RTCPSDesReport sdesReport)
    {
        SenderReport = senderReport;
        SDesReport = sdesReport;
    }

    public RTCPCompoundPacket(RTCPReceiverReport receiverReport, RTCPSDesReport sdesReport)
    {
        ReceiverReport = receiverReport;
        SDesReport = sdesReport;
    }

    /// <summary>
    /// Creates a new RTCP compound packet from a serialised buffer.
    /// </summary>
    /// <param name="packet">The serialised RTCP compound packet to parse.</param>
    public RTCPCompoundPacket(ReadOnlySpan<byte> packet)
    {
        var offset = 0;
        while (offset < packet.Length)
        {
            if (packet.Length - offset < RTCPHeader.HEADER_BYTES_LENGTH)
            {
                // Not enough bytes left for a RTCP header.
                break;
            }
            else
            {
                var buffer = packet.Slice(offset);

                // The payload type field is the second byte in the RTCP header.
                var packetTypeID = buffer[1];
                switch (packetTypeID)
                {
                    case (byte)RTCPReportTypesEnum.SR:
                        SenderReport = new RTCPSenderReport(buffer);
                        var srLength = SenderReport.GetByteCount();
                        offset += srLength;
                        break;
                    case (byte)RTCPReportTypesEnum.RR:
                        ReceiverReport = new RTCPReceiverReport(buffer);
                        var rrLength = ReceiverReport.GetByteCount();
                        offset += rrLength;
                        break;
                    case (byte)RTCPReportTypesEnum.SDES:
                        SDesReport = new RTCPSDesReport(buffer);
                        var sdesLength = SDesReport.GetByteCount();
                        offset += sdesLength;
                        break;
                    case (byte)RTCPReportTypesEnum.BYE:
                        Bye = new RTCPBye(buffer);
                        var byeLength = Bye.GetByteCount();
                        offset += byeLength;
                        break;
                    case (byte)RTCPReportTypesEnum.RTPFB:
                        var typ = RTCPHeader.ParseFeedbackType(buffer);
                        switch (typ)
                        {
                            case RTCPFeedbackTypesEnum.TWCC:
                                TWCCFeedback = new RTCPTWCCFeedback(buffer);
                                var twccFeedbackLength = (TWCCFeedback.Header.Length + 1) * 4;
                                offset += twccFeedbackLength;
                                break;
                            default:
                                Feedback = new RTCPFeedback(buffer);
                                var rtpfbFeedbackLength = Feedback.GetByteCount();
                                offset += rtpfbFeedbackLength;
                                break;
                        }
                        break;
                    case (byte)RTCPReportTypesEnum.PSFB:
                        // TODO: Interpret Payload specific feedback reports.
                        Feedback = new RTCPFeedback(buffer);
                        var psfbFeedbackLength = Feedback.GetByteCount();
                        offset += psfbFeedbackLength;
                        //var psfbHeader = new RTCPHeader(buffer);
                        //offset += psfbHeader.Length * 4 + 4;
                        break;
                    default:
                        offset = int.MaxValue;
                        logger.LogRtcpCompoundPacketUnrecognizedType(packetTypeID, buffer);
                        break;
                }
            }
        }
    }

    // TODO: optimize this
    /// <inheritdoc/>
    public int GetByteCount()
    {
        Debug.Assert(SDesReport is { });

        return (SenderReport?.GetByteCount()).GetValueOrDefault() +
            (ReceiverReport?.GetByteCount()).GetValueOrDefault() +
            SDesReport.GetByteCount() +
            (Bye?.GetByteCount()).GetValueOrDefault();
    }

    /// <inheritdoc/>
    public int WriteBytes(Span<byte> buffer)
    {
        if (SenderReport is null && ReceiverReport is null)
        {
            throw new InvalidOperationException("An RTCP compound packet must have either a Sender or Receiver report set.");
        }
        else if (SDesReport is null)
        {
            throw new InvalidOperationException("An RTCP compound packet must have an SDES report set.");
        }

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
        if (SenderReport is { })
        {
            var bytesWritten = SenderReport.WriteBytes(buffer);
            buffer = buffer.Slice(bytesWritten);
        }
        else
        {
            Debug.Assert(ReceiverReport is { });
            var bytesWritten = ReceiverReport.WriteBytes(buffer);
            buffer = buffer.Slice(bytesWritten);
        }

        {
            Debug.Assert(SDesReport is { });
            var bytesWritten = SDesReport.WriteBytes(buffer);
            buffer = buffer.Slice(bytesWritten);
        }

        if (Bye is { })
        {
            var bytesWritten = Bye.WriteBytes(buffer);
        }
    }

    /// <summary>
    /// Creates a new RTCP compound packet from a serialised buffer.
    /// </summary>
    /// <param name="packet"></param>
    /// <param name="rtcpCompoundPacket"></param>
    /// <param name="consumed"></param>
    /// <returns>The amount read from the packet</returns>
    public static bool TryParse(
        ReadOnlySpan<byte> packet,
        RTCPCompoundPacket rtcpCompoundPacket,
        out int consumed)
    {
        if (rtcpCompoundPacket is null)
        {
            rtcpCompoundPacket = new RTCPCompoundPacket();
        }

        var offset = 0;

        while (offset < packet.Length)
        {
            if (packet.Length - offset < RTCPHeader.HEADER_BYTES_LENGTH)
            {
                break;
            }
            else
            {
                var buffer = packet.Slice(offset);
                var packetTypeID = buffer[1];

                switch (packetTypeID)
                {
                    case (byte)RTCPReportTypesEnum.SR:
                        {
                            var report = new RTCPSenderReport(buffer);
                            rtcpCompoundPacket.SenderReport = report;
                            var length = report?.GetByteCount() ?? int.MaxValue;
                            offset += length;
                            break;
                        }
                    case (byte)RTCPReportTypesEnum.RR:
                        {
                            var report = new RTCPReceiverReport(buffer);
                            rtcpCompoundPacket.ReceiverReport = report;
                            var length = report?.GetByteCount() ?? int.MaxValue;
                            offset += length;
                            break;
                        }
                    case (byte)RTCPReportTypesEnum.SDES:
                        {
                            var report = new RTCPSDesReport(buffer);
                            rtcpCompoundPacket.SDesReport = report;
                            var length = report?.GetByteCount() ?? int.MaxValue;
                            offset += length;
                            break;
                        }
                    case (byte)RTCPReportTypesEnum.BYE:
                        {
                            var report = new RTCPBye(buffer);
                            rtcpCompoundPacket.Bye = report;
                            var length = report?.GetByteCount() ?? int.MaxValue;
                            offset += length;
                            break;
                        }
                    case (byte)RTCPReportTypesEnum.RTPFB:
                        {
                            var typ = RTCPHeader.ParseFeedbackType(buffer);
                            switch (typ)
                            {
                                default:
                                    {
                                        var feedback = new RTCPFeedback(buffer);
                                        rtcpCompoundPacket.Feedback = feedback;
                                        var length = feedback?.GetByteCount() ?? int.MaxValue;
                                        offset += length;
                                        break;
                                    }
                                case RTCPFeedbackTypesEnum.TWCC:
                                    {
                                        var feedback = new RTCPTWCCFeedback(buffer);
                                        rtcpCompoundPacket.TWCCFeedback = feedback;
                                        var length = feedback?.GetByteCount() ?? int.MaxValue;
                                        offset += length;
                                        break;
                                    }
                            }
                            break;
                        }
                    case (byte)RTCPReportTypesEnum.PSFB:
                        {
                            var feedback = new RTCPFeedback(buffer);
                            rtcpCompoundPacket.Feedback = feedback;
                            var length = feedback?.GetByteCount() ?? int.MaxValue;
                            offset += length;
                            break;
                        }
                    default:
                        {
                            offset = int.MaxValue;
                            logger.LogRtcpCompoundPacketUnrecognizedType(packetTypeID, packet.ToArray());
                            break;
                        }
                }
            }
        }

        consumed = offset;
        return true;
    }
}
