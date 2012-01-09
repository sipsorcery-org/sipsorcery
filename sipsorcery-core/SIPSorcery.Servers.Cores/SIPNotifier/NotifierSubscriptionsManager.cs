// ============================================================================
// FileName: NotifierSubscriptionsManager.cs
//
// Description:
// A client subscriptions manager for a SIP Notifier server as described in RFC3265 
// "Session Initiation Protocol (SIP)-Specific Event Notification". This class keeps track of subscriptions that
// have been created by SIP client user agents and handles forwarding notifications for those subscriptions.
//
// Author(s):
// Aaron Clauson
//
// History:
// 23 Feb 2010	Aaron Clauson	Created.
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
using System.Collections.Generic;
using System.Data.Linq;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Web.Services;
using log4net;

namespace SIPSorcery.Servers
{
    public class NotifierSubscriptionsManager
    {
        private const string GET_NOTIFICATIONS_THREAD_NAME = "subscriptionmanager-get";

        private static ILog logger = AppState.GetLogger("sipsubmngr");

        private static readonly int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;

        private SIPMonitorLogDelegate MonitorLogEvent_External;
        private SIPAssetGetListDelegate<SIPDialogueAsset> GetDialogues_External;
        private SIPAssetGetByIdDelegate<SIPDialogueAsset> GetDialogue_External;
        private SIPAssetCountDelegate<SIPRegistrarBinding> GetSIPRegistrarBindingsCount_External;
        private SIPAssetPersistor<SIPAccount> m_sipAssetPersistor;
        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private ISIPMonitorPublisher m_publisher;                           // The SIP monitor event publisher, could be a memory or WPF boundary.
        private string m_notificationsAddress = Guid.NewGuid().ToString();  // The address used to subscribe to the SIP monitor event publisher.

        private Dictionary<string, SIPEventSubscription> m_subscriptions = new Dictionary<string, SIPEventSubscription>();    // [monitor session ID, subscription].

        public NotifierSubscriptionsManager(
            SIPMonitorLogDelegate logDelegate,
            SIPAssetGetListDelegate<SIPDialogueAsset> getDialogues,
            SIPAssetGetByIdDelegate<SIPDialogueAsset> getDialogue,
            SIPAssetPersistor<SIPAccount> sipAssetPersistor,
            SIPAssetCountDelegate<SIPRegistrarBinding> getBindingsCount,
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            ISIPMonitorPublisher publisher)
        {
            MonitorLogEvent_External = logDelegate;
            GetDialogues_External = getDialogues;
            GetDialogue_External = getDialogue;
            GetSIPRegistrarBindingsCount_External = getBindingsCount;
            m_sipAssetPersistor = sipAssetPersistor;
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_publisher = publisher;
            m_publisher.MonitorEventReady += MonitorEventAvailable;
        }

