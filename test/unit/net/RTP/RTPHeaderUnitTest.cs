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

using System.Linq;
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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader rtpHeader = new RTPHeader();
            byte[] headerBuffer = rtpHeader.GetHeader(1, 0, 1);

            int byteNum = 1;
            foreach (byte headerByte in headerBuffer)
            {
                logger.LogDebug("{ByteNum}: {HeaderByte}", byteNum, headerByte.ToString("x"));
                byteNum++;
            }
        }

        [Fact]
        public void HeaderRoundTripTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader src = new RTPHeader();
            byte[] headerBuffer = src.GetHeader(1, 0, 1);
            RTPHeader dst = new RTPHeader(headerBuffer);

            logger.LogDebug("Versions: {SrcVersion}, {DstVersion}", src.Version, dst.Version);
            logger.LogDebug("PaddingFlag: {SrcPaddingFlag}, {DstPaddingFlag}", src.PaddingFlag, dst.PaddingFlag);
            logger.LogDebug("HeaderExtensionFlag: {SrcHeaderExtensionFlag}, {DstHeaderExtensionFlag}", src.HeaderExtensionFlag, dst.HeaderExtensionFlag);
            logger.LogDebug("CSRCCount: {SrcCSRCCount}, {DstCSRCCount}", src.CSRCCount, dst.CSRCCount);
            logger.LogDebug("MarkerBit: {SrcMarkerBit}, {DstMarkerBit}", src.MarkerBit, dst.MarkerBit);
            logger.LogDebug("PayloadType: {SrcPayloadType}, {DstPayloadType}", src.PayloadType, dst.PayloadType);
            logger.LogDebug("SequenceNumber: {SrcSequenceNumber}, {DstSequenceNumber}", src.SequenceNumber, dst.SequenceNumber);
            logger.LogDebug("Timestamp: {SrcTimestamp}, {DstTimestamp}", src.Timestamp, dst.Timestamp);
            logger.LogDebug("SyncSource: {SrcSyncSource}, {DstSyncSource}", src.SyncSource, dst.SyncSource);

            logger.LogDebug("Raw Header: {RawHeader}", System.Text.Encoding.ASCII.GetString(headerBuffer, 0, headerBuffer.Length));

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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
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

            logger.LogDebug("Versions: {SrcVersion}, {DstVersion}", src.Version, dst.Version);
            logger.LogDebug("PaddingFlag: {SrcPaddingFlag}, {DstPaddingFlag}", src.PaddingFlag, dst.PaddingFlag);
            logger.LogDebug("HeaderExtensionFlag: {SrcHeaderExtensionFlag}, {DstHeaderExtensionFlag}", src.HeaderExtensionFlag, dst.HeaderExtensionFlag);
            logger.LogDebug("CSRCCount: {SrcCSRCCount}, {DstCSRCCount}", src.CSRCCount, dst.CSRCCount);
            logger.LogDebug("MarkerBit: {SrcMarkerBit}, {DstMarkerBit}", src.MarkerBit, dst.MarkerBit);
            logger.LogDebug("PayloadType: {SrcPayloadType}, {DstPayloadType}", src.PayloadType, dst.PayloadType);
            logger.LogDebug("SequenceNumber: {SrcSequenceNumber}, {DstSequenceNumber}", src.SequenceNumber, dst.SequenceNumber);
            logger.LogDebug("Timestamp: {SrcTimestamp}, {DstTimestamp}", src.Timestamp, dst.Timestamp);
            logger.LogDebug("SyncSource: {SrcSyncSource}, {DstSyncSource}", src.SyncSource, dst.SyncSource);

            string rawHeader = null;
            foreach (byte headerByte in headerBuffer)
            {
                rawHeader += headerByte.ToString("x");
            }

            logger.LogDebug("Raw Header: {RawHeader}", rawHeader);

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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
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
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
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
            Verify(rtpPacket);

            void Verify(RTPPacket localRtpPacket)
            {
                Assert.True(2 == localRtpPacket.Header.Version, "Version was mismatched.");
                Assert.True(0 == localRtpPacket.Header.PaddingFlag, "PaddingFlag was mismatched.");
                Assert.True(1 == localRtpPacket.Header.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
                Assert.True(0 == localRtpPacket.Header.CSRCCount, "CSRCCount was mismatched.");
                Assert.True(1 == localRtpPacket.Header.MarkerBit, "MarkerBit was mismatched.");
                Assert.True(59133 == localRtpPacket.Header.SequenceNumber, "SequenceNumber was mismatched..");
                Assert.True(240U == localRtpPacket.Header.Timestamp, "Timestamp was mismatched.");
                Assert.True(3739283087 == localRtpPacket.Header.SyncSource, "SyncSource was mismatched.");
                Assert.True(1U == localRtpPacket.Header.ExtensionLength, "Extension Length was mismatched.");
                Assert.True(localRtpPacket.Header.ExtensionPayload.Length == localRtpPacket.Header.ExtensionLength * 4, "Extension length and payload were mismatched.");
            }

            var result = RTPPacket.TryParse(rtpBytes, out var p, out var consumed);
            Assert.True(result, "RTP packet was not parsed correctly.");
            Verify(p);
        }

        [Fact]
        public void ParseChromeRtpPacketUnitTest()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var buffer = TypeExtensions.ParseHexStr("800000099D5904B4838FCECF7F1E");

            RTPPacket rtp = new RTPPacket(buffer);

            logger.LogDebug("RTP SSRC: {RtpSSRC}", rtp.Header.SyncSource);
            logger.LogDebug("RTP length: {RtpLength}", rtp.Header.Length);

            Assert.NotNull(rtp);
        }

        [Fact]
        public void ParseHeaderExtensions()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var rtpHeaderBytes = new byte[] {
                0x90, 0x60, 0x0c, 0xd5, 0x83, 0x0a, 0xcd, 0x97, 0x2e, 0xba, 0x23, 0x57, 0xbe, 0xde, 0x00, 0x05,
                0xdf, 0xb3, 0x85, 0xb0, 0x8f, 0x0c, 0x13, 0x9d, 0xe5, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            };

            var header = new RTPHeader(rtpHeaderBytes);
            var extensions = header.GetHeaderExtensions();
            Assert.Single(extensions);
            var extension = extensions.Single();
            Assert.Equal(13, extension.Id);
            Assert.Equal(RTPHeaderExtensionType.OneByte, extension.Type);

            var expectedValue = new byte[] {0xb3, 0x85, 0xb0, 0x8f, 0xc, 0x13, 0x9d, 0xe5, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0, 0x0};
            Assert.Equal(expectedValue, extension.Data);
        }

        [Fact]
        public void should_empty_rtp_header_extensions()
        {
            var rtpPayload = new byte[] {
                0x90, 0x00, 0xb7, 0x5e, 0x5a, 0xbd,
                0x51, 0x99, 0x80, 0x09, 0x9e, 0x1b, 0xe0, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x09,
                0x9e, 0x1b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x71, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x13, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff
            };
            var packet = new RTPPacket(rtpPayload);
            var header = packet.Header;
            var extensions= header.GetHeaderExtensions().ToList();
            Assert.NotNull(extensions);
            Assert.Empty(extensions);
        }
    }
}
