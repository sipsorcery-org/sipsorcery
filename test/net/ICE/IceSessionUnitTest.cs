//-----------------------------------------------------------------------------
// Filename: IceSessionUnitTest.cs
//
// Description: Unit tests for the IceSession class.
//
// History:
// 21 Mar 2020	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class IceSessionUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public IceSessionUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that creating a new IceSession instance works correctly. An IceSession
        /// instance will immediately attempt to get some or all candidates.
        /// </summary>
        [Fact]
        public void CreateInstanceUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var iceSession = new IceSession();

            Assert.NotNull(iceSession);
        }
    }
}
