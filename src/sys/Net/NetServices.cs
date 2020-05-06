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
using System.Runtime.CompilerServices;
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
        private const int MAXIMUM_RTP_PORT_BIND_ATTEMPTS = 25;  // The maximum number of re-attempts that will be made when trying to bind the RTP port.
        private const string INTERNET_IPADDRESS = "1.1.1.1";    // IP address to use when getting default IP address from OS. No connection is established.
        private const int NETWORK_TEST_PORT = 5060;                       // Port to use when doing a Udp.Connect to determine local IP address (port 0 does not work on MacOS).
        private const int LOCAL_ADDRESS_CACHE_LIFETIME_SECONDS = 300;   // The amount of time to leave the result of a local IP address determination in the cache.
        private const int RTP_STEP_MIN = 2;
        private const int RTP_STEM_MAX = 20;

        private static ILogger logger = Log.Logger;

        private static Mutex _allocatePortsMutex = new Mutex();

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

        public static UdpClient CreateRandomUDPListener(IPAddress localAddress, out IPEndPoint localEndPoint)
        {
            return CreateRandomUDPListener(localAddress, UDP_PORT_START, UDP_PORT_END, null, out localEndPoint);
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

            if (bindAddress != null && bindAddress.AddressFamily == AddressFamily.InterNetworkV6 && !Socket.OSSupportsIPv6)
            {
                throw new ApplicationException("An RTP socket cannot be created on an IPv6 address due to lack of OS support.");
            }
            if (bindAddress != null && bindAddress.AddressFamily == AddressFamily.InterNetwork && !Socket.OSSupportsIPv4)
            {
                throw new ApplicationException("An RTP socket cannot be created on an IPv4 address due to lack of OS support.");
            }

            logger.LogDebug($"CreateRtpSocket start port using OS default ephemeral port range on {bindAddress}.");

            rtpSocket = null;
            controlSocket = null;
            int bindAttempts = 0;
            AddressFamily addressFamily = bindAddress.AddressFamily;
            bool success = false;

            do
            {
                Socket firstSocket = null;
                Socket secondSocket = null;

                // If we're unable to even bind a socket on the address we want using a port chosen by the OS 
                // then there's no point trying repeatedly. Most likely there's something wrong with the networking
                // set up. Except... if an address already in use error occurs it could be because another process/thread
                // asked the OS for a bind at the same time. In that case it is worth re-trying.
                try
                {
                    // Create a dual mode IPv4/IPv6 socket.
                    firstSocket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
                    firstSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                    firstSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                    if (addressFamily == AddressFamily.InterNetworkV6)
                    {
                        //logger.LogDebug($"CreateRtpSocket setting first socket dual mode to {SupportsDualModeIPv4PacketInfo}.");
                        firstSocket.DualMode = SupportsDualModeIPv4PacketInfo;
                    }

                    firstSocket.Bind(new IPEndPoint(bindAddress, 0));
                }
                catch (SocketException sockExcp)
                {
                    if (sockExcp.SocketErrorCode == SocketError.AddressAlreadyInUse)
                    {
                        // Try again if the port is already in use.
                        logger.LogWarning($"Address already in use exception attempting to create RTP socket, attempt {bindAttempts}.");
                    }
                    else
                    {
                        logger.LogError($"SocketException in NetServices.CreateRtpSocket. {sockExcp}");
                        break;
                    }
                }
                catch (Exception excp)
                {
                    logger.LogError($"Exception in NetServices.CreateRtpSocket attempting the initial socket bind on address {bindAddress}. {excp}");
                    break;
                }

                if (createControlSocket)
                {
                    try
                    {
                        // For legacy VoIP the RTP and Control sockets need to be consecutive with the RTP port being
                        // an even number.

                        int firstSocketPort = (firstSocket.LocalEndPoint as IPEndPoint).Port;
                        int secondSocketPort = (firstSocketPort % 2 == 0) ? firstSocketPort + 1 : firstSocketPort - 1;

                        logger.LogDebug($"CreateRtpSocket successfully bound on {firstSocket.LocalEndPoint}, attempting second socket bind on port {secondSocketPort}.");

                        secondSocket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);
                        secondSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                        secondSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                        if (addressFamily == AddressFamily.InterNetworkV6)
                        {
                            //logger.LogDebug($"CreateRtpSocket setting second socket dual mode to {SupportsDualModeIPv4PacketInfo}.");
                            secondSocket.DualMode = SupportsDualModeIPv4PacketInfo;
                        }

                        secondSocket.Bind(new IPEndPoint(bindAddress, secondSocketPort));

                        rtpSocket = (firstSocketPort % 2 == 0) ? firstSocket : secondSocket;
                        controlSocket = (firstSocketPort % 2 == 0) ? secondSocket : firstSocket;

                        if (addressFamily == AddressFamily.InterNetworkV6)
                        {
                            logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} (dual mode {rtpSocket.DualMode}) and control socket {controlSocket.LocalEndPoint} (dual mode {controlSocket.DualMode}).");
                        }
                        else
                        {
                            logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} and control socket {controlSocket.LocalEndPoint}.");
                        }

                        success = true;
                        break;
                    }
                    catch (SocketException sockExcp)
                    {
                        if (sockExcp.SocketErrorCode == SocketError.AddressAlreadyInUse)
                        {
                            // Try again if the port is already in use.
                            logger.LogWarning($"Address already in use exception attempting to create control socket, attempt {bindAttempts}.");
                        }
                        else
                        {
                            logger.LogError($"SocketException in NetServices.CreateRtpSocket. {sockExcp}");
                            throw;
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.LogError($"Exception in NetServices.CreateRtpSocket. {excp}");
                        throw;
                    }
                    finally
                    {
                        if (!success)
                        {
                            firstSocket?.Close();
                            secondSocket?.Close();
                        }
                    }
                }
                else
                {
                    rtpSocket = firstSocket;

                    if (addressFamily == AddressFamily.InterNetworkV6)
                    {
                        logger.LogDebug($"Successfully bound RTP socket on {rtpSocket.LocalEndPoint} (dual mode {rtpSocket.DualMode}).");
                    }
                    else
                    {
                        logger.LogDebug($"Successfully bound RTP socket on {rtpSocket.LocalEndPoint}.");
                    }
                    break;
                }

                bindAttempts++;
            } while (bindAttempts < MAXIMUM_RTP_PORT_BIND_ATTEMPTS);
        }

        /// <summary>
        /// Dual mode sockets are created by default if an IPv6 bind address was specified.
        /// Dual mode needs to be disabled for Mac OS sockets as they don't support the use
        /// of dual mode and the receive methods that return packet information. Packet info
        /// is needed to get the remote recipient.
        /// </summary>
        /// <returns></returns>
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

                    //AsyncCallback endReceiveCB = (IAsyncResult ar) =>
                    //{

                    //    try
                    //    {
                    //        testSocket?.EndReceiveFrom(ar, ref remoteEP);
                    //    }
                    //    catch { }
                    //};

                    //testSocket.BeginReceiveFrom(buf, 0, buf.Length, SocketFlags.None, ref remoteEP, endReceiveCB, null);
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
        public static void CreateRtpSocketInRange(int rangeStartPort, int rangeEndPort, int startPort, bool createControlSocket, IPAddress localAddress, out Socket rtpSocket, out Socket controlSocket)
        {
            if (startPort == 0)
            {
                startPort = Crypto.GetRandomInt(rangeStartPort, rangeEndPort);
            }
            else if (startPort < rangeStartPort || startPort > rangeEndPort)
            {
                logger.LogWarning($"The start port of {startPort} supplied to CreateRtpSocket was outside the request range of {rangeStartPort}:{rangeEndPort}. A new valid start port will be pseudo-randomly chosen.");
                startPort = Crypto.GetRandomInt(rangeStartPort, rangeEndPort);
            }

            logger.LogDebug($"CreateRtpSocket start port {startPort}, range {rangeStartPort}:{rangeEndPort}.");

            rtpSocket = null;
            controlSocket = null;

            bool bindSuccess = false;
            int rtpPort = startPort;

            // Attempt to adjust the start port for:
            // - If in use ports can be checked find the first even unused port,
            // - Otherwise if not even then set to the nearest even port.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // On Windows we can get a list of in use UDP ports and avoid attempting to bind to them.
                IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                var udpListeners = ipGlobalProperties.GetActiveUdpListeners();

                var portRange = Enumerable.Range(rangeStartPort, rangeEndPort - rangeStartPort).OrderBy(x => (x > startPort) ? x : x + rangeEndPort);
                var inUsePorts = udpListeners.Where(x => x.Port >= rangeStartPort && x.Port <= rangeEndPort).Select(x => x.Port);

                logger.LogDebug($"In use UDP ports count {inUsePorts.Count()}.");

                rtpPort = portRange.Except(inUsePorts).Where(x => x % 2 == 0).FirstOrDefault();
            }
            else
            {
                // If the start port isn't even adjust it so it is. The original RTP specification required RTP ports to be even 
                // numbered and the control port to be the RTP port + 1.
                if (rtpPort % 2 != 0)
                {
                    rtpPort = (rtpPort + 1) > rangeEndPort ? rtpPort - 1 : rtpPort + 1;
                }
            }

            for (int bindAttempts = 0; bindAttempts <= MAXIMUM_RTP_PORT_BIND_ATTEMPTS; bindAttempts++)
            {
                //lock (_allocatePortsMutex)
                //{
                int controlPort = (createControlSocket == true) ? rtpPort + 1 : 0;

                try
                {
                    // The potential ports have been found now try and use them.
                    if (localAddress != null)
                    {
                        rtpSocket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                        rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                        rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;
                        rtpSocket.Bind(new IPEndPoint(localAddress, rtpPort));
                    }
                    else
                    {
                        // Create a dual mode IPv4/IPv6 socket.
                        rtpSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
                        rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                        rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;
                        var bindAddress = (Socket.OSSupportsIPv6) ? IPAddress.IPv6Any : IPAddress.Any;
                        rtpSocket.Bind(new IPEndPoint(bindAddress, rtpPort));
                    }

                    if (controlPort != 0)
                    {
                        if (localAddress != null)
                        {
                            controlSocket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                            controlSocket.Bind(new IPEndPoint(localAddress, controlPort));
                        }
                        else
                        {
                            // Create a dual mode IPv4/IPv6 socket.
                            controlSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
                            var bindAddress = (Socket.OSSupportsIPv6) ? IPAddress.IPv6Any : IPAddress.Any;
                            controlSocket.Bind(new IPEndPoint(bindAddress, controlPort));
                        }

                        logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint} and control socket {controlSocket.LocalEndPoint}.");
                    }
                    else
                    {
                        logger.LogDebug($"Successfully bound RTP socket {rtpSocket.LocalEndPoint}.");
                    }

                    bindSuccess = true;

                    break;
                }
                catch (SocketException sockExcp)
                {
                    if (sockExcp.SocketErrorCode != SocketError.AddressAlreadyInUse)
                    {
                        if (controlPort != 0)
                        {
                            logger.LogWarning($"Socket error {sockExcp.ErrorCode} binding to address {localAddress} and RTP port {rtpPort} and/or control port of {controlPort}, attempt {bindAttempts}.");
                        }
                        else
                        {
                            logger.LogWarning($"Socket error {sockExcp.ErrorCode} binding to address {localAddress} and RTP port {rtpPort}, attempt {bindAttempts}.");
                        }
                    }
                    else
                    {
                        logger.LogWarning($"SocketException in NetServices.CreateRtpSocket. {sockExcp}");
                    }
                }
                catch (Exception excp)
                {
                    logger.LogWarning($"Exception in NetServices.CreateRtpSocket. {excp}");
                }

                // Adjust the start port for the next attempt.
                int step = Crypto.GetRandomInt(RTP_STEP_MIN, RTP_STEM_MAX);
                step = (step % 2 == 0) ? step : step + 1;
                rtpPort = (rtpPort + step + 1) > rangeEndPort ? rangeStartPort + step : rtpPort + step;
                //}
            }

            if (!bindSuccess)
            {
                throw new ApplicationException($"RTP socket allocation failure range {rangeStartPort}:{rangeEndPort}.");
            }
        }

        public static UdpClient CreateRandomUDPListener(IPAddress localAddress, int start, int end, ArrayList inUsePorts, out IPEndPoint localEndPoint)
        {
            try
            {
                UdpClient randomClient = null;
                int attempts = 1;

                localEndPoint = null;

                while (attempts < 50)
                {
                    int port = Crypto.GetRandomInt(start, end);
                    if (inUsePorts == null || !inUsePorts.Contains(port))
                    {
                        try
                        {
                            localEndPoint = new IPEndPoint(localAddress, port);
                            randomClient = new UdpClient(localEndPoint);
                            break;
                        }
                        catch
                        {
                            //logger.LogWarning("Warning couldn't create UDP end point for " + localAddress + ":" + port + "." + excp.Message);
                        }

                        attempts++;
                    }
                }

                //logger.LogDebug("Attempts to create UDP end point for " + localAddress + ":" + port + " was " + attempts);

                return randomClient;
            }
            catch
            {
                throw new ApplicationException("Unable to create a random UDP listener between " + start + " and " + end);
            }
        }

        /// <summary>
        /// This method utilises the OS routing table to determine the local IP address to connection to a destination end point.
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
