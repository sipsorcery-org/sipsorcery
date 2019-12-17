//-----------------------------------------------------------------------------
// Filename: SIPTransport.cs
//
// Description: SIP transport layer implementation. Handles different network
// transport options, retransmits, timeouts and transaction matching.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Feb 2006  Aaron Clauson   Created, Dublin, Ireland.
// 26 Apr 2008  Aaron Clauson   Added TCP support.
// 16 Oct 2019  Aaron Clauson   Added IPv6 support.
// 25 Oct 2019  Aaron Clauson   Added async options for sending requests and responses.
// 30 Oct 2019  Aaron Clauson   Added support for reliable provisional responses as per RFC3262.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class SIPTransport
    {
        private const int MAX_QUEUEWAIT_PERIOD = 2000;              // Maximum time to wait to check the message received queue if no events are received.
        private const int PENDINGREQUESTS_CHECK_PERIOD = 500;      // Time between checking the pending requests queue to resend reliable requests that have not been responded to.
        private const int MAX_INMESSAGE_QUEUECOUNT = 5000;          // The maximum number of messages that can be stored in the incoming message queue.
        private const int MAX_RELIABLETRANSMISSIONS_COUNT = 5000;  // The maximum number of messages that can be maintained for reliable transmissions.

        public const string m_allowedSIPMethods = SIPConstants.ALLOWED_SIP_METHODS;

        private static readonly int m_t1 = SIPTimings.T1;
        private static readonly int m_t2 = SIPTimings.T2;
        private static readonly int m_t6 = SIPTimings.T6;
        private static string m_looseRouteParameter = SIPConstants.SIP_LOOSEROUTER_PARAMETER;
        public static IPAddress BlackholeAddress = IPAddress.Any;  // (IPAddress.Any is 0.0.0.0) Any SIP messages with this IP address will be dropped.

        private static ILogger logger = Log.Logger;

        /// <summary>
        /// Determines whether the transport later will queue incoming requests for processing on a separate thread of process
        /// immediately on the same thread. Most SIP elements with the exception of Stateless Proxies will typically want to 
        /// queue incoming SIP messages.
        /// </summary>
        private bool m_queueIncoming = true;

        private bool m_transportThreadStarted = false;
        private ConcurrentQueue<IncomingMessage> m_inMessageQueue = new ConcurrentQueue<IncomingMessage>();
        private ManualResetEvent m_inMessageArrived = new ManualResetEvent(false);
        private bool m_closed = false;

        /// <summary>
        /// List of the SIP channels that have been opened and are under management by this instance.
        /// The dictionary key is channel ID (previously was a serialised SIP end point).
        /// </summary>
        private Dictionary<string, SIPChannel> m_sipChannels = new Dictionary<string, SIPChannel>();

        private SIPTransactionEngine m_transactionEngine;
        private ResolveSIPEndPointDelegate ResolveSIPEndPoint_External;

        public event SIPTransportRequestDelegate SIPTransportRequestReceived;
        public event SIPTransportResponseDelegate SIPTransportResponseReceived;
        public event STUNRequestReceivedDelegate STUNRequestReceived;

        public event SIPTransportRequestDelegate SIPRequestInTraceEvent;
        public event SIPTransportRequestDelegate SIPRequestOutTraceEvent;
        public event SIPTransportResponseDelegate SIPResponseInTraceEvent;
        public event SIPTransportResponseDelegate SIPResponseOutTraceEvent;
        public event SIPTransportSIPBadMessageDelegate SIPBadRequestInTraceEvent;
        public event SIPTransportSIPBadMessageDelegate SIPBadResponseInTraceEvent;
        public event SIPTransactionRequestRetransmitDelegate SIPRequestRetransmitTraceEvent;
        public event SIPTransactionResponseRetransmitDelegate SIPResponseRetransmitTraceEvent;

        /// <summary>
        /// If set this host name (or IP address) will be set whenever the transport layer is asked to
        /// do a substitution on the Contact URI. The substitution is requested by a request or response
        /// setting the Contact URI host to IPAddress.Any ("0.0.0.0") or IPAddress.IPV6.Any ("::0").
        /// <code>
        /// var sipRequest = GetRequest(
        ///   method,
        ///   uri,
        ///   new SIPToHeader(
        ///     null, 
        ///     new SIPURI(uri.User, uri.Host, null, uri.Scheme, SIPProtocolsEnum.udp), 
        ///     null),
        ///   SIPFromHeader.GetDefaultSIPFromHeader(uri.Scheme));
        ///   
        /// // Set the Contact header to a default value that lets the transport layer know to update it
        /// // when the sending socket is selected.
        /// sipRequest.Header.Contact = new List&lt;SIPContactHeader&gt;() { SIPContactHeader.GetDefaultSIPContactHeader() };
        /// </code>
        /// </summary>
        public string ContactHost;

        /// <summary>
        /// Contains a list of the SIP Requests/Response that are being monitored or responses and retransmitted 
        /// on when none is recieved to attempt a more reliable delivery rather then just relying on the initial 
        /// request to get through.
        /// </summary>
        private ConcurrentDictionary<string, SIPTransaction> m_reliableTransmissions = new ConcurrentDictionary<string, SIPTransaction>();
        private bool m_reliablesThreadRunning = false;   // Only gets started when a request is made to send a reliable request.

        /// <summary>
        /// Creates a SIP transport class with default DNS resolver and SIP transaction engine.
        /// </summary>
        public SIPTransport()
        {
            ResolveSIPEndPoint_External = SIPDNSManager.ResolveSIPService;
            m_transactionEngine = new SIPTransactionEngine();
        }

        internal SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine)
        {
            if (sipResolver == null)
            {
                throw new ArgumentNullException("The SIP end point resolver must be set when creating a SIPTransport object.");
            }

            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
        }

        internal SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine, bool queueIncoming)
        {
            if (sipResolver == null)
            {
                throw new ArgumentNullException("The SIP end point resolver must be set when creating a SIPTransport object.");
            }

            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
            m_queueIncoming = queueIncoming;
        }

        internal SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine, SIPChannel sipChannel, bool queueIncoming)
        {
            if (sipResolver == null)
            {
                throw new ArgumentNullException("The SIP end point resolver must be set when creating a SIPTransport object.");
            }

            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
            m_queueIncoming = queueIncoming;

            AddSIPChannel(sipChannel);
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
        public void AddSIPChannel(SIPChannel sipChannel)
        {
            try
            {
                m_sipChannels.Add(sipChannel.ID, sipChannel);

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
            if (m_sipChannels.ContainsKey(sipChannel.ID))
            {
                m_sipChannels.Remove(sipChannel.ID);
                sipChannel.SIPMessageReceived -= ReceiveMessage;
            }
        }

        private void StartTransportThread()
        {
            if (!m_transportThreadStarted)
            {
                m_transportThreadStarted = true;
                Task.Run((Action)ProcessInMessage);
            }
        }

        private void StartReliableTransmissionsThread()
        {
            if (!m_reliablesThreadRunning)
            {
                m_reliablesThreadRunning = true;
                Task.Run((Action)ProcessTransactions);
            }
        }

        public async void ReceiveMessage(SIPChannel sipChannel, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            try
            {
                if (!m_queueIncoming)
                {
                    await SIPMessageReceived(sipChannel, localEndPoint, remoteEndPoint, buffer);
                }
                else
                {
                    IncomingMessage incomingMessage = new IncomingMessage(sipChannel, localEndPoint, remoteEndPoint, buffer);

                    // Keep the queue within size limits 
                    if (m_inMessageQueue.Count >= MAX_INMESSAGE_QUEUECOUNT)
                    {
                        logger.LogWarning("SIPTransport queue full new message from " + remoteEndPoint + " being discarded.");
                    }
                    else
                    {
                        m_inMessageQueue.Enqueue(incomingMessage);
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

                m_inMessageArrived.Set();

                foreach (SIPChannel channel in m_sipChannels.Values)
                {
                    channel.Close();
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport Shutdown. " + excp.Message);
            }
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
                        if (sipChannel.IsChannelSocket(sipRequest.URI.Host))
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
                            // TODO: For IPAddress.Any have to check all available IP addresses not just listening one.
                            if (sipChannel.IsChannelSocket(sipRequest.Header.Routes.TopRoute.URI.Host))
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

            SIPChannel sendSIPChannel = m_sipChannels[localSIPEndPoint.ChannelID];
            sendSIPChannel.Send(dstEndPoint.GetIPEndPoint(), buffer, null);
        }

        /// <summary>
        /// Attempts to send a SIP request to a destination end point. This method will attempt to:
        /// - determine the IP address and port to send the request to by using SIP routing and DNS rules.
        /// - find the most appropriate local SIP channel in this SIP transport to send the request on.
        /// </summary>
        /// <param name="sipRequest">The SIP request to send.</param>
        public void SendRequest(SIPRequest sipRequest)
        {
            SendRequestAsync(sipRequest).Wait();
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
                throw new ApplicationException("No channels are configured in the SIP transport layer.");
            }

            SIPDNSLookupResult dnsResult = GetRequestEndPoint(sipRequest, null, true);

            if (dnsResult.LookupError != null)
            {
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
                    logger.LogWarning($"SIP Transport could not send request as end point could not be determined:  {sipRequest.StatusLine}.");
                    return SocketError.HostNotFound;
                }
            }
        }

        /// <summary>
        /// Attempts to send a SIP request to a destination end point. This method will attempt to find the most appropriate
        /// local SIP channel in this SIP transport to send the request on.
        /// </summary>
        /// <param name="dstEndPoint">The destination end point to send the request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        public void SendRequest(SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
            SendRequestAsync(dstEndPoint, sipRequest).Wait();
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

            SIPChannel sipChannel = GetSIPChannelForDestination(dstEndPoint.Protocol, dstEndPoint.GetIPEndPoint(), sipRequest.SendFromHintChannelID);
            SIPEndPoint sendFromSIPEndPoint = sipChannel.GetLocalSIPEndPointForDestination(dstEndPoint.GetIPEndPoint().Address);

            // Once the channel has been determined check some specific header fields and replace the placeholder end point.
            AdjustHeadersForEndPoint(sendFromSIPEndPoint, ref sipRequest.Header);

            return await SendRequestAsync(sipChannel, sendFromSIPEndPoint, dstEndPoint, sipRequest);
        }

        /// <summary>
        /// Attempts to send a SIP request to the destination end point using the specified SIP channel.
        /// </summary>
        /// <param name="sipChannel">The SIP channel to use to send the SIP request.</param>
        /// <param name="dstEndPoint">The destination to send the SIP request to.</param>
        /// <param name="sipRequest">The SIP request to send.</param>
        private async Task<SocketError> SendRequestAsync(SIPChannel sipChannel, SIPEndPoint sendFromSIPEndPoint, SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
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

            sipRequest.Header.ContentLength = (sipRequest.Body.NotNullOrBlank()) ? Encoding.UTF8.GetByteCount(sipRequest.Body) : 0;

            FireSIPRequestOutTraceEvent(sendFromSIPEndPoint, dstEndPoint, sipRequest);

            SocketError sendResult = SocketError.Success;

            if (sipChannel.IsSecure)
            {
                sendResult = await sipChannel.SendSecureAsync(dstEndPoint.GetIPEndPoint(), Encoding.UTF8.GetBytes(sipRequest.ToString()), sipRequest.URI.Host, sipRequest.SendFromHintConnectionID);
            }
            else
            {
                sendResult = await sipChannel.SendAsync(dstEndPoint.GetIPEndPoint(), Encoding.UTF8.GetBytes(sipRequest.ToString()), sipRequest.SendFromHintConnectionID);
            }

            return sendResult;
        }

        /// <summary>
        /// Sends a SIP request/response and keeps track of whether a response/acknowledgement has been received.
        /// For UDP "reliably" means retransmitting the message up to eleven times.
        /// If no response is received then periodic retransmits are made for up to T1 x 64 seconds (defaults to 30 seconds with 11 retransmits).
        /// </summary>
        /// <param name="sipTransaction">The SIP transaction encapsulating the SIP request or response that needs to be sent reliably.</param>
        public void SendReliable(SIPTransaction sipTransaction)
        {
            if (sipTransaction == null)
            {
                throw new ArgumentNullException("sipTransaction", "The SIP transaction parameter must be set for SendSIPReliable.");
            }
            else if (m_sipChannels.Count == 0)
            {
                throw new ApplicationException("No channels are configured in the SIP transport layer. The request could not be sent.");
            }
            else if (m_reliableTransmissions.Count >= MAX_RELIABLETRANSMISSIONS_COUNT)
            {
                throw new ApplicationException("Cannot send reliable SIP message as the reliable transmissions queue is full.");
            }

            if (!m_transactionEngine.Exists(sipTransaction.TransactionId))
            {
                m_transactionEngine.AddTransaction(sipTransaction);
            }

            if (!m_reliableTransmissions.ContainsKey(sipTransaction.TransactionId))
            {
                m_reliableTransmissions.TryAdd(sipTransaction.TransactionId, sipTransaction);
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
        public void SendResponse(SIPResponse sipResponse)
        {
            SendResponseAsync(sipResponse).Wait();
        }

        /// <summary>
        /// Asynchronously forwards a SIP response. There are two main cases for a SIP response to be forwarded:
        /// - First case is when we have processed a request and are returning a response. In this case the response
        ///   should be sent back on exactly the same socket the request came on.
        /// - Second case is when we are acting as a Proxy and the response is on it's way back from the agent
        ///   that processed the request. In this case it's highly likely the response needs to be forwarded to
        ///   a different end point then the one it came from and it's also possible it will need to use a completely
        ///   different channel to send on compared to the one it arrived on.
        /// 
        /// Forwarding logic:
        /// - If the channel hints are set then an attempt will be made to use them to find an approriate channel to
        ///   send the response on. If the hinted channel can't be found or it is found but is the wrong protocol then
        ///   move onto the next step,
        /// - The information in the Top Via header will be used to find the best channel to forward the response on.
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

            // First step, resolving the destination end point.
            SIPEndPoint dstEndPoint = null;
            var result = GetDestinationForResponse(sipResponse);
            if (result.status == SocketError.Success)
            {
                dstEndPoint = result.dstEndPoint;
            }
            else
            {
                // The destination couldn't be resolved, return the error message.
                // Note this could be a temporary failure it we're still waiting for a DNS resolution.
                return result.status;
            }

            return await SendResponseAsync(dstEndPoint, sipResponse);
        }

        /// <summary>
        /// Asynchronously forwards a SIP response to the specified destination.
        /// </summary>
        /// <param name="dstEndPoint">The destination end point to send the response to.</param>
        /// <param name="sipResponse">The SIP response to send.</param>
        public async Task<SocketError> SendResponseAsync(SIPEndPoint dstEndPoint, SIPResponse sipResponse)
        {
            if (dstEndPoint == null)
            {
                throw new ArgumentNullException("dstEndPoint", "The destination end point must be set for SendResponseAsync.");
            }
            else if (sipResponse == null)
            {
                throw new ArgumentNullException("sipResponse", "The SIP response must be set for SendResponseAsync.");
            }
            else if (m_sipChannels.Count == 0)
            {
                throw new ApplicationException("No channels are configured in the SIP transport layer. The response could not be sent.");
            }

            if (dstEndPoint != null && dstEndPoint.Address.Equals(BlackholeAddress))
            {
                // Ignore packet, it's destined for the blackhole.
                return SocketError.Success;
            }
            else
            {
                // Once the destination is known determine the local SIP channel to reach it.
                SIPChannel sendFromChannel = GetSIPChannelForDestination(dstEndPoint.Protocol, dstEndPoint.GetIPEndPoint(), sipResponse.SendFromHintChannelID);
                SIPEndPoint sendFromSIPEndPoint = sendFromChannel.GetLocalSIPEndPointForDestination(dstEndPoint.GetIPEndPoint().Address);

                // Once the channel has been determined check some specific header fields and replace the placeholder end point.
                AdjustHeadersForEndPoint(sendFromSIPEndPoint, ref sipResponse.Header);

                sipResponse.Header.ContentLength = (sipResponse.Body.NotNullOrBlank()) ? Encoding.UTF8.GetByteCount(sipResponse.Body) : 0;

                FireSIPResponseOutTraceEvent(sendFromSIPEndPoint, dstEndPoint, sipResponse);

                // Now have a destination and sending channel, go ahead and forward.
                return await sendFromChannel.SendAsync(dstEndPoint.GetIPEndPoint(), Encoding.UTF8.GetBytes(sipResponse.ToString()), sipResponse.SendFromHintConnectionID);
            }
        }

        /// <summary>
        /// Checks specific SIP headers for "0.0.0.0" or "::0" strings and where found replaces them with the socket that the
        /// request or response is being sent from. This mechanism is used to allow higher level agents to indicate they want to defer
        /// the setting of those header fields to the transport class.
        /// </summary>
        /// <param name="sendFromEndPoint">The IP end point the request or response is being sent from.</param>
        /// <param name="sipHeader">The SIP header object to apply the adjustments to. The header object will be updated
        /// in place with any header adjustments.</param>
        private void AdjustHeadersForEndPoint(SIPEndPoint sendFromSIPEndPoint, ref SIPHeader header)
        {
            IPEndPoint sendFromEndPoint = sendFromSIPEndPoint.GetIPEndPoint();

            // Top Via header.
            if (header.Vias.TopViaHeader.ContactAddress.StartsWith(IPAddress.Any.ToString()) ||
                header.Vias.TopViaHeader.ContactAddress.StartsWith(IPAddress.IPv6Any.ToString()))
            {
                header.Vias.Via[0].Host = sendFromEndPoint.Address.ToString();
                header.Vias.Via[0].Port = sendFromEndPoint.Port;
            }

            if (header.Vias.TopViaHeader.Transport != sendFromSIPEndPoint.Protocol)
            {
                header.Vias.Via[0].Transport = sendFromSIPEndPoint.Protocol;
            }

            // From header.
            if (header.From.FromURI.Host.StartsWith(IPAddress.Any.ToString()) ||
                header.From.FromURI.Host.StartsWith(IPAddress.IPv6Any.ToString()))
            {
                header.From.FromURI.Host = sendFromEndPoint.ToString();
            }

            // Contact header.
            if (header.Contact != null && header.Contact.Count == 1)
            {
                if (header.Contact.Single().ContactURI.Host.StartsWith(IPAddress.Any.ToString()) ||
                    header.Contact.Single().ContactURI.Host.StartsWith(IPAddress.IPv6Any.ToString()))
                {
                    if (!String.IsNullOrEmpty(ContactHost))
                    {
                        header.Contact.Single().ContactURI.Host = ContactHost + ":" + sendFromEndPoint.Port.ToString();
                    }
                    else
                    {
                        header.Contact.Single().ContactURI.Host = sendFromEndPoint.ToString();
                    }
                }

                if (header.Contact.Single().ContactURI.Scheme == SIPSchemesEnum.sip && sendFromSIPEndPoint.Protocol != SIPProtocolsEnum.udp)
                {
                    header.Contact.Single().ContactURI.Protocol = sendFromSIPEndPoint.Protocol;
                }
            }
        }

        /// <summary>
        /// Attempts to resolve the desintation end point for a SIP response from the top SIP Via header. 
        /// Normally the address in the header will be an IP address but the standard does permit a host which 
        /// will require a DNS lookup.
        /// </summary>
        /// <param name="sipResponse">The SIP response to forward.</param>
        /// <returns>A socket error object indicating the result of the resolve attempt and if successful a SIP
        /// end point to forward the SIP response to.</returns>
        private (SocketError status, SIPEndPoint dstEndPoint) GetDestinationForResponse(SIPResponse sipResponse)
        {
            SIPViaHeader topViaHeader = sipResponse.Header.Vias.TopViaHeader;
            if (topViaHeader == null)
            {
                logger.LogWarning($"There was no top Via header on a SIP response from {sipResponse.RemoteSIPEndPoint} in SendResponseAsync, response dropped.");
                return (SocketError.Fault, null);
            }
            else
            {
                SIPURI lookupURI = new SIPURI(null, topViaHeader.ReceivedFromAddress, null, SIPSchemesEnum.sip, topViaHeader.Transport);
                //SIPDNSLookupResult lookupResult = GetHostEndPoint(topViaHeader.ReceivedFromAddress, false);
                SIPDNSLookupResult lookupResult = GetURIEndPoint(lookupURI, false);

                if (lookupResult.LookupError != null)
                {
                    logger.LogWarning("Could not resolve destination for response.\n" + sipResponse.ToString());
                    return (SocketError.HostNotFound, null);
                }
                else if (lookupResult.Pending)
                {
                    // Ignore this response transmission and wait for the transaction retransmit mechanism to try again when DNS will have 
                    // hopefully resolved the end point.
                    return (SocketError.IOPending, null);
                }
                else
                {
                    return (SocketError.Success, lookupResult.GetSIPEndPoint());
                }
            }
        }

        private async void ProcessInMessage()
        {
            try
            {
                while (!m_closed)
                {
                    m_transactionEngine.RemoveExpiredTransactions();

                    while (m_inMessageQueue.Count > 0)
                    {
                        m_inMessageQueue.TryDequeue(out var incomingMessage);
                        if (incomingMessage != null)
                        {
                            await SIPMessageReceived(incomingMessage.LocalSIPChannel, incomingMessage.LocalEndPoint, incomingMessage.RemoteEndPoint, incomingMessage.Buffer);
                        }
                    }

                    if (!m_closed)
                    {
                        m_inMessageArrived.Reset();
                        m_inMessageArrived.WaitOne(MAX_QUEUEWAIT_PERIOD);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport ProcessInMessage. " + excp.Message);
            }
        }

        /// <summary>
        /// A long running method that monitors and processes a list of transactions that need to send a reliable
        /// request or response.
        /// </summary>
        private async void ProcessTransactions()
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

                    List<string> completedTransactions = new List<string>();

                    foreach (SIPTransaction transaction in m_reliableTransmissions.Values)
                    {
                        try
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
                                if (transaction.InitialTransmit != DateTime.MinValue && DateTime.Now.Subtract(transaction.InitialTransmit).TotalMilliseconds >= m_t6)
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

                                    if (transaction.InitialTransmit == DateTime.MinValue || DateTime.Now.Subtract(transaction.LastTransmit).TotalMilliseconds >= nextTransmitMilliseconds)
                                    {
                                        if (transaction.InitialTransmit == DateTime.MinValue)
                                        {
                                            transaction.InitialTransmit = DateTime.Now;
                                        }

                                        transaction.Retransmits = transaction.Retransmits + 1;
                                        transaction.LastTransmit = DateTime.Now;

                                        SocketError result = SocketError.Success;

                                        if (transaction.TransactionType == SIPTransactionTypesEnum.InviteServer)
                                        {
                                            if (transaction.TransactionState == SIPTransactionStatesEnum.Completed && transaction.TransactionFinalResponse != null)
                                            {
                                                if (transaction.Retransmits > 1)
                                                {
                                                    transaction.OnRetransmitFinalResponse();
                                                    FireSIPResponseRetransmitTraceEvent(transaction, transaction.TransactionFinalResponse, transaction.Retransmits);
                                                }
                                                result = await SendResponseAsync(transaction.TransactionFinalResponse);
                                            }
                                            else if (transaction.TransactionState == SIPTransactionStatesEnum.Proceeding && transaction.ReliableProvisionalResponse != null)
                                            {
                                                if (transaction.Retransmits > 1)
                                                {
                                                    transaction.OnRetransmitProvisionalResponse();
                                                    FireSIPResponseRetransmitTraceEvent(transaction, transaction.ReliableProvisionalResponse, transaction.Retransmits);
                                                }
                                                result = await SendResponseAsync(transaction.ReliableProvisionalResponse);
                                            }
                                        }
                                        else
                                        {
                                            if (transaction.Retransmits > 1)
                                            {
                                                FireSIPRequestRetransmitTraceEvent(transaction, transaction.TransactionRequest, transaction.Retransmits);
                                            }

                                            if (transaction.OutboundProxy != null)
                                            {
                                                result = await SendRequestAsync(transaction.OutboundProxy, transaction.TransactionRequest);
                                            }
                                            else
                                            {
                                                result = await SendRequestAsync(transaction.TransactionRequest);
                                            }

                                            if (transaction.Retransmits > 1 && result == SocketError.Success)
                                            {
                                                transaction.RequestRetransmit();
                                            }
                                        }

                                        if (result != SocketError.Success)
                                        {
                                            // One example of a failure here is requiring a specific TCP or TLS connection that no longer exists.
                                            transaction.DeliveryPending = false;
                                            transaction.DeliveryFailed = true;
                                            transaction.TimedOutAt = DateTime.Now;
                                            transaction.FireTransactionTimedOut();
                                            completedTransactions.Add(transaction.TransactionId);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception excp)
                        {
                            logger.LogError($"Exception processing reliable transaction. {excp.Message}");
                        }
                    }

                    // Remove timed out or complete transactions from reliable transmissions list.
                    if (completedTransactions.Count > 0)
                    {
                        foreach (string transactionId in completedTransactions)
                        {
                            if (m_reliableTransmissions.ContainsKey(transactionId))
                            {
                                m_reliableTransmissions.TryRemove(transactionId, out _);
                            }
                        }
                    }
                }

                await Task.Delay(PENDINGREQUESTS_CHECK_PERIOD);

            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport ProcessTransactions. " + excp.Message);
            }
            finally
            {
                m_reliablesThreadRunning = false;
            }
        }

        /// <summary>
        /// Processes incoming data transmission from a SIP channel.
        /// </summary>
        /// <param name="sipChannel">The SIP channel the message was received on.</param>
        /// <param name="localEndPoint">The local end point that the SIP channel received the message on.</param>
        /// <param name="remoteEndPoint">The remote end point the message came from.</param>
        /// <param name="buffer">The raw message received.</param>
        private async Task SIPMessageReceived(SIPChannel sipChannel, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, byte[] buffer)
        {
            string rawSIPMessage = null;

            try
            {
                if (buffer != null && buffer.Length > 0)
                {
                    if ((buffer[0] == 0x0 || buffer[0] == 0x1) && buffer.Length >= 20)
                    {
                        // Treat any messages that cannot be SIP as STUN requests.
                        STUNRequestReceived?.Invoke(localEndPoint.GetIPEndPoint(), remoteEndPoint.GetIPEndPoint(), buffer, buffer.Length);
                    }
                    else
                    {
                        // Treat all messages that don't match STUN requests as SIP.
                        if (buffer.Length > SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH)
                        {
                            string rawErrorMessage = Encoding.UTF8.GetString(buffer, 0, 1024) + "\r\n..truncated";
                            FireSIPBadRequestInTraceEvent(localEndPoint, remoteEndPoint, "SIP message too large, " + buffer.Length + " bytes, maximum allowed is " + SIPConstants.SIP_MAXIMUM_RECEIVE_LENGTH + " bytes.", SIPValidationFieldsEnum.Request, rawErrorMessage);
                            SIPResponse tooLargeResponse = SIPResponse.GetResponse(localEndPoint, remoteEndPoint, SIPResponseStatusCodesEnum.MessageTooLarge, null);
                            await SendResponseAsync(tooLargeResponse);
                        }
                        else
                        {
                            // TODO: Future improvement (4.5.2 doesn't support) is to use a ReadOnlySpan to check for the existence 
                            // of 'S', 'I', 'P' before the first EOL.
                            rawSIPMessage = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            if (rawSIPMessage.IsNullOrBlank() || SIPMessageBuffer.IsPing(buffer))
                            {
                                // An empty transmission has been received. More than likely this is a NAT keep alive and can be disregarded.
                                return;
                            }
                            else if (!rawSIPMessage.Contains("SIP"))
                            {
                                FireSIPBadRequestInTraceEvent(localEndPoint, remoteEndPoint, "Missing SIP string.", SIPValidationFieldsEnum.NoSIPString, rawSIPMessage);
                                return;
                            }

                            SIPMessageBuffer sipMessageBuffer = SIPMessageBuffer.ParseSIPMessage(rawSIPMessage, localEndPoint, remoteEndPoint);

                            if (sipMessageBuffer != null)
                            {
                                if (sipMessageBuffer.SIPMessageType == SIPMessageTypesEnum.Response)
                                {
                                    #region SIP Response.

                                    try
                                    {
                                        SIPResponse sipResponse = SIPResponse.ParseSIPResponse(sipMessageBuffer);

                                        if (SIPResponseInTraceEvent != null)
                                        {
                                            FireSIPResponseInTraceEvent(localEndPoint, remoteEndPoint, sipResponse);
                                        }

                                        if (m_transactionEngine != null && m_transactionEngine.Exists(sipResponse))
                                        {
                                            SIPTransaction transaction = m_transactionEngine.GetTransaction(sipResponse);

                                            if (transaction.TransactionState != SIPTransactionStatesEnum.Completed)
                                            {
                                                transaction.DeliveryPending = false;
                                                if (m_reliableTransmissions.ContainsKey(transaction.TransactionId))
                                                {
                                                    m_reliableTransmissions.TryRemove(transaction.TransactionId, out _);
                                                }
                                            }

                                            transaction.GotResponse(localEndPoint, remoteEndPoint, sipResponse);
                                        }
                                        else
                                        {
                                            SIPTransportResponseReceived?.Invoke(localEndPoint, remoteEndPoint, sipResponse);
                                        }
                                    }
                                    catch (SIPValidationException sipValidationException)
                                    {
                                        FireSIPBadResponseInTraceEvent(localEndPoint, remoteEndPoint, sipMessageBuffer.RawMessage, sipValidationException.SIPErrorField, sipMessageBuffer.RawMessage);
                                    }

                                    #endregion
                                }
                                else
                                {
                                    #region SIP Request.

                                    try
                                    {
                                        SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessageBuffer);

                                        SIPValidationFieldsEnum sipRequestErrorField = SIPValidationFieldsEnum.Unknown;
                                        string sipRequestValidationError = null;
                                        if (!sipRequest.IsValid(out sipRequestErrorField, out sipRequestValidationError))
                                        {
                                            throw new SIPValidationException(sipRequestErrorField, sipRequestValidationError);
                                        }

                                        if (SIPRequestInTraceEvent != null)
                                        {
                                            FireSIPRequestInTraceEvent(localEndPoint, remoteEndPoint, sipRequest);
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
                                                    await SendResponseAsync(requestTransaction.TransactionFinalResponse);
                                                    requestTransaction.OnRetransmitFinalResponse();
                                                }
                                            }
                                            else if (sipRequest.Method == SIPMethodsEnum.ACK)
                                            {
                                                if (requestTransaction.TransactionState == SIPTransactionStatesEnum.Completed)
                                                {
                                                    sipRequest.Header.Vias.UpateTopViaHeader(remoteEndPoint.GetIPEndPoint());
                                                    requestTransaction.ACKReceived(localEndPoint, remoteEndPoint, sipRequest);
                                                }
                                                else
                                                {
                                                    FireSIPBadRequestInTraceEvent(localEndPoint, remoteEndPoint, "ACK recieved on " + requestTransaction.TransactionState + " transaction, ignoring.", SIPValidationFieldsEnum.Request, null);
                                                }
                                            }
                                            else if (sipRequest.Method == SIPMethodsEnum.PRACK)
                                            {
                                                sipRequest.Header.Vias.UpateTopViaHeader(remoteEndPoint.GetIPEndPoint());
                                                requestTransaction.PRACKReceived(localEndPoint, remoteEndPoint, sipRequest);
                                            }
                                            else
                                            {
                                                logger.LogWarning("Transaction already exists, ignoring duplicate request, " + sipRequest.Method + " " + sipRequest.URI.ToString() + ".");
                                            }
                                        }
                                        else if (m_transactionEngine != null && sipRequest.Method == SIPMethodsEnum.CANCEL &&
                                            GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE)) != null)
                                        {
                                            UASInviteTransaction inviteTransaction = (UASInviteTransaction)GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));
                                            SIPCancelTransaction cancelTransaction = new SIPCancelTransaction(this, sipRequest, inviteTransaction);
                                            cancelTransaction.GotRequest(localEndPoint, remoteEndPoint, sipRequest);
                                        }
                                        else if (SIPTransportRequestReceived != null)
                                        {
                                            // This is a new SIP request and if the validity checks are passed it will be handed off to all subscribed new request listeners
                                            if (sipRequest.Header.MaxForwards == 0 && sipRequest.Method != SIPMethodsEnum.OPTIONS)
                                            {
                                                // Check the MaxForwards value, if equal to 0 the request must be discarded. If MaxForwards is -1 it indicates the
                                                // header was not present in the request and that the MaxForwards check should not be undertaken.
                                                FireSIPBadRequestInTraceEvent(localEndPoint, remoteEndPoint, $"Zero MaxForwards on {sipRequest.Method} {sipRequest.URI} from {sipRequest.Header.From.FromURI.User} {remoteEndPoint}.", SIPValidationFieldsEnum.Request, sipRequest.ToString());
                                                SIPResponse tooManyHops = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.TooManyHops, null);
                                                await SendResponseAsync(tooManyHops);
                                                return;
                                            }
                                            else if (sipRequest.Header.UnknownRequireExtension != null)
                                            {
                                                // The sender requires an extension that we don't support.
                                                FireSIPBadRequestInTraceEvent(localEndPoint, remoteEndPoint, $"Rejecting request to one or more required exensions not being supported, unsupported extensions: {sipRequest.Header.UnknownRequireExtension}.", SIPValidationFieldsEnum.Request, sipRequest.ToString());
                                                SIPResponse badRequireResp = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BadExtension, null);
                                                badRequireResp.Header.Unsupported = sipRequest.Header.UnknownRequireExtension;
                                                await SendResponseAsync(badRequireResp);
                                                return;
                                            }

                                            if (sipRequest.Header.Routes.Length > 0)
                                            {
                                                PreProcessRouteInfo(sipRequest);
                                            }

                                            // Request has passed validity checks, adjust the client Via header to reflect the socket the request was received on.
                                            sipRequest.Header.Vias.UpateTopViaHeader(remoteEndPoint.GetIPEndPoint());

                                            // Stateful cores should create a transaction once they receive this event, stateless cores should not.
                                            SIPTransportRequestReceived(localEndPoint, remoteEndPoint, sipRequest);
                                        }
                                    }
                                    catch (SIPValidationException sipRequestExcp)
                                    {
                                        FireSIPBadRequestInTraceEvent(localEndPoint, remoteEndPoint, sipRequestExcp.Message, sipRequestExcp.SIPErrorField, sipMessageBuffer.RawMessage);
                                        SIPResponse errorResponse = SIPResponse.GetResponse(localEndPoint, remoteEndPoint, sipRequestExcp.SIPResponseErrorCode, sipRequestExcp.Message);
                                        await SendResponseAsync(errorResponse);
                                    }

                                    #endregion
                                }
                            }
                            else
                            {
                                FireSIPBadRequestInTraceEvent(localEndPoint, remoteEndPoint, "Not parseable as SIP message.", SIPValidationFieldsEnum.Unknown, rawSIPMessage);
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                FireSIPBadRequestInTraceEvent(localEndPoint, remoteEndPoint, "Exception SIPTransport. " + excp.Message, SIPValidationFieldsEnum.Unknown, rawSIPMessage);
            }
        }

        /// <summary>
        /// Attempts to locate a SIP channel that can be used to communicate with a remote end point
        /// over a specific SIP protocol.
        /// </summary>
        /// <param name="protocol">The SIP protocol required for the communication.</param>
        /// <param name="dst">The destination end point.</param>
        /// <param name="channelIDHint">An optional channel ID that gives a hint as to the preferred 
        /// channel to select.</param>
        /// <returns>If found a SIP channel or null if not.</returns>
        private SIPChannel GetSIPChannelForDestination(SIPProtocolsEnum protocol, IPEndPoint dst, string channelIDHint)
        {
            if (m_sipChannels == null || m_sipChannels.Count == 0)
            {
                throw new ApplicationException("The transport layer does not have any SIP channels.");
            }
            else if (!m_sipChannels.Any(x => x.Value.SIPProtocol == protocol && x.Value.ListeningIPAddress.AddressFamily == dst.Address.AddressFamily))
            {
                throw new ApplicationException($"The transport layer does not have any SIP channels matching {protocol} and {dst.AddressFamily}.");
            }
            else if (!String.IsNullOrEmpty(channelIDHint) && m_sipChannels.Any(x => x.Value.SIPProtocol == protocol && x.Key == channelIDHint))
            {
                return m_sipChannels[channelIDHint];
            }
            else
            {
                SIPChannel matchingChannel = null;

                // There's at least one channel available. If there's an IPAddress.Any channel choose that first
                // since it's able to use all the machine's active network interfaces and should be able to reach
                // any remote end point.
                IPAddress addrAny = (dst.AddressFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any;
                matchingChannel = GetSIPChannel(protocol, addrAny);
                if (matchingChannel != null)
                {
                    return matchingChannel;
                }

                // Check for an exact match on the destination address and a SIP channel. Barring duplciate IP addresses and other 
                // shenanigans this would mean we're on the same machine. Note this will also catch loopback to loopback cases.
                matchingChannel = GetSIPChannel(protocol, dst.Address);
                if (matchingChannel != null)
                {
                    return matchingChannel;
                }

                // Now we'll rely on the Operating Systems routing table to tell us which local IP address would be the one 
                // chosen to communicate with the destination. And then look for an exact match on a channel listening address.
                IPAddress srcAddr = NetServices.GetLocalAddressForRemote(dst.Address);
                if (srcAddr != null)
                {
                    matchingChannel = GetSIPChannel(protocol, srcAddr);
                    if (matchingChannel != null)
                    {
                        return matchingChannel;
                    }
                }

                // Now we're clutching at straws. Try the IP address the OS routing table tells us is used for accessing the Internet.
                IPAddress internetSrcAddr = NetServices.GetLocalAddressForInternet();
                if (internetSrcAddr != null)
                {
                    matchingChannel = GetSIPChannel(protocol, internetSrcAddr);
                    if (matchingChannel != null)
                    {
                        return matchingChannel;
                    }
                }

                // Hard to see how we could get to here. Maybe some weird IPv6 edge case or a network interface has gone down. Just return the first channel.
                return m_sipChannels.Where(x => x.Value.SIPProtocol == protocol && dst.Address.AddressFamily == x.Value.AddressFamily)
                    .Select(x => x.Value).First();
            }
        }

        /// <summary>
        /// Helper method for GetSIPChannelForDestination to do the SIP channel match check when it is known 
        /// exactly which SIP protocol and listening IP address we're after.
        /// </summary>
        /// <param name="protocol">The SIP protcol to find a match for.</param>
        /// <param name="reqdAddress">The listening IP address to find a match for.</param>
        /// <returns>A SIP channel if a match is found or null if not.</returns>
        private SIPChannel GetSIPChannel(SIPProtocolsEnum protocol, IPAddress listeningAddress)
        {
            if (m_sipChannels.Any(x => x.Value.SIPProtocol == protocol && listeningAddress.Equals(x.Value.ListeningIPAddress)))
            {
                return m_sipChannels.Where(x =>
                             x.Value.SIPProtocol == protocol && listeningAddress.Equals(x.Value.ListeningIPAddress))
                           .Select(x => x.Value)
                           .First();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Gets a list of this tranpsort's SIP channels.
        /// </summary>
        /// <returns>A list of SIP channels.</returns>
        public List<SIPChannel> GetSIPChannels()
        {
            return m_sipChannels.Select(x => x.Value).ToList();
        }

        /// <summary>
        /// Adds a SIP transaction to the transaction engine. Once in the engine the transport 
        /// layer is aware of it can can do things like match a CANCEL request to a UAS transaction.
        /// </summary>
        /// <param name="transaction">The transaction to start tracking.</param>
        public void AddTransaction(SIPTransaction transaction)
        {
            if (!m_transactionEngine.Exists(transaction.TransactionId))
            {
                m_transactionEngine.AddTransaction(transaction);
            }
        }

        /// <summary>
        /// Attempts to retrieve the transaction matching the supplied ID.
        /// </summary>
        /// <param name="transactionId">The transaction ID to match.</param>
        /// <returns>If found a transaction object or null if not.</returns>
        private SIPTransaction GetTransaction(string transactionId)
        {
            return m_transactionEngine?.GetTransaction(transactionId);
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

        #region Obsolete methods.

        [Obsolete("Set the Contact header using SIPContactHeader.GetDefaultSIPContactHeader and the Send methods will adjust based on the socket that gets used to send.", true)]
        public SIPEndPoint GetDefaultTransportContact(SIPProtocolsEnum protocol)
        {
            //SIPChannel defaultChannel = GetDefaultChannel(protocol);

            //if (defaultChannel != null)
            //{
            //    return defaultChannel.DefaultSIPChannelEndPoint;
            //}
            //else
            //{
            //    return null;
            //}

            throw new NotImplementedException();
        }

        /// <summary>
        /// Attempts to find the a matching SIP channel for the specified protocol. Preference is given to non-loopback
        /// channels.
        /// </summary>
        /// <returns>A SIP channel.</returns>
        [Obsolete("Set the Contact header using SIPContactHeader.GetDefaultSIPContactHeader and the Send methods will adjust based on the socket that gets used to send.", true)]
        public SIPEndPoint GetDefaultSIPEndPoint(SIPProtocolsEnum protocol)
        {
            throw new NotImplementedException();

            //if (m_sipChannels == null || m_sipChannels.Count == 0)
            //{
            //    throw new ApplicationException("No SIP channels available.");
            //}

            //var matchingChannels = m_sipChannels.Values.Where(x => x.SIPProtocol == protocol);

            //if (matchingChannels.Count() == 0)
            //{
            //    throw new ApplicationException($"The SIP transport layer does not have any SIP channels available for protocol {protocol}.");
            //}
            //else if (matchingChannels.Count() == 1)
            //{
            //    return matchingChannels.First().DefaultSIPChannelEndPoint;
            //}
            //else
            //{
            //    return matchingChannels.OrderBy(x => x.IsLoopbackAddress).First().DefaultSIPChannelEndPoint;
            //}
        }

        /// <summary>
        /// Attempts to locate the SIP channel that can communicate with the destination end point.
        /// </summary>
        /// <param name="destinationEP">The remote SIP end point to find a SIP channel for.</param>
        /// <returns>If successful the SIP end point of a SIP channel that can be used to communicate 
        /// with the destination end point.</returns>
        [Obsolete("Set the Contact header using SIPContactHeader.GetDefaultSIPContactHeader and the Send methods will adjust based on the socket that gets used to send.", true)]
        public SIPEndPoint GetDefaultSIPEndPoint(SIPEndPoint destinationEP)
        {
            throw new NotImplementedException();

            //if (m_sipChannels == null || m_sipChannels.Count == 0)
            //{
            //    return null;
            //}
            //else if (m_sipChannels.Count == 1)
            //{
            //    return m_sipChannels.First().Value.DefaultSIPChannelEndPoint;
            //}
            //else if (IPAddress.IsLoopback(destinationEP.Address))
            //{
            //    // If destination is a loopback IP address look for a protocol and IP protocol match.
            //    return m_sipChannels.Where(x => x.Value.SIPProtocol == destinationEP.Protocol &&
            //        (x.Value.IsLoopbackAddress
            //            || IPAddress.Equals(IPAddress.Any, x.Value.ListeningIPAddress)
            //            || IPAddress.Equals(IPAddress.IPv6Any, x.Value.ListeningIPAddress)) &&
            //        x.Value.AddressFamily == destinationEP.Address.AddressFamily)
            //        .Select(x => x.Value.DefaultSIPChannelEndPoint).FirstOrDefault();
            //}
            //else if (m_sipChannels.Count(x => x.Value.SIPProtocol == destinationEP.Protocol &&
            //    x.Value.AddressFamily == destinationEP.Address.AddressFamily &&
            //    !x.Value.IsLoopbackAddress) == 1)
            //{
            //    // If there is only one channel matching the required SIP protocol and IP protocol pair return it.
            //    return m_sipChannels.Where(x => x.Value.SIPProtocol == destinationEP.Protocol &&
            //        x.Value.AddressFamily == destinationEP.Address.AddressFamily &&
            //        !x.Value.IsLoopbackAddress).Select(x => x.Value.DefaultSIPChannelEndPoint).Single();
            //}
            //else
            //{
            //    //var localAddress = NetServices.GetLocalAddress(destinationEP.Address);

            //    //foreach (SIPChannel sipChannel in m_sipChannels.Values)
            //    //{
            //    //    if (sipChannel.SIPChannelEndPoint.Protocol == destinationEP.Protocol &&
            //    //        (localAddress == null || sipChannel.SIPChannelEndPoint.Address.Equals(localAddress)))
            //    //    {
            //    //        return sipChannel.SIPChannelEndPoint;
            //    //    }
            //    //}

            //    // Return the first matching end point for the destination end point's protocol.
            //    return m_sipChannels.Where(x => x.Value.SIPProtocol == destinationEP.Protocol).Select(y => y.Value).FirstOrDefault()?.DefaultSIPChannelEndPoint;
            //}
        }

        /// <summary>
        /// Attempts to match a SIPChannel that is listening on the specified local end point.
        /// </summary>
        /// <param name="localEndPoint">The local socket endpoint of the SIPChannel to find.</param>
        /// <returns>A matching SIPChannel if found otherwise null.</returns>
        [Obsolete("Set the Contact header using SIPContactHeader.GetDefaultSIPContactHeader and the Send methods will adjust based on the socket that gets used to send.", true)]
        public SIPChannel FindSIPChannel(SIPEndPoint localSIPEndPoint)
        {
            throw new NotImplementedException();

            //if (localSIPEndPoint == null)
            //{
            //    return null;
            //}
            //else
            //{
            //    if (localSIPEndPoint.ChannelID != null && m_sipChannels.ContainsKey(localSIPEndPoint.ChannelID))
            //    {
            //        return m_sipChannels[localSIPEndPoint.ChannelID];
            //    }
            //    else if (m_sipChannels.Values.Any(x => x.SIPProtocol == localSIPEndPoint.Protocol))
            //    {
            //        // No match on channel ID do fallback is to return the first available channel that matches the desired channel protocol.
            //        return m_sipChannels.Values.Where(x => x.SIPProtocol == localSIPEndPoint.Protocol).First();
            //    }
            //    else
            //    {
            //        logger.LogWarning($"No SIP channel could be found for local SIP end point {localSIPEndPoint}.");
            //        return null;
            //    }
            //}
        }

        [Obsolete("Doesn't work with IPAddress.Any.", true)]
        public bool IsLocalSIPEndPoint(SIPEndPoint sipEndPoint)
        {
            // Also need this check to accommodate cases where a channel is listening on IPAddress.Any.
            return m_sipChannels.ContainsKey(sipEndPoint.ToString());
        }

        [Obsolete("Use GetSIPChannels.", true)]
        public List<SIPEndPoint> GetListeningSIPEndPoints()
        {
            return m_sipChannels.Select(x => x.Value.ListeningSIPEndPoint).ToList();
        }

        /// <summary>
        /// Builds a very basic SIP request. In most cases additional headers will need to be added in order for it to be useful.
        /// When this method is called the channel used for sending the request has not been decided. The headers below depend on 
        /// the sending channel. By setting them to "0.0.0.0:0" the send request methods will substitute in the appropriate value
        /// at send time:
        /// - Top Via header.
        /// - From header.
        /// - Contact header.
        /// </summary>
        /// <param name="method">The method for the SIP request.</param>
        /// <param name="uri">The destination URI for the request.</param>
        /// <param name="to">The To header for the request.</param>
        /// <param name="from">The From header for the request.</param>
        /// <returns>A SIP request object.</returns>
        [Obsolete("This method has been moved to the SIPRequest class.", true)]
        public SIPRequest GetRequest(SIPMethodsEnum method, SIPURI uri, SIPToHeader to, SIPFromHeader from)
        {
            SIPRequest request = new SIPRequest(method, uri);

            SIPHeader header = new SIPHeader(from, to, 1, CallProperties.CreateNewCallId());
            request.Header = header;
            header.CSeqMethod = method;
            header.Allow = m_allowedSIPMethods;
            header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader());

            return request;
        }

        /// <summary>
        /// Builds a very basic SIP request. In most cases additional headers will need to be added in order for it to be useful.
        /// When this method is called the channel used for sending the request has not been decided. The headers below depend on 
        /// the sending channel. By setting them to "0.0.0.0:0" the send request methods will substitute in the appropriate value
        /// at send time:
        /// - Top Via header.
        /// - From header.
        /// - Contact header.
        /// </summary>
        /// <param name="method">The method for the SIP request.</param>
        /// <param name="uri">The destination URI for the request.</param>
        /// <returns>A SIP request object.</returns>
        [Obsolete("This method has been moved to the SIPRequest class.", true)]
        public SIPRequest GetRequest(SIPMethodsEnum method, SIPURI uri)
        {
            return GetRequest(
                method,
                uri,
                new SIPToHeader(null, new SIPURI(uri.User, uri.Host, null, uri.Scheme, SIPProtocolsEnum.udp), null),
                SIPFromHeader.GetDefaultSIPFromHeader(uri.Scheme));
        }

        /// <summary>
        /// Helper method to create a SIP response for a SIP request. This method can be thoght of as creating a 
        /// vanilla (or no frills) response for a request. It's suitable for generating error responses. For requests that
        /// require an action such as creating a call or registering a contact the response will require additional 
        /// information and this method will not be suitable.
        /// </summary>
        /// <param name="sipRequest">The SIP request to create the response for.</param>
        /// <param name="responseCode">The response code.</param>
        /// <param name="reasonPhrase">Optional reason phrase to set on the response (needs to be short).</param>
        /// <returns>A SIP response object.</returns>
        [Obsolete("This method has been moved to the SIPResponse class.", true)]
        public static SIPResponse GetResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode, reasonPhrase);
                response.SetSendFromHints(sipRequest.LocalSIPEndPoint);

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
                response.Header.Allow = m_allowedSIPMethods;

                return response;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport GetResponse. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Used to create a SIP response for a request when it was not possible to parse the incoming SIP request. 
        /// The response generated by this method may or may not make it back to the requester. Because the SIP 
        /// request could not be parsed there are no Via headers available and without those the return network
        /// path is missing. Instead a new Via header is generated that may get through if the requester is only
        /// one SIP hop away.
        /// </summary>
        /// <param name="localSIPEndPoint">The local SIP end point the request was received on.</param>
        /// <param name="remoteSIPEndPoint">The remote SIP end point the request was received on.</param>
        /// <param name="responseCode">The repsonse code to set on the response.</param>
        /// <param name="reasonPhrase">Optional reason phrase to set on the response (keep short).</param>
        [Obsolete("This method has been moved to the SIPResponse class.", true)]
        private SIPResponse GetResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteSIPEndPoint, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode, reasonPhrase);
                response.SetSendFromHints(localSIPEndPoint);
                SIPSchemesEnum sipScheme = (localSIPEndPoint.Protocol == SIPProtocolsEnum.tls) ? SIPSchemesEnum.sips : SIPSchemesEnum.sip;
                SIPFromHeader from = new SIPFromHeader(null, new SIPURI(sipScheme, localSIPEndPoint), null);
                SIPToHeader to = new SIPToHeader(null, new SIPURI(sipScheme, localSIPEndPoint), null);
                int cSeq = 1;
                string callId = CallProperties.CreateNewCallId();
                response.Header = new SIPHeader(from, to, cSeq, callId);
                response.Header.CSeqMethod = SIPMethodsEnum.NONE;
                response.Header.Vias.PushViaHeader(new SIPViaHeader(new SIPEndPoint(localSIPEndPoint.Protocol, remoteSIPEndPoint.GetIPEndPoint()), CallProperties.CreateBranchId()));
                response.Header.MaxForwards = Int32.MinValue;
                response.Header.Allow = m_allowedSIPMethods;

                return response;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransport GetResponse. " + excp.Message);
                throw;
            }
        }

        [Obsolete("Transaction can be created using its constructor.", true)]
        public SIPNonInviteTransaction CreateNonInviteTransaction(SIPRequest sipRequest, SIPEndPoint outboundProxy)
        {
            try
            {
                //CheckTransactionEngineExists();
                SIPNonInviteTransaction nonInviteTransaction = new SIPNonInviteTransaction(this, sipRequest, outboundProxy);
                m_transactionEngine.AddTransaction(nonInviteTransaction);
                return nonInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateNonInviteTransaction. " + excp.Message);
                throw;
            }
        }

        [Obsolete("Transaction can be created using its constructor.", true)]
        public UACInviteTransaction CreateUACTransaction(SIPRequest sipRequest, SIPEndPoint outboundProxy, bool sendOkAckManually = false, bool disablePrackSupport = false)
        {
            try
            {
                //CheckTransactionEngineExists();
                UACInviteTransaction uacInviteTransaction = new UACInviteTransaction(this, sipRequest, outboundProxy, sendOkAckManually);
                m_transactionEngine.AddTransaction(uacInviteTransaction);
                return uacInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateUACTransaction. " + excp.Message);
                throw;
            }
        }

        [Obsolete("Transaction can be created using its constructor.", true)]
        public UASInviteTransaction CreateUASTransaction(SIPRequest sipRequest, SIPEndPoint outboundProxy, bool noCDR = false)
        {
            try
            {
                //CheckTransactionEngineExists();
                UASInviteTransaction uasInviteTransaction = new UASInviteTransaction(this, sipRequest, outboundProxy, noCDR);
                m_transactionEngine.AddTransaction(uasInviteTransaction);
                return uasInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateUASTransaction. " + excp);
                throw;
            }
        }

        [Obsolete("Transaction can be created using its constructor.", true)]
        public SIPCancelTransaction CreateCancelTransaction(SIPRequest sipRequest, UASInviteTransaction inviteTransaction)
        {
            try
            {
                //CheckTransactionEngineExists();
                SIPCancelTransaction cancelTransaction = new SIPCancelTransaction(this, sipRequest, inviteTransaction);
                m_transactionEngine.AddTransaction(cancelTransaction);
                return cancelTransaction;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CreateCancelTransaction. " + excp);
                throw;
            }
        }

        #endregion
    }
}
