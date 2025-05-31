//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionUnitTest.cs
//
// Description: Unit tests for the RTCPeerConnection class.
//
// History:
// 16 Mar 2020	Aaron Clauson	Created.
// 14 Dec 2020  Aaron Clauson   Moved from unit to integration tests (while not 
//              really integration tests the duration is long'ish for a unit test).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.IntegrationTests
{
    [Trait("Category", "integration")]
    public class RTCPeerConnectionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPeerConnectionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that generating the local SDP offer works correctly.
        /// </summary>
        /// <code>
        /// // Javascript equivalent:
        /// let pc = new RTCPeerConnection(null);
        /// const offer = await pc.createOffer();
        /// console.log(offer);
        /// </code>
        [Fact]
        public void GenerateLocalOfferUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPeerConnection pc = new RTCPeerConnection(null);
            var offer = pc.createOffer(new RTCOfferOptions());

            Assert.NotNull(offer);

            logger.LogDebug("{Offer}", offer.ToString());
        }

        /// <summary>
        /// Tests that generating the local SDP offer with an audio track works correctly.
        /// </summary>
        /// <code>
        /// // Javascript equivalent:
        /// const constraints = {'audio': true }
        /// const localStream = await navigator.mediaDevices.getUserMedia({video: false, audio: true});
        /// let pc = new RTCPeerConnection(null);
        /// pc.addTrack(localStream.getTracks()[0]);
        /// const offer = await pc.createOffer();
        /// console.log(offer);
        /// </code>
        [Fact]
        public void GenerateLocalOfferWithAudioTrackUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPeerConnection pc = new RTCPeerConnection(null);
            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
            pc.addTrack(audioTrack);
            var offer = pc.createOffer(new RTCOfferOptions());

            SDP offerSDP = SDP.ParseSDPDescription(offer.sdp);

            Assert.NotNull(offer);
            Assert.NotNull(offer.sdp);
            Assert.Equal(RTCSdpType.offer, offer.type);
            Assert.Single(offerSDP.Media);
            Assert.Contains(offerSDP.Media, x => x.Media == SDPMediaTypesEnum.audio);

            logger.LogDebug("{Offer}", offer.ToString());
        }

        /// <summary>
        /// Checks that the media identifier tags are correctly reused in the generated answer
        /// tracks.
        /// </summary>
        [Fact]
        public void CheckAudioVideoMediaIdentifierTagsAreReusedForAnswerUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // In this SDP, the audio media identifier's tag is "bar" and the video media identifier's tag is "foo"
            string remoteSdp =
            @"v=0
o=- 1064364449942365659 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE bar foo
a=msid-semantic: WMS stream0
m=audio 9 UDP/TLS/RTP/SAVPF 111 103 104 9 102 0 8 106 105 13 110 112 113 126
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:G5P/
a=ice-pwd:FICf2eBzvl5r/O/uf1ktSyuc
a=ice-options:trickle renomination
a=fingerprint:sha-256 5D:03:7C:22:69:2E:E7:10:17:5F:31:86:E6:47:2F:6F:1D:4C:A6:BF:5B:DE:0C:FB:8A:17:15:AA:22:63:0C:FD
a=setup:actpass
a=mid:bar
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=sendrecv
a=rtcp-mux
a=rtpmap:111 opus/48000/2
a=rtcp-fb:111 transport-cc
a=fmtp:111 minptime=10;useinbandfec=1
a=rtpmap:103 ISAC/16000
a=rtpmap:104 ISAC/32000
a=rtpmap:9 G722/8000
a=rtpmap:102 ILBC/8000
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:106 CN/32000
a=rtpmap:105 CN/16000
a=rtpmap:13 CN/8000
a=rtpmap:110 telephone-event/48000
a=rtpmap:112 telephone-event/32000
a=rtpmap:113 telephone-event/16000
a=rtpmap:126 telephone-event/8000
a=ssrc:3780525913 cname:FLLo3gHcblO+MbrR
a=ssrc:3780525913 msid:stream0 audio0
a=ssrc:3780525913 mslabel:stream0
a=ssrc:3780525913 label:audio0
m=video 9 UDP/TLS/RTP/SAVPF 96 97 98 99 100 101 127
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:G5P/
a=ice-pwd:FICf2eBzvl5r/O/uf1ktSyuc
a=ice-options:trickle renomination
a=fingerprint:sha-256 5D:03:7C:22:69:2E:E7:10:17:5F:31:86:E6:47:2F:6F:1D:4C:A6:BF:5B:DE:0C:FB:8A:17:15:AA:22:63:0C:FD
a=setup:actpass
a=mid:foo
a=extmap:14 urn:ietf:params:rtp-hdrext:toffset
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:13 urn:3gpp:video-orientation
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:5 http://www.webrtc.org/experiments/rtp-hdrext/playout-delay
a=extmap:6 http://www.webrtc.org/experiments/rtp-hdrext/video-content-type
a=extmap:7 http://www.webrtc.org/experiments/rtp-hdrext/video-timing
a=extmap:8 http://www.webrtc.org/experiments/rtp-hdrext/color-space
a=sendrecv
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:96 VP8/90000
a=rtcp-fb:96 goog-remb
a=rtcp-fb:96 transport-cc
a=rtcp-fb:96 ccm fir
a=rtcp-fb:96 nack
a=rtcp-fb:96 nack pli
a=rtpmap:97 rtx/90000
a=fmtp:97 apt=96
a=rtpmap:98 VP9/90000
a=rtcp-fb:98 goog-remb
a=rtcp-fb:98 transport-cc
a=rtcp-fb:98 ccm fir
a=rtcp-fb:98 nack
a=rtcp-fb:98 nack pli
a=rtpmap:99 rtx/90000
a=fmtp:99 apt=98
a=rtpmap:100 red/90000
a=rtpmap:101 rtx/90000
a=fmtp:101 apt=100
a=rtpmap:127 ulpfec/90000
a=ssrc-group:FID 3851740345 4165955869
a=ssrc:3851740345 cname:FLLo3gHcblO+MbrR
a=ssrc:3851740345 msid:stream0 video0
a=ssrc:3851740345 mslabel:stream0
a=ssrc:3851740345 label:video0
a=ssrc:4165955869 cname:FLLo3gHcblO+MbrR
a=ssrc:4165955869 msid:stream0 video0
a=ssrc:4165955869 mslabel:stream0
a=ssrc:4165955869 label:video0";

            RTCPeerConnection pc = new RTCPeerConnection(null);
            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
            pc.addTrack(audioTrack);
            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pc.addTrack(localVideoTrack);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {Result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.CreateAnswer(null);
            var answerString = answer.ToString();

            logger.LogDebug("Local answer: {Answer}", answer);

            Assert.Equal("bar", answer.Media[0].MediaID);
            Assert.Equal(SDPMediaTypesEnum.audio, answer.Media[0].Media);
            Assert.Equal("foo", answer.Media[1].MediaID);
            Assert.Equal(SDPMediaTypesEnum.video, answer.Media[1].Media);
            Assert.Contains("a=group:BUNDLE bar foo", answerString);
            Assert.Contains("a=mid:bar", answerString);
            Assert.Contains("a=mid:foo", answerString);

            pc.Close("normal");
        }

        /// <summary>
        /// Checks that the media identifier tags for datachannel (application data) are correctly reused in
        /// the generated answer.
        /// </summary>
        [Fact]
        public void CheckDataChannelMediaIdentifierTagsAreReusedForAnswerUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // In this SDP, the datachannel1's media identifier's tag is "application1"
            string remoteSdp =
            @"v=0
