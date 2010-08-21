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
        // The Trim method only trims 0x0009, 0x000a, 0x000b, 0x000c, 0x000d, 0x0085, 0x2028, and 0x2029.
        // This array adds in control characters.
        public static readonly char[] WhiteSpaceChars = new char[] { (char)0x00, (char)0x01, (char)0x02, (char)0x03, (char)0x04, (char)0x05, 
            (char)0x06, (char)0x07, (char)0x08, (char)0x09, (char)0x0a, (char)0x0b, (char)0x0c, (char)0x0d, (char)0x0e, (char)0x0f, 
            (char)0x10, (char)0x11, (char)0x12, (char)0x13, (char)0x14, (char)0x15, (char)0x16, (char)0x17, (char)0x18, (char)0x19, (char)0x20,
            (char)0x1a, (char)0x1b, (char)0x1c, (char)0x1d, (char)0x1e, (char)0x1f, (char)0x7f, (char)0x85, (char)0x2028, (char)0x2029 };

        /// <summary>    
        /// Gets a value that indicates whether or not the collection is empty.    
        /// </summary>    
        public static bool IsNullOrBlank(this string s)    
        {
            if (s == null || s.Trim(WhiteSpaceChars).Length == 0)
            {
                return true;
            }

            return false;
        }

        public static long GetEpoch(this DateTime dateTime)
        {
            var unixTime = dateTime.ToUniversalTime() -
                new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            return Convert.ToInt64(unixTime.TotalSeconds);
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

            [Test]
            public void ZeroBytesTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                String myString = Encoding.UTF8.GetString(new byte[]{ 0x00, 0x00, 0x00, 0x00 });

                Console.WriteLine("Trimmed length=" + myString.Trim().Length + ".");

                Assert.IsTrue(myString.IsNullOrBlank(), "String was not correctly detected as blank.");
            }
        }

        #endif

        #endregion
    }
}
