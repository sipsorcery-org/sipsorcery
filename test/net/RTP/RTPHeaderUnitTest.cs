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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Net.UnitTests
{
    [TestClass]
    public class RTPHeaderUnitTest
    {
        [TestMethod]
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

        [TestMethod]
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

            Assert.IsTrue(src.Version == dst.Version, "Version was mismatched.");
            Assert.IsTrue(src.PaddingFlag == dst.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.IsTrue(src.HeaderExtensionFlag == dst.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
            Assert.IsTrue(src.CSRCCount == dst.CSRCCount, "CSRCCount was mismatched.");
            Assert.IsTrue(src.MarkerBit == dst.MarkerBit, "MarkerBit was mismatched.");
            Assert.IsTrue(src.SequenceNumber == dst.SequenceNumber, "PayloadType was mismatched.");
            Assert.IsTrue(src.HeaderExtensionFlag == dst.HeaderExtensionFlag, "SequenceNumber was mismatched.");
            Assert.IsTrue(src.Timestamp == dst.Timestamp, "Timestamp was mismatched.");
            Assert.IsTrue(src.SyncSource == dst.SyncSource, "SyncSource was mismatched.");
        }

        [TestMethod]
        public void CustomisedHeaderRoundTripTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTPHeader src = new RTPHeader();
            src.Version = 3;
            src.PaddingFlag = 1;
            src.HeaderExtensionFlag = 1;
            src.MarkerBit = 1;
            src.CSRCCount = 3;
            src.PayloadType = (int)RTPPayloadTypesEnum.GSM;

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

            Assert.IsTrue(src.Version == dst.Version, "Version was mismatched.");
            Assert.IsTrue(src.PaddingFlag == dst.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.IsTrue(src.HeaderExtensionFlag == dst.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
            Assert.IsTrue(src.CSRCCount == dst.CSRCCount, "CSRCCount was mismatched.");
            Assert.IsTrue(src.MarkerBit == dst.MarkerBit, "MarkerBit was mismatched.");
            Assert.IsTrue(src.SequenceNumber == dst.SequenceNumber, "PayloadType was mismatched.");
            Assert.IsTrue(src.HeaderExtensionFlag == dst.HeaderExtensionFlag, "SequenceNumber was mismatched.");
            Assert.IsTrue(src.Timestamp == dst.Timestamp, "Timestamp was mismatched.");
            Assert.IsTrue(src.SyncSource == dst.SyncSource, "SyncSource was mismatched.");
        }

        [TestMethod]
        public void ParseRawRtpTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] rtpBytes = new byte[] {
                0x80, 0x88, 0xe6, 0xfd, 0x00, 0x00, 0x00, 0xf0, 0xde, 0xe0, 0xee, 0x8f,
                0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5, 0xd5
               };

            RTPPacket rtpPacket = new RTPPacket(rtpBytes);

            Assert.AreEqual(2, rtpPacket.Header.Version, "Version was mismatched.");
            Assert.AreEqual(0, rtpPacket.Header.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.AreEqual(0, rtpPacket.Header.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
            Assert.AreEqual(0, rtpPacket.Header.CSRCCount, "CSRCCount was mismatched.");
            Assert.AreEqual(1, rtpPacket.Header.MarkerBit, "MarkerBit was mismatched.");
            Assert.AreEqual(59133, rtpPacket.Header.SequenceNumber, "SequenceNumber was mismatched..");
            Assert.AreEqual(240U, rtpPacket.Header.Timestamp, "Timestamp was mismatched.");
            Assert.AreEqual(3739283087, rtpPacket.Header.SyncSource, "SyncSource was mismatched.");
        }

        [TestMethod]
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

            Assert.AreEqual(2, rtpPacket.Header.Version, "Version was mismatched.");
            Assert.AreEqual(0, rtpPacket.Header.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.AreEqual(1, rtpPacket.Header.HeaderExtensionFlag, "HeaderExtensionFlag was mismatched.");
            Assert.AreEqual(0, rtpPacket.Header.CSRCCount, "CSRCCount was mismatched.");
            Assert.AreEqual(1, rtpPacket.Header.MarkerBit, "MarkerBit was mismatched.");
            Assert.AreEqual(59133, rtpPacket.Header.SequenceNumber, "SequenceNumber was mismatched..");
            Assert.AreEqual(240U, rtpPacket.Header.Timestamp, "Timestamp was mismatched.");
            Assert.AreEqual(3739283087, rtpPacket.Header.SyncSource, "SyncSource was mismatched.");
            Assert.AreEqual(1U, rtpPacket.Header.ExtensionLength, "Extension Length was mismatched.");
            Assert.AreEqual(rtpPacket.Header.ExtensionPayload.Length, rtpPacket.Header.ExtensionLength * 4, "Extension length and payload were mismatched.");
        }
    }
}
