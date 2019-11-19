//-----------------------------------------------------------------------------
// Filename: SIPTCPChannel.cs
//
// Description: SIP transport for TCP.
//
// Author(s):
// Aaron Clauson
//
// History:
// 19 Apr 2008	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
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
// Additional background information on TIME_WAIT:
// https://tools.ietf.org/html/draft-faber-time-wait-avoidance-00: RFC for mechanism to avoid TIME_WAIT state on busy web servers.
// http://www.serverframework.com/asynchronousevents/2011/01/time-wait-and-its-design-implications-for-protocols-and-scalable-servers.html:
// Explanation of TIME_WAIT state purpose.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPTCPChannel : SIPChannel
    {
        private const int MAX_TCP_CONNECTIONS = 1000;               // Maximum number of connections for the TCP listener.
        private const int INITIALPRUNE_CONNECTIONS_DELAY = 60000;   // Wait this long before starting the prune checks, there will be no connections to prune initially and the CPU is needed elsewhere.
        private const int PRUNE_CONNECTIONS_INTERVAL = 60000;        // The period at which to prune the connections.
        private const int PRUNE_NOTRANSMISSION_MINUTES = 70;         // The number of minutes after which if no transmissions are sent or received a connection will be pruned.

        protected TcpListener m_tcpServerListener;
        protected List<string> m_connectingSockets = new List<string>();  // List of sockets that are in the process of being connected to. Need to avoid SIP re-transmits initiating multiple connect attempts.

        virtual protected string ProtDescr { get; } = "TCP";

        // Can be set to allow TCP channels hosted in the same process to send to each other. Useful for testing.
        // By default sends between TCP channels in the same process are disabled to prevent resource exhaustion.
        public bool DisableLocalTCPSocketsCheck;

        private static List<string> m_localTCPSockets = new List<string>(); // Keeps a list of TCP sockets this process is listening on to prevent it establishing TCP connections to itself.

        /// <summary>
        /// Maintains a list of all current TCP connections currently connected to/from this channel. This allows the SIP transport
        /// layer to quickly find a channel where the same connection must be re-used.
        /// </summary>
        private ConcurrentDictionary<string, SIPStreamConnection> m_connections = new ConcurrentDictionary<string, SIPStreamConnection>();

        /// <summary>
        /// Creates a SIP channel to listen for and send SIP messages over TCP.
        /// </summary>
        /// <param name="endPoint">The IP end point to listen on and send from.</param>
        /// <param name="protocol">Whether the channel is being used with TCP or TLS (TLS channels get upgraded once connected).</param>
        public SIPTCPChannel(IPEndPoint endPoint, SIPProtocolsEnum protocol)
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException("endPoint", "The end point must be specified when creating a SIPTCPChannel.");
            }

            ID = Crypto.GetRandomInt(CHANNEL_ID_LENGTH).ToString();
            ListeningIPAddress = endPoint.Address;
            Port = endPoint.Port;
            SIPProtocol = protocol;
            IsReliable = true;
            Initialise(endPoint);
        }

        public SIPTCPChannel(IPEndPoint endPoint)
            : this(endPoint, SIPProtocolsEnum.tcp)
        { }

        public SIPTCPChannel(IPAddress listenAddress, int listenPort)
            : this(new IPEndPoint(listenAddress, listenPort), SIPProtocolsEnum.tcp)
        { }

        /// <summary>
        /// Initialises the SIP channel's socket listener.
        /// </summary>
        private void Initialise(IPEndPoint listenEndPoint)
        {
            try
            {
                m_tcpServerListener = new TcpListener(listenEndPoint);
                m_tcpServerListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                m_tcpServerListener.Server.LingerState = new LingerOption(true, 0);
                if (listenEndPoint.AddressFamily == AddressFamily.InterNetworkV6) m_tcpServerListener.Server.DualMode = true;
                m_tcpServerListener.Start(MAX_TCP_CONNECTIONS);

                if (listenEndPoint.Port == 0)
                {
                    listenEndPoint.Port = (m_tcpServerListener.Server.LocalEndPoint as IPEndPoint).Port;
                }

                m_localTCPSockets.Add(listenEndPoint.ToString());

                Task.Run(AcceptConnections);
                Task.Run(PruneConnections);

                logger.LogInformation($"SIP {ProtDescr} Channel created for {listenEndPoint}.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTCPChannel Initialise. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Processes the socket accepts from the channel's socket listener.
        /// </summary>
        private async void AcceptConnections()
        {
            logger.LogDebug($"SIP {ProtDescr} Channel socket on {m_tcpServerListener.Server.LocalEndPoint} accept connections thread started.");

            while (!Closed)
            {
                try
                {
                    Socket clientSocket = m_tcpServerListener.AcceptSocket();
                    logger.LogDebug($"SIP {ProtDescr} Channel connection accepted from {clientSocket.RemoteEndPoint} by {clientSocket.LocalEndPoint}.");

                    if (!Closed)
                    {
                        clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        clientSocket.LingerState = new LingerOption(true, 0);

                        SIPStreamConnection sipStmConn = new SIPStreamConnection(clientSocket, clientSocket.RemoteEndPoint as IPEndPoint, SIPProtocol);
                        sipStmConn.SIPMessageReceived += SIPTCPMessageReceived;

                        m_connections.TryAdd(sipStmConn.ConnectionID, sipStmConn);

                        await OnAccept(sipStmConn);
                    }
                }
                catch (SocketException acceptSockExcp) when (acceptSockExcp.SocketErrorCode == SocketError.Interrupted)
                {
                    // This is a result of the transport channel being closed and WSACancelBlockingCall being called in WinSock2. Safe to ignore.
                    logger.LogDebug($"SIPTCPChannel accepts for {ListeningIPAddress}:{Port} cancelled.");
                }
                catch (Exception acceptExcp)
                {
                    // This exception gets thrown if the remote end disconnects during the socket accept.
                    logger.LogWarning("Exception SIPTCPChannel accepting socket (" + acceptExcp.GetType() + "). " + acceptExcp.Message);
                }
            }
        }

        /// <summary>
        /// For TCP channel no special action is required when accepting a new client connection. Can start receiving immeidately.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly accepted client socket.</param>
        protected virtual Task OnAccept(SIPStreamConnection streamConnection)
        {
            SocketAsyncEventArgs args = streamConnection.RecvSocketArgs;
            args.AcceptSocket = streamConnection.StreamSocket;
            args.UserToken = streamConnection;
            args.Completed += IO_Completed;

            bool willRaise = streamConnection.StreamSocket.ReceiveAsync(args);
            if (!willRaise)
            {
                ProcessReceive(args);
            }

            return Task.FromResult(0);
        }

        /// <summary>
        /// Event handler for the socket newer SendAsync and ReceiveAsync socket calls.
        /// </summary>
        /// <param name="sender">The socket that the IO event occurred on.</param>
        /// <param name="e">The socket args for the completed IO operation.</param>
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
                case SocketAsyncOperation.Disconnect:
                    var sipStreamConn = m_connections.Where(x => x.Value.RemoteEndPoint.Equals(e.RemoteEndPoint as IPEndPoint)).FirstOrDefault().Value;
                    OnSIPStreamDisconnected(sipStreamConn, e.SocketError);
                    break;
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        /// <summary>
        /// Receive event handler for the newer ReceiveAsync socket call.
        /// </summary>
        /// <param name="e">The socket args for the completed receive operation.</param>
        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            SIPStreamConnection streamConn = (SIPStreamConnection)e.UserToken;

            try
            {
                if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
                {
                    byte[] buffer = streamConn.RecvSocketArgs.Buffer;
                    streamConn.ExtractSIPMessages(this, buffer, e.BytesTransferred);

                    streamConn.RecvSocketArgs.SetBuffer(buffer, streamConn.RecvEndPosn, buffer.Length - streamConn.RecvEndPosn);

                    bool willRaiseEvent = streamConn.StreamSocket.ReceiveAsync(e);
                    if (!willRaiseEvent)
                    {
                        ProcessReceive(e);
                    }
                }
                else
                {
                    OnSIPStreamDisconnected(streamConn, e.SocketError);
                }
            }
            catch (SocketException sockExcp)
            {
                OnSIPStreamDisconnected(streamConn, sockExcp.SocketErrorCode);
            }
            catch (Exception excp)
            {
                // There was an error processing the last message received. Remove the disconnected socket.
                logger.LogError($"Exception processing SIP stream receive on read from {e.RemoteEndPoint} closing connection. {excp.Message}");
                OnSIPStreamDisconnected(streamConn, SocketError.Fault);
            }
        }

        /// <summary>
        /// Send event handler for the newer SendAsync socket call.
        /// </summary>
        /// <param name="e">The socket args for the completed send operation.</param>
        private void ProcessSend(SocketAsyncEventArgs e)
        {
            SIPStreamConnection streamConn = (SIPStreamConnection)e.UserToken;

            if (e.BytesTransferred == 0 || e.SocketError != SocketError.Success)
            {
                // There was an error processing the last message send. Remove the disconnected socket.
                logger.LogWarning($"SIPTCPChannel Socket send to {e.RemoteEndPoint} failed with socket error {e.SocketError}, removing connection.");
                OnSIPStreamDisconnected(streamConn, e.SocketError);
            }
        }

        /// <summary>
        /// Attempts to create a client TCP socket connection to a remote end point.
        /// </summary>
        /// <param name="dstEndPoint">The remote TCP end point to attempt to connect to.</param>
        /// <param name="buffer">An optional buffer that if set can contain data to transmit immediately after connecting.</param>
        /// <returns>If successful a connected client socket or null if not.</returns>
        public async Task<SocketError> ConnectClientAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            // No existing TCP connection to the destination. Attempt a new socket connection.
            IPAddress localAddress = ListeningIPAddress;
            if (ListeningIPAddress == IPAddress.Any)
            {
                localAddress = NetServices.GetLocalAddressForRemote(dstEndPoint.Address);
            }
            IPEndPoint localEndPoint = new IPEndPoint(localAddress, Port);

            logger.LogDebug($"ConnectAsync SIP TCP Channel local end point of {localEndPoint} selected for connection to {dstEndPoint}.");

            Socket clientSocket = new Socket(dstEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            clientSocket.LingerState = new LingerOption(true, 0);
            clientSocket.Bind(localEndPoint);

            SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs
            {
                AcceptSocket = clientSocket,
                RemoteEndPoint = dstEndPoint
            };

            // If this is a TCP channel can take a shortcut and set the first send payload on the connect args.
            if (buffer != null && buffer.Length > 0 && serverCertificateName == null)
            {
                connectArgs.SetBuffer(buffer, 0, buffer.Length);
            }

            // Attempt to connect.
            TaskCompletionSource<SocketError> connectTcs = new TaskCompletionSource<SocketError>();
            connectArgs.Completed += (sender, sockArgs) => { if (sockArgs.LastOperation == SocketAsyncOperation.Connect) connectTcs.SetResult(sockArgs.SocketError); };
            bool willRaiseEvent = clientSocket.ConnectAsync(connectArgs);
            if (!willRaiseEvent) if (connectArgs.LastOperation == SocketAsyncOperation.Connect) connectTcs.SetResult(connectArgs.SocketError);

            var connectResult = await connectTcs.Task;

            logger.LogDebug($"ConnectAsync SIP TCP Channel connect completed result for {localEndPoint}->{dstEndPoint} {connectResult}.");

            if (connectResult != SocketError.Success)
            {
                logger.LogWarning($"SIP TCP Channel send to {dstEndPoint} failed. Attempt to create a client socket failed.");
            }
            else
            {
                SIPStreamConnection sipStmConn = new SIPStreamConnection(clientSocket, clientSocket.RemoteEndPoint as IPEndPoint, SIPProtocol);
                sipStmConn.SIPMessageReceived += SIPTCPMessageReceived;

                m_connections.TryAdd(sipStmConn.ConnectionID, sipStmConn);

                OnClientConnect(sipStmConn, buffer, serverCertificateName);
            }

            return connectResult;
        }

        /// <summary>
        /// For TCP channel no special action is required when a new outgoing client connection is established. 
        /// Can start receiving immeidately.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly connected client socket.</param>
        /// <param name="buffer">Optional parameter that contains the data that still needs to be sent once the connection is established.</param>
        protected virtual void OnClientConnect(SIPStreamConnection streamConnection, byte[] buffer, string certificateName)
        {
            SocketAsyncEventArgs recvArgs = streamConnection.RecvSocketArgs;
            recvArgs.AcceptSocket = streamConnection.StreamSocket;
            recvArgs.UserToken = streamConnection;
            recvArgs.Completed += IO_Completed;

            bool willRaise = streamConnection.StreamSocket.ReceiveAsync(recvArgs);
            if (!willRaise)
            {
                ProcessReceive(recvArgs);
            }
        }

        /// <summary>
        /// Attempts to send data to the remote end point over a reliable TCP connection.
        /// </summary>
        /// <param name="dstEndPoint">The remote end point to send to.</param>
        /// <param name="message">The data to send.</param>
        public override async void Send(IPEndPoint destinationEndPoint, string message)
        {
            byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
            await SendAsync(destinationEndPoint, messageBuffer, null);
        }

        /// <summary>
        /// Attempts to send data to the remote end point over a reliable TCP connection.
        /// </summary>
        /// <param name="dstEndPoint">The remote end point to send to.</param>
        /// <param name="buffer">The data to send.</param>
        public override async void Send(IPEndPoint dstEndPoint, byte[] buffer)
        {
            await SendAsync(dstEndPoint, buffer, null);
        }

        public override async void Send(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            await SendAsync(dstEndPoint, buffer, serverCertificateName);
        }

        public override async Task<SocketError> SendAsync(IPEndPoint dstEndPoint, byte[] buffer)
        {
            if (dstEndPoint == null)
            {
                throw new ArgumentException("dstEndPoint", "An empty destination was specified to Send in SIPTCPChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in SIPTCPChannel.");
            }

            try
            {
                return await SendAsync(dstEndPoint, buffer, null);
            }
            catch (SocketException sockExcp)
            {
                return sockExcp.SocketErrorCode;
            }
        }

        /// <summary>
        /// Attempts to send data to the remote end point over a reliable connection. If an existing
        /// connection exists it will be used otherwise an attempt will be made to establish a new connection.
        /// </summary>
        /// <param name="dstEndPoint">The remote end point to send the reliable data to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <param name="serverCertificateName">Optional. Only relevant for SSL streams. The common name
        /// that is expected for the remote SSL server.</param>
        public override async Task<SocketError> SendAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            try
            {
                if (buffer == null || buffer.Length == 0)
                {
                    throw new ApplicationException("An empty buffer was specified to Send in SIPTCPChannel.");
                }
                else if (DisableLocalTCPSocketsCheck == false && m_localTCPSockets.Contains(dstEndPoint.ToString()))
                {
                    logger.LogWarning($"SIPTCPChannel blocked Send to {dstEndPoint} as it was identified as a locally hosted TCP socket.\r\n" + Encoding.UTF8.GetString(buffer));
                    throw new ApplicationException("A Send call was blocked in SIPTCPChannel due to the destination being another local TCP socket.");
                }
                else
                {
                    // Lookup a client socket that is connected to the destination. If it does not exist attempt to connect a new one.
                    if (HasConnection(dstEndPoint))
                    {
                        var sipStreamConn = m_connections.Where(x => x.Value.RemoteEndPoint.Equals(dstEndPoint)).First().Value;
                        SendOnConnected(sipStreamConn, buffer);
                        return SocketError.Success;
                    }
                    else
                    {
                        return await ConnectClientAsync(dstEndPoint, buffer, serverCertificateName);
                    }
                }
            }
            catch (SocketException sockExcp)
            {
                return sockExcp.SocketErrorCode;
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

        /// <summary>
        /// Sends a SIP message asynchronously on a specific stream connection.
        /// </summary>
        /// <param name="connectionID">The ID of the specific TCP connection that the message must be sent on.</param>
        /// <param name="buffer">The data to send.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public override Task<SocketError> SendAsync(string connectionID, byte[] buffer)
        {
            if (String.IsNullOrEmpty(connectionID))
            {
                throw new ArgumentException("connectionID", "An empty connection ID was specified for a Send in SIPTCPChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in SIPTCPChannel.");
            }

            try
            {
                SIPStreamConnection sipStreamConn = null;
                m_connections.TryGetValue(connectionID, out sipStreamConn);

                if (sipStreamConn != null)
                {
                    SendOnConnected(sipStreamConn, buffer);
                    return Task.FromResult(SocketError.Success);
                }
                else
                {
                    return Task.FromResult(SocketError.ConnectionReset);
                }
            }
            catch (SocketException sockExcp)
            {
                return Task.FromResult(sockExcp.SocketErrorCode);
            }
        }

        /// <summary>
        /// Sends data once the stream is connected.
        /// Can be overridden in sub classes that need to implement a different mechanism to send. For example SSL connections.
        /// </summary>
        /// <param name="sipStreamConn">The connected SIP stream wrapping the TCP connection.</param>
        /// <param name="buffer">The data to send.</param>
        protected virtual void SendOnConnected(SIPStreamConnection sipStreamConn, byte[] buffer)
        {
            IPEndPoint dstEndPoint = sipStreamConn.RemoteEndPoint;

            try
            {
                lock (sipStreamConn.StreamSocket)
                {
                    sipStreamConn.LastTransmission = DateTime.Now;
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
                logger.LogWarning($"SocketException SIPTCPChannel SendOnConnected {dstEndPoint}. ErrorCode {sockExcp.SocketErrorCode}. {sockExcp}");
                OnSIPStreamDisconnected(sipStreamConn, sockExcp.SocketErrorCode);
                throw;
            }
        }

        /// <summary>
        /// Event handler for a reliable SIP stream socket being disconnected.
        /// </summary>
        /// <param name="connection">The disconnected stream.</param>
        /// <param name="socketError">The cause of the disconnect.</param>
        protected void OnSIPStreamDisconnected(SIPStreamConnection connection, SocketError socketError)
        {
            try
            {
                if (connection != null)
                {
                    logger.LogWarning($"SIP stream disconnected {SIPProtocol}:{connection.RemoteEndPoint} {socketError}.");

                    if (m_connections.TryRemove(connection.ConnectionID, out _))
                    {
                        var socket = connection.StreamSocket;

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
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception OnSIPStreamDisconnected. " + excp.Message);
            }
        }

        /// <summary>
        /// Gets fired when a suspected SIP message is extracted from the TCP data stream.
        /// </summary>
        protected void SIPTCPMessageReceived(SIPChannel channel, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            SIPMessageReceived?.Invoke(channel, localEndPoint, remoteEndPoint, buffer);
        }

        /// <summary>
        /// Checks whether the SIP channel has a connection matching a unique connection ID.
        /// </summary>
        /// <param name="connectionID">The connection ID to check for a match on.</param>
        /// <returns>True if a match is found or false if not.</returns>
        public override bool HasConnection(string connectionID)
        {
            return m_connections.ContainsKey(connectionID);
        }

        /// <summary>
        /// Checks whether there is an existing connection for a remote end point. Existing connections include
        /// connections that have been accepted by this channel's listener and connections that have been initiated
        /// due to sends from this channel.
        /// </summary>
        /// <param name="remoteEndPoint">The remote end point to check for an existing connection.</param>
        /// <returns>True if there is a connection or false if not.</returns>
        public override bool HasConnection(IPEndPoint remoteEndPoint)
        {
            return m_connections.Any(x => x.Value.RemoteEndPoint.Equals(remoteEndPoint));
        }

        /// <summary>
        /// Closes the channel and any open sockets.
        /// </summary>
        public override void Close()
        {
            if (!Closed == true)
            {
                logger.LogDebug($"Closing SIP {ProtDescr} Channel {ListeningIPAddress}:{Port}.");

                Closed = true;

                lock (m_connections)
                {
                    foreach (SIPStreamConnection streamConnection in m_connections.Values)
                    {
                        if (streamConnection.StreamSocket != null)
                        {
                            // See explanation in OnSIPStreamSocketDisconnected on why the close is done on the socket and NOT the stream.
                            streamConnection.StreamSocket.Close();
                        }
                    }
                    m_connections.Clear();
                }

                try
                {
                    m_tcpServerListener.Stop();
                }
                catch (Exception excp)
                {
                    logger.LogWarning($"Exception SIPTCPChannel Close (shutting down listener). {excp.Message}");
                }
            }
        }

        public override void Dispose()
        {
            this.Close();
        }

        /// <summary>
        /// Periodically checks the established connections and closes any that have not had a transmission for a specified 
        /// period or where the number of connections allowed per IP address has been exceeded. Only relevant for connection
        /// oriented channels such as TCP and TLS.
        /// </summary>
        private async void PruneConnections()
        {
            try
            {
                await Task.Delay(INITIALPRUNE_CONNECTIONS_DELAY);

                while (!Closed)
                {
                    bool checkComplete = false;

                    while (!checkComplete)
                    {
                        try
                        {
                            SIPStreamConnection inactiveConnection = null;

                            lock (m_connections)
                            {
                                var inactiveConnectionKey = (from connection in m_connections
                                                             where connection.Value.LastTransmission < DateTime.Now.AddMinutes(PRUNE_NOTRANSMISSION_MINUTES * -1)
                                                             select connection.Key).FirstOrDefault();

                                if (inactiveConnectionKey != null)
                                {
                                    inactiveConnection = m_connections[inactiveConnectionKey];
                                    m_connections.TryRemove(inactiveConnectionKey, out _);
                                }
                            }

                            if (inactiveConnection != null)
                            {
                                logger.LogDebug($"Pruning inactive connection on {ListeningIPAddress}:{Port} to remote end point {inactiveConnection.RemoteEndPoint}.");
                                inactiveConnection.StreamSocket.Close();
                            }
                            else
                            {
                                checkComplete = true;
                            }
                        }
                        catch (SocketException sockExcp)
                        {
                            // Will be thrown if the socket is already closed.
                            logger.LogWarning($"Socket error in PruneConnections. {sockExcp.Message} ({sockExcp.ErrorCode}).");
                        }
                        catch (Exception pruneExcp)
                        {
                            logger.LogError("Exception PruneConnections (pruning). " + pruneExcp.Message);
                            checkComplete = true;
                        }
                    }

                    await Task.Delay(PRUNE_CONNECTIONS_INTERVAL);
                    checkComplete = false;
                }

                logger.LogDebug($"SIPChannel socket on {ListeningIPAddress}:{Port} pruning connections halted.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPChannel PruneConnections. " + excp.Message);
            }
        }
    }
}
