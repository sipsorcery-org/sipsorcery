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

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPUserFieldUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public SIPUserFieldUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ParamsInUserPortionURITest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPUserField userField = SIPUserField.ParseSIPUserField("<sip:C=on;t=DLPAN@10.0.0.1:5060;lr>");

            Assert.True("C=on;t=DLPAN" == userField.URI.User, "SIP user portion parsed incorrectly.");
            Assert.True("10.0.0.1:5060" == userField.URI.Host, "SIP host portion parsed incorrectly.");

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests parsing a standard SIP user field works correctly.
        /// </summary>
        [Fact]
        public void ParseSIPUserFieldUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var userField = SIPUserField.ParseSIPUserField("\"Jane Doe\" <sip:jane@doe.com>");

            Assert.Equal("Jane Doe", userField.Name);
            Assert.Equal(SIPURI.ParseSIPURI("sip:jane@doe.com"), userField.URI);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests parsing a SIP user field without angle brackets works correctly.
        /// </summary>
        [Fact]
        public void ParseSIPUserFieldNoAnglesUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var userField = SIPUserField.ParseSIPUserField("sip:jane@doe.com");

            Assert.Null(userField.Name);
            Assert.Equal(SIPURI.ParseSIPURI("sip:jane@doe.com"), userField.URI);

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests parsing a SIP user field with header parameters works correctly.
        /// </summary>
        [Fact]
        public void ParseWithHeaderParametersUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var userField = SIPUserField.ParseSIPUserField("\"Jane Doe\" <sip:jane@doe.com>p=1;q=2");

            Assert.Equal("Jane Doe", userField.Name);
            Assert.Equal(SIPURI.ParseSIPURI("sip:jane@doe.com"), userField.URI);
            Assert.Equal(2, userField.Parameters.Count);
            Assert.Equal("1", userField.Parameters.Get("p"));
            Assert.Equal("2", userField.Parameters.Get("q"));

            logger.LogDebug("-----------------------------------------");
        }

        /// <summary>
        /// Tests parsing a SIP user field with both header and URI parameters works correctly.
        /// </summary>
        [Fact]
        public void ParseWithHeaderAndURIParametersUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var userField = SIPUserField.ParseSIPUserField("\"Jane Doe\" <sip:jane@doe.com;a=x;b=y;c=z>p=1;q=2");

            Assert.Equal("Jane Doe", userField.Name);
            Assert.Equal(SIPURI.ParseSIPURI("sip:jane@doe.com;a=x;b=y;c=z"), userField.URI);
            Assert.Equal(3, userField.URI.Parameters.Count);
            Assert.Equal("x", userField.URI.Parameters.Get("a"));
            Assert.Equal("y", userField.URI.Parameters.Get("b"));
            Assert.Equal("z", userField.URI.Parameters.Get("c"));
            Assert.Equal(2, userField.Parameters.Count);
            Assert.Equal("1", userField.Parameters.Get("p"));
            Assert.Equal("2", userField.Parameters.Get("q"));

            logger.LogDebug("-----------------------------------------");
        }
    }
}
