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

using System.Net;
using System.Net.Sockets;

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
            string[] connectionFields = connectionLine.Substring(2).Trim().Split(' ');
            connectionInfo.ConnectionNetworkType = connectionFields[0].Trim();
            connectionInfo.ConnectionAddressType = connectionFields[1].Trim();
            connectionInfo.ConnectionAddress = connectionFields[2].Trim();
            return connectionInfo;
        }

        public override string ToString()
        {
            return "c=" + ConnectionNetworkType + " " + ConnectionAddressType + " " + ConnectionAddress + m_CRLF;
        }
    }
}
