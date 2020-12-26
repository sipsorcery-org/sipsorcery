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

using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPHeaderUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RTPHeaderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void GetHeaderTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader rtpHeader = new RTPHeader();
            byte[] headerBuffer = rtpHeader.GetHeader(1, 0, 1);

            int byteNum = 1;
            foreach (byte headerByte in headerBuffer)
            {
                logger.LogDebug(byteNum + ": " + headerByte.ToString("x"));
                byteNum++;
            }
        }

        [Fact]
        public void HeaderRoundTripTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader src = new RTPHeader();
            byte[] headerBuffer = src.GetHeader(1, 0, 1);
            RTPHeader dst = new RTPHeader(headerBuffer);

            logger.LogDebug("Versions: " + src.Version + ", " + dst.Version);
            logger.LogDebug("PaddingFlag: " + src.PaddingFlag + ", " + dst.PaddingFlag);
            logger.LogDebug("HeaderExtensionFlag: " + src.HeaderExtensionFlag + ", " + dst.HeaderExtensionFlag);
            logger.LogDebug("CSRCCount: " + src.CSRCCount + ", " + dst.CSRCCount);
            logger.LogDebug("MarkerBit: " + src.MarkerBit + ", " + dst.MarkerBit);
            logger.LogDebug("PayloadType: " + src.PayloadType + ", " + dst.PayloadType);
            logger.LogDebug("SequenceNumber: " + src.SequenceNumber + ", " + dst.SequenceNumber);
            logger.LogDebug("Timestamp: " + src.Timestamp + ", " + dst.Timestamp);
            logger.LogDebug("SyncSource: " + src.SyncSource + ", " + dst.SyncSource);

            logger.LogDebug("Raw Header: " + System.Text.Encoding.ASCII.GetString(headerBuffer, 0, headerBuffer.Length));

            Assert.True(src.Version == dst.Version, "Version was mismatched.");
            Assert.True(src.PaddingFlag == dst.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.True(src.HeaderExtensionFlag == dst.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
            Assert.True(src.CSRCCount == dst.CSRCCount, "CSRCCount was mismatched.");
            Assert.True(src.MarkerBit == dst.MarkerBit, "MarkerBit was mismatched.");
            Assert.True(src.SequenceNumber == dst.SequenceNumber, "PayloadType was mismatched.");
            Assert.True(src.HeaderExtensionFlag == dst.HeaderExtensionFlag, "SequenceNumber was mismatched.");
            Assert.True(src.Timestamp == dst.Timestamp, "Timestamp was mismatched.");
            Assert.True(src.SyncSource == dst.SyncSource, "SyncSource was mismatched.");
        }

        [Fact]
        public void CustomisedHeaderRoundTripTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader src = new RTPHeader();
            src.Version = 3;
            src.PaddingFlag = 1;
            src.HeaderExtensionFlag = 1;
            src.MarkerBit = 1;
            src.CSRCCount = 3;
            src.PayloadType = (int)SDPWellKnownMediaFormatsEnum.PCMA;

            byte[] headerBuffer = src.GetHeader(1, 0, 1);

            RTPHeader dst = new RTPHeader(headerBuffer);

            logger.LogDebug("Versions: " + src.Version + ", " + dst.Version);
            logger.LogDebug("PaddingFlag: " + src.PaddingFlag + ", " + dst.PaddingFlag);
            logger.LogDebug("HeaderExtensionFlag: " + src.HeaderExtensionFlag + ", " + dst.HeaderExtensionFlag);
            logger.LogDebug("CSRCCount: " + src.CSRCCount + ", " + dst.CSRCCount);
            logger.LogDebug("MarkerBit: " + src.MarkerBit + ", " + dst.MarkerBit);
            logger.LogDebug("PayloadType: " + src.PayloadType + ", " + dst.PayloadType);
            logger.LogDebug("SequenceNumber: " + src.SequenceNumber + ", " + dst.SequenceNumber);
            logger.LogDebug("Timestamp: " + src.Timestamp + ", " + dst.Timestamp);
            logger.LogDebug("SyncSource: " + src.SyncSource + ", " + dst.SyncSource);

            string rawHeader = null;
            foreach (byte headerByte in headerBuffer)
            {
                rawHeader += headerByte.ToString("x");
            }

            logger.LogDebug("Raw Header: " + rawHeader);

            Assert.True(src.Version == dst.Version, "Version was mismatched.");
            Assert.True(src.PaddingFlag == dst.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.True(src.HeaderExtensionFlag == dst.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
            Assert.True(src.CSRCCount == dst.CSRCCount, "CSRCCount was mismatched.");
            Assert.True(src.MarkerBit == dst.MarkerBit, "MarkerBit was mismatched.");
            Assert.True(src.SequenceNumber == dst.SequenceNumber, "PayloadType was mismatched.");
            Assert.True(src.HeaderExtensionFlag == dst.HeaderExtensionFlag, "SequenceNumber was mismatched.");
            Assert.True(src.Timestamp == dst.Timestamp, "Timestamp was mismatched.");
            Assert.True(src.SyncSource == dst.SyncSource, "SyncSource was mismatched.");
        }

        [Fact]
        public void ParseRawRtpTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] rtpBytes = new byte[] {
                0x80, 0x88, 0xe6, 0xfd, 0x00, 0x00, 0x00, 0xf0, 0xde, 0xe0, 0xee, 0x8f,
                0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5
               };

            RTPPacket rtpPacket = new RTPPacket(rtpBytes);

            Assert.True(2 == rtpPacket.Header.Version, "Version was mismatched.");
            Assert.True(0 == rtpPacket.Header.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.True(0 == rtpPacket.Header.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
            Assert.True(0 == rtpPacket.Header.CSRCCount, "CSRCCount was mismatched.");
            Assert.True(1 == rtpPacket.Header.MarkerBit, "MarkerBit was mismatched.");
            Assert.True(59133 == rtpPacket.Header.SequenceNumber, "SequenceNumber was mismatched..");
            Assert.True(240U == rtpPacket.Header.Timestamp, "Timestamp was mismatched.");
            Assert.True(3739283087 == rtpPacket.Header.SyncSource, "SyncSource was mismatched.");
        }

        [Fact]
        public void ParseRawRtpWithExtensionTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] rtpBytes = new byte[] {
                0x90, 0x88, 0xe6, 0xfd,
                0x00, 0x00, 0x00, 0xf0,
                0xde, 0xe0, 0xee, 0x8f,
                0x00, 0x01, 0x00, 0x01,
                0xd5, 0xd5, 0xd5, 0xd5,
                0xd5, 0xd5, 0xd5, 0xd5,
                0xd5, 0xd5, 0xd5, 0xd5
               };

            RTPPacket rtpPacket = new RTPPacket(rtpBytes);

            Assert.True(2 == rtpPacket.Header.Version, "Version was mismatched.");
            Assert.True(0 == rtpPacket.Header.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.True(1 == rtpPacket.Header.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
            Assert.True(0 == rtpPacket.Header.CSRCCount, "CSRCCount was mismatched.");
            Assert.True(1 == rtpPacket.Header.MarkerBit, "MarkerBit was mismatched.");
            Assert.True(59133 == rtpPacket.Header.SequenceNumber, "SequenceNumber was mismatched..");
            Assert.True(240U == rtpPacket.Header.Timestamp, "Timestamp was mismatched.");
            Assert.True(3739283087 == rtpPacket.Header.SyncSource, "SyncSource was mismatched.");
            Assert.True(1U == rtpPacket.Header.ExtensionLength, "Extension Length was mismatched.");
            Assert.True(rtpPacket.Header.ExtensionPayload.Length == rtpPacket.Header.ExtensionLength * 4, "Extension length and payload were mismatched.");
        }

        [Fact]
        public void ParseChromeRtpPacketUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var buffer = TypeExtensions.ParseHexStr("800000099D5904B4838FCECF7F1E");

            RTPPacket rtp = new RTPPacket(buffer);

            logger.LogDebug($"RTP SSRC: {rtp.Header.SyncSource}");
            logger.LogDebug($"RTP length: {rtp.Header.Length}");

            Assert.NotNull(rtp);
        }
    }
}
