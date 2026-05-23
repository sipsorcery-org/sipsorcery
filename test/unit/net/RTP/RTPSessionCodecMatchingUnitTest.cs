//-----------------------------------------------------------------------------
// Filename: RTPSessionCodecMatchingUnitTest.cs
//
// Description: Integration-level characterization tests for codec /
// payload-type matching as exercised through
// RTPSession.SetRemoteDescription. Locks down the OBSERVABLE effect on
// the session's local tracks after a remote offer is set:
//
//   - LocalTrack.Capabilities becomes the intersection of local + remote
//   - Capabilities ordering follows the offerer's priority
//   - Telephone-event PT gets adjusted to match the remote's PT
//   - No common codec → media-type-specific Incompatible return value
//
// Category 4 in the SDP-refactor test plan. The pure-helper counterpart
// is SDPAudioVideoMediaFormatMatchingUnitTest.
//
// History:
// 22 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionCodecMatchingUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionCodecMatchingUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// When local advertises PCMU+PCMA and remote offers only PCMA,
        /// the local track's "voice" capabilities collapse to PCMA only.
        /// (Audio tracks also carry a telephone-event capability — either
        /// a DefaultRTPEventFormat injected by SetRemoteDescription or
        /// the negotiated one — so the raw Capabilities count is 2; this
        /// test filters that out to focus on the voice intersection.)
        /// </summary>
        [Fact]
        public void IntersectsLocalSupersetWithRemoteSubset()
        {
            var localTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                });

            using (var session = new RtpSessionBuilder().WithTrack(localTrack).Build())
            {
                // Offer with PCMA only (PT 8).
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 8
a=rtpmap:8 PCMA/8000
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                var voiceCaps = session.AudioStream.LocalTrack.Capabilities
                    .Where(c => c.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE)
                    .ToList();
                Assert.Single(voiceCaps);
                Assert.Equal("PCMA", voiceCaps[0].Name());
                Assert.Equal(8, voiceCaps[0].ID);
            }
        }

        /// <summary>
        /// Sister-fact to the above: when local has no telephone-event in
        /// its capabilities and the remote offer doesn't either,
        /// SetRemoteDescription still injects a DefaultRTPEventFormat
        /// entry into the local track. This is documented behaviour worth
        /// locking down so a refactor doesn't quietly drop the default.
        /// </summary>
        [Fact]
        public void NoTelephoneEventOnEitherSide_StillInjectsDefault()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                bool hasTelephoneEvent = session.AudioStream.LocalTrack.Capabilities
                    .Any(c => c.Name().ToLower() == SDP.TELEPHONE_EVENT_ATTRIBUTE);
                Assert.True(hasTelephoneEvent,
                    "Expected DefaultRTPEventFormat to be injected when neither side advertises telephone-event.");
            }
        }

        /// <summary>
        /// When local and remote have NO common audio codec (excluding
        /// telephone-event), SetRemoteDescription returns AudioIncompatible.
        /// </summary>
        [Fact]
        public void NoCommonAudioCodec_ReturnsAudioIncompatible()
        {
            // Local: PCMU only.
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                // Remote: G.722 only.
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 9
a=rtpmap:9 G722/8000
a=sendrecv");

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.AudioIncompatible, result);
            }
        }

        /// <summary>
        /// When the only common audio format is telephone-event (DTMF),
        /// the negotiation is still AudioIncompatible — telephone-event
        /// is excluded from the "common codec" count by design.
        /// </summary>
        [Fact]
        public void OnlyTelephoneEventInCommon_ReturnsAudioIncompatible()
        {
            // Local: PCMU + telephone-event.
            var localTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 101, "telephone-event", 8000),
                });

            using (var session = new RtpSessionBuilder().WithTrack(localTrack).Build())
            {
                // Remote: G.722 + telephone-event (no PCMU).
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 9 101
a=rtpmap:9 G722/8000
a=rtpmap:101 telephone-event/8000
a=fmtp:101 0-15
a=sendrecv");

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.AudioIncompatible, result);
            }
        }

        /// <summary>
        /// When local has telephone-event at PT 101 but the remote
        /// announces telephone-event at PT 100, after SetRemoteDescription
        /// the local track's telephone-event capability is at PT 100 to
        /// match the remote. The session's NegotiatedRtpEventPayloadID is
        /// also set to 100.
        /// </summary>
        [Fact]
        public void TelephoneEventPayloadIdAdjustsToMatchRemote()
        {
            var localTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 101, "telephone-event", 8000),
                });

            using (var session = new RtpSessionBuilder().WithTrack(localTrack).Build())
            {
                // Remote: PCMU + telephone-event at PT 100 (different from local's 101).
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 0 100
a=rtpmap:0 PCMU/8000
a=rtpmap:100 telephone-event/8000
a=fmtp:100 0-15
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                // The local track's telephone-event should now use PT 100.
                var teCap = session.AudioStream.LocalTrack.Capabilities
                    .FirstOrDefault(c => c.Name().ToLower() == SDP.TELEPHONE_EVENT_ATTRIBUTE);
                Assert.False(teCap.IsEmpty());
                Assert.Equal(100, teCap.ID);

                Assert.Equal(100, session.AudioStream.NegotiatedRtpEventPayloadID);
            }
        }

        /// <summary>
        /// When local has telephone-event at PT 101 and remote announces
        /// telephone-event at the same PT 101, the local capability stays
        /// at 101 (no adjustment needed) and NegotiatedRtpEventPayloadID
        /// is 101.
        /// </summary>
        [Fact]
        public void TelephoneEventSamePayloadId_IsPreserved()
        {
            var localTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.audio, 101, "telephone-event", 8000),
                });

            using (var session = new RtpSessionBuilder().WithTrack(localTrack).Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferPcmuWithDtmf);

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                var teCap = session.AudioStream.LocalTrack.Capabilities
                    .FirstOrDefault(c => c.Name().ToLower() == SDP.TELEPHONE_EVENT_ATTRIBUTE);
                Assert.False(teCap.IsEmpty());
                Assert.Equal(101, teCap.ID);
                Assert.Equal(101, session.AudioStream.NegotiatedRtpEventPayloadID);
            }
        }

        /// <summary>
        /// When remote offers PCMA before PCMU, the local capability
        /// order after SetRemoteDescription puts PCMA first — even if
        /// the local track was originally configured PCMU-first. The
        /// offerer's order wins for an offer (sdpType=offer makes remote
        /// the priority key).
        /// </summary>
        [Fact]
        public void OnOffer_RemoteOrderWinsAsPriority()
        {
            // Local: PCMU first, PCMA second.
            var localTrack = new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                });

            using (var session = new RtpSessionBuilder().WithTrack(localTrack).Build())
            {
                // Remote: PCMA first, PCMU second.
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20000 RTP/AVP 8 0
a=rtpmap:0 PCMU/8000
a=rtpmap:8 PCMA/8000
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                // PCMA must lead the local capability list because the
                // offerer (remote) put it first.
                var caps = session.AudioStream.LocalTrack.Capabilities
                    .Where(c => c.Name().ToLower() != SDP.TELEPHONE_EVENT_ATTRIBUTE)
                    .ToList();
                Assert.Equal(2, caps.Count);
                Assert.Equal("PCMA", caps[0].Name());
                Assert.Equal("PCMU", caps[1].Name());
            }
        }

        /// <summary>
        /// When remote offers a video codec the local doesn't support
        /// (e.g. local has VP8, remote offers H264 only), the answer
        /// returns VideoIncompatible.
        /// </summary>
        [Fact]
        public void NoCommonVideoCodec_ReturnsVideoIncompatible()
        {
            using (var session = new RtpSessionBuilder()
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=video 20000 RTP/AVP 96
a=rtpmap:96 H264/90000
a=sendrecv");

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.VideoIncompatible, result);
            }
        }

        /// <summary>
        /// Dynamic-PT video codec matching is by rtpmap (codec name +
        /// clock rate), not by the PT integer. Local VP8 at PT 96 vs
        /// remote VP8 at PT 97 still matches and the negotiation
        /// succeeds.
        /// </summary>
        [Fact]
        public void DynamicVideoPtMismatchSameRtpmap_StillMatches()
        {
            using (var session = new RtpSessionBuilder()
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=video 20000 RTP/AVP 97
a=rtpmap:97 VP8/90000
a=sendrecv");

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.OK, result);
            }
        }
    }
}
