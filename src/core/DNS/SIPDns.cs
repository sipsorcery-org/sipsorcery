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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

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

        private static readonly ILogger logger = Log.Logger;

        /// <summary>
        /// Set to true to prefer IPv6 lookups of hostnames. By default IPv4 lookups will be performed.
        /// </summary>
        public static bool PreferIPv6NameResolution = false;

        /// <summary>
        /// Don't use IN_ANY queries by default. These are useful if a DNS server supports them as they can
        /// return IPv4 and IPv6 results in a single query. For DNS servers that don't support them it means
        /// an extra delay.
        /// </summary>
        public static bool UseANYLookups = false;

        private static LookupClient _lookupClient;
        public static LookupClient LookupClient
        {
            get
            {
                return _lookupClient;
            }
        }

        static SIPDns()
        {
            LookupClientOptions clientOptions = new LookupClientOptions()
            {
                Retries = DNS_RETRIES_PER_SERVER,
                Timeout = TimeSpan.FromSeconds(DNS_TIMEOUT_SECONDS),
                UseCache = true,
                //UseTcpFallback = false
            };

            _lookupClient = new LookupClient(clientOptions);
        }

        /// <summary>
        /// Resolve method that can be used to request an AAAA result and fallback to an A
        /// record lookup if none found.
        /// </summary>
        /// <param name="uri">The SIP URI to lookup.</param>
        /// <param name="preferIPv6">True if IPv6 (AAAA record lookup) is preferred.</param>
        /// <returns>A SIPEndPoint or null.</returns>
        public static Task<SIPEndPoint> Resolve(SIPURI uri, bool preferIPv6 = false)
        {
            var queryType = preferIPv6 || PreferIPv6NameResolution ? QueryType.AAAA : QueryType.A;
            if(UseANYLookups)
            {
                queryType = QueryType.ANY;
            }

            return Resolve(uri, queryType);
        }

        /// <summary>
        /// Resolve method that performs either an A or AAAA record lookup. If required
        /// a SRV record lookup will be performed prior to the A or AAAA lookup.
        /// </summary>
        /// <param name="uri">The SIP URI to lookup.</param>
        /// <param name="queryType">Whether the address lookup should be A or AAAA.</param>
        /// <returns>A SIPEndPoint or null.</returns>
        private static Task<SIPEndPoint> Resolve(SIPURI uri, QueryType queryType)
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
                    AddressFamily family = (queryType == QueryType.AAAA) ? AddressFamily.InterNetworkV6 :
                        AddressFamily.InterNetwork;

                    // The lookup is for a local network host. Use the OS DNS logic as the 
                    // main DNS client can be configured to use external DNS servers that won't
                    // be able to lookup this hostname.

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
                            return Task.FromResult<SIPEndPoint>(null);
                        }
                        else
                        {
                            if (addressList.Any(x => x.AddressFamily == family))
                            {
                                var addressResult = addressList.First(x => x.AddressFamily == family);
                                return Task.FromResult(new SIPEndPoint(uri.Protocol, addressResult, uriPort));
                            }
                            else
                            {
                                // Didn't get a result for the preferred address family so just use the 
                                // first available result.
                                var addressResult = addressList.First();
                                return Task.FromResult(new SIPEndPoint(uri.Protocol, addressResult, uriPort));
                            }
                        }
                    }
                    else
                    {
                        return Task.FromResult<SIPEndPoint>(null);
                    }
                }
                else
                {
                    if (uri.HostPort != null)
                    {
                        // Explicit port means no SRV lookup.
                        return Task.FromResult(HostQuery(uri.Protocol, uri.MAddrOrHostAddress, uriPort, queryType));
                    }
                    else
                    {
                        // No explicit port so use a SRV -> (A | AAAA -> A | ANY) record lookup.
                        var srvProtocol = SIPServices.GetSRVProtocolForSIPURI(uri);
                        return _lookupClient.ResolveServiceAsync(uri.MAddrOrHostAddress, uri.Scheme.ToString(), srvProtocol.ToString())
                            .ContinueWith<SIPEndPoint>(x =>
                            {
                                ServiceHostEntry srvResult = null;

                                if (x.IsFaulted)
                                {
                                    logger.LogWarning($"SIP DNS SRV lookup failure for {uri}. {x.Exception?.InnerException?.Message}");
                                }
                                else if (x.Result == null || x.Result.Count() == 0)
                                {
                                    logger.LogWarning($"SIP DNS SRV lookup returned no results for {uri}.");
                                }
                                else
                                {
                                    srvResult = x.Result.OrderBy(y => y.Priority).ThenByDescending(w => w.Weight).FirstOrDefault();
                                }

                                string host = uri.MAddrOrHostAddress;       // If no SRV results then fallback is to lookup the hostname directly.
                                int port = SIPConstants.DEFAULT_SIP_PORT;   // If no SRV results then fallback is to use the default port.

                                if (srvResult != null)
                                {
                                    host = srvResult.HostName;
                                    port = srvResult.Port;
                                }

                                return HostQuery(uri.Protocol, host, port, queryType);
                            });
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to resolve a hostname.
        /// </summary>
        /// <param name="host">The hostname to resolve.</param>
        /// <param name="port">The service port to use in the end pint result (not used for the lookup).</param>
        /// <param name="queryType">The lookup query type, either A or AAAA.</param>
        /// <returns>If successful an IPEndPoint or null if not.</returns>
        private static SIPEndPoint HostQuery(SIPProtocolsEnum protocol, string host, int port, QueryType queryType)
        {
            try
            {
                var addrRecord = _lookupClient.Query(host, queryType).Answers.FirstOrDefault();
                if (addrRecord != null)
                {
                    return GetFromLookupResult(protocol, addrRecord, port);
                }
            }
            catch (Exception excp)
            {
                logger.LogWarning($"SIP DNS lookup failure for {host} and query {queryType}. {excp.Message}");
            }

            if (queryType == QueryType.AAAA)
            {
                return HostQuery(protocol, host, port, QueryType.A);
            }

            return null;
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
    }
}
