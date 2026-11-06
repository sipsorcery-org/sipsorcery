//-----------------------------------------------------------------------------
// Filename: SDPParsingRegressionUnitTest.cs
//
// Description: Regression tests covering SDP parsing compatibility and malformed
// attributes to guard against data loss, crashes and corruption when refactoring
// the SDP parser.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 Jun 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class SDPParsingRegressionUnitTest
    {
        private const string CRLF = "\r\n";

        private const string SessionHeader =
            "v=0\r\n" +
            "o=- 1 1 IN IP4 127.0.0.1\r\n" +
            "s=session\r\n" +
            "c=IN IP4 127.0.0.1\r\n" +
            "t=0 0\r\n";

        private readonly Microsoft.Extensions.Logging.ILogger logger = null;

        public SDPParsingRegressionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a crypto attribute whose value is only whitespace is rejected gracefully rather
        /// than throwing. The mandatory tag and crypto-suite fields are absent, so parsing must fail
        /// without an IndexOutOfRangeException.
        /// </summary>
        [Fact]
        public void CryptoLineWithWhitespaceValueReturnsFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = SDPSecurityDescription.TryParse("a=crypto:  ", out _);

            Assert.False(result);
        }

        /// <summary>
        /// Tests that a crypto attribute that is missing the mandatory crypto-suite field (only a tag
        /// is present) is rejected gracefully rather than throwing.
        /// </summary>
        [Fact]
        public void CryptoLineWithMissingCryptoSuiteReturnsFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = SDPSecurityDescription.TryParse("a=crypto:1 ", out _);

            Assert.False(result);
        }

        /// <summary>
        /// Tests that a crypto attribute that is missing its key parameters is rejected gracefully.
        /// </summary>
        [Fact]
        public void CryptoLineWithMissingKeyParamsReturnsFalse()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            bool result = SDPSecurityDescription.TryParse("a=crypto:1 AES_CM_128_HMAC_SHA1_80", out var securityDescription);

            Assert.False(result);
        }

        /// <summary>
        /// Tests that an rtpmap attribute with a non-numeric payload ID is handled as a
        /// recognised-but-invalid line: the parser understands it is an rtpmap, sees the payload ID is
        /// not numeric (RTP payload types are always numeric), and therefore drops it rather than
        /// round-tripping it. The line must not produce a media format entry, must not corrupt the
        /// valid formats, and must not be re-emitted when the SDP is serialised. (Genuinely unknown
        /// attribute lines are still expected to round-trip; this only covers lines the parser
        /// recognises and knows to be invalid.)
        /// </summary>
        [Fact]
        public void NonNumericRtpmapIdIsDroppedNotRoundTripped()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string sdpStr =
                $"v=0{CRLF}" +
                $"o=- 1 1 IN IP4 127.0.0.1{CRLF}" +
                $"s=session{CRLF}" +
                $"c=IN IP4 127.0.0.1{CRLF}" +
                $"t=0 0{CRLF}" +
                $"m=audio 12345 RTP/AVP 0{CRLF}" +
                $"a=rtpmap:0 PCMU/8000{CRLF}" +
                $"a=rtpmap:xyz nonsense/1{CRLF}" +
                $"a=sendrecv";

            SDP sdp = SDP.ParseSDPDescription(sdpStr);

            Assert.NotNull(sdp);
            Assert.Single(sdp.Media);

            var announcement = sdp.Media[0];

            // The valid PCMU format must be parsed correctly.
            Assert.True(announcement.MediaFormats.ContainsKey(0));
            Assert.Equal("PCMU/8000", announcement.MediaFormats[0].Rtpmap);

            // The malformed non-numeric rtpmap must not have produced a media format entry.
            Assert.Single(announcement.MediaFormats);

            // A recognised-but-invalid line must be dropped, not preserved as an extra attribute...
            Assert.DoesNotContain("a=rtpmap:xyz nonsense/1", announcement.ExtraMediaAttributes);

            // ...and therefore must not be re-emitted when the SDP is round-tripped through ToString().
            Assert.DoesNotContain("xyz", sdp.ToString());
        }

        /// <summary>
        /// Unhandled session and media attributes survive parsing and serialization.
        /// </summary>
        [Theory]
        [InlineData(false, "a=x-session:keep")]
        [InlineData(false, "a=msid-semantic: WMS stream")]
        [InlineData(true, "a=rtcp-mux")]
        [InlineData(true, "a=ptime:20")]
        [InlineData(true, "a=rtcp-fb:0 nack")]
        [InlineData(true, "a=x-media:keep")]
        public void UnknownAttributesArePreservedAndRoundTripped(bool mediaLevel, string attribute)
        {
            // Arrange

            var sdpText = SessionHeader +
                (mediaLevel ? "" : attribute + CRLF) +
                $"m=audio 12345 RTP/AVP 0{CRLF}" +
                (mediaLevel ? attribute + CRLF : "");

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);
            var serialized = sdp.ToString();
            var roundTrip = SDP.ParseSDPDescription(serialized);

            // Assert

            var attributes = mediaLevel ? sdp.Media[0].ExtraMediaAttributes : sdp.ExtraSessionAttributes;
            var roundTripAttributes = mediaLevel ? roundTrip.Media[0].ExtraMediaAttributes : roundTrip.ExtraSessionAttributes;
            Assert.Equal(new[] { attribute }, attributes);
            Assert.Contains(attribute + CRLF, serialized);
            Assert.Equal(new[] { attribute }, roundTripAttributes);
        }

        /// <summary>
        /// Both parser overloads retain the verbatim input independently of changes to parsed fields.
        /// </summary>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void RawStringPreservesOriginalInput(bool changeSessionName, bool useSpan)
        {
            // Arrange

            var sdpText = (SessionHeader + $"m=audio 12345 RTP/AVP 0{CRLF}").Replace(CRLF, "\n");

            // Act

            var sdp = useSpan
                ? SDP.ParseSDPDescription(sdpText.AsSpan())
                : SDP.ParseSDPDescription(sdpText);
            if (changeSessionName)
            {
                sdp.SessionName = "changed";
            }

            var raw = sdp.RawString();

            // Assert

            Assert.Equal(sdpText, raw);
        }

        /// <summary>
        /// Overflowing payload IDs cannot be mistaken for payload zero.
        /// </summary>
        [Theory]
        [InlineData("rtpmap", "2147483648")]
        [InlineData("rtpmap", "999999999999")]
        [InlineData("fmtp", "2147483648")]
        [InlineData("fmtp", "999999999999")]
        public void OverflowingPayloadIdDoesNotChangeExistingFormats(string attribute, string payloadId)
        {
            // Arrange

            var attributeValue = attribute is "rtpmap" ? "opus/48000/2" : "overwritten=1";
            var sdpText = SessionHeader +
                $"m=audio 12345 RTP/AVP 0{CRLF}" +
                $"a=rtpmap:0 PCMU/8000{CRLF}" +
                $"a=fmtp:0 keep=1{CRLF}" +
                $"a={attribute}:{payloadId} {attributeValue}{CRLF}";

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var formats = sdp.Media[0].MediaFormats;
            Assert.Equal(new[] { 0 }, formats.Keys);
            Assert.Equal(0, formats[0].ID);
            Assert.Equal("PCMU/8000", formats[0].Rtpmap);
            Assert.Equal("keep=1", formats[0].Fmtp);
        }

        /// <summary>
        /// Text characters other than CR and LF are not SDP line separators.
        /// </summary>
        [Theory]
        [InlineData(0x000C)]
        [InlineData(0x0085)]
        [InlineData(0x2028)]
        [InlineData(0x2029)]
        public void NonCrLfCharactersRemainInSessionName(int codePoint)
        {
            // Arrange

            var sessionName = $"Alpha{(char)codePoint}Omega";
            var sdpText = SessionHeader.Replace("s=session", "s=" + sessionName) +
                $"m=audio 12345 RTP/AVP 0{CRLF}";

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);
            var serialized = sdp.ToString();

            // Assert

            Assert.Equal(sessionName, sdp.SessionName);
            Assert.Contains("s=" + sessionName + CRLF, serialized);
        }

        /// <summary>
        /// Existing whitespace tolerance applies to both rtpmap and pending fmtp attributes.
        /// </summary>
        [Theory]
        [InlineData(0x0020)]
        [InlineData(0x0009)]
        [InlineData(0x000B)]
        [InlineData(0x00A0)]
        public void MediaFormatAttributesAcceptLeadingWhitespace(int codePoint)
        {
            // Arrange

            var whitespace = (char)codePoint;
            var sdpText = SessionHeader +
                $"m=audio 12345 RTP/AVP 96{CRLF}" +
                $"a=fmtp:{whitespace}96 minptime=10;useinbandfec=1{CRLF}" +
                $"a=rtpmap:{whitespace}96 opus/48000/2{CRLF}";

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var formats = sdp.Media[0].MediaFormats;
            Assert.Equal(new[] { 96 }, formats.Keys);
            Assert.Equal("opus/48000/2", formats[96].Rtpmap);
            Assert.Equal("minptime=10;useinbandfec=1", formats[96].Fmtp);
        }

        /// <summary>
        /// A payload ID and its attribute value must be separate whitespace-delimited tokens.
        /// </summary>
        [Theory]
        [InlineData("rtpmap")]
        [InlineData("fmtp")]
        public void MediaFormatAttributesRequireWhitespaceAfterPayloadId(string attribute)
        {
            // Arrange

            var attributeValue = attribute is "rtpmap" ? "PCMA/8000" : "minptime=20";
            var sdpText = SessionHeader +
                $"m=audio 12345 RTP/AVP 96{CRLF}" +
                $"a=rtpmap:96 opus/48000/2{CRLF}" +
                $"a=fmtp:96 minptime=10{CRLF}" +
                $"a={attribute}:96{attributeValue}{CRLF}";

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var formats = sdp.Media[0].MediaFormats;
            Assert.Equal(new[] { 96 }, formats.Keys);
            Assert.Equal("opus/48000/2", formats[96].Rtpmap);
            Assert.Equal("minptime=10", formats[96].Fmtp);
        }

        /// <summary>
        /// Extension mappings retain support for repeated spaces and tab separators.
        /// </summary>
        [Theory]
        [InlineData(0x0020, 2)]
        [InlineData(0x0009, 1)]
        [InlineData(0x0009, 2)]
        [InlineData(0x000B, 1)]
        [InlineData(0x00A0, 2)]
        public void ExtensionMapsAcceptWhitespaceSeparators(int codePoint, int count)
        {
            // Arrange

            const string extensionUri = "urn:ietf:params:rtp-hdrext:ssrc-audio-level";
            var whitespace = new string((char)codePoint, count);
            var sdpText = SessionHeader +
                $"m=audio 12345 RTP/AVP 0{CRLF}" +
                $"a=extmap:1{whitespace}{extensionUri}{CRLF}";

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);
            var roundTrip = SDP.ParseSDPDescription(sdp.ToString());

            // Assert

            var extensions = sdp.Media[0].HeaderExtensions;
            Assert.Equal(new[] { 1 }, extensions.Keys);
            Assert.Equal(1, extensions[1].Id);
            Assert.Equal(extensionUri, extensions[1].Uri);
            Assert.Equal(new[] { 1 }, roundTrip.Media[0].HeaderExtensions.Keys);
            Assert.Equal(extensionUri, roundTrip.Media[0].HeaderExtensions[1].Uri);
        }

        /// <summary>
        /// Reusing known protocol tokens must preserve the spelling of all other tokens.
        /// </summary>
        [Theory]
        [InlineData("IN", "IP4", "RTP/AVP")]
        [InlineData("IN", "IP6", "RTP/SAVP")]
        [InlineData("IN", "IP4", "RTP/AVPF")]
        [InlineData("IN", "IP4", "RTP/SAVPF")]
        [InlineData("IN", "IP4", "UDP/TLS/RTP/SAVP")]
        [InlineData("IN", "IP6", "UDP/TLS/RTP/SAVPF")]
        [InlineData("IN", "IP4", "DTLS/SCTP")]
        [InlineData("IN", "IP4", "UDP/DTLS/SCTP")]
        [InlineData("in", "ip4", "rtp/avp")]
        [InlineData("IN", "ip6", "udp/tls/rtp/savpf")]
        [InlineData("X-NET", "X-ADDR", "CUSTOM/PROFILE")]
        public void ProtocolTokenSpellingSurvivesParsingAndSerialization(string networkType, string addressType, string transport)
        {
            // Arrange

            var sdpText = SessionHeader.Replace("IN IP4", $"{networkType} {addressType}") +
                $"m=audio 12345 {transport} 0{CRLF}";

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);
            var roundTrip = SDP.ParseSDPDescription(sdp.ToString());

            // Assert

            Assert.Equal(networkType, sdp.NetworkType);
            Assert.Equal(addressType, sdp.AddressType);
            Assert.Equal(networkType, sdp.Connection.ConnectionNetworkType);
            Assert.Equal(addressType, sdp.Connection.ConnectionAddressType);
            Assert.Equal(transport, sdp.Media[0].Transport);
            Assert.Equal(networkType, roundTrip.NetworkType);
            Assert.Equal(addressType, roundTrip.AddressType);
            Assert.Equal(networkType, roundTrip.Connection.ConnectionNetworkType);
            Assert.Equal(addressType, roundTrip.Connection.ConnectionAddressType);
            Assert.Equal(transport, roundTrip.Media[0].Transport);
        }

        /// <summary>
        /// Duplicate packetization-mode parameters retain the existing last-value-wins behavior.
        /// </summary>
        [Theory]
        [InlineData("packetization-mode=1;packetization-mode=0", false)]
        [InlineData("packetization-mode=0;packetization-mode=1", true)]
        [InlineData("packetization-mode=1;profile-level-id=42e01f;packetization-mode=0", false)]
        [InlineData("packetization-mode=0;profile-level-id=42e01f;PACKETIZATION-MODE=1", true)]
        public void DuplicatePacketizationModeUsesLastValue(string fmtp, bool expectedCompatible)
        {
            // Arrange

            var format = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264/90000", fmtp);

            // Act

            var compatible = format.CheckCompatible();

            // Assert

            Assert.Equal(expectedCompatible, compatible);
        }

        /// <summary>
        /// Only msrp:// paths are parsed into the message media format. Other path URIs, such as
        /// msrps:// for MSRP over TLS, are not understood and must be kept as unknown attributes so
        /// they survive serialization.
        /// </summary>
        [Theory]
        [InlineData("a=path:msrps://atlanta.example.com:7654/jshA7weztas;tcp")]
        [InlineData("a=path:x-custom")]
        public void NonMsrpPathAttributesArePreservedAndRoundTripped(string attribute)
        {
            // Arrange

            var sdpText = SessionHeader +
                $"m=message 7654 TCP/TLS/MSRP *{CRLF}" +
                attribute + CRLF;

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);
            var serialized = sdp.ToString();
            var roundTrip = SDP.ParseSDPDescription(serialized);

            // Assert

            Assert.Equal(new[] { attribute }, sdp.Media[0].ExtraMediaAttributes);
            Assert.Contains(attribute + CRLF, serialized);
            Assert.Equal(new[] { attribute }, roundTrip.Media[0].ExtraMediaAttributes);
        }

        /// <summary>
        /// An msrp:// path is parsed into its address, port and endpoint.
        /// </summary>
        [Fact]
        public void MsrpPathAttributeIsParsed()
        {
            // Arrange

            var sdpText = SessionHeader +
                $"m=message 7654 TCP/MSRP *{CRLF}" +
                $"a=path:msrp://192.168.0.105:7654/jshA7weztas;tcp{CRLF}";

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var messageFormat = sdp.Media[0].MessageMediaFormat;
            Assert.Equal("192.168.0.105", messageFormat.IP);
            Assert.Equal("7654", messageFormat.Port);
            Assert.Equal("jshA7weztas;tcp", messageFormat.Endpoint);
            Assert.Empty(sdp.Media[0].ExtraMediaAttributes);
        }

        /// <summary>
        /// An ssrc attribute carrying only the SSRC value still registers that SSRC on the
        /// announcement, and the SSRC survives serialization.
        /// </summary>
        [Fact]
        public void SsrcAttributeWithoutAttributeValueRegistersSsrc()
        {
            // Arrange

            var sdpText = SessionHeader +
                $"m=audio 12345 RTP/AVP 0{CRLF}" +
                $"a=ssrc:1234{CRLF}";

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);
            var roundTrip = SDP.ParseSDPDescription(sdp.ToString());

            // Assert

            var ssrcAttribute = Assert.Single(sdp.Media[0].SsrcAttributes);
            Assert.Equal(1234U, ssrcAttribute.SSRC);
            Assert.Null(ssrcAttribute.Cname);
            Assert.Equal(1234U, Assert.Single(roundTrip.Media[0].SsrcAttributes).SSRC);
        }

        /// <summary>
        /// The port and port count must be whole whitespace-delimited numeric tokens. A media line
        /// with trailing characters on the port is invalid and must not create an announcement, rather
        /// than being split into a truncated port and a bogus transport.
        /// </summary>
        [Theory]
        [InlineData("m=audio 12345x RTP/AVP 0")]
        [InlineData("m=audio 123x45 RTP/AVP 0")]
        [InlineData("m=audio 12345/2x RTP/AVP 0")]
        [InlineData("m=audio 12345/x RTP/AVP 0")]
        public void MediaLineWithMalformedPortIsRejected(string mediaLine)
        {
            // Arrange

            var sdpText = SessionHeader + mediaLine + CRLF;

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            Assert.Empty(sdp.Media);
        }

        /// <summary>
        /// Valid port and port count forms remain accepted.
        /// </summary>
        [Theory]
        [InlineData("m=audio 12345 RTP/AVP 0", 12345, 0)]
        [InlineData("m=audio 12345/2 RTP/AVP 0", 12345, 2)]
        [InlineData("m=audio\t12345\tRTP/AVP\t0", 12345, 0)]
        public void MediaLineWithValidPortIsParsed(string mediaLine, int expectedPort, int expectedPortCount)
        {
            // Arrange

            var sdpText = SessionHeader + mediaLine + CRLF;

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var announcement = Assert.Single(sdp.Media);
            Assert.Equal(expectedPort, announcement.Port);
            Assert.Equal(expectedPortCount, announcement.PortCount);
            Assert.Equal("RTP/AVP", announcement.Transport);
            Assert.Equal(new[] { 0 }, announcement.MediaFormats.Keys);
        }

        /// <summary>
        /// Whitespace between the setup attribute's colon and its role is tolerated at both session
        /// and media level.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SetupAttributeAcceptsWhitespaceBeforeRole(bool mediaLevel)
        {
            // Arrange

            const string attribute = "a=setup: active";
            var sdpText = SessionHeader +
                (mediaLevel ? "" : attribute + CRLF) +
                $"m=audio 12345 RTP/AVP 0{CRLF}" +
                (mediaLevel ? attribute + CRLF : "");

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var role = mediaLevel ? sdp.Media[0].IceRole : sdp.IceRole;
            Assert.Equal(IceRolesEnum.active, role);
        }

        /// <summary>
        /// Lines may end with CR, LF or CR LF, alone or in runs that leave blank lines, and with trailing whitespace;
        /// the parsed description is the same as when every line ends with CR LF.
        /// </summary>
        [Theory]
        [InlineData("\n", "\n")]
        [InlineData("\r", "\r")]
        [InlineData("\r\n", "\n")]
        [InlineData("\n", "\r")]
        [InlineData("\r\r\n", "\r\r\n")]
        [InlineData("\n\r", "\n\r")]
        [InlineData("\r\n\r\n", "\n\n")]
        [InlineData(" \r\n", "\t\n")]
        public void LineEndingsDoNotChangeParsedDescription(string lineEnding, string alternateLineEnding)
        {
            // Arrange

            var lines = Helpers.SdpFixtures.LargeWebRtcOffer.Replace("\r\n", "\n").Split('\n');
            var expected = SDP.ParseSDPDescription(string.Join(CRLF, lines)).ToString();

            var sdpText = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                sdpText.Append(lines[i]).Append(i % 2 == 0 ? lineEnding : alternateLineEnding);
            }

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText.ToString());

            // Assert

            Assert.Equal(expected, sdp.ToString());
            Assert.Equal(3, sdp.Media.Count);
        }

        /// <summary>
        /// Video RTCP feedback attributes, for a payload type or the "*" wildcard and before or after the format's
        /// rtpmap, give each format its feedback types in document order with white space collapsed and case
        /// insensitive duplicates removed.
        /// </summary>
        [Fact]
        public void RtcpFeedbackAppliesInDocumentOrderWithoutDuplicates()
        {
            // Arrange

            var sdpText = SessionHeader +
                "m=video 5000 RTP/AVPF 96 97 98" + CRLF +
                "a=rtcp-fb:98 nack" + CRLF +
                "a=rtcp-fb:96 NACK PLI" + CRLF +
                "a=rtcp-fb:* nack pli" + CRLF +
                "a=rtcp-fb:96 ccm\tfir" + CRLF +
                "a=rtcp-fb:96 ccm  fir" + CRLF +
                "a=rtcp-fb:97 goog-remb" + CRLF +
                "a=rtcp-fb:* transport-cc" + CRLF +
                "a=rtpmap:96 VP8/90000" + CRLF +
                "a=rtpmap:97 H264/90000" + CRLF +
                "a=rtpmap:98 VP9/90000" + CRLF;

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var announcement = Assert.Single(sdp.Media);
            Assert.Equal(new[] { "NACK PLI", "ccm fir", "transport-cc" }, announcement.MediaFormats[96].SupportedRtcpFeedbackMessages);
            Assert.Equal(new[] { "nack pli", "goog-remb", "transport-cc" }, announcement.MediaFormats[97].SupportedRtcpFeedbackMessages);
            Assert.Equal(new[] { "nack", "nack pli", "transport-cc" }, announcement.MediaFormats[98].SupportedRtcpFeedbackMessages);
            Assert.Empty(announcement.ExtraMediaAttributes);
        }

        /// <summary>
        /// Video RTCP feedback attributes only apply to the format whose ID is written exactly as their payload type.
        /// Those whose payload type is not a number or is the ID of no format are kept as extra attributes, after the
        /// other extra attributes, while those whose payload type is the ID of a format written differently, such as
        /// 096, are dropped. An attribute without a feedback type is kept in place as an extra attribute.
        /// </summary>
        [Fact]
        public void RtcpFeedbackMatchingNoFormatIsKeptAsExtraAttributeOrDropped()
        {
            // Arrange

            var sdpText = SessionHeader +
                "m=video 5000 RTP/AVPF 96 97" + CRLF +
                "a=rtpmap:96 VP8/90000" + CRLF +
                "a=rtcp-fb:120 nack" + CRLF +
                "a=x-custom:1" + CRLF +
                "a=rtcp-fb:96" + CRLF +
                "a=rtcp-fb:abc nack" + CRLF +
                "a=rtcp-fb:096 nack" + CRLF +
                "a=rtcp-fb:097 nack" + CRLF +
                "a=rtcp-fb:0121 ccm fir" + CRLF +
                "a=rtcp-fb:96 goog-remb" + CRLF +
                "a=rtpmap:97 H264/90000" + CRLF;

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var announcement = Assert.Single(sdp.Media);
            Assert.Equal(new[] { "goog-remb" }, announcement.MediaFormats[96].SupportedRtcpFeedbackMessages);
            Assert.Empty(announcement.MediaFormats[97].SupportedRtcpFeedbackMessages);
            Assert.Equal(
                new[] { "a=x-custom:1", "a=rtcp-fb:96", "a=rtcp-fb:120 nack", "a=rtcp-fb:abc nack", "a=rtcp-fb:0121 ccm fir" },
                announcement.ExtraMediaAttributes);
        }

        /// <summary>
        /// Video RTCP feedback attributes whose payload type is negative or larger than any format ID apply to no format
        /// and are kept as extra attributes in document order, even when they come between those for the formats and
        /// the "*" wildcard, which still give each format its feedback types in document order without duplicates.
        /// </summary>
        [Fact]
        public void RtcpFeedbackForNegativeOrLargePayloadTypesAppliesToNoFormat()
        {
            // Arrange

            var sdpText = SessionHeader +
                "m=video 5000 RTP/AVPF 96 98 100 127" + CRLF +
                "a=rtpmap:96 VP8/90000" + CRLF +
                "a=rtpmap:98 VP9/90000" + CRLF +
                "a=rtpmap:100 H264/90000" + CRLF +
                "a=rtpmap:127 H264/90000" + CRLF +
                "a=rtcp-fb:100 goog-remb" + CRLF +
                "a=rtcp-fb:2147483647 nack pli" + CRLF +
                "a=rtcp-fb:-1 nack" + CRLF +
                "a=rtcp-fb:* NACK" + CRLF +
                "a=rtcp-fb:98 ccm fir" + CRLF +
                "a=rtcp-fb:100 nack" + CRLF +
                "a=rtcp-fb:-2147483648 ccm fir" + CRLF +
                "a=rtcp-fb:* goog-remb" + CRLF +
                "a=rtcp-fb:98 Goog-REMB" + CRLF +
                "a=rtcp-fb:128 transport-cc" + CRLF;

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            var announcement = Assert.Single(sdp.Media);
            Assert.Equal(new[] { "NACK", "goog-remb" }, announcement.MediaFormats[96].SupportedRtcpFeedbackMessages);
            Assert.Equal(new[] { "NACK", "ccm fir", "goog-remb" }, announcement.MediaFormats[98].SupportedRtcpFeedbackMessages);
            Assert.Equal(new[] { "goog-remb", "NACK" }, announcement.MediaFormats[100].SupportedRtcpFeedbackMessages);
            Assert.Equal(new[] { "NACK", "goog-remb" }, announcement.MediaFormats[127].SupportedRtcpFeedbackMessages);
            Assert.Equal(
                new[] { "a=rtcp-fb:2147483647 nack pli", "a=rtcp-fb:-1 nack", "a=rtcp-fb:-2147483648 ccm fir", "a=rtcp-fb:128 transport-cc" },
                announcement.ExtraMediaAttributes);
        }

        /// <summary>
        /// RTCP feedback attributes at session level or in a media announcement other than video are kept as extra
        /// attributes and do not apply to any format.
        /// </summary>
        [Fact]
        public void RtcpFeedbackOutsideVideoIsKeptAsExtraAttribute()
        {
            // Arrange

            var sdpText = SessionHeader +
                "a=rtcp-fb:* nack" + CRLF +
                "m=audio 5000 RTP/AVP 0" + CRLF +
                "a=rtcp-fb:0 nack" + CRLF +
                "m=video 5002 RTP/AVPF 96" + CRLF +
                "a=rtpmap:96 VP8/90000" + CRLF;

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);

            // Assert

            Assert.Equal(new[] { "a=rtcp-fb:* nack" }, sdp.ExtraSessionAttributes);
            Assert.Equal(new[] { "a=rtcp-fb:0 nack" }, sdp.Media[0].ExtraMediaAttributes);
            Assert.False(sdp.Media[0].MediaFormats[0].HasExplicitRtcpFeedbackMessages);
            Assert.Empty(sdp.Media[1].MediaFormats[96].SupportedRtcpFeedbackMessages);
            Assert.Empty(sdp.Media[1].ExtraMediaAttributes);
        }

        /// <summary>
        /// Formats compare their RTCP feedback lists by reference, so the same video format parsed twice compares
        /// equal exactly when two formats given the same feedback types through WithUpdatedRtcpFeedbackMessages do,
        /// which for no feedback types depends on whether the runtime shares one empty array.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("a=rtcp-fb:97 nack\r\n")]
        [InlineData("a=rtcp-fb:96 nack\r\n", "nack")]
        public void ParsedFormatsCompareLikeFormatsWithTheSameFeedback(string feedbackAttribute, params string[] expectedFeedback)
        {
            // Arrange

            var sdpText = SessionHeader +
                "m=video 5000 RTP/AVPF 96 97" + CRLF +
                feedbackAttribute +
                "a=rtpmap:96 VP8/90000" + CRLF +
                "a=rtpmap:97 H264/90000" + CRLF;
            var format = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "VP8/90000");
            var expectedEquality = format.WithUpdatedRtcpFeedbackMessages(new List<string>(expectedFeedback))
                .Equals(format.WithUpdatedRtcpFeedbackMessages(new List<string>(expectedFeedback)));

            // Act

            var first = SDP.ParseSDPDescription(sdpText).Media[0].MediaFormats[96];
            var second = SDP.ParseSDPDescription(sdpText).Media[0].MediaFormats[96];

            // Assert

            Assert.Equal(expectedFeedback, first.SupportedRtcpFeedbackMessages);
            Assert.Equal(expectedEquality, first.Equals(second));
        }
    }
}
