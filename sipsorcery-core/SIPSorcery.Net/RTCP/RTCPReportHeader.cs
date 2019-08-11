//-----------------------------------------------------------------------------
// Filename: RTCPReportHeader.cs
//
// Description: Header for custom RTCP reports.
//
//      Custom RTCP Report Packet
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |      Report Type              |             Length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         Report                                |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// Header contains two fields eacha 16 bit unisgned integer:
// - Report Type: Type of report the report data contains,
//      - 0 RTCP
//      - 1 Network Test Descriptor.
// - Length: Length of the data in the report.
//
// History:
// 22 Feb 2007	Aaron Clauson	Created.
// 11 Aug 2019  Aaron Clauson   Added full license header.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007-2019 Aaron Clauson (aaron@sipsorcery.com), Montreux, Switzerland (www.sipsorcery.com)
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
using System.Collections;
using System.Net;
using SIPSorcery.Sys;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
    public enum RTCPReportTypesEnum
    {
        RTCP = 0,
        NetTestDescription = 1,
    }

    public class RTCPReportTypes
    {
        public static RTCPReportTypesEnum GetRTCPReportTypeForId(ushort rtcpReportTypeId)
        {
            return (RTCPReportTypesEnum)Enum.Parse(typeof(RTCPReportTypesEnum), rtcpReportTypeId.ToString(), true);
        }
    }

    public class RTCPReportHeader
	{
		public const int HEADER_BYTES_LENGTH = 4;

        public RTCPReportTypesEnum ReportType;      // 16 bits.
        public UInt16 Length;                       // 16 bits.

        public RTCPReportHeader(RTCPReportTypesEnum reportType, ushort payloadLength)
		{
            ReportType = reportType;
            Length = payloadLength;
        }

		/// <summary>
		/// Extract and load the RTCPReportHeader from packet.
		/// </summary>
		/// <param name="packet"></param>
		public RTCPReportHeader(byte[] packet)
		{
            if (packet.Length < HEADER_BYTES_LENGTH)
			{
				throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP Report Header packet.");
			}

            if (BitConverter.IsLittleEndian)
            {
                ReportType = RTCPReportTypes.GetRTCPReportTypeForId(NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 0)));
                Length = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
            }
            else
            {
                ReportType = RTCPReportTypes.GetRTCPReportTypeForId(BitConverter.ToUInt16(packet, 0));
                Length = BitConverter.ToUInt16(packet, 2);
            }
		}

		public byte[] GetBytes()
		{
            byte[] rtcpReportHeader = new byte[HEADER_BYTES_LENGTH];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian((ushort)ReportType)), 0, rtcpReportHeader, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(Length)), 0, rtcpReportHeader, 2, 2);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)ReportType), 0, rtcpReportHeader, 0, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(Length), 0, rtcpReportHeader, 2, 2);
            }

            return rtcpReportHeader;
		}

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class RTCPReportHeaderUnitTest
		{
			[TestFixtureSetUp]
			public void Init()
			{
				
			}

			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

			[Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");
			}
		}

		#endif

		#endregion
	}
}
