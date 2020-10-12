//-----------------------------------------------------------------------------
// Filename: VideoTestPatternSourceUnitTest.cs
//
// Description: Unit test for the VideoTestPatternSource class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Xunit;
using SIPSorcery.Media;

namespace SIPSorcery.SIP.App.UnitTests
{
    [Trait("Category", "unit")]
    public class VideoTestPatternSourceUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public VideoTestPatternSourceUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Checks that the VideoTestPatternSource class can be instantiated. The constructor creates a
        /// bitmap from an embedded resource so the unit test checks that works correctly.
        /// </summary>
        [Fact]
        public void CanInstantiateUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            VideoTestPatternSource testPatternSource = new VideoTestPatternSource();

            Assert.NotNull(testPatternSource);
        }
    }
}
