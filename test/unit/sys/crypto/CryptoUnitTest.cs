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
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            int initRandomNumber = Crypto.GetRandomInt(Crypto.DEFAULT_RANDOM_LENGTH);
            logger.LogDebug("Random int = {initRandomNumber}.", initRandomNumber);
            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void CallRandomNumberWebServiceUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            logger.LogDebug("Random number = {RandomNumber}", Crypto.GetRandomInt(Crypto.DEFAULT_RANDOM_LENGTH));

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void GetRandomNumberTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            logger.LogDebug("Random number = {RandomNumber}", Crypto.GetRandomInt(Crypto.DEFAULT_RANDOM_LENGTH));

            logger.LogDebug("-----------------------------------------");
        }

        [Fact]
        public void GetOneHundredRandomNumbersTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            for (int index = 0; index < 100; index++)
            {
                logger.LogDebug("Random number = {RandomNumber}", Crypto.GetRandomInt(Crypto.DEFAULT_RANDOM_LENGTH));
            }

            logger.LogDebug("-----------------------------------------");
        }
    }
}
