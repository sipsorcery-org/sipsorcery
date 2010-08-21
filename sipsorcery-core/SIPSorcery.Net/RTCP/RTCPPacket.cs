//-----------------------------------------------------------------------------
// Filename: RTCPPacket.cs
//
// Description: Encapsulation of an RTCP (Real Time Control Protocol) packet.
//
//      RTCP Packet
//        0                   1                   2                   3
//        0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//header |V=2|P|    RC   |   PT=SR=200   |             length            |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         SSRC of sender                        |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//sender |              NTP timestamp, most significant word             |
//info   +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |             NTP timestamp, least significant word             |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         RTP timestamp                         |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                     sender's packet count                     |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                      sender's octet count                     |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//report |                 SSRC_1 (SSRC of first source)                 |
//block  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  1    | fraction lost |       cumulative number of packets lost       |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |           extended highest sequence number received           |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                      interarrival jitter                      |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                         last SR (LSR)                         |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//       |                   delay since last SR (DLSR)                  |
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//report |                 SSRC_2 (SSRC of second source)                |
//block  +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
//  2    :                               ...                             :
//       +=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//       |                  profile-specific extensions                  |
//       +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
// 
// History:
// 22 Feb 2007	Aaron Clauson	Created.
//
// License: 
// Aaron Clauson
//-----------------------------------------------------------------------------

using System;
using SIPSorcery.Sys;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Net
{
	public class RTCPPacket
	{
        public const int SENDERINFO_BYTES_LENGTH = 24;
        
        public RTCPHeader Header;                               // 32 bits.
        public uint SenderSyncSource;							// 32 bits.
        public UInt64 NTPTimestamp;                              // 64 bits.
        public uint RTPTimestamp;                                // 32 bits.
        public uint SenderPacketCount;                           // 32 bits.
        public uint SenderOctetCount;                            // 32 bits.
        public byte[] Reports;

		public RTCPPacket(uint senderSyncSource, ulong ntpTimestamp, uint rtpTimestamp, uint senderPacketCount, uint senderOctetCount)
		{
			Header = new RTCPHeader();
            SenderSyncSource = senderSyncSource;
            NTPTimestamp = ntpTimestamp;
            RTPTimestamp = rtpTimestamp;
            SenderPacketCount = senderPacketCount;
            SenderOctetCount = senderOctetCount;
		}
		
		public RTCPPacket(byte[] packet)
		{
			Header = new RTCPHeader(packet);

            if (BitConverter.IsLittleEndian)
            {
                SenderSyncSource = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 4));
                NTPTimestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt64(packet, 8));
                RTPTimestamp = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 16));
                SenderPacketCount = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 20));
                SenderOctetCount = NetConvert.DoReverseEndian(BitConverter.ToUInt32(packet, 24));
            }
            else
            {
                SenderSyncSource = BitConverter.ToUInt32(packet, 4);
                NTPTimestamp = BitConverter.ToUInt64(packet, 8);
                RTPTimestamp = BitConverter.ToUInt32(packet, 16);
                SenderPacketCount = BitConverter.ToUInt32(packet, 20);
                SenderOctetCount = BitConverter.ToUInt32(packet, 24);
            }

            Reports = new byte[packet.Length - RTCPHeader.HEADER_BYTES_LENGTH - SENDERINFO_BYTES_LENGTH];
            Buffer.BlockCopy(packet, RTCPHeader.HEADER_BYTES_LENGTH + SENDERINFO_BYTES_LENGTH, Reports, 0, Reports.Length);
		}

		public byte[] GetBytes(byte[] reports)
		{
            Reports = reports;
            byte[] payload = new byte[SENDERINFO_BYTES_LENGTH + reports.Length];

            if (BitConverter.IsLittleEndian)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderSyncSource)), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(NTPTimestamp)), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(RTPTimestamp)), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderPacketCount)), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NetConvert.DoReverseEndian(SenderOctetCount)), 0, payload, 20, 4);
            }
            else
            {
                Buffer.BlockCopy(BitConverter.GetBytes(SenderSyncSource), 0, payload, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(NTPTimestamp), 0, payload, 4, 8);
                Buffer.BlockCopy(BitConverter.GetBytes(RTPTimestamp), 0, payload, 12, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(SenderPacketCount), 0, payload, 16, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(SenderOctetCount), 0, payload, 20, 4);
            }

            Buffer.BlockCopy(reports, 0, payload, 24, reports.Length);

            Header.Length = Convert.ToUInt16(payload.Length);
            byte[] header = Header.GetBytes();
            byte[] packet = new byte[header.Length + payload.Length];
            Array.Copy(header, packet, header.Length);
            Array.Copy(payload, 0, packet, header.Length, payload.Length);

			return packet;
		}

        #region Unit testing.

		#if UNITTEST
	
		[TestFixture]
		public class RTPCPacketUnitTest
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
            public void GetRTCPPacketTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                RTCPPacket rtcpPacket = new RTCPPacket(1, 1, 1, 1, 1);
                byte[] reports = new byte[84];
                byte[] packetBuffer = rtcpPacket.GetBytes(reports);

                int byteNum = 1;
                foreach (byte packetByte in packetBuffer)
                {
                    Console.WriteLine(byteNum + ": " + packetByte.ToString("x"));
                    byteNum++;
                }
            }

            [Test]
            public void RTCPHeaderRoundTripTest()
            {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                RTCPPacket src = new RTCPPacket(12, 122, 561, 6756, 56434);
                byte[] reports = new byte[84];
                byte[] packetBuffer = src.GetBytes(reports);
                RTCPPacket dst = new RTCPPacket(packetBuffer);

                Console.WriteLine("SenderSyncSource: " + src.SenderSyncSource + ", " + dst.SenderSyncSource);
                Console.WriteLine("NTPTimestamp: " + src.NTPTimestamp + ", " + dst.NTPTimestamp);
                Console.WriteLine("RTPTimestamp: " + src.RTPTimestamp + ", " + dst.RTPTimestamp);
                Console.WriteLine("SenderPacketCount: " + src.SenderPacketCount + ", " + dst.SenderPacketCount);
                Console.WriteLine("SenderOctetCount: " + src.SenderOctetCount + ", " + dst.SenderOctetCount);
                Console.WriteLine("Reports Length: " + src.Reports.Length + ", " + dst.Reports.Length);

                //Console.WriteLine("Raw Header: " + System.Text.Encoding.ASCII.GetString(headerBuffer, 0, headerBuffer.Length));

                Assert.IsTrue(src.SenderSyncSource == dst.SenderSyncSource, "SenderSyncSource was mismatched.");
                Assert.IsTrue(src.NTPTimestamp == dst.NTPTimestamp, "NTPTimestamp was mismatched.");
                Assert.IsTrue(src.RTPTimestamp == dst.RTPTimestamp, "RTPTimestamp was mismatched.");
                Assert.IsTrue(src.SenderPacketCount == dst.SenderPacketCount, "SenderPacketCount was mismatched.");
                Assert.IsTrue(src.SenderOctetCount == dst.SenderOctetCount, "SenderOctetCount was mismatched.");
                Assert.IsTrue(src.Reports.Length == dst.Reports.Length, "Reports length was mismatched.");
            }
		}

		#endif

		#endregion
	}
}
