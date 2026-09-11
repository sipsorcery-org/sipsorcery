//-----------------------------------------------------------------------------
// Filename: H26XPacketiserUnitTest.cs
//
// Description: Unit tests for the H264 and H265 packetiser NAL parsing. These
// pin the exact NAL boundaries produced from Annex B access units so that any
// change to the buffer slicing logic (e.g. converting LINQ Skip/Take to span
// slices) that introduces an off-by-one or out-of-range length is caught.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 11 Jun 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.net.RTP.Packetisation;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class H26XPacketiserUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public H26XPacketiserUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that an H264 Annex B access unit containing a NAL delimited by a 4 byte start
        /// code followed by a NAL delimited by a 3 byte start code is split into the correct NAL
        /// payloads with the correct last-NAL indications.
        /// </summary>
        [Fact]
        public void ParseH264NalsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] nal1 = { 0x67, 0xAA, 0xBB, 0xCC, 0xDD };
            byte[] nal2 = { 0x68, 0xEE, 0x99 };

            byte[] accessUnit = new byte[] { 0x00, 0x00, 0x00, 0x01 }
                .Concat(nal1)
                .Concat(new byte[] { 0x00, 0x00, 0x01 })
                .Concat(nal2)
                .ToArray();

            var nals = H264Packetiser.ParseNals(accessUnit).ToList();

            Assert.Equal(2, nals.Count);
            Assert.Equal(nal1, nals[0].NAL);
            Assert.False(nals[0].IsLast);
            Assert.Equal(nal2, nals[1].NAL);
            Assert.True(nals[1].IsLast);
        }

        /// <summary>
        /// Tests that an H265 Annex B access unit containing a NAL delimited by a 4 byte start
        /// code followed by a NAL delimited by a 3 byte start code is split into the correct NAL
        /// payloads with the correct last-NAL indications.
        /// </summary>
        [Fact]
        public void ParseH265NalsUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            byte[] nal1 = { 0x40, 0x01, 0xAA, 0xBB, 0xCC };
            byte[] nal2 = { 0x42, 0x01, 0xDD };

            byte[] accessUnit = new byte[] { 0x00, 0x00, 0x00, 0x01 }
                .Concat(nal1)
                .Concat(new byte[] { 0x00, 0x00, 0x01 })
                .Concat(nal2)
                .ToArray();

            var nals = H265Packetiser.ParseNals(accessUnit).ToList();

            Assert.Equal(2, nals.Count);
            Assert.Equal(nal1, nals[0].NAL);
            Assert.False(nals[0].IsLast);
            Assert.Equal(nal2, nals[1].NAL);
            Assert.True(nals[1].IsLast);
        }
    }
}
