//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    /// <summary>
    /// Unit tests for SIPStreamConnection class.
    /// </summary>
    [Trait("Category", "unit")]
    public class SIPStreamConnectionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPStreamConnectionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private string CRLF = SIPConstants.CRLF;

        /// <summary>
        /// Tests that a socket read leaves the buffers and positions in the correct state.
        /// </summary>
        [Fact]
        public void TestSocketReadSingleMessageTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
"SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" + CRLF +
CRLF +
"includesdp=true";

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

            SIPStreamConnection testConnection = new SIPStreamConnection(null, null, SIPProtocolsEnum.tcp);
            //Array.Copy(testReceiveBytes, 0, testConnection.RecvSocketArgs.Buffer, 0, testReceiveBytes.Length);

            testConnection.ExtractSIPMessages(null, testReceiveBytes, testReceiveBytes.Length);

            Assert.True(testConnection.RecvEndPosn == 0, "The receive buffer end position should have been 0.");
        }

        /// <summary>
        /// Tests that a socket read leaves the buffers and positions in the correct state when the SIP message has spurious characters
        /// preceding the transmission.
        /// </summary>
        [Fact]
        public void TestSocketReadWithBytesToSkipTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
"            SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" +
CRLF + CRLF +
"includesdp=true";

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

            Socket dummySocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            dummySocket.Bind(new IPEndPoint(IPAddress.Any, 0));
            SIPStreamConnection testConnection = new SIPStreamConnection(dummySocket, new IPEndPoint(IPAddress.Loopback, 0), SIPProtocolsEnum.tcp);
            int sipMessages = 0;
            testConnection.SIPMessageReceived += (chan, localEp, ep, buffer) => { sipMessages++; return Task.FromResult(0); };

            MockSIPChannel mockChannel = new MockSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            testConnection.ExtractSIPMessages(mockChannel, testReceiveBytes, testReceiveBytes.Length);

            Assert.True(sipMessages == 1, "The number of SIP messages parsed was incorrect, was " + sipMessages + ".");
            Assert.True(testConnection.RecvStartPosn == 0, $"The receive buffer start position was incorrect, was {testConnection.RecvStartPosn}.");
            Assert.True(testConnection.RecvEndPosn == 0, $"The receive buffer end position was incorrect, was {testConnection.RecvEndPosn}.");
        }

        /// <summary>
        /// Tests that a socket read leaves the buffers and positions in the correct state when the receive contains multiple 
        /// SIP message has spurious characters preceding the transmission.
        /// </summary>
        [Fact]
        public void TestSocketReadWithTwoMessagesAndBytesToSkipTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string testReceive =
