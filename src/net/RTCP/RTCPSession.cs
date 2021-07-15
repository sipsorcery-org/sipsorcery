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
        /// The media type this report session is measuring.
        /// </summary>
        public SDPMediaTypesEnum MediaType { get; private set; }

        /// <summary>
        /// The SSRC number of the RTP packets we are sending.
        /// </summary>
        public uint Ssrc { get; internal set; }

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
        public DateTime LastActivityAt { get; private set; } = DateTime.MinValue;

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
        /// Indicates whether the RTCP session has been closed.
        /// An RTCP BYE request will typically trigger an close.
        /// </summary>
        public bool IsClosed { get; private set; } = false;

        /// <summary>
        /// Time to schedule the delivery of RTCP reports.
        /// </summary>
        private Timer m_rtcpReportTimer;

        private ReceptionReport m_receptionReport;
        private uint m_previousPacketsSentCount = 0;    // Used to track whether we have sent any packets since the last report was sent.

        /// <summary>
        /// Event handler for sending RTCP reports.
        /// </summary>
        public event Action<SDPMediaTypesEnum, RTCPCompoundPacket> OnReportReadyToSend;

        /// <summary>
        /// Fires when the connection is classified as timed out due to not
        /// receiving any RTP or RTCP packets within the given period.
        /// </summary>
        public event Action<SDPMediaTypesEnum> OnTimeout;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="mediaType">The media type this reporting session will be measuring.</param>
        /// <param name="ssrc">The SSRC of the RTP stream being sent.</param>
        public RTCPSession(SDPMediaTypesEnum mediaType, uint ssrc)
        {
            MediaType = mediaType;
            Ssrc = ssrc;
            CreatedAt = DateTime.Now;
            Cname = Guid.NewGuid().ToString();
        }

        public void Start()
        {
            StartedAt = DateTime.Now;

            // Schedule an immediate sender report.
            var interval = GetNextRtcpInterval(RTCP_MINIMUM_REPORT_PERIOD_MILLISECONDS);
            m_rtcpReportTimer = new Timer(SendReportTimerCallback, null, interval, Timeout.Infinite);
        }

        public void Close(string reason)
        {
            if (!IsClosed)
            {
                IsClosed = true;
                m_rtcpReportTimer?.Dispose();

                var byeReport = GetRtcpReport();
                byeReport.Bye = new RTCPBye(Ssrc, reason);
                OnReportReadyToSend?.Invoke(MediaType, byeReport);
            }
        }

        /// <summary>
        /// Event handler for an RTP packet being received by the RTP session.
        /// Used for measuring transmission statistics.
        /// </summary>
        public void RecordRtpPacketReceived(RTPPacket rtpPacket)
        {
            LastActivityAt = DateTime.Now;
            IsTimedOut = false;
            PacketsReceivedCount++;
            OctetsReceivedCount += (uint)rtpPacket.Payload.Length;

            if (m_receptionReport == null)
            {
                m_receptionReport = new ReceptionReport(rtpPacket.Header.SyncSource);
            }

            m_receptionReport.RtpPacketReceived(rtpPacket.Header.SequenceNumber, rtpPacket.Header.Timestamp, DateTimeToNtpTimestamp32(DateTime.Now));
        }

        /// <summary>
        /// Removes the reception report when the remote party indicates no more RTP packets
        /// for that SSRC will be received by sending an RTCP BYE.
        /// </summary>
        /// <param name="ssrc">The SSRC of the reception report being closed. Typically this
        /// should be the SSRC received in the RTCP BYE.</param>
        public void RemoveReceptionReport(uint ssrc)
        {
            if (m_receptionReport != null && m_receptionReport.SSRC == ssrc)
            {
                logger.LogDebug($"RTCP session removing reception report for remote ssrc {ssrc}.");
                m_receptionReport = null;
            }
        }

        /// <summary>
        /// Event handler for an RTP packet being sent by the RTP session.
        /// Used for measuring transmission statistics.
        /// </summary>
        public void RecordRtpPacketSend(RTPPacket rtpPacket)
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
        public void ReportReceived(IPEndPoint remoteEndPoint, RTCPCompoundPacket rtcpCompoundPacket)
        {
            try
            {
                LastActivityAt = DateTime.Now;
                IsTimedOut = false;

                if (rtcpCompoundPacket != null)
                {
                    if (rtcpCompoundPacket.SenderReport != null && m_receptionReport != null)
                    {
                        m_receptionReport.RtcpSenderReportReceived(DateTimeToNtpTimestamp(DateTime.Now));
                    }

                    // TODO: Apply information from report.
                    //if (rtcpCompoundPacket.SenderReport != null)
                    //{
                    //    if (m_receptionReport == null)
                    //    {
                    //        m_receptionReport = new ReceptionReport(rtcpCompoundPacket.SenderReport.SSRC);
                    //    }

                    //    m_receptionReport.RtcpSenderReportReceived(rtcpCompoundPacket.SenderReport.NtpTimestamp);

                    //    var sr = rtcpCompoundPacket.SenderReport;
                    //}

                    //if (rtcpCompoundPacket.ReceiverReport != null)
                    //{
                    //    var rr = rtcpCompoundPacket.ReceiverReport.ReceptionReports.First();
                    //}
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception RTCPSession.ReportReceived. {excp.Message}");
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
                if (!IsClosed)
                {
                    lock (m_rtcpReportTimer)
                    {
                        if ((LastActivityAt != DateTime.MinValue && DateTime.Now.Subtract(LastActivityAt).TotalMilliseconds > NO_ACTIVITY_TIMEOUT_MILLISECONDS) ||
                            (LastActivityAt == DateTime.MinValue && DateTime.Now.Subtract(CreatedAt).TotalMilliseconds > NO_ACTIVITY_TIMEOUT_MILLISECONDS))
                        {
                            if (!IsTimedOut)
                            {
                                logger.LogWarning($"RTCP session for local ssrc {Ssrc} has not had any activity for over {NO_ACTIVITY_TIMEOUT_MILLISECONDS / 1000} seconds.");
                                IsTimedOut = true;

                                OnTimeout?.Invoke(MediaType);
                            }
                        }

                        //logger.LogDebug($"SendRtcpSenderReport ssrc {Ssrc}, last seqnum {LastSeqNum}, pkts {PacketsSentCount}, bytes {OctetsSentCount} ");

                        var report = GetRtcpReport();

                        OnReportReadyToSend?.Invoke(MediaType, report);

                        m_previousPacketsSentCount = PacketsSentCount;

                        var interval = GetNextRtcpInterval(RTCP_MINIMUM_REPORT_PERIOD_MILLISECONDS);
                        if (m_rtcpReportTimer == null)
                        {
                            m_rtcpReportTimer = new Timer(SendReportTimerCallback, null, interval, Timeout.Infinite);
                        }
                        else
                        {
                            m_rtcpReportTimer?.Change(interval, Timeout.Infinite);
                        }
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
            var sdesReport = new RTCPSDesReport(Ssrc, Cname);

            if (PacketsSentCount > m_previousPacketsSentCount)
            {
                // If we have sent a packet since the last report then we send an RTCP Sender Report.
                var senderReport = new RTCPSenderReport(Ssrc, LastNtpTimestampSent, LastRtpTimestampSent, PacketsSentCount, OctetsSentCount, (rr != null) ? new List<ReceptionReportSample> { rr } : null);
                return new RTCPCompoundPacket(senderReport, sdesReport);
            }
            else
            {
                // If we have NOT sent a packet since the last report then we send an RTCP Receiver Report.
                if (rr != null)
                {
                    var receiverReport = new RTCPReceiverReport(Ssrc, new List<ReceptionReportSample> { rr });
                    return new RTCPCompoundPacket(receiverReport, sdesReport);
                }
                else
                {
                    var receiverReport = new RTCPReceiverReport(Ssrc, null);
                    return new RTCPCompoundPacket(receiverReport, sdesReport);
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
