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
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class BufferUtilsUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public BufferUtilsUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void HasStringUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] sample = Encoding.ASCII.GetBytes("The quick brown fox jumped over...");

            bool hasFox = BufferUtils.HasString(sample, 0, Int32.MaxValue, "fox", null);

            Assert.True(hasFox, "The string was not found in the buffer.");
        }

        [Fact]
        public void NotBeforeEndUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] sample = Encoding.ASCII.GetBytes("The quick brown fox jumped over...");

            bool hasFox = BufferUtils.HasString(sample, 0, Int32.MaxValue, "fox", "brown");

            Assert.True(!hasFox, "The string was not found in the buffer.");
        }

        [Fact]
        public void GetStringIndexUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:Blue Face SIP/2.0\r\n" +
                "Via: SIP/2.0/UDP 127.0.0.1:1720;branch=z9hG4bKlgnUQcaywCOaPcXR\r\n" +
                "Max-Forwards: 70\r\n" +
                "User-Agent: PA168S\r\n" +
                "From: \"user\" <sip:user@Blue Face>;tag=81swjAV7dHG1yjd5\r\n" +
                "To: \"user\" <sip:user@Blue Face>\r\n" +
                "Call-ID: DHZVs1HFuMoTQ6LO@82.114.95.1\r\n" +
                "CSeq: 15754 REGISTER\r\n" +
                "Contact: <sip:user@127.0.0.1:1720>\r\n" +
                "Expires: 30\r\n" +
                "Content-Length: 0\r\n\r\n";

            byte[] sample = Encoding.ASCII.GetBytes(sipMsg);

            int endOfMsgIndex = BufferUtils.GetStringPosition(sample, 0, Int32.MaxValue, "\r\n\r\n", null);

            Assert.True(endOfMsgIndex == sample.Length - 4, "The string position was not correctly found in the buffer.");
        }

        [Fact]
        public void GetStringIndexSIPInviteUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                 "INVITE sip:12345@sip.domain.com:5060;TCID-0 SIP/2.0\r\n" +
                 "From: UNAVAILABLE<sip:user@sip.domain.com:5060>;tag=c0a83dfe-13c4-26bf01-975a21d0-2d8a\r\n" +
                 "To: <sip:1234@sipdomain.com:5060>\r\n" +
                 "Call-ID: 94b6e3f8-c0a83dfe-13c4-26bf01-975a21ce-52c@sip.domain.com\r\n" +
                 "CSeq: 1 INVITE\r\n" +
                 "Via: SIP/2.0/UDP 86.9.84.23:5060;branch=z9hG4bK-26bf01-975a21d0-1ffb\r\n" +
                 "Max-Forwards: 70\r\n" +
                 "User-Agent: TA612V-V1.2_54\r\n" +
                 "Supported: timer,replaces\r\n" +
                 "Contact: <sip:user@88.8.88.88:5060>\r\n" +
                 "Content-Type: application/SDP\r\n" +
                 "Content-Length: 386\r\n" +
                 "\r\n" +
                 "v=0\r\n" +
                 "o=b0000 613 888 IN IP4 88.8.88.88\r\n" +
                 "s=SIP Call\r\n" +
                 "c=IN IP4 88.8.88.88\r\n" +
                 "t=0 0\r\n" +
                 "m=audio 10000 RTP/AVP 0 101 18 100 101 2 103 8\r\n" +
                 "a=fmtp:101 0-15\r\n" +
                 "a=fmtp:18 annexb=no\r\n" +
                 "a=sendrecv\r\n" +
                 "a=rtpmap:0 PCMU/8000\r\n" +
                 "a=rtpmap:101 telephone-event/8000\r\n" +
                 "a=rtpmap:18 G729/8000\r\n" +
                 "a=rtpmap:100 G726-16/8000\r\n" +
                 "a=rtpmap:101 G726-24/8000\r\n" +
                 "a=rtpmap:2 G726-32/8000\r\n" +
                 "a=rtpmap:103 G726-40/8000\r\n" +
                 "a=rtpmap:8 PCMA/8000";

            byte[] sample = Encoding.ASCII.GetBytes(sipMsg);

            int endOfMsgIndex = BufferUtils.GetStringPosition(sample, 0, Int32.MaxValue, "\r\n\r\n", null);

            Assert.True(endOfMsgIndex == sipMsg.IndexOf("\r\n\r\n"), "The string position was not correctly found in the buffer. Index found was " + endOfMsgIndex + ", should have been " + sipMsg.IndexOf("\r\n\r\n") + ".");
        }

        [Fact]
        public void GetStringIndexNotFoundUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sipMsg =
                "REGISTER sip:Blue Face SIP/2.0\r\n" +
                "Via: SIP/2.0/UDP 127.0.0.1:1720;branch=z9hG4bKlgnUQcaywCOaPcXR\r\n" +
                "Max-Forwards: 70\r\n" +
                "User-Agent: PA168S\r\n" +
                "From: \"user\" <sip:user@Blue Face>;tag=81swjAV7dHG1yjd5\r\n" +
                "To: \"user\" <sip:user@Blue Face>\r\n" +
                "Call-ID: DHZVs1HFuMoTQ6LO@82.114.95.1\r\n" +
                "CSeq: 15754 REGISTER\r\n" +
                "Contact: <sip:user@127.0.0.1:1720>\r\n" +
                "Expires: 30\r\n" +
                "Content-Length: 0\r\n";

            byte[] sample = Encoding.ASCII.GetBytes(sipMsg);

            int endOfMsgIndex = BufferUtils.GetStringPosition(sample, 0, Int32.MaxValue, "\r\n\r\n", null);

            Assert.True(endOfMsgIndex == -1, "The string position was not correctly found in the buffer.");
        }

        [Fact]
        public void HexStrUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] buffer = new byte[] { 1, 2, 3 };

            logger.LogDebug($"HexStr result: {buffer.HexStr()}.");

            Assert.Equal("010203", buffer.HexStr());
        }

        [Fact]
        public void HexStrWithSeparatorUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] buffer = new byte[] { 1, 2, 3 };

            logger.LogDebug($"HexStr result: {buffer.HexStr(':')}.");

            Assert.Equal("01:02:03", buffer.HexStr(':'));
        }
    }
}
