//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionDtlsFingerprintUnitTest.cs
//
// Description: Unit tests for the handling of the DTLS fingerprint attribute in
// a remote session description. Covers the validation of the attribute and the
// rejection of a fingerprint change once the DTLS handshake has completed.
//
// History:
// 20 Aug 2026  Aaron Clauson   Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionDtlsFingerprintUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPeerConnectionDtlsFingerprintUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Once the DTLS handshake has completed the remote peer's certificate is pinned. A
        /// subsequent offer that carries a different fingerprint must be rejected. Previously the
        /// new fingerprint was applied to RemotePeerDtlsFingerprint without any re-verification,
        /// leaving the property reporting a certificate that the SRTP keys were not derived from.
        /// </summary>
        [Fact]
        public void RenegotiationWithChangedDtlsFingerprintIsRejected()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var (offerer, answerer, initialOffer) = CompleteInitialNegotiation();
            var pinnedFingerprint = answerer.RemotePeerDtlsFingerprint;
            Assert.NotNull(pinnedFingerprint);

            SetDtlsNegotiationComplete(answerer);

            var reOffer = WithFingerprintValue(initialOffer, string.Join(":", Enumerable.Repeat("AB", 32)));

            var setResult = answerer.setRemoteDescription(reOffer);

            Assert.Equal(SetDescriptionResultEnum.DtlsFingerprintChanged, setResult);
            Assert.Equal(pinnedFingerprint.value, answerer.RemotePeerDtlsFingerprint.value);

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// A rejected session description must not be applied. The check is made before the remote
        /// description is stored so that the application does not observe an SDP, and a fingerprint,
        /// that the peer connection refused.
        /// </summary>
        [Fact]
        public void RejectedFingerprintChangeDoesNotApplyRemoteDescription()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var (offerer, answerer, initialOffer) = CompleteInitialNegotiation();
            SetDtlsNegotiationComplete(answerer);

            var acceptedSdp = answerer.remoteDescription.sdp.ToString();
            var reOffer = WithFingerprintValue(initialOffer, string.Join(":", Enumerable.Repeat("AB", 32)));

            var setResult = answerer.setRemoteDescription(reOffer);

            Assert.Equal(SetDescriptionResultEnum.DtlsFingerprintChanged, setResult);
            Assert.Equal(acceptedSdp, answerer.remoteDescription.sdp.ToString());

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// A renegotiation that keeps the same fingerprint is the normal case and must continue to
        /// be accepted after the DTLS handshake has completed.
        /// </summary>
        [Fact]
        public void RenegotiationWithUnchangedDtlsFingerprintIsAccepted()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var (offerer, answerer, initialOffer) = CompleteInitialNegotiation();
            SetDtlsNegotiationComplete(answerer);

            var setResult = answerer.setRemoteDescription(initialOffer);

            Assert.Equal(SetDescriptionResultEnum.OK, setResult);

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// Before the DTLS handshake completes there is no established association to protect and
        /// the handshake verifies whatever fingerprint is current. A re-offer arriving mid
        /// negotiation, for example after a rollback, must still be accepted.
        /// </summary>
        [Fact]
        public void FingerprintChangeBeforeDtlsHandshakeCompleteIsAccepted()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var (offerer, answerer, initialOffer) = CompleteInitialNegotiation();

            var changedFingerprint = string.Join(":", Enumerable.Repeat("AB", 32));
            var reOffer = WithFingerprintValue(initialOffer, changedFingerprint);

            var setResult = answerer.setRemoteDescription(reOffer);

            Assert.Equal(SetDescriptionResultEnum.OK, setResult);
            Assert.Equal(changedFingerprint.ToLower(), answerer.RemotePeerDtlsFingerprint.value);

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// A session description with no fingerprint attribute is rejected and, since the check is
        /// made up front, must not be applied.
        /// </summary>
        [Fact]
        public void MissingDtlsFingerprintIsRejectedWithoutApplyingDescription()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTCPeerConnection answerer = CreateAudioPeerConnection();
            var offerer = CreateAudioPeerConnection();
            var offer = offerer.createOffer(new RTCOfferOptions());
            var strippedSdp = string.Join("\r\n",
                offer.sdp.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Where(x => !x.StartsWith("a=fingerprint:", StringComparison.OrdinalIgnoreCase)));

            var setResult = answerer.setRemoteDescription(
                new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = strippedSdp });

            Assert.Equal(SetDescriptionResultEnum.DtlsFingerprintMissing, setResult);
            Assert.Null(answerer.remoteDescription);
            Assert.Null(answerer.RemotePeerDtlsFingerprint);

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// A fingerprint using a digest that cannot be calculated is rejected. Without a usable
        /// digest the certificate supplied during the handshake could not be checked.
        /// </summary>
        [Fact]
        public void UnsupportedDtlsFingerprintDigestIsRejectedWithoutApplyingDescription()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTCPeerConnection answerer = CreateAudioPeerConnection();
            var offerer = CreateAudioPeerConnection();
            var offer = offerer.createOffer(new RTCOfferOptions());
            var unsupportedSdp = Regex.Replace(offer.sdp, @"a=fingerprint:\S+", "a=fingerprint:sha-999");

            var setResult = answerer.setRemoteDescription(
                new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = unsupportedSdp });

            Assert.Equal(SetDescriptionResultEnum.DtlsFingerprintDigestNotSupported, setResult);
            Assert.Null(answerer.remoteDescription);
            Assert.Null(answerer.RemotePeerDtlsFingerprint);

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// A single DTLS association is used for the whole session. If a media announcement asks for a
        /// different certificate to the one already established for the session it cannot be honoured.
        /// Previously only the first fingerprint found was used and the rest were silently discarded.
        /// </summary>
        [Fact]
        public void ConflictingMediaAnnouncementFingerprintsAreRejected()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTCPeerConnection answerer = CreateAudioVideoPeerConnection();
            var offerer = CreateAudioVideoPeerConnection();
            var offer = offerer.createOffer(new RTCOfferOptions());

            var conflictingSdp = ReplaceLastFingerprintValue(offer.sdp, string.Join(":", Enumerable.Repeat("AB", 32)));

            var setResult = answerer.setRemoteDescription(
                new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = conflictingSdp });

            Assert.Equal(SetDescriptionResultEnum.DtlsFingerprintConflict, setResult);
            Assert.Null(answerer.remoteDescription);
            Assert.Null(answerer.RemotePeerDtlsFingerprint);

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// Media announcements that agree on the fingerprint, which is the normal case for a bundled
        /// session, must continue to be accepted.
        /// </summary>
        [Fact]
        public void MatchingMediaAnnouncementFingerprintsAreAccepted()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTCPeerConnection answerer = CreateAudioVideoPeerConnection();
            var offerer = CreateAudioVideoPeerConnection();
            var offer = offerer.createOffer(new RTCOfferOptions());

            var offerSdp = SDP.ParseSDPDescription(offer.sdp);
            Assert.Equal(2, offerSdp.Media.Count);
            Assert.All(offerSdp.Media, x => Assert.False(string.IsNullOrWhiteSpace(x.DtlsFingerprint)));

            var setResult = answerer.setRemoteDescription(offer);

            Assert.Equal(SetDescriptionResultEnum.OK, setResult);
            Assert.NotNull(answerer.RemotePeerDtlsFingerprint);

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// A rejected media announcement has no DTLS association of its own so a stale fingerprint left
        /// on it must not cause the whole session description to be rejected.
        /// </summary>
        [Fact]
        public void FingerprintOnRejectedMediaAnnouncementIsIgnored()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            RTCPeerConnection answerer = CreateAudioVideoPeerConnection();
            var offerer = CreateAudioVideoPeerConnection();
            var offer = offerer.createOffer(new RTCOfferOptions());

            var offerSdp = SDP.ParseSDPDescription(offer.sdp);
            var expectedFingerprint = offerSdp.Media[0].DtlsFingerprint;
            offerSdp.Media[1].Port = 0;
            offerSdp.Media[1].DtlsFingerprint = "sha-256 " + string.Join(":", Enumerable.Repeat("AB", 32));

            var setResult = answerer.setRemoteDescription(
                new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offerSdp.ToString() });

            Assert.Equal(SetDescriptionResultEnum.OK, setResult);
            Assert.Equal(expectedFingerprint.Trim().ToLower(), answerer.RemotePeerDtlsFingerprint.ToString().ToLower());

            offerer.close();
            answerer.close();
        }

        /// <summary>
        /// Creates a peer connection with a single audio track.
        /// </summary>
        private static RTCPeerConnection CreateAudioPeerConnection()
        {
            var pc = new RTCPeerConnection(null);
            pc.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.audio, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                }));
            return pc;
        }

        /// <summary>
        /// Creates a peer connection with an audio and a video track so that the offer it generates
        /// has two media announcements, each carrying a fingerprint attribute.
        /// </summary>
        private static RTCPeerConnection CreateAudioVideoPeerConnection()
        {
            var pc = CreateAudioPeerConnection();
            pc.addTrack(new MediaStreamTrack(
                SDPMediaTypesEnum.video, false,
                new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8", 90000)
                }));
            return pc;
        }

        /// <summary>
        /// Replaces the value of the last fingerprint attribute in a session description, leaving any
        /// earlier ones as they were.
        /// </summary>
        private static string ReplaceLastFingerprintValue(string sdp, string value)
        {
            var matches = Regex.Matches(sdp, @"(a=fingerprint:\S+\s+)([0-9A-Fa-f:]+)");
            Assert.True(matches.Count > 1, "The session description needs more than one fingerprint attribute.");

            var last = matches[matches.Count - 1];
            return sdp.Substring(0, last.Index) + last.Groups[1].Value + value + sdp.Substring(last.Index + last.Length);
        }

        /// <summary>
        /// Runs an offer/answer exchange between two peer connections and returns them along with
        /// the offer so that it can be replayed as a renegotiation.
        /// </summary>
        private static (RTCPeerConnection offerer, RTCPeerConnection answerer, RTCSessionDescriptionInit offer)
            CompleteInitialNegotiation()
        {
            var offerer = CreateAudioPeerConnection();
            var answerer = CreateAudioPeerConnection();

            var offer = offerer.createOffer(new RTCOfferOptions());
            Assert.Equal(SetDescriptionResultEnum.OK, answerer.setRemoteDescription(offer));

            var answer = answerer.createAnswer();
            Assert.Equal(SetDescriptionResultEnum.OK, offerer.setRemoteDescription(answer));

            return (offerer, answerer, offer);
        }

        /// <summary>
        /// Returns a copy of a session description with the value of every fingerprint attribute
        /// replaced, leaving the digest algorithm as it was.
        /// </summary>
        private static RTCSessionDescriptionInit WithFingerprintValue(RTCSessionDescriptionInit description, string value)
        {
            var sdp = Regex.Replace(
                description.sdp,
                @"(a=fingerprint:\S+\s+)[0-9A-Fa-f:]+",
                m => m.Groups[1].Value + value);

            Assert.Contains(value, sdp);

            return new RTCSessionDescriptionInit { type = description.type, sdp = sdp };
        }

        /// <summary>
        /// Marks the DTLS handshake as complete. The property is only set by a real handshake so
        /// reflection is used to get the peer connection into the post handshake state.
        /// </summary>
        private static void SetDtlsNegotiationComplete(RTCPeerConnection pc)
        {
            var property = typeof(RTCPeerConnection).GetProperty(
                "IsDtlsNegotiationComplete",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(property);

            var setter = property.GetSetMethod(nonPublic: true);
            Assert.NotNull(setter);

            setter.Invoke(pc, new object[] { true });
            Assert.True(pc.IsDtlsNegotiationComplete);
        }

        /// <summary>
        /// Tests inputs that cannot identify a supported fingerprint algorithm and value.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("sha-256")]
        [InlineData("sha-999 AA:BB")]
        public void TryParseInvalidDtlsFingerprintUnitTest(string value)
        {
            Assert.False(RTCDtlsFingerprint.TryParse(value, out var fingerprint));
            Assert.Null(fingerprint);
        }

        /// <summary>
        /// Tests parsing a fingerprint that uses a supported digest.
        /// </summary>
        [Fact]
        public void TryParseValidDtlsFingerprintUnitTest()
        {
            Assert.True(RTCDtlsFingerprint.TryParse("sha-256 aa:bb", out var fingerprint));
            Assert.Equal("sha-256", fingerprint.algorithm);
            Assert.Equal("aa:bb", fingerprint.value);
        }

        /// <summary>
        /// Tests the SDP representation, including the uppercase fingerprint required by Firefox.
        /// </summary>
        [Fact]
        public void ToStringDtlsFingerprintUnitTest()
        {
            var fingerprint = new RTCDtlsFingerprint
            {
                algorithm = "sha-256",
                value = "aa:bb:0c"
            };

            Assert.Equal("sha-256 AA:BB:0C", fingerprint.ToString());
        }
    }
}
