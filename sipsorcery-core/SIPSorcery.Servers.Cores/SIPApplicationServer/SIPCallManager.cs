// ============================================================================
// FileName: SIPCallManager.cs
//
// Description:
// Processes new SIP calls.
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
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Configuration;
using System.Text;
using System.Threading;
using System.Transactions;
using SIPSorcery.AppServer.DialPlan;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;

namespace SIPSorcery.Servers
{
    public class SIPCallManager : ISIPCallManager
    {
        private const int MAX_FORWARD_BINDINGS = 5;
        private const string MONITOR_CALLLIMITS_THREAD_NAME = "sipcallmanager-monitorcalls";
        private const string PROCESS_CALLS_THREAD_NAME_PREFIX = "sipcallmanager-processcalls";
        private const int MAX_QUEUEWAIT_PERIOD = 4000;              // Maximum time to wait to check the new calls queue if no events are received.
        private const int MAX_NEWCALL_QUEUE = 10;                   // Maximum number of new calls that will be queued for processing.
        public const string DISPATCHER_SIPACCOUNT_NAME = "dispatcher";
        private const int MAX_CALLBACK_WAIT_SECONDS = 30;
        private const int EXPIRECALL_HANGUP_FAILURE_RETRY = 60;     // When a call is hungup due to time limit being reached a new hangup time will be set in case this server agent crashes.
        private const string DISPATCHER_CONTRACT_NAME = "SIPSorcery.Web.Services.ICallDispatcherService";
        private const int RETRY_FAILED_PROXY = 20000;

        private static ILog logger = AppState.logger;

        private static readonly string m_sipDialPlanExecutionCountPropertyName = SIPDialPlan.PROPERTY_EXECUTIONCOUNT_NAME;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private SIPDialogueManager m_sipDialogueManager;
        private string m_traceDirectory;
        private bool m_monitorCalls;        // If true this call manager instance will monitor the sip dialogues table and hangup any expired calls.
        private bool m_stop;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPAssetPersistor<SIPCDRAsset> m_sipCDRPersistor;
        private SIPAssetPersistor<Customer> m_customerPersistor;
        private SIPAssetPersistor<SIPDialPlan> m_dialPlanPersistor;
        private SIPMonitorLogDelegate Log_External;
        private SIPAssetGetListDelegate<SIPProvider> GetSIPProviders_External;
        private SIPAssetGetDelegate<SIPDialPlan> GetDialPlan_External;                          // Function to load user dial plans.
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;                         // Function in authenticate user outgoing calls.
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetSIPAccountBindings_External;    // Function to lookup bindings that have been registered for a SIP account.
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;

        private Dictionary<string, string> m_inDialogueTransactions = new Dictionary<string, string>();     // <Forwarded transaction id, Origin transaction id>.
        private Queue<ISIPServerUserAgent> m_newCalls = new Queue<ISIPServerUserAgent>();
        private AutoResetEvent m_newCallReady = new AutoResetEvent(false);
        private DialPlanEngine m_dialPlanEngine;

        private Dictionary<string, CallDispatcherProxy> m_dispatcherProxy = new Dictionary<string, CallDispatcherProxy>(); // [config name, proxy].
        private Dictionary<string, CallbackWaiter> m_waitingForCallbacks = new Dictionary<string, CallbackWaiter>();

        public SIPCallManager(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPMonitorLogDelegate logDelegate,
            SIPDialogueManager sipDialogueManager,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPAssetPersistor<SIPCDRAsset> sipCDRPersistor,
            DialPlanEngine dialPlanEngine,
            SIPAssetGetDelegate<SIPDialPlan> getDialPlan,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getSIPAccountBindings,
            SIPAssetGetListDelegate<SIPProvider> getSIPProviders,
            GetCanonicalDomainDelegate getCanonicalDomain,
            SIPAssetPersistor<Customer> customerPersistor,
            SIPAssetPersistor<SIPDialPlan> dialPlanPersistor,
            string traceDirectory,
            bool monitorCalls)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            Log_External = logDelegate;
            m_sipDialogueManager = sipDialogueManager;
            m_sipDialoguePersistor = sipDialoguePersistor;
            m_sipCDRPersistor = sipCDRPersistor;
            m_dialPlanEngine = dialPlanEngine;
            GetDialPlan_External = getDialPlan;
            GetSIPAccount_External = getSIPAccount;
            GetSIPAccountBindings_External = getSIPAccountBindings;
            GetSIPProviders_External = getSIPProviders;
            GetCanonicalDomain_External = getCanonicalDomain;
            m_customerPersistor = customerPersistor;
            m_dialPlanPersistor = dialPlanPersistor;
            m_traceDirectory = traceDirectory;
            m_monitorCalls = monitorCalls;
        }

