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
        /// RawString returns the verbatim input, independently of changes to parsed fields.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RawStringPreservesOriginalInput(bool changeSessionName)
        {
            // Arrange

            var sdpText = (SessionHeader + $"m=audio 12345 RTP/AVP 0{CRLF}").Replace(CRLF, "\n");

            // Act

            var sdp = SDP.ParseSDPDescription(sdpText);
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
        /// Duplicate packetization-mode parameters retain the existing last-value-wins behavior.
        /// </summary>
        [Theory]
        [InlineData("packetization-mode=1;packetization-mode=0", false)]
        [InlineData("packetization-mode=0;packetization-mode=1", true)]
        public void DuplicatePacketizationModeUsesLastValue(string fmtp, bool expectedCompatible)
        {
            // Arrange

            var format = new SDPAudioVideoMediaFormat(SDPMediaTypesEnum.video, 96, "H264/90000", fmtp);

            // Act

            var compatible = format.CheckCompatible();

            // Assert

            Assert.Equal(expectedCompatible, compatible);
        }
    }
}
