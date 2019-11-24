//-----------------------------------------------------------------------------
// Filename: IPSocketAddress.cs
//
// Description: Converts special charatcers in XML to their safe equivalent.
//
// Author(s):
// Aaron Clauson
//
// History:
// 22 Jun 2005	Aaron Clauson	Created.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Text.RegularExpressions;

namespace SIPSorcery.Sys
{
    public class IPSocketAddress
    {
        /// <summary>
        /// Returns an IPv4 end point from a socket address in 10.0.0.1:5060 format.
        /// </summary>>
        public static IPEndPoint GetIPEndPoint(string ipSocketAddress)
        {
            if (ipSocketAddress == null || ipSocketAddress.Trim().Length == 0)
            {
                throw new ApplicationException("IPSocketAddress cannot parse an IPEndPoint from an empty string.");
            }

            Match socketMatch = Regex.Match(ipSocketAddress, @"(?<ipaddress>(\d+\.){3}\d+):(?<port>\d+)");

            if (socketMatch.Success)
            {
                string ipAddress = socketMatch.Result("${ipaddress}");
                int port = Convert.ToInt32(socketMatch.Result("${port}"));
                IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(ipAddress), port);

                return endPoint;
            }
            else
            {
                throw new ApplicationException("IPSocket address could not parse an IPEndPoint from " + ipSocketAddress + ". Supplied address must be in 10.0.0.1:5060 format.");
            }
        }

        public static string GetSocketString(IPEndPoint endPoint)
        {
            if (endPoint != null)
            {
                return endPoint.Address.ToString() + ":" + endPoint.Port;
            }
            else
            {
                return null;
            }
        }
    }
}
