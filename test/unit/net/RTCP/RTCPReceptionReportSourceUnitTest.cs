//-----------------------------------------------------------------------------
// Filename: RTCPReceptionReportSourceUnitTest.cs
//
// Description: Tests for which remote SSRC an RTCP session's reception report
// describes. When the remote party changes, for example when an RTP session is
// reused for a transferred call, the report has to move to the new source rather
// than keep describing the old one, without flip-flopping between interleaved
// sources.
//
// History:
// 12 Nov 2026  Claude Code - Opus 5.5  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPReceptionReportSourceUnitTest
    {
        private const uint LOCAL_SSRC = 1234;
        private const uint OLD_REMOTE_SSRC = 1111;
        private const uint NEW_REMOTE_SSRC = 2222;
        private const int PCMU_PAYLOAD_ID = 0;
        private const int PACKET_DURATION_MS = 20;
        private const uint RTP_UNITS_PER_PACKET = 160;

        private class Source
        {
            public uint Ssrc;
            public ushort Seq;
            public uint RtpTimestamp;

            public RTPPacket NextPacket()
            {
                var packet = new RTPPacket(160)
                {
                    Header = new RTPHeader { SyncSource = Ssrc, PayloadType = PCMU_PAYLOAD_ID, SequenceNumber = Seq, Timestamp = RtpTimestamp }
                };
                Seq++;
                RtpTimestamp += RTP_UNITS_PER_PACKET;
                return packet;
            }
        }

        private static RTCPSession CreateSession() =>
            new RTCPSession(SDPMediaTypesEnum.audio, LOCAL_SSRC) { PayloadSampleRateHz = 8000 };

        private static ReceptionReportSample GetReport(RTCPSession rtcpSession)
        {
            var report = rtcpSession.GetRtcpReport();
            var reports = report.SenderReport?.ReceptionReports ?? report.ReceiverReport?.ReceptionReports ?? new List<ReceptionReportSample>();
            Assert.Single(reports);
            return reports[0];
        }

        /// <summary>
        /// Feeds a number of packets from one source at a steady 20ms.
        /// </summary>
        /// <returns>The arrival time for the next packet.</returns>
        private static DateTime Feed(RTCPSession rtcpSession, Source source, int count, DateTime arrival)
        {
            for (int i = 0; i < count; i++)
            {
                rtcpSession.RecordRtpPacketReceived(source.NextPacket(), arrival);
                arrival = arrival.AddMilliseconds(PACKET_DURATION_MS);
            }

            return arrival;
        }

        /// <summary>
        /// Checks the reception report moves to a new SSRC when the old source stops and a new
        /// one starts after a pause, as happens when the RTP session is reused for a transfer.
        /// </summary>
        [Fact]
        public void SwitchesToNewSourceAfterOldSourceStops()
        {
            var rtcpSession = CreateSession();
            var oldSource = new Source { Ssrc = OLD_REMOTE_SSRC, Seq = 100, RtpTimestamp = 1000 };
            var newSource = new Source { Ssrc = NEW_REMOTE_SSRC, Seq = 50000, RtpTimestamp = 900000 };

            var arrival = Feed(rtcpSession, oldSource, 50, DateTime.Now);
            Assert.Equal(OLD_REMOTE_SSRC, GetReport(rtcpSession).SSRC);

            arrival = arrival.AddSeconds(2);
            Feed(rtcpSession, newSource, 50, arrival);

            var report = GetReport(rtcpSession);
            Assert.Equal(NEW_REMOTE_SSRC, report.SSRC);
            Assert.Equal((uint)newSource.Seq - 1, report.ExtendedHighestSequenceNumber);
        }

        /// <summary>
        /// Checks the reception report moves to a new SSRC that starts as soon as the old one
        /// stops, once the old source has been quiet for the threshold and not before.
        /// </summary>
        [Fact]
        public void SwitchesOnceOldSourceHasBeenQuietForThreshold()
        {
            var rtcpSession = CreateSession();
            var oldSource = new Source { Ssrc = OLD_REMOTE_SSRC, Seq = 100, RtpTimestamp = 1000 };
            var newSource = new Source { Ssrc = NEW_REMOTE_SSRC, Seq = 7, RtpTimestamp = 5000 };

            var arrival = Feed(rtcpSession, oldSource, 50, DateTime.Now);

            int packetsWithinThreshold = RTCPSession.RECEPTION_REPORT_SOURCE_QUIET_MILLISECONDS / PACKET_DURATION_MS - 1;
            arrival = Feed(rtcpSession, newSource, packetsWithinThreshold, arrival);
            Assert.Equal(OLD_REMOTE_SSRC, GetReport(rtcpSession).SSRC);

            Feed(rtcpSession, newSource, 5, arrival);
            Assert.Equal(NEW_REMOTE_SSRC, GetReport(rtcpSession).SSRC);
        }

        /// <summary>
        /// Checks two sources sending interleaved packets do not make the reception report
        /// switch between them, and the other source's packets do not corrupt the report.
        /// </summary>
        [Fact]
        public void InterleavedSourcesDoNotSwitchReport()
        {
            var rtcpSession = CreateSession();
            var oldSource = new Source { Ssrc = OLD_REMOTE_SSRC, Seq = 100, RtpTimestamp = 1000 };
            var otherSource = new Source { Ssrc = NEW_REMOTE_SSRC, Seq = 60000, RtpTimestamp = 900000 };

            var arrival = DateTime.Now;
            for (int i = 0; i < 500; i++)
            {
                rtcpSession.RecordRtpPacketReceived(oldSource.NextPacket(), arrival);
                rtcpSession.RecordRtpPacketReceived(otherSource.NextPacket(), arrival.AddMilliseconds(PACKET_DURATION_MS / 2));
                arrival = arrival.AddMilliseconds(PACKET_DURATION_MS);
            }

            var report = GetReport(rtcpSession);
            Assert.Equal(OLD_REMOTE_SSRC, report.SSRC);
            Assert.Equal((uint)oldSource.Seq - 1, report.ExtendedHighestSequenceNumber);
            Assert.Equal(0, report.PacketsLost);
        }

        /// <summary>
        /// Checks a single stray packet from another SSRC while the source is paused does not
        /// replace the source.
        /// </summary>
        [Fact]
        public void SingleStrayPacketDoesNotSwitchReport()
        {
            var rtcpSession = CreateSession();
            var oldSource = new Source { Ssrc = OLD_REMOTE_SSRC, Seq = 100, RtpTimestamp = 1000 };
            var straySource = new Source { Ssrc = NEW_REMOTE_SSRC, Seq = 9, RtpTimestamp = 5000 };

            var arrival = Feed(rtcpSession, oldSource, 50, DateTime.Now);
            arrival = arrival.AddSeconds(2);
            rtcpSession.RecordRtpPacketReceived(straySource.NextPacket(), arrival);
            Feed(rtcpSession, oldSource, 10, arrival.AddMilliseconds(PACKET_DURATION_MS));

            Assert.Equal(OLD_REMOTE_SSRC, GetReport(rtcpSession).SSRC);
        }

        /// <summary>
        /// Checks the report can switch back to the original SSRC if it returns after the
        /// replacement source has itself gone quiet, as when a call is transferred back.
        /// </summary>
        [Fact]
        public void SwitchesBackWhenOriginalSourceReturns()
        {
            var rtcpSession = CreateSession();
            var firstSource = new Source { Ssrc = OLD_REMOTE_SSRC, Seq = 100, RtpTimestamp = 1000 };
            var secondSource = new Source { Ssrc = NEW_REMOTE_SSRC, Seq = 50000, RtpTimestamp = 900000 };

            var arrival = Feed(rtcpSession, firstSource, 50, DateTime.Now);
            arrival = Feed(rtcpSession, secondSource, 50, arrival.AddSeconds(2));
            Assert.Equal(NEW_REMOTE_SSRC, GetReport(rtcpSession).SSRC);

            Feed(rtcpSession, firstSource, 10, arrival.AddSeconds(2));
            Assert.Equal(OLD_REMOTE_SSRC, GetReport(rtcpSession).SSRC);
        }
    }
}
