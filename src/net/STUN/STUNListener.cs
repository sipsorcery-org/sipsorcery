//-----------------------------------------------------------------------------
// Filename: STUNListener.cs
//
// Description: Creates the duplex sockets to listen for STUN client requests.
//
// Author(s):
// Aaron Clauson
//
// History:
// 27 Dec 2006	Aaron Clauson	Created  (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public delegate void STUNMessageReceived(IPEndPoint receivedEndPoint, IPEndPoint receivedOnEndPoint, byte[] buffer, int bufferLength);

    public class STUNListener
    {
        private const string STUN_LISTENER_THREAD_NAME = "stunlistener-";

        private static ILogger logger = Log.Logger;

        private IPEndPoint m_localEndPoint = null;
        private UdpClient m_stunConn = null;
        private bool m_closed = false;

        public event STUNMessageReceived MessageReceived;

        public IPEndPoint SIPChannelEndPoint
        {
            get { return m_localEndPoint; }
        }

        public STUNListener(IPEndPoint endPoint)
        {
            try
            {
                m_localEndPoint = InitialiseSockets(endPoint.Address, endPoint.Port);
                logger.LogInformation("STUNListener created " + endPoint.Address + ":" + endPoint.Port + ".");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception STUNListener (ctor). " + excp.Message);
                throw;
            }
        }

        public void Dispose(bool disposing)
        {
            try
            {
                this.Close();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception Disposing STUNListener. " + excp.Message);
            }
        }

        private IPEndPoint InitialiseSockets(IPAddress localIPAddress, int localPort)
        {
            try
            {
                IPEndPoint localEndPoint = null;
                UdpClient stunConn = null;

                localEndPoint = new IPEndPoint(localIPAddress, localPort);
                stunConn = new UdpClient(localEndPoint);

                m_stunConn = stunConn;

                Thread listenThread = new Thread(new ThreadStart(Listen));
                listenThread.Start();

                return localEndPoint;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception STUNListener InitialiseSockets. " + excp.Message);
                throw;
            }
        }

        private void Listen()
        {
            try
            {
                UdpClient stunConn = m_stunConn;

                IPEndPoint inEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = null;

                Thread.CurrentThread.Name = STUN_LISTENER_THREAD_NAME + inEndPoint.Port.ToString();

                while (!m_closed)
                {
                    try
                    {
                        buffer = stunConn.Receive(ref inEndPoint);
                    }
                    catch (Exception bufExcp)
                    {
                        logger.LogError("Exception listening in STUNListener. " + bufExcp.Message + ".");
                        inEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        continue;
                    }

                    if (buffer == null || buffer.Length == 0)
                    {
                        logger.LogError("Unable to read from STUNListener local end point " + m_localEndPoint.Address.ToString() + ":" + m_localEndPoint.Port);
                    }
                    else
                    {
                        if (MessageReceived != null)
                        {
                            try
                            {
                                MessageReceived(m_localEndPoint, inEndPoint, buffer, buffer.Length);
                            }
                            catch (Exception excp)
                            {
                                logger.LogError("Exception processing STUNListener MessageReceived. " + excp.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception STUNListener Listen. " + excp.Message);
                throw;
            }
        }

        public virtual void Send(IPEndPoint destinationEndPoint, byte[] buffer)
        {
            try
            {
                if (destinationEndPoint == null)
                {
                    logger.LogError("An empty destination was specified to Send in STUNListener.");
                }

                m_stunConn.Send(buffer, buffer.Length, destinationEndPoint);
            }
            catch (ObjectDisposedException)
            {
                logger.LogWarning("The STUNListener was not accessible when attempting to send a message to, " + IPSocket.GetSocketString(destinationEndPoint) + ".");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception (" + excp.GetType().ToString() + ") STUNListener Send (sendto=>" + IPSocket.GetSocketString(destinationEndPoint) + "). " + excp.Message);
                throw;
            }
        }

        public void Close()
        {
            try
            {
                logger.LogDebug("Closing STUNListener.");

                m_closed = true;
                m_stunConn.Close();
            }
            catch (Exception excp)
            {
                logger.LogWarning("Exception STUNListener Close. " + excp.Message);
            }
        }
    }
}
