//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionAnswerUnitTest.cs
//
// Description: Unit tests for RTCPeerConnection.createAnswer().
//
// History:
// 16 Feb 2026	CraziestPower	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionAnswerUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPeerConnectionAnswerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that createAnswer generates a=setup:active or a=setup:passive,
        /// never a=setup:actpass. Per RFC 4145 Section 4.1 and RFC 5763 Section 5,
        /// an SDP answer must not contain setup:actpass.
        /// See https://github.com/sipsorcery-org/sipsorcery/issues/1463.
        /// </summary>
        [Fact]
        public void AnswerSdpSetupAttributeNotActpass()
        {
            // Create an offerer with a video track.
            RTCPeerConnection offerer = new RTCPeerConnection(null);
            var videoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            offerer.addTrack(videoTrack);

            var offer = offerer.createOffer(new RTCOfferOptions());
            Assert.NotNull(offer?.sdp);

            logger.LogDebug("Offer SDP:\n{Sdp}", offer.sdp);

            // Create an answerer, set the remote offer, then create the answer.
            RTCPeerConnection answerer = new RTCPeerConnection(null);
            var answerVideoTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                });
            answerer.addTrack(answerVideoTrack);

            var setResult = answerer.setRemoteDescription(offer);
            Assert.Equal(SetDescriptionResultEnum.OK, setResult);

            var answer = answerer.createAnswer();
            Assert.NotNull(answer?.sdp);

            logger.LogDebug("Answer SDP:\n{Sdp}", answer.sdp);

            // Parse the answer SDP and verify every media announcement has
            // setup:active or setup:passive, never actpass.
            SDP answerSdp = SDP.ParseSDPDescription(answer.sdp);
            Assert.NotEmpty(answerSdp.Media);

            foreach (var media in answerSdp.Media)
            {
                Assert.NotNull(media.IceRole);
                Assert.NotEqual(IceRolesEnum.actpass, media.IceRole.Value);
                Assert.True(
                    media.IceRole == IceRolesEnum.active || media.IceRole == IceRolesEnum.passive,
                    $"Answer SDP media {media.Media} had setup:{media.IceRole} instead of active or passive.");
            }

            // Also verify the raw SDP string does not contain "a=setup:actpass".
            Assert.DoesNotContain("a=setup:actpass", answer.sdp);

            offerer.close();
            answerer.close();
        }
    }
}
