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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Net.UnitTests
{
    [TestClass]
    public class RTSPRequestUnitTest
    {
        /// <summary>
        /// Tests that an RTSP request with standard headers can be correctly serialised and parsed.
        /// </summary>
        [TestMethod]
        public void RTSPRequestWIthStandardHeadersParseTest()
        {
            int cseq = 23;
            string session = Guid.NewGuid().ToString();

            RTSPRequest setupRequest = new RTSPRequest(RTSPMethodsEnum.SETUP, RTSPURL.ParseRTSPURL("rtsp://localhost/sample"));
            setupRequest.Header = new RTSPHeader(cseq, session);
            
            byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(setupRequest.ToString());
            RTSPRequest req = RTSPRequest.ParseRTSPRequest(RTSPMessage.ParseRTSPMessage(rtspRequestBuffer, null, null));

            Assert.AreEqual(RTSPMethodsEnum.SETUP, req.Method);
            Assert.AreEqual(cseq, req.Header.CSeq);
            Assert.AreEqual(session, req.Header.Session);
        }
    }
}
