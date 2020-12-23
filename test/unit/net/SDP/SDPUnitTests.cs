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

using System.Diagnostics;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions.V1;
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
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SDPUnitTests(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ParseSDPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddress == "10.0.0.4", "The connection address was not parsed  correctly.");
            Assert.True(sdp.Media[0].Media == SDPMediaTypesEnum.audio, "The media type not parsed correctly.");
            Assert.True(sdp.Media[0].Port == 12228, "The connection port was not parsed correctly.");
            Assert.True(sdp.Media[0].GetFormatListToString() == "0 101", "The media format list was incorrect.");
            Assert.True(sdp.Media[0].MediaFormats[0].ID == 0, "The highest priority media format ID was incorrect.");
            Assert.True(sdp.Media[0].MediaFormats[0].Name() == "PCMU", "The highest priority media format name was incorrect.");
            Assert.Equal(SDPWellKnownMediaFormatsEnum.PCMU.ToString(), sdp.Media[0].MediaFormats[0].Name());
            Assert.True(sdp.Media[0].MediaFormats[0].Rtpmap == "PCMU/8000", "The highest priority media format rtpmap was incorrect.");
        }

        [Fact]
        public void ParseBriaSDPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            string sdpStr = 
                "v=0" + 
                "o=- 5 2 IN IP4 10.1.1.2" + m_CRLF +
                "s=CounterPath Bria" + m_CRLF +
                "c=IN IP4 144.137.16.240" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=audio 34640 RTP/AVP 0 8 101" + m_CRLF +
                "a=sendrecv" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-15" + m_CRLF +
                "a=alt:1 1 : STu/ZtOu 7hiLQmUp 10.1.1.2 34640";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddress == "144.137.16.240", "The connection address was not parsed correctly.");
            Assert.True(sdp.Media[0].Port == 34640, "The connection port was not parsed correctly.");
            Assert.True(sdp.Media[0].MediaFormats[0].Name() == "PCMU", "The highest priority media format name was incorrect.");
            Assert.Equal(SDPWellKnownMediaFormatsEnum.PCMU.ToString(), sdp.Media[0].MediaFormats[0].Name());
        }

        /// <summary>
        /// Tests that the telephone event media format gets correctly recognised.
        /// </summary>
        [Fact]
        public void ParseTelephoneEventSDPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                " v=0" + m_CRLF +
                " o=root 3285 3285 IN IP4 10.0.0.4" + m_CRLF +
                " s=session" + m_CRLF +
                " c=IN IP4 10.0.0.4" + m_CRLF +
                " t=0 0" + m_CRLF +
                " m=audio 12228 RTP/AVP 0 101" + m_CRLF +
                " a=rtpmap:0 PCMU/8000" + m_CRLF +
                " a=rtpmap:101 telephone-event/8000" + m_CRLF +
                " a=fmtp:101 0-16" + m_CRLF +
                " a=silenceSupp:off - - - -" + m_CRLF +
                " a=ptime:20" + m_CRLF +
                " a=sendrecv";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            //logger.LogDebug($"audio format[0]: {sdp.Media[0].MediaFormats[0]}");
            //logger.LogDebug($"audio format[1]: {sdp.Media[0].MediaFormats[101]}");

            Assert.True(sdp.Connection.ConnectionAddress == "10.0.0.4", "The connection address was not parsed  correctly.");
            Assert.True(sdp.Username == "root", "The owner was not parsed correctly.");
            Assert.True(sdp.SessionName == "session", "The SessionName was not parsed correctly.");
            Assert.True(sdp.Media[0].Media == SDPMediaTypesEnum.audio, "The media type not parsed correctly.");
            Assert.Equal(SDPWellKnownMediaFormatsEnum.PCMU.ToString(), sdp.Media[0].MediaFormats[0].Name());
            Assert.Equal(SDP.TELEPHONE_EVENT_ATTRIBUTE, sdp.Media[0].MediaFormats[101].Name());
        }

        [Fact]
        public void ParseBadFormatBriaSDPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);
            string sdpStr = " v=0\r\no=- 5 2 IN IP4 10.1.1.2\r\n s=CounterPath Bria\r\nc=IN IP4 144.137.16.240\r\nt=0 0\r\n m=audio 34640 RTP/AVP 0 8 101\r\na=sendrecv\r\na=rtpmap:101 telephone-event/8000\r\na=fmtp:101 0-15\r\na=alt:1 1 : STu/ZtOu 7hiLQmUp 10.1.1.2 34640\r\n";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Debug.WriteLine(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddress == "144.137.16.240", "The connection address was not parsed correctly.");
            Assert.True(sdp.SessionName == "CounterPath Bria", "The SessionName was not parsed correctly.");
        }

        [Fact]
        public void ParseICESessionAttributesUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug(sdp.ToString());

            //Assert.True(sdp.Connection.ConnectionAddress == "101.180.234.134", "The connection address was not parsed correctly.");
            Assert.NotEmpty(sdp.Media);
            Assert.True(sdp.Media[0].Media == SDPMediaTypesEnum.audio, "The media type not parsed correctly.");
            Assert.Equal(SDPWellKnownMediaFormatsEnum.PCMU.ToString(), sdp.Media[0].MediaFormats[0].Name());
            Assert.True(sdp.Media[1].Media == SDPMediaTypesEnum.video, "The media type not parsed correctly.");
            Assert.True(sdp.Media[1].Connection.ConnectionAddress == "10.0.0.10", "The connection address was not parsed correctly.");
        }

        [Fact]
        public void ParseMediaTypeImageUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr = "v=0" + m_CRLF +
                "o=OfficeMasterDirectSIP 806542878 806542879 IN IP4 10.2.0.110" + m_CRLF +
                "s=FOIP Call" + m_CRLF +
                "c=IN IP4 10.2.0.110" + m_CRLF +
                "t=0 0" + m_CRLF +
                "m=image 50594 udptl t38" + m_CRLF +
                "a=T38FaxRateManagement:transferredTCF" + m_CRLF +
                "a=T38FaxVersion:0";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddress == "10.2.0.110", "The connection address was not parsed correctly.");
            Assert.NotEmpty(sdp.Media);
            Assert.True(sdp.Media[0].Media == SDPMediaTypesEnum.image, "The media type not parsed correctly.");
            //Assert.True(sdp.Media[0].HasMediaFormat("t38"), "The highest priority media format ID was incorrect.");
            //Assert.True(sdp.Media[0].Transport == "udptl", "The media transport string was incorrect.");
        }

        /// <summary>
        /// Test that an SDP payload from Microsoft's Edge browser for a WebRTC session gets parsed correctly.
        /// </summary>
        [Fact]
        public void ParseEdgeBrowserSdpUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            logger.LogDebug(sdp.ToString());

            Assert.True(sdp.Connection.ConnectionAddressType == "IP6", "The connection address type not parsed correctly.");
            Assert.True(sdp.Connection.ConnectionAddress == "FF1E:03AD::7F2E:172A:1E24", "The connection address was not parsed correctly.");
        }

        /// <summary>
        /// Tests that the first RTP end point corresponding to a media offer can be extracted.
        /// </summary>
        [Fact]
        public void GetFirstMediaOfferRTPSocketUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
        public void GetFirstMediaOfferIPv6RTPSocketUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

        /// <summary>
        /// Tests that the media stream status for the first media announcement is correctly parsed.
        /// </summary>
        [Fact]
        public void GetFirstMediaSteamStatusUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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

            Assert.Equal(MediaStreamStatusEnum.SendRecv, sdp.Media.First().MediaStreamStatus);
        }

        /// <summary>
        /// Tests that the media stream status for the first media announcement is correctly parsed when it's not the
        /// default value.
        /// </summary>
        [Fact]
        public void GetFirstMediaSteamStatusNonDefaultUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
                "a=sendonly";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Assert.Equal(MediaStreamStatusEnum.SendOnly, sdp.Media.First().MediaStreamStatus);
        }

        /// <summary>
        /// Tests that the media stream status for the session is parsed correctly.
        /// </summary>
        [Fact]
        public void GetSessionMediaSteamStatusUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                "v=0" + m_CRLF +
                "o=root 3285 3285 IN IP4 10.0.0.4" + m_CRLF +
                "s=session" + m_CRLF +
                "c=IN IP4 10.0.0.4" + m_CRLF +
                "t=0 0" + m_CRLF +
                "a=recvonly" + m_CRLF +
                "m=audio 12228 RTP/AVP 0 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-16" + m_CRLF +
                "a=silenceSupp:off - - - -" + m_CRLF +
                "a=ptime:20";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Assert.Equal(MediaStreamStatusEnum.RecvOnly, sdp.SessionMediaStreamStatus);
            Assert.Equal(MediaStreamStatusEnum.RecvOnly, sdp.Media.First().MediaStreamStatus);
        }

        /// <summary>
        /// Tests that the media stream status for an announcement is set correctly when it
        /// differs from the session status.
        /// </summary>
        [Fact]
        public void GetAnnMediaSteamDiffToStreamStatusUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                "v=0" + m_CRLF +
                "o=root 3285 3285 IN IP4 10.0.0.4" + m_CRLF +
                "s=session" + m_CRLF +
                "c=IN IP4 10.0.0.4" + m_CRLF +
                "t=0 0" + m_CRLF +
                "a=recvonly" + m_CRLF +
                "m=audio 12228 RTP/AVP 0 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-16" + m_CRLF +
                "a=silenceSupp:off - - - -" + m_CRLF +
                "a=ptime:20" + m_CRLF +
                "a=sendonly";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Assert.Equal(MediaStreamStatusEnum.RecvOnly, sdp.SessionMediaStreamStatus);
            Assert.Equal(MediaStreamStatusEnum.SendOnly, sdp.Media.First().MediaStreamStatus);
        }

        /// <summary>
        /// Tests that the media stream status for an announcement is set correctly when there
        /// is no session of announcement attribute.
        /// </summary>
        [Fact]
        public void GetAnnMediaSteamNotreamStatusAttributesUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
                "a=ptime:20";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Assert.Null(sdp.SessionMediaStreamStatus);
            Assert.Equal(MediaStreamStatusEnum.SendRecv, sdp.Media.First().MediaStreamStatus);
        }

        /// <summary>
        /// Tests that the media stream status for a media announcement is correctly parsed and serialised correctly.
        /// </summary>
        [Fact]
        public void AnnouncementMediaSteamStatuRoundtripUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
                "a=sendonly";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP sdpRoundTrip = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal(MediaStreamStatusEnum.SendOnly, sdpRoundTrip.Media.First().MediaStreamStatus);
        }

        /// <summary>
        /// Tests that the media stream status for the session is parsed and serialised correctly.
        /// </summary>
        [Fact]
        public void SessionMediaSteamStatusRoundTripUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                "v=0" + m_CRLF +
                "o=root 3285 3285 IN IP4 10.0.0.4" + m_CRLF +
                "s=session" + m_CRLF +
                "c=IN IP4 10.0.0.4" + m_CRLF +
                "t=0 0" + m_CRLF +
                "a=recvonly" + m_CRLF +
                "m=audio 12228 RTP/AVP 0 101" + m_CRLF +
                "a=rtpmap:0 PCMU/8000" + m_CRLF +
                "a=rtpmap:101 telephone-event/8000" + m_CRLF +
                "a=fmtp:101 0-16" + m_CRLF +
                "a=silenceSupp:off - - - -" + m_CRLF +
                "a=ptime:20";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP sdpRoundTrip = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal(MediaStreamStatusEnum.RecvOnly, sdpRoundTrip.SessionMediaStreamStatus);
        }

        /// <summary>
        /// Tests that parsing a typical SDP for a WebRTC session gets parsed correctly
        /// </summary>
        [Fact]
        public void ParseWebRtcSDPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=- 1090343221 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE audio video