o=- 6803632431644503613 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE application1
a=extmap-allow-mixed
a=msid-semantic: WMS
m=application 9 UDP/DTLS/SCTP webrtc-datachannel
c=IN IP4 0.0.0.0
a=ice-ufrag:xort
a=ice-pwd:6/W7mcRWqCOpmKhfY4a+KK0m
a=ice-options:trickle
a=fingerprint:sha-256 B7:C9:01:0F:B4:BE:00:45:73:4B:F4:52:A9:E7:87:04:72:EB:1A:DC:30:AF:BD:5D:19:BF:12:DE:FF:AF:74:00
a=setup:actpass
a=mid:application1
a=sctp-port:5000
a=max-message-size:262144";

            RTCPeerConnection pc = new RTCPeerConnection(null);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.CreateAnswer(null);
            var answerString = answer.ToString();

            logger.LogDebug("Local answer: {Answer}", answer);

            Assert.Equal("application1", answer.Media[0].MediaID);
            Assert.Equal(SDPMediaTypesEnum.application, answer.Media[0].Media);
            Assert.Contains("a=group:BUNDLE application1", answerString);
            Assert.Contains("a=mid:application1", answerString);

            pc.Close("normal");
        }

        /// <summary>
        /// Checks that datachannel (application data), audio and video are correctly reused in
        /// the generated answer (order is correct)
        /// </summary>
        [Fact]
        public void CheckDataChannelVideoAndAudioAreWellManagedInAnswerUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // In this SDP, the datachannel1's media identifier's tag is "application1"
            string remoteSdp =
            @"v=0
