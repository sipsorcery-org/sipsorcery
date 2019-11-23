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
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class TypeExtensionsUnitTest
    {
        [Fact]
        public void TrimTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            String myString = null;

            Assert.True(myString.IsNullOrBlank(), "String was not correctly detected as blank.");
        }

        [Fact]
        public void ZeroBytesTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            String myString = Encoding.UTF8.GetString(new byte[] { 0x00, 0x00, 0x00, 0x00 });

            Console.WriteLine("Trimmed length=" + myString.Trim().Length + ".");

            Assert.True(myString.IsNullOrBlank(), "String was not correctly detected as blank.");
        }
    }
}
