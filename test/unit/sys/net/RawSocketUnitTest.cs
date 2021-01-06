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

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class RawSocketUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RawSocketUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Fact]
        public void IPHeaderConstructionUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            IPv4Header header = new IPv4Header(ProtocolType.Udp, 4567, IPAddress.Parse("127.0.0.1"), IPAddress.Parse("127.0.0.1"));
            byte[] headerData = header.GetBytes();

            int count = 0;
            foreach (byte headerByte in headerData)
            {
                logger.LogDebug("0x{0,-2:x} ", headerByte);
                count++;
                if (count % 4 == 0)
                {
                    logger.LogDebug("\n");
                }
            }

            logger.LogDebug("\n");

            Assert.True(true, "True was false.");
        }

        [Fact(Skip = "Will only work on Win10 or where raw socket privileges have been explicitly granted.")]
        public void PreconstructedIPPacketSendUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

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
                rawSocket.SendTo(ipPacket, new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5060));
            }
            catch (SocketException sockExcp)
            {
                logger.LogDebug("Socket exception error code= " + sockExcp.ErrorCode + ". " + sockExcp.Message);
                throw;
            }

            rawSocket.Shutdown(SocketShutdown.Both);
            rawSocket.Close();

            Assert.True(true, "True was false.");
        }

        [Fact(Skip = "Will only work on Win10 or where raw socket privileges have been explicitly granted.")]
        public void IPEmptyPacketSendUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            UDPPacket udpPacket = new UDPPacket(4001, 4001, new byte[] { 0x1, 0x2, 0x3, 0x4 });
            IPv4Header header = new IPv4Header(ProtocolType.Udp, 7890, IPAddress.Parse("127.0.0.1"), IPAddress.Parse("127.0.0.1"));
            byte[] headerData = header.GetBytes();

            foreach (byte headerByte in headerData)
            {
                logger.LogDebug("0x{0:x} ", headerByte);
            }

            logger.LogDebug("\n");

            Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
            //rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);
            rawSocket.SendTo(headerData, new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5060));

            rawSocket.Shutdown(SocketShutdown.Both);
            rawSocket.Close();

            Assert.True(true, "True was false.");
        }
    }
}
