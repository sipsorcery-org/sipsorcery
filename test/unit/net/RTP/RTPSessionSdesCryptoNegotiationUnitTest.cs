//-----------------------------------------------------------------------------
// Filename: RTPSessionSdesCryptoNegotiationUnitTest.cs
//
// Description: Characterization tests for SDP-driven SRTP (SDES) crypto
// negotiation in RTPSession.SetRemoteDescription. The
// UseSdpCryptoNegotiation branch of the negotiator has its own decision
// tree on top of the usual codec-matching logic — these tests pin down
// its observable behaviour ahead of the planned refactor.
//
// Category 5 in the SDP-refactor test plan. The early-guard subset
// (non-secure transport, no compatible suite, single compatible suite)
// is already covered by Category 1's
// RTPSessionSetRemoteDescriptionValidationUnitTest; this file covers the
// rest of the matrix.
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
    public class RTPSessionSdesCryptoNegotiationUnitTest
    {
        private readonly ILogger logger;

        public RTPSessionSdesCryptoNegotiationUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Successful SDES negotiation across a full offer-answer flow
        /// must set the MediaStream's security context (i.e.
        /// IsSecurityContextReady becomes true). Without this, RTP
        /// packets would never be encrypted on the wire.
        ///
        /// Note: SDES is two-sided. SetRemoteDescription alone sets up
        /// the REMOTE half; the local half (and therefore the completed
        /// negotiation) only lands after CreateAnswer publishes the
        /// local crypto attributes. So this test exercises both calls.
        /// </summary>
        [Fact]
        public void CompatibleCryptoSuite_AfterFullOfferAnswer_SetsSecurityContext()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferWithSdesCrypto);

                Assert.False(session.AudioStream.IsSecurityContextReady(),
                    "Security context must not be ready before negotiation.");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);

                Assert.True(session.AudioStream.IsSecurityContextReady(),
                    "Security context must be ready after offer + answer.");
            }
        }

        /// <summary>
        /// AES_CM_128_HMAC_SHA1_80 is the first of the two default-
        /// supported suites and must be accepted when offered alone.
        /// </summary>
        [Fact]
        public void DefaultSupportedSuite_AesCm128HmacSha1_80_IsAccepted()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);

                Assert.True(session.AudioStream.IsSecurityContextReady());
            }
        }

        /// <summary>
        /// AES_CM_128_HMAC_SHA1_32 is the second default-supported suite
        /// (shorter tag length) and must also be accepted. Locks down
        /// that both default suites are usable, not just the first.
        /// </summary>
        [Fact]
        public void DefaultSupportedSuite_AesCm128HmacSha1_32_IsAccepted()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AES_CM_128_HMAC_SHA1_32 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);

                Assert.True(session.AudioStream.IsSecurityContextReady());
            }
        }

        /// <summary>
        /// When the offer presents multiple crypto suites — some
        /// unsupported, one supported — negotiation succeeds. The
        /// implementation walks the list and accepts the first match.
        /// </summary>
        [Fact]
        public void MultipleCryptoSuitesFirstUnsupportedSecondSupported_NegotiatesSuccessfully()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                // First tag is AEAD_AES_256_GCM (not in default supported);
                // second is AES_CM_128_HMAC_SHA1_80 (supported).
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AEAD_AES_256_GCM inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVzAAAAAAAAAAAAAAAA
a=crypto:2 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);

                Assert.True(session.AudioStream.IsSecurityContextReady());
            }
        }

        /// <summary>
        /// When the offer rejects media by setting m= port=0 AND omits
        /// the a=crypto line, the SetRemoteDescription path must NOT
        /// return CryptoNegotiationFailed for that announcement — the
        /// explicit port=0 guard at RTPSession.cs around line 1202 only
        /// triggers CryptoNegotiationFailed when the port is nonzero.
        /// This characterises the call-hold / rejected-media branch.
        ///
        /// Note: in the current implementation a subsequent CreateAnswer
        /// on this session WILL throw (see
        /// RejectedMediaPortZeroWithoutCrypto_CreateAnswerThrows below).
        /// SetRemoteDescription itself succeeds though, which is what
        /// this test pins down.
        /// </summary>
        [Fact]
        public void RejectedMediaPortZeroWithoutCrypto_SetRemoteDescriptionReturnsOk()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 1 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv
