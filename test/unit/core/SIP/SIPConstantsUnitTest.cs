//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
//
// History:
//
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPConstantsUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPConstantsUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// A known extension should be recognised and no unknown extensions reported.
        /// </summary>
        [Fact]
        public void ParseKnownExtensionTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var known = SIPExtensionHeaders.ParseSIPExtensions("100rel", out string unknown);

            Assert.Contains(SIPExtensions.Prack, known);
            Assert.Null(unknown);
        }

        /// <summary>
        /// Extension names must be matched case-insensitively.
        /// </summary>
        [Fact]
        public void ParseExtensionCaseInsensitiveTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var known = SIPExtensionHeaders.ParseSIPExtensions("100REL", out string unknown);

            Assert.Contains(SIPExtensions.Prack, known);
            Assert.Null(unknown);
        }

        /// <summary>
        /// Regression test: a known extension followed by a trailing comma (which yields an empty
        /// split element) must NOT register a spurious unknown extension. A non-null unknown
        /// extensions value causes a SIP request carrying a Require header to be rejected with a
        /// 420 Bad Extension response (see SIPTransport.SIPMessageReceived), so an empty entry
        /// leaking through here turns a previously accepted request into a rejected one.
        /// </summary>
        [Fact]
        public void ParseExtensionsTrailingCommaTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var known = SIPExtensionHeaders.ParseSIPExtensions("100rel,", out string unknown);

            Assert.Contains(SIPExtensions.Prack, known);
            Assert.True(string.IsNullOrEmpty(unknown), $"Expected no unknown extensions but got [{unknown}].");
        }

        /// <summary>
        /// Regression test: doubled-up delimiters between known extensions must not register empty
        /// unknown extensions.
        /// </summary>
        [Fact]
        public void ParseExtensionsDoubledDelimiterTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var known = SIPExtensionHeaders.ParseSIPExtensions("100rel,,replaces", out string unknown);

            Assert.Contains(SIPExtensions.Prack, known);
            Assert.Contains(SIPExtensions.Replaces, known);
            Assert.True(string.IsNullOrEmpty(unknown), $"Expected no unknown extensions but got [{unknown}].");
        }

        /// <summary>
        /// Regression test: a whitespace-only extension list must not register an unknown extension.
        /// </summary>
        [Fact]
        public void ParseExtensionsWhitespaceOnlyTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var known = SIPExtensionHeaders.ParseSIPExtensions("   ", out string unknown);

            Assert.Empty(known);
            Assert.True(string.IsNullOrEmpty(unknown), $"Expected no unknown extensions but got [{unknown}].");
        }

        /// <summary>
        /// A genuinely unknown extension must be reported in the unknown extensions output.
        /// </summary>
        [Fact]
        public void ParseUnknownExtensionTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var known = SIPExtensionHeaders.ParseSIPExtensions("100rel,madeupext", out string unknown);

            Assert.Contains(SIPExtensions.Prack, known);
            Assert.NotNull(unknown);
            Assert.Contains("madeupext", unknown);
        }
    }
}
