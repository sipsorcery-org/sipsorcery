//-----------------------------------------------------------------------------
// Filename: RTSPHeaderUnitTest.cs
//
// Description: Unit tests for the RTSPHeader class, in particular the header
// splitting/line-folding logic in SplitHeaders.
//
// Author(s):
// Aaron Clauson
//
// History:
// 01 Jun 2026	Aaron Clauson	Created to characterise SplitHeaders line folding.
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
    public class RTSPHeaderUnitTest
    {
        private const string CRLF = "\r\n";

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTSPHeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a well formed block of CRLF separated headers is split into one
        /// entry per header line.
        /// </summary>
        [Fact]
        public void SplitHeadersBasicTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string message = "CSeq: 1" + CRLF + "Session: abc123" + CRLF + "Content-Length: 0";

            string[] headers = RTSPHeader.SplitHeaders(message);

            Assert.Equal(3, headers.Length);
            Assert.Equal("CSeq: 1", headers[0]);
            Assert.Equal("Session: abc123", headers[1]);
            Assert.Equal("Content-Length: 0", headers[2]);
        }

        /// <summary>
        /// Tests that a header value folded across multiple lines (a continuation line begins
        /// with whitespace) is collapsed back onto a single line separated by a single space.
        /// </summary>
        [Fact]
        public void SplitHeadersFoldedWithSpaceTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string message = "Accept: application/sdp," + CRLF + " application/rtsl" + CRLF + "CSeq: 2";

            string[] headers = RTSPHeader.SplitHeaders(message);

            Assert.Equal(2, headers.Length);
            Assert.Equal("Accept: application/sdp, application/rtsl", headers[0]);
            Assert.Equal("CSeq: 2", headers[1]);
        }

        /// <summary>
        /// Tests that a continuation line indented with a tab is also folded back onto the
        /// previous line.
        /// </summary>
        [Fact]
        public void SplitHeadersFoldedWithTabTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string message = "Accept: a," + CRLF + "\tb" + CRLF + "CSeq: 3";

            string[] headers = RTSPHeader.SplitHeaders(message);

            Assert.Equal(2, headers.Length);
            Assert.Equal("Accept: a, b", headers[0]);
            Assert.Equal("CSeq: 3", headers[1]);
        }

        /// <summary>
        /// Tests that a malformed line ending of a lone carriage return followed by a space
        /// (some user agents don't get the \r\n right) is treated as a proper line break.
        /// </summary>
        [Fact]
        public void SplitHeadersMalformedCarriageReturnTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            string message = "CSeq: 4\r Session: xyz";

            string[] headers = RTSPHeader.SplitHeaders(message);

            Assert.Equal(2, headers.Length);
            Assert.Equal("CSeq: 4", headers[0]);
            Assert.Equal("Session: xyz", headers[1]);
        }
    }
}
