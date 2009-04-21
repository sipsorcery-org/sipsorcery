using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Sys
{
    public static class TypeExtensions
    {
        /// <summary>    
        /// Gets a value that indicates whether or not the collection is empty.    
        /// </summary>    
        public static bool IsNullOrBlank(this string s)    
        {
            if (s == null || s.Trim().Length == 0)
            {
                return true;
            }

            return false;
        }

        #region Unit Testing.

        #if UNITTEST
	
		[TestFixture]
		public class SIPURIUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{}

			[TestFixtureTearDown]
			public void Dispose()
			{}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
            public void TrimTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                String myString = null;

                Assert.IsTrue(myString.IsNullOrBlank(), "String was not correctly detected as blank.");
            }
        }

        #endif

        #endregion
    }
}
