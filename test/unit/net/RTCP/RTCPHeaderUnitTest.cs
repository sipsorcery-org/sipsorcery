//-----------------------------------------------------------------------------
// Filename: RTCPHeaderUnitTest.cs
//
// Description: Unit tests for the RTCPHeader class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 11 Aug 2019	Aaron Clauson	Refactored from RTCP class, Montreux, Switzerland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPHeaderUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTCPHeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void GetRTCPHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPHeader rtcpHeader = new RTCPHeader(RTCPReportTypesEnum.SR, 1);
            byte[] headerBuffer = rtcpHeader.GetHeader(0, 0);

            int byteNum = 1;
            foreach (byte headerByte in headerBuffer)
            {
                logger.LogDebug(byteNum + ": " + headerByte.ToString("x"));
                byteNum++;
            }
        }

        [Fact]
        public void RTCPHeaderRoundTripTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPHeader src = new RTCPHeader(RTCPReportTypesEnum.SR, 1);
            byte[] headerBuffer = src.GetHeader(17, 54443);
            RTCPHeader dst = new RTCPHeader(headerBuffer);

            logger.LogDebug("Version: " + src.Version + ", " + dst.Version);
            logger.LogDebug("PaddingFlag: " + src.PaddingFlag + ", " + dst.PaddingFlag);
            logger.LogDebug("ReceptionReportCount: " + src.ReceptionReportCount + ", " + dst.ReceptionReportCount);
            logger.LogDebug("PacketType: " + src.PacketType + ", " + dst.PacketType);
            logger.LogDebug("Length: " + src.Length + ", " + dst.Length);

            logger.LogDebug($"Raw Header: {headerBuffer.HexStr(headerBuffer.Length)}.");

            Assert.True(src.Version == dst.Version, "Version was mismatched.");
            Assert.True(src.PaddingFlag == dst.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.True(src.ReceptionReportCount == dst.ReceptionReportCount, "ReceptionReportCount was mismatched.");
            Assert.True(src.PacketType == dst.PacketType, "PacketType was mismatched.");
            Assert.True(src.Length == dst.Length, "Length was mismatched.");
        }
    }
}
