//-----------------------------------------------------------------------------
// Filename: RTSPMessageUnitTest.cs
//
// Description: Unit tests for the RTSPMessage class.
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

namespace SIPSorcery.Net.UnitTests
{
    [TestClass]
    public class RTSPMessageUnitTest
    {
        /// <summary>
        /// Tests that an RTSP request with headers and a body is correctly serialised and parsed.
        /// </summary>
        [TestMethod]
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

            Assert.AreEqual(RTSPMessageTypesEnum.Response, rtspMessage.RTSPMessageType);
            Assert.AreEqual(body, rtspMessage.Body);
        }
    }
}
