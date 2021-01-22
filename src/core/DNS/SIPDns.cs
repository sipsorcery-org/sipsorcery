//-----------------------------------------------------------------------------
// Filename: SIPDns.cs
//
// Description: An implementation of a SIP DNS service resolver.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 Jul 2020	Aaron Clauson	Created, Dublin, Ireland.
// 05 Jan 2021  Aaron Clauson   Re-enabled cache lookups (now supported by DNS library).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

[assembly: InternalsVisibleToAttribute("SIPSorcery.IntegrationTests")]

namespace SIPSorcery.SIP
{
    /// <summary>
    /// SIP specific DNS resolution.
    /// </summary>
    /// <remarks>
    /// 1. If transport parameter is specified it takes precedence,
    /// 2. If no transport parameter and target is an IP address then sip should use udp and sips tcp,
    /// 3. If no transport parameter and target is a host name with an explicit port then sip should use 
    ///    udp and sips tcp and host should be resolved using an A or AAAA record DNS lookup (section 4.2),
    /// 4*. If no transport protocol and no explicit port and target is a host name then the client should no
    ///    an NAPTR lookup and utilise records for services SIP+D2U, SIP+D2T, SIP+D2S, SIPS+D2T and SIPS+D2S,
    /// 5. If NAPTR record(s) are found select the desired transport and lookup the SRV record,
    /// 6. If no NAPT records are found lookup SRV record for desired protocol _sip._udp, _sip._tcp, _sips._tcp,
    ///    _sip._tls,
    /// 7. If no SRV records found lookup A or AAAA record.
    /// 
    /// * NAPTR lookups are currently not implemented as they have been found to be hardly ever used and can 
    /// increase the DNS query time noticeably.
    /// 
    /// Observations from the field.
    /// - A DNS server has been observed to not respond at all to NAPTR or SRV record queries meaning lookups for
    ///   them will permanently time out.
    /// </remarks>
    public class SIPDns
    {
        public const string MDNS_TLD = "local"; // Top Level Domain name for multicast lookups as per RFC6762.
        public const int DNS_TIMEOUT_SECONDS = 1;
        public const int DNS_RETRIES_PER_SERVER = 1;
        public const int CACHE_FAILED_RESULTS_DURATION = 10;    // Cache failed DNS responses for this duration in seconds.

        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// Don't use IN_ANY queries by default. These are useful if a DNS server supports them as they can
        /// return IPv4 and IPv6 results in a single query. For DNS servers that don't support them it means
        /// an extra delay.
        /// </summary>
        public static bool UseANYLookups = false;

        //public static List<DnsClient.NameServer> DefaultNameServers { get; set; }

        private static LookupClient _lookupClient;
        public static LookupClient LookupClient
        {
            get
            {
                return _lookupClient;
            }
            internal set
            {
                // Intended to allow unit testing with client options that will cause the
                // lookup logic to execute failure conditions.
                _lookupClient = value;
            }
        }

        static SIPDns()
        {
            var nameServers = NameServer.ResolveNameServers(skipIPv6SiteLocal: true, fallbackToGooglePublicDns: true);
            LookupClientOptions clientOptions = new LookupClientOptions(nameServers.ToArray())
            {
                Retries = DNS_RETRIES_PER_SERVER,
                Timeout = TimeSpan.FromSeconds(DNS_TIMEOUT_SECONDS),
                UseCache = true,
                CacheFailedResults = true,
                FailedResultsCacheDuration = TimeSpan.FromSeconds(CACHE_FAILED_RESULTS_DURATION)
            };

            _lookupClient = new LookupClient(clientOptions);
        }

