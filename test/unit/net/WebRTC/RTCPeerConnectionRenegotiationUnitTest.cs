//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionRenegotiationUnitTest.cs
//
// Description: Unit tests for WebRTC SDP renegotiation — adding tracks after
// the initial offer/answer exchange. Covers m-line ordering preservation
// (RFC 3264 §8), new media type inclusion, and ICE endpoint stability.
//
// History:
// 08 May 2026  Contributors    Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionRenegotiationUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPeerConnectionRenegotiationUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// When a video-only offer/answer completes and an audio track is added
        /// afterwards, the renegotiation offer must contain both m-lines with
        /// video preserving its original index (mid:0) and audio appended (mid:1).
        /// </summary>
        [Fact]
        public void RenegotiationOfferPreservesVideoMLineAndAppendsAudio()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // --- Offerer: start with video only ---
            RTCPeerConnection offerer = new RTCPeerConnection(null);
            var videoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            offerer.addTrack(videoTrack);

            // Initial offer — video only.
            var initialOffer = offerer.createOffer(new RTCOfferOptions());
            Assert.NotNull(initialOffer?.sdp);
            var initialOfferSdp = SDP.ParseSDPDescription(initialOffer.sdp);
            Assert.Single(initialOfferSdp.Media);
            Assert.Equal(SDPMediaTypesEnum.video, initialOfferSdp.Media[0].Media);

            logger.LogDebug("Initial offer SDP:\n{Sdp}", initialOffer.sdp);

            // --- Answerer: accept video ---
            RTCPeerConnection answerer = new RTCPeerConnection(null);
            var answerVideoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            answerer.addTrack(answerVideoTrack);

            var setResult = answerer.setRemoteDescription(initialOffer);
            Assert.Equal(SetDescriptionResultEnum.OK, setResult);

            var answer = answerer.createAnswer();
            Assert.NotNull(answer?.sdp);

            logger.LogDebug("Answer SDP:\n{Sdp}", answer.sdp);

            // Offerer processes the answer.
            setResult = offerer.setRemoteDescription(answer);
            Assert.Equal(SetDescriptionResultEnum.OK, setResult);

            // --- Add audio track after initial handshake ---
            var audioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                });
            offerer.addTrack(audioTrack);

            // Renegotiation offer — must have both video and audio.
            var renegOffer = offerer.createOffer(new RTCOfferOptions());
            Assert.NotNull(renegOffer?.sdp);
            var renegOfferSdp = SDP.ParseSDPDescription(renegOffer.sdp);

            logger.LogDebug("Renegotiation offer SDP:\n{Sdp}", renegOffer.sdp);

            Assert.Equal(2, renegOfferSdp.Media.Count);
            Assert.Equal(SDPMediaTypesEnum.video, renegOfferSdp.Media[0].Media);
            Assert.Equal(SDPMediaTypesEnum.audio, renegOfferSdp.Media[1].Media);
            Assert.Equal("0", renegOfferSdp.Media[0].MediaID);
            Assert.Equal("1", renegOfferSdp.Media[1].MediaID);

            offerer.close();
            answerer.close();

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// When an audio-only offer/answer completes and a video track is added
        /// afterwards, the renegotiation offer must preserve audio at its original
        /// index and append video.
        /// </summary>
        [Fact]
        public void RenegotiationOfferPreservesAudioMLineAndAppendsVideo()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPeerConnection offerer = new RTCPeerConnection(null);
            var audioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                });
            offerer.addTrack(audioTrack);

            var initialOffer = offerer.createOffer(new RTCOfferOptions());
            var initialOfferSdp = SDP.ParseSDPDescription(initialOffer.sdp);
            Assert.Single(initialOfferSdp.Media);
            Assert.Equal(SDPMediaTypesEnum.audio, initialOfferSdp.Media[0].Media);

            // Answerer accepts audio.
            RTCPeerConnection answerer = new RTCPeerConnection(null);
            var answerAudioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                });
            answerer.addTrack(answerAudioTrack);
            answerer.setRemoteDescription(initialOffer);
            var answer = answerer.createAnswer();
            offerer.setRemoteDescription(answer);

            // Add video after handshake.
            var videoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            offerer.addTrack(videoTrack);

            var renegOffer = offerer.createOffer(new RTCOfferOptions());
            var renegOfferSdp = SDP.ParseSDPDescription(renegOffer.sdp);

            logger.LogDebug("Renegotiation offer SDP:\n{Sdp}", renegOffer.sdp);

            Assert.Equal(2, renegOfferSdp.Media.Count);
            Assert.Equal(SDPMediaTypesEnum.audio, renegOfferSdp.Media[0].Media);
            Assert.Equal(SDPMediaTypesEnum.video, renegOfferSdp.Media[1].Media);
            Assert.Equal("0", renegOfferSdp.Media[0].MediaID);
            Assert.Equal("1", renegOfferSdp.Media[1].MediaID);

            offerer.close();
            answerer.close();

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// The RemoteDescription must not be nulled when RequireRenegotiation is
        /// set to true (e.g. by addTrack). createBaseSdp() needs it to look up
        /// existing m-line indices.
        /// </summary>
        [Fact]
        public void RemoteDescriptionPreservedAfterAddTrack()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPeerConnection offerer = new RTCPeerConnection(null);
            var videoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            offerer.addTrack(videoTrack);

            var offer = offerer.createOffer(new RTCOfferOptions());

            RTCPeerConnection answerer = new RTCPeerConnection(null);
            var answerTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            answerer.addTrack(answerTrack);
            answerer.setRemoteDescription(offer);
            var answer = answerer.createAnswer();

            offerer.setRemoteDescription(answer);
            Assert.NotNull(offerer.RemoteDescription);

            // Adding a new track should NOT clear RemoteDescription.
            var audioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                });
            offerer.addTrack(audioTrack);

            Assert.NotNull(offerer.RemoteDescription);

            offerer.close();
            answerer.close();

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// In multiplexed (WebRTC/BUNDLE) mode, SetRemoteDescription must not
        /// overwrite a stream's DestinationEndPoint once ICE has established it.
        /// The existing IGNORE_RTP_PORT_NUMBER guard only protects port-9 m-lines;
        /// the first m-line in a browser answer typically carries the real ICE
        /// candidate port which would otherwise overwrite the ICE endpoint.
        /// </summary>
        [Fact]
        public void MultiplexedDestinationEndPointPreservedOnRenegotiation()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Multiplexed + secure = WebRTC mode.
            RTPSession rtpSession = new RTPSession(new RtpSessionConfig
            {
                IsMediaMultiplexed = true,
                IsRtcpMultiplexed = true,
            });

            var videoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            rtpSession.addTrack(videoTrack);

            // Simulate initial offer/answer — set a remote description with a real port.
            string initialAnswerSdp =
