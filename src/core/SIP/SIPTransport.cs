//-----------------------------------------------------------------------------
// Filename: SIPTransport.cs
//
// Description: SIP transport layer implementation. Handles different network
// transport options, retransmits, timeouts and transaction matching.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 14 Feb 2006	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
// 26 Apr 2008  Aaron Clauson   Added TCP support.
// 16 Oct 2019  Aaron Clauson   Added IPv6 support.
// 25 Oct 2019  Aaron Clauson   Added async options for sending requests and responses.
// 30 Oct 2019  Aaron Clauson   Added support for reliable provisional responses as per RFC3262.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPTransport
    {
        private const int NO_ERROR = 0;

        private const string RECEIVE_THREAD_NAME = "siptransport-receive";
        private const string RELIABLES_THREAD_NAME = "siptransport-reliables";
        private const int MAX_QUEUEWAIT_PERIOD = 2000;              // Maximum time to wait to check the message received queue if no events are received.
        private const int PENDINGREQUESTS_CHECK_PERIOD = 500;       // Time between checking the pending requests queue to resend reliable requests that have not been responded to.
        private const int MAX_INMESSAGE_QUEUECOUNT = 5000;          // The maximum number of messages that can be stored in the incoming message queue.
        private const int MAX_RELIABLETRANSMISSIONS_COUNT = 5000;   // The maximum number of messages that can be maintained for reliable transmissions.

        public const string ALLOWED_SIP_METHODS = "ACK, BYE, CANCEL, INFO, INVITE, NOTIFY, OPTIONS, REFER, REGISTER, SUBSCRIBE";

        private static readonly int m_t1 = SIPTimings.T1;
        private static readonly int m_t2 = SIPTimings.T2;
        private static readonly int m_t6 = SIPTimings.T6;
        private static string m_looseRouteParameter = SIPConstants.SIP_LOOSEROUTER_PARAMETER;
        public static IPAddress BlackholeAddress = IPAddress.Any;  // (IPAddress.Any is 0.0.0.0) Any SIP messages with this IP address will be dropped.

        private static ILogger logger = Log.Logger;

        // Dictates whether the transport later will queue incoming requests for processing on a separate thread of process immediately on the same thread.
        // Most SIP elements with the exception of Stateless Proxies will typically want to queue incoming SIP messages.
        private bool m_queueIncoming = true;

        private bool m_transportThreadStarted = false;
        private Queue<IncomingMessage> m_inMessageQueue = new Queue<IncomingMessage>();
        private ManualResetEvent m_inMessageArrived = new ManualResetEvent(false);
        private bool m_closed = false;

        private Dictionary<string, SIPChannel> m_sipChannels = new Dictionary<string, SIPChannel>();    // List of the physical channels that have been opened and are under management by this instance.

        private SIPTransactionEngine m_transactionEngine;

        public event SIPTransportRequestDelegate SIPTransportRequestReceived;
        public event SIPTransportResponseDelegate SIPTransportResponseReceived;
        public event STUNRequestReceivedDelegate STUNRequestReceived;
        private ResolveSIPEndPointDelegate ResolveSIPEndPoint_External;

        public event SIPTransportRequestDelegate SIPRequestInTraceEvent;
        public event SIPTransportRequestDelegate SIPRequestOutTraceEvent;
        public event SIPTransportResponseDelegate SIPResponseInTraceEvent;
        public event SIPTransportResponseDelegate SIPResponseOutTraceEvent;
        public event SIPTransportSIPBadMessageDelegate SIPBadRequestInTraceEvent;
        public event SIPTransportSIPBadMessageDelegate SIPBadResponseInTraceEvent;
        public event SIPTransactionRequestRetransmitDelegate SIPRequestRetransmitTraceEvent;
        public event SIPTransactionResponseRetransmitDelegate SIPResponseRetransmitTraceEvent;

        // Allows an application to set the prefix for the performance monitor counter it wants to use for tracking the SIP transport metrics.
        public string PerformanceMonitorPrefix;

        // If set this host name (or IP address) will be passed to the UAS Invite transaction so it
        // can be used as the Contact address in Ok responses.
        public string ContactHost;

        // Contains a list of the SIP Requests/Response that are being monitored or responses and retransmitted on when none is recieved to attempt a more reliable delivery
        // rather then just relying on the initial request to get through.
        private Dictionary<string, SIPTransaction> m_reliableTransmissions = new Dictionary<string, SIPTransaction>();
        private bool m_reliablesThreadRunning = false;   // Only gets started when a request is made to send a reliable request.

        /// <summary>
        /// Creates a SIP transport class with default DNS resolver and SIP transaction engine.
        /// </summary>
        public SIPTransport()
        {
            ResolveSIPEndPoint_External = SIPDNSManager.ResolveSIPService;
            m_transactionEngine = new SIPTransactionEngine();
        }

        public SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine)
        {
            if (sipResolver == null)
            {
                throw new ArgumentNullException("The SIP end point resolver must be set when creating a SIPTransport object.");
            }

            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
        }

        public SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine, bool queueIncoming)
        {
            if (sipResolver == null)
            {
                throw new ArgumentNullException("The SIP end point resolver must be set when creating a SIPTransport object.");
            }

            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
            m_queueIncoming = queueIncoming;
        }

        public SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine, SIPChannel sipChannel, bool queueIncoming)
        {
            if (sipResolver == null)
            {
                throw new ArgumentNullException("The SIP end point resolver must be set when creating a SIPTransport object.");
            }

            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
            AddSIPChannel(sipChannel);

            m_queueIncoming = queueIncoming;
        }

        /// <summary>
        /// Adds additional SIP Channels to the transport layer.
        /// </summary>
        public void AddSIPChannel(List<SIPChannel> sipChannels)
        {
            foreach (SIPChannel sipChannel in sipChannels)
            {
                AddSIPChannel(sipChannel);
            }
        }

        /// <summary>
        /// Adds an additional SIP Channel to the transport layer.
        /// </summary>
        /// <param name="localEndPoint"></param>
        public void AddSIPChannel(SIPChannel sipChannel)
        {
            try
            {
                m_sipChannels.Add(sipChannel.SIPChannelEndPoint.ToString(), sipChannel);

                // Wire up the SIP transport to the SIP channel.
                sipChannel.SIPMessageReceived += ReceiveMessage;

                if (m_queueIncoming && !m_transportThreadStarted)
                {
                    StartTransportThread();
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception AddSIPChannel. " + excp.Message);
                throw excp;
            }
        }

        public void RemoveSIPChannel(SIPChannel sipChannel)
        {
            if (m_sipChannels.ContainsKey(sipChannel.SIPChannelEndPoint.ToString()))
            {
                m_sipChannels.Remove(sipChannel.SIPChannelEndPoint.ToString());
                sipChannel.SIPMessageReceived -= ReceiveMessage;
            }
        }

        private void StartTransportThread()
        {
            if (!m_transportThreadStarted)
            {
                m_transportThreadStarted = true;

                Thread inMessageThread = new Thread(new ThreadStart(ProcessInMessage));
                inMessageThread.Name = RECEIVE_THREAD_NAME;
                inMessageThread.Start();
            }
        }

        private void StartReliableTransmissionsThread()
        {
            m_reliablesThreadRunning = true;

            Thread reliableTransmissionsThread = new Thread(new ThreadStart(ProcessPendingReliableTransactions));
            reliableTransmissionsThread.Name = RELIABLES_THREAD_NAME;
            reliableTransmissionsThread.Start();
        }

        public void ReceiveMessage(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            try
            {
                if (!m_queueIncoming)
                {
                    SIPMessageReceived(sipChannel, remoteEndPoint, buffer);
                }
                else
                {
                    IncomingMessage incomingMessage = new IncomingMessage(sipChannel, remoteEndPoint, buffer);

                    // Keep the queue within size limits 
                    if (m_inMessageQueue.Count >= MAX_INMESSAGE_QUEUECOUNT)
                    {
                        logger.LogWarning("SIPTransport queue full new message from " + remoteEndPoint + " being discarded.");
                    }
                    else
                    {
                        lock (m_inMessageQueue)
                        {
                            m_inMessageQueue.Enqueue(incomingMessage);
                        }
                    }

                    m_inMessageArrived.Set();
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport ReceiveMessage. " + excp.Message);
                throw excp;
            }
        }

        public void Shutdown()
        {
            try
            {
                m_closed = true;

                foreach (SIPChannel channel in m_sipChannels.Values)
                {
                    channel.Close();
                }

                m_inMessageArrived.Set();

                //logger.LogDebug("SIPTransport Shutdown Complete.");
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport Shutdown. " + excp.Message);
            }
        }

        public SIPEndPoint GetDefaultTransportContact(SIPProtocolsEnum protocol)
        {
            SIPChannel defaultChannel = GetDefaultChannel(protocol);

            if (defaultChannel != null)
            {
                return defaultChannel.SIPChannelEndPoint;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Attempts to find the a matching SIP channel for the specified protocol. Preference is given to non-loopback
        /// channels.
        /// </summary>
        /// <returns>A SIP channel.</returns>
        public SIPEndPoint GetDefaultSIPEndPoint(SIPProtocolsEnum protocol)
        {
            if (m_sipChannels == null || m_sipChannels.Count == 0)
            {
                throw new ApplicationException("No SIP channels available.");
            }

            var matchingChannels = m_sipChannels.Values.Where(x => x.SIPProtocol == protocol);

            if(matchingChannels.Count() == 0)
            {
                throw new ApplicationException($"The SIP transport layer does not have any SIP channels available for protocol {protocol}.");
            }
            else if (matchingChannels.Count() == 1)
            {
                return matchingChannels.First().SIPChannelEndPoint;
            }
            else
            {
                return matchingChannels.OrderBy(x => x.IsLoopbackAddress).First().SIPChannelEndPoint;
            }
        }

        /// <summary>
        /// Attempts to locate the SIP channel that can communicate with the destination end point.
        /// </summary>
        /// <param name="destinationEP">The remote SIP end point to find a SIP channel for.</param>
        /// <returns>If successful the SIP end point of a SIP channel that can be used to communicate 
        /// with the destination end point.</returns>
        public SIPEndPoint GetDefaultSIPEndPoint(SIPEndPoint destinationEP)
        {
            if (m_sipChannels == null || m_sipChannels.Count == 0)
            {
                return null;
            }
            else if (m_sipChannels.Count == 1)
            {
                return m_sipChannels.First().Value.SIPChannelEndPoint;
            }
            else if (IPAddress.IsLoopback(destinationEP.Address))
            {
                // If destination is a loopback IP address look for a protocol and IP protocol match.
                return m_sipChannels.Where(x => x.Value.SIPProtocol == destinationEP.Protocol &&
                    (x.Value.IsLoopbackAddress
                        || IPAddress.Equals(IPAddress.Any, x.Value.SIPChannelEndPoint.Address)
                        || IPAddress.Equals(IPAddress.IPv6Any, x.Value.SIPChannelEndPoint.Address)) &&
                    x.Value.AddressFamily == destinationEP.Address.AddressFamily)
                    .Select(x => x.Value.SIPChannelEndPoint).FirstOrDefault();
            }
            else if (m_sipChannels.Count(x => x.Value.SIPProtocol == destinationEP.Protocol &&
                x.Value.AddressFamily == destinationEP.Address.AddressFamily &&
                !x.Value.IsLoopbackAddress) == 1)
            {
                // If there is only one channel matching the required SIP protocol and IP protocol pair return it.
                return m_sipChannels.Where(x => x.Value.SIPProtocol == destinationEP.Protocol &&
                    x.Value.AddressFamily == destinationEP.Address.AddressFamily &&
                    !x.Value.IsLoopbackAddress).Select(x => x.Value.SIPChannelEndPoint).Single();
            }
            else
            {
                var localAddress = NetServices.GetLocalAddress(destinationEP.Address);

                foreach (SIPChannel sipChannel in m_sipChannels.Values)
                {
                    if (sipChannel.SIPChannelEndPoint.Protocol == destinationEP.Protocol &&
                        (localAddress == null || sipChannel.SIPChannelEndPoint.Address.Equals(localAddress)))
                    {
                        return sipChannel.SIPChannelEndPoint;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// This function performs processing on a request to handle any actions that need to be taken based on the Route header.
        /// </summary>
        /// <remarks>
        /// The main sections in the RFC3261 dealing with Route header processing are sections 12.2.1.1 for request processing and
        /// 16.4 for proxy processing.
        /// The steps to process requests for Route headers are:
        ///  1. If route set is empty no further action is required, forward to destination resolved from request URI,
        ///  2. If the request URI is identified as a value that was previously set as a Route by this SIP agent it means the
        ///     previous hop was a strict router. Replace the reqest URI with the last Route header and go to next step,
        ///  3. If the top most route header was set by this SIP agent then remove it and go to next step,
        ///  4. If the top most route set does contain the lr parameter then forward to the destination resolved by it,
        ///  5. If the top most route header does NOT contain the lr parameter is must be popped and inserted as the request URI
        ///     and the original request URI must be added to the end of the route set, forward to destination resolved from request URI,
        /// </remarks>
        public void PreProcessRouteInfo(SIPRequest sipRequest)
        {
            // If there are no routes defined then there is nothing to do.
            if (sipRequest.Header.Routes != null && sipRequest.Header.Routes.Length > 0)
            {
                // If this stack's route URI is being used as the request URI then it will have the loose route parameter (see remarks step 2).
                if (sipRequest.URI.Parameters.Has(m_looseRouteParameter))
                {
                    foreach (SIPChannel sipChannel in m_sipChannels.Values)
                    {
                        if (sipRequest.URI.ToSIPEndPoint() == sipChannel.SIPChannelEndPoint)
                        {
                            // The request URI was this router's address so it was set by a strict router.
                            // Replace the URI with the original SIP URI that is stored at the end of the route header.
                            sipRequest.URI = sipRequest.Header.Routes.BottomRoute.URI;
                            sipRequest.Header.Routes.RemoveBottomRoute();
                        }
                    }
                }

                // The possibility of a strict router on the previous hop has now been handled. 
                if (sipRequest.Header.Routes != null && sipRequest.Header.Routes.Length > 0)
                {
                    // Check whether the top route header belongs to this proxy (see remarks step 3).
                    if (!sipRequest.Header.Routes.TopRoute.IsStrictRouter)
                    {
                        foreach (SIPChannel sipChannel in m_sipChannels.Values)
                        {
                            if (sipRequest.Header.Routes.TopRoute.ToSIPEndPoint() == sipChannel.SIPChannelEndPoint)
                            {
                                // Remove the top route as it belongs to this proxy.
                                sipRequest.ReceivedRoute = sipRequest.Header.Routes.PopRoute();
                                break;
                            }
                        }
                    }

                    // Check whether the top route header is a strict router and if so adjust the request accordingly (see remarks step 5).
                    if (sipRequest.Header.Routes != null && sipRequest.Header.Routes.Length > 0)
                    {
                        if (sipRequest.Header.Routes.TopRoute.IsStrictRouter)
                        {
                            // Put the strict router's uri into the request URI and place the original request URI at the end of the route set.
                            SIPRoute strictRoute = sipRequest.Header.Routes.PopRoute();
                            SIPRoute uriRoute = new SIPRoute(sipRequest.URI);
                            sipRequest.Header.Routes.AddBottomRoute(uriRoute);
                            sipRequest.URI = strictRoute.URI;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Allows raw bytes to be sent from one of the SIPTransport sockets. This should not be used for SIP payloads and instead is
        /// provided to allow other types of payloads to be multi-plexed on the SIP socket. Examples are sending NAT keep alives and
        /// STUN responses where it's useful to use the same socket as the SIP packets.
        /// </summary>
        /// <param name="localSIPEndPoint">The local SIP end point to do the send from. Must match the local end point of one of
        /// the SIP transports channels.</param>
        /// <param name="dstEndPoint">The destination end point to send the buffer to.</param>
        /// <param name="buffer">The data buffer to send.</param>
        public void SendRaw(SIPEndPoint localSIPEndPoint, SIPEndPoint dstEndPoint, byte[] buffer)
        {
            if (localSIPEndPoint == null)
            {
                throw new ArgumentNullException("localSIPEndPoint", "The local SIP end point must be set for SendRaw.");
            }
            else if (dstEndPoint == null)
            {
                throw new ArgumentNullException("dstEndPoint", "The destination end point must be set for SendRaw.");
            }
            if (dstEndPoint.Address.Equals(BlackholeAddress))
            {
                // Ignore packet, it's destined for the blackhole.
                return;
            }
            else if (m_sipChannels.Count == 0)
            {
                throw new ApplicationException("No channels are configured in the SIP transport layer. The data could not be sent.");
            }

            SIPChannel sendSIPChannel = FindSIPChannel(localSIPEndPoint);
            if (sendSIPChannel != null)
            {
                sendSIPChannel.Send(dstEndPoint.GetIPEndPoint(), buffer, null);
            }
            else
            {
                logger.LogWarning($"No SIPChannel could be found for {localSIPEndPoint} when attempting a SendRaw to {dstEndPoint}.");
            }
        }

        /// <summary>
        /// Attempts to send a SIP request to a destination end point. This method will attempt to:
        /// - determine the IP address and port to send the request to by using SIP routing and DNS rules.
        /// - find the most appropriate local SIP channel in this SIP transport to send the request on.
        /// </summary>
        /// <param name="sipRequest">The SIP request to send.</param>
        public async void SendRequest(SIPRequest sipRequest)
        {
            await SendRequestAsync(sipRequest);
        }

        /// <summary>
        /// Sends a SIP request asynchronously. This method will attempt to find the most appropriate
        /// local SIP channel to send the request on.
        /// </summary>
        /// <param name="sipRequest">The SIP request to send.</param>
        public async Task<SocketError> SendRequestAsync(SIPRequest sipRequest)
        {
            if (sipRequest == null)
            {
                throw new ArgumentNullException("sipRequest", "The SIP request must be set for SendRequest.");
            }
            else if (m_sipChannels.Count == 0)
            {
                throw new ApplicationException("No channels are configured in the SIP transport layer. No attempt made to send the request.");
            }

            SIPDNSLookupResult dnsResult = GetRequestEndPoint(sipRequest, null, true);

            if (dnsResult.LookupError != null)
            {
                //SIPResponse unresolvableResponse = GetResponse(sipRequest, SIPResponseStatusCodesEnum.AddressIncomplete, "DNS resolution for " + dnsResult.URI.Host + " failed " + dnsResult.LookupError);
                //SendResponse(unresolvableResponse);
                return SocketError.HostNotFound;
            }
            else if (dnsResult.Pending)
            {
                // The DNS lookup is still in progress, ignore this request and rely on the fact that the transaction retransmit mechanism will send another request.
                return SocketError.InProgress;
            }
            else
            {
                SIPEndPoint requestEndPoint = dnsResult.GetSIPEndPoint();

                if (requestEndPoint != null && requestEndPoint.Address.Equals(BlackholeAddress))
                {
                    // Ignore packet, it's destined for the blackhole.
                    return SocketError.Success;
                }
                else if (requestEndPoint != null)
                {
                    return await SendRequestAsync(requestEndPoint, sipRequest);
                }
                else
                {
                    throw new ApplicationException("SIP Transport could not send request as end point could not be determined.\r\n" + sipRequest.ToString());
                }
            }
        }

        /// <summary>
        /// Attempts to send a SIP request to a destination end point. This method will attempt to find the most appropriate
        /// local SIP channel in this SIP transport to send the request on.
        /// </summary>
        /// <param name="dstEndPoint">The destination end point to send the request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        public async void SendRequest(SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
            await SendRequestAsync(dstEndPoint, sipRequest);
        }

        /// <summary>
        /// Sends a SIP request asynchronously. This method will attempt to find the most appropriate
        /// local SIP channel in this SIP transport to send the request on.
        /// </summary>
        /// <param name="dstEndPoint">The destination end point to send the request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        public async Task<SocketError> SendRequestAsync(SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
            if (dstEndPoint == null)
            {
                throw new ArgumentNullException("dstEndPoint", "The destination end point must be set for SendRequest.");
            }
            else if (sipRequest == null)
            {
                throw new ArgumentNullException("sipRequest", "The SIP request must be set for SendRequest.");
            }
            else if (dstEndPoint.Address.Equals(BlackholeAddress))
            {
                // Ignore packet, it's destined for the blackhole.
                return SocketError.Success;
            }
            else if (m_sipChannels.Count == 0)
            {
                throw new ApplicationException("No channels are configured in the SIP transport layer. No attempt made to send the request.");
            }

            SIPChannel sipChannel = null;

            if (sipRequest.LocalSIPEndPoint != null)
            {
                sipChannel = FindSIPChannel(sipRequest.LocalSIPEndPoint);
                sipChannel = sipChannel ?? GetDefaultChannel(sipRequest.LocalSIPEndPoint);
            }
            else
            {
                sipChannel = GetDefaultChannel(dstEndPoint);
            }

            if (sipChannel != null)
            {
                return await SendRequestAsync(sipChannel, dstEndPoint, sipRequest);
            }
            else
            {
                throw new ApplicationException("A default SIP channel could not be found for protocol " + sipRequest.LocalSIPEndPoint.Protocol + " when sending SIP request.");
            }
        }

        /// <summary>
        /// Attempts to send a SIP request to the destination end point using the specified SIP channel.
        /// </summary>
        /// <param name="sipChannel">The SIP channel to use to send the SIP request.</param>
        /// <param name="dstEndPoint">The destination to send the SIP request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        private async Task<SocketError> SendRequestAsync(SIPChannel sipChannel, SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
            //try
            //{
            if (sipChannel == null)
            {
                throw new ArgumentNullException("sipChannel", "The SIP channel must be set for SendRequest.");
            }
            else if (dstEndPoint == null)
            {
                throw new ArgumentNullException("dstEndPoint", "The destination end point must be set for SendRequest.");
            }
            else if (sipRequest == null)
            {
                throw new ArgumentNullException("sipRequest", "The SIP request must be set for SendRequest.");
            }
            else if (dstEndPoint.Address.Equals(BlackholeAddress))
            {
                // Ignore packet, it's destined for the blackhole.
                return SocketError.Success;
            }

            FireSIPRequestOutTraceEvent(sipChannel.SIPChannelEndPoint, dstEndPoint, sipRequest);

            sipRequest.Header.ContentLength = (sipRequest.Body.NotNullOrBlank()) ? Encoding.UTF8.GetByteCount(sipRequest.Body) : 0;

            if (sipChannel.IsSecure)
            {
                return await sipChannel.SendAsync(dstEndPoint.GetIPEndPoint(), Encoding.UTF8.GetBytes(sipRequest.ToString()), sipRequest.URI.Host);
            }
            else
            {
                return await sipChannel.SendAsync(dstEndPoint.GetIPEndPoint(), Encoding.UTF8.GetBytes(sipRequest.ToString()));
            }

            //}
            //catch (ApplicationException appExcp)
            //{
            //    logger.LogWarning("ApplicationException SIPTransport SendRequest. " + appExcp.Message);

            //    SIPResponse errorResponse = GetResponse(sipRequest, SIPResponseStatusCodesEnum.InternalServerError, appExcp.Message);

            //    // Remove any Via headers, other than the last one, that are for sockets hosted by this process.
            //    while (errorResponse.Header.Vias.Length > 0)
            //    {
            //        if (IsLocalSIPEndPoint(SIPEndPoint.ParseSIPEndPoint(errorResponse.Header.Vias.TopViaHeader.ReceivedFromAddress)))
            //        {
            //            errorResponse.Header.Vias.PopTopViaHeader();
            //        }
            //        else
            //        {
            //            break;
            //        }
            //    }

            //    if (errorResponse.Header.Vias.Length == 0)
            //    {
            //        logger.LogWarning("Could not send error response for " + appExcp.Message + " as no non-local Via headers were available.");
            //    }
            //    else
            //    {
            //        SendResponse(errorResponse);
            //    }
            //}
        }

        /// <summary>
        /// Sends a SIP request/response and keeps track of whether a response/acknowledgement has been received. 
        /// If no response is received then periodic retransmits are made for up to T1 x 64 seconds.
        /// </summary>
        /// <param name="sipTransaction">The SIP transaction encapsulating the SIP request or response that needs to be sent reliably.</param>
        public void SendSIPReliable(SIPTransaction sipTransaction)
        {
            if (sipTransaction == null)
            {
                throw new ArgumentNullException("sipTransaction", "The SIP transaction parameter must be set for SendSIPReliable.");
            }
            else if (sipTransaction.RemoteEndPoint != null && sipTransaction.RemoteEndPoint.Address.Equals(BlackholeAddress))
            {
                sipTransaction.Retransmits = 1;
                sipTransaction.InitialTransmit = DateTime.Now;
                sipTransaction.LastTransmit = DateTime.Now;
                sipTransaction.DeliveryPending = false;
                return;
            }
            else if (m_sipChannels.Count == 0)
            {
                throw new ApplicationException("No channels are configured in the SIP transport layer. The request could not be sent.");
            }
            else if (m_reliableTransmissions.Count >= MAX_RELIABLETRANSMISSIONS_COUNT)
            {
                throw new ApplicationException("Cannot send reliable SIP message as the reliable transmissions queue is full.");
            }

            //logger.LogDebug("SendSIPReliable transaction URI " + sipTransaction.TransactionRequest.URI.ToString() + ".");

            if (sipTransaction.TransactionType == SIPTransactionTypesEnum.InviteServer)
            {
                // This is a user agent server INVITE transaction that wants to send a reliable provisional or final response.
                if (sipTransaction.LocalSIPEndPoint == null)
                {
                    throw new ApplicationException("The SIPTransport layer cannot send a reliable SIP response because the send from socket has not been set for the transaction.");
                }
                else if (sipTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding)
                {
                    SendResponse(sipTransaction.ReliableProvisionalResponse);
                }
                else if (sipTransaction.TransactionState == SIPTransactionStatesEnum.Completed)
                {
                    SendResponse(sipTransaction.TransactionFinalResponse);
                }
            }
            else
            {
                if (sipTransaction.OutboundProxy != null)
                {
                    SendRequest(sipTransaction.OutboundProxy, sipTransaction.TransactionRequest);
                }
                else if (sipTransaction.RemoteEndPoint != null)
                {
                    SendRequest(sipTransaction.RemoteEndPoint, sipTransaction.TransactionRequest);
                }
                else
                {
                    SendRequest(sipTransaction.TransactionRequest);
                }
            }

            sipTransaction.Retransmits = 1;
            sipTransaction.InitialTransmit = DateTime.Now;
            sipTransaction.LastTransmit = DateTime.Now;
            sipTransaction.DeliveryPending = true;

            if (!m_reliableTransmissions.ContainsKey(sipTransaction.TransactionId))
            {
                lock (m_reliableTransmissions)
                {
                    m_reliableTransmissions.Add(sipTransaction.TransactionId, sipTransaction);
                }
            }

            if (!m_reliablesThreadRunning)
            {
                StartReliableTransmissionsThread();
            }
        }

        /// <summary>
        /// Attempts to send a SIP response back to the SIP request origin.
        /// </summary>
        /// <param name="sipResponse">The SIP response to send.</param>
        public async void SendResponse(SIPResponse sipResponse)
        {
            await SendResponseAsync(sipResponse);
        }

        /// <summary>
        /// Attempts an asynchronous send of a SIP response back to the SIP request origin.
        /// </summary>
        /// <param name="sipResponse">The SIP response to send.</param>
        public async Task<SocketError> SendResponseAsync(SIPResponse sipResponse)
        {
            if (sipResponse == null)
            {
                throw new ArgumentNullException("sipResponse", "The SIP response must be set for SendResponseAsync.");
            }
            else if (m_sipChannels.Count == 0)
            {
                throw new ApplicationException("No channels are configured in the SIP transport layer. The response could not be sent.");
            }

            string connectionID = sipResponse.LocalSIPEndPoint?.ConnectionID ?? sipResponse.RemoteSIPEndPoint?.ConnectionID;

            if (!String.IsNullOrEmpty(connectionID))
            {
                // This reponse needs to be sent on a specific connection.
                var sipChannel = FindSIPChannel(connectionID);

                FireSIPResponseOutTraceEvent(sipChannel.SIPChannelEndPoint, sipResponse.RemoteSIPEndPoint, sipResponse);

                sipResponse.Header.ContentLength = (sipResponse.Body.NotNullOrBlank()) ? Encoding.UTF8.GetByteCount(sipResponse.Body) : 0;
                return await sipChannel.SendAsync(connectionID, Encoding.UTF8.GetBytes(sipResponse.ToString()));
            }
            else
            {
                // Need to determine the desintation from the SIP response headers. If possible the SIP response should be sent out from
                // the same local SIP end point as the SIP request it's for.
                SIPChannel sipChannel = FindSIPChannel(sipResponse.LocalSIPEndPoint);
                sipChannel = sipChannel ?? GetDefaultChannel(sipResponse.LocalSIPEndPoint.Protocol);

                if (sipChannel != null)
                {
                    return await SendResponseAsync(sipChannel, sipResponse);
                }
                else
                {
                    throw new ApplicationException($"Could not find a SIP channel to send SIP Response for transport protcol {sipResponse.LocalSIPEndPoint.Protocol}.");
                }
            }
        }

        /// <summary>
        /// Attempts to send a SIP response on a specific SIP channel.
        /// </summary>
        /// <param name="sipChannel">The SIP channel to send the SIP response on.</param>
        /// <param name="sipResponse">The SIP response to send.</param>
        private async void SendResponse(SIPChannel sipChannel, SIPResponse sipResponse)
        {
            await SendResponseAsync(sipChannel, sipResponse);
        }

        /// <summary>
        /// Attempts an asynchronous send of a SIP response on a specific SIP channel. This method attempts to resolve the
        /// desintation end point from the top SIP Via header. Normally the address in the header will be an IP address but
        /// the standard does permit a host which will require a DNS resolution.
        /// </summary>
        /// <param name="sipChannel">The SIP channel to send the SIP response on.</param>
        /// <param name="sipResponse">The SIP response to send.</param>
        private async Task<SocketError> SendResponseAsync(SIPChannel sipChannel, SIPResponse sipResponse)
        {
            if (sipResponse == null)
            {
                throw new ArgumentNullException("sipResponse", "The SIP response must be set for SendResponseAsync.");
            }
            else if (sipChannel == null)
            {
                throw new ArgumentNullException("sipChannel", "The SIP channel must be set for SendResponseAsync.");
            }

            SIPViaHeader topViaHeader = sipResponse.Header.Vias.TopViaHeader;
            if (topViaHeader == null)
            {
                logger.LogWarning($"There was no top Via header on a SIP response from {sipResponse.RemoteSIPEndPoint} in SendResponseAsync, response dropped.");
                return SocketError.Fault;
            }
            else
            {
                SIPDNSLookupResult lookupResult = GetHostEndPoint(topViaHeader.ReceivedFromAddress, false);

                if (lookupResult.LookupError != null)
                {
                    throw new ApplicationException("Could not resolve destination for response.\n" + sipResponse.ToString());
                }
                else if (lookupResult.Pending)
                {
                    // Ignore this response transmission and wait for the transaction retransmit mechanism to try again when DNS will have hopefully resolved the end point.
                    return SocketError.IOPending;
                }
                else
                {
                    SIPEndPoint dstEndPoint = lookupResult.GetSIPEndPoint();

                    if (dstEndPoint != null && dstEndPoint.Address.Equals(BlackholeAddress))
                    {
                        // Ignore packet, it's destined for the blackhole.
                        return SocketError.Success;
                    }
                    else if (dstEndPoint != null)
                    {
                        FireSIPResponseOutTraceEvent(sipChannel.SIPChannelEndPoint, dstEndPoint, sipResponse);

                        sipResponse.Header.ContentLength = (sipResponse.Body.NotNullOrBlank()) ? Encoding.UTF8.GetByteCount(sipResponse.Body) : 0;
                        return await sipChannel.SendAsync(dstEndPoint.GetIPEndPoint(), Encoding.UTF8.GetBytes(sipResponse.ToString()));
                    }
                    else
                    {
                        throw new ApplicationException("SendResponse could not send a response as no end point was resolved.\n" + sipResponse.ToString());
                    }
                }
            }
        }

        private void ProcessInMessage()
        {
            try
            {
                while (!m_closed)
                {
                    m_transactionEngine.RemoveExpiredTransactions();

                    while (m_inMessageQueue.Count > 0)
                    {
                        IncomingMessage incomingMessage = null;

                        lock (m_inMessageQueue)
                        {
                            incomingMessage = m_inMessageQueue.Dequeue();
                        }

                        if (incomingMessage != null)
                        {
                            SIPMessageReceived(incomingMessage.LocalSIPChannel, incomingMessage.RemoteEndPoint, incomingMessage.Buffer);
                        }
                    }

                    m_inMessageArrived.Reset();
                    m_inMessageArrived.WaitOne(MAX_QUEUEWAIT_PERIOD);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport ProcessInMessage. " + excp.Message);
            }
        }

        private void ProcessPendingReliableTransactions()
        {
            try
            {
                m_reliablesThreadRunning = true;

                while (!m_closed)
                {
                    if (m_reliableTransmissions.Count == 0)
                    {
                        // No request retransmissions in progress close down thread until next one required.
                        m_reliablesThreadRunning = false;
                        break;
                    }

                    try
                    {
                        List<string> completedTransactions = new List<string>();

                        lock (m_reliableTransmissions)
                        {
                            foreach (SIPTransaction transaction in m_reliableTransmissions.Values)
                            {
                                if (!transaction.DeliveryPending)
                                {
                                    completedTransactions.Add(transaction.TransactionId);
                                }
                                else if (transaction.TransactionState == SIPTransactionStatesEnum.Terminated ||
                                        transaction.TransactionState == SIPTransactionStatesEnum.Confirmed ||
                                        transaction.TransactionState == SIPTransactionStatesEnum.Cancelled ||
                                        transaction.HasTimedOut)
                                {
                                    transaction.DeliveryPending = false;
                                    completedTransactions.Add(transaction.TransactionId);
                                }
                                else
                                {
                                    if (DateTime.Now.Subtract(transaction.InitialTransmit).TotalMilliseconds >= m_t6)
                                    {
                                        //logger.LogDebug("Request timed out " + transaction.TransactionRequest.Method + " " + transaction.TransactionRequest.URI.ToString() + ".");

                                        if (transaction.TransactionType == SIPTransactionTypesEnum.InviteServer && transaction.TransactionState == SIPTransactionStatesEnum.Proceeding)
                                        {
                                            // If the transaction is a UAS and still in the progress state then the timeout was for a provisional response
                                            // and it should not set any transaction properties that will affect the delvery of any subsequent final response.
                                            transaction.OnTimedOutProvisionalResponse();
                                        }
                                        else
                                        {
                                            transaction.DeliveryPending = false;
                                            transaction.DeliveryFailed = true;
                                            transaction.TimedOutAt = DateTime.Now;
                                            transaction.HasTimedOut = true;
                                            transaction.FireTransactionTimedOut();
                                        }

                                        completedTransactions.Add(transaction.TransactionId);
                                    }
                                    else
                                    {
                                        double nextTransmitMilliseconds = Math.Pow(2, transaction.Retransmits - 1) * m_t1;
                                        nextTransmitMilliseconds = (nextTransmitMilliseconds > m_t2) ? m_t2 : nextTransmitMilliseconds;
                                        //logger.LogDebug("Time since retransmit " + transaction.Retransmits + " for " + transaction.TransactionRequest.Method + " " + transaction.TransactionRequest.URI.ToString() + " " + DateTime.Now.Subtract(transaction.LastTransmit).TotalMilliseconds + ".");

                                        if (DateTime.Now.Subtract(transaction.LastTransmit).TotalMilliseconds >= nextTransmitMilliseconds)
                                        {
                                            transaction.Retransmits = transaction.Retransmits + 1;
                                            transaction.LastTransmit = DateTime.Now;

                                            if (transaction.TransactionType == SIPTransactionTypesEnum.InviteServer)
                                            {
                                                if (transaction.TransactionState == SIPTransactionStatesEnum.Completed && transaction.TransactionFinalResponse != null)
                                                {
                                                    SendResponse(transaction.TransactionFinalResponse);
                                                    transaction.Retransmits += 1;
                                                    transaction.LastTransmit = DateTime.Now;
                                                    transaction.OnRetransmitFinalResponse();

                                                    FireSIPResponseRetransmitTraceEvent(transaction, transaction.TransactionFinalResponse, transaction.Retransmits);
                                                }
                                                else if (transaction.TransactionState == SIPTransactionStatesEnum.Proceeding && transaction.ReliableProvisionalResponse != null)
                                                {
                                                    SendResponse(transaction.ReliableProvisionalResponse);
                                                    transaction.Retransmits += 1;
                                                    transaction.LastTransmit = DateTime.Now;
                                                    transaction.OnRetransmitProvisionalResponse();

                                                    FireSIPResponseRetransmitTraceEvent(transaction, transaction.ReliableProvisionalResponse, transaction.Retransmits);
                                                }
                                            }
                                            else
                                            {
                                                FireSIPRequestRetransmitTraceEvent(transaction, transaction.TransactionRequest, transaction.Retransmits);

                                                if (transaction.OutboundProxy != null)
                                                {
                                                    SendRequest(transaction.OutboundProxy, transaction.TransactionRequest);
                                                }
                                                else if (transaction.RemoteEndPoint != null)
                                                {
                                                    SendRequest(transaction.RemoteEndPoint, transaction.TransactionRequest);
                                                }
                                                else
                                                {
                                                    SendRequest(transaction.TransactionRequest);
                                                }

                                                transaction.RequestRetransmit();
                                            }
                                        }
                                    }
                                }
                            }

                            // Remove timed out or complete transactions from reliable transmissions list.
                            if (completedTransactions.Count > 0)
                            {
                                foreach (string transactionId in completedTransactions)
                                {
                                    if (m_reliableTransmissions.ContainsKey(transactionId))
                                    {
                                        m_reliableTransmissions.Remove(transactionId);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.LogError("Exception SIPTransport ProcessPendingRequests checking pendings. " + excp.Message);
                    }

                    Thread.Sleep(PENDINGREQUESTS_CHECK_PERIOD);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport ProcessPendingRequests. " + excp.Message);
            }
            finally
            {
                m_reliablesThreadRunning = false;
            }
        }

        private void SIPMessageReceived(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            string rawSIPMessage = null;

            try
            {
                if (buffer != null && buffer.Length > 0)
                {
                    if ((buffer[0] == 0x0 || buffer[0] == 0x1) && buffer.Length >= 20)
                    {
                        // Treat any messages that cannot be SIP as STUN requests.
                        STUNRequestReceived?.Invoke(sipChannel.SIPChannelEndPoint.GetIPEndPoint(), remoteEndPoint.GetIPEndPoint(), buffer, buffer.Length);
                    }
                    else
                    {
                        // Treat all messages that don't match STUN requests as SIP.
                        if (buffer.Length > SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH)
                        {
                            string rawErrorMessage = Encoding.UTF8.GetString(buffer, 0, 1024) + "\r\n..truncated";
                            FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "SIP message too large, " + buffer.Length + " bytes, maximum allowed is " + SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH + " bytes.", SIPValidationFieldsEnum.Request, rawErrorMessage);
                            SIPResponse tooLargeResponse = GetResponse(sipChannel.SIPChannelEndPoint, remoteEndPoint, SIPResponseStatusCodesEnum.MessageTooLarge, null);
                            SendResponse(tooLargeResponse);
                        }
                        else
                        {
                            rawSIPMessage = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            if (rawSIPMessage.IsNullOrBlank())
                            {
                                // An emptry transmission has been received. More than likely this is a NAT keep alive and can be disregarded.
                                return;
                            }
                            else if (!rawSIPMessage.Contains("SIP"))
                            {
                                FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "Missing SIP string.", SIPValidationFieldsEnum.NoSIPString, rawSIPMessage);
                                return;
                            }

                            SIPMessage sipMessage = SIPMessage.ParseSIPMessage(rawSIPMessage, sipChannel.SIPChannelEndPoint, remoteEndPoint);

                            if (sipMessage != null)
                            {
                                if (sipMessage.SIPMessageType == SIPMessageTypesEnum.Response)
                                {
                                    #region SIP Response.

                                    try
                                    {
                                        // TODO: Provide a hook so consuming assemblies can get these stats.
                                        //if (PerformanceMonitorPrefix != null)
                                        //{
                                        //    SIPSorceryPerformanceMonitor.IncrementCounter(PerformanceMonitorPrefix + SIPSorceryPerformanceMonitor.SIP_TRANSPORT_SIP_RESPONSES_PER_SECOND_SUFFIX);
                                        //}

                                        SIPResponse sipResponse = SIPResponse.ParseSIPResponse(sipMessage);

                                        if (SIPResponseInTraceEvent != null)
                                        {
                                            FireSIPResponseInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipResponse);
                                        }

                                        if (m_transactionEngine != null && m_transactionEngine.Exists(sipResponse))
                                        {
                                            SIPTransaction transaction = m_transactionEngine.GetTransaction(sipResponse);

                                            if (transaction.TransactionState != SIPTransactionStatesEnum.Completed)
                                            {
                                                transaction.DeliveryPending = false;
                                                if (m_reliableTransmissions.ContainsKey(transaction.TransactionId))
                                                {
                                                    lock (m_reliableTransmissions)
                                                    {
                                                        m_reliableTransmissions.Remove(transaction.TransactionId);
                                                    }
                                                }
                                            }

                                            transaction.GotResponse(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipResponse);
                                        }
                                        else if (SIPTransportResponseReceived != null)
                                        {
                                            SIPTransportResponseReceived(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipResponse);
                                        }
                                    }
                                    catch (SIPValidationException sipValidationException)
                                    {
                                        //logger.LogWarning("Invalid SIP response from " + sipMessage.ReceivedFrom + ", " + sipResponse.ValidationError + " , ignoring.");
                                        //logger.LogWarning(sipMessage.RawMessage);
                                        FireSIPBadResponseInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipMessage.RawMessage, sipValidationException.SIPErrorField, sipMessage.RawMessage);
                                    }

                                    #endregion
                                }
                                else
                                {
                                    #region SIP Request.

                                    // TODO: Provide a hook so consuming assemblies can get these stats.
                                    //if (PerformanceMonitorPrefix != null)
                                    //{
                                    //    SIPSorceryPerformanceMonitor.IncrementCounter(PerformanceMonitorPrefix + SIPSorceryPerformanceMonitor.SIP_TRANSPORT_SIP_REQUESTS_PER_SECOND_SUFFIX);
                                    //}

                                    try
                                    {
                                        SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessage);

                                        SIPValidationFieldsEnum sipRequestErrorField = SIPValidationFieldsEnum.Unknown;
                                        string sipRequestValidationError = null;
                                        if (!sipRequest.IsValid(out sipRequestErrorField, out sipRequestValidationError))
                                        {
                                            throw new SIPValidationException(sipRequestErrorField, sipRequestValidationError);
                                        }

                                        if (SIPRequestInTraceEvent != null)
                                        {
                                            FireSIPRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequest);
                                        }

                                        // Stateful cores will create transactions once they get the request and the transport layer will use those transactions.
                                        // Stateless cores will not be affected by this step as the transaction layer will always return false.
                                        SIPTransaction requestTransaction = (m_transactionEngine != null) ? m_transactionEngine.GetTransaction(sipRequest) : null;
                                        if (requestTransaction != null)
                                        {
                                            if (requestTransaction.TransactionState == SIPTransactionStatesEnum.Completed && sipRequest.Method != SIPMethodsEnum.ACK
                                                && sipRequest.Method != SIPMethodsEnum.PRACK)
                                            {
                                                if (requestTransaction.TransactionFinalResponse != null)
                                                {
                                                    logger.LogWarning("Resending final response for " + sipRequest.Method + ", " + sipRequest.URI.ToString() + ", cseq=" + sipRequest.Header.CSeq + ".");
                                                    SendResponse(requestTransaction.TransactionFinalResponse);
                                                    requestTransaction.OnRetransmitFinalResponse();
                                                }
                                            }
                                            else if (sipRequest.Method == SIPMethodsEnum.ACK)
                                            {
                                                //logger.LogDebug("ACK received for " + requestTransaction.TransactionRequest.URI.ToString() + ".");

                                                if (requestTransaction.TransactionState == SIPTransactionStatesEnum.Completed)
                                                {
                                                    //logger.LogDebug("ACK received for INVITE, setting state to Confirmed, " + sipRequest.URI.ToString() + " from " + sipRequest.Header.From.FromURI.User + " " + remoteEndPoint + ".");
                                                    //requestTransaction.UpdateTransactionState(SIPTransactionStatesEnum.Confirmed);
                                                    sipRequest.Header.Vias.UpateTopViaHeader(remoteEndPoint.GetIPEndPoint());
                                                    requestTransaction.ACKReceived(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequest);
                                                }
                                                else
                                                {
                                                    //logger.LogWarning("ACK recieved from " + remoteEndPoint.ToString() + " on " + requestTransaction.TransactionState + " transaction, ignoring.");
                                                    FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "ACK recieved on " + requestTransaction.TransactionState + " transaction, ignoring.", SIPValidationFieldsEnum.Request, null);
                                                }
                                            }
                                            else if (sipRequest.Method == SIPMethodsEnum.PRACK)
                                            {
                                                if (requestTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding)
                                                {
                                                    sipRequest.Header.Vias.UpateTopViaHeader(remoteEndPoint.GetIPEndPoint());
                                                    requestTransaction.PRACKReceived(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequest);
                                                }
                                                else
                                                {
                                                    FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "PRACK recieved on " + requestTransaction.TransactionState + " transaction, ignoring.", SIPValidationFieldsEnum.Request, null);
                                                }
                                            }
                                            else
                                            {
                                                logger.LogWarning("Transaction already exists, ignoring duplicate request, " + sipRequest.Method + " " + sipRequest.URI.ToString() + ".");
                                                //FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "Transaction already exists, ignoring duplicate request, " + sipRequest.Method + " " + sipRequest.URI.ToString() + " from " + remoteEndPoint + ".", SIPValidationFieldsEnum.Request);
                                            }
                                        }
                                        else if (SIPTransportRequestReceived != null)
                                        {
                                            // This is a new SIP request and if the validity checks are passed it will be handed off to all subscribed new request listeners.

                                            #region Check for invalid SIP requests.

                                            if (sipRequest.Header.MaxForwards == 0 && sipRequest.Method != SIPMethodsEnum.OPTIONS)
                                            {
                                                // Check the MaxForwards value, if equal to 0 the request must be discarded. If MaxForwards is -1 it indicates the
                                                // header was not present in the request and that the MaxForwards check should not be undertaken.
                                                //logger.LogWarning("SIPTransport responding with TooManyHops due to 0 MaxForwards.");
                                                FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "Zero MaxForwards on " + sipRequest.Method + " " + sipRequest.URI.ToString() + " from " + sipRequest.Header.From.FromURI.User + " " + remoteEndPoint.ToString(), SIPValidationFieldsEnum.Request, sipRequest.ToString());
                                                SIPResponse tooManyHops = GetResponse(sipRequest, SIPResponseStatusCodesEnum.TooManyHops, null);
                                                SendResponse(sipChannel, tooManyHops);
                                                return;
                                            }
                                            /*else if (sipRequest.IsLoop(sipChannel.SIPChannelEndPoint.SocketEndPoint.Address.ToString(), sipChannel.SIPChannelEndPoint.SocketEndPoint.Port, sipRequest.CreateBranchId()))
                                            {
                                                //logger.LogWarning("SIPTransport Dropping looped request.");
                                                FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "Dropping looped request, " + sipRequest.Method + " " + sipRequest.URI.ToString() + " from " + sipRequest.Header.From.FromURI.User + " " + IPSocket.GetSocketString(remoteEndPoint), SIPValidationFieldsEnum.Request);
                                                SIPResponse loopResponse = GetResponse(sipRequest, SIPResponseStatusCodesEnum.LoopDetected, null);
                                                SendResponse(loopResponse);
                                                return;
                                            }*/

                                            #endregion

                                            #region Route pre-processing.

                                            if (sipRequest.Header.Routes.Length > 0)
                                            {
                                                PreProcessRouteInfo(sipRequest);
                                            }

                                            #endregion

                                            // Request has passed validity checks, adjust the client Via header to reflect the socket the request was received on.
                                            sipRequest.Header.Vias.UpateTopViaHeader(remoteEndPoint.GetIPEndPoint());

                                            // Stateful cores should create a transaction once they receive this event, stateless cores should not.
                                            SIPTransportRequestReceived(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequest);
                                        }
                                    }
                                    catch (SIPValidationException sipRequestExcp)
                                    {
                                        FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequestExcp.Message, sipRequestExcp.SIPErrorField, sipMessage.RawMessage);
                                        SIPResponse errorResponse = GetResponse(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequestExcp.SIPResponseErrorCode, sipRequestExcp.Message);
                                        SendResponse(sipChannel, errorResponse);
                                    }

                                    #endregion
                                }
                            }
                            else
                            {
                                FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "Not parseable as SIP message.", SIPValidationFieldsEnum.Unknown, rawSIPMessage);
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "Exception SIPTransport. " + excp.Message, SIPValidationFieldsEnum.Unknown, rawSIPMessage);
            }
        }

        /// <summary>
        /// Attempts to find a connection oriented SIPChannel that matches the specified connection ID.
        /// </summary>
        /// <param name="connectionID">The connection ID of the SIPChannel to find.</param>
        /// <returns>A matching SIPChannel if found otherwise null.</returns>
        public SIPChannel FindSIPChannel(string connectionID)
        {
            if (String.IsNullOrEmpty(connectionID))
            {
                return null;
            }
            else
            {
                return m_sipChannels.Where(x => x.Value.HasConnection(connectionID)).FirstOrDefault().Value;
            }
        }

        /// <summary>
        /// Attempts to match a SIPChannel that is listening on the specified local end point.
        /// </summary>
        /// <param name="localEndPoint">The local socket endpoint of the SIPChannel to find.</param>
        /// <returns>A matching SIPChannel if found otherwise null.</returns>
        public SIPChannel FindSIPChannel(SIPEndPoint localSIPEndPoint)
        {
            if (localSIPEndPoint == null)
            {
                return null;
            }
            else
            {
                if (m_sipChannels.ContainsKey(localSIPEndPoint.ToString()))
                {
                    return m_sipChannels[localSIPEndPoint.ToString()];
                }
                else
                {
                    logger.LogWarning("No SIP channel could be found for local SIP end point " + localSIPEndPoint.ToString() + ".");
                    return null;
                }
            }
        }

        /// <summary>
        /// Returns the first SIPChannel found for the requested protocol.
        /// </summary>
        /// <param name="protocol"></param>
        /// <returns></returns>
        private SIPChannel GetDefaultChannel(SIPProtocolsEnum protocol)
        {
            // Channels that are not on a loopback address take priority.
            foreach (SIPChannel sipChannel in m_sipChannels.Values)
            {
                if (sipChannel.SIPChannelEndPoint.Protocol == protocol && !IPAddress.IsLoopback(sipChannel.SIPChannelEndPoint.Address))
                {
                    return sipChannel;
                }
            }
            foreach (SIPChannel sipChannel in m_sipChannels.Values)
            {
                if (sipChannel.SIPChannelEndPoint.Protocol == protocol)
                {
                    return sipChannel;
                }
            }

            logger.LogWarning("No default SIP channel could be found for " + protocol + ".");
            return null;
        }

        private SIPChannel GetDefaultChannel(SIPEndPoint dstEndPoint)
        {
            var e = GetDefaultSIPEndPoint(dstEndPoint);
            var sipChannel = FindSIPChannel(e);
            if (sipChannel != null)
            {
                return sipChannel;
            }

            logger.LogWarning("No default SIP channel could be found for " + dstEndPoint.ToString() + ".");
            return null;
        }

        public bool IsLocalSIPEndPoint(SIPEndPoint sipEndPoint)
        {
            return m_sipChannels.ContainsKey(sipEndPoint.ToString());
        }

        public bool DoesTransactionExist(SIPRequest sipRequest)
        {
            if (m_transactionEngine == null)
            {
                return false;
            }
            else if (m_transactionEngine.GetTransaction(sipRequest) != null)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public List<SIPEndPoint> GetListeningSIPEndPoints()
        {
            try
            {
                List<SIPEndPoint> endPointsList = new List<SIPEndPoint>();

                foreach (SIPChannel channel in m_sipChannels.Values)
                {
                    endPointsList.Add(channel.SIPChannelEndPoint);
                }

                return endPointsList;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GetListeningSIPEndPoints. " + excp.Message);
                throw;
            }
        }

        #region Logging.

        private void FireSIPRequestInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                SIPRequestInTraceEvent?.Invoke(localSIPEndPoint, remoteEndPoint, sipRequest);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSIPRequestInTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPRequestOutTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                SIPRequestOutTraceEvent?.Invoke(localSIPEndPoint, remoteEndPoint, sipRequest);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSIPRequestOutTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPResponseInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            try
            {
                SIPResponseInTraceEvent?.Invoke(localSIPEndPoint, remoteEndPoint, sipResponse);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSIPResponseInTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPResponseOutTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            try
            {
                SIPResponseOutTraceEvent?.Invoke(localSIPEndPoint, remoteEndPoint, sipResponse);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSIPResponseOutTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPBadRequestInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, string message, SIPValidationFieldsEnum sipErrorField, string rawMessage)
        {
            try
            {
                SIPBadRequestInTraceEvent?.Invoke(localSIPEndPoint, remoteEndPoint, message, sipErrorField, rawMessage);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSIPBadRequestInTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPBadResponseInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, string message, SIPValidationFieldsEnum sipErrorField, string rawMessage)
        {
            try
            {
                SIPBadResponseInTraceEvent?.Invoke(localSIPEndPoint, remoteEndPoint, message, sipErrorField, rawMessage);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSIPBadResponseInTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPRequestRetransmitTraceEvent(SIPTransaction sipTransaction, SIPRequest sipRequest, int retransmitNumber)
        {
            try
            {
                SIPRequestRetransmitTraceEvent?.Invoke(sipTransaction, sipRequest, retransmitNumber);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSIPRequestRetransmitTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPResponseRetransmitTraceEvent(SIPTransaction sipTransaction, SIPResponse sipResponse, int retransmitNumber)
        {
            try
            {
                SIPResponseRetransmitTraceEvent?.Invoke(sipTransaction, sipResponse, retransmitNumber);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireSIPResponseRetransmitTraceEvent. " + excp.Message);
            }
        }

        #endregion

        #region Request, Response and Transaction retrieval and creation methods.

        public static SIPResponse GetResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode, reasonPhrase, sipRequest.LocalSIPEndPoint, sipRequest.RemoteSIPEndPoint);

                if (reasonPhrase != null)
                {
                    response.ReasonPhrase = reasonPhrase;
                }

                SIPHeader requestHeader = sipRequest.Header;
                SIPFromHeader from = (requestHeader == null || requestHeader.From != null) ? requestHeader.From : new SIPFromHeader(null, new SIPURI(sipRequest.URI.Scheme, sipRequest.LocalSIPEndPoint), null);
                SIPToHeader to = (requestHeader == null || requestHeader.To != null) ? requestHeader.To : new SIPToHeader(null, new SIPURI(sipRequest.URI.Scheme, sipRequest.LocalSIPEndPoint), null);
                int cSeq = (requestHeader == null || requestHeader.CSeq != -1) ? requestHeader.CSeq : 1;
                string callId = (requestHeader == null || requestHeader.CallId != null) ? requestHeader.CallId : CallProperties.CreateNewCallId();

                response.Header = new SIPHeader(from, to, cSeq, callId);
                response.Header.CSeqMethod = (requestHeader != null) ? requestHeader.CSeqMethod : SIPMethodsEnum.NONE;

                if (requestHeader == null || requestHeader.Vias == null || requestHeader.Vias.Length == 0)
                {
                    response.Header.Vias.PushViaHeader(new SIPViaHeader(sipRequest.RemoteSIPEndPoint, CallProperties.CreateBranchId()));
                }
                else
                {
                    response.Header.Vias = requestHeader.Vias;
                }

                response.Header.MaxForwards = Int32.MinValue;
                response.Header.Allow = ALLOWED_SIP_METHODS;

                return response;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport GetResponse. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Used to create a SIP response when it was not possible to parse the incoming SIP request.
        /// </summary>
        public SIPResponse GetResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            try
            {
                if (localSIPEndPoint == null)
                {
                    localSIPEndPoint = GetDefaultSIPEndPoint(remoteEndPoint != null ? remoteEndPoint.Protocol : SIPProtocolsEnum.udp);
                }

                SIPResponse response = new SIPResponse(responseCode, reasonPhrase, localSIPEndPoint, remoteEndPoint);
                SIPSchemesEnum sipScheme = (localSIPEndPoint.Protocol == SIPProtocolsEnum.tls) ? SIPSchemesEnum.sips : SIPSchemesEnum.sip;
                SIPFromHeader from = new SIPFromHeader(null, new SIPURI(sipScheme, localSIPEndPoint), null);
                SIPToHeader to = new SIPToHeader(null, new SIPURI(sipScheme, localSIPEndPoint), null);
                int cSeq = 1;
                string callId = CallProperties.CreateNewCallId();
                response.Header = new SIPHeader(from, to, cSeq, callId);
                response.Header.CSeqMethod = SIPMethodsEnum.NONE;
                response.Header.Vias.PushViaHeader(new SIPViaHeader(new SIPEndPoint(localSIPEndPoint.Protocol, remoteEndPoint.GetIPEndPoint()), CallProperties.CreateBranchId()));
                response.Header.MaxForwards = Int32.MinValue;
                response.Header.Allow = ALLOWED_SIP_METHODS;

                return response;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport GetResponse. " + excp.Message);
                throw;
            }
        }

        public SIPRequest GetRequest(SIPMethodsEnum method, SIPURI uri)
        {
            return GetRequest(method, uri, new SIPToHeader(null, uri, null), null);
        }

        public SIPRequest GetRequest(SIPMethodsEnum method, SIPURI uri, SIPToHeader to, SIPEndPoint localSIPEndPoint)
        {
            if (localSIPEndPoint == null)
            {
                localSIPEndPoint = GetDefaultSIPEndPoint(uri.Protocol);
            }

            SIPRequest request = new SIPRequest(method, uri);
            request.LocalSIPEndPoint = localSIPEndPoint;

            SIPContactHeader contactHeader = new SIPContactHeader(null, new SIPURI(SIPSchemesEnum.sip, localSIPEndPoint));
            SIPFromHeader fromHeader = new SIPFromHeader(null, contactHeader.ContactURI, CallProperties.CreateNewTag());
            SIPHeader header = new SIPHeader(contactHeader, fromHeader, to, 1, CallProperties.CreateNewCallId());
            request.Header = header;
            header.CSeqMethod = method;
            header.Allow = ALLOWED_SIP_METHODS;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
            header.Vias.PushViaHeader(viaHeader);

            return request;
        }

        public SIPTransaction GetTransaction(string transactionId)
        {
            CheckTransactionEngineExists();
            return m_transactionEngine.GetTransaction(transactionId);
        }

        public SIPTransaction GetTransaction(SIPRequest sipRequest)
        {
            CheckTransactionEngineExists();
            return m_transactionEngine.GetTransaction(sipRequest);
        }

        public SIPNonInviteTransaction CreateNonInviteTransaction(SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint, SIPEndPoint outboundProxy)
        {
            try
            {
                if (localSIPEndPoint == null)
                {
                    localSIPEndPoint = GetDefaultSIPEndPoint(localSIPEndPoint);
                }

                CheckTransactionEngineExists();
                SIPNonInviteTransaction nonInviteTransaction = new SIPNonInviteTransaction(this, sipRequest, dstEndPoint, localSIPEndPoint, outboundProxy);
                m_transactionEngine.AddTransaction(nonInviteTransaction);
                return nonInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateNonInviteTransaction. " + excp.Message);
                throw;
            }
        }

        public UACInviteTransaction CreateUACTransaction(SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint, SIPEndPoint outboundProxy, bool sendOkAckManually = false, bool disablePrackSupport = false)
        {
            try
            {
                if (localSIPEndPoint == null)
                {
                    localSIPEndPoint = GetDefaultSIPEndPoint(dstEndPoint != null ? dstEndPoint.Protocol : SIPProtocolsEnum.udp);
                }

                CheckTransactionEngineExists();
                UACInviteTransaction uacInviteTransaction = new UACInviteTransaction(this, sipRequest, dstEndPoint, localSIPEndPoint, outboundProxy, sendOkAckManually);
                m_transactionEngine.AddTransaction(uacInviteTransaction);
                return uacInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateUACTransaction. " + excp.Message);
                throw;
            }
        }

        public UASInviteTransaction CreateUASTransaction(SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint, SIPEndPoint outboundProxy, bool noCDR = false)
        {
            try
            {
                if (localSIPEndPoint == null)
                {
                    localSIPEndPoint = GetDefaultSIPEndPoint(dstEndPoint != null ? dstEndPoint.Protocol : SIPProtocolsEnum.udp);
                }

                CheckTransactionEngineExists();
                UASInviteTransaction uasInviteTransaction = new UASInviteTransaction(this, sipRequest, dstEndPoint, localSIPEndPoint, outboundProxy, ContactHost, noCDR);
                m_transactionEngine.AddTransaction(uasInviteTransaction);
                return uasInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateUASTransaction. " + excp);
                throw;
            }
        }

        public SIPCancelTransaction CreateCancelTransaction(SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint, UASInviteTransaction inviteTransaction)
        {
            try
            {
                if (localSIPEndPoint == null)
                {
                    localSIPEndPoint = GetDefaultSIPEndPoint(dstEndPoint != null ? dstEndPoint.Protocol : SIPProtocolsEnum.udp);
                }

                CheckTransactionEngineExists();
                SIPCancelTransaction cancelTransaction = new SIPCancelTransaction(this, sipRequest, dstEndPoint, localSIPEndPoint, inviteTransaction);
                m_transactionEngine.AddTransaction(cancelTransaction);
                return cancelTransaction;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateUASTransaction. " + excp);
                throw;
            }
        }

        private void CheckTransactionEngineExists()
        {
            if (m_transactionEngine == null)
            {
                throw new ApplicationException("A transaction engine is required for this operation but one has not been provided.");
            }
        }

        #endregion

        #region DNS resolution methods.

        public SIPDNSLookupResult GetHostEndPoint(string host, bool async)
        {
            return ResolveSIPEndPoint_External(SIPURI.ParseSIPURIRelaxed(host), async);
        }

        public SIPDNSLookupResult GetURIEndPoint(SIPURI uri, bool async)
        {
            return ResolveSIPEndPoint_External(uri, async);
        }

        /// <summary>
        /// Based on the information in the SIP request attempts to determine the end point the request should
        /// be sent to.
        /// </summary>
        public SIPDNSLookupResult GetRequestEndPoint(SIPRequest sipRequest, SIPEndPoint outboundProxy, bool async)
        {
            SIPURI lookupURI = (sipRequest.Header.Routes != null && sipRequest.Header.Routes.Length > 0) ? sipRequest.Header.Routes.TopRoute.URI : sipRequest.URI;

            if (outboundProxy != null)
            {
                return new SIPDNSLookupResult(lookupURI, outboundProxy);
            }
            else
            {
                return GetURIEndPoint(lookupURI, async);
            }
        }

        #endregion
    }
}
