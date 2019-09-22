using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Sys.UnitTests
{
    [TestClass]
    public class AppStateUnitTest
    {
        [TestMethod]
        public void CheckAppConfigFileExistsTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            Assert.IsTrue(File.Exists("app.config"), "The app.config file was not correctly deployed by the test framework.");
            Console.WriteLine("-----------------------------------------");
        }

        //[TestMethod]
        //public void CheckAppConfigFileExistsTest()
        //{
        //    Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
        //    Assert.IsTrue(File.Exists("app.config"), "The app.config file was not correctly deployed by the test framework.");
        //    Console.WriteLine("-----------------------------------------");
        //}
    }
}