        public static SIPEndPoint ResolveFromCache(SIPURI uri, bool preferIPv6)
        {
            if (uri == null || String.IsNullOrWhiteSpace(uri.MAddrOrHostAddress))
            {
                throw new ArgumentNullException("uri", "SIP DNS resolve was supplied an empty input.");
            }

            if (!ushort.TryParse(uri.HostPort, out var uriPort))
            {
                uriPort = SIPConstants.DEFAULT_SIP_PORT;
            }

            if (IPAddress.TryParse(uri.MAddrOrHostAddress, out var ipAddress))
            {
                // Target is already an IP address, no DNS lookup required.
                return new SIPEndPoint(uri.Protocol, ipAddress, uriPort);
            }
            else if (!uri.MAddrOrHostAddress.Contains(".") || uri.MAddrOrHostAddress.EndsWith(MDNS_TLD))
            {
                // No caching for local network hostnames.
                return null;
            }
            else
            {
                if (uri.HostPort != null)
                {
                    // Explicit port means no SRV lookup.
                    return SIPLookupFromCache(uri, preferIPv6 ? QueryType.AAAA : QueryType.A, preferIPv6);
                }
                else
                {
                    return SIPLookupFromCache(uri, QueryType.SRV, preferIPv6);
                }
            }
        }

        /// <summary>
        /// Resolve method that performs either an A or AAAA record lookup. If required
        /// a SRV record lookup will be performed prior to the A or AAAA lookup.
        /// </summary>
        /// <param name="uri">The SIP URI to lookup.</param>
        /// <param name="preferIPv6">Whether the address lookup would prefer to have an IPv6 address
        /// returned.</param>
        /// <returns>A SIPEndPoint or null.</returns>
        public static Task<SIPEndPoint> ResolveAsync(SIPURI uri, bool preferIPv6, CancellationToken ct)
        {
            if (uri == null || String.IsNullOrWhiteSpace(uri.MAddrOrHostAddress))
            {
                throw new ArgumentNullException("uri", "SIP DNS resolve was supplied an empty input.");
            }

            if (!ushort.TryParse(uri.HostPort, out var uriPort))
            {
                uriPort = SIPConstants.DEFAULT_SIP_PORT;
            }

            if (IPAddress.TryParse(uri.MAddrOrHostAddress, out var ipAddress))
            {
                // Target is already an IP address, no DNS lookup required.
                return Task.FromResult(new SIPEndPoint(uri.Protocol, ipAddress, uriPort));
            }
            else
            {
                if (!uri.MAddrOrHostAddress.Contains(".") || uri.MAddrOrHostAddress.EndsWith(MDNS_TLD))
                {
                    return Task.FromResult(ResolveLocalHostname(uri, preferIPv6));
                }
                else
                {
                    if (uri.HostPort != null)
                    {
                        // Explicit port means no SRV lookup.
                        return SIPLookupAsync(uri, preferIPv6 ? QueryType.AAAA : QueryType.A, preferIPv6, ct);
                    }
                    else
                    {
                        return SIPLookupAsync(uri, QueryType.SRV, preferIPv6, ct);
                    }
                }
            }
        }