m=audio 11158 RTP/SAVP 0
c=IN IP4 127.0.0.1
a=candidate:1988909849 1 udp 1124657401 192.168.11.50 11158 typ host generation 0
a=candidate:1846148317 1 udp 2094219785 127.0.0.1 11158 typ host generation 0
a=candidate:2012632329 1 udp 2122820711 172.30.224.1 11158 typ host generation 0
a=end-of-candidates 
a=ice-ufrag:UWWAVCUMPZHPCLNIMZYA
a=ice-pwd:IEUVYLWMXMQZKCMLTXQHZZVWXRCBLPPNUYFPCABK
a=fingerprint:sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD
a=setup:actpass
a=sendonly
a=rtcp-mux
a=mid:audio
a=rtpmap:0 PCMU/8000
m=video 0 RTP/SAVP 100
c=IN IP4 127.0.0.1
a=ice-ufrag:UWWAVCUMPZHPCLNIMZYA
a=ice-pwd:IEUVYLWMXMQZKCMLTXQHZZVWXRCBLPPNUYFPCABK
a=fingerprint:sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD
a=bundle-only 
a=setup:actpass
a=sendonly
a=rtcp-mux
a=mid:video
a=rtpmap:100 VP8/90000";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP rndTripSdp = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal("BUNDLE audio video", sdp.Group);
            Assert.Equal("BUNDLE audio video", rndTripSdp.Group);
            Assert.Equal("UWWAVCUMPZHPCLNIMZYA", sdp.Media[0].IceUfrag);
            Assert.Equal("UWWAVCUMPZHPCLNIMZYA", rndTripSdp.Media[0].IceUfrag);
            Assert.Equal("IEUVYLWMXMQZKCMLTXQHZZVWXRCBLPPNUYFPCABK", sdp.Media[0].IcePwd);
            Assert.Equal("IEUVYLWMXMQZKCMLTXQHZZVWXRCBLPPNUYFPCABK", rndTripSdp.Media[0].IcePwd);
            Assert.Equal(3, sdp.Media[0].IceCandidates.Count());
            Assert.Equal(3, rndTripSdp.Media[0].IceCandidates.Count());
            Assert.Equal("sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD", sdp.Media[0].DtlsFingerprint);
            Assert.Equal("sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD", rndTripSdp.Media[0].DtlsFingerprint);
            Assert.Equal("audio", sdp.Media[0].MediaID);
            Assert.Equal("audio", rndTripSdp.Media[0].MediaID);
            Assert.Equal("video", sdp.Media[1].MediaID);
            Assert.Equal("video", rndTripSdp.Media[1].MediaID);
        }

        /// <summary>
        /// Tests that parsing an SDP offer from Chrome gets parsed correctly.
        /// </summary>
        [Fact]
        public void ParseChromeOfferSDPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=- 118981680356865692 3 IN IP4 127.0.0.1
