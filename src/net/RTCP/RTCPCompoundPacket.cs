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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Represents an RTCP compound packet consisting of 1 or more
    /// RTCP packets combined together in a single buffer. According to RFC3550 RTCP 
    /// transmissions should always have at least 2 RTCP packets (a sender/receiver report
    /// and an SDES report). This implementation does not enforce that constraint for
    /// received reports but does for sends.
    /// </summary>
    public class RTCPCompoundPacket
    {
        private static ILogger logger = Log.Logger;

        public RTCPSenderReport SenderReport { get; private set; }
        public RTCPReceiverReport ReceiverReport { get; private set; }
        public RTCPSDesReport SDesReport { get; private set; }
        public RTCPBye Bye { get; set; }
        public RTCPFeedback Feedback { get; set; }
        public RTCPTWCCFeedback TWCCFeedback { get; set; }

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
        [Obsolete("Use RTCPCompoundPacket(ReadOnlySpan<byte> packet) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public RTCPCompoundPacket(byte[] packet) : this(new ReadOnlySpan<byte>(packet))
        {
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
                            var srLength = SenderReport.GetPacketSize();
                            offset += srLength;
                            break;
                        case (byte)RTCPReportTypesEnum.RR:
                            ReceiverReport = new RTCPReceiverReport(buffer);
                            var rrLength = ReceiverReport.GetPacketSize();
                            offset += rrLength;
                            break;
                        case (byte)RTCPReportTypesEnum.SDES:
                            SDesReport = new RTCPSDesReport(buffer);
                            var sdesLength = SDesReport.GetPacketSize();
                            offset += sdesLength;
                            break;
                        case (byte)RTCPReportTypesEnum.BYE:
                            Bye = new RTCPBye(buffer);
                            var byeLength = Bye.GetPacketSize();
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
                                    var rtpfbFeedbackLength = Feedback.GetPacketSize();
                                    offset += rtpfbFeedbackLength;
                                    break;
                            }
                            break;
                        case (byte)RTCPReportTypesEnum.PSFB:
                            // TODO: Interpret Payload specific feedback reports.
                            Feedback = new RTCPFeedback(buffer);
                            var psfbFeedbackLength = Feedback.GetPacketSize();
                            offset += psfbFeedbackLength;
                            //var psfbHeader = new RTCPHeader(buffer);
                            //offset += psfbHeader.Length * 4 + 4;
                            break;
                        default:
                            offset = Int32.MaxValue;
                            logger.LogWarning("RTCPCompoundPacket did not recognise packet type ID {PacketTypeID}. {Packet}", packetTypeID, packet.HexStr());
                            break;
                    }
                }
            }
        }

        // TODO: optimize this
        public int GetPacketSize() =>
            (SenderReport?.GetPacketSize()).GetValueOrDefault() +
            (ReceiverReport?.GetPacketSize()).GetValueOrDefault() +
            SDesReport.GetPacketSize() +
            (Bye?.GetPacketSize()).GetValueOrDefault();

        /// <summary>
        /// Serialises a compound RTCP packet to a byte array ready for transmission.
        /// </summary>
        /// <returns>A byte array representing a serialised compound RTCP packet.</returns>
        public byte[] GetBytes()
        {
            if (SenderReport is null && ReceiverReport is null)
            {
                throw new InvalidOperationException("An RTCP compound packet must have either a Sender or Receiver report set.");
            }
            else if (SDesReport is null)
            {
                throw new InvalidOperationException("An RTCP compound packet must have an SDES report set.");
            }

            var size = GetPacketSize();

            var buffer = new byte[size];

            WriteBytesCore(buffer);

            return buffer;
        }

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

            var size = GetPacketSize();

            if (buffer.Length < size)
            {
                throw new ArgumentOutOfRangeException($"The buffer should have at least {size} bytes and had only {buffer.Length}.");
            }

            WriteBytesCore(buffer.Slice(0, size));

            return size;
        }

        private void WriteBytesCore(Span<byte> buffer)
        {
            if (SenderReport is not null)
            {
                var bytesWritten = SenderReport.WriteBytes(buffer);
                buffer = buffer.Slice(bytesWritten);
            }
            else
            {
                var bytesWritten = ReceiverReport.WriteBytes(buffer);
                buffer = buffer.Slice(bytesWritten);
            }

            {
                var bytesWritten = SDesReport.WriteBytes(buffer);
                buffer = buffer.Slice(bytesWritten);
            }

            if (Bye != null)
            {
                var bytesWritten = Bye.WriteBytes(buffer);
            }
        }

        public string GetDebugSummary()
        {
            StringBuilder sb = new StringBuilder();

            if (Bye != null)
            {
                sb.AppendLine("BYE");
            }

            if (SDesReport != null)
            {
                sb.AppendLine($"SDES: SSRC={SDesReport.SSRC}, CNAME={SDesReport.CNAME}");
            }

            if (SenderReport != null)
            {
                var sr = SenderReport;
                sb.AppendLine($"Sender: SSRC={sr.SSRC}, PKTS={sr.PacketCount}, BYTES={sr.OctetCount}");
                if (sr.ReceptionReports != null)
                {
                    foreach (var rr in sr.ReceptionReports)
                    {
                        sb.AppendLine($" RR: SSRC={rr.SSRC}, LOST={rr.PacketsLost}, JITTER={rr.Jitter}");
                    }
                }
            }

            if (ReceiverReport != null)
            {
                var recv = ReceiverReport;
                sb.AppendLine($"Receiver: SSRC={recv.SSRC}");
                if (recv.ReceptionReports != null)
                {
                    foreach (var rr in recv.ReceptionReports)
                    {
                        sb.AppendLine($" RR: SSRC={rr.SSRC}, LOST={rr.PacketsLost}, JITTER={rr.Jitter}");
                    }
                }
            }

            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Creates a new RTCP compound packet from a serialised buffer.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="rtcpCompoundPacket"></param>
        /// <param name="consumed"></param>
        /// <returns>The amount read from the packet</returns>
        [Obsolete("Use TryParse(ReadOnlySpan<byte>, RTCPCompoundPacket, int) instead.", false)]
        [EditorBrowsable(EditorBrowsableState.Advanced)]
        public static bool TryParse(
            byte[] packet,
            RTCPCompoundPacket rtcpCompoundPacket,
            out int consumed)
        {
            return TryParse(packet.AsSpan(), rtcpCompoundPacket, out consumed);
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
            if (rtcpCompoundPacket == null)
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
                                var length = report?.GetPacketSize() ?? int.MaxValue;
                                offset += length;
                                break;
                            }
                        case (byte)RTCPReportTypesEnum.RR:
                            {
                                var report = new RTCPReceiverReport(buffer);
                                rtcpCompoundPacket.ReceiverReport = report;
                                var length = report?.GetPacketSize() ?? int.MaxValue;
                                offset += length;
                                break;
                            }
                        case (byte)RTCPReportTypesEnum.SDES:
                            {
                                var report = new RTCPSDesReport(buffer);
                                rtcpCompoundPacket.SDesReport = report;
                                var length = report?.GetPacketSize() ?? int.MaxValue;
                                offset += length;
                                break;
                            }
                        case (byte)RTCPReportTypesEnum.BYE:
                            {
                                var report = new RTCPBye(buffer);
                                rtcpCompoundPacket.Bye = report;
                                var length = report?.GetPacketSize() ?? int.MaxValue;
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
                                            var length = feedback?.GetPacketSize() ?? int.MaxValue;
                                            offset += length;
                                            break;
                                        }
                                    case RTCPFeedbackTypesEnum.TWCC:
                                        {
                                            var feedback = new RTCPTWCCFeedback(buffer);
                                            rtcpCompoundPacket.TWCCFeedback = feedback;
                                            var length = feedback?.GetPacketSize() ?? int.MaxValue;
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
                                var length = feedback?.GetPacketSize() ?? int.MaxValue;
                                offset += length;
                                break;
                            }
                        default:
                            {
                                offset = int.MaxValue;
                                logger.LogWarning("RTCPCompoundPacket did not recognise packet type ID {PacketTypeID}. {Packet}", packetTypeID, packet.HexStr());
                                break;
                            }
                    }
                }
            }

            consumed = offset;
            return true;
        }
    }
}
