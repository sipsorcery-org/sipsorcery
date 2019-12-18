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

using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class TypeExtensionsUnitTest
    {
        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        public TypeExtensionsUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void TrimTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            String myString = null;

            Assert.True(myString.IsNullOrBlank(), "String was not correctly detected as blank.");
        }

        [Fact]
        public void ZeroBytesTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            String myString = Encoding.UTF8.GetString(new byte[] { 0x00, 0x00, 0x00, 0x00 });

            logger.LogDebug("Trimmed length=" + myString.Trim().Length + ".");

            Assert.True(myString.IsNullOrBlank(), "String was not correctly detected as blank.");
        }
    }
}
