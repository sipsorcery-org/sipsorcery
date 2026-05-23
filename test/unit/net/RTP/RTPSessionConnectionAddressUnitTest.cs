//-----------------------------------------------------------------------------
// Filename: RTPSessionConnectionAddressUnitTest.cs
//
// Description: Characterization tests for connection-address and hold
// semantics in RTPSession. Covers the corners of
// SetLocalTrackStreamStatus / GetSdpConnectionAddress that earlier
// categories did not touch:
//
//   - IPv6 c= line round-trips through the SDP parser
//   - IPv6 hold form (c=IN IP6 ::) flips LocalTrack Inactive
//   - The magic ICE port "9" (SDP.IGNORE_RTP_PORT_NUMBER) on an Any
//     address keeps LocalTrack ACTIVE (the WebRTC offer shape)
//   - The magic port also skips DestinationEndPoint overwrite, so the
//     ICE layer remains the source of truth for routing
//
// Category 10 in the SDP-refactor test plan.
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
    public class RTPSessionConnectionAddressUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionConnectionAddressUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// An IPv6 session-level c= line is parsed with
        /// ConnectionAddressType == "IP6" and the IPv6 address preserved.
        /// Sanity check for the parser before relying on it in the
        /// hold/mismatch tests below.
        /// </summary>
        [Fact]
        public void IPv6OfferConnectionLine_ParsesAsIp6Family()
        {
            SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP6 ::1
s=-
c=IN IP6 ::1
t=0 0
m=audio 20000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv");

            Assert.NotNull(offer.Connection);
            Assert.Equal("IP6", offer.Connection.ConnectionAddressType);
            Assert.Equal("::1", offer.Connection.ConnectionAddress);
        }

        /// <summary>
        /// IPv6 hold form: c=IN IP6 :: (IPv6Any). The Any-address rule in
        /// SetLocalTrackStreamStatus treats IPv6Any the same as IPv4
        /// 0.0.0.0 — the LocalTrack flips to Inactive when the port is
        /// not the magic 9.
        /// </summary>
        [Fact]
        public void IPv6HoldFormWithNormalPort_SetsLocalInactive()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP6 ::1
s=-
c=IN IP6 ::
t=0 0
m=audio 20000 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendonly");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Equal(MediaStreamStatusEnum.Inactive,
                    session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// The WebRTC offer shape: c=IN IP4 0.0.0.0 with m=audio 9 ...
        /// The Any-address rule in SetLocalTrackStreamStatus has an
        /// explicit "if port != 9" gate so this combination does NOT
        /// flip LocalTrack to Inactive — the ICE layer is expected to
        /// drive the actual destination, and SDP routing is deliberately
        /// suppressed.
        /// </summary>
        [Fact]
        public void IPv4AnyAddressWithMagicPort9_KeepsLocalTrackActive()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 0.0.0.0
s=-
c=IN IP4 0.0.0.0
t=0 0
m=audio 9 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                // Local must NOT be flipped to inactive — port 9 means
                // "ICE drives the destination", not "media disabled".
                Assert.Equal(MediaStreamStatusEnum.SendRecv,
                    session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// Same rule for IPv6: c=IN IP6 :: with m=audio 9 must NOT flip
        /// LocalTrack to Inactive. Verifies the magic port 9 exception
        /// fires on both address families.
        /// </summary>
        [Fact]
        public void IPv6AnyAddressWithMagicPort9_KeepsLocalTrackActive()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP6 ::1
s=-
c=IN IP6 ::
t=0 0
m=audio 9 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Equal(MediaStreamStatusEnum.SendRecv,
                    session.AudioStream.LocalTrack.StreamStatus);
            }
        }

        /// <summary>
        /// When the offer uses port 9 (IGNORE_RTP_PORT_NUMBER), the
        /// destination-endpoint assignment in SetRemoteDescription is
        /// skipped — leaving any previously-set DestinationEndPoint
        /// in place (or null for a fresh session). This is the WebRTC
        /// pattern: ICE will set DestinationEndPoint via
        /// SetGlobalDestination once candidates pair, and SDP routing
        /// must not override it.
        /// </summary>
        [Fact]
        public void OfferWithPort9_DoesNotOverwriteDestinationEndPoint()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 1000 0 IN IP4 0.0.0.0
s=-
c=IN IP4 0.0.0.0
t=0 0
m=audio 9 RTP/AVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                // No real address came from the SDP, so DestinationEndPoint
                // must not have been clobbered with 0.0.0.0:9.
                if (session.AudioStream.DestinationEndPoint != null)
                {
                    Assert.NotEqual(SDP.IGNORE_RTP_PORT_NUMBER,
                        session.AudioStream.DestinationEndPoint.Port);
                }
            }
        }

        /// <summary>
        /// A normal-port offer (non-9) on a routable address sets the
        /// LocalTrack DestinationEndPoint to that address + port. This
        /// is the SIP path — SDP carries the destination.
        /// </summary>
        [Fact]
        public void OfferWithNormalPortAndRoutableAddress_SetsDestinationEndPoint()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOnlyOfferPcmu);

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                Assert.NotNull(session.AudioStream.DestinationEndPoint);
                Assert.Equal("192.0.2.10", session.AudioStream.DestinationEndPoint.Address.ToString());
                Assert.Equal(20000, session.AudioStream.DestinationEndPoint.Port);
            }
        }

        /// <summary>
        /// CreateOffer with the unspecified IPAddress.Any falls back to
        /// the local stack's interface selection — the resulting SDP c=
        /// line is populated. The specific value depends on the runtime
        /// network, so the test only asserts non-null.
        /// </summary>
        [Fact]
        public void CreateOfferWithIPAddressAny_StillProducesCLine()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP sdp = session.CreateOffer(IPAddress.Any);

                Assert.NotNull(sdp);
                Assert.NotNull(sdp.Connection);
                Assert.False(string.IsNullOrEmpty(sdp.Connection.ConnectionAddress));
            }
        }

        /// <summary>
        /// RemoteTrack.StreamStatus must reflect the announced direction
        /// even when the connection address is the "any" hold form. The
        /// LocalTrack flips Inactive (Quirk #10) but the RemoteTrack
        /// keeps the original announced direction, so callers can still
        /// see what the remote SAID it wanted to do.
        /// </summary>
        [Fact]
        public void HoldOffer_RemoteTrackKeepsAnnouncedDirection()
        {
            using (var session = new RtpSessionBuilder().WithAudioTrack().Build())
            {
                SDP hold = SDP.ParseSDPDescription(SdpFixtures.AudioOfferHoldNullConnectionAddress);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, hold));

                // Local goes Inactive (per Quirk #10 / Category 7).
                Assert.Equal(MediaStreamStatusEnum.Inactive,
                    session.AudioStream.LocalTrack.StreamStatus);
                // Remote-side track preserves the announced sendonly.
                Assert.Equal(MediaStreamStatusEnum.SendOnly,
                    session.AudioStream.RemoteTrack.StreamStatus);
            }
        }
    }
}
