//-----------------------------------------------------------------------------
// Filename: SIPTCPChannel.cs
//
// Description: SIP transport for TCP.
// 
// History:
// 19 Apr 2008	Aaron Clauson	Created.
// 16 Oct 2019  Aaron Clauson   Added IPv6 support.
// 24 Oct 2019  Aaron Clauson   Major refactor to avoid TIME_WAIT state on connection close.
//
// Notes:
// See https://stackoverflow.com/questions/58506815/how-to-apply-linger-option-with-winsock2/58511052#58511052 for
// a discussion about the TIME_WAIT and the Linger option. It's very relevant for this class which potentially
// needs to close a TCP connection and then quickly re-establish it. For example if there is a SIP parsing 
// error and the end of a SIP message cannot be determined the only reliable way to recover is to close and 
// re-establish the TCP connection.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPTCPChannel : SIPChannel
    {
        private const string ACCEPT_THREAD_NAME = "siptcp-";
        private const string PRUNE_THREAD_NAME = "siptcpprune-";

        private const int MAX_TCP_CONNECTIONS = 1000;               // Maximum number of connections for the TCP listener.
        private const int CONNECTION_ATTEMPTS_ALLOWED = 3;          // The number of failed connection attempts permitted before classifying a remote socket as failed.
        private const int FAILED_CONNECTION_DONTUSE_INTERVAL = 300; // If a socket cannot be connected to don't try and reconnect to it for this interval.
        //private const int CLIENT_CONNECT_TIMEOUT = 10;              // Number of seconds after which to timeout a client connection attempt.

        protected TcpListener m_tcpServerListener;
        protected Dictionary<string, SIPStreamConnection> m_connectedSockets = new Dictionary<string, SIPStreamConnection>();
        protected List<string> m_connectingSockets = new List<string>();                                  // List of sockets that are in the process of being connected to. Need to avoid SIP re-transmits initiating multiple connect attempts.
        protected Dictionary<string, int> m_connectionFailureStrikes = new Dictionary<string, int>();     // Tracks the number of connection attempts made to a remote socket, three strikes and it's out.
        protected Dictionary<string, DateTime> m_connectionFailures = new Dictionary<string, DateTime>(); // Tracks sockets that have had a connection failure on them to avoid endless re-connect attmepts.

        // Can be set to allow TCP channels hosted in the same process to send to each other. Useful for testing.
        // By default sends between TCP channels in the same process are disabled to prevent resource exhaustion.
        public bool DisableLocalTCPSocketsCheck;

        public SIPTCPChannel(IPEndPoint endPoint, SIPProtocolsEnum protocol)
        {
            m_localSIPEndPoint = new SIPEndPoint(protocol, endPoint);
            m_isReliable = true;
            Initialise();
        }

        public SIPTCPChannel(IPEndPoint endPoint)
            : this(endPoint, SIPProtocolsEnum.tcp)
        { }

        public SIPTCPChannel(IPAddress listenAddress, int listenPort)
            : this(new IPEndPoint(listenAddress, listenPort), SIPProtocolsEnum.tcp)
        { }

        private void Initialise()
        {
            try
            {
                IPEndPoint listenEndPoint = m_localSIPEndPoint.GetIPEndPoint();

                m_tcpServerListener = new TcpListener(listenEndPoint);
                m_tcpServerListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                m_tcpServerListener.Server.LingerState = new LingerOption(true, 0);
                m_tcpServerListener.Start(MAX_TCP_CONNECTIONS);

                if (m_localSIPEndPoint.Port == 0)
                {
                    m_localSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.tcp, listenEndPoint);
                }

                LocalTCPSockets.Add(listenEndPoint.ToString());

                ThreadPool.QueueUserWorkItem(delegate { AcceptConnections(ACCEPT_THREAD_NAME + m_localSIPEndPoint.Port); });
                ThreadPool.QueueUserWorkItem(delegate { PruneConnections(PRUNE_THREAD_NAME + m_localSIPEndPoint.Port); });

                logger.LogDebug("SIP TCP Channel listener created " + m_localSIPEndPoint.GetIPEndPoint() + ".");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTCPChannel Initialise. " + excp.Message);
                throw excp;
            }
        }

        private void AcceptConnections(string threadName)
        {
            Thread.CurrentThread.Name = threadName;

            logger.LogDebug("SIPTCPChannel socket on " + m_localSIPEndPoint + " accept connections thread started.");

            while (!Closed)
            {
                try
                {
                    Socket clientSocket = m_tcpServerListener.AcceptSocket();
                    logger.LogDebug($"SIP TCP Channel connection accepted from {clientSocket.RemoteEndPoint}.");

                    if (!Closed)
                    {
                        clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        clientSocket.LingerState = new LingerOption(true, 0);

                        SIPConnection sipTCPConnection = new SIPConnection(this, clientSocket.RemoteEndPoint as IPEndPoint, m_localSIPEndPoint.Protocol, SIPConnectionsEnum.Listener);
                        sipTCPConnection.SIPMessageReceived += SIPTCPMessageReceived;
                        SIPStreamConnection streamConnection = new SIPStreamConnection(clientSocket, sipTCPConnection);

                        lock (m_connectedSockets)
                        {
                            m_connectedSockets.Add(clientSocket.RemoteEndPoint.ToString(), streamConnection);
                        }

                        OnAccept(streamConnection);
                    }
                }
                catch (SocketException acceptSockExcp) when (acceptSockExcp.SocketErrorCode == SocketError.Interrupted)
                {
                    // This is a result of the transport channel being closed and WSACancelBlockingCall being called in WinSock2. Safe to ignore.
                    logger.LogDebug($"SIPTCPChannel accepts for {m_localSIPEndPoint.ToString()} cancelled.");
                }
                catch (Exception acceptExcp)
                {
                    // This exception gets thrown if the remote end disconnects during the socket accept.
                    logger.LogWarning("Exception SIPTCPChannel accepting socket (" + acceptExcp.GetType() + "). " + acceptExcp.Message);
                }
            }

            logger.LogDebug("SIPTCPChannel socket on " + m_localSIPEndPoint + " client accepts halted.");
        }

        /// <summary>
        /// For TCP channel no special action is required when accepting a new client connection. Can start receiving immeidately.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly accepted client socket.</param>
        protected virtual void OnAccept(SIPStreamConnection streamConnection)
        {
            SocketAsyncEventArgs args = streamConnection.ConnectionProps.RecvSocketArgs;
            args.AcceptSocket = streamConnection.StreamSocket;
            args.UserToken = streamConnection;
            args.Completed += IO_Completed;

            bool willRaise = streamConnection.StreamSocket.ReceiveAsync(args);
            if (!willRaise)
            {
                ProcessReceive(args);
            }
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    ProcessReceive(e);
                    break;
                case SocketAsyncOperation.Send:
                    ProcessSend(e);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            Console.WriteLine($"ProcessReceive Socket result {e.SocketError}, bytes read {e.BytesTransferred}.");

            SIPStreamConnection streamConn = (SIPStreamConnection)e.UserToken;

            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                byte[] buffer = streamConn.ConnectionProps.RecvSocketArgs.Buffer;

                if (streamConn.ConnectionProps.SocketReadCompleted(e.BytesTransferred, buffer))
                {
                    streamConn.ConnectionProps.RecvSocketArgs.SetBuffer(buffer, streamConn.ConnectionProps.RecvEndPosition, buffer.Length - streamConn.ConnectionProps.RecvEndPosition);

                    bool willRaiseEvent = streamConn.StreamSocket.ReceiveAsync(e);
                    if (!willRaiseEvent)
                    {
                        ProcessReceive(e);
                    }
                }
                else
                {
                    // There was an error processing the last message received. Disconnect the client.
                    logger.LogWarning($"SIPTCPChannel Socket read from {e.RemoteEndPoint} failed, closing connection.");
                    SIPTCPSocketDisconnected(e.RemoteEndPoint as IPEndPoint);
                }
            }
            else
            {
                SIPTCPSocketDisconnected(streamConn.ConnectionProps.RemoteEndPoint);
            }
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            Console.WriteLine($"ProcessSend Socket result {e.SocketError}, bytes sent {e.BytesTransferred}.");
        }

        /// <summary>
        /// Attempts to create a client TCP socket connection to a remote end point.
        /// </summary>
        /// <param name="dstEndPoint">The remote TCP end point to attempt to connect to.</param>
        /// <param name="buffer">An optional buffer that if set can contain data to transmit immediately after connecting.</param>
        /// <returns>IF successful a connected client socket or null if not.</returns>
        public async Task ConnectClientAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            // No existing TCP connection to the destination. Attempt a new socket connection.
            IPEndPoint localEndPoint = m_localSIPEndPoint.GetIPEndPoint();

            Socket clientSocket = new Socket(dstEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            clientSocket.LingerState = new LingerOption(true, 0);
            clientSocket.Bind(localEndPoint);

            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs
            {
                AcceptSocket = clientSocket,
                RemoteEndPoint = dstEndPoint
            };

            if (buffer != null && buffer.Length > 0 && serverCertificateName == null)
            {
                // If this is a TCP channel can take a shortcut and set the first send payload on the connect args.
                connectArgs.SetBuffer(buffer, 0, buffer.Length);
            }

            TaskCompletionSource<Socket> connectTcs = new TaskCompletionSource<Socket>();

            connectArgs.Completed += (sender, sockArgs) =>
            {
                if (sockArgs.LastOperation == SocketAsyncOperation.Connect)
                {
                    logger.LogDebug($"Completed SIP TCP Channel connect completed result for {localEndPoint}->{dstEndPoint} {sockArgs.SocketError}.");
                    bool connectResult = connectArgs.SocketError == SocketError.Success;
                    connectTcs.SetResult(clientSocket);
                }
                else
                {
                    logger.LogDebug($"Completed last operation {sockArgs.LastOperation}.");
                }
            };

            bool willRaiseEvent = clientSocket.ConnectAsync(connectArgs);
            if (!willRaiseEvent)
            {
                if (connectArgs.LastOperation == SocketAsyncOperation.Connect)
                {
                    logger.LogDebug($"ConnectAsync SIP TCP Channel connect completed result for {localEndPoint}->{dstEndPoint} {connectArgs.SocketError}.");
                    bool connectResult = connectArgs.SocketError == SocketError.Success;
                    connectTcs.SetResult(clientSocket);
                }
                else
                {
                    logger.LogDebug($"ConnectAsync last operation {connectArgs.LastOperation}.");
                }
            }

            var connectedSocket = await connectTcs.Task;

            if (connectedSocket == null)
            {
                logger.LogWarning($"SIP TCP Channel sent to {dstEndPoint} failed. Attempt to create a client socket failed.");
                lock (m_connectionFailures)
                {
                    m_connectionFailures.Add(dstEndPoint.ToString(), DateTime.Now);
                }
            }
            else
            {
                SIPConnection sipTCPConnection = new SIPConnection(this, connectedSocket.RemoteEndPoint as IPEndPoint, m_localSIPEndPoint.Protocol, SIPConnectionsEnum.Caller);
                sipTCPConnection.SIPMessageReceived += SIPTCPMessageReceived;
                SIPStreamConnection streamConnection = new SIPStreamConnection(connectedSocket, sipTCPConnection);

                lock (m_connectedSockets)
                {
                    m_connectedSockets.Add(connectedSocket.RemoteEndPoint.ToString(), streamConnection);
                }

                OnClientConnect(streamConnection, serverCertificateName, buffer);
            }
        }

        /// <summary>
        /// For TCP channel no special action is required when a new outgoing client connection is established. 
        /// Can start receiving immeidately.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly connected client socket.</param>
        /// <param name="buffer">Optional parameter that contains the data that still needs to be sent once the connection is established.</param>
        protected virtual void OnClientConnect(SIPStreamConnection streamConnection, string certificateName, byte[] buffer)
        {
            SocketAsyncEventArgs recvArgs = streamConnection.ConnectionProps.RecvSocketArgs;
            recvArgs.AcceptSocket = streamConnection.StreamSocket;
            recvArgs.UserToken = streamConnection;
            recvArgs.Completed += IO_Completed;

            bool willRaise = streamConnection.StreamSocket.ReceiveAsync(recvArgs);
            if (!willRaise)
            {
                ProcessReceive(recvArgs);
            }
        }

        public override void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            Send(destinationEndPoint, messageBuffer, null);
        }

        public override void Send(IPEndPoint dstEndPoint, byte[] buffer)
        {
            Send(dstEndPoint, buffer, null);
        }

        public override async void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            try
            {
                if (buffer == null || buffer.Length == 0)
                {
                    throw new ApplicationException("An empty buffer was specified to Send in SIPTCPChannel.");
                }
                else if (DisableLocalTCPSocketsCheck == false && LocalTCPSockets.Contains(dstEndPoint.ToString()))
                {
                    logger.LogWarning($"SIPTCPChannel blocked Send to {dstEndPoint} as it was identified as a locally hosted TCP socket.\r\n" + Encoding.UTF8.GetString(buffer));
                    throw new ApplicationException("A Send call was made in SIPTCPChannel to send to another local TCP socket.");
                }
                else if (m_connectionFailures.ContainsKey(dstEndPoint.ToString()))
                {
                    throw new ApplicationException($"SIP TCP channel connect attempt to {dstEndPoint} failed.");
                }
                else
                {
                    SIPStreamConnection sipStreamConn = null;

                    // Lookup a client socket that is connected to the destination. If it does not exist attempt to connect a new one.
                    if (m_connectedSockets.ContainsKey(dstEndPoint.ToString()))
                    {
                        sipStreamConn = m_connectedSockets[dstEndPoint.ToString()];
                        DoSend(sipStreamConn, buffer);
                    }
                    else
                    {
                        await ConnectClientAsync(dstEndPoint, buffer, serverCertificateName);
                    }
                }
            }
            catch (ApplicationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception (" + excp.GetType().ToString() + ") SIPTCPChannel Send (sendto=>" + dstEndPoint + "). " + excp.Message);
                throw;
            }
        }

        protected virtual void DoSend(SIPStreamConnection sipStreamConn, byte[] buffer)
        {
            IPEndPoint dstEndPoint = sipStreamConn.ConnectionProps.RemoteEndPoint;

            try
            {
                lock (sipStreamConn.StreamSocket)
                {
                    sipStreamConn.ConnectionProps.LastTransmission = DateTime.Now;
                    var args = new SocketAsyncEventArgs();
                    args.UserToken = sipStreamConn;
                    args.SetBuffer(buffer, 0, buffer.Length);
                    args.Completed += IO_Completed;
                    if (!sipStreamConn.StreamSocket.SendAsync(args))
                    {
                        ProcessSend(args);
                    }
                }
            }
            catch (SocketException sockExcp)
            {
                logger.LogWarning($"SocketException SIP TCP Channel sending to {dstEndPoint}. ErrorCode {sockExcp.SocketErrorCode}. {sockExcp}");
                SIPTCPSocketDisconnected(dstEndPoint);
                throw;
            }
        }

        protected void SIPTCPSocketDisconnected(IPEndPoint remoteEndPoint, bool remove = true)
        {
            try
            {
                logger.LogDebug($"Closing and removing entry for TCP socket {remoteEndPoint}.");

                lock (m_connectedSockets)
                {
                    if (m_connectedSockets.ContainsKey(remoteEndPoint.ToString()))
                    {
                        var socket = m_connectedSockets[remoteEndPoint.ToString()].StreamSocket;

                        // Important: Due to the way TCP works the end of the connection that initiates the close
                        // is meant to go into a TIME_WAIT state. On Windows that results in the same pair of sockets
                        // being unable to reconnect for 30s. SIP can deal with stray and duplicate messages at the 
                        // appliction layer so the TIME_WAIT is not that useful. While not useful it is also a major annoyance
                        // as if a connection is dropped for whatever reason, such as a parser error or inactivity, it will
                        // prevent the connection being re-established.
                        //
                        // For this reason this implementation uses a hard RST close for client initiated socket closes. This
                        // results in a TCP RST packet instead of the graceful FIN-ACK sequence. Two things are necessary with
                        // WinSock2 to force the hard RST:
                        //
                        // - the Linger option must be set on the raw socket before binding as Linger option {1, 0}.
                        // - the close method must be called on teh socket without shutting down the stream.

                        socket.Close();

                        if (remove == true)
                        {
                            m_connectedSockets.Remove(remoteEndPoint.ToString());
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTCPSocketDisconnected. " + excp.Message);
            }
        }

        /// <summary>
        /// Gets fired when a suspected SIP message is extracted from the TCP data stream.
        /// </summary>
        protected void SIPTCPMessageReceived(SIPChannel channel, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            if (m_connectionFailures.ContainsKey(remoteEndPoint.GetIPEndPoint().ToString()))
            {
                m_connectionFailures.Remove(remoteEndPoint.GetIPEndPoint().ToString());
            }

            if (m_connectionFailureStrikes.ContainsKey(remoteEndPoint.GetIPEndPoint().ToString()))
            {
                m_connectionFailureStrikes.Remove(remoteEndPoint.GetIPEndPoint().ToString());
            }

            SIPMessageReceived?.Invoke(channel, remoteEndPoint, buffer);
        }

        public override void Close()
        {
            if (!Closed == true)
            {
                logger.LogDebug("Closing SIP TCP Channel " + SIPChannelEndPoint + ".");

                Closed = true;

                lock (m_connectedSockets)
                {
                    foreach (SIPStreamConnection tcpConnection in m_connectedSockets.Values)
                    {
                        SIPTCPSocketDisconnected(tcpConnection.ConnectionProps.RemoteEndPoint, false);
                    }
                    m_connectedSockets.Clear();
                }

                try
                {
                    m_tcpServerListener.Stop();
                }
                catch (Exception listenerCloseExcp)
                {
                    logger.LogWarning("Exception SIPTCPChannel Close (shutting down listener). " + listenerCloseExcp.Message);
                }
            }
        }

        public override void Dispose()
        {
            this.Close();
        }

        public override bool IsConnectionEstablished(IPEndPoint remoteEndPoint)
        {
            lock (m_connectedSockets)
            {
                return m_connectedSockets.ContainsKey(remoteEndPoint.ToString());
            }
        }

        protected override Dictionary<string, SIPStreamConnection> GetConnectionsList()
        {
            return m_connectedSockets;
        }
    }
}
