//-----------------------------------------------------------------------------
// Filename: ReceptionReportUnitTest.cs
//
// Description: Unit tests for the ReceptionReport class.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Dec 2020  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class ReceptionReportUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public ReceptionReportUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Checks that the lost packet count and sequence number are correctly sampled for
        /// a single RTP packet received.
        /// </summary>
        [Fact]
        public void CheckSinglePacketReportUnitTest()
        {
            uint ssrc = 1234;

            ReceptionReport rep = new ReceptionReport(ssrc);

            var sample0 = rep.GetSample(0);

            Assert.NotNull(sample0);
            Assert.Equal(0, sample0.PacketsLost);
            Assert.Equal(0U, sample0.ExtendedHighestSequenceNumber);
        }

        /// <summary>
        /// Checks that the lost packet count and sequence number are correctly sampled for
        /// three in order RTP packets being received.
        /// </summary>
        [Fact]
        public void CheckThreePacketsReportUnitTest()
        {
            uint ssrc = 1234;
            ushort seq = 0;

            ReceptionReport rep = new ReceptionReport(ssrc);

            rep.RtpPacketReceived(seq++, 0, 0);
            rep.RtpPacketReceived(seq++, 0, 0);
            rep.RtpPacketReceived(seq++, 0, 0);

            var sample3 = rep.GetSample(0);

            Assert.NotNull(sample3);
            Assert.Equal(0, sample3.PacketsLost);
            Assert.Equal(2U, sample3.ExtendedHighestSequenceNumber);
        }

        /// <summary>
        /// The first packet's relative transit time includes the arbitrary offset between the
        /// sender's and receiver's clocks, there is no previous packet to compare it with, so it
        /// must not add to the jitter.
        /// </summary>
        [Fact]
        public void FirstPacket_DoesNotAddJitter()
        {
            var rep = new ReceptionReport(1234);

            rep.RtpPacketReceived(0, 1000, 987654321);

            Assert.Equal(0U, rep.GetSample(0).Jitter);
        }

        /// <summary>
        /// A transit time that differs from the previous packet's by the same amount on every
        /// packet converges the jitter estimate on that amount, RFC3550 A.8.
        /// </summary>
        [Fact]
        public void ConstantTransitDeviation_JitterConvergesOnTheDeviation()
        {
            const uint deviation = 80;
            var rep = new ReceptionReport(1234);
            uint rtpTimestamp = 5000;
            uint arrival = 70000;

            for (ushort seq = 0; seq < 400; seq++)
            {
                rep.RtpPacketReceived(seq, rtpTimestamp, arrival + (seq % 2 == 1 ? deviation : 0));
                rtpTimestamp += 160;
                arrival += 160;
            }

            var jitter = rep.GetSample(0).Jitter;
            logger.LogDebug("Jitter {Jitter}.", jitter);

            Assert.InRange(jitter, deviation - 2, deviation + 2);
        }

        /// <summary>
        /// After the transit time is reset, for example because the RTP clock rate changed, the
        /// next packet's transit time is not compared with the previous one's and the jitter
        /// already estimated is kept.
        /// </summary>
        [Fact]
        public void ResetJitterTransit_NextPacketDoesNotAddJitter()
        {
            var rep = new ReceptionReport(1234);

            rep.RtpPacketReceived(0, 0, 1000);
            rep.RtpPacketReceived(1, 160, 1160 + 320);
            var jitterBefore = rep.GetSample(0).Jitter;
            Assert.True(jitterBefore > 0);

            rep.ResetJitterTransit();
            rep.RtpPacketReceived(2, 999999, 5);

            Assert.Equal(jitterBefore, rep.GetSample(0).Jitter);
        }
    }
}
