//-----------------------------------------------------------------------------
// Filename: SDPParsingRegressionUnitTest.cs
//
// Description: Regression tests covering SDP parsing edge cases (malformed
// crypto lines and malformed rtpmap attributes) to guard against crashes and
// corruption when refactoring the SDP parser.
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
    }
}
