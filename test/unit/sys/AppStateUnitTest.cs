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
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class LogUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public LogUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void CheckLoggingTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            Log.Logger.LogDebug("LogDebug CheckLoggingTest");
            Log.Logger.LogInformation("LogInfo CheckLoggingTest");
            logger.LogDebug("-----------------------------------------");
        }
    }
}
