//-----------------------------------------------------------------------------
// Filename: RTSPMessageUnitTest.cs
//
// Description: Unit tests for the RTSPMessage class.
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

using System;
using System.Text;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTSPMessageUnitTest
    {
        /// <summary>
        /// Tests that an RTSP request with headers and a body is correctly serialised and parsed.
        /// </summary>
        [Fact]
        public void RTSPRequestWIthStandardHeadersParseTest()
        {
            int cseq = 23;
            string session = Guid.NewGuid().ToString();
            string body =  @"v=0
o=- 2890844526 2890842807 IN IP4 192.16.24.202
s=RTSP Session
m=audio 3456 RTP/AVP 0
a=control:rtsp://live.example.com/concert/audio
c=IN IP4 224.2.0.1/16";

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
