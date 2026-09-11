//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionBundleAndMidUnitTest.cs
//
// Description: Characterization tests for the BUNDLE group and per-m-line
// MediaID ("mid") handling in RTCPeerConnection. Covers:
//
//   - a=group:BUNDLE attribute construction in offers
//   - mid values assigned to m-lines in initial offers
//   - mid values mirrored from a remote offer in the answer
//   - mid assignment when a new m-line is appended on re-negotiation
//
// Category 8 in the SDP-refactor test plan.
//
// History:
// 23 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionBundleAndMidUnitTest
    {
        private readonly ILogger logger;

        public RTCPeerConnectionBundleAndMidUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// An audio-only offer declares a BUNDLE group containing the
        /// single mid value of the audio m-line. The session-level
        /// Group attribute always begins with the literal "BUNDLE "
        /// followed by space-separated mid tags.
        /// </summary>
        [Fact]
        public void AudioOnlyOffer_BundleGroupContainsSingleMid()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());
                SDP sdp = SdpAssert.Parse(offerInit.sdp);

                Assert.Equal("BUNDLE 0", sdp.Group);
                SdpAssert.HasMid(SdpAssert.Audio(sdp), "0");
            }
        }

        /// <summary>
        /// An audio+video offer bundles both m-lines: Group is
        /// "BUNDLE 0 1" and each m-line carries its corresponding mid.
        /// The mid values run sequentially from the first m-line.
        /// </summary>
        [Fact]
        public void AudioVideoOffer_BundleGroupContainsBothMidsInOrder()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());
                SDP sdp = SdpAssert.Parse(offerInit.sdp);

                Assert.Equal("BUNDLE 0 1", sdp.Group);
                SdpAssert.HasMid(SdpAssert.Audio(sdp), "0");
                SdpAssert.HasMid(SdpAssert.Video(sdp), "1");
            }
        }

        /// <summary>
        /// Each m-line in the offer must have a mid value that appears
        /// in the BUNDLE group. This is the structural invariant the
        /// group attribute exists to enforce.
        /// </summary>
        [Fact]
        public void Offer_EveryMediaLineMidAppearsInBundleGroup()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());
                SDP sdp = SdpAssert.Parse(offerInit.sdp);

                var groupMids = sdp.Group.Substring("BUNDLE ".Length).Split(' ');
                foreach (var media in sdp.Media)
                {
                    Assert.NotNull(media.MediaID);
                    Assert.Contains(media.MediaID, groupMids);
                }
            }
        }

        /// <summary>
        /// A peer connection with no tracks at all still produces an
        /// offer init (covered by the offer-generation tests) — and the
        /// SDP has NO BUNDLE group line, because there are no m-lines
        /// to bundle. The Group attribute stays null/empty.
        /// </summary>
        [Fact]
        public void NoTracksOffer_NoBundleGroupEmitted()
        {
            using (var pc = new PeerConnectionBuilder().Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());
                SDP sdp = SdpAssert.Parse(offerInit.sdp);

                Assert.Empty(sdp.Media);
                Assert.True(string.IsNullOrEmpty(sdp.Group));
            }
        }

        /// <summary>
        /// The answer's BUNDLE group lists the same mid values the
        /// remote offer used. createAnswer mirrors the offerer's mid
        /// scheme, not its own internal numbering.
        /// </summary>
        [Fact]
        public void AnswerMirrorsRemoteMidValues()
        {
            // Build a remote offer with non-numeric mid strings — the
            // answer must echo them verbatim, not substitute "0"/"1".
            string remoteOffer =
@"v=0
o=- 4000 0 IN IP4 0.0.0.0
s=-
t=0 0
a=group:BUNDLE audio-mid video-mid
m=audio 9 UDP/TLS/RTP/SAVPF 0
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:audio-mid
a=sendrecv
a=rtcp-mux
a=rtpmap:0 PCMU/8000
m=video 9 UDP/TLS/RTP/SAVPF 96
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:video-mid
a=sendrecv
a=rtcp-mux
a=rtpmap:96 VP8/90000";

            using (var answerer = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                answerer.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = remoteOffer
                });

                var answerInit = answerer.createAnswer();
                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);

                SdpAssert.HasMid(SdpAssert.Audio(answerSdp), "audio-mid");
                SdpAssert.HasMid(SdpAssert.Video(answerSdp), "video-mid");
                Assert.Equal("BUNDLE audio-mid video-mid", answerSdp.Group);
            }
        }

        /// <summary>
        /// When a re-offer follows a prior negotiation that already had
        /// an audio m-line, and a NEW video track has been added to the
        /// local peer, the re-offer must:
        ///   - preserve the original audio mid
        ///   - append the new video m-line with mid = next available
        ///     index (i.e. the remote's previous Media.Count)
        ///   - extend the BUNDLE group to include both
        /// Lines up with the "RFC 3264 §8 preserves m-line ordering"
        /// note in RTCPeerConnection.createBaseSdp.
        /// </summary>
        [Fact]
        public void RenegotiationWithNewVideoTrack_AppendsMidAtEnd()
        {
            // Round one: audio only on both sides.
            using (var offerer = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            using (var answerer = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                var firstOffer = offerer.createOffer(new RTCOfferOptions());
                Assert.Equal(SetDescriptionResultEnum.OK,
                    answerer.setRemoteDescription(firstOffer));
                var firstAnswer = answerer.createAnswer();
                Assert.Equal(SetDescriptionResultEnum.OK,
                    offerer.setRemoteDescription(firstAnswer));

                // Add a video track on the offerer side, then re-offer.
                var videoTrack = new MediaStreamTrack(
                    SDPMediaTypesEnum.video, false,
                    new List<SDPAudioVideoMediaFormat>
                    {
                        new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000),
                    });
                offerer.addTrack(videoTrack);

                var reOffer = offerer.createOffer(new RTCOfferOptions());
                SDP reOfferSdp = SdpAssert.Parse(reOffer.sdp);

                SdpAssert.HasMediaLines(reOfferSdp, "audio", "video");
                SdpAssert.HasMid(SdpAssert.Audio(reOfferSdp), "0");
                SdpAssert.HasMid(SdpAssert.Video(reOfferSdp), "1");
                Assert.Equal("BUNDLE 0 1", reOfferSdp.Group);
            }
        }

        /// <summary>
        /// The BUNDLE group ordering follows the underlying m-line
        /// ordering. Since RTCPeerConnection.createOffer canonicalises
        /// to audio-first (see existing CategoryC 2 test
        /// VideoThenAudio_RtcPeerConnectionStillEmitsAudioFirst), the
        /// bundle group is "BUNDLE 0 1" with audio="0" and video="1"
        /// regardless of which track was added to the peer first.
        /// </summary>
        [Fact]
        public void VideoThenAudio_BundleGroupStillListsAudioMidFirst()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithVideoTrack(96, "VP8", 90000)
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());
                SDP sdp = SdpAssert.Parse(offerInit.sdp);

                // Audio appears first (canonicalisation), so its mid is 0.
                SdpAssert.HasMediaLines(sdp, "audio", "video");
                SdpAssert.HasMid(SdpAssert.Audio(sdp), "0");
                SdpAssert.HasMid(SdpAssert.Video(sdp), "1");
                Assert.Equal("BUNDLE 0 1", sdp.Group);
            }
        }
    }
}
