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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Net.UnitTests
{
    [TestClass]
    public class RTSPConnectionUnitTest
    {
        /// <summary>
        /// Tests that a receive buffer is correctly identified as containing a complete RTSP message when there is no Content-Length
        /// header.
        /// </summary>
        [TestMethod]
        public void RTSPMessageWithNoContentLengthHeaderAvailable()
        {
            RTSPRequest setupRequest = new RTSPRequest(RTSPMethodsEnum.SETUP, RTSPURL.ParseRTSPURL("rtsp://localhost/sample"));
            byte[] rtspRequestBuffer = Encoding.UTF8.GetBytes(setupRequest.ToString());

            byte[] rtspMessageBuffer = null;

            RTSPConnection rtspConnection = new RTSPConnection(null, null, null);
            rtspConnection.RTSPMessageReceived += (conn, remoteEndPoint, buffer) => { rtspMessageBuffer = buffer; };
            rtspConnection.SocketBuffer = rtspRequestBuffer;

            rtspConnection.SocketReadCompleted(rtspRequestBuffer.Length);

            Assert.IsNotNull(rtspMessageBuffer);

            RTSPRequest req = RTSPRequest.ParseRTSPRequest(RTSPMessage.ParseRTSPMessage(rtspMessageBuffer, null, null));

            Assert.AreEqual(RTSPMethodsEnum.SETUP, req.Method);
        }
    }
}
