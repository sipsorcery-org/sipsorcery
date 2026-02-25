//-----------------------------------------------------------------------------
// Filename: RTPSessionCreateAnswerUnitTest.cs
//
// Description: Characterization tests for RTPSession.CreateAnswer
// (the plain-RTP / SIP-VoIP answer surface). Pairs with the
// RTPSessionCreateOffer tests from Category 2 — locks down the
// observable shape of the answer SDP that is produced for typical
// offer/local combinations.
//
// Category 3 in the SDP-refactor test plan.
//
// History:
// 22 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionCreateAnswerUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionCreateAnswerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// CreateAnswer must throw SipSorceryException when no remote
        /// description has been set. Mirrors the WebRTC createAnswer
        /// contract.
        /// </summary>
        [Fact]
        public void NoRemoteDescriptionSet_Throws()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                Assert.Throws<SipSorceryException>(() => session.CreateAnswer(IPAddress.Loopback));
            }
        }

        /// <summary>
        /// CreateAnswer against a basic audio-only PCMU offer produces an
        /// audio-only answer advertising PCMU.
        /// </summary>
        [Fact]
        public void AudioOnlyOffer_AudioOnlyLocal_ProducesAudioAnswer()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                Assert.NotNull(answer);
                SdpAssert.HasMediaLines(answer, "audio");
                SdpAssert.HasCodec(SdpAssert.Audio(answer), "PCMU", 0);
                logger.LogDebug("Answer SDP:\n{Sdp}", answer.ToString());
            }
        }

        /// <summary>
        /// RFC 3264 §6 contract: "The order of the media lines in the
        /// answer MUST match the order in the offer." Audio-first offer
        /// produces audio-first answer.
        /// </summary>
        [Fact]
        public void AudioFirstOffer_AnswerHasAudioFirst()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                session.SetRemoteDescription(SdpType.offer, offer);

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                SdpAssert.HasMediaLines(answer, "audio", "video");
            }
        }

        /// <summary>
        /// Mirror of the previous test for inverse order. Video-first
        /// offer MUST produce video-first answer per RFC 3264.
        /// </summary>
        [Fact]
        public void VideoFirstOffer_AnswerHasVideoFirst()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferVideoFirst);
                session.SetRemoteDescription(SdpType.offer, offer);

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                SdpAssert.HasMediaLines(answer, "video", "audio");
            }
        }

        /// <summary>
        /// The answer's c= line is always populated. The exact address
        /// comes from GetSdpConnectionAddress, which prefers (in order):
        /// relay endpoint, STUN srflx endpoint, OS-routed local address
        /// to the offer's c= line, then the supplied fallback. Asserting
        /// the literal value is environment-dependent (the OS routes to
        /// whatever interface can reach the offer's address), so this
        /// test only verifies non-null. The offer-with-0.0.0.0 fallback
        /// path is covered separately below.
        /// </summary>
        [Fact]
        public void Answer_AlwaysHasPopulatedConnectionAddress()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                session.SetRemoteDescription(SdpType.offer, offer);

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                Assert.NotNull(answer.Connection);
                Assert.False(string.IsNullOrEmpty(answer.Connection.ConnectionAddress));
            }
        }

        /// <summary>
        /// When the offer's connection address is 0.0.0.0 (the classic
        /// "hold" form, IPAddress.Any), GetSdpConnectionAddress skips the
        /// OS-routing lookup and falls through to the supplied
        /// connectionAddress. The supplied loopback then appears on the
        /// answer's c= line verbatim.
        /// </summary>
        [Fact]
        public void Answer_OfferConnectionIsAny_UsesSuppliedFallbackAddress()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferHoldNullConnectionAddress);
                session.SetRemoteDescription(SdpType.offer, offer);

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                Assert.NotNull(answer.Connection);
                Assert.Equal(IPAddress.Loopback.ToString(), answer.Connection.ConnectionAddress);
            }
        }

        /// <summary>
        /// Answering an offer twice (e.g. during renegotiation) is
        /// idempotent at the SDP-shape level. After normalisation the
        /// two answers are equal.
        /// </summary>
        [Fact]
        public void TwoConsecutiveAnswersForSameOffer_AreIdenticalAfterNormalisation()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                session.SetRemoteDescription(SdpType.offer, offer);

                SDP answer1 = session.CreateAnswer(IPAddress.Loopback);
                SDP answer2 = session.CreateAnswer(IPAddress.Loopback);

                Assert.Equal(
                    SdpNormaliser.NormaliseCompact(answer1.ToString()),
                    SdpNormaliser.NormaliseCompact(answer2.ToString()));
            }
        }

        /// <summary>
        /// When the offer rejects video with m=video 0, the answer still
        /// emits a video m-line (preserving offer order) but with port 0,
        /// confirming RFC 3264 §6 "the answer MUST also set the port to
        /// zero" handling.
        /// </summary>
        [Fact]
        public void OfferWithRejectedVideoPortZero_AnswerHasVideoRejected()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                // First settle the streams with a full audio+video offer
                // (so the renegotiation hits the existing media streams).
                SDP firstOffer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, firstOffer));

                SDP reInvite = SDP.ParseSDPDescription(SdpFixtures.ReInviteRejectsVideoPortZero);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, reInvite));

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                SdpAssert.HasMediaLines(answer, "audio", "video");
                SdpAssert.IsAccepted(SdpAssert.Audio(answer));
                SdpAssert.IsRejected(SdpAssert.Video(answer));
            }
        }

        /// <summary>
        /// Answering an audio+video offer when the local side only has an
        /// audio track: the answer still includes both m-lines (because
        /// SetRemoteDescription created an Inactive local video track),
        /// and the video line in the answer is Inactive.
        /// </summary>
        [Fact]
        public void AudioVideoOffer_AudioOnlyLocal_AnswerHasBothWithVideoInactive()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                SDP answer = session.CreateAnswer(IPAddress.Loopback);

                SdpAssert.HasMediaLines(answer, "audio", "video");
                SdpAssert.HasDirection(SdpAssert.Audio(answer), MediaStreamStatusEnum.SendRecv);
                SdpAssert.HasDirection(SdpAssert.Video(answer), MediaStreamStatusEnum.Inactive);
            }
        }
    }
}
