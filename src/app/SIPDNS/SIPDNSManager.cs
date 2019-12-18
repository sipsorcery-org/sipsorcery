//-----------------------------------------------------------------------------
// Filename: SIPDNSManager.cs
//
// Description: An implementation of RFC 3263 to resolve SIP URIs using NAPTR, SRV and A records (could it be anymore convoluted?).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 11 Mar 2009	Aaron Clauson	Created, Hobart, Australia.
// 28 Oct 2019  Aaron Clauson   Added lookup mechanism for local machine hostname. Useful for testing purposes.
// 18 Nov 2019  Aaron Clauson   Added Task-based Asyn Pattern (TAP) resolve method (i.e. allow await/async).
// rj2: enable NAPTR lookup
// rj2: IPv6 Name Resolution
// rj2: add nullable argument to select if you prefer to get IPv4 or IPv6 Addresses in NameResolution
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Heijden.DNS;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// SIP specific DNS resolution.
    /// </summary>
    /// <remarks>
    /// 1. If transport parameter is specified it takes precedence,
    /// 2. If no transport parameter and target is an IP address then sip should use udp and sips tcp,
    /// 3. If no transport parameter and target is a host name with an explicit port then sip should use 
    ///    udp and sips tcp and host should be resolved using an A or AAAA record DNS lookup (section 4.2),
    /// 4. If no transport protocol and no explicit port and target is a host name then the client should no
    ///    an NAPTR lookup and utilise records for services SIP+D2U, SIP+D2T, SIP+D2S, SIPS+D2T and SIPS+D2S,
    /// 5. If NAPTR record(s) are found select the desired transport and lookup the SRV record,
    /// 6. If no NAPT records are found lookup SRV record for desired protocol _sip._udp, _sip._tcp, _sips._tcp,
    ///    _sip._tls,
    /// 7. If no SRV records found lookup A or AAAA record.
    /// 
    /// Observations from the field.
    /// - A DNS server has been observed to not respond at all to NAPTR or SRV record queries meaning lookups for
    ///   them will permanently time out.
    /// </remarks>
    public class SIPDNSManager
    {
        private const int DNS_LOOKUP_TIMEOUT = 5;                       // 2 second timeout for DNS lookups.
        private const int DNS_A_RECORD_LOOKUP_TIMEOUT = 15;              // 5 second timeout for crticial A record DNS lookups.

        private static ILogger logger = Log.Logger;

        private static int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static int m_defaultSIPSPort = SIPConstants.DEFAULT_SIP_TLS_PORT;
        private static List<string> m_inProgressSIPServiceLookups = new List<string>();

        public static SIPMonitorLogDelegate SIPMonitorLogEvent;

        //rj2: default value for preferIPv6 Name Reolution Argument
        public static bool PreferIPv6NameResolution = false;

        static SIPDNSManager()
        {
            SIPMonitorLogEvent = (e) => { };
        }

        public static SIPDNSLookupResult ResolveSIPService(string host)
        {
            try
            {
                //return ResolveSIPService(SIPURI.ParseSIPURIRelaxed(host), true);
                return ResolveSIPService(SIPURI.ParseSIPURIRelaxed(host), true, SIPDNSManager.PreferIPv6NameResolution);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPDNSManager ResolveSIPService (" + host + "). " + excp.Message);
                throw;
            }
        }

        // TODO need to consolidate ResolveSIPService and ResolveAsync.
        //public static SIPDNSLookupResult ResolveSIPService(SIPURI sipURI, bool async)
        public static SIPDNSLookupResult ResolveSIPService(SIPURI sipURI, bool async, bool? preferIPv6 = null)
        {
            try
            {
                if (sipURI == null)
                {
                    throw new ArgumentNullException("sipURI", "Cannot resolve SIP service on a null URI.");
                }

                if (IPSocket.TryParseIPEndPoint(sipURI.MAddrOrHost, out var ipEndPoint))
                {
                    // Target is an IP address, no DNS lookup required.
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, ipEndPoint), 0);
                    SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                    result.AddLookupResult(sipLookupEndPoint);
                    return result;
                }
                else
                {
                    string host = sipURI.MAddrOrHostAddress;
                    //rj2 2018-10-17: apply default port from transport protocol, not from sip scheme, just as it was implemented in SIPEndpoint and SIPTransportConfig
                    //int port = (sipURI.Scheme == SIPSchemesEnum.sip) ? m_defaultSIPPort : m_defaultSIPSPort;
                    int port = sipURI.Protocol != SIPProtocolsEnum.tls ? m_defaultSIPPort : m_defaultSIPSPort;
                    if (preferIPv6 == null)
                    {
                        preferIPv6 = SIPDNSManager.PreferIPv6NameResolution;
                    }
                    bool explicitPort = false;

                    try
                    {
                        //Parse returns true if sipURI.Host can be parsed as an ipaddress
                        bool parseresult = SIPSorcery.Sys.IPSocket.Parse(sipURI.MAddrOrHost, out host, out port);
                        explicitPort = port >= 0;
                        if (!explicitPort)
                        {
                            //port = (sipURI.Scheme == SIPSchemesEnum.sip) ? m_defaultSIPPort : m_defaultSIPSPort;
                            port = sipURI.Protocol != SIPProtocolsEnum.tls ? m_defaultSIPPort : m_defaultSIPSPort;
                        }
                        if (parseresult == true)
                        {
                            IPAddress hostIP = IPAddress.Parse(host);
                            SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(hostIP, port)), 0);
                            SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                            result.AddLookupResult(sipLookupEndPoint);
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "Exception SIPDNSManager ResolveSIPService (" + sipURI.ToString() + "). " + ex, null));

                        //rj2: if there is a parsing exception, then fallback to original sipsorcery parsing mechanism
                        port = IPSocket.ParsePortFromSocket(sipURI.Host);
                        explicitPort = port >= 0;

                        //if(Regex.Match(host, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$").Success)
                        if (SIPSorcery.Sys.IPSocket.IsIPAddress(host))
                        {
                            // Target is an IP address, no DNS lookup required.
                            IPAddress hostIP = IPAddress.Parse(host);
                            SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(hostIP, port)), 0);
                            SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                            result.AddLookupResult(sipLookupEndPoint);
                            return result;
                        }
                    }
                    if (!explicitPort)
                    {
                        //port = (sipURI.Scheme == SIPSchemesEnum.sip) ? m_defaultSIPPort : m_defaultSIPSPort;
                        port = sipURI.Protocol != SIPProtocolsEnum.tls ? m_defaultSIPPort : m_defaultSIPSPort;
                    }

                    if (host.Contains(".") == false)
                    {
                        string hostOnly = IPSocket.ParseHostFromSocket(host);

                        // If host is not fully qualified then assume there's no point using NAPTR or SRV record look ups and go straight to A's.
                        if (hostOnly.ToLower() == System.Net.Dns.GetHostName()?.ToLower())
                        {
                            // The lookup is for the current machine.
                            var addressList = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList;

                            if (addressList?.Length == 0)
                            {
                                return new SIPDNSLookupResult(sipURI, $"Failed to resolve local machine hostname.");
                            }
                            else
                            {
                                // Preference for IPv4 IP address for local host name lookup.
                                IPAddress firstAddress = addressList.Where(x => x.AddressFamily == (preferIPv6 == true ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork)).FirstOrDefault() ?? addressList.FirstOrDefault();
                                SIPEndPoint resultEp = new SIPEndPoint(sipURI.Protocol, new IPEndPoint(firstAddress, port));
                                return new SIPDNSLookupResult(sipURI, resultEp);
                            }
                        }
                        else
                        {
                            return DNSNameRecordLookup(hostOnly, port, async, sipURI, null, preferIPv6);
                        }
                    }
                    else if (explicitPort)
                    {
                        // If target is a hostname with an explicit port then SIP lookup rules state to use DNS lookup for A or AAAA record.
                        string hostOnly = IPSocket.ParseHostFromSocket(host);
                        //return DNSARecordLookup(hostOnly, port, async, sipURI);
                        return DNSNameRecordLookup(hostOnly, port, async, sipURI, null, preferIPv6);
                    }
                    else
                    {
                        // Target is a hostname with no explicit port, use the whole NAPTR->SRV->A lookup procedure.
                        SIPDNSLookupResult sipLookupResult = new SIPDNSLookupResult(sipURI);

                        // Do without the NAPTR lookup for the time being. Very few organisations appear to use them and it can cost up to 2.5s to get a failed resolution.
                        //rj2: uncomment this section for telekom 1TR118 lookup
                        SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS full lookup requested for " + sipURI.ToString() + ".", null));
                        DNSNAPTRRecordLookup(host, async, ref sipLookupResult);
                        if (sipLookupResult.Pending)
                        {
                            if (!m_inProgressSIPServiceLookups.Contains(sipURI.ToString()))
                            {
                                m_inProgressSIPServiceLookups.Add(sipURI.ToString());
                                //ThreadPool.QueueUserWorkItem(delegate { ResolveSIPService(sipURI, false); });
                                System.Threading.ThreadPool.QueueUserWorkItem((obj) => ResolveSIPService(sipURI, false, preferIPv6));
                            }
                            return sipLookupResult;
                        }

                        DNSSRVRecordLookup(sipURI.Scheme, sipURI.Protocol, host, async, ref sipLookupResult);
                        if (sipLookupResult.Pending)
                        {
                            //logger.LogDebug("SIPDNSManager SRV lookup for " + host + " is pending.");
                            return sipLookupResult;
                        }
                        else
                        {
                            //logger.LogDebug("SIPDNSManager SRV lookup for " + host + " is final.");

                            // Add some custom logic to cope with sips SRV records using _sips._tcp (e.g. free.call.ciscospark.com).
                            // By default only _sips._tls SRV records are checked for. THis block adds an additional check for _sips._tcp SRV records.
                            //if ((sipLookupResult.SIPSRVResults == null || sipLookupResult.SIPSRVResults.Count == 0) && sipURI.Scheme == SIPSchemesEnum.sips)
                            //{
                            //    DNSSRVRecordLookup(sipURI.Scheme, SIPProtocolsEnum.tcp, host, async, ref sipLookupResult);
                            //    SIPDNSServiceResult nextSRVRecord = sipLookupResult.GetNextUnusedSRV();
                            //    int lookupPort = (nextSRVRecord != null) ? nextSRVRecord.Port : port;
                            //    return DNSARecordLookup(nextSRVRecord, host, lookupPort, async, sipLookupResult.URI);
                            //}
                            //else
                            //{
                            //    SIPDNSServiceResult nextSRVRecord = sipLookupResult.GetNextUnusedSRV();
                            //    int lookupPort = (nextSRVRecord != null) ? nextSRVRecord.Port : port;
                            //    return DNSARecordLookup(nextSRVRecord, host, lookupPort, async, sipLookupResult.URI);
                            //}
                            return DNSNameRecordLookup(host, port, async, sipLookupResult.URI, ref sipLookupResult, preferIPv6);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPDNSManager ResolveSIPService (" + sipURI.ToString() + "). " + excp.Message);
                m_inProgressSIPServiceLookups.Remove(sipURI.ToString());
                return new SIPDNSLookupResult(sipURI, excp.Message);
            }
        }

        public static async Task<SIPDNSLookupResult> ResolveAsync(SIPURI sipURI, bool? preferIPv6 = null)
        {
            try
            {
                if (sipURI == null)
                {
                    throw new ArgumentNullException("sipURI", "Cannot resolve SIP service on a null URI.");
                }

                if (IPSocket.TryParseIPEndPoint(sipURI.MAddrOrHost, out var ipEndPoint))
                {
                    // Target is an IP address, no DNS lookup required.
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, ipEndPoint), 0);
                    SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                    result.AddLookupResult(sipLookupEndPoint);
                    return result;
                }
                else
                {
                    string host = sipURI.MAddrOrHostAddress;
                    //int port = IPSocket.ParsePortFromSocket(host);
                    int port = sipURI.Protocol != SIPProtocolsEnum.tls ? m_defaultSIPPort : m_defaultSIPSPort;
                    if (preferIPv6 == null)
                    {
                        preferIPv6 = SIPDNSManager.PreferIPv6NameResolution;
                    }
                    bool explicitPort = false;

                    try
                    {
                        //Parse returns true if sipURI.Host can be parsed as an ipaddress
                        bool parseresult = SIPSorcery.Sys.IPSocket.Parse(sipURI.MAddrOrHost, out host, out port);
                        explicitPort = port >= 0;
                        if (!explicitPort)
                        {
                            //port = (sipURI.Scheme == SIPSchemesEnum.sip) ? m_defaultSIPPort : m_defaultSIPSPort;
                            port = sipURI.Protocol != SIPProtocolsEnum.tls ? m_defaultSIPPort : m_defaultSIPSPort;
                        }
                        if (parseresult == true)
                        {
                            IPAddress hostIP = IPAddress.Parse(host);
                            SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(hostIP, port)), 0);
                            SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                            result.AddLookupResult(sipLookupEndPoint);
                            return result;
                        }
                    }
                    catch (Exception ex)
                    {
                        SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "Exception SIPDNSManager ResolveSIPService (" + sipURI.ToString() + "). " + ex, null));

                        //rj2: if there is a parsing exception, then fallback to original sipsorcery parsing mechanism
                        port = IPSocket.ParsePortFromSocket(sipURI.Host);
                        explicitPort = port > 0;

                        //if(Regex.Match(host, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$").Success)
                        if (SIPSorcery.Sys.IPSocket.IsIPAddress(host))
                        {
                            // Target is an IP address, no DNS lookup required.
                            IPAddress hostIP = IPAddress.Parse(host);
                            SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(hostIP, port)), 0);
                            SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                            result.AddLookupResult(sipLookupEndPoint);
                            return result;
                        }
                    }

                    if (!explicitPort)
                    {
                        //port = (sipURI.Scheme == SIPSchemesEnum.sip) ? m_defaultSIPPort : m_defaultSIPSPort;
                        port = sipURI.Protocol != SIPProtocolsEnum.tls ? m_defaultSIPPort : m_defaultSIPSPort;
                    }

                    if (host.Contains(".") == false)
                    {
                        string hostOnly = IPSocket.ParseHostFromSocket(host);

                        // If host is not fully qualified then assume there's no point using NAPTR or SRV record look ups and go straight to A's.
                        if (hostOnly.ToLower() == System.Net.Dns.GetHostName()?.ToLower())
                        {
                            // The lookup is for the current machine.
                            var addressList = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList;

                            if (addressList?.Length == 0)
                            {
                                return new SIPDNSLookupResult(sipURI, $"Failed to resolve local machine hostname.");
                            }
                            else
                            {
                                // Preference for IPv4 IP address for local host name lookup.
                                IPAddress firstAddress = addressList.Where(x => x.AddressFamily == (preferIPv6 == true ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork)).FirstOrDefault() ?? addressList.FirstOrDefault();
                                SIPEndPoint resultEp = new SIPEndPoint(sipURI.Protocol, new IPEndPoint(firstAddress, port));
                                return new SIPDNSLookupResult(sipURI, resultEp);
                            }
                        }
                        else
                        {
                            return await Task.Run(() => DNSNameRecordLookup(hostOnly, port, false, sipURI, null, preferIPv6));
                        }
                    }
                    else if (explicitPort)
                    {
                        // If target is a hostname with an explicit port then SIP lookup rules state to use DNS lookup for A or AAAA record.
                        host = host.Substring(0, host.LastIndexOf(':'));
                        return await Task.Run(() => DNSNameRecordLookup(host, port, false, sipURI, null, preferIPv6));
                    }
                    else
                    {
                        // Target is a hostname with no explicit port, use the whole NAPTR->SRV->A lookup procedure.
                        SIPDNSLookupResult sipLookupResult = new SIPDNSLookupResult(sipURI);

                        // Do without the NAPTR lookup for the time being. Very few organisations appear to use them and it can cost up to 2.5s to get a failed resolution.
                        //rj2: uncomment this section for telekom 1TR118 lookup
                        SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS full lookup requested for " + sipURI.ToString() + ".", null));
                        DNSNAPTRRecordLookup(host, false, ref sipLookupResult);
                        if (sipLookupResult.Pending)
                        {
                            if (!m_inProgressSIPServiceLookups.Contains(sipURI.ToString()))
                            {
                                m_inProgressSIPServiceLookups.Add(sipURI.ToString());
                                //ThreadPool.QueueUserWorkItem(delegate { ResolveSIPService(sipURI, false); });
                                System.Threading.ThreadPool.QueueUserWorkItem((obj) => ResolveSIPService(sipURI, false, preferIPv6));
                            }
                            return sipLookupResult;
                        }

                        return await Task.Run(() =>
                        {
                            DNSSRVRecordLookup(sipURI.Scheme, sipURI.Protocol, host, false, ref sipLookupResult);
                            if (sipLookupResult.Pending)
                            {
                                //logger.LogDebug("SIPDNSManager SRV lookup for " + host + " is pending.");
                                return sipLookupResult;
                            }
                            else
                            {
                                //logger.LogDebug("SIPDNSManager SRV lookup for " + host + " is final.");

                                // Add some custom logic to cope with sips SRV records using _sips._tcp (e.g. free.call.ciscospark.com).
                                // By default only _sips._tls SRV records are checked for. THis block adds an additional check for _sips._tcp SRV records.
                                //if ((sipLookupResult.SIPSRVResults == null || sipLookupResult.SIPSRVResults.Count == 0) && sipURI.Scheme == SIPSchemesEnum.sips)
                                //{
                                //    DNSSRVRecordLookup(sipURI.Scheme, SIPProtocolsEnum.tcp, host, async, ref sipLookupResult);
                                //    SIPDNSServiceResult nextSRVRecord = sipLookupResult.GetNextUnusedSRV();
                                //    int lookupPort = (nextSRVRecord != null) ? nextSRVRecord.Port : port;
                                //    return DNSARecordLookup(nextSRVRecord, host, lookupPort, async, sipLookupResult.URI);
                                //}
                                //else
                                //{
                                //   SIPDNSServiceResult nextSRVRecord = sipLookupResult.GetNextUnusedSRV();
                                //   int lookupPort = (nextSRVRecord != null) ? nextSRVRecord.Port : port;
                                //   return DNSARecordLookup(nextSRVRecord, host, lookupPort, false, sipLookupResult.URI);
                                //}
                                return DNSNameRecordLookup(host, port, false, sipLookupResult.URI, ref sipLookupResult, preferIPv6);
                            }
                        });
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPDNSManager ResolveSIPService (" + sipURI.ToString() + "). " + excp.Message);
                m_inProgressSIPServiceLookups.Remove(sipURI.ToString());
                return new SIPDNSLookupResult(sipURI, excp.Message);
            }
        }

        public static SIPDNSLookupResult DNSARecordLookup(string host, int port, bool async, SIPURI uri, SIPDNSLookupResult lookupResult = null)
        {
            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record lookup requested for " + host + ".", null));
            SIPDNSLookupResult result = new SIPDNSLookupResult(uri);

            DNSResponse aRecordResponse = DNSManager.Lookup(host, QType.A, DNS_A_RECORD_LOOKUP_TIMEOUT, null, true, async);
            if (aRecordResponse == null && async)
            {
                result.Pending = true;
            }
            else if (aRecordResponse == null)
            {
            }
            else if (aRecordResponse.Timedout)
            {
                result.ATimedoutAt = DateTime.Now;
            }
            else if (!string.IsNullOrWhiteSpace(aRecordResponse.Error))
            {
                result.LookupError = aRecordResponse.Error;
            }
            else if (aRecordResponse.RecordsA == null || aRecordResponse.RecordsA.Length == 0)
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS no A records found for " + host + ".", null));
                result.LookupError = "No A records found for " + host + ".";
            }
            else
            {
                SIPURI sipURI = result.URI;
                foreach (RecordA aRecord in aRecordResponse.RecordsA)
                {
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(aRecord.Address, port), null, null), aRecord.RR.TTL);
                    result.AddLookupResult(sipLookupEndPoint);
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record found for " + host + ", result " + sipLookupEndPoint.LookupEndPoint.ToString() + ".", null));
                }
            }

            return result;
        }

        public static SIPDNSLookupResult DNSAAAARecordLookup(string host, int port, bool async, SIPURI uri, SIPDNSLookupResult lookupResult = null)
        {
            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record lookup requested for " + host + ".", null));

            SIPDNSLookupResult result = lookupResult ?? new SIPDNSLookupResult(uri);
            result.LookupError = null;

            DNSResponse aaaaRecordResponse = DNSManager.Lookup(host, QType.AAAA, DNS_A_RECORD_LOOKUP_TIMEOUT, null, true, async);
            if (aaaaRecordResponse == null && async)
            {
                result.Pending = true;
            }
            else if (aaaaRecordResponse == null)
            {
            }
            else if (aaaaRecordResponse.Timedout)
            {
                result.ATimedoutAt = DateTime.Now;
            }
            else if (!string.IsNullOrWhiteSpace(aaaaRecordResponse.Error))
            {
                result.LookupError = aaaaRecordResponse.Error;
            }
            else if (aaaaRecordResponse.RecordsAAAA == null || aaaaRecordResponse.RecordsAAAA.Length == 0)
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS no AAAA records found for " + host + ".", null));
                result.LookupError = "No AAAA records found for " + host + ".";
            }
            else
            {
                SIPURI sipURI = result.URI;
                foreach (RecordAAAA aRecord in aaaaRecordResponse.RecordsAAAA)
                {
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(aRecord.Address, port)), aRecord.RR.TTL);
                    result.AddLookupResult(sipLookupEndPoint);
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS AAAA record found for " + host + ", result " + sipLookupEndPoint.LookupEndPoint.ToString() + ".", null));
                }
            }

            return result;
        }

        public static SIPDNSLookupResult DNSARecordLookup(SIPDNSServiceResult nextSRVRecord, string host, int port, bool async, SIPURI lookupURI)
        {
            if (nextSRVRecord != null && nextSRVRecord.Data != null)
            {
                return DNSARecordLookup(nextSRVRecord.Data, port, async, lookupURI);
            }
            else
            {
                return DNSARecordLookup(host, port, async, lookupURI);
            }
        }

        public static SIPDNSLookupResult DNSAAAARecordLookup(SIPDNSServiceResult nextSRVRecord, string host, int port, bool async, SIPURI lookupURI)
        {
            if (nextSRVRecord != null && nextSRVRecord.Data != null)
            {
                return DNSAAAARecordLookup(nextSRVRecord.Data, port, async, lookupURI);
            }
            else
            {
                return DNSAAAARecordLookup(host, port, async, lookupURI);
            }
        }

        public static SIPDNSLookupResult DNSNameRecordLookup(string host, int port, bool async, SIPURI uri, SIPDNSLookupResult lookupResult = null, bool? preferIPv6 = null, int recursionLevel = 0)
        {
            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS Name record lookup requested for " + host + ".", null));

            SIPDNSLookupResult result = lookupResult ?? new SIPDNSLookupResult(uri);
            result.LookupError = null;

            DNSResponse aRecordResponse = DNSManager.Lookup(host, QType.ANY, DNS_A_RECORD_LOOKUP_TIMEOUT, null, true, async);
            if (aRecordResponse == null && async)
            {
                result.Pending = true;
            }
            else if (aRecordResponse == null)
            {
            }
            else if (aRecordResponse.Timedout)
            {
                result.ATimedoutAt = DateTime.Now;
            }
            else if (!string.IsNullOrWhiteSpace(aRecordResponse.Error))
            {
                result.LookupError = aRecordResponse.Error;
            }
            else if ((aRecordResponse.RecordsAAAA == null || aRecordResponse.RecordsAAAA.Length == 0) && (aRecordResponse.RecordsA == null || aRecordResponse.RecordsA.Length == 0) && (aRecordResponse.RecordsCNAME == null || aRecordResponse.RecordsCNAME.Length == 0))
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS no CNAME, A or AAAA records found for " + host + ".", null));
                result.LookupError = "No CNAME, A or AAAA records found for " + host + ".";
            }
            else
            {
                if (preferIPv6 == null)
                {
                    preferIPv6 = SIPDNSManager.PreferIPv6NameResolution;
                }

                foreach (RecordCNAME aRecord in aRecordResponse.RecordsCNAME)
                {
                    //CNAME could be another CNAME or A/AAAA Record -> max 3 levels recursive name resolution
                    if (recursionLevel < 3)
                    {
                        SIPDNSLookupResult resultCName = DNSNameRecordLookup(aRecord.CNAME, port, async, uri, lookupResult, preferIPv6, recursionLevel + 1);
                        if (resultCName != null)
                        {
                            foreach (SIPDNSLookupEndPoint ep in resultCName.EndPointResults)
                            {
                                result.AddLookupResult(ep);
                            }
                        }
                    }
                }
                if (preferIPv6 == true)
                {
                    SIPURI sipURI = result.URI;
                    foreach (RecordAAAA aRecord in aRecordResponse.RecordsAAAA)
                    {
                        SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(aRecord.Address, port)), aRecord.RR.TTL);
                        result.AddLookupResult(sipLookupEndPoint);
                        SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS AAAA record found for " + host + ", result " + sipLookupEndPoint.LookupEndPoint.ToString() + ".", null));
                    }
                    foreach (RecordA aRecord in aRecordResponse.RecordsA)
                    {
                        SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(aRecord.Address, port)), aRecord.RR.TTL);
                        result.AddLookupResult(sipLookupEndPoint);
                        SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record found for " + host + ", result " + sipLookupEndPoint.LookupEndPoint.ToString() + ".", null));
                    }
                }
                else
                {
                    SIPURI sipURI = result.URI;
                    foreach (RecordA aRecord in aRecordResponse.RecordsA)
                    {
                        SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(aRecord.Address, port)), aRecord.RR.TTL);
                        result.AddLookupResult(sipLookupEndPoint);
                        SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record found for " + host + ", result " + sipLookupEndPoint.LookupEndPoint.ToString() + ".", null));
                    }
                    foreach (RecordAAAA aRecord in aRecordResponse.RecordsAAAA)
                    {
                        SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(aRecord.Address, port)), aRecord.RR.TTL);
                        result.AddLookupResult(sipLookupEndPoint);
                        SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS AAAA record found for " + host + ", result " + sipLookupEndPoint.LookupEndPoint.ToString() + ".", null));
                    }
                }
            }

            if (result.LookupError != null)
            {
                if (preferIPv6 == true)
                {
                    result = DNSAAAARecordLookup(host, port, async, uri, lookupResult);
                }
                else
                {
                    result = DNSARecordLookup(host, port, async, uri, lookupResult);
                }
            }

            return result;
        }

        public static SIPDNSLookupResult DNSNameRecordLookup(SIPDNSServiceResult nextSRVRecord, string host, int port, bool async, SIPURI lookupURI, bool? preferIPv6 = null)
        {
            if (nextSRVRecord != null && nextSRVRecord.Data != null)
            {
                return DNSNameRecordLookup(nextSRVRecord.Data, port, async, lookupURI, null, preferIPv6);
            }
            else
            {
                return DNSNameRecordLookup(host, port, async, lookupURI, null, preferIPv6);
            }
        }

        public static SIPDNSLookupResult DNSNameRecordLookup(string host, int port, bool async, SIPURI lookupURI, ref SIPDNSLookupResult lookupResult, bool? preferIPv6 = null)
        {
            SIPDNSLookupResult result = null;
            if (lookupResult.SIPSRVResults != null)
            {
                foreach (SIPDNSServiceResult nextSRVRecord in lookupResult.SIPSRVResults)
                {
                    if (nextSRVRecord != null && nextSRVRecord.Data != null)
                    {
                        result = DNSNameRecordLookup(nextSRVRecord.Data, nextSRVRecord.Port, async, lookupResult.URI, lookupResult, preferIPv6);
                        if (result.LookupError == null)
                        {
                            return result;
                        }
                    }
                }
            }

            result = DNSNameRecordLookup(host, port, async, lookupResult.URI, lookupResult, preferIPv6);
            return result;
        }

        public static void DNSNAPTRRecordLookup(string host, bool async, ref SIPDNSLookupResult lookupResult)
        {
            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS NAPTR record lookup initiated for " + host + ".", null));

            // Target is a hostname with no explicit port, DNS lookup for NAPTR records.
            DNSResponse naptrRecordResponse = DNSManager.Lookup(host, QType.NAPTR, DNS_LOOKUP_TIMEOUT, null, true, async);
            if (naptrRecordResponse == null && async)
            {
                lookupResult.Pending = true;
            }
            else if (naptrRecordResponse.Timedout)
            {
                lookupResult.NAPTRTimedoutAt = DateTime.Now;
            }
            else if (string.IsNullOrWhiteSpace(naptrRecordResponse.Error) && naptrRecordResponse.RecordsNAPTR != null && naptrRecordResponse.RecordsNAPTR.Length > 0)
            {
                foreach (RecordNAPTR naptrRecord in naptrRecordResponse.RecordsNAPTR)
                {
                    SIPDNSServiceResult sipNAPTRResult = new SIPDNSServiceResult(SIPServices.GetService(naptrRecord.SERVICES), naptrRecord.ORDER, 0, naptrRecord.RR.TTL, naptrRecord.REPLACEMENT, 0, DateTime.Now);
                    lookupResult.AddNAPTRResult(sipNAPTRResult);
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS NAPTR record found for " + host + ", result " + naptrRecord.SERVICES + " " + naptrRecord.REPLACEMENT + ".", null));
                }
            }
            else
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS no NAPTR records found for " + host + ".", null));
            }
        }

        public static void DNSSRVRecordLookup(SIPSchemesEnum scheme, SIPProtocolsEnum protocol, string host, bool async, ref SIPDNSLookupResult lookupResult)
        {
            SIPServicesEnum reqdNAPTRService = SIPServicesEnum.none;
            if (scheme == SIPSchemesEnum.sip && protocol == SIPProtocolsEnum.udp)
            {
                reqdNAPTRService = SIPServicesEnum.sipudp;
            }
            else if (scheme == SIPSchemesEnum.sip && protocol == SIPProtocolsEnum.tcp)
            {
                reqdNAPTRService = SIPServicesEnum.siptcp;
            }
            else if (scheme == SIPSchemesEnum.sips && protocol == SIPProtocolsEnum.tcp)
            {
                reqdNAPTRService = SIPServicesEnum.sipstcp;
            }
            //rj2 2018-10-17: this looks wrong, but the www says: if sips then protocol is _tcp not _tls
            else if (scheme == SIPSchemesEnum.sips && protocol == SIPProtocolsEnum.tls)
            {
                reqdNAPTRService = SIPServicesEnum.sipstcp;
            }
            else if (scheme == SIPSchemesEnum.sip && protocol == SIPProtocolsEnum.tls)
            {
                reqdNAPTRService = SIPServicesEnum.siptls;
            }

            // If there are NAPTR records available see if there is a matching one for the SIP scheme and protocol required.
            // this works best (only works) if protocol in sip-uri matches sip-service of NAPTRRecord
            SIPDNSServiceResult naptrService = null;
            if (lookupResult.SIPNAPTRResults != null && lookupResult.SIPNAPTRResults.Count > 0)
            {
                if (reqdNAPTRService != SIPServicesEnum.none && lookupResult.SIPNAPTRResults.ContainsKey(reqdNAPTRService))
                {
                    naptrService = lookupResult.SIPNAPTRResults[reqdNAPTRService];
                }
            }

            // Construct the SRV target to lookup depending on whether an NAPTR record was available or not.
            string srvLookup = null;
            if (naptrService != null)
            {
                srvLookup = naptrService.Data;
            }
            else
            {
                if (scheme == SIPSchemesEnum.sips)
                {
                    srvLookup = SIPDNSConstants.SRV_SIPS_TCP_QUERY_PREFIX + host;
                }
                else
                {
                    srvLookup = "_" + scheme.ToString() + "._" + protocol.ToString() + "." + host;
                }
            }

            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS SRV record lookup requested for " + srvLookup + ".", null));

            DNSResponse srvRecordResponse = DNSManager.Lookup(srvLookup, QType.SRV, DNS_LOOKUP_TIMEOUT, null, true, async);
            if (srvRecordResponse == null && async)
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS SRV record lookup pending for " + srvLookup + ".", null));
                lookupResult.Pending = true;
            }
            else if (srvRecordResponse.Timedout)
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS SRV record lookup timed out for " + srvLookup + ".", null));
                lookupResult.SRVTimedoutAt = DateTime.Now;
            }
            else if (!string.IsNullOrWhiteSpace(srvRecordResponse.Error))
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS SRV record lookup for " + srvLookup + " returned error of " + lookupResult.LookupError + ".", null));
            }
            else if (string.IsNullOrWhiteSpace(srvRecordResponse.Error) && srvRecordResponse.RecordSRV != null && srvRecordResponse.RecordSRV.Length > 0)
            {
                foreach (RecordSRV srvRecord in srvRecordResponse.RecordSRV)
                {
                    SIPDNSServiceResult sipSRVResult = new SIPDNSServiceResult(reqdNAPTRService, srvRecord.PRIORITY, srvRecord.WEIGHT, srvRecord.RR.TTL, srvRecord.TARGET, srvRecord.PORT, DateTime.Now);
                    lookupResult.AddSRVResult(sipSRVResult);
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS SRV record found for " + srvLookup + ", result " + srvRecord.TARGET + " " + srvRecord.PORT + ".", null));
                }
            }
            else
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS no SRV records found for " + srvLookup + ".", null));
            }
        }
    }
}
