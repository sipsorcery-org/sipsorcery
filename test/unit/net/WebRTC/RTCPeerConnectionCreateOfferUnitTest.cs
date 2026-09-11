//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionCreateOfferUnitTest.cs
//
// Description: Characterization tests for RTCPeerConnection.createOffer.
// Locks down the OBSERVABLE shape of the offer SDP produced by the current
// (pre-refactor) implementation across the matrix of track combinations,
// directions, and WebRTC plumbing it has to emit (ICE creds, DTLS
// fingerprint, BUNDLE group, rtcp-mux, setup:actpass).
//
// Category 2 in the SDP-refactor test plan. Out of scope here:
//   - Answer generation (Category 3)
//   - Codec / payload-type matching (Category 4)
//   - Header-extension negotiation deep dive (Category 6)
//
// History:
// 22 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionCreateOfferUnitTest
    {
        private readonly ILogger logger;

        public RTCPeerConnectionCreateOfferUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// createOffer on a peer connection with a single PCMU audio track
        /// must return an init wrapper of type=offer with a non-empty sdp
        /// that parses cleanly and advertises exactly one audio m-line.
        /// </summary>
        [Fact]
        public void AudioOnly_ProducesOfferWithSingleAudioMediaLine()
        {
            using (var pc = new PeerConnectionBuilder().WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU).Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                Assert.NotNull(offerInit);
                Assert.Equal(RTCSdpType.offer, offerInit.type);
                Assert.False(string.IsNullOrEmpty(offerInit.sdp));

                SDP sdp = SdpAssert.Parse(offerInit.sdp);
                SdpAssert.HasMediaLines(sdp, "audio");
                SdpAssert.HasCodec(SdpAssert.Audio(sdp), "PCMU", 0);
                logger.LogDebug("Offer SDP:\n{Sdp}", offerInit.sdp);
            }
        }

        /// <summary>
        /// Adding a video track only emits a single video m-line, with the
        /// requested dynamic-PT codec advertised.
        /// </summary>
        [Fact]
        public void VideoOnly_ProducesOfferWithSingleVideoMediaLine()
        {
            using (var pc = new PeerConnectionBuilder().WithVideoTrack(96, "VP8", 90000).Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                SDP sdp = SdpAssert.Parse(offerInit.sdp);
                SdpAssert.HasMediaLines(sdp, "video");
                SdpAssert.HasCodec(SdpAssert.Video(sdp), "VP8", 96);
            }
        }

        /// <summary>
        /// m-line order in the offer must match the order tracks were added.
        /// Audio-then-video addition emits audio first.
        /// </summary>
        [Fact]
        public void AudioThenVideo_PreservesInsertionOrderInOffer()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                SDP sdp = SdpAssert.Parse(offerInit.sdp);
                SdpAssert.HasMediaLines(sdp, "audio", "video");
            }
        }

        /// <summary>
        /// Adding video before audio on an RTCPeerConnection still emits
        /// audio first in the resulting offer. This is asymmetric with
        /// RTPSession.CreateOffer (which preserves insertion order) and is
        /// worth locking down: the WebRTC offer flow canonicalises m-line
        /// order to audio-then-video regardless of addTrack sequence.
        /// </summary>
        [Fact]
        public void VideoThenAudio_RtcPeerConnectionStillEmitsAudioFirst()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithVideoTrack(96, "VP8", 90000)
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                SDP sdp = SdpAssert.Parse(offerInit.sdp);
                SdpAssert.HasMediaLines(sdp, "audio", "video");
            }
        }

        /// <summary>
        /// A WebRTC offer must always carry ICE credentials (ufrag + pwd),
        /// a DTLS fingerprint, and a BUNDLE group attribute, regardless of
        /// how many tracks the local side has configured. These are the
        /// non-negotiable transport bits the spec requires.
        /// </summary>
        [Fact]
        public void Offer_AlwaysIncludesIceFingerprintAndBundle()
        {
            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());
                SDP sdp = SdpAssert.Parse(offerInit.sdp);

                SdpAssert.HasIceCredentials(sdp);
                SdpAssert.HasFingerprint(sdp);
                SdpAssert.HasBundle(sdp);
            }
        }

        /// <summary>
        /// Per RFC 5763 §5, an SDP offer must declare a=setup:actpass on
        /// every m-line — the offerer is willing to be either DTLS client
        /// or server. The answerer commits to active or passive in the
        /// answer (covered by Category 3).
        /// </summary>
        [Fact]
        public void Offer_EveryMediaLineHasSetupActpass()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());
                SDP sdp = SdpAssert.Parse(offerInit.sdp);

                Assert.NotEmpty(sdp.Media);
                foreach (var media in sdp.Media)
                {
                    Assert.NotNull(media.IceRole);
                    Assert.Equal(IceRolesEnum.actpass, media.IceRole.Value);
                }
                Assert.Contains("a=setup:actpass", offerInit.sdp);
            }
        }

        /// <summary>
        /// An offer with no remote description set must request rtcp-mux
        /// on every m-line. SIPSorcery always offers it.
        /// </summary>
        [Fact]
        public void Offer_EveryMediaLineHasRtcpMux()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                Assert.Contains("a=rtcp-mux", offerInit.sdp);
                // Both m-lines bundled, so both must declare rtcp-mux.
                int count = 0;
                int idx = 0;
                while ((idx = offerInit.sdp.IndexOf("a=rtcp-mux", idx)) >= 0)
                {
                    count++;
                    idx += "a=rtcp-mux".Length;
                }
                Assert.Equal(2, count);
            }
        }

        /// <summary>
        /// Two distinct peer connections built from the same builder spec
        /// must produce SDPs that match after stripping volatile fields
        /// (session id, ICE creds, fingerprint, ssrc, cname, ports). Locks
        /// down the deterministic component of offer generation so a
        /// refactor that changes attribute ordering or capability set is
        /// caught immediately.
        /// </summary>
        [Fact]
        public void TwoPeerConnectionsSameBuilder_ProduceIdenticalNormalisedOffers()
        {
            string sdp1, sdp2;
            using (var pc1 = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                sdp1 = pc1.createOffer(new RTCOfferOptions()).sdp;
            }
            using (var pc2 = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                sdp2 = pc2.createOffer(new RTCOfferOptions()).sdp;
            }

            Assert.Equal(
                SdpNormaliser.NormaliseCompact(sdp1),
                SdpNormaliser.NormaliseCompact(sdp2));
        }

        /// <summary>
        /// Building a peer connection with no tracks must still return an
        /// offer init (not crash), even if the produced SDP has no m-lines.
        /// This documents the current contract; a refactor that changes
        /// this behaviour to e.g. throw is a breaking change and the test
        /// flags it.
        /// </summary>
        [Fact]
        public void NoLocalTracks_ReturnsOfferInitWithNoMediaLines()
        {
            using (var pc = new PeerConnectionBuilder().Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                Assert.NotNull(offerInit);
                Assert.Equal(RTCSdpType.offer, offerInit.type);
                Assert.False(string.IsNullOrEmpty(offerInit.sdp));

                SDP sdp = SdpAssert.Parse(offerInit.sdp);
                Assert.Empty(sdp.Media);
            }
        }

        /// <summary>
        /// createOffer must be idempotent at the SDP shape level — two
        /// calls on the same peer connection produce SDPs that normalise
        /// identically (allowing for the session-id / version bump which
        /// the normaliser strips).
        /// </summary>
        [Fact]
        public void TwoConsecutiveCallsOnSamePeer_ProduceIdenticalNormalisedOffers()
        {
            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offer1 = pc.createOffer(new RTCOfferOptions());
                var offer2 = pc.createOffer(new RTCOfferOptions());

                Assert.Equal(
                    SdpNormaliser.NormaliseCompact(offer1.sdp),
                    SdpNormaliser.NormaliseCompact(offer2.sdp));
            }
        }

        /// <summary>
        /// A track explicitly set to Inactive (via DefaultStreamStatus) is
        /// reverted to its default direction when createOffer runs. The
        /// current implementation does this on line 840 of RTCPeerConnection,
        /// which is easy to lose track of in a refactor.
        /// </summary>
        [Fact]
        public void InactiveLocalTrack_RevertsToDefaultStreamStatusOnOffer()
        {
            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                // Force the audio track inactive after addTrack.
                pc.AudioStream.LocalTrack.StreamStatus = MediaStreamStatusEnum.Inactive;
                Assert.Equal(MediaStreamStatusEnum.Inactive, pc.AudioStream.LocalTrack.StreamStatus);

                pc.createOffer(new RTCOfferOptions());

                // DefaultStreamStatus for a builder-created audio track is SendRecv.
                Assert.Equal(MediaStreamStatusEnum.SendRecv, pc.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// The X_ExcludeIceCandidates offer option suppresses a=candidate
        /// lines in the produced SDP. Used by callers that want to gather
        /// candidates out-of-band.
        /// </summary>
        [Fact]
        public void OfferOptions_ExcludeIceCandidates_ProducesSdpWithNoCandidateLines()
        {
            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions { X_ExcludeIceCandidates = true });

                Assert.DoesNotContain("a=candidate:", offerInit.sdp);
            }
        }
    }
}
