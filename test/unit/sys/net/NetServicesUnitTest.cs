//-----------------------------------------------------------------------------
// Filename: NetServicesUnitTest.cs
//
// Description: Unit tests for the NetServices class.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Nov 2019  Aaron Clauson   Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Sys.UnitTests
{
    [Trait("Category", "unit")]
    public class NetServicesUnitTest
    {
        private static byte[] MAGIC_COOKIE = new byte[] { 0x41, 0x42, 0x41, 0x42, 0x41 };
        private static int TEST_RECEIVE_TIMEOUT_MILLISECONDS = 1000;

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public NetServicesUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Tests that a local IPv4 interface is matched against a destination address on the same network.
        /// </summary>
        [Fact]
        public void GetLocalIPAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("192.168.11.48"));
            Assert.NotNull(localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the loopback address is returned for a loopback destination.
        /// </summary>
        [Fact]
        public void GetLocalForLoopbackAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Loopback);
            Assert.Equal(IPAddress.Loopback, localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the a local address is returned for an Internet destination.
        /// </summary>
        [Fact]
        public void GetLocalForInternetAdressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.Parse("67.222.131.147"));
            Assert.NotNull(localAddress);

            logger.LogDebug($"Local address {localAddress}.");
        }

        /// <summary>
        /// Tests that the IPv6 loopback address is returned for an IPv6 loopback destination.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6")]
        public void GetLocalForIPv6LoopbackAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (Socket.OSSupportsIPv6)
            {
                var localAddress = NetServices.GetLocalAddressForRemote(IPAddress.IPv6Loopback);
                Assert.Equal(IPAddress.IPv6Loopback, localAddress);

                logger.LogDebug($"Local address {localAddress}.");
            }
            else
            {
                logger.LogDebug("Test skipped as OS does not support IPv6.");
            }
        }

        /// <summary>
        /// Tests that a local IPv6 interface is matched against a destination address on the same network.
        /// </summary>
        [Fact]
        [Trait("Category", "IPv6")]
        public void GetLocalIPv6AddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (Socket.OSSupportsIPv6)
            {
                var ipv6LinkLocal = NetServices.LocalIPAddresses.Where(x => x.IsIPv6LinkLocal).FirstOrDefault();

                if (ipv6LinkLocal == null)
                {
                    logger.LogDebug("No IPv6 link local address available.");
                }
                else
                {
                    logger.LogDebug($"IPv6 link local address for this host {ipv6LinkLocal}.");

                    var localAddress = NetServices.GetLocalAddressForRemote(ipv6LinkLocal);

                    Assert.NotNull(localAddress);
                    Assert.Equal(ipv6LinkLocal, localAddress);

                    logger.LogDebug($"Local address {localAddress}.");
                }
            }
            else
            {
                logger.LogDebug("Test skipped as OS does not support IPv6.");
            }
        }

        /// <summary>
        /// Tests that the list of local IP addresses is returned.
        /// </summary>
        [Fact]
        public void GetAllLocalIPAddressesUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddresses = NetServices.LocalIPAddresses;
            Assert.NotEmpty(localAddresses);

            foreach (var localAddress in localAddresses)
            {
                logger.LogDebug($"Local address {localAddress}.");
            }
        }

        /// <summary>
        /// Tests that the local address for accessing the Internet on this machine can be determined.
        /// </summary>
        [Fact]
        public void GetInternetAddressUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localInternetAddresses = NetServices.InternetDefaultAddress;
            Assert.NotNull(localInternetAddresses);

            logger.LogDebug($"Local Internet address {localInternetAddresses}.");
        }

        /// <summary>
        /// Tests that RTP and control listeners can be created with a pseudo-random port assignment.
        /// </summary>
        [Fact]
        public void CreateRtpAndControlSocketsUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            Socket rtpSocket = null;
            Socket controlSocket = null;

            NetServices.CreateRtpSocket(true, null, 0, out rtpSocket, out controlSocket);

            Assert.NotNull(rtpSocket);
            Assert.NotNull(controlSocket);

            rtpSocket.Close();
            controlSocket.Close();
        }

        /// <summary>
        /// Tests that RTP and control listeners can be created with a pseudo-random port assignment
        /// on the wildcard IPv4 address.
        /// </summary>
        [Fact]
        public void CreateRtpAndControlSocketsOnIP4AnyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            Socket rtpSocket = null;
            Socket controlSocket = null;

            NetServices.CreateRtpSocket(true, IPAddress.Any, 0, out rtpSocket, out controlSocket);

            Assert.NotNull(rtpSocket);
            Assert.NotNull(controlSocket);

            rtpSocket.Close();
            controlSocket.Close();
        }

        /// <summary>
        /// Tests that RTP and control listeners can be created with a pseudo-random port assignment
        /// on the wildcard IPv6 address.
        /// </summary>
        [Fact]
        public void CreateRtpAndControlSocketsOnIP6AnyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (Socket.OSSupportsIPv6)
            {
                Socket rtpSocket = null;
                Socket controlSocket = null;

                NetServices.CreateRtpSocket(true, IPAddress.IPv6Any, 0, out rtpSocket, out controlSocket);

                Assert.NotNull(rtpSocket);
                Assert.NotNull(controlSocket);

                rtpSocket.Close();
                controlSocket.Close();
            }
        }

        /// <summary>
        /// Tests that multiple RTP and control bound sockets can be created with correctly allocated
        /// port numbers.
        /// </summary>
        [Fact]
        public void CreateRtpAndControlMultipleSocketsUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            List<Socket> sockets = new List<Socket>();

            for (int i = 0; i < 20; i++)
            {
                Socket rtpSocket = null;
                Socket controlSocket = null;

                NetServices.CreateRtpSocket(true, null, 0, out rtpSocket, out controlSocket);

                Assert.NotNull(rtpSocket);
                Assert.NotNull(controlSocket);
                Assert.True((rtpSocket.LocalEndPoint as IPEndPoint).Port % 2 == 0);
                Assert.False((controlSocket.LocalEndPoint as IPEndPoint).Port % 2 == 0);

                sockets.Add(rtpSocket);
                sockets.Add(controlSocket);
            }

            foreach (var socket in sockets)
            {
                socket.Close();
            }
        }

        /// <summary>
        /// Runs the check to determine whether the underlying OS supports dual mode 
        /// sockets WITH packet info (needed to get remote end point). The only OS currently
        /// known not to is Mac OSX.
        /// </summary>
        [Fact]
        public void CheckSupportsDualModeIPv4PacketInfoUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            bool supports = NetServices.SupportsDualModeIPv4PacketInfo;

            logger.LogDebug($"SupportsDualModeIPv4PacketInfo result for OS {RuntimeInformation.OSDescription} is {supports}.");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.False(supports);
            }
            else
            {
                Assert.True(supports);
            }
        }

        /// <summary>
        /// Checks that when two sockets are bound on the same port but with different address specifiers.
        /// Workaround for bug: https://github.com/dotnet/runtime/issues/36618
        /// The behaviour for this test is different on Windows and Ubuntu:
        ///  - On Windows the IPv4 bound socket can receive the IPv6 one can't,
        ///  - On Ubuntu the duplicate bind causes an exception, which is the correct behaviour,
        ///  - On Mac this library does not create dual mode IPv6 sockets as they can be used
        ///    with the required "receive from" methods. Which means the attempt to receive
        ///    on a socket bound to [::] by sending to 127.0.0.1 fails but in this case because
        ///    it's not created as dual mode rather than because of the bug on Windows.
        /// </summary>
        [Fact]
        public void CheckCreateSocketFailsForInUseSocketUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (!Socket.OSSupportsIPv6)
            {
                logger.LogDebug("Test not executed as no IPv6 support.");
            }
            else
            {
                var socket4Any = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket4Any.Bind(new IPEndPoint(IPAddress.Any, 0));
                IPEndPoint anyEP = socket4Any.LocalEndPoint as IPEndPoint;

                var socketIP6Any = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                socketIP6Any.DualMode = true;

                // It will be useful to detect if this behaviour ever changes hence the different asserts for
                // the different Operating Systems.

                logger.LogDebug($"Environment.OSVersion: {Environment.OSVersion}.");
                logger.LogDebug($"Environment.OSVersion.Platform: {Environment.OSVersion.Platform}.");
                logger.LogDebug($"RuntimeInformation.OSDescription: {RuntimeInformation.OSDescription}.");

                // Note Ubuntu on Windows Subsystem for Linux (WSL2) does NOT throw a binding exception and allows
                // the duplicate binding the same as Windows.
                if (Environment.OSVersion.Platform == PlatformID.Unix &&
                    !RuntimeInformation.OSDescription.Contains("Microsoft") &&  // WSL does not throw on duplicate bind attempt.
                    !RuntimeInformation.OSDescription.Contains("Darwin"))       // MacOS does not throw. 
                {
                    Assert.Throws<SocketException>(() => socketIP6Any.Bind(new IPEndPoint(IPAddress.IPv6Any, anyEP.Port)));
                }
                else
                {
                    // No exception on Windows or Ubuntu.
                    socketIP6Any.Bind(new IPEndPoint(IPAddress.IPv6Any, anyEP.Port));

                    Assert.True(DoTestReceive(socket4Any, null));
                    Assert.False(DoTestReceive(socketIP6Any, null));
                }
            }
        }

        /// <summary>
        /// Checks that a bind attempt fails if the socket is already bound on IPv4 0.0.0.0 and an
        /// attempt is made to use the same port on IPv6 [::].
        ///
        /// This test should be excluded on MacOS (or any other OS that can't be used with dual mode IPv6 sockets 
        /// coupled with the "receivefrom" methods need to get packet information i.e get the sending socket).
        /// AC 10 Jun 2020.
        /// </summary>
        [Fact]
        public void CheckFailsOnDuplicateForIP4AnyThenIPv6AnyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (Socket.OSSupportsIPv6 && NetServices.SupportsDualModeIPv4PacketInfo)
            {
                Socket rtpSocket = null;
                Socket controlSocket = null;

                NetServices.CreateRtpSocket(true, IPAddress.Any, 0, out rtpSocket, out controlSocket);

                Assert.NotNull(rtpSocket);
                Assert.NotNull(controlSocket);

                Assert.Throws<ApplicationException>(() => NetServices.CreateBoundUdpSocket((rtpSocket.LocalEndPoint as IPEndPoint).Port, IPAddress.IPv6Any, false, true));

                rtpSocket.Close();
                controlSocket.Close();
            }
            else
            {
                logger.LogDebug("Test skipped as IPv6 dual mode sockets are not in use on this OS.");
            }
        }

        /// <summary>
        /// Checks that a bind attempt fails if the socket is already bound on IPv6 [::] and an
        /// attempt is made to use the same port on IPv4 0.0.0.0.
        /// 
        /// This test should be excluded on MacOS (or any other OS that can't be used with dual mode IPv6 sockets 
        /// coupled with the "receivefrom" methods need to get packet information i.e get the sending socket).
        /// AC 10 Jun 2020.
        /// </summary>
        [Fact]
        public void CheckFailsOnDuplicateForIP6AnyThenIPv4AnyUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            if (Socket.OSSupportsIPv6 && NetServices.SupportsDualModeIPv4PacketInfo)
            {
                Socket rtpSocket = null;
                Socket controlSocket = null;

                NetServices.CreateRtpSocket(true, IPAddress.IPv6Any, 0, out rtpSocket, out controlSocket);

                Assert.NotNull(rtpSocket);
                Assert.NotNull(controlSocket);

                Assert.Throws<ApplicationException>(() => NetServices.CreateBoundUdpSocket((rtpSocket.LocalEndPoint as IPEndPoint).Port, IPAddress.Any));

                rtpSocket.Close();
                controlSocket.Close();
            }
            else
            {
                logger.LogDebug("Test skipped as IPv6 dual mode sockets are not in use on this OS.");
            }
        }

        /// <summary>
        /// Tests that the local IP addresses for a single interface can be obtained.
        /// </summary>
        [Fact]
        public void GetIPAddressesForInterfaceUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            var localAddresses = NetServices.GetLocalAddressesOnInterface(null);
            Assert.NotEmpty(localAddresses);

            foreach (var localAddress in localAddresses)
            {
                logger.LogDebug($"Local address {localAddress}.");
            }
        }

        /// <summary>
        /// Checks that a bound socket is able to receive. The need for this test arose when it was found
        /// that Windows was allocating the same port if a bind was attempted on 0.0.0.0:0 and then [::]:0.
        /// Only one of the two sockets could then receive packets to the OS allocated port.
        /// This check is an attempt to work around the behaviour, see
        /// https://github.com/dotnet/runtime/issues/36618
        /// </summary>
        /// <param name="socket">The bound socket to check for a receive.</param>
        /// <param name="bindAddress">Optional. If the socket was bound to a single specific address
        /// this parameter needs to be set so the test can send to it. If not set the test will send to 
        /// the IPv4 loopback addresses.</param>
        /// <returns>True is the receive was successful and the socket is usable. False if not.</returns>
        private bool DoTestReceive(Socket socket, IPAddress bindAddress)
        {
            try
            {
                if (bindAddress != null)
                {
                    logger.LogDebug($"DoTestReceive for {socket.LocalEndPoint} and bind address {bindAddress}.");
                }
                else
                {
                    logger.LogDebug($"DoTestReceive for {socket.LocalEndPoint}.");
                }

                byte[] buffer = new byte[MAGIC_COOKIE.Length];
                ManualResetEvent mre = new ManualResetEvent(false);
                int bytesRead = 0;

                void endReceive(IAsyncResult ar)
                {
                    try
                    {
                        if (socket != null)
                        {
                            bytesRead = socket.EndReceive(ar);
                        }
                    }
                    catch (Exception) { }
                    mre.Set();
                };

                socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, endReceive, null);

                int sendToPort = (socket.LocalEndPoint as IPEndPoint).Port;

                IPAddress sendToAddress = bindAddress ?? IPAddress.Loopback;
                if (IPAddress.IPv6Any.Equals(sendToAddress))
                {
                    sendToAddress = IPAddress.IPv6Loopback;
                }
                else if (IPAddress.Any.Equals(sendToAddress))
                {
                    sendToAddress = IPAddress.Loopback;
                }

                IPEndPoint sendTo = new IPEndPoint(sendToAddress, sendToPort);
                socket.SendTo(MAGIC_COOKIE, sendTo);

                if (mre.WaitOne(TimeSpan.FromMilliseconds(TEST_RECEIVE_TIMEOUT_MILLISECONDS), false))
                {
                    // The receive worked. Check that the magic cookie was received.
                    if (bytesRead != MAGIC_COOKIE.Length)
                    {
                        logger.LogDebug("Bytes read was wrong length for magic cookie in DoTestReceive.");
                        return false;
                    }
                    else if (Encoding.ASCII.GetString(buffer) != Encoding.ASCII.GetString(MAGIC_COOKIE))
                    {
                        logger.LogDebug("The bytes read did not match the magic cookie in DoTestReceive.");
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
                else
                {
                    logger.LogDebug("Timed out waiting for magic cookie in DoTestReceive.");
                    return false;
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning($"DoTestReceive received failed with exception {excp.Message}");
                return false;
            }
        }
    }
}
