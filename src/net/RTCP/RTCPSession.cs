//-----------------------------------------------------------------------------
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
using System.Linq;
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
        private const int RTCP_MINIMUM_REPORT_PERIOD_MILLISECONDS = 5000;
        private const float RTCP_INTERVAL_LOW_RANDOMISATION_FACTOR = 0.5F;
        private const float RTCP_INTERVAL_HIGH_RANDOMISATION_FACTOR = 1.5F;
        private const int NO_ACTIVITY_TIMEOUT_FACTOR = 6;
        private const int NO_ACTIVITY_TIMEOUT_MILLISECONDS = NO_ACTIVITY_TIMEOUT_FACTOR * RTCP_MINIMUM_REPORT_PERIOD_MILLISECONDS;

        private static ILogger logger = Log.Logger;

        private static DateTime UtcEpoch2036 = new DateTime(2036, 2, 7, 6, 28, 16, DateTimeKind.Utc);
        private static DateTime UtcEpoch1900 = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The SSRC number of the RTP packets we are sending.
        /// </summary>
        public uint Ssrc { get; private set; }

        /// <summary>
        /// Timestamp that the RTCP session was created at.
        /// </summary>
        public DateTime CreatedAt { get; private set; }

        /// <summary>
        /// Timestamp that the RTCP session sender report scheduler was started at.
        /// </summary>
        public DateTime StartedAt { get; private set; }

        /// <summary>
        /// Timestamp that the last RTP or RTCP packet for was received at.
        /// </summary>
        public DateTime LastActivityAt { get; private set; }

        /// <summary>
        /// Indicates whether the session is currently in a timed out state. This
        /// occurs if no RTP or RTCP packets have been received during an expected
        /// interval.
        /// </summary>
        public bool IsTimedOut { get; private set; } = false;

        /// <summary>
        /// Number of RTP packets sent to the remote party.
        /// </summary>
        public uint PacketsSentCount { get; private set; }

        /// <summary>
        /// Number of RTP bytes sent to the remote party.
        /// </summary>
        public uint OctetsSentCount { get; private set; }

        /// <summary>
        /// The last RTP sequence number sent by us.
        /// </summary>
        public ushort LastSeqNum { get; private set; }

        /// <summary>
        /// The last RTP timestamp sent by us.
        /// </summary>
        public uint LastRtpTimestampSent { get; private set; }

        /// <summary>
        /// The last NTP timestamp corresponding to the last RTP timestamp sent by us.
        /// </summary>
        public ulong LastNtpTimestampSent { get; private set; }

        /// <summary>
        /// Number of RTP packets received from the remote party.
        /// </summary>
        public uint PacketsReceivedCount { get; private set; }

        /// <summary>
        /// Number of RTP bytes received from the remote party.
        /// </summary>
        public uint OctetsReceivedCount { get; private set; }

        /// <summary>
        /// Unique common name field for use in SDES packets.
        /// </summary>
        public string Cname { get; private set; }

        /// <summary>
        /// The reception report to keep track of the RTP statistics
        /// from packets received from the remote call party.
        /// </summary>
        public ReceptionReport ReceptionReport { get; private set; }

        /// <summary>
        /// The SSRC number of the RTP stream from the remote call party.
        /// </summary>
        public uint RemoteSsrc
        {
            get { return m_receptionReport != null ? m_receptionReport.SSRC : 0; }
        }

        /// <summary>
        /// Time to schedule the delivery of RTCP reports.
        /// </summary>
        private Timer m_rtcpReportTimer;

        private bool m_isClosed = false;
        private ReceptionReport m_receptionReport;
        private bool m_isFirstReport = true;

        /// <summary>
        /// Event handler for sending RTCP reports.
        /// </summary>
        internal event Action<RTCPCompoundPacket> OnReportReadyToSend;

        public RTCPSession(uint ssrc)
        {
            Ssrc = ssrc;
            CreatedAt = DateTime.Now;
            Cname = Guid.NewGuid().ToString();
        }

        public void Start()
        {
            StartedAt = DateTime.Now;

            // Schedule the report timer. Will most likely get beaten to it if an RTP packet
            // is received.
            var interval = GetNextRtcpInterval(RTCP_MINIMUM_REPORT_PERIOD_MILLISECONDS);
            m_rtcpReportTimer = new Timer(SendReportTimerCallback, null, interval, interval);
        }

        public void Close(string reason)
        {
            if (!m_isClosed)
            {
                m_isClosed = true;
                m_rtcpReportTimer?.Dispose();

                var byeReport = GetRtcpReport();
                byeReport.Bye = new RTCPBye(Ssrc, reason);
                OnReportReadyToSend?.Invoke(byeReport);
            }
        }

        /// <summary>
        /// Event handler for an RTP packet being received by the RTP session.
        /// Used for measuring transmission statistics.
        /// </summary>
        internal void RecordRtpPacketReceived(RTPPacket rtpPacket)
        {
            LastActivityAt = DateTime.Now;
            IsTimedOut = false;
            PacketsReceivedCount++;
            OctetsReceivedCount += (uint)rtpPacket.Payload.Length;

            if (m_receptionReport == null)
            {
                m_receptionReport = new ReceptionReport(rtpPacket.Header.SyncSource);
            }

            bool ready = m_receptionReport.RtpPacketReceived(rtpPacket.Header.SequenceNumber, rtpPacket.Header.Timestamp, DateTimeToNtpTimestamp32(DateTime.Now));

            if (!m_isClosed && ready == true && m_isFirstReport)
            {
                // Send the initial RTCP sender report once the first RTP packet arrives.
                SendReportTimerCallback(null);
            }
        }

        /// <summary>
        /// Event handler for an RTP packet being sent by the RTP session.
        /// Used for measuring transmission statistics.
        /// </summary>
        internal void RecordRtpPacketSend(RTPPacket rtpPacket)
        {
            PacketsSentCount++;
            OctetsSentCount += (uint)rtpPacket.Payload.Length;
            LastSeqNum = rtpPacket.Header.SequenceNumber;
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
                LastActivityAt = DateTime.Now;
                IsTimedOut = false;

                //if (SrtcpUnprotect != null)
                //{
                //    int res = SrtcpUnprotect(buffer, buffer.Length);

                //    if (res != 0)
                //    {
                //        logger.LogWarning($"SRTCP unprotect failed, result {res}.");
                //        return;
                //    }
                //}

                //logger.LogDebug("RTCP in: " + buffer.HexStr());

                var rtcpCompoundPacket = new RTCPCompoundPacket(buffer);

                if (rtcpCompoundPacket != null)
                {
                    if (rtcpCompoundPacket.SenderReport != null)
                    {
                        if (m_receptionReport == null)
                        {
                            m_receptionReport = new ReceptionReport(rtcpCompoundPacket.SenderReport.SSRC);
                        }

                        m_receptionReport.RtcpSenderReportReceived(rtcpCompoundPacket.SenderReport.NtpTimestamp);

                        var sr = rtcpCompoundPacket.SenderReport;

                        logger.LogDebug($"Received RTCP sender report from {remoteEndPoint} pkts {sr.PacketCount} bytes {sr.OctetCount}");
                    }

                    if (rtcpCompoundPacket.ReceiverReport != null)
                    {
                        var rr = rtcpCompoundPacket.ReceiverReport.ReceptionReports.First();
                        logger.LogDebug($"Received RTCP receiver report from {remoteEndPoint} ssrc {rr.SSRC} highest seqnum {rr.ExtendedHighestSequenceNumber}");
                    }
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
                m_isFirstReport = false;

                if (!m_isClosed)
                {
                    if ((LastActivityAt != DateTime.MinValue && DateTime.Now.Subtract(LastActivityAt).TotalMilliseconds > NO_ACTIVITY_TIMEOUT_MILLISECONDS) ||
                        (LastActivityAt == DateTime.MinValue && DateTime.Now.Subtract(CreatedAt).TotalMilliseconds > NO_ACTIVITY_TIMEOUT_MILLISECONDS))
                    {
                        if (!IsTimedOut)
                        {
                            logger.LogWarning($"RTCP session for ssrc {Ssrc} has not had any activity for over {NO_ACTIVITY_TIMEOUT_MILLISECONDS / 1000} seconds.");
                            IsTimedOut = true;
                        }
                    }

                    logger.LogDebug($"SendRtcpSenderReport ssrc {Ssrc}, last seqnum {LastSeqNum}, pkts {PacketsReceivedCount}, bytes {OctetsReceivedCount} ");

                    var report = GetRtcpReport();
                    OnReportReadyToSend?.Invoke(report);

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