m=video 0 RTP/SAVP 96
a=rtpmap:96 VP8/90000");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
            }
        }

        /// <summary>
        /// Companion to the previous test: although
        /// SetRemoteDescription accepts an SDES-mode offer with a
        /// port=0 (and crypto-less) video m-line, the current
        /// CreateAnswer implementation throws ApplicationException
        /// when it tries to emit the answer for that rejected video
        /// stream ("Error creating crypto attribute for SDP answer.
        /// No compatible offer.").
        ///
        /// This is observable behaviour worth locking down — a
        /// refactor that "fixes" CreateAnswer to handle this case
        /// silently is a behaviour change callers may be relying on.
        /// </summary>
        [Fact]
        public void RejectedMediaPortZeroWithoutCrypto_CreateAnswerThrows()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 1 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv
m=video 0 RTP/SAVP 96
a=rtpmap:96 VP8/90000");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));

                Assert.Throws<SIPSorcery.SipSorceryException>(
                    () => session.CreateAnswer(IPAddress.Loopback));
            }
        }

        /// <summary>
        /// When the offer presents an active (port != 0) media line that
        /// is missing a crypto attribute, negotiation fails with
        /// CryptoNegotiationFailed. This is the inverse of the previous
        /// test — the port=0 guard exists specifically to allow rejected
        /// media to slip through, but active media must still carry crypto.
        /// </summary>
        [Fact]
        public void ActiveMediaWithoutCrypto_FailsNegotiation()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                // RTP/SAVP transport but no a=crypto line and port != 0.
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=sendrecv");

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.CryptoNegotiationFailed, result);
            }
        }

        /// <summary>
        /// Renegotiation with the SAME crypto attribute is a no-op for
        /// the SRTP handler — IsNegotiationComplete + the unchanged
        /// remote security description short-circuit the SetupRemote
        /// call. The session must remain securely contextualised after
        /// the second offer-answer cycle.
        /// </summary>
        [Fact]
        public void RenegotiationSameCrypto_PreservesSecurityContext()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .Build())
            {
                SDP firstOffer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferWithSdesCrypto);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, firstOffer));
                session.CreateAnswer(IPAddress.Loopback);
                Assert.True(session.AudioStream.IsSecurityContextReady());

                // Same crypto, second SetRemoteDescription (e.g. re-INVITE).
                SDP secondOffer = SDP.ParseSDPDescription(SdpFixtures.AudioOfferWithSdesCrypto);
                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, secondOffer));
                Assert.True(session.AudioStream.IsSecurityContextReady());
            }
        }

        /// <summary>
        /// Multi-media offer where each m-line carries crypto: both
        /// streams get their security context set. Locks down the
        /// per-m-line independence of SDES negotiation.
        /// </summary>
        [Fact]
        public void MultiMediaOffer_BothCarryCompatibleCrypto_BothStreamsSecure()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv
m=video 20032 RTP/SAVP 96
a=rtpmap:96 VP8/90000
a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv");

                Assert.Equal(SetDescriptionResultEnum.OK,
                    session.SetRemoteDescription(SdpType.offer, offer));
                session.CreateAnswer(IPAddress.Loopback);

                Assert.True(session.AudioStream.IsSecurityContextReady());
                Assert.True(session.VideoStream.IsSecurityContextReady());
            }
        }

        /// <summary>
        /// Multi-media offer where one m-line uses RTP/SAVP+crypto but
        /// the other uses plain RTP/AVP (non-secure): the SAVP guard
        /// fires for the non-secure m-line and the whole negotiation
        /// fails. Locks down the "all-or-nothing transport" behaviour
        /// of the SDES branch.
        /// </summary>
        [Fact]
        public void MultiMediaOffer_OneNonSecureMediaLine_FailsEntireNegotiation()
        {
            using (var session = new RtpSessionBuilder()
                .WithSdpCryptoNegotiation()
                .WithAudioTrack()
                .WithVideoTrack()
                .Build())
            {
                SDP offer = SDP.ParseSDPDescription(
@"v=0
o=- 5000 0 IN IP4 192.0.2.10
s=-
c=IN IP4 192.0.2.10
t=0 0
m=audio 20030 RTP/SAVP 0
a=rtpmap:0 PCMU/8000
a=crypto:1 AES_CM_128_HMAC_SHA1_80 inline:WVNfX19zZW1jdGwgKCkgewkyMjA7fQp9CnVubGVz|2^20|1:32
a=sendrecv
m=video 20032 RTP/AVP 96
a=rtpmap:96 VP8/90000
a=sendrecv");

                var result = session.SetRemoteDescription(SdpType.offer, offer);

                Assert.Equal(SetDescriptionResultEnum.CryptoNegotiationFailed, result);
            }
        }
    }
}
