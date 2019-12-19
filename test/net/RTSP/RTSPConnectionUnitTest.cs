//-----------------------------------------------------------------------------
// Filename: RTSPConnectionUnitTest.cs
//
// Description: Unit tests for the RTSPConnection class.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Jan 2014	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTSPConnectionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTSPConnectionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a receive buffer is correctly identified as containing a complete RTSP message when there is no Content-Length
        /// header.
        /// </summary>
        [Fact]
        public void RTSPMessageWithNoContentLengthHeaderAvailable()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTSPRequest setupRequest = new RTSPRequest(RTSPMethodsEnum.SETUP, RTSPURL.ParseRTSPURL("rtsp://localhost/sample"));
            byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(setupRequest.ToString());

            byte[] rtspMessageBuffer = null;

            RTSPConnection rtspConnection = new RTSPConnection(null, null, null);
            rtspConnection.RTSPMessageReceived += (conn, remoteEndPoint, buffer) => { rtspMessageBuffer = buffer; };
            rtspConnection.SocketBuffer = rtspRequestBuffer;

            rtspConnection.SocketReadCompleted(rtspRequestBuffer.Length);

            Assert.NotNull(rtspMessageBuffer);

            RTSPRequest req = RTSPRequest.ParseRTSPRequest(RTSPMessage.ParseRTSPMessage(rtspMessageBuffer, null, null));

            Assert.Equal(RTSPMethodsEnum.SETUP, req.Method);
        }
    }
}
