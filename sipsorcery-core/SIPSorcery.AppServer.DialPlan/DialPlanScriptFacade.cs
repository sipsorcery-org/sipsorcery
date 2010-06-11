// ============================================================================
// FileName: DialPlanScriptFacade.cs
//
// Description:
// Dial plan script facade or helper methods for dial plan scripts.
//
// Author(s):
// Aaron Clauson
//
// History:
// 16 Sep 2008  Aaron Clauson   Created (extracted from SIPDialPlan).
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd
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
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using SIPSorcery.Net;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using Heijden.DNS;
using log4net;
using agsXMPP;
using agsXMPP.protocol;
using agsXMPP.protocol.client;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.AppServer.DialPlan
{
    /// <summary>
    /// Helper functions for use in dial plan scripts.
    /// </summary>
    public class DialPlanScriptFacade
    {
        private const int DEFAULT_CREATECALL_RINGTIME = 60;
        private const int ENUM_LOOKUP_TIMEOUT = 5;              // Default timeout in seconds for ENUM lookups.
        private const string DEFAULT_LOCAL_DOMAIN = "local";
        private const int MAX_BYTES_WEB_GET = 8192;             // The maximum number of bytes that will be read from the response stream in the WebGet application.
        private const string EMAIL_FROM_ADDRESS = "dialplan@sipsorcery.com";    // The from address that will be set for emails sent from the dialplan.
        private const int ALLOWED_ADDRESSES_PER_EMAIL = 5;     // The maximum number of addresses that can be used in an email.
        private const int ALLOWED_EMAILS_PER_EXECUTION = 3;     // The maximum number of emails that can be sent pre dialplan execution.
        private const int MAX_EMAIL_SUBJECT_LENGTH = 256;
        private const int MAX_EMAIL_BODY_LENGTH = 2048;
        private const string USERDATA_DBTYPE_KEY = "UserDataDBType";
        private const string USERDATA_DBCONNSTR_KEY = "UserDataDBConnStr";
        private const int MAX_DATA_ENTRIES_PER_USER = 100;
        private const int MAX_CALLS_ALLOWED = 20;       // The maximum number of outgoing call requests that will be allowed per dialplan execution.
        private const int MAX_CALLBACKS_ALLOWED = 3;    // The maximum number of callback method calls that will be alowed per dialplan instance.
        private const int WEBGET_MAXIMUM_TIMEOUT = 300; // The maximum number of seconds a web get can be set to wait for a response.
        private const int DEFAULT_GOOGLEVOICE_PHONETYPE = 2;

        private static int m_maxRingTime = SIPTimings.MAX_RING_TIME;

        private static readonly StorageTypes m_userDataDBType = StorageTypes.Unknown;
        private static readonly string m_userDataDBConnStr;

        private static ILog logger = AppState.logger;
        private SIPMonitorLogDelegate m_dialPlanLogDelegate;

        private SIPTransport m_sipTransport;
        private DialPlanExecutingScript m_executingScript;
        private List<SIPProvider> m_sipProviders;
        private DialogueBridgeCreatedDelegate CreateBridge_External;

        private GetCanonicalDomainDelegate m_getCanonicalDomainDelegate;
        private SIPRequest m_sipRequest;                                                // This is a copy of the SIP request from m_clientTransaction.
        private ForkCall m_currentCall;
        private SIPEndPoint m_outboundProxySocket;                                      // If this app forwards calls via an outbound proxy this value will be set.
        private List<string> m_customSIPHeaders = new List<string>();                   // Allows a dialplan user to add or customise SIP headers for forwarded requests.
        private string m_customContent;                                                 // If set will be used by the Dial command as the INVITE body on forwarded requests.
        private string m_customContentType;
        private string m_customFromName;
        private string m_customFromUser;
        private string m_customFromHost;
        //private List<string> m_switchboardHeaders = new List<string>();
        private SIPCallDirection m_callDirection;
        private DialStringParser m_dialStringParser;
        private bool m_clientCallCancelled;
        private ManualResetEvent m_waitForCallCompleted;

        // Deprecated, use LastFailureReason.
        public string LastFailureMessage
        {
            get { return LastFailureReason; }
        }

        // The error message from the first call leg on the final dial attempt used when the call fails to provide a reason.
        public string LastFailureReason
        {
            get { return m_executingScript.LastFailureReason; }
            set { m_executingScript.LastFailureReason = value; }
        }
        public SIPResponseStatusCodesEnum LastFailureStatus
        {
            get { return m_executingScript.LastFailureStatus; }
            set { m_executingScript.LastFailureStatus = value; }
        }
        private int m_emailCount = 0;               // Keeps count of the emails that have been sent during this dialpan execution.
        private int m_callInitialisationCount = 0;  // Keeps count of the number of call initialisations that have been attempted by a dialplan execution.
        private int m_callbackRequests = 0;         // Keeps count of the number of call back requests that have been attempted by a dialplan execution.

        private SIPAssetPersistor<SIPAccount> m_sipAccountPersistor;
        private SIPAssetPersistor<SIPDialPlan> m_sipDialPlanPersistor;
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetSIPAccountBindings_External;   // This event must be wired up to an external function in order to be able to lookup bindings that have been registered for a SIP account.  
        private ISIPCallManager m_callManager;

        private DialPlanContext m_dialPlanContext;
        public DialPlanContext DialPlanContext
        {
            get { return m_dialPlanContext; }
        }

        private string m_username;
        public string Username
        {
            get { return m_username; }
        }
        private string m_adminMemberId;
        public bool Out
        {
            get { return m_callDirection == SIPCallDirection.Out; }
        }
        public bool In
        {
            get { return m_callDirection == SIPCallDirection.In; }
        }
        public List<SIPTransaction> LastDialled;
        public bool Trace
        {
            get { return m_dialPlanContext.SendTrace; }
            set { m_dialPlanContext.SendTrace = value; }
        }

        // Switchboard descriptive fields.
        public string SwitchboardCallerDescription;
        public string SwitchboardDescription;
        public string SwitchboardOwner;

        public static IPAddress PublicIPAddress;    // If the app server is behind a NAT then it can set this address to be used in mangled SDP.

        static DialPlanScriptFacade()
        {
            try
            {
                m_userDataDBType = (AppState.GetConfigSetting(USERDATA_DBTYPE_KEY) != null) ? StorageTypesConverter.GetStorageType(AppState.GetConfigSetting(USERDATA_DBTYPE_KEY)) : StorageTypes.Unknown;
                m_userDataDBConnStr = AppState.GetConfigSetting(USERDATA_DBCONNSTR_KEY);
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialPlanScriptHelper (static ctor). " + excp.Message);
            }
        }

        public DialPlanScriptFacade(
            SIPTransport sipTransport,
            DialPlanExecutingScript executingScript,
            SIPMonitorLogDelegate logDelegate,
            DialogueBridgeCreatedDelegate createBridge,
            SIPRequest sipRequest,
            SIPCallDirection callDirection,
            DialPlanContext dialPlanContext,
            GetCanonicalDomainDelegate getCanonicalDomain,
            ISIPCallManager callManager,
            SIPAssetPersistor<SIPAccount> sipAccountPersistor,
            SIPAssetPersistor<SIPDialPlan> sipDialPlanPersistor,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getSIPAccountBindings,
            SIPEndPoint outboundProxySocket
            )
        {
            m_sipTransport = sipTransport;
            m_executingScript = executingScript;
            m_dialPlanLogDelegate = logDelegate;
            CreateBridge_External = createBridge;
            m_sipRequest = sipRequest;
            m_callDirection = callDirection;
            m_dialPlanContext = dialPlanContext;
            m_getCanonicalDomainDelegate = getCanonicalDomain;
            m_callManager = callManager;
            m_sipAccountPersistor = sipAccountPersistor;
            m_sipDialPlanPersistor = sipDialPlanPersistor;
            GetSIPAccountBindings_External = getSIPAccountBindings;
            m_outboundProxySocket = outboundProxySocket;

            if (m_dialPlanContext != null)
            {
                m_username = dialPlanContext.Owner;
                m_adminMemberId = dialPlanContext.AdminMemberId;
                m_sipProviders = dialPlanContext.SIPProviders;

                m_dialPlanContext.TraceLog.AppendLine("DialPlan=> Dialplan trace commenced at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + ".");
                m_dialPlanContext.CallCancelledByClient += ClientCallTerminated;

                SIPAssetGetDelegate<SIPAccount> getSIPAccount = null;
                if (m_sipAccountPersistor != null)
                {
                    getSIPAccount = m_sipAccountPersistor.Get;
                }
                m_dialStringParser = new DialStringParser(m_sipTransport, m_dialPlanContext.Owner, m_dialPlanContext.SIPAccount, m_sipProviders, getSIPAccount, GetSIPAccountBindings_External, m_getCanonicalDomainDelegate, logDelegate, m_dialPlanContext.SIPDialPlan.DialPlanName);
            }
        }

        /// <remarks>
        /// This method will be called on the thread that owns the dialplancontext object so it's critical that Thread abort 
        /// is not called in it or from it.
        /// </remarks>
        /// <param name="cancelCause"></param>
        private void ClientCallTerminated(CallCancelCause cancelCause)
        {
            try
            {
                m_clientCallCancelled = true;

                Log("Dialplan call was terminated by client side due to " + cancelCause + ".");

                if (m_currentCall != null)
                {
                    m_currentCall.CancelNotRequiredCallLegs(cancelCause);
                }

                if (m_waitForCallCompleted != null)
                {
                    m_waitForCallCompleted.Set();
                }
                else
                {
                    m_executingScript.StopExecution();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ClientCallTerminated. " + excp.Message);
            }
        }

        /// <summary>
        /// Attempts to dial a series of forwards and bridges the first one that connects with the client call.
        /// </summary>
        /// <param name="data">The dial string containing the list of call legs to attempt to forward the call to.</param>
        /// <returns>A code that best represents how the dial command ended.</returns>
        public DialPlanAppResult Dial(string data)
        {
            return Dial(data, m_maxRingTime);
        }

        /// <summary>
        /// Attempts to dial a series of forwards and bridges the first one that connects with the client call.
        /// </summary>
        /// <param name="data">The dial string containing the list of call legs to attempt to forward the call to.</param>
        /// <param name="ringTimeout">The period in seconds to perservere with the dial command attempt without a final response before giving up.</param>
        /// <returns>A code that best represents how the dial command ended.</returns>
        public DialPlanAppResult Dial(string data, int ringTimeout)
        {
            return Dial(data, ringTimeout, 0);
        }

        /// <summary>
        /// Attempts to dial a series of forwards and bridges the first one that connects with the client call.
        /// </summary>
        /// <param name="data">The dial string containing the list of call legs to attempt to forward the call to.</param>
        /// /// <param name="answeredCallLimit">If greater than 0 this specifies the period in seconds an answered call will be hungup after.</param>
        /// <param name="ringTimeout">The period in seconds to perservere with the dial command attempt without a final response before giving up.</param>
        /// <returns>A code that best represents how the dial command ended.</returns>
        public DialPlanAppResult Dial(string data, int ringTimeout, int answeredCallLimit)
        {
            return Dial(data, ringTimeout, answeredCallLimit, m_sipRequest);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="ringTimeout"></param>
        /// <param name="answeredCallLimit"></param>
        /// <param name="redirectMode"></param>
        /// <param name="clientTransaction"></param>
        /// <param name="keepScriptAlive">If false will let the dial plan engine know the script has finished and the call is answered. For applications
        /// like Callback which need to have two calls answered it will be true.</param>
        /// <returns></returns>
        private DialPlanAppResult Dial(
            string data,
            int ringTimeout,
            int answeredCallLimit,
            SIPRequest clientRequest)
        {
            if (m_dialPlanContext.IsAnswered)
            {
                Log("The call has already been answered the Dial command was not processed.");
                return DialPlanAppResult.AlreadyAnswered;
            }
            else if (data.IsNullOrBlank())
            {
                Log("The dial string cannot be empty when calling Dial.");
                return DialPlanAppResult.Error;
            }
            else if (m_callInitialisationCount > MAX_CALLS_ALLOWED)
            {
                Log("You have exceeded the maximum allowed calls for a dialplan execution.");
                return DialPlanAppResult.Error;
            }
            else
            {
                Log("Commencing Dial with: " + data + ".");

                DialPlanAppResult result = DialPlanAppResult.Unknown;
                m_waitForCallCompleted = new ManualResetEvent(false);

                SIPResponseStatusCodesEnum answeredStatus = SIPResponseStatusCodesEnum.None;
                string answeredReason = null;
                string answeredContentType = null;
                string answeredBody = null;
                SIPDialogue answeredDialogue = null;
                SIPDialogueTransferModesEnum uasTransferMode = SIPDialogueTransferModesEnum.Default;
                int numberLegs = 0;

                m_currentCall = new ForkCall(m_sipTransport, FireProxyLogEvent, m_callManager.QueueNewCall, m_dialStringParser, Username, m_adminMemberId, m_outboundProxySocket, out LastDialled);
                m_currentCall.CallProgress += m_dialPlanContext.CallProgress;
                m_currentCall.CallFailed += (status, reason, headers) =>
                {
                    LastFailureStatus = status;
                    LastFailureReason = reason;
                    result = DialPlanAppResult.Failed;
                    m_waitForCallCompleted.Set();
                };
                m_currentCall.CallAnswered += (status, reason, toTag, headers, contentType, body, dialogue, transferMode) =>
                {
                    answeredStatus = status;
                    answeredReason = reason;
                    answeredContentType = contentType;
                    answeredBody = body;
                    answeredDialogue = dialogue;
                    uasTransferMode = transferMode;
                    result = DialPlanAppResult.Answered;
                    m_waitForCallCompleted.Set();
                };

                try
                {
                    SwitchboardHeaders switchboardHeaders = new SwitchboardHeaders(m_sipRequest.Header.CallId, SwitchboardCallerDescription, SwitchboardDescription, SwitchboardOwner);

                    Queue<List<SIPCallDescriptor>> callsQueue = m_dialStringParser.ParseDialString(
                        DialPlanContextsEnum.Script,
                        clientRequest,
                        data,
                        m_customSIPHeaders,
                        m_customContentType,
                        m_customContent,
                        m_dialPlanContext.CallersNetworkId,
                        m_customFromName,
                        m_customFromUser,
                        m_customFromHost,
                        switchboardHeaders);

                    List<SIPCallDescriptor>[] callListArray = callsQueue.ToArray();
                    callsQueue.ToList().ForEach((list) => numberLegs += list.Count);

                    if (numberLegs == 0)
                    {
                        Log("The dial string did not result in any call legs.");
                        return DialPlanAppResult.Error;
                    }
                    else
                    {
                        m_callInitialisationCount += numberLegs;
                        if (m_callInitialisationCount > MAX_CALLS_ALLOWED)
                        {
                            Log("You have exceeded the maximum allowed calls for a dialplan execution.");
                            return DialPlanAppResult.Error;
                        }
                    }

                    m_currentCall.Start(callsQueue);

                    // Wait for an answer.
                    if (ringTimeout <= 0 || ringTimeout * 1000 > m_maxRingTime)
                    {
                        ringTimeout = m_maxRingTime;
                    }
                    else
                    {
                        ringTimeout = ringTimeout * 1000;
                    }
                    ExtendScriptTimeout(ringTimeout + DEFAULT_CREATECALL_RINGTIME);
                    DateTime startTime = DateTime.Now;

                    if (m_waitForCallCompleted.WaitOne(ringTimeout, false))
                    {
                        if (!m_clientCallCancelled)
                        {
                            if (result == DialPlanAppResult.Answered)
                            {
                                // The call limit duration is only used if there hasn't already been a per leg duration set on the call.
                                if (answeredCallLimit > 0 && answeredDialogue.CallDurationLimit == 0)
                                {
                                    answeredDialogue.CallDurationLimit = answeredCallLimit;
                                }

                                // Set switchboard dialogue values from the answered response or from dialplan set values.
                                answeredDialogue.SwitchboardCallerDescription = SwitchboardCallerDescription ?? m_sipRequest.Header.SwitchboardCallerDescription;
                                answeredDialogue.SwitchboardDescription = SwitchboardDescription ?? m_sipRequest.Header.SwitchboardDescription;
                                answeredDialogue.SwitchboardOwner = SwitchboardOwner ?? m_sipRequest.Header.SwitchboardOwner;
                                
                                m_dialPlanContext.CallAnswered(answeredStatus, answeredReason, null, null, answeredContentType, answeredBody, answeredDialogue, uasTransferMode);

                                // Dial plan script stops once there is an answered call to bridge to or the client call is cancelled.
                                Log("Dial command was successfully answered in " + DateTime.Now.Subtract(startTime).TotalSeconds.ToString("0.00") + "s.");
                                m_executingScript.StopExecution();
                            }
                        }
                    }
                    else
                    {
                        if (!m_clientCallCancelled)
                        {
                            // Call timed out.
                            m_currentCall.CancelNotRequiredCallLegs(CallCancelCause.TimedOut);
                            result = DialPlanAppResult.TimedOut;
                        }
                    }

                    if (m_clientCallCancelled)
                    {
                        Log("Dial command was halted by cancellation of client call after " + DateTime.Now.Subtract(startTime).TotalSeconds.ToString("#.00") + "s.");
                        m_executingScript.StopExecution();
                    }

                    return result;
                }
                catch (ThreadAbortException)
                {
                    return DialPlanAppResult.Unknown;
                }
                catch (Exception excp)
                {
                    logger.Error("Exception DialPlanScriptHelper Dial. " + excp.Message);
                    return DialPlanAppResult.Error;
                }
            }
        }

        /// <summary>
        /// Logs a message with the proxy. Typically this records the message in the database and also prints it out
        /// on the proxy monitor telnet console.
        /// </summary>
        /// <param name="message"></param>
        public void Log(string message)
        {
            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, message, Username));
        }

        /// <summary>
        /// See Callback method below.
        /// </summary>
        public void Callback(string dest1, string dest2)
        {
            Callback(dest1, dest2, 0);
        }

        public void Callback(string dest1, string dest2, int delaySeconds)
        {
            m_callbackRequests++;
            if (m_callbackRequests > MAX_CALLBACKS_ALLOWED)
            {
                Log("You have exceeded the maximum allowed callbacks for a dialplan execution.");
            }
            else
            {
                CallbackApp callbackApp = new CallbackApp(m_sipTransport, m_callManager, m_dialStringParser, FireProxyLogEvent, m_username, m_adminMemberId, m_outboundProxySocket);
                ThreadPool.QueueUserWorkItem(delegate { callbackApp.Callback(dest1, dest2, delaySeconds); });
            }
        }

        public void Respond(int statusCode, string reason)
        {
            Respond(statusCode, reason, null);
        }

        /// <summary>
        /// Sends a SIP response to the client call. If a final response is sent then the client call will hang up.
        /// </summary>
        /// <param name="statusCode"></param>
        /// <param name="reason"></param>
        /// <param name="customerHeaders">Optional list of pipe '|' delimited custom headers.</param>
        public void Respond(int statusCode, string reason, string customHeaders)
        {
            try
            {
                if (m_dialPlanContext.IsAnswered)
                {
                    Log("The call has already been answered the Respond command was not processed.");
                }
                else if (statusCode >= 200 && statusCode < 300)
                {
                    Log("Respond cannot be used for 2xx responses.");
                }
                else
                {
                    string[] customHeadersList = null;
                    if (!customHeaders.IsNullOrBlank())
                    {
                        customHeadersList = customHeaders.Split('|');
                    }

                    SIPResponseStatusCodesEnum status = SIPResponseStatusCodes.GetStatusTypeForCode(statusCode);
                    if (statusCode >= 300)
                    {
                        m_dialPlanContext.CallFailed(status, reason, customHeadersList);
                        m_executingScript.StopExecution();
                    }
                    else if (statusCode < 200)
                    {
                        m_dialPlanContext.CallProgress(status, reason, customHeadersList, null, null);
                    }
                }
            }
            catch (ThreadAbortException) { }
            catch (Exception excp)
            {
                Log("Exception Respond. " + excp.Message);
            }
        }

        /// <summary>
        /// Trys an ENUM lookup on the specified number.
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public string ENUMLookup(string number)
        {
            try
            {
                string e164DottedNumber = FormatForENUMLookup(number);

                if (number == null)
                {
                    logger.Warn("The ENUMLookup number format was not recognised needs to be number.domain.");
                    return null;
                }
                else
                {
                    logger.Debug("Starting ENUM lookup for " + e164DottedNumber + ".");

                    DNSResponse enumResponse = DNSManager.Lookup(e164DottedNumber, DNSQType.NAPTR, ENUM_LOOKUP_TIMEOUT, null, false, false);
                    if (enumResponse.Answers != null && enumResponse.RecordNAPTR != null)
                    {
                        foreach (RecordNAPTR naptr in enumResponse.RecordNAPTR)
                        {
                            logger.Debug("NAPTR result=" + naptr.ToString() + " (ttl=" + naptr.RR.TTL + ").");
                        }

                        string enumURI = ApplyENUMRules(number, enumResponse.RecordNAPTR);

                        if (enumURI != null)
                        {
                            logger.Debug("ENUM URI found for " + number + "=" + enumURI);
                            return enumURI;
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else
                    {
                        logger.Debug("No NAPTR records found for " + number + ".");
                        return null;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ENUMLookup. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// Checks whether the dialplan owner's default SIP account is online (has any current bindings).
        /// </summary>
        public bool IsAvailable()
        {
            return IsAvailable(Username, DEFAULT_LOCAL_DOMAIN);
        }

        /// <summary>
        /// Checks whether the specified SIP account is online (has any current bindings).
        /// </summary>
        public bool IsAvailable(string username, string domain)
        {
            try
            {
                string canonicalDomain = m_getCanonicalDomainDelegate(domain, false);
                if (canonicalDomain.IsNullOrBlank())
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "The " + domain + " is not a serviced domain.", Username));
                    return false;
                }
                else
                {
                    SIPAccount sipAccount = m_sipAccountPersistor.Get(s => s.SIPUsername == username && s.SIPDomain == canonicalDomain);
                    if (sipAccount == null)
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No sip account exists in IsAvailable for " + username + "@" + canonicalDomain + ".", Username));
                        return false;
                    }
                    else
                    {
                        SIPRegistrarBinding[] bindings = GetBindings(username, canonicalDomain);
                        return (bindings != null && bindings.Length > 0);
                    }
                }
            }
            catch (Exception excp)
            {
                Log("Exception IsAvailable. " + excp.Message);
                return false;
            }
        }

        /// <summary>
        /// Used to check for the existence of a SIP account in the default domain.
        /// </summary>
        /// <param name="username">The SIP account username to check for.</param>
        /// <returns>Returns true if the SIP account exists, false otherwise.</returns>
        public bool DoesSIPAccountExist(string username)
        {
            return DoesSIPAccountExist(username, DEFAULT_LOCAL_DOMAIN);
        }

        /// <summary>
        /// Used to check for the existence of a SIP account in the specified domain.
        /// </summary>
        /// <param name="username">The SIP account username to check for.</param>
        /// <param name="domain">The SIP domain to check for the account in.</param>
        /// <returns>Returns true if the SIP account exists, false otherwise.</returns>
        public bool DoesSIPAccountExist(string username, string domain)
        {
            string canonicalDomain = m_getCanonicalDomainDelegate(domain, false);
            if (!canonicalDomain.IsNullOrBlank())
            {
                return (m_sipAccountPersistor.Count(s => s.SIPUsername == username && s.SIPDomain == canonicalDomain) > 0);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Gets an array of the registered contacts for the dialplan owner's SIP account.
        /// </summary>
        public SIPRegistrarBinding[] GetBindings()
        {
            string canonicalDomain = m_getCanonicalDomainDelegate(DEFAULT_LOCAL_DOMAIN, true);
            return GetBindings(Username, canonicalDomain);
        }

        /// <summary>
        /// Gets an array of the registered contacts for the specified SIP account. Only the owner of the SIP account
        /// will be allowed to retrieve a list of bindings for it.
        /// </summary>
        public SIPRegistrarBinding[] GetBindings(string username, string domain)
        {
            try
            {
                string canonicalDomain = m_getCanonicalDomainDelegate(domain, false);
                if (canonicalDomain.IsNullOrBlank())
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "The " + domain + " is not a serviced domain.", Username));
                    return null;
                }
                else
                {
                    SIPAccount sipAccount = m_sipAccountPersistor.Get(s => s.SIPUsername == username && s.SIPDomain == domain);
                    if (sipAccount == null)
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No sip account exists in GetBindings for " + username + "@" + domain + ".", Username));
                        return null;
                    }
                    else if (sipAccount.Owner != m_username)
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "You are not authorised to call GetBindings for " + username + "@" + domain + ".", Username));
                        return null;
                    }
                    else
                    {
                        List<SIPRegistrarBinding> bindings = GetSIPAccountBindings_External(s => s.SIPAccountId == sipAccount.Id, null, 0, Int32.MaxValue);

                        if (bindings != null)
                        {
                            return bindings.ToArray();
                        }
                        else
                        {
                            return null;
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                Log("Exception GetBindings. " + excp);
                return null;
            }
        }

        /// <summary>
        /// Allows a hostname to be tested to determine if it is a sipsorcery serviced domain and if it is the canonical domain
        /// will be returned.
        /// </summary>
        /// <param name="host">The hostname to attempt to retrieve the canonical domain for.</param>
        /// <returns>If the host is a sipsorcery serviced domain the canonical domain for the host will be returned, otherwise null.</returns>
        public string GetCanonicalDomain(string host)
        {
            if (host.IsNullOrBlank())
            {
                return null;
            }
            else
            {
                return m_getCanonicalDomainDelegate(host, false);
            }
        }

        /// <summary>
        /// Adds a name value pair to the custom SIP headers list. The custom headers will be added to any forwarded call requests.
        /// </summary>
        /// <param name="headerName">The name of the SIP header to add.</param>
        /// <param name="headerValue">The value of the SIP header to add.</param>
        public void SetCustomSIPHeader(string headerName, string headerValue)
        {
            if (headerName.IsNullOrBlank())
            {
                Log("The name of the header to set was empty, the header was not added.");
            }
            else if (Regex.Match(headerName.Trim(), @"^(Via|To|From|Contact|CSeq|Call-ID|Max-Forwards|Content-Length)$", RegexOptions.IgnoreCase).Success)
            {
                Log("The name of the header to set is not permitted, the header was not added.");
            }
            else
            {
                string trimmedName = headerName.Trim();
                string trimmedValue = (headerValue != null) ? headerValue.Trim() : String.Empty;
                /*if (m_customSIPHeaders.Contains(trimmedName)) {
                    m_customSIPHeaders[trimmedName] = trimmedValue;
                }
                else {
                    m_customSIPHeaders.Add(trimmedName, trimmedValue);
                }*/
                m_customSIPHeaders.Add(trimmedName + ": " + trimmedValue);
                Log("Custom SIP header " + trimmedName + " successfully added to list.");
            }
        }

        /// <summary>
        /// If present removes a SIP header from the list of custom headers.
        /// </summary>
        /// <param name="headerName">The name of the SIP header to remove.</param>
        public void RemoveCustomSIPHeader(string headerName)
        {

            if (!headerName.IsNullOrBlank() && m_customSIPHeaders.Contains(headerName.Trim()))
            {
                m_customSIPHeaders.Remove(headerName.Trim());
                Log("Custom SIP header " + headerName.Trim() + " successfully removed.");
            }
            else
            {
                Log("Custom SIP header " + headerName.Trim() + " was not in the list.");
            }
        }

        /// <summary>
        /// Clears all the custom SIP header values from the list.
        /// </summary>
        public void ClearCustomSIPHeaders()
        {
            m_customSIPHeaders.Clear();
        }

        /// <summary>
        /// Dumps the currently stored custom SIP headers to the console or monitoring screen to allow
        /// users to troubleshoot.
        /// </summary>
        public void PrintCustomSIPHeaders()
        {
            Log("Custom SIP Header List:");
            foreach (string customHeader in m_customSIPHeaders)
            {
                Log(" " + customHeader);
            }
        }

        /// <summary>
        /// Sets the value of part or all of the From header that will be set on forwarded calls. Leaving a part of the 
        /// header as null will result in the corresponding value from the originating request being used.
        /// </summary>
        /// <param name="fromName">The custom From header display name to set.</param>
        /// <param name="fromUser">The custom From header URI user value to set.</param>
        /// <param name="fromHost">The custom From header URI host value to set.</param>
        public void SetFromHeader(string fromName, string fromUser, string fromHost)
        {
            m_customFromName = fromName;
            m_customFromUser = fromUser;
            m_customFromHost = fromHost;
        }

        /// <summary>
        /// Reset the custom From header values so that the corresponding values from the originating request will
        /// be used.
        /// </summary>
        public void ClearFromHeader()
        {
            m_customFromName = null;
            m_customFromUser = null;
            m_customFromHost = null;
        }

        /// <summary>
        /// Sets the custom body that will override the incoming request body for forwarded INVITE requests.
        /// </summary>
        /// <param name="body">The custom body that will be sent in forwarded INVITE requests.</param>
        public void SetCustomContent(string content)
        {
            m_customContent = content;
        }

        /// <summary>
        /// Sets the custom body that will override the incoming request body for forwarded INVITE requests.
        /// </summary>
        /// <param name="body">The custom body that will be sent in forwarded INVITE requests.</param>
        public void SetCustomContent(string contentType, string content)
        {
            m_customContentType = contentType;
            m_customContent = content;
        }

        /// <summary>
        /// Clears the custom body so that the incoming request body will again be used on forwarded requests.
        /// </summary>
        public void ClearCustomBody()
        {
            m_customContentType = null;
            m_customContent = null;
        }

        /// <summary>
        /// Attempts to send a gTalk IM to the specified account.
        /// </summary>
        public void GTalk(string username, string password, string sendToUser, string message)
        {
            try
            {
                XmppClientConnection xmppCon = new XmppClientConnection();
                xmppCon.Password = password;
                xmppCon.Username = username;
                xmppCon.Server = "gmail.com";
                xmppCon.ConnectServer = "talk.google.com";
                xmppCon.AutoAgents = false;
                xmppCon.AutoPresence = false;
                xmppCon.AutoRoster = false;
                xmppCon.AutoResolveConnectServer = true;

                Log("Attempting to connect to gTalk for " + username + ".");

                ManualResetEvent waitForConnect = new ManualResetEvent(false);

                xmppCon.OnLogin += new ObjectHandler((sender) => waitForConnect.Set());
                xmppCon.Open();

                if (waitForConnect.WaitOne(5000, false))
                {
                    Log("Connected to gTalk for " + username + "@gmail.com.");
                    xmppCon.Send(new Message(new Jid(sendToUser + "@gmail.com"), MessageType.chat, message));
                    // Give the message time to be sent.
                    Thread.Sleep(1000);
                }
                else
                {
                    Log("Connection to gTalk for " + username + " timed out.");
                }

                xmppCon.Close();
            }
            catch (Exception excp)
            {
                logger.Error("Exception GTalk. " + excp.Message);
                Log("Exception GTalk. " + excp.Message);
            }
        }

        //public void GoogleVoiceCall(string emailAddress, string password, string forwardingNumber, string destinationNumber, bool notUsed)
        //{
        //    GoogleVoiceCall(emailAddress, password, forwardingNumber, destinationNumber, null, DEFAULT_GOOGLEVOICE_PHONETYPE, 0);
        //}

        public void GoogleVoiceCall(string emailAddress, string password, string forwardingNumber, string destinationNumber)
        {
            GoogleVoiceCall(emailAddress, password, forwardingNumber, destinationNumber, null, DEFAULT_GOOGLEVOICE_PHONETYPE, 0);
        }

        public void GoogleVoiceCall(string emailAddress, string password, string forwardingNumber, string destinationNumber, string fromURIUserToMatch)
        {
            GoogleVoiceCall(emailAddress, password, forwardingNumber, destinationNumber, fromURIUserToMatch, DEFAULT_GOOGLEVOICE_PHONETYPE, 0);
        }

        public void GoogleVoiceCall(string emailAddress, string password, string forwardingNumber, string destinationNumber, string fromURIUserToMatch, int phoneType)
        {
            GoogleVoiceCall(emailAddress, password, forwardingNumber, destinationNumber, fromURIUserToMatch, phoneType, 0);
        }

        public void GoogleVoiceCall(string emailAddress, string password, string forwardingNumber, string destinationNumber, string fromURIUserToMatch, int phoneType, int waitForCallbackTimeout)
        {
            try
            {
                DateTime startTime = DateTime.Now;

                ExtendScriptTimeout(DEFAULT_CREATECALL_RINGTIME);
                GoogleVoiceCall googleCall = new GoogleVoiceCall(m_sipTransport, m_callManager, m_dialPlanLogDelegate, m_username, m_adminMemberId, m_outboundProxySocket);
                m_dialPlanContext.CallCancelledByClient += googleCall.ClientCallTerminated;
                googleCall.CallProgress += m_dialPlanContext.CallProgress;

                string content = m_sipRequest.Body;
                IPAddress requestSDPAddress = (PublicIPAddress != null) ? PublicIPAddress : SIPPacketMangler.GetRequestIPAddress(m_sipRequest);

                IPEndPoint sdpEndPoint = (content.IsNullOrBlank()) ? null : SDP.GetSDPRTPEndPoint(content);
                if (sdpEndPoint != null)
                {
                    if (!IPSocket.IsPrivateAddress(sdpEndPoint.Address.ToString()))
                    {
                        Log("SDP on GoogleVoiceCall call had public IP not mangled, RTP socket " + sdpEndPoint.ToString() + ".");
                    }
                    else
                    {
                        bool wasSDPMangled = false;
                        if (requestSDPAddress != null)
                        {
                            if (sdpEndPoint != null)
                            {
                                content = SIPPacketMangler.MangleSDP(content, requestSDPAddress.ToString(), out wasSDPMangled);
                            }
                        }

                        if (wasSDPMangled)
                        {
                            Log("SDP on GoogleVoiceCall call had RTP socket mangled from " + sdpEndPoint.ToString() + " to " + requestSDPAddress.ToString() + ":" + sdpEndPoint.Port + ".");
                        }
                        else if (sdpEndPoint != null)
                        {
                            Log("SDP on GoogleVoiceCall could not be mangled, using original RTP socket of " + sdpEndPoint.ToString() + ".");
                        }
                    }
                }
                else
                {
                    Log("SDP RTP socket on GoogleVoiceCall call could not be determined.");
                }

                SIPDialogue answeredDialogue = googleCall.InitiateCall(emailAddress, password, forwardingNumber, destinationNumber, fromURIUserToMatch, phoneType, waitForCallbackTimeout, m_sipRequest.Header.ContentType, content);
                if (answeredDialogue != null)
                {
                    m_dialPlanContext.CallAnswered(SIPResponseStatusCodesEnum.Ok, null, null, null, answeredDialogue.ContentType, answeredDialogue.RemoteSDP, answeredDialogue, SIPDialogueTransferModesEnum.Default);

                    // Dial plan script stops once there is an answered call to bridge to or the client call is cancelled.
                    Log("Google Voice Call was successfully answered in " + DateTime.Now.Subtract(startTime).TotalSeconds.ToString("0.00") + "s.");
                    m_executingScript.StopExecution();
                }
            }
            catch (ThreadAbortException) { }
            catch (Exception excp)
            {
                Log("Exception on GoogleVoiceCall. " + excp.Message);
            }
        }

        /// <summary>
        /// Executes a HTTP GET request and if succesful returns up to the first 1024 bytes read from the
        /// response to the caller.
        /// </summary>
        /// <param name="url">The URL of the server to call.</param>
        /// <returns>The first 1024 bytes read from the response.</returns>
        public string WebGet(string url, int timeout)
        {
           Timer cancelTimer = null;

           try
           {
               if (!url.IsNullOrBlank())
               {
                   using (WebClient webClient = new WebClient())
                   {

                       if (timeout > 0)
                       {
                           timeout = (timeout > WEBGET_MAXIMUM_TIMEOUT) ? timeout = WEBGET_MAXIMUM_TIMEOUT : timeout;
                           cancelTimer = new Timer(delegate
                               {
                                   Log("WebGet to " + url + " timed out after " + timeout + " seconds.");
                                   webClient.CancelAsync();
                               },
                               null, timeout * 1000, Timeout.Infinite);
                       }

                       Log("WebGet attempting to read from " + url + ".");

                       System.IO.Stream responseStream = webClient.OpenRead(url);
                       if (responseStream != null)
                       {
                           byte[] buffer = new byte[MAX_BYTES_WEB_GET];
                           int bytesRead = responseStream.Read(buffer, 0, MAX_BYTES_WEB_GET);
                           responseStream.Close();
                           return Encoding.ASCII.GetString(buffer, 0, bytesRead);
                       }
                   }
               }

               return null;
           }
           catch (Exception excp)
           {
               logger.Error("Exception WebGet. " + excp.Message);
               Log("Error in WebGet for " + url + ".");
               return null;
           }
           finally
           {
               if (cancelTimer != null)
               {
                   cancelTimer.Dispose();
               }
           }
        }

        public string WebGet(string url)
        {
            return WebGet(url, 0);
        }

        /// <summary>
        /// Sends an email from the dialplan. There are a number of restrictions put in place and this is a privileged
        /// application that users must be manually authorised for.
        /// </summary>
        /// <param name="to">The list of addressees to send the email to. Limited to a maximum of ALLOWED_ADDRESSES_PER_EMAIL.</param>
        /// <param name="subject">The email subject. Limited to a maximum length of MAX_EMAIL_SUBJECT_LENGTH.</param>
        /// <param name="body">The email body. Limited to a maximum length of MAX_EMAIL_BODY_LENGTH.</param>
        public void Email(string to, string subject, string body)
        {
            try
            {
                if (!IsAppAuthorised(m_dialPlanContext.SIPDialPlan.AuthorisedApps, "email"))
                {
                    Log("You are not authorised to use the Email application, please contact admin@sipsorcery.com.");
                }
                else if (m_emailCount >= ALLOWED_EMAILS_PER_EXECUTION)
                {
                    Log("The maximum number of emails have been sent for this dialplan execution, email not sent.");
                }
                else
                {
                    if (to.IsNullOrBlank())
                    {
                        Log("The To field was blank, email not be sent.");
                    }
                    else if (subject.IsNullOrBlank())
                    {
                        Log("The Subject field was blank, email not be sent.");
                    }
                    else if (body.IsNullOrBlank())
                    {
                        Log("The Body was empty, email not be sent.");
                    }
                    else
                    {
                        string[] addressees = to.Split(';');
                        if (addressees.Length > ALLOWED_ADDRESSES_PER_EMAIL)
                        {
                            Log("The number of Email addressees is to high, only the first " + ALLOWED_ADDRESSES_PER_EMAIL + " will be used.");
                            to = null;
                            for (int index = 0; index < ALLOWED_ADDRESSES_PER_EMAIL; index++)
                            {
                                to += addressees[index] + ";";
                            }
                        }

                        m_emailCount++;
                        subject = (subject.Length > MAX_EMAIL_SUBJECT_LENGTH) ? subject.Substring(0, MAX_EMAIL_SUBJECT_LENGTH) : subject;
                        body = (body.Length > MAX_EMAIL_BODY_LENGTH) ? body.Substring(0, MAX_EMAIL_BODY_LENGTH) : body;
                        SIPSorcerySMTP.SendEmail(to, EMAIL_FROM_ADDRESS, subject, body);
                        Log("Email sent to " + to + " with subject of \"" + subject + "\".");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Email. " + excp.Message);
                Log("Error sending Email to " + to + " with subject of \"" + subject + "\".");
            }
        }

        /// <summary>
        /// Gets the number of currently active calls for the dial plan owner.
        /// </summary>
        /// <returns>The number of active calls or -1 if there is an error.</returns>
        public int GetCurrentCallCount()
        {
            return m_callManager.GetCurrentCallCount(m_username);
        }

        public void DBWrite(string key, string value)
        {
            if (m_userDataDBType == StorageTypes.Unknown || m_userDataDBConnStr.IsNullOrBlank())
            {
                Log("DBWrite failed as no default user database settings are configured. As an alternative you can specify your own database type and connection string.");
            }
            else
            {
                DBWrite(m_userDataDBType, m_userDataDBConnStr, key, value);
            }
        }

        public void DBWrite(string dbType, string dbConnStr, string key, string value)
        {
            StorageTypes storageType = GetStorageType(dbType);
            if (storageType != StorageTypes.Unknown)
            {
                DBWrite(storageType, dbConnStr, key, value);
            }
        }

        private void DBWrite(StorageTypes storageType, string dbConnStr, string key, string value)
        {
            try
            {
                StorageLayer storageLayer = new StorageLayer(storageType, dbConnStr);

                Dictionary<string, object> parameters = new Dictionary<string, object>();
                parameters.Add("dataowner", m_username);
                int ownerKeyCount = Convert.ToInt32(storageLayer.ExecuteScalar("select count(*) from dialplandata where dataowner = @dataowner", parameters));

                if (ownerKeyCount == MAX_DATA_ENTRIES_PER_USER)
                {
                    Log("DBWrite failed, you have reached the maximum number of database entries allowed.");
                }
                else
                {
                    parameters.Add("datakey", key);
                    int count = Convert.ToInt32(storageLayer.ExecuteScalar("select count(*) from dialplandata where datakey = @datakey and dataowner = @dataowner", parameters));
                    parameters.Add("datavalue", value);

                    if (count == 0)
                    {
                        storageLayer.ExecuteNonQuery(storageType, dbConnStr, "insert into dialplandata (dataowner, datakey, datavalue) values (@dataowner, @datakey, @datavalue)", parameters);
                    }
                    else
                    {
                        storageLayer.ExecuteNonQuery(storageType, dbConnStr, "update dialplandata set datavalue = @datavalue where dataowner = @dataowner and datakey = @datakey", parameters);
                    }
                    Log("DBWrite sucessful for datakey \"" + key + "\".");
                }
            }
            catch (Exception excp)
            {
                Log("Exception DBWrite. " + excp.Message);
            }
        }

        public void DBDelete(string key)
        {
            if (m_userDataDBType == StorageTypes.Unknown || m_userDataDBConnStr.IsNullOrBlank())
            {
                Log("DBDelete failed as no default user database settings are configured. As an alternative you can specify your own database type and connection string.");
            }
            else
            {
                DBDelete(m_userDataDBType, m_userDataDBConnStr, key);
            }
        }

        public void DBDelete(string dbType, string dbConnStr, string key)
        {
            StorageTypes storageType = GetStorageType(dbType);
            if (storageType != StorageTypes.Unknown)
            {
                DBDelete(storageType, dbConnStr, key);
            }
        }

        private void DBDelete(StorageTypes storageType, string dbConnStr, string key)
        {
            try
            {
                StorageLayer storageLayer = new StorageLayer(storageType, dbConnStr);

                Dictionary<string, object> parameters = new Dictionary<string, object>();
                parameters.Add("dataowner", m_username);
                parameters.Add("datakey", key);
                storageLayer.ExecuteNonQuery(storageType, dbConnStr, "delete from dialplandata where dataowner = @dataowner and datakey = @datakey", parameters);
                Log("DBDelete sucessful for datakey \"" + key + "\".");
            }
            catch (Exception excp)
            {
                Log("Exception DBDelete. " + excp.Message);
            }
        }

        public void DBExecuteNonQuery(string dbType, string dbConnStr, string query)
        {
            try
            {
                if (!IsAppAuthorised(m_dialPlanContext.SIPDialPlan.AuthorisedApps, "dbexecutenonquery"))
                {
                    Log("You are not authorised to use the DBExecuteNonQuery application, please contact admin@sipsorcery.com.");
                }
                else
                {
                    StorageTypes storageType = GetStorageType(dbType);
                    if (storageType != StorageTypes.Unknown)
                    {
                        StorageLayer storageLayer = new StorageLayer(storageType, dbConnStr);
                        storageLayer.ExecuteNonQuery(storageType, dbConnStr, query);
                        Log("DBExecuteNonQuery successful for " + query + ".");
                    }
                    else
                    {
                        Log("Exception DBExecuteNonQuery did not recognise database type " + dbType + ".");
                    }
                }
            }
            catch (Exception excp)
            {
                Log("Exception DBExecuteNonQuery. " + excp.Message);
            }
        }

        public string DBRead(string key)
        {
            if (m_userDataDBType == StorageTypes.Unknown || m_userDataDBConnStr.IsNullOrBlank())
            {
                Log("DBRead failed as no default user database settings are configured. As an alternative you can specify your own database type and connection string.");
                return null;
            }
            else
            {
                return DBRead(m_userDataDBType, m_userDataDBConnStr, key);
            }
        }

        public string DBRead(string dbType, string dbConnStr, string key)
        {
            StorageTypes storageType = GetStorageType(dbType);
            if (storageType != StorageTypes.Unknown)
            {
                return DBRead(storageType, dbConnStr, key);
            }
            return null;
        }

        private string DBRead(StorageTypes storageType, string dbConnStr, string key)
        {
            try
            {
                StorageLayer storageLayer = new StorageLayer(storageType, dbConnStr);

                Dictionary<string, object> parameters = new Dictionary<string, object>();
                parameters.Add("dataowner", m_username);
                parameters.Add("datakey", key);
                string result = storageLayer.ExecuteScalar("select datavalue from dialplandata where dataowner = @dataowner and datakey = @datakey", parameters) as string;
                Log("DBRead sucessful for datakey \"" + key + "\", value=" + result + ".");
                return result;
            }
            catch (Exception excp)
            {
                Log("Exception DBRead. " + excp.Message);
                return null;
            }
        }

        public string DBExecuteScalar(string dbType, string dbConnStr, string query)
        {
            try
            {
                if (!IsAppAuthorised(m_dialPlanContext.SIPDialPlan.AuthorisedApps, "dbexecutescalar"))
                {
                    Log("You are not authorised to use the DBExecuteScalar application, please contact admin@sipsorcery.com.");
                    return null;
                }
                else
                {
                    StorageTypes storageType = GetStorageType(dbType);
                    if (storageType != StorageTypes.Unknown)
                    {
                        StorageLayer storageLayer = new StorageLayer(storageType, dbConnStr);
                        string result = storageLayer.ExecuteScalar(storageType, dbConnStr, query) as string;
                        Log("DBExecuteScalar successful result=" + result + ".");
                        return result;
                    }
                    else
                    {
                        Log("Exception DBExecuteScalar did not recognise database type " + dbType + ".");
                        return null;
                    }
                }
            }
            catch (Exception excp)
            {
                Log("Exception DBExecuteScalar. " + excp.Message);
                return null;
            }
        }

        private StorageTypes GetStorageType(string dbType)
        {
            if (dbType.IsNullOrBlank())
            {
                Log("The database type was empty for DBWrite or DBRead.");
                return StorageTypes.Unknown;
            }
            else if (Regex.Match(dbType, "mysql", RegexOptions.IgnoreCase).Success)
            {
                return StorageTypes.MySQL;
            }
            else if (Regex.Match(dbType, "(pgsql|postgres)", RegexOptions.IgnoreCase).Success)
            {
                return StorageTypes.Postgresql;
            }
            else
            {
                Log("Database type " + dbType + " is not supported in DBWrite and DBRead.");
                return StorageTypes.Unknown;
            }
        }

        /// <summary>
        /// Applies a set of NAPTR rules obtained from an ENUM lookup to attempt to get a SIP URI.
        /// This functionality should be moved closer to the DNS classes once it becomes more mature and universal.
        /// See RFC 2915.
        /// </summary>
        /// <param name="naptrRecords"></param>
        /// <returns></returns>
        private string ApplyENUMRules(string number, RecordNAPTR[] naptrRecords)
        {
            try
            {
                RecordNAPTR priorityRecord = null;
                foreach (RecordNAPTR naptrRecord in naptrRecords)
                {
                    if (naptrRecord.Service != null && naptrRecord.Service.ToUpper() == RecordNAPTR.SIP_SERVICE_KEY)
                    {
                        if (priorityRecord == null)
                        {
                            priorityRecord = naptrRecord;
                        }
                        else if (naptrRecord.Order < priorityRecord.Order)
                        {
                            priorityRecord = naptrRecord;
                        }
                        else if (naptrRecord.Order == priorityRecord.Order && naptrRecord.Preference < priorityRecord.Preference)
                        {
                            priorityRecord = naptrRecord;
                        }
                    }
                }

                if (priorityRecord != null && priorityRecord.Rule != null && Regex.Match(priorityRecord.Rule, "!.+!.+!").Success)
                {
                    //logger.Debug("rule=" + priorityRecord.Rule + ".");
                    Match match = Regex.Match(priorityRecord.Rule, "!(?<pattern>.+?)!(?<substitute>.+?)!(?<options>.*)");

                    if (match.Success)
                    {
                        string pattern = match.Result("${pattern}");
                        //logger.Debug("pattern=" + pattern + ".");
                        string substitute = Regex.Replace(match.Result("${substitute}"), @"\\(?<digit>\d)", @"${${digit}}");
                        string options = match.Result("${options}");

                        logger.Debug("ENUM rule: s/" + pattern + "/" + substitute + "/" + options);
                        string domainlessNumber = number.Substring(0, number.IndexOf('.'));

                        Regex enumRegex = new Regex(pattern);

                        if (enumRegex.Match(domainlessNumber).Success)
                        {
                            //{
                            //    Log("enum substitute /" + number + "/" + pattern + "/" + substitute + "/");
                            //    return Regex.Replace(number, pattern, substitute);
                            //}
                            //else
                            //{
                            // Remove the domain from number and match.

                            Log("enum substitute /" + domainlessNumber + "/" + pattern + "/" + substitute + "/");
                            return Regex.Replace(domainlessNumber, pattern, substitute);
                        }
                        else if (enumRegex.Match("+" + domainlessNumber).Success)
                        {
                            // Remove the domain from number and match.
                            Log("enum substitute /+" + domainlessNumber + "/" + pattern + "/" + substitute + "/");
                            return Regex.Replace("+" + domainlessNumber, pattern, substitute);
                        }
                    }
                    else
                    {
                        logger.Warn("Priority rule for an ENUM lookup was not recognised: " + priorityRecord.Rule + ".");
                    }
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception ApplyENUMRules. " + excp.Message);
                return null;
            }
        }

        /// <summary>
        /// Attempts to put a number into e164 format for an ENUM lookup.
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        private string FormatForENUMLookup(string number)
        {
            try
            {
                if (number == null || number.Trim().Length == 0)
                {
                    return null;
                }
                else if (Regex.Match(number, @"(\d\.){5}").Success)
                {
                    logger.Debug("Number looks like it's already in the required format.");
                    return number;
                }
                else
                {
                    //number = number.Trim().Trim(new char[] { '+', '0' });
                    Match match = Regex.Match(number, @"(?<number>[^\.]+)\.(?<domain>.+)");
                    if (match.Success)
                    {
                        char[] enumNumber = match.Result("${number}").ToCharArray();
                        string domain = match.Result("${domain}");
                        string result = null;
                        for (int index = enumNumber.Length - 1; index >= 0; index--)
                        {
                            result += enumNumber[index] + ".";
                        }
                        return result + domain;
                    }
                    else
                    {
                        logger.Error("ENUM lookup number was not in the correct format, must be number.domain");
                        return null;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FormatForENUMLookup. " + excp);
                return number;
            }
        }

        public void ExtendScriptTimeout(int seconds)
        {
            m_executingScript.EndTime = DateTime.Now.AddSeconds(seconds + DialPlanExecutingScript.MAX_SCRIPTPROCESSING_SECONDS);
        }

        /// <summary>
        /// Used to authorise calls to privileged dialplan applications. For example sending an email requires that the dialplan has the "email" in
        /// its list of authorised apps.
        /// </summary>
        /// <param name="authorisedApps">A semi-colon delimited list of authorised applications for this dialplan.</param>
        /// <param name="applicationName">The name of the dialplan application checking for authorisation.</param>
        /// <returns>True if authorised, false otherwise.</returns>
        private bool IsAppAuthorised(string authorisedApps, string applicationName)
        {
            try
            {
                if (authorisedApps.IsNullOrBlank() || applicationName.IsNullOrBlank())
                {
                    return false;
                }
                else if (authorisedApps == SIPDialPlan.ALL_APPS_AUTHORISED)
                {
                    return true;
                }
                else
                {
                    string[] authorisedAppsSplit = authorisedApps.Split(';');
                    foreach (string app in authorisedAppsSplit)
                    {
                        if (!app.IsNullOrBlank() && app.Trim().ToLower() == applicationName.Trim().ToLower())
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception IsAppAuthorised. " + excp.Message);
                return false;
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            try
            {
                if (monitorEvent is SIPMonitorConsoleEvent)
                {
                    SIPMonitorConsoleEvent consoleEvent = monitorEvent as SIPMonitorConsoleEvent;

                    if (m_dialPlanContext.TraceLog != null)
                    {
                        m_dialPlanContext.TraceLog.AppendLine(consoleEvent.EventType + "=> " + monitorEvent.Message);
                    }
                }

                if (m_dialPlanLogDelegate != null)
                {
                    m_dialPlanLogDelegate(monitorEvent);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireProxyLogEvent DialPlanScriptHelper. " + excp.Message);
            }
        }

        #region Unit testing.

        #if UNITTEST

        [TestFixture]
		public class DialPlanScriptHelperUnitTest
		{
            [TestFixtureSetUp]
            public void Init() {
                log4net.Config.BasicConfigurator.Configure();
            }		

            [TestFixtureTearDown]
            public void Dispose()
            { }
                
            [Test]
			public void SampleTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				Assert.IsTrue(true, "True was false.");
			}

            /// <summary>
            /// Tests applying an ENUM rule.
            /// </summary>
            [Test]
            public void ApplyENUMRuleUnitTest() {
                Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                //DialPlanContext dialPlanContext = new DialPlanContext(null, null, null, null, null, null, null, null, null, Guid.Empty);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, "sip:1234@host.com");
                DialPlanScriptHelper helper = new DialPlanScriptHelper(null, null, (e) => { Console.WriteLine(e.Message); }, null, request, SIPCallDirection.None, 
                    null, null, null, null, null, null, null);
                string result = helper.ENUMLookup("+18005551212.e164.org");
                Console.WriteLine("lookup result=" + result + ".");
                Assert.IsTrue(true, "True was false.");
            }
        }

        #endif

        #endregion
    }
}