        public string SubscribeClient(
            string owner,
            string adminID,
            SIPRequest subscribeRequest,
            string toTag,
            SIPURI canonicalResourceURI,
            out SIPResponseStatusCodesEnum errorResponse,
            out string errorReason)
        {
            try
            {
                errorResponse = SIPResponseStatusCodesEnum.None;
                errorReason = null;

                SIPURI resourceURI = subscribeRequest.URI.CopyOf();
                SIPEventPackage eventPackage = SIPEventPackage.Parse(subscribeRequest.Header.Event);
                int expiry = subscribeRequest.Header.Expires;

                if (!(eventPackage == SIPEventPackage.Dialog || eventPackage == SIPEventPackage.Presence))
                {
                    throw new ApplicationException("Event package " + eventPackage.ToString() + " is not supported by the subscriptions manager.");
                }
                else
                {
                    if (expiry > 0)
                    {
                        string subscribeError = null;
                        string sessionID = Guid.NewGuid().ToString();
                        SIPDialogue subscribeDialogue = new SIPDialogue(subscribeRequest, owner, adminID, toTag);

                        if (eventPackage == SIPEventPackage.Dialog)
                        {
                            string monitorFilter = "dialog " + canonicalResourceURI.ToString();
                            if (!subscribeRequest.Body.IsNullOrBlank())
                            {
                                monitorFilter += " and " + subscribeRequest.Body;
                            }

                            m_publisher.Subscribe(owner, adminID, m_notificationsAddress, sessionID, SIPMonitorClientTypesEnum.Machine.ToString(), monitorFilter, expiry, null, out subscribeError);

                            if (subscribeError != null)
                            {
                                throw new ApplicationException(subscribeError);
                            }
                            else
                            {
                                SIPDialogEventSubscription subscription = new SIPDialogEventSubscription(MonitorLogEvent_External, sessionID, resourceURI, canonicalResourceURI, monitorFilter, subscribeDialogue, expiry, GetDialogues_External, GetDialogue_External);
                                m_subscriptions.Add(sessionID, subscription);
                                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAccept, "New dialog subscription created for " + resourceURI.ToString() + ", expiry " + expiry + "s.", owner));
                            }
                        }
                        else if (eventPackage == SIPEventPackage.Presence)
                        {
                            string monitorFilter = "presence " + canonicalResourceURI.ToString();
                            m_publisher.Subscribe(owner, adminID, m_notificationsAddress, sessionID, SIPMonitorClientTypesEnum.Machine.ToString(), monitorFilter, expiry, null, out subscribeError);

                            if (subscribeError != null)
                            {
                                throw new ApplicationException(subscribeError);
                            }
                            else
                            {
                                bool switchboardAccountsOnly = subscribeRequest.Body == SIPPresenceEventSubscription.SWITCHBOARD_FILTER;
                                SIPPresenceEventSubscription subscription = new SIPPresenceEventSubscription(MonitorLogEvent_External, sessionID, resourceURI, canonicalResourceURI, monitorFilter, subscribeDialogue, expiry, m_sipAssetPersistor, GetSIPRegistrarBindingsCount_External, switchboardAccountsOnly);
                                m_subscriptions.Add(sessionID, subscription);
                                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAccept, "New presence subscription created for " + resourceURI.ToString() + ", expiry " + expiry + "s.", owner));
                            }
                        }

                        return sessionID;
                    }

                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotifierSubscriptionsManager SubscribeClient. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Attempts to renew an existing subscription.
        /// </summary>
        /// <returns>The session ID if the renewal was successful or null if it wasn't.</returns>
        public string RenewSubscription(SIPRequest subscribeRequest, out SIPResponseStatusCodesEnum errorResponse, out string errorReason)
        {
            errorResponse = SIPResponseStatusCodesEnum.None;
            errorReason = null;

            try
            {
                int expiry = subscribeRequest.Header.Expires;
                string toTag = subscribeRequest.Header.To.ToTag;
                string fromTag = subscribeRequest.Header.From.FromTag;
                string callID = subscribeRequest.Header.CallId;
                int cseq = subscribeRequest.Header.CSeq;

                // Check for an existing subscription.
                SIPEventSubscription existingSubscription = (from sub in m_subscriptions.Values where sub.SubscriptionDialogue.CallId == callID select sub).FirstOrDefault();

                if (existingSubscription != null)
                {
                    if (expiry == 0)
                    {
                        // Subsciption is being cancelled.
                        StopSubscription(existingSubscription);
                        return null;
                    }
                    else if (cseq > existingSubscription.SubscriptionDialogue.RemoteCSeq)
                    {
                        logger.Debug("Renewing subscription for " + existingSubscription.SessionID + " and " + existingSubscription.SubscriptionDialogue.Owner + ".");
                        existingSubscription.SubscriptionDialogue.RemoteCSeq = cseq;
                        //existingSubscription.ProxySendFrom = (!subscribeRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(subscribeRequest.Header.ProxyReceivedOn) : null;

                        string extensionResult = m_publisher.ExtendSession(m_notificationsAddress, existingSubscription.SessionID, expiry);
                        if (extensionResult != null)
                        {
                            // One or more of the monitor servers could not extend the session. Close all the existing sessions and re-create.
                            MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeFailed, "Monitor session extension for " + existingSubscription.SubscriptionEventPackage.ToString() + " " + existingSubscription.ResourceURI.ToString() + " failed. " + extensionResult, existingSubscription.SubscriptionDialogue.Owner));
                            m_publisher.CloseSession(m_notificationsAddress, existingSubscription.SessionID);

                            // Need to re-establish the sessions with the notification servers.
                            string subscribeError = null;
                            string sessionID = Guid.NewGuid().ToString();
                            m_publisher.Subscribe(existingSubscription.SubscriptionDialogue.Owner, existingSubscription.SubscriptionDialogue.AdminMemberId, m_notificationsAddress, sessionID, SIPMonitorClientTypesEnum.Machine.ToString(), existingSubscription.MonitorFilter, expiry, null, out subscribeError);

                            if (subscribeError != null)
                            {
                                throw new ApplicationException(subscribeError);
                            }
                            else
                            {
                                lock (m_subscriptions)
                                {
                                    m_subscriptions.Remove(existingSubscription.SessionID);
                                    existingSubscription.SessionID = sessionID;
                                    m_subscriptions.Add(sessionID, existingSubscription);
                                }
                                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeAccept, "Monitor session recreated for " + existingSubscription.SubscriptionEventPackage.ToString() + " " + existingSubscription.ResourceURI.ToString() + ".", existingSubscription.SubscriptionDialogue.Owner));
                            }
                        }

