//-----------------------------------------------------------------------------
// Filename: RTCPStreamSynchronisationUnitTest.cs
//
// Description: Tests for the pieces a remote party needs in order to
// synchronise the streams of an RTP session against each other (issue #1751):
//   - every stream of one RTP session reports the same RTCP CNAME,
//   - a sender report's RTP timestamp refers to the same instant as its NTP
//     timestamp,
//   - the RTP clock rate is taken from the negotiated format,
//   - NTP timestamp arithmetic carries out of the fractional field.
//
// History:
// 27 Oct 2026  Claude Code - Opus 5  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPStreamSynchronisationUnitTest
    {
        private const int PCMU_CLOCK_RATE = 8000;
        private const int VP8_CLOCK_RATE = 90000;

        private readonly ILogger logger;

        public RTCPStreamSynchronisationUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static RTPPacket SentPacket(uint rtpTimestamp) => new RTPPacket(10)
        {
            Header = new RTPHeader { SyncSource = 1234, SequenceNumber = 1, Timestamp = rtpTimestamp }
        };

        /// <summary>
        /// RFC3550 6.5.1 makes the CNAME the binding between the SSRCs of one sender. Audio and
        /// video therefore have to report the same CNAME or the remote party is not entitled to
        /// synchronise them.
        /// </summary>
        [Fact]
        public void AllStreamsInOneSession_ReportTheSameCname()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().WithVideoTrack().Build())
            {
                Assert.Equal(session.Cname, session.AudioStream.RtcpSession.Cname);
                Assert.Equal(session.Cname, session.VideoStream.RtcpSession.Cname);
            }
        }

        /// <summary>
        /// The same CNAME has to reach the wire, both in the SDP ssrc attributes and in the SDES
        /// report that accompanies every RTCP report.
        /// </summary>
        [Fact]
        public void AllStreamsInOneSession_AdvertiseTheSameCname_InSdpAndSdes()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().WithVideoTrack().Build())
            {
                var offer = session.CreateOffer(System.Net.IPAddress.Loopback);

                var sdpCnames = offer.Media
                    .SelectMany(x => x.SsrcAttributes)
                    .Select(x => x.Cname)
                    .Distinct()
                    .ToList();

                logger.LogDebug("SDP cnames: {Cnames}", string.Join(", ", sdpCnames));

                Assert.Equal(2, offer.Media.Count);
                Assert.Single(sdpCnames);
                Assert.Equal(session.Cname, sdpCnames[0]);

                var audioSdes = session.AudioStream.RtcpSession.GetRtcpReport().SDesReport;
                var videoSdes = session.VideoStream.RtcpSession.GetRtcpReport().SDesReport;

                Assert.Equal(session.Cname, audioSdes.CNAME);
                Assert.Equal(session.Cname, videoSdes.CNAME);

                // The SSRCs still have to differ, it is only the CNAME that is shared.
                Assert.NotEqual(session.AudioStream.LocalTrack.Ssrc, session.VideoStream.LocalTrack.Ssrc);
            }
        }

        /// <summary>
        /// Two sessions are two senders, so they must not collide on a CNAME.
        /// </summary>
        [Fact]
        public void SeparateSessions_ReportDifferentCnames()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var first = new RtpSessionBuilder().WithAudioTrack().Build())
            using (var second = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                Assert.NotEqual(first.Cname, second.Cname);
            }
        }

        /// <summary>
        /// An RTCP session constructed without a CNAME still gets one of its own, so the
        /// pre-existing two argument constructor keeps working.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void RtcpSession_WithNoCnameSupplied_GeneratesOne(string cname)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var session = new RTCPSession(SDPMediaTypesEnum.audio, 1234, cname);

            Assert.False(string.IsNullOrWhiteSpace(session.Cname));
            Assert.NotEqual(cname, session.Cname);
        }

        /// <summary>
        /// RFC3550 6.4.1: the sender report's RTP timestamp has to correspond to the same instant
        /// as its NTP timestamp. Previously the NTP field was the report time while the RTP field
        /// was the last timestamp sent, so the pair was out by the time since the last send.
        /// </summary>
        [Fact]
        public void SenderReport_RtpTimestamp_CorrespondsToItsNtpTimestamp()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            const uint SENT_RTP_TIMESTAMP = 160_000;
            var rtcpSession = new RTCPSession(SDPMediaTypesEnum.audio, 1234) { PayloadSampleRateHz = PCMU_CLOCK_RATE };

            rtcpSession.RecordRtpPacketSend(SentPacket(SENT_RTP_TIMESTAMP));
            var sendAnchor = rtcpSession.LastNtpTimestampSent;

            // Nothing more is sent before the report timer fires.
            Thread.Sleep(1500);

            var sr = rtcpSession.GetRtcpReport().SenderReport;
            Assert.NotNull(sr);

            // Reverse the mapping the report states: what wall clock time does it claim for the
            // RTP timestamp that was actually sent?
            var reportedSecondsSinceSend = Ntp64Timestamp.GetElapsedSeconds(sendAnchor, sr.NtpTimestamp);
            var reportedRtpUnitsSinceSend = sr.RtpTimestamp - SENT_RTP_TIMESTAMP;
            var impliedSecondsSinceSend = reportedRtpUnitsSinceSend / (double)PCMU_CLOCK_RATE;

            logger.LogDebug("elapsed per the NTP field : {Ntp:0.000}s", reportedSecondsSinceSend);
            logger.LogDebug("elapsed per the RTP field : {Rtp:0.000}s ({Units} units)", impliedSecondsSinceSend, reportedRtpUnitsSinceSend);

            Assert.True(reportedSecondsSinceSend > 1.0, "the report should have been generated well after the send");

            // The two halves of the pair must agree to within the resolution of the clock.
            var disagreementMs = Math.Abs(impliedSecondsSinceSend - reportedSecondsSinceSend) * 1000;
            Assert.True(disagreementMs < 10, "NTP and RTP timestamps disagree by " + disagreementMs.ToString("0.0") + "ms");
        }

        /// <summary>
        /// The extrapolation runs at the RTP clock rate, so the same elapsed time produces
        /// proportionally more RTP units on a 90kHz video clock than on an 8kHz audio clock.
        /// </summary>
        [Theory]
        [InlineData(PCMU_CLOCK_RATE)]
        [InlineData(VP8_CLOCK_RATE)]
        public void SenderReport_RtpTimestamp_AdvancesAtTheRtpClockRate(int clockRate)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            const uint SENT_RTP_TIMESTAMP = 1_000;
            var rtcpSession = new RTCPSession(SDPMediaTypesEnum.video, 1234) { PayloadSampleRateHz = clockRate };

            rtcpSession.RecordRtpPacketSend(SentPacket(SENT_RTP_TIMESTAMP));
            var sendAnchor = rtcpSession.LastNtpTimestampSent;

            Thread.Sleep(500);

            var sr = rtcpSession.GetRtcpReport().SenderReport;
            var elapsedSeconds = Ntp64Timestamp.GetElapsedSeconds(sendAnchor, sr.NtpTimestamp);
            var expectedUnits = elapsedSeconds * clockRate;
            var actualUnits = sr.RtpTimestamp - SENT_RTP_TIMESTAMP;

            logger.LogDebug("{Rate}Hz: expected ~{Expected:0} units, got {Actual}", clockRate, expectedUnits, actualUnits);

            Assert.True(actualUnits > 0);
            // One clock tick of slack for the truncation in the conversion.
            Assert.True(Math.Abs(actualUnits - expectedUnits) <= 1.0,
                "expected ~" + expectedUnits.ToString("0") + " units at " + clockRate + "Hz but got " + actualUnits);
        }

        /// <summary>
        /// With no clock rate available there is no relationship to extrapolate with, so the report
        /// falls back to the last timestamp sent rather than guessing.
        /// </summary>
        [Fact]
        public void SenderReport_WithNoClockRate_FallsBackToLastTimestampSent()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            const uint SENT_RTP_TIMESTAMP = 160_000;
            var rtcpSession = new RTCPSession(SDPMediaTypesEnum.audio, 1234);
            Assert.Equal(0, rtcpSession.PayloadSampleRateHz);

            rtcpSession.RecordRtpPacketSend(SentPacket(SENT_RTP_TIMESTAMP));
            Thread.Sleep(100);

            var sr = rtcpSession.GetRtcpReport().SenderReport;

            Assert.Equal(SENT_RTP_TIMESTAMP, sr.RtpTimestamp);
        }

        /// <summary>
        /// The RTP timestamp is a 32 bit counter, so extrapolating past its top must wrap rather
        /// than saturate or throw.
        /// </summary>
        [Fact]
        public void SenderReport_RtpTimestamp_WrapsPastUInt32Max()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            uint sentRtpTimestamp = uint.MaxValue - 100;
            var rtcpSession = new RTCPSession(SDPMediaTypesEnum.audio, 1234) { PayloadSampleRateHz = PCMU_CLOCK_RATE };

            rtcpSession.RecordRtpPacketSend(SentPacket(sentRtpTimestamp));
            Thread.Sleep(100);

            var sr = rtcpSession.GetRtcpReport().SenderReport;

            logger.LogDebug("sent {Sent}, reported {Reported}", sentRtpTimestamp, sr.RtpTimestamp);

            // 100ms at 8kHz is 800 units, which takes it past the wrap point.
            Assert.True(sr.RtpTimestamp < sentRtpTimestamp, "the timestamp should have wrapped");
            Assert.True(unchecked(sr.RtpTimestamp - sentRtpTimestamp) > 0);
        }

        /// <summary>
        /// The clock rate has to come from the negotiation, nothing else knows it.
        /// </summary>
        [Fact]
        public void NegotiatedFormat_SetsTheRtpClockRateOnEachRtcpSession()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().WithVideoTrack().Build())
            {
                Assert.Equal(0, session.AudioStream.RtcpSession.PayloadSampleRateHz);

                var offer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Equal(PCMU_CLOCK_RATE, session.AudioStream.RtcpSession.PayloadSampleRateHz);
                Assert.Equal(VP8_CLOCK_RATE, session.VideoStream.RtcpSession.PayloadSampleRateHz);
            }
        }

        /// <summary>
        /// A clock rate the application set itself is not overwritten by the negotiation.
        /// </summary>
        [Fact]
        public void NegotiatedFormat_DoesNotOverwriteAnApplicationSuppliedClockRate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.AudioStream.RtcpSession.PayloadSampleRateHz = 12345;

                var offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Equal(12345, session.AudioStream.RtcpSession.PayloadSampleRateHz);
            }
        }

        /// <summary>
        /// RFC3551 4.5.2: G722 samples at 16kHz but its RTP timestamps advance at 8kHz. Using the
        /// codec clock rate here would make the sender report claim twice the elapsed media time.
        /// </summary>
        [Fact]
        public void RtpClockRate_ForG722_Is8000NotTheCodecClockRate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var g722 = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.G722);

            logger.LogDebug("G722 ClockRate {Clock}, RtpClockRate {Rtp}", g722.ClockRate(), g722.RtpClockRate());

            Assert.Equal(16000, g722.ClockRate());
            Assert.Equal(8000, g722.RtpClockRate());
        }

        /// <summary>
        /// The telephone-event format is not the stream's media clock, so it must not be the format
        /// the clock rate is taken from even when it sorts ahead of the codec.
        /// </summary>
        [Fact]
        public void NegotiatedFormat_IgnoresTelephoneEventWhenPickingTheClockRate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                var offer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferPcmuWithDtmf);
                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Equal(PCMU_CLOCK_RATE, session.AudioStream.RtcpSession.PayloadSampleRateHz);
            }
        }

        /// <summary>
        /// Adding a fraction that takes the 32 bit fractional field past one second has to carry
        /// into the seconds field. Dropping the carry lost a whole second.
        /// </summary>
        [Fact]
        public void Ntp64Timestamp_AddFraction_CarriesIntoTheSecondsField()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            // Second 1000 with the fractional field already at 0.8s.
            ulong anchor = ((ulong)1000 << 32) | (uint)(0.8 * uint.MaxValue);

            var result = Ntp64Timestamp.AddFraction(anchor, 0.5);

            var seconds = Ntp64Timestamp.GetSeconds(result);
            var fraction = Ntp64Timestamp.GetFraction(result) / (double)uint.MaxValue;

            logger.LogDebug("1000s + 0.800 plus 0.5 = {Seconds}s + {Fraction:0.000}", seconds, fraction);

            Assert.Equal(1001u, seconds);
            Assert.True(Math.Abs(fraction - 0.3) < 0.001, "fraction was " + fraction.ToString("0.000"));
        }

        /// <summary>
        /// The same carry through the interpolation helper, which is what the abs-capture-time
        /// header extension and any SR based mapping build on.
        /// </summary>
        [Fact]
        public void Ntp64Timestamp_InterpolateNtpTime_CarriesAcrossASecondBoundary()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            ulong anchor = ((ulong)1000 << 32) | (uint)(0.8 * uint.MaxValue);
            var pair = new TimestampPair { RtpTimestamp = 0, NtpTimestamp = anchor };

            // 4000 units at 8kHz is 0.5s, which crosses the second boundary.
            var result = Ntp64Timestamp.InterpolateNtpTime(pair, 4000, PCMU_CLOCK_RATE);

            Assert.Equal(1001u, Ntp64Timestamp.GetSeconds(result));
            Assert.True(Math.Abs(Ntp64Timestamp.GetFraction(result) / (double)uint.MaxValue - 0.3) < 0.001);
        }

        /// <summary>
        /// A fraction of a second or more carries whole seconds as well as the remainder.
        /// </summary>
        [Fact]
        public void Ntp64Timestamp_AddFraction_CarriesWholeSeconds()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            ulong anchor = (ulong)1000 << 32;

            var result = Ntp64Timestamp.AddFraction(anchor, 2.25);

            Assert.Equal(1002u, Ntp64Timestamp.GetSeconds(result));
            Assert.True(Math.Abs(Ntp64Timestamp.GetFraction(result) / (double)uint.MaxValue - 0.25) < 0.001);
        }

        [Fact]
        public void Ntp64Timestamp_AddFraction_RejectsANegativeFraction()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            Assert.Throws<ArgumentOutOfRangeException>(() => Ntp64Timestamp.AddFraction((ulong)1000 << 32, -0.5));
        }

        /// <summary>
        /// GetElapsedSeconds is what the sender report extrapolation measures with.
        /// </summary>
        [Fact]
        public void Ntp64Timestamp_GetElapsedSeconds_SpansSecondsAndFraction()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            ulong from = ((ulong)1000 << 32) | (uint)(0.75 * uint.MaxValue);
            ulong to = ((ulong)1002 << 32) | (uint)(0.25 * uint.MaxValue);

            Assert.True(Math.Abs(Ntp64Timestamp.GetElapsedSeconds(from, to) - 1.5) < 0.001);
            Assert.True(Math.Abs(Ntp64Timestamp.GetElapsedSeconds(to, from) + 1.5) < 0.001);
        }
    }
}
