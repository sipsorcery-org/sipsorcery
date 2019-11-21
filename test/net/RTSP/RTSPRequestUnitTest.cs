//-----------------------------------------------------------------------------
// Filename: RTSPRequestUnitTest.cs
//
// Description: Unit tests for the RTSPRequest class.
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
    public class RTSPRequestUnitTest
    {
        /// <summary>
        /// Tests that an RTSP request with standard headers can be correctly serialised and parsed.
        /// </summary>
        [Fact]
        public void RTSPRequestWIthStandardHeadersParseTest()
        {
            int cseq = 23;
            string session = Guid.NewGuid().ToString();

            RTSPRequest setupRequest = new RTSPRequest(RTSPMethodsEnum.SETUP, RTSPURL.ParseRTSPURL("rtsp://localhost/sample"));
            setupRequest.Header = new RTSPHeader(cseq, session);
            
            byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(setupRequest.ToString());
            RTSPRequest req = RTSPRequest.ParseRTSPRequest(RTSPMessage.ParseRTSPMessage(rtspRequestBuffer, null, null));

            Assert.Equal(RTSPMethodsEnum.SETUP, req.Method);
            Assert.Equal(cseq, req.Header.CSeq);
            Assert.Equal(session, req.Header.Session);
        }
    }
}
