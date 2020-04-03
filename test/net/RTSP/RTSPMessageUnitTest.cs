//-----------------------------------------------------------------------------
// Filename: RTSPMessageUnitTest.cs
//
// Description: Unit tests for the RTSPMessage class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Jan 2014	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTSPMessageUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTSPMessageUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private string m_CRLF = SIP.SIPConstants.CRLF;

        /// <summary>
        /// Tests that an RTSP request with headers and a body is correctly serialised and parsed.
        /// </summary>
        [Fact]
        public void RTSPRequestWIthStandardHeadersParseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int cseq = 23;
            string session = Guid.NewGuid().ToString();
            string body = "v=0" + m_CRLF +
"o=- 2890844526 2890842807 IN IP4 192.16.24.202" + m_CRLF +
"s=RTSP Session" + m_CRLF +
"m=audio 3456 RTP/AVP 0" + m_CRLF +
"a=control:rtsp://live.example.com/concert/audio" + m_CRLF +
"c=IN IP4 224.2.0.1/16";

            RTSPResponse describeResponse = new RTSPResponse(RTSPResponseStatusCodesEnum.OK, null);
            describeResponse.Header = new RTSPHeader(cseq, session);
            describeResponse.Body = body;

            byte[] buffer = Encoding.UTF8.GetBytes(describeResponse.ToString());
            RTSPMessage rtspMessage = RTSPMessage.ParseRTSPMessage(buffer, null, null);

            Assert.Equal(RTSPMessageTypesEnum.Response, rtspMessage.RTSPMessageType);
            Assert.Equal(body, rtspMessage.Body);
        }
    }
}
