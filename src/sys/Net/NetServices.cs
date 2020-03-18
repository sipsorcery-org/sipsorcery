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
        private const int RTP_RECEIVE_BUFFER_SIZE = 100000000;
        private const int RTP_SEND_BUFFER_SIZE = 100000000;
        private const int MAXIMUM_RTP_PORT_BIND_ATTEMPTS = 25;  // The maximum number of re-attempts that will be made when trying to bind the RTP port.
        private const string INTERNET_IPADDRESS = "1.1.1.1";    // IP address to use when getting default IP address from OS. No connection is established.
        private const int NETWORK_TEST_PORT = 5060;                       // Port to use when doing a Udp.Connect to determine local IP address (port 0 does not work on macos).
        private const int LOCAL_ADDRESS_CACHE_LIFETIME_SECONDS = 300;   // The amount of time to leave the result of a local IP address determination in the cache.
        private const int RECENT_PORTS_QUEUE_SIZE = 100;

        private static ILogger logger = Log.Logger;

        private static ConcurrentQueue<int> _recentlyAllocatedPorts = new ConcurrentQueue<int>();
        private static Mutex _allocatePortsMutex = new Mutex();

        /// <summary>
        /// A lookup collection to cache the local IP address for a destination address. The collection will cache results of
        /// asking the Operating System which local address to use for a destination address. The cache saves a relatively 
        /// expensive call to create a socket and ask the OS for a route lookup.
        /// 
        /// TODO:  Clear this cache if the state of the local network interfaces change.
        /// </summary>
        private static ConcurrentDictionary<IPAddress, Tuple<IPAddress, DateTime>> m_localAddressTable = new ConcurrentDictionary<IPAddress, Tuple<IPAddress, DateTime>>();

        public static UdpClient CreateRandomUDPListener(IPAddress localAddress, out IPEndPoint localEndPoint)
        {
            return CreateRandomUDPListener(localAddress, UDP_PORT_START, UDP_PORT_END, null, out localEndPoint);
        }

        public static void CreateRtpSocket(IPAddress localAddress, int rangeStartPort, int rangeEndPort, int startPort, bool createControlSocket, out Socket rtpSocket, out Socket controlSocket)
        {
            logger.LogDebug($"CreateRtpSocket start port {startPort}, range {rangeStartPort}:{rangeEndPort}.");

            rtpSocket = null;
            controlSocket = null;

            bool bindSuccess = false;

            for (int bindAttempts = 0; bindAttempts <= MAXIMUM_RTP_PORT_BIND_ATTEMPTS; bindAttempts++)
            {
                lock (_allocatePortsMutex)
                {
                    IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                    var udpListeners = ipGlobalProperties.GetActiveUdpListeners();

                    var portRange = Enumerable.Range(rangeStartPort, rangeEndPort - rangeStartPort).OrderBy(x => (x > startPort) ? x : x + rangeEndPort);
                    var inUsePorts = udpListeners.Where(x => x.Port >= rangeStartPort && x.Port <= rangeEndPort).Select(x => x.Port); //.OrderBy(x => x);

                    logger.LogDebug($"In use UDP ports count {inUsePorts.Count()}.");

                    int rtpPort = portRange.Except(inUsePorts).Where(x => x % 2 == 0).FirstOrDefault();

                    int controlPort = (createControlSocket == true) ? rtpPort + 1 : 0;

                    try
                    {
                        // The potential ports have been found now try and use them.
                        rtpSocket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                        rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                        rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                        rtpSocket.Bind(new IPEndPoint(localAddress, rtpPort));

                        if (controlPort != 0)
                        {
                            controlSocket = new Socket(localAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                            controlSocket.Bind(new IPEndPoint(localAddress, controlPort));

                            logger.LogDebug($"Successfully bound RTP socket {localAddress}:{rtpPort} and control socket {localAddress}:{controlPort}.");
                        }
                        else
                        {
                            logger.LogDebug($"Successfully bound RTP socket {localAddress}:{rtpPort}.");
                        }

                        bindSuccess = true;

                        break;
                    }
                    catch (System.Net.Sockets.SocketException sockExcp)
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
                    }
                }
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
                UdpClient udpClient = new UdpClient(destination.AddressFamily);
                udpClient.Connect(destination, NETWORK_TEST_PORT);
                var localAddress = (udpClient.Client.LocalEndPoint as IPEndPoint).Address;

                m_localAddressTable.TryAdd(destination, new Tuple<IPAddress, DateTime>(localAddress, DateTime.Now));

                return localAddress;
            }
        }

        /// <summary>
        /// Gets the default local address for this machine for communicating with the Internet.
        /// </summary>
        /// <returns>The local address this machine should use for communicating with the Internet.</returns>
        public static IPAddress GetLocalAddressForInternet()
        {
            var internetAddress = IPAddress.Parse(INTERNET_IPADDRESS);
            return GetLocalAddressForRemote(internetAddress);
        }

        /// <summary>
        /// Gets all the IP addresses for all active interfaces on the machine.
        /// </summary>
        /// <returns>A list of all local IP addresses.</returns>
        public static List<IPAddress> GetAllLocalIPAddresses()
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
