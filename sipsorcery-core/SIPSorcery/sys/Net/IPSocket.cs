//-----------------------------------------------------------------------------
// Filename: IPSocket.cs
//
// Description: Converts special charatcers in XML to their safe equivalent.
//
// History:
// 22 jun 2005	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace SIPSorcery.Sys
{
	public class IPSocket
	{
        /// <summary>
        /// Specifies the minimum acceptable value for the <see cref='System.Net.IPEndPoint.Port'/> property.
        /// </summary>
        public const int MinPort = 0x00000000;

        /// <summary>
        /// Specifies the maximum acceptable value for the <see cref='System.Net.IPEndPoint.Port'/> property.
        /// </summary>
        public const int MaxPort = 0x0000FFFF;

        /// <summary>
        /// Returns an IPv4 end point from a socket address in 10.0.0.1:5060 format.
        /// </summary>>
        //      public static IPEndPoint GetIPEndPoint(string IPSocket)
        //{
        //	if(IPSocket == null || IPSocket.Trim().Length == 0)
        //	{
        //		throw new ApplicationException("IPSocket cannot parse an IPEndPoint from an empty string.");
        //	}

        //	try
        //	{
        //		int colonIndex = IPSocket.IndexOf(":");

        //		if(colonIndex != -1)
        //		{
        //			string ipAddress = IPSocket.Substring(0, colonIndex).Trim();
        //			int port = Convert.ToInt32(IPSocket.Substring(colonIndex+1).Trim());
        //			IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);

        //			return endPoint;
        //		}
        //		else
        //		{
        //			return new IPEndPoint(IPAddress.Parse(IPSocket.Trim()), 0);
        //		}
        //	}
        //	catch(Exception excp)
        //	{
        //		throw new ApplicationException(excp.Message + "(" + IPSocket + ")");
        //	}
        //}

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
        /// <param name="result">If the parse is successfull this output parameter will contain the IPv4 or IPv6 end point.</param>
        /// <returns>Returns true if the string could be successfully parsed an an IPv4 or IPv6 end point. False if not.</returns>
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
            if(TryParseIPEndPoint(s, out var ipEndPoint))
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
            //if (host != null && host.Trim().Length > 0)
            //{
            //    if (host.StartsWith("127.0.0.1") ||
            //        host.StartsWith("10.") ||
            //        Regex.Match(host, @"^172\.1[6-9]\.").Success ||
            //        Regex.Match(host, @"^172\.2\d\.").Success ||
            //        host.StartsWith("172.30.") ||
            //        host.StartsWith("172.31.") ||
            //        host.StartsWith("192.168."))
            //    {
            //        return true;
            //    }
            //    else
            //    {
            //        return false;
            //    }
            //}
            //else
            //{
            //    return false;
            //}

            if(IPAddress.TryParse(host, out var ipAddress))
            {
                if(IPAddress.IsLoopback(ipAddress) || ipAddress.IsIPv6LinkLocal || ipAddress.IsIPv6SiteLocal)
                {
                    return true;
                }
                else if(ipAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    byte[] addrBytes = ipAddress.GetAddressBytes();
                    if ((addrBytes[0] == 10) ||
                        (addrBytes[0] == 172 && (addrBytes[1] >= 16 && addrBytes[1] <=31)) ||
                        (addrBytes[0] == 192 && addrBytes[1] == 168))
                    {
                        return true;
                    }
                }
             }

            return false;
        }
    }
}