o=- 6660358274987668701 3 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0 1 2
a=extmap-allow-mixed
a=msid-semantic: WMS 03a8cc82-c386-4b9e-9f7d-bb29d00d117d
m=application 61790 UDP/DTLS/SCTP webrtc-datachannel
c=IN IP4 51.195.41.188
a=candidate:2076133753 1 udp 2113937151 7da52ae2-cbda-46a4-a41a-352694bb27cc.local 51273 typ host generation 0 network-cost 999
a=candidate:875361675 1 udp 2113939711 102e0cd0-cecf-4fa5-9282-1ebed8600fe4.local 51274 typ host generation 0 network-cost 999
a=candidate:842163049 1 udp 1677729535 86.127.230.60 51273 typ srflx raddr 0.0.0.0 rport 0 generation 0 network-cost 999
a=candidate:1382836467 1 udp 33562623 51.195.41.188 61790 typ relay raddr 86.127.230.60 rport 51273 generation 0 network-cost 999
a=candidate:2754337611 1 udp 33562367 51.75.71.217 49926 typ relay raddr 86.127.230.60 rport 51273 generation 0 network-cost 999
a=ice-ufrag:fXw4
a=ice-pwd:vb5CJ96xIInblnc6dTFCCpWj
a=ice-options:trickle
a=fingerprint:sha-256 2B:35:E2:AA:C0:D7:A6:A6:3C:A1:C8:CE:30:96:E6:9A:42:44:64:CE:9D:95:F6:22:DC:99:DC:BC:E3:00:B8:9B
a=setup:actpass
a=mid:0
a=sctp-port:5000
a=max-message-size:262144
m=audio 9 RTP/SAVPF 111 101
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:kpML
a=ice-pwd:0Mn2nGBHRfhZ94rSx0LCuuPF
a=ice-options:trickle
a=fingerprint:sha-256 D5:E7:98:7A:3B:B3:BD:72:39:DC:B1:D7:A7:A6:D4:1D:89:AC:82:B0:F5:55:3D:51:24:67:E9:BB:C1:C3:E7:09
a=setup:active
a=sendrecv
a=mid:1
a=rtcp-mux
a=rtpmap:111 opus/48000/2
a=fmtp:111 minptime=10;useinbandfec=1
a=rtpmap:101 telephone-event/8000
a=ssrc:3700183740 cname:yYXyhROknEh4kAox
a=ssrc:3700183740 msid:- 82cbd7f2-f11e-4f36-992e-e64fd28602d0
a=ssrc:3700183740 mslabel:-
a=ssrc:3700183740 label:82cbd7f2-f11e-4f36-992e-e64fd28602d0
m=video 9 UDP/TLS/RTP/SAVPF 96
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:fXw4
a=ice-pwd:vb5CJ96xIInblnc6dTFCCpWj
a=ice-options:trickle
a=fingerprint:sha-256 2B:35:E2:AA:C0:D7:A6:A6:3C:A1:C8:CE:30:96:E6:9A:42:44:64:CE:9D:95:F6:22:DC:99:DC:BC:E3:00:B8:9B
a=setup:actpass
a=mid:2
a=extmap:1 urn:ietf:params:rtp-hdrext:toffset
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:3 urn:3gpp:video-orientation
a=extmap:4 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:5 http://www.webrtc.org/experiments/rtp-hdrext/playout-delay
a=extmap:6 http://www.webrtc.org/experiments/rtp-hdrext/video-content-type
a=extmap:7 http://www.webrtc.org/experiments/rtp-hdrext/video-timing
a=extmap:8 http://www.webrtc.org/experiments/rtp-hdrext/color-space
a=extmap:9 urn:ietf:params:rtp-hdrext:sdes:mid
a=extmap:10 urn:ietf:params:rtp-hdrext:sdes:rtp-stream-id
a=extmap:11 urn:ietf:params:rtp-hdrext:sdes:repaired-rtp-stream-id
a=sendrecv
a=msid:03a8cc82-c386-4b9e-9f7d-bb29d00d117d 5b06be39-0752-497f-80f5-6cf3db665f14
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:96 VP8/90000
a=rtcp-fb:96 goog-remb
a=rtcp-fb:96 transport-cc
a=rtcp-fb:96 ccm fir
a=rtcp-fb:96 nack
a=rtcp-fb:96 nack pli
a=ssrc-group:FID 16371961 1091343449
a=ssrc:16371961 cname:DE2fEykfrhlS8lo5
a=ssrc:16371961 msid:03a8cc82-c386-4b9e-9f7d-bb29d00d117d 5b06be39-0752-497f-80f5-6cf3db665f14
a=ssrc:16371961 mslabel:03a8cc82-c386-4b9e-9f7d-bb29d00d117d
a=ssrc:16371961 label:5b06be39-0752-497f-80f5-6cf3db665f14
a=ssrc:1091343449 cname:DE2fEykfrhlS8lo5
a=ssrc:1091343449 msid:03a8cc82-c386-4b9e-9f7d-bb29d00d117d 5b06be39-0752-497f-80f5-6cf3db665f14
a=ssrc:1091343449 mslabel:03a8cc82-c386-4b9e-9f7d-bb29d00d117d
a=ssrc:1091343449 label:5b06be39-0752-497f-80f5-6cf3db665f14";

            RTCPeerConnection pc = new RTCPeerConnection(null);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.CreateAnswer(null);
            var answerString = answer.ToString();

            logger.LogDebug("Local answer: {Answer}", answer);

            // Check DataChannel
            Assert.Equal("0", answer.Media[0].MediaID);
            Assert.Equal(SDPMediaTypesEnum.application, answer.Media[0].Media);

            // Check Audio
            Assert.Equal("1", answer.Media[1].MediaID);
            Assert.Equal(SDPMediaTypesEnum.audio, answer.Media[1].Media);

            // Check Video
            Assert.Equal("2", answer.Media[2].MediaID);
            Assert.Equal(SDPMediaTypesEnum.video, answer.Media[2].Media);

            Assert.Contains("a=group:BUNDLE 0 1 2", answerString);
            
            Assert.Contains("a=mid:0", answerString);
            Assert.Contains("a=mid:1", answerString);
            Assert.Contains("a=mid:2", answerString);

            pc.Close("normal");
        }

        /// <summary>
        /// Checks that the media identifier tags in the generated answer are in the same order as in the 
        /// received offer.
        /// </summary>
        [Fact]
        public void CheckMediaIdentifierTagOrderRemainsForAnswerUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // In this SDP, the audio media identifier's tag is "zzz" and the video media identifier's tag is "aaa".
            // Such tag are meant to ensure that we do not sort sdp's media tracks by alphabetical order.
            string remoteSdp =
            @"v=0
