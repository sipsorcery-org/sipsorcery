//-----------------------------------------------------------------------------
// Filename: SDPConnectionInformation.cs
//
// Description: 
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// ??	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Polyfills;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class SDPConnectionInformation
    {
        public const string CONNECTION_ADDRESS_TYPE_IPV4 = "IP4";
        public const string CONNECTION_ADDRESS_TYPE_IPV6 = "IP6";

        public const string m_CRLF = "\r\n";

        /// <summary>
        /// Type of network, IN = Internet.
        /// </summary>
        public string ConnectionNetworkType = "IN";

        /// <summary>
        /// Session level address family.
        /// </summary>
        public string ConnectionAddressType = CONNECTION_ADDRESS_TYPE_IPV4;

        /// <summary>
        /// IP or multicast address for the media connection.
        /// </summary>
        public string ConnectionAddress;

        private SDPConnectionInformation()
        { }

        public SDPConnectionInformation(IPAddress connectionAddress)
        {
            ConnectionAddress = connectionAddress.ToString();
            ConnectionAddressType = (connectionAddress.AddressFamily == AddressFamily.InterNetworkV6) ? CONNECTION_ADDRESS_TYPE_IPV6 : CONNECTION_ADDRESS_TYPE_IPV4;
        }

        public static SDPConnectionInformation ParseConnectionInformation(string connectionLine)
        {
            SDPConnectionInformation connectionInfo = new SDPConnectionInformation();
            var connectionFields = connectionLine.AsSpan(2).Trim();
            var fieldIndex = 0;
            foreach (var fieldRange in connectionFields.Split(' '))
            {
                var field = connectionFields[fieldRange].Trim().ToString();
                if (fieldIndex == 0)
                {
                    connectionInfo.ConnectionNetworkType = field;
                }
                else if (fieldIndex == 1)
                {
                    connectionInfo.ConnectionAddressType = field;
                }
                else if (fieldIndex == 2)
                {
                    connectionInfo.ConnectionAddress = field;
                    break;
                }

                fieldIndex++;
            }

            return connectionInfo;
        }

        public override string ToString()
        {
            return $"c={ConnectionNetworkType} {ConnectionAddressType} {ConnectionAddress}{m_CRLF}";
        }
    }
}
