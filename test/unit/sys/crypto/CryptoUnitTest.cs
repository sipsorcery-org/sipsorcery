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

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class CryptoUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public CryptoUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void SampleTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int initRandomNumber = Crypto.GetRandomInt();
            logger.LogDebug("Random int = " + initRandomNumber + ".");
            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void CallRandomNumberWebServiceUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            logger.LogDebug("Random number = " + Crypto.GetRandomInt());

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void GetRandomNumberTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            logger.LogDebug("Random number = " + Crypto.GetRandomInt());

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void GetOneHundredRandomNumbersTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            for (int index = 0; index < 100; index++)
            {
                logger.LogDebug("Random number = " + Crypto.GetRandomInt());
            }

            logger.LogDebug("-----------------------------------------");
        }
    }
}
