//-----------------------------------------------------------------------------
// Filename: STUNDns.cs
//
// Description: An implementation of a STUN and TURN DNS service resolver.
// The resolution of STUN and TURN services is similar to that of SIP services
// but without the use of NAPTR records.
// 
// The abbreviated procedure is:
// 1. Lookup a SRV record that matches the STUN URI, e.g:
//   a. For "stun" URI's:   _stun._udp.sipsorcery.com
//   b. For "stuns" URI's:  _stuns._tcp.sipsorcery.com
//   c. Likewise for "turn" and "turns" URI's.
// 2. Lookup the A or AAAA record specified by the SRV lookup result or
//    if no SRV result available lookup the hostname directly.
//
// It seems likely that STUN lookups will only ever need to return IPv4 results 
// with A records since any host that can use IPv6 shouldn't need to use STUN
// as a NAT workaround. An option to return IPv6 results with AAAA records
// is provided for completeness.
//
// Notes:
// Relevant RFC's:
// https://tools.ietf.org/html/rfc7064: URI Scheme for the Session Traversal Utilities for NAT (STUN) Protocol
// https://tools.ietf.org/html/rfc7065: Traversal Using Relays around NAT (TURN) Uniform Resource Identifiers
// https://tools.ietf.org/html/rfc5389#section-9: Session Traversal Utilities for NAT (STUN)
// https://tools.ietf.org/html/rfc6762: Multicast DNS (for ".local" Top Level Domain lookups)
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 09 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

public static class STUNDns
{
    public const string MDNS_TLD = "local"; // Top Level Domain name for multicast lookups as per RFC6762.
    public const int DNS_TIMEOUT_SECONDS = 1;
    public const int DNS_RETRIES_PER_SERVER = 1;

    private static readonly ILogger logger = Log.Logger;

    private static LookupClient _lookupClient;

    /// <summary>
    /// Set to true to attempt a DNS lookup over TCP if the UDP lookup fails.
    /// </summary>
    private static bool _dnsUseTcpFallback;

    /// <summary>
    /// Set to true to attempt a DNS lookup over TCP if the UDP lookup fails.
    /// </summary>
    public static bool DnsUseTcpFallback
    {
        get => _dnsUseTcpFallback;
        set
        {
            if (_dnsUseTcpFallback != value)
            {
                _dnsUseTcpFallback = value;
                _lookupClient = CreateLookupClient();
            }
        }
    }

    static STUNDns()
    {
        _lookupClient = CreateLookupClient();
    }

    /// <summary>
    /// Resolve method that can be used to request an AAAA result and fallback to a A
    /// lookup if none found.
    /// </summary>
    /// <param name="uri">The URI to lookup.</param>
    /// <param name="preferIPv6">True if IPv6 (AAAA record lookup) is preferred.</param>
    /// <returns>An IPEndPoint or null.</returns>
    public static Task<IPEndPoint?> Resolve(STUNUri uri, bool preferIPv6 = false)
    {
        return Resolve(uri, preferIPv6 ? QueryType.AAAA : QueryType.A);
    }

    /// <summary>
    /// Resolve method that performs either an A or AAAA record lookup. If required
    /// a SRV record lookup will be performed prior to the A or AAAA lookup.
    /// </summary>
    /// <param name="uri">The STUN uri to lookup.</param>
    /// <param name="queryType">Whether the address lookup should be A or AAAA.</param>
    /// <returns>An IPEndPoint or null.</returns>
    private static async Task<IPEndPoint?> Resolve(STUNUri uri, QueryType queryType)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(uri.Host);

