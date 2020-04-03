//-----------------------------------------------------------------------------
// Filename: RTCPByeUnitTest.cs
//
// Description: Unit tests for the RTCPBye class.

// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPByeUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPByeUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a RTCP Bye payload can be correctly serialised and 
        /// deserialised.
        /// </summary>
        [Fact]
        public void RoundtripRTCPByeUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 23;

            RTCPBye bye = new RTCPBye(ssrc, null);
            byte[] buffer = bye.GetBytes();

            RTCPBye parsedBye = new RTCPBye(buffer);

            Assert.Equal(ssrc, parsedBye.SSRC);
            Assert.Null(parsedBye.Reason);
        }

        /// <summary>
        /// Tests that a RTCP Bye payload can be correctly serialised and 
        /// deserialised when the reason value does not line up with a 4 byte boundary.
        /// </summary>
        [Fact]
        public void RoundtripByeWithReasonUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 19;
            string reason = "x";

            RTCPBye bye = new RTCPBye(ssrc, reason);
            byte[] buffer = bye.GetBytes();

            RTCPBye parsedBye = new RTCPBye(buffer);

            Assert.Equal(12, buffer.Length);
            Assert.Equal(ssrc, parsedBye.SSRC);
            Assert.Equal(reason, parsedBye.Reason);
        }

        /// <summary>
        /// Tests that a RTCP Bye payload can be correctly serialised and 
        /// deserialised when the reason lines up with a 4 byte boundary.
        /// </summary>
        [Fact]
        public void RoundtripRTCPByeOnBoundaryUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 123121231;
            string reason = "1234567";

            RTCPBye bye = new RTCPBye(ssrc, reason);
            byte[] buffer = bye.GetBytes();

            RTCPBye parsedBye = new RTCPBye(buffer);

            Assert.Equal(16, buffer.Length);
            Assert.Equal(ssrc, parsedBye.SSRC);
            Assert.Equal(reason, parsedBye.Reason);
        }

        /// <summary>
        /// Tests that a RTCP Bye payload can be correctly serialised and 
        /// deserialised when the timeout reason is used.
        /// </summary>
        [Fact]
        public void RoundtripByeWithTimeoutReasonUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint ssrc = 19;
            string reason = RTCPSession.NO_ACTIVITY_TIMEOUT_REASON;

            RTCPBye bye = new RTCPBye(ssrc, reason);
            byte[] buffer = bye.GetBytes();

            RTCPBye parsedBye = new RTCPBye(buffer);

            Assert.Equal(32, buffer.Length);
            Assert.Equal(ssrc, parsedBye.SSRC);
            Assert.Equal(reason, parsedBye.Reason);
        }
    }
}