@"v=0
o=- 1000 0 IN IP4 127.0.0.1
s=-
c=IN IP4 192.168.1.100
t=0 0
m=video 50000 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=recvonly";
            var initialAnswer = SDP.ParseSDPDescription(initialAnswerSdp);
            var result = rtpSession.SetRemoteDescription(SIP.App.SdpType.answer, initialAnswer);
            Assert.Equal(SetDescriptionResultEnum.OK, result);

            // Simulate ICE setting the real endpoint (as SetGlobalDestination does).
            var iceEndPoint = new System.Net.IPEndPoint(
                System.Net.IPAddress.Parse("10.0.0.1"), 12345);
            rtpSession.VideoStream.SetDestination(iceEndPoint, iceEndPoint);
            Assert.Equal(iceEndPoint, rtpSession.VideoStream.DestinationEndPoint);

            // Add audio track for renegotiation.
            var audioTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                });
            rtpSession.addTrack(audioTrack);

            // Simulate renegotiation answer — browser uses a real port for video
            // (selected ICE candidate port) and port 9 for bundled audio.
            string renegAnswerSdp =
@"v=0
o=- 1000 1 IN IP4 127.0.0.1
s=-
c=IN IP4 192.168.1.100
t=0 0
m=video 50000 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=recvonly
m=audio 9 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=recvonly";
            var renegAnswer = SDP.ParseSDPDescription(renegAnswerSdp);
            result = rtpSession.SetRemoteDescription(SIP.App.SdpType.answer, renegAnswer);
            Assert.Equal(SetDescriptionResultEnum.OK, result);

            // Video DestinationEndPoint must still be the ICE endpoint, not the SDP address.
            Assert.Equal(iceEndPoint, rtpSession.VideoStream.DestinationEndPoint);

            rtpSession.Close("normal");

            logger.LogDebug("-----------------------------------------");
        }
    }
}
