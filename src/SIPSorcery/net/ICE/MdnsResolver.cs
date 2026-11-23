//-----------------------------------------------------------------------------
// Filename: MdnsResolver.cs
//
// Description: Multicast DNS (RFC 6762) hostname resolver used by
// RtpIceChannel to look up the .local hostnames Chrome (and other WebRTC
// stacks) publish as ICE candidates for privacy reasons.
//
// .NET's Dns.GetHostAddressesAsync does not reliably do mDNS on most
// platforms -- on Windows it depends on whether the OS-level mDNS resolver
// is enabled and whether the multicast packet round-trips through the
// firewall in time. The previous fallback in RtpIceChannel.ResolveMdnsName
// surfaced this as
//
//   System.Net.Sockets.SocketException (11001): No such host is known.
//
// every time a Chrome peer was used, breaking browser-vs-SIPSorcery
// WebRTC interop on most Windows installs.
//
// This class sends an actual mDNS query (UDP/5353 multicast to 224.0.0.251
// and ff02::fb on every multicast capable interface) for the requested
// hostname and returns whatever A / AAAA answers arrive within a bounded
// timeout. It only implements the small slice of mDNS needed for one-shot
// hostname lookups: no responder, no caching and no service discovery. It
// previously wrapped Makaretu.Dns.Multicast but that package is no longer
// maintained and drags in vulnerable transitive dependencies (see #1780).
//
// The sockets are cached for the lifetime of the process to avoid the
// per-query cost of rejoining the multicast group on every NIC.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com) + Claude Opus 4.7.
//
// History:
// 03 May 2026  Claude          Created.
// 24 Oct 2026  Claude          Replaced Makaretu.Dns.Multicast with a built-in query client.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    /// <summary>
    /// Resolves multicast-DNS (.local) hostnames by sending an RFC 6762 query
    /// and collecting the A / AAAA answers.
    /// Used by <see cref="RtpIceChannel"/> as the default fallback when the
    /// caller has not supplied its own
    /// <see cref="RtpIceChannel.MdnsGetAddresses"/> /
    /// <see cref="RtpIceChannel.MdnsResolve"/> hook.
    /// </summary>
    internal sealed class MdnsResolver
    {
        // Helper-only class with all static members; prevent instantiation.
        // Cannot be 'static class' because LogFactory.CreateLogger<T>()
        // requires T to be a non-static type (CS0718).
        private MdnsResolver() { }

        public const int MDNS_PORT = 5353;
        public static readonly IPAddress MDNS_GROUP_IPV4 = IPAddress.Parse("224.0.0.251");
        public static readonly IPAddress MDNS_GROUP_IPV6 = IPAddress.Parse("ff02::fb");

        internal const ushort DNS_TYPE_A = 1;
        internal const ushort DNS_TYPE_AAAA = 28;
        internal const ushort DNS_CLASS_IN = 1;

        private const int DNS_HEADER_LENGTH = 12;
        private const int MAX_DNS_NAME_LENGTH = 255;
        private const int MAX_MDNS_PACKET_SIZE = 9000;

        /// <summary>
        /// Default time to wait for any answer to arrive after the query
        /// is sent. mDNS responses on a healthy LAN typically arrive in
        /// under 100 ms; the longer timeout covers congested wifi and
        /// virtual NIC weirdness.
        /// </summary>
        public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(2);

        private static readonly ILogger logger = LogFactory.CreateLogger<MdnsResolver>();

        /// <summary>
        /// Fired for every A / AAAA record received in an mDNS response.
        /// The hostname has any trailing dot removed.
        /// </summary>
        private static event Action<string, IPAddress> AnswerReceived;

        // Single shared set of sockets per process. Opening and closing them
        // per query would re-join the multicast group on every NIC every
        // time -- wasteful and also racey on Windows where the rebind can
        // take a few hundred ms.
        private static readonly object s_lock = new object();
        private static List<MdnsSocket> s_sockets;
        private static bool s_startFailed;

        /// <summary>
        /// A socket for one address family plus the interfaces queries
        /// should be sent out on.
        /// </summary>
        private sealed class MdnsSocket
        {
            public Socket Socket;
            public IPEndPoint Destination;

            /// <summary>
            /// Values for the MulticastInterface socket option, one per
            /// interface (the address as an int for IPv4, the interface
            /// index for IPv6).
            /// </summary>
            public List<int> Interfaces = new List<int>();
        }

        private static List<MdnsSocket> GetSockets()
        {
            if (s_sockets != null || s_startFailed) return s_sockets;
            lock (s_lock)
            {
                if (s_sockets != null || s_startFailed) return s_sockets;

                var sockets = new List<MdnsSocket>();

                try
                {
                    var nics = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(x => x.OperationalStatus == OperationalStatus.Up &&
                                    x.SupportsMulticast &&
                                    x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .ToList();

                    var v4 = CreateSocket(AddressFamily.InterNetwork, nics);
                    if (v4 != null) sockets.Add(v4);

                    if (Socket.OSSupportsIPv6)
                    {
                        var v6 = CreateSocket(AddressFamily.InterNetworkV6, nics);
                        if (v6 != null) sockets.Add(v6);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to enumerate network interfaces for mDNS.");
                }

                if (sockets.Count == 0)
                {
                    s_startFailed = true;
                    logger.LogWarning("Failed to start mDNS sockets; .local hostname resolution will not work.");
                    return null;
                }

                foreach (var mdnsSocket in sockets)
                {
                    var receiveThread = new Thread(() => Listen(mdnsSocket.Socket))
                    {
                        IsBackground = true,
                        Name = $"mdns-{mdnsSocket.Socket.AddressFamily}"
                    };
                    receiveThread.Start();
                }

                s_sockets = sockets;
                return s_sockets;
            }
        }

        private static MdnsSocket CreateSocket(AddressFamily family, List<NetworkInterface> nics)
        {
            bool isV6 = family == AddressFamily.InterNetworkV6;
            var group = isV6 ? MDNS_GROUP_IPV6 : MDNS_GROUP_IPV4;
            var any = isV6 ? IPAddress.IPv6Any : IPAddress.Any;
            var socketLevel = isV6 ? SocketOptionLevel.IPv6 : SocketOptionLevel.IP;
            Socket socket = null;

            try
            {
                socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                // Share port 5353 with any OS level mDNS responder so that
                // multicast responses are received. If that's not possible
                // fall back to an ephemeral port, in which case RFC 6762
                // section 6.7 "legacy unicast" responses are sent straight
                // back to the querying port.
                try
                {
                    socket.Bind(new IPEndPoint(any, MDNS_PORT));
                }
                catch (SocketException bindExcp)
                {
                    logger.LogDebug("mDNS could not bind to {Family} port {Port}, using an ephemeral port. {Error}", family, MDNS_PORT, bindExcp.Message);
                    socket.Dispose();
                    socket = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(any, 0));
                }

                socket.SetSocketOption(socketLevel, SocketOptionName.MulticastTimeToLive, 255);
                socket.SetSocketOption(socketLevel, SocketOptionName.MulticastLoopback, true);

                var mdnsSocket = new MdnsSocket
                {
                    Socket = socket,
                    Destination = new IPEndPoint(group, MDNS_PORT)
                };

                foreach (var nic in nics)
                {
                    try
                    {
                        var props = nic.GetIPProperties();

                        if (isV6)
                        {
                            if (!nic.Supports(NetworkInterfaceComponent.IPv6) ||
                                !props.UnicastAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetworkV6))
                            {
                                continue;
                            }

                            int index = props.GetIPv6Properties().Index;
                            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.AddMembership, new IPv6MulticastOption(group, index));
                            mdnsSocket.Interfaces.Add(index);
                        }
                        else
                        {
                            var addr = props.UnicastAddresses
                                .Select(x => x.Address)
                                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);

                            if (addr == null)
                            {
                                continue;
                            }

                            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(group, addr));
                            mdnsSocket.Interfaces.Add(BitConverter.ToInt32(addr.GetAddressBytes(), 0));
                        }
                    }
                    catch (Exception nicExcp)
                    {
                        // Typically the interface does not support the
                        // address family or has already joined the group.
                        logger.LogDebug("mDNS failed to join {Group} on interface {Interface}. {Error}", group, nic.Name, nicExcp.Message);
                    }
                }

                if (mdnsSocket.Interfaces.Count == 0)
                {
                    socket.Dispose();
                    return null;
                }

                return mdnsSocket;
            }
            catch (Exception ex)
            {
                logger.LogDebug("Failed to create {Family} mDNS socket. {Error}", family, ex.Message);
                socket?.Dispose();
                return null;
            }
        }

        private static void Listen(Socket socket)
        {
            var buffer = new byte[MAX_MDNS_PACKET_SIZE];

            while (true)
            {
                EndPoint remoteEP = socket.AddressFamily == AddressFamily.InterNetworkV6 ?
                    new IPEndPoint(IPAddress.IPv6Any, 0) :
                    new IPEndPoint(IPAddress.Any, 0);

                int bytesRead;
                try
                {
                    bytesRead = socket.ReceiveFrom(buffer, ref remoteEP);
                }
                catch (SocketException)
                {
                    // Transient errors, e.g. ICMP port unreachable surfacing
                    // as ConnectionReset on Windows.
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    var handler = AnswerReceived;
                    if (handler != null)
                    {
                        foreach (var answer in ParseAddressRecords(buffer, bytesRead))
                        {
                            handler(answer.Key, answer.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Never let one bad packet take down the shared receiver.
                    logger.LogDebug("Error processing mDNS packet from {RemoteEndPoint}. {Error}", remoteEP, ex.Message);
                }
            }
        }

        /// <summary>
        /// Send an mDNS query for the supplied hostname (typically
        /// "&lt;guid&gt;.local") and return whatever A and AAAA answers
        /// arrive within <see cref="DefaultTimeout"/>. Returns an empty
        /// array on timeout.
        /// </summary>
        public static async Task<IPAddress[]> ResolveAsync(string hostname, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(hostname))
            {
                return Array.Empty<IPAddress>();
            }

            var trimmedHostname = hostname.TrimEnd('.');
            byte[] query;

            try
            {
                query = BuildQuery(trimmedHostname);
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning("Invalid mDNS hostname {Hostname}. {Error}", hostname, ex.Message);
                return Array.Empty<IPAddress>();
            }

            var sockets = GetSockets();
            if (sockets == null)
            {
                return Array.Empty<IPAddress>();
            }

            var addresses = new List<IPAddress>();
            var addressesLock = new object();
            var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Action<string, IPAddress> handler = (name, addr) =>
            {
                if (!string.Equals(name, trimmedHostname, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                lock (addressesLock)
                {
                    if (!addresses.Contains(addr))
                    {
                        addresses.Add(addr);
                    }
                }

                answered.TrySetResult(true);
            };

            AnswerReceived += handler;
            try
            {
                foreach (var mdnsSocket in sockets)
                {
                    var level = mdnsSocket.Socket.AddressFamily == AddressFamily.InterNetworkV6 ?
                        SocketOptionLevel.IPv6 : SocketOptionLevel.IP;

                    // The socket is shared between concurrent queries and the
                    // outgoing interface is a socket option, so serialise the
                    // select-interface-then-send pairs.
                    lock (mdnsSocket)
                    {
                        foreach (var iface in mdnsSocket.Interfaces)
                        {
                            try
                            {
                                mdnsSocket.Socket.SetSocketOption(level, SocketOptionName.MulticastInterface, iface);
                                mdnsSocket.Socket.SendTo(query, mdnsSocket.Destination);
                            }
                            catch
                            {
                                // Interface went away or doesn't route multicast.
                            }
                        }
                    }
                }

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(DefaultTimeout);
                    using (cts.Token.Register(() => answered.TrySetResult(false)))
                    {
                        await answered.Task.ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                AnswerReceived -= handler;
            }

            lock (addressesLock)
            {
                return addresses.ToArray();
            }
        }

        /// <summary>
        /// Builds an mDNS query message with two questions, A and AAAA, for
        /// the supplied hostname. Asking for both specific types rather than
        /// ANY because some peers only respond to specific types.
        /// </summary>
        internal static byte[] BuildQuery(string hostname)
        {
            var name = EncodeName(hostname);
            var query = new byte[DNS_HEADER_LENGTH + 2 * (name.Length + 4)];

            // Header: ID 0, flags 0 (standard query), QDCOUNT 2, rest 0.
            query[5] = 2;

            int offset = DNS_HEADER_LENGTH;
            foreach (var type in new[] { DNS_TYPE_A, DNS_TYPE_AAAA })
            {
                Buffer.BlockCopy(name, 0, query, offset, name.Length);
                offset += name.Length;
                query[offset++] = (byte)(type >> 8);
                query[offset++] = (byte)type;
                query[offset++] = 0;
                query[offset++] = (byte)DNS_CLASS_IN;
            }

            return query;
        }

        private static byte[] EncodeName(string hostname)
        {
            var labels = hostname.TrimEnd('.').Split('.');
            var buffer = new List<byte>();

            foreach (var label in labels)
            {
                var bytes = Encoding.UTF8.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63)
                {
                    throw new ArgumentException($"DNS label length {bytes.Length} is invalid.", nameof(hostname));
                }
                buffer.Add((byte)bytes.Length);
                buffer.AddRange(bytes);
            }
            buffer.Add(0);

            if (buffer.Count > MAX_DNS_NAME_LENGTH)
            {
                throw new ArgumentException("DNS name is too long.", nameof(hostname));
            }

            return buffer.ToArray();
        }

        /// <summary>
        /// Extracts the A and AAAA records from an mDNS response message.
        /// Records from the answer, authority and additional sections are
        /// all returned since mDNS responders are free to place address
        /// records in any of them. Malformed messages yield whatever records
        /// were parsed before the error.
        /// </summary>
        /// <param name="buffer">The buffer holding the received message.</param>
        /// <param name="length">The number of bytes in the message.</param>
        /// <returns>Pairs of hostname (without a trailing dot) and address.</returns>
        internal static List<KeyValuePair<string, IPAddress>> ParseAddressRecords(byte[] buffer, int length)
        {
            var results = new List<KeyValuePair<string, IPAddress>>();

            if (buffer == null || length < DNS_HEADER_LENGTH || length > buffer.Length)
            {
                return results;
            }

            bool isResponse = (buffer[2] & 0x80) != 0;
            if (!isResponse)
            {
                return results;
            }

            int qdCount = ReadUInt16(buffer, 4);
            int rrCount = ReadUInt16(buffer, 6) + ReadUInt16(buffer, 8) + ReadUInt16(buffer, 10);
            int offset = DNS_HEADER_LENGTH;

            for (int i = 0; i < qdCount; i++)
            {
                if (ReadName(buffer, length, ref offset) == null || offset + 4 > length)
                {
                    return results;
                }
                offset += 4; // QTYPE + QCLASS.
            }

            for (int i = 0; i < rrCount; i++)
            {
                var name = ReadName(buffer, length, ref offset);
                if (name == null || offset + 10 > length)
                {
                    return results;
                }

                ushort type = ReadUInt16(buffer, offset);
                // The top bit of the class is the mDNS cache-flush flag.
                ushort rrClass = (ushort)(ReadUInt16(buffer, offset + 2) & 0x7FFF);
                int rdLength = ReadUInt16(buffer, offset + 8);
                offset += 10;

                if (offset + rdLength > length)
                {
                    return results;
                }

                if (rrClass == DNS_CLASS_IN)
                {
                    if (type == DNS_TYPE_A && rdLength == 4)
                    {
                        results.Add(new KeyValuePair<string, IPAddress>(name, new IPAddress(Slice(buffer, offset, 4))));
                    }
                    else if (type == DNS_TYPE_AAAA && rdLength == 16)
                    {
                        results.Add(new KeyValuePair<string, IPAddress>(name, new IPAddress(Slice(buffer, offset, 16))));
                    }
                }

                offset += rdLength;
            }

            return results;
        }

        /// <summary>
        /// Reads a possibly compressed DNS name. On success the offset is
        /// advanced past the name as it appears at the original position.
        /// </summary>
        /// <returns>The dot separated name without a trailing dot, or null if the name is malformed.</returns>
        private static string ReadName(byte[] buffer, int length, ref int offset)
        {
            var sb = new StringBuilder();
            int position = offset;
            int endOffset = -1;
            int jumps = 0;
            int nameLength = 0;

            while (true)
            {
                if (position >= length)
                {
                    return null;
                }

                int labelLength = buffer[position];

                if ((labelLength & 0xC0) == 0xC0)
                {
                    // Compression pointer.
                    if (position + 1 >= length || ++jumps > MAX_DNS_NAME_LENGTH)
                    {
                        return null;
                    }

                    if (endOffset < 0)
                    {
                        endOffset = position + 2;
                    }

                    position = ((labelLength & 0x3F) << 8) | buffer[position + 1];
                    continue;
                }
                else if ((labelLength & 0xC0) != 0)
                {
                    // Extended label types are not used by mDNS.
                    return null;
                }

                position++;

                if (labelLength == 0)
                {
                    break;
                }

                nameLength += labelLength + 1;
                if (position + labelLength > length || nameLength > MAX_DNS_NAME_LENGTH)
                {
                    return null;
                }

                if (sb.Length > 0)
                {
                    sb.Append('.');
                }
                sb.Append(Encoding.UTF8.GetString(buffer, position, labelLength));
                position += labelLength;
            }

            offset = endOffset >= 0 ? endOffset : position;
            return sb.ToString();
        }

        private static ushort ReadUInt16(byte[] buffer, int offset) =>
            (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

        private static byte[] Slice(byte[] buffer, int offset, int count)
        {
            var slice = new byte[count];
            Buffer.BlockCopy(buffer, offset, slice, 0, count);
            return slice;
        }
    }
}