o=- 1064364449942365659 2 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE zzz aaa 
a=msid-semantic: WMS stream0
m=audio 9 UDP/TLS/RTP/SAVPF 111 103 104 9 102 0 8 106 105 13 110 112 113 126
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:G5P/
a=ice-pwd:FICf2eBzvl5r/O/uf1ktSyuc
a=ice-options:trickle renomination
a=fingerprint:sha-256 5D:03:7C:22:69:2E:E7:10:17:5F:31:86:E6:47:2F:6F:1D:4C:A6:BF:5B:DE:0C:FB:8A:17:15:AA:22:63:0C:FD
a=setup:actpass
a=mid:zzz
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=sendrecv
a=rtcp-mux
a=rtpmap:111 opus/48000/2
a=rtcp-fb:111 transport-cc
a=fmtp:111 minptime=10;useinbandfec=1
a=rtpmap:103 ISAC/16000
a=rtpmap:104 ISAC/32000
a=rtpmap:9 G722/8000
a=rtpmap:102 ILBC/8000
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:106 CN/32000
a=rtpmap:105 CN/16000
a=rtpmap:13 CN/8000
a=rtpmap:110 telephone-event/48000
a=rtpmap:112 telephone-event/32000
a=rtpmap:113 telephone-event/16000
a=rtpmap:126 telephone-event/8000
a=ssrc:3780525913 cname:FLLo3gHcblO+MbrR
a=ssrc:3780525913 msid:stream0 audio0
a=ssrc:3780525913 mslabel:stream0
a=ssrc:3780525913 label:audio0
m=video 9 UDP/TLS/RTP/SAVPF 96 97 98 99 100 101 127
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:G5P/
a=ice-pwd:FICf2eBzvl5r/O/uf1ktSyuc
a=ice-options:trickle renomination
a=fingerprint:sha-256 5D:03:7C:22:69:2E:E7:10:17:5F:31:86:E6:47:2F:6F:1D:4C:A6:BF:5B:DE:0C:FB:8A:17:15:AA:22:63:0C:FD
a=setup:actpass
a=mid:aaa
a=extmap:14 urn:ietf:params:rtp-hdrext:toffset
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:13 urn:3gpp:video-orientation
a=extmap:3 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:5 http://www.webrtc.org/experiments/rtp-hdrext/playout-delay
a=extmap:6 http://www.webrtc.org/experiments/rtp-hdrext/video-content-type
a=extmap:7 http://www.webrtc.org/experiments/rtp-hdrext/video-timing
a=extmap:8 http://www.webrtc.org/experiments/rtp-hdrext/color-space
a=sendrecv
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:96 VP8/90000
a=rtcp-fb:96 goog-remb
a=rtcp-fb:96 transport-cc
a=rtcp-fb:96 ccm fir
a=rtcp-fb:96 nack
a=rtcp-fb:96 nack pli
a=rtpmap:97 rtx/90000
a=fmtp:97 apt=96
a=rtpmap:98 VP9/90000
a=rtcp-fb:98 goog-remb
a=rtcp-fb:98 transport-cc
a=rtcp-fb:98 ccm fir
a=rtcp-fb:98 nack
a=rtcp-fb:98 nack pli
a=rtpmap:99 rtx/90000
a=fmtp:99 apt=98
a=rtpmap:100 red/90000
a=rtpmap:101 rtx/90000
a=fmtp:101 apt=100
a=rtpmap:127 ulpfec/90000
a=ssrc-group:FID 3851740345 4165955869
a=ssrc:3851740345 cname:FLLo3gHcblO+MbrR
a=ssrc:3851740345 msid:stream0 video0
a=ssrc:3851740345 mslabel:stream0
a=ssrc:3851740345 label:video0
a=ssrc:4165955869 cname:FLLo3gHcblO+MbrR
a=ssrc:4165955869 msid:stream0 video0
a=ssrc:4165955869 mslabel:stream0
a=ssrc:4165955869 label:video0";

            RTCPeerConnection pc = new RTCPeerConnection(null);
            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
            pc.addTrack(audioTrack);
            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pc.addTrack(localVideoTrack);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.CreateAnswer(null);
            var answerString = answer.ToString();

            logger.LogDebug("Local answer: {Answer}", answer);

            Assert.Equal("zzz", answer.Media[0].MediaID);
            Assert.Equal(SDPMediaTypesEnum.audio, answer.Media[0].Media);
            Assert.Equal("aaa", answer.Media[1].MediaID);
            Assert.Equal(SDPMediaTypesEnum.video, answer.Media[1].Media);
            Assert.Contains("a=group:BUNDLE zzz aaa", answerString);
            Assert.Contains("a=mid:zzz", answerString);
            Assert.Contains("a=mid:aaa", answerString);

            pc.Close("normal");
        }

        /// <summary>
        /// Tests that attempting to send an RTCP feedback report for an audio stream works correctly.
        /// </summary>
        [Fact]
        public void SendVideoRtcpFeedbackReportUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                X_UseRtpFeedbackProfile = true
            };

            RTCPeerConnection pcSrc = new RTCPeerConnection(pcConfiguration);
            var videoTrackSrc = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pcSrc.addTrack(videoTrackSrc);
            var offer = pcSrc.createOffer(new RTCOfferOptions());

            logger.LogDebug("offer: {OfferSdp}", offer.sdp);

            RTCPeerConnection pcDst = new RTCPeerConnection(pcConfiguration);
            var videoTrackDst = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pcDst.addTrack(videoTrackDst);

            var setOfferResult = pcDst.setRemoteDescription(offer);
            Assert.Equal(SetDescriptionResultEnum.OK, setOfferResult);

            var answer = pcDst.createAnswer(null);
            var setAnswerResult = pcSrc.setRemoteDescription(answer);
            Assert.Equal(SetDescriptionResultEnum.OK, setAnswerResult);

            logger.LogDebug("answer: {AnswerSdp}", answer.sdp);

            RTCPFeedback pliReport = new RTCPFeedback(pcDst.VideoStream.LocalTrack.Ssrc, pcDst.VideoStream.RemoteTrack.Ssrc, PSFBFeedbackTypesEnum.PLI);
            pcDst.SendRtcpFeedback(SDPMediaTypesEnum.video, pliReport);
        }

        /// <summary>
        /// Checks that the media formats are correctly negotiated when for a remote offer and the local
        /// tracks.
        /// </summary>
        [Fact]
        public void CheckMediaFormatNegotiationUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // By default offers made by us always put audio first. Create a remote SDP offer 
            // with the video first.
            string remoteSdp =
            @"v=0
