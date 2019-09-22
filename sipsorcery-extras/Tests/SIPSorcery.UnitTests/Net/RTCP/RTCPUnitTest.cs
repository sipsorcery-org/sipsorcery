//-----------------------------------------------------------------------------
// Filename: RTCPUnitTest.cs
//
// Description: Implementation of RTP Control Protocol.
//
// History:
// 11 Aug 2019	Aaron Clauson	Refactored from RTCP class.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2019 Aaron Clauson (aaron@sipsorcery.com), Montreux, Switzerland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Net.UnitTests.RTCP
{
    [TestClass]
    public class RTCPReportUnitTest
    {
        [TestMethod]
        public void SampleTest()
        {
            Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
        }

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
    }
}
