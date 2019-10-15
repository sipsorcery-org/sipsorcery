//-----------------------------------------------------------------------------
// Filename: RTSPTransportHeaderUnitTest.cs
//
// Description: Unit tests for the RTSPTransportHeader class.
// 
// History:
// 21 Jan 2014	Aaron Clauson	Created.
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

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Net.UnitTests
{
    [TestClass]
    public class RTSPTransportHeaderUnitTest
    {
        /// <summary>
        /// Tests that a typical RTSP transport header can be correctly parsed.
        /// </summary>
        [TestMethod]
        public void RTSPTransportHeaderParseTest()
        {
            string header = "RTP/AVP;unicast;destination=192.168.33.170;source=192.168.33.103;client_port=61132-61133;server_port=6970-6971";

            var transportHeader = RTSPTransportHeader.Parse(header);

            Assert.AreEqual("RTP/AVP", transportHeader.TransportSpecifier);
            Assert.AreEqual("unicast", transportHeader.BroadcastType);
            Assert.AreEqual("192.168.33.170", transportHeader.Destination);
            Assert.AreEqual("192.168.33.103", transportHeader.Source);
            Assert.AreEqual("61132-61133", transportHeader.ClientRTPPortRange);
            Assert.AreEqual("6970-6971", transportHeader.ServerRTPPortRange);
        }

        /// <summary>
        /// Tests that a typical RTSP transport header can be formatted to a header string..
        /// </summary>
        [TestMethod]
        public void RTSPTransportHeaderToStringTest()
        {
            var transportHeader = new RTSPTransportHeader() { Destination = "192.168.33.170", Source = "192.168.33.103", ClientRTPPortRange = "61132-61133", ServerRTPPortRange = "6970-6971" };

            Assert.AreEqual("RTP/AVP/UDP", transportHeader.TransportSpecifier);
            Assert.AreEqual("unicast", transportHeader.BroadcastType);
            Assert.AreEqual("192.168.33.170", transportHeader.Destination);
            Assert.AreEqual("192.168.33.103", transportHeader.Source);
            Assert.AreEqual("61132-61133", transportHeader.ClientRTPPortRange);
            Assert.AreEqual("6970-6971", transportHeader.ServerRTPPortRange);

            Assert.AreEqual("RTP/AVP/UDP;unicast;destination=192.168.33.170;source=192.168.33.103;client_port=61132-61133;server_port=6970-6971", transportHeader.ToString());
        }
    }
}