o=- 62533 0 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0 1
a=msid-semantic: WMS
m=audio 57148 UDP/TLS/RTP/SAVP 0 101
c=IN IP6 2a02:8084:6981:7880::76
a=rtcp:9 IN IP4 0.0.0.0
a=candidate:2944 1 udp 659136 192.168.11.50 57148 typ host generation 0
a=candidate:2488 1 udp 659136 192.168.0.50 57148 typ host generation 0
a=candidate:2507 1 udp 659136 fe80::54a9:d238:b2ee:ceb%24 57148 typ host generation 0
a=candidate:3159 1 udp 659136 2a02:8084:6981:7880::76 57148 typ host generation 0
a=ice-ufrag:CUTK
a=ice-pwd:QTCZWDIEBCIBGOYAGSIXRFIL
a=ice-options:ice2,trickle
a=fingerprint:sha-256 06:2F:61:85:1F:83:64:88:1B:93:93:8C:E5:FF:1C:D9:82:EA:60:97:1E:0D:DA:FA:28:11:00:FA:74:69:23:DB
a=setup:actpass
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:0 PCMU/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-16
m=video 9 UDP/TLS/RTP/SAVP 100
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:CUTK
a=ice-pwd:QTCZWDIEBCIBGOYAGSIXRFIL
a=ice-options:ice2,trickle
a=fingerprint:sha-256 06:2F:61:85:1F:83:64:88:1B:93:93:8C:E5:FF:1C:D9:82:EA:60:97:1E:0D:DA:FA:28:11:00:FA:74:69:23:DB
a=setup:actpass
a=mid:1
a=sendrecv
a=rtcp-mux
a=rtpmap:100 VP8/90000";

            // Create a local session and add the video track first.
            RTCPeerConnection pc = new RTCPeerConnection(null);
            MediaStreamTrack localAudioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> {
                new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 110, "OPUS/48000/2")
            });
            pc.addTrack(localAudioTrack);
            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pc.addTrack(localVideoTrack);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.createAnswer();

            logger.LogDebug("Local answer: {Answer}", answer);

            Assert.Equal(2, pc.AudioStream.LocalTrack.Capabilities.Count());
            Assert.Equal(0, pc.AudioStream.LocalTrack.Capabilities.Single(x => x.Name() == "PCMU").ID);
            Assert.Equal(100, pc.VideoStream.LocalTrack.Capabilities.Single(x => x.Name() == "VP8").ID);

            pc.Close("normal");
        }

        /// <summary>
        /// Checks that an inactive audio track gets added if the offer contains audio and video but
        /// the local peer connection only supports video.
        /// </summary>
        [Fact]
        public void CheckNoAudioNegotiationUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // By default offers made by us always put audio first. Create a remote SDP offer 
            // with the video first.
            string remoteSdp =
            @"v=0
o=- 62533 0 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0 1
a=msid-semantic: WMS
m=audio 57148 UDP/TLS/RTP/SAVP 0 101
c=IN IP6 2a02:8084:6981:7880::76
a=rtcp:9 IN IP4 0.0.0.0
a=candidate:2944 1 udp 659136 192.168.11.50 57148 typ host generation 0
a=candidate:2488 1 udp 659136 192.168.0.50 57148 typ host generation 0
a=candidate:2507 1 udp 659136 fe80::54a9:d238:b2ee:ceb%24 57148 typ host generation 0
a=candidate:3159 1 udp 659136 2a02:8084:6981:7880::76 57148 typ host generation 0
a=ice-ufrag:CUTK
a=ice-pwd:QTCZWDIEBCIBGOYAGSIXRFIL
a=ice-options:ice2,trickle
a=fingerprint:sha-256 06:2F:61:85:1F:83:64:88:1B:93:93:8C:E5:FF:1C:D9:82:EA:60:97:1E:0D:DA:FA:28:11:00:FA:74:69:23:DB
a=setup:actpass
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:0 PCMU/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-16
m=video 9 UDP/TLS/RTP/SAVP 100
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:CUTK
a=ice-pwd:QTCZWDIEBCIBGOYAGSIXRFIL
a=ice-options:ice2,trickle
a=fingerprint:sha-256 06:2F:61:85:1F:83:64:88:1B:93:93:8C:E5:FF:1C:D9:82:EA:60:97:1E:0D:DA:FA:28:11:00:FA:74:69:23:DB
a=setup:actpass
a=mid:1
a=sendrecv
a=rtcp-mux
a=rtpmap:100 VP8/90000";

            // Create a local session and add the video track first.
            RTCPeerConnection pc = new RTCPeerConnection(null);
            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pc.addTrack(localVideoTrack);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.CreateAnswer(null);

            logger.LogDebug("Local answer: {Answer}", answer);

            Assert.Equal(MediaStreamStatusEnum.Inactive, pc.AudioStream.LocalTrack.StreamStatus);
            Assert.Equal(100, pc.VideoStream.LocalTrack.Capabilities.Single(x => x.Name() == "VP8").ID);

            pc.Close("normal");
        }

        /// <summary>
        /// Checks that an inactive audio track gets added if the offer contains inactive audio and sendrecv video but
        /// the local peer connection only supports video.
        /// </summary>
        [Fact]
        public void Check_Inactive_Audio_Negotiation_Test()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // By default offers made by us always put audio first. Create a remote SDP offer 
            // with the video first.
            string remoteSdp =
            @"v=0
