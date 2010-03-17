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
    public class DialogSubscription
    {
        public string SessionID { get; private set; }
        public string CustomerUsername { get; private set; }
        public string AdminID { get; private set; }
        public SIPEndPoint ProxySendFrom { get; private set; }
        public SIPURI ContactURI { get; private set; }
        public SIPEventDialogInfo DialogInfo;
        public string ToTag;
        public string FromTag;
        public string CallID;
        public int CSeq;
        public string Filter;

        public DialogSubscription(
            string sessionID,
            string customerUsername,
            string adminID,
            SIPURI contactURI,
            SIPURI resourceURI,
            string toTag,
            string fromTag,
            string callID,
            int cseq,
            string filter,
            SIPEndPoint proxySendFrom)
        {
            SessionID = sessionID;
            CustomerUsername = customerUsername;
            AdminID = adminID;
            ContactURI = contactURI;
            DialogInfo = new SIPEventDialogInfo(0, SIPEventDialogInfoStateEnum.full, resourceURI);
            ToTag = toTag;
            FromTag = fromTag;
            CallID = callID;
            CSeq = cseq;
            Filter = filter;
            ProxySendFrom = proxySendFrom;
        }
    }

    public class NotifierSubscriptionsManager
    {
        private const string GET_NOTIFICATIONS_THREAD_NAME = "subscriptionmanager-get";
        private const int MAX_DIALOGUES_FOR_NOTIFY = 25;

        private static ILog logger = AppState.GetLogger("sipsubmngr");

        private event SIPMonitorLogDelegate MonitorLogEvent_External;
        private SIPAssetGetListDelegate<SIPDialogueAsset> GetDialogues_External;
        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private ISIPMonitorPublisher m_publisher;                           // The SIP monitor event publisher, could be a memory or WPF boundary.
        private string m_notificationsAddress = Guid.NewGuid().ToString();  // The address used to subscribe to the SIP monitor event publisher.

        private Dictionary<string, DialogSubscription> m_dialogSubscriptions = new Dictionary<string, DialogSubscription>();    // [monitor session ID, subscription].

        //private bool m_exit;

        public NotifierSubscriptionsManager(
            SIPMonitorLogDelegate logDelegate,
            SIPAssetGetListDelegate<SIPDialogueAsset> getDialogues,
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            ISIPMonitorPublisher publisher)
        {
            MonitorLogEvent_External = logDelegate;
            GetDialogues_External = getDialogues;
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_publisher = publisher;
            m_publisher.NotificationReady += MonitorEventAvailable;
            //ThreadPool.QueueUserWorkItem(delegate { GetNotifications(); });
        }

        public void Stop()
        {
            // m_exit = true;
        }

        public string SubscribeClient(
            string owner,
            string adminID,
            SIPRequest subscribeRequest)
        {
            try
            {
                SIPURI resourceURI = subscribeRequest.URI.CopyOf();
                SIPEventPackage eventPackage = SIPEventPackage.Parse(subscribeRequest.Header.Event);
                SIPURI contactURI = subscribeRequest.Header.Contact[0].ContactURI.CopyOf();
                int expiry = subscribeRequest.Header.Expires;
                string toTag = subscribeRequest.Header.To.ToTag;
                string fromTag = subscribeRequest.Header.From.FromTag;
                string callID = subscribeRequest.Header.CallId;
                int cseq = subscribeRequest.Header.CSeq;
                string subscribeBody = subscribeRequest.Body;
                SIPEndPoint proxySendFrom = (!subscribeRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(subscribeRequest.Header.ProxyReceivedOn) : null;

                if (IPSocket.IsPrivateAddress(contactURI.Host))
                {
                    // If the Contact URI is private and the request was received on a public IP mangle it so NOTIFY requests can get through.
                    SIPEndPoint receivedFrom = (!subscribeRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(subscribeRequest.Header.ProxyReceivedFrom) : null;
                    if( receivedFrom != null && !IPSocket.IsPrivateAddress(receivedFrom.SocketEndPoint.Address.ToString()))
                    {
                        contactURI.Host = receivedFrom.SocketEndPoint.ToString();
                    }
                }

                logger.Debug("New subscription client for " + owner + " for resource " + resourceURI.ToString() + " and event package " + eventPackage.ToString() + ".");

                if (eventPackage != SIPEventPackage.Dialog)
                {
                    throw new ApplicationException("Event package is not supported by the subscriptions manager.");
                }
                else
                {
                    if (expiry > 0)
                    {
                        string subscribeError = null;
                        string sessionID = Guid.NewGuid().ToString();
                        m_publisher.Subscribe(owner, adminID, m_notificationsAddress, sessionID, SIPMonitorClientTypesEnum.Machine.ToString(), "dialog " + resourceURI.ToString(), expiry, null, out subscribeError);

                        if (subscribeError != null)
                        {
                            throw new ApplicationException(subscribeError);
                        }
                        else
                        {
                            logger.Debug("New subscription created for " + sessionID + " and " + owner + ", Call-ID=" + callID + ".");
                            DialogSubscription dialogSubscription = new DialogSubscription(sessionID, owner, adminID, contactURI, resourceURI, toTag, fromTag, callID, cseq, subscribeBody, proxySendFrom);
                            m_dialogSubscriptions.Add(sessionID, dialogSubscription);
                            return sessionID;
                        }
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
        /// <returns>True if the subscription was found, false if it wasn't.</returns>
        public bool RenewSubscription(int expiry, string toTag, string fromTag, string callID, int cseq)
        {
            try
            {
                // Check for an existing subscription.
                DialogSubscription existingSubscription = (from sub in m_dialogSubscriptions.Values where sub.CallID == callID select sub).FirstOrDefault();

                if (existingSubscription != null)
                {
                    if (expiry == 0)
                    {
                        // Subsciption is being cancelled.
                        logger.Debug("Stopping subscription for " + existingSubscription.SessionID + " and " + existingSubscription.CustomerUsername + ".");
                        m_publisher.CloseSession(m_notificationsAddress, existingSubscription.SessionID);
                        m_dialogSubscriptions.Remove(existingSubscription.SessionID);
                    }
                    else if (cseq > existingSubscription.CSeq)
                    {
                        logger.Debug("Renewing subscription for " + existingSubscription.SessionID + " and " + existingSubscription.CustomerUsername + ".");
                        m_publisher.ExtendSession(m_notificationsAddress, existingSubscription.SessionID, expiry);
                        existingSubscription.CSeq = cseq;
                        SendFullStateNotify(existingSubscription.SessionID);
                    }

                    return true;
                }

                return false;
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
                DialogSubscription dialogSubscription = m_dialogSubscriptions[sessionID];
                dialogSubscription.DialogInfo.State = SIPEventDialogInfoStateEnum.full;
                List<SIPDialogueAsset> dialogueAssets = GetDialogues_External(d => d.Owner == dialogSubscription.CustomerUsername, "Inserted", 0, MAX_DIALOGUES_FOR_NOTIFY);

                int dialogItemsAdded = 0;
                foreach (SIPDialogueAsset dialogueAsset in dialogueAssets)
                {
                    dialogSubscription.DialogInfo.DialogItems.Add(new SIPEventDialog(dialogueAsset.SIPDialogue.Id.ToString(), "confirmed", dialogueAsset.SIPDialogue));
                    dialogItemsAdded++;

                    //if (dialogItemsAdded >= 3)
                    //{
                    //    SendNotifyRequestForSubscription(dialogSubscription);
                    //    dialogItemsAdded = 0;
                    //}
                }

                SendNotifyRequestForSubscription(dialogSubscription);

                dialogSubscription.DialogInfo.State = SIPEventDialogInfoStateEnum.partial;
                logger.Debug("Starting dialog subscription for " + dialogSubscription.CustomerUsername + " for resource " + dialogSubscription.DialogInfo.Entity.ToString() + ".");
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotifierSubscriptionsManager StartSubscription. " + excp.Message);
                throw;
            }
        }

        private void SendNotifyRequestForSubscription(DialogSubscription dialogSubscription)
        {
            try
            {
                logger.Debug(DateTime.Now.ToString("HH:mm:ss:fff") + " Sending NOTIFY request for " + dialogSubscription.CustomerUsername + " and " + dialogSubscription.DialogInfo.Entity.ToString() + " to " +
                    dialogSubscription.ContactURI.ToString() + ", version=" + dialogSubscription.DialogInfo.Version + ", cseq=" + (dialogSubscription.CSeq + 1)+ ".");
                dialogSubscription.CSeq++;

                SIPRequest notifyRequest = m_sipTransport.GetRequest(SIPMethodsEnum.NOTIFY, dialogSubscription.ContactURI, new SIPToHeader(null, dialogSubscription.ContactURI, dialogSubscription.ToTag), null);
                notifyRequest.Header.From.FromTag = dialogSubscription.FromTag;
                notifyRequest.Header.Event = SIPEventPackage.Dialog.ToString();
                notifyRequest.Header.CSeq = dialogSubscription.CSeq; ;
                notifyRequest.Header.CallId = dialogSubscription.CallID;
                notifyRequest.Body = dialogSubscription.DialogInfo.ToXMLText(dialogSubscription.Filter);
                notifyRequest.Header.ContentLength = notifyRequest.Body.Length;
                notifyRequest.Header.ProxySendFrom = (dialogSubscription.ProxySendFrom != null) ? dialogSubscription.ProxySendFrom.ToString() : null;

                if (m_outboundProxy != null)
                {
                    m_sipTransport.SendRequest(m_outboundProxy, notifyRequest);
                }
                else
                {
                    m_sipTransport.SendRequest(notifyRequest);
                }

                dialogSubscription.DialogInfo.DialogItems.Clear();
                dialogSubscription.DialogInfo.Version++;

                logger.Debug(DateTime.Now.ToString("HH:mm:ss:fff") + " Notification sent.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendNotifyRequestForSubscription. " + excp.Message);
            }
        }

        //private void GetNotifications()
        /*private void MonitorEventAvailable(string monitorEventStr)
        {
            try
            {
                //Thread.CurrentThread.Name = GET_NOTIFICATIONS_THREAD_NAME;

                //while (!m_exit)
                //{
                    //if (m_dialogSubscriptions.Count > 0)
                   // {
                       // while (m_publisher.IsNotificationReady(m_notificationsAddress))
                       // {
                            //logger.Debug("NotifierSubscriptionsManager getting notifications for " + m_notificationsAddress + ".");

                            //string sessionID = null;
                            //string sessionError = null;
                            //List<string> notifications = m_publisher.GetNotifications(m_notificationsAddress, out sessionID, out sessionError);

                            //if (sessionError != null)
                            //{
                            //    logger.Warn("NotifierSubscriptionsManager GetNotifications returned a session error. " + sessionError);
                            //}
                            //else if (notifications != null && notifications.Count() > 0)
                            //{
                                SIPMonitorEvent monitorEvent = SIPMonitorEvent.ParseEventCSV(monitorEventStr);

                                if(monitorEvent != null && monitorEvent.SessionID.IsNullOrBlank() && m_dialogSubscriptions.ContainsKey(monitorEvent.SessionID))
                                {
                                    DialogSubscription dialogSubscription = m_dialogSubscriptions[sessionID];

                                //var machineEvents = (from notification in notifications select SIPMonitorMachineEvent.ParseMachineEventCSV(notification)).OrderByDescending(e => e.Created);
                                //int dialogItemsAdded = 0;

                                //foreach (SIPMonitorMachineEvent machineEvent in machineEvents)
                                //{
                                    // The machine events are sorted in order of the most recent first, if a duplicate dialog ID occurs it can be ignored
                                    // since the most recent notification for that dialog must have already been included.
                                    //if ((from dialog in dialogSubscription.DialogInfo.DialogItems where dialog.ID == machineEvent.Dialogue.Id.ToString() select dialog).Count() > 0)
                                    //{
                                    //    logger.Debug(" notifier skipping notification for " + machineEvent.Dialogue.Id + " and " + GetStateForEventType(machineEvent.MachineEventType) + ".");
                                    //    continue;
                                    //}

                                    //logger.Debug("NotifierSubscriptionsManager received new " + machineEvent.MachineEventType + ", message=" + machineEvent.Message + ".");

                                    if (dialogSubscription != null)
                                    {
                                        string state = GetStateForEventType(machineEvent.MachineEventType);
                                        SIPDialogue sipDialogue = machineEvent.Dialogue;
                                        SIPDialogue sipDialogue = null;
                                        if (state != "terminated")
                                        {
                                            SIPDialogueAsset sipDialogueAsset = GetDialogue_External(new Guid(machineEvent.Message));
                                            sipDialogue = (sipDialogueAsset != null) ? sipDialogueAsset.SIPDialogue : null;
                                        }
                                        dialogSubscription.DialogInfo.DialogItems.Add(new SIPEventDialog(sipDialogue.Id.ToString(), state, sipDialogue));
                                        dialogItemsAdded++;
                                    }

                                    // if (dialogItemsAdded >= 3)
                                    //{
                                    //    SendNotifyRequestForSubscription(dialogSubscription);
                                    //     dialogItemsAdded = 0;
                                    // }
                                }

                                if (dialogItemsAdded > 0)
                                {
                                    SendNotifyRequestForSubscription(dialogSubscription);
                                }
                                }
                            }
                        //}
                    //}

                    //Thread.Sleep(1000);
                //}

                //logger.Debug("NotifierSubscriptionsManager GetNotifications halted.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotifierSubscriptionsManager GetNotifications. " + excp.Message);
            }
        }*/

        private void MonitorEventAvailable(string monitorEventStr)
        {
            try
            {
                SIPMonitorMachineEvent machineEvent = SIPMonitorEvent.ParseEventCSV(monitorEventStr) as SIPMonitorMachineEvent;

                if (machineEvent != null && !machineEvent.SessionID.IsNullOrBlank() && m_dialogSubscriptions.ContainsKey(machineEvent.SessionID))
                {
                    DialogSubscription dialogSubscription = m_dialogSubscriptions[machineEvent.SessionID];
                    //logger.Debug("NotifierSubscriptionsManager received new " + machineEvent.MachineEventType + ", message=" + machineEvent.Message + ".");

                    if (dialogSubscription != null)
                    {
                        string state = GetStateForEventType(machineEvent.MachineEventType);
                        SIPDialogue sipDialogue = machineEvent.Dialogue;
                        dialogSubscription.DialogInfo.DialogItems.Add(new SIPEventDialog(sipDialogue.Id.ToString(), state, sipDialogue));
                        SendNotifyRequestForSubscription(dialogSubscription);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotifierSubscriptionsManager GetNotifications. " + excp.Message);
            }
        }

        private string GetStateForEventType(SIPMonitorMachineEventTypesEnum machineEventType)
        {
            switch (machineEventType)
            {
                case SIPMonitorMachineEventTypesEnum.SIPDialogueCreated: return "confirmed";
                case SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved: return "terminated";
                case SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated: return "updated";
                default: throw new ApplicationException("The state for a dialog SIP event could not be determined from the monitor event type.");
            }
        }
    }
}
