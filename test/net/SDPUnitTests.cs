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
using System.Diagnostics;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    /// <summary>
    /// Unit tests for Session Description Protocol (SDP) class.
    /// </summary>
    [Trait("Category", "unit")]
    public class SDPUnitTests
    {
        private static string m_CRLF = SDP.CRLF;
        private static ILogger Logger = SIPSorcery.Sys.Log.Logger;

        [Fact]
        public void ParseSDPUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                "v=0" + m_CRLF +
                "o=root 3285 3285 IN IP4 10.0.0.4" + m_CRLF +
                "s=session" + m_CRLF +
                "c=IN IP4 10.0.0.4" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 12228 RTP/AVP 0 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-16" + m_CRLF +
                "a=silenceSupp:off - - - -" + m_CRLF +
                "a=ptime:20" + m_CRLF +
                "a=sendrecv";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Debug.WriteLine(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddress == "10.0.0.4", "The connection address was not parsed  correctly.");
            Assert.True(sdp.Media[0].Media == SDPMediaTypesEnum.audio, "The media type not parsed correctly.");
            Assert.True(sdp.Media[0].Port == 12228, "The connection port was not parsed correctly.");
            Assert.True(sdp.Media[0].GetFormatListToString() == "0 101", "The media format list was incorrect.");
            Assert.True(sdp.Media[0].MediaFormats[0].FormatID == 0, "The highest priority media format ID was incorrect.");
            Assert.True(sdp.Media[0].MediaFormats[0].Name == "PCMU", "The highest priority media format name was incorrect.");
            Assert.True(sdp.Media[0].MediaFormats[0].ClockRate == 8000, "The highest priority media format clockrate was incorrect.");
        }

        [Fact]
        public void ParseBriaSDPUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
            string sdpStr = "v=0\r\no=- 5 2 IN IP4 10.1.1.2\r\ns=CounterPath Bria\r\nc=IN IP4 144.137.16.240\r\nt=0 0\r\nm=audio 34640 RTP/AVP 0 8 101\r\na=sendrecv\r\na=rtpmap:101 telephone-event/8000\r\na=fmtp:101 0-15\r\na=alt:1 1 : STu/ZtOu 7hiLQmUp 10.1.1.2 34640\r\n";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Debug.WriteLine(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddress == "144.137.16.240", "The connection address was not parsed correctly.");
            Assert.True(sdp.Media[0].Port == 34640, "The connection port was not parsed correctly.");
            Assert.True(sdp.Media[0].MediaFormats[0].Name == "PCMU", "The highest priority media format name was incorrect.");
        }

        [Fact]
        public void ParseICESessionAttributesUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
              "v=0" + m_CRLF +
              "o=jdoe 2890844526 2890842807 IN IP4 10.0.1.1" + m_CRLF +
              "s=" + m_CRLF +
              "c=IN IP4 192.0.2.3" + m_CRLF +
              "t=0 0" + m_CRLF +
              "a=ice-pwd:asd88fgpdd777uzjYhagZg" + m_CRLF +
              "a=ice-ufrag:8hhY" + m_CRLF +
              "m=audio 45664 RTP/AVP 0" + m_CRLF +
              "b=RS:0" + m_CRLF +
              "b=RR:0" + m_CRLF +
              "a=rtpmap:0 PCMU/8000" + m_CRLF +
              "a=candidate:1 1 UDP 2130706431 10.0.1.1 8998 typ host" + m_CRLF +
              "a=candidate:2 1 UDP 1694498815 192.0.2.3 45664 typ srflx raddr 10.0.1.1 rport 8998";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Debug.WriteLine(sdp.ToString());

            Assert.True(sdp.IceUfrag == "8hhY", "The ICE username was not parsed correctly.");
            Assert.True(sdp.IcePwd == "asd88fgpdd777uzjYhagZg", "The ICE password was not parsed correctly.");
        }

        /// <summary>
        /// Test that an SDP payload with multiple media announcements (in this test audio and video) are correctly
        /// parsed.
        /// </summary>
        [Fact]
        public void ParseMultipleMediaAnnouncementsUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr = "v=0" + m_CRLF +
                "o=- 13064410510996677 3 IN IP4 10.1.1.2" + m_CRLF +
                "s=Bria 4 release 4.1.1 stamp 74246" + m_CRLF +
                "c=IN IP4 10.1.1.2" + m_CRLF +
                "b=AS:2064" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 49290 RTP/AVP 0" + m_CRLF +
                "a=sendrecv" + m_CRLF +
                "m=video 56674 RTP/AVP 96" + m_CRLF +
                "b=TIAS:2000000" + m_CRLF +
                "a=rtpmap:96 VP8/90000" + m_CRLF +
                "a=sendrecv" + m_CRLF +
                "a=rtcp-fb:* nack pli";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Debug.WriteLine(sdp.ToString());

            Assert.Equal(2, sdp.Media.Count);
            Assert.Equal(49290, sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).FirstOrDefault().Port);
            Assert.Equal(56674, sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).FirstOrDefault().Port);
        }

        /// <summary>
        /// Test that an SDP payload with multiple connection options is correctly parsed.
        /// </summary>
        [Fact]
        public void ParseAudioAndVideoConnectionsUnitTest()
        {
            Logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr = "v=0" + m_CRLF +
                "o=Cisco-SIPUA 6396 0 IN IP4 101.180.234.134" + m_CRLF +
                "s=SIP Call" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 19586 RTP/AVP 0" + m_CRLF +
                "c=IN IP4 101.180.234.134" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=sendrecv" + m_CRLF +
                "m=video 0 RTP/AVP 96" + m_CRLF +
                "c=IN IP4 10.0.0.10";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Logger.LogDebug(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddress == "101.180.234.134", "The connection address was not parsed correctly.");
        }

        /// <summary>
        /// Test that an SDP payload from Mircosoft's Edge browser for a WebRTC session gets parsed correctly..
        /// </summary>
        [Fact]
        public void ParseEdgeBrowserSdpUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr = "v=0" + m_CRLF +
                "o=- 8028343537520473029 0 IN IP4 127.0.0.1" + m_CRLF +
                "s=-" + m_CRLF +
                "t=0 0" + m_CRLF +
                "a=msid-semantic: WMS" + m_CRLF +
                "a=group:BUNDLE audio" + m_CRLF +
                "m=audio 7038 UDP/TLS/RTP/SAVPF 0" + m_CRLF +
                "c=IN IP4 10.0.75.1" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtcp:9 IN IP4 0.0.0.0" + m_CRLF +
                "a=setup:active" + m_CRLF +
                "a=mid:audio" + m_CRLF +
                "a=maxptime:60" + m_CRLF +
                "a=recvonly" + m_CRLF +
                "a=ice-ufrag:1Fs+" + m_CRLF +
                "a=ice-pwd:oiLbCgce1c9xzyamdrWtn9Q/" + m_CRLF +
                "a=fingerprint:sha-256 B0:1F:2C:72:8F:1A:14:CD:92:15:47:F0:C3:0A:69:F9:A9:43:35:EE:10:CB:F0:11:18:B8:0E:F9:A6:95:5F:B1" + m_CRLF +
                "a=candidate:1 1 udp 2130706431 10.0.75.1 7038 typ host" + m_CRLF +
                "a=candidate:2 1 udp 2130705919 172.22.240.1 31136 typ host" + m_CRLF +
                "a=candidate:3 1 udp 2130705407 172.22.48.1 21390 typ host" + m_CRLF +
                "a=candidate:4 1 udp 2130704895 192.168.11.50 26878 typ host" + m_CRLF +
                "a=candidate:5 1 tcp 1684797439 10.0.75.1 7038 typ srflx raddr 10.0.75.1 rport 7038 tcptype active" + m_CRLF +
                "a=rtcp-mux" + m_CRLF +
                "m=video 0 UDP/TLS/RTP/SAVPF" + m_CRLF +
                "c=IN IP4 0.0.0.0" + m_CRLF +
                "a=inactive";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Debug.WriteLine(sdp.ToString());

            Assert.Equal(2, sdp.Media.Count);
            Assert.True(sdp.Media.First().Media == SDPMediaTypesEnum.audio);
            Assert.True(sdp.Media.First().Transport == "UDP/TLS/RTP/SAVPF");
            Assert.True(sdp.Media.Last().Media == SDPMediaTypesEnum.video);
            Assert.True(sdp.Media.Last().Transport == "UDP/TLS/RTP/SAVPF");
        }

        /// <summary>
        /// Test that an SDP packet with IPv6 addresses can be correctly parsed.
        /// </summary>
        [Fact]
        public void ParseIPv6SDPUnitTest()
        {
            Logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr = "v=0" + m_CRLF +
                "o=nasa1 971731711378798081 0 IN IP6 2201:056D::112E:144A:1E24" + m_CRLF +
                "s=(Almost) live video feed from Mars-II satellite" + m_CRLF +
                "p=+1 713 555 1234" + m_CRLF +
                "c=IN IP6 FF1E:03AD::7F2E:172A:1E24" + m_CRLF +
                "t=3338481189 3370017201" + m_CRLF +
                "m=audio 6000 RTP/AVP 2" + m_CRLF +
                "a=rtpmap:2 G726-32/8000" + m_CRLF +
                "m=video 6024 RTP/AVP 107" + m_CRLF +
                "a=rtpmap:107 H263-1998/90000";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Logger.LogDebug(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddressType == "IP6", "The connection address type not parsed correctly.");
            Assert.True(sdp.Connection.ConnectionAddress == "FF1E:03AD::7F2E:172A:1E24", "The connection address was not parsed correctly.");
        }

        /// <summary>
        /// Tests that the first RTP end point corresponding to a media offer can be extracted.
        /// </summary>
        [Fact]
        public void GetFirstMedaiOfferRTPSocketUnitTest()
        {
            Logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                "v=0" + m_CRLF +
                "o=root 3285 3285 IN IP4 10.0.0.4" + m_CRLF +
                "s=session" + m_CRLF +
                "c=IN IP4 10.0.0.4" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 12228 RTP/AVP 0 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-16" + m_CRLF +
                "a=silenceSupp:off - - - -" + m_CRLF +
                "a=ptime:20" + m_CRLF +
                "a=sendrecv";

            IPEndPoint audioRtpEndPoint = SDP.GetSDPRTPEndPoint(sdpStr);

            Assert.True(audioRtpEndPoint.Address.Equals(IPAddress.Parse("10.0.0.4")), "The media RTP address was not correct.");
            Assert.True(audioRtpEndPoint.Port == 12228, "The media RTP port was not correct.");
        }

        /// <summary>
        /// Tests that the first IPv6 RTP end point corresponding to a media offer can be extracted.
        /// </summary>
        [Fact]
        public void GetFirstMedaiOfferIPv6RTPSocketUnitTest()
        {
            Logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr = "v=0" + m_CRLF +
                "o=nasa1 971731711378798081 0 IN IP6 2201:056D::112E:144A:1E24" + m_CRLF +
                "s=(Almost) live video feed from Mars-II satellite" + m_CRLF +
                "p=+1 713 555 1234" + m_CRLF +
                "c=IN IP6 FF1E:03AD::7F2E:172A:1E24" + m_CRLF +
                "t=3338481189 3370017201" + m_CRLF +
                "m=audio 6000 RTP/AVP 2" + m_CRLF +
                "a=rtpmap:2 G726-32/8000" + m_CRLF +
                "m=video 6024 RTP/AVP 107" + m_CRLF +
                "a=rtpmap:107 H263-1998/90000";

            IPEndPoint audioRtpEndPoint = SDP.GetSDPRTPEndPoint(sdpStr);

            Assert.True(audioRtpEndPoint.Address.Equals(IPAddress.Parse("FF1E:03AD::7F2E:172A:1E24")), "The media RTP address was not correct.");
            Assert.True(audioRtpEndPoint.Port == 6000, "The media RTP port was not correct.");
        }
    }
}