o=- 62533 0 IN IP4 127.0.0.1
s=-
t=0 0
a=group:BUNDLE 0 1
a=msid-semantic: WMS
m=video 9 UDP/TLS/RTP/SAVPF 96 97
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:Hvje
a=ice-pwd:CXdPuoviwBPUGkw1PystrRs1
a=ice-options:trickle
a=fingerprint:sha-256 D6:82:3F:4F:23:A4:09:5A:BC:99:42:7D:E6:94:D8:2F:41:56:CF:01:14:35:1A:61:7B:95:C8:F4:FC:D5:3A:16
a=setup:actpass
a=mid:0
a=extmap:1 urn:ietf:params:rtp-hdrext:toffset
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:3 urn:3gpp:video-orientation
a=extmap:4 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:5 http://www.webrtc.org/experiments/rtp-hdrext/playout-delay
a=extmap:6 http://www.webrtc.org/experiments/rtp-hdrext/video-content-type
a=extmap:7 http://www.webrtc.org/experiments/rtp-hdrext/video-timing
a=extmap:8 http://www.webrtc.org/experiments/rtp-hdrext/color-space
a=extmap:9 urn:ietf:params:rtp-hdrext:sdes:mid
a=extmap:10 urn:ietf:params:rtp-hdrext:sdes:rtp-stream-id
a=extmap:11 urn:ietf:params:rtp-hdrext:sdes:repaired-rtp-stream-id
a=recvonly
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:96 VP8/90000
a=rtcp-fb:96 goog-remb
a=rtcp-fb:96 transport-cc
a=rtcp-fb:96 ccm fir
a=rtcp-fb:96 nack
a=rtcp-fb:96 nack pli
a=rtpmap:97 rtx/90000
a=fmtp:97 apt=96
m=audio 9 UDP/TLS/RTP/SAVPF 111 63 9 0 8 13 110 126
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:Hvje
a=ice-pwd:CXdPuoviwBPUGkw1PystrRs1
a=ice-options:trickle
a=fingerprint:sha-256 D6:82:3F:4F:23:A4:09:5A:BC:99:42:7D:E6:94:D8:2F:41:56:CF:01:14:35:1A:61:7B:95:C8:F4:FC:D5:3A:16
a=setup:actpass
a=mid:1
a=extmap:14 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=extmap:2 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:4 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=extmap:9 urn:ietf:params:rtp-hdrext:sdes:mid
a=inactive
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:111 opus/48000/2
a=rtcp-fb:111 transport-cc
a=fmtp:111 minptime=10;useinbandfec=1
a=rtpmap:63 red/48000/2
a=fmtp:63 111/111
a=rtpmap:9 G722/8000
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:13 CN/8000
a=rtpmap:110 telephone-event/48000
a=rtpmap:126 telephone-event/8000";

            // Create a local session and add the video track first.
            RTCPeerConnection pc = new RTCPeerConnection(null);
            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pc.addTrack(localVideoTrack);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.CreateAnswer(null);

            logger.LogDebug("Local answer: {Answer}", answer);

            Assert.Equal(MediaStreamStatusEnum.Inactive, pc.AudioStream.LocalTrack.StreamStatus);
            Assert.Equal(96, pc.VideoStream.LocalTrack.Capabilities.Single(x => x.Name() == "VP8").ID);

            pc.Close("normal");
        }

        /// <summary>
        /// Tests that two peer connection instances can reach the connected state.
        /// </summary>
        [Fact]
        public async Task CheckPeerConnectionEstablishment()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var aliceConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bobConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var alice = new RTCPeerConnection();
            alice.onconnectionstatechange += (state) =>
            {
                if (state == RTCPeerConnectionState.connected)
                {
                    logger.LogDebug("Alice connected.");
                    aliceConnected.SetResult(true);
                }
            };
            alice.addTrack(new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU));
            var aliceOffer = alice.createOffer();
            await alice.setLocalDescription(aliceOffer);

            logger.LogDebug("alice offer: {Sdp}", aliceOffer.sdp);

            var bob = new RTCPeerConnection();
            bob.onconnectionstatechange += (state) =>
            {
                if (state == RTCPeerConnectionState.connected)
                {
                    logger.LogDebug("Bob connected.");
                    bobConnected.SetResult(true);
                }
            };
            bob.addTrack(new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU));

            var setOfferResult = bob.setRemoteDescription(aliceOffer);
            Assert.Equal(SetDescriptionResultEnum.OK, setOfferResult);

            var bobAnswer = bob.createAnswer();
            await bob.setLocalDescription(bobAnswer);
            var setAnswerResult = alice.setRemoteDescription(bobAnswer);
            Assert.Equal(SetDescriptionResultEnum.OK, setAnswerResult);

            logger.LogDebug("answer: {BobAnswerSdp}", bobAnswer.sdp);

            await Task.WhenAny(Task.WhenAll(aliceConnected.Task, bobConnected.Task), Task.Delay(2000));

            Assert.True(aliceConnected.Task.IsCompleted);
            Assert.True(await aliceConnected.Task);
            Assert.True(bobConnected.Task.IsCompleted);
            Assert.True(await bobConnected.Task);

            bob.close();
            alice.close();
        }

        /// <summary>
        /// Tests that two peer connection instances can establish a data channel.
        /// </summary>
        [Fact]
        public async Task CheckDataChannelEstablishment()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var aliceDataConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bobDataOpened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var alice = new RTCPeerConnection();
            var dc = await alice.createDataChannel("dc1", null);
            dc.onopen += () => aliceDataConnected.TrySetResult(true);
            var aliceOffer = alice.createOffer();
            await alice.setLocalDescription(aliceOffer);

            logger.LogDebug("alice offer: {Sdp}", aliceOffer.sdp);

            var bob = new RTCPeerConnection();
            RTCDataChannel bobData = null;
            bob.ondatachannel += (chan) =>
            {
                bobData = chan;
                bobDataOpened.TrySetResult(true);
            };

            var setOfferResult = bob.setRemoteDescription(aliceOffer);
            Assert.Equal(SetDescriptionResultEnum.OK, setOfferResult);

            var bobAnswer = bob.createAnswer();
            await bob.setLocalDescription(bobAnswer);
            var setAnswerResult = alice.setRemoteDescription(bobAnswer);
            Assert.Equal(SetDescriptionResultEnum.OK, setAnswerResult);

            logger.LogDebug("answer: {BobAnswerSdp}", bobAnswer.sdp);

            await Task.WhenAny(Task.WhenAll(aliceDataConnected.Task, bobDataOpened.Task), Task.Delay(2000));

            Assert.True(aliceDataConnected.Task.IsCompleted);
            Assert.True(await aliceDataConnected.Task);
            Assert.True(bobDataOpened.Task.IsCompleted);
            Assert.True(await bobDataOpened.Task);
            Assert.True(dc.IsOpened);
            Assert.True(bobData.IsOpened);

            bob.close();
            alice.close();
        }

        /// <summary>
        /// Checks that the correct answer is generated for an SDP offer from GStreamer.
        /// </summary>
        [Fact]
        public void CheckAnswerForGStreamerOfferUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Remote offer from GStreamer, see https://github.com/sipsorcery-org/sipsorcery/issues/596.
            string remoteSdp =
            @"v=0
