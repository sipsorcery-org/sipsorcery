// ============================================================================
// FileName: Resolver.cs
//
// Description:
// Implements functionality to resolver DNS requests.
//
// Author(s):
// Alphons van der Heijden
// Aaron Clauson
//
// History:
// 28 Mar 2008	Aaron Clauson   Added to sipwitch code base based on http://www.codeproject.com/KB/library/DNS.NET_Resolver.aspx.
// 28 Mar 2008  Aaron Clauson   Moved timeout from being class scoped field to parameter for individual DNS requests.
// 28 Mar 2008  Aaron Clauson   Removed deprecated System.DNS methods.
// 28 Mar 2008  Aaron Clauson   Added log4net logging.
// 14 Oct 2019  Aaron Clauson   Synchronised (maintaining modifications) with latest version of source from at https://www.codeproject.com/Articles/23673/DNS-NET-Resolver-C.
//
// License:
// The Code Project Open License (CPOL) https://www.codeproject.com/info/cpol10.aspx
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

/*
 * Network Working Group                                     P. Mockapetris
 * Request for Comments: 1035                                           ISI
 *                                                            November 1987
 *
 *           DOMAIN NAMES - IMPLEMENTATION AND SPECIFICATION
 *
 */

namespace Heijden.DNS
{
    /// <summary>
    /// Resolver is the main class to do DNS query lookups
    /// </summary>
    public class Resolver
    {
        /// <summary>
        /// Version of this set of routines, when not in a library
        /// </summary>
        public string Version
        {
            get
            {
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }

        /// <summary>
        /// Default DNS port
        /// </summary>
        public const int DefaultPort = 53;

        /// <summary>
        /// Gets list of OPENDNS servers
        /// </summary>
        public static readonly List<IPEndPoint> DefaultDnsServers = new List<IPEndPoint>()
        {
            new IPEndPoint(IPAddress.Parse("208.67.222.222"), DefaultPort),
            new IPEndPoint(IPAddress.Parse("208.67.220.220"), DefaultPort)
        };

        public const int DEFAULT_TIMEOUT = 2;               // Default timeout in seconds for DNS lookups.
        private const int SWITCH_ACTIVE_TIMEOUT_COUNT = 5; // The timeout in seconds that if no response is received on the active DNS server it will be switched.
        private const int FAILURE_RETRY = 60;                // The timeout in seconds to retry DNS lookups that failed.
        private const int MIN_CACHE_SECONDS = 60;           // A record will not be removed from the cache when it has been in less than this number of seconds irrespective of the DNS TTL.

        private static ILogger logger = Log.Logger;

        private ushort m_Unique;
        private bool m_UseCache;
        private bool m_Recursion;
        private int m_Retries;
        private int m_Timeout;
        private TransportType m_TransportType;
        private bool m_stop;

        private List<IPEndPoint> m_DnsServers;
        private IPEndPoint m_activeDNSServer;   // The DNS server currently being used for lookups.
        private Dictionary<IPEndPoint, int> m_receiveTimeouts = new Dictionary<IPEndPoint, int>();

        private Dictionary<string, DNSResponse> m_ResponseCache;
        private Dictionary<string, DNSResponse> m_lookupFailures;

        /// <summary>
        /// Constructor of Resolver using DNS servers specified.
        /// </summary>
        /// <param name="DnsServers">Set of DNS servers</param>
        public Resolver(IPEndPoint[] DnsServers)
        {
            m_ResponseCache = new Dictionary<string, DNSResponse>();
            m_lookupFailures = new Dictionary<string, DNSResponse>();
            m_DnsServers = new List<IPEndPoint>();
            m_DnsServers.AddRange(DnsServers);

            m_Unique = (ushort)(new Random()).Next();
            m_Retries = 3;
            m_Timeout = 1;
            m_Recursion = true;
            m_UseCache = true;
            m_TransportType = TransportType.Udp;
            m_stop = false;
        }

        /// <summary>
        /// Constructor of Resolver using DNS server specified.
        /// </summary>
        /// <param name="DnsServer">DNS server to use</param>
        public Resolver(IPEndPoint DnsServer)
            : this(new IPEndPoint[] { DnsServer })
        {
        }

        /// <summary>
        /// Constructor of Resolver using DNS server and port specified.
        /// </summary>
        /// <param name="ServerIpAddress">DNS server to use</param>
        /// <param name="ServerPortNumber">DNS port to use</param>
        public Resolver(IPAddress ServerIpAddress, int ServerPortNumber)
            : this(new IPEndPoint(ServerIpAddress, ServerPortNumber))
        {
        }

        /// <summary>
        /// Constructor of Resolver using DNS address and port specified.
        /// </summary>
        /// <param name="ServerIpAddress">DNS server address to use</param>
        /// <param name="ServerPortNumber">DNS port to use</param>
        public Resolver(string ServerIpAddress, int ServerPortNumber)
            : this(IPAddress.Parse(ServerIpAddress), ServerPortNumber)
        {
        }

        /// <summary>
        /// Constructor of Resolver using DNS address.
        /// </summary>
        /// <param name="ServerIpAddress">DNS server address to use</param>
        public Resolver(string ServerIpAddress)
            : this(IPAddress.Parse(ServerIpAddress), DefaultPort)
        {
        }

        /// <summary>
        /// Resolver constructor, using DNS servers specified by Windows
        /// </summary>
        public Resolver()
            : this(GetDnsServers())
        {
        }

        public class VerboseOutputEventArgs : EventArgs
        {
            public string Message;
            public VerboseOutputEventArgs(string Message)
            {
                this.Message = Message;
            }
        }

        private void Verbose(string format, params object[] args)
        {
            OnVerbose?.Invoke(this, new VerboseEventArgs(string.Format(format, args)));
        }

        /// <summary>
        /// Verbose messages from internal operations
        /// </summary>
        public event VerboseEventHandler OnVerbose;
        public delegate void VerboseEventHandler(object sender, VerboseEventArgs e);

        public class VerboseEventArgs : EventArgs
        {
            public string Message;
            public VerboseEventArgs(string Message)
            {
                this.Message = Message;
            }
        }

        /// <summary>
        /// Gets or sets timeout in milliseconds
        /// </summary>
        public int TimeOut
        {
            get
            {
                return m_Timeout;
            }
            set
            {
                m_Timeout = value;
            }
        }

        /// <summary>
        /// Gets or sets number of retries before giving up
        /// </summary>
        public int Retries
        {
            get
            {
                return m_Retries;
            }
            set
            {
                if (value >= 1)
                {
                    m_Retries = value;
                }
            }
        }

        /// <summary>
        /// Gets or set recursion for doing queries
        /// </summary>
        public bool Recursion
        {
            get
            {
                return m_Recursion;
            }
            set
            {
                m_Recursion = value;
            }
        }

        /// <summary>
        /// Gets or sets protocol to use
        /// </summary>
        public TransportType TransportType
        {
            get
            {
                return m_TransportType;
            }
            set
            {
                m_TransportType = value;
            }
        }

        /// <summary>
        /// Gets or sets list of DNS servers to use
        /// </summary>
        public IPEndPoint[] DnsServers
        {
            get
            {
                return m_DnsServers.ToArray();
            }
            set
            {
                m_DnsServers.Clear();
                m_DnsServers.AddRange(value);
            }
        }

        /// <summary>
        /// Gets first DNS server address or sets first DNS server to use
        /// </summary>
        public string DnsServer
        {
            get
            {
                return m_DnsServers[0].Address.ToString();
            }
            set
            {
                //rj2: use IPSocket.Parse to get Parse String as IPEndpoint
                //with IPAddress.TryParse there would be no way to set DnsServer with (different) Port
                IPEndPoint ep = null;
                try
                {
                    ep = IPSocket.Parse(value, DefaultPort);
                }
                catch
                {

                }
                if (ep == null)
                {
                    DNSResponse response = Query(value, QType.A, DEFAULT_TIMEOUT);
                    if (response.RecordsA.Length > 0 && response.Error.IsNullOrBlank())
                    {
                        ep = new IPEndPoint(response.RecordsA[0].Address, DefaultPort);
                    }
                }
                if (m_DnsServers.Contains(ep))
                {
                    m_DnsServers.Remove(ep);
                    m_DnsServers.Insert(0, ep);
                }
                else
                {
                    m_DnsServers.Insert(0, ep);
                }
            }
        }

        public bool UseCache
        {
            get
            {
                return m_UseCache;
            }
            set
            {
                m_UseCache = value;
                if (!m_UseCache)
                {
                    m_ResponseCache.Clear();
                }
            }
        }

        public bool Stop
        {
            get
            {
                return this.m_stop;
            }
            set
            {
                this.m_stop = value;
            }
        }

        /// <summary>
        /// Clear the resolver cache
        /// </summary>
        public void ClearCache()
        {
            m_ResponseCache.Clear();
        }

        private DNSResponse SearchInCache(Question question)
        {
            if (!m_UseCache)
            {
                return null;
            }

            string strKey = question.QClass + "-" + question.QType + "-" + question.QName;
            //logger.LogDebug($"Searching DNS results cache for {strKey}.");

            lock (m_lookupFailures)
            {
                if (m_lookupFailures.ContainsKey(strKey))
                {
                    if (DateTime.Now.Subtract(m_lookupFailures[strKey].TimeStamp).TotalSeconds < FAILURE_RETRY)
                    {
                        return m_lookupFailures[strKey];
                    }
                    else
                    {
                        m_lookupFailures.Remove(strKey);
                        return null;
                    }
                }
            }

            DNSResponse response = null;

            lock (m_ResponseCache)
            {
                if (!m_ResponseCache.ContainsKey(strKey))
                {
                    return null;
                }

                response = m_ResponseCache[strKey];

                if (response.Answers.Count == 0)
                {
                    // A response should not have been cached with no answer records.
                    m_ResponseCache.Remove(strKey);
                    return null;
                }
            }

            int TimeLived = (int)((DateTime.Now.Ticks - response.TimeStamp.Ticks) / TimeSpan.TicksPerSecond);
            int secondsLived = (int)(DateTime.Now.Subtract(response.TimeStamp).TotalSeconds % Int32.MaxValue);
            //logger.LogDebug("Seconds lived=" + secondsLived + ".");
            foreach (RR rr in response.RecordsRR)
            {
                rr.TimeLived = TimeLived;
                // The TTL property calculates its actual time to live
                if (secondsLived > MIN_CACHE_SECONDS && secondsLived >= rr.TTL)
                {
                    logger.LogDebug($"DNS cache out of date result found for {strKey}. SecondsLived({secondsLived}) is >= remaining TimeToLive({rr.TTL})");
                    return null; // out of date
                }
                if (rr.TTL == 0)
                {
                    logger.LogDebug("DNS cache out of date (TTL==0) result found for " + strKey + ".");
                    return null; // out of date
                }
            }

            logger.LogDebug("DNS cache curent result found for " + strKey + ".");
            return response;
        }

        private void AddToCache(DNSResponse response, string questionKey)
        {
            if (!m_UseCache)
            {
                return;
            }
            else if (questionKey.IsNullOrBlank())
            {
                throw new ApplicationException("Cannot add a DNS response to the cache with an empty question key.");
            }

            // Question question = response.Questions[0];

            //string questionKey = question.QClass + "-" + question.QType + "-" + question.QName;
            if (response.Answers.Count == 0)
            {
                response.Error = "DNS response had no answers.";
            }

            //if (response.header.RCODE != RCode.NOERROR || response.Error != null)
            if (response.Error.IsNullOrBlank())
            {
                // Cache error responses for a short period of time to avoid overloading the server with failing DNS lookups.
                logger.LogDebug("Caching DNS lookup failure for " + questionKey + " error was " + response.Error + ".");
                lock (m_lookupFailures)
                {
                    if (m_lookupFailures.ContainsKey(questionKey))
                    {
                        m_lookupFailures.Remove(questionKey);
                    }

                    m_lookupFailures.Add(questionKey, response);
                }
            }
            else if (!response.Timedout && response.Answers.Count > 0)
            {
                // Cache non-error responses.
                logger.LogDebug("Caching DNS lookup success for " + questionKey + ".");
                lock (m_ResponseCache)
                {
                    if (m_ResponseCache.ContainsKey(questionKey))
                    {
                        m_ResponseCache.Remove(questionKey);
                    }
                    m_ResponseCache.Add(questionKey, response);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <param name="timeout">Timeout for lookup in seconds.</param>
        /// <returns></returns>
        private DNSResponse UdpRequest(DNSRequest request, List<IPEndPoint> dnsServers, int timeout)
        {
            // RFC1035 max. size of a UDP datagram is 512 bytes
            byte[] responseMessage = new byte[512];

            IPEndPoint activeDNSServer = GetActiveDNSServer();
            if (dnsServers == null)
            {
                dnsServers = new List<IPEndPoint>();
            }

            if (dnsServers.Count == 0)
            {
                dnsServers.Add(activeDNSServer);
            }

            ResetAllTimeoutCountIfNecessary();

            string requestStr = request.Questions[0].QType + " " + request.Questions[0].QName;
            for (int nAttempts = 0; nAttempts < m_Retries && !m_stop; nAttempts++)
            {
                for (int nDnsServer = 0; nDnsServer < dnsServers.Count && !m_stop; nDnsServer++)
                {
                    if (dnsServers[nDnsServer].Address.IsIPv6LinkLocal)
                    {
                        continue;
                    }
                    if (GetTimeoutCount(dnsServers[nDnsServer]) >= SWITCH_ACTIVE_TIMEOUT_COUNT)
                    {
                        logger.LogDebug("Resolver not sending UDP DNS request to " + dnsServers[nDnsServer] + " because of maximum count of request timeouts reached");
                        continue;
                    }
                    string requestStrWithDns = "for '" + requestStr + "' on dns-server '" + dnsServers[nDnsServer] + "'";
                    logger.LogDebug("Resolver sending UDP DNS request " + requestStrWithDns);

                    Socket socket = new Socket(dnsServers[nDnsServer].AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                    socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, timeout * 1000);

                    try
                    {
                        socket.SendTo(request.Data, dnsServers[nDnsServer]);
                        int nReceived = socket.Receive(responseMessage);
                        ResetTimeoutCount(dnsServers[nDnsServer]);
                        byte[] data = new byte[nReceived];
                        Array.Copy(responseMessage, data, nReceived);
                        DNSResponse response = new DNSResponse(dnsServers[nDnsServer], data);
                        if (response.header.RCODE == RCode.NOERROR)
                        {
                            logger.LogInformation("Success in UdpRequest " + requestStrWithDns);
                            return response;
                        }
                        else
                        {
                            logger.LogDebug("Error " + response.header.RCODE + " in Resolver UdpRequest " + requestStrWithDns);
                        }
                    }
                    catch (SocketException sockExcp)
                    {
                        IncrementTimeoutCount(dnsServers[nDnsServer]);
                        logger.LogWarning("SocketExcpetion(" + sockExcp.ErrorCode + ") Resolver UdpRequest connection to nameserver " + dnsServers[nDnsServer] + " failed. ", sockExcp);
                        continue; // next try
                    }
                    catch (Exception excp)
                    {
                        logger.LogError("Exception Resolver UdpRequest " + requestStrWithDns + ". ", excp);
                    }
                    finally
                    {
                        m_Unique++;

                        // close the socket
                        socket.Close();
                    }
                }
            }

            logger.LogWarning("Resolver UDP request timed out for " + requestStr + ".");
            DNSResponse responseTimeout = new DNSResponse();
            responseTimeout.Timedout = true;
            responseTimeout.Error = "Timeout Error";
            return responseTimeout;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="request"></param>
        /// <param name="timeout">Timeout for lookup in seconds.</param>
        /// <returns></returns>
        private DNSResponse TcpRequest(DNSRequest request, List<IPEndPoint> dnsServers, int timeout)
        {
            byte[] responseMessage = new byte[512];

            IPEndPoint activeDNSServer = GetActiveDNSServer();
            if (dnsServers == null)
            {
                dnsServers = new List<IPEndPoint>();
            }

            if (dnsServers.Count == 0)
            {
                dnsServers.Add(activeDNSServer);
            }

            ResetAllTimeoutCountIfNecessary();

            string requestStr = request.Questions[0].QType + " " + request.Questions[0].QName;
            for (int nAttempts = 0; nAttempts < m_Retries && !m_stop; nAttempts++)
            {
                for (int nDnsServer = 0; nDnsServer < dnsServers.Count && !m_stop; nDnsServer++)
                {
                    if (dnsServers[nDnsServer].Address.IsIPv6LinkLocal)
                    {
                        continue;
                    }
                    if (GetTimeoutCount(dnsServers[nDnsServer]) >= SWITCH_ACTIVE_TIMEOUT_COUNT)
                    {
                        logger.LogDebug("Resolver not sending TCP DNS request to " + dnsServers[nDnsServer] + " because of maximum count of request timeouts reached");
                        continue;
                    }
                    string requestStrWithDns = "for '" + requestStr + "' on dns-server '" + dnsServers[nDnsServer] + "'";
                    logger.LogDebug("Resolver sending TCP DNS request " + requestStrWithDns);

                    TcpClient tcpClient = new TcpClient();
                    tcpClient.ReceiveTimeout = timeout * 1000;

                    try
                    {
                        IAsyncResult result = tcpClient.BeginConnect(dnsServers[nDnsServer].Address, dnsServers[nDnsServer].Port, null, null);

                        bool success = result.AsyncWaitHandle.WaitOne(timeout * 1000, true);

                        if (!success || !tcpClient.Connected)
                        {
                            tcpClient.Close();
                            Verbose(string.Format(";; Connection to nameserver {0} failed", (nDnsServer + 1)));
                            continue;
                        }

                        BufferedStream bs = new BufferedStream(tcpClient.GetStream());

                        byte[] data = request.Data;
                        bs.WriteByte((byte)((data.Length >> 8) & 0xff));
                        bs.WriteByte((byte)(data.Length & 0xff));
                        bs.Write(data, 0, data.Length);
                        bs.Flush();

                        DNSResponse TransferResponse = new DNSResponse();
                        int intSoa = 0;
                        int intMessageSize = 0;

                        //Debug.WriteLine("Sending "+ (request.Length+2) + " bytes in "+ sw.ElapsedMilliseconds+" mS");

                        while (true)
                        {
                            int intLength = bs.ReadByte() << 8 | bs.ReadByte();
                            if (intLength <= 0)
                            {
                                tcpClient.Close();
                                Verbose(string.Format(";; Connection to nameserver {0} failed", (nDnsServer + 1)));
                                throw new SocketException(); // next try
                            }

                            intMessageSize += intLength;

                            data = new byte[intLength];
                            bs.Read(data, 0, intLength);
                            DNSResponse response = new DNSResponse(m_DnsServers[nDnsServer], data);

                            if (response.header.RCODE != RCode.NOERROR)
                            {
                                logger.LogDebug("Error " + response.header.RCODE + " in Resolver TcpRequest " + requestStrWithDns);
                                return response;
                            }
                            else
                            {
                                logger.LogInformation("Success in TcpRequest " + requestStrWithDns);
                            }

                            if (response.Questions[0].QType != QType.AXFR)
                            {
                                return response;
                            }

                            // Zone transfer!!

                            if (TransferResponse.Questions.Count == 0)
                            {
                                TransferResponse.Questions.AddRange(response.Questions);
                            }

                            TransferResponse.Answers.AddRange(response.Answers);
                            TransferResponse.Authorities.AddRange(response.Authorities);
                            TransferResponse.Additionals.AddRange(response.Additionals);

                            if (response.Answers[0].Type == DnsType.SOA)
                            {
                                intSoa++;
                            }

                            if (intSoa == 2)
                            {
                                TransferResponse.header.QDCOUNT = (ushort)TransferResponse.Questions.Count;
                                TransferResponse.header.ANCOUNT = (ushort)TransferResponse.Answers.Count;
                                TransferResponse.header.NSCOUNT = (ushort)TransferResponse.Authorities.Count;
                                TransferResponse.header.ARCOUNT = (ushort)TransferResponse.Additionals.Count;
                                TransferResponse.MessageSize = intMessageSize;
                                return TransferResponse;
                            }
                        }
                    } // try
                    catch (SocketException sockExcp)
                    {
                        IncrementTimeoutCount(dnsServers[nDnsServer]);
                        logger.LogWarning("SocketExcpetion(" + sockExcp.ErrorCode + ") Resolver TcpRequest connection to nameserver " + dnsServers[nDnsServer] + " failed. ", sockExcp);
                        continue; // next try
                    }
                    catch (Exception excp)
                    {
                        logger.LogError("Exception Resolver UdpRequest " + requestStrWithDns + ". ", excp);
                    }
                    finally
                    {
                        m_Unique++;

                        // close the socket
                        tcpClient.Close();
                    }
                }
            }

            logger.LogWarning("Resolver TCP request timed out for " + requestStr + ".");
            DNSResponse responseTimeout = new DNSResponse();
            responseTimeout.Timedout = true;
            responseTimeout.Error = "Timeout Error";
            return responseTimeout;
        }

        /// <summary>
        /// Do an QClass=IN Query on the previously cached results.
        /// </summary>
        /// <param name="name">Name to query</param>
        /// <param name="qtype">Question type</param>
        /// <returns>Response of the query</returns>
        public DNSResponse QueryCache(string name, QType qtype)
        {
            Question question = new Question(name, qtype, QClass.IN);
            return SearchInCache(question);
        }

        /// <summary>
        /// Do an QClass=IN Query on the previously cached results.
        /// </summary>
        /// <param name="name">Name to query</param>
        /// <param name="qtype">Question type</param>
        /// <returns>Response of the query</returns>
        public DNSResponse QueryCache(string name, QType qtype, QClass qclass)
        {
            Question question = new Question(name, qtype, qclass);
            return SearchInCache(question);
        }

        /// <summary>
        /// Do Query on specified DNS servers
        /// </summary>
        /// <param name="name">Name to query</param>
        /// <param name="qtype">Question type</param>
        /// <param name="timeout">Timeout for lookup in seconds.</param>
        /// <param name="qclass">Class type</param>
        /// <returns>Response of the query</returns>
        public DNSResponse Query(string name, QType qtype, QClass qclass, int timeout)
        {
            return this.Query(name, qtype, qclass, timeout, this.m_DnsServers);
        }

        /// <summary>
        /// Do an QClass=IN Query on specified DNS servers
        /// </summary>
        /// <param name="name">Name to query</param>
        /// <param name="qtype">Question type</param>
        /// <param name="timeout">Timeout for lookup in seconds.</param>
        /// <returns>Response of the query</returns>
        public DNSResponse Query(string name, QType qtype, int timeout)
        {
            return this.Query(name, qtype, QClass.IN, timeout, this.m_DnsServers);
        }

        public DNSResponse Query(string name, QType qtype, int timeout, List<IPEndPoint> dnsServers)
        {
            return this.Query(name, qtype, QClass.IN, timeout, dnsServers);
        }

        /// <summary>
        /// Do an QClass=IN Query on specified DNS servers
        /// </summary>
        /// <param name="name">Name to query</param>
        /// <param name="qtype">Question type</param>
        /// <param name="qclass">Class type</param>
        /// <param name="timeout">Timeout for lookup in seconds.</param>
        /// <returns>Response of the query</returns>
        public DNSResponse Query(string name, QType qtype, QClass qclass, int timeout, List<IPEndPoint> dnsServers)
        {
            Question question = new Question(name, qtype, qclass);
            DNSResponse response = SearchInCache(question);
            if (response != null)
            {
                return response;
            }

            DNSRequest request = new DNSRequest();
            request.AddQuestion(question);
            return GetResponse(request, dnsServers, timeout);
        }

        /// <summary>
        /// Do Query on specified DNS servers
        /// </summary>
        /// <param name="name">Name to query</param>
        /// <param name="qtype">Question type</param>
        /// <param name="qclass">Class type</param>
        /// <returns>Response of the query</returns>
        public DNSResponse Query(string name, QType qtype, QClass qclass)
        {
            return this.Query(name, qtype, qclass, this.m_DnsServers);
        }

        public DNS.DNSResponse Query(string name, QType qtype, QClass qclass, List<IPEndPoint> dnsServers)
        {
            return this.Query(name, qtype, qclass, DEFAULT_TIMEOUT, dnsServers);
        }

        /// <summary>
        /// Do an QClass=IN Query on specified DNS servers
        /// </summary>
        /// <param name="name">Name to query</param>
        /// <param name="qtype">Question type</param>
        /// <returns>Response of the query</returns>
        public DNSResponse Query(string name, QType qtype)
        {
            return this.Query(name, qtype, QClass.IN, this.m_DnsServers);
        }

        private DNSResponse GetResponse(DNSRequest request, List<IPEndPoint> dnsServers, int timeout)
        {
            request.header.ID = m_Unique;
            request.header.RD = m_Recursion;
            DNSResponse response = null;

            if (m_TransportType == TransportType.Udp)
            {
                response = UdpRequest(request, dnsServers, timeout);
            }

            if (m_TransportType == TransportType.Tcp)
            {
                response = TcpRequest(request, dnsServers, timeout);
            }

            if (response == null)
            {
                response = new DNSResponse();
                response.Error = "Unknown TransportType";
            }

            Question question = request.Questions[0];
            string questionKey = question.QClass + "-" + question.QType + "-" + question.QName;
            AddToCache(response, questionKey);

            return response;
        }

        /// <summary>
        /// Gets a list of default DNS servers used on the Windows machine.
        /// </summary>
        /// <returns></returns>
        public static IPEndPoint[] GetDnsServers()
        {
            List<IPEndPoint> list = new List<IPEndPoint>();

            NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface n in adapters)
            {
                if (n.OperationalStatus == OperationalStatus.Up)
                {
                    IPInterfaceProperties ipProps = n.GetIPProperties();
                    // thanks to Jon Webster on May 20, 2008
                    foreach (IPAddress ipAddr in ipProps.DnsAddresses)
                    {
                        // Exclude IPv6 site local addresses. Windows 10 adds default IPv6 DNS server addresses in the form of
                        // fec0:0:0:ffff::1%1, fec0:0:0:ffff::2%1, fec0:0:0:ffff::3%1 to virtual adapters. TODO track down
                        // what the purpose of setting such addresses is/was?
                        if (!ipAddr.IsIPv6SiteLocal)
                        {
                            IPEndPoint entry = new IPEndPoint(ipAddr, DefaultPort);
                            if (!list.Contains(entry))
                            {
                                list.Add(entry);
                            }
                        }
                    }
                }
            }
            return list.ToArray();
        }

        private IPHostEntry MakeEntry(string HostName, int timeout = DEFAULT_TIMEOUT)
        {
            IPHostEntry entry = new IPHostEntry();

            entry.HostName = HostName;

            DNSResponse response = Query(HostName, QType.A, QClass.IN, timeout);

            // fill AddressList and aliases
            List<IPAddress> AddressList = new List<IPAddress>();
            List<string> Aliases = new List<string>();
            foreach (AnswerRR answerRR in response.Answers)
            {
                if (answerRR.Type == DnsType.A)
                {
                    // answerRR.RECORD.ToString() == (answerRR.RECORD as RecordA).Address
                    AddressList.Add(IPAddress.Parse((answerRR.RECORD.ToString())));
                    entry.HostName = answerRR.NAME;
                }
                else
                {
                    if (answerRR.Type == DnsType.CNAME)
                    {
                        Aliases.Add(answerRR.NAME);
                    }
                }
            }
            entry.AddressList = AddressList.ToArray();
            entry.Aliases = Aliases.ToArray();

            return entry;
        }

        /// <summary>
        /// Translates the IPV4 or IPV6 address into an arpa address
        /// </summary>
        /// <param name="ip">IP address to get the arpa address form</param>
        /// <returns>The 'mirrored' IPV4 or IPV6 arpa address</returns>
        public static string GetArpaFromIp(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("in-addr.arpa.");
                foreach (byte b in ip.GetAddressBytes())
                {
                    sb.Insert(0, string.Format("{0}.", b));
                }
                return sb.ToString();
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("ip6.arpa.");
                foreach (byte b in ip.GetAddressBytes())
                {
                    sb.Insert(0, string.Format("{0:x}.", (b >> 4) & 0xf));
                    sb.Insert(0, string.Format("{0:x}.", (b >> 0) & 0xf));
                }
                return sb.ToString();
            }
            return "?";
        }

        /// <summary>
        ///		Resolves an IP address to an System.Net.IPHostEntry instance.
        /// </summary>
        /// <param name="ip">An IP address.</param>
        /// <returns>
        ///		An System.Net.IPHostEntry instance that contains address information about
        ///		the host specified in address.
        ///</returns>
        public IPHostEntry GetHostEntry(IPAddress ip)
        {
            DNSResponse response = Query(GetArpaFromIp(ip), QType.PTR, QClass.IN, DEFAULT_TIMEOUT);
            if (response.RecordsPTR.Length > 0)
            {
                return MakeEntry(response.RecordsPTR[0].PTRDNAME, DEFAULT_TIMEOUT);
            }
            else
            {
                return new IPHostEntry();
            }
        }

        /// <summary>
        ///		Resolves a host name or IP address to an System.Net.IPHostEntry instance.
        /// </summary>
        /// <param name="hostNameOrAddress">The host name or IP address to resolve.</param>
        /// <returns>
        ///		An System.Net.IPHostEntry instance that contains address information about
        ///		the host specified in hostNameOrAddress. 
        ///</returns>
        public IPHostEntry GetHostEntry(string hostNameOrAddress)
        {
            IPAddress iPAddress;
            if (IPAddress.TryParse(hostNameOrAddress, out iPAddress))
            {
                return GetHostEntry(iPAddress);
            }
            else
            {
                return MakeEntry(hostNameOrAddress, DEFAULT_TIMEOUT);
            }
        }

        private delegate IPHostEntry GetHostEntryViaIPDelegate(IPAddress ip);
        private delegate IPHostEntry GetHostEntryDelegate(string hostNameOrAddress);

        /// <summary>
        /// Asynchronously resolves a host name or IP address to an System.Net.IPHostEntry instance.
        /// </summary>
        /// <param name="hostNameOrAddress">The host name or IP address to resolve.</param>
        /// <param name="requestCallback">
        ///		An System.AsyncCallback delegate that references the method to invoke when
        ///		the operation is complete.
        ///</param>
        /// <param name="stateObject">
        ///		A user-defined object that contains information about the operation. This
        ///		object is passed to the requestCallback delegate when the operation is complete.
        /// </param>
        /// <returns>An System.IAsyncResult instance that references the asynchronous request.</returns>
        public IAsyncResult BeginGetHostEntry(string hostNameOrAddress, AsyncCallback requestCallback, object stateObject)
        {
            GetHostEntryDelegate g = new GetHostEntryDelegate(GetHostEntry);
            return g.BeginInvoke(hostNameOrAddress, requestCallback, stateObject);
        }

        /// <summary>
        /// Asynchronously resolves an IP address to an System.Net.IPHostEntry instance.
        /// </summary>
        /// <param name="ip">The IP address to resolve.</param>
        /// <param name="requestCallback">
        ///		An System.AsyncCallback delegate that references the method to invoke when
        ///		the operation is complete.
        /// </param>
        /// <param name="stateObject">
        ///		A user-defined object that contains information about the operation. This
        ///     object is passed to the requestCallback delegate when the operation is complete.
        /// </param>
        /// <returns>An System.IAsyncResult instance that references the asynchronous request.</returns>
        public IAsyncResult BeginGetHostEntry(IPAddress ip, AsyncCallback requestCallback, object stateObject)
        {
            GetHostEntryViaIPDelegate g = new GetHostEntryViaIPDelegate(GetHostEntry);
            return g.BeginInvoke(ip, requestCallback, stateObject);
        }

        /// <summary>
        /// Ends an asynchronous request for DNS information.
        /// </summary>
        /// <param name="AsyncResult">
        ///		An System.IAsyncResult instance returned by a call to an 
        ///		Overload:Heijden.Dns.Resolver.BeginGetHostEntry method.
        /// </param>
        /// <returns>
        ///		An System.Net.IPHostEntry instance that contains address information about
        ///		the host. 
        ///</returns>
        //public IPHostEntry EndGetHostEntry(IAsyncResult AsyncResult)
        //{
        //    AsyncResult aResult = (AsyncResult)AsyncResult;
        //    if (aResult.AsyncDelegate is GetHostEntryDelegate)
        //    {
        //        GetHostEntryDelegate g = (GetHostEntryDelegate)aResult.AsyncDelegate;
        //        return g?.EndInvoke(AsyncResult);
        //    }
        //    if (aResult.AsyncDelegate is GetHostEntryViaIPDelegate)
        //    {
        //        GetHostEntryViaIPDelegate g = (GetHostEntryViaIPDelegate)aResult.AsyncDelegate;
        //        return g?.EndInvoke(AsyncResult);
        //    }
        //    return null;
        //}

        private IPEndPoint GetActiveDNSServer()
        {
            if (m_DnsServers == null)
            {
                m_DnsServers = DefaultDnsServers;
            }

            if (m_DnsServers.Count == 1)
            {
                return m_DnsServers[0];
            }
            else
            {
                if (m_activeDNSServer == null)
                {
                    m_activeDNSServer = m_DnsServers[0];
                }
                else
                {
                    // Check that the active DNS server is getting responses.
                    if (m_receiveTimeouts.ContainsKey(m_activeDNSServer) && m_receiveTimeouts[m_activeDNSServer] >= SWITCH_ACTIVE_TIMEOUT_COUNT)
                    {
                        // Switch active DNS server to alternative end point as too many consectutive receive failures on this one.
                        foreach (IPEndPoint dnsServer in m_DnsServers)
                        {
                            if (dnsServer != m_activeDNSServer)
                            {
                                logger.LogDebug("Switching active DNS server from " + m_activeDNSServer + " to " + dnsServer + ".");
                                m_activeDNSServer = dnsServer;
                                ResetTimeoutCount(m_activeDNSServer);
                            }
                        }
                    }
                }

                return m_activeDNSServer;
            }
        }

        private void IncrementTimeoutCount(IPEndPoint dnsServer)
        {
            if (m_DnsServers != null && m_DnsServers.Count > 1)
            {
                if (!m_receiveTimeouts.ContainsKey(dnsServer))
                {
                    m_receiveTimeouts.Add(dnsServer, 1);
                }
                else
                {
                    m_receiveTimeouts[dnsServer] = m_receiveTimeouts[dnsServer] + 1;
                }
            }
        }

        private void ResetTimeoutCount(IPEndPoint dnsServer)
        {
            if (m_DnsServers != null && m_DnsServers.Count > 1)
            {
                if (!m_receiveTimeouts.ContainsKey(dnsServer))
                {
                    m_receiveTimeouts.Add(dnsServer, 0);
                }
                else
                {
                    m_receiveTimeouts[dnsServer] = 0;
                }
            }
        }

        private int GetTimeoutCount(IPEndPoint dnsServer)
        {
            if (m_DnsServers != null && m_DnsServers.Count > 1)
            {
                if (m_receiveTimeouts.ContainsKey(dnsServer))
                {
                    return m_receiveTimeouts[dnsServer];
                }
            }
            return 0;
        }
        private void ResetAllTimeoutCountIfNecessary()
        {
            if (m_DnsServers != null && m_DnsServers.Count > 1)
            {
                foreach (KeyValuePair<IPEndPoint, int> count in m_receiveTimeouts)
                {
                    //leave method if not every counter is set to maximum
                    if (count.Value < SWITCH_ACTIVE_TIMEOUT_COUNT)
                    {
                        return;
                    }
                }
                foreach (IPEndPoint dnsServer in m_DnsServers)
                {
                    if (!m_receiveTimeouts.ContainsKey(dnsServer))
                    {
                        m_receiveTimeouts.Add(dnsServer, 0);
                    }
                    else
                    {
                        m_receiveTimeouts[dnsServer] = 0;
                    }
                }
            }
        }
    }
}