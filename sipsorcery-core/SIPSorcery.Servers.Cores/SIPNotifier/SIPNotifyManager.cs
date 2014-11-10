// ============================================================================
// FileName: SIPNotifyManager.cs
//
// Description:
// Queues Notify requests received from the Application Server Core before dispatching them to the appropriate user
// agents.
//
// Author(s):
// Aaron Clauson
//
// History:
// 03 Jul 2009  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Net;
using System.Text;
using System.Threading;
using SIPSorcery.AppServer.DialPlan;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    public class SIPNotifyManager
    {
        private const int MAX_FORWARD_BINDINGS = 5;
        private const string PROCESS_NOTIFICATIONS_THREAD_NAME = "sipnotifymanager-processrequests";
        private const int MAX_QUEUEWAIT_PERIOD = 4000;              // Maximum time to wait to check the new notifications queue if no events are received.
        private const int MAX_NEWCALL_QUEUE = 10;                   // Maximum number of notification requests that will be queued for processing.
        private const string MWI_EVENT_TYPE = "message-summary";

        private static ILog logger = AppState.logger;

        private static readonly int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static readonly string m_crlf = SIPConstants.CRLF;
            
        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private bool m_stop;

        private SIPMonitorLogDelegate Log_External;
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;                         // Function in authenticate user outgoing calls.
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetSIPAccountBindings_External;    // Function to lookup bindings that have been registered for a SIP account.
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;

        private Queue<SIPRequest> m_newNotifications = new Queue<SIPRequest>();
        private ManualResetEvent m_newNotificationReady = new ManualResetEvent(false);

        public SIPNotifyManager(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPMonitorLogDelegate logDelegate,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getSIPAccountBindings,
            GetCanonicalDomainDelegate getCanonicalDomain)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            Log_External = logDelegate;
            GetSIPAccount_External = getSIPAccount;
            GetSIPAccountBindings_External = getSIPAccountBindings;
            GetCanonicalDomain_External = getCanonicalDomain;

            ThreadPool.QueueUserWorkItem(delegate { ProcessNewNotifications(PROCESS_NOTIFICATIONS_THREAD_NAME); });
        }

        public void Stop()
        {
            logger.Debug("SIPNotifyManager stopping.");
            m_stop = true;
            m_newNotificationReady.Set();
        }

        private void ProcessNewNotifications(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                while (!m_stop)
                {
                    while (m_newNotifications.Count > 0)
                    {
                        SIPRequest newNotification = null;

                        lock (m_newNotifications)
                        {
                            newNotification = m_newNotifications.Dequeue();
                        }

                        if (newNotification != null)
                        {
                            ProcessNotifyRequest(newNotification);
                        }
                    }

                    m_newNotificationReady.Reset();
                    m_newNotificationReady.WaitOne(MAX_QUEUEWAIT_PERIOD);
                }

                logger.Warn("SIPNotifyManager ProcessNewNotifications thread stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPNotifyManager ProcessNewNotifications. " + excp.Message);
            }
        }

        public void QueueNotification(SIPRequest notifyRequest)
        {
            try
            {
                if (m_newNotifications.Count < MAX_NEWCALL_QUEUE)
                {
                    lock (m_newNotifications)
                    {
                        m_newNotifications.Enqueue(notifyRequest);
                        m_newNotificationReady.Set();
                    }
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Warn, "Notify rejected request as queue full.", null));
                    SIPResponse overloadedResponse = SIPTransport.GetResponse(notifyRequest, SIPResponseStatusCodesEnum.TemporarilyUnavailable, "Notify Manager overloaded");
                    m_sipTransport.SendResponse(overloadedResponse);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPNotifyManager QueueNotification. " + excp.Message);
            }
        }

        public void ProcessNotifyRequest(SIPRequest sipRequest)
        {
            try
            {
                // Hack to work around MWI request from callcentric not having a trailing CRLF and breaking some softphones like the Bria.
                if (sipRequest.Header.Event == MWI_EVENT_TYPE && sipRequest.Body.NotNullOrBlank() && sipRequest.Body.Substring(sipRequest.Body.Length - 2) != m_crlf)
                {
                    sipRequest.Body += m_crlf;
                }

                string fromURI = (sipRequest.Header.From != null && sipRequest.Header.From.FromURI != null) ? sipRequest.Header.From.FromURI.ToString() : "unknown";

                string domain = GetCanonicalDomain_External(sipRequest.URI.Host, true);
                if (domain != null)
                {
                    SIPAccount sipAccount = GetSIPAccount_External(s => s.SIPUsername == sipRequest.URI.User && s.SIPDomain == domain);

                    if (sipAccount != null)
                    {
                        List<SIPRegistrarBinding> bindings = GetSIPAccountBindings_External(b => b.SIPAccountId == sipAccount.Id, null, 0, MAX_FORWARD_BINDINGS);

                        if (bindings != null)
                        {
                            foreach (SIPRegistrarBinding binding in bindings)
                            {
                                SIPURI dstURI = binding.MangledContactSIPURI;
                                SIPEndPoint localSIPEndPoint = (m_outboundProxy != null) ? m_sipTransport.GetDefaultSIPEndPoint(m_outboundProxy.Protocol) : m_sipTransport.GetDefaultSIPEndPoint(dstURI.Protocol);

                                SIPEndPoint dstSIPEndPoint = null;

                                // If the outbound proxy is a loopback address, as it will normally be for local deployments, then it cannot be overriden.
                                if(m_outboundProxy != null && IPAddress.IsLoopback(m_outboundProxy.Address))
                                {
                                    dstSIPEndPoint = m_outboundProxy;
                                }
                                else if (binding.ProxySIPEndPoint != null)
                                {
                                    // If the binding has a specific proxy end point sent then the request needs to be forwarded to the proxy's default end point for it to take care of.
                                    dstSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(binding.ProxySIPEndPoint.Address, m_defaultSIPPort));
                                }
                                else if (m_outboundProxy != null)
                                {
                                    dstSIPEndPoint = m_outboundProxy;
                                }
                                else
                                {
                                    SIPDNSLookupResult lookupResult = m_sipTransport.GetURIEndPoint(dstURI, false);
                                    if(lookupResult.LookupError != null)
                                    {
                                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.MWI, "A NOTIFY request from " + fromURI + " was not forwarded due to DNS failure for " + dstURI.Host + ", " + lookupResult.LookupError + ".", sipAccount.Owner));
                                    }
                                    else
                                    {
                                        dstSIPEndPoint = lookupResult.GetSIPEndPoint();
                                    }
                                }

                                if (dstSIPEndPoint != null)
                                {
                                    // Rather than create a brand new request copy the received one and modify the headers that need to be unique.
                                    SIPRequest notifyRequest = sipRequest.Copy();
                                    notifyRequest.URI = dstURI;
                                    notifyRequest.Header.Contact = SIPContactHeader.CreateSIPContactList(new SIPURI(dstURI.Scheme, localSIPEndPoint));
                                    notifyRequest.Header.To = new SIPToHeader(null, dstURI, null);
                                    notifyRequest.Header.CallId = CallProperties.CreateNewCallId();
                                    SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
                                    notifyRequest.Header.Vias = new SIPViaSet();
                                    notifyRequest.Header.Vias.PushViaHeader(viaHeader);

                                    // If the binding has a proxy socket defined set the header to ask the upstream proxy to use it.
                                    if (binding.ProxySIPEndPoint != null)
                                    {
                                        notifyRequest.Header.ProxySendFrom = binding.ProxySIPEndPoint.ToString();

                                        // If the binding has a specific proxy end point sent then the request needs to be forwarded to the proxy's default end point for it to take care of.
                                        dstSIPEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(binding.ProxySIPEndPoint.Address, m_defaultSIPPort));
                                    }

                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.MWI, "Forwarding NOTIFY request from " + fromURI + " to registered binding at " + dstURI.ToString() + ", proxy " + dstSIPEndPoint.ToString() + ".", sipAccount.Owner));
                                    SIPNonInviteTransaction notifyTransaction = m_sipTransport.CreateNonInviteTransaction(notifyRequest, dstSIPEndPoint, localSIPEndPoint, dstSIPEndPoint);
                                    notifyTransaction.SendReliableRequest();
                                }
                                else
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.MWI, "A NOTIFY request from " + fromURI + " was not forwarded as no destination end point was resolved.", sipAccount.Owner));
                                }
                            }

                            // Send OK response to server.
                            SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                            m_sipTransport.SendResponse(okResponse);
                        }
                        else
                        {
                            // Send unavailable response to server.
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.MWI, "NOTIFY request from " + fromURI + " for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " but no bindings available, responding with temporarily unavailable.", sipAccount.Owner));
                            SIPResponse notAvailableResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.TemporarilyUnavailable, null);
                            m_sipTransport.SendResponse(notAvailableResponse);
                        }
                    }
                    else
                    {
                        // Send Not found response to server.
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.MWI, "NOTIFY request from " + fromURI + " for " + sipRequest.URI.ToString() + " but no matching SIP account, responding with not found.", null));
                        SIPResponse notFoundResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, null);
                        m_sipTransport.SendResponse(notFoundResponse);
                    }
                }
                else
                {
                    // Send Not Serviced response to server.
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.MWI, "NOTIFY request from " + fromURI + " for a non-serviced domain responding with not found.", null));
                    SIPResponse notServicedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, "Domain not serviced");
                    m_sipTransport.SendResponse(notServicedResponse);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPNotifyManager ProcessNotifyRequest. " + excp.Message);
            }
        }
    }
}
