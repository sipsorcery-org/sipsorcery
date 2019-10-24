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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys.UnitTests
{
    [TestClass]
    public class LogUnitTest
    {
        [TestMethod]
        public void CheckLoggingTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            Log.Logger.LogDebug("LogDebug CheckLoggingTest");
            Log.Logger.LogInformation("LogInfo CheckLoggingTest");
            Console.WriteLine("-----------------------------------------");
        }
    }
}