s=-
c=IN IP4 192.168.11.50
t=0 0
a=group:BUNDLE 0 1
a=msid-semantic: WMS ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ
m=audio 61680 UDP/TLS/RTP/SAVPF 111 103 104 9 0 8 106 105 13 110 112 113 126
c=IN IP4 192.168.11.50
a=ice-ufrag:RsdO
a=ice-pwd:s7vuz5UHOQhh8kN0+U6k6VLb
a=fingerprint:sha-256 BE:31:7F:12:ED:29:5B:59:F3:0D:68:E4:F4:E5:3F:80:86:10:CE:A2:2A:A7:42:79:5A:CF:98:B6:E4:E1:ED:AA
a=candidate:1390596646 1 udp 1880747346 192.168.11.50 61680 typ host generation 0
a=end-of-candidates
a=mid:0
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10;useinbandfec=1
a=rtpmap:103 ISAC/16000
a=rtpmap:104 ISAC/32000
a=rtpmap:106 CN/32000
a=rtpmap:105 CN/16000
a=rtpmap:13 CN/8000
a=rtpmap:110 telephone-event/48000
a=rtpmap:112 telephone-event/32000
a=rtpmap:113 telephone-event/16000
a=rtpmap:126 telephone-event/8000
a=rtcp:9 IN IP4 0.0.0.0
a=ice-options:trickle
a=setup:actpass
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:4 urn:ietf:params:rtp-hdrext:sdes:mid
a=extmap:5 urn:ietf:params:rtp-hdrext:sdes:rtp-stream-id
a=extmap:6 urn:ietf:params:rtp-hdrext:sdes:repaired-rtp-stream-id
a=msid:ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ eda73d03-71e1-4409-bc45-71b67a6175d4
a=rtcp-mux
a=rtcp-fb:111 transport-cc
a=ssrc:1676157391 cname:NBv/ONzaeuNdPp6L
a=ssrc:1676157391 msid:ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ eda73d03-71e1-4409-bc45-71b67a6175d4
a=ssrc:1676157391 mslabel:ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ
a=ssrc:1676157391 label:eda73d03-71e1-4409-bc45-71b67a6175d4
a=sendrecv
m=video 61682 UDP/TLS/RTP/SAVPF 96 97 98 99 100 101 102 122 127 121 125 107 108 109 124 120 123 119 114 115 116
c=IN IP4 192.168.11.50
a=ice-ufrag:RsdO
a=ice-pwd:s7vuz5UHOQhh8kN0+U6k6VLb
a=fingerprint:sha-256 BE:31:7F:12:ED:29:5B:59:F3:0D:68:E4:F4:E5:3F:80:86:10:CE:A2:2A:A7:42:79:5A:CF:98:B6:E4:E1:ED:AA
a=candidate:1764481976 1 udp 1635682652 192.168.11.50 61682 typ host generation 0
a=end-of-candidates
a=mid:1
a=rtpmap:96 VP8/90000
a=rtpmap:97 rtx/90000
a=fmtp:97 apt=96
a=rtpmap:98 VP9/90000
a=fmtp:98 profile-id=0
a=rtpmap:99 rtx/90000
a=fmtp:99 apt=98
a=rtpmap:100 VP9/90000
a=fmtp:100 profile-id=2
a=rtpmap:101 rtx/90000
a=fmtp:101 apt=100
a=rtpmap:102 H264/90000
a=fmtp:102 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42001f
a=rtpmap:122 rtx/90000
a=fmtp:122 apt=102
a=rtpmap:127 H264/90000
a=fmtp:127 level-asymmetry-allowed=1;packetization-mode=0;profile-level-id=42001f
a=rtpmap:121 rtx/90000
a=fmtp:121 apt=127
a=rtpmap:125 H264/90000
a=fmtp:125 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f
a=rtpmap:107 rtx/90000
a=fmtp:107 apt=125
a=rtpmap:108 H264/90000
a=fmtp:108 level-asymmetry-allowed=1;packetization-mode=0;profile-level-id=42e01f
a=rtpmap:109 rtx/90000
a=fmtp:109 apt=108
a=rtpmap:124 H264/90000
a=fmtp:124 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=4d0032
a=rtpmap:120 rtx/90000
a=fmtp:120 apt=124
a=rtpmap:123 H264/90000
a=fmtp:123 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=640032
a=rtpmap:119 rtx/90000
a=fmtp:119 apt=123
a=rtpmap:114 red/90000
a=rtpmap:115 rtx/90000
a=fmtp:115 apt=114
a=rtpmap:116 ulpfec/90000
a=rtcp:9 IN IP4 0.0.0.0
a=ice-options:trickle
a=setup:actpass
a=extmap:14 urn:ietf:params:rtp-hdrext:toffset
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:13 urn:3gpp:video-orientation
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:12 http://www.webrtc.org/experiments/rtp-hdrext/playout-delay
a=extmap:11 http://www.webrtc.org/experiments/rtp-hdrext/video-content-type
a=extmap:7 http://www.webrtc.org/experiments/rtp-hdrext/video-timing
a=extmap:8 http://tools.ietf.org/html/draft-ietf-avtext-framemarking-07
a=extmap:9 http://www.webrtc.org/experiments/rtp-hdrext/color-space
a=extmap:4 urn:ietf:params:rtp-hdrext:sdes:mid
a=extmap:5 urn:ietf:params:rtp-hdrext:sdes:rtp-stream-id
a=extmap:6 urn:ietf:params:rtp-hdrext:sdes:repaired-rtp-stream-id
a=msid:ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ e9e6e397-1589-4df3-bd6a-53124925325a
a=rtcp-mux
a=rtcp-rsize
a=rtcp-fb:96 goog-remb
a=rtcp-fb:96 transport-cc
a=rtcp-fb:96 ccm fir
a=rtcp-fb:96 nack
a=rtcp-fb:96 nack pli
a=rtcp-fb:98 goog-remb
a=rtcp-fb:98 transport-cc
a=rtcp-fb:98 ccm fir
a=rtcp-fb:98 nack
a=rtcp-fb:98 nack pli
a=rtcp-fb:100 goog-remb
a=rtcp-fb:100 transport-cc
a=rtcp-fb:100 ccm fir
a=rtcp-fb:100 nack
a=rtcp-fb:100 nack pli
a=rtcp-fb:102 goog-remb
a=rtcp-fb:102 transport-cc
a=rtcp-fb:102 ccm fir
a=rtcp-fb:102 nack
a=rtcp-fb:102 nack pli
a=rtcp-fb:127 goog-remb
a=rtcp-fb:127 transport-cc
a=rtcp-fb:127 ccm fir
a=rtcp-fb:127 nack
a=rtcp-fb:127 nack pli
a=rtcp-fb:125 goog-remb
a=rtcp-fb:125 transport-cc
a=rtcp-fb:125 ccm fir
a=rtcp-fb:125 nack
a=rtcp-fb:125 nack pli
a=rtcp-fb:108 goog-remb
a=rtcp-fb:108 transport-cc
a=rtcp-fb:108 ccm fir
a=rtcp-fb:108 nack
a=rtcp-fb:108 nack pli
a=rtcp-fb:124 goog-remb
a=rtcp-fb:124 transport-cc
a=rtcp-fb:124 ccm fir
a=rtcp-fb:124 nack
a=rtcp-fb:124 nack pli
a=rtcp-fb:123 goog-remb
a=rtcp-fb:123 transport-cc
a=rtcp-fb:123 ccm fir
a=rtcp-fb:123 nack
a=rtcp-fb:123 nack pli
a=ssrc-group:FID 3966011320 1316862390
a=ssrc:3966011320 cname:NBv/ONzaeuNdPp6L
a=ssrc:3966011320 msid:ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ e9e6e397-1589-4df3-bd6a-53124925325a
a=ssrc:3966011320 mslabel:ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ
a=ssrc:3966011320 label:e9e6e397-1589-4df3-bd6a-53124925325a
a=ssrc:1316862390 cname:NBv/ONzaeuNdPp6L
a=ssrc:1316862390 msid:ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ e9e6e397-1589-4df3-bd6a-53124925325a
a=ssrc:1316862390 mslabel:ZJChDbsCl9zy9kENxMxREmEpZqKfhy2AGtsZ
a=ssrc:1316862390 label:e9e6e397-1589-4df3-bd6a-53124925325a
a=sendrecv";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP rndTripSdp = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal("BUNDLE 0 1", sdp.Group);
            Assert.Equal("BUNDLE 0 1", rndTripSdp.Group);
            Assert.Single(sdp.Media.First().IceCandidates);
            Assert.Single(rndTripSdp.Media.First().IceCandidates);
            Assert.Contains(sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single().SsrcAttributes, x => x.SSRC == 1676157391U);
            Assert.Contains(sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().SsrcAttributes, x => x.SSRC == 3966011320U);
            Assert.Contains(sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().SsrcAttributes, x => x.SSRC == 1316862390U);
            Assert.Contains(rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single().SsrcAttributes, x => x.SSRC == 1676157391U);
            Assert.Contains(rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().SsrcAttributes, x => x.SSRC == 3966011320U);
            Assert.Contains(rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().SsrcAttributes, x => x.SSRC == 1316862390U);
        }

        /// <summary>
        /// Tests that parsing an SDP offer that only contains a data channel media announcement gets parsed correctly.
        /// </summary>
        [Fact]
        public void ParseDataChannelOnlyOfferSDPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=- 2691666610091555147 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0