o=- 4385423089851900022 0 IN IP4 0.0.0.0
s=-
t=0 0
a=ice-options:trickle
a=group:BUNDLE video0 application1
m=video 9 UDP/TLS/RTP/SAVPF 96
c=IN IP4 0.0.0.0
a=setup:actpass
a=ice-ufrag:lsJx+7d6hsCyL8K6m8/KbgcqMqizaZqy
a=ice-pwd:zFUTJmx6hNnr/JRAq2b3wOtmm88XERb3
a=rtcp-mux
a=rtcp-rsize
a=sendrecv
a=rtpmap:96 H264/90000
a=rtcp-fb:96 nack pli
a=framerate:30
a=fmtp:96 packetization-mode=1;profile-level-id=42E01F;sprop-parameter-sets=Z00AKeKQDwBE/LNwEBAaUABt3QAZv8wA8SIq,aO48gA==
a=ssrc:3776670536 msid:user3344942761@host-c94b5db webrtctransceiver11
a=ssrc:3776670536 cname:user3344942761@host-c94b5db
a=mid:video0
a=fingerprint:sha-256 AE:1C:59:19:00:7B:C2:1C:85:95:0C:6C:8C:14:E8:67:A4:7D:D0:AE:90:5D:8F:BB:D7:5B:95:49:03:6E:94:8F
m=application 0 UDP/DTLS/SCTP webrtc-datachannel
c=IN IP4 0.0.0.0
a=setup:actpass
a=ice-ufrag:lsJx+7d6hsCyL8K6m8/KbgcqMqizaZqy
a=ice-pwd:zFUTJmx6hNnr/JRAq2b3wOtmm88XERb3
a=bundle-only
a=mid:application1
a=sctp-port:5000
a=fingerprint:sha-256 AE:1C:59:19:00:7B:C2:1C:85:95:0C:6C:8C:14:E8:67:A4:7D:D0:AE:90:5D:8F:BB:D7:5B:95:49:03:6E:94:8F";

            // Create a local session and add the video track first.
            RTCPeerConnection pc = new RTCPeerConnection(null);
            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 100, "H264", 90000) });
            pc.addTrack(localVideoTrack);

            var offer = SDP.ParseSDPDescription(remoteSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.createAnswer();

            logger.LogDebug("Local answer: {Answer}", answer);

            Assert.NotNull(pc.VideoStream.LocalTrack);
            Assert.Equal(96, pc.VideoStream.LocalTrack.Capabilities.Single(x => x.Name() == "H264").ID);
            Assert.Equal(IceRolesEnum.active, pc.IceRole);

            pc.Close("normal");
        }
        
        /// <summary>
        ///  https://github.com/sipsorcery-org/sipsorcery/issues/1093
        /// </summary>
        [Fact]
        public void AnswerShouldNotContainAbsSendTimeIfOfferDidNot()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            string offerSdp =
                @"v=0
