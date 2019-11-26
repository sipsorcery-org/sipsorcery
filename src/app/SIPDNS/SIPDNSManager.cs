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

        static SIPDNSManager()
        {
            SIPMonitorLogEvent = (e) => { };
        }

        public static SIPDNSLookupResult ResolveSIPService(string host)
        {
            try
            {
                return ResolveSIPService(SIPURI.ParseSIPURIRelaxed(host), true);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPDNSManager ResolveSIPService (" + host + "). " + excp.Message);
                throw;
            }
        }

        // TODO need to consolidate ResolveSIPService and ResolveAsync.
        public static SIPDNSLookupResult ResolveSIPService(SIPURI sipURI, bool async)
        {
            try
            {
                if (sipURI == null)
                {
                    throw new ArgumentNullException("sipURI", "Cannot resolve SIP service on a null URI.");
                }

                if (IPSocket.TryParseIPEndPoint(sipURI.Host, out var ipEndPoint))
                {
                    // Target is an IP address, no DNS lookup required.
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, ipEndPoint), 0);
                    SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                    result.AddLookupResult(sipLookupEndPoint);
                    return result;
                }
                else
                {
                    string host = sipURI.Host;
                    int port = IPSocket.ParsePortFromSocket(sipURI.Host);
                    bool explicitPort = (port != 0);

                    if (!explicitPort)
                    {
                        port = (sipURI.Scheme == SIPSchemesEnum.sip) ? m_defaultSIPPort : m_defaultSIPSPort;
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
                                // Preference for IPv4 IP address for local host anem lookup.
                                IPAddress firstAddress = addressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault() ?? addressList.FirstOrDefault();
                                SIPEndPoint resultEp = new SIPEndPoint(sipURI.Protocol, new IPEndPoint(firstAddress, port));
                                return new SIPDNSLookupResult(sipURI, resultEp);
                            }
                        }
                        else
                        {
                            return DNSARecordLookup(hostOnly, port, async, sipURI);
                        }
                    }
                    else if (explicitPort)
                    {
                        // If target is a hostname with an explicit port then SIP lookup rules state to use DNS lookup for A or AAAA record.
                        host = host.Substring(0, host.LastIndexOf(':'));
                        return DNSARecordLookup(host, port, async, sipURI);
                    }
                    else
                    {
                        // Target is a hostname with no explicit port, use the whole NAPTR->SRV->A lookup procedure.
                        SIPDNSLookupResult sipLookupResult = new SIPDNSLookupResult(sipURI);

                        // Do without the NAPTR lookup for the time being. Very few organisations appear to use them and it can cost up to 2.5s to get a failed resolution.
                        /*SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS full lookup requested for " + sipURI.ToString() + ".", null));
                        DNSNAPTRRecordLookup(host, async, ref sipLookupResult);
                        if (sipLookupResult.Pending)
                        {
                            if (!m_inProgressSIPServiceLookups.Contains(sipURI.ToString()))
                            {
                                m_inProgressSIPServiceLookups.Add(sipURI.ToString());
                                ThreadPool.QueueUserWorkItem(delegate { ResolveSIPService(sipURI, false); });
                            }
                            return sipLookupResult;
                        }*/

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
                            SIPDNSServiceResult nextSRVRecord = sipLookupResult.GetNextUnusedSRV();
                            int lookupPort = (nextSRVRecord != null) ? nextSRVRecord.Port : port;
                            return DNSARecordLookup(nextSRVRecord, host, lookupPort, async, sipLookupResult.URI);
                            //}
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

        public static async Task<SIPDNSLookupResult> ResolveAsync(SIPURI sipURI)
        {
            try
            {
                if (sipURI == null)
                {
                    throw new ArgumentNullException("sipURI", "Cannot resolve SIP service on a null URI.");
                }

                if (IPSocket.TryParseIPEndPoint(sipURI.Host, out var ipEndPoint))
                {
                    // Target is an IP address, no DNS lookup required.
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, ipEndPoint), 0);
                    SIPDNSLookupResult result = new SIPDNSLookupResult(sipURI);
                    result.AddLookupResult(sipLookupEndPoint);
                    return result;
                }
                else
                {
                    string host = sipURI.Host;
                    int port = IPSocket.ParsePortFromSocket(sipURI.Host);
                    bool explicitPort = (port != 0);

                    if (!explicitPort)
                    {
                        port = (sipURI.Scheme == SIPSchemesEnum.sip) ? m_defaultSIPPort : m_defaultSIPSPort;
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
                                IPAddress firstAddress = addressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault() ?? addressList.FirstOrDefault();
                                SIPEndPoint resultEp = new SIPEndPoint(sipURI.Protocol, new IPEndPoint(firstAddress, port));
                                return new SIPDNSLookupResult(sipURI, resultEp);
                            }
                        }
                        else
                        {
                            return await Task.Run(() => DNSARecordLookup(hostOnly, port, false, sipURI));
                        }
                    }
                    else if (explicitPort)
                    {
                        // If target is a hostname with an explicit port then SIP lookup rules state to use DNS lookup for A or AAAA record.
                        host = host.Substring(0, host.LastIndexOf(':'));
                        return await Task.Run(() => DNSARecordLookup(host, port, false, sipURI));
                    }
                    else
                    {
                        // Target is a hostname with no explicit port, use the whole NAPTR->SRV->A lookup procedure.
                        SIPDNSLookupResult sipLookupResult = new SIPDNSLookupResult(sipURI);

                        // Do without the NAPTR lookup for the time being. Very few organisations appear to use them and it can cost up to 2.5s to get a failed resolution.
                        /*SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS full lookup requested for " + sipURI.ToString() + ".", null));
                        DNSNAPTRRecordLookup(host, async, ref sipLookupResult);
                        if (sipLookupResult.Pending)
                        {
                            if (!m_inProgressSIPServiceLookups.Contains(sipURI.ToString()))
                            {
                                m_inProgressSIPServiceLookups.Add(sipURI.ToString());
                                ThreadPool.QueueUserWorkItem(delegate { ResolveSIPService(sipURI, false); });
                            }
                            return sipLookupResult;
                        }*/

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
                                SIPDNSServiceResult nextSRVRecord = sipLookupResult.GetNextUnusedSRV();
                                int lookupPort = (nextSRVRecord != null) ? nextSRVRecord.Port : port;
                                return DNSARecordLookup(nextSRVRecord, host, lookupPort, false, sipLookupResult.URI);
                                //}
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

        public static SIPDNSLookupResult DNSARecordLookup(string host, int port, bool async, SIPURI uri)
        {
            SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record lookup requested for " + host + ".", null));
            SIPDNSLookupResult result = new SIPDNSLookupResult(uri);

            DNSResponse aRecordResponse = DNSManager.Lookup(host, QType.A, DNS_A_RECORD_LOOKUP_TIMEOUT, null, true, async);
            if (aRecordResponse == null && async)
            {
                result.Pending = true;
            }
            else if (aRecordResponse.Timedout)
            {
                result.ATimedoutAt = DateTime.Now;
            }
            else if (aRecordResponse.Error != null)
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
                    SIPDNSLookupEndPoint sipLookupEndPoint = new SIPDNSLookupEndPoint(new SIPEndPoint(sipURI.Protocol, new IPEndPoint(aRecord.Address, port)), aRecord.RR.TTL);
                    result.AddLookupResult(sipLookupEndPoint);
                    SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS A record found for " + host + ", result " + sipLookupEndPoint.LookupEndPoint.ToString() + ".", null));
                }
            }

            return result;
        }

        public static SIPDNSLookupResult DNSARecordLookup(SIPDNSServiceResult nextSRVRecord, string host, int port, bool async, SIPURI lookupURI)
        {
            if (nextSRVRecord != null && nextSRVRecord.Data != null)
            {
                return DNSARecordLookup(nextSRVRecord.Data, port, async, lookupURI);
                //nextSRVRecord.ResolvedAt = DateTime.Now;
            }
            else
            {
                return DNSARecordLookup(host, port, async, lookupURI);
            }
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
            else if (naptrRecordResponse.Error == null && naptrRecordResponse.RecordNAPTR != null && naptrRecordResponse.RecordNAPTR.Length > 0)
            {
                foreach (RecordNAPTR naptrRecord in naptrRecordResponse.RecordNAPTR)
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
            else if (scheme == SIPSchemesEnum.sip && protocol == SIPProtocolsEnum.tls)
            {
                reqdNAPTRService = SIPServicesEnum.siptls;
            }

            // If there are NAPTR records available see if there is a matching one for the SIP scheme and protocol required.
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
            else if (srvRecordResponse.Error != null)
            {
                SIPMonitorLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Unknown, SIPMonitorEventTypesEnum.DNS, "SIP DNS SRV record lookup for " + srvLookup + " returned error of " + lookupResult.LookupError + ".", null));
            }
            else if (srvRecordResponse.Error == null && srvRecordResponse.RecordSRV != null && srvRecordResponse.RecordSRV.Length > 0)
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
