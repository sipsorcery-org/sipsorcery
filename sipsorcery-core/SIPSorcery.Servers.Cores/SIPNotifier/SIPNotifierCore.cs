// ============================================================================
// FileName: NotifierCore.cs
//
// Description:
// SIP Notifier server as described in RFC3265 "Session Initiation Protocol (SIP)-Specific Event Notification".
//
// Author(s):
// Aaron Clauson
//
// History:
// 22 Feb 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com)
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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public class NotifierCore
    {
        private const int MAX_NOTIFIER_QUEUE_SIZE = 1000;
        private const int MAX_NOTIFIER_SLEEP_TIME = 10000;
        private const string NOTIFIER_THREAD_NAME_PREFIX = "sipnotifier-core";
        private const int MIN_SUBSCRIPTION_EXPIRY = 60;
        private const int MAX_SUBSCRIPTION_EXPIRY = 3600;

        private static ILog logger = AppState.GetLogger("sipnotifier");

        private static string m_wildcardUser = SIPMonitorFilter.WILDCARD;
        private string m_topLevelAdminID = Customer.TOPLEVEL_ADMIN_ID;

        private SIPMonitorLogDelegate MonitorLogEvent_External;
        private SIPTransport m_sipTransport;
        private SIPAssetGetDelegate<Customer> GetCustomer_External;
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        private SIPAuthenticateRequestDelegate SIPRequestAuthenticator_External;
        private SIPAssetPersistor<SIPAccount> m_sipAssetPersistor;

        private Queue<SIPNonInviteTransaction> m_notifierQueue = new Queue<SIPNonInviteTransaction>();
        private AutoResetEvent m_notifierARE = new AutoResetEvent(false);
        private SIPEndPoint m_outboundProxy;
        private NotifierSubscriptionsManager m_subscriptionsManager;

        private bool m_exit;

        public NotifierCore(
            SIPMonitorLogDelegate logDelegate,
            SIPTransport sipTransport,
            SIPAssetGetDelegate<Customer> getCustomer,
            SIPAssetGetListDelegate<SIPDialogueAsset> getDialogues,
            SIPAssetGetByIdDelegate<SIPDialogueAsset> getDialogue,
            GetCanonicalDomainDelegate getCanonicalDomain,
            SIPAssetPersistor<SIPAccount> sipAssetPersistor,
            SIPAssetCountDelegate<SIPRegistrarBinding> getBindingsCount,
            SIPAuthenticateRequestDelegate sipRequestAuthenticator,
            SIPEndPoint outboundProxy,
            ISIPMonitorPublisher publisher)
        {
            MonitorLogEvent_External = logDelegate;
            m_sipTransport = sipTransport;
            GetCustomer_External = getCustomer;
            m_sipAssetPersistor = sipAssetPersistor;
            GetCanonicalDomain_External = getCanonicalDomain;
            SIPRequestAuthenticator_External = sipRequestAuthenticator;
            m_outboundProxy = outboundProxy;
            m_subscriptionsManager = new NotifierSubscriptionsManager(MonitorLogEvent_External, getDialogues, getDialogue, m_sipAssetPersistor, getBindingsCount, m_sipTransport, m_outboundProxy, publisher);

            ThreadPool.QueueUserWorkItem(delegate { ProcessSubscribeRequest(NOTIFIER_THREAD_NAME_PREFIX + "1"); });
        }

        public void Stop()
        {
            m_exit = true;

            //if (m_subscriptionsManager != null)
            //{
            //    m_subscriptionsManager.Stop();
            //}
        }

        public void AddSubscribeRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest subscribeRequest)
        {
            try
            {
                if (subscribeRequest.Method != SIPMethodsEnum.SUBSCRIBE)
                {
                    SIPResponse notSupportedResponse = SIPTransport.GetResponse(subscribeRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, "Subscribe requests only");
                    m_sipTransport.SendResponse(notSupportedResponse);
                }
                else
                {
                    #region Do as many validation checks as possible on the request before adding it to the queue.

                    if (subscribeRequest.Header.Event.IsNullOrBlank() ||
                        !(subscribeRequest.Header.Event.ToLower() == SIPEventPackage.Dialog.ToString() || subscribeRequest.Header.Event.ToLower() == SIPEventPackage.Presence.ToString()))
                    {
                        SIPResponse badEventResponse = SIPTransport.GetResponse(subscribeRequest, SIPResponseStatusCodesEnum.BadEvent, null);
                        m_sipTransport.SendResponse(badEventResponse);
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Warn, "Event type " + subscribeRequest.Header.Event + " not supported for " + subscribeRequest.URI.ToString() + ".", null));
                    }
                    else if (subscribeRequest.Header.Expires > 0 && subscribeRequest.Header.Expires < MIN_SUBSCRIPTION_EXPIRY)
                    {
                        SIPResponse tooBriefResponse = SIPTransport.GetResponse(subscribeRequest, SIPResponseStatusCodesEnum.IntervalTooBrief, null);
                        tooBriefResponse.Header.MinExpires = MIN_SUBSCRIPTION_EXPIRY;
                        m_sipTransport.SendResponse(tooBriefResponse);
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Warn, "Subscribe request was rejected as interval too brief " + subscribeRequest.Header.Expires + ".", null));
                    }
                    else if (subscribeRequest.Header.Contact == null || subscribeRequest.Header.Contact.Count == 0)
                    {
                        SIPResponse noContactResponse = SIPTransport.GetResponse(subscribeRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing Contact header");
                        m_sipTransport.SendResponse(noContactResponse);
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Warn, "Subscribe request was rejected due to no Contact header.", null));
                    }

                    #endregion

                    else
                    {
                        if (m_notifierQueue.Count < MAX_NOTIFIER_QUEUE_SIZE)
                        {
                            SIPNonInviteTransaction subscribeTransaction = m_sipTransport.CreateNonInviteTransaction(subscribeRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                            lock (m_notifierQueue)
                            {
                                m_notifierQueue.Enqueue(subscribeTransaction);
                            }
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeQueued, "Subscribe queued for " + subscribeRequest.Header.To.ToURI.ToString() + ".", null));
                        }
                        else
                        {
                            logger.Error("Subscribe queue exceeded max queue size " + MAX_NOTIFIER_QUEUE_SIZE + ", overloaded response sent.");
                            SIPResponse overloadedResponse = SIPTransport.GetResponse(subscribeRequest, SIPResponseStatusCodesEnum.TemporarilyUnavailable, "Notifier overloaded, please try again shortly");
                            m_sipTransport.SendResponse(overloadedResponse);
                        }

                        m_notifierARE.Set();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AddNotifierRequest (" + remoteEndPoint.ToString() + "). " + excp.Message);
            }
        }

        private void ProcessSubscribeRequest(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                while (!m_exit)
                {
                    if (m_notifierQueue.Count > 0)
                    {
                        try
                        {
                            SIPNonInviteTransaction subscribeTransaction = null;
                            lock (m_notifierQueue)
                            {
                                subscribeTransaction = m_notifierQueue.Dequeue();
                            }

                            if (subscribeTransaction != null)
                            {
                                DateTime startTime = DateTime.Now;
                                Subscribe(subscribeTransaction);
                                TimeSpan duration = DateTime.Now.Subtract(startTime);
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Timing, "Subscribe time=" + duration.TotalMilliseconds + "ms, user=" + subscribeTransaction.TransactionRequest.Header.To.ToURI.User + ".", null));
                            }
                        }
                        catch (Exception regExcp)
                        {
                            logger.Error("Exception ProcessSubscribeRequest Subscribe Job. " + regExcp.Message);
                        }
                    }
                    else
                    {
                        m_notifierARE.WaitOne(MAX_NOTIFIER_SLEEP_TIME);
                    }
                }

                logger.Warn("ProcessSubscribeRequest thread " + Thread.CurrentThread.Name + " stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProcessSubscribeRequest (" + Thread.CurrentThread.Name + "). " + excp.Message);
            }
        }

        private void Subscribe(SIPTransaction subscribeTransaction)
        {
            try
            {
                SIPRequest sipRequest = subscribeTransaction.TransactionRequest;
                string fromUser = sipRequest.Header.From.FromURI.User;
                string fromHost = sipRequest.Header.From.FromURI.Host;
                string canonicalDomain = GetCanonicalDomain_External(fromHost, true);

                if (canonicalDomain == null)
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Warn, "Subscribe request for " + fromHost + " rejected as no matching domain found.", null));
                    SIPResponse noDomainResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, "Domain not serviced");
                    subscribeTransaction.SendFinalResponse(noDomainResponse);
                    return;
                }

                SIPAccount sipAccount = m_sipAssetPersistor.Get(s => s.SIPUsername == fromUser && s.SIPDomain == canonicalDomain);
                SIPRequestAuthenticationResult authenticationResult = SIPRequestAuthenticator_External(subscribeTransaction.LocalSIPEndPoint, subscribeTransaction.RemoteEndPoint, sipRequest, sipAccount, FireProxyLogEvent);

                if (!authenticationResult.Authenticated)
                {
                    // 401 Response with a fresh nonce needs to be sent.
                    SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                    authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                    subscribeTransaction.SendFinalResponse(authReqdResponse);

                    if (authenticationResult.ErrorResponse == SIPResponseStatusCodesEnum.Forbidden)
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Warn, "Forbidden " + fromUser + "@" + canonicalDomain + " does not exist, " + sipRequest.Header.ProxyReceivedFrom.ToString() + ", " + sipRequest.Header.UserAgent + ".", null));
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAuth, "Authentication required for " + fromUser + "@" + canonicalDomain + " from " + subscribeTransaction.RemoteEndPoint + ".", sipAccount.Owner));
                    }
                    return;
                }
                else
                {
                    if (sipRequest.Header.To.ToTag != null)
                    {
                        // Request is to renew an existing subscription.
                        SIPResponseStatusCodesEnum errorResponse = SIPResponseStatusCodesEnum.None;
                        string errorResponseReason = null;

                        string sessionID = m_subscriptionsManager.RenewSubscription(sipRequest, out errorResponse, out errorResponseReason);
                        if (errorResponse != SIPResponseStatusCodesEnum.None)
                        {
                            // A subscription renewal attempt failed
                            SIPResponse renewalErrorResponse = SIPTransport.GetResponse(sipRequest, errorResponse, errorResponseReason);
                            subscribeTransaction.SendFinalResponse(renewalErrorResponse);
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeFailed, "Subscription renewal failed for event type " + sipRequest.Header.Event + " " + sipRequest.URI.ToString() + ", " + errorResponse + " " + errorResponseReason + ".", sipAccount.Owner));
                        }
                        else if (sipRequest.Header.Expires == 0)
                        {
                            // Existing subscription was closed.
                            SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                            subscribeTransaction.SendFinalResponse(okResponse);
                        }
                        else
                        {
                            // Existing subscription was found.
                            SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                            subscribeTransaction.SendFinalResponse(okResponse);
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeRenew, "Subscription renewal for " + sipRequest.URI.ToString() + ", event type " + sipRequest.Header.Event + " and expiry " + sipRequest.Header.Expires + ".", sipAccount.Owner));
                            m_subscriptionsManager.SendFullStateNotify(sessionID);
                        }
                    }
                    else
                    {
                        // Authenticated but the this is a new subscription request and authorisation to subscribe to the requested resource also needs to be checked.
                        SIPURI canonicalResourceURI = sipRequest.URI.CopyOf();
                        string resourceCanonicalDomain = GetCanonicalDomain_External(canonicalResourceURI.Host, true);
                        canonicalResourceURI.Host = resourceCanonicalDomain;
                        SIPAccount resourceSIPAccount = null;

                        if (resourceCanonicalDomain == null)
                        {
                            SIPResponse notFoundResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, "Domain " + resourceCanonicalDomain + " not serviced");
                            subscribeTransaction.SendFinalResponse(notFoundResponse);
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeFailed, "Subscription failed for " + sipRequest.URI.ToString() + ", event type " + sipRequest.Header.Event + ", domain not serviced.", sipAccount.Owner));
                            return;
                        }

                        if (canonicalResourceURI.User != m_wildcardUser)
                        {
                            resourceSIPAccount = m_sipAssetPersistor.Get(s => s.SIPUsername == canonicalResourceURI.User && s.SIPDomain == canonicalResourceURI.Host);

                            if (resourceSIPAccount == null)
                            {
                                SIPResponse notFoundResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, "Requested resource does not exist");
                                subscribeTransaction.SendFinalResponse(notFoundResponse);
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeFailed, "Subscription failed for " + sipRequest.URI.ToString() + ", event type " + sipRequest.Header.Event + ", SIP account does not exist.", sipAccount.Owner));
                                return;
                            }
                        }

                        // Check the owner permissions on the requesting and subscribed resources.
                        bool authorised = false;
                        string adminID = null;

                        if (canonicalResourceURI.User == m_wildcardUser || sipAccount.Owner == resourceSIPAccount.Owner)
                        {
                            authorised = true;
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAuth, "Subscription to " + canonicalResourceURI.ToString() + " authorised due to common owner.", sipAccount.Owner));
                        }
                        else
                        {
                            // Lookup the customer record for the requestor and check the administrative level on it.
                            Customer requestingCustomer = GetCustomer_External(c => c.CustomerUsername == sipAccount.Owner);
                            adminID = requestingCustomer.AdminId;
                            if (!resourceSIPAccount.AdminMemberId.IsNullOrBlank() && requestingCustomer.AdminId == resourceSIPAccount.AdminMemberId)
                            {
                                authorised = true;
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAuth, "Subscription to " + canonicalResourceURI.ToString() + " authorised due to requestor admin permissions for domain " + resourceSIPAccount.AdminMemberId + ".", sipAccount.Owner));
                            }
                            else if (requestingCustomer.AdminId == m_topLevelAdminID)
                            {
                                authorised = true;
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAuth, "Subscription to " + canonicalResourceURI.ToString() + " authorised due to requestor having top level admin permissions.", sipAccount.Owner));
                            }
                        }

                        if (authorised)
                        {
                            // Request is to create a new subscription.
                            SIPResponseStatusCodesEnum errorResponse = SIPResponseStatusCodesEnum.None;
                            string errorResponseReason = null;
                            string toTag = CallProperties.CreateNewTag();
                            string sessionID = m_subscriptionsManager.SubscribeClient(sipAccount.Owner, adminID, sipRequest, toTag, canonicalResourceURI, out errorResponse, out errorResponseReason);

                            if (errorResponse != SIPResponseStatusCodesEnum.None)
                            {
                                SIPResponse subscribeErrorResponse = SIPTransport.GetResponse(sipRequest, errorResponse, errorResponseReason);
                                subscribeTransaction.SendFinalResponse(subscribeErrorResponse);
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAccept, "Subscription failed for " + sipRequest.URI.ToString() + ", event type " + sipRequest.Header.Event + ", " + errorResponse + " " + errorResponseReason + ".", sipAccount.Owner));
                            }
                            else
                            {
                                SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                                okResponse.Header.To.ToTag = toTag;
                                okResponse.Header.Expires = sipRequest.Header.Expires;
                                okResponse.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(SIPSchemesEnum.sip, subscribeTransaction.LocalSIPEndPoint)) };
                                subscribeTransaction.SendFinalResponse(okResponse);
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAccept, "Subscription accepted for " + sipRequest.URI.ToString() + ", event type " + sipRequest.Header.Event + " and expiry " + sipRequest.Header.Expires + ".", sipAccount.Owner));

                                if (sessionID != null)
                                {
                                    m_subscriptionsManager.SendFullStateNotify(sessionID);
                                }
                            }
                        }
                        else
                        {
                            SIPResponse forbiddenResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, "Requested resource not authorised");
                            subscribeTransaction.SendFinalResponse(forbiddenResponse);
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeFailed, "Subscription failed for " + sipRequest.URI.ToString() + ", event type " + sipRequest.Header.Event + ", requesting account " + sipAccount.Owner + " was not authorised.", sipAccount.Owner));
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception notifiercore subscribing. " + excp.Message + "\r\n" + subscribeTransaction.TransactionRequest.ToString());
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Error, "Exception notifiercore subscribing. " + excp.Message, null));
                SIPResponse errorResponse = SIPTransport.GetResponse(subscribeTransaction.TransactionRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                subscribeTransaction.SendFinalResponse(errorResponse);
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            if (MonitorLogEvent_External != null)
            {
                try
                {
                    MonitorLogEvent_External(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireProxyLogEvent NotifierCore. " + excp.Message);
                }
            }
        }
    }
}
