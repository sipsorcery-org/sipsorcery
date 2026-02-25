//-----------------------------------------------------------------------------
// Filename: RTPSessionRtcpLifecycleUnitTest.cs
//
// Description: Characterization tests for the RTCP session lifecycle
// driven by SetRemoteDescription:
//   - RTCP session is created on addTrack
//   - Closed when the remote sets direction Inactive in a re-INVITE
//   - Closed when the remote sends a hold form (c=IN IP4 0.0.0.0)
//   - NOT closed when the remote stays active
//   - WebRTC always emits a=rtcp-mux on every m-line
//   - Plain RTPSession does NOT add a=rtcp-mux by default
//
// Category 9 in the SDP-refactor test plan. Complements the existing
// RTPSessionRenegotiationUnitTest (port=0 rejection) — this file
// covers the other "remote unavailable" signals that should also
// close RTCP.
//
// History:
// 23 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionRtcpLifecycleUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionRtcpLifecycleUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// addTrack triggers InitMediaStream which creates an RTCP
        /// session. Before any remote description is set, the RTCP
        /// session exists and is not closed.
        /// </summary>
        [Fact]
        public void AddTrack_CreatesRtcpSession()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                Assert.NotNull(session.AudioStream.RtcpSession);
                Assert.False(session.AudioStream.RtcpSession.IsClosed);
            }
        }

        /// <summary>
        /// When a re-INVITE flips the audio direction to Inactive, the
        /// audio RTCP session must close. This is the
        /// LocalTrack.StreamStatus == Inactive branch of the close-on-
        /// inactive logic at RTPSession.cs:1269-1275 — separate from
        /// the port=0 rejection branch covered by
        /// RTPSessionRenegotiationUnitTest.
        /// </summary>
        [Fact]
        public void ReInviteWithInactiveDirection_ClosesAudioRtcpSession()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                // Initial offer: full audio.
                SDP firstOffer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, firstOffer));
                Assert.False(session.AudioStream.RtcpSession.IsClosed);

                // Re-INVITE: same media, direction = inactive.
                SDP inactiveReoffer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferInactive);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, inactiveReoffer));

                Assert.True(session.AudioStream.RtcpSession.IsClosed,
                    "Audio RTCP session must be closed after remote sets direction Inactive.");
            }
        }

        /// <summary>
        /// Classic SIP hold (c=IN IP4 0.0.0.0) flips the local track to
        /// Inactive (via the SetLocalTrackStreamStatus Any-address
        /// rule) which in turn closes the RTCP session. Same close-on-
        /// inactive path, different trigger.
        /// </summary>
        [Fact]
        public void ReInviteWithHoldConnectionAddress_ClosesAudioRtcpSession()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP firstOffer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, firstOffer));
                Assert.False(session.AudioStream.RtcpSession.IsClosed);

                SDP holdReoffer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferHoldNullConnectionAddress);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, holdReoffer));

                Assert.True(session.AudioStream.RtcpSession.IsClosed,
                    "Audio RTCP session must be closed after remote sends hold form.");
            }
        }

        /// <summary>
        /// When a re-INVITE keeps the media active (no direction change,
        /// no port=0, no hold), the existing RTCP session must remain
        /// open. Guards against an over-eager refactor that closes on
        /// any renegotiation.
        /// </summary>
        [Fact]
        public void ReInviteKeepingActive_LeavesAudioRtcpSessionOpen()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP firstOffer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, firstOffer));

                // Reapply the same offer.
                SDP secondOffer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, secondOffer));

                Assert.False(session.AudioStream.RtcpSession.IsClosed);
            }
        }

        /// <summary>
        /// In an audio+video session where the re-INVITE rejects only
        /// the video, the audio RTCP session must stay open while the
        /// video RTCP session closes. Confirms per-stream independence
        /// of the close-on-inactive logic.
        /// </summary>
        [Fact]
        public void ReInviteRejectsVideoOnly_AudioRtcpStaysOpenVideoRtcpCloses()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP firstOffer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, firstOffer));
                Assert.False(session.AudioStream.RtcpSession.IsClosed);
                Assert.False(session.VideoStream.RtcpSession.IsClosed);

                SDP reject = SDP.ParseSDPDescription(SdpFixtures.ReInviteRejectsVideoPortZero);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, reject));

                Assert.False(session.AudioStream.RtcpSession.IsClosed,
                    "Audio RTCP must stay open when only video is rejected.");
                Assert.True(session.VideoStream.RtcpSession.IsClosed,
                    "Video RTCP must close when video is rejected.");
            }
        }

        /// <summary>
        /// RTCPeerConnection always emits a=rtcp-mux on every m-line of
        /// every offer — the WebRTC mandate. SIPSorcery hard-codes this
        /// via RTCP_MUX_ATTRIBUTE in createBaseSdp regardless of any
        /// configuration toggle.
        /// </summary>
        [Fact]
        public void RtcPeerConnectionOffer_AlwaysEmitsRtcpMuxOnEveryMline()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                var offerInit = pc.createOffer(new RTCOfferOptions());

                int count = 0;
                int idx = 0;
                while ((idx = offerInit.sdp.IndexOf("a=rtcp-mux", idx, System.StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    idx += "a=rtcp-mux".Length;
                }
                Assert.Equal(2, count);
            }
        }

        /// <summary>
        /// Plain RTPSession.CreateOffer does NOT emit a=rtcp-mux by
        /// default — it is a WebRTC-specific concept. Confirms the
        /// asymmetry between the two surfaces so a refactor that
        /// accidentally adds rtcp-mux to the plain offer gets flagged.
        /// </summary>
        [Fact]
        public void RtpSessionOffer_DoesNotEmitRtcpMuxByDefault()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP sdp = session.CreateOffer(System.Net.IPAddress.Loopback);

                Assert.DoesNotContain("a=rtcp-mux", sdp.ToString());
            }
        }
    }
}
