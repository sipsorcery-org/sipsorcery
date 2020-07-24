//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionUnitTest.cs
//
// Description: Unit tests for the RTCPeerConnection class.
//
// History:
// 16 Mar 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
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
            var audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
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
                certificates = new List<RTCCertificate>
                {
                    new RTCCertificate
                    {
                        Certificate = DtlsUtils.CreateSelfSignedCert()
        }
                },
                X_UseRtpFeedbackProfile = true
            };

            RTCPeerConnection pcSrc = new RTCPeerConnection(pcConfiguration);
            var videoTrackSrc = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
            pcSrc.addTrack(videoTrackSrc);
            var offer = pcSrc.createOffer(new RTCOfferOptions());

            logger.LogDebug($"offer: {offer.sdp}");

            RTCPeerConnection pcDst = new RTCPeerConnection(pcConfiguration);
            var videoTrackDst = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
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
    }
}
