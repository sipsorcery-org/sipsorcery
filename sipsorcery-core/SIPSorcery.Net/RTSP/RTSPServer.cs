//-----------------------------------------------------------------------------
// Filename: RTSPServer.cs
//
// Description: A very rudimentary implementation of a Real Time Streaming Protocol (RTSP),
// see RFC 2326 (http://www.ietf.org/rfc/rfc2326.txt), server channel. The implementation is not
// complete and only supports SETUP, PLAY and TEARDOWN commands.
// 
// History:
// 20 Jan 2014	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2014 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Net
{
    public class RTSPServer
    {
        private const string ACCEPT_THREAD_NAME = "rtspsvr-";
        private const string PRUNE_THREAD_NAME = "rtspsvrprune-";

        private const int INITIALPRUNE_CONNECTIONS_DELAY = 60000;   // Wait this long before starting the prune checks, there will be no connections to prune initially and the CPU is needed elsewhere.
        private const int PRUNE_CONNECTIONS_INTERVAL = 60000;        // The period at which to prune the connections.
        private const int PRUNE_NOTRANSMISSION_MINUTES = 70;         // The number of minutes after which if no transmissions are sent or received a connection will be pruned.
        private const int MAX_TCP_CONNECTIONS = 1000;               // Maximum number of connections for the TCP listener.
        private const int CONNECTION_ATTEMPTS_ALLOWED = 3;          // The number of failed connection attempts permitted before classifying a remote socket as failed.
        private const int FAILED_CONNECTION_DONTUSE_INTERVAL = 300; // If a socket cannot be connected to don't try and reconnect to it for this interval.

        private static int MaxMessageSize = RTSPConstants.RTSP_MAXIMUM_LENGTH;

        private static ILog logger = AppState.logger;

        private IPEndPoint m_localIPEndPoint;
        private TcpListener m_tcpServerListener;
        private Dictionary<string, RTSPConnection> m_connectedSockets = new Dictionary<string, RTSPConnection>();
        
        public bool Closed;

        public IPEndPoint LocalEndPoint
        {
            get { return m_localIPEndPoint; }
        }

        public Action<RTSPConnection, IPEndPoint, byte[]> RTSPClientMessageReceived;
        public Action<IPEndPoint> RTSPClientSocketDisconnected;

        public RTSPServer(IPEndPoint endPoint)
        {
            m_localIPEndPoint = endPoint;
            Initialise();
        }

        private void Initialise()
        {
            try
            {
                m_tcpServerListener = new TcpListener(m_localIPEndPoint);
                m_tcpServerListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                m_tcpServerListener.Start(MAX_TCP_CONNECTIONS);

                ThreadPool.QueueUserWorkItem(delegate { AcceptConnections(ACCEPT_THREAD_NAME + m_localIPEndPoint.Port); });
                ThreadPool.QueueUserWorkItem(delegate { PruneConnections(PRUNE_THREAD_NAME + m_localIPEndPoint.Port); });

                logger.Debug("RTSP server listener created " + m_localIPEndPoint + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPServer Initialise. " + excp.Message);
                throw excp;
            }
        }

        private void AcceptConnections(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                logger.Debug("RTSP server socket on " + m_localIPEndPoint + " accept connections thread started.");

                while (!Closed)
                {
                    try
                    {
                        TcpClient tcpClient = m_tcpServerListener.AcceptTcpClient();

                        tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                        IPEndPoint remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
                        logger.Debug("RTSP server accepted connection from " + remoteEndPoint + ".");

                        RTSPConnection rtspClientConnection = new RTSPConnection(this, tcpClient.GetStream(), remoteEndPoint);

                        lock (m_connectedSockets)
                        {
                            m_connectedSockets.Add(remoteEndPoint.ToString(), rtspClientConnection);
                        }

                        rtspClientConnection.RTSPSocketDisconnected += RTSPClientDisconnected;
                        rtspClientConnection.RTSPMessageReceived += RTSPMessageReceived;

                        rtspClientConnection.Stream.BeginRead(rtspClientConnection.SocketBuffer, 0, MaxMessageSize, new AsyncCallback(ReceiveCallback), rtspClientConnection);
                    }
                    catch (Exception acceptExcp)
                    {
                        // This exception gets thrown if the remote end disconnects during the socket accept.
                        logger.Warn("Exception RTSPServer  accepting socket (" + acceptExcp.GetType() + "). " + acceptExcp.Message);
                    }
                }

                logger.Debug("RTSP server socket on " + m_localIPEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPServer Listen. " + excp.Message);
            }
        }

        public void ReceiveCallback(IAsyncResult ar)
        {
            RTSPConnection rtspConnection = (RTSPConnection)ar.AsyncState;

            try
            {
                int bytesRead = rtspConnection.Stream.EndRead(ar);
                if (rtspConnection.SocketReadCompleted(bytesRead))
                {
                    rtspConnection.Stream.BeginRead(rtspConnection.SocketBuffer, rtspConnection.SocketBufferEndPosition, MaxMessageSize - rtspConnection.SocketBufferEndPosition, new AsyncCallback(ReceiveCallback), rtspConnection);
                }
            }
            catch (SocketException)  // Occurs if the remote end gets disconnected.
            { }
            catch (Exception excp)
            {
                logger.Warn("Exception RTSPServer ReceiveCallback. " + excp.Message);
                RTSPClientSocketDisconnected(rtspConnection.RemoteEndPoint);
            }
        }

        public bool IsConnectionEstablished(IPEndPoint remoteEndPoint)
        {
            lock (m_connectedSockets)
            {
                return m_connectedSockets.ContainsKey(remoteEndPoint.ToString());
            }
        }

        private void RTSPClientDisconnected(IPEndPoint remoteEndPoint)
        {
            try
            {
                logger.Debug("RTSP client socket from " + remoteEndPoint + " disconnected.");

                lock (m_connectedSockets)
                {
                    if (m_connectedSockets.ContainsKey(remoteEndPoint.ToString()))
                    {
                        m_connectedSockets.Remove(remoteEndPoint.ToString());
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPClientDisconnected. " + excp.Message);
            }
        }

        private void RTSPMessageReceived(RTSPConnection rtspConnection, IPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (RTSPClientMessageReceived != null)
            {
                RTSPClientMessageReceived(rtspConnection, remoteEndPoint, buffer);
            }
        }

        public void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer);
        }

        public void Send(IPEndPoint dstEndPoint, byte[] buffer)
        {
            try
            {
                if (buffer == null)
                {
                    throw new ApplicationException("An empty buffer was specified to Send in SIPTCPChannel.");
                }
                else if (m_localIPEndPoint.ToString() == dstEndPoint.ToString())
                {
                    logger.Error("The RTSPServer blocked Send to " + dstEndPoint.ToString() + " as it was identified as a locally hosted TCP socket.\r\n" + Encoding.UTF8.GetString(buffer));
                    throw new ApplicationException("A Send call was made in RTSPServer to send to another local TCP socket.");
                }
                else
                {
                    // Lookup a client socket that is connected to the destination.
                    if (m_connectedSockets.ContainsKey(dstEndPoint.ToString()))
                    {
                        RTSPConnection rtspClientConnection = m_connectedSockets[dstEndPoint.ToString()];

                        try
                        {
                            rtspClientConnection.Stream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(EndSend), rtspClientConnection);
                            rtspClientConnection.LastTransmission = DateTime.Now;
                        }
                        catch (SocketException)
                        {
                            logger.Warn("RTSPServer could not send to TCP socket " + dstEndPoint + ", closing and removing.");
                            rtspClientConnection.Stream.Close();
                            m_connectedSockets.Remove(dstEndPoint.ToString());
                        }
                    }
                    else
                    {
                        logger.Warn("Could not send RTSP packet to TCP " + dstEndPoint + " as there was no current connection to the client, dropping message.");
                    }
                }
            }
            catch (ApplicationException appExcp)
            {
                logger.Warn("ApplicationException RTSPServer Send (sendto=>" + dstEndPoint + "). " + appExcp.Message);
                throw;
            }
            catch (Exception excp)
            {
                logger.Error("Exception (" + excp.GetType().ToString() + ") RTSPServer Send (sendto=>" + dstEndPoint + "). " + excp.Message);
                throw;
            }
        }

        private void EndSend(IAsyncResult ar)
        {
            try
            {
                RTSPConnection rtspConnection = (RTSPConnection)ar.AsyncState;
                rtspConnection.Stream.EndWrite(ar);
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPServer EndSend. " + excp.Message);
            }
        }

        public void Close()
        {
            logger.Debug("Closing RTSP server socket " + m_localIPEndPoint + ".");

            Closed = true;

            try
            {
                m_tcpServerListener.Stop();
            }
            catch (Exception listenerCloseExcp)
            {
                logger.Warn("Exception RTSPServer Close (shutting down listener). " + listenerCloseExcp.Message);
            }

            foreach (RTSPConnection rtspConnection in m_connectedSockets.Values)
            {
                try
                {
                    rtspConnection.Stream.Close();
                }
                catch (Exception connectionCloseExcp)
                {
                    logger.Warn("Exception RTSPServer Close (shutting down connection to " + rtspConnection.RemoteEndPoint + "). " + connectionCloseExcp.Message);
                }
            }
        }

        /// <summary>
        /// Periodically checks the established connections and closes any that have not had a transmission for a specified 
        /// period or where the number of connections allowed per IP address has been exceeded. 
        /// </summary>
        protected void PruneConnections(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                Thread.Sleep(INITIALPRUNE_CONNECTIONS_DELAY);

                while (!Closed)
                {
                    bool checkComplete = false;

                    while (!checkComplete)
                    {
                        try
                        {
                            RTSPConnection inactiveConnection = null;

                            lock (m_connectedSockets)
                            {
                                var inactiveConnectionKey = (from connection in m_connectedSockets
                                                             where connection.Value.LastTransmission < DateTime.Now.AddMinutes(PRUNE_NOTRANSMISSION_MINUTES * -1)
                                                             select connection.Key).FirstOrDefault();

                                if (inactiveConnectionKey != null)
                                {
                                    inactiveConnection = m_connectedSockets[inactiveConnectionKey];
                                    m_connectedSockets.Remove(inactiveConnectionKey);
                                }
                            }

                            if (inactiveConnection != null)
                            {
                                logger.Debug("Pruning inactive RTSP connection on to remote end point " + inactiveConnection.RemoteEndPoint.ToString() + ".");
                                inactiveConnection.Close();
                            }
                            else
                            {
                                checkComplete = true;
                            }
                        }
                        catch (SocketException)
                        {
                            // Will be thrown if the socket is already closed.
                        }
                        catch (Exception pruneExcp)
                        {
                            logger.Error("Exception RTSPServer PruneConnections (pruning). " + pruneExcp.Message);
                            checkComplete = true;
                        }
                    }

                    Thread.Sleep(PRUNE_CONNECTIONS_INTERVAL);
                    checkComplete = false;
                }

                logger.Debug("RTSPServer socket on " + m_localIPEndPoint.ToString() + " pruning connections halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTSPServer PruneConnections. " + excp.Message);
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
                logger.Error("Exception Disposing RTSPServer. " + excp.Message);
            }
        }
    }
}
