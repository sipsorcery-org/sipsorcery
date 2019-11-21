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
    public class UtilitiesUnitTest
    {
        [Fact]
        public void ReverseUInt16SampleTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            ushort testNum = 45677;
            byte[] testNumBytes = BitConverter.GetBytes(testNum);

            ushort reversed = NetConvert.DoReverseEndian(testNum);
            byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

            ushort unReversed = NetConvert.DoReverseEndian(reversed);

            int testNumByteCount = 0;
            foreach (byte testNumByte in testNumBytes)
            {
                Console.WriteLine("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                testNumByteCount++;
            }

            int reverseNumByteCount = 0;
            foreach (byte reverseNumByte in reversedNumBytes)
            {
                Console.WriteLine("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                reverseNumByteCount++;
            }

            Console.WriteLine("Original=" + testNum);
            Console.WriteLine("Reversed=" + reversed);
            Console.WriteLine("Unreversed=" + unReversed);

            Assert.True(testNum == unReversed, "Reverse endian operation for uint16 did not work successfully.");
        }

        [Fact]
        public void ReverseUInt32SampleTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint testNum = 123124;
            byte[] testNumBytes = BitConverter.GetBytes(testNum);

            uint reversed = NetConvert.DoReverseEndian(testNum);
            byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

            uint unReversed = NetConvert.DoReverseEndian(reversed);

            int testNumByteCount = 0;
            foreach (byte testNumByte in testNumBytes)
            {
                Console.WriteLine("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                testNumByteCount++;
            }

            int reverseNumByteCount = 0;
            foreach (byte reverseNumByte in reversedNumBytes)
            {
                Console.WriteLine("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                reverseNumByteCount++;
            }

            Console.WriteLine("Original=" + testNum);
            Console.WriteLine("Reversed=" + reversed);
            Console.WriteLine("Unreversed=" + unReversed);

            Assert.True(testNum == unReversed, "Reverse endian operation for uint32 did not work successfully.");
        }

        [Fact]
        public void ReverseUInt64SampleTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            ulong testNum = 1231265499856464;
            byte[] testNumBytes = BitConverter.GetBytes(testNum);

            ulong reversed = NetConvert.DoReverseEndian(testNum);
            byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

            ulong unReversed = NetConvert.DoReverseEndian(reversed);

            int testNumByteCount = 0;
            foreach (byte testNumByte in testNumBytes)
            {
                Console.WriteLine("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                testNumByteCount++;
            }

            int reverseNumByteCount = 0;
            foreach (byte reverseNumByte in reversedNumBytes)
            {
                Console.WriteLine("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                reverseNumByteCount++;
            }

            Console.WriteLine("Original=" + testNum);
            Console.WriteLine("Reversed=" + reversed);
            Console.WriteLine("Unreversed=" + unReversed);

            Assert.True(testNum == unReversed, "Reverse endian operation for uint64 did not work successfully.");
        }
    }
}
