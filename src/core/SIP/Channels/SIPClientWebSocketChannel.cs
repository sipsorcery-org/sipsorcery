//-----------------------------------------------------------------------------
// Filename: SIPClientWebSocketChannel.cs
//
// Description: SIP channel for egress web socket connections. These are
// connections initiated by us to a remote web socket server.
//
// Note: The TCP port used to establish the connection is pseudo-randomly 
// chosen by the .Net framework (or OS).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Dec 2019  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    /// <summary>
    ///  A SIP transport Channel for establishing an outbound connection  over a Web Socket communications layer as per RFC7118.
    ///  The channel can manage multiple connections. All SIP clients wishing to initiate a connection to a SIP web socket
    ///  server should use a single instance of this class.
    /// </summary>
    public class SIPClientWebSocketChannel : SIPChannel
    {
        /// <summary>
        /// Holds the state for a current web socket client connection.
        /// </summary>
        private class ClientWebSocketConnection
        {
            private static int MaxSIPTCPMessageSize = SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH;

            public SIPEndPoint LocalEndPoint;
            public Uri ServerUri;
            public SIPEndPoint RemoteEndPoint;
            public string ConnectionID;
            public ArraySegment<byte> ReceiveBuffer;
            public Task<WebSocketReceiveResult> ReceiveTask;
            public ClientWebSocket Client;

            /// <summary>
            /// Records when a transmission was last sent or received on this stream.
            /// </summary>
            public DateTime LastTransmission;

            /// <summary>
            /// Pending receives that we don't have the full SIP message for yet.
            /// </summary>
            public byte[] PendingReceiveBuffer { get; internal set; } = new byte[2 * MaxSIPTCPMessageSize];

            /// <summary>
            /// The current start position of unprocessed data in the receive buffer.
            /// </summary>
            public int RecvStartPosn { get; internal set; }

            /// <summary>
            /// The current end position of unprocessed data in the receive buffer.
            /// </summary>
            public int RecvEndPosn { get; internal set; }
        }

        public const string SIP_Sec_WebSocket_Protocol = "sip"; // Web socket protocol string for SIP as defined in RFC7118.
        public const string WEB_SOCKET_URI_PREFIX = "ws://";
        public const string WEB_SOCKET_SECURE_URI_PREFIX = "wss://";

        /// <summary>
        /// Maintains a list of current egress web socket connections (one's that have been initiated by us).
        /// </summary>
        private ConcurrentDictionary<string, ClientWebSocketConnection> m_egressConnections = new ConcurrentDictionary<string, ClientWebSocketConnection>();

        /// <summary>
        /// Cancellation source passed to all async operations in this class.
        /// </summary>
        private CancellationTokenSource m_cts = new CancellationTokenSource();

        /// <summary>
        /// Indicates whether the receive thread that monitors the receive tasks for each web socket client is running.
        /// </summary>
        private bool m_isReceiveTaskRunning = false;

        /// <summary>
        /// Creates a SIP channel to establish outbound connections and send SIP messages 
        /// over a web socket communications layer.
        /// </summary>
        public SIPClientWebSocketChannel() : base()
        {
            IsReliable = true;
            SIPProtocol = SIPProtocolsEnum.ws;

            // TODO: These values need to be adjusted. The problem is the source end point isn't available from
            // the client web socket connection.
            ListeningIPAddress = IPAddress.Any;
            Port = SIPConstants.GetDefaultPort(SIPProtocol);
        }

        /// <summary>
        /// Ideally sends on the web socket channel should specify the connection ID. But if there's
        /// a good reason not to we can check if there is an existing client connection with the
        /// requested remote end point and use it.
        /// </summary>
        /// <param name="dstEndPoint">The remote destination end point to send the data to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <param name="connectionIDHint">The ID of the specific web socket connection to try and send the message on.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public override Task<SocketError> SendAsync(SIPEndPoint dstEndPoint, byte[] buffer, bool canInitiateConnection, string connectionIDHint)
        {
            if (dstEndPoint == null)
            {
                throw new ApplicationException("An empty destination was specified to Send in SIPClientWebSocketChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in SIPClientWebSocketChannel.");
            }

            return SendAsync(dstEndPoint, buffer);
        }

        /// <summary>
        /// Send to a secure web socket server.
        /// </summary>
        public override Task<SocketError> SendSecureAsync(SIPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName, bool canInitiateConnection, string connectionIDHint)
        {
            if (dstEndPoint == null)
            {
                throw new ApplicationException("An empty destination was specified to SendSecure in SIPClientWebSocketChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for SendSecure in SIPClientWebSocketChannel.");
            }

            return SendAsync(dstEndPoint, buffer);
        }

        /// <summary>
        /// Attempts a send to a remote web socket server. If there is an existing connection it will be used
        /// otherwise an attempt will made to establish a new one.
        /// </summary>
        /// <param name="serverEndPoint">The remote web socket server URI to send to.</param>
        /// <param name="buffer">The data buffer to send.</param>
        /// <returns>A success value or an error for failure.</returns>
        private async Task<SocketError> SendAsync(SIPEndPoint serverEndPoint, byte[] buffer)
        {
            try
            {
                string uriPrefix = (serverEndPoint.Protocol == SIPProtocolsEnum.wss) ? WEB_SOCKET_SECURE_URI_PREFIX : WEB_SOCKET_URI_PREFIX;
                var serverUri = new Uri($"{uriPrefix}{serverEndPoint.GetIPEndPoint()}");

                string connectionID = GetConnectionID(serverUri);
                serverEndPoint.ChannelID = this.ID;
                serverEndPoint.ConnectionID = connectionID;

                if (m_egressConnections.TryGetValue(connectionID, out var conn))
                {
                    logger.LogDebug($"Sending {buffer.Length} bytes on client web socket connection to {conn.ServerUri}.");

                    ArraySegment<byte> segmentBuffer = new ArraySegment<byte>(buffer);
                    await conn.Client.SendAsync(segmentBuffer, WebSocketMessageType.Text, true, m_cts.Token).ConfigureAwait(false);

                    return SocketError.Success;
                }
                else
                {
                    // Attempt a new connection.
                    ClientWebSocket clientWebSocket = new ClientWebSocket();
                    await clientWebSocket.ConnectAsync(serverUri, m_cts.Token).ConfigureAwait(false);

                    logger.LogDebug($"Successfully connected web socket client to {serverUri}.");

                    ArraySegment<byte> segmentBuffer = new ArraySegment<byte>(buffer);
                    await clientWebSocket.SendAsync(segmentBuffer, WebSocketMessageType.Text, true, m_cts.Token).ConfigureAwait(false);

                    var recvBuffer = new ArraySegment<byte>(new byte[2 * SIPStreamConnection.MaxSIPTCPMessageSize]);
                    Task<WebSocketReceiveResult> receiveTask = clientWebSocket.ReceiveAsync(recvBuffer, m_cts.Token);

                    // There's currently no way to get the socket IP end point used by the client web socket to establish
                    // the connection. Instead provide a dummy local end point that has as much of the information as we can.
                    IPEndPoint localEndPoint = new IPEndPoint((serverEndPoint.Address.AddressFamily == AddressFamily.InterNetwork) ? IPAddress.Any : IPAddress.IPv6Any, 0);
                    SIPEndPoint localSIPEndPoint = new SIPEndPoint(serverEndPoint.Protocol, localEndPoint, this.ID, connectionID);

                    ClientWebSocketConnection newConn = new ClientWebSocketConnection
                    {
                        LocalEndPoint = localSIPEndPoint,
                        ServerUri = serverUri,
                        RemoteEndPoint = serverEndPoint,
                        ConnectionID = connectionID,
                        ReceiveBuffer = recvBuffer,
                        ReceiveTask = receiveTask,
                        Client = clientWebSocket
                    };

                    if (!m_egressConnections.TryAdd(connectionID, newConn))
                    {
                        logger.LogError($"Could not add web socket client connected to {serverUri} to channel collection, closing.");
                        await Close(connectionID, clientWebSocket).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!m_isReceiveTaskRunning)
                        {
                            m_isReceiveTaskRunning = true;
                            _ = Task.Factory.StartNew(MonitorReceiveTasks, TaskCreationOptions.LongRunning);
                        }
                    }

                    return SocketError.Success;
                }
            }
            catch (SocketException sockExcp)
            {
                return sockExcp.SocketErrorCode;
            }
        }

        /// <summary>
        /// Checks whether the web socket SIP channel has a connection matching a unique connection ID.
        /// </summary>
        /// <param name="connectionID">The connection ID to check for a match on.</param>
        /// <returns>True if a match is found or false if not.</returns>
        public override bool HasConnection(string connectionID)
        {
            return m_egressConnections.ContainsKey(connectionID);
        }

        /// <summary>
        /// Checks whether the client web socket SIP channel has a connection to the requested server end point.
        /// </summary>
        public override bool HasConnection(SIPEndPoint serverEndPoint)
        {
            string uriPrefix = (serverEndPoint.Protocol == SIPProtocolsEnum.wss) ? WEB_SOCKET_SECURE_URI_PREFIX : WEB_SOCKET_URI_PREFIX;
            var serverUri = new Uri($"{uriPrefix}{serverEndPoint.GetIPEndPoint()}");
            string connectionID = GetConnectionID(serverUri);

            return m_egressConnections.ContainsKey(connectionID);
        }

        /// <summary>
        /// Checks whether there is an existing client web socket connection for a remote end point.
        /// </summary>
        /// <param name="serverUri">The server URI to check for an existing connection.</param>
        /// <returns>True if there is a connection or false if not.</returns>
        public override bool HasConnection(Uri serverUri)
        {
            return m_egressConnections.ContainsKey(GetConnectionID(serverUri));
        }

        /// <summary>
        /// Checks whether the specified address family is supported.
        /// </summary>
        /// <param name="addresFamily">The address family to check.</param>
        /// <returns>True if supported, false if not.</returns>
        public override bool IsAddressFamilySupported(AddressFamily addresFamily)
        {
            // We can establish client web sockets with both IPv4 and IPv6.
            return true;
        }

        /// <summary>
        /// Checks whether the specified protocol is supported.
        /// </summary>
        /// <param name="protocol">The protocol to check.</param>
        /// <returns>True if supported, false if not.</returns>
        public override bool IsProtocolSupported(SIPProtocolsEnum protocol)
        {
            // We can establish client web sockets to both ws and wss servers.
            return protocol == SIPProtocolsEnum.ws || protocol == SIPProtocolsEnum.wss;
        }

        /// <summary>
        /// Get the local SIPEndPoint this channel will use for communicating with the destination SIP end point.
        /// </summary>
        /// <param name="dstEndPoint">The destination SIP end point.</param>
        /// <returns>The local SIP end points this channel selects to use for connecting to the destination.</returns>
        internal override SIPEndPoint GetLocalSIPEndPointForDestination(SIPEndPoint dstEndPoint)
        {
            IPAddress dstAddress = dstEndPoint.GetIPEndPoint().Address;
            IPAddress localAddress = GetLocalIPAddressForDestination(dstAddress);

            // Need to return ws or wss to match the destination.
            return new SIPEndPoint(dstEndPoint.Protocol, localAddress, Port, ID, null);
        }

        /// <summary>
        /// Closes all web socket connections.
        /// </summary>
        public override void Close()
        {
            try
            {
                logger.LogDebug($"Closing SIP Client Web Socket Channel.");

                Closed = true;
                m_cts.Cancel();

                foreach (var conn in m_egressConnections)
                {
                    _ = Close(conn.Key, conn.Value.Client);
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning(excp, "Exception SIPClientWebSocketChannel Close. " + excp.Message);
            }
        }

        /// <summary>
        /// Calls close on the channel when it is disposed.
        /// </summary>
        public override void Dispose()
        {
            this.Close();
        }

        /// <summary>
        /// Closes a single web socket client connection.
        /// </summary>
        /// <param name="client">The client to close.</param>
        private Task Close(string connectionID, ClientWebSocket client)
        {
            if (!Closed)
            {
                // Don't touch the lists if the whole channel is being closed. At that point
                // The connection list is being iterated.
                m_egressConnections.TryRemove(connectionID, out _);
            }

            return client.CloseAsync(WebSocketCloseStatus.NormalClosure, null, m_cts.Token);
        }

        /// <summary>
        /// Gets the connection ID for a server URI.
        /// </summary>
        /// <param name="serverUri">The web socket server URI for the connection.</param>
        /// <returns>A string connection ID.</returns>
        private string GetConnectionID(Uri serverUri)
        {
            return Crypto.GetSHAHashAsString(serverUri.ToString());
        }

        /// <summary>
        /// Monitors the client web socket tasks for new receives.
        /// </summary>
        private void MonitorReceiveTasks()
        {
            try
            {
                while (!Closed && m_egressConnections.Count > 0)
                {
                    try
                    {
                        var receiveTasks = m_egressConnections.Select(x => x.Value.ReceiveTask).ToArray();
                        int completedTaskIndex = Task.WaitAny(receiveTasks);
                        Task<WebSocketReceiveResult> receiveTask = receiveTasks[completedTaskIndex];
                        var conn = m_egressConnections.Where(x => x.Value.ReceiveTask.Id == receiveTask.Id).Single().Value;

                        if (receiveTask.IsCompleted)
                        {
                            logger.LogDebug($"Client web socket connection to {conn.ServerUri} received {receiveTask.Result.Count} bytes.");
                            //SIPMessageReceived(this, conn.LocalEndPoint, conn.RemoteEndPoint, conn.ReceiveBuffer.Take(receiveTask.Result.Count).ToArray()).Wait();
                            ExtractSIPMessages(this, conn, conn.ReceiveBuffer, receiveTask.Result.Count);
                            conn.ReceiveTask = conn.Client.ReceiveAsync(conn.ReceiveBuffer, m_cts.Token);
                        }
                        else
                        {
                            logger.LogWarning($"Client web socket connection to {conn.ServerUri} returned without completing, closing.");
                            Close(conn.ConnectionID, conn.Client);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (AggregateException) { } // This gets thrown if task is cancelled.
                    catch (Exception excp)
                    {
                        logger.LogError($"Exception SIPClientWebSocketChannel processing receive tasks. {excp.Message}");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPClientWebSocketChannel.MonitorReceiveTasks. {excp.Message}");
            }
            finally
            {
                m_isReceiveTaskRunning = false;
            }
        }

        /// <summary>
        /// Attempts to extract SIP messages from the data that has been received on the client web socket SIP stream connection.
        /// </summary>
        /// <param name="recvChannel">The receiving SIP channel.</param>
        /// <param name="clientConn">The web socket client connection properties including pending receive buffer</param>
        /// <param name="buffer">The buffer holding the current data from the stream. Note that the buffer can 
        /// stretch over multiple receives.</param>
        /// <param name="bytesRead">The bytes that were read by the latest receive operation (the new bytes available).</param>
        private void ExtractSIPMessages(SIPChannel recvChannel, ClientWebSocketConnection clientConn, ArraySegment<byte> buffer, int bytesRead)
        {
            if (bytesRead + clientConn.RecvEndPosn > clientConn.PendingReceiveBuffer.Length)
            {
                // Don't have enough space for the read. Throw away the pending buffer and hope the SIP transaction is re-attempted.
                clientConn.RecvStartPosn = clientConn.RecvEndPosn = 0;
            }

            Buffer.BlockCopy(buffer.ToArray(), 0, clientConn.PendingReceiveBuffer, clientConn.RecvEndPosn, bytesRead);
            clientConn.RecvEndPosn += bytesRead;

            int bytesSkipped = 0;
            byte[] sipMsgBuffer = SIPMessageBuffer.ParseSIPMessageFromStream(clientConn.PendingReceiveBuffer, clientConn.RecvStartPosn, clientConn.RecvEndPosn, out bytesSkipped);

            while (sipMsgBuffer != null)
            {
                // A SIP message is available.
                if (SIPMessageReceived != null)
                {
                    clientConn.LastTransmission = DateTime.Now;
                    SIPMessageReceived(recvChannel, clientConn.LocalEndPoint, clientConn.RemoteEndPoint, sipMsgBuffer).Wait();
                }

                clientConn.RecvStartPosn += (sipMsgBuffer.Length + bytesSkipped);

                if (clientConn.RecvStartPosn == clientConn.RecvEndPosn)
                {
                    // All data has been successfully extracted from the receive buffer.
                    clientConn.RecvStartPosn = clientConn.RecvEndPosn = 0;
                    break;
                }
                else
                {
                    // Try and extract another SIP message from the receive buffer.
                    sipMsgBuffer = SIPMessageBuffer.ParseSIPMessageFromStream(clientConn.PendingReceiveBuffer, clientConn.RecvStartPosn, clientConn.RecvEndPosn, out bytesSkipped);
                }
            }
        }
    }
}
