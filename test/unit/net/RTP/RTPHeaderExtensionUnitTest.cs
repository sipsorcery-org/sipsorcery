using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.net.RTP;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.UnitTests.net.RTP
{
    [Trait("Category", "unit")]
    public class RTPHeaderExtensionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTPHeaderExtensionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ExtensionShouldReturnRightUlongValue() {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var absCaptureTime = new byte[] {0xb3, 0x85, 0xb0, 0x8f, 0xc, 0x13, 0x9d, 0xe5, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0};
            var extension = new RTPHeaderExtensionData(13, absCaptureTime, RTPHeaderExtensionType.OneByte);

            var val = extension.GetUlong(0);
            Assert.NotNull(val);
            Assert.Equal(0xb385b08f0c139de5, val.Value);

            val = extension.GetUlong(8);
            Assert.NotNull(val);
            Assert.Equal(0ul, val.Value);

            val = extension.GetUlong(9);
            Assert.Null(val);
        }

        [Fact]
        public void ShouldReturnCorrectTimestamps() {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var absCaptureTime = new byte[] { 0xb3, 0x85, 0xb0, 0x8f, 0xc, 0x13, 0x9d, 0xe5, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 };
            var extension = new RTPHeaderExtensionData(13, absCaptureTime, RTPHeaderExtensionType.OneByte);
            var extensions = new Dictionary<int, RTPHeaderExtension>() {{13, new RTPHeaderExtension(13, "http://www.webrtc.org/experiments/rtp-hdrext/abs-capture-time") }};
            var ntpTimestamp = extension.GetNtpTimestamp(extensions);

            Assert.NotNull(ntpTimestamp);
            Assert.Equal(extension.GetUlong(0), ntpTimestamp.Value);
        }

        [Fact]
        public void ReturnsNullTimestamps()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var absCaptureTime = new byte[] { 0xb3, 0x85, 0xb0, 0x8f, 0xc, 0x13, 0x9d, 0xe5, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0 };
            var extension = new RTPHeaderExtensionData(13, absCaptureTime, RTPHeaderExtensionType.OneByte);
            const uint timestamp = 0x123456u;
            var extensions = new Dictionary<int, RTPHeaderExtension>();
            var timestamps = extension.GetNtpTimestamp(extensions);

            Assert.Null(timestamps);
        }
    }
}
