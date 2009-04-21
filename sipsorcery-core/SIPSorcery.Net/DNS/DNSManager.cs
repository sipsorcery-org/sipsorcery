// ============================================================================
// FileName: DNSManager.cs
//
// Description:
// Manages DNS lookups in a non-blocking way.
//
// Author(s):
// Aaron Clauson
//
// History:
// 19 Oct 2007	Aaron Clauson	Created.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text.RegularExpressions;
using Heijden.DNS;
using log4net;

namespace SIPSorcery.Net
{
	public class DNSManager
	{
        struct LookupRequest
        {
            public static LookupRequest Empty = new LookupRequest(null, DNSQType.NULL, DEFAULT_DNS_TIMEOUT, null, null);
            
            public string Hostname;
            public DNSQType QueryType;
            public int Timeout;
            public List<IPEndPoint> DNSServers;
            public ManualResetEvent CompleteEvent;

            public LookupRequest(string hostname, DNSQType queryType, int timeout, List<IPEndPoint> dnsServers, ManualResetEvent completeEvent)
            {
                Hostname = hostname;
                QueryType = queryType;
                Timeout = timeout;
                DNSServers = dnsServers;
                CompleteEvent = completeEvent;
            }
        }
        
        private const int DNS_REFRESH_INTERVAL = 2;     // DNS entries will be discarded after 2 minutes.
        private const int NUMBER_LOOKUP_THREADS = 5;    // Number of threads that will be available to undertake DNS lookups.
        private const string LOOKUP_THREAD_NAME = "dnslookup";
        private const int DEFAULT_DNS_TIMEOUT = 10;  // Default timeout in seconds for DNS lookups.
        private const string SIP_TCP_QUERY_PREFIX = "_sip._tcp.";
        private const string SIP_UDP_QUERY_PREFIX = "_sip._udp.";
        private const string SIP_TLS_QUERY_PREFIX = "_sip._tls.";
        private const string SIPS_TCP_QUERY_PREFIX = "_sips._tcp.";

        private static ILog logger = LogManager.GetLogger(LOOKUP_THREAD_NAME);

        private static Dictionary<string, DNSResponse> m_dnsResponses = new Dictionary<string, DNSResponse>();  // DNS query responses that have been looked up and stored.

        private static Queue<LookupRequest> m_queuedLookups = new Queue<LookupRequest>();                       // Used to store queued lookups.
        private static List<string> m_inProgressLookups = new List<string>();
        private static AutoResetEvent m_lookupARE = new AutoResetEvent(false);                                  // Used to trigger next waiting thread to do a queued lookup.

        private static Resolver m_resolver = null;

        private static bool m_close = false;    // Used to shutdown the DNS manager.

