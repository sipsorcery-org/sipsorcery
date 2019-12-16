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
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
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

            Assert.True(report.TotalPackets == 1, "Incorrect number of packets in report.");
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

            Assert.True(report.m_samples.Count == 2, "Incorrect number of reports in the queue.");

            RTCPReport sample1 = report.GetNextSample();
            RTCPReport sample2 = report.GetNextSample();

            Console.WriteLine("Sample1: " + sample1.SampleStartTime.ToString("mm:ss:fff") + " to " + sample1.SampleEndTime.ToString("mm:ss:fff"));
            Console.WriteLine("Sample2: " + sample2.SampleStartTime.ToString("mm:ss:fff") + " to " + sample2.SampleEndTime.ToString("mm:ss:fff"));

            Assert.True(sample1.TotalPackets == 2, "Incorrect number of packets in sample1.");
            Assert.True(report.m_previousSample == null, "Previous sample should have been null after an empty sample.");

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

            Assert.True(report.TotalPackets == 2, "Incorrect packet count in sample.");
            Assert.True(report.PacketsLost == 1, "Incorrect dropped packet count.");	

            Thread.Sleep(120);

            report.AddSample(seqNum++, DateTime.Now, 100);

            Thread.Sleep(120);

            report.AddSample(seqNum++, DateTime.Now, 100);

            Assert.True(report.m_samples.Count == 1, "Queue size was incorrect.");

            RTCPReport sample1 = report.GetNextSample();
            Console.WriteLine("Packets lost = " + sample1.PacketsLost);

            Assert.True(sample1.PacketsLost == 1, "Packets lost count was incorrect.");
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

            Assert.True(report.TotalPackets == 3, "Incorrect packet count in sample.");
            Assert.True(report.OutOfOrderPackets == 2, "Incorrect outoforder packet count.");	
        }
        */

        [Fact]
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

        [Fact]
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

            Assert.True(src.Version == dst.Version, "Version was mismatched.");
            Assert.True(src.PaddingFlag == dst.PaddingFlag, "PaddingFlag was mismatched.");
            Assert.True(src.ReceptionReportCount == dst.ReceptionReportCount, "ReceptionReportCount was mismatched.");
            Assert.True(src.PacketType == dst.PacketType, "PacketType was mismatched.");
            Assert.True(src.Length == dst.Length, "Length was mismatched.");
        }


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
            src.PayloadType = (int)SDPMediaFormatsEnum.PCMA;

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