a=msid-semantic: WMS
m=application 9 UDP/DTLS/SCTP webrtc-datachannel
c=IN IP4 0.0.0.0
a=ice-ufrag:KQVl
a=ice-pwd:E+imlxJhaTN7Is+Qen3Hu6eK
a=ice-options:trickle
a=fingerprint:sha-256 6E:04:B9:05:60:84:22:B5:5A:A3:E9:00:6D:1A:29:FC:6F:C7:D9:79:D7:3B:BC:8D:BC:3D:7F:FC:94:3A:10:9E
a=setup:actpass
a=mid:0
a=sctp-port:5000
a=max-message-size:262144";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP rndTripSdp = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal("BUNDLE 0", rndTripSdp.Group);
            Assert.Single(rndTripSdp.Media);
            Assert.Equal("sha-256 6E:04:B9:05:60:84:22:B5:5A:A3:E9:00:6D:1A:29:FC:6F:C7:D9:79:D7:3B:BC:8D:BC:3D:7F:FC:94:3A:10:9E", rndTripSdp.Media.Single().DtlsFingerprint);
            Assert.Single(rndTripSdp.Media.Single().ApplicationMediaFormats);
            Assert.Equal(5000, rndTripSdp.Media.Single().SctpPort.Value);
            Assert.Equal(262144, rndTripSdp.Media.Single().MaxMessageSize);
        }

        /// <summary>
        /// Tests that parsing an SDP offer from the Pion Go library that only contains a data channel media 
        /// announcement gets parsed correctly.
        /// </summary>
        [Fact]
        public void ParsePionDataChannelOnlyOfferSDPUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=- 87119400 1595185172 IN IP4 0.0.0.0
