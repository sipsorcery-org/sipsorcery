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
    ///  The channel can manage multiple connections. All SIP clients wishing to initate a connection to a SIP web socket
    ///  server should use a single instance of this class.
    ///  
    /// <code>
    /// 
    /// </code>
    /// </summary>
    public class SIPClientWebSocketChannel : SIPChannel
    {
        /// <summary>
        /// Holds the state for a current web socket client connection.
        /// </summary>
        private struct ClientWebSocketConnection
        {
            public Uri ServerUri;
            public string ConnectionID;
            public ArraySegment<byte> ReceiveBuffer;
            public Task<WebSocketReceiveResult> ReceiveTask;
            public ClientWebSocket Client;
        }

        public const string SIP_Sec_WebSocket_Protocol = "sip"; // Web socket protocol string for SIP as defined in RFC7118.
        public const string WEB_SOCKET_URI_PREFIX = "ws://";
        public const string WEB_SOCKET_SECURE_URI_PREFIX = "wss://";

        /// <summary>
        /// Maintains a list of current egresss web socket connections (one's that have been initiated by us).
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
            Port = 80;
        }

        /// <summary>
        /// Ideally sends on the web socket channel should specify the connection ID. But if there's
        /// a good reason not to we can check if there is an existing client connection with the
        /// requested remote end point and use it.
        /// </summary>
        /// <param name="destinationEndPoint">The remote destination end point to send the data to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public async override void Send(IPEndPoint destinationEndPoint, byte[] buffer, string connectionIDHint)
        {
            if (destinationEndPoint == null)
            {
                throw new ApplicationException("An empty destination was specified to Send in SIPClientWebSocketChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in SIPClientWebSocketChannel.");
            }

            await SendAsync(destinationEndPoint, buffer, connectionIDHint);
        }

        /// <summary>
        /// Ideally sends on the web socket channel should specify the connection ID. But if there's
        /// a good reason not to we can check if there is an existing client connection with the
        /// requested remote end point and use it.
        /// </summary>
        /// <param name="destinationEndPoint">The remote destination end point to send the data to.</param>
        /// <param name="buffer">The data to send.</param>
        /// <param name="connectionIDHint">The ID of the specific web socket connection to try and send the message on.</param>
        /// <returns>If no errors SocketError.Success otherwise an error value.</returns>
        public override Task<SocketError> SendAsync(IPEndPoint destinationEndPoint, byte[] buffer, string connectionIDHint)
        {
            if (destinationEndPoint == null)
            {
                throw new ApplicationException("An empty destination was specified to Send in SIPClientWebSocketChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for Send in SIPClientWebSocketChannel.");
            }

            var serverUri = new Uri($"{WEB_SOCKET_URI_PREFIX}{destinationEndPoint}");
            return SendAsync(serverUri, buffer);
        }

        /// <summary>
        /// Send to a secure web socket server.
        /// </summary>
        public override Task<SocketError> SendSecureAsync(IPEndPoint dstEndPoint, byte[] buffer, string serverCertificateName, string connectionIDHint)
        {
            if (dstEndPoint == null)
            {
                throw new ApplicationException("An empty destination was specified to SendSecure in SIPClientWebSocketChannel.");
            }
            else if (buffer == null || buffer.Length == 0)
            {
                throw new ArgumentException("buffer", "The buffer must be set and non empty for SendSecure in SIPClientWebSocketChannel.");
            }

            var serverUri = new Uri($"{WEB_SOCKET_SECURE_URI_PREFIX}{dstEndPoint}");
            return SendAsync(serverUri, buffer);
        }

        /// <summary>
        /// Attempts a send to a remote web socket server. If there is an existing connection it will be used
        /// otherwise an attempt will made to establish a new one.
        /// </summary>
        /// <param name="serverUri">The remote web socket server URI to send to.</param>
        /// <param name="buffer">The data buffer to send.</param>
        /// <returns>A success value or an error for failure.</returns>
        private async Task<SocketError> SendAsync(Uri serverUri, byte[] buffer)
        {
            try
            {
                string connectionID = GetConnectionID(serverUri);

                if (m_egressConnections.TryGetValue(connectionID, out var connection))
                {
                    ArraySegment<byte> segmentBuffer = new ArraySegment<byte>(buffer);
                    await connection.Client.SendAsync(segmentBuffer, WebSocketMessageType.Text, true, m_cts.Token);

                    return SocketError.Success;
                }
                else
                {
                    // Attempt a new connection.
                    ClientWebSocket clientWebSocket = new ClientWebSocket();
                    await clientWebSocket.ConnectAsync(serverUri, m_cts.Token);

                    logger.LogDebug($"Successfully connected web socket client to {serverUri}.");

                    ArraySegment<byte> segmentBuffer = new ArraySegment<byte>(buffer);
                    await clientWebSocket.SendAsync(segmentBuffer, WebSocketMessageType.Text, true, m_cts.Token);

                    var recvBuffer = new ArraySegment<byte>(new byte[2 * SIPStreamConnection.MaxSIPTCPMessageSize]);
                    Task<WebSocketReceiveResult> receiveTask = clientWebSocket.ReceiveAsync(recvBuffer, m_cts.Token);

                    ClientWebSocketConnection conn = new ClientWebSocketConnection
                    {
                        ServerUri = serverUri,
                        ConnectionID = connectionID,
                        ReceiveBuffer = recvBuffer,
                        ReceiveTask = receiveTask,
                        Client = clientWebSocket
                    };

                    if (!m_egressConnections.TryAdd(connectionID, conn))
                    {
                        logger.LogError($"Could not added web socket client connected to {serverUri} to channel collection, closing.");

                        _ = Close(connectionID, clientWebSocket);
                    }
                    else
                    {
                        if (!m_isReceiveTaskRunning)
                        {
                            m_isReceiveTaskRunning = true;
                            _ = Task.Run((Action)MonitorReceiveTasks);
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
        /// Not implemented for the WebSocket channel.
        /// </summary>
        public override bool HasConnection(IPEndPoint serverEndPoint)
        {
            throw new NotImplementedException("This HasConnection method is not available in the SIP Client Web Socket channel, please use an alternative overload.");
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
            return true;
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
                logger.LogWarning("Exception SIPClientWebSocketChannel Close. " + excp.Message);
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
        private async void MonitorReceiveTasks()
        {
            try
            {
                while (!Closed && m_egressConnections.Count > 0)
                {
                    try
                    {
                        Task<WebSocketReceiveResult> receiveTask = await Task.WhenAny(m_egressConnections.Select(x => x.Value.ReceiveTask));
                        var conn = m_egressConnections.Where(x => x.Value.ReceiveTask.Id == receiveTask.Id).Single().Value;

                        if (receiveTask.IsCompleted)
                        {
                            logger.LogDebug($"Client web socket connection to {conn.ServerUri} received {receiveTask.Result.Count} bytes.");
                            SIPMessageReceived(this, null, null, conn.ReceiveBuffer.Take(receiveTask.Result.Count).ToArray());
                            conn.ReceiveTask = conn.Client.ReceiveAsync(conn.ReceiveBuffer, m_cts.Token);
                            break;
                        }
                        else
                        {
                            logger.LogWarning($"Client web socket connection to {conn.ServerUri} returned without completeing, closing.");
                            _ = Close(conn.ConnectionID, conn.Client);
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.LogError($"Exception SIPCLientWebSocketChannel processing receive tasks. {excp.Message}");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception SIPCLientWebSocketChannel.MonitorReceiveTasks. {excp.Message}");
            }
            finally
            {
                m_isReceiveTaskRunning = false;
            }
        }
    }
}
