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

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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
        /// The address advertised in XOR-RELAYED-ADDRESS responses. Set to a public IP when the server is behind NAT.
        /// Defaults to <see cref="ListenAddress"/>.
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

        /// <summary>
        /// Optional shared secret enabling REST-style ephemeral credentials (draft-uberti-behave-turn-rest, also
        /// referenced by RFC 8489 Section 9.2). When set, <see cref="Username"/> / <see cref="Password"/> are ignored.
        /// Clients must present <c> USERNAME = "{unix-expiry}:{userId}"</c> and <c> PASSWORD =
        /// base64(HMAC-SHA1(StaticAuthSecret, "USERNAME:REALM"))</c>. The expiry is enforced and expired credentials
        /// are rejected with 401 Unauthorized.
        /// </summary>
        public string StaticAuthSecret { get; set; }

        /// <summary>
        /// Inclusive lower bound for the relay UDP port. If both <see cref="RelayPortMin"/> and
        /// <see cref="RelayPortMax"/> are zero (the default) the relay socket is bound to an ephemeral port chosen by
        /// the OS.
        /// </summary>
        public int RelayPortMin { get; set; } = 0;

        /// <summary>
        /// Inclusive upper bound for the relay UDP port. See <see cref="RelayPortMin"/>.
        /// </summary>
        public int RelayPortMax { get; set; } = 0;
    }

    /// <summary>
    /// Represents a TURN allocation — the server-side state for a single client's relay session.
    /// </summary>
    public class TurnAllocation : IDisposable
    {
        /// <summary>Unique identifier (typically the client's remote endpoint string).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The UDP socket used to relay data to/from peers.</summary>
        public UdpClient RelaySocket { get; set; }

        /// <summary>The relay endpoint (IP + port) advertised to the client.</summary>
        public IPEndPoint RelayEndPoint { get; set; }

        /// <summary>When this allocation expires (UTC).</summary>
        public DateTime Expiry { get; set; }

        /// <summary>
        /// Installed permissions: peer IP address → expiry time. Per RFC 5766 Section 8, permissions expire after 300
        /// seconds.
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

        // Internal: HMAC key used to sign responses for this allocation. Pre-computed once at
        // allocation time so REST/ephemeral creds (per-user key) and long-term creds (shared
        // key) can be handled uniformly without re-deriving on every message.
        internal byte[] HmacKey { get; set; }

        public void Dispose()
        {
            try { Cts.Cancel(); } catch { }
            try { Cts.Dispose(); } catch { }
            try { RelaySocket?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// A lightweight TURN relay server (RFC 5766) supporting TCP and UDP control channels. Provides NAT traversal by
    /// relaying UDP traffic between clients and peers. Intended for development, testing, and small-scale/embedded
    /// scenarios — not for production use at scale (use coturn or similar for that).
    /// </summary>
    /// <remarks>
    /// <para> <strong> Known limitations (contributions welcome):</strong></para> <list type="bullet"> <item> Two
    /// credential modes: a single long-term username/password, or REST-style ephemeral credentials
    /// (draft-uberti-behave-turn-rest / RFC 8489 Section 9.2) when <see cref="TurnServerConfig.StaticAuthSecret"/> is
    /// set. There is no per-user credential database for non-REST deployments.</item> <item> No nonce validation/expiry
    /// — nonces are generated but never verified on subsequent requests, so replay attacks are possible within the
    /// allocation lifetime.</item> <item> No rate limiting or per-IP allocation caps — a misbehaving client can exhaust
    /// server resources.</item> <item> No TLS/DTLS for the control channel — credentials are sent in the clear unless
    /// the transport is already secured.</item> <item> UDP-only relay — the relay leg is always UDP; no TCP relay (RFC
    /// 6062) or TURN-over-TLS (RFC 5766 Section 6).</item> <item> No REQUESTED-TRANSPORT validation — the attribute is
    /// ignored entirely.</item> <item> No EVEN-PORT / RESERVATION-TOKEN support.</item> <item> IPv4 only (no IPv6 relay
    /// addresses).</item> <item> Allocation lifetime is not capped — clients can request arbitrarily long
    /// lifetimes.</item> <item> No ALTERNATE-SERVER support.</item> </list> <para> <strong> Security
    /// considerations:</strong></para> <list type="bullet"> <item> Default credentials (<c>turn-user</c> / <c>
    /// turn-pass</c>) — callers MUST configure real credentials; defaults are intentionally weak to encourage
    /// replacement.</item> <item> Default listen address is loopback — safe by default, but if bound to a public
    /// interface without TLS, credentials travel in cleartext.</item> <item> No input validation on allocation count or
    /// relay port range — in production you would want to bound these.</item> </list>
    /// </remarks>
    /// <example>
    /// <code> var server = new TurnServer(new TurnServerConfig { ListenAddress = IPAddress.Loopback, Port = 3478,
    /// Username = "user", Password = "pass", Realm = "example.com" }); server.Start(); // ... server is running ...
    /// server.Dispose(); // or server.Stop(); </code>
    /// </example>
    public class TurnServer : IDisposable
    {
        private const int PERMISSION_LIFETIME_SECONDS = 300; // RFC 5766 Section 8
        private const int CLEANUP_INTERVAL_SECONDS = 30;

        private static readonly ILogger logger = LogFactory.CreateLogger<TurnServer>();

        private readonly TurnServerConfig _config;
        private readonly byte[] _hmacKey;
        private readonly IPAddress _relayAddress;
        private readonly bool _useStaticAuthSecret;
        private readonly byte[] _realmBytes;
        private readonly byte[] _staticAuthSecretBytes;
        private int _nextRelayPortOffset = -1;

        private TcpListener _tcpListener;
        private UdpClient _udpSocket;
        private Timer _cleanupTimer;
        private volatile bool _running;

        private readonly ConcurrentDictionary<string, TurnAllocation> _allocations =
            new ConcurrentDictionary<string, TurnAllocation>();

        // Cache of local interface IPv4 addresses, used by TranslateLocalSource to
        // recognize hairpinned relay packets. Refreshed lazily — interfaces don't change
        // often enough to warrant locking on the hot path.
        private HashSet<IPAddress> _localIPv4Cache;
        private DateTime _localIPv4CacheExpiry = DateTime.MinValue;

        /// <summary>
        /// Gets a read-only view of current allocations.
        /// </summary>
        public IReadOnlyDictionary<string, TurnAllocation> Allocations => _allocations;

        /// <summary>
        /// Translates a packet's observed source endpoint into the advertised relay endpoint when the source is one of
        /// this server's own relay sockets. Returns <c> null</c> if the endpoint isn't recognized as a local relay.
        /// This is the hook that makes hairpinning work when a peer on the same machine as the TURN server uses one of
        /// its allocations: the OS picks a local interface address as the source IP, which differs from the public IP
        /// advertised in <c> XOR-RELAYED-ADDRESS</c>. Wiring this method into <c>
        /// RTCPeerConnection.RemoteEndpointTranslator</c> lets the ICE source filter and candidate matcher reconcile
        /// the two views.
        /// </summary>
        public IPEndPoint TranslateLocalSource(IPEndPoint observedSource)
        {
            if (observedSource == null)
            {
                return null;
            }

            // Normalize IPv4-mapped IPv6 addresses to pure IPv4 for the comparison.
            var addr = observedSource.Address.IsIPv4MappedToIPv6
                ? observedSource.Address.MapToIPv4()
                : observedSource.Address;

            if (!IsLocalIPv4(addr))
            {
                return null;
            }

            // Does the port match one of our current allocations' relay ports?
            // Iteration is fine — allocation counts in the small-scale deployments this
            // server targets are well under the threshold where a hashed index would matter.
            foreach (var alloc in _allocations.Values)
            {
                if (alloc.RelayEndPoint?.Port == observedSource.Port)
                {
                    return new IPEndPoint(_relayAddress, observedSource.Port);
                }
            }

            return null;
        }

        private bool IsLocalIPv4(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            // Refresh the cache every 60 seconds to pick up interface changes (VPNs, etc).
            if (_localIPv4Cache == null || DateTime.UtcNow > _localIPv4CacheExpiry)
            {
                try
                {
                    _localIPv4Cache = new HashSet<IPAddress>(
                        NetworkInterface.GetAllNetworkInterfaces()
                            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                            .Where(uni => uni.Address.AddressFamily == AddressFamily.InterNetwork)
                            .Select(uni => uni.Address));
                }
                catch
                {
                    _localIPv4Cache = new HashSet<IPAddress>();
                }
                _localIPv4CacheExpiry = DateTime.UtcNow.AddSeconds(60);
            }

            return _localIPv4Cache.Contains(address);
        }

        /// <summary>
        /// Creates a new TURN server with the specified configuration.
        /// </summary>
        /// <param name="config">Server configuration.</param>
        public TurnServer(TurnServerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _relayAddress = config.RelayAddress ?? config.ListenAddress;
            _useStaticAuthSecret = !string.IsNullOrEmpty(_config.StaticAuthSecret);
            _realmBytes = Encoding.UTF8.GetBytes(_config.Realm);

            if (_useStaticAuthSecret)
            {
                _staticAuthSecretBytes = Encoding.UTF8.GetBytes(_config.StaticAuthSecret);
            }
            else
            {
                // Long-term credential mode: HMAC key = MD5(username:realm:password)
                _hmacKey = DeriveLongTermKey(_config.Username, _config.Realm, _config.Password);
            }
            // REST mode (StaticAuthSecret set): _hmacKey stays null; the per-request key is
            // derived from the USERNAME attribute when an Allocate request arrives.
        }

        private static byte[] DeriveLongTermKey(string username, string realm, string password)
        {
            var input = Encoding.UTF8.GetBytes($"{username}:{realm}:{password}");
#if NET5_0_OR_GREATER
            return MD5.HashData(input);
#else
            using (var md5 = MD5.Create())
            {
                return md5.ComputeHash(input);
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
            var clientEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
            var clientId = clientEndPoint?.ToString() ?? "unknown";
            var stream = tcpClient.GetStream();
            var header = new byte[4];
            var paddingBuffer = new byte[3];
            TurnAllocation allocation = null;

            try
            {
                while (_running && tcpClient.Connected)
                {
                    // TCP framing: read first 4 bytes to determine STUN message vs ChannelData.
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
                            if (!await ReadExactAsync(stream, paddingBuffer, 0, padding).ConfigureAwait(false))
                                break;
                        }

                        HandleChannelData(allocation, channelNumber, data, 0, data.Length);
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

                        var stunMsg = STUNMessage.ParseSTUNMessage(fullMsg.AsSpan(0, fullMsg.Length));
                        if (stunMsg == null)
                        {
                            logger.LogWarning("Failed to parse STUN message from TCP client {Client}.", clientId);
                            continue;
                        }

                        ProcessMessage(stunMsg, clientId, clientEndPoint,
                            (responseBytes) => SendTcpResponseAsync(stream, responseBytes),
                            ref allocation,
                            stream, null, null);
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
                if (allocation != null)
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
                    // Find the allocation for this client
                    if (_allocations.TryGetValue(clientId, out var allocation))
                    {
                        HandleChannelData(allocation, channelNumber, data, 4, dataLength);
                    }
                }
                return;
            }

            var stunMsg = STUNMessage.ParseSTUNMessage(data.AsSpan(0, data.Length));
            if (stunMsg == null)
            {
                logger.LogWarning("Failed to parse STUN message from UDP client {Client}.", clientId);
                return;
            }

            _allocations.TryGetValue(clientId, out var udpAllocation);

            ProcessMessage(stunMsg, clientId, remoteEndPoint,
                (responseBytes) => SendUdpResponseAsync(remoteEndPoint, responseBytes),
                ref udpAllocation,
                null, remoteEndPoint, _udpSocket);
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

        private void ProcessMessage(
            STUNMessage msg,
            string clientId,
            IPEndPoint clientEndPoint,
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
                        var response = HandleBindingRequest(msg, clientEndPoint);
                        var bytes = new byte[response.GetByteBufferSize(null, false)];
                        response.WriteToBuffer(bytes, null, false);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.Allocate:
                    {
                        var (response, signingKey) = HandleAllocate(msg, clientId, clientEndPoint,
                            tcpStream, udpClientEndPoint, udpControlSocket,
                            ref allocation);
                        var includeIntegrity = signingKey != null;
                        var bytes = new byte[response.GetByteBufferSize(signingKey, includeIntegrity)];
                        response.WriteToBuffer(bytes, signingKey, includeIntegrity);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.Refresh:
                    {
                        var response = HandleRefresh(msg, clientId, ref allocation);
                        var bytes = SignResponse(response, allocation);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.CreatePermission:
                    {
                        var response = HandleCreatePermission(msg, allocation);
                        var bytes = SignResponse(response, allocation);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.ChannelBind:
                    {
                        var response = HandleChannelBind(msg, allocation);
                        var bytes = SignResponse(response, allocation);
                        _ = sendResponse(bytes);
                    }
                    break;

                case STUNMessageTypesEnum.SendIndication:
                    HandleSendIndication(msg, allocation);
                    break; // Indications get no response

                default:
                    logger.LogWarning("Unhandled STUN message type: {Type}.", msgType);
                    break;
            }
        }

        private STUNMessage HandleBindingRequest(STUNMessage request, IPEndPoint clientEndPoint)
        {
            var response = new STUNMessage(STUNMessageTypesEnum.BindingSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;
            // The whole point of a Binding response is to tell the client its reflexive
            // transport address as seen by the server — not the server's own address.
            if (clientEndPoint != null)
            {
                response.AddXORMappedAddressAttribute(clientEndPoint.Address, clientEndPoint.Port);
            }
            return response;
        }

        /// <summary>
        /// Serialize a response, signing it with the allocation's cached HMAC key when available, falling back to the
        /// server's static key (long-term cred mode). In REST mode without a known allocation the response goes out
        /// unsigned — the client will retry with fresh credentials anyway.
        /// </summary>
        private byte[] SignResponse(STUNMessage response, TurnAllocation allocation)
        {
            var key = allocation?.HmacKey ?? _hmacKey;
            var includeIntegrity = key != null;
            var bytes = new byte[response.GetByteBufferSize(key, includeIntegrity)];
            response.WriteToBuffer(bytes, key, includeIntegrity);
            return bytes;
        }

        /// <summary>
        /// In REST mode, derive the per-user long-term HMAC key from the USERNAME in the request and validate the
        /// embedded expiry. Returns false (with rejectReason populated) when the credential is malformed or expired.
        /// </summary>
        private bool TryDeriveRestKey(STUNMessage request, out byte[] key, out string rejectReason)
        {
            key = null;
            rejectReason = null;

            var usernameAttr = request.GetFirstAttribute(STUNAttributeTypesEnum.Username);
            if (usernameAttr?.Value == null || usernameAttr.Value.Length == 0)
            {
                rejectReason = "missing USERNAME";
                return false;
            }

            var username = Encoding.UTF8.GetString(usernameAttr.Value.Span);
            var colonIdx = username.IndexOf(':');
            if (colonIdx <= 0)
            {
                rejectReason = "USERNAME not in '<expiry>:<user>' form";
                return false;
            }

            if (!long.TryParse(username.Substring(0, colonIdx), out var expiryUnix))
            {
                rejectReason = "USERNAME expiry is not a unix timestamp";
                return false;
            }

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (expiryUnix <= nowUnix)
            {
                rejectReason = "credential expired";
                return false;
            }

            // Compute the REST password: base64(HMAC-SHA1(staticAuthSecret, "USERNAME:REALM"))
            var msgBytes = Encoding.UTF8.GetBytes($"{username}:{_config.Realm}");
            string password;
#if NET6_0_OR_GREATER
            password = Convert.ToBase64String(HMACSHA1.HashData(_staticAuthSecretBytes, msgBytes));
#else
            using (var hmac = new HMACSHA1(_staticAuthSecretBytes))
            {
                password = Convert.ToBase64String(hmac.ComputeHash(msgBytes));
            }
#endif

            key = DeriveLongTermKey(username, _config.Realm, password);
            return true;
        }

        private (STUNMessage response, byte[] signingKey) HandleAllocate(
            STUNMessage request,
            string clientId,
            IPEndPoint clientEndPoint,
            NetworkStream tcpStream,
            IPEndPoint udpClientEndPoint,
            UdpClient udpControlSocket,
            ref TurnAllocation allocation)
        {
            // Check for MESSAGE-INTEGRITY — first request won't have it
            var hasIntegrity = HasAttribute(request, STUNAttributeTypesEnum.MessageIntegrity);

            if (!hasIntegrity)
            {
                // Send 401 Unauthorized with REALM and NONCE (unsigned)
                return (BuildAuthChallenge(request), null);
            }

            // Derive the HMAC key for this request. In long-term cred mode this is the static
            // _hmacKey; in REST mode it depends on the USERNAME attribute (and we also
            // validate the embedded expiry timestamp here).
            byte[] requestKey;
            if (_useStaticAuthSecret)
            {
                if (!TryDeriveRestKey(request, out requestKey, out var reason))
                {
                    logger.LogWarning("TURN Allocate: REST credential rejected from {Client}: {Reason}.",
                        clientId, reason);
                    return (BuildAuthChallenge(request), null);
                }
            }
            else
            {
                requestKey = _hmacKey;
            }

            if (!request.CheckIntegrity(requestKey))
            {
                logger.LogWarning("TURN Allocate: integrity check failed from {Client}.", clientId);
                var errResponse = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(new STUNErrorCodeAttribute(401, "Unauthorized"));
                return (errResponse, null);
            }

            // Check if there's already an allocation for this client
            if (allocation != null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(new STUNErrorCodeAttribute(437, "Allocation Mismatch"));
                return (errResponse, requestKey);
            }

            // Create the UDP relay socket — within the configured port range if set, else any.
            if (!TryBindRelaySocket(out var relaySocket))
            {
                logger.LogWarning("TURN Allocate: no free relay port in [{Min}..{Max}] for {Client}.",
                    _config.RelayPortMin, _config.RelayPortMax, clientId);
                var errResponse = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(new STUNErrorCodeAttribute(508, "Insufficient Capacity"));
                return (errResponse, requestKey);
            }
            var relayEndpoint = (IPEndPoint)relaySocket.Client.LocalEndPoint;

            allocation = new TurnAllocation
            {
                Id = clientId,
                RelaySocket = relaySocket,
                RelayEndPoint = relayEndpoint,
                Expiry = DateTime.UtcNow.AddSeconds(_config.DefaultLifetimeSeconds),
                TcpStream = tcpStream,
                UdpClientEndPoint = udpClientEndPoint,
                UdpControlSocket = udpControlSocket,
                HmacKey = requestKey,
            };

            _allocations[clientId] = allocation;

            // Start relaying UDP → client
            _ = RelayUdpToClientAsync(allocation);

            logger.LogInformation("TURN allocation created for {Client}: relay port {Port}.",
                clientId, relayEndpoint.Port);

            // Build success response
            var response = new STUNMessage(STUNMessageTypesEnum.AllocateSuccessResponse);
            response.Header.TransactionId = request.Header.TransactionId;

            // XOR-RELAYED-ADDRESS
            response.Attributes.Add(new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORRelayedAddress,
                relayEndpoint.Port,
                _relayAddress,
                request.Header.TransactionId));

            // XOR-MAPPED-ADDRESS — per RFC 5766 §6.3, this is the client's reflexive
            // transport address (the source of the Allocate request as the server saw it),
            // not the server's own address.
            if (clientEndPoint != null)
            {
                response.Attributes.Add(new STUNXORAddressAttribute(
                    STUNAttributeTypesEnum.XORMappedAddress,
                    clientEndPoint.Port,
                    clientEndPoint.Address,
                    request.Header.TransactionId));
            }

            // LIFETIME
            response.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.Lifetime, (uint)_config.DefaultLifetimeSeconds));

            return (response, requestKey);
        }

        private STUNMessage BuildAuthChallenge(STUNMessage request)
        {
            var errResponse = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
            errResponse.Header.TransactionId = request.Header.TransactionId;
            errResponse.Attributes.Add(new STUNErrorCodeAttribute(401, "Unauthorized"));
            errResponse.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                _realmBytes));
            errResponse.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                Encoding.UTF8.GetBytes(GenerateNonce())));
            return errResponse;
        }

        /// <summary>
        /// Bind the relay UDP socket. If a relay port range is configured walk it in order and bind to the first free
        /// port; if no range is set let the OS pick an ephemeral port. Returns false when a range was set but every
        /// port in it is occupied.
        /// </summary>
        private bool TryBindRelaySocket(out UdpClient socket)
        {
            socket = null;
            if (_config.RelayPortMin <= 0 || _config.RelayPortMax < _config.RelayPortMin)
            {
                socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                return true;
            }

            var portCount = _config.RelayPortMax - _config.RelayPortMin + 1;
            var startOffset = (uint)Interlocked.Increment(ref _nextRelayPortOffset);

            for (int i = 0; i < portCount; i++)
            {
                var port = _config.RelayPortMin + (int)((startOffset + (uint)i) % (uint)portCount);
                try
                {
                    socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                    return true;
                }
                catch (SocketException)
                {
                    // Port in use — try the next one.
                }
            }
            return false;
        }

        private STUNMessage HandleRefresh(STUNMessage request, string clientId, ref TurnAllocation allocation)
        {
            if (allocation == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.RefreshErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(new STUNErrorCodeAttribute(437, "Allocation Mismatch"));
                return errResponse;
            }

            // Extract requested lifetime
            var lifetimeAttr = request.GetFirstAttribute(STUNAttributeTypesEnum.Lifetime);
            uint lifetime = (uint)_config.DefaultLifetimeSeconds;
            if (lifetimeAttr?.Value != null && lifetimeAttr.Value.Length >= 4)
            {
                lifetime = (uint)((lifetimeAttr.Value.Span[0] << 24) | (lifetimeAttr.Value.Span[1] << 16) |
                                  (lifetimeAttr.Value.Span[2] << 8) | lifetimeAttr.Value.Span[3]);
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
                errResponse.Attributes.Add(new STUNErrorCodeAttribute(437, "Allocation Mismatch"));
                return errResponse;
            }

            var permissionExpiry = DateTime.UtcNow.AddSeconds(PERMISSION_LIFETIME_SECONDS);
            foreach (var attr in request.Attributes)
            {
                if (attr.AttributeType != STUNAttributeTypesEnum.XORPeerAddress)
                {
                    continue;
                }

                var xorAddr = new STUNXORAddressAttribute(
                    STUNAttributeTypesEnum.XORPeerAddress,
                    attr.Value, request.Header.TransactionId);
                var peerIp = xorAddr.Address.ToString();
                allocation.Permissions[peerIp] = permissionExpiry;
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
                errResponse.Attributes.Add(new STUNErrorCodeAttribute(437, "Allocation Mismatch"));
                return errResponse;
            }

            var channelAttr = request.GetFirstAttribute(STUNAttributeTypesEnum.ChannelNumber);
            if (channelAttr?.Value == null || channelAttr.Value.Length < 2)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ChannelBindErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(new STUNErrorCodeAttribute(400, "Bad Request"));
                return errResponse;
            }

            var channelNumber = (ushort)((channelAttr.Value.Span[0] << 8) | channelAttr.Value.Span[1]);

            var peerAttr = request.GetFirstAttribute(STUNAttributeTypesEnum.XORPeerAddress);
            if (peerAttr?.Value == null)
            {
                var errResponse = new STUNMessage(STUNMessageTypesEnum.ChannelBindErrorResponse);
                errResponse.Header.TransactionId = request.Header.TransactionId;
                errResponse.Attributes.Add(new STUNErrorCodeAttribute(400, "Bad Request"));
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

            var peerAttr = msg.GetFirstAttribute(STUNAttributeTypesEnum.XORPeerAddress);
            var dataAttr = msg.GetFirstAttribute(STUNAttributeTypesEnum.Data);

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
                allocation.RelaySocket.Send(dataAttr.Value.Span, peerEndpoint);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to relay UDP to {Peer}. {ErrorMessage}", peerEndpoint, ex.Message);
            }
        }

        private void HandleChannelData(
            TurnAllocation allocation,
            ushort channelNumber,
            byte[] data,
            int offset,
            int length)
        {
            if (allocation == null) return;

            if (allocation.ChannelBindings.TryGetValue(channelNumber, out var peer))
            {
                try
                {
                    if (offset == 0 && length == data.Length)
                    {
                        allocation.RelaySocket.Send(data, length, peer);
                    }
                    else
                    {
                        allocation.RelaySocket.Client.SendTo(
                            data, offset, length, SocketFlags.None, peer);
                    }
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

                    var now = DateTime.UtcNow;
                    var senderIp = result.RemoteEndPoint.Address.ToString();
                    var senderKey = result.RemoteEndPoint.ToString();

                    // Enforce permissions: drop if sender IP not permitted (RFC 5766 Section 8)
                    if (!HasPermission(allocation, senderIp, now))
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
                        var bytes = new byte[indication.GetByteBufferSize(null, false)];
                        indication.WriteToBuffer(bytes, null, false);
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
            return HasPermission(allocation, peerIp, DateTime.UtcNow);
        }

        private static bool HasPermission(TurnAllocation allocation, string peerIp, DateTime now)
        {
            if (allocation.Permissions.TryGetValue(peerIp, out var expiry))
            {
                if (now < expiry)
                {
                    return true;
                }

                allocation.Permissions.TryRemove(peerIp, out _);
            }
            return false;
        }

        private static bool HasAttribute(STUNMessage message, STUNAttributeTypesEnum attributeType)
        {
            foreach (var attribute in message.Attributes)
            {
                if (attribute.AttributeType == attributeType)
                {
                    return true;
                }
            }

            return false;
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
