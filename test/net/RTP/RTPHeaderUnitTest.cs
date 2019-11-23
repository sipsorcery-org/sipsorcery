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

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTPHeaderUnitTest
    {
        [Fact]
        public void GetHeaderTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader rtpHeader = new RTPHeader();
            byte[] headerBuffer = rtpHeader.GetHeader(1, 0, 1);

            int byteNum = 1;
            foreach (byte headerByte in headerBuffer)
            {
                Console.WriteLine(byteNum + ": " + headerByte.ToString("x"));
                byteNum++;
            }
        }

        [Fact]
        public void HeaderRoundTripTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader src = new RTPHeader();
            byte[] headerBuffer = src.GetHeader(1, 0, 1);
            RTPHeader dst = new RTPHeader(headerBuffer);

            Console.WriteLine("Versions: " + src.Version + ", " + dst.Version);
            Console.WriteLine("PaddingFlag: " + src.PaddingFlag + ", " + dst.PaddingFlag);
            Console.WriteLine("HeaderExtensionFlag: " + src.HeaderExtensionFlag + ", " + dst.HeaderExtensionFlag);
            Console.WriteLine("CSRCCount: " + src.CSRCCount + ", " + dst.CSRCCount);
            Console.WriteLine("MarkerBit: " + src.MarkerBit + ", " + dst.MarkerBit);
            Console.WriteLine("PayloadType: " + src.PayloadType + ", " + dst.PayloadType);
            Console.WriteLine("SequenceNumber: " + src.SequenceNumber + ", " + dst.SequenceNumber);
            Console.WriteLine("Timestamp: " + src.Timestamp + ", " + dst.Timestamp);
            Console.WriteLine("SyncSource: " + src.SyncSource + ", " + dst.SyncSource);

            Console.WriteLine("Raw Header: " + System.Text.Encoding.ASCII.GetString(headerBuffer, 0, headerBuffer.Length));

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
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader src = new RTPHeader();
            src.Version = 3;
            src.PaddingFlag = 1;
            src.HeaderExtensionFlag = 1;
            src.MarkerBit = 1;
            src.CSRCCount = 3;
            src.PayloadType = (int)RTPPayloadTypesEnum.PCMA;

            byte[] headerBuffer = src.GetHeader(1, 0, 1);

            RTPHeader dst = new RTPHeader(headerBuffer);

            Console.WriteLine("Versions: " + src.Version + ", " + dst.Version);
            Console.WriteLine("PaddingFlag: " + src.PaddingFlag + ", " + dst.PaddingFlag);
            Console.WriteLine("HeaderExtensionFlag: " + src.HeaderExtensionFlag + ", " + dst.HeaderExtensionFlag);
            Console.WriteLine("CSRCCount: " + src.CSRCCount + ", " + dst.CSRCCount);
            Console.WriteLine("MarkerBit: " + src.MarkerBit + ", " + dst.MarkerBit);
            Console.WriteLine("PayloadType: " + src.PayloadType + ", " + dst.PayloadType);
            Console.WriteLine("SequenceNumber: " + src.SequenceNumber + ", " + dst.SequenceNumber);
            Console.WriteLine("Timestamp: " + src.Timestamp + ", " + dst.Timestamp);
            Console.WriteLine("SyncSource: " + src.SyncSource + ", " + dst.SyncSource);

            string rawHeader = null;
            foreach (byte headerByte in headerBuffer)
            {
                rawHeader += headerByte.ToString("x");
            }

            Console.WriteLine("Raw Header: " + rawHeader);

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
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
    }
}
