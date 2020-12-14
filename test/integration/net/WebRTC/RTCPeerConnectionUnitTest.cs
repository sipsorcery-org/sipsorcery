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
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions.V1;
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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPeerConnection pc = new RTCPeerConnection(null);
            var offer = pc.createOffer(new RTCOfferOptions());

            Assert.NotNull(offer);

            logger.LogDebug(offer.ToString());
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
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
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

            logger.LogDebug(offer.sdp);
        }

        /// <summary>
        /// Tests that attempting to send an RTCP feedback report for an audio stream works correctly.
        /// </summary>
        [Fact]
        public void SendVideoRtcpFeedbackReportUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCConfiguration pcConfiguration = new RTCConfiguration
            {
                X_UseRtpFeedbackProfile = true
            };

            RTCPeerConnection pcSrc = new RTCPeerConnection(pcConfiguration);
            var videoTrackSrc = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pcSrc.addTrack(videoTrackSrc);
            var offer = pcSrc.createOffer(new RTCOfferOptions());

            logger.LogDebug($"offer: {offer.sdp}");

            RTCPeerConnection pcDst = new RTCPeerConnection(pcConfiguration);
            var videoTrackDst = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000) });
            pcDst.addTrack(videoTrackDst);

            var setOfferResult = pcDst.setRemoteDescription(offer);
            Assert.Equal(SetDescriptionResultEnum.OK, setOfferResult);

            var answer = pcDst.createAnswer(null);
            var setAnswerResult = pcSrc.setRemoteDescription(answer);
            Assert.Equal(SetDescriptionResultEnum.OK, setAnswerResult);

            logger.LogDebug($"answer: {answer.sdp}");

            RTCPFeedback pliReport = new RTCPFeedback(pcDst.VideoLocalTrack.Ssrc, pcDst.VideoRemoteTrack.Ssrc, PSFBFeedbackTypesEnum.PLI);
            pcDst.SendRtcpFeedback(SDPMediaTypesEnum.video, pliReport);
        }

        /// <summary>
        /// Checks that the media formats are correctly negotiated when for a remote offer and the local
        /// tracks.
        /// </summary>
        [Fact]
        public void CheckMediaFormatNegotiationUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
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

            logger.LogDebug($"Remote offer: {offer}");

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer =pc.CreateAnswer(null);

            logger.LogDebug($"Local answer: {answer}");

            Assert.Equal(2, pc.AudioLocalTrack.Capabilities.Count());
            Assert.Equal(0, pc.AudioLocalTrack.Capabilities.Single(x => x.Name() == "PCMU").ID);
            Assert.Equal(100, pc.VideoLocalTrack.Capabilities.Single(x => x.Name() == "VP8").ID);

            pc.Close("normal");
        }

        /// <summary>
        /// Checks that an inactive audio track gets added if the offer contains audio and video but
        /// the local peer connection only supports video.
        /// </summary>
        [Fact]
        public void CheckNoAudioNegotiationUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
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

            logger.LogDebug($"Remote offer: {offer}");

            var result = pc.SetRemoteDescription(SIP.App.SdpType.offer, offer);

            logger.LogDebug($"Set remote description on local session result {result}.");

            Assert.Equal(SetDescriptionResultEnum.OK, result);

            var answer = pc.CreateAnswer(null);

            logger.LogDebug($"Local answer: {answer}");

            Assert.Equal(MediaStreamStatusEnum.Inactive, pc.AudioLocalTrack.StreamStatus);
            Assert.Equal(100, pc.VideoLocalTrack.Capabilities.Single(x => x.Name() == "VP8").ID);

            pc.Close("normal");
        }
    }
}
