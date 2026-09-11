//-----------------------------------------------------------------------------
// Filename: RTPSessionRenegotiationAdvancedUnitTest.cs
//
// Description: Characterization tests for the deeper renegotiation
// behaviours of RTPSession — specifically the SDP AnnouncementVersion
// increment policy and the direction-mirroring rules in
// SetLocalTrackStreamStatus that fire during SetRemoteDescription.
//
// Category 7 in the SDP-refactor test plan. Complements the existing
// RTPSessionRenegotiationUnitTest (RTCP-session lifecycle on rejection)
// and RTCPeerConnectionRenegotiationUnitTest (m-line append on re-offer,
// multiplexed-endpoint preservation).
//
// History:
// 23 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPSessionRenegotiationAdvancedUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionRenegotiationAdvancedUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// The first SDP produced by CreateOffer has an
        /// AnnouncementVersion of 0 (the initial value of
        /// m_sdpAnnouncementVersion).
        /// </summary>
        [Fact]
        public void FirstOffer_HasAnnouncementVersionZero()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Loopback);

                Assert.NotNull(sdp);
                Assert.Equal(0UL, sdp.AnnouncementVersion);
            }
        }

        /// <summary>
        /// Calling CreateOffer twice without any SetMediaStreamStatus
        /// between calls does NOT increment the AnnouncementVersion.
        /// This documents a known limitation of the current
        /// implementation: per RFC 3264 §8 the version SHOULD increment
        /// whenever the SDP changes, but the current code only bumps it
        /// in SetMediaStreamStatus. A refactor that "fixes" this is a
        /// behaviour change worth flagging.
        /// </summary>
        [Fact]
        public void TwoOffersWithoutChanges_AnnouncementVersionDoesNotIncrement()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP first = session.CreateOffer(IPAddress.Loopback);
                SDP second = session.CreateOffer(IPAddress.Loopback);

                Assert.Equal(first.AnnouncementVersion, second.AnnouncementVersion);
            }
        }

        /// <summary>
        /// SetMediaStreamStatus on the audio track increments
        /// AnnouncementVersion. The next CreateOffer sees the bumped
        /// value.
        /// </summary>
        [Fact]
        public void SetMediaStreamStatusAudio_IncrementsAnnouncementVersion()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP first = session.CreateOffer(IPAddress.Loopback);
                Assert.Equal(0UL, first.AnnouncementVersion);

                session.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.SendOnly);

                SDP second = session.CreateOffer(IPAddress.Loopback);
                Assert.Equal(1UL, second.AnnouncementVersion);
            }
        }

        /// <summary>
        /// SetMediaStreamStatus on the video track also increments
        /// AnnouncementVersion. Mirror of the audio test for the video
        /// branch of SetMediaStreamStatus.
        /// </summary>
        [Fact]
        public void SetMediaStreamStatusVideo_IncrementsAnnouncementVersion()
        {
            using (var session = new RtpSessionBuilder()
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                SDP first = session.CreateOffer(IPAddress.Loopback);
                Assert.Equal(0UL, first.AnnouncementVersion);

                session.SetMediaStreamStatus(SDPMediaTypesEnum.video, MediaStreamStatusEnum.RecvOnly);

                SDP second = session.CreateOffer(IPAddress.Loopback);
                Assert.Equal(1UL, second.AnnouncementVersion);
            }
        }

        /// <summary>
        /// SetMediaStreamStatus on a media type with no matching local
        /// track is a silent no-op — AnnouncementVersion does NOT bump.
        /// Guards against a regression that increments unconditionally.
        /// </summary>
        [Fact]
        public void SetMediaStreamStatus_NoMatchingLocalTrack_DoesNotIncrement()
        {
            // Session has only audio.
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP first = session.CreateOffer(IPAddress.Loopback);

                // Try to set status on video (no local video track).
                session.SetMediaStreamStatus(SDPMediaTypesEnum.video, MediaStreamStatusEnum.SendOnly);

                SDP second = session.CreateOffer(IPAddress.Loopback);
                Assert.Equal(first.AnnouncementVersion, second.AnnouncementVersion);
            }
        }

        /// <summary>
        /// Three consecutive SetMediaStreamStatus calls increment the
        /// version exactly three times. Verifies monotonic, per-call
        /// increment semantics.
        /// </summary>
        [Fact]
        public void MultipleSetMediaStreamStatusCalls_IncrementMonotonically()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.SendOnly);
                session.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.RecvOnly);
                session.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.SendRecv);

                SDP sdp = session.CreateOffer(IPAddress.Loopback);
                Assert.Equal(3UL, sdp.AnnouncementVersion);
            }
        }

        /// <summary>
        /// When the remote offer declares an Inactive m-line, the
        /// receiver's LocalTrack.StreamStatus is set to Inactive too
        /// (the direction-mirroring rule for "remote unavailable").
        /// </summary>
        [Fact]
        public void RemoteInactiveOffer_SetsLocalTrackInactive()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferInactive);

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Equal(MediaStreamStatusEnum.Inactive,
                    session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// Classic SIP hold form: c=IN IP4 0.0.0.0. When the connection
        /// address is the "any" address (and the port isn't the magic
        /// 9), the receiver flips LocalTrack to Inactive even though the
        /// remote's announced direction is SendOnly. The Any-address
        /// rule takes precedence.
        /// </summary>
        [Fact]
        public void RemoteHoldOfferNullConnectionAddress_SetsLocalInactive()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferHoldNullConnectionAddress);

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Equal(MediaStreamStatusEnum.Inactive,
                    session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// When the remote announces sendonly direction (with a routable
        /// connection address and non-zero port), the receiver's
        /// LocalTrack.StreamStatus does NOT mirror to recvonly — it
        /// stays at the default SendRecv. This is the asymmetric
        /// direction-handling behaviour: the local only flips to
        /// Inactive in response to remote Inactive / Any-address / port-0,
        /// not to other direction values.
        /// </summary>
        [Fact]
        public void RemoteSendOnlyOffer_LocalDirectionStaysAtDefault()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferSendOnly);

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                // Remote announced sendonly, but local stays SendRecv.
                Assert.Equal(MediaStreamStatusEnum.SendRecv,
                    session.AudioStream.LocalTrack.StreamStatus);
                // The remote track still reflects what was announced.
                Assert.Equal(MediaStreamStatusEnum.SendOnly,
                    session.AudioStream.RemoteTrack.StreamStatus);
            }
        }

        /// <summary>
        /// After a hold (LocalTrack becomes Inactive), if the remote
        /// then sends a re-INVITE with an active offer, the LocalTrack
        /// is restored to its DefaultStreamStatus. This is the
        /// "Inactive → DefaultStreamStatus" branch at the top of
        /// SetLocalTrackStreamStatus.
        /// </summary>
        [Fact]
        public void HoldThenResume_LocalTrackRestoredToDefault()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                // Hold → LocalTrack inactive.
                SDP hold = SDP.ParseSDPDescription(SdpFixtures.AudioOfferHoldNullConnectionAddress);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, hold));
                Assert.Equal(MediaStreamStatusEnum.Inactive,
                    session.AudioStream.LocalTrack.StreamStatus);

                // Resume → LocalTrack restored.
                SDP resume = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, resume));
                Assert.Equal(MediaStreamStatusEnum.SendRecv,
                    session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// When the remote re-INVITE re-activates a previously-rejected
        /// m-line (port flipped from 0 back to a routable port), the
        /// LocalTrack for that media is restored from Inactive to
        /// its DefaultStreamStatus.
        /// </summary>
        [Fact]
        public void RejectedMediaThenReactivated_LocalTrackRestored()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                // First offer: full audio+video.
                SDP first = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, first));
                Assert.Equal(MediaStreamStatusEnum.SendRecv,
                    session.VideoStream.LocalTrack.StreamStatus);

                // Re-INVITE rejecting video (port=0).
                SDP rejectVideo = SDP.ParseSDPDescription(SdpFixtures.ReInviteRejectsVideoPortZero);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, rejectVideo));
                Assert.Equal(MediaStreamStatusEnum.Inactive,
                    session.VideoStream.LocalTrack.StreamStatus);

                // Second re-INVITE re-activating both.
                SDP reactivate = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, reactivate));
                Assert.Equal(MediaStreamStatusEnum.SendRecv,
                    session.VideoStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// RemoteTrack.StreamStatus must reflect EXACTLY what the
        /// remote announced. SendOnly / RecvOnly / SendRecv / Inactive
        /// round-trip through SetRemoteDescription without translation
        /// or coercion (unlike LocalTrack, which has the asymmetric
        /// mirroring rules).
        /// </summary>
        [Theory]
        [InlineData(SdpFixtures.AudioOfferSendOnly, MediaStreamStatusEnum.SendOnly)]
        [InlineData(SdpFixtures.AudioOfferRecvOnly, MediaStreamStatusEnum.RecvOnly)]
        [InlineData(SdpFixtures.AudioOfferInactive, MediaStreamStatusEnum.Inactive)]
        [InlineData(SdpFixtures.AudioOnlyOfferPcmu, MediaStreamStatusEnum.SendRecv)]
        public void RemoteTrackStreamStatus_ReflectsAnnouncedDirection(string fixtureSdp, MediaStreamStatusEnum expected)
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(fixtureSdp);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Equal(expected, session.AudioStream.RemoteTrack.StreamStatus);
            }
        }
    }
}
