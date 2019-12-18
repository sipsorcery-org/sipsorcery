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

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPVP8HeaderUnitTest
    {
        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        public RTPVP8HeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests getting the VP8 header for an intermediate (non-key) frame.
        /// </summary>
        [Fact]
        public void GeIntermediateFrameHeaderTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPVP8Header rtpVP8Header = new RTPVP8Header()
            {
                StartOfVP8Partition = true,
                FirstPartitionSize = 54
            };

            byte[] headerBuffer = rtpVP8Header.GetBytes();

            logger.LogDebug(BitConverter.ToString(headerBuffer, 0));
        }

        /// <summary>
        /// Tests that a known VP8 header is correctly parsed.
        /// </summary>
        [Fact]
        public void ParseKnownVP8HeaderTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] rawHeader = new byte[] { 0x90, 0x80, 0x00, 0x30, 0xd4, 0x00 };

            var knownHeader = RTPVP8Header.GetVP8Header(rawHeader);
            var outputBytes = knownHeader.GetBytes();

            Assert.Equal(1697, knownHeader.FirstPartitionSize);

            for (int index = 0; index < rawHeader.Length; index++)
            {
                Assert.Equal(rawHeader[index], outputBytes[index]);
            }
        }


        /// <summary>
        /// Tests that the first partition size is parsed and then returned correctly.
        /// </summary>
        [Fact]
        public void ReversePartitionSizeTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPVP8Header rtpVP8Header = new RTPVP8Header()
            {
                StartOfVP8Partition = true,
                FirstPartitionSize = 5897
            };

            byte[] headerBuffer = rtpVP8Header.GetBytes();

            var mirroredHeader = RTPVP8Header.GetVP8Header(rtpVP8Header.GetBytes());

            Assert.Equal(rtpVP8Header.FirstPartitionSize, mirroredHeader.FirstPartitionSize);
        }

        /// <summary>
        /// Tests that the VP8 header is correctly parsed when a two byte picure ID is used.
        /// </summary>
        [Fact]
        public void CheckLengthForTwoBytePicutreIDTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] rawHeader = new byte[] { 0x80, 0x80, 0x80, 0x01 };

            var vp8Header = RTPVP8Header.GetVP8Header(rawHeader);

            Assert.Equal(4, vp8Header.PayloadDescriptorLength);
        }

        /// <summary>
        /// Tests that the VP8 header is correctly parsed when a single byte picure ID is used.
        /// </summary>
        [Fact]
        public void CheckLengthForSingleBytePicutreIDTest()
        {
            logger.LogDebug(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] rawHeader = new byte[] { 0x80, 0x80, 0x7F };

            var vp8Header = RTPVP8Header.GetVP8Header(rawHeader);

            Assert.Equal(3, vp8Header.PayloadDescriptorLength);
        }
    }
}
