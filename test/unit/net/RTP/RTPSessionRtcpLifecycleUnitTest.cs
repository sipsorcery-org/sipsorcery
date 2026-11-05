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
// 05 Nov 2026	Claude Code - Opus 5.5	Added tests for not starting RTCP on
//                                      streams the remote did not negotiate.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
        /// An audio and video session answering an audio only offer must not start
        /// the video RTCP session. Nothing will ever arrive on that stream so its
        /// inactivity timer would fire OnTimeout and SIPUserAgent would hang up a
        /// healthy audio call.
        /// </summary>
        [Fact]
        public async Task AnswerAudioOnlyOffer_WithAudioVideoSession_DoesNotStartVideoRtcp()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                var answer = session.CreateAnswer(IPAddress.Loopback);
                Assert.DoesNotContain(answer.Media, m => m.Media == SDPMediaTypesEnum.video && m.Port != 0);

                await session.Start();

                Assert.True(session.IsAudioStarted);
                Assert.NotEqual(DateTime.MinValue, session.AudioStream.RtcpSession.StartedAt);

                Assert.False(session.IsVideoStarted);
                Assert.Equal(DateTime.MinValue, session.VideoStream.RtcpSession.StartedAt);
            }
        }

        /// <summary>
        /// Same scenario as above but with a short inactivity timeout, confirming the
        /// unnegotiated video stream never raises OnTimeout. The RTCP report timer first
        /// fires 2.5 to 7.5 seconds after a session is started, so wait for the (silent,
        /// but negotiated) audio stream to time out and then check video did not.
        /// </summary>
        [Fact]
        public async Task AnswerAudioOnlyOffer_WithAudioVideoSession_DoesNotRaiseVideoTimeout()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                session.AudioStream.RtcpSession.NoActivityTimeoutMilliseconds = 1;
                session.VideoStream.RtcpSession.NoActivityTimeoutMilliseconds = 1;

                var audioTimedOut = new ManualResetEventSlim(false);
                bool videoTimedOut = false;
                session.OnTimeout += (mediaType) =>
                {
                    if (mediaType == SDPMediaTypesEnum.video)
                    {
                        videoTimedOut = true;
                    }
                    else if (mediaType == SDPMediaTypesEnum.audio)
                    {
                        audioTimedOut.Set();
                    }
                };

                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);
                await session.Start();

                Assert.True(audioTimedOut.Wait(10000), "Audio RTCP session should have timed out.");
                Assert.False(videoTimedOut, "Unnegotiated video RTCP session must not time out.");
                Assert.False(session.VideoStream.RtcpSession.IsTimedOut);
            }
        }

        /// <summary>
        /// When both audio and video are negotiated both RTCP sessions start.
        /// Guards against the start condition being too restrictive.
        /// </summary>
        [Fact]
        public async Task AnswerAudioVideoOffer_StartsBothRtcpSessions()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);

                await session.Start();

                Assert.True(session.IsAudioStarted);
                Assert.True(session.IsVideoStarted);
                Assert.NotEqual(DateTime.MinValue, session.VideoStream.RtcpSession.StartedAt);
            }
        }

        /// <summary>
        /// A call that starts audio only and later accepts a re-INVITE adding video
        /// must start the video RTCP session on the next Start call (SIPUserAgent
        /// calls MediaSession.Start after answering a re-INVITE that adds a stream).
        /// </summary>
        [Fact]
        public async Task ReInviteAddingVideo_StartsVideoRtcpOnNextStart()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);
                await session.Start();
                Assert.False(session.IsVideoStarted);

                SDP reoffer = SDP.ParseSDPDescription(SdpFixtures.AudioVideoOfferAudioFirst);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, reoffer));
                session.CreateAnswer(IPAddress.Loopback);
                await session.Start();

                Assert.True(session.IsVideoStarted);
                Assert.NotEqual(DateTime.MinValue, session.VideoStream.RtcpSession.StartedAt);
                Assert.False(session.VideoStream.RtcpSession.IsClosed);
            }
        }

        /// <summary>
        /// The caller side of SIPUserAgent.AddVideo: an audio call sends a re-INVITE
        /// offering video and the far end refuses it with m=video 0. The video RTCP
        /// session must not be started and audio must be unaffected.
        /// </summary>
        [Fact]
        public async Task ReInviteVideoRefusedWithPortZero_DoesNotStartVideoRtcp()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);
                await session.Start();

                // Local re-INVITE offering video, answered with the video port set to 0.
                session.CreateOffer(IPAddress.Loopback);
                SDP answer = SDP.ParseSDPDescription(SdpFixtures.ReInviteRejectsVideoPortZero);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.answer, answer));
                await session.Start();

                Assert.False(session.IsVideoStarted);
                Assert.Equal(DateTime.MinValue, session.VideoStream.RtcpSession.StartedAt);
                Assert.True(session.IsAudioStarted);
                Assert.False(session.AudioStream.RtcpSession.IsClosed);
            }
        }

        /// <summary>
        /// An RTP session used without SDP negotiation (no remote description set)
        /// must still start its RTCP session.
        /// </summary>
        [Fact]
        public async Task StartWithoutRemoteDescription_StartsRtcp()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                await session.Start();

                Assert.True(session.IsAudioStarted);
                Assert.NotEqual(DateTime.MinValue, session.AudioStream.RtcpSession.StartedAt);
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
