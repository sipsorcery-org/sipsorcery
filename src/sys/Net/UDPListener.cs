//-----------------------------------------------------------------------------
// Filename: UDPListener.cs
//
// Description: Generic listener for UDP based protocols.
//
// Author(s):
// Aaron Clauson
//
// History:
// 27 Feb 2012	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    public class UDPListener
    {
        private const string THREAD_NAME = "udplistener-";

        private static ILogger logger = Log.Logger;

        private IPEndPoint m_localEndPoint;
        private Guid m_socketId = Guid.NewGuid();
        private UdpClient m_udpClient;
        private bool m_closed;

        public Action<UDPListener, IPEndPoint, IPEndPoint, byte[]> PacketReceived;

        public UDPListener(IPEndPoint endPoint)
        {
            m_localEndPoint = endPoint;
            Initialise();
        }

        private void Initialise()
        {
            try
            {
                m_udpClient = new UdpClient(m_localEndPoint);

                Thread listenThread = new Thread(new ThreadStart(Listen));
                listenThread.Name = THREAD_NAME + Crypto.GetRandomString(4);
                listenThread.Start();

                logger.LogDebug("UDPListener listener created " + m_localEndPoint + ".");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UDPListener Initialise. " + excp.Message);
                throw excp;
            }
        }

        private void Dispose(bool disposing)
        {
            try
            {
                this.Close();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception Disposing UDPListener. " + excp.Message);
            }
        }

        private void Listen()
        {
            try
            {
                byte[] buffer = null;

                logger.LogDebug("UDPListener socket on " + m_localEndPoint + " listening started.");

                while (!m_closed)
                {
                    IPEndPoint inEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    try
                    {
                        buffer = m_udpClient.Receive(ref inEndPoint);
                    }
                    catch (SocketException)
                    {
                        // ToDo. Pretty sure these exceptions get thrown when an ICMP message comes back indicating there is no listening
                        // socket on the other end. It would be nice to be able to relate that back to the socket that the data was sent to
                        // so that we know to stop sending.
                        continue;
                    }
                    catch (Exception listenExcp)
                    {
                        // There is no point logging this as without processing the ICMP message it's not possible to know which socket the rejection came from.
                        logger.LogError("Exception listening on UDPListener. " + listenExcp.Message);

                        inEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        continue;
                    }

                    if (buffer == null || buffer.Length == 0)
                    {
                        // No need to care about zero byte packets.
                        //string remoteEndPoint = (inEndPoint != null) ? inEndPoint.ToString() : "could not determine";
                        //logger.LogError("Zero bytes received on SIPUDPChannel " + m_localSIPEndPoint.ToString() + ".");
                    }
                    else
                    {
                        if (PacketReceived != null)
                        {
                            PacketReceived(this, m_localEndPoint, inEndPoint, buffer);
                        }
                    }
                }

                logger.LogDebug("UDPListener socket on " + m_localEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UDPListener Listen. " + excp.Message);
                //throw excp;
            }
        }

        public void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer);
        }

        public void Send(IPEndPoint destinationEndPoint, byte[] buffer)
        {
            Send(destinationEndPoint, buffer, buffer.Length);
        }

        public void Send(IPEndPoint destinationEndPoint, byte[] buffer, int length)
        {
            try
            {
                if (destinationEndPoint == null)
                {
                    throw new ApplicationException("An empty destination was specified to Send in SIPUDPChannel.");
                }
                else
                {
                    if (m_udpClient != null && m_udpClient.Client != null)
                    {
                        m_udpClient.Send(buffer, length, destinationEndPoint);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception (" + excp.GetType().ToString() + ") UDPListener Send (sendto=>" + IPSocket.GetSocketString(destinationEndPoint) + "). " + excp.Message);
                throw excp;
            }
        }

        public void Close()
        {
            try
            {
                logger.LogDebug("Closing UDPListener " + m_localEndPoint + ".");

                m_closed = true;
                m_udpClient.Close();
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception UDPListener Close. " + excp.Message);
            }
        }
    }
}
