// ============================================================================
// FileName: NetServices.cs
//
// Description:
// Contains wrappers to access the functionality of the underlying operating
// system.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Dec 2005	Aaron Clauson	Created.
// ============================================================================

using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    public enum PlatformEnum
    {
        Windows = 1,
        Linux = 2,
    }
    
    public class NetServices
	{
        public const int UDP_PORT_START = 1025;
        public const int UDP_PORT_END = 65535;
        private const int RTP_RECEIVE_BUFFER_SIZE = 100000000;
        private const int RTP_SEND_BUFFER_SIZE = 100000000;
        private const int MAXIMUM_RTP_PORT_BIND_ATTEMPTS = 5;               // The maximum number of re-attempts that will be made when trying to bind the RTP port.

        private static ILogger logger = Log.Logger;

        public static PlatformEnum Platform = PlatformEnum.Windows;

        private static Mutex _allocatePortsMutex = new Mutex();

        public static UdpClient CreateRandomUDPListener(IPAddress localAddress, out IPEndPoint localEndPoint)
        {
            return CreateRandomUDPListener(localAddress, UDP_PORT_START, UDP_PORT_END, null, out localEndPoint);
        }

        public static void CreateRtpSocket(IPAddress localAddress, int startPort, int endPort, bool createControlSocket, out Socket rtpSocket, out Socket controlSocket)
        {
            rtpSocket = null;
            controlSocket = null;

            lock (_allocatePortsMutex)
            {
                //if (_nextMediaPort >= MEDIA_PORT_END)
                //{
                //    // The media port range has been used go back to the start. Some connections have most likely been closed.
                //    _nextMediaPort = MEDIA_PORT_START;
                //}

                var inUseUDPPorts = (from p in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners() where p.Port >= startPort && p.Port <= endPort && p.Address.ToString() == localAddress.ToString() select p.Port).OrderBy(x => x).ToList();

                // Make the RTP port start on an even port. Some legacy systems require the RTP port to be an even port number.
                if(startPort % 2 != 0)
                {
                    startPort += 1;
                }

                int rtpPort = startPort;
                int controlPort = (createControlSocket == true) ? rtpPort + 1 : 0;

                if (inUseUDPPorts.Count > 0)
                {
                    // Find the first two available for the RTP socket.
                    for (int index = startPort; index <= endPort; index += 2)
                    {
                        if (!inUseUDPPorts.Contains(index))
                        {
                            rtpPort = index;

                            if (!createControlSocket)
                            {
                                break;
                            }
                            else if (!inUseUDPPorts.Contains(index + 1))
                            {
                                controlPort = index + 1;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    rtpPort = startPort;

                    if (createControlSocket)
                    {
                        controlPort = rtpPort + 1;
                    }
                }

                if (rtpPort != 0)
                {
                    bool bindSuccess = false;

                    for (int bindAttempts = 0; bindAttempts <= MAXIMUM_RTP_PORT_BIND_ATTEMPTS; bindAttempts++)
                    {
                        try
                        {
                            // The potential ports have been found now try and use them.
                            rtpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                            rtpSocket.ReceiveBufferSize = RTP_RECEIVE_BUFFER_SIZE;
                            rtpSocket.SendBufferSize = RTP_SEND_BUFFER_SIZE;

                            rtpSocket.Bind(new IPEndPoint(localAddress, rtpPort));

                            if (controlPort != 0)
                            {
                                controlSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                                controlSocket.Bind(new IPEndPoint(localAddress, controlPort));

                                logger.LogDebug("Successfully bound RTP socket " + localAddress + ":" + rtpPort + " and control socket " + localAddress + ":" + controlPort + ".");
                            }
                            else
                            {
                                logger.LogDebug("Successfully bound RTP socket " + localAddress + ":" + rtpPort + ".");
                            }

                            bindSuccess = true;

                            break;
                        }
                        catch (System.Net.Sockets.SocketException)
                        {
                            if (controlPort != 0)
                            {
                                logger.LogWarning("Failed to bind on address " + localAddress + " to RTP port " + rtpPort + " and/or control port of " + controlPort + ", attempt " + bindAttempts + ".");
                            }
                            else
                            {
                                logger.LogWarning("Failed to bind on address " + localAddress + " to RTP port " + rtpPort + ", attempt " + bindAttempts + ".");
                            }

                            // Increment the port range in case there is an OS/network issue closing/cleaning up already used ports.
                            rtpPort += 2;
                            controlPort += (controlPort != 0) ? 2 : 0;
                        }
                    }

                    if (!bindSuccess)
                    {
                        throw new ApplicationException("An RTP socket could be created due to a failure to bind on address " + localAddress + " to the RTP and/or control ports within the range of " + startPort + " to " + endPort + ".");
                    }
                }
                else
                {
                    throw new ApplicationException("An RTP socket could be created due to a failure to allocate on address " + localAddress + " and an RTP and/or control ports within the range " + startPort + " to " + endPort + ".");
                }
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
        /// Extracts the default gateway from the route print command
        /// </summary>
        /// <returns>The IP Address of the default gateway.</returns>
        public static IPAddress GetDefaultGateway()
        {
            try
            {
                string routeTable = CallRoute();

                if (routeTable != null)
                {
                    if (Platform == PlatformEnum.Windows)
                    {
                        Match gatewayMatch = Regex.Match(routeTable, @"Gateway\s*:\s*(?<gateway>(\d+\.){3}\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                        if (gatewayMatch.Success)
                        {
                            return IPAddress.Parse(gatewayMatch.Result("${gateway}"));
                        }
                    }
                    else
                    {
                        Match gatewayMatch = Regex.Match(routeTable, @"default\s*(?<gateway>(\d+\.){3}\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                        if (gatewayMatch.Success)
                        {
                            return IPAddress.Parse(gatewayMatch.Result("${gateway}"));
                        }
                    }
                }

                return null;
            }
            catch
            {
                //logger.LogError("Exception GetDefaultGateway. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// Attempts to get the local IP address that is being used with the default gateway and is therefore the one being used
        /// to connect to the internet.
        /// </summary>
        /// <param name="defaultGateway"></param>
        /// <returns></returns>
        public static IPAddress GetDefaultIPAddress(IPAddress defaultGateway)
        {
            try
            {
                string[] gatewayOctets = Regex.Split(defaultGateway.ToString(), @"\.");

                IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName()); 

                ArrayList possibleMatches = new ArrayList();
                foreach (IPAddress localAddress in hostEntry.AddressList)
                {
                    possibleMatches.Add(localAddress);
                }

                for (int octetIndex = 0; octetIndex < 4; octetIndex++)
                {
                    IPAddress[] testAddresses = (IPAddress[])possibleMatches.ToArray(typeof(IPAddress));
                    foreach (IPAddress localAddress in testAddresses)
                    {
                        string[] localOctets = Regex.Split(localAddress.ToString(), @"\.");
                        if (gatewayOctets[octetIndex] != localOctets[octetIndex])
                        {
                            possibleMatches.Remove(localAddress);
                        }

                        if (possibleMatches.Count == 1)
                        {
                            return (IPAddress)possibleMatches[0];
                        }
                    }
                }

                return null;
            }
            catch
            {
                //logger.LogError("Exception GetDefaultIPAddress. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// Calls the operating system command 'route print' to obtain the IP
        /// routing information.
        /// </summary>
        /// <returns>A string holding the output of the command.</returns>
        public static string CallRoute()
        {
            try
            {
                if (Platform == PlatformEnum.Windows)
                {
                    return CallShellCommand("route", "print");
                }
                else
                {
                    return CallShellCommand("route", "");
                }
            }
            catch (Exception excp)
            {
                //logger.LogError("Exception call to 'route print': " + excp.Message);
                throw new ApplicationException("An attempt to call 'route print' failed. " + excp.Message);
            }
        }

        /// Creates a new process to execute a specified shell command and returns the output
        /// to the caller as a string.
        /// </summary>
        public static string CallShellCommand(string command, string commandLine)
        {
            Process osProcess = new Process();
            osProcess.StartInfo.CreateNoWindow = true;
            osProcess.StartInfo.UseShellExecute = false;
            //osProcess.StartInfo.UseShellExecute = true;
            osProcess.StartInfo.RedirectStandardOutput = true;

            osProcess.StartInfo.FileName = command;
            osProcess.StartInfo.Arguments = commandLine;

            osProcess.Start();

            osProcess.WaitForExit();
            return osProcess.StandardOutput.ReadToEnd();
        }
	}
}