        static DNSManager()
        {
            try
            {
                IPEndPoint[] osDNSServers = Resolver.GetDnsServers();
                if (osDNSServers != null && osDNSServers.Length > 0)
                {
                    logger.Debug("Initialising DNS resolver with operating system DNS server entries.");
                    m_resolver = new Resolver(osDNSServers);
                }
                else
                {
                    logger.Debug("Initialising DNS resolver with OpenDNS server entries.");
                    m_resolver = new Resolver(Resolver.DefaultDnsServers);
                }
                m_resolver.Recursion = true;
                m_resolver.UseCache = false;

                for (int index = 0; index < NUMBER_LOOKUP_THREADS; index++)
                {
                    Thread lookupThread = new Thread(new ThreadStart(ProcessLookups));
                    lookupThread.Name = LOOKUP_THREAD_NAME + "-" + index.ToString();
                    lookupThread.Start();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception DNSManager (static ctor). " + excp);
            }
        }

        /// <summary>
        /// Does a lookup on the DNSManager's currently cached entries. If not found it returns null indicating no information is currently available on the 
        /// host while at the same time queueing a lookup for the DNSManager to do a lookup. Once the lookup has been completed the entry will be stored in
        /// cache and available for subsequent lookup requests. 
        /// 
        /// This approach to lookups is very useful for some SIP request situations. SIP has a built in retransmit mechanism so rather than hold up the processing
        /// of a SIP request while a DNS lookup is done the request can be ignored and in the time it takes for the SIP request retransmit the lookup can be done.
        /// </summary>
        /// <param name="hostname">The hostname of the A record to lookup in DNS.</param>
        /// <returns>If null is returned it means this is the first lookup for this hostname. The caller should wait a few seconds and call the method again.</returns>
        public static DNSResponse LookupAsync(string hostname)
        {
            return Lookup(hostname, DNSQType.A, DEFAULT_DNS_TIMEOUT, null, true, true);
        }

        public static DNSResponse LookupAsync(string hostname, DNSQType queryType)
        {
            return Lookup(hostname, queryType, DEFAULT_DNS_TIMEOUT, null, true, true);
        }
        
        /// <summary>
        /// This method will wait until either the lookup completes or the timeout is reached before returning.
        /// </summary>
        /// <param name="hostname">The hostname of the A record to lookup in DNS.</param>
        /// <param name="timeout">Timeout in seconds for the lookup.</param>
        /// <returns></returns>
        public static DNSResponse Lookup(string hostname, DNSQType queryType, int timeout, List<IPEndPoint> dnsServers)
        {
            return Lookup(hostname, queryType, timeout, dnsServers, true, false);
        }

        public static DNSResponse Lookup(string hostname, DNSQType queryType, int timeout, List<IPEndPoint> dnsServers, bool useCache, bool async)
        {
            if (hostname == null || hostname.Trim().Length == 0)
            {
                return null;
            }
            
            DNSResponse ipAddressResult = MatchIPAddress(hostname);

            if (ipAddressResult != null)
            {
                return ipAddressResult;
            }
            else if (useCache && LookupFromCache(hostname.Trim().ToLower(), queryType) != null)
            {
                return LookupFromCache(hostname.Trim().ToLower(), queryType);
            }
            else if (async)
            {
                QueueLookup(new LookupRequest(hostname.Trim().ToLower(), queryType, timeout, dnsServers, null));
                return null;
            }
            else
            {
                ManualResetEvent completeEvent = new ManualResetEvent(false);
                QueueLookup(new LookupRequest(hostname.Trim().ToLower(), queryType, timeout, dnsServers, completeEvent));

                if (completeEvent.WaitOne(timeout * 1000, false))
                {
                    // Completed event was fired, the DNS entry will now be in cache.
                    DNSResponse result = LookupFromCache(hostname, queryType);
                    if (result != null)
                    {
                        return result;
                    }
                    else
                    {
                        DNSResponse errorResponse = new DNSResponse();
                        errorResponse.Error = "Error retrieving from cache";
                        return errorResponse;
                    }
                }
                else
                {
                    // Timeout.
                    DNSResponse timeoutResponse = new DNSResponse();
                    timeoutResponse.Error = "Timeout";
                    return timeoutResponse;
                }
            }
        }

        /// <summary>
        /// This method attempts to synchronously lookup a SIP host. The method is suitable for something like a Stateless SIP Proxy which cannot afford
        /// to block and wait for DNS resolutions. If there are no entries cached for the host being lookedup the method will fail BUT it will still 
        /// queue the lookup. Because SIP use re-transmits this should result in the DNS lookup results being available when one of the subsequent 
        /// re-transmits arrives.
        /// </summary>
        /// <param name="hostname">The SIP service host to attempt to resolve.</param>
        /// <returns>If successful a list of IP addresses corresponding to the host name. If the host name is not cached in the lookup results then null
        /// will be returned BUT the lookup will still be queued so that if the host name is resolvable the result will be available in the cache for
        /// subsequent lookups.</returns>
        public static IPAddress[] LookupSIPServer(string hostname, bool synchronous)
        {
            if (!synchronous)
            {
                DNSResponse udpSRVResult = Lookup(SIP_UDP_QUERY_PREFIX + hostname, DNSQType.SRV, DEFAULT_DNS_TIMEOUT, null, true, true);
                DNSResponse tcpSRVResult = Lookup(SIP_TCP_QUERY_PREFIX + hostname, DNSQType.SRV, DEFAULT_DNS_TIMEOUT, null, true, true);
                DNSResponse ipAddressResult = Lookup(hostname, DNSQType.A, DEFAULT_DNS_TIMEOUT, null, true, true);

                if (udpSRVResult != null || tcpSRVResult != null || ipAddressResult != null)
                {
                    if (ipAddressResult.Error != null || ipAddressResult.RecordsA == null || ipAddressResult.RecordsA.Length == 0)
                    {
                        throw new ApplicationException("Could not resolve " + hostname + ".");
                    }
                    else
                    {
                        return new IPAddress[] { ipAddressResult.RecordsA[0].Address };
                    }
                }
                else
                {
                    return null;
                }
            }
            else
            {
                
                DNSResponse udpSRVResult = Lookup(SIP_UDP_QUERY_PREFIX + hostname, DNSQType.SRV, DEFAULT_DNS_TIMEOUT, null, true, false);
                DNSResponse tcpSRVResult = Lookup(SIP_TCP_QUERY_PREFIX + hostname, DNSQType.SRV, DEFAULT_DNS_TIMEOUT, null, true, false);
                DNSResponse ipAddressResult = Lookup(hostname, DNSQType.A, DEFAULT_DNS_TIMEOUT, null, true, false);

                if (udpSRVResult != null || tcpSRVResult != null || ipAddressResult != null)
                {
                    if (ipAddressResult.Error != null || ipAddressResult.RecordsA == null || ipAddressResult.RecordsA.Length == 0)
                    {
                        throw new ApplicationException("Could not resolve " + hostname + ".");
                    }
                    else
                    {
                        return new IPAddress[] { ipAddressResult.RecordsA[0].Address };
                    }
                }
                else
                {
                    return null;
                }
            }
        }

        private static DNSResponse MatchIPAddress(string hostname)
        {
            try
            {
                if (hostname != null && hostname.Trim().Length > 0)
                {
                    hostname = hostname.Trim();

                    if (Regex.Match(hostname, @"(\d+\.){3}\d+(:\d+$|$)").Success)
                    {
                        string ipAddress = Regex.Match(hostname, @"(?<ipaddress>(\d+\.){3}\d+)(:\d+$|$)").Result("${ipaddress}");
                        DNSResponse result = new DNSResponse(IPAddress.Parse(ipAddress));
                        return result;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MatchIPAddress. " + excp);
                return null;
            }
        }

        private static DNSResponse LookupFromCache(string hostname, DNSQType queryType)
        {
            try
            {
                string canonicalHostname = hostname.Trim().ToLower();

                if (m_dnsResponses.ContainsKey(queryType.ToString() + ":" + canonicalHostname))
                {
                    DNSResponse result = m_dnsResponses[queryType.ToString() + ":" + canonicalHostname];

                    if (DateTime.Now.Subtract(result.TimeStamp).TotalMinutes > DNS_REFRESH_INTERVAL)
                    {
                        //logger.Debug("Removing expired DNS entry for " + result.Hostname + ".");

                        // Entry removed and placed back in the queue for a lookup refresh.
                        lock (m_dnsResponses)
                        {
                            m_dnsResponses.Remove(queryType.ToString() + ":" + canonicalHostname);
                        }

                        return null;
                    }
                    else
                    {
                        return result;
                    }
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception LookupFromCache (" + hostname + "). " + excp);
                return null;
            }
        }

        public static void Stop()
        {
            logger.Debug("DNSManager Stopping.");

            m_close = true;

            for (int index = 0; index < NUMBER_LOOKUP_THREADS; index++)
            {
                m_lookupARE.Set();
            }
        }

        private static void QueueLookup(LookupRequest lookupRequest)
        {
            // If there is no need to alert the caller when the lookup is complete AND the lookup is already queued there is no need to re-add it.
            if (lookupRequest.CompleteEvent != null || !m_inProgressLookups.Contains(lookupRequest.Hostname))
           {
               lock (m_inProgressLookups)
               {
                   m_inProgressLookups.Add(lookupRequest.QueryType.ToString() + ":" + lookupRequest.Hostname);      // Stops the same hostname going into the queue while this one is being looked up.
               }

                lock (m_queuedLookups)
                {
                    m_queuedLookups.Enqueue(lookupRequest);
                }

                logger.Debug("Lookup queueud for " + lookupRequest.QueryType + " " + lookupRequest.Hostname + ", queue size=" + m_queuedLookups.Count + ", in progress=" + m_inProgressLookups.Count + ".");
                
                m_lookupARE.Set();
            }
        }

        private static void ProcessLookups()
        {
            string hostname = null;
            
            try
            {
                string threadName = Thread.CurrentThread.Name;
                //logger.Debug("DNS Lookup Thread " + threadName + " started.");

                while (!m_close)
                {
                    int lookups = 0;
                    while (m_queuedLookups.Count > 0 && !m_close)
                    {
                        LookupRequest lookupRequest = LookupRequest.Empty;
                        string queryType = null;
                        //string hostname = null;
                        DNSResponse dnsResponse = null;
                        DateTime startLookupTime = DateTime.Now;

                        try
                        {
                            lock (m_queuedLookups)
                            {
                                if (m_queuedLookups.Count > 0)
                                {
                                    lookupRequest = m_queuedLookups.Dequeue();
                                    hostname = lookupRequest.Hostname;
                                    queryType = lookupRequest.QueryType.ToString();
                                }
                                else
                                {
                                    // Another thread got in ahead of this one to do the lookup.
                                    continue;
                                }
                            }

                            lookups++;
                            logger.Debug("Thread " + threadName + " looking up " + queryType + " " + lookupRequest.Hostname + ".");

                            //dnsEntry = new DNSEntry(hostname);
                            //dnsEntry.LastLookup = DateTime.Now;

                            //IPHostEntry ipHostEntry = Dns.GetHostEntry(hostname);
                            if (lookupRequest.DNSServers == null)
                            {
                                dnsResponse = m_resolver.Query(lookupRequest.Hostname, lookupRequest.QueryType, lookupRequest.Timeout);
                            }
                            else
                            {
                                dnsResponse = m_resolver.Query(lookupRequest.Hostname, lookupRequest.QueryType, lookupRequest.Timeout, lookupRequest.DNSServers);
                            }

                            if (dnsResponse == null)
                            {
                                logger.Debug("Error resolving " + lookupRequest.Hostname + " no response was returned. Time taken=" + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                            }
                            else if (dnsResponse.Error != null)
                            {
                                logger.Debug("Error resolving " + lookupRequest.Hostname + ". " + dnsResponse.Error + ". Time taken=" + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                            }
                            else if (lookupRequest.QueryType == DNSQType.A)
                            {
                                if (dnsResponse.RecordsA != null && dnsResponse.RecordsA.Length > 0)
                                {
                                    logger.Debug("Resolved A record for " + lookupRequest.Hostname + " to " + dnsResponse.RecordsA[0].Address.ToString() + " in " + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                                }
                                else
                                {
                                    logger.Debug("Could not resolve A record for " + lookupRequest.Hostname + " in " + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                                }
                            }
                            else if (lookupRequest.QueryType == DNSQType.SRV)
                            {
                                logger.Debug("Resolve time for " + lookupRequest.Hostname + " " + lookupRequest.QueryType + " " + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                                if (dnsResponse.RecordsRR == null || dnsResponse.RecordsRR.Length == 0)
                                {
                                    logger.Debug(" no resource records found for " + lookupRequest.Hostname + ".");
                                }
                                else
                                {
                                    foreach (RecordSRV srvRecord in dnsResponse.RecordSRV)
                                    {
                                        logger.Debug(" result: priority=" + srvRecord.Priority + ", weight=" + srvRecord.Weight + ", port=" + srvRecord.Port + ", target=" + srvRecord.Target + ".");
                                    }
                                }
                            }
                            else
                            {
                                logger.Debug("Resolve time for " + lookupRequest.Hostname + " " + lookupRequest.QueryType + " " + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                            }

                            /*if (ipHostEntry != null)
                            {
                                logger.Debug("Resolve time for " + hostname + " " + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                                //dnsEntry.AddResult(DNSType.A, ipHostEntry);

                                //logger.Debug(ipHostEntry.AddressList.Length + " addresses returned for " + hostname + ".");
                                //foreach (IPAddress hostAddress in ipHostEntry.AddressList)
                                //{
                                //    logger.Debug(" " + hostAddress);
                                //}
                            }
                            else
                            {
                                dnsEntry.Unresolvable = true;
                                logger.Debug("Could not resolve " + hostname + " in " + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                            }*/
                        }
                        /*catch (SocketException)
                        {
                            dnsEntry.Unresolvable = true;
                            logger.Debug("Could not resolve " + hostname + " in " + DateTime.Now.Subtract(startLookupTime).TotalMilliseconds + "ms.");
                        }*/
                        catch (Exception lookupExcp)
                        {
                            //dnsEntry.Unresolvable = true;
                            dnsResponse.Error = "Exception lookup. " + lookupExcp.Message;
                            logger.Error("Exception ProcessLookups Lookup (thread, " + threadName + ", hostname=" + hostname + "). " + lookupExcp.GetType().ToString() + "-" + lookupExcp.Message);
                        }
                        finally
                        {
                            try
                            {
                                if (dnsResponse != null)
                                {
                                    lock (m_dnsResponses)
                                    {
                                        if (m_dnsResponses.ContainsKey(queryType + ":" + hostname))
                                        {
                                            m_dnsResponses.Remove(queryType + ":" + hostname);
                                        }

                                        if (!m_dnsResponses.ContainsKey(queryType + ":" + hostname))
                                        {
                                            m_dnsResponses.Add(queryType + ":" + hostname, dnsResponse);
                                        }
                                    }

                                    if (lookupRequest.CompleteEvent != null)
                                    {
                                        lookupRequest.CompleteEvent.Set();
                                    }

                                    // Mark any requests for the same hostname complete and fire the completed lookup event where required.
                                    lock (m_queuedLookups)
                                    {
                                        foreach (LookupRequest pendingRequest in m_queuedLookups)
                                        {
                                            if (pendingRequest.Hostname == hostname && pendingRequest.QueryType == lookupRequest.QueryType && pendingRequest.CompleteEvent != null)
                                            {
                                                pendingRequest.CompleteEvent.Set();
                                            }
                                        }
                                    }
                                }

                                lock (m_inProgressLookups)
                                {
                                    m_inProgressLookups.Remove(queryType + ":" + hostname);
                                }
                            }
                            catch (Exception excp)
                            {
                                logger.Error("Exception ProcessLookup Adding DNS Response. " + excp.Message);
                            }
                        }
                    }

                    //logger.Debug("Thread " + threadName + " performed " + lookups + " lookups.");

                    // No more lookups outstanding, put thread to sleep until a new lookup is required.
                    m_lookupARE.WaitOne();

                    //logger.Debug("Thread " + threadName + " signalled.");
                }

                //logger.Debug("Thread " + threadName + " shutdown.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception DNSManager ProcessLookups. " + excp.Message);
            }
            finally
            {
                if (m_close)
                {
                    m_lookupARE.Set();
                }

                logger.Debug("DNSManager thread shutdown.");
            }
        }
    }
}