        /// <summary>
        /// Attempts a lookup from cache.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="startQuery"></param>
        /// <param name="preferIPv6"></param>
        /// <returns></returns>
        private static SIPEndPoint SIPLookupFromCache(
            SIPURI uri,
            QueryType startQuery,
            bool preferIPv6)
        {
            SIPEndPoint result = null;
            QueryType queryType = startQuery;

            string host = uri.MAddrOrHostAddress;
            int port = SIPConstants.GetDefaultPort(uri.Protocol);
            if (ushort.TryParse(uri.HostPort, out var uriPort))
            {
                port = uriPort;
            }

            bool isDone = false;

            while (!isDone)
            {
                switch (queryType)
                {
                    case QueryType.SRV:

                        var srvProtocol = SIPServices.GetSRVProtocolForSIPURI(uri);
                        string serviceHost = DnsQueryExtensions.ConcatServiceName(uri.MAddrOrHostAddress, uri.Scheme.ToString(), srvProtocol.ToString());
                        var srvResult = _lookupClient.QueryCache(serviceHost, QueryType.SRV);
                        (var srvHost, var srvPort) = GetHostAndPortFromSrvResult(srvResult);
                        if (srvHost != null)
                        {
                            host = srvHost;
                            port = srvPort != 0 ? srvPort : port;
                        }
                        queryType = preferIPv6 ? QueryType.AAAA : QueryType.A;

                        break;

                    case QueryType.AAAA:

                        var aaaaResult = _lookupClient.QueryCache(host, UseANYLookups ? QueryType.ANY : QueryType.AAAA, QueryClass.IN);
                        if (aaaaResult?.Answers?.Count > 0)
                        {
                            result = GetFromLookupResult(uri.Protocol, aaaaResult.Answers.OrderByDescending(x => x.RecordType).First(), port);
                            isDone = true;
                        }
                        else
                        {
                            queryType = QueryType.A;
                        }

                        break;

                    default:
                        // A record lookup.

                        var aResult = _lookupClient.QueryCache(host, QueryType.A, QueryClass.IN);
                        if (aResult != null)
                        {
                            if (aResult.Answers?.Count > 0)
                            {
                                result = GetFromLookupResult(uri.Protocol, aResult.Answers.First(), port);
                            }
                            else
                            {
                                // We got a result back but it was empty indicating an unresolvable host or
                                // some other DNS error condition.
                                result = SIPEndPoint.Empty;
                            }
                        }

                        isDone = true;
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Attempts a lookup from DNS.
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="startQuery"></param>
        /// <param name="preferIPv6"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private static async Task<SIPEndPoint> SIPLookupAsync(
            SIPURI uri,
            QueryType startQuery,
            bool preferIPv6,
            CancellationToken ct)
        {
            SIPEndPoint result = null;
            QueryType queryType = startQuery;

            string host = uri.MAddrOrHostAddress;
            int port = SIPConstants.GetDefaultPort(uri.Protocol);
            if (ushort.TryParse(uri.HostPort, out var uriPort))
            {
                port = uriPort;
            }

            bool isDone = false;

            while (!isDone && !ct.IsCancellationRequested)
            {
                switch (queryType)
                {
                    case QueryType.SRV:

                        try
                        {
                            var srvProtocol = SIPServices.GetSRVProtocolForSIPURI(uri);
                            var srvResult = await _lookupClient.ResolveServiceAsync(uri.MAddrOrHostAddress, uri.Scheme.ToString(), srvProtocol.ToString()).ConfigureAwait(false);
                            (var srvHost, var srvPort) = GetHostAndPortFromSrvResult(srvResult);
                            if (srvHost != null)
                            {
                                host = srvHost;
                                port = srvPort != 0 ? srvPort : port;

                                logger.LogDebug($"SIP DNS SRV for {uri} resolved to {host} and port {port}.");
                            }
                        }
                        catch (Exception srvExcp)
                        {
                            logger.LogWarning($"SIPDNS exception on SRV lookup. {srvExcp.Message}.");
                        }
                        queryType = preferIPv6 ? QueryType.AAAA : QueryType.A;

                        break;

                    case QueryType.AAAA:

                        try
                        {
                            var aaaaResult = await _lookupClient.QueryAsync(host, UseANYLookups ? QueryType.ANY : QueryType.AAAA, QueryClass.IN, ct).ConfigureAwait(false);
                            if (aaaaResult?.Answers?.Count > 0)
                            {
                                result = GetFromLookupResult(uri.Protocol, aaaaResult.Answers.AddressRecords().OrderByDescending(x => x.RecordType).First(), port);
                                isDone = true;
                            }
                            else
                            {
                                queryType = QueryType.A;
                            }
                        }
                        catch (Exception srvExcp)
                        {
                            logger.LogWarning($"SIPDNS exception on AAAA lookup. {srvExcp.Message}.");
                            queryType = QueryType.A;
                        }

                        break;

                    default:
                        // A record lookup.
                        try
                        {
                            var aResult = await _lookupClient.QueryAsync(host, QueryType.A, QueryClass.IN, ct).ConfigureAwait(false);
                            if (aResult != null)
                            {
                                if (aResult.Answers?.Count > 0)
                                {
                                    result = GetFromLookupResult(uri.Protocol, aResult.Answers.AddressRecords().First(), port);
                                }
                                else
                                {
                                    // We got a result back but it was empty indicating an unresolvable host or
                                    // some other DNS error condition.
                                    result = SIPEndPoint.Empty;
                                }
                            }

                        }
                        catch (Exception srvExcp)
                        {
                            logger.LogWarning($"SIPDNS exception on A lookup. {srvExcp.Message}.");
                            result = SIPEndPoint.Empty;
                        }

                        isDone = true;
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Extracts the server hostname and port from a SRV lookup.
        /// </summary>
        /// <param name="srvResult">The DNS response from an SRV lookup.</param>
        /// <returns>The hostname and port for the chosen SRV result record.</returns>
        private static (string, int) GetHostAndPortFromSrvResult(IDnsQueryResponse srvResult)
        {
            if (srvResult != null)
            {
                if (srvResult.Answers.Count() > 0)
                {
                    var serviceHostEntries = DnsQueryExtensions.ResolveServiceProcessResult(srvResult);

                    return GetHostAndPortFromSrvResult(serviceHostEntries);
                }
            }

            return (null, 0);
        }

        /// <summary>
        /// Extracts the server hostname and port from a SRV lookup.
        /// </summary>
        /// <param name="serviceHostEntries">The DNS response from an SRV lookup.</param>
        /// <returns>The hostname and port for the chosen SRV result record.</returns>
        private static (string, int) GetHostAndPortFromSrvResult(ServiceHostEntry[] serviceHostEntries)
        {
            if (serviceHostEntries != null && serviceHostEntries.Length > 0)
            {
                // TODO: Should be applying some randomisation logic here to take advantage if there are multiple SRV records.
                var srvEntry = serviceHostEntries.OrderBy(y => y.Priority).ThenByDescending(w => w.Weight).FirstOrDefault();

                return (srvEntry.HostName, srvEntry.Port);
            }
            else
            {
                return (null, 0);
            }
        }

        /// <summary>
        /// Helper method to extract the appropriate IP address from a DNS lookup result.
        /// The query may have returned an AAAA or A record. This method checks which 
        /// and extracts the IP address accordingly.
        /// </summary>
        /// <param name="addrRecord">The DNS lookup result.</param>
        /// <param name="port">The port for the IP end point.</param>
        /// <returns>An IP end point or null.</returns>
        private static SIPEndPoint GetFromLookupResult(SIPProtocolsEnum protocol, DnsResourceRecord addrRecord, int port)
        {
            if (addrRecord is AaaaRecord)
            {
                return new SIPEndPoint(protocol, (addrRecord as AaaaRecord).Address, port);
            }
            else if (addrRecord is ARecord)
            {
                return new SIPEndPoint(protocol, (addrRecord as ARecord).Address, port);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// The lookup is for a local network host. Use the OS DNS logic as the 
        /// main DNS client can be configured to use external DNS servers that won't
        /// be able to lookup this hostname.
        /// </summary>
        /// <param name="uri">The SIP URI to lookup.</param>
        /// <param name="queryType">Whether the lookup should prefer an IPv6 result.</param>
        /// <returns>A SIP end point for the host or null if the URI cannot be resolved.</returns>
        private static SIPEndPoint ResolveLocalHostname(SIPURI uri, bool preferIPv6)
        {
            AddressFamily family = preferIPv6 ? AddressFamily.InterNetworkV6 :
                       AddressFamily.InterNetwork;

            if (!ushort.TryParse(uri.HostPort, out var uriPort))
            {
                uriPort = SIPConstants.DEFAULT_SIP_PORT;
            }

            IPHostEntry hostEntry = null;

            try
            {
                hostEntry = Dns.GetHostEntry(uri.MAddrOrHostAddress);
            }
            catch (SocketException)
            {
                // Socket exception gets thrown for failed lookups,
            }

            if (hostEntry != null)
            {
                var addressList = hostEntry.AddressList;

                if (addressList?.Length == 0)
                {
                    logger.LogWarning($"Operating System DNS lookup failed for {uri.MAddrOrHostAddress}.");
                    return null;
                }
                else
                {
                    if (addressList.Any(x => x.AddressFamily == family))
                    {
                        var addressResult = addressList.First(x => x.AddressFamily == family);
                        return new SIPEndPoint(uri.Protocol, addressResult, uriPort);
                    }
                    else
                    {
                        // Didn't get a result for the preferred address family so just use the 
                        // first available result.
                        var addressResult = addressList.First();
                        return new SIPEndPoint(uri.Protocol, addressResult, uriPort);
                    }
                }
            }
            else
            {
                return null;
            }
        }
    }
}