s=-
t=0 0
a=fingerprint:sha-256 81:5C:47:85:9C:3D:CC:E6:B5:94:0B:3B:65:D5:39:1A:CD:8F:48:2D:78:0F:9F:0B:18:93:BF:C9:F6:C9:8E:F8
a=group:BUNDLE 0
m=application 9 DTLS/SCTP 5000
c=IN IP4 0.0.0.0
a=setup:active
a=mid:0
a=sendrecv
a=sctpmap:5000 webrtc-datachannel 1024";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP rndTripSdp = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal("BUNDLE 0", rndTripSdp.Group);
            Assert.Single(rndTripSdp.Media);
            Assert.Equal("sha-256 81:5C:47:85:9C:3D:CC:E6:B5:94:0B:3B:65:D5:39:1A:CD:8F:48:2D:78:0F:9F:0B:18:93:BF:C9:F6:C9:8E:F8", rndTripSdp.DtlsFingerprint);
            Assert.Single(rndTripSdp.Media.Single().ApplicationMediaFormats);
            Assert.Equal(5000, rndTripSdp.Media.Single().SctpPort.Value);
            Assert.Equal(1024, rndTripSdp.Media.Single().MaxMessageSize);
        }

        /// <summary>
        /// Tests that parsing an SDP media format attribute where the name has a hyphen in it works correctly.
        /// </summary>
        [Fact]
        public void ParseMediaFormatWithHyphenNameUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=- 1970544282 0 IN IP4 127.0.0.1
