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
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

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
            => ParseConnectionInformation(connectionLine.AsSpan());

        public static SDPConnectionInformation ParseConnectionInformation(ReadOnlySpan<char> connectionLine)
        {
            var connectionInfo = new SDPConnectionInformation();

            connectionLine = connectionLine.Slice(2).Trim();

            Span<Range> fields = stackalloc Range[4];
            var fieldCount = connectionLine.Split(fields, ' ', StringSplitOptions.RemoveEmptyEntries);

            if (fieldCount > 0)
            {
                connectionInfo.ConnectionNetworkType = connectionLine[fields[0]].Trim().ToString();
            }

            if (fieldCount > 1)
            {
                connectionInfo.ConnectionAddressType = connectionLine[fields[1]].Trim().ToString();
            }

            if (fieldCount > 2)
            {
                connectionInfo.ConnectionAddress = connectionLine[fields[2]].Trim().ToString();
            }

            return connectionInfo;
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            WriteString(builder);
            return builder.ToString();
        }

        public void WriteString(StringBuilder builder)
        {
            builder
                .Append("c=")
                .Append(ConnectionNetworkType)
                .Append(' ')
                .Append(ConnectionAddressType)
                .Append(' ')
                .Append(ConnectionAddress)
                .Append(m_CRLF);
        }
    }
}
