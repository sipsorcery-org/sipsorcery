//-----------------------------------------------------------------------------
// Filename: SIPTCPChannel.cs
//
// Description: SIP transport for TCP.
// 
// History:
// 19 Apr 2008	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    public class SIPTCPChannel : SIPChannel
    {
        private const string ACCEPT_THREAD_NAME = "siptcp-";
        private const string PRUNE_THREAD_NAME = "siptcpprune-";

        private const int MAX_TCP_CONNECTIONS = 1000;               // Maximum number of connections for the TCP listener.
        //private const int MAX_TCP_CONNECTIONS_PER_IPADDRESS = 10;   // Maximum number of connections allowed for a single remote IP address.
        private const int CONNECTION_ATTEMPTS_ALLOWED = 3;          // The number of failed connection attempts permitted before classifying a remote socket as failed.
        private const int FAILED_CONNECTION_DONTUSE_INTERVAL = 300; // If a socket cannot be connected to don't try and reconnect to it for this interval.

        private static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH;
        
        private TcpListener m_tcpServerListener;
        private Dictionary<string, SIPConnection> m_connectedSockets = new Dictionary<string, SIPConnection>();
        private List<string> m_connectingSockets = new List<string>();                                  // List of sockets that are in the process of being connected to. Need to avoid SIP re-transmits initiating multiple connect attempts.
        private Dictionary<string, int> m_connectionFailureStrikes = new Dictionary<string, int>();     // Tracks the number of connection attempts made to a remote socket, three strikes and it's out.
        private Dictionary<string, DateTime> m_connectionFailures = new Dictionary<string, DateTime>(); // Tracks sockets that have had a connection failure on them to avoid endless re-connect attmepts.
        private static object m_writeLock = new object();

        public SIPTCPChannel(IPEndPoint endPoint)
        {
            m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, endPoint);
            m_isReliable = true;
            Initialise();
        }

        private void Initialise()
        {
            try
            {
                m_tcpServerListener = new TcpListener(m_localSIPEndPoint.GetIPEndPoint());
                m_tcpServerListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                m_tcpServerListener.Start(MAX_TCP_CONNECTIONS);

                if (m_localSIPEndPoint.Port == 0)
                {
                    m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, (IPEndPoint)m_tcpServerListener.Server.LocalEndPoint);
                }

                LocalTCPSockets.Add(((IPEndPoint)m_tcpServerListener.Server.LocalEndPoint).ToString());

                ThreadPool.QueueUserWorkItem(delegate { AcceptConnections(ACCEPT_THREAD_NAME + m_localSIPEndPoint.Port); });
                ThreadPool.QueueUserWorkItem(delegate { PruneConnections(PRUNE_THREAD_NAME + m_localSIPEndPoint.Port); });

                logger.Debug("SIP TCP Channel listener created " + m_localSIPEndPoint.GetIPEndPoint() + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTCPChannel Initialise. " + excp.Message);
                throw excp;
            }
        }

        private void AcceptConnections(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                logger.Debug("SIPTCPChannel socket on " + m_localSIPEndPoint + " accept connections thread started.");

                while (!Closed)
                {
                    try
                    {
                        TcpClient tcpClient = m_tcpServerListener.AcceptTcpClient();

                        if (!Closed)
                        {
                            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            tcpClient.LingerState = new LingerOption(false, 0);
                            //clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                            //IPEndPoint remoteEndPoint = (IPEndPoint)clientSocket.RemoteEndPoint;
                            IPEndPoint remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
                            logger.Debug("SIP TCP Channel connection accepted from " + remoteEndPoint + ".");

                            //SIPTCPConnection sipTCPClient = new SIPTCPConnection(this, clientSocket, remoteEndPoint, SIPTCPConnectionsEnum.Listener);
                            SIPConnection sipTCPConnection = new SIPConnection(this, tcpClient, tcpClient.GetStream(), remoteEndPoint, SIPProtocolsEnum.tcp, SIPConnectionsEnum.Listener);
                            //SIPConnection sipTCPClient = new SIPConnection(this, tcpClient.Client, remoteEndPoint, SIPProtocolsEnum.tcp, SIPConnectionsEnum.Listener);

                            lock (m_connectedSockets)
                            {
                                m_connectedSockets.Add(remoteEndPoint.ToString(), sipTCPConnection);
                            }

                            sipTCPConnection.SIPSocketDisconnected += SIPTCPSocketDisconnected;
                            sipTCPConnection.SIPMessageReceived += SIPTCPMessageReceived;
                            // clientSocket.BeginReceive(sipTCPClient.SocketBuffer, 0, SIPTCPConnection.MaxSIPTCPMessageSize, SocketFlags.None, new AsyncCallback(sipTCPClient.ReceiveCallback), null);
                            //byte[] receiveBuffer = new byte[MaxSIPTCPMessageSize];
                            sipTCPConnection.SIPStream.BeginRead(sipTCPConnection.SocketBuffer, 0, MaxSIPTCPMessageSize, new AsyncCallback(ReceiveCallback), sipTCPConnection);
                        }
                    }
                    catch (Exception acceptExcp)
                    {
                        // This exception gets thrown if the remote end disconnects during the socket accept.
                        logger.Warn("Exception SIPTCPChannel  accepting socket (" + acceptExcp.GetType() + "). " + acceptExcp.Message);
                    }
                }

                logger.Debug("SIPTCPChannel socket on " + m_localSIPEndPoint + " listening halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTCPChannel Listen. " + excp.Message);
                //throw excp;
            }
        }

        public void ReceiveCallback(IAsyncResult ar)
        {
            SIPConnection sipTCPConnection = (SIPConnection)ar.AsyncState;

            try
            {
                int bytesRead = sipTCPConnection.SIPStream.EndRead(ar);
                if (sipTCPConnection.SocketReadCompleted(bytesRead))
                {
                    sipTCPConnection.SIPStream.BeginRead(sipTCPConnection.SocketBuffer, sipTCPConnection.SocketBufferEndPosition, MaxSIPTCPMessageSize - sipTCPConnection.SocketBufferEndPosition, new AsyncCallback(ReceiveCallback), sipTCPConnection);
                }
            }
            catch (SocketException)  // Occurs if the remote end gets disconnected.
            { }
            catch (Exception excp)
            {
                logger.Warn("Exception SIPTCPChannel ReceiveCallback. " + excp.Message);
                SIPTCPSocketDisconnected(sipTCPConnection.RemoteEndPoint);
            }
        }

        public override bool IsConnectionEstablished(IPEndPoint remoteEndPoint)
        {
            lock (m_connectedSockets)
            {
                return m_connectedSockets.ContainsKey(remoteEndPoint.ToString());
            }
        }

        protected override Dictionary<string, SIPConnection> GetConnectionsList()
        {
            return m_connectedSockets;
        }

        private void SIPTCPSocketDisconnected(IPEndPoint remoteEndPoint)
        {
            try
            {
                logger.Debug("TCP socket from " + remoteEndPoint + " disconnected.");

                lock (m_connectedSockets)
                {
                    if(m_connectedSockets.ContainsKey(remoteEndPoint.ToString()))
                    {
                        m_connectedSockets.Remove(remoteEndPoint.ToString());
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTCPClientDisconnected. " + excp.Message);
            }
        }

        private void SIPTCPMessageReceived(SIPChannel channel, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (m_connectionFailures.ContainsKey(remoteEndPoint.GetIPEndPoint().ToString()))
            {
                m_connectionFailures.Remove(remoteEndPoint.GetIPEndPoint().ToString());
            }

            if (m_connectionFailureStrikes.ContainsKey(remoteEndPoint.GetIPEndPoint().ToString()))
            {
                m_connectionFailureStrikes.Remove(remoteEndPoint.GetIPEndPoint().ToString());
            }

            if (SIPMessageReceived != null)
            {
                SIPMessageReceived(channel, remoteEndPoint, buffer);
            }
        }

        public override void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer);
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer)
        {
            try
            {
                if (buffer == null)
                {
                    throw new ApplicationException("An empty buffer was specified to Send in SIPTCPChannel.");
                }
                else if (LocalTCPSockets.Contains(dstEndPoint.ToString()))
                {
                    logger.Error("SIPTCPChannel blocked Send to " + dstEndPoint.ToString() + " as it was identified as a locally hosted TCP socket.\r\n" + Encoding.UTF8.GetString(buffer));
                    throw new ApplicationException("A Send call was made in SIPTCPChannel to send to another local TCP socket.");
                }
                else
                {
                    bool sent = false;

                    // Lookup a client socket that is connected to the destination.
                    //m_sipConn(buffer, buffer.Length, destinationEndPoint);
                    if (m_connectedSockets.ContainsKey(dstEndPoint.ToString()))
                    {
                        SIPConnection sipTCPClient = m_connectedSockets[dstEndPoint.ToString()];

                        try
                        {
                            lock (m_writeLock)
                            {
                                //logger.Warn("TCP channel BeginWrite from " + SIPChannelEndPoint.ToString() + " to " + sipTCPClient.RemoteEndPoint + ": " + Encoding.ASCII.GetString(buffer, 0, 32) + ".");
                                sipTCPClient.SIPStream.BeginWrite(buffer, 0, buffer.Length, new AsyncCallback(EndSend), sipTCPClient);
                                //logger.Warn("TCP channel BeginWrite complete from " + SIPChannelEndPoint.ToString() + " to " + sipTCPClient.RemoteEndPoint + ".");
                                //sipTCPClient.SIPStream.Flush();
                                sent = true;
                                sipTCPClient.LastTransmission = DateTime.Now;
                            }
                        }
                        catch (SocketException)
                        {
                            logger.Warn("Could not send to TCP socket " + dstEndPoint + ", closing and removing.");
                            sipTCPClient.SIPStream.Close();
                            m_connectedSockets.Remove(dstEndPoint.ToString());
                        }
                    }

                    if (!sent)
                    {
                        if (m_connectionFailures.ContainsKey(dstEndPoint.ToString()) && m_connectionFailures[dstEndPoint.ToString()] < DateTime.Now.AddSeconds(FAILED_CONNECTION_DONTUSE_INTERVAL * -1))
                        {
                            m_connectionFailures.Remove(dstEndPoint.ToString());
                        }

                        if (m_connectionFailures.ContainsKey(dstEndPoint.ToString()))
                        {
                            throw new ApplicationException("TCP connection attempt to " + dstEndPoint.ToString() + " was not attempted, too many failures.");
                        }
                        else if (!m_connectingSockets.Contains(dstEndPoint.ToString()))
                        {
                            logger.Debug("Attempting to establish TCP connection to " + dstEndPoint + ".");

                            TcpClient tcpClient = new TcpClient();
                            tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            tcpClient.Client.Bind(m_localSIPEndPoint.GetIPEndPoint());

                            m_connectingSockets.Add(dstEndPoint.ToString());
                            tcpClient.BeginConnect(dstEndPoint.Address, dstEndPoint.Port, EndConnect, new object[] { tcpClient, dstEndPoint, buffer });
                        }
                        else
                        {
                            //logger.Warn("Could not send SIP packet to TCP " + dstEndPoint + " and another connection was already in progress so dropping message.");
                        }
                    }
                }
            }
            catch (ApplicationException appExcp)
            {
                logger.Warn("ApplicationException SIPTCPChannel Send (sendto=>" + dstEndPoint + "). " + appExcp.Message);
                throw;
            }
            catch (Exception excp)
            {
                logger.Error("Exception (" + excp.GetType().ToString() + ") SIPTCPChannel Send (sendto=>" + dstEndPoint + "). " + excp.Message);
                throw;
            }
        }

        private void EndSend(IAsyncResult ar)
        {
            try
            {
                SIPConnection sipTCPConnection = (SIPConnection)ar.AsyncState;
                sipTCPConnection.SIPStream.EndWrite(ar);
                OnSendComplete(EventArgs.Empty);

                //logger.Debug("EndSend on TCP " + SIPChannelEndPoint.ToString() + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception EndSend. " + excp.Message);
            }
        }
        protected override void OnSendComplete(EventArgs args)
        {
            base.OnSendComplete(args);
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            throw new ApplicationException("This Send method is not available in the SIP TCP channel, please use an alternative overload.");
        }

        private void EndConnect(IAsyncResult ar)
        {
            bool connected = false;
            IPEndPoint dstEndPoint = null;

            try
            {
                object[] stateObj = (object[])ar.AsyncState;
                TcpClient tcpClient = (TcpClient)stateObj[0];
                dstEndPoint = (IPEndPoint)stateObj[1];
                byte[] buffer = (byte[])stateObj[2];

                m_connectingSockets.Remove(dstEndPoint.ToString());

                tcpClient.EndConnect(ar);

                if (tcpClient != null && tcpClient.Connected)
                {
                    logger.Debug("Established TCP connection to " + dstEndPoint + ".");
                    connected = true;

                    m_connectionFailureStrikes.Remove(dstEndPoint.ToString());
                    m_connectionFailures.Remove(dstEndPoint.ToString());

                    SIPConnection callerConnection = new SIPConnection(this, tcpClient, tcpClient.GetStream(), dstEndPoint, SIPProtocolsEnum.tcp, SIPConnectionsEnum.Caller);
                    m_connectedSockets.Add(dstEndPoint.ToString(), callerConnection);

                    callerConnection.SIPSocketDisconnected += SIPTCPSocketDisconnected;
                    callerConnection.SIPMessageReceived += SIPTCPMessageReceived;
                    //byte[] receiveBuffer = new byte[MaxSIPTCPMessageSize];
                    callerConnection.SIPStream.BeginRead(callerConnection.SocketBuffer, 0, MaxSIPTCPMessageSize, new AsyncCallback(ReceiveCallback), callerConnection);
                    callerConnection.SIPStream.BeginWrite(buffer, 0, buffer.Length, EndSend, callerConnection);
                }
                else
                {
                    logger.Warn("Could not establish TCP connection to " + dstEndPoint + ".");
                }
            }
            catch (SocketException sockExcp)
            {
                logger.Warn("SocketException SIPTCPChannel EndConnect. " + sockExcp.Message);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTCPChannel EndConnect (" + excp.GetType() + "). " + excp.Message);
            }
            finally
            {
                if (!connected && dstEndPoint != null)
                {
                    if (m_connectionFailureStrikes.ContainsKey(dstEndPoint.ToString()))
                    {
                        m_connectionFailureStrikes[dstEndPoint.ToString()] = m_connectionFailureStrikes[dstEndPoint.ToString()] + 1;
                    }
                    else
                    {
                        m_connectionFailureStrikes.Add(dstEndPoint.ToString(), 1);
                    }

                    if (m_connectionFailureStrikes[dstEndPoint.ToString()] >= CONNECTION_ATTEMPTS_ALLOWED)
                    {
                        if (!m_connectionFailures.ContainsKey(dstEndPoint.ToString()))
                        {
                            m_connectionFailures.Add(dstEndPoint.ToString(), DateTime.Now);
                        }

                        m_connectionFailureStrikes.Remove(dstEndPoint.ToString());
                    }
                }
            }
        }

        public override void Close()
        {
            if (!Closed == true)
            {
                logger.Debug("Closing SIP TCP Channel " + SIPChannelEndPoint + ".");

                Closed = true;

                try
                {
                    m_tcpServerListener.Stop();
                }
                catch (Exception listenerCloseExcp)
                {
                    logger.Warn("Exception SIPTCPChannel Close (shutting down listener). " + listenerCloseExcp.Message);
                }

                lock (m_connectedSockets)
                {
                    foreach (SIPConnection tcpConnection in m_connectedSockets.Values)
                    {
                        try
                        {
                            tcpConnection.Close();
                        }
                        catch (Exception connectionCloseExcp)
                        {
                            logger.Warn("Exception SIPTCPChannel Close (shutting down connection to " + tcpConnection.RemoteEndPoint ?? "?" + "). " + connectionCloseExcp.Message);
                        }
                    }
                }
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
                logger.Error("Exception Disposing SIPTCPChannel. " + excp.Message);
            }
        }
    }
}
