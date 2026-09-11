//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionCreateAnswerUnitTest.cs
//
// Description: Characterization tests for RTCPeerConnection.createAnswer.
// Pairs with RTCPeerConnectionCreateOfferUnitTest (Category 2) — locks
// down the observable shape of the answer SDP produced after a remote
// offer is set.
//
// Category 3 in the SDP-refactor test plan. The existing
// RTCPeerConnectionAnswerUnitTest already covers the RFC 4145 setup
// requirement (no actpass in an answer); this file fills the rest of
// the matrix.
//
// Out of scope here:
//   - Codec / payload-type matching (Category 4)
//   - Header-extension negotiation deep dive (Category 6)
//   - RTCP session lifecycle (Category 9)
//
// History:
// 22 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionCreateAnswerUnitTest
    {
        private readonly ILogger logger;

        public RTCPeerConnectionCreateAnswerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// createAnswer must throw when no remote description has been set.
        /// The current implementation throws ApplicationException; locking
        /// down the exception type here so a refactor can't quietly change
        /// it to a typed RTCError or similar.
        /// </summary>
        [Fact]
        public void NoRemoteDescriptionSet_Throws()
        {
            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                Assert.Throws<ApplicationException>(() => pc.createAnswer(null));
            }
        }

        /// <summary>
        /// Answer to a basic audio-only offer is itself an audio-only
        /// answer, of type=answer, with non-empty sdp.
        /// </summary>
        [Fact]
        public void AudioOnlyOffer_AudioOnlyLocal_ProducesAudioAnswer()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU).Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU).Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                Assert.Equal(SetDescriptionResultEnum.OK, answerer.setRemoteDescription(offer));

                var answerInit = answerer.createAnswer();

                Assert.NotNull(answerInit);
                Assert.Equal(RTCSdpType.answer, answerInit.type);
                Assert.False(string.IsNullOrEmpty(answerInit.sdp));

                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);
                SdpAssert.HasMediaLines(answerSdp, "audio");
                SdpAssert.HasCodec(SdpAssert.Audio(answerSdp), "PCMU", 0);
            }
        }

        /// <summary>
        /// Answer to a video-only offer is a video-only answer.
        /// </summary>
        [Fact]
        public void VideoOnlyOffer_VideoOnlyLocal_ProducesVideoAnswer()
        {
            using (var offerer = new PeerConnectionBuilder().WithVideoTrack(96, "VP8", 90000).Build())
            using (var answerer = new PeerConnectionBuilder().WithVideoTrack(96, "VP8", 90000).Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);

                var answerInit = answerer.createAnswer();

                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);
                SdpAssert.HasMediaLines(answerSdp, "video");
                SdpAssert.HasCodec(SdpAssert.Video(answerSdp), "VP8", 96);
            }
        }

        /// <summary>
        /// When the offer has both audio and video and the local side has
        /// both too, the answer must include both m-lines in the same
        /// order as the offer.
        /// </summary>
        [Fact]
        public void AudioVideoOffer_AnswerHasBothLinesInOfferOrder()
        {
            using (var offerer = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            using (var answerer = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);

                var answerInit = answerer.createAnswer();

                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);
                SdpAssert.HasMediaLines(answerSdp, "audio", "video");
            }
        }

        /// <summary>
        /// Every m-line in the answer must carry ICE credentials, a DTLS
        /// fingerprint, and the SDP must declare a BUNDLE group. These
        /// are non-negotiable transport bits per the WebRTC spec.
        /// </summary>
        [Fact]
        public void Answer_AlwaysIncludesIceFingerprintAndBundle()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);

                var answerInit = answerer.createAnswer();
                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);

                SdpAssert.HasIceCredentials(answerSdp);
                SdpAssert.HasFingerprint(answerSdp);
                SdpAssert.HasBundle(answerSdp);
            }
        }

        /// <summary>
        /// Per RFC 4145 §4.1 and RFC 5763 §5, an SDP answer must declare
        /// a=setup:active or a=setup:passive, never actpass. The default
        /// for the answerer (when IceRole isn't explicitly active) is
        /// passive. Verified across both m-lines of an audio+video answer.
        /// </summary>
        [Fact]
        public void Answer_EveryMediaLineHasSetupActiveOrPassiveNeverActpass()
        {
            using (var offerer = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            using (var answerer = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);

                var answerInit = answerer.createAnswer();
                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);

                Assert.NotEmpty(answerSdp.Media);
                foreach (var media in answerSdp.Media)
                {
                    Assert.NotNull(media.IceRole);
                    Assert.NotEqual(IceRolesEnum.actpass, media.IceRole.Value);
                    Assert.True(
                        media.IceRole.Value == IceRolesEnum.active ||
                        media.IceRole.Value == IceRolesEnum.passive,
                        $"Answer m-line {media.Media} had setup:{media.IceRole}.");
                }
                Assert.DoesNotContain("a=setup:actpass", answerInit.sdp);
            }
        }

        /// <summary>
        /// A local track explicitly set to Inactive is reverted to its
        /// DefaultStreamStatus when createAnswer runs (mirroring the
        /// same behaviour in createOffer). Easy to lose in a refactor of
        /// the loop at the top of createAnswer.
        /// </summary>
        [Fact]
        public void InactiveLocalTrack_RevertsToDefaultStreamStatusOnAnswer()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);

                answerer.AudioStream.LocalTrack.StreamStatus = MediaStreamStatusEnum.Inactive;
                Assert.Equal(MediaStreamStatusEnum.Inactive, answerer.AudioStream.LocalTrack.StreamStatus);

                answerer.createAnswer();

                Assert.Equal(MediaStreamStatusEnum.SendRecv, answerer.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// X_ExcludeIceCandidates option suppresses a=candidate lines in
        /// the produced answer SDP. Matches the equivalent option on
        /// createOffer.
        /// </summary>
        [Fact]
        public void AnswerOptions_ExcludeIceCandidates_ProducesSdpWithNoCandidateLines()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);

                var answerInit = answerer.createAnswer(new RTCAnswerOptions { X_ExcludeIceCandidates = true });

                Assert.DoesNotContain("a=candidate:", answerInit.sdp);
            }
        }

        /// <summary>
        /// Two independent answerers, given functionally-identical offers,
        /// produce SDPs that match after normalisation. Locks down the
        /// deterministic portion of answer generation so any structural
        /// change in the refactor (attribute ordering, capability set,
        /// rtcp-mux/setup placement) surfaces immediately.
        /// </summary>
        [Fact]
        public void TwoAnswerersSameOfferShape_ProduceIdenticalNormalisedAnswers()
        {
            string answer1, answer2;
            using (var offerer1 = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU).Build())
            using (var answerer1 = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU).Build())
            {
                var o1 = offerer1.createOffer(new RTCOfferOptions());
                answerer1.setRemoteDescription(o1);
                answer1 = answerer1.createAnswer().sdp;
            }
            using (var offerer2 = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU).Build())
            using (var answerer2 = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU).Build())
            {
                var o2 = offerer2.createOffer(new RTCOfferOptions());
                answerer2.setRemoteDescription(o2);
                answer2 = answerer2.createAnswer().sdp;
            }

            Assert.Equal(
                SdpNormaliser.NormaliseCompact(answer1),
                SdpNormaliser.NormaliseCompact(answer2));
        }

        /// <summary>
        /// When the offer has both audio and video but the answerer only
        /// has a local audio track, createAnswer still emits both m-lines
        /// (the unmatched video gets an Inactive local track created by
        /// SetRemoteDescription). The video m-line in the answer is
        /// present but Inactive.
        /// </summary>
        [Fact]
        public void AudioVideoOffer_AudioOnlyLocal_AnswerStillHasBothLines()
        {
            using (var offerer = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            using (var answerer = new PeerConnectionBuilder()
                .WithAudioTrack()
                .Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);

                var answerInit = answerer.createAnswer();
                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);

                SdpAssert.HasMediaLines(answerSdp, "audio", "video");
                SdpAssert.HasDirection(SdpAssert.Audio(answerSdp), MediaStreamStatusEnum.SendRecv);
                SdpAssert.HasDirection(SdpAssert.Video(answerSdp), MediaStreamStatusEnum.Inactive);
            }
        }
    }
}
