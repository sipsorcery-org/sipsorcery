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

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class CryptoUnitTest
    {
        [Fact]
        public void SampleTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            int initRandomNumber = Crypto.GetRandomInt();
            Console.WriteLine("Random int = " + initRandomNumber + ".");
            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void CallRandomNumberWebServiceUnitTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Console.WriteLine("Random number = " + Crypto.GetRandomInt());

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
        public void GetRandomNumberTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            Console.WriteLine("Random number = " + Crypto.GetRandomInt());

            Console.WriteLine("-----------------------------------------");
        }

        [Fact]
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
