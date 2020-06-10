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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    public class STUNDns
    {
        private static readonly ILogger logger = Log.Logger;

        private static LookupClient _lookupClient;

        static STUNDns()
        {
            _lookupClient = new LookupClient();
        }

        /// <summary>
        /// Resolve method that can be used to request an AAAA result and fallback to a A
        /// lookup if none found.
        /// </summary>
        /// <param name="uri">The URI to lookup.</param>
        /// <param name="preferIPv6">True if IPv6 (AAAA record lookup) is preferred.</param>
        /// <returns>An IPEndPoint or null.</returns>
        public static Task<IPEndPoint> Resolve(STUNUri uri, bool preferIPv6 = false)
        {
            if (preferIPv6)
            {
                // Try AAAA record lookup followed by A record if none found.
                return Resolve(uri, QueryType.AAAA)
                    .ContinueWith(x => x.Result ?? Resolve(uri, QueryType.A).Result);
            }
            else
            {
                return Resolve(uri, QueryType.A);
            }
        }

        /// <summary>
        /// Resolve method that performs either an A or AAAA record lookup. If required
        /// a SRV record lookup will be performed prior to the A or AAAA lookup.
        /// </summary>
        /// <param name="uri">The STUN uri to lookup.</param>
        /// <param name="queryType">Whether the address lookup should be A or AAAA.</param>
        /// <returns>An IPEndPoint or null.</returns>
        public static Task<IPEndPoint> Resolve(STUNUri uri, QueryType queryType)
        {
            if (uri == null || String.IsNullOrWhiteSpace(uri.Host))
            {
                throw new ArgumentNullException("uri", "DNS resolve was supplied an empty input.");
            }

            if (IPAddress.TryParse(uri.Host, out var ipAddress))
            {
                // Target is already an IP address, no DNS lookup required.
                return Task.FromResult(new IPEndPoint(ipAddress, uri.Port));
            }
            else
            {
                if (!uri.Host.Contains("."))
                {
                    AddressFamily family = (queryType == QueryType.AAAA) ? AddressFamily.InterNetworkV6 :
                        AddressFamily.InterNetwork;

                    // The lookup is for a local network host. Use the OS DNS logic as the 
                    // main DNS client can be configured to use external DNS servers that won't
                    // be able to lookup this hostname.

                    IPHostEntry hostEntry = null;

                    try
                    {
                       hostEntry = Dns.GetHostEntry(uri.Host);
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
                            logger.LogWarning($"Operating System DNS lookup failed for {uri.Host}.");
                            return Task.FromResult<IPEndPoint>(null);
                        }
                        else
                        {
                            if (addressList.Any(x => x.AddressFamily == family))
                            {
                                var addressResult = addressList.First(x => x.AddressFamily == family);
                                return Task.FromResult(new IPEndPoint(addressResult, uri.Port));
                            }
                            else
                            {
                                return Task.FromResult<IPEndPoint>(null);
                            }
                        }
                    }
                    else
                    {
                        return Task.FromResult<IPEndPoint>(null);
                    }
                }
                else
                {
                    if (uri.ExplicitPort)
                    {
                        // If the URI has an explicit port then it indicates SRV records should not be used.
                        return _lookupClient.QueryAsync(uri.Host, queryType)
                            .ContinueWith<IPEndPoint>(x =>
                            {
                                var addrRecord = x.Result.Answers.FirstOrDefault();
                                return GetFromLookupResult(addrRecord, uri.Port);
                            });
                    }
                    else
                    {
                        // No explicit port so use a SRV -> (A | AAAA) record lookup.
                        return _lookupClient.ResolveServiceAsync(uri.Host, uri.Scheme.ToString(), uri.Protocol)
                            .ContinueWith<IPEndPoint>(x =>
                            {
                                var srvResult = x.Result.OrderBy(y => y.Priority).ThenByDescending(w => w.Weight).FirstOrDefault();

                                string host = uri.Host; // If no SRV results then fallback is to lookup the hostname directly.
                                int port = uri.Port;    // If no SRV results then fallback is to use the default port.

                                if (srvResult != null)
                                {
                                    host = srvResult.HostName;
                                    port = srvResult.Port;
                                }

                                var addrRecord = _lookupClient.Query(host, queryType).Answers.FirstOrDefault();
                                return GetFromLookupResult(addrRecord, uri.Port);
                            });
                    }
                }
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
        private static IPEndPoint GetFromLookupResult(DnsResourceRecord addrRecord, int port)
        {
            if (addrRecord is AaaaRecord)
            {
                return new IPEndPoint((addrRecord as AaaaRecord).Address, port);
            }
            else if (addrRecord is ARecord)
            {
                return new IPEndPoint((addrRecord as ARecord).Address, port);
            }
            else
            {
                return null;
            }
        }
    }
}
