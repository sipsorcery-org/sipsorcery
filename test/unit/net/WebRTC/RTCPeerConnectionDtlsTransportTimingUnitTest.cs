//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionDtlsTransportTimingUnitTest.cs
//
// Description: Tests for when the DTLS transport is created. It used to be
// created on ICE nomination, but a remote peer may start its DTLS handshake as
// soon as it has a valid candidate pair (RFC 8445 section 12), which is a round
// trip or more earlier, and browsers do exactly that. Everything the remote sent
// in that window hit a null transport and was logged and dropped ("DTLS packet
// received ... but no DTLS transport available"), costing a DTLS retransmission
// timeout on connection setup.
//
// The transport is now created as soon as the remote description resolves the
// DTLS role, so the opening flight is buffered instead.
//
// History:
// 19 Sep 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionDtlsTransportTimingUnitTest
    {
        private readonly ILogger logger;

        public RTCPeerConnectionDtlsTransportTimingUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Nothing has resolved the DTLS role before a remote description is set, so there is
        /// nothing to create the transport from and no way for a remote peer to have reached us.
        /// </summary>
        [Fact]
        public void BeforeRemoteDescription_NoDtlsTransport()
        {
            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                Assert.Null(GetDtlsHandle(pc));
            }
        }

        /// <summary>
        /// The regression this fixes. Setting the remote offer resolves the DTLS role, and the
        /// transport must exist from that point so DTLS arriving before ICE nominates a pair is
        /// buffered rather than dropped.
        /// </summary>
        [Fact]
        public void AfterRemoteOffer_DtlsTransportCreated()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());

                Assert.Equal(SetDescriptionResultEnum.OK, answerer.setRemoteDescription(offer));

                Assert.NotNull(GetDtlsHandle(answerer));
            }
        }

        /// <summary>
        /// The same must hold for the offerer once it receives the answer.
        /// </summary>
        [Fact]
        public void AfterRemoteAnswer_DtlsTransportCreated()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);
                var answer = answerer.createAnswer();

                Assert.Equal(SetDescriptionResultEnum.OK, offerer.setRemoteDescription(answer));

                Assert.NotNull(GetDtlsHandle(offerer));
            }
        }

        /// <summary>
        /// Creating the transport early must not start the handshake early. The handshake still
        /// waits for ICE to nominate a pair, which is the point a destination is known to send to.
        /// </summary>
        [Fact]
        public void AfterRemoteDescription_HandshakeNotStarted()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                answerer.setRemoteDescription(offerer.createOffer(new RTCOfferOptions()));

                Assert.NotNull(GetDtlsHandle(answerer));
                Assert.Equal(0, GetDtlsHandshakeStarted(answerer));
                Assert.False(answerer.IsDtlsNegotiationComplete);
                Assert.False(GetDtlsHandle(answerer).IsHandshakeComplete());
            }
        }

        /// <summary>
        /// The transport is built from the ICE role, so creating it earlier must not change which
        /// end is the DTLS client. The answerer takes the active role and is the DTLS client, the
        /// offerer takes passive and is the DTLS server.
        /// </summary>
        [Fact]
        public void DtlsRole_MatchesIceRole()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());
                answerer.setRemoteDescription(offer);
                offerer.setRemoteDescription(answerer.createAnswer());

                Assert.Equal(IceRolesEnum.active, answerer.IceRole);
                Assert.True(GetDtlsHandle(answerer).IsClient);

                Assert.Equal(IceRolesEnum.passive, offerer.IceRole);
                Assert.False(GetDtlsHandle(offerer).IsClient);
            }
        }

        /// <summary>
        /// A renegotiation must not swap out the transport. Replacing it would throw away a live
        /// DTLS association along with the SRTP context derived from it.
        /// </summary>
        [Fact]
        public void Renegotiation_KeepsExistingDtlsTransport()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                answerer.setRemoteDescription(offerer.createOffer(new RTCOfferOptions()));
                answerer.createAnswer();

                var firstHandle = GetDtlsHandle(answerer);
                Assert.NotNull(firstHandle);

                Assert.Equal(SetDescriptionResultEnum.OK,
                    answerer.setRemoteDescription(offerer.createOffer(new RTCOfferOptions())));

                Assert.Same(firstHandle, GetDtlsHandle(answerer));
            }
        }

        /// <summary>
        /// A closed peer connection must not acquire a transport it will never use.
        /// </summary>
        [Fact]
        public void AfterClose_NoDtlsTransportCreated()
        {
            using (var offerer = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var offer = offerer.createOffer(new RTCOfferOptions());

                using (var answerer = new PeerConnectionBuilder().WithAudioTrack().Build())
                {
                    answerer.close();

                    answerer.setRemoteDescription(offer);

                    Assert.Null(GetDtlsHandle(answerer));
                }
            }
        }

        // ---------- helpers ----------

        /// <summary>
        /// Reaches into the RTCPeerConnection's private DTLS transport field. The transport is not
        /// exposed publicly, so reflection is the only way to observe when it comes into being.
        /// </summary>
        private static DtlsSrtpTransport GetDtlsHandle(RTCPeerConnection pc)
        {
            var field = typeof(RTCPeerConnection).GetField(
                "_dtlsHandle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            return field.GetValue(pc) as DtlsSrtpTransport;
        }

        /// <summary>
        /// Reads the flag that records whether the DTLS handshake has been kicked off, which is now
        /// distinct from the transport existing.
        /// </summary>
        private static int GetDtlsHandshakeStarted(RTCPeerConnection pc)
        {
            var field = typeof(RTCPeerConnection).GetField(
                "_dtlsHandshakeStarted",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);

            return (int)field.GetValue(pc);
        }
    }
}
