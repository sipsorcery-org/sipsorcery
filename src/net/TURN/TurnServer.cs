//-----------------------------------------------------------------------------
// Filename: TurnServer.cs
//
// Description: Implements a TURN Server as defined in RFC 5766.
//
// Provides relay-based NAT traversal for clients that cannot use direct
// peer-to-peer connectivity. Supports both TCP and UDP control channels,
// long-term credentials (RFC 5389 Section 10.2), permissions, and
// channel bindings.
//
// Author(s):
// SIPSorcery Contributors
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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Configuration for <see cref="TurnServer"/>.
    /// </summary>
    public class TurnServerConfig
    {
        /// <summary>
        /// The address to listen on for client connections. Default is <see cref="IPAddress.Loopback"/>.
        /// </summary>
        public IPAddress ListenAddress { get; set; } = IPAddress.Loopback;

        /// <summary>
        /// The port to listen on. Default is 3478 (standard STUN/TURN port).
        /// </summary>
        public int Port { get; set; } = 3478;

        /// <summary>
        /// Whether to accept TCP control connections. Default is true.
        /// </summary>
        public bool EnableTcp { get; set; } = true;

        /// <summary>
        /// Whether to accept UDP control datagrams. Default is true.
        /// </summary>
        public bool EnableUdp { get; set; } = true;

        /// <summary>
        /// The address advertised in XOR-RELAYED-ADDRESS responses. Set to a public IP
        /// when the server is behind NAT. Defaults to <see cref="ListenAddress"/>.
        /// </summary>
        public IPAddress RelayAddress { get; set; }

        /// <summary>
        /// Long-term credential username (RFC 5389 Section 10.2).
        /// </summary>
        public string Username { get; set; } = "turn-user";

        /// <summary>
        /// Long-term credential password.
        /// </summary>
        public string Password { get; set; } = "turn-pass";

        /// <summary>
        /// The REALM value for authentication challenges.
        /// </summary>
        public string Realm { get; set; } = "sipsorcery";

        /// <summary>
        /// Default allocation lifetime in seconds. Default is 600 (10 minutes).
        /// </summary>
        public int DefaultLifetimeSeconds { get; set; } = 600;
    }

    /// <summary>
    /// Represents a TCP connection to a peer established via RFC 6062 Connect or
    /// via an incoming connection on a TCP relay listener.
    /// </summary>
    public class TcpPeerConnection : IDisposable
    {
        /// <summary>Connection ID assigned by the server (unique within the allocation).</summary>
        public uint ConnectionId { get; set; }

        /// <summary>The remote peer endpoint.</summary>
        public IPEndPoint PeerEndPoint { get; set; }

        /// <summary>The TCP client connected to the peer.</summary>
        public TcpClient TcpClient { get; set; }

        /// <summary>Network stream for the peer TCP connection.</summary>
        public NetworkStream Stream { get; set; }

        /// <summary>
        /// Network stream for the client's data connection (set after ConnectionBind).
        /// </summary>
        public NetworkStream ClientDataStream { get; set; }

        /// <summary>Whether this connection has been bound to a client data connection.</summary>
        public bool IsBound { get; set; }

        /// <summary>Cancellation source for the relay tasks.</summary>
        public CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();

        public void Dispose()
        {
            try { Cts.Cancel(); } catch { }
            try { Cts.Dispose(); } catch { }
            try { TcpClient?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Represents a TURN allocation — the server-side state for a single client's relay session.
    /// </summary>
    public class TurnAllocation : IDisposable
    {
        /// <summary>Unique identifier (typically the client's remote endpoint string).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The UDP socket used to relay data to/from peers (null for TCP relay).</summary>
        public UdpClient RelaySocket { get; set; }

        /// <summary>The relay endpoint (IP + port) advertised to the client.</summary>
        public IPEndPoint RelayEndPoint { get; set; }

        /// <summary>When this allocation expires (UTC).</summary>
        public DateTime Expiry { get; set; }

        /// <summary>
        /// Installed permissions: peer IP address → expiry time.
        /// Per RFC 5766 Section 8, permissions expire after 300 seconds.
        /// </summary>
        public ConcurrentDictionary<string, DateTime> Permissions { get; } = new ConcurrentDictionary<string, DateTime>();

        /// <summary>Channel number → peer endpoint mapping.</summary>
        public ConcurrentDictionary<ushort, IPEndPoint> ChannelBindings { get; } = new ConcurrentDictionary<ushort, IPEndPoint>();

        /// <summary>Peer endpoint string → channel number (reverse lookup).</summary>
        public ConcurrentDictionary<string, ushort> ReverseChannelBindings { get; } = new ConcurrentDictionary<string, ushort>();

        // Internal: TCP stream for sending relay data back to the client (null for UDP clients).
        internal NetworkStream TcpStream { get; set; }

        // Internal: UDP client endpoint for sending relay data back (null for TCP clients).
        internal IPEndPoint UdpClientEndPoint { get; set; }

        // Internal: reference to the server's UDP control socket for sending responses.
        internal UdpClient UdpControlSocket { get; set; }

        // Internal: cancellation for the relay loop.
        internal CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();

        /// <summary>Whether this is a TCP relay allocation (RFC 6062) vs UDP relay.</summary>
        public bool IsTcpRelay { get; set; }

        /// <summary>TCP listener for accepting peer connections (TCP relay only).</summary>
        public TcpListener RelayTcpListener { get; set; }

        /// <summary>Active TCP peer connections (Connection ID → connection).</summary>
        public ConcurrentDictionary<uint, TcpPeerConnection> TcpPeerConnections { get; } =
            new ConcurrentDictionary<uint, TcpPeerConnection>();

        private int _nextConnectionId;

        /// <summary>Allocates a unique connection ID for this allocation.</summary>
        public uint AllocateConnectionId()
        {
            return (uint)Interlocked.Increment(ref _nextConnectionId);
        }

        public void Dispose()
        {
            try { Cts.Cancel(); } catch { }
            try { Cts.Dispose(); } catch { }
            try { RelaySocket?.Dispose(); } catch { }
            try { RelayTcpListener?.Stop(); } catch { }
            foreach (var conn in TcpPeerConnections.Values)
            {
                try { conn.Dispose(); } catch { }
            }
            TcpPeerConnections.Clear();
        }
    }

    /// <summary>
    /// A lightweight TURN relay server (RFC 5766) supporting TCP and UDP control channels.
    /// Provides NAT traversal by relaying UDP traffic between clients and peers.
    /// Intended for development, testing, and small-scale/embedded scenarios — not for
    /// production use at scale (use coturn or similar for that).
    /// </summary>
    /// <remarks>
    /// <para><strong>Known limitations (contributions welcome):</strong></para>
    /// <list type="bullet">
    ///   <item>Single static credential (one username/password pair) — no per-user credential
    ///         database or REST API-based ephemeral credentials (RFC 8489 Section 9.2).</item>
    ///   <item>No nonce validation/expiry — nonces are generated but never verified on subsequent
    ///         requests, so replay attacks are possible within the allocation lifetime.</item>
    ///   <item>No rate limiting or per-IP allocation caps — a misbehaving client can exhaust
    ///         server resources.</item>
    ///   <item>No TLS/DTLS for the control channel — credentials are sent in the clear unless the
    ///         transport is already secured.</item>
    ///   <item>No TURN-over-TLS (RFC 5766 Section 6).</item>
    ///   <item>No EVEN-PORT / RESERVATION-TOKEN support.</item>
    ///   <item>IPv4 only (no IPv6 relay addresses).</item>
    ///   <item>Allocation lifetime is not capped — clients can request arbitrarily long lifetimes.</item>
    ///   <item>No ALTERNATE-SERVER support.</item>
    /// </list>
    /// <para><strong>Security considerations:</strong></para>
    /// <list type="bullet">
    ///   <item>Default credentials (<c>turn-user</c> / <c>turn-pass</c>) — callers MUST configure
    ///         real credentials; defaults are intentionally weak to encourage replacement.</item>
    ///   <item>Default listen address is loopback — safe by default, but if bound to a public
    ///         interface without TLS, credentials travel in cleartext.</item>
    ///   <item>The manual HMAC verification and error-code construction are workarounds for bugs in
    ///         <c>STUNMessage.CheckIntegrity()</c> (#1510) and <c>STUNErrorCodeAttribute</c>
    ///         (#1509); once those are merged, TurnServer should be updated to use the fixed
    ///         library methods.</item>
    ///   <item>No input validation on allocation count or relay port range — in production you
    ///         would want to bound these.</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// var server = new TurnServer(new TurnServerConfig
    /// {
    ///     ListenAddress = IPAddress.Loopback,
    ///     Port = 3478,
    ///     Username = "user",
    ///     Password = "pass",
    ///     Realm = "example.com"
    /// });
    /// server.Start();
    /// // ... server is running ...
    /// server.Dispose(); // or server.Stop();
    /// </code>
    /// </example>
    public class TurnServer : IDisposable
    {
        private const int PERMISSION_LIFETIME_SECONDS = 300; // RFC 5766 Section 8
        private const int CLEANUP_INTERVAL_SECONDS = 30;

        private static ILogger logger = Log.Logger;

        private readonly TurnServerConfig _config;
        private readonly byte[] _hmacKey;
        private readonly IPAddress _relayAddress;

        private TcpListener _tcpListener;
        private UdpClient _udpSocket;
        private Timer _cleanupTimer;
        private volatile bool _running;

        private readonly ConcurrentDictionary<string, TurnAllocation> _allocations =
            new ConcurrentDictionary<string, TurnAllocation>();

        /// <summary>
        /// Gets a read-only view of current allocations.
        /// </summary>
        public IReadOnlyDictionary<string, TurnAllocation> Allocations => _allocations;

        /// <summary>
        /// Creates a new TURN server with the specified configuration.
        /// </summary>
        /// <param name="config">Server configuration.</param>
        public TurnServer(TurnServerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _relayAddress = config.RelayAddress ?? config.ListenAddress;

            // Long-term credential: HMAC key = MD5(username:realm:password)
#if NET5_0_OR_GREATER
            _hmacKey = MD5.HashData(
                Encoding.UTF8.GetBytes($"{_config.Username}:{_config.Realm}:{_config.Password}"));
#else
            using (var md5 = MD5.Create())
            {
                _hmacKey = md5.ComputeHash(
                    Encoding.UTF8.GetBytes($"{_config.Username}:{_config.Realm}:{_config.Password}"));
            }
#endif
        }

        /// <summary>
        /// Starts listening for client connections and datagrams.
        /// </summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;

            if (_config.EnableTcp)
            {
                _tcpListener = new TcpListener(_config.ListenAddress, _config.Port);
                _tcpListener.Start();
                _ = AcceptTcpClientsAsync();
                logger.LogDebug("TURN server TCP listener started on {Address}:{Port}.",
                    _config.ListenAddress, _config.Port);
            }

            if (_config.EnableUdp)
            {
                _udpSocket = new UdpClient(new IPEndPoint(_config.ListenAddress, _config.Port));
                _ = ReceiveUdpAsync();
                logger.LogDebug("TURN server UDP listener started on {Address}:{Port}.",
                    _config.ListenAddress, _config.Port);
            }

            _cleanupTimer = new Timer(CleanExpiredAllocations, null,
                TimeSpan.FromSeconds(CLEANUP_INTERVAL_SECONDS),
                TimeSpan.FromSeconds(CLEANUP_INTERVAL_SECONDS));

            logger.LogInformation("TURN server started on {Address}:{Port} (TCP={Tcp}, UDP={Udp}).",
                _config.ListenAddress, _config.Port, _config.EnableTcp, _config.EnableUdp);
        }

        /// <summary>
        /// Stops the server and disposes all allocations.
        /// </summary>
        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            _cleanupTimer?.Dispose();
            _cleanupTimer = null;

            try { _tcpListener?.Stop(); } catch { }
            try { _udpSocket?.Dispose(); } catch { }

            foreach (var kvp in _allocations)
            {
                kvp.Value.Dispose();
            }
            _allocations.Clear();

            logger.LogInformation("TURN server stopped.");
        }

        public void Dispose()
        {
            Stop();
        }

        #region TCP handling

        private async Task AcceptTcpClientsAsync()
        {
            try
            {
                while (_running)
                {
                    TcpClient client;
                    try
                    {
                        client = await _tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }

                    logger.LogDebug("TURN TCP client connected from {Remote}.", client.Client.RemoteEndPoint);
                    _ = HandleTcpClientAsync(client);
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "TURN TCP accept loop error. {ErrorMessage}", ex.Message);
            }
        }

        private async Task HandleTcpClientAsync(TcpClient tcpClient)
        {
            var clientId = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
            var stream = tcpClient.GetStream();
            TurnAllocation allocation = null;

            try
            {
                while (_running && tcpClient.Connected)
                {
                    // TCP framing: read first 4 bytes to determine STUN message vs ChannelData.
                    var header = new byte[4];
                    if (!await ReadExactAsync(stream, header, 0, 4).ConfigureAwait(false))
                        break;

                    if ((header[0] & 0xC0) == 0x40)
                    {
                        // ChannelData: first 2 bytes = channel number, next 2 = data length
                        var channelNumber = (ushort)((header[0] << 8) | header[1]);
                        var dataLength = (ushort)((header[2] << 8) | header[3]);

                        var data = new byte[dataLength];
                        if (dataLength > 0 && !await ReadExactAsync(stream, data, 0, dataLength).ConfigureAwait(false))
                            break;

                        // Pad to 4-byte boundary (consume padding bytes from TCP stream)
                        var padding = (4 - (dataLength % 4)) % 4;
                        if (padding > 0)
                        {
                            var padBuf = new byte[padding];
                            await ReadExactAsync(stream, padBuf, 0, padding).ConfigureAwait(false);
                        }

                        HandleChannelData(allocation, channelNumber, data);
                    }
                    else
                    {
                        // STUN message: bytes 2-3 = attributes length
                        var msgLength = (ushort)((header[2] << 8) | header[3]);
                        var remaining = 16 + msgLength; // magic cookie(4) + txnId(12) + attributes
                        var fullMsg = new byte[4 + remaining];
                        Buffer.BlockCopy(header, 0, fullMsg, 0, 4);

                        if (remaining > 0 && !await ReadExactAsync(stream, fullMsg, 4, remaining).ConfigureAwait(false))
                            break;

                        var stunMsg = STUNMessage.ParseSTUNMessage(fullMsg, fullMsg.Length);
                        if (stunMsg == null)
                        {
                            logger.LogWarning("Failed to parse STUN message from TCP client {Client}.", clientId);
                            continue;
                        }

                        bool connectionBound = ProcessMessage(stunMsg, fullMsg, clientId,
                            (responseBytes) => SendTcpResponseAsync(stream, responseBytes),
                            ref allocation,
                            stream, null, null);

                        if (connectionBound)
                        {
                            // After ConnectionBind, this TCP connection is now a raw data
                            // channel (RFC 6062 Section 5.4). Exit the STUN message loop
                            // but keep the connection alive for the relay.
                            logger.LogDebug("TCP connection from {Client} bound for data relay.", clientId);

                            // Wait for the relay to complete (keeps the TcpClient alive)
                            // Find the peer connection that was just bound to this stream
                            foreach (var alloc in _allocations.Values)
                            {
                                foreach (var pc in alloc.TcpPeerConnections.Values)
                                {
                                    if (pc.IsBound && pc.ClientDataStream == stream)
                                    {
                                        try
                                        {
                                            await Task.Delay(Timeout.Infinite, pc.Cts.Token).ConfigureAwait(false);
                                        }
                                        catch (OperationCanceledException) { }
                                        return;
                                    }
                                }
                            }
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (System.IO.IOException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "TURN TCP client handler error for {Client}. {ErrorMessage}", clientId, ex.Message);
            }
            finally
            {
                // Only clean up the allocation if this is the control connection, not a data connection.
                // Data connections do not own the allocation.
                if (allocation != null && allocation.TcpStream == stream)
                {
                    _allocations.TryRemove(allocation.Id, out _);
                    allocation.Dispose();
                    logger.LogDebug("Cleaned up TCP allocation for {Client}.", clientId);
                }
                tcpClient.Dispose();
            }
        }

        private static async Task SendTcpResponseAsync(NetworkStream stream, byte[] data)
        {
            await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            var totalRead = 0;
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead).ConfigureAwait(false);
                if (read == 0) return false; // Connection closed
                totalRead += read;
            }
            return true;
        }

        #endregion

        #region UDP handling

        private async Task ReceiveUdpAsync()
        {
            try
            {
                while (_running)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await _udpSocket.ReceiveAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }

                    HandleUdpDatagram(result.Buffer, result.RemoteEndPoint);
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "TURN UDP receive loop error. {ErrorMessage}", ex.Message);
            }
        }

        private void HandleUdpDatagram(byte[] data, IPEndPoint remoteEndPoint)
        {
            var clientId = remoteEndPoint.ToString();

            if (data.Length >= 4 && (data[0] & 0xC0) == 0x40)
            {
                // ChannelData message
                var channelNumber = (ushort)((data[0] << 8) | data[1]);
                var dataLength = (ushort)((data[2] << 8) | data[3]);

                if (data.Length >= 4 + dataLength)
                {
                    var payload = new byte[dataLength];
                    Buffer.BlockCopy(data, 4, payload, 0, dataLength);

                    // Find the allocation for this client
                    if (_allocations.TryGetValue(clientId, out var allocation))
                    {
                        HandleChannelData(allocation, channelNumber, payload);
                    }
                }
                return;
            }

            var stunMsg = STUNMessage.ParseSTUNMessage(data, data.Length);
            if (stunMsg == null)
            {
                logger.LogWarning("Failed to parse STUN message from UDP client {Client}.", clientId);
                return;
            }

            TurnAllocation udpAllocation = null;
            _allocations.TryGetValue(clientId, out udpAllocation);

            _ = ProcessMessage(stunMsg, data, clientId,
                (responseBytes) => SendUdpResponseAsync(remoteEndPoint, responseBytes),
                ref udpAllocation,
                null, remoteEndPoint, _udpSocket);

            // Update the dictionary if a new allocation was created
            if (udpAllocation != null && !_allocations.ContainsKey(clientId))
            {
                _allocations[clientId] = udpAllocation;
            }
        }

        private async Task SendUdpResponseAsync(IPEndPoint remoteEndPoint, byte[] data)
        {
            try
            {
                await _udpSocket.SendAsync(data, data.Length, remoteEndPoint).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to send UDP response to {Endpoint}. {ErrorMessage}", remoteEndPoint, ex.Message);
            }
        }

        #endregion

        #region Message processing

        /// <returns>True if a ConnectionBind was completed and the TCP connection is now a raw data channel.</returns>
        private bool ProcessMessage(
            STUNMessage msg,
            byte[] rawBytes,
            string clientId,
            Func<byte[], Task> sendResponse,
            ref TurnAllocation allocation,
            NetworkStream tcpStream,
            IPEndPoint udpClientEndPoint,
            UdpClient udpControlSocket)
        {
            var msgType = msg.Header.MessageType;
            logger.LogDebug("TURN {Type} from {Client}.", msgType, clientId);

            switch (msgType)
            {
                case STUNMessageTypesEnum.BindingRequest:
                    {
                        var response = HandleBindingRequest(msg);
                        var bytes = response.ToByteBuffer(null, false);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.Allocate:
                    {
                        var (response, needsAuth) = HandleAllocate(msg, rawBytes, clientId,
                            tcpStream, udpClientEndPoint, udpControlSocket,
                            ref allocation);
                        var bytes = needsAuth
                            ? response.ToByteBuffer(_hmacKey, true)
                            : response.ToByteBuffer(null, false);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.Refresh:
                    {
                        var response = HandleRefresh(msg, clientId, ref allocation);
                        var bytes = response.ToByteBuffer(_hmacKey, true);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.CreatePermission:
                    {
                        var response = HandleCreatePermission(msg, allocation);
                        var bytes = response.ToByteBuffer(_hmacKey, true);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.ChannelBind:
                    {
                        var response = HandleChannelBind(msg, allocation);
                        var bytes = response.ToByteBuffer(_hmacKey, true);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.SendIndication:
                    HandleSendIndication(msg, allocation);
                    break; // Indications get no response

                case STUNMessageTypesEnum.Connect:
                    {
                        var response = HandleConnect(msg, allocation);
                        var bytes = response.ToByteBuffer(_hmacKey, true);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.ConnectionBind:
                    {
                        var (response, connectionBound) = HandleConnectionBind(msg, allocation, tcpStream);
                        var bytes = response.ToByteBuffer(_hmacKey, true);
                        _ = sendResponse(bytes);
                        if (connectionBound)
                        {
                            return true;
                        }
                    }
                    break;

                default:
                    logger.LogWarning("Unhandled STUN message type: {Type}.", msgType);
                    break;
            }

            return false;
        }

        private STUNMessage HandleBindingRequest(STUNMessage request)
        {
            var response = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;
            response.AddXORMappedAddressAttribute(_relayAddress, _config.Port);
            return response;
        }

        private (STUNMessage response, bool needsAuth) HandleAllocate(
            STUNMessage request,
            byte[] rawBytes,
            string clientId,
            NetworkStream tcpStream,
            IPEndPoint udpClientEndPoint,
            UdpClient udpControlSocket,
            ref TurnAllocation allocation)
        {
            // Check for MESSAGE-INTEGRITY — first request won't have it
            var hasIntegrity = request.Attributes.Any(
                a => a.AttributeType == STUNAttributeTypesEnum.MessageIntegrity);

            if (!hasIntegrity)
            {
                // Send 401 Unauthorized with REALM and NONCE (unsigned)
                var errResponse = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(401, "Unauthorized"));
                errResponse.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                    Encoding.UTF8.GetBytes(_config.Realm)));
                errResponse.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                    Encoding.UTF8.GetBytes(GenerateNonce())));
                return (errResponse, false);
            }

            // Verify MESSAGE-INTEGRITY manually on the raw bytes.
            // SIPSorcery's CheckIntegrity() requires a valid FINGERPRINT as the last attribute
            // (isFingerprintValid guard), which browsers may not send for TURN messages.
            // See: https://github.com/sipsorcery-org/sipsorcery/pull/1510
            if (!VerifyMessageIntegrity(rawBytes, _hmacKey))
            {
                logger.LogWarning("TURN Allocate: integrity check failed from {Client}.", clientId);
                var errResponse = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(401, "Unauthorized"));
                return (errResponse, false);
            }

            // Check if there's already an allocation for this client
            if (allocation != null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(437, "Allocation Mismatch"));
                return (errResponse, true);
            }

            // Parse REQUESTED-TRANSPORT to determine UDP or TCP relay
            bool isTcpRelay = false;
            var transportAttr = request.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.RequestedTransport);
            if (transportAttr?.Value != null && transportAttr.Value.Length >= 1)
            {
                byte transportProto = transportAttr.Value[0];
                if (transportProto == 0x06) // TCP
                {
                    isTcpRelay = true;
                }
                else if (transportProto != 0x11) // Not UDP either
                {
                    var errResponse = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
                    errResponse.Header.TransactionId = request.Header.TransactionId;
                    errResponse.Attributes.Add(BuildErrorCodeAttribute(442, "Unsupported Transport Protocol"));
                    return (errResponse, true);
                }
            }

            IPEndPoint relayEndpoint;

            if (isTcpRelay)
            {
                // Create TCP relay listener
                var tcpRelayListener = new TcpListener(IPAddress.Any, 0);
                tcpRelayListener.Start();
                relayEndpoint = (IPEndPoint)tcpRelayListener.LocalEndpoint;

                allocation = new TurnAllocation
                {
                    Id = clientId,
                    RelayEndPoint = relayEndpoint,
                    Expiry = DateTime.UtcNow.AddSeconds(_config.DefaultLifetimeSeconds),
                    TcpStream = tcpStream,
                    UdpClientEndPoint = udpClientEndPoint,
                    UdpControlSocket = udpControlSocket,
                    IsTcpRelay = true,
                    RelayTcpListener = tcpRelayListener,
                };

                _allocations[clientId] = allocation;

                // Start accepting peer TCP connections
                _ = AcceptTcpPeerConnectionsAsync(allocation);

                logger.LogInformation("TURN TCP relay allocation created for {Client}: relay port {Port}.",
                    clientId, relayEndpoint.Port);
            }
            else
            {
                // Create UDP relay socket
                var relaySocket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                relayEndpoint = (IPEndPoint)relaySocket.Client.LocalEndPoint;

                allocation = new TurnAllocation
                {
                    Id = clientId,
                    RelaySocket = relaySocket,
                    RelayEndPoint = relayEndpoint,
                    Expiry = DateTime.UtcNow.AddSeconds(_config.DefaultLifetimeSeconds),
                    TcpStream = tcpStream,
                    UdpClientEndPoint = udpClientEndPoint,
                    UdpControlSocket = udpControlSocket,
                };

                _allocations[clientId] = allocation;

                // Start relaying UDP → client
                _ = RelayUdpToClientAsync(allocation);

                logger.LogInformation("TURN UDP allocation created for {Client}: relay port {Port}.",
                    clientId, relayEndpoint.Port);
            }

            // Build success response
            var response = new STUNMessage(STUNMessageTypesEnum.AllocateSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;

            // XOR-RELAYED-ADDRESS
            response.Attributes.Add(new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORRelayedAddress,
                relayEndpoint.Port,
                _relayAddress,
                request.Header.TransactionId));

            // XOR-MAPPED-ADDRESS
            response.Attributes.Add(new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORMappedAddress,
                _config.Port,
                _relayAddress,
                request.Header.TransactionId));

            // LIFETIME
            response.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.Lifetime, (uint)_config.DefaultLifetimeSeconds));

            return (response, true);
        }

        private STUNMessage HandleRefresh(STUNMessage request, string clientId, ref TurnAllocation allocation)
        {
            if (allocation == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.RefreshErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(437, "Allocation Mismatch"));
                return errResponse;
            }

            // Extract requested lifetime
            var lifetimeAttr = request.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.Lifetime);
            uint lifetime = (uint)_config.DefaultLifetimeSeconds;
            if (lifetimeAttr?.Value != null && lifetimeAttr.Value.Length >= 4)
            {
                lifetime = (uint)((lifetimeAttr.Value[0] << 24) | (lifetimeAttr.Value[1] << 16) |
                                  (lifetimeAttr.Value[2] << 8) | lifetimeAttr.Value[3]);
            }

            if (lifetime == 0)
            {
                _allocations.TryRemove(allocation.Id, out _);
                allocation.Dispose();
                allocation = null;
                logger.LogInformation("TURN allocation deleted by refresh (lifetime=0) for {Client}.", clientId);
            }
            else
            {
                allocation.Expiry = DateTime.UtcNow.AddSeconds(lifetime);
            }

            var response = new STUNMessage(STUNMessageTypesEnum.RefreshSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;
            response.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, lifetime));
            return response;
        }

        private STUNMessage HandleCreatePermission(STUNMessage request, TurnAllocation allocation)
        {
            if (allocation == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.CreatePermissionErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(437, "Allocation Mismatch"));
                return errResponse;
            }

            foreach (var attr in request.Attributes.Where(
                a => a.AttributeType == STUNAttributeTypesEnum.XORPeerAddress))
            {
                var xorAddr = new STUNXORAddressAttribute(
                    STUNAttributeTypesEnum.XORPeerAddress,
                    attr.Value, request.Header.TransactionId);
                var peerIp = xorAddr.Address.ToString();
                allocation.Permissions[peerIp] = DateTime.UtcNow.AddSeconds(PERMISSION_LIFETIME_SECONDS);
                logger.LogDebug("TURN permission added: {Address} (expires in {Seconds}s).",
                    peerIp, PERMISSION_LIFETIME_SECONDS);
            }

            var response = new STUNMessage(STUNMessageTypesEnum.CreatePermissionSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;
            return response;
        }

        private STUNMessage HandleChannelBind(STUNMessage request, TurnAllocation allocation)
        {
            if (allocation == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ChannelBindErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(437, "Allocation Mismatch"));
                return errResponse;
            }

            var channelAttr = request.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ChannelNumber);
            if (channelAttr?.Value == null || channelAttr.Value.Length < 2)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ChannelBindErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(400, "Bad Request"));
                return errResponse;
            }

            var channelNumber = (ushort)((channelAttr.Value[0] << 8) | channelAttr.Value[1]);

            var peerAttr = request.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.XORPeerAddress);
            if (peerAttr?.Value == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ChannelBindErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(400, "Bad Request"));
                return errResponse;
            }

            var peerAddr = new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORPeerAddress,
                peerAttr.Value, request.Header.TransactionId);
            var peerEndpoint = new IPEndPoint(peerAddr.Address, peerAddr.Port);

            allocation.ChannelBindings[channelNumber] = peerEndpoint;
            allocation.ReverseChannelBindings[peerEndpoint.ToString()] = channelNumber;

            logger.LogDebug("TURN channel bind: 0x{Channel:X4} -> {Peer}.", channelNumber, peerEndpoint);

            var response = new STUNMessage(STUNMessageTypesEnum.ChannelBindSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;
            return response;
        }

        private void HandleSendIndication(STUNMessage msg, TurnAllocation allocation)
        {
            if (allocation == null) return;

            var peerAttr = msg.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.XORPeerAddress);
            var dataAttr = msg.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.Data);

            if (peerAttr?.Value == null || dataAttr?.Value == null) return;

            var peerAddr = new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORPeerAddress,
                peerAttr.Value, msg.Header.TransactionId);
            var peerEndpoint = new IPEndPoint(peerAddr.Address, peerAddr.Port);

            // Check permission before relaying
            if (!HasPermission(allocation, peerEndpoint.Address.ToString()))
            {
                logger.LogDebug("TURN SendIndication dropped: no permission for {Peer}.", peerEndpoint);
                return;
            }

            try
            {
                allocation.RelaySocket.Send(dataAttr.Value, dataAttr.Value.Length, peerEndpoint);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to relay UDP to {Peer}. {ErrorMessage}", peerEndpoint, ex.Message);
            }
        }

        /// <summary>
        /// Handles a Connect request (RFC 6062 Section 4.3).
        /// Client sends Connect(XOR-PEER-ADDRESS) → server opens TCP to peer.
        /// </summary>
        private STUNMessage HandleConnect(STUNMessage request, TurnAllocation allocation)
        {
            if (allocation == null || !allocation.IsTcpRelay)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(437, "Allocation Mismatch"));
                return errResponse;
            }

            var peerAttr = request.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.XORPeerAddress);
            if (peerAttr?.Value == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(400, "Bad Request"));
                return errResponse;
            }

            var peerAddr = new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORPeerAddress,
                peerAttr.Value, request.Header.TransactionId);
            var peerEndpoint = new IPEndPoint(peerAddr.Address, peerAddr.Port);

            // Check permission
            if (!HasPermission(allocation, peerEndpoint.Address.ToString()))
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(403, "Forbidden"));
                return errResponse;
            }

            // Check if a connection to this peer already exists
            if (allocation.TcpPeerConnections.Values.Any(c => c.PeerEndPoint.Equals(peerEndpoint)))
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(446, "Connection Already Exists"));
                return errResponse;
            }

            // Try to connect to peer
            TcpClient peerTcpClient;
            try
            {
                peerTcpClient = new TcpClient();
                if (!peerTcpClient.ConnectAsync(peerEndpoint.Address, peerEndpoint.Port)
                    .Wait(TimeSpan.FromSeconds(5)))
                {
                    peerTcpClient.Dispose();
                    var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectErrorResponse);
                    errResponse.Header.TransactionId = request.Header.TransactionId;
                    errResponse.Attributes.Add(BuildErrorCodeAttribute(447, "Connection Timeout or Failure"));
                    return errResponse;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "TURN Connect: failed to connect to peer {Peer}. {ErrorMessage}", peerEndpoint, ex.Message);
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(447, "Connection Timeout or Failure"));
                return errResponse;
            }

            var connectionId = allocation.AllocateConnectionId();
            var peerConnection = new TcpPeerConnection
            {
                ConnectionId = connectionId,
                PeerEndPoint = peerEndpoint,
                TcpClient = peerTcpClient,
                Stream = peerTcpClient.GetStream(),
            };

            allocation.TcpPeerConnections[connectionId] = peerConnection;

            logger.LogDebug("TURN Connect: connected to peer {Peer}, connectionId={ConnectionId}.",
                peerEndpoint, connectionId);

            var response = new STUNMessage(STUNMessageTypesEnum.ConnectSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;
            response.Attributes.Add(new STUNConnectionIdAttribute(connectionId));
            return response;
        }

        /// <summary>
        /// Handles a ConnectionBind request (RFC 6062 Section 4.4).
        /// Received on a new TCP data connection. Pairs this connection with an existing peer connection.
        /// </summary>
        private (STUNMessage response, bool connectionBound) HandleConnectionBind(
            STUNMessage request, TurnAllocation allocation, NetworkStream dataStream)
        {
            // ConnectionBind can arrive on a new TCP connection that has no allocation.
            // We need to find the allocation by looking up the connection ID across all allocations.
            var connectionIdAttr = request.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ConnectionId) as STUNConnectionIdAttribute;

            if (connectionIdAttr == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectionBindErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(400, "Bad Request"));
                return (errResponse, false);
            }

            uint connectionId = connectionIdAttr.ConnectionId;

            // Find the peer connection across all allocations
            TcpPeerConnection peerConnection = null;
            TurnAllocation ownerAllocation = allocation;

            if (ownerAllocation != null)
            {
                ownerAllocation.TcpPeerConnections.TryGetValue(connectionId, out peerConnection);
            }

            if (peerConnection == null)
            {
                // Search across all allocations if the data connection has no associated allocation
                foreach (var kvp in _allocations)
                {
                    if (kvp.Value.TcpPeerConnections.TryGetValue(connectionId, out peerConnection))
                    {
                        ownerAllocation = kvp.Value;
                        break;
                    }
                }
            }

            if (peerConnection == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectionBindErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(400, "Bad Request"));
                return (errResponse, false);
            }

            if (peerConnection.IsBound)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ConnectionBindErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(BuildErrorCodeAttribute(400, "Bad Request"));
                return (errResponse, false);
            }

            // Bind the data connection
            peerConnection.ClientDataStream = dataStream;
            peerConnection.IsBound = true;

            logger.LogDebug("TURN ConnectionBind: connectionId={ConnectionId} now bound for data relay.",
                connectionId);

            // Start bidirectional relay
            _ = RelayTcpBidirectionalAsync(peerConnection);

            var response = new STUNMessage(STUNMessageTypesEnum.ConnectionBindSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;
            return (response, true);
        }

        /// <summary>
        /// Accepts incoming TCP peer connections on a TCP relay listener (RFC 6062 Section 4.5).
        /// </summary>
        private async Task AcceptTcpPeerConnectionsAsync(TurnAllocation allocation)
        {
            try
            {
                while (!allocation.Cts.IsCancellationRequested)
                {
                    TcpClient peerClient;
                    try
                    {
                        peerClient = await allocation.RelayTcpListener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }

                    var peerEndpoint = (IPEndPoint)peerClient.Client.RemoteEndPoint;
                    var peerIp = peerEndpoint.Address.ToString();

                    // Check permissions
                    if (!HasPermission(allocation, peerIp))
                    {
                        logger.LogDebug("TURN TCP relay: rejected peer connection from {Peer} (no permission).", peerEndpoint);
                        peerClient.Dispose();
                        continue;
                    }

                    var connectionId = allocation.AllocateConnectionId();
                    var peerConnection = new TcpPeerConnection
                    {
                        ConnectionId = connectionId,
                        PeerEndPoint = peerEndpoint,
                        TcpClient = peerClient,
                        Stream = peerClient.GetStream(),
                    };

                    allocation.TcpPeerConnections[connectionId] = peerConnection;

                    logger.LogDebug("TURN TCP relay: peer connected from {Peer}, connectionId={ConnectionId}.",
                        peerEndpoint, connectionId);

                    // Send ConnectionAttemptIndication to the client
                    var indication = new STUNMessage(STUNMessageTypesEnum.ConnectionAttemptIndication);
                    indication.Attributes.Add(new STUNConnectionIdAttribute(connectionId));
                    indication.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
                    var indicationBytes = indication.ToByteBuffer(null, false);

                    await SendToClientAsync(allocation, indicationBytes).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "TURN TCP peer accept loop ended for allocation {Id}. {ErrorMessage}",
                    allocation.Id, ex.Message);
            }
        }

        /// <summary>
        /// Bidirectional raw byte relay between client data stream and peer stream (RFC 6062 Section 5.4).
        /// </summary>
        private async Task RelayTcpBidirectionalAsync(TcpPeerConnection connection)
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(connection.Cts.Token);

                var clientToPeer = CopyStreamAsync(connection.ClientDataStream, connection.Stream, linkedCts.Token);
                var peerToClient = CopyStreamAsync(connection.Stream, connection.ClientDataStream, linkedCts.Token);

                // When either direction closes, cancel both
                await Task.WhenAny(clientToPeer, peerToClient).ConfigureAwait(false);
                linkedCts.Cancel();

                logger.LogDebug("TURN TCP relay ended for connectionId={ConnectionId}.", connection.ConnectionId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "TURN TCP bidirectional relay error for connectionId={ConnectionId}. {ErrorMessage}",
                    connection.ConnectionId, ex.Message);
            }
        }

        private static async Task CopyStreamAsync(NetworkStream source, NetworkStream destination, CancellationToken ct)
        {
            var buffer = new byte[8192];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int bytesRead;
#if NET5_0_OR_GREATER
                    bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
#else
                    bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
#endif
                    if (bytesRead == 0) break;

#if NET5_0_OR_GREATER
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
#else
                    await destination.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
#endif
                    await destination.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (System.IO.IOException) { }
        }

        private void HandleChannelData(TurnAllocation allocation, ushort channelNumber, byte[] data)
        {
            if (allocation == null) return;

            if (allocation.ChannelBindings.TryGetValue(channelNumber, out var peer))
            {
                try
                {
                    allocation.RelaySocket.Send(data, data.Length, peer);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to relay channel data to {Peer}. {ErrorMessage}", peer, ex.Message);
                }
            }
        }

        #endregion

        #region Relay (peer → client)

        private async Task RelayUdpToClientAsync(TurnAllocation allocation)
        {
            try
            {
                while (!allocation.Cts.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await allocation.RelaySocket.ReceiveAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { break; }

                    var senderIp = result.RemoteEndPoint.Address.ToString();
                    var senderKey = result.RemoteEndPoint.ToString();

                    // Enforce permissions: drop if sender IP not permitted (RFC 5766 Section 8)
                    if (!HasPermission(allocation, senderIp))
                    {
                        logger.LogDebug("TURN relay dropped packet from {Sender}: no permission.", senderKey);
                        continue;
                    }

                    // Try to send as ChannelData if there's a binding
                    if (allocation.ReverseChannelBindings.TryGetValue(senderKey, out var channelNum))
                    {
                        var channelData = BuildChannelData(channelNum, result.Buffer);
                        await SendToClientAsync(allocation, channelData).ConfigureAwait(false);
                    }
                    else
                    {
                        // Send as DataIndication
                        var indication = new STUNMessage(STUNMessageTypesEnum.DataIndication);
                        indication.AddXORPeerAddressAttribute(
                            result.RemoteEndPoint.Address, result.RemoteEndPoint.Port);
                        indication.Attributes.Add(new STUNAttribute(
                            STUNAttributeTypesEnum.Data, result.Buffer));
                        var bytes = indication.ToByteBuffer(null, false);
                        await SendToClientAsync(allocation, bytes).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "UDP relay loop ended for allocation {Id}. {ErrorMessage}",
                    allocation.Id, ex.Message);
            }
        }

        private static async Task SendToClientAsync(TurnAllocation allocation, byte[] data)
        {
            try
            {
                if (allocation.TcpStream != null)
                {
                    await allocation.TcpStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                }
                else if (allocation.UdpControlSocket != null && allocation.UdpClientEndPoint != null)
                {
                    await allocation.UdpControlSocket.SendAsync(
                        data, data.Length, allocation.UdpClientEndPoint).ConfigureAwait(false);
                }
            }
            catch { }
        }

        #endregion

        #region Helpers

        private static bool HasPermission(TurnAllocation allocation, string peerIp)
        {
            if (allocation.Permissions.TryGetValue(peerIp, out var expiry))
            {
                return DateTime.UtcNow < expiry;
            }
            return false;
        }

        /// <summary>
        /// Build an ERROR-CODE attribute manually (RFC 5389 Section 15.6).
        /// Workaround: SIPSorcery's STUNErrorCodeAttribute(int, string) constructor has a bug
        /// where it reads the ErrorCode property (which depends on ErrorClass) before ErrorClass
        /// is assigned, and also doesn't populate the base Value field.
        /// See: https://github.com/sipsorcery-org/sipsorcery/pull/1509
        /// </summary>
        internal static STUNAttribute BuildErrorCodeAttribute(int errorCode, string reasonPhrase)
        {
            var reasonBytes = Encoding.UTF8.GetBytes(reasonPhrase);
            var value = new byte[4 + reasonBytes.Length];
            value[2] = (byte)(errorCode / 100);
            value[3] = (byte)(errorCode % 100);
            Buffer.BlockCopy(reasonBytes, 0, value, 4, reasonBytes.Length);
            return new STUNAttribute(STUNAttributeTypesEnum.ErrorCode, value);
        }

        /// <summary>
        /// Verify MESSAGE-INTEGRITY directly on raw STUN bytes per RFC 5389 Section 15.4.
        /// Workaround: SIPSorcery's CheckIntegrity() requires a valid FINGERPRINT attribute,
        /// which browsers may not send for TURN messages.
        /// See: https://github.com/sipsorcery-org/sipsorcery/pull/1510
        /// </summary>
        internal bool VerifyMessageIntegrity(byte[] rawBytes, byte[] hmacKey)
        {
            if (rawBytes.Length < 24) return false; // minimum: 20-byte header + 4-byte attr header

            // Walk attributes in the raw bytes to find MESSAGE-INTEGRITY (type 0x0008)
            int offset = 20; // skip STUN header
            int totalLength = rawBytes.Length;

            while (offset + 4 <= totalLength)
            {
                ushort attrType = (ushort)((rawBytes[offset] << 8) | rawBytes[offset + 1]);
                ushort attrLength = (ushort)((rawBytes[offset + 2] << 8) | rawBytes[offset + 3]);
                int paddedLength = (attrLength + 3) & ~3;

                if (attrType == 0x0008) // MESSAGE-INTEGRITY
                {
                    if (attrLength != 20) return false; // HMAC-SHA1 is always 20 bytes
                    if (offset + 4 + 20 > totalLength) return false;

                    // Extract the stored HMAC value
                    var storedHmac = new byte[20];
                    Buffer.BlockCopy(rawBytes, offset + 4, storedHmac, 0, 20);

                    // Build the pre-image: raw bytes up to MESSAGE-INTEGRITY
                    var preImage = new byte[offset];
                    Buffer.BlockCopy(rawBytes, 0, preImage, 0, offset);

                    // Patch MessageLength field: must reflect message as if MI were the last attribute
                    ushort adjustedLength = (ushort)(offset - 20 + 24);
                    preImage[2] = (byte)(adjustedLength >> 8);
                    preImage[3] = (byte)(adjustedLength & 0xFF);

                    // Compute HMAC-SHA1
#if NET6_0_OR_GREATER
                    byte[] computed = HMACSHA1.HashData(hmacKey, preImage);
#else
                    byte[] computed;
                    using (var hmac = new HMACSHA1(hmacKey))
                    {
                        computed = hmac.ComputeHash(preImage);
                    }
#endif

                    // Constant-time comparison
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    bool match = CryptographicOperations.FixedTimeEquals(computed, storedHmac);
#else
                    if (computed.Length != storedHmac.Length) return false;
                    int diff = 0;
                    for (int i = 0; i < computed.Length; i++)
                    {
                        diff |= computed[i] ^ storedHmac[i];
                    }
                    bool match = diff == 0;
#endif

                    if (!match)
                    {
                        logger.LogDebug("TURN integrity mismatch.");
                    }

                    return match;
                }

                offset += 4 + paddedLength;
            }

            return false; // No MESSAGE-INTEGRITY attribute found
        }

        private static byte[] BuildChannelData(ushort channelNumber, byte[] data)
        {
            var dataLen = data.Length;
            var padding = (4 - (dataLen % 4)) % 4;
            var buf = new byte[4 + dataLen + padding];
            buf[0] = (byte)(channelNumber >> 8);
            buf[1] = (byte)(channelNumber & 0xFF);
            buf[2] = (byte)(dataLen >> 8);
            buf[3] = (byte)(dataLen & 0xFF);
            Buffer.BlockCopy(data, 0, buf, 4, dataLen);
            return buf;
        }

        private static string GenerateNonce()
        {
            var nonceBytes = new byte[16];
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            RandomNumberGenerator.Fill(nonceBytes);
#else
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(nonceBytes);
            }
#endif

#if NET5_0_OR_GREATER
            return Convert.ToHexString(nonceBytes).ToLowerInvariant();
#else
            var sb = new StringBuilder(nonceBytes.Length * 2);
            for (int i = 0; i < nonceBytes.Length; i++)
            {
                sb.Append(nonceBytes[i].ToString("x2"));
            }
            return sb.ToString();
#endif
        }

        private void CleanExpiredAllocations(object state)
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _allocations)
            {
                var allocation = kvp.Value;

                if (now > allocation.Expiry)
                {
                    if (_allocations.TryRemove(kvp.Key, out var removed))
                    {
                        removed.Dispose();
                        logger.LogInformation("TURN allocation expired and removed: {Id}.", kvp.Key);
                    }
                }
                else
                {
                    // Clean expired permissions
                    foreach (var perm in allocation.Permissions)
                    {
                        if (now > perm.Value)
                        {
                            allocation.Permissions.TryRemove(perm.Key, out _);
                        }
                    }
                }
            }
        }

        #endregion
    }
}
