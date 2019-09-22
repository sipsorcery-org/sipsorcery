using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Sys.UnitTests
{
    [TestClass]
    public class LogUnitTest
    {
        [TestMethod]
        public void CheckLoggingTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            Log.logger.Debug("CheckLoggingTest");
            Console.WriteLine("-----------------------------------------");
        }
    }
}
