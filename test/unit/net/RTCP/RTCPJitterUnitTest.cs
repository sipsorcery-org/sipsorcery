//-----------------------------------------------------------------------------
// Filename: RTCPJitterUnitTest.cs
//
// Description: Tests for the interarrival jitter an RTCP session measures for
// received RTP packets. RFC3550 6.4.1 measures it in the units of the received
// packets' RTP timestamps, so the arrival times have to be measured at the RTP
// clock rate of the codec actually being received, which need not be the first
// negotiated codec or the codec being sent.
//
// History:
// 06 Nov 2026  Claude Code - Opus 5.5  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPJitterUnitTest
    {
        private const int OPUS_PAYLOAD_ID = 111;
        private const int G722_PAYLOAD_ID = 9;
        private const int PCMU_PAYLOAD_ID = 0;
        private const int OPUS_RTP_CLOCK_RATE = 48000;
        private const int G722_RTP_CLOCK_RATE = 8000;
        private const int PCMU_RTP_CLOCK_RATE = 8000;
        private const int PACKET_DURATION_MS = 20;

        /// <summary>
        /// One millisecond at an 8kHz RTP clock. Truncating the arrival time to whole RTP units
        /// can leave a unit of noise in the transit times, so a perfect stream is allowed this.
        /// </summary>
        private const uint NEAR_ZERO_JITTER = 8;

        private const string AudioAnswerOpusAndG722 =
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 111 9 101
a=rtpmap:111 opus/48000/2
a=fmtp:111 useinbandfec=1
a=rtpmap:9 G722/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-15
a=sendrecv";

        private readonly ILogger logger;

        public RTCPJitterUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static RTPSession CreateOpusAndG722Session()
        {
            var track = new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(AudioCommonlyUsedFormats.OpusWebRTC),
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.G722),
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 101, "telephone-event/8000", "0-15")
                },
                MediaStreamStatusEnum.SendRecv);

            var session = new RtpSessionBuilder().WithTrack(track).Build();

            // The local party offers and the remote party answers with the same formats, so the
            // offerer's order, Opus first, sets the stream's sending clock.
            session.CreateOffer(IPAddress.Loopback);
            var answer = SDP.ParseSDPDescription(AudioAnswerOpusAndG722);
            Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.answer, answer));

            return session;
        }

        private static RTPPacket ReceivedPacket(int payloadType, ushort seq, uint rtpTimestamp) => new RTPPacket(10)
        {
            Header = new RTPHeader { SyncSource = 5678, PayloadType = payloadType, SequenceNumber = seq, Timestamp = rtpTimestamp }
        };

        private static uint GetJitter(RTCPSession rtcpSession)
        {
            var report = rtcpSession.GetRtcpReport();
            var reports = report.SenderReport?.ReceptionReports ?? report.ReceiverReport?.ReceptionReports ?? new List<ReceptionReportSample>();
            Assert.Single(reports);
            return reports[0].Jitter;
        }

        /// <summary>
        /// Feeds packets of one payload type at a steady 20ms into the RTCP session.
        /// </summary>
        /// <returns>The sequence number and RTP timestamp to continue from.</returns>
        private static (ushort seq, uint rtpTimestamp, DateTime arrival) FeedSteadyPackets(RTCPSession rtcpSession, int payloadType, int rtpClockRate,
            int count, ushort seq, uint rtpTimestamp, DateTime arrival)
        {
            uint rtpUnitsPerPacket = (uint)(rtpClockRate * PACKET_DURATION_MS / 1000);

            for (int i = 0; i < count; i++)
            {
                rtcpSession.RecordRtpPacketReceived(ReceivedPacket(payloadType, seq++, rtpTimestamp), arrival);
                rtpTimestamp += rtpUnitsPerPacket;
                arrival = arrival.AddMilliseconds(PACKET_DURATION_MS);
            }

            return (seq, rtpTimestamp, arrival);
        }

        /// <summary>
        /// The negotiation offers Opus first and G722 second, so the stream's sending clock is
        /// Opus' 48kHz. The remote party is free to send G722, whose RTP clock is 8kHz. A steady
        /// G722 stream must measure as no jitter. Measuring its arrival times at 48kHz reported a
        /// steady 800 units, 100ms at G722's clock, on a perfect loopback call.
        /// </summary>
        [Fact]
        public void OpusAndG722Negotiated_SteadyG722Received_JitterStaysNearZero()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = CreateOpusAndG722Session())
            {
                var rtcpSession = session.AudioStream.RtcpSession;
                logger.LogDebug("Negotiated {Formats}.", string.Join(", ", session.AudioStream.LocalTrack.Capabilities.Select(f => f.ID + " " + f.Rtpmap)));
                Assert.Equal(OPUS_RTP_CLOCK_RATE, rtcpSession.PayloadSampleRateHz);

                FeedSteadyPackets(rtcpSession, G722_PAYLOAD_ID, G722_RTP_CLOCK_RATE, 200, 1000, 123456, DateTime.Now.AddSeconds(1));

                var jitter = GetJitter(rtcpSession);
                logger.LogDebug("G722 jitter {Jitter}.", jitter);

                Assert.True(jitter <= NEAR_ZERO_JITTER, $"Jitter for a steady G722 stream was {jitter} RTP units.");

                // The sending clock is left as negotiated.
                Assert.Equal(OPUS_RTP_CLOCK_RATE, rtcpSession.PayloadSampleRateHz);
            }
        }

        /// <summary>
        /// The remote party switching codec mid call changes the units of both the RTP and the
        /// arrival timestamps. The switch must not show up as a jitter spike.
        /// </summary>
        [Fact]
        public void OpusAndG722Negotiated_RemoteSwitchesCodec_NoJitterSpike()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = CreateOpusAndG722Session())
            {
                var rtcpSession = session.AudioStream.RtcpSession;

                var next = FeedSteadyPackets(rtcpSession, OPUS_PAYLOAD_ID, OPUS_RTP_CLOCK_RATE, 100, 1000, 3000000, DateTime.Now.AddSeconds(1));
                var opusJitter = GetJitter(rtcpSession);

                // A new codec means a new RTP timestamp base in practice, which is what makes
                // comparing transit times across the switch meaningless.
                FeedSteadyPackets(rtcpSession, G722_PAYLOAD_ID, G722_RTP_CLOCK_RATE, 5, next.seq, 77, next.arrival);
                var switchJitter = GetJitter(rtcpSession);

                logger.LogDebug("Opus jitter {OpusJitter}, jitter after switching to G722 {SwitchJitter}.", opusJitter, switchJitter);

                Assert.True(opusJitter <= NEAR_ZERO_JITTER, $"Jitter for a steady Opus stream was {opusJitter} RTP units.");
                Assert.True(switchJitter <= NEAR_ZERO_JITTER, $"Jitter after switching to G722 was {switchJitter} RTP units.");
            }
        }

        /// <summary>
        /// Telephone events are not the stream's media clock. They are measured on the clock of
        /// the media they are interleaved with rather than resetting the jitter estimate.
        /// </summary>
        [Fact]
        public void TelephoneEventPayload_IsMeasuredOnTheMediaClock()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var rtcpSession = new RTCPSession(SDPMediaTypesEnum.audio, 1234) { PayloadSampleRateHz = OPUS_RTP_CLOCK_RATE };
            rtcpSession.SetReceivePayloadClockRates(new Dictionary<int, int> { { G722_PAYLOAD_ID, G722_RTP_CLOCK_RATE } });

            var next = FeedSteadyPackets(rtcpSession, G722_PAYLOAD_ID, G722_RTP_CLOCK_RATE, 50, 1000, 5000, DateTime.Now.AddSeconds(1));
            next = FeedSteadyPackets(rtcpSession, 101, G722_RTP_CLOCK_RATE, 50, next.seq, next.rtpTimestamp, next.arrival);

            var jitter = GetJitter(rtcpSession);
            logger.LogDebug("Jitter {Jitter}.", jitter);

            Assert.True(jitter <= NEAR_ZERO_JITTER, $"Jitter with interleaved telephone events was {jitter} RTP units.");
        }

        /// <summary>
        /// With a single codec the jitter must still converge on the mean deviation of the
        /// transit time. Packets alternately arriving on time and 5ms late give a transit
        /// difference of 5ms, 40 units at 8kHz, on every packet.
        /// </summary>
        [Fact]
        public void SingleCodec_JitterConvergesOnTheTransitDeviation()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                var offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK, session.SetRemoteDescription(SdpType.offer, offer));

                var rtcpSession = session.AudioStream.RtcpSession;
                Assert.Equal(PCMU_RTP_CLOCK_RATE, rtcpSession.PayloadSampleRateHz);

                var start = DateTime.Now.AddSeconds(1);
                uint rtpTimestamp = 1000;
                ushort seq = 0;

                for (int i = 0; i < 400; i++)
                {
                    var arrival = start.AddMilliseconds((double)i * PACKET_DURATION_MS + (i % 2 == 1 ? 5 : 0));
                    rtcpSession.RecordRtpPacketReceived(ReceivedPacket(PCMU_PAYLOAD_ID, seq++, rtpTimestamp), arrival);
                    rtpTimestamp += PCMU_RTP_CLOCK_RATE * PACKET_DURATION_MS / 1000;
                }

                var jitter = GetJitter(rtcpSession);
                logger.LogDebug("PCMU jitter {Jitter}.", jitter);

                Assert.InRange(jitter, 38u, 42u);
            }
        }
    }
}
