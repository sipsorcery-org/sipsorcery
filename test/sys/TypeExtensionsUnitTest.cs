using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Sys.UnitTests
{
    [TestClass]
    public class TypeExtensionsUnitTest
    {
        [TestMethod]
        public void TrimTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            String myString = null;

            Assert.IsTrue(myString.IsNullOrBlank(), "String was not correctly detected as blank.");
        }

        [TestMethod]
        public void ZeroBytesTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            String myString = Encoding.UTF8.GetString(new byte[] { 0x00, 0x00, 0x00, 0x00 });

            Console.WriteLine("Trimmed length=" + myString.Trim().Length + ".");

            Assert.IsTrue(myString.IsNullOrBlank(), "String was not correctly detected as blank.");
        }
    }
}