"            SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF +
"Contact: <sip:10.1.1.5:62647;transport=tcp>" + CRLF +
"Max-Forwards: 70" + CRLF +
"Expires: 600" + CRLF +
"Content-Length: 15" + CRLF +
"Content-Type: text/text" + CRLF +
"Event: dialog" +
CRLF + CRLF +
"includesdp=true       " +
CRLF +
" SUBSCRIBE sip:aaron@10.1.1.5 SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:62647;branch=z9hG4bKa58b912c426f415daa887289efda50cd;rport" + CRLF +
"To: <sip:aaron@10.1.1.5>" + CRLF +
"From: <sip:switchboard@10.1.1.5>;tag=1902440575" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 3 SUBSCRIBE" + CRLF
+ CRLF +
"SUBSCRIBE sip:aaron@10.1.1";

            byte[] testReceiveBytes = UTF8Encoding.UTF8.GetBytes(testReceive);

            Socket dummySocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            dummySocket.Bind(new IPEndPoint(IPAddress.Any, 0));
            SIPStreamConnection testConnection = new SIPStreamConnection(dummySocket, new IPEndPoint(IPAddress.Loopback, 0), SIPProtocolsEnum.tcp);
            int sipMessages = 0;
            testConnection.SIPMessageReceived += (chan, localEp, ep, buffer) => { sipMessages++; return Task.FromResult(0); };
            Array.Copy(testReceiveBytes, 0, testConnection.RecvSocketArgs.Buffer, 0, testReceiveBytes.Length);

            MockSIPChannel mockChannel = new MockSIPChannel(new IPEndPoint(IPAddress.Any, 0));
            testConnection.ExtractSIPMessages(mockChannel, testConnection.RecvSocketArgs.Buffer, testReceiveBytes.Length);
            string remainingBytes = Encoding.UTF8.GetString(testConnection.RecvSocketArgs.Buffer, testConnection.RecvStartPosn, testConnection.RecvEndPosn - testConnection.RecvStartPosn);

            logger.LogDebug("SocketBufferEndPosition=" + testConnection.RecvEndPosn + ".");
            logger.LogDebug("SocketBuffer=" + remainingBytes + ".");

            Assert.True(sipMessages == 2, "The number of SIP messages parsed was incorrect.");
            Assert.True(708 == testConnection.RecvStartPosn, $"The receive buffer start position was incorrect, was {testConnection.RecvStartPosn}.");
            Assert.True(734 == testConnection.RecvEndPosn, $"The receive buffer end position was incorrect, was {testConnection.RecvEndPosn}.");
            Assert.True(remainingBytes == "SUBSCRIBE sip:aaron@10.1.1", $"The leftover bytes in the socket buffer were incorrect {remainingBytes}.");
        }

        /// <summary>
        /// Tests that the Content-Length is correctly parsed when a compact header form is used.
        /// </summary>
        [Fact]
        public void ContentLengthParseWhenUpperCaseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string notifyRequest =
"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1" + CRLF +
"To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968" + CRLF +
"From: <sip:127.0.0.1:5003>;tag=1555449860" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 4 NOTIFY" + CRLF +
"CONTENT-LENGTH: 2393" + CRLF +
"Contact: <sip:127.0.0.1:5003>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Event: dialog" + CRLF + CRLF;

            byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

            int contentLength = SIPMessageBuffer.GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

            Assert.True(contentLength == 2393, "The content length was parsed incorrectly.");
        }

        /// <summary>
        /// Tests that the Content-Length is correctly parsed when the SIP Contact header has mixed case.
        /// </summary>
        [Fact]
        public void ContentLengthParseWhenMixedCaseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string notifyRequest =
"NOTIFY sip:10.1.1.5:62647;transport=tcp SIP/2.0" + CRLF +
"Via: SIP/2.0/TCP 10.1.1.5:4506;branch=z9hG4bKa4d17f991015b1d8b788f2ac54d66ec66811226a;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5060;branch=z9hG4bKc2224b79f5af4c4a9b1cd649890c6497;rport" + CRLF +
"Via: SIP/2.0/UDP 127.0.0.1:5003;branch=z9hG4bK0495dc29b7eb40008779a75c3734c4c5;rport=5003;received=127.0.0.1" + CRLF +
"To: <sip:10.1.1.5:62647;transport=tcp>;tag=1892981968" + CRLF +
"From: <sip:127.0.0.1:5003>;tag=1555449860" + CRLF +
"Call-ID: 1b569032-d1e4-4869-be9f-67d4ba8a4e3a" + CRLF +
"CSeq: 4 NOTIFY" + CRLF +
"CoNtENT-LengTH: 2393" + CRLF +
"Contact: <sip:127.0.0.1:5003>" + CRLF +
"Max-Forwards: 69" + CRLF +
"Event: dialog" + CRLF + CRLF;

            byte[] notifyRequestBytes = UTF8Encoding.UTF8.GetBytes(notifyRequest);

            int contentLength = SIPMessageBuffer.GetContentLength(notifyRequestBytes, 0, notifyRequestBytes.Length);

            Assert.True(contentLength == 2393, "The content length was parsed incorrectly.");
        }
    }
}
