//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionBundleIceCandidateUnitTest.cs
//
// Description: Tests that the ICE candidate attributes in a generated offer or
// answer are placed on the m-line matching the BUNDLE-tag (the first
// identification-tag in the a=group:BUNDLE list) as required by RFC 8843,
// rather than on whichever media announcement happens to be generated first.
//
// Regression coverage for issue #1763: the local media stream list is always
// ordered audio, video, text, so when a remote offer put video in the first
// m-line the candidates ended up on the audio (non-tagged) section and
// browsers ignored them.
//
// History:
// 27 Oct 2026	Claude Code - Opus 5	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionBundleIceCandidateUnitTest
    {
        private readonly ILogger logger;

        public RTCPeerConnectionBundleIceCandidateUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// A remote offer that lists video as its first bundled m-line. The
        /// BUNDLE-tag is therefore "0", the video section.
        /// </summary>
        private const string VideoFirstRemoteOffer =
@"v=0
o=- 4000 0 IN IP4 0.0.0.0
s=-
t=0 0
a=group:BUNDLE 0 1
m=video 9 UDP/TLS/RTP/SAVPF 96
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:96 VP8/90000
m=audio 9 UDP/TLS/RTP/SAVPF 0
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:1
a=sendrecv
a=rtcp-mux
a=rtpmap:0 PCMU/8000";

        /// <summary>
        /// Sanity check for the rest of the fixtures in this class: a locally
        /// generated offer does carry at least one a=candidate attribute, so the
        /// "which m-line are the candidates on" assertions below are not
        /// vacuously true.
        /// </summary>
        [Fact]
        public void LocalOfferCarriesIceCandidates()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                Assert.Contains("a=candidate:", offerInit.sdp);
            }
        }

        /// <summary>
        /// When the remote offer puts video in the first m-line the answer's
        /// candidates must go on that video section, because it is the BUNDLE-tag.
        /// Before the fix for issue #1763 they were placed on the audio section
        /// (mid "1") because the local media stream list is always iterated
        /// audio-first.
        /// </summary>
        [Fact]
        public void VideoFirstRemoteOffer_AnswerPlacesCandidatesOnBundleTagMediaLine()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var answerer = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                Assert.Equal(SetDescriptionResultEnum.OK,
                    answerer.setRemoteDescription(new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.offer,
                        sdp = VideoFirstRemoteOffer
                    }));

                var answerInit = answerer.createAnswer();
                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);

                Assert.Equal("BUNDLE 0 1", answerSdp.Group);
                SdpAssert.HasMid(SdpAssert.Video(answerSdp), "0");
                SdpAssert.HasMid(SdpAssert.Audio(answerSdp), "1");

                Assert.NotEmpty(SdpAssert.Video(answerSdp).IceCandidates);
                Assert.Null(SdpAssert.Audio(answerSdp).IceCandidates);
            }
        }

        /// <summary>
        /// The audio-first case, which worked by accident before the fix, must keep
        /// working: the candidates stay on the audio section when that is the
        /// BUNDLE-tag.
        /// </summary>
        [Fact]
        public void AudioFirstRemoteOffer_AnswerPlacesCandidatesOnAudioMediaLine()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

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
                Assert.Equal(SetDescriptionResultEnum.OK, answerer.setRemoteDescription(offer));

                var answerInit = answerer.createAnswer();
                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);

                Assert.Equal("BUNDLE 0 1", answerSdp.Group);
                SdpAssert.HasMid(SdpAssert.Audio(answerSdp), "0");

                Assert.NotEmpty(SdpAssert.Audio(answerSdp).IceCandidates);
                Assert.Null(SdpAssert.Video(answerSdp).IceCandidates);
            }
        }

        /// <summary>
        /// The structural invariant: exactly one m-line carries the a=candidate
        /// attributes and its mid is the first tag listed in the BUNDLE group.
        /// </summary>
        [Fact]
        public void VideoFirstRemoteOffer_ExactlyOneMediaLineCarriesCandidatesAndItIsTheBundleTag()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var answerer = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                answerer.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = VideoFirstRemoteOffer
                });

                var answerInit = answerer.createAnswer();
                SDP answerSdp = SdpAssert.Parse(answerInit.sdp);

                string bundleTag = answerSdp.Group.Substring("BUNDLE ".Length).Split(' ').First();
                var withCandidates = answerSdp.Media.Where(x => x.IceCandidates?.Count > 0).ToList();

                Assert.Single(withCandidates);
                Assert.Equal(bundleTag, withCandidates[0].MediaID);
            }
        }

        /// <summary>
        /// X_ExcludeIceCandidates still suppresses every a=candidate attribute when
        /// the BUNDLE-tag is not the audio section.
        /// </summary>
        [Fact]
        public void VideoFirstRemoteOffer_ExcludeIceCandidates_ProducesSdpWithNoCandidateLines()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var answerer = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                answerer.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = VideoFirstRemoteOffer
                });

                var answerInit = answerer.createAnswer(new RTCAnswerOptions { X_ExcludeIceCandidates = true });

                Assert.DoesNotContain("a=candidate:", answerInit.sdp);
            }
        }
    }
}