s=-
c=IN IP4 10.10.1.8
t=0 0
m=audio 57982 RTP/AVP 0 8
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv
m=video 57984 RTP/AVP 96
a=rtpmap:96 H263-1998/90000
a=fmtp:96 QCIF=3
a=sendrecv";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP rndTripSdp = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal(96, rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaFormats.Single().Key);
            Assert.Equal("H263-1998", rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaFormats.Single().Value.Name());
        }

        /// <summary>
        /// Tests that parsing an SDP media format attribute where the name has additional information following a '/'
        /// character is parsed correctly.
        /// </summary>
        [Fact]
        public void ParseMediaFormatWithFowardSlashUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=- 1970544282 0 IN IP4 127.0.0.1
s=-
c=IN IP4 10.10.1.8
t=0 0
m=audio 57982 RTP/AVP 111
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10;useinbandfec=1
a=sendrecv";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP rndTripSdp = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal(111, rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single().MediaFormats.Single().Key);
            Assert.Equal("opus", rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single().MediaFormats.Single().Value.Name());
        }

        /// <summary>
        /// Tests that parsing an SDP media format attribute which specifies a dynamic media format fmtp in advance of the 
        /// rtpmap works correctly.
        /// </summary>
        [Fact]
        public void ParseOfferWithFmtpPreceedingRtmapTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=mozilla...THIS_IS_SDPARTA-80.0.1 5936658357711814578 0 IN IP4 0.0.0.0
