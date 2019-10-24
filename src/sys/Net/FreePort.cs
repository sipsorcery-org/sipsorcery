//-----------------------------------------------------------------------------
// Filename: FreePort.cs
//
// Description: Helper methods to find the next free UDP and TCP ports.
//
// History:
// 28 Mar 2012	Aaron Clauson	Copied from http://www.mattbrindley.com/developing/windows/net/detecting-the-next-available-free-tcp-port/.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;

namespace SIPSorcery.Sys
{
    public class FreePort
    {
        private const string PortReleaseGuid = "8875BD8E-4D5B-11DE-B2F4-691756D89593";

        /// <summary>
        /// Check if startPort is available, incrementing and
        /// checking again if it's in use until a free port is found
        /// </summary>
        /// <param name="startPort">The first port to check</param>
        /// <returns>The first available port</returns>
        public static int FindNextAvailableTCPPort(int startPort)
        {
            int port = startPort;
            bool isAvailable = true;

            var mutex = new Mutex(false,
                string.Concat("Global/", PortReleaseGuid));
            mutex.WaitOne();
            try
            {
                IPGlobalProperties ipGlobalProperties =
                    IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] endPoints =
                    ipGlobalProperties.GetActiveTcpListeners();

                do
                {
                    if (!isAvailable)
                    {
                        port++;
                        isAvailable = true;
                    }

                    foreach (IPEndPoint endPoint in endPoints)
                    {
                        if (endPoint.Port != port) continue;
                        isAvailable = false;
                        break;
                    }

                } while (!isAvailable && port < IPEndPoint.MaxPort);

                if (!isAvailable)
                    throw new ApplicationException("Not able to find a free TCP port.");

                return port;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Check if startPort is available, incrementing and
        /// checking again if it's in use until a free port is found
        /// </summary>
        /// <param name="startPort">The first port to check</param>
        /// <returns>The first available port</returns>
        public static int FindNextAvailableUDPPort(int startPort)
        {
            int port = startPort;
            bool isAvailable = true;

            var mutex = new Mutex(false,
                string.Concat("Global/", PortReleaseGuid));
            mutex.WaitOne();
            try
            {
                IPGlobalProperties ipGlobalProperties =
                    IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] endPoints =
                    ipGlobalProperties.GetActiveUdpListeners();

                do
                {
                    if (!isAvailable)
                    {
                        port++;
                        isAvailable = true;
                    }

                    foreach (IPEndPoint endPoint in endPoints)
                    {
                        if (endPoint.Port != port) continue;
                        isAvailable = false;
                        break;
                    }

                } while (!isAvailable && port < IPEndPoint.MaxPort);

                if (!isAvailable)
                    throw new ApplicationException("Not able to find a free UDP port.");

                return port;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
