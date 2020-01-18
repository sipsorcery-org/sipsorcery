﻿//-----------------------------------------------------------------------------
// Filename: RTCPSession.cs
//
// Description: Represents an RTCP session intended to be used in conjunction
// with an RTP session. This class needs to get notified of all RTP sends and
// receives and will take care of RTCP reporting.
//
// Notes: Design decisions:
// - No switch from Sender Report to Receiver Report if there are no sends
//   within the 2 sample period Window. For 2 party sessions the tiny 
//   bandwidth saving does not justify the complexity.
// - First report will be sent straight after the first RTP send. The initial
//   delay is inconsequential for 2 party sessions.
// - The jitter calculation uses a millisecond resolution NTP timestamp for the
//   arrival time. RFC3550 states to use a arrival clock with the same resolution
//   as the RTP stream. Given the jitter calculation is to compare the difference
//   between differences in packet arrivals the NTP timestamp may be sufficient.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 29 Dec 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Represents an RTCP session intended to be used in conjunction with an 
    /// RTP session. This class needs to get notified of all RTP sends and receives 
    /// and will take care of RTCP reporting.
    /// </summary>
    /// <remarks>
    /// RTCP Design Decisions:
    /// - Minimum Report Period set to 5s as per RFC3550: 6.2 RTCP Transmission Interval (page 24).
    /// - Delay for initial report transmission set to 2.5s (0.5 * minimum report period) as per RFC3550: 6.2 RTCP Transmission Interval (page 26).
    /// - Randomisation factor to apply to report intervals to attempt to ensure RTCP reports amongst participants don't become synchronised
    ///   [0.5 * interval, 1.5 * interval] as per RFC3550: 6.2 RTCP Transmission Interval (page 26).
    /// - Timeout period during which if no RTP or RTCP packets received a participant is assumed to have dropped
    ///   5 x minimum report period as per RFC3550: 6.2.1 (page 27) and 6.3.5 (page 31).
    /// - All RTCP composite reports must satisfy (this includes when a BYE is sent):
    ///   - First RTCP packet must be a SR or RR,
    ///   - Must contain an SDES packet.
    /// </remarks>
    public class RTCPSession
    {
        public const string NO_ACTIVITY_TIMEOUT_REASON = "No activity timeout.";
        private const int SRTP_AUTH_KEY_LENGTH = RTPSession.SRTP_AUTH_KEY_LENGTH;
        private const int RTCP_MINIMUM_REPORT_PERIOD_MILLISECONDS = 5000;
        private const float RTCP_INTERVAL_LOW_RANDOMISATION_FACTOR = 0.5F;
        private const float RTCP_INTERVAL_HIGH_RANDOMISATION_FACTOR = 1.5F;
        private const int RTP_NO_ACTIVITY_TIMEOUT_FACTOR = 5;
        private const int RTP_NO_ACTIVITY_TIMEOUT_MILLISECONDS = RTP_NO_ACTIVITY_TIMEOUT_FACTOR * RTCP_MINIMUM_REPORT_PERIOD_MILLISECONDS;

        private static ILogger logger = Log.Logger;

        private static DateTime UtcEpoch2036 = new DateTime(2036, 2, 7, 6, 28, 16, DateTimeKind.Utc);
        private static DateTime UtcEpoch1900 = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public uint Ssrc { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime StartedAt { get; private set; }
        public DateTime RTPLastActivityAt { get; private set; }

        /// <summary>
        /// Number of RTP packets sent to the remote party.
        /// </summary>
        public uint PacketsSentCount { get; private set; }

        /// <summary>
        /// Number of RTP bytes sent to the remote party.
        /// </summary>
        public uint OctetsSentCount { get; private set; }

        public uint LastRtpTimestampSent { get; private set; }

        public ulong LastNtpTimestampSent { get; private set; }

        /// <summary>
        /// Number of RTP packets received from the remote party.
        /// </summary>
        public uint PacketsReceivedCount { get; private set; }

        /// <summary>
        /// Number of RTP bytes received from the remote party.
        /// </summary>
        public uint OctetsReceivedCount { get; private set; }

        public string Cname { get; private set; }

        public ReceptionReport ReceptionReport { get; private set; }

        /// <summary>
        /// The remote control (RTCP) end point this session is sending to.
        /// </summary>
        public IPEndPoint ControlDestinationEndPoint;

        /// <summary>
        /// Time to schedule the delivery of RTCP reports.
        /// </summary>
        private Timer m_rtcpReportTimer;

        private RTPChannel m_rtpChannel;
        private bool m_isClosed = false;
        private ReceptionReport m_receptionReport;
        private bool m_isMultiplexed;               // if true indicates RTCP reports should be sent on the RTP socket.

        /// <summary>
        /// Function pointer to an SRTCP context that encrypts an RTCP packet.
        /// </summary>
        public ProtectRtpPacket RtcpProtect { get; set; }

        public RTCPSession(uint ssrc, RTPChannel rtpChannel, bool isMultiplexed)
        {
            Ssrc = ssrc;
            m_rtpChannel = rtpChannel;
            m_isMultiplexed = isMultiplexed;
            CreatedAt = DateTime.Now;
            Cname = Guid.NewGuid().ToString();
        }

        public void Start()
        {
            StartedAt = DateTime.Now;
        }

        public void Close(string reason)
        {
            if (!m_isClosed)
            {
                var report = GetRtcpReport();
                report.Bye = new RTCPBye(Ssrc, reason);
                SendRtcpReport(report);

                m_isClosed = true;
                m_rtcpReportTimer?.Dispose();
                m_rtpChannel.Close(reason);
            }
        }

        /// <summary>
        /// Event handler for an RTP packet being received by the RTP session.
        /// </summary>
        internal void RtpPacketReceived(RTPPacket rtpPacket)
        {
            RTPLastActivityAt = DateTime.Now;
            PacketsReceivedCount++;
            OctetsReceivedCount += (uint)rtpPacket.Payload.Length;

            if (m_receptionReport == null)
            {
                m_receptionReport = new ReceptionReport(rtpPacket.Header.SyncSource);
            }

            bool ready = m_receptionReport.RtpPacketReceived(rtpPacket.Header.SequenceNumber, rtpPacket.Header.Timestamp, DateTimeToNtpTimestamp32(DateTime.Now));

            if (!m_isClosed && ready == true && m_rtcpReportTimer == null)
            {
                // Send the initial RTCP sender report once the first RTP packet arrives.
                SendReportTimerCallback(null);
            }
        }

        /// <summary>
        /// Event handler for an RTP packet being sent by the RTP session.
        /// </summary>
        internal void RtpPacketSent(RTPPacket rtpPacket)
        {
            PacketsSentCount++;
            OctetsSentCount += (uint)rtpPacket.Payload.Length;
            LastRtpTimestampSent = rtpPacket.Header.Timestamp;
            LastNtpTimestampSent = DateTimeToNtpTimestamp(DateTime.Now);
        }

        /// <summary>
        /// Event handler for an RTCP packet being received from the remote party.
        /// </summary>
        /// <param name="remoteEndPoint">The end point the packet was received from.</param>
        /// <param name="buffer">The data received.</param>
        internal void ControlDataReceived(IPEndPoint remoteEndPoint, byte[] buffer)
        {
            try
            {
                var rtcpCompoundPacket = new RTCPCompoundPacket(buffer);

                if (rtcpCompoundPacket != null && rtcpCompoundPacket.SenderReport != null)
                {
                    if (m_receptionReport == null)
                    {
                        m_receptionReport = new ReceptionReport(rtcpCompoundPacket.SenderReport.SSRC);
                    }

                    m_receptionReport.RtcpSenderReportReceived(rtcpCompoundPacket.SenderReport.NtpTimestamp);

                    var sr = rtcpCompoundPacket.SenderReport;

                    logger.LogDebug($"Received RtcpSenderReport from {remoteEndPoint} pkts {sr.PacketCount} bytes {sr.OctetCount}");
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception RTCPSession.ControlDataReceived. {excp.Message}");
            }
        }

        /// <summary>
        /// Callback function for the RTCP report timer.
        /// </summary>
        /// <param name="stateInfo">Not used.</param>
        private void SendReportTimerCallback(Object stateInfo)
        {
            try
            {
                if (!m_isClosed && ControlDestinationEndPoint != null)
                {
                    if ((RTPLastActivityAt != DateTime.MinValue && DateTime.Now.Subtract(RTPLastActivityAt).TotalMilliseconds > RTP_NO_ACTIVITY_TIMEOUT_MILLISECONDS) ||
                        (RTPLastActivityAt == DateTime.MinValue && DateTime.Now.Subtract(CreatedAt).TotalMilliseconds > RTP_NO_ACTIVITY_TIMEOUT_MILLISECONDS))
                    {
                        logger.LogWarning($"RTP channel on {m_rtpChannel?.RTPLocalEndPoint} has not had any activity for over {RTP_NO_ACTIVITY_TIMEOUT_MILLISECONDS / 1000} seconds, closing.");
                        m_rtcpReportTimer?.Dispose();
                        Close(NO_ACTIVITY_TIMEOUT_REASON);
                    }
                    else
                    {
                        logger.LogDebug($"SendRtcpSenderReport {m_rtpChannel?.RTPLocalEndPoint}->{m_rtpChannel?.LastRtpDestination} pkts {PacketsReceivedCount} bytes {OctetsReceivedCount}");

                        var report = GetRtcpReport();
                        SendRtcpReport(report);
                    }

                    var interval = GetNextRtcpInterval(RTCP_MINIMUM_REPORT_PERIOD_MILLISECONDS);
                    if (m_rtcpReportTimer == null)
                    {
                        m_rtcpReportTimer = new Timer(SendReportTimerCallback, null, interval, interval);
                    }
                    else
                    {
                        m_rtcpReportTimer.Change(interval, interval);
                    }
                }
            }
            catch (ObjectDisposedException) // The RTP socket can disappear between the null check and the report send.
            {
                m_rtcpReportTimer?.Dispose();
            }
            catch (Exception excp)
            {
                // RTCP reports are not critical enough to bubble the exception up to the application.
                logger.LogError($"Exception SendReportTimerCallback. {excp.Message}");
                m_rtcpReportTimer?.Dispose();
            }
        }

        /// <summary>
        /// Gets the RTCP compound packet containing the RTCP reports we send.
        /// </summary>
        /// <returns>An RTCP compound packet.</returns>
        private RTCPCompoundPacket GetRtcpReport()
        {
            ReceptionReportSample rr = (m_receptionReport != null) ? m_receptionReport.GetSample(DateTimeToNtpTimestamp32(DateTime.Now)) : null;
            var senderReport = new RTCPSenderReport(Ssrc, LastNtpTimestampSent, LastRtpTimestampSent, PacketsSentCount, OctetsSentCount, (rr != null) ? new List<ReceptionReportSample> { rr } : null);
            var sdesReport = new RTCPSDesReport(Ssrc, Cname);
            return new RTCPCompoundPacket(senderReport, sdesReport);
        }

        /// <summary>
        /// Sends the RTCP report to the remote call party.
        /// </summary>
        /// <param name="report">RTCP report to send.</param>
        private void SendRtcpReport(RTCPCompoundPacket report)
        {
            var reportBytes = report.GetBytes();

            var sendOnSocket = (m_isMultiplexed) ? RTPChannelSocketsEnum.RTP : RTPChannelSocketsEnum.Control;

            if (RtcpProtect == null)
            {
                m_rtpChannel.SendAsync(sendOnSocket, ControlDestinationEndPoint, reportBytes);
            }
            else
            {
                byte[] sendBuffer = new byte[reportBytes.Length + SRTP_AUTH_KEY_LENGTH];
                Buffer.BlockCopy(reportBytes, 0, sendBuffer, 0, reportBytes.Length);

                int rtperr = RtcpProtect(sendBuffer, sendBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                if (rtperr != 0)
                {
                    logger.LogWarning("SRTP RTCP packet protection failed, result " + rtperr + ".");
                }
                else
                {
                    m_rtpChannel.SendAsync(sendOnSocket, m_rtpChannel.LastControlDestination, sendBuffer);
                }
            }
        }

        /// <summary>
        /// Gets a pseudo-randomised interval for the next RTCP report period.
        /// </summary>
        /// <param name="baseInterval">The base report interval to randomise.</param>
        /// <returns>A value in milliseconds to use for the next RTCP report interval.</returns>
        private int GetNextRtcpInterval(int baseInterval)
        {
            return Crypto.GetRandomInt((int)(RTCP_INTERVAL_LOW_RANDOMISATION_FACTOR * baseInterval),
                (int)(RTCP_INTERVAL_HIGH_RANDOMISATION_FACTOR * baseInterval));
        }

        public static uint DateTimeToNtpTimestamp32(DateTime value) { return (uint)((DateTimeToNtpTimestamp(value) >> 16) & 0xFFFFFFFF); }

        /// <summary>
        /// Converts specified DateTime value to long NTP time.
        /// </summary>
        /// <param name="value">DateTime value to convert. This value must be in local time.</param>
        /// <returns>Returns NTP value.</returns>
        /// <notes>
        /// Wallclock time (absolute date and time) is represented using the
        /// timestamp format of the Network Time Protocol (NPT), which is in
        /// seconds relative to 0h UTC on 1 January 1900 [4].  The full
        /// resolution NPT timestamp is a 64-bit unsigned fixed-point number with
        /// the integer part in the first 32 bits and the fractional part in the
        /// last 32 bits. In some fields where a more compact representation is
        /// appropriate, only the middle 32 bits are used; that is, the low 16
        /// bits of the integer part and the high 16 bits of the fractional part.
        /// The high 16 bits of the integer part must be determined independently.
        /// </notes>
        public static ulong DateTimeToNtpTimestamp(DateTime value)
        {
            DateTime baseDate = value >= UtcEpoch2036 ? UtcEpoch2036 : UtcEpoch1900;

            TimeSpan elapsedTime = value > baseDate ? value.ToUniversalTime() - baseDate.ToUniversalTime() : baseDate.ToUniversalTime() - value.ToUniversalTime();

            long ticks = elapsedTime.Ticks;

            return (ulong)(elapsedTime.Ticks / TimeSpan.TicksPerSecond << 32) | (ulong)(elapsedTime.Ticks % TimeSpan.TicksPerSecond);
        }
    }
}
