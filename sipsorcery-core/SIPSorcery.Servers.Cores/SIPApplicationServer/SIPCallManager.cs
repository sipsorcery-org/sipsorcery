// ============================================================================
// FileName: SIPCallManager.cs
//
// Description:
// Manages established calls.
//
// Author(s):
// Aaron Clauson
//
// History:
// 10 Feb 2008  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    public class SIPCallManager : ISIPCallManager {
        private const int MAX_FORWARD_BINDINGS = 5;
        private const string WEB_CALLBACK_DIALPLAN_NAME = "webcallback";
        private const string MONITOR_CALLLIMITS_THREAD_NAME = "sipcallmanager-monitorcalls";
        private const string PROCESS_CALLS_THREAD_NAME_PREFIX = "sipcallmanager-processcalls";
        private const int MAX_QUEUEWAIT_PERIOD = 4000;              // Maximum time to wait to check the new calls queue if no events are received.
        private const int MAX_NEWCALL_QUEUE = 10;                   // Maximum number of new calls that will be queued for processing.

        private static ILog logger = AppState.logger;

        private static string m_userAgentString = SIPConstants.SIP_USERAGENT_STRING;
        private static string m_remoteHangupCause = SIPConstants.SIP_REMOTEHANGUP_CAUSE;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private string m_traceDirectory;
        private bool m_stop;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPAssetPersistor<SIPCDRAsset> m_sipCDRPersistor;
        private SIPAssetPersistor<Customer> m_customerPersistor;
        private SIPMonitorLogDelegate Log_External;
        private SIPAssetGetListDelegate<SIPProvider> GetSIPProviders_External;
        private SIPAssetGetDelegate<SIPDialPlan> GetDialPlan_External;                          // Function to load user dial plans.
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;                         // Function in authenticate user outgoing calls.
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetSIPAccountBindings_External;    // Function to lookup bindings that have been registered for a SIP account.
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;

        private Dictionary<string, string> m_inDialogueTransactions = new Dictionary<string, string>();     // <Forwarded transaction id, Origin transaction id>.
        //private List<string> m_reInvitedDialogues = new List<string>();                                   // <Dialogue Id being reinvited, Replacement Dialogue Id>.
        private Queue<SIPServerUserAgent> m_newCalls = new Queue<SIPServerUserAgent>();
        private AutoResetEvent m_newCallReady = new AutoResetEvent(false);
        private DialPlanEngine m_dialPlanEngine;

        public SIPCallManager(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPMonitorLogDelegate logDelegate,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPAssetPersistor<SIPCDRAsset> sipCDRPersistor,
            DialPlanEngine dialPlanEngine,
            SIPAssetGetDelegate<SIPDialPlan> getDialPlan,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getSIPAccountBindings,
            SIPAssetGetListDelegate<SIPProvider> getSIPProviders,
            GetCanonicalDomainDelegate getCanonicalDomain,
            SIPAssetPersistor<Customer> customerPersistor,
            string traceDirectory) {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            Log_External = logDelegate;
            m_sipDialoguePersistor = sipDialoguePersistor;
            m_sipCDRPersistor = sipCDRPersistor;
            m_dialPlanEngine = dialPlanEngine;
            GetDialPlan_External = getDialPlan;
            GetSIPAccount_External = getSIPAccount;
            GetSIPAccountBindings_External = getSIPAccountBindings;
            GetSIPProviders_External = getSIPProviders;
            GetCanonicalDomain_External = getCanonicalDomain;
            m_customerPersistor = customerPersistor;
            m_traceDirectory = traceDirectory;
        }

        public void Start() {
            ThreadPool.QueueUserWorkItem(delegate { MonitorCalls(); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessNewCalls(PROCESS_CALLS_THREAD_NAME_PREFIX + "-1"); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessNewCalls(PROCESS_CALLS_THREAD_NAME_PREFIX + "-2"); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessNewCalls(PROCESS_CALLS_THREAD_NAME_PREFIX + "-3"); });
        }

        public void Stop() {
            logger.Debug("SIPCallManager stopping.");
            m_stop = true;
            m_newCallReady.Set();
        }

        private void MonitorCalls() {
            try {
                Thread.CurrentThread.Name = MONITOR_CALLLIMITS_THREAD_NAME;

                while (!m_stop) {
                    try {
                        SIPDialogueAsset expiredCall = m_sipDialoguePersistor.Get(d => d.HangupAt <= DateTime.Now);
                        if (expiredCall != null) {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Hanging up expired call to " + expiredCall.RemoteTarget + " after " + expiredCall.CallDurationLimit + "s.", expiredCall.Owner));
                            expiredCall.SIPDialogue.Hangup(m_sipTransport);
                            CallHungup(expiredCall.SIPDialogue, "Call duration limit reached");
                        }
                    }
                    catch (Exception monitorExcp) {
                        logger.Error("Exception MonitorCalls Monitoring. " + monitorExcp.Message);
                    }

                    Thread.Sleep(1000);
                }

                logger.Warn("SIPCallManger MonitorCalls thread stopping.");
            }
            catch (Exception excp) {
                logger.Error("Exception MonitorCalls. " + excp.Message);
            }
        }

        private void ProcessNewCalls(string threadName) {
            try {
                Thread.CurrentThread.Name = threadName;

                while (!m_stop) {
                    while (m_newCalls.Count > 0) {
                        SIPServerUserAgent newCall = null;

                        lock (m_newCalls) {
                            newCall = m_newCalls.Dequeue();
                        }

                        if (newCall != null) {
                            if (newCall.CallDirection == SIPCallDirection.In) {
                                if (newCall.LoadSIPAccountForIncomingCall()) {
                                    ProcessNewCall(newCall);
                                }
                            }
                            else if (newCall.AuthenticateCall()) {
                                ProcessNewCall(newCall);
                            }
                        }
                    }

                    m_newCallReady.Reset();
                    m_newCallReady.WaitOne(MAX_QUEUEWAIT_PERIOD);
                }

                logger.Warn("SIPCallManager ProcessNewCalls thread stopping.");
            }
            catch (Exception excp) {
                logger.Error("Exception SIPCallManager ProcessNewCalls. " + excp.Message);
            }
        }

        public void QueueNewCall(SIPServerUserAgent serverUA) {
            try {
                if (m_newCalls.Count < MAX_NEWCALL_QUEUE) {
                    lock (m_newCalls) {
                        m_newCalls.Enqueue(serverUA);
                        m_newCallReady.Set();
                    }
                }
                else {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call Managre rejected call as new calls queue full.", null));
                    serverUA.Reject(SIPResponseStatusCodesEnum.TemporarilyNotAvailable, "Call Manager overloaded");
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPCallManager QueueNewCall. " + excp.Message);
            }
        }

        private void ProcessNewCall(SIPServerUserAgent uas) {
            UASInviteTransaction inviteTransaction = uas.SIPTransaction;

            try {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call Manager processing new call on thread " + Thread.CurrentThread.Name + " for " + inviteTransaction.TransactionRequest.URI.ToString() + ".", null));

                SIPURI callURI = inviteTransaction.TransactionRequest.URI;
                SIPCallDirection callDirection = uas.CallDirection; // Only outgoing calls will come through to here authenticated.
                SIPAccount sipAccount = uas.SIPAccount;

                if (sipAccount != null) {
                    if (sipAccount.IsDisabled) {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " is disabled for " + callDirection + " call.", sipAccount.Owner));
                        SIPResponse serverErrorResponse = SIPTransport.GetResponse(inviteTransaction.TransactionRequest, SIPResponseStatusCodesEnum.Forbidden, "SIP account disabled");
                        inviteTransaction.SendFinalResponse(serverErrorResponse);
                    }
                    else if (sipAccount.IsIncomingOnly && callDirection == SIPCallDirection.Out) {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " is not permitted to make outgoing calls", sipAccount.Owner));
                        SIPResponse serverErrorResponse = SIPTransport.GetResponse(inviteTransaction.TransactionRequest, SIPResponseStatusCodesEnum.Forbidden, "SIP account not permitted to make outgoing calls");
                        inviteTransaction.SendFinalResponse(serverErrorResponse);
                    }
                    else {
                        inviteTransaction.CDR.Owner = sipAccount.Owner;
                        string dialPlanName = (callDirection == SIPCallDirection.Out) ? sipAccount.OutDialPlanName : sipAccount.InDialPlanName;
                        SIPDialPlan dialPlan = GetDialPlan_External(d => d.Owner == sipAccount.Owner && d.DialPlanName == dialPlanName);

                        if (dialPlan != null) {
                            if (dialPlan.ExecutionCount >= dialPlan.MaxExecutionCount) {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Execution of dial plan " + dialPlan.DialPlanName + " was not processed as maximum execution count has been reached.", sipAccount.Owner));
                                SIPResponse serverErrorResponse = SIPTransport.GetResponse(inviteTransaction.TransactionRequest, SIPResponseStatusCodesEnum.TemporarilyNotAvailable, "Dial plan execution exceeded maximum allowed");
                                inviteTransaction.SendFinalResponse(serverErrorResponse);
                            }
                            else {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Using dialplan " + dialPlanName + " for " + callDirection + " call to " + callURI.ToString() + ".", sipAccount.Owner));

                                if (dialPlan.ScriptType == SIPDialPlanScriptTypesEnum.Asterisk) {
                                    DialPlanLineContext lineContext = new DialPlanLineContext(
                                        Log_External,
                                        m_sipTransport,
                                        CreateDialogueBridge,
                                        m_outboundProxy,
                                        inviteTransaction,
                                        dialPlan,
                                        GetSIPProviders_External(p => p.Owner == sipAccount.Owner, null, 0, Int32.MaxValue),
                                        m_traceDirectory,
                                        sipAccount.NetworkId);
                                    m_dialPlanEngine.Execute(lineContext, inviteTransaction, callDirection, CreateDialogueBridge, this);
                                }
                                else {
                                    DialPlanScriptContext scriptContext = new DialPlanScriptContext(
                                        Log_External,
                                        m_sipTransport,
                                        CreateDialogueBridge,
                                        m_outboundProxy,
                                        inviteTransaction,
                                        dialPlan,
                                        GetSIPProviders_External(p => p.Owner == sipAccount.Owner, null, 0, Int32.MaxValue),
                                        m_traceDirectory,
                                        sipAccount.NetworkId);
                                    m_dialPlanEngine.Execute(scriptContext, inviteTransaction, callDirection, CreateDialogueBridge, this);
                                }
                            }
                        }
                        else if (dialPlan == null && callDirection == SIPCallDirection.In) {
                            // The SIP account has no dialplan for an incoming call therefore send to the SIP account's bindings.
                            List<SIPRegistrarBinding> bindings = GetSIPAccountBindings_External(b => b.SIPAccountId == sipAccount.Id, null, 0, MAX_FORWARD_BINDINGS);
                            if (bindings != null && bindings.Count > 0) {
                                // Create a pseudo-dialplan to process the incoming call.
                                string pseudoScript = "sys.Dial(\"" + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + "\")\n";
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding incoming call for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " to " + bindings.Count + " bindings.", sipAccount.Owner));
                                SIPDialPlan incomingDialPlan = new SIPDialPlan(sipAccount.Owner, null, null, pseudoScript, SIPDialPlanScriptTypesEnum.Ruby);
                                DialPlanScriptContext scriptContext = new DialPlanScriptContext(
                                        Log_External,
                                        m_sipTransport,
                                        CreateDialogueBridge,
                                        m_outboundProxy,
                                        inviteTransaction,
                                        incomingDialPlan,
                                        null,
                                        m_traceDirectory,
                                        sipAccount.NetworkId);
                                m_dialPlanEngine.Execute(scriptContext, inviteTransaction, callDirection, CreateDialogueBridge, this);
                            }
                            else {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No bindings available for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " returning temporarily not available.", sipAccount.Owner));
                                SIPResponse serverErrorResponse = SIPTransport.GetResponse(inviteTransaction.TransactionRequest, SIPResponseStatusCodesEnum.TemporarilyNotAvailable, null);
                                inviteTransaction.SendFinalResponse(serverErrorResponse);
                            }
                        }
                        else {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dialplan could not be loaded for " + callDirection + " call to " + callURI.ToString() + ".", sipAccount.Owner));
                            SIPResponse serverErrorResponse = SIPTransport.GetResponse(inviteTransaction.TransactionRequest, SIPResponseStatusCodesEnum.InternalServerError, "Error loading dial plan");
                            inviteTransaction.SendFinalResponse(serverErrorResponse);
                        }
                    }
                }
                else {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIP account could not be loaded for " + callDirection + " call to " + callURI.ToString() + ".", null));
                    SIPResponse serverErrorResponse = SIPTransport.GetResponse(inviteTransaction.TransactionRequest, SIPResponseStatusCodesEnum.InternalServerError, "Error loading SIP account");
                    inviteTransaction.SendFinalResponse(serverErrorResponse);
                }
            }
            catch (Exception excp) {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Exception SIPCallManager ProcessNewCall. " + excp.Message, null));
                SIPResponse serverErrorResponse = SIPTransport.GetResponse(inviteTransaction.TransactionRequest, SIPResponseStatusCodesEnum.InternalServerError, "Exception ProcessNewCall");
                inviteTransaction.SendFinalResponse(serverErrorResponse);
            }
        }

        public string ProcessWebCallback(string username, string number) {
            try {
                Customer customer = m_customerPersistor.Get(c => c.CustomerUsername == username);
                if (customer == null) {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Web Callback rejected for " + username + " and " + number + ", as no matching customer.", null));
                    return "Sorry no matching user was found, the callback was not initiated.";
                }
                else {
                    SIPDialPlan webCallbackDialPlan = GetDialPlan_External(d => d.Owner == username && d.DialPlanName == WEB_CALLBACK_DIALPLAN_NAME);
                    if (webCallbackDialPlan == null) {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Web Callback rejected as no " + WEB_CALLBACK_DIALPLAN_NAME + " dialplan exists.", username));
                        return "Sorry the specified user has not enabled callbacks, the callback was not initiated.";
                    }
                    else {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Web Callback initialising to " + number + ".", username));

                        UASInviteTransaction dummyTransaction = GetDummyWebCallbackTransaction(number);
                        DialPlanScriptContext scriptContext = new DialPlanScriptContext(
                                Log_External,
                                m_sipTransport,
                                CreateDialogueBridge,
                                m_outboundProxy,
                                dummyTransaction,
                                webCallbackDialPlan,
                                GetSIPProviders_External(p => p.Owner == username, null, 0, Int32.MaxValue),
                                m_traceDirectory,
                                null);
                        m_dialPlanEngine.Execute(scriptContext, dummyTransaction, SIPCallDirection.Out, CreateDialogueBridge, this);

                        return "Callback was successfully initiated.";
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPCallManager ProcessWebCallback. " + excp.Message);
                return "Sorry there was an unexpected error, the callback was not initiated.";
            }
        }

        private UASInviteTransaction GetDummyWebCallbackTransaction(string number) {
            SIPRequest dummyInvite = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURIRelaxed(number + "@sipsorcery.com"));
            SIPHeader dummyHeader = new SIPHeader("<sip:anon@sipsorcery.com>", "<sip:anon@sipsorcery.com>", 1, CallProperties.CreateNewCallId());
            dummyHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            dummyHeader.Vias.PushViaHeader(new SIPViaHeader("127.0.0.1:5090", CallProperties.CreateBranchId()));
            dummyInvite.Header = dummyHeader;
            UASInviteTransaction dummyTransaction = m_sipTransport.CreateUASTransaction(dummyInvite, SIPEndPoint.ParseSIPEndPoint("127.0.0.1"), SIPEndPoint.ParseSIPEndPoint("127.0.0.1"), null);
            return dummyTransaction;

        }

        public void CreateDialogueBridge(SIPDialogue clientDiaglogue, SIPDialogue forwardedDialogue, string owner) {
            Guid bridgeId = Guid.NewGuid();
            clientDiaglogue.BridgeId = bridgeId;
            forwardedDialogue.BridgeId = bridgeId;

            AddDialogue(clientDiaglogue);
            AddDialogue(forwardedDialogue);

            SIPEndPoint clientDialogueRemoteEP = (IPSocket.IsIPSocket(clientDiaglogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(clientDiaglogue.RemoteTarget.Host) : null;
            Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueCreated, clientDiaglogue.Owner, clientDialogueRemoteEP, clientDiaglogue.DialogueId));

            SIPEndPoint forwardedDialogueRemoteEP = (IPSocket.IsIPSocket(forwardedDialogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(forwardedDialogue.RemoteTarget.Host) : null;
            Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueCreated, forwardedDialogue.Owner, forwardedDialogueRemoteEP, forwardedDialogue.DialogueId));
        }

        public void CallHungup(SIPDialogue sipDialogue, string hangupCause) {
            try {
                if (sipDialogue != null && sipDialogue.BridgeId != Guid.Empty) {
                    SIPDialogue orphanedDialogue = GetOppositeDialogue(sipDialogue);

                    // Update CDR's.
                    if (sipDialogue.CDRId != Guid.Empty) {
                        SIPCDRAsset cdr = m_sipCDRPersistor.Get(sipDialogue.CDRId);
                        if (cdr != null) {
                            cdr.BridgeId = sipDialogue.BridgeId.ToString();
                            cdr.Hungup(hangupCause);
                        }
                    }
                    else {
                        logger.Warn("There was no CDR attached to local dialogue in SIPCallManager CallHungup.");
                    }

                    if (orphanedDialogue.CDRId != Guid.Empty) {
                        SIPCDRAsset cdr = m_sipCDRPersistor.Get(orphanedDialogue.CDRId);
                        if (cdr != null) {
                            cdr.BridgeId = orphanedDialogue.BridgeId.ToString();
                            cdr.Hungup(m_remoteHangupCause);
                        }
                    }
                    else {
                        logger.Warn("There was no CDR attached to orphaned dialogue in SIPCallManager CallHungup.");
                    }

                    orphanedDialogue.Hangup();

                    m_sipDialoguePersistor.Delete(new SIPDialogueAsset(sipDialogue));
                    m_sipDialoguePersistor.Delete(new SIPDialogueAsset(orphanedDialogue));

                    SIPEndPoint hungupDialogueRemoteEP = (IPSocket.IsIPSocket(sipDialogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(sipDialogue.RemoteTarget.Host) : null;
                    Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved, sipDialogue.Owner, hungupDialogueRemoteEP, sipDialogue.DialogueId));

                    SIPEndPoint orphanedDialogueRemoteEP = (IPSocket.IsIPSocket(orphanedDialogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(orphanedDialogue.RemoteTarget.Host) : null;
                    Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved, orphanedDialogue.Owner, orphanedDialogueRemoteEP, orphanedDialogue.DialogueId));
                }
                else {
                    logger.Warn("No bridge could be found for hungup call.");
                }

                //DumpStoredDialogues();
            }
            catch (Exception excp) {
                logger.Error("Exception CallManager CallHungup. " + excp.Message);
            }
        }

        /// <summary>
        /// Attempts to locate a dialogue for an in-dialogue transaction.
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public SIPDialogue GetDialogue(SIPRequest sipRequest, SIPEndPoint remoteEndPoint) {
            try {
                string callId = sipRequest.Header.CallId;
                string localTag = sipRequest.Header.To.ToTag;
                string remoteTag = sipRequest.Header.From.FromTag;

                return GetDialogue(callId, localTag, remoteTag, remoteEndPoint);
            }
            catch (Exception excp) {
                logger.Error("Exception GetDialogue. " + excp);
                return null;
            }
        }

        public SIPDialogue GetDialogue(string callId, string localTag, string remoteTag, SIPEndPoint remoteEndPoint) {
            try {
                SIPDialogue sipDialogue = null;
                string dialogueId = SIPDialogue.GetDialogueId(callId, localTag, remoteTag);
                SIPDialogueAsset dialogueAsset = m_sipDialoguePersistor.Get(d => d.DialogueId == dialogueId);

                if (dialogueAsset != null) {
                    sipDialogue = dialogueAsset.SIPDialogue;
                }
                else {
                    //logger.Warn("The CallManager could not locate a dialogue based on the dialogue id. Checking local tag only.");

                    // Try on To tag.
                    dialogueAsset = m_sipDialoguePersistor.Get(d => d.LocalTag == localTag);
                    if (dialogueAsset != null) {
                        logger.Warn("CallManager dialogue match found on To tag (" + remoteEndPoint.ToString() + ").");
                        sipDialogue = dialogueAsset.SIPDialogue;
                    }

                    // Try on From tag.
                    dialogueAsset = m_sipDialoguePersistor.Get(d => d.RemoteTag == remoteTag);
                    if (dialogueAsset != null) {
                        logger.Warn("CallManager dialogue match found on From tag (" + remoteEndPoint.ToString() + ").");
                        sipDialogue = dialogueAsset.SIPDialogue;
                    }

                    // As an experiment will try on the Call-ID as well. However as a safeguard it will only succeed if there is only one instance of the
                    // Call-ID in use. Since the Call-ID is not mandated by the SIP standard as being unique there it may be that matching on it causes more
                    // problems then it solves.
                    dialogueAsset = m_sipDialoguePersistor.Get(d => d.CallId == callId);
                    if (dialogueAsset != null) {
                        logger.Warn("The Call-ID match mechanism matched in the CallManager for a request from " + remoteEndPoint + ".");
                        sipDialogue = dialogueAsset.SIPDialogue;
                    }
                }

                if (sipDialogue != null) {
                    sipDialogue.m_sipTransport = m_sipTransport;
                    return sipDialogue;
                }
                else {
                    return null;
                }
            }
            catch (Exception excp) {
                logger.Error("Exception GetDialogue. " + excp);
                return null;
            }
        }

        /// <summary>
        /// Retrieves the other end of a call given the dialogue from one end.
        /// </summary>
        /// <param name="dialogue"></param>
        /// <returns></returns>
        public SIPDialogue GetOppositeDialogue(SIPDialogue dialogue) {
            if (dialogue.BridgeId != Guid.Empty) {
                string bridgeIdString = dialogue.BridgeId.ToString();
                SIPDialogueAsset dialogueAsset = m_sipDialoguePersistor.Get(d => d.BridgeId == bridgeIdString && d.DialogueId != dialogue.DialogueId);
                SIPDialogue sipDialogue = (dialogueAsset != null) ? dialogueAsset.SIPDialogue : null;
                if (sipDialogue != null) {
                    sipDialogue.m_sipTransport = m_sipTransport;
                }
                return sipDialogue;
            }
            else {
                return null;
            }
        }

        private void AddDialogue(SIPDialogue dialogue) {
            m_sipDialoguePersistor.Add(new SIPDialogueAsset(dialogue));
        }

        /// <summary>
        /// Attempts to reinvite an existing end of a call by sending a new SDP.
        /// </summary>
        /// <param name="dialogue">The dialogue describing the end of the call to be re-invited.</param>
        /// <param name="newSDP">The session description for the new dialogue desired.</param>
        public void ReInvite(SIPDialogue dialogue, string replacementSDP) {
            try {
                dialogue.CSeq = dialogue.CSeq + 1;
                m_sipDialoguePersistor.Update(new SIPDialogueAsset(dialogue));
                SIPEndPoint localSIPEndPoint = m_sipTransport.GetDefaultTransportContact(SIPProtocolsEnum.udp);
                SIPRequest reInviteReq = GetInviteRequest(dialogue, localSIPEndPoint, replacementSDP);
                SIPEndPoint reinviteEndPoint = m_sipTransport.GetRequestEndPoint(reInviteReq, m_outboundProxy, true);

                if (reinviteEndPoint != null) {
                    UACInviteTransaction reInviteTransaction = m_sipTransport.CreateUACTransaction(reInviteReq, reinviteEndPoint, localSIPEndPoint, m_outboundProxy);
                    reInviteTransaction.UACInviteTransactionFinalResponseReceived += new SIPTransactionResponseReceivedDelegate(ReInviteTransactionFinalResponseReceived);
                    reInviteTransaction.SendInviteRequest(reinviteEndPoint, reInviteReq);
                }
                else {
                    throw new ApplicationException("Could not forward re-invite as request end point could not be determined.\r\n" + reInviteReq.ToString());
                }
            }
            catch (Exception excp) {
                logger.Error("Exception CallManager ReInvite. " + excp.Message);
                throw excp;
            }
        }

        private void ReInviteTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) {
            try {
                SIPRequest inviteRequest = sipTransaction.TransactionRequest;
                string dialogueId = GetDialogue(inviteRequest.Header.CallId, inviteRequest.Header.From.FromTag, inviteRequest.Header.To.ToTag, remoteEndPoint).DialogueId;
                //m_dialogueBridges[dialogueId] = m_reInvitedDialogues[dialogueId];
                //m_reInvitedDialogues.Remove(dialogueId);
            }
            catch (Exception excp) {
                logger.Error("Exception ReInviteTransactionFinalResponseReceived. " + excp.Message);
                throw excp;
            }
        }

        public void ForwardInDialogueRequest(SIPDialogue dialogue, SIPTransaction inDialogueTransaction, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint) {
            try {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "In dialogue request " + inDialogueTransaction.TransactionRequest.Method + " received from for uri=" + inDialogueTransaction.TransactionRequest.URI.ToString() + ".", null));

                // Update the CSeq based on the latest received request.
                dialogue.CSeq = inDialogueTransaction.TransactionRequest.Header.CSeq;

                // Get the dialogue for the other end of the bridge.
                SIPDialogue bridgedDialogue = GetOppositeDialogue(dialogue);

                SIPEndPoint forwardSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint(new SIPEndPoint(bridgedDialogue.RemoteTarget));
                IPAddress remoteUAIPAddress = (inDialogueTransaction.TransactionRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? remoteEndPoint.SocketEndPoint.Address : SIPEndPoint.ParseSIPEndPoint(inDialogueTransaction.TransactionRequest.Header.ProxyReceivedFrom).SocketEndPoint.Address;

                SIPRequest forwardedRequest = inDialogueTransaction.TransactionRequest.Copy();
                forwardedRequest.URI = bridgedDialogue.RemoteTarget;
                forwardedRequest.Header.Routes = bridgedDialogue.RouteSet;
                forwardedRequest.Header.CallId = bridgedDialogue.CallId;
                bridgedDialogue.CSeq = bridgedDialogue.CSeq + 1;
                forwardedRequest.Header.CSeq = bridgedDialogue.CSeq;
                forwardedRequest.Header.To = new SIPToHeader(bridgedDialogue.RemoteUserField.Name, bridgedDialogue.RemoteUserField.URI, bridgedDialogue.RemoteTag);
                forwardedRequest.Header.From = new SIPFromHeader(bridgedDialogue.LocalUserField.Name, bridgedDialogue.LocalUserField.URI, bridgedDialogue.LocalTag);
                forwardedRequest.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(bridgedDialogue.RemoteTarget.Scheme, forwardSIPEndPoint)) };
                forwardedRequest.Header.Vias = new SIPViaSet();
                forwardedRequest.Header.Vias.PushViaHeader(new SIPViaHeader(forwardSIPEndPoint, CallProperties.CreateBranchId()));
                forwardedRequest.Header.UserAgent = m_userAgentString;
                forwardedRequest.Header.ProxyReceivedFrom = null;
                forwardedRequest.Header.ProxyReceivedOn = null;

                if (inDialogueTransaction.TransactionRequest.Body != null && inDialogueTransaction.TransactionRequest.Method == SIPMethodsEnum.INVITE) {
                    bool wasMangled = false;
                    forwardedRequest.Body = SIPPacketMangler.MangleSDP(inDialogueTransaction.TransactionRequest.Body, remoteUAIPAddress.ToString(), out wasMangled);
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Re-INVITE wasmangled=" + wasMangled + " remote=" + remoteUAIPAddress.ToString() + ".", null));
                    forwardedRequest.Header.ContentLength = forwardedRequest.Body.Length;
                }

                SIPEndPoint forwardEndPoint = m_sipTransport.GetRequestEndPoint(forwardedRequest, m_outboundProxy, true);

                if (forwardEndPoint != null) {
                    if (inDialogueTransaction.TransactionRequest.Method == SIPMethodsEnum.INVITE) {
                        UACInviteTransaction forwardedTransaction = m_sipTransport.CreateUACTransaction(forwardedRequest, forwardEndPoint, localSIPEndPoint, m_outboundProxy);
                        forwardedTransaction.CDR = null;    // Don't want CDR's on re-INVITES.
                        forwardedTransaction.UACInviteTransactionFinalResponseReceived += InDialogueTransactionFinalResponseReceived;
                        forwardedTransaction.UACInviteTransactionInformationResponseReceived += InDialogueTransactionInfoResponseReceived;
                        forwardedTransaction.TransactionRemoved += InDialogueTransactionRemoved;

                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding re-INVITE from " + remoteEndPoint + " to " + forwardedRequest.URI.ToString() + ", first hop " + forwardEndPoint + ".", dialogue.Owner));

                        forwardedTransaction.SendReliableRequest();

                        lock (m_inDialogueTransactions) {
                            m_inDialogueTransactions.Add(forwardedTransaction.TransactionId, inDialogueTransaction.TransactionId);
                        }
                    }
                    else {
                        SIPNonInviteTransaction forwardedTransaction = m_sipTransport.CreateNonInviteTransaction(forwardedRequest, forwardEndPoint, localSIPEndPoint, m_outboundProxy);
                        forwardedTransaction.NonInviteTransactionFinalResponseReceived += InDialogueTransactionFinalResponseReceived;
                        forwardedTransaction.NonInviteTransactionInfoResponseReceived += InDialogueTransactionInfoResponseReceived;
                        forwardedTransaction.TransactionRemoved += InDialogueTransactionRemoved;

                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding in dialogue " + forwardedRequest.Method + " from " + remoteEndPoint + " to " + forwardedRequest.URI.ToString() + ", first hop " + forwardEndPoint + ".", dialogue.Owner));

                        forwardedTransaction.SendReliableRequest();

                        lock (m_inDialogueTransactions) {
                            m_inDialogueTransactions.Add(forwardedTransaction.TransactionId, inDialogueTransaction.TransactionId);
                        }
                    }

                    // Update the dialogues CSeqs so future in dialogue requests can be forwarded correctly.
                    m_sipDialoguePersistor.UpdateProperty(bridgedDialogue.Id, "CSeq", bridgedDialogue.CSeq);
                    m_sipDialoguePersistor.UpdateProperty(dialogue.Id, "CSeq", dialogue.CSeq);
                }
                else {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not forward in dialogue request end point could not be determined " + forwardedRequest.URI.ToString() + ".", dialogue.Owner));
                }
            }
            catch (Exception excp) {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Exception forwarding in dialogue request. " + excp.Message, dialogue.Owner));
            }
        }

        private void InDialogueTransactionInfoResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) {
            SIPDialogue dialogue = GetDialogue(sipResponse.Header.CallId, sipResponse.Header.To.ToTag, sipResponse.Header.From.FromTag, remoteEndPoint);
            string owner = (dialogue != null) ? dialogue.Owner : null;

            try {
                // Lookup the originating transaction.
                SIPTransaction originTransaction = m_sipTransport.GetTransaction(m_inDialogueTransactions[sipTransaction.TransactionId]);

                SIPResponse response = sipResponse.Copy();
                response.Header.Vias = originTransaction.TransactionRequest.Header.Vias;
                response.Header.To = originTransaction.TransactionRequest.Header.To;
                response.Header.From = originTransaction.TransactionRequest.Header.From;
                response.Header.CallId = originTransaction.TransactionRequest.Header.CallId;
                response.Header.CSeq = originTransaction.TransactionRequest.Header.CSeq;
                response.Header.Contact = new List<SIPContactHeader>();
                response.Header.Contact = SIPContactHeader.CreateSIPContactList(new SIPURI(originTransaction.TransactionRequest.URI.Scheme, localSIPEndPoint));
                response.Header.RecordRoutes = null;    // Can't change route set within a dialogue.
                response.Header.UserAgent = m_userAgentString;

                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding in dialogue from " + remoteEndPoint + " " + sipResponse.Header.CSeqMethod + " info response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " to " + response.Header.Vias.TopViaHeader.ReceivedFromAddress + ".", owner));

                // Forward the response back to the requester.
                originTransaction.SendInformationalResponse(response);
            }
            catch (Exception excp) {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Exception processing in dialogue " + sipResponse.Header.CSeqMethod + " info response. " + excp.Message, owner));
            }
        }

        private void InDialogueTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) {
            SIPDialogue dialogue = GetDialogue(sipResponse.Header.CallId, sipResponse.Header.To.ToTag, sipResponse.Header.From.FromTag, remoteEndPoint);
            string owner = (dialogue != null) ? dialogue.Owner : null;

            try {
                logger.Debug("Final response on " + sipResponse.Header.CSeqMethod + " in-dialogue transaction.");

                // Lookup the originating transaction.
                SIPTransaction originTransaction = m_sipTransport.GetTransaction(m_inDialogueTransactions[sipTransaction.TransactionId]);
                IPAddress remoteUAIPAddress = (sipResponse.Header.ProxyReceivedFrom.IsNullOrBlank()) ? remoteEndPoint.SocketEndPoint.Address : SIPEndPoint.ParseSIPEndPoint(sipResponse.Header.ProxyReceivedFrom).SocketEndPoint.Address;
                SIPEndPoint forwardSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint(sipResponse.Header.Vias.TopViaHeader.Transport);

                SIPResponse response = sipResponse.Copy();
                response.Header.Vias = originTransaction.TransactionRequest.Header.Vias;
                response.Header.To = originTransaction.TransactionRequest.Header.To;
                response.Header.From = originTransaction.TransactionRequest.Header.From;
                response.Header.CallId = originTransaction.TransactionRequest.Header.CallId;
                response.Header.CSeq = originTransaction.TransactionRequest.Header.CSeq;
                response.Header.Contact = new List<SIPContactHeader>();
                response.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(originTransaction.TransactionRequest.URI.Scheme, forwardSIPEndPoint)) };
                response.Header.RecordRoutes = null;    // Can't change route set within a dialogue.
                response.Header.UserAgent = m_userAgentString;

                if (sipResponse.Body != null && sipResponse.Header.CSeqMethod == SIPMethodsEnum.INVITE) {
                    bool wasMangled = false;
                    response.Body = SIPPacketMangler.MangleSDP(sipResponse.Body, remoteUAIPAddress.ToString(), out wasMangled);
                    response.Header.ContentLength = response.Body.Length;
                }

                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding in dialogue  from " + remoteEndPoint + " " + sipResponse.Header.CSeqMethod + " final response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " to " + response.Header.Vias.TopViaHeader.ReceivedFromAddress + ".", owner));

                // Forward the response back to the requester.
                originTransaction.SendFinalResponse(response);
            }
            catch (Exception excp) {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Exception processing in dialogue " + sipResponse.Header.CSeqMethod + " final response. " + excp.Message, owner));
            }
        }

        private void InDialogueTransactionRemoved(SIPTransaction sipTransaction) {
            try {
                if (m_inDialogueTransactions.ContainsKey(sipTransaction.TransactionId)) {
                    lock (m_inDialogueTransactions) {
                        m_inDialogueTransactions.Remove(sipTransaction.TransactionId);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception InDialogueTransactionStateChanged. " + excp);
            }
        }

        private SIPRequest GetInviteRequest(SIPDialogue dialogue, SIPEndPoint localSIPEndPoint, string body) {
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, dialogue.RemoteTarget);
            SIPProtocolsEnum protocol = inviteRequest.URI.Protocol;

            SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader(dialogue.LocalUserField.ToString()), SIPToHeader.ParseToHeader(dialogue.RemoteUserField.ToString()), dialogue.CSeq, dialogue.CallId);
            SIPURI contactURI = new SIPURI(dialogue.RemoteTarget.Scheme, localSIPEndPoint);
            inviteHeader.Contact = SIPContactHeader.ParseContactHeader("<" + contactURI.ToString() + ">");
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteRequest.Header = inviteHeader;
            inviteRequest.Header.Routes = dialogue.RouteSet;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
            inviteRequest.Header.Vias.PushViaHeader(viaHeader);

            inviteRequest.Body = body;
            inviteRequest.Header.ContentLength = body.Length;
            inviteRequest.Header.ContentType = "application/sdp";

            return inviteRequest;
        }

        public void Transfer(SIPDialogue dialogue, SIPNonInviteTransaction referTransaction) {
            logger.Debug("Transfer requested to " + referTransaction.TransactionRequest.Header.ReferTo + ".");

            SIPResponse errorResponse = SIPTransport.GetResponse(referTransaction.TransactionRequest, SIPResponseStatusCodesEnum.NotImplemented, null);
            m_sipTransport.SendResponse(errorResponse);
        }

        public int GetCurrentCallCount(string owner) {
            try {
                return m_sipDialoguePersistor.Count(d => d.Owner == owner);
            }
            catch (Exception excp) {
                logger.Error("Exception GetCurrentCallCount. " + excp.Message);
                return -1;
            }
        }
    }
}
