// ============================================================================
// FileName: NetServices.cs
//
// Description:
// Contains wrappers to access the functionality of the underlying operating
// system.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Dec 2005	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    /// <summary>
    /// Helper class to provide network services.
    /// </summary>
    public class NetServices
    {
        public const int UDP_PORT_START = 1025;
        public const int UDP_PORT_END = 65535;
        private const int RTP_RECEIVE_BUFFER_SIZE = 1000000; //100000000;
        private const int RTP_SEND_BUFFER_SIZE = 1000000; //100000000;
        private const int MAXIMUM_UDP_PORT_BIND_ATTEMPTS = 25;  // The maximum number of re-attempts that will be made when trying to bind a UDP socket.
        private const string INTERNET_IPADDRESS = "1.1.1.1";    // IP address to use when getting default IP address from OS. No connection is established.
        private const int NETWORK_TEST_PORT = 5060;                       // Port to use when doing a Udp.Connect to determine local IP address (port 0 does not work on MacOS).
        private const int LOCAL_ADDRESS_CACHE_LIFETIME_SECONDS = 300;   // The amount of time to leave the result of a local IP address determination in the cache.
        private static byte[] MAGIC_COOKIE = new byte[] { 0x41, 0x42, 0x41, 0x42, 0x41 };

        private static ILogger logger = Log.Logger;

        /// <summary>
        /// Doing the same check as here https://github.com/dotnet/corefx/blob/e99ec129cfd594d53f4390bf97d1d736cff6f860/src/System.Net.Sockets/src/System/Net/Sockets/SocketPal.Unix.cs#L19.
        /// Which is checking if a dual mode socket can use the *ReceiveFrom* methods in order to
        /// be able to get the remote destination end point.
        /// To date the only case this has cropped up for is Mac OS as per https://github.com/sipsorcery/sipsorcery/issues/207.
        /// </summary>
        private static bool? _supportsDualModeIPv4PacketInfo = null;
        public static bool SupportsDualModeIPv4PacketInfo
        {
            get
            {
                if (!_supportsDualModeIPv4PacketInfo.HasValue)
                {
                    try
                    {
                        _supportsDualModeIPv4PacketInfo = DoCheckSupportsDualModeIPv4PacketInfo();
                    }
                    catch
                    {
                        _supportsDualModeIPv4PacketInfo = false;
                    }
                }

                return _supportsDualModeIPv4PacketInfo.Value;
            }
        }

        /// <summary>
        /// A lookup collection to cache the local IP address for a destination address. The collection will cache results of
        /// asking the Operating System which local address to use for a destination address. The cache saves a relatively 
        /// expensive call to create a socket and ask the OS for a route lookup.
        /// 
        /// TODO:  Clear this cache if the state of the local network interfaces change.
        /// </summary>
        private static ConcurrentDictionary<IPAddress, Tuple<IPAddress, DateTime>> m_localAddressTable = new ConcurrentDictionary<IPAddress, Tuple<IPAddress, DateTime>>();

        /// <summary>
        /// The list of IP addresses that this machine can use.
        /// </summary>
        public static List<IPAddress> LocalIPAddresses
        {
            get
            {
                // TODO: Reset if the local network interfaces change.
                if (_localIPAddresses == null)
                {
                    _localIPAddresses = NetServices.GetAllLocalIPAddresses();
                }

                return _localIPAddresses;
            }
        }
        private static List<IPAddress> _localIPAddresses = null;

        /// <summary>
        /// The local IP address this machine uses to communicate with the Internet.
        /// </summary>
        public static IPAddress InternetDefaultAddress
        {
            get
            {
                // TODO: Reset if the local network interfaces change.
                if (_internetDefaultAddress == null)
                {
                    _internetDefaultAddress = GetLocalAddressForInternet();
                }

                return _internetDefaultAddress;
            }
        }
        private static IPAddress _internetDefaultAddress = null;

        /// <summary>
        /// Checks whether an IP address can be used on the underlying System.
        /// </summary>
        /// <param name="bindAddress">The bind address to use.</param>
        private static void CheckBindAddressAndThrow(IPAddress bindAddress)
        {
            if (bindAddress != null && bindAddress.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
            {
                throw new ApplicationException("An RTP socket cannot be created on an IPv6 address due to lack of OS support.");
            }
            else if (bindAddress != null && bindAddress.AddressFamily == AddressFamily.InterNetwork && !Socket.OSSupportsIPv4)
            {
                throw new ApplicationException("An RTP socket cannot be created on an IPv4 address due to lack of OS support.");
            }
        }

        /// <summary>
        /// Attempts to create and bind a UDP socket. This method will also verify that the socket can be used to do a single
        /// magic cookie receive to verify the underlying System did in fact provide a usable socket. The additional check
        /// is to accommodate a bug where Windows 10 was observed to allow the same port to be bound to two different
        /// sockets, see https://github.com/dotnet/runtime/issues/36618.
        /// </summary>
        /// <param name="port">The port to attempt to bind on. Set to 0 to request the underlying OS to select a port.</param>
        /// <param name="bindAddress">Optional. If specified the UDP socket will attempt to bind using this specific address.
        /// If not specified the broadest possible address will be chosen. Either IPAddress.Any or IPAddress.IPv6Any.</param>
        /// <param name="requireEvenPort">If true the method will only return successfully if it is able to bind on an
        /// even numbered port.</param>
        /// <returns>A bound socket if successful or throws an ApplicationException if unable to bind.</returns>
        public static Socket CreateBoundUdpSocket(int port, IPAddress bindAddress, bool requireEvenPort = false)
        {
            if (requireEvenPort && port != 0)
            {
                throw new ArgumentException("Cannot specify both require even port and a specific port to bind on. Set port to 0.");
            }

            if (bindAddress == null)
            {
                bindAddress = (Socket.OSSupportsIPv6 && SupportsDualModeIPv4PacketInfo) ? IPAddress.IPv6Any : IPAddress.Any;
            }

            IPEndPoint logEp = new IPEndPoint(bindAddress, port);
            logger.LogDebug($"CreateBoundUdpSocket attempting to create and bind UDP socket(s) on {logEp}.");

            CheckBindAddressAndThrow(bindAddress);

            int bindAttempts = 0;
            AddressFamily addressFamily = bindAddress.AddressFamily;
            bool success = false;
            Socket socket = null;

            while (bindAttempts < MAXIMUM_UDP_PORT_BIND_ATTEMPTS)
            {
                try
                {
                    socket = CreateUdpSocket(addressFamily);
                    socket.Bind(new IPEndPoint(bindAddress, port));

                    int boundPort = (socket.LocalEndPoint as IPEndPoint).Port;

                    if (requireEvenPort && boundPort % 2 != 0 && boundPort == IPEndPoint.MaxPort)
                    {
                        logger.LogDebug($"CreateBoundUdpSocket even port required, closing socket on {socket.LocalEndPoint}, max port reached request new bind.");
                        success = false;
                    }
                    else
                    {
                        if (requireEvenPort && boundPort % 2 != 0)
                        {
                            logger.LogDebug($"CreateBoundUdpSocket even port required, closing socket on {socket.LocalEndPoint} and retrying on {boundPort + 1}.");

                            // Close the socket, create a new one and try binding on the next consecutive port.
                            socket.Close();
                            socket = CreateUdpSocket(addressFamily);
                            socket.Bind(new IPEndPoint(bindAddress, boundPort + 1));
                        }
                        else
                        {
                            logger.LogDebug($"CreateBoundUdpSocket successfully bound on {socket.LocalEndPoint}.");
                        }

                        success = true;
                    }
                }
                catch (SocketException sockExcp)
                {
                    if (sockExcp.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        // Try again if the port is already in use.
                        logger.LogWarning($"Address already in use exception attempting to bind UDP socket, attempt {bindAttempts}.");
                        success = false;
                    }
                    else if (sockExcp.SocketErrorCode == SocketError.AccessDenied)
                    {
                        // This exception seems to be interchangeable with address already in use. Perhaps a race condition with another process
                        // attempting to bind at the same time.
                        logger.LogWarning($"Access denied exception attempting to bind UDP socket, attempt {bindAttempts}.");
                        success = false;
                    }
                    else
                    {
                        logger.LogError($"SocketException in NetServices.CreateCheckedUdpSocket. {sockExcp}");
                        throw;
                    }
                }
                catch (Exception excp)
                {
                    logger.LogError($"Exception in NetServices.CreateBoundUdpSocket attempting the initial socket bind on address {bindAddress}. {excp}");
                    throw;
                }
                finally
                {
                    if (!success)
                    {
                        socket?.Close();
                    }
                }

                if (success || port != 0)
                {
                    // If the bind was requested on a specific port there is no need to try again.
                    break;
                }
                else
                {
                    bindAttempts++;
                }
            }

            if (success)
            {
                return socket;
            }
            else
            {
                throw new ApplicationException($"Unable to bind UDP socket using end point {logEp}.");
            }
        }

        /// <summary>
        /// Common instantiation logic for creating a new UDP socket.
        /// </summary>
        /// <param name="addressFamily">The address family for the new socket, IPv4 or IPv6.</param>
        /// <returns>A new socket instance.</returns>
        private static Socket CreateUdpSocket(AddressFamily addressFamily)
        {
            var sock = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
            if (addressFamily == AddressFamily.InterNetworkV6)
            {
                sock.DualMode = SupportsDualModeIPv4PacketInfo;
            }
            return sock;
        }

        /// <summary>
        /// Attempts to create and bind a new RTP, and optionally an control (RTCP), socket(s).
        /// The RTP and control sockets created are IPv4 and IPv6 dual mode sockets which means they can send and receive
        /// either IPv4 or IPv6 packets.
        /// </summary>
        /// <param name="createControlSocket">True if a control (RTCP) socket should be created. Set to false if RTP
        /// and RTCP are being multiplexed on the same connection.</param>
        /// <param name="bindAddress">Optional. If null The RTP and control sockets will be created as IPv4 and IPv6 dual mode 
        /// sockets which means they can send and receive either IPv4 or IPv6 packets. If the bind address is specified an attempt
        /// will be made to bind the RTP and optionally control listeners on it.</param>
        /// <param name="rtpSocket">An output parameter that will contain the allocated RTP socket.</param>
        /// <param name="controlSocket">An output parameter that will contain the allocated control (RTCP) socket.</param>
        public static void CreateRtpSocket(bool createControlSocket, IPAddress bindAddress, out Socket rtpSocket, out Socket controlSocket)
        {
            if (bindAddress == null)
            {
                bindAddress = (Socket.OSSupportsIPv6 && SupportsDualModeIPv4PacketInfo) ? IPAddress.IPv6Any : IPAddress.Any;
            }

            CheckBindAddressAndThrow(bindAddress);

            IPEndPoint bindEP = new IPEndPoint(bindAddress, 0);
            logger.LogDebug($"CreateRtpSocket attempting to create and bind RTP socket(s) on {bindEP}.");

            rtpSocket = null;
            controlSocket = null;
            int bindAttempts = 0;

            while (bindAttempts < MAXIMUM_UDP_PORT_BIND_ATTEMPTS)
            {
                try
                {
                    rtpSocket = CreateBoundUdpSocket(0, bindAddress, true);
                    rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                    rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                    if (createControlSocket)
                    {
                        // For legacy VoIP the RTP and Control sockets need to be consecutive with the RTP port being
                        // an even number.
                        int rtpPort = (rtpSocket.LocalEndPoint as IPEndPoint).Port;
                        int controlPort = rtpPort + 1;

                        // Hopefully the next OS port allocation will be back in range.
                        if (controlPort <= IPEndPoint.MaxPort)
                        {
                            // This bind is being attempted on a specific port and can therefore legitimately fail if the port is already in use.
                            // Certain expected failure are caught and the attempt to bind two consecutive port will be re-attempted.
                            controlSocket = CreateBoundUdpSocket(controlPort, bindAddress);
                            controlSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                            controlSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;
                        }
                    }
                }
                catch (ApplicationException) { }

                if (rtpSocket != null && (!createControlSocket || controlSocket != null))
                {
                    break;
                }
                else
                {
                    rtpSocket?.Close();
                    controlSocket?.Close();
                    bindAttempts++;

                    rtpSocket = null;
                    controlSocket = null;

                    logger.LogWarning($"CreateRtpSocket failed to create and bind RTP socket(s) on {bindEP}, bind attempt {bindAttempts}.");
                }
            }

            if (createControlSocket && rtpSocket != null && controlSocket != null)
            {
                if (rtpSocket.LocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} (dual mode {rtpSocket.DualMode}) and control socket {controlSocket.LocalEndPoint} (dual mode {controlSocket.DualMode}).");
                }
                else
                {
                    logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} and control socket {controlSocket.LocalEndPoint}.");
                }
            }
            else if (!createControlSocket && rtpSocket != null)
            {
                if (rtpSocket.LocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} (dual mode {rtpSocket.DualMode}).");
                }
                else
                {
                    logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint}.");
                }
            }
            else
            {
                throw new ApplicationException($"Failed to create and bind RTP socket using bind address {bindAddress}.");
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
        public static bool DoTestReceive(Socket socket, IPAddress bindAddress)
        {
            try
            {
                logger.LogDebug($"DoTestReeceive for {socket.LocalEndPoint} and bind address {bindAddress}.");

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

                if (mre.WaitOne(TimeSpan.FromMilliseconds(500), false))
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

        /// <summary>
        /// Dual mode sockets are created by default if an IPv6 bind address was specified.
        /// Dual mode needs to be disabled for Mac OS sockets as they don't support the use
        /// of dual mode and the receive methods that return packet information. Packet info
        /// is needed to get the remote recipient.
        /// </summary>
        /// <returns>True if the underlying OS supports dual mode IPv6 sockets WITH the socket ReceiveFrom methods
        /// which are required to get the remote end point. False if not</returns>
        private static bool DoCheckSupportsDualModeIPv4PacketInfo()
        {
            bool hasDualModeReceiveSupport = true;

            if (!Socket.OSSupportsIPv6)
            {
                hasDualModeReceiveSupport = false;
            }
            else
            {
                var testSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                testSocket.DualMode = true;

                try
                {
                    testSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                    testSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 1);
                    byte[] buf = new byte[1];
                    EndPoint remoteEP = new IPEndPoint(IPAddress.IPv6Any, 0);

                    testSocket.BeginReceiveFrom(buf, 0, buf.Length, SocketFlags.None, ref remoteEP, null, null);
                    hasDualModeReceiveSupport = true;
                }
                catch (PlatformNotSupportedException platExcp)
                {
                    logger.LogWarning($"A socket 'receive from' attempt on a dual mode socket failed (dual mode RTP sockets will not be used) with a platform exception {platExcp.Message}");
                    hasDualModeReceiveSupport = false;
                }
                catch (Exception excp)
                {
                    logger.LogWarning($"A socket 'receive from' attempt on a dual mode socket failed (dual mode RTP sockets will not be used) with {excp.Message}");
                    hasDualModeReceiveSupport = false;
                }
                finally
                {
                    testSocket.Close();
                }
            }

            return hasDualModeReceiveSupport;
        }

        /// <summary>
        /// Attempts to create and bind a new RTP, and optionally an control (RTCP), socket(s) within a specified port range.
        /// The RTP and control sockets created are IPv4 and IPv6 dual mode sockets which means they can send and receive
        /// either IPv4 or IPv6 packets.
        /// </summary>
        /// <param name="rangeStartPort">The start of the port range that the sockets should be created within.</param>
        /// <param name="rangeEndPort">The end of the port range that the sockets should be created within.</param>
        /// <param name="startPort">A port within the range indicated by the start and end ports to attempt to
        /// bind the new socket(s) on. The main purpose of this parameter is to provide a pseudo-random way to allocate
        /// the port for a new RTP socket.</param>
        /// <param name="createControlSocket">True if a control (RTCP) socket should be created. Set to false if RTP
        /// and RTCP are being multiplexed on the same connection.</param>
        /// <param name="localAddress">Optional. If null The RTP and control sockets will be created as IPv4 and IPv6 dual mode 
        /// sockets which means they can send and receive either IPv4 or IPv6 packets. If the local address is specified an attempt
        /// will be made to bind the RTP and optionally control listeners on it.</param>
        /// <param name="rtpSocket">An output parameter that will contain the allocated RTP socket.</param>
        /// <param name="controlSocket">An output parameter that will contain the allocated control (RTCP) socket.</param>
        //public static void CreateRtpSocketInRange(int rangeStartPort, int rangeEndPort, int startPort, bool createControlSocket, IPAddress localAddress, out Socket rtpSocket, out Socket controlSocket)
        //{
        //    if (startPort == 0)
        //    {
        //        startPort = Crypto.GetRandomInt(rangeStartPort, rangeEndPort);
        //    }
        //    else if (startPort < rangeStartPort || startPort > rangeEndPort)
        //    {
        //        logger.LogWarning($"The start port of {startPort} supplied to CreateRtpSocket was outside the request range of {rangeStartPort}:{rangeEndPort}. A new valid start port will be pseudo-randomly chosen.");
        //        startPort = Crypto.GetRandomInt(rangeStartPort, rangeEndPort);
        //    }

        //    logger.LogDebug($"CreateRtpSocket start port {startPort}, range {rangeStartPort}:{rangeEndPort}.");

        //    rtpSocket = null;
        //    controlSocket = null;

        //    bool bindSuccess = false;
        //    int rtpPort = startPort;

        //    // Attempt to adjust the start port for:
        //    // - If in use ports can be checked find the first even unused port,
        //    // - Otherwise if not even then set to the nearest even port.
        //    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        //    {
        //        // On Windows we can get a list of in use UDP ports and avoid attempting to bind to them.
        //        IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        //        var udpListeners = ipGlobalProperties.GetActiveUdpListeners();

        //        var portRange = Enumerable.Range(rangeStartPort, rangeEndPort - rangeStartPort).OrderBy(x => (x > startPort) ? x : x + rangeEndPort);
        //        var inUsePorts = udpListeners.Where(x => x.Port >= rangeStartPort && x.Port <= rangeEndPort).Select(x => x.Port);

        //        logger.LogDebug($"In use UDP ports count {inUsePorts.Count()}.");

        //        rtpPort = portRange.Except(inUsePorts).Where(x => x % 2 == 0).FirstOrDefault();
        //    }
        //    else
        //    {
        //        // If the start port isn't even adjust it so it is. The original RTP specification required RTP ports to be even 
        //        // numbered and the control port to be the RTP port + 1.
        //        if (rtpPort % 2 != 0)
        //        {
        //            rtpPort = (rtpPort + 1) > rangeEndPort ? rtpPort - 1 : rtpPort + 1;
        //        }
        //    }

        //    for (int bindAttempts = 0; bindAttempts <= MAXIMUM_RTP_PORT_BIND_ATTEMPTS; bindAttempts++)
        //    {
        //        //lock (_allocatePortsMutex)
        //        //{
        //        int controlPort = (createControlSocket == true) ? rtpPort + 1 : 0;

        //        try
        //        {
        //            // The potential ports have been found now try and use them.
        //            if (localAddress != null)
        //            {
        //                rtpSocket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        //                rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
        //                rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;
        //                rtpSocket.Bind(new IPEndPoint(localAddress, rtpPort));
        //            }
        //            else
        //            {
        //                // Create a dual mode IPv4/IPv6 socket.
        //                rtpSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        //                rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
        //                rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;
        //                var bindAddress = (Socket.OSSupportsIPv6) ? IPAddress.IPv6Any : IPAddress.Any;
        //                rtpSocket.Bind(new IPEndPoint(bindAddress, rtpPort));
        //            }

        //            if (controlPort != 0)
        //            {
        //                if (localAddress != null)
        //                {
        //                    controlSocket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        //                    controlSocket.Bind(new IPEndPoint(localAddress, controlPort));
        //                }
        //                else
        //                {
        //                    // Create a dual mode IPv4/IPv6 socket.
        //                    controlSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        //                    var bindAddress = (Socket.OSSupportsIPv6) ? IPAddress.IPv6Any : IPAddress.Any;
        //                    controlSocket.Bind(new IPEndPoint(bindAddress, controlPort));
        //                }

        //                logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} and control socket {controlSocket.LocalEndPoint}.");
        //            }
        //            else
        //            {
        //                logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint}.");
        //            }

        //            bindSuccess = true;

        //            break;
        //        }
        //        catch (SocketException sockExcp)
        //        {
        //            if (sockExcp.SocketErrorCode != SocketError.AddressAlreadyInUse)
        //            {
        //                if (controlPort != 0)
        //                {
        //                    logger.LogWarning($"Socket error {sockExcp.ErrorCode} binding to address {localAddress} and RTP port {rtpPort} and/or control port of {controlPort}, attempt {bindAttempts}.");
        //                }
        //                else
        //                {
        //                    logger.LogWarning($"Socket error {sockExcp.ErrorCode} binding to address {localAddress} and RTP port {rtpPort}, attempt {bindAttempts}.");
        //                }
        //            }
        //            else
        //            {
        //                logger.LogWarning($"SocketException in NetServices.CreateRtpSocket. {sockExcp}");
        //            }
        //        }
        //        catch (Exception excp)
        //        {
        //            logger.LogWarning($"Exception in NetServices.CreateRtpSocket. {excp}");
        //        }

        //        // Adjust the start port for the next attempt.
        //        int step = Crypto.GetRandomInt(RTP_STEP_MIN, RTP_STEM_MAX);
        //        step = (step % 2 == 0) ? step : step + 1;
        //        rtpPort = (rtpPort + step + 1) > rangeEndPort ? rangeStartPort + step : rtpPort + step;
        //        //}
        //    }

        //    if (!bindSuccess)
        //    {
        //        throw new ApplicationException($"RTP socket allocation failure range {rangeStartPort}:{rangeEndPort}.");
        //    }
        //}

        //public static UdpClient CreateRandomUDPListener(IPAddress localAddress, int start, int end, ArrayList inUsePorts, out IPEndPoint localEndPoint)
        //{
        //    try
        //    {
        //        UdpClient randomClient = null;
        //        int attempts = 1;

        //        localEndPoint = null;

        //        while (attempts < 50)
        //        {
        //            int port = Crypto.GetRandomInt(start, end);
        //            if (inUsePorts == null || !inUsePorts.Contains(port))
        //            {
        //                try
        //                {
        //                    localEndPoint = new IPEndPoint(localAddress, port);
        //                    randomClient = new UdpClient(localEndPoint);
        //                    break;
        //                }
        //                catch
        //                {
        //                    //logger.LogWarning("Warning couldn't create UDP end point for " + localAddress + ":" + port + "." + excp.Message);
        //                }

        //                attempts++;
        //            }
        //        }

        //        //logger.LogDebug("Attempts to create UDP end point for " + localAddress + ":" + port + " was " + attempts);

        //        return randomClient;
        //    }
        //    catch
        //    {
        //        throw new ApplicationException("Unable to create a random UDP listener between " + start + " and " + end);
        //    }
        //}

        /// <summary>
        /// This method utilises the OS routing table to determine the local IP address to connect to a destination end point.
        /// It selects the correct local IP address, on a potentially multi-honed host, to communicate with a destination IP address.
        /// See https://github.com/sipsorcery/sipsorcery/issues/97 for elaboration.
        /// </summary>
        /// <param name="destination">The remote destination to find a local IP address for.</param>
        /// <returns>The local IP address to use to connect to the remote end point.</returns>
        public static IPAddress GetLocalAddressForRemote(IPAddress destination)
        {
            if (destination == null || IPAddress.Any.Equals(destination) || IPAddress.IPv6Any.Equals(destination))
            {
                return null;
            }

            if (m_localAddressTable.TryGetValue(destination, out var cachedAddress))
            {
                if (DateTime.Now.Subtract(cachedAddress.Item2).TotalSeconds >= LOCAL_ADDRESS_CACHE_LIFETIME_SECONDS)
                {
                    m_localAddressTable.TryRemove(destination, out _);
                }

                return cachedAddress.Item1;
            }
            else
            {
                IPAddress localAddress = null;

                if (destination.AddressFamily == AddressFamily.InterNetwork || destination.IsIPv4MappedToIPv6)
                {
                    UdpClient udpClient = new UdpClient();
                    udpClient.Connect(destination.MapToIPv4(), NETWORK_TEST_PORT);
                    localAddress = (udpClient.Client.LocalEndPoint as IPEndPoint).Address;
                }
                else
                {
                    UdpClient udpClient = new UdpClient(AddressFamily.InterNetworkV6);
                    udpClient.Connect(destination, NETWORK_TEST_PORT);
                    localAddress = (udpClient.Client.LocalEndPoint as IPEndPoint).Address;
                }

                m_localAddressTable.TryAdd(destination, new Tuple<IPAddress, DateTime>(localAddress, DateTime.Now));

                return localAddress;
            }
        }

        /// <summary>
        /// Gets the default local address for this machine for communicating with the Internet.
        /// </summary>
        /// <returns>The local address this machine should use for communicating with the Internet.</returns>
        private static IPAddress GetLocalAddressForInternet()
        {
            var internetAddress = IPAddress.Parse(INTERNET_IPADDRESS);
            return GetLocalAddressForRemote(internetAddress);
        }

        /// <summary>
        /// Gets all the IP addresses for all active interfaces on the machine.
        /// </summary>
        /// <returns>A list of all local IP addresses.</returns>
        private static List<IPAddress> GetAllLocalIPAddresses()
        {
            List<IPAddress> localAddresses = new List<IPAddress>();

            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface n in adapters)
            {
                if (n.OperationalStatus == OperationalStatus.Up)
                {
                    IPInterfaceProperties ipProps = n.GetIPProperties();
                    foreach (var unicastAddr in ipProps.UnicastAddresses)
                    {
                        localAddresses.Add(unicastAddr.Address);
                    }
                }
            }

            return localAddresses;
        }
    }
}
