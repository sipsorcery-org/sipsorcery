//-----------------------------------------------------------------------------
// Filename: RTPSessionSetRemoteDescriptionValidationUnitTest.cs
//
// Description: Characterization tests for the validation / guard-clause
// paths of RTPSession.SetRemoteDescription. Locks down the OBSERVABLE
// behaviour of the current (pre-refactor) implementation so the planned
// extraction of SDP negotiation logic doesn't change semantics.
//
// What's in scope here (Category 1 of the SDP-refactor test plan):
//   - Null/empty/single-media validation
//   - SdpCryptoNegotiation transport + crypto-suite validation
//   - Per-media-type "no compatible codecs" error returns
//   - Happy-path side-effects: RemoteDescription set, OnRemoteDescriptionChanged
//     fires, RequireRenegotiation cleared, RemoteTracks populated
//   - Renegotiation: previously-set RemoteTracks are replaced
//
// Out of scope (covered by later categories):
//   - Offer/answer generation (Categories 2 + 3)
//   - Codec / payload-type matching (Category 4)
//   - Header-extension negotiation (Category 6)
//   - RTCP session lifecycle on inactive (Category 9)
//
// History:
// 20 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionSetRemoteDescriptionValidationUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionSetRemoteDescriptionValidationUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Passing a null session description is a programmer error rather
        /// than a recoverable negotiation failure, so the method throws
        /// ArgumentNullException with the expected parameter name.
        /// </summary>
        [Fact]
        public void NullSessionDescription_ThrowsArgumentNullException()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                    () => session.SetRemoteDescription(SdpType.offer, null));
                Assert.Equal("sessionDescription", ex.ParamName);
            }
        }

        /// <summary>
        /// An SDP that parses successfully but contains no m= lines returns
        /// NoRemoteMedia. Used to surface "session-level fields only"
        /// payloads as a distinct failure mode from a malformed SDP.
        /// </summary>
        [Fact]
        public void EmptyMediaList_ReturnsNoRemoteMedia()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP empty = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
t=0 0");
                Assert.Empty(empty.Media);

                var result = session.SetRemoteDescription(SdpType.offer, empty);

                Assert.Equal(SetDescriptionResultEnum.NoRemoteMedia, result);
            }
        }

        /// <summary>
        /// Single-media audio offer with no local audio track returns
        /// NoMatchingMediaType. Locally the session has only a video
        /// track so the audio offer can't be answered.
        /// </summary>
        [Fact]
        public void SingleAudioOffer_NoLocalAudioTrack_ReturnsNoMatchingMediaType()
        {
            using (var session = new RtpSessionBuilder().WithVideoTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.NoMatchingMediaType, result);
            }
        }

        /// <summary>
        /// Single-media video offer with no local video track returns
        /// NoMatchingMediaType. Mirror of the audio case for the video
        /// branch in the guard.
        /// </summary>
        [Fact]
        public void SingleVideoOffer_NoLocalVideoTrack_ReturnsNoMatchingMediaType()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.VideoOnlyOfferVp8);

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.NoMatchingMediaType, result);
            }
        }

        /// <summary>
        /// Single-media text (t140) offer with no local text track returns
        /// NoMatchingMediaType. Third branch of the same guard.
        /// </summary>
        [Fact]
        public void SingleTextOffer_NoLocalTextTrack_ReturnsNoMatchingMediaType()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=text 20040 RTP/AVP 98
