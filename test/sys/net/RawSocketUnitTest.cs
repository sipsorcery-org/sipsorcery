using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SIPSorcery.Sys.UnitTests
{
    [TestClass]
    public class RawSocketUnitTest
    {
        [TestMethod]
        public void IPHeaderConstructionUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            IPv4Header header = new IPv4Header(ProtocolType.Udp, 4567, IPAddress.Parse("194.213.29.54"), IPAddress.Parse("194.213.29.54"));
            byte[] headerData = header.GetBytes();

            int count = 0;
            foreach (byte headerByte in headerData)
            {
                Console.Write("0x{0,-2:x} ", headerByte);
                count++;
                if (count % 4 == 0)
                {
                    Console.Write("\n");
                }
            }

            Console.WriteLine();

            Assert.IsTrue(true, "True was false.");
        }

        [TestMethod]
        [Ignore("Will only work on WinXP or where raw socket privileges have been explicitly granted.")]
        public void PreconstructedIPPacketSendUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            byte[] ipPacket = new byte[] {
                     // IP Header.
                    0x45, 0x30, 0x0, 0x4,
                    0xe4, 0x29, 0x0, 0x0,
                    0x80, 0x0, 0x62, 0x3d,
                    0x0a, 0x0, 0x0, 0x64,
                    0xc2, 0xd5, 0x1d, 0x36,
                    // Data.
                    0x0, 0x0, 0x0, 0x0
                };

            Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);

            try
            {
                rawSocket.SendTo(ipPacket, new IPEndPoint(IPAddress.Parse("194.213.29.54"), 5060));
            }
            catch (SocketException sockExcp)
            {
                Console.WriteLine("Socket exception error code= " + sockExcp.ErrorCode + ". " + sockExcp.Message);
                throw sockExcp;
            }

            rawSocket.Shutdown(SocketShutdown.Both);
            rawSocket.Close();

            Assert.IsTrue(true, "True was false.");
        }

        [TestMethod]
        [Ignore("Will only work on WinXP or where raw socket privileges have been explicitly granted.")]
        public void IPEmptyPacketSendUnitTest()
        {
            Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

            UDPPacket udpPacket = new UDPPacket(4001, 4001, new byte[] { 0x1, 0x2, 0x3, 0x4 });
            IPv4Header header = new IPv4Header(ProtocolType.Udp, 7890, IPAddress.Parse("194.213.29.54"), IPAddress.Parse("194.213.29.54"));
            byte[] headerData = header.GetBytes();

            foreach (byte headerByte in headerData)
            {
                Console.Write("0x{0:x} ", headerByte);
            }

            Console.WriteLine();

            Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            //rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);
            rawSocket.SendTo(headerData, new IPEndPoint(IPAddress.Parse("194.213.29.54"), 5060));

            rawSocket.Shutdown(SocketShutdown.Both);
            rawSocket.Close();

            Assert.IsTrue(true, "True was false.");
        }
    }
}