        public void Start()
        {
            InitialiseDispatcherProxies();

            if (m_monitorCalls)
            {
                ThreadPool.QueueUserWorkItem(delegate { MonitorCalls(); });
            }
            ThreadPool.QueueUserWorkItem(delegate { ProcessNewCalls(PROCESS_CALLS_THREAD_NAME_PREFIX + "-1"); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessNewCalls(PROCESS_CALLS_THREAD_NAME_PREFIX + "-2"); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessNewCalls(PROCESS_CALLS_THREAD_NAME_PREFIX + "-3"); });
        }

        public void Stop()
        {
            logger.Debug("SIPCallManager stopping.");
            m_stop = true;
            m_newCallReady.Set();
        }

        private void MonitorCalls()
        {
            try
            {
                Thread.CurrentThread.Name = MONITOR_CALLLIMITS_THREAD_NAME;

                while (!m_stop)
                {
                    try
                    {
                        SIPDialogueAsset expiredCall = GetNextExpiredCall();
                        if (expiredCall != null)
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Hanging up expired call to " + expiredCall.RemoteTarget + " after " + expiredCall.CallDurationLimit + "s.", expiredCall.Owner));
                            expiredCall.SIPDialogue.Hangup(m_sipTransport, m_outboundProxy);
                            m_sipDialogueManager.CallHungup(expiredCall.SIPDialogue, "Call duration limit reached");
                        }
                    }
                    catch (Exception monitorExcp)
                    {
                        logger.Error("Exception MonitorCalls Monitoring. " + monitorExcp.Message);
                    }

                    Thread.Sleep(1000);
                }

                logger.Warn("SIPCallManger MonitorCalls thread stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception MonitorCalls. " + excp.Message);
            }
        }

        private SIPDialogueAsset GetNextExpiredCall()
        {
            //logger.Debug("GetNextExpiredCall");

            using (var trans = new TransactionScope())
            {
                SIPDialogueAsset expiredCall = m_sipDialoguePersistor.Get(d => d.HangupAt <= DateTimeOffset.UtcNow);

                if (expiredCall != null)
                {
                    //logger.Debug("GetNextExpiredCall, expired call found " + expiredCall.RemoteTarget + ".");

                    m_sipDialoguePersistor.UpdateProperty(expiredCall.Id, "HangupAt", DateTimeOffset.UtcNow.AddSeconds(EXPIRECALL_HANGUP_FAILURE_RETRY));
                }

                trans.Complete();

                return expiredCall;
            }
        }

        public void QueueNewCall(ISIPServerUserAgent serverUA)
        {
            try
            {
                // Attempt to queue the call.
                if (m_newCalls.Count < MAX_NEWCALL_QUEUE)
                {
                    lock (m_newCalls)
                    {
                        m_newCalls.Enqueue(serverUA);
                        m_newCallReady.Set();
                    }
                }
                else
                {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call Manager rejected call as new calls queue full.", null));
                    serverUA.Reject(SIPResponseStatusCodesEnum.TemporarilyNotAvailable, "Call Manager overloaded", null);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCallManager QueueNewCall. " + excp.Message);
            }
        }

        private void ProcessNewCalls(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                while (!m_stop)
                {
                    while (m_newCalls.Count > 0)
                    {
                        ISIPServerUserAgent newCall = null;

                        lock (m_newCalls)
                        {
                            newCall = m_newCalls.Dequeue();
                        }

                        if (newCall != null)
                        {
                            if (newCall.CallDirection == SIPCallDirection.In)
                            {
                                if (newCall.CallRequest.URI.User == DISPATCHER_SIPACCOUNT_NAME)
                                {
                                    // This is a special system account used by monitoring or dispatcher agents that want to check
                                    // the state of the application server.
                                    newCall.NoCDR();
                                    ProcessNewCall(newCall);
                                }
                                else
                                {
                                    bool loadInAccountResult = newCall.LoadSIPAccountForIncomingCall();

                                    if (loadInAccountResult && newCall.SIPAccount != null)
                                    {
                                        #region Check if this call is being waited for by a dialplan application.

                                        CallbackWaiter matchingApp = null;

                                        try
                                        {
                                            if (m_waitingForCallbacks.Count > 0)
                                            {
                                                List<CallbackWaiter> expiredWaiters = new List<CallbackWaiter>();
                                                lock (m_waitingForCallbacks)
                                                {
                                                    foreach (CallbackWaiter waitingApp in m_waitingForCallbacks.Values)
                                                    {
                                                        if (DateTime.Now.Subtract(waitingApp.Added).TotalSeconds > MAX_CALLBACK_WAIT_SECONDS)
                                                        {
                                                            expiredWaiters.Add(waitingApp);
                                                        }
                                                        else
                                                        {
                                                            bool match = waitingApp.IsMyCall(newCall);
                                                            if (match)
                                                            {
                                                                matchingApp = waitingApp;
                                                                break;
                                                            }
                                                        }
                                                    }

                                                    if (expiredWaiters.Count > 0)
                                                    {
                                                        foreach (CallbackWaiter expiredApp in expiredWaiters)
                                                        {
                                                            m_waitingForCallbacks.Remove(expiredApp.UniqueId);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception callbackExcp)
                                        {
                                            logger.Error("Exception ProcessNewCalls Checking Callbacks. " + callbackExcp.Message);
                                        }

                                        #endregion

                                        if (matchingApp != null)
                                        {
                                            lock (m_waitingForCallbacks)
                                            {
                                                m_waitingForCallbacks.Remove(matchingApp.UniqueId);
                                            }
                                        }
                                        else
                                        {
                                            ProcessNewCall(newCall);
                                        }
                                    }
                                    else
                                    {
                                        logger.Debug("ProcessNewCalls could not load incoming SIP Account for " + newCall.CallRequest.URI.ToString() + ".");
                                        newCall.Reject(SIPResponseStatusCodesEnum.NotFound, null, null);
                                    }
                                }
                            }
                            else if (newCall.AuthenticateCall())
                            {
                                ProcessNewCall(newCall);
                            }
                        }
                    }

                    m_newCallReady.Reset();
                    m_newCallReady.WaitOne(MAX_QUEUEWAIT_PERIOD);
                }

                logger.Warn("SIPCallManager ProcessNewCalls thread stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCallManager ProcessNewCalls. " + excp.Message);
            }
        }

        private void ProcessNewCall(ISIPServerUserAgent uas)
        {
            bool wasExecutionCountIncremented = false;
            Customer customer = null;
            SIPDialPlan dialPlan = null;

            try
            {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call Manager processing new call on thread " + Thread.CurrentThread.Name + " for " + uas.CallRequest.Method + " to " + uas.CallRequest.URI.ToString() + ".", null));

                #region Do some pre-flight checks on the SIP account to determine if the call should be processed.

                if (uas.SIPAccount == null)
                {
                    if (uas.CallRequest.URI.User == DISPATCHER_SIPACCOUNT_NAME)
                    {
                        // This is a call from the monitoring system allow to proceed.
                    }
                    else if (uas.CallDirection == SIPCallDirection.Out)
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIP account " + uas.CallRequest.Header.From.FromURI.ToParameterlessString() + " not found for outgoing call to " + uas.CallRequest.URI.ToString() + ".", null));
                        uas.Reject(SIPResponseStatusCodesEnum.Forbidden, null, null);
                        return;
                    }
                    else if (uas.CallDirection == SIPCallDirection.In)
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIP account " + uas.CallRequest.URI.ToParameterlessString() + " not found for incoming call.", null));
                        uas.Reject(SIPResponseStatusCodesEnum.NotFound, null, null);
                        return;
                    }
                }
                else
                {
                    if (uas.SIPAccount.IsDisabled)
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIP account " + uas.SIPAccount.SIPUsername + "@" + uas.SIPAccount.SIPDomain + " is disabled for " + uas.CallDirection + " call.", uas.SIPAccount.Owner));
                        uas.Reject(SIPResponseStatusCodesEnum.Forbidden, "SIP account disabled", null);
                        return;
                    }
                    else if (uas.SIPAccount.IsIncomingOnly && uas.CallDirection == SIPCallDirection.Out)
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SIP account " + uas.SIPAccount.SIPUsername + "@" + uas.SIPAccount.SIPDomain + " is not permitted to make outgoing calls", uas.SIPAccount.Owner));
                        uas.Reject(SIPResponseStatusCodesEnum.Forbidden, "SIP account not permitted to make outgoing calls", null);
                        return;
                    }
                }

                #endregion

                SIPURI callURI = (uas.CallRequest != null) ? uas.CallRequest.URI : null;
                SIPAccount sipAccount = uas.SIPAccount;

                if (uas.CallDirection == SIPCallDirection.In && callURI.User == DISPATCHER_SIPACCOUNT_NAME)
                {
                    uas.NoCDR();

                    #region Create a pseudo-dialplan to process the monitoring process call.

                    string pseudoScript =
                        "sys.Log(\"Dispatcher Call.\")\n" +
                        "result = sys.DoesSIPAccountExist(\"" + DISPATCHER_SIPACCOUNT_NAME + "\")\n" + // Allows the test call to check the database connectivity.
                        "sys.Log(\"DoesSIPAccountExist result=#{result}.\")\n" +
                        "sys.Respond(420, nil, \"DialPlanEngine-ExecutionCount: " + m_dialPlanEngine.ScriptCount + "\")\n";
                    SIPDialPlan dispatcherDialPlan = new SIPDialPlan(null, null, null, pseudoScript, SIPDialPlanScriptTypesEnum.Ruby);
                    dispatcherDialPlan.Id = Guid.Empty; // Prevents the increment and decrement on the execution counts.
                    DialPlanScriptContext scriptContext = new DialPlanScriptContext(
                            null,
                            m_sipTransport,
                            CreateDialogueBridge,
                            DecrementDialPlanExecutionCount,
                            m_outboundProxy,
                            uas,
                            dispatcherDialPlan,
                            null,
                            m_traceDirectory,
                            null,
                            Guid.Empty);
                    m_dialPlanEngine.Execute(scriptContext, uas, uas.CallDirection, null, this);

                    #endregion
                }
                else
                {
                    string dialPlanName = dialPlanName = (uas.CallDirection == SIPCallDirection.Out) ? sipAccount.OutDialPlanName : sipAccount.InDialPlanName;
                    string owner = (uas.IsB2B) ? uas.SIPAccount.Owner : uas.Owner;

                    if (GetDialPlanAndCustomer(owner, dialPlanName, uas, out customer, out dialPlan))
                    {
                        wasExecutionCountIncremented = true;

                        if (dialPlan != null)
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Using dialplan " + dialPlanName + " for " + uas.CallDirection + " call to " + callURI.ToString() + ".", owner));

                            if (dialPlan.ScriptType == SIPDialPlanScriptTypesEnum.Asterisk)
                            {
                                DialPlanLineContext lineContext = new DialPlanLineContext(
                                    Log_External,
                                    m_sipTransport,
                                    CreateDialogueBridge,
                                    DecrementDialPlanExecutionCount,
                                    m_outboundProxy,
                                    uas,
                                    dialPlan,
                                    GetSIPProviders_External(p => p.Owner == owner, null, 0, Int32.MaxValue),
                                    m_traceDirectory,
                                    (uas.CallDirection == SIPCallDirection.Out) ? sipAccount.NetworkId : null,
                                    customer.Id);
                                m_dialPlanEngine.Execute(lineContext, uas, uas.CallDirection, CreateDialogueBridge, this);
                            }
                            else
                            {
                                dialPlan.AuthorisedApps = customer.AuthorisedApps + dialPlan.AuthorisedApps;
                                DialPlanScriptContext scriptContext = new DialPlanScriptContext(
                                    Log_External,
                                    m_sipTransport,
                                    CreateDialogueBridge,
                                    DecrementDialPlanExecutionCount,
                                    m_outboundProxy,
                                    uas,
                                    dialPlan,
                                    GetSIPProviders_External(p => p.Owner == owner, null, 0, Int32.MaxValue),
                                    m_traceDirectory,
                                    (uas.CallDirection == SIPCallDirection.Out) ? sipAccount.NetworkId : null,
                                    customer.Id);
                                m_dialPlanEngine.Execute(scriptContext, uas, uas.CallDirection, CreateDialogueBridge, this);
                            }
                        }
                        else if (uas.CallDirection == SIPCallDirection.In)
                        {
                            if (uas.IsB2B)
                            {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dialplan could not be loaded for incoming B2B call to " + callURI.ToString() + ".", owner));
                                uas.Reject(SIPResponseStatusCodesEnum.InternalServerError, "Error loading incoming dial plan for B2B call", null);
                                DecrementDialPlanExecutionCount(null, customer.Id);
                            }
                            else
                            {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No dialplan specified for incoming call to " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + ", registered bindings will be used.", owner));

                                // The SIP account has no dialplan for an incoming call therefore send to the SIP account's bindings.
                                List<SIPRegistrarBinding> bindings = GetSIPAccountBindings_External(b => b.SIPAccountId == sipAccount.Id, null, 0, MAX_FORWARD_BINDINGS);
                                if (bindings != null && bindings.Count > 0)
                                {
                                    // Create a pseudo-dialplan to process the incoming call.
                                    string pseudoScript = "sys.Dial(\"" + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + "\")\n";
                                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding incoming call for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " to " + bindings.Count + " bindings.", owner));
                                    SIPDialPlan incomingDialPlan = new SIPDialPlan(sipAccount.Owner, null, null, pseudoScript, SIPDialPlanScriptTypesEnum.Ruby);
                                    incomingDialPlan.Id = Guid.Empty; // Prevents the increment and decrement on the execution counts.
                                    DialPlanScriptContext scriptContext = new DialPlanScriptContext(
                                            Log_External,
                                            m_sipTransport,
                                            CreateDialogueBridge,
                                            DecrementDialPlanExecutionCount,
                                            m_outboundProxy,
                                            uas,
                                            incomingDialPlan,
                                            null,
                                            m_traceDirectory,
                                            null,
                                            customer.Id);
                                    m_dialPlanEngine.Execute(scriptContext, uas, uas.CallDirection, CreateDialogueBridge, this);
                                }
                                else
                                {
                                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No bindings available for " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " returning temporarily not available.", owner));
                                    uas.Reject(SIPResponseStatusCodesEnum.TemporarilyNotAvailable, null, null);
                                    DecrementDialPlanExecutionCount(null, customer.Id);
                                }
                            }
                        }
                        else
                        {
                            // Couldn't load a dialplan for an outgoing call.
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dialplan could not be loaded for " + uas.CallDirection + " call to " + callURI.ToString() + ".", owner));
                            uas.Reject(SIPResponseStatusCodesEnum.InternalServerError, "Error loading dial plan", null);
                            DecrementDialPlanExecutionCount(null, customer.Id);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Exception SIPCallManager ProcessNewCall. " + excp.Message, null));
                uas.Reject(SIPResponseStatusCodesEnum.InternalServerError, "Exception ProcessNewCall", null);

                if (wasExecutionCountIncremented)
                {
                    DecrementDialPlanExecutionCount(dialPlan, customer.Id);
                }
            }
        }

        public string ProcessWebCall(string username, string number, string dialplanName, string replacesCallID)
        {
            bool wasExecutionCountIncremented = false;
            Customer customer = null;
            SIPDialPlan dialPlan = null;

            try
            {
                customer = m_customerPersistor.Get(c => c.CustomerUsername == username);
                SIPDialogue replacesDialogue = (!replacesCallID.IsNullOrBlank() && customer != null) ? m_sipDialogueManager.GetDialogueRelaxed(customer.CustomerUsername, replacesCallID) : null;

                if (customer == null)
                {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Web " + dialplanName + " rejected for " + username + " and " + number + ", as no matching user.", null));
                    return "Sorry no matching user was found, the " + dialplanName + " was not initiated.";
                }
                else if (customer.Suspended)
                {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Web " + dialplanName + " rejected for " + username + " and " + number + ", user account is suspended.", null));
                    return "Sorry the user's account is suspended.";
                }
                else if (!replacesCallID.IsNullOrBlank() && replacesDialogue == null)
                {
                    return "Sorry the blind transfer could not be initiated, the Call-ID to transfer could not be found.";
                }
                else
                {
                    dialPlan = GetDialPlan_External(d => d.Owner == username && d.DialPlanName == dialplanName);
                    if (dialPlan == null)
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Web " + dialplanName + " rejected as no " + dialplanName + " dialplan exists.", username));
                        return "Sorry the specified user has not enabled callbacks, the callback was not initiated.";
                    }
                    else
                    {
                        if (!IsDialPlanExecutionAllowed(dialPlan, customer))
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Execution of web call for dialplan " + dialplanName + " was not processed as maximum execution count has been reached.", username));
                            return "Sorry the callback was not initiated, dial plan execution exceeded maximum allowed";
                        }
                        else
                        {
                            IncrementDialPlanExecutionCount(dialPlan, customer);

                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Web call for " + dialplanName + " initialising to " + number + ".", username));

                            ISIPServerUserAgent uas = null;
                            if (replacesCallID.IsNullOrBlank())
                            {
                                UASInviteTransaction dummyTransaction = GetDummyWebCallbackTransaction(number);
                                uas = new SIPServerUserAgent(m_sipTransport, m_outboundProxy, username, SIPDomainManager.DEFAULT_LOCAL_DOMAIN, SIPCallDirection.Out, GetSIPAccount_External, null, Log_External, dummyTransaction);
                            }
                            else
                            {
                                SIPDialogue oppositeDialogue = m_sipDialogueManager.GetOppositeDialogue(replacesDialogue);
                                uas = new SIPTransferServerUserAgent(Log_External, m_sipDialogueManager.BlindTransfer, m_sipTransport, m_outboundProxy, replacesDialogue, oppositeDialogue, number, customer.CustomerUsername, customer.AdminId);
                            }

                            DialPlanScriptContext scriptContext = new DialPlanScriptContext(
                                    Log_External,
                                    m_sipTransport,
                                    CreateDialogueBridge,
                                    DecrementDialPlanExecutionCount,
                                    m_outboundProxy,
                                    uas,
                                    dialPlan,
                                    GetSIPProviders_External(p => p.Owner == username, null, 0, Int32.MaxValue),
                                    m_traceDirectory,
                                    null,
                                    customer.Id);
                            m_dialPlanEngine.Execute(scriptContext, uas, SIPCallDirection.Out, CreateDialogueBridge, this);

                            if (replacesCallID.IsNullOrBlank())
                            {
                                return "Web call was successfully initiated.";
                            }
                            else
                            {
                                return "Blind transfer was successfully initiated.";
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCallManager ProcessWebCall. " + excp.Message);
                return "Sorry there was an unexpected error, the callback was not initiated.";

                if (wasExecutionCountIncremented)
                {
                    DecrementDialPlanExecutionCount(dialPlan, customer.Id);
                }
            }
        }

        private bool GetDialPlanAndCustomer(string owner, string dialPlanName, ISIPServerUserAgent uas, out Customer customer, out SIPDialPlan dialPlan)
        {
            try
            {
                dialPlan = null;
                customer = m_customerPersistor.Get(c => c.CustomerUsername == owner);

                if (customer == null)
                {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call rejected for customer " + owner + " as no matching account found.", null));
                    uas.Reject(SIPResponseStatusCodesEnum.DoesNotExistAnywhere, "No matching user was found", null);
                }
                else if (customer.Suspended)
                {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call rejected for customer " + owner + " as account is suspended.", null));
                    uas.Reject(SIPResponseStatusCodesEnum.DoesNotExistAnywhere, "User account is suspended", null);
                }
                else if (dialPlanName.IsNullOrBlank() && uas.CallDirection == SIPCallDirection.Out)
                {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call rejected for customer " + owner + " as no dialplan is configured for an " + uas.CallDirection + " call.", null));
                    uas.Reject(SIPResponseStatusCodesEnum.TemporarilyNotAvailable, "SIP account missing dialplan setting", null);
                }
                else
                {
                    dialPlan = GetDialPlan_External(d => d.Owner == owner && d.DialPlanName == dialPlanName);
                    if (!IsDialPlanExecutionAllowed(dialPlan, customer))
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Execution of dial plan " + dialPlanName + " was not processed as maximum execution count has been reached.", owner));
                        uas.Reject(SIPResponseStatusCodesEnum.TemporarilyNotAvailable, "Dial plan execution exceeded maximum allowed", null);
                    }
                    else
                    {
                        IncrementDialPlanExecutionCount(dialPlan, customer);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetDialPlanAndCustomer. " + excp.Message);
                throw;
            }
        }

        private bool IsDialPlanExecutionAllowed(SIPDialPlan dialPlan, Customer customer)
        {
            try
            {
                int dialPlanExecutionCount = (dialPlan != null) ? dialPlan.ExecutionCount : 0;
                int dialPlanMaxExecutionCount = (dialPlan != null) ? dialPlan.MaxExecutionCount : 0;

                if (customer.ExecutionCount >= customer.MaxExecutionCount ||
                    (dialPlan != null && dialPlanExecutionCount >= dialPlanMaxExecutionCount))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception IsDialPlanExecutionAllowed. " + excp.Message);
                throw excp;
            }
        }

        private void IncrementDialPlanExecutionCount(SIPDialPlan dialPlan, Customer customer)
        {
            try
            {
                using (var trans = new TransactionScope())
                {
                    if (dialPlan != null && dialPlan.Id != Guid.Empty)
                    {
                        int executionCount = Convert.ToInt32(m_dialPlanPersistor.GetProperty(dialPlan.Id, m_sipDialPlanExecutionCountPropertyName));
                        logger.Debug("Incrementing dial plan execution count for " + dialPlan.DialPlanName + "@" + dialPlan.Owner + ", currently=" + executionCount + ".");
                        m_dialPlanPersistor.UpdateProperty(dialPlan.Id, m_sipDialPlanExecutionCountPropertyName, executionCount + 1);
                    }

                    int customerExecutionCount = Convert.ToInt32(m_customerPersistor.GetProperty(customer.Id, m_sipDialPlanExecutionCountPropertyName));
                    logger.Debug("Incrementing customer execution count for " + customer.CustomerUsername + ", currently=" + customerExecutionCount + ".");
                    m_customerPersistor.UpdateProperty(customer.Id, m_sipDialPlanExecutionCountPropertyName, customerExecutionCount + 1);

                    trans.Complete();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCallManager IncrementDialPlanExecutionCount. " + excp.Message);
                throw;
            }
        }

        private void DecrementDialPlanExecutionCount(SIPDialPlan dialPlan, Guid customerId)
        {
            try
            {
                using (var trans = new TransactionScope())
                {
                    if (dialPlan != null && dialPlan.Id != Guid.Empty)
                    {
                        int executionCount = Convert.ToInt32(m_dialPlanPersistor.GetProperty(dialPlan.Id, m_sipDialPlanExecutionCountPropertyName));
                        logger.Debug("Decrementing dial plan execution count for " + dialPlan.DialPlanName + "@" + dialPlan.Owner + ", currently=" + executionCount + ".");
                        executionCount = (executionCount > 0) ? executionCount - 1 : 0;
                        m_dialPlanPersistor.UpdateProperty(dialPlan.Id, m_sipDialPlanExecutionCountPropertyName, executionCount);
                    }

                    if (customerId != Guid.Empty)
                    {
                        int customerExecutionCount = Convert.ToInt32(m_customerPersistor.GetProperty(customerId, m_sipDialPlanExecutionCountPropertyName));
                        logger.Debug("Decrementing customer execution count for customer ID " + customerId + ", currently=" + customerExecutionCount + ".");
                        customerExecutionCount = (customerExecutionCount > 0) ? customerExecutionCount - 1 : 0;
                        m_customerPersistor.UpdateProperty(customerId, m_sipDialPlanExecutionCountPropertyName, customerExecutionCount);
                    }

                    trans.Complete();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPCallManager DecrementDialPlanExecutionCount. " + excp.Message);
            }
        }

        private UASInviteTransaction GetDummyWebCallbackTransaction(string number)
        {
            SIPRequest dummyInvite = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURIRelaxed(number + "@sipsorcery.com"));
            SIPHeader dummyHeader = new SIPHeader("<sip:anon@sipsorcery.com>", "<sip:anon@sipsorcery.com>", 1, CallProperties.CreateNewCallId());
            dummyHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            dummyHeader.Vias.PushViaHeader(new SIPViaHeader(SIPTransport.Blackhole, CallProperties.CreateBranchId()));
            dummyInvite.Header = dummyHeader;
            UASInviteTransaction dummyTransaction = m_sipTransport.CreateUASTransaction(dummyInvite, SIPTransport.Blackhole, SIPTransport.Blackhole, null);
            return dummyTransaction;
        }

        public void Transfer(SIPDialogue dialogue, SIPNonInviteTransaction referTransaction)
        {
            logger.Debug("Transfer requested to " + referTransaction.TransactionRequest.Header.ReferTo + ".");

            SIPResponse errorResponse = SIPTransport.GetResponse(referTransaction.TransactionRequest, SIPResponseStatusCodesEnum.NotImplemented, null);
            m_sipTransport.SendResponse(errorResponse);
        }

        public int GetCurrentCallCount(string owner)
        {
            try
            {
                return m_sipDialoguePersistor.Count(d => d.Owner == owner);
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetCurrentCallCount. " + excp.Message);
                return -1;
            }
        }

        public void AddWaitingApplication(CallbackWaiter callbackWaiter)
        {
            lock (m_waitingForCallbacks)
            {
                if (m_waitingForCallbacks.ContainsKey(callbackWaiter.UniqueId))
                {
                    m_waitingForCallbacks[callbackWaiter.UniqueId] = callbackWaiter;
                }
                else
                {
                    m_waitingForCallbacks.Add(callbackWaiter.UniqueId, callbackWaiter);
                }
            }

            if (m_dispatcherProxy.Count > 0)
            {
                // Register wil the SIP proxies to get the next call for the owning user directed to this application server.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        foreach (CallDispatcherProxy proxy in m_dispatcherProxy.Values)
                        {
                            if (proxy.State != CommunicationState.Faulted)
                            {
                                proxy.SetNextCallDest(callbackWaiter.Owner, m_sipTransport.GetDefaultSIPEndPoint().ToString());
                            }
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.Error("Exception SIPCallManager AddWaitingApplication. " + excp.Message);
                    }
                });
            }
        }

        public void ReInvite(SIPDialogue dialogue, string replacementSDP)
        {
            m_sipDialogueManager.ReInvite(dialogue, replacementSDP);
        }

        public void CreateDialogueBridge(SIPDialogue clientDiaglogue, SIPDialogue forwardedDialogue, string owner)
        {
            m_sipDialogueManager.CreateDialogueBridge(clientDiaglogue, forwardedDialogue, owner);
        }

        private void InitialiseDispatcherProxies()
        {
            try
            {
                List<string> clientEndPointNames = new List<string>();

                ServiceModelSectionGroup serviceModelSectionGroup = ServiceModelSectionGroup.GetSectionGroup(ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None));
                foreach (ChannelEndpointElement client in serviceModelSectionGroup.Client.Endpoints)
                {
                    if (client.Contract == DISPATCHER_CONTRACT_NAME)
                    {
                        logger.Debug("MonitorProxyManager found client endpoint for " + DISPATCHER_CONTRACT_NAME + ", name=" + client.Name + ".");
                        CallDispatcherProxy dispatcherProxy = new CallDispatcherProxy(client.Name);
                        m_dispatcherProxy.Add(client.Name, dispatcherProxy);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception InitialiseDispatcherProxy. " + excp.Message);
            }
        }

        /*private void CreateProxy(string name)
        {
            CallDispatcherProxy dispatcherProxy = new CallDispatcherProxy(name);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string configName = name;

                try
                {
                    dispatcherProxy.IsAlive();
                    ((ICommunicationObject)dispatcherProxy).Faulted += ProxyChannelFaulted;
                    logger.Debug("SIPCallManager CallDispatcherProxy successfully created for " + name + ".");
                    m_dispatcherProxy.Add(configName, dispatcherProxy);
                }
                catch (Exception excp)
                {
                    logger.Warn("Could not connect to dispatcher proxy " + name + ". " + excp.Message);
                    Timer retryProxy = new Timer(delegate { CreateProxy(name); }, null, RETRY_FAILED_PROXY, Timeout.Infinite);
                }
            });
        }*/

        /*private void ProxyChannelFaulted(object sender, EventArgs e)
        {
            for (int index = 0; index < m_dispatcherProxy.Count; index++)
            {
                KeyValuePair<string, CallDispatcherProxy> proxyEntry = m_dispatcherProxy.ElementAt(index);
                CallDispatcherProxy proxy = proxyEntry.Value;

                if (proxy.State == CommunicationState.Faulted)
                {
                    logger.Debug("Removing faulted dispatcher proxy for " + proxyEntry.Key + ".");
                    m_dispatcherProxy.Remove(proxyEntry.Key);
                    CreateProxy(proxyEntry.Key);
                    index--;

                    logger.Warn("SIPCallManager received a fault on proxy channel, comms state=" + proxy.InnerChannel.State + ".");

                    try
                    {
                        proxy.Abort();
                    }
                    catch (Exception abortExcp)
                    {
                        logger.Error("Exception ProxyChannelFaulted Abort. " + abortExcp.Message);
                    }
                }
            }
        }*/
    }
}
