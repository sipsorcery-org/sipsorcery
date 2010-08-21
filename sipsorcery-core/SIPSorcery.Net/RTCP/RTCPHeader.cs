//-----------------------------------------------------------------------------
// Filename: RTCPHeader.cs
//
// Description: RTCP Header as defined in RFC3550.
// 
//
//      RTCP Header
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |V=2|P|    RC   |   PT=SR=200   |             Length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         Payload                               |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// (V)ersion (2 bits) = 2
// (P)adding (1 bit) = Inidcates whether the packet contains additional padding octets.
// Reception Report Count (RC) (5 bits) = The number of reception report blocks contained in this packet. A
//      value of zero is valid.
// Packet Type (PT) (8 bits) = Contains the constant 200 to identify this as an RTCP SR packet.
// Length (16 bits) = The length of this RTCP packet in 32-bit words minus one, including the header and any padding.
//
// History:
// 22 Feb 2007	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
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
	public class RTCPHeader
	{
		public const int HEADER_BYTES_LENGTH = 4;
        public const int MAX_RECEPTIONREPORT_COUNT = 32;

		public const int RTCP_VERSION = 2;
        public const UInt16 RTCP_PACKET_TYPE = 200;

		public int Version = RTCP_VERSION;						// 2 bits.
		public int PaddingFlag = 0;								// 1 bit.
        public int ReceptionReportCount = 0;                    // 5 bits.
        public UInt16 PacketType = RTCP_PACKET_TYPE;            // 8 bits.
        public UInt16 Length;                                   // 16 bits.

		public RTCPHeader()
		{}

		/// <summary>
		/// Extract and load the RTP header from an RTP packet.
		/// </summary>
		/// <param name="packet"></param>
		public RTCPHeader(byte[] packet)
		{
            if (packet.Length < HEADER_BYTES_LENGTH)
			{
				throw new ApplicationException("The packet did not contain the minimum number of bytes for an RTCP header packet.");
			}

            UInt16 firstWord = BitConverter.ToUInt16(packet, 0);

            if (BitConverter.IsLittleEndian)
            {
                firstWord = NetConvert.DoReverseEndian(firstWord);
                Length = NetConvert.DoReverseEndian(BitConverter.ToUInt16(packet, 2));
            }
            else
            {
                Length = BitConverter.ToUInt16(packet, 2);
            }

			Version = Convert.ToInt32(firstWord >> 14);
			PaddingFlag = Convert.ToInt32((firstWord >> 13) & 0x1);
			ReceptionReportCount = Convert.ToInt32((firstWord >> 8) & 0x1f);
            PacketType = Convert.ToUInt16(firstWord & 0x00ff);
		}

		public byte[] GetHeader(int receptionReportCount, UInt16 length)
		{
            if (receptionReportCount > MAX_RECEPTIONREPORT_COUNT)
            {
                throw new ApplicationException("The Reception Report Count value cannot be larger than " + MAX_RECEPTIONREPORT_COUNT + ".");
            }

            ReceptionReportCount = receptionReportCount;
            Length = length;

			return GetBytes();
		}

		public byte[] GetBytes()
		{
			byte[] header = new byte[4];

            UInt32 firstWord = Convert.ToUInt32(Version * Math.Pow(2, 30) + PaddingFlag * Math.Pow(2, 29) + ReceptionReportCount * Math.Pow(2, 24) + PacketType * Math.Pow(2, 16) + Length);

			if(BitConverter.IsLittleEndian)
			{
				Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(firstWord)), 0, header, 0, 4);
			}
			else
			{
				Buffer.BlockCopy(BitConverter.GetBytes(firstWord), 0, header, 0, 4);
			}

			return header;
		}

		#region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class RTCPHeaderUnitTest
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

            [Test]
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

            [Test]
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

		#endif

		#endregion
	}
}
