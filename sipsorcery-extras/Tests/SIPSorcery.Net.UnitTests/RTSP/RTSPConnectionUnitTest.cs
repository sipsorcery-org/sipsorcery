//-----------------------------------------------------------------------------
// Filename: RTSPConnectionUnitTest.cs
//
// Description: Unit tests for the RTSPConnection class.
// 
// History:
// 20 Jan 2014	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Net.UnitTests.RTSP
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
