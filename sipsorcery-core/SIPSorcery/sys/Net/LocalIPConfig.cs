//-----------------------------------------------------------------------------
// Filename: LocalIPConfig.cs
//
// Description: Provides information about the Internet Protocol configuration of the local machine.
//
// History:
// 25 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using log4net;

namespace SIPSorcery.Sys
{
    public static class LocalIPConfig
    {
        public const string ALL_LOCAL_IPADDRESSES_KEY = "*";
        public const string LINK_LOCAL_BLOCK_PREFIX = "169.254";    // Used by hosts attempting to acquire a DHCP address. See RFC 3330.

        private static ILog logger = AppState.logger;

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

        public static List<IPEndPoint> ParseIPSockets(XmlNode socketNodes)
        {
            List<IPEndPoint> endPoints = new List<IPEndPoint>();
            List<IPAddress> localAddresses = GetLocalIPv4Addresses();

            foreach (XmlNode socketNode in socketNodes.ChildNodes)
            {
                string socketString = socketNode.InnerText;
                logger.Debug("Parsing end point from socket string " + socketString + ".");

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
                    endPoints.Add(IPSocket.ParseSocketString(socketString));
                }
            }

            return endPoints;
        }
    }
}
