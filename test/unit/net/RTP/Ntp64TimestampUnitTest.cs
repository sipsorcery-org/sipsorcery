using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using Xunit;

namespace SIPSorcery.UnitTests.net.RTP
{
    [Trait("Category", "unit")]
    public class Ntp64TimestampUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public Ntp64TimestampUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void InterpolatesNtpTimestampFromRtpTimestamp() {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var lastNtpTimestamp = 0x00000001_00000000ul;
            var lastRtpTimestamp = 1100u;
            var timestampPair = new TimestampPair() {RtpTimestamp = lastRtpTimestamp, NtpTimestamp = lastNtpTimestamp};
            int clockrate = 1000;
            var currentRtpTimestamp = 2101u;
            var diff = currentRtpTimestamp - lastRtpTimestamp;
            var timeUnits = (double)diff / clockrate;
            var interpolatedNtpTimestamp = Ntp64Timestamp.InterpolateNtpTime(timestampPair, currentRtpTimestamp, clockrate);

            var secs = Ntp64Timestamp.GetSeconds(interpolatedNtpTimestamp);
            var fraction = Ntp64Timestamp.GetFraction(interpolatedNtpTimestamp);

            Assert.Equal(2u, secs);
            Assert.Equal(Math.Round(fraction/(double)uint.MaxValue, 3), Math.Round(timeUnits - Math.Truncate(timeUnits), 3));
        }
        
    }
}
