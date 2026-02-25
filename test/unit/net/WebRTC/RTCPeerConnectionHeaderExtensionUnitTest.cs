//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionHeaderExtensionUnitTest.cs
//
// Description: Characterization tests for RTP header-extension
// negotiation in RTCPeerConnection — the intersection-with-id-reuse
// logic in createOffer / setRemoteDescription / createAnswer for
// a=extmap lines.
//
// Category 6 in the SDP-refactor test plan.
//
// Known extension URIs in the static registry
// (RTPHeaderExtension.GetRTPHeaderExtension):
//   AbsSendTime      — abs-send-time, any media
//   AudioLevel       — ssrc-audio-level, audio-only
//   CVO              — urn:3gpp:video-orientation, video-only
//   TransportWideCC  — transport-wide-cc-extensions-01, any media
//
// Anything outside this set is silently dropped by the SDP parser, so
// the negotiation surface for header extensions is bounded to these four.
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
    public class RTCPeerConnectionHeaderExtensionUnitTest
    {
        private readonly ILogger logger;

        public RTCPeerConnectionHeaderExtensionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        // ----- track-construction helpers -----

        private static MediaStreamTrack AudioTrackWithExtensions(
            params (int id, string uri)[] extensions)
        {
            var extDict = new Dictionary<int, RTPHeaderExtension>();
            foreach (var (id, uri) in extensions)
            {
                var ext = RTPHeaderExtension.GetRTPHeaderExtension(id, uri, SDPMediaTypesEnum.audio);
                if (ext != null) { extDict[id] = ext; }
            }
            return new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                },
                MediaStreamStatusEnum.SendRecv,
                null,
                extDict);
        }

        private static MediaStreamTrack VideoTrackWithExtensions(
            params (int id, string uri)[] extensions)
        {
            var extDict = new Dictionary<int, RTPHeaderExtension>();
            foreach (var (id, uri) in extensions)
            {
                var ext = RTPHeaderExtension.GetRTPHeaderExtension(id, uri, SDPMediaTypesEnum.video);
                if (ext != null) { extDict[id] = ext; }
            }
            return new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000),
                },
                MediaStreamStatusEnum.SendRecv,
                null,
                extDict);
        }

        // ----- tests -----

        /// <summary>
        /// When a local track is configured with an AbsSendTime header
        /// extension, the produced offer SDP must contain a matching
        /// a=extmap line on the audio m-line.
        /// </summary>
        [Fact]
        public void Offer_LocalAudioExtension_EmittedAsExtmap()
        {
            var localAudio = AudioTrackWithExtensions(
                (2, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI));

            using (var pc = new PeerConnectionBuilder().WithTrack(localAudio).Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                Assert.Contains(
                    $"a=extmap:2 {AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI}",
                    offerInit.sdp);
            }
        }

        /// <summary>
        /// When the remote offer carries an unknown extension URI (one
        /// not in the static registry), the SDP parser silently drops it.
        /// It does NOT appear in the announcement's HeaderExtensions and
        /// does NOT appear in the answer.
        /// </summary>
        [Fact]
        public void SetRemoteDescription_UnknownExtensionUri_SilentlyDropped()
        {
            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                // toffset is NOT in the registry.
                string offerSdp =
@"v=0
o=- 4000 0 IN IP4 0.0.0.0
s=-
t=0 0
a=group:BUNDLE 0
m=audio 9 UDP/TLS/RTP/SAVPF 0
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:0 PCMU/8000
a=extmap:7 urn:ietf:params:rtp-hdrext:toffset";

                SDP parsed = SDP.ParseSDPDescription(offerSdp);
                Assert.Empty(parsed.Media[0].HeaderExtensions);

                pc.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = offerSdp
                });

                var answerInit = pc.createAnswer();
                Assert.DoesNotContain("urn:ietf:params:rtp-hdrext:toffset", answerInit.sdp);
            }
        }

        /// <summary>
        /// AudioLevel is registered as audio-only. If a remote offer lists
        /// it on a VIDEO m-line, the parser drops it (IsMediaSupported
        /// check). The answer for that video m-line does not carry it.
        /// </summary>
        [Fact]
        public void SetRemoteDescription_AudioOnlyExtensionOnVideoMline_Dropped()
        {
            string offerSdp =
@"v=0
o=- 4001 0 IN IP4 0.0.0.0
s=-
t=0 0
a=group:BUNDLE 0
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
a=extmap:1 urn:ietf:params:rtp-hdrext:ssrc-audio-level";

            SDP parsed = SDP.ParseSDPDescription(offerSdp);
            Assert.Empty(parsed.Media[0].HeaderExtensions);
        }

        /// <summary>
        /// When the remote offer has an extension URI we also have on the
        /// local track (with the same ID), the answer includes the
        /// extension at that ID.
        /// </summary>
        [Fact]
        public void Answer_SharedExtensionSameId_IncludedAtThatId()
        {
            var localAudio = AudioTrackWithExtensions(
                (2, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI));

            using (var pc = new PeerConnectionBuilder().WithTrack(localAudio).Build())
            {
                string offerSdp = MinimalAudioOfferWithExtmap(2, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI);

                pc.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = offerSdp
                });

                var answerInit = pc.createAnswer();
                Assert.Contains(
                    $"a=extmap:2 {AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI}",
                    answerInit.sdp);
            }
        }

        /// <summary>
        /// When local and remote both have the same extension URI but use
        /// DIFFERENT IDs, the answer reuses the REMOTE's ID — the
        /// _rtpExtensionsUsed registry was seeded by setRemoteDescription
        /// to the remote's ID, and createAnswer mutates the local
        /// extension's ID to match.
        /// </summary>
        [Fact]
        public void Answer_ExtensionIdConflict_AnswerUsesRemoteId()
        {
            // Local: id=7
            var localAudio = AudioTrackWithExtensions(
                (7, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI));

            using (var pc = new PeerConnectionBuilder().WithTrack(localAudio).Build())
            {
                // Remote: id=2
                string offerSdp = MinimalAudioOfferWithExtmap(2, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI);

                pc.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = offerSdp
                });

                var answerInit = pc.createAnswer();

                // Answer must use remote's id (2), not local's original (7).
                Assert.Contains(
                    $"a=extmap:2 {AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI}",
                    answerInit.sdp);
                Assert.DoesNotContain(
                    $"a=extmap:7 {AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI}",
                    answerInit.sdp);
            }
        }

        /// <summary>
        /// When the remote offers an extension we don't have locally, the
        /// answer does NOT include it. createAnswer's loop walks the
        /// remote extensions but only adds those for which there's a
        /// FirstOrDefault local match by URI.
        /// </summary>
        [Fact]
        public void Answer_RemoteOffersExtensionLocalDoesNotHave_NotInAnswer()
        {
            // Local: no extensions configured.
            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                string offerSdp = MinimalAudioOfferWithExtmap(2, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI);

                pc.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = offerSdp
                });

                var answerInit = pc.createAnswer();

                Assert.DoesNotContain("a=extmap:", answerInit.sdp);
            }
        }

        /// <summary>
        /// When the local side has an extension but the remote OFFER
        /// doesn't list it, the answer does NOT include the extension —
        /// even though local supports it. The answerer's job is to
        /// respond to what the offerer asked for, not advertise extras.
        /// </summary>
        [Fact]
        public void Answer_LocalHasExtensionRemoteOfferDoesNot_NotInAnswer()
        {
            var localAudio = AudioTrackWithExtensions(
                (2, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI));

            using (var pc = new PeerConnectionBuilder().WithTrack(localAudio).Build())
            {
                // Remote offer with NO extmap lines.
                string offerSdp =
@"v=0
o=- 4000 0 IN IP4 0.0.0.0
s=-
t=0 0
a=group:BUNDLE 0
m=audio 9 UDP/TLS/RTP/SAVPF 0
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:0 PCMU/8000";

                pc.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = offerSdp
                });

                var answerInit = pc.createAnswer();
                Assert.DoesNotContain("a=extmap:", answerInit.sdp);
            }
        }

        /// <summary>
        /// When both audio and video tracks reference the SAME extension
        /// URI (e.g. abs-send-time), the same ID is used for both m-lines
        /// in the resulting offer — the _rtpExtensionsUsed registry
        /// ensures URI-to-ID is a one-to-one mapping across the session.
        /// </summary>
        [Fact]
        public void Offer_AudioAndVideoShareExtensionUri_SharedIdAcrossMLines()
        {
            var localAudio = AudioTrackWithExtensions(
                (2, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI));
            var localVideo = VideoTrackWithExtensions(
                (2, AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI));

            using (var pc = new PeerConnectionBuilder()
                .WithTrack(localAudio)
                .WithTrack(localVideo)
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                int count = CountOccurrences(
                    offerInit.sdp,
                    $"a=extmap:2 {AbsSendTimeExtension.RTP_HEADER_EXTENSION_URI}");

                // Once on the audio m-line, once on the video m-line.
                Assert.Equal(2, count);
            }
        }

        // ----- inline SDP helper -----

        private static string MinimalAudioOfferWithExtmap(int extId, string extUri) =>
$@"v=0
o=- 4000 0 IN IP4 0.0.0.0
s=-
t=0 0
a=group:BUNDLE 0
m=audio 9 UDP/TLS/RTP/SAVPF 0
c=IN IP4 0.0.0.0
a=rtcp:9 IN IP4 0.0.0.0
a=ice-ufrag:abcd
a=ice-pwd:abcdefghijklmnopqrstuvwx
a=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF
a=setup:actpass
a=mid:0
a=sendrecv
a=rtcp-mux
a=rtpmap:0 PCMU/8000
a=extmap:{extId} {extUri}";

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}
