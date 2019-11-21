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
    public class RTCPPacketUnitTest
    {
        [Fact]
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

        [Fact]
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

            Assert.True(src.SenderSyncSource == dst.SenderSyncSource, "SenderSyncSource was mismatched.");
            Assert.True(src.NTPTimestamp == dst.NTPTimestamp, "NTPTimestamp was mismatched.");
            Assert.True(src.RTPTimestamp == dst.RTPTimestamp, "RTPTimestamp was mismatched.");
            Assert.True(src.SenderPacketCount == dst.SenderPacketCount, "SenderPacketCount was mismatched.");
            Assert.True(src.SenderOctetCount == dst.SenderOctetCount, "SenderOctetCount was mismatched.");
            Assert.True(src.Reports.Length == dst.Reports.Length, "Reports length was mismatched.");
        }
    }
}
