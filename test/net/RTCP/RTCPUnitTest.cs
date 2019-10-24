//-----------------------------------------------------------------------------
// Filename: RTCPUnitTest.cs
//
// Description: Implementation of RTP Control Protocol.
//
// Author(s):
// Aaron Clauson
//
// History:
// 11 Aug 2019	Aaron Clauson	Refactored from RTCP class(aaron@sipsorcery.com), Montreux, Switzerland (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Net.UnitTests
{
    [TestClass]
    public class RTCPReportUnitTest
    {
        /*			
        [Test]
        public void InitialSampleTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            ushort syncSource = 1234;
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5060);
            ushort startSeqNum = 1;

            RTCPReport report = new RTCPReport(syncSource, localEndPoint, syncSource);

            report.AddSample(startSeqNum++, DateTime.Now, 100);

            Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

            Assert.IsTrue(report.TotalPackets == 1, "Incorrect number of packets in report.");
        }


        [Test]
        public void EmpytySampleTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            ushort syncSource = 1234;
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5060);
            ushort startSeqNum = 1;

            RTCPReport report = new RTCPReport(syncSource, localEndPoint, syncSource);
            report.ReportSampleDuration = 100; // Reduce report duration for unit test.

            report.AddSample(startSeqNum++, DateTime.Now, 100);

            Thread.Sleep(50);

            report.AddSample(startSeqNum++, DateTime.Now, 100);

            Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

            Thread.Sleep(300);

            report.AddSample(startSeqNum++, DateTime.Now, 100);

            Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

            Assert.IsTrue(report.m_samples.Count == 2, "Incorrect number of reports in the queue.");

            RTCPReport sample1 = report.GetNextSample();
            RTCPReport sample2 = report.GetNextSample();

            Console.WriteLine("Sample1: " + sample1.SampleStartTime.ToString("mm:ss:fff") + " to " + sample1.SampleEndTime.ToString("mm:ss:fff"));
            Console.WriteLine("Sample2: " + sample2.SampleStartTime.ToString("mm:ss:fff") + " to " + sample2.SampleEndTime.ToString("mm:ss:fff"));

            Assert.IsTrue(sample1.TotalPackets == 2, "Incorrect number of packets in sample1.");
            Assert.IsTrue(report.m_previousSample == null, "Previous sample should have been null after an empty sample.");

            //Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

            report.AddSample(startSeqNum++, DateTime.Now, 100);
            report.AddSample(startSeqNum++, DateTime.Now, 100);
            report.AddSample(startSeqNum++, DateTime.Now, 100);

            Thread.Sleep(120);

            Console.WriteLine("new sample");

            report.AddSample(startSeqNum++, DateTime.Now, 100);
            report.AddSample(startSeqNum++, DateTime.Now, 100);

            Thread.Sleep(120);

            Console.WriteLine("new sample");

            report.AddSample(startSeqNum++, DateTime.Now, 100);

            Console.WriteLine("Sample count = " + report.m_samples.Count + ".");

            sample1 = report.GetNextSample();

            Console.WriteLine("Sample1: " + sample1.SampleStartTime.ToString("mm:ss:fff") + " to " + sample1.SampleEndTime.ToString("mm:ss:fff"));
            Console.WriteLine(sample1.StartSequenceNumber + " to " + sample1.EndSequenceNumber);
        }


        [Test]
        public void DropTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            ushort syncSource = 1234;
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5060);
            ushort seqNum = 1;

            RTCPReport report = new RTCPReport(syncSource, localEndPoint, syncSource);
            report.ReportSampleDuration = 100;	

            report.AddSample(seqNum, DateTime.Now, 100);

            seqNum += 2;

            report.AddSample(seqNum, DateTime.Now, 100);

            Console.WriteLine("total packets = " + report.TotalPackets + ", outoforder = " + report.OutOfOrderPackets + ", drop " + report.PacketsLost + "."); 

            Assert.IsTrue(report.TotalPackets == 2, "Incorrect packet count in sample.");
            Assert.IsTrue(report.PacketsLost == 1, "Incorrect dropped packet count.");	

            Thread.Sleep(120);

            report.AddSample(seqNum++, DateTime.Now, 100);

            Thread.Sleep(120);

            report.AddSample(seqNum++, DateTime.Now, 100);

            Assert.IsTrue(report.m_samples.Count == 1, "Queue size was incorrect.");

            RTCPReport sample1 = report.GetNextSample();
            Console.WriteLine("Packets lost = " + sample1.PacketsLost);

            Assert.IsTrue(sample1.PacketsLost == 1, "Packets lost count was incorrect.");
        }

        [Test]
        public void OutOfOrderTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

            ushort syncSource = 1234;
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, 5060);
            ushort seqNum = 1;

            RTCPReport report = new RTCPReport(syncSource, localEndPoint, syncSource);
            report.ReportSampleDuration = 100000;	// Stop timings interfering.

            report.AddSample(seqNum, DateTime.Now, 100);

            seqNum += 2;

            report.AddSample(seqNum, DateTime.Now, 100);
            report.AddSample(Convert.ToUInt16(seqNum-1), DateTime.Now, 100);

            Console.WriteLine("total packets = " + report.TotalPackets + ", outoforder = " + report.OutOfOrderPackets + "."); 

            Assert.IsTrue(report.TotalPackets == 3, "Incorrect packet count in sample.");
            Assert.IsTrue(report.OutOfOrderPackets == 2, "Incorrect outoforder packet count.");	
        }
        */

        [TestMethod]
        public void GetRTCPHeaderTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPHeader rtcpHeader = new RTCPHeader();
            byte[] headerBuffer = rtcpHeader.GetHeader(0, 0);

            int byteNum = 1;
            foreach (byte headerByte in headerBuffer)
            {
                Console.WriteLine(byteNum + ": " + headerByte.ToString("x"));
                byteNum++;
            }
        }

        [TestMethod]
        public void RTCPHeaderRoundTripTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            RTCPHeader src = new RTCPHeader();
            byte[] headerBuffer = src.GetHeader(17, 54443);
            RTCPHeader dst = new RTCPHeader(headerBuffer);

            Console.WriteLine("Version: " + src.Version + ", " + dst.Version);
            Console.WriteLine("PaddingFlag: " + src.PaddingFlag + ", " + dst.PaddingFlag);
            Console.WriteLine("ReceptionReportCount: " + src.ReceptionReportCount + ", " + dst.ReceptionReportCount);
            Console.WriteLine("PacketType: " + src.PacketType + ", " + dst.PacketType);
            Console.WriteLine("Length: " + src.Length + ", " + dst.Length);

            //Console.WriteLine("Raw Header: " + System.Text.Encoding.ASCII.GetString(headerBuffer, 0, headerBuffer.Length));

            Assert.IsTrue(src.Version == dst.Version, "Version was mismatched.");
            Assert.IsTrue(src.PaddingFlag == dst.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.IsTrue(src.ReceptionReportCount == dst.ReceptionReportCount, "ReceptionReportCount was mismatched.");
            Assert.IsTrue(src.PacketType == dst.PacketType, "PacketType was mismatched.");
            Assert.IsTrue(src.Length == dst.Length, "Length was mismatched.");
        }


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
