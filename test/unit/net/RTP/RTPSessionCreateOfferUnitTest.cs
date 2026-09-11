//-----------------------------------------------------------------------------
// Filename: RTPSessionCreateOfferUnitTest.cs
//
// Description: Characterization tests for RTPSession.CreateOffer (the
// plain-RTP / SIP-VoIP offer surface, distinct from the WebRTC
// RTCPeerConnection.createOffer covered in
// RTCPeerConnectionCreateOfferUnitTest). Locks down the observable shape
// of the SDP that is produced for typical track configurations.
//
// Category 2 in the SDP-refactor test plan. Out of scope here:
//   - Answer generation (Category 3)
//   - SDES crypto negotiation deep dive (Category 5)
//
// History:
// 22 May 2026	Claude Code - Opus 4.7	Created.
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
    public class RTPSessionCreateOfferUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionCreateOfferUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// CreateOffer on a session with a single PCMU audio track must
        /// return a parsed SDP with one audio m-line advertising PCMU.
        /// </summary>
        [Fact]
        public void AudioOnly_ProducesOfferWithSingleAudioMediaLine()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Loopback);

                Assert.NotNull(sdp);
                SdpAssert.HasMediaLines(sdp, "audio");
                SdpAssert.HasCodec(SdpAssert.Audio(sdp), "PCMU", 0);
                logger.LogDebug("Offer SDP:\n{Sdp}", sdp.ToString());
            }
        }

        /// <summary>
        /// CreateOffer on a session with no local tracks returns null and
        /// does not throw. Documents the no-local-media contract; callers
        /// must guard against null.
        /// </summary>
        [Fact]
        public void NoLocalTracks_ReturnsNull()
        {
            using (var session = new RtpSessionBuilder().Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Loopback);

                Assert.Null(sdp);
            }
        }

        /// <summary>
        /// CreateOffer sets RequireRenegotiation to true on completion.
        /// (Fresh sessions also start with RequireRenegotiation=true, so
        /// this is technically "keeps it true" on a fresh session — but
        /// the more important contract is that it must still be true
        /// after a SetRemoteDescription has cleared it, which the second
        /// half of this test characterises.)
        /// </summary>
        [Fact]
        public void Offer_LeavesRequireRenegotiationTrueAfterPriorAnswerCleared()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                // Drive RequireRenegotiation to false via a successful
                // SetRemoteDescription, then re-issue an offer.
                SDP remote = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, remote));
                Assert.False(session.RequireRenegotiation);

                session.CreateOffer(IPAddress.Loopback);

                Assert.True(session.RequireRenegotiation);
            }
        }

        /// <summary>
        /// Adding audio then video produces an offer with audio m-line
        /// first, then video. Locks down the insertion-order contract that
        /// re-INVITE flows depend on.
        /// </summary>
        [Fact]
        public void AudioThenVideo_PreservesInsertionOrderInOffer()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Loopback);

                SdpAssert.HasMediaLines(sdp, "audio", "video");
            }
        }

        /// <summary>
        /// Inverse insertion order: adding video first must emit a video
        /// m-line ahead of the audio one.
        /// </summary>
        [Fact]
        public void VideoThenAudio_PreservesInsertionOrderInOffer()
        {
            using (var session = new RtpSessionBuilder()
                .WithVideoTrack(96, "VP8", 90000)
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Loopback);

                SdpAssert.HasMediaLines(sdp, "video", "audio");
            }
        }

        /// <summary>
        /// The supplied connectionAddress (an IPv4 loopback in this test)
        /// must appear on the SDP c= line. Sanity check that the offered
        /// SDP carries a routable destination.
        /// </summary>
        [Fact]
        public void Offer_UsesSuppliedConnectionAddressOnCLine()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Loopback);

                Assert.NotNull(sdp.Connection);
                Assert.Equal(IPAddress.Loopback.ToString(), sdp.Connection.ConnectionAddress);
            }
        }

        /// <summary>
        /// An IPv6 connection address gets propagated to the SDP c= line
        /// and the address-type field flips to IP6. Locks down the dual-
        /// stack handling in GetSdpConnectionAddress.
        /// </summary>
        [Fact]
        public void Offer_UsesIPv6ConnectionAddressOnCLine()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.IPv6Loopback);

                Assert.NotNull(sdp.Connection);
                Assert.Equal(IPAddress.IPv6Loopback.ToString(), sdp.Connection.ConnectionAddress);
                Assert.Equal("IP6", sdp.Connection.ConnectionAddressType);
            }
        }

        /// <summary>
        /// A track explicitly set to Inactive (via DefaultStreamStatus) is
        /// reverted to its default direction when CreateOffer runs. Mirrors
        /// the RTCPeerConnection behaviour from Category 2's WebRTC tests.
        /// </summary>
        [Fact]
        public void InactiveLocalTrack_RevertsToDefaultStreamStatusOnOffer()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                session.AudioStream.LocalTrack.StreamStatus = MediaStreamStatusEnum.Inactive;
                Assert.Equal(MediaStreamStatusEnum.Inactive, session.AudioStream.LocalTrack.StreamStatus);

                session.CreateOffer(IPAddress.Loopback);

                Assert.Equal(MediaStreamStatusEnum.SendRecv, session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// A track configured with SendOnly stays SendOnly through offer
        /// generation — only Inactive gets reverted (per
        /// InactiveLocalTrack_RevertsToDefaultStreamStatusOnOffer). The
        /// produced SDP must reflect the configured direction.
        /// </summary>
        [Fact]
        public void SendOnlyLocalTrack_OfferAdvertisesSendOnly()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(direction: MediaStreamStatusEnum.SendOnly)
                .Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Loopback);

                SdpAssert.HasDirection(SdpAssert.Audio(sdp), MediaStreamStatusEnum.SendOnly);
            }
        }

        /// <summary>
        /// RecvOnly direction is preserved through offer generation.
        /// </summary>
        [Fact]
        public void RecvOnlyLocalTrack_OfferAdvertisesRecvOnly()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(direction: MediaStreamStatusEnum.RecvOnly)
                .Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Loopback);

                SdpAssert.HasDirection(SdpAssert.Audio(sdp), MediaStreamStatusEnum.RecvOnly);
            }
        }

        /// <summary>
        /// Two sessions built from the same builder spec produce SDPs that
        /// match after normalisation. Same idea as the WebRTC counterpart
        /// — locks down the deterministic portion of plain-RTP offer
        /// generation so any structural change in the refactor surfaces
        /// immediately.
        /// </summary>
        [Fact]
        public void TwoSessionsSameBuilder_ProduceIdenticalNormalisedOffers()
        {
            string sdp1, sdp2;
            using (var s1 = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                sdp1 = s1.CreateOffer(IPAddress.Loopback).ToString();
            }
            using (var s2 = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                sdp2 = s2.CreateOffer(IPAddress.Loopback).ToString();
            }

            Assert.Equal(
                SdpNormaliser.NormaliseCompact(sdp1),
                SdpNormaliser.NormaliseCompact(sdp2));
        }
    }
}
