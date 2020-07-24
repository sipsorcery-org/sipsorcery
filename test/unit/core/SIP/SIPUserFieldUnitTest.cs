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
    }
}