s=-
t=0 0
a=sendrecv
a=fingerprint:sha-256 46:7C:4B:FD:47:E1:22:16:28:FC:52:94:C8:9D:7D:24:2F:C3:A8:66:02:17:0D:41:DF:34:99:1C:48:CB:9F:D5
a=group:BUNDLE 0
a=ice-options:trickle
a=msid-semantic:WMS *
m=video 9 UDP/TLS/RTP/SAVP 96
c=IN IP4 0.0.0.0
a=recvonly
a=fmtp:96 max-fs=12288;max-fr=60
a=ice-pwd:8136ef42e22d9d6b31d23b39a662bf8d
a=ice-ufrag:2cbeec1e
a=mid:0
a=rtcp-mux
a=rtpmap:96 VP8/90000
a=setup:active
a=ssrc:2404235415 cname:{7c06c5db-d3db-4891-b729-df4919014c3f}";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Assert.Equal(96, sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaFormats.Single().Key);
            Assert.Equal("VP8", sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaFormats.Single().Value.Name());
            Assert.Equal("VP8/90000", sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaFormats.Single().Value.Rtpmap);
            Assert.Equal("max-fs=12288;max-fr=60", sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaFormats.Single().Value.Fmtp);
        }

        /// <summary>
        /// Tests that parsing an SDP media format attribute for a Mission Critical Push To Talk (MCPTT)
        /// announcement works correctly.
        /// </summary>
        [Fact]
        public void ParseMcpttTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=root 5936658357711814578 0 IN IP4 0.0.0.0
