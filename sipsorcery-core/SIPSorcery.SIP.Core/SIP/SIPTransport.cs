 //-----------------------------------------------------------------------------
// Filename: SIPTransport.cs
//
// Description: SIP transport layer implementation. Handles different network
// transport options, retransmits, timeouts and transaction matching.
// 
// History:
// 14 Feb 2006	Aaron Clauson	Created.
// 26 Apr 2008  Aaron Clauson   Added TCP support.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{   
    /// <summary>
    /// Record number of each type of request received.
    /// </summary>
    public struct SIPTransportMetric
    {
        public const string PACKET_VOLUMES_KEY = "pkts";
        public const string SIPMETHOD_VOLUMES_KEY = "meth";
        public const string TOPTALKERS_VOLUME_KEY = "talk";
        
        public DateTime ReceivedAt;
        public IPEndPoint RemoteEndPoint;
        public SIPMessageTypesEnum SIPMessageType;
        public SIPMethodsEnum SIPMethod;
        public bool STUNRequest;
        public bool UnrecognisedPacket;
        public bool BadSIPMessage;                  // Set to true if the message appeared to be a SIP Message but then couldn't be parsed as one.
        public bool Discard;                        // If true indicates the SIP message was not parsed due to the receive queue being full and was instead discarded.
        public bool TooLarge;                       // If the message is greater than the max accepted length.
        public bool Originated;                     // If true inidcates the SIP message was sent by the transport layer, false means it was received.
        public double ParseDuration;                // Time it took to parse the message in milliseconds.

        public SIPTransportMetric(
            DateTime receivedAt, 
            IPEndPoint remoteEndPoint, 
            SIPMessageTypesEnum sipMessageType, 
            SIPMethodsEnum sipMethod, 
            bool stunRequest, 
            bool unrecognisedPacket, 
            bool badSIPMessage,
            bool discard, 
            bool tooLarge,
            bool originated,
            double parseDuration)
        {
            ReceivedAt = receivedAt;
            RemoteEndPoint = remoteEndPoint;
            SIPMessageType = sipMessageType;
            SIPMethod = sipMethod;
            STUNRequest = stunRequest;
            UnrecognisedPacket = unrecognisedPacket;
            BadSIPMessage = badSIPMessage;
            Discard = discard;
            TooLarge = tooLarge;
            Originated = originated;
            ParseDuration = parseDuration;
        }
    }

    public class SIPTransport
	{
        private const string THREAD_NAME = "siptransport";
        private const string METRICS_THREAD_NAME = "siptransport-metrics";
        private const int TIME_WAIT_FINALRESPONSE = 2000;           // Milliseconds to wait after transmitting the final request on a reliable transmission before timing out the request.
        private const int MAX_QUEUEWAIT_PERIOD = 2000;              // Maximum time to wait to check the message received queue if no events are received.
        private const int PENDINGREQUESTS_CHECK_PERIOD = 500;       // Time between checking the pending requests queue to resend reliable requests that have not been responded to.
        private const int MAX_INMESSAGE_QUEUECOUNT = 5000;          // The maximum number of messages that can be stored in the incoming message queue.
        private const int MAX_RELIABLETRANSMISSIONS_COUNT = 5000;   // The maximum number of messages that can be maintained for reliable transmissions.
        private const int MAX_MEASUREMENTSQUEUE_SIZE = 100000;       // If metrics are being used the maximum size the queue will be allowed to reach after which no more measurements will be accepted.
        private const int METRICS_SAMPLE_PERIOD = 60;                // Sample period in seconds for the metrics queue. 
        
        protected static readonly int m_t1 = SIPTimings.T1;         // SIP Timer T1 in milliseconds.
        protected static readonly int m_t6 = SIPTimings.T6;         // this x T1 is how long a reliable request will have retransmits performed for.
        private static string m_looseRouteParameter = SIPConstants.SIP_LOOSEROUTER_PARAMETER;

        private static ILog logger = AssemblyState.logger;
        private static ILog metricsLogger = AppState.GetLogger("siptransportmetrics");

        private bool m_queueIncoming = true;     // Dictates whether the transport later will queue incoming requests for processing on a separate thread of process immediately on the same thread.
                                                 // Most SIP elements with the exception of Stateless Proxies would typically want to queue incoming SIP messages.
        private bool m_useMetrics = false;       // Dictates whether measurements will be taken for top talkers, message parsing time and request methods.
        private Queue<SIPTransportMetric> m_sipTransportMeasurements = new Queue<SIPTransportMetric>();
        private bool m_metricsThreadStarted = false;

        private bool m_transportThreadStarted = false;
		private Queue<IncomingMessage> m_inMessageQueue = new Queue<IncomingMessage>();
		private ManualResetEvent m_inMessageArrived = new ManualResetEvent(false);
        private ManualResetEvent m_stopMetrics = new ManualResetEvent(false);
		private bool m_closed = false;

        private Dictionary<SIPEndPoint, SIPChannel> m_sipChannels = new Dictionary<SIPEndPoint, SIPChannel>();    // List of the physical channels that have been opened and are under management by this instance.
        //private List<SIPEndPoint> m_sipLocalEndPoints = new List<SIPEndPoint>();

        private SIPTransactionEngine m_transactionEngine;

        public event SIPTransportRequestDelegate SIPTransportRequestReceived;
        public event SIPTransportResponseDelegate SIPTransportResponseReceived;
        public event STUNRequestReceivedDelegate STUNRequestReceived;
        private ResolveSIPEndPointDelegate ResolveSIPEndPoint_External;

        #region Logging and metrics.

        public event SIPTransportRequestDelegate SIPRequestInTraceEvent;
        public event SIPTransportRequestDelegate SIPRequestOutTraceEvent;
        public event SIPTransportResponseDelegate SIPResponseInTraceEvent;
        public event SIPTransportResponseDelegate SIPResponseOutTraceEvent;
        public event SIPTransportSIPBadRequestMessageDelegate SIPBadRequestInTraceEvent;
        public event SIPTransportSIPBadResponseMessageDelegate SIPBadResponseInTraceEvent;
        public event UnrecognisedMessageReceivedDelegate UnrecognisedMessageReceived;

        #endregion

        // Contains a list of the SIP Requests/Response that are being monitored or responses and retransmitted on when none is recieved to attempt a more reliable delivery
        // rather then just relying on the initial request to get through.
        private Dictionary<string, SIPTransaction> m_reliableTransmissions = new Dictionary<string, SIPTransaction>();
        private bool m_reliablesThreadRunning = false;   // Only gets started when a request is made to send a reliable request.

        public int ReliableTrasmissionsCount
        {
            get { return m_reliableTransmissions.Count; }
        }

        public SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine)
        {
            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
        }

        public SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine, bool queueIncoming, bool useMetrics)
        {
            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
            m_queueIncoming = queueIncoming;
            m_useMetrics = useMetrics;
        }

        public SIPTransport(ResolveSIPEndPointDelegate sipResolver, SIPTransactionEngine transactionEngine, SIPChannel sipChannel, bool queueIncoming, bool useMetrics)
		{
            ResolveSIPEndPoint_External = sipResolver;
            m_transactionEngine = transactionEngine;
            AddSIPChannel(sipChannel);

            m_queueIncoming = queueIncoming;
            m_useMetrics = useMetrics;

            if (m_queueIncoming)
            {
                StartTransportThread();
            }

            if (m_useMetrics)
            {
                StartMetricsThread();
            }
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
                m_sipChannels.Add(sipChannel.SIPChannelEndPoint, sipChannel);

                // Wire up the SIP transport to the SIP channel.
                sipChannel.SIPMessageReceived += ReceiveMessage;

                if (m_queueIncoming && !m_transportThreadStarted)
                {
                    StartTransportThread();
                }

                if (m_useMetrics && !m_metricsThreadStarted)
                {
                    StartMetricsThread();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AddSIPChannel. " + excp.Message);
                throw excp;
            }
        }

        private void StartTransportThread()
        {
            if (!m_transportThreadStarted)
            {
                m_transportThreadStarted = true;
                
                Thread inMessageThread = new Thread(new ThreadStart(ProcessInMessage));
                //inMessageThread.Priority = ThreadPriority.AboveNormal;
                inMessageThread.Name = THREAD_NAME;
                inMessageThread.Start();
            }
        }

        private void StartMetricsThread()
        {
            if (!m_metricsThreadStarted)
            {
                m_metricsThreadStarted = true;

                Thread metricsThread = new Thread(new ThreadStart(ProcessMetrics));
                metricsThread.Name = METRICS_THREAD_NAME;
                metricsThread.Start();
            }
        }

        private void StartReliableTransmissionsThread()
        {
            m_reliablesThreadRunning = true;

            Thread reliableTransmissionsThread = new Thread(new ThreadStart(ProcessPendingReliableTransactions));
            reliableTransmissionsThread.Name = "siptransport-reliable";
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
                       logger.Warn("SIPTransport queue full new message from " + remoteEndPoint + " being discarded.");

                       if (m_useMetrics)
                       {
                           RecordDiscardMetric(remoteEndPoint);
                       }

                       //while (m_inMessageQueue.Count >= MAX_INMESSAGE_QUEUECOUNT)
                       //{
                       //    m_inMessageQueue.Dequeue();
                       //}
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
			catch(Exception excp)
			{
				logger.Error("Exception SIPTransport ReceiveMessage. " + excp.Message);
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
				m_inMessageArrived.Set();

                m_stopMetrics.Set();

                logger.Debug("SIPTransport Shutdown Complete.");
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPTransport Shutdown. " + excp.Message);
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

        public SIPEndPoint GetDefaultSIPEndPoint()
        {
            foreach (SIPChannel sipChannel in m_sipChannels.Values)
            {
                if (sipChannel.SIPChannelEndPoint.SIPProtocol == SIPProtocolsEnum.udp)
                {
                    return sipChannel.SIPChannelEndPoint;
                }
            }

            return m_sipChannels.First().Value.SIPChannelEndPoint;
        }

        public SIPEndPoint GetDefaultSIPEndPoint(SIPProtocolsEnum protocol)
        {
            foreach (SIPChannel sipChannel in m_sipChannels.Values)
            {
                if (sipChannel.SIPChannelEndPoint.SIPProtocol == protocol)
                {
                    return sipChannel.SIPChannelEndPoint;
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
        public void SendRaw(SIPEndPoint localSIPEndPoint, SIPEndPoint destinationEndPoint, byte[] buffer)
        {
            SIPChannel sendSIPChannel = FindSIPChannel(localSIPEndPoint);
            if (sendSIPChannel != null)
            {
                sendSIPChannel.Send(destinationEndPoint.SocketEndPoint, buffer);
            }
            else
            {
                logger.Warn("No SIPChannel could be found for " + localSIPEndPoint + " in SIPTransport.SendRaw.");
            }
        }

        public void SendRequest(SIPRequest sipRequest)
        {
            SIPEndPoint requestEndPoint = GetRequestEndPoint(sipRequest, true);

            if (requestEndPoint != null)
            {
                SendRequest(requestEndPoint, sipRequest);
            }
            else
            {
                throw new ApplicationException("SIP Transport could not send request as end point could not be determined.\r\n" + sipRequest.ToString());
            }
        }

        public void SendRequest(SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
            SIPChannel sipChannel = null;

            if (sipRequest.LocalSIPEndPoint != null)
            {
                sipChannel = FindSIPChannel(sipRequest.LocalSIPEndPoint);
                sipChannel = sipChannel ?? GetDefaultChannel(sipRequest.LocalSIPEndPoint.SIPProtocol);
            }
            else
            {
                sipChannel = GetDefaultChannel(dstEndPoint.SIPProtocol);
            }

            if (sipChannel != null)
            {
                SendRequest(sipChannel, dstEndPoint, sipRequest);
            }
            else
            {
                throw new ApplicationException("A default SIP channel could not be found for protocol " + sipRequest.LocalSIPEndPoint.SIPProtocol + " when sending SIP request.");
            }
        }

        private void SendRequest(SIPChannel sipChannel, SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
            if (sipChannel.IsTLS) {
                sipChannel.Send(dstEndPoint.SocketEndPoint, Encoding.UTF8.GetBytes(sipRequest.ToString()), sipRequest.URI.Host);
            }
            else {
                sipChannel.Send(dstEndPoint.SocketEndPoint, Encoding.UTF8.GetBytes(sipRequest.ToString()));
            }

            if (m_useMetrics)
            {
                RecordSIPMessageSendMetric(dstEndPoint, SIPMessageTypesEnum.Request, sipRequest.Method);
            }

            if (SIPRequestOutTraceEvent != null)
            {
                FireSIPRequestOutTraceEvent(sipChannel.SIPChannelEndPoint, dstEndPoint, sipRequest);
            }
        }

        /// <summary>
        /// Sends a SIP request/response and keeps track of whether a response/acknowledgement has been received. If no response is received then periodic retransmits are made
        /// for up to T1 x 64 seconds.
        /// </summary>
        public void SendSIPReliable(SIPTransaction sipTransaction)
        {
            if (m_reliableTransmissions.Count >= MAX_RELIABLETRANSMISSIONS_COUNT)
            {
                throw new ApplicationException("Cannot send reliable SIP message as the reliable transmissions queue is full.");
            }

           //logger.Debug("SendSIPReliable transaction URI " + sipTransaction.TransactionRequest.URI.ToString() + ".");

            if (sipTransaction.TransactionType == SIPTransactionTypesEnum.Invite &&
                sipTransaction.TransactionState == SIPTransactionStatesEnum.Completed)
            {
                // This is an INVITE transaction that wants to send a reliable response.
                if (sipTransaction.LocalSIPEndPoint == null)
                {
                    throw new ApplicationException("The SIPTransport layer cannot send a reliable SIP response because the send from socket has not been set for the transaction.");
                }
                else
                {
                    SIPViaHeader topViaHeader = sipTransaction.TransactionFinalResponse.Header.Vias.TopViaHeader;
                    SendResponse(sipTransaction.TransactionFinalResponse);
                }
            }
            else
            {
                if (sipTransaction.RemoteEndPoint == null)
                {
                    SIPEndPoint resolvedEndPoint = GetRequestEndPoint(sipTransaction.TransactionRequest, true);
                    if (resolvedEndPoint != null)
                    {
                        sipTransaction.RemoteEndPoint = resolvedEndPoint;
                        SendRequest(sipTransaction.RemoteEndPoint, sipTransaction.TransactionRequest);
                    }
                    else
                    {
                        throw new ApplicationException("SIP Transport could not send request as end point could not be determined.\r\n" + sipTransaction.TransactionRequest.ToString());
                    }
                }
                else
                {
                    SendRequest(sipTransaction.RemoteEndPoint, sipTransaction.TransactionRequest);
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

        public void SendResponse(SIPResponse sipResponse)
        {
            //SIPChannel sipChannel = GetChannelForSocketId(sipResponse.SocketId);
            SIPViaHeader topViaHeader = sipResponse.Header.Vias.TopViaHeader;
            SIPChannel sipChannel = FindSIPChannel(sipResponse.LocalSIPEndPoint);
            sipChannel = sipChannel ?? GetDefaultChannel(topViaHeader.Transport);

            if (sipChannel != null)
            {
                SendResponse(sipChannel, sipResponse);
            }
            else
            {
                logger.Warn("Could not find channel to send SIP Response in SendResponse.");
            }
        }

        private void SendResponse(SIPChannel sipChannel, SIPResponse sipResponse)
        {
            SIPViaHeader topVia = sipResponse.Header.Vias.TopViaHeader;
            SIPEndPoint dstEndPoint = GetHostEndPoint(topVia.ReceivedFromAddress, true);
            dstEndPoint.SIPProtocol = topVia.Transport;
            SendResponse(sipChannel, dstEndPoint, sipResponse);
        }

        private void SendResponse(SIPChannel sipChannel, SIPEndPoint dstEndPoint, SIPResponse sipResponse)
        {
            sipChannel.Send(dstEndPoint.SocketEndPoint, Encoding.UTF8.GetBytes(sipResponse.ToString()));

            if (m_useMetrics)
            {
                RecordSIPMessageSendMetric(dstEndPoint, SIPMessageTypesEnum.Response, sipResponse.Header.CSeqMethod);
            }

            if (SIPRequestOutTraceEvent != null)
            {
                FireSIPResponseOutTraceEvent(sipChannel.SIPChannelEndPoint, dstEndPoint, sipResponse);
            }
        }

		private void ProcessInMessage()
		{
			try
			{			
				while(!m_closed)
				{
                    m_transactionEngine.RemoveExpiredTransactions();
                    
                    while(m_inMessageQueue.Count > 0)
					{
						IncomingMessage incomingMessage = null;
                        						
						lock(m_inMessageQueue)
						{
							incomingMessage = m_inMessageQueue.Dequeue();
						}

						if(incomingMessage != null)
						{
                            SIPMessageReceived(incomingMessage.LocalSIPChannel, incomingMessage.RemoteEndPoint, incomingMessage.Buffer);
						}
					}

					m_inMessageArrived.Reset();
					//m_inMessageArrived.WaitOne(MAX_QUEUEWAIT_PERIOD, false);
                    m_inMessageArrived.WaitOne(MAX_QUEUEWAIT_PERIOD);
				}
			}
			catch(Exception excp)
			{
				logger.Error("Exception SIPTransport ProcessInMessage. " + excp.Message);
			}
		}

        private void ProcessMetrics()
        {
            try
            {
                logger.Debug("SIPTransport ProcessMetrics thread started.");
                
                while (!m_closed)
                {
                    string sampleTimeString = DateTime.Now.ToString("dd MMM yyyy HH:mm:ss");
                    
                    if (m_sipTransportMeasurements.Count > 0)
                    {
                        lock (m_sipTransportMeasurements)
                        {
                            // Remove all samples older than current sample period.
                            while (m_sipTransportMeasurements.Count > 0 && DateTime.Now.Subtract(m_sipTransportMeasurements.Peek().ReceivedAt).TotalSeconds > METRICS_SAMPLE_PERIOD)
                            {
                                m_sipTransportMeasurements.Dequeue();
                            }

                            if (m_sipTransportMeasurements.Count > 0)
                            {
                                #region Collate samples into single measurement.

                                // Take sample.
                                DateTime startSampleDate = DateTime.Now.AddSeconds(-1 * METRICS_SAMPLE_PERIOD);
                                DateTime endSampleDate = DateTime.Now;
                                int totalPackets = 0;
                                double totalParsedPackets = 0;
                                double totalParseTime = 0;
                                int sipMessageCount = 0;
                                int sipRequestCount = 0;
                                int sipResponseCount = 0;
                                int sipRequestSentCount = 0;
                                int sipResponseSentCount = 0;
                                int discardsCount = 0;
                                int badSIPCount = 0;
                                int tooLargeCount = 0;
                                int stunRequestsCount = 0;
                                int unrecognisedCount = 0;
                                Dictionary<string, int> topTalkers = new Dictionary<string, int>();
                                Dictionary<SIPMethodsEnum, int> sipMessageTypes = new Dictionary<SIPMethodsEnum, int>();

                                SIPTransportMetric[] measurements = m_sipTransportMeasurements.ToArray();
                                foreach (SIPTransportMetric measurement in measurements)
                                {
                                    totalPackets++;

                                    if (measurement.RemoteEndPoint != null)
                                    {
                                        string talker = measurement.RemoteEndPoint.ToString();
                                        if(topTalkers.ContainsKey(talker))
                                        {
                                            topTalkers[talker] = topTalkers[talker] + 1;
                                        }
                                        else
                                        {
                                            topTalkers.Add(talker, 1);
                                        }
                                    }

                                    if (!measurement.Originated && !measurement.Discard)
                                    {
                                        totalParsedPackets++;
                                        totalParseTime += measurement.ParseDuration;
                                    }

                                    if (measurement.Discard)
                                    {
                                        discardsCount++;
                                    }
                                    else if (measurement.BadSIPMessage)
                                    {
                                        badSIPCount++;
                                    }
                                    else if (measurement.UnrecognisedPacket)
                                    {
                                        unrecognisedCount++;
                                    }
                                    else if (measurement.STUNRequest)
                                    {
                                        stunRequestsCount++;
                                    }
                                    else if (measurement.TooLarge)
                                    {
                                        tooLargeCount++;
                                    }
                                    else
                                    {
                                        sipMessageCount++;

                                        if (sipMessageTypes.ContainsKey(measurement.SIPMethod))
                                        {
                                            sipMessageTypes[measurement.SIPMethod] = sipMessageTypes[measurement.SIPMethod] + 1;
                                        }
                                        else
                                        {
                                            sipMessageTypes.Add(measurement.SIPMethod, 1);
                                        }

                                        if (measurement.SIPMessageType == SIPMessageTypesEnum.Request)
                                        {
                                            if (measurement.Originated)
                                            {
                                                sipRequestSentCount++;
                                            }
                                            else
                                            {
                                                sipRequestCount++;
                                            }
                                        }
                                        else
                                        {
                                            if (measurement.Originated)
                                            {
                                                sipResponseSentCount++;
                                            }
                                            else
                                            {
                                                sipResponseCount++;
                                            }
                                        }
                                    }
                                }

                                #endregion
                                
                                double avgParseTime = 0;
                                if (totalParsedPackets > 0)
                                {
                                    avgParseTime = totalParseTime / totalParsedPackets;
                                }

                                metricsLogger.Info(
                                    SIPTransportMetric.PACKET_VOLUMES_KEY + "," +
                                    sampleTimeString + "," +
                                    METRICS_SAMPLE_PERIOD + "," +
                                    totalPackets + "," +
                                    sipRequestCount + "," +
                                    sipResponseCount + "," +
                                    sipRequestSentCount + "," +
                                    sipResponseSentCount + "," +
                                    //SIPTransaction.Count + "," +
                                    unrecognisedCount + "," +
                                    badSIPCount + "," +
                                    stunRequestsCount + "," +
                                    discardsCount + "," +
                                    tooLargeCount + "," +
                                    totalParseTime.ToString("0.###") + "," +
                                    avgParseTime.ToString("0.###"));

                                #region Build SIP methods metric string.

                                if (sipMessageTypes.Count > 0)
                                {
                                    string methodCountStr = SIPTransportMetric.SIPMETHOD_VOLUMES_KEY + "," + sampleTimeString  + "," + METRICS_SAMPLE_PERIOD;
                                    foreach (KeyValuePair<SIPMethodsEnum, int> methodCount in sipMessageTypes)
                                    {
                                        methodCountStr += "," + methodCount.Key + "=" + methodCount.Value;
                                    }
                                    metricsLogger.Info(methodCountStr);
                                }
                                else
                                {
                                    metricsLogger.Info(SIPTransportMetric.SIPMETHOD_VOLUMES_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD);
                                }

                                #endregion

                                #region Build top talkers metric string.

                                if (topTalkers.Count > 0)
                                {
                                    string topTalkersStr = SIPTransportMetric.TOPTALKERS_VOLUME_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD;

                                    for (int index = 0; index < 10; index++)
                                    {
                                        if (topTalkers.Count == 0)
                                        {
                                            break;
                                        }

                                        string curTopTalker = null;
                                        int curTopTalkerCount = 0;
                                        foreach (KeyValuePair<string, int> topTalker in topTalkers)
                                        {
                                            if (topTalker.Value > curTopTalkerCount)
                                            {
                                                curTopTalker = topTalker.Key;
                                                curTopTalkerCount = topTalker.Value;
                                            }
                                        }
                                        topTalkersStr += "," + curTopTalker + "=" + curTopTalkerCount;
                                        topTalkers.Remove(curTopTalker);
                                    }
                                    metricsLogger.Info(topTalkersStr);
                                }
                                else
                                {
                                    metricsLogger.Info(SIPTransportMetric.TOPTALKERS_VOLUME_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD);
                                }

                                #endregion
                            }
                            else
                            {
                                //metricsLogger.Info(SIPTransportMetric.PACKET_VOLUMES_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD + ",0,0,0,0,0,0," + SIPTransaction.Count + ",0,0,0,0,0,0");
                                metricsLogger.Info(SIPTransportMetric.SIPMETHOD_VOLUMES_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD);
                                metricsLogger.Info(SIPTransportMetric.TOPTALKERS_VOLUME_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD);
                            }
                        }
                    }
                    else
                    {
                       // metricsLogger.Info(SIPTransportMetric.PACKET_VOLUMES_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD + ",0,0,0,0,0,0," + SIPTransaction.Count + ",0,0,0,0,0,0");
                        metricsLogger.Info(SIPTransportMetric.SIPMETHOD_VOLUMES_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD);
                        metricsLogger.Info(SIPTransportMetric.TOPTALKERS_VOLUME_KEY + "," + sampleTimeString + "," + METRICS_SAMPLE_PERIOD);
                    }

                    //m_stopMetrics.WaitOne(METRICS_SAMPLE_PERIOD * 1000, false);
                    m_stopMetrics.WaitOne(METRICS_SAMPLE_PERIOD * 1000);
                }

                logger.Debug("SIPTransport ProcessMetrics thread stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTransport ProcessMetrics. " + excp.Message);
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
                        List<string> deliveredTransactions = new List<string>();

                        lock (m_reliableTransmissions)
                        {
                            foreach (SIPTransaction transaction in m_reliableTransmissions.Values)
                            {
                                if (!transaction.DeliveryPending)
                                {
                                    deliveredTransactions.Add(transaction.TransactionId);
                                }
                                else if(transaction.TransactionState == SIPTransactionStatesEnum.Terminated || 
                                        transaction.TransactionState == SIPTransactionStatesEnum.Confirmed ||
                                        transaction.TransactionState == SIPTransactionStatesEnum.Cancelled || 
                                        transaction.HasTimedOut)
                                {
                                    deliveredTransactions.Add(transaction.TransactionId);
                                }
                                else
                                {
                                    if (DateTime.Now.Subtract(transaction.InitialTransmit).TotalMilliseconds > (m_t6 * 2 + TIME_WAIT_FINALRESPONSE))
                                    {
                                        logger.Debug("Request timed out " + transaction.TransactionRequest.Method + " " + transaction.TransactionRequest.URI.ToString() + ".");

                                        // Transaction timeout event will be fired by the transaction class so do not fire here.
                                        transaction.DeliveryFailed = true;
                                        deliveredTransactions.Add(transaction.TransactionId);
                                    }
                                    else
                                    {
                                        double nextTransmitMilliseconds = Math.Pow(2, transaction.Retransmits - 1) * m_t1;
                                        //logger.Debug("Time since retransmit " + transaction .RequestTransmits + " for " + transaction.InitialRequest.Method + " " + transaction.InitialRequest.URI.ToString() + " " + DateTime.Now.Subtract(transaction.LastRequestTransmit).TotalMilliseconds + ".");

                                        if (DateTime.Now.Subtract(transaction.LastTransmit).TotalMilliseconds >= nextTransmitMilliseconds)
                                        {
                                            transaction.Retransmits = transaction.Retransmits + 1;
                                            transaction.LastTransmit = DateTime.Now;

                                            if (transaction.TransactionType == SIPTransactionTypesEnum.Invite && transaction.TransactionState == SIPTransactionStatesEnum.Completed)
                                            {
                                                //logger.Debug("Retransmit " + transaction.Retransmits + "(" + transaction.TransactionId + ") for INVITE reponse " + transaction.TransactionRequest.URI.ToString() + ", last=" + DateTime.Now.Subtract(transaction.LastTransmit).TotalMilliseconds + "ms, first=" + DateTime.Now.Subtract(transaction.InitialTransmit).TotalMilliseconds + "ms.");

                                                // This is an INVITE transaction that wants to send a reliable response, once the ACK is received it will change the transaction state to confirmed.
                                                SIPViaHeader topViaHeader = transaction.TransactionFinalResponse.Header.Vias.TopViaHeader;
                                                SendResponse(transaction.TransactionFinalResponse);
                                                transaction.ResponseRetransmit();
                                            }
                                            else
                                            {
                                                //logger.Debug("Retransmit " + transaction.Retransmits + " for request " + transaction.TransactionRequest.Method + " " + transaction.TransactionRequest.URI.ToString() + ", last=" + DateTime.Now.Subtract(transaction.LastTransmit).TotalMilliseconds + "ms, first=" + DateTime.Now.Subtract(transaction.InitialTransmit).TotalMilliseconds + "ms.");
                                                SendRequest(transaction.RemoteEndPoint, transaction.TransactionRequest);
                                                transaction.RequestRetransmit();
                                            }
                                        }
                                    }
                                }
                            }

                            // Remove timed out or complete transactions from reliable transmissions list.
                            if (deliveredTransactions.Count > 0)
                            {
                                foreach (string transactionId in deliveredTransactions)
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
                        logger.Error("Exception SIPTransport ProcessPendingRequests checking pendings. " + excp.Message);
                    }

                    Thread.Sleep(PENDINGREQUESTS_CHECK_PERIOD);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTransport ProcessPendingRequests. " + excp.Message);
            }
            finally
            {
                m_reliablesThreadRunning = false;
            }
        }
	
		private void SIPMessageReceived(SIPChannel sipChannel, SIPEndPoint remoteEndPoint, byte[] buffer)
		{
            string erroneousSIPMessage = null;

			try
			{
                DateTime startParseTime = DateTime.Now;

                if (buffer != null && buffer.Length > 0)
                {
                    if ((buffer[0] == 0x0 || buffer[0] == 0x1) && buffer.Length >= 20)
                    {                       
                        // Treat any messages that cannot be SIP as STUN requests.
                        if (STUNRequestReceived != null)
                        {
                            STUNRequestReceived(sipChannel.SIPChannelEndPoint.SocketEndPoint, remoteEndPoint.SocketEndPoint, buffer, buffer.Length);
                        }

                        if (m_useMetrics)
                        {
                            RecordSTUNMessageMetric(remoteEndPoint.SocketEndPoint, startParseTime);
                        }
                    }
                    else
                    {
                        // Treat all messages that don't match STUN requests as SIP.
                        if (buffer.Length > SIPConstants.SIP_MAXIMUM_LENGTH)
                        {
                            FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "SIP message too large.", SIPValidationFieldsEnum.Request);
                            SIPResponse tooLargeResponse = GetResponse(sipChannel.SIPChannelEndPoint, remoteEndPoint, SIPResponseStatusCodesEnum.MessageTooLarge, null);
                            SendResponse(tooLargeResponse);

                            if (m_useMetrics)
                            {
                                RecordTooLargeMessageMetric(remoteEndPoint, startParseTime);
                            }
                        }
                        else
                        {
                            erroneousSIPMessage = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                            SIPMessage sipMessage = SIPMessage.ParseSIPMessage(buffer, sipChannel.SIPChannelEndPoint, remoteEndPoint);

                            if (sipMessage != null)
                            {
                                if (sipMessage.SIPMessageType == SIPMessageTypesEnum.Response)
                                {
                                    #region SIP Response.

                                    try
                                    {
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

                                        if (m_useMetrics)
                                        {
                                            RecordSIPMessageMetric(remoteEndPoint, SIPMessageTypesEnum.Response, sipResponse.Header.CSeqMethod, startParseTime);
                                        }
                                    }
                                    catch (SIPValidationException sipValidationException)
                                    {
                                        //logger.Warn("Invalid SIP response from " + sipMessage.ReceivedFrom + ", " + sipResponse.ValidationError + " , ignoring.");
                                        //logger.Warn(sipMessage.RawMessage);
                                        FireSIPBadResponseInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipMessage.RawMessage, sipValidationException.SIPErrorField);

                                        if (m_useMetrics)
                                        {
                                            RecordBadSIPMessageMetric(remoteEndPoint, startParseTime);
                                        }
                                    }

                                    #endregion

                                }
                                else
                                {
                                    #region SIP Request.

                                    try
                                    {
                                        SIPRequest sipRequest = SIPRequest.ParseSIPRequest(sipMessage);

                                        if (SIPRequestInTraceEvent != null)
                                        {
                                            FireSIPRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequest);
                                        }

                                        // Stateful cores will create transactions once they get the request and the transport layer will use those transactions.
                                        // Stateless cores will not be affected by this step as the transaction layer will always return false.
                                        SIPTransaction requestTransaction = (m_transactionEngine != null) ? m_transactionEngine.GetTransaction(sipRequest) : null;
                                        if (requestTransaction != null)
                                        {
                                            if (requestTransaction.TransactionState == SIPTransactionStatesEnum.Completed && sipRequest.Method != SIPMethodsEnum.ACK)
                                            {
                                                //logger.Debug("Resending final response for " + sipRequest.Method + " " + sipRequest.URI.ToString() + " from " + IPSocket.GetSocketString(remoteEndPoint) + ".");

                                                SIPResponse finalResponse = requestTransaction.TransactionFinalResponse;
                                                SendResponse(finalResponse);
                                                requestTransaction.Retransmits += 1;
                                                requestTransaction.LastTransmit = DateTime.Now;
                                                requestTransaction.ResponseRetransmit();
                                            }
                                            else if (sipRequest.Method == SIPMethodsEnum.ACK)
                                            {
                                                //logger.Debug("ACK received for (" + requestTransaction.TransactionId + ") " + requestTransaction.TransactionRequest.URI.ToString() + ", callid=" + sipRequest.Header.CallId + ".");

                                                if (requestTransaction.TransactionState == SIPTransactionStatesEnum.Completed)
                                                {
                                                    //logger.Debug("ACK received for INVITE, setting state to Confirmed, " + sipRequest.URI.ToString() + " from " + sipRequest.Header.From.FromURI.User + " " + remoteEndPoint + ".");
                                                    //requestTransaction.UpdateTransactionState(SIPTransactionStatesEnum.Confirmed);
                                                    requestTransaction.ACKReceived(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequest);
                                                }
                                                else if (requestTransaction.TransactionState == SIPTransactionStatesEnum.Confirmed)
                                                {
                                                    // ACK retransmit, ignore as a previous ACK was received and the transaction has already been confirmed.
                                                }
                                                else
                                                {
                                                    //logger.Debug("ACK recieved on " + requestTransaction.TransactionState + " transaction ignoring.");
                                                    FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "ACK recieved on " + requestTransaction.TransactionState + " transaction ignoring.", SIPValidationFieldsEnum.Request);
                                                }
                                            }
                                            else
                                            {
                                                //logger.Debug("Transaction already exists, ignoring duplicate request, " + sipRequest.Method + " " + sipRequest.URI.ToString() + " from " + sipRequest.Header.From.FromURI.User + " " + IPSocket.GetSocketString(remoteEndPoint) + ".");

                                                FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "Transaction already exists, ignoring duplicate request, " + sipRequest.Method + " " + sipRequest.URI.ToString() + " from " + remoteEndPoint + ".", SIPValidationFieldsEnum.Request);
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
                                                //logger.Warn("SIPTransport responding with TooManyHops due to 0 MaxForwards.");
                                                FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, "Zero MaxForwards on " + sipRequest.Method + " " + sipRequest.URI.ToString() + " from " + sipRequest.Header.From.FromURI.User + " " + remoteEndPoint.ToString(), SIPValidationFieldsEnum.Request);
                                                SIPResponse tooManyHops = GetResponse(sipRequest, SIPResponseStatusCodesEnum.TooManyHops, null);
                                                SendResponse(sipChannel, tooManyHops);
                                                return;
                                            }
                                            /*else if (sipRequest.IsLoop(sipChannel.SIPChannelEndPoint.SocketEndPoint.Address.ToString(), sipChannel.SIPChannelEndPoint.SocketEndPoint.Port, sipRequest.CreateBranchId()))
                                            {
                                                //logger.Warn("SIPTransport Dropping looped request.");
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
                                            //SIPViaHeader originalTopViaHeader = sipRequest.Header.Via.TopViaHeader;
                                            sipRequest.Header.Vias.UpateTopViaHeader(remoteEndPoint.SocketEndPoint);

                                            // Stateful cores should create a transaction once they receive this event, stateless cores should not.
                                            SIPTransportRequestReceived(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequest);
                                        }

                                        if (m_useMetrics)
                                        {
                                            RecordSIPMessageMetric(remoteEndPoint, SIPMessageTypesEnum.Request, sipRequest.Header.CSeqMethod, startParseTime);
                                        }
                                    }
                                    catch (SIPValidationException sipRequestExcp)
                                    {
                                        FireSIPBadRequestInTraceEvent(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipMessage.RawMessage, sipRequestExcp.SIPErrorField);
                                        SIPResponse errorResponse = GetResponse(sipChannel.SIPChannelEndPoint, remoteEndPoint, sipRequestExcp.SIPResponseErrorCode, sipRequestExcp.Message);
                                        SendResponse(sipChannel, errorResponse);

                                        if (m_useMetrics)
                                        {
                                            RecordBadSIPMessageMetric(remoteEndPoint, startParseTime);
                                        }
                                    }

                                    #endregion
                                }
                            }
                            else
                            {
                                if (UnrecognisedMessageReceived != null)
                                {
                                    UnrecognisedMessageReceived(sipChannel.SIPChannelEndPoint, remoteEndPoint, buffer);
                                }

                                if (m_useMetrics)
                                {
                                    RecordUnrecognisedMessageMetric(remoteEndPoint, startParseTime);
                                }
                            }
                        }
                    }
                }
			}
			catch(Exception excp)
			{
                logger.Error("Exception SIPTransport SIPMessageReceived. " + excp.Message + "\r\n" + erroneousSIPMessage);
				//throw excp;
			}
		}

        /// <summary>
        /// Checks the Contact SIP URI host and if it is recognised as a private address it is replaced with the socket
        /// the SIP message was received on.
        /// 
        /// Private address space blocks RFC 1597.
        ///		10.0.0.0        -   10.255.255.255
        ///		172.16.0.0      -   172.31.255.255
        ///		192.168.0.0     -   192.168.255.255
        ///
        /// </summary>
        public static bool IsPrivateAddress(string host)
        {
            if (host != null && host.Trim().Length > 0)
            {
                if (host.StartsWith("127.0.0.1") ||
                    host.StartsWith("10.") ||
                    Regex.Match(host, @"^172\.2\d\.").Success ||
                    host.StartsWith("172.30.") ||
                    host.StartsWith("172.31.") ||
                    host.StartsWith("192.168."))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                logger.Error("Cannot check private address against an empty host.");
                return false;
            }
        }

        public static SIPResponse GetResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode, reasonPhrase, sipRequest.LocalSIPEndPoint);

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
                response.Header.CSeqMethod = (requestHeader != null ) ? requestHeader.CSeqMethod : SIPMethodsEnum.NONE;

                if (requestHeader == null || requestHeader.Vias == null || requestHeader.Vias.Length == 0)
                {
                    response.Header.Vias.PushViaHeader(new SIPViaHeader(sipRequest.RemoteSIPEndPoint, CallProperties.CreateBranchId()));
                }
                else
                {
                    response.Header.Vias = requestHeader.Vias;
                }

                response.Header.MaxForwards = Int32.MinValue;

                return response;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTransport GetResponse. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Used to create a SIP response when it was not possible to parse the incoming SIP request.
        /// </summary>
        public static SIPResponse GetResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponseStatusCodesEnum responseCode, string reasonPhrase)
        {
            try
            {
                SIPResponse response = new SIPResponse(responseCode, reasonPhrase, localSIPEndPoint);

                if (reasonPhrase != null)
                {
                    response.ReasonPhrase = reasonPhrase;
                }

                SIPSchemesEnum sipScheme = (localSIPEndPoint.SIPProtocol == SIPProtocolsEnum.tls) ? SIPSchemesEnum.sips : SIPSchemesEnum.sip;
                SIPFromHeader from = new SIPFromHeader(null, new SIPURI(sipScheme, localSIPEndPoint), null);
                SIPToHeader to = new SIPToHeader(null, new SIPURI(sipScheme, localSIPEndPoint), null);
                int cSeq = 1;
                string callId = CallProperties.CreateNewCallId();
                response.Header = new SIPHeader(from, to, cSeq, callId);
                response.Header.CSeqMethod = SIPMethodsEnum.NONE;
                response.Header.Vias.PushViaHeader(new SIPViaHeader(new SIPEndPoint(localSIPEndPoint.SIPProtocol, remoteEndPoint.SocketEndPoint), CallProperties.CreateBranchId()));
                response.Header.MaxForwards = Int32.MinValue;

                return response;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTransport GetResponse. " + excp.Message);
                throw excp;
            }
        }

        public static SIPRequest GetRequest(SIPMethodsEnum method, SIPURI uri, SIPToHeader to, IPEndPoint localEndPoint)
        {
            SIPRequest request = new SIPRequest(method, uri);

            SIPContactHeader contactHeader = SIPContactHeader.ParseContactHeader("sip:" + localEndPoint)[0];
            SIPFromHeader fromHeader = new SIPFromHeader(null, contactHeader.ContactURI, CallProperties.CreateNewTag());
            SIPHeader header = new SIPHeader(fromHeader, to, Crypto.GetRandomInt(1000, 9999), CallProperties.CreateNewCallId());
            request.Header = header;
            header.CSeqMethod = method;
            SIPViaHeader viaHeader = new SIPViaHeader(localEndPoint.Address.ToString(), localEndPoint.Port, CallProperties.CreateBranchId());
            header.Vias.PushViaHeader(viaHeader);

            return request;
        }

        /// <summary>
        /// Attempts to match a SIPChannel for this process that has the specified local end point and protocol.
        /// </summary>
        /// <param name="localEndPoint">The local socket endpoint of the SIPChannel to find.</param>
        /// <returns>A matching SIPChannel if found otherwise null.</returns>
        public SIPChannel FindSIPChannel(SIPEndPoint localSIPEndPoint) {
            if (localSIPEndPoint == null) {
                return null;
            }
            else {
                if (m_sipChannels.ContainsKey(localSIPEndPoint)) {
                    return m_sipChannels[localSIPEndPoint];
                }
                else {
                    logger.Warn("No SIP channel could be found for local SIP end point " + localSIPEndPoint.ToString() + ".");
                    return null;
                }
            }
        }

        /// <summary>
        /// Attempts to find the SIPChannel that matches the provided name.
        /// </summary>
        /// <param name="name">The name of the SIPChannel to find.</param>
        /// <returns>A matching SIPChannel if found otherwise null.</returns>
        public SIPChannel FindSIPChannel(string name) {
            if (name.IsNullOrBlank()) {
                return null;
            }
            else {
                var sipChannel = from channel in m_sipChannels.Values
                                 where channel.Name == name
                                 select channel;

                if (sipChannel.Count() > 0) {
                    return sipChannel.First();
                }
                else {
                    logger.Warn("No SIP channel could be found for channel name " + name + ".");
                    return null;
                }
            }
        }

        /// <summary>
        /// Returns the first SIPChannel found for the requested protocol.
        /// </summary>
        /// <param name="protocol"></param>
        /// <returns></returns>
        private SIPChannel GetDefaultChannel(SIPProtocolsEnum protocol) {
            foreach (SIPChannel sipChannel in m_sipChannels.Values) {
                if (sipChannel.SIPChannelEndPoint.SIPProtocol == protocol) {
                    return sipChannel;
                }
            }

            logger.Warn("No default SIP channel could be found for " + protocol + ".");
            return null;
        }

        public bool IsLocalSIPEndPoint(SIPEndPoint sipEndPoint) {
            return m_sipChannels.ContainsKey(sipEndPoint);
        }

        #region Logging and metrics..

        private void FireSIPRequestInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                if (SIPRequestInTraceEvent != null)
                {
                    SIPRequestInTraceEvent(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPRequestInTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPRequestOutTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                if (SIPRequestOutTraceEvent != null)
                {
                    SIPRequestOutTraceEvent(localSIPEndPoint, remoteEndPoint, sipRequest);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPRequestOutTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPResponseInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            try
            {
                if (SIPResponseInTraceEvent != null)
                {
                    SIPResponseInTraceEvent(localSIPEndPoint, remoteEndPoint, sipResponse);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPResponseInTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPResponseOutTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            try
            {
                if (SIPResponseOutTraceEvent != null)
                {
                    SIPResponseOutTraceEvent(localSIPEndPoint, remoteEndPoint, sipResponse);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPResponseOutTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPBadRequestInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, string message, SIPValidationFieldsEnum sipErrorField)
        {
            try
            {
                if (SIPBadRequestInTraceEvent != null)
                {
                    SIPBadRequestInTraceEvent(localSIPEndPoint, remoteEndPoint, message, sipErrorField);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPBadRequestInTraceEvent. " + excp.Message);
            }
        }

        private void FireSIPBadResponseInTraceEvent(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, string message, SIPValidationFieldsEnum sipErrorField)
        {
            try
            {
                if (SIPBadResponseInTraceEvent != null)
                {
                    SIPBadResponseInTraceEvent(localSIPEndPoint, remoteEndPoint, message, sipErrorField);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPBadResponseInTraceEvent. " + excp.Message);
            }
        }

        private void RecordDiscardMetric(SIPEndPoint remoteEndPoint)
        {
            SIPTransportMetric metric = new SIPTransportMetric(DateTime.Now, remoteEndPoint.SocketEndPoint, SIPMessageTypesEnum.Unknown, SIPMethodsEnum.UNKNOWN, false, false, false, true, false, false, 0);
            AddMetricMeasurement(metric);
        }

        private void RecordSIPMessageMetric(SIPEndPoint remoteEndPoint, SIPMessageTypesEnum messageType, SIPMethodsEnum method, DateTime startParseTime)
        {
            SIPTransportMetric metric = new SIPTransportMetric(DateTime.Now, remoteEndPoint.SocketEndPoint, messageType, method, false, false, false, false, false, false, DateTime.Now.Subtract(startParseTime).TotalMilliseconds);
            AddMetricMeasurement(metric);
        }

        private void RecordSTUNMessageMetric(IPEndPoint remoteEndPoint, DateTime startParseTime)
        {
            SIPTransportMetric metric = new SIPTransportMetric(DateTime.Now, remoteEndPoint, SIPMessageTypesEnum.Unknown, SIPMethodsEnum.UNKNOWN, true, false, false, false, false, false, DateTime.Now.Subtract(startParseTime).TotalMilliseconds);
            AddMetricMeasurement(metric);
        }

        private void RecordBadSIPMessageMetric(SIPEndPoint remoteEndPoint, DateTime startParseTime)
        {
            SIPTransportMetric metric = new SIPTransportMetric(DateTime.Now, remoteEndPoint.SocketEndPoint, SIPMessageTypesEnum.Unknown, SIPMethodsEnum.UNKNOWN, false, false, true, false, false, false, DateTime.Now.Subtract(startParseTime).TotalMilliseconds);
            AddMetricMeasurement(metric);
        }

        private void RecordUnrecognisedMessageMetric(SIPEndPoint remoteEndPoint, DateTime startParseTime)
        {
            SIPTransportMetric metric = new SIPTransportMetric(DateTime.Now, remoteEndPoint.SocketEndPoint, SIPMessageTypesEnum.Unknown, SIPMethodsEnum.UNKNOWN, false, true, false, false, false, false, DateTime.Now.Subtract(startParseTime).TotalMilliseconds);
            AddMetricMeasurement(metric);
        }
        
        private void RecordTooLargeMessageMetric(SIPEndPoint remoteEndPoint, DateTime startParseTime)
        {
            SIPTransportMetric metric = new SIPTransportMetric(DateTime.Now, remoteEndPoint.SocketEndPoint, SIPMessageTypesEnum.Unknown, SIPMethodsEnum.UNKNOWN, false, false, false, false, true, false, DateTime.Now.Subtract(startParseTime).TotalMilliseconds);
            AddMetricMeasurement(metric);
        }

        private void RecordSIPMessageSendMetric(SIPEndPoint remoteEndPoint, SIPMessageTypesEnum messageType, SIPMethodsEnum method)
        {
            SIPTransportMetric metric = new SIPTransportMetric(DateTime.Now, remoteEndPoint.SocketEndPoint, messageType, method, false, false, false, false, false, true, 0);
            AddMetricMeasurement(metric);
        }

        private void AddMetricMeasurement(SIPTransportMetric metric)
        {
            lock (m_sipTransportMeasurements)
            {
                if (m_sipTransportMeasurements.Count < MAX_MEASUREMENTSQUEUE_SIZE)
                {
                    m_sipTransportMeasurements.Enqueue(metric);
                }
                else
                {
                    logger.Warn("SIPTransport metric not recorded as metrics maximum queue size of " + MAX_MEASUREMENTSQUEUE_SIZE + " has been reached.");
                }
            }
        }

        #endregion

        #region Transaction retrieval and creation methods.

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

        public SIPNonInviteTransaction CreateNonInviteTransaction(SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint)
        {
            try
            {
                CheckTransactionEngineExists();
                SIPNonInviteTransaction nonInviteTransaction = new SIPNonInviteTransaction(this, sipRequest, dstEndPoint, localSIPEndPoint);
                m_transactionEngine.AddTransaction(nonInviteTransaction);
                return nonInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateNonInviteTransaction. " + excp.Message);
                throw;
            }
        }

        public UACInviteTransaction CreateUACTransaction(SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint)
        {
            try
            {
                CheckTransactionEngineExists();
                UACInviteTransaction uacInviteTransaction = new UACInviteTransaction(this, sipRequest, dstEndPoint, localSIPEndPoint);
                m_transactionEngine.AddTransaction(uacInviteTransaction);
                return uacInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateUACTransaction. " + excp.Message);
                throw;
            }
        }

        public UASInviteTransaction CreateUASTransaction(SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint)
        {
            try
            {
                CheckTransactionEngineExists();
                UASInviteTransaction uasInviteTransaction = new UASInviteTransaction(this, sipRequest, dstEndPoint, localSIPEndPoint);
                m_transactionEngine.AddTransaction(uasInviteTransaction);
                return uasInviteTransaction;
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateUASTransaction. " + excp.Message);
                throw;
            }
        }

        public SIPCancelTransaction CreateCancelTransaction(SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint, UASInviteTransaction inviteTransaction)
        {
            try
            {
                CheckTransactionEngineExists();
                SIPCancelTransaction cancelTransaction = new SIPCancelTransaction(this, sipRequest, dstEndPoint, localSIPEndPoint, inviteTransaction);
                m_transactionEngine.AddTransaction(cancelTransaction);
                return cancelTransaction;
            }
            catch (Exception excp)
            {
                logger.Error("Exception CreateUASTransaction. " + excp.Message);
                throw;
            }
        }

        private void CheckTransactionEngineExists() {
            if (m_transactionEngine == null) {
                throw new ApplicationException("A transaction engine is required for this operation but one has not been provided.");
            }
        }

        #endregion

        #region DNS resolution methods.

        public SIPEndPoint GetHostEndPoint(string host, bool synchronous)
        {
            return ResolveSIPEndPoint_External(SIPURI.ParseSIPURIRelaxed(host), synchronous);
        }

        public SIPEndPoint GetURIEndPoint(SIPURI uri, bool synchronous)
        {
            return ResolveSIPEndPoint_External(uri, synchronous);
        }

        /// <summary>
        /// Based on the information in the SIP request attempts to determine the end point the request should
        /// be sent to.
        /// </summary>
        public SIPEndPoint GetRequestEndPoint(SIPRequest sipRequest, bool synchronous)
        {
            SIPEndPoint requestEndPoint = null;
            if (sipRequest.Header.Routes != null && sipRequest.Header.Routes.Length > 0)
            {
                requestEndPoint = GetURIEndPoint(sipRequest.Header.Routes.TopRoute.URI, synchronous);
            }
            else
            {
                requestEndPoint = GetURIEndPoint(sipRequest.URI, synchronous);
            }

            return requestEndPoint;
        }

        #endregion
    }
}
