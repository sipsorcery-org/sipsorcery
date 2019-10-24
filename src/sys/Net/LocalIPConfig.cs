//-----------------------------------------------------------------------------
// Filename: LocalIPConfig.cs
//
// Description: Provides information about the Internet Protocol configuration of the local machine.
//
// Author(s):
// Aaron Clauson
//
// History:
// 25 Mar 2009	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    public static class LocalIPConfig
    {
        public const string ALL_LOCAL_IPADDRESSES_KEY = "*";
        public const string LINK_LOCAL_BLOCK_PREFIX = "169.254";    // Used by hosts attempting to acquire a DHCP address. See RFC 3330.

        private static ILogger logger = Log.Logger;

        public static List<IPAddress> GetLocalIPv4Addresses()
        {
            List<IPAddress> localAddresses = new List<IPAddress>();

            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in adapters)
            {
                IPInterfaceProperties adapterProperties = adapter.GetIPProperties();

                UnicastIPAddressInformationCollection localIPs = adapterProperties.UnicastAddresses;
                foreach (UnicastIPAddressInformation localIP in localIPs)
                {
                    if (localIP.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !localIP.Address.ToString().StartsWith(LINK_LOCAL_BLOCK_PREFIX))
                    {
                        localAddresses.Add(localIP.Address);
                    }
                }
            }

            return localAddresses;
        }

        public static IPAddress GetDefaultIPv4Address()
        {
            var adapters = from adapter in NetworkInterface.GetAllNetworkInterfaces()
                           where adapter.OperationalStatus == OperationalStatus.Up && adapter.Supports(NetworkInterfaceComponent.IPv4)
                           && adapter.GetIPProperties().GatewayAddresses.Count > 0 &&
                           adapter.GetIPProperties().GatewayAddresses[0].Address.ToString() != "0.0.0.0"
                           select adapter;

            if (adapters == null || adapters.Count() == 0)
            {
                throw new ApplicationException("The default IPv4 address could not be determined as there are were no interfaces with a gateway.");
            }
            else
            {
                UnicastIPAddressInformationCollection localIPs = adapters.First().GetIPProperties().UnicastAddresses;
                foreach (UnicastIPAddressInformation localIP in localIPs)
                {
                    if (localIP.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !localIP.Address.ToString().StartsWith(LINK_LOCAL_BLOCK_PREFIX) &&
                        !IPAddress.IsLoopback(localIP.Address))
                    {
                        return localIP.Address;
                    }
                }
            }

            return null;
        }

        public static List<IPEndPoint> GetLocalIPv4EndPoints(int port)
        {
            List<IPEndPoint> localEndPoints = new List<IPEndPoint>();
            List<IPAddress> localAddresses = GetLocalIPv4Addresses();

            foreach (IPAddress localAddress in localAddresses)
            {
                localEndPoints.Add(new IPEndPoint(localAddress, port));
            }

            return localEndPoints;
        }

        public static IPAddress GetDefaultIPv6Address()
        {
            var adapters = from adapter in NetworkInterface.GetAllNetworkInterfaces()
                           where adapter.OperationalStatus == OperationalStatus.Up && adapter.Supports(NetworkInterfaceComponent.IPv6)
                           && adapter.GetIPProperties().GatewayAddresses.Count > 0 
                           select adapter;

            if (adapters == null || adapters.Count() == 0)
            {
                throw new ApplicationException("The default IPv6 address could not be determined as there are were no interfaces with a gateway.");
            }
            else
            {
                UnicastIPAddressInformationCollection localIPs = adapters.First().GetIPProperties().UnicastAddresses;
                foreach (UnicastIPAddressInformation localIP in localIPs)
                {
                    if (localIP.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                        !IPAddress.IsLoopback(localIP.Address) &&
                        localIP.Address.IsIPv6SiteLocal == false &&
                        localIP.Address.IsIPv6LinkLocal == false)
                    {
                        return localIP.Address;
                    }
                }
            }

            return null;
        }

        public static List<IPEndPoint> ParseIPSockets(XmlNode socketNodes)
        {
            List<IPEndPoint> endPoints = new List<IPEndPoint>();
            List<IPAddress> localAddresses = GetLocalIPv4Addresses();

            foreach (XmlNode socketNode in socketNodes.ChildNodes)
            {
                string socketString = socketNode.InnerText;
                logger.LogDebug("Parsing end point from socket string " + socketString + ".");

                int port = IPSocket.ParsePortFromSocket(socketString);
                if (socketString.StartsWith(ALL_LOCAL_IPADDRESSES_KEY))
                {
                    foreach (IPAddress ipAddress in localAddresses)
                    {
                        endPoints.Add(new IPEndPoint(ipAddress, port));
                    }
                }
                else
                {
                    if (IPSocket.TryParseIPEndPoint(socketString, out var ipEndPoint))
                    {
                        endPoints.Add(ipEndPoint);
                    }
                    else
                    {
                        logger.LogWarning($"Could not parse IP end point string {socketString} in LocalIPConfig.ParseIPSockets.");
                    }
                }
            }

            return endPoints;
        }
    }
}
