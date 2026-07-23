//-----------------------------------------------------------------------------
// Filename: SdpTestHelpersSmokeTest.cs
//
// Description: Smoke tests for the SDP test infrastructure
// (SdpFixtures / PeerConnectionBuilder / RtpSessionBuilder / SdpAssert /
// SdpNormaliser). These tests don't add behavioural coverage of the
// production code — they only prove the helpers themselves compile,
// run, and produce the shapes downstream tests will rely on.
//
// If any of these fail, the bulk SDP-negotiation characterization
// suite that depends on these helpers will fail in confusing ways.
// Run these first when something looks wrong.
//
// History:
// 20 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests.Helpers
{
    [Trait("Category", "unit")]
    public class SdpTestHelpersSmokeTest
    {
        private readonly ILogger logger;

        public SdpTestHelpersSmokeTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void SdpFixtures_AudioOnlyOfferPcmu_ParsesAndAdvertisesPcmu()
        {
            SDP sdp = SdpAssert.Parse(SdpFixtures.AudioOnlyOfferPcmu);
            SdpAssert.HasMediaLines(sdp, "audio");
            SdpAssert.HasCodec(SdpAssert.Audio(sdp), "PCMU", 0);
            SdpAssert.HasDirection(SdpAssert.Audio(sdp), MediaStreamStatusEnum.SendRecv);
            SdpAssert.IsAccepted(SdpAssert.Audio(sdp));
        }

        [Fact]
        public void SdpFixtures_AudioVideoOfferAudioFirst_HasAudioThenVideoInOrder()
        {
            SDP sdp = SdpAssert.Parse(SdpFixtures.AudioVideoOfferAudioFirst);
            SdpAssert.HasMediaLines(sdp, "audio", "video");
            SdpAssert.HasCodec(SdpAssert.Audio(sdp), "PCMU", 0);
            SdpAssert.HasCodec(SdpAssert.Video(sdp), "VP8", 96);
        }

        [Fact]
        public void SdpFixtures_AudioVideoOfferVideoFirst_HasVideoThenAudioInOrder()
        {
            SDP sdp = SdpAssert.Parse(SdpFixtures.AudioVideoOfferVideoFirst);
            SdpAssert.HasMediaLines(sdp, "video", "audio");
        }

        [Fact]
        public void SdpFixtures_HoldOffer_HasNullConnectionAddress()
        {
            SDP sdp = SdpAssert.Parse(SdpFixtures.AudioOfferHoldNullConnectionAddress);
            Assert.NotNull(sdp.Connection);
            Assert.Equal("0.0.0.0", sdp.Connection.ConnectionAddress);
            SdpAssert.HasDirection(SdpAssert.Audio(sdp), MediaStreamStatusEnum.SendOnly);
        }

        [Fact]
        public void SdpFixtures_ReInviteRejectsVideoPortZero_VideoIsRejected()
        {
            SDP sdp = SdpAssert.Parse(SdpFixtures.ReInviteRejectsVideoPortZero);
            SdpAssert.HasMediaLines(sdp, "audio", "video");
            SdpAssert.IsAccepted(SdpAssert.Audio(sdp));
            SdpAssert.IsRejected(SdpAssert.Video(sdp));
        }

        [Fact]
        public void SdpFixtures_WebRtcAudioOfferOpus_HasDtlsAndIceAndBundle()
        {
            SDP sdp = SdpAssert.Parse(SdpFixtures.WebRtcAudioOfferOpus);
            SdpAssert.HasMediaLines(sdp, "audio");
            SdpAssert.HasFingerprint(sdp);
            SdpAssert.HasIceCredentials(sdp);
            SdpAssert.HasBundle(sdp);
            SdpAssert.HasMid(SdpAssert.Audio(sdp), "0");
        }

        [Fact]
        public void SdpFixtures_ChromeOffer_ParsesAndExposesBothMedia()
        {
            // The Chrome capture is large enough that just-parsing it is a
            // meaningful smoke test for SDP.ParseSDPDescription. If
            // production parsing regresses on Chrome SDP, this fails first.
            SDP sdp = SdpAssert.Parse(SdpFixtures.ChromeAudioVideoWebRtcOffer);
            SdpAssert.HasMediaLines(sdp, "audio", "video");
            SdpAssert.HasFingerprint(sdp);
            SdpAssert.HasIceCredentials(sdp);
            SdpAssert.HasBundle(sdp);
            SdpAssert.HasCodec(SdpAssert.Audio(sdp), "opus", 111);
            SdpAssert.HasCodec(SdpAssert.Video(sdp), "VP8", 96);
            SdpAssert.HasCodec(SdpAssert.Video(sdp), "H264", 102);
        }

        [Fact]
        public void PeerConnectionBuilder_AudioOnly_BuildsConstructibleOfferer()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                var offer = pc.createOffer(new RTCOfferOptions());
                Assert.NotNull(offer?.sdp);

                SDP parsed = SdpAssert.Parse(offer.sdp);
                SdpAssert.HasMediaLines(parsed, "audio");
                SdpAssert.HasCodec(SdpAssert.Audio(parsed), "PCMU", 0);
                logger.LogDebug("Offer SDP:\n{Sdp}", offer.sdp);
            }
        }

        [Fact]
        public void PeerConnectionBuilder_AudioPlusVideo_BuildsOfferWithBothMediaLines()
        {
            using (var pc = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .WithVideoTrack(96, "VP8", 90000)
                .Build())
            {
                var offer = pc.createOffer(new RTCOfferOptions());
                Assert.NotNull(offer?.sdp);

                SDP parsed = SdpAssert.Parse(offer.sdp);
                SdpAssert.HasMediaLines(parsed, "audio", "video");
            }
        }

        [Fact]
        public void RtpSessionBuilder_AudioOnly_AcceptsBasicOffer()
        {
            using (var session = new RtpSessionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                SDP offer = SdpAssert.Parse(SdpFixtures.AudioOnlyOfferPcmu);
                SetDescriptionResultEnum result = session.SetRemoteDescription(SdpType.offer, offer);
                Assert.Equal(SetDescriptionResultEnum.OK, result);
            }
        }

        [Fact]
        public void SdpNormaliser_ReplacesSessionIdAndPortAndIceCreds()
        {
            var sdp = SdpFixtures.WebRtcAudioOfferOpus;
            var normalised = SdpNormaliser.Normalise(sdp);

            Assert.Contains("<SID> <SVER>", normalised);
            Assert.Contains("<UFRAG>", normalised);
            Assert.Contains("<PWD>", normalised);
            Assert.Contains("<HASH>", normalised);
            // The literal ufrag/pwd/fingerprint hash from the fixture must be gone.
            Assert.DoesNotContain("ice-ufrag:abcd", normalised);
            Assert.DoesNotContain("ice-pwd:abcdefghijklmnopqrstuvwx", normalised);
            Assert.DoesNotContain("00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF", normalised);
        }

        [Fact]
        public void SdpNormaliser_TwoOfferersFromSameBuilder_ProduceIdenticalNormalisedSdp()
        {
            // Same builder, two peer connections — the volatile bits
            // (session id, ICE creds, fingerprint, ssrc, port) differ;
            // the normalised form must not.
            string sdp1, sdp2;
            using (var pc1 = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                sdp1 = pc1.createOffer(new RTCOfferOptions()).sdp;
            }
            using (var pc2 = new PeerConnectionBuilder()
                .WithAudioTrack(SDPWellKnownMediaFormatsEnum.PCMU)
                .Build())
            {
                sdp2 = pc2.createOffer(new RTCOfferOptions()).sdp;
            }

            var norm1 = SdpNormaliser.NormaliseCompact(sdp1);
            var norm2 = SdpNormaliser.NormaliseCompact(sdp2);

            logger.LogDebug("Normalised offer 1:\n{Sdp}", norm1);
            logger.LogDebug("Normalised offer 2:\n{Sdp}", norm2);

            Assert.Equal(norm1, norm2);
        }
    }
}
