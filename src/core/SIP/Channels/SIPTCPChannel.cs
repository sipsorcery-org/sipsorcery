//-----------------------------------------------------------------------------
// Filename: SIPTCPChannel.cs
//
// Description: SIP transport for TCP. Note this is also the base class for the
// SIPTLSChannel. For the TLS channel the TCP base class will accept or connect and
// then switch to the TLS class to upgrade to an SSL stream.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 19 Apr 2008	Aaron Clauson	Created, Hobart, Australia.
// 16 Oct 2019  Aaron Clauson   Added IPv6 support.
// 24 Oct 2019  Aaron Clauson   Major refactor to avoid TIME_WAIT state on connection close.
// 19 Nov 2019  Aaron Clauson   Enhanced to deal with listening on IPAddress.Any.
// 02 Feb 2021  Aaron Clauson   Added socket dual mode support.
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
// Asynchronous Sockets:
// The async socket mechanism used in this class is the "new high performance" approach described at
// https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.socketasynceventargs?view=netframework-4.8#remarks
// While that sounds nice the main motivation for this class was simply to switch to an async method
// that did not require the BeginReceive & EndReceive callbacks or the "Asynchronous Programming Model" (APM)
// see https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/. A "Task-based Asynchronous Pattern"
// would have been preferred but only for consistency with the rest of the code base and .NET libraries.
// Also note that the SIPUDPChannel is using the APM BeginReceiveMessageFrom/EndReceiveMessageFrom approach. The motivation
// for that decision is that it's the only one of the UDP socket receives methods that provides access to the received on
// IP address when listening on IPAddress.Any.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// SIP transport for TCP. Note this is also the base class for the SIPTLSChannel.
    /// For the TLS channel the TCP base class will accept or connect and then switch 
    /// to the TLS class to upgrade to an SSL stream.
    /// </summary>
    public class SIPTCPChannel : SIPChannel
    {
        private const int MAX_TCP_CONNECTIONS = 1000;          // Maximum number of connections for the TCP listener.
        private const int PRUNE_CONNECTIONS_INTERVAL = 60000;  // The period at which to prune the connections.
        private const int PRUNE_NOTRANSMISSION_MINUTES = 70;   // The number of minutes after which if no transmissions are sent or received a connection will be pruned.
        private const int TCP_ATTEMPT_CONNECT_TIMEOUT = 5000;

        /// <summary>
        /// This is the main object managed by this class. It is the socket listening for incoming connections.
        /// </summary>
        protected Socket m_channelSocket;

        /// <summary>
        /// List of sockets that are in the process of being connected to. 
        /// Needed to avoid SIP re-transmits initiating multiple connect attempts.
        /// </summary>
        protected List<string> m_connectingSockets = new List<string>();

        /// <summary>
        /// This string is used in debug messages. It makes it possible to differentiate
        /// whether an instance in acting solely as a TCP channel or as the base class of a TLS channel.
        /// </summary>
        virtual protected string ProtDescr { get; } = "TCP";

        /// <summary>
        /// Can be set to allow TCP channels hosted in the same process to send to each other. Useful for testing.
        /// By default sends between TCP channels in the same process are disabled to prevent resource exhaustion.
        /// </summary>
        public bool DisableLocalTCPSocketsCheck;

        /// <summary>
        /// Maintains a list of all current TCP connections currently connected to/from this channel. This allows the SIP transport
        /// layer to quickly find a channel where the same connection must be re-used.
        /// </summary>
        private ConcurrentDictionary<string, SIPStreamConnection> m_connections = new ConcurrentDictionary<string, SIPStreamConnection>();

        private CancellationTokenSource m_cts = new CancellationTokenSource();
        private bool m_isDualMode;

        /// <summary>
        /// Creates a SIP channel to listen for and send SIP messages over TCP.
        /// </summary>
        /// <param name="endPoint">The IP end point to send from and optionally listen on.</param>
        /// <param name="protocol">Whether the channel is being used with TCP or TLS (TLS channels get upgraded once connected).</param>
        /// <param name="canListen">Indicates whether the channel is capable of listening for new client connections.
        /// A TLS channel without a certificate cannot listen.</param>
        public SIPTCPChannel(IPEndPoint endPoint, SIPProtocolsEnum protocol, bool canListen = true, bool useDualMode = false) : base()
        {
            if (endPoint == null)
            {
                throw new ArgumentNullException("endPoint", "The end point must be specified when creating a SIPTCPChannel.");
            }

            SIPProtocol = protocol;
            IsReliable = true;

            m_channelSocket = new Socket(endPoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            m_channelSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            m_channelSocket.LingerState = new LingerOption(true, 0);

            if (useDualMode && endPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                m_isDualMode = true;
                m_channelSocket.DualMode = true;
            }

            m_channelSocket.Bind(endPoint);

            Port = (m_channelSocket.LocalEndPoint as IPEndPoint).Port;
            ListeningIPAddress = (m_channelSocket.LocalEndPoint as IPEndPoint).Address;

            logger.LogInformation($"SIP {ProtDescr} Channel created for {ListeningSIPEndPoint}.");

            if (canListen)
            {
                m_channelSocket.Listen(MAX_TCP_CONNECTIONS);

                Task.Factory.StartNew(AcceptConnections, TaskCreationOptions.LongRunning);
                Task.Factory.StartNew(PruneConnections, TaskCreationOptions.LongRunning);
            }
        }

        public SIPTCPChannel(IPEndPoint endPoint, bool useDualMode = false)
            : this(endPoint, SIPProtocolsEnum.tcp, true, useDualMode)
        { }

        public SIPTCPChannel(IPAddress listenAddress, int listenPort, bool useDualMode = false)
            : this(new IPEndPoint(listenAddress, listenPort), SIPProtocolsEnum.tcp, true, useDualMode)
        { }

        /// <summary>
        /// Processes the socket accepts from the channel's socket listener.
        /// </summary>
        private void AcceptConnections()
        {
            logger.LogDebug($"SIP {ProtDescr} Channel socket on {ListeningSIPEndPoint} accept connections thread started.");

            while (!Closed)
            {
                try
                {
                    Socket clientSocket = m_channelSocket.Accept();

                    if (!Closed)
                    {
                        var remoteEndPoint = new SIPEndPoint(SIPProtocol, clientSocket.RemoteEndPoint as IPEndPoint);

                        logger.LogDebug($"SIP {ProtDescr} Channel connection accepted from {remoteEndPoint} by {ListeningSIPEndPoint}.");

                        clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        clientSocket.LingerState = new LingerOption(true, 0);

                        SIPStreamConnection sipStmConn = new SIPStreamConnection(clientSocket, remoteEndPoint, SIPProtocol);
                        sipStmConn.SIPMessageReceived += SIPTCPMessageReceived;

                        m_connections.TryAdd(sipStmConn.ConnectionID, sipStmConn);

                        OnAccept(sipStmConn);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // This is a result of the transport channel being closed. Safe to ignore.
                    //logger.LogDebug($"SIP {ProtDescr} Channel accepts for {ListeningEndPoint} cancelled.");
                }
                catch (SocketException acceptSockExcp) when (acceptSockExcp.SocketErrorCode == SocketError.Interrupted)
                {
                    // This is a result of the transport channel being closed and WSACancelBlockingCall being called in WinSock2. Safe to ignore.
                    //logger.LogDebug($"SIP {ProtDescr} Channel accepts for {ListeningEndPoint} cancelled.");
                }
                catch (System.AggregateException)
                {
                    // This is a result of the transport channel being closed. Safe to ignore.
                }
                catch (Exception acceptExcp)
                {
                    // This exception gets thrown if the remote end disconnects during the socket accept.
                    logger.LogWarning(acceptExcp, $"Exception SIP {ProtDescr} Channel accepting socket ({acceptExcp.GetType()}). {acceptExcp.Message}");
                }
            }
        }

        /// <summary>
        /// For TCP channel no special action is required when accepting a new client connection. Can start receiving immediately.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly accepted client socket.</param>
        protected virtual void OnAccept(SIPStreamConnection streamConnection)
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
                    var sipStreamConn = m_connections.Where(x => x.Value.RemoteSIPEndPoint.GetIPEndPoint().Equals(e.RemoteEndPoint as IPEndPoint)).FirstOrDefault().Value;
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
                logger.LogError($"Exception processing SIP {ProtDescr} stream receive on read from {e.RemoteEndPoint} closing connection. {excp.Message}");
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
                logger.LogWarning($"SIP {ProtDescr} Channel Socket send to {e.RemoteEndPoint} failed with socket error {e.SocketError}, removing connection.");
                OnSIPStreamDisconnected(streamConn, e.SocketError);
            }
        }

        /// <summary>
        /// Attempts to create a client TCP socket connection to a remote end point.
        /// </summary>
        /// <param name="dstEndPoint">The remote TCP end point to attempt to connect to.</param>
        /// <param name="buffer">An optional buffer that if set can contain data to transmit immediately after connecting.</param>
        /// <returns>If successful a connected client socket or null if not.</returns>
        internal async Task<SocketError> ConnectClientAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName)
        {
            try
            {
                // Map IPv4 to IPv6 for dual mode socket sends.
                if(dstEndPoint.AddressFamily == AddressFamily.InterNetwork && m_isDualMode)
                {
                    dstEndPoint = new IPEndPoint(dstEndPoint.Address.MapToIPv6(), dstEndPoint.Port);
                }

                logger.LogDebug($"ConnectClientAsync SIP {ProtDescr} Channel local end point of {ListeningSIPEndPoint} selected for connection to {dstEndPoint}.");

                Socket clientSocket = new Socket(ListeningIPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                clientSocket.LingerState = new LingerOption(true, 0);

                if (m_isDualMode && ListeningIPAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    clientSocket.DualMode = true;
                }

                clientSocket.Bind(ListeningEndPoint);

                SocketAsyncEventArgs connectArgs = new SocketAsyncEventArgs
                {
                    RemoteEndPoint = dstEndPoint
                };

                // NOTE: The approach of setting the buffer on the connect args worked properly on Windows BUT
                // not so on Linux. Since it's such a tiny saving skip setting the buffer on the connect and 
                // do the send once the sockets are connected (use SendOnConnected).
                // If this is a TCP channel can take a shortcut and set the first send payload on the connect args.
                //if (buffer != null && buffer.Length > 0 && serverCertificateName == null)
                //{
                //    connectArgs.SetBuffer(buffer, 0, buffer.Length);
                //}

                logger.LogDebug($"Attempting TCP connection from {ListeningSIPEndPoint} to {dstEndPoint}.");

                // Attempt to connect.
                TaskCompletionSource<SocketError> connectTcs = new TaskCompletionSource<SocketError>(TaskCreationOptions.RunContinuationsAsynchronously);
                connectArgs.Completed += (sender, sockArgs) =>
                {
                    if (sockArgs.LastOperation == SocketAsyncOperation.Connect)
                    {
                        connectTcs.SetResult(sockArgs.SocketError);
                    }
                };
                bool willRaiseEvent = clientSocket.ConnectAsync(connectArgs);
                if (!willRaiseEvent && connectArgs.LastOperation == SocketAsyncOperation.Connect)
                {
                    connectTcs.SetResult(connectArgs.SocketError);
                }

                var timeoutTask = Task.Delay(TCP_ATTEMPT_CONNECT_TIMEOUT);
                var connectResult = await Task.WhenAny(connectTcs.Task, timeoutTask).ConfigureAwait(false);

                if (timeoutTask.IsCompleted)
                {
                    logger.LogWarning($"SIP {ProtDescr} channel timed out attempting to establish a connection to {dstEndPoint}.");
                    return SocketError.TimedOut;
                }
                else if (connectTcs.Task.Result != SocketError.Success)
                {
                    logger.LogWarning($"SIP {ProtDescr} Channel send to {dstEndPoint} failed. Attempt to create a client socket failed with {connectTcs.Task.Result}.");
                    return connectTcs.Task.Result;
                }
                else
                {
                    logger.LogDebug($"ConnectAsync SIP {ProtDescr} Channel connect completed result for {ListeningSIPEndPoint}->{dstEndPoint} {connectTcs.Task.Result}.");

                    var remoteSIPEndPoint = new SIPEndPoint(SIPProtocol, clientSocket.RemoteEndPoint as IPEndPoint);
                    SIPStreamConnection sipStmConn = new SIPStreamConnection(clientSocket, remoteSIPEndPoint, SIPProtocol);
                    sipStmConn.SIPMessageReceived += SIPTCPMessageReceived;

                    var postConnectResult = await OnClientConnect(sipStmConn, serverCertificateName).ConfigureAwait(false);

                    if (postConnectResult != SocketError.Success)
                    {
                        logger.LogWarning($"SIP {ProtDescr} Channel send to {dstEndPoint} failed. Attempt to connect to server at {dstEndPoint} failed with {postConnectResult}.");
                    }
                    else
                    {
                        m_connections.TryAdd(sipStmConn.ConnectionID, sipStmConn);
                        await SendOnConnected(sipStmConn, buffer).ConfigureAwait(false);
                    }

                    return postConnectResult;
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception ConnectClientAsync. {excp}");
                return SocketError.Fault;
            }
        }

        /// <summary>
        /// For TCP channel no special action is required when a new outgoing client connection is established. 
        /// Can start receiving immediately.
        /// </summary>
        /// <param name="streamConnection">The stream connection holding the newly connected client socket.</param>
        protected virtual Task<SocketError> OnClientConnect(SIPStreamConnection streamConnection, string certificateName)
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

            return Task.FromResult(SocketError.Success);
        }

        public override Task<SocketError> SendAsync(SIPEndPoint dstEndPoint, byte[] buffer, bool canInitiateConnection, string connectionIDHint)
        {
            return SendSecureAsync(dstEndPoint, buffer, null, canInitiateConnection, connectionIDHint);
        }

        /// <summary>
        /// Attempts to send data to the remote end point over a reliable connection. If an existing
        /// connection exists it will be used otherwise an attempt will be made to establish a new connection.
        /// </summary>
        /// <param name="dstSIPEndPoint">The remote SIP end point to send the reliable data to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <param name="serverCertificateName">Optional. Only relevant for SSL streams. The common name
        /// that is expected for the remote SSL server.</param>
        /// <param name="canInitiateConnection">Indicates whether this send should initiate a connection if needed.
        /// The typical case is SIP requests can initiate new connections but responses should not. Responses should
        /// only be sent on the same TCP or TLS connection that the original request was received on.</param>
        /// <param name="connectionIDHint">Optional. The ID of the specific TCP connection to try and the send the message on.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public override Task<SocketError> SendSecureAsync(
            SIPEndPoint dstSIPEndPoint,
            byte[] buffer,
            string serverCertificateName,
            bool canInitiateConnection,
            string connectionIDHint)
        {
            try
            {
                if (dstSIPEndPoint == null)
                {
                    throw new ArgumentException(nameof(dstSIPEndPoint), "An empty destination was specified to Send in SIPTCPChannel.");
                }
                if (buffer == null || buffer.Length == 0)
                {
                    throw new ApplicationException("An empty buffer was specified to Send in SIPTCPChannel.");
                }
                else if (!DisableLocalTCPSocketsCheck && NetServices.LocalIPAddresses.Contains(dstSIPEndPoint.Address) && Port == dstSIPEndPoint.Port)
                {
                    logger.LogWarning($"SIP {ProtDescr} Channel blocked Send to {dstSIPEndPoint} as it was identified as a locally hosted {ProtDescr} socket.\r\n" + Encoding.UTF8.GetString(buffer));
                    throw new ApplicationException($"A Send call was blocked in SIP {ProtDescr} Channel due to the destination being another local TCP socket.");
                }
                else
                {
                    IPEndPoint dstEndPoint = dstSIPEndPoint.GetIPEndPoint(m_isDualMode);

                    // Lookup a client socket that is connected to the destination. If it does not exist attempt to connect a new one.
                    SIPStreamConnection sipStreamConn = null;

                    if (connectionIDHint != null)
                    {
                        m_connections.TryGetValue(connectionIDHint, out sipStreamConn);
                    }

                    if (sipStreamConn == null && HasConnection(dstSIPEndPoint))
                    {
                        sipStreamConn = m_connections.Where(x => x.Value.RemoteSIPEndPoint.IsSocketEqual(dstSIPEndPoint)).First().Value;
                    }

                    if (sipStreamConn != null)
                    {
                        SendOnConnected(sipStreamConn, buffer);
                        return Task.FromResult(SocketError.Success);
                    }
                    else if(canInitiateConnection)
                    {
                        return ConnectClientAsync(dstEndPoint, buffer, serverCertificateName);
                    }
                    else
                    {
                        logger.LogWarning($"SIP {ProtDescr} Channel did not have an existing connection for send to {dstSIPEndPoint} and requested not to initiate a connection.");
                        return Task.FromResult(SocketError.NotConnected);
                    }
                }
            }
            catch (SocketException sockExcp)
            {
                return Task.FromResult(sockExcp.SocketErrorCode);
            }
            catch (ApplicationException)
            {
                throw;
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPTCPChannel Send (sendto=>{dstSIPEndPoint}). {excp}");
                throw;
            }
        }

        /// <summary>
        /// Sends data once the stream is connected.
        /// Can be overridden in sub classes that need to implement a different mechanism to send. For example SSL connections.
        /// </summary>
        /// <param name="sipStreamConn">The connected SIP stream wrapping the TCP connection.</param>
        /// <param name="buffer">The data to send.</param>
        protected virtual Task SendOnConnected(SIPStreamConnection sipStreamConn, byte[] buffer)
        {
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

                return Task.CompletedTask;
            }
            catch (SocketException sockExcp)
            {
                logger.LogWarning(sockExcp, $"SocketException SIP {ProtDescr} Channel SendOnConnected {sipStreamConn.RemoteSIPEndPoint}. ErrorCode {sockExcp.SocketErrorCode}. {sockExcp}");
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
                    if (socketError == SocketError.ConnectionReset)
                    {
                        // Connection reset seems to be the way normal closures are reported.
                        logger.LogDebug($"SIP {ProtDescr} stream disconnected {connection.RemoteSIPEndPoint} {socketError}.");
                    }
                    else
                    {
                        logger.LogWarning($"SIP {ProtDescr} stream disconnected {connection.RemoteSIPEndPoint} {socketError}.");
                    }

                    if (m_connections.TryRemove(connection.ConnectionID, out _))
                    {
                        var socket = connection.StreamSocket;

                        // Important: Due to the way TCP works the end of the connection that initiates the close
                        // is meant to go into a TIME_WAIT state. On Windows that results in the same pair of sockets
                        // being unable to reconnect for 30s. SIP can deal with stray and duplicate messages at the 
                        // application layer so the TIME_WAIT is not that useful. In fact it TIME_WAIT is a major annoyance for SIP
                        // as if a connection is dropped for whatever reason, such as a parser error or inactivity, it will
                        // prevent the connection being re-established for the TIME_WAIT period.
                        //
                        // For this reason this implementation uses a hard RST close for client initiated socket closes. This
                        // results in a TCP RST packet instead of the graceful FIN-ACK sequence. Two things are necessary with
                        // WinSock2 to force the hard RST:
                        //
                        // - the Linger option must be set on the raw socket before binding as Linger option {1, 0}.
                        // - the close method must be called on the socket without shutting down the stream.

                        // Linux (WSL) note: This mechanism does not work. Calling socket close does not send the RST and instead
                        // sends the graceful FIN-ACK.
                        // TODO: Research if there is a way to force a socket reset with dotnet on LINUX.

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
        protected Task SIPTCPMessageReceived(SIPChannel channel, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            return SIPMessageReceived?.Invoke(channel, localEndPoint, remoteEndPoint, buffer);
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
        public override bool HasConnection(SIPEndPoint remoteEndPoint)
        {
            return m_connections.Any(x => x.Value.RemoteSIPEndPoint.IsSocketEqual(remoteEndPoint));
        }

        /// <summary>
        /// Not implemented for the TCP channel.
        /// </summary>
        public override bool HasConnection(Uri serverUri)
        {
            throw new NotImplementedException("This HasConnection method is not available in the SIP TCP channel, please use an alternative overload.");
        }

        /// <summary>
        /// Checks whether the specified address family is supported.
        /// </summary>
        /// <param name="addresFamily">The address family to check.</param>
        /// <returns>True if supported, false if not.</returns>
        public override bool IsAddressFamilySupported(AddressFamily addresFamily)
        {
            if (ListeningIPAddress.AddressFamily == AddressFamily.InterNetworkV6 && m_isDualMode)
            {
                return true;
            }
            else
            {
                return addresFamily == ListeningIPAddress.AddressFamily;
            }
        }

        /// <summary>
        /// Checks whether the specified protocol is supported.
        /// </summary>
        /// <param name="protocol">The protocol to check.</param>
        /// <returns>True if supported, false if not.</returns>
        public override bool IsProtocolSupported(SIPProtocolsEnum protocol)
        {
            return protocol == SIPProtocolsEnum.tcp;
        }

        /// <summary>
        /// Closes the channel and any open sockets.
        /// </summary>
        public override void Close()
        {
            if (!Closed == true)
            {
                logger.LogDebug($"Closing SIP {ProtDescr} Channel {ListeningEndPoint}.");

                Closed = true;
                m_cts.Cancel();

                lock (m_connections)
                {
                    foreach (SIPStreamConnection streamConnection in m_connections.Values)
                    {
                        try
                        {
                            // See explanation in OnSIPStreamSocketDisconnected on why the close is done on the socket and NOT the stream.
                            streamConnection.StreamSocket?.Close();
                        }
                        catch (Exception closeExcp)
                        {
                            logger.LogError($"Exception closing SIP connection on {ProtDescr}. {closeExcp.Message}");
                        }
                    }
                    m_connections.Clear();
                }

                if (m_channelSocket != null)
                {
                    try
                    {
                        logger.LogDebug($"Stopping SIP {ProtDescr} Channel listener {ListeningEndPoint}.");

                        m_channelSocket.Close();
                    }
                    catch (Exception stopExcp)
                    {
                        logger.LogError($"Exception SIP {ProtDescr} Channel Close (shutting down listener). {stopExcp.Message}");
                    }
                }

                logger.LogDebug($"Successfully closed SIP {ProtDescr} Channel for {ListeningEndPoint}.");
            }
        }

        public override void Dispose()
        {
            this.Close();
        }

        /// <summary>
        /// Periodically checks the established connections and closes any that have not had a transmission for a specified 
        /// period or where the number of connections allowed per IP address has been exceeded.
        /// </summary>
        private void PruneConnections()
        {
            try
            {
                Task.Delay(PRUNE_CONNECTIONS_INTERVAL, m_cts.Token).Wait();

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
                                logger.LogDebug($"Pruning inactive connection on {ProtDescr} {ListeningSIPEndPoint} to remote end point {inactiveConnection.RemoteSIPEndPoint}.");
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
                            logger.LogWarning(sockExcp, $"Socket error in PruneConnections. {sockExcp.Message} ({sockExcp.ErrorCode}).");
                        }
                        catch (Exception pruneExcp)
                        {
                            logger.LogError("Exception PruneConnections (pruning). " + pruneExcp.Message);
                            checkComplete = true;
                        }
                    }

                    Task.Delay(PRUNE_CONNECTIONS_INTERVAL, m_cts.Token).Wait();
                    checkComplete = false;
                }

                logger.LogDebug($"SIP {ProtDescr} Channel socket on {ListeningSIPEndPoint} pruning connections halted.");
            }
            catch (OperationCanceledException) { }
            catch (AggregateException) { } // This gets thrown if task is cancelled.
            catch (Exception excp)
            {
                logger.LogError($"Exception SIP {ProtDescr} Channel PruneConnections. " + excp.Message);
            }
        }
    }
}
