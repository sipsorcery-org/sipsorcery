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
using Xunit;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class LogUnitTest
    {
        [Fact]
        public void CheckLoggingTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            Log.Logger.LogDebug("LogDebug CheckLoggingTest");
            Log.Logger.LogInformation("LogInfo CheckLoggingTest");
            Console.WriteLine("-----------------------------------------");
        }
    }
}