o=mozilla...THIS_IS_SDPARTA-99.0 1197589987011925003 0 IN IP4 0.0.0.0
s=-
t=0 0
a=fingerprint:sha-256 C8:44:FD:0C:0B:CF:3B:89:FF:0A:8E:B8:14:95:FA:88:AC:C2:D0:AA:3B:BF:89:9C:D8:44:2D:01:EE:8D:A9:23
a=group:BUNDLE 0 1 2
a=ice-options:trickle
a=msid-semantic:WMS *
m=audio 9 UDP/TLS/RTP/SAVPF 109 9 0 8 101
c=IN IP4 0.0.0.0
a=recvonly
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level
a=extmap:2/recvonly urn:ietf:params:rtp-hdrext:csrc-audio-level
a=extmap:3 urn:ietf:params:rtp-hdrext:sdes:mid
a=fmtp:109 maxplaybackrate=48000;stereo=1;useinbandfec=1
a=fmtp:101 0-15
a=ice-pwd:6f1379310e8465d2cf74fc662461e8a5
a=ice-ufrag:fed34101
a=mid:0
a=rtcp-mux
a=rtpmap:109 opus/48000/2
a=rtpmap:9 G722/8000/1
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=rtpmap:101 telephone-event/8000/1
a=setup:actpass
a=ssrc:3465053168 cname:{d9cfdcd2-1130-487c-ab95-b81a583d4d7c}
m=video 9 UDP/TLS/RTP/SAVPF 120 124 121 125 126 127 97 98 123 122 119
c=IN IP4 0.0.0.0
a=recvonly
a=extmap:3 urn:ietf:params:rtp-hdrext:sdes:mid
a=extmap:4 http://www.webrtc.org/experiments/rtp-hdrext/abs-send-time
a=extmap:5 urn:ietf:params:rtp-hdrext:toffset
a=extmap:6/recvonly http://www.webrtc.org/experiments/rtp-hdrext/playout-delay
a=extmap:7 http://www.ietf.org/id/draft-holmer-rmcat-transport-wide-cc-extensions-01
a=fmtp:126 profile-level-id=42e01f;level-asymmetry-allowed=1;packetization-mode=1
a=fmtp:97 profile-level-id=42e01f;level-asymmetry-allowed=1
a=fmtp:120 max-fs=12288;max-fr=60
a=fmtp:124 apt=120
a=fmtp:121 max-fs=12288;max-fr=60
a=fmtp:125 apt=121
a=fmtp:127 apt=126
a=fmtp:98 apt=97
a=fmtp:119 apt=122
a=ice-pwd:6f1379310e8465d2cf74fc662461e8a5
a=ice-ufrag:fed34101
a=mid:1
a=rtcp-fb:120 nack
a=rtcp-fb:120 nack pli
a=rtcp-fb:120 ccm fir
a=rtcp-fb:120 goog-remb
a=rtcp-fb:120 transport-cc
a=rtcp-fb:121 nack
a=rtcp-fb:121 nack pli
a=rtcp-fb:121 ccm fir
a=rtcp-fb:121 goog-remb
a=rtcp-fb:121 transport-cc
a=rtcp-fb:126 nack
a=rtcp-fb:126 nack pli
a=rtcp-fb:126 ccm fir
a=rtcp-fb:126 goog-remb
a=rtcp-fb:126 transport-cc
a=rtcp-fb:97 nack
a=rtcp-fb:97 nack pli
a=rtcp-fb:97 ccm fir
a=rtcp-fb:97 goog-remb
a=rtcp-fb:97 transport-cc
a=rtcp-fb:123 nack
a=rtcp-fb:123 nack pli
a=rtcp-fb:123 ccm fir
a=rtcp-fb:123 goog-remb
a=rtcp-fb:123 transport-cc
a=rtcp-fb:122 nack
a=rtcp-fb:122 nack pli
a=rtcp-fb:122 ccm fir
a=rtcp-fb:122 goog-remb
a=rtcp-fb:122 transport-cc
a=rtcp-mux
a=rtcp-rsize
a=rtpmap:120 VP8/90000
a=rtpmap:124 rtx/90000
a=rtpmap:121 VP9/90000
a=rtpmap:125 rtx/90000
a=rtpmap:126 H264/90000
a=rtpmap:127 rtx/90000
a=rtpmap:97 H264/90000
a=rtpmap:98 rtx/90000
a=rtpmap:123 ulpfec/90000
a=rtpmap:122 red/90000
a=rtpmap:119 rtx/90000
a=setup:actpass
a=ssrc:146552387 cname:{d9cfdcd2-1130-487c-ab95-b81a583d4d7c}
m=application 9 UDP/DTLS/SCTP webrtc-datachannel
c=IN IP4 0.0.0.0
a=sendrecv
a=ice-pwd:6f1379310e8465d2cf74fc662461e8a5
a=ice-ufrag:fed34101
a=mid:2
a=setup:actpass
a=sctp-port:5000
a=max-message-size:1073741823";

            int extensionId = 1;
            var audioExtensions = new Dictionary<int, RTPHeaderExtension>();
            audioExtensions.Add(extensionId, RTPHeaderExtension.GetRTPHeaderExtension(extensionId, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI, SDPMediaTypesEnum.audio));

            RTCPeerConnection pc = new RTCPeerConnection(null);
            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, headerExtensions: audioExtensions);
            pc.addTrack(audioTrack);

            var videoExtensions = new Dictionary<int, RTPHeaderExtension>();
            videoExtensions.Add(extensionId, RTPHeaderExtension.GetRTPHeaderExtension(extensionId, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI, SDPMediaTypesEnum.video));

            MediaStreamTrack localVideoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) }, headerExtensions: videoExtensions);
            pc.addTrack(localVideoTrack);

            var offer = SDP.ParseSDPDescription(offerSdp);

            logger.LogDebug("Remote offer: {RemoteOffer}", offer);

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug("Set remote description on local session result {result}.", result);

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.CreateAnswer(null);
            var answerString = answer.ToString();

            logger.LogDebug("Local answer: {AnswerString}", answerString);

            logger.LogDebug("First media shouldn't have abs-send-time");
            Assert.DoesNotContain(answer.Media[0].HeaderExtensions, 
                ext => ext.Value.Uri == AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI);

            logger.LogDebug("Second media should have abs-send-time");
            Assert.Contains(answer.Media[1].HeaderExtensions, 
                ext => ext.Value.Uri == AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI);

            // 4 is the ID for abs-send-time ext in offer
            Assert.Equal(4, answer.Media[1].HeaderExtensions[4].Id);
            Assert.Equal(AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI, answer.Media[1].HeaderExtensions[4].Uri);

            pc.Close("normal");
        }
    }
}
