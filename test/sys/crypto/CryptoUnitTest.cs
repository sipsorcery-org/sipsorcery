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

namespace SIPSorcery.Sys.UnitTests
{
    [TestClass]
    public class CryptoUnitTest
    {
        [TestMethod]
        public void SampleTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            int initRandomNumber = Crypto.GetRandomInt();
            Console.WriteLine("Random int = " + initRandomNumber + ".");
            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void CallRandomNumberWebServiceUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Console.WriteLine("Random number = " + Crypto.GetRandomInt());

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void GetRandomNumberTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Console.WriteLine("Random number = " + Crypto.GetRandomInt());

            Console.WriteLine("-----------------------------------------");
        }

        [TestMethod]
        public void GetOneHundredRandomNumbersTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            for (int index = 0; index < 100; index++)
            {
                Console.WriteLine("Random number = " + Crypto.GetRandomInt());
            }

            Console.WriteLine("-----------------------------------------");
        }
    }
}