        if (IPAddress.TryParse(uri.Host, out var ipAddress))
        {
            // Target is already an IP address, no DNS lookup required.
            return new IPEndPoint(ipAddress, uri.Port);
        }
        else
        {
            var useDnsClient = true;

            if (!uri.Host.Contains('.') || uri.Host.EndsWith(MDNS_TLD) || queryType == QueryType.A || queryType == QueryType.AAAA)
            {
                useDnsClient = false;
                var family = (queryType == QueryType.AAAA)
                    ? AddressFamily.InterNetworkV6
                    : AddressFamily.InterNetwork;

                // The lookup is for a local network host. Use the OS DNS logic as the 
                // main DNS client can be configured to use external DNS servers that won't
                // be able to lookup this hostname.

                IPHostEntry? hostEntry = null;

                try
                {
                    hostEntry = await Dns.GetHostEntryAsync(uri.Host).ConfigureAwait(ConfigureAwaitOptions.None);
                }
                catch (SocketException)
                {
                    // Socket exception gets thrown for failed lookups,
                }

                if (hostEntry is { })
                {
                    var addressList = hostEntry.AddressList ?? Array.Empty<IPAddress>();

                    if (addressList.Length == 0)
                    {
                        logger.LogStunDnsOsLookupFailed(uri.Host);
                        useDnsClient = true;
                    }
                    else
                    {
                        for (var i = 0; i < addressList.Length; i++)
                        {
                            if (addressList[i].AddressFamily == family)
                            {
                                return new IPEndPoint(addressList[i], uri.Port);
                            }
                        }

                        // Didn't get a result for the preferred address family so just use the 
                        // first available result.
                        return new IPEndPoint(addressList[0], uri.Port);
                    }
                }
                else
                {
                    useDnsClient = true;
                }
            }

            if (useDnsClient)
            {
                if (uri.ExplicitPort)
                {
                    return HostQuery(uri.Host, uri.Port, queryType);
                }
                else
                {
                    try
                    {
                        // No explicit port so use a SRV -> (A | AAAA -> A) record lookup.
                        var result = await _lookupClient.ResolveServiceAsync(uri.Host, uri.Scheme.ToStringFast(), uri.Protocol.ToLowerString()).ConfigureAwait(false);
                        Debug.Assert(result is { });

                        string? host;
                        int port;

                        if (result.Length <= 0)
                        {
                            host = uri.Host; // If no SRV results then fallback is to lookup the hostname directly.
                            port = uri.Port; // If no SRV results then fallback is to use the default port.
                        }
                        else
                        {
                            // result.OrderBy(y => y.Priority).ThenByDescending(w => w.Weight).FirstOrDefault();
                            var srvResult = result[0];
                            for (var i = 1; i < result.Length; i++)
                            {
                                var entry = result[i];
                                if (entry.Priority < srvResult.Priority ||
                                    (entry.Priority == srvResult.Priority && entry.Weight > srvResult.Weight))
                                {
                                    srvResult = entry;
                                }
                            }

                            host = srvResult.HostName;
                            port = srvResult.Port;
                        }

                        return HostQuery(host, port, queryType);
                    }
                    catch (Exception e)
                    {
                        logger.LogStunDnsSrvLookupFailure(uri, e.InnerException?.Message, e);
                        return null;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Attempts to resolve a hostname.
    /// </summary>
    /// <param name="host">The hostname to resolve.</param>
    /// <param name="port">The service port to use in the end pint result (not used for the lookup).</param>
    /// <param name="queryType">The lookup query type, either A or AAAA.</param>
    /// <returns>If successful an IPEndPoint or null if not.</returns>
    private static IPEndPoint? HostQuery(string host, int port, QueryType queryType)
    {
        try
        {
            var answers = _lookupClient.Query(host, queryType).Answers;
            if (answers.Count > 0)
            {
                return GetFromLookupResult(answers, port);
            }
        }
        catch (Exception excp)
        {
            logger.LogStunDnsLookupFailure(host, queryType, excp.Message, excp);
        }

        if (queryType == QueryType.AAAA)
        {
            return HostQuery(host, port, QueryType.A);
        }

        return null;
    }

    /// <summary>
    /// Helper method to extract the appropriate IP address from a DNS lookup result.
    /// The query may have returned an AAAA or A record. This method checks which 
    /// and extracts the IP address accordingly.
    /// </summary>
    /// <param name="answers">The DNS lookup result.</param>
    /// <param name="port">The port for the IP end point.</param>
    /// <returns>An IP end point or null.</returns>
    private static IPEndPoint? GetFromLookupResult(IEnumerable<DnsResourceRecord> answers, int port)
    {
        foreach (var rr in answers)
        {
            if (rr is AddressRecord ar)
            {
                return new IPEndPoint(ar.Address, port);
            }
        }
        return null;
    }

    /// <summary>
    /// Creates a LookupClient
    /// </summary>
    /// <returns>A LookupClient</returns>
    private static LookupClient CreateLookupClient()
    {
        var nameServers = NameServer.ResolveNameServers(skipIPv6SiteLocal: true, fallbackToGooglePublicDns: true);
        var clientOptions = new LookupClientOptions(nameServers.ToArray())
        {
            Retries = DNS_RETRIES_PER_SERVER,
            Timeout = TimeSpan.FromSeconds(DNS_TIMEOUT_SECONDS),
            UseCache = true,
            UseTcpFallback = DnsUseTcpFallback
        };

        return new LookupClient(clientOptions);
    }
}
