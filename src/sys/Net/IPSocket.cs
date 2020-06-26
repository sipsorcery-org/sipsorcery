//-----------------------------------------------------------------------------
// Filename: IPSocket.cs
//
// Description: Helper functions for socket strings and IP end points.
// Note that as of 16 Nov 2019 a number of equivalent functions are now
// contained in the System.Net.IPEndPoint v4 class BUT are missing from
// the Net Standard version.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 22 jun 2005	Aaron Clauson   Created, Dublin, Ireland.
// rj2: need some more helper methods
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace SIPSorcery.Sys
{
    public class IPSocket
    {
        /// <summary>
        /// Specifies the minimum acceptable value for the <see cref='System.Net.IPEndPoint'/> Port property.
        /// </summary>
        public const int MinPort = 0x00000000;

        /// <summary>
        /// Specifies the maximum acceptable value for the <see cref='System.Net.IPEndPoint'/> Port property.
        /// </summary>
        public const int MaxPort = 0x0000FFFF;

        /// <summary>
        /// This code is based on the IPEndPoint.ToString method in the dotnet source code at
        /// https://github.com/dotnet/corefx/blob/master/src/System.Net.Primitives/src/System/Net/IPEndPoint.cs.
        /// If/when that feature makes it into .NET Standard this method can be replaced.
        /// </summary>
        public static string GetSocketString(IPEndPoint endPoint)
        {
            string format = (endPoint.Address.AddressFamily == AddressFamily.InterNetworkV6) ? "[{0}]:{1}" : "{0}:{1}";
            return string.Format(format, endPoint.Address.ToString(), endPoint.Port.ToString(NumberFormatInfo.InvariantInfo));
        }

        /// <summary>
        /// This code is based on the IPEndPoint.TryParse method in the dotnet source code at
        /// https://github.com/dotnet/corefx/blob/master/src/System.Net.Primitives/src/System/Net/IPEndPoint.cs.
        /// If/when that feature makes it into .NET Standard this method can be replaced.
        /// </summary>
        /// <param name="s">The end point string to parse.</param>
        /// <param name="result">If the parse is successful this output parameter will contain the IPv4 or IPv6 end point.</param>
        /// <returns>Returns true if the string could be successfully parsed as an IPv4 or IPv6 end point. False if not.</returns>
        public static bool TryParseIPEndPoint(string s, out IPEndPoint result)
        {
            int addressLength = s.Length;  // If there's no port then send the entire string to the address parser
            int lastColonPos = s.LastIndexOf(':');

            // Look to see if this is an IPv6 address with a port.
            if (lastColonPos > 0)
            {
                if (s[lastColonPos - 1] == ']')
                {
                    addressLength = lastColonPos;
                }
                // Look to see if this is IPv4 with a port (IPv6 will have another colon)
                else if (s.Substring(0, lastColonPos).LastIndexOf(':') == -1)
                {
                    addressLength = lastColonPos;
                }
            }

            if (IPAddress.TryParse(s.Substring(0, addressLength), out IPAddress address))
            {
                uint port = 0;
                if (addressLength == s.Length ||
                    (uint.TryParse(s.Substring(addressLength + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port) && port <= MaxPort))
                {
                    result = new IPEndPoint(address, (int)port);
                    return true;
                }
            }

            result = null;
            return false;
        }

        public static IPEndPoint ParseSocketString(string s)
        {
            if (TryParseIPEndPoint(s, out var ipEndPoint))
            {
                return ipEndPoint;
            }
            else
            {
                throw new ApplicationException($"Could not parse IP end point from {s}.");
            }
        }

        public static string ParseHostFromSocket(string socket)
        {
            string host = socket;

            if (socket != null && socket.Trim().Length > 0 && socket.IndexOf(':') != -1)
            {
                host = socket.Substring(0, socket.LastIndexOf(':')).Trim();
            }

            return host;
        }

        /// <summary>
        /// For IPv6 addresses with port the string format is of the form:
        /// [2a02:8084:6981:7880:54a9:d238:b2ee:ceb]:6060
        /// Without a port the form is:
        /// 2a02:8084:6981:7880:54a9:d238:b2ee:ceb
        /// </summary>
        /// <param name="socket">The socket string to check </param>
        /// <returns>The socket string's explicit port number or 0 if it does not have one.</returns>
        public static int ParsePortFromSocket(string socket)
        {
            int port = 0;

            int lastColonPos = socket.LastIndexOf(':');

            // Look to see if this is an IPv6 address with a port.
            if (lastColonPos > 0)
            {
                if (socket[lastColonPos - 1] == ']')
                {
                    // This is an IPv6 address WITH a port.
                }
                // Look to see if this is IPv4 with a port (IPv6 will have another colon)
                // If it's a host name there will also not be another ':'.
                else if (socket.Substring(0, lastColonPos).LastIndexOf(':') != -1)
                {
                    // This is an IPv6 address WITHOUT a port.
                    lastColonPos = -1;
                }
            }

            if (socket != null && socket.Trim().Length > 0 && lastColonPos != -1)
            {
                port = Convert.ToInt32(socket.Substring(lastColonPos + 1).Trim());
            }

            return port;
        }

        /// <summary>
        /// (convenience method) check if string can be parsed as IPAddress
        /// </summary>
        /// <param name="socket">string to check</param>
        /// <returns>true/false</returns>
        public static bool IsIPAddress(string socket)
        {
            if (socket == null || socket.Trim().Length == 0)
            {
                return false;
            }
            else
            {
                IPAddress ipaddr;
                return IPAddress.TryParse(socket, out ipaddr);
            }
        }

        /// <summary>
        /// Checks the Contact SIP URI host and if it is recognised as a private address it is replaced with the socket
        /// the SIP message was received on.
        /// 
        /// Private address space blocks RFC 1597.
        ///		10.0.0.0        -   10.255.255.255
        ///		172.16.0.0      -   172.31.255.255
        ///		192.168.0.0     -   192.168.255.255
        ///
        /// </summary>
        public static bool IsPrivateAddress(string host)
        {
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                if (IPAddress.IsLoopback(ipAddress) || ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6SiteLocal)
                {
                    return true;
                }
                else if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    byte[] addrBytes = ipAddress.GetAddressBytes();
                    if ((addrBytes[0] == 10) ||
                        (addrBytes[0] == 172 && (addrBytes[1] >= 16 && addrBytes[1] <= 31)) ||
                        (addrBytes[0] == 192 && addrBytes[1] == 168))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if <paramref name="endpointstring"/> contains a hostname or ip-address and ip-port
        /// accepts IPv4 and IPv6 and IPv6 mapped IPv4 addresses
        /// return detected values in <paramref name="host"/> and <paramref name="port"/>
        /// 
        /// adapted from: http://stackoverflow.com/questions/2727609/best-way-to-create-ipendpoint-from-string
        /// </summary>
        /// <remarks>
        /// rj2: I had the requirement of parsing an IPEndpoint with IPv6, v4 and hostnames and getting them as string and int
        /// </remarks>
        /// <param name="endpointstring">string to check</param>
        /// <param name="host">host-portion of <paramref name="endpointstring"/>, if host can be parsed as IPAddress, then <paramref name="host"/> is IPAddress.ToString</param>
        /// <param name="port">port-portion of <paramref name="endpointstring"/></param>
        /// <returns>true if host-portion of endpoint string is valid ip-address</returns>
        /// <exception cref="System.ArgumentException">if <paramref name="endpointstring"/> is null/empty </exception>
        /// <exception cref="System.FormatException">if host looks like ip-address but can't be parsed</exception>
        public static bool Parse(string endpointstring, out string host, out int port)
        {
            bool rc = false;
            if (string.IsNullOrWhiteSpace(endpointstring))
            {
                throw new ArgumentException("Endpoint descriptor must not be empty.");
            }

            string[] values = null;
            if (endpointstring.IndexOf(';') > 0)
            {
                values = endpointstring.Substring(0, endpointstring.IndexOf(';')).Split(new char[] { ':' });
            }
            else
            {
                values = endpointstring.Split(new char[] { ':' });
            }

            IPAddress ipaddr;
            port = -1;

            //check if we have an IPv6 or ports
            if (values.Length <= 2) // ipv4 or hostname
            {
                if (values.Length == 1)
                {
                    //no port is specified, default
                    port = -1;
                }
                else
                {
                    port = getPort(values[1]);
                }

                host = values[0];
                //try to use the address as IPv4, otherwise get hostname
                if (!IPAddress.TryParse(values[0], out ipaddr))
                {
                    host = values[0];
                }
                else
                {
                    host = ipaddr.ToString();
                    rc = true;
                }
            }
            else if (values.Length > 2) //ipv6
            {
                //could [a:b:c]:d
                if (values[0].StartsWith("[") && values[values.Length - 2].EndsWith("]"))
                {
                    string ipaddressstring = string.Join(":", values.Take(values.Length - 1).ToArray());
                    ipaddr = IPAddress.Parse(ipaddressstring);
                    port = getPort(values[values.Length - 1]);
                    host = ipaddr.ToString();
                }
                else //[a:b:c] or a:b:c
                {
                    if (endpointstring.IndexOf(';') > 0)
                    {
                        ipaddr = IPAddress.Parse(endpointstring.Substring(0, endpointstring.IndexOf(';')));
                    }
                    else
                    {
                        ipaddr = IPAddress.Parse(endpointstring);
                    }

                    host = ipaddr.ToString();
                    port = -1;
                }
                rc = true;
            }
            else
            {
                throw new FormatException(string.Format("Invalid endpoint ipaddress '{0}'", endpointstring));
            }

            return rc;
        }

        public static IPEndPoint Parse(string endpointstring, int defaultport = -1)
        {
            if (endpointstring.IsNullOrBlank())
            {
                throw new ArgumentException("Endpoint descriptor must not be empty.");
            }

            if (defaultport != -1 &&
                (defaultport < IPEndPoint.MinPort
                || defaultport > IPEndPoint.MaxPort))
            {
                throw new ArgumentException(string.Format("Invalid default port '{0}'", defaultport));
            }

            string[] values = endpointstring.Split(new char[] { ':' });
            IPAddress ipaddr;
            int port = -1;

            //check if we have an IPv6 or ports
            if (values.Length <= 2) // ipv4 or hostname
            {
                if (values.Length == 1)
                {
                    //no port is specified, default
                    port = defaultport;
                }
                else
                {
                    port = getPort(values[1]);
                }

                //try to use the address as IPv4, otherwise get hostname
                if (!IPAddress.TryParse(values[0], out ipaddr))
                {
                    try
                    {
                        ipaddr = getIPfromHost(values[0]);
                    }
                    catch
                    {
                        throw new FormatException(string.Format("Invalid endpoint ipaddress '{0}'", endpointstring));
                    }
                }
            }
            else if (values.Length > 2) //ipv6
            {
                //could [a:b:c]:d
                if (values[0].StartsWith("[") && values[values.Length - 2].EndsWith("]"))
                {
                    string ipaddressstring = string.Join(":", values.Take(values.Length - 1).ToArray());
                    ipaddr = IPAddress.Parse(ipaddressstring);
                    port = getPort(values[values.Length - 1]);
                }
                else //[a:b:c] or a:b:c
                {
                    ipaddr = IPAddress.Parse(endpointstring);
                    port = defaultport;
                }
            }
            else
            {
                throw new FormatException(string.Format("Invalid endpoint ipaddress '{0}'", endpointstring));
            }

            if (port == -1)
            {
                port = 0;
            }

            return new IPEndPoint(ipaddr, port);
        }

        private static int getPort(string p)
        {
            int port;

            if (!int.TryParse(p, out port)
             || port < IPEndPoint.MinPort
             || port > IPEndPoint.MaxPort)
            {
                throw new FormatException(string.Format("Invalid end point port '{0}'", p));
            }

            return port;
        }

        private static IPAddress getIPfromHost(string p)
        {
            try
            {
                var hosts = Dns.GetHostAddresses(p);

                if (hosts == null || hosts.Length == 0)
                {
                    throw new ArgumentException(string.Format("Host not found: {0}", p));
                }
                return hosts[0];
            }
            catch
            {
                throw new ArgumentException(string.Format("Host not found: {0}", p));
            }
        }

        /// <summary>
        /// Returns an IPv4 end point from a socket address in 10.0.0.1:5060 format.
        /// </summary>>
        public static IPEndPoint GetIPEndPoint(string IPSocket)
        {
            return Parse(IPSocket);
        }
    }
}
