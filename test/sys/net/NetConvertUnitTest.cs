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
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class UtilitiesUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public UtilitiesUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void ReverseUInt16SampleTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            ushort testNum = 45677;
            byte[] testNumBytes = BitConverter.GetBytes(testNum);

            ushort reversed = NetConvert.DoReverseEndian(testNum);
            byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

            ushort unReversed = NetConvert.DoReverseEndian(reversed);

            int testNumByteCount = 0;
            foreach (byte testNumByte in testNumBytes)
            {
                logger.LogDebug("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                testNumByteCount++;
            }

            int reverseNumByteCount = 0;
            foreach (byte reverseNumByte in reversedNumBytes)
            {
                logger.LogDebug("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                reverseNumByteCount++;
            }

            logger.LogDebug("Original=" + testNum);
            logger.LogDebug("Reversed=" + reversed);
            logger.LogDebug("Unreversed=" + unReversed);

            Assert.True(testNum == unReversed, "Reverse endian operation for uint16 did not work successfully.");
        }

        [Fact]
        public void ReverseUInt32SampleTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            uint testNum = 123124;
            byte[] testNumBytes = BitConverter.GetBytes(testNum);

            uint reversed = NetConvert.DoReverseEndian(testNum);
            byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

            uint unReversed = NetConvert.DoReverseEndian(reversed);

            int testNumByteCount = 0;
            foreach (byte testNumByte in testNumBytes)
            {
                logger.LogDebug("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                testNumByteCount++;
            }

            int reverseNumByteCount = 0;
            foreach (byte reverseNumByte in reversedNumBytes)
            {
                logger.LogDebug("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                reverseNumByteCount++;
            }

            logger.LogDebug("Original=" + testNum);
            logger.LogDebug("Reversed=" + reversed);
            logger.LogDebug("Unreversed=" + unReversed);

            Assert.True(testNum == unReversed, "Reverse endian operation for uint32 did not work successfully.");
        }

        [Fact]
        public void ReverseUInt64SampleTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            ulong testNum = 1231265499856464;
            byte[] testNumBytes = BitConverter.GetBytes(testNum);

            ulong reversed = NetConvert.DoReverseEndian(testNum);
            byte[] reversedNumBytes = BitConverter.GetBytes(reversed);

            ulong unReversed = NetConvert.DoReverseEndian(reversed);

            int testNumByteCount = 0;
            foreach (byte testNumByte in testNumBytes)
            {
                logger.LogDebug("original " + testNumByteCount + ": " + testNumByte.ToString("x"));
                testNumByteCount++;
            }

            int reverseNumByteCount = 0;
            foreach (byte reverseNumByte in reversedNumBytes)
            {
                logger.LogDebug("reversed " + reverseNumByteCount + ": " + reverseNumByte.ToString("x"));
                reverseNumByteCount++;
            }

            logger.LogDebug("Original=" + testNum);
            logger.LogDebug("Reversed=" + reversed);
            logger.LogDebug("Unreversed=" + unReversed);

            Assert.True(testNum == unReversed, "Reverse endian operation for uint64 did not work successfully.");
        }

        [Fact]
        public void ReverseInt32UnitTest()
        {
            //var b = BitConverter.GetBytes(-923871);
            //logger.LogDebug($"{b[0]:X} {b[1]:X} {b[2]:X} {b[3]:X}");
            //logger.LogDebug($"{b[3]:X} {b[2]:X} {b[1]:X} {b[0]:X}");
            //logger.LogDebug(BitConverter.ToInt32(new byte[] { b[3], b[2], b[1], b[0] }, 0).ToString());

            Assert.Equal(0, NetConvert.DoReverseEndian(0));
            Assert.Equal(-1, NetConvert.DoReverseEndian(-1));
            Assert.Equal(-2, NetConvert.DoReverseEndian(-16777217));
            Assert.Equal(568848895, NetConvert.DoReverseEndian(-923871));
            Assert.Equal(128, NetConvert.DoReverseEndian(Int32.MinValue));
            Assert.Equal(1, NetConvert.DoReverseEndian(16777216));
            Assert.Equal(2, NetConvert.DoReverseEndian(33554432));
            Assert.Equal(4564522, NetConvert.DoReverseEndian(715539712));
            Assert.Equal(-129, NetConvert.DoReverseEndian(Int32.MaxValue));
            Assert.Equal(0x0d0c0b0a, NetConvert.DoReverseEndian(0x0a0b0c0d));
        }
    }
}