s=-
t=0 0
m=audio 55316 RTP/AVP 0 101
a=rtpmap:0 PCMU/8000
a=label:1
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-15
a=ptime:20
a=sendrecv
m=application 55317 udp MCPTT
a=fmtp:MCPTT mc_queueing;mc_priority=4";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP rndTripSdp = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal("MCPTT", rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.application).Single().ApplicationMediaFormats.Single().Key);
            Assert.Equal("mc_queueing;mc_priority=4", rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.application).Single().ApplicationMediaFormats.Single().Value.Fmtp);
        }

        /// <summary>
        /// Tests that a description attribute can be successfully round tripped.
        /// </summary>
        [Fact]
        public void DescriptionAttributeRoundTripTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string sdpStr =
                @"v=0
o=root 5936658357711814578 0 IN IP4 0.0.0.0
s=-
i=A session description
t=0 0
m=audio 55316 RTP/AVP 0 101
a=rtpmap:0 PCMU/8000
a=label:1
i=speech
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-15
a=ptime:20
a=sendrecv
m=video 61682 UDP/TLS/RTP/SAVPF 96
c=IN IP4 192.168.11.50
a=rtpmap:96 VP8/90000
a=label:2
i=video title
a=sendrecv
";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            logger.LogDebug(sdp.ToString());

            SDP rndTripSdp = SDP.ParseSDPDescription(sdp.ToString());

            Assert.Equal("A session description", rndTripSdp.SessionDescription);
            Assert.Equal("speech", rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).Single().MediaDescription);
            Assert.Equal("video title", rndTripSdp.Media.Where(x => x.Media == SDPMediaTypesEnum.video).Single().MediaDescription);
        }
    }
}
