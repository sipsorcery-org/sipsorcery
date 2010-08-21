//-----------------------------------------------------------------------------
// Filename: DNSResolver.cs
//
// Description: Attempts to resolve hostnames within a specified time.
//
// History:
// 13 Nov 2006	Aaron Clauson	Created.
//
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using log4net;

namespace BlueFace.Sys.Net
{
    public class DNSResolver
    {
        public const int DEFAULT_SIP_PORT = 5060;

        private static ILog logger = AppState.logger;

        private string m_socket = null;
        private ManualResetEvent m_resolveTimeoutEvent = new ManualResetEvent(false);
        private IPEndPoint m_resolvedSocket = null;

        public static IPEndPoint ResolveSIPSocket(string socket)
        {
            DNSResolver dnsResolver = new DNSResolver();
            return dnsResolver.ResolveSIPSocketInternal(socket);
        }

        public static IPEndPoint ResolveSIPSocket(string socket, int timeoutMilliseconds)
        {
            DNSResolver dnsResolver = new DNSResolver();
            return dnsResolver.ResolveSIPSocketInternal(socket, timeoutMilliseconds); 
        }

        private IPEndPoint ResolveSIPSocketInternal(string socket)
        {
            logger.Debug("Attempting to resolve SIP socket " + socket + ".");

            if (socket == null || socket.Trim().Length == 0)
            {
                logger.Warn("An empty socket string was passed to the DNSResolver.");
                return null;
            }
            
            int port = DEFAULT_SIP_PORT;
            string hostname = socket;
            
            int colonIndex = socket.LastIndexOf(":");
            if (colonIndex != -1)
            {
                port = Convert.ToInt32(socket.Substring(colonIndex + 1));
                hostname = socket.Substring(0, colonIndex);
            }

            DateTime startTime = DateTime.Now;
            IPHostEntry serverEntry = Dns.GetHostEntry(hostname);
            TimeSpan resolveTime = DateTime.Now.Subtract(startTime);

            if (serverEntry != null)
            {
                IPAddress address = ((IPAddress[])serverEntry.AddressList)[0];

                if (resolveTime.TotalMilliseconds > 20)
                {
                    logger.Debug("Hostname " + hostname + " resolved to " + address.ToString() + " in " + resolveTime.TotalMilliseconds + "ms.");
                }

                return IPSocket.ParseSocketString(address.ToString() + ":" + port);
            }
            else
            {
                //throw new ApplicationException("Could not resolve " + socket + ".");
                return null;
            }
        }

        private IPEndPoint ResolveSIPSocketInternal(string socket, int timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0)
            {
                // No timeout specified.
                return ResolveSIPSocketInternal(socket);
            }
            else
            {
                m_socket = socket;
                Thread resolveThread = new Thread(new ThreadStart(ResolveSIPSocketAsync));
                resolveThread.Start();

                if (!m_resolveTimeoutEvent.WaitOne(timeoutMilliseconds, false))
                {
                    throw new ApplicationException("DNS resolution of " + socket + " timed out after " + timeoutMilliseconds + "ms.");
                }
                else
                {
                    return m_resolvedSocket;
                }
            }
        }

        private void ResolveSIPSocketAsync()
        {
            try
            {
                m_resolvedSocket = ResolveSIPSocketInternal(m_socket);
            }
            catch
            { 
                //logger.Error("Exception ResolveSIPSocketAsync. " + m_socket + ". " + excp.Message);
            }
            finally
            {
                m_resolveTimeoutEvent.Set();
            }     
        }
    }
}
