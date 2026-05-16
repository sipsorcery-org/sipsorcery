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
        /// Without the fix, either audio is omitted entirely (MEDIA_INDEX_NOT_PRESENT
        /// caused it to be dropped) or m-line order is wrong (RemoteDescription was
        /// nulled, falling back to insertion-order indices).
        /// </summary>
        [Fact]
        public void RenegotiationOfferPreservesVideoMLineAndAppendsAudio()
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

            // Initial offer — video only.
            var initialOffer = offerer.createOffer(new RTCOfferOptions());
            Assert.NotNull(initialOffer?.sdp);
            var initialOfferSdp = SDP.ParseSDPDescription(initialOffer.sdp);
            Assert.Single(initialOfferSdp.Media);
            Assert.Equal(SDPMediaTypesEnum.video, initialOfferSdp.Media[0].Media);

            logger.LogDebug("Initial offer SDP:\n{Sdp}", initialOffer.sdp);

            // Answerer accepts video.
            RTCPeerConnection answerer = new RTCPeerConnection(null);
            answerer.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                }));

            var setResult = answerer.setRemoteDescription(initialOffer);
            Assert.Equal(SetDescriptionResultEnum.OK, setResult);
            var answer = answerer.createAnswer();
            Assert.NotNull(answer?.sdp);

            // Offerer processes the answer — RemoteDescription is now set.
            setResult = offerer.setRemoteDescription(answer);
            Assert.Equal(SetDescriptionResultEnum.OK, setResult);

            // Add audio track after initial handshake completes.
            offerer.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                }));

            // Renegotiation offer must have both m-lines in correct order.
            var renegOffer = offerer.createOffer(new RTCOfferOptions());
            Assert.NotNull(renegOffer?.sdp);
            var renegSdp = SDP.ParseSDPDescription(renegOffer.sdp);

            logger.LogDebug("Renegotiation offer SDP:\n{Sdp}", renegOffer.sdp);

            Assert.Equal(2, renegSdp.Media.Count);

            // Video must keep its original position and mid.
            Assert.Equal(SDPMediaTypesEnum.video, renegSdp.Media[0].Media);
            Assert.Equal("0", renegSdp.Media[0].MediaID);
            Assert.Contains(renegSdp.Media[0].MediaFormats.Values,
                f => f.Name().ToUpper() == "VP8");

            // Audio must be appended after video.
            Assert.Equal(SDPMediaTypesEnum.audio, renegSdp.Media[1].Media);
            Assert.Equal("1", renegSdp.Media[1].MediaID);
            Assert.Contains(renegSdp.Media[1].MediaFormats.Values,
                f => f.Name().ToUpper() == "PCMU");

            offerer.close();
            answerer.close();

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Mirror of the above test with the media types reversed: audio-only
        /// initial handshake, then video added via renegotiation.
        /// </summary>
        [Fact]
        public void RenegotiationOfferPreservesAudioMLineAndAppendsVideo()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPeerConnection offerer = new RTCPeerConnection(null);
            offerer.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                }));

            var initialOffer = offerer.createOffer(new RTCOfferOptions());
            var initialOfferSdp = SDP.ParseSDPDescription(initialOffer.sdp);
            Assert.Single(initialOfferSdp.Media);
            Assert.Equal(SDPMediaTypesEnum.audio, initialOfferSdp.Media[0].Media);

            RTCPeerConnection answerer = new RTCPeerConnection(null);
            answerer.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                }));
            answerer.setRemoteDescription(initialOffer);
            var answer = answerer.createAnswer();
            offerer.setRemoteDescription(answer);

            // Add video after handshake.
            offerer.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                }));

            var renegOffer = offerer.createOffer(new RTCOfferOptions());
            var renegSdp = SDP.ParseSDPDescription(renegOffer.sdp);

            logger.LogDebug("Renegotiation offer SDP:\n{Sdp}", renegOffer.sdp);

            Assert.Equal(2, renegSdp.Media.Count);

            Assert.Equal(SDPMediaTypesEnum.audio, renegSdp.Media[0].Media);
            Assert.Equal("0", renegSdp.Media[0].MediaID);
            Assert.Contains(renegSdp.Media[0].MediaFormats.Values,
                f => f.Name().ToUpper() == "PCMU");

            Assert.Equal(SDPMediaTypesEnum.video, renegSdp.Media[1].Media);
            Assert.Equal("1", renegSdp.Media[1].MediaID);
            Assert.Contains(renegSdp.Media[1].MediaFormats.Values,
                f => f.Name().ToUpper() == "VP8");

            offerer.close();
            answerer.close();

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// <summary>
        /// RemoteDescription must survive addTrack so that createBaseSdp can
        /// look up existing m-line indices during renegotiation. Without the fix,
        /// RequireRenegotiation = true nulls RemoteDescription and the renegotiation
        /// offer falls back to sequential indices (wrong order).
        /// </summary>
        [Fact]
        public void RemoteDescriptionPreservedAfterAddTrack()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPeerConnection offerer = new RTCPeerConnection(null);
            offerer.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                }));

            var offer = offerer.createOffer(new RTCOfferOptions());

            RTCPeerConnection answerer = new RTCPeerConnection(null);
            answerer.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                }));
            answerer.setRemoteDescription(offer);
            var answer = answerer.createAnswer();

            offerer.setRemoteDescription(answer);

            // Capture the RemoteDescription content before addTrack.
            var remoteDescBefore = offerer.RemoteDescription?.ToString();
            Assert.NotNull(remoteDescBefore);
            Assert.Contains("m=video", remoteDescBefore);

            // addTrack triggers RequireRenegotiation = true.
            offerer.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                }));

            // RemoteDescription must still be intact with the same content.
            Assert.NotNull(offerer.RemoteDescription);
            var remoteDescAfter = offerer.RemoteDescription.ToString();
            Assert.Equal(remoteDescBefore, remoteDescAfter);

            offerer.close();
            answerer.close();

            logger.LogDebug("-----------------------------------------");
        }

        /// In multiplexed (WebRTC/BUNDLE) mode, SetRemoteDescription must not
        /// overwrite a stream's DestinationEndPoint once ICE has established it.
        /// The existing IGNORE_RTP_PORT_NUMBER guard only protects port-9 m-lines;
        /// the first m-line in a browser answer typically carries the real ICE
        /// candidate port which would otherwise overwrite the ICE endpoint.
        /// Without the fix, video RTP is sent to the SDP address instead of the
        /// ICE-negotiated endpoint, breaking media delivery.
        /// </summary>
        [Fact]
        public void MultiplexedDestinationEndPointPreservedOnRenegotiation()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPSession rtpSession = new RTPSession(new RtpSessionConfig
            {
                IsMediaMultiplexed = true,
                IsRtcpMultiplexed = true,
            });

            rtpSession.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                }));

            // Initial answer with a real port (as browsers send for the first m-line).
            string initialAnswerSdp =