a=rtpmap:98 t140/1000
a=sendrecv");

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.NoMatchingMediaType, result);
            }
        }

        /// <summary>
        /// The NoMatchingMediaType guard only fires for single-media offers.
        /// When the offer is multi-media and only one side has a matching
        /// local track, negotiation succeeds and the unmatched media gets
        /// an Inactive local track. Locks down this asymmetry so the
        /// refactor preserves it.
        /// </summary>
        [Fact]
        public void MultiMediaOffer_AudioPlusVideo_NoLocalVideo_StillSucceedsButVideoIsInactive()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.OK, result);
                Assert.NotNull(session.VideoStream);
                Assert.NotNull(session.VideoStream.LocalTrack);
                Assert.Equal(MediaStreamStatusEnum.Inactive, session.VideoStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// When the session is configured for SDES crypto, the remote
        /// transport must be RTP/SAVP. A remote that offers plain RTP/AVP
        /// fails crypto negotiation outright.
        /// </summary>
        [Fact]
        public void SdpCryptoNegotiation_RemoteTransportIsNotSecure_ReturnsCryptoNegotiationFailed()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.CryptoNegotiationFailed, result);
            }
        }

        /// <summary>
        /// When the session is configured for SDES crypto and the remote
        /// offer uses RTP/SAVP but advertises only crypto suites outside
        /// the locally-accepted set (AES_CM_128_HMAC_SHA1_{80,32}),
        /// negotiation fails.
        /// </summary>
        [Fact]
        public void SdpCryptoNegotiation_RemoteSecureNoCompatibleSuite_ReturnsCryptoNegotiationFailed()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AES_256_CM_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv");

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.CryptoNegotiationFailed, result);
            }
        }

        /// <summary>
        /// Happy-path SDES negotiation: remote uses RTP/SAVP and offers a
        /// compatible AES_CM_128_HMAC_SHA1_80 suite, the session accepts
        /// and returns OK.
        /// </summary>
        [Fact]
        public void SdpCryptoNegotiation_RemoteSecureCompatibleSuite_ReturnsOk()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferWithSdesCrypto);

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.OK, result);
            }
        }

        /// <summary>
        /// On successful negotiation the RemoteDescription property holds a
        /// reference to the exact SDP instance that was passed in (not a
        /// copy). Important for callers that compare by reference.
        /// </summary>
        [Fact]
        public void OkOffer_SetsRemoteDescriptionPropertyToOriginalSdp()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);

                Assert.Null(session.RemoteDescription);
                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.OK, result);
                Assert.Same(offer, session.RemoteDescription);
            }
        }

        /// <summary>
        /// OnRemoteDescriptionChanged must fire exactly once per successful
        /// negotiation, with the same SDP instance that was passed in.
        /// Locks down the event semantics for downstream subscribers.
        /// </summary>
        [Fact]
        public void OkOffer_FiresOnRemoteDescriptionChangedExactlyOnce()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                int callCount = 0;
                SDP captured = null;
                session.OnRemoteDescriptionChanged += sdp =>
                {
                    callCount++;
                    captured = sdp;
                };

                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(1, callCount);
                Assert.Same(offer, captured);
            }
        }

        /// <summary>
        /// After a successful negotiation, the matching MediaStream has a
        /// RemoteTrack populated, of the right kind, and flagged IsRemote.
        /// </summary>
        [Fact]
        public void OkOffer_PopulatesRemoteTrackOnMatchingMediaStream()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                session.SetRemoteDescription(SdpType.offer, offer);

                Assert.NotNull(session.AudioStream);
                Assert.NotNull(session.AudioStream.RemoteTrack);
                Assert.Equal(SDPMediaTypesEnum.audio, session.AudioStream.RemoteTrack.Kind);
                Assert.True(session.AudioStream.RemoteTrack.IsRemote);
            }
        }

        /// <summary>
        /// Second call to SetRemoteDescription must replace (not append to)
        /// the existing RemoteTrack. Guards against a regression where
        /// renegotiation accumulated stale track state.
        /// </summary>
        [Fact]
        public void Renegotiation_ReplacesPreviousRemoteTrack_NotAddsToIt()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP firstOffer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                session.SetRemoteDescription(SdpType.offer, firstOffer);
                MediaStreamTrack firstRemoteTrack = session.AudioStream.RemoteTrack;
                Assert.NotNull(firstRemoteTrack);

                SDP secondOffer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferPcmuWithDtmf);
                var result = session.SetRemoteDescription(SdpType.offer, secondOffer);

                Assert.Equal(SetDescriptionResultEnum.OK, result);
                Assert.NotSame(firstRemoteTrack, session.AudioStream.RemoteTrack);
                Assert.Same(secondOffer, session.RemoteDescription);
            }
        }

        /// <summary>
        /// A re-INVITE that rejects video with m=video 0 must leave audio
        /// SendRecv and the local video track Inactive. Regression scenario
        /// for issue #1496.
        /// </summary>
        [Fact]
        public void Renegotiation_VideoRejectedByPortZero_AudioContinuesAndVideoInactive()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP firstOffer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, firstOffer));

                SDP reInvite = SDP.ParseSDPDescription(SdpFixtures.ReInviteRejectsVideoPortZero);
                var result = session.SetRemoteDescription(SdpType.offer, reInvite);

                Assert.Equal(SetDescriptionResultEnum.OK, result);
                Assert.Equal(MediaStreamStatusEnum.SendRecv, session.AudioStream.LocalTrack.StreamStatus);
                Assert.Equal(MediaStreamStatusEnum.Inactive, session.VideoStream.LocalTrack.StreamStatus);
            }
        }
    }
}