                        MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.SubscribeRenew, "Monitor session successfully renewed for " + existingSubscription.SubscriptionEventPackage.ToString() + " " + existingSubscription.ResourceURI.ToString() + ".", existingSubscription.SubscriptionDialogue.Owner));

                        return existingSubscription.SessionID;
                    }
                    else
                    {
                        throw new ApplicationException("A duplicate SUBSCRIBE request was received by NotifierSubscriptionsManager.");
                    }
                }
                else
                {
                    //throw new ApplicationException("No existing subscription could be found for a subscribe renewal request.");
                    errorResponse = SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist;
                    errorReason = "Subscription dialog not found";
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RenewSubscription. " + excp.Message);
                throw;
            }
        }

        public void SendFullStateNotify(string sessionID)
        {
            try
            {
                if (m_subscriptions.ContainsKey(sessionID))
                {
                    SIPEventSubscription subscription = m_subscriptions[sessionID];

                    lock (subscription)
                    {
                        subscription.GetFullState();
                        SendNotifyRequestForSubscription(subscription);
                    }
                }
                else
                {
                    logger.Warn("No subscription could be found for " + sessionID + " when attempting to send a full state notification.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotifierSubscriptionsManager SendFullStateNotify. " + excp.Message);
                throw;
            }
        }

        private void StopSubscription(SIPEventSubscription subscription)
        {
            try
            {
                m_publisher.CloseSession(m_notificationsAddress, subscription.SessionID);
                lock (m_subscriptions)
                {
                    m_subscriptions.Remove(subscription.SessionID);
                }

                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Warn, "Stopping subscription for " + subscription.SubscriptionEventPackage.ToString() + " " + subscription.ResourceURI.ToString() + ".", subscription.SubscriptionDialogue.Owner));
            }
            catch (Exception excp)
            {
                logger.Error("Exception StopSubscription. " + excp.Message);
            }
        }

        private void SendNotifyRequestForSubscription(SIPEventSubscription subscription)
        {
            try
            {
                subscription.SubscriptionDialogue.CSeq++;

                //logger.Debug(DateTime.Now.ToString("HH:mm:ss:fff") + " Sending NOTIFY request for " + subscription.SubscriptionDialogue.Owner + " event " + subscription.SubscriptionEventPackage.ToString()
                //    + " and " + subscription.ResourceURI.ToString() + " to " + subscription.SubscriptionDialogue.RemoteTarget.ToString() + ", cseq=" + (subscription.SubscriptionDialogue.CSeq) + ".");

                int secondsRemaining = Convert.ToInt32(subscription.LastSubscribe.AddSeconds(subscription.Expiry).Subtract(DateTime.Now).TotalSeconds % Int32.MaxValue);

                SIPRequest notifyRequest = m_sipTransport.GetRequest(SIPMethodsEnum.NOTIFY, subscription.SubscriptionDialogue.RemoteTarget);
                notifyRequest.Header.From = SIPFromHeader.ParseFromHeader(subscription.SubscriptionDialogue.LocalUserField.ToString());
                notifyRequest.Header.To = SIPToHeader.ParseToHeader(subscription.SubscriptionDialogue.RemoteUserField.ToString());
                notifyRequest.Header.Event = subscription.SubscriptionEventPackage.ToString();
                notifyRequest.Header.CSeq = subscription.SubscriptionDialogue.CSeq;
                notifyRequest.Header.CallId = subscription.SubscriptionDialogue.CallId;
                notifyRequest.Body = subscription.GetNotifyBody();
                notifyRequest.Header.ContentLength = notifyRequest.Body.Length;
                notifyRequest.Header.SubscriptionState = "active;expires=" + secondsRemaining.ToString();
                notifyRequest.Header.ContentType = subscription.NotifyContentType;
                notifyRequest.Header.ProxySendFrom = subscription.SubscriptionDialogue.ProxySendFrom;

                // If the outbound proxy is a loopback address, as it will normally be for local deployments, then it cannot be overriden.
                SIPEndPoint dstEndPoint = m_outboundProxy;
                if (m_outboundProxy != null && IPAddress.IsLoopback(m_outboundProxy.Address))
                {
                    dstEndPoint = m_outboundProxy;
                }
                else if (subscription.SubscriptionDialogue.ProxySendFrom != null)
                {
                    // The proxy will always be listening on UDP port 5060 for requests from internal servers.
                    dstEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(SIPEndPoint.ParseSIPEndPoint(subscription.SubscriptionDialogue.ProxySendFrom).Address, m_defaultSIPPort));
                }

                SIPNonInviteTransaction notifyTransaction = m_sipTransport.CreateNonInviteTransaction(notifyRequest, dstEndPoint, m_sipTransport.GetDefaultSIPEndPoint(dstEndPoint), m_outboundProxy);
                notifyTransaction.NonInviteTransactionFinalResponseReceived += (local, remote, transaction, response) => { NotifyTransactionFinalResponseReceived(local, remote, transaction, response, subscription); };
                m_sipTransport.SendSIPReliable(notifyTransaction);

                //logger.Debug(notifyRequest.ToString());

                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.NotifySent, "Notification sent for " + subscription.SubscriptionEventPackage.ToString() + " and " + subscription.ResourceURI.ToString() + " to " + subscription.SubscriptionDialogue.RemoteTarget.ToString() + " (cseq=" + notifyRequest.Header.CSeq + ").", subscription.SubscriptionDialogue.Owner));

                subscription.NotificationSent();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendNotifyRequestForSubscription. " + excp.Message);
                throw;
            }
        }

        private void NotifyTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse, SIPEventSubscription subscription)
        {
            try
            {
                if (sipResponse.StatusCode >= 300)
                {
                    // The NOTIFY request was met with an error response.
                    MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Warn, "A notify request received an error response of " + sipResponse.Status + " " + sipResponse.ReasonPhrase + ".", subscription.SubscriptionDialogue.Owner));
                    StopSubscription(subscription);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotifyTransactionFinalResponseReceived. " + excp.Message);
            }
        }

        private bool MonitorEventAvailable(SIPMonitorEvent sipMonitorEvent)
        {
            try
            {
                SIPMonitorMachineEvent machineEvent = sipMonitorEvent as SIPMonitorMachineEvent;

                if (machineEvent != null && !machineEvent.SessionID.IsNullOrBlank() && m_subscriptions.ContainsKey(machineEvent.SessionID))
                {
                    SIPEventSubscription subscription = m_subscriptions[machineEvent.SessionID];

                    lock (subscription)
                    {
                        string resourceURI = (machineEvent.ResourceURI != null) ? machineEvent.ResourceURI.ToString() : null;

                        //logger.Debug("NotifierSubscriptionsManager received new " + machineEvent.MachineEventType + ", resource ID=" + machineEvent.ResourceID + ", resource URI=" + resourceURI + ".");

                        //MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Monitor, "NotifierSubscriptionsManager received new " + machineEvent.MachineEventType + ", resource ID=" + machineEvent.ResourceID + ", resource URI=" + resourceURI + ".", subscription.SubscriptionDialogue.Owner));

                        if (subscription.AddMonitorEvent(machineEvent))
                        {
                            SendNotifyRequestForSubscription(subscription);
                        }

                        //logger.Debug("NotifierSubscriptionsManager completed " + machineEvent.MachineEventType + ", resource ID=" + machineEvent.ResourceID + ", resource URI=" + resourceURI + ".");
                    }

                    return true;
                }

                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotifierSubscriptionsManager MonitorEventAvailable. " + excp.Message);
                return false;
            }
        }
    }
}
