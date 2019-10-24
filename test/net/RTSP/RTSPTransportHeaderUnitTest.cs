//-----------------------------------------------------------------------------
// Filename: RTSPTransportHeaderUnitTest.cs
//
// Description: Unit tests for the RTSPTransportHeader class.
//
// Author(s):
// Aaron Clauson
//// History:
// 21 Jan 2014	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
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