@"v=0
o=- 1000 0 IN IP4 127.0.0.1
s=-
c=IN IP4 192.168.1.100
t=0 0
m=video 50000 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=recvonly";
            var result = rtpSession.SetRemoteDescription(
                SIP.App.SdpType.answer, SDP.ParseSDPDescription(initialAnswerSdp));
            Assert.Equal(SetDescriptionResultEnum.OK, result);

            // Simulate ICE establishing the real endpoint.
            var iceEndPoint = new System.Net.IPEndPoint(
                System.Net.IPAddress.Parse("10.0.0.1"), 12345);
            rtpSession.VideoStream.SetDestination(iceEndPoint, iceEndPoint);
            Assert.Equal(iceEndPoint, rtpSession.VideoStream.DestinationEndPoint);

            // Add audio for renegotiation.
            rtpSession.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                }));

            // Renegotiation answer — browser uses real port for video, port 9 for
            // bundled audio (standard Chrome behavior).
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
            result = rtpSession.SetRemoteDescription(
                SIP.App.SdpType.answer, SDP.ParseSDPDescription(renegAnswerSdp));
            Assert.Equal(SetDescriptionResultEnum.OK, result);

            // Video DestinationEndPoint must still be the ICE endpoint.
            Assert.Equal(iceEndPoint, rtpSession.VideoStream.DestinationEndPoint);

            rtpSession.Close("normal");

            logger.LogDebug("-----------------------------------------");
        }
    }
}
