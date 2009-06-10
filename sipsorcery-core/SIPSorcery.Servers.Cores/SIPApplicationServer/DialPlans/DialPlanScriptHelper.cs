// ============================================================================
// FileName: DialPlanScriptHelper.cs
//
// Description:
// Dial plan script helper methods for Ruby dial plan scripts.
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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Net;
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

namespace SIPSorcery.Servers
{
    /// <summary>
    /// Helper functions for use in dial plan scripts.
    /// </summary>
    public class DialPlanScriptHelper
    {
        private const int DEFAULT_CREATECALL_RINGTIME = 60;     
        private const int ENUM_LOOKUP_TIMEOUT = 5;              // Default timeout in seconds for ENUM lookups.

        private static int m_maxRingTime = SIPTimings.MAX_RING_TIME;
        private static SIPSchemesEnum m_defaultScheme = SIPSchemesEnum.sip;

        private static ILog logger = AppState.logger;
        private SIPMonitorLogDelegate m_dialPlanLogDelegate;

        private SIPTransport m_sipTransport;
        private DialPlanExecutingScript m_executingScript;
        private List<SIPProvider> m_sipProviders;

        private DialogueBridgeCreatedDelegate m_createBridgeDelegate;
        private GetCanonicalDomainDelegate m_getCanonicalDomainDelegate;
        private SIPRequest m_sipRequest;                                // This is a copy of the SIP request from m_clientTransaction.
        private SwitchCallMulti m_currentCall;
        private SIPEndPoint m_outboundProxySocket;           // If this app forwards calls via na outbound proxy this value will be set.
        private string m_customSIPHeaders;                   // Allows a dialplan user to add or customise SIP headers.
        private string m_customFromName;
        private string m_customFromUser;
        private string m_customFromHost;
        private SIPCallDirection m_callDirection;
        private DialStringParser m_dialStringParser;
              
        // Deprecated, use LastFailureReason.
        public string LastFailureMessage
        {
            get { return LastFailureReason; }
        }
        
        // The error message from the first call leg on the final dial attempt used when the call fails to provide a reason.
        public string LastFailureReason {
            get { return m_executingScript.LastFailureReason; }
            set { m_executingScript.LastFailureReason = value; }
        }
        public SIPResponseStatusCodesEnum LastFailureStatus {
            get { return m_executingScript.LastFailureStatus; }
            set { m_executingScript.LastFailureStatus = value; }
        }

        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetSIPAccountBindings_External;   // This event must be wired up to an external function in order to be able to lookup bindings that have been registered for a SIP account.
        private SIPCallManager m_callManager;

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
      
        public DialPlanScriptHelper(
            SIPTransport sipTransport,
            DialPlanExecutingScript executingScript,
            SIPMonitorLogDelegate logDelegate, 
            DialogueBridgeCreatedDelegate createBridgeDelegate,
            UASInviteTransaction clientTransaction,
            SIPRequest sipRequest,
            SIPCallDirection callDirection,
            DialPlanContext dialPlanContext,
            GetCanonicalDomainDelegate getCanonicalDomain,
            SIPCallManager callManager,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getSIPAccountBindings,
            SIPEndPoint outboundProxySocket
            )
        {
            m_sipTransport = sipTransport;
            m_executingScript = executingScript;
            m_dialPlanLogDelegate = logDelegate;
            m_createBridgeDelegate = createBridgeDelegate;
            m_sipRequest = sipRequest;
            m_callDirection = callDirection;
            m_dialPlanContext = dialPlanContext;
            m_username = dialPlanContext.Owner;
            m_adminMemberId = dialPlanContext.AdminMemberId;
            m_sipProviders = dialPlanContext.SIPProviders;
            m_getCanonicalDomainDelegate = getCanonicalDomain;
            m_callManager = callManager;
            GetSIPAccount_External = getSIPAccount;
            GetSIPAccountBindings_External = getSIPAccountBindings;
            m_outboundProxySocket = outboundProxySocket;

            m_dialPlanContext.TraceLog.AppendLine("DialPlan=> Dialplan trace commenced at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + ".");
            m_dialPlanContext.CallCancelledByClient += ClientCallTerminated;
            m_dialStringParser = new DialStringParser(m_sipTransport, m_username, m_sipProviders, GetSIPAccount_External, GetSIPAccountBindings_External, m_getCanonicalDomainDelegate);
        }

        /// <remarks>
        /// This method will be called on the thread that owns the dialplancontext object so it's critical that Thread abort 
        /// is not called in it or from it.
        /// </remarks>
        /// <param name="cancelCause"></param>
        private void ClientCallTerminated(CallCancelCause cancelCause) {
            try {
                Log("Dialplan call was terminated by client side due to " + cancelCause + ".");

                if (m_currentCall != null) {
                    m_currentCall.CancelNotRequiredCallLegs(cancelCause);
                }

                m_executingScript.StopExecution();
            }
            catch (Exception excp) {
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
            SIPRequest clientRequest) {
            DialPlanAppResult result = DialPlanAppResult.Unknown;
            ManualResetEvent waitForCallCompleted = new ManualResetEvent(false);

            SIPResponseStatusCodesEnum answeredStatus = SIPResponseStatusCodesEnum.None;
            string answeredReason = null;
            string answeredContentType = null;
            string answeredBody = null;
            SIPDialogue answeredDialogue = null;

            m_currentCall = new SwitchCallMulti(m_sipTransport, FireProxyLogEvent, Username, m_adminMemberId, LastDialled, m_outboundProxySocket, clientRequest.Header.ContentType, clientRequest.Body);
            m_currentCall.CallProgress += m_dialPlanContext.CallProgress;
            m_currentCall.CallFailed += (status, reason) => {
                LastFailureStatus = status;
                LastFailureReason = reason;
                result = DialPlanAppResult.Failed;
                waitForCallCompleted.Set();
            };
            m_currentCall.CallAnswered += (status, reason, contentType, body, dialogue) => {
                answeredStatus = status;
                answeredReason = reason;
                answeredContentType = contentType;
                answeredBody = body;
                answeredDialogue = dialogue;
                result = DialPlanAppResult.Answered;
                waitForCallCompleted.Set();
            };

            LastDialled = new List<SIPTransaction>();

            try {
                Queue<List<SIPCallDescriptor>> callsQueue = m_dialStringParser.ParseDialString(DialPlanContextsEnum.Script, clientRequest, data, m_customSIPHeaders);
                if (m_customFromName != null || m_customFromUser != null || m_customFromHost != null) {
                    UpdateCallQueueFromHeaders(callsQueue, m_customFromName, m_customFromUser, m_customFromHost);
                }
                m_currentCall.Start(callsQueue);

                // Wait for an answer.
                ringTimeout = (ringTimeout > m_maxRingTime) ? m_maxRingTime : ringTimeout;
                ExtendScriptTimeout(ringTimeout + DEFAULT_CREATECALL_RINGTIME);
                if (waitForCallCompleted.WaitOne(ringTimeout * 1000, false)) {
                    if (result == DialPlanAppResult.Answered) {
                        m_dialPlanContext.CallAnswered(answeredStatus, answeredReason, answeredContentType, answeredBody, answeredDialogue);
                        // Dial plan script stops once there is an answered call to bridge to.
                        m_executingScript.StopExecution();
                    }
                }
                else {
                    // Call timed out.
                    m_currentCall.CancelNotRequiredCallLegs(CallCancelCause.TimedOut);
                    result = DialPlanAppResult.TimedOut;
                }

                return result;
            }
            catch (ThreadAbortException) {
                // This exception will be thrown under normal circumstances as the script will abort the thread if a call is answered.
                return result;
            }
            catch (Exception excp) {
                logger.Error("Exception DialPlanScriptHelper Dial. " + excp);
                return DialPlanAppResult.Error;
            }
        }

        /// <summary>
        /// Logs a message with the proxy. Typically this records the message in the database and also prints it out
        /// on the proxy monitor telnet console.
        /// </summary>
        /// <param name="message"></param>
        public void Log(string message)
        {
            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, message, Username));
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
            CallbackApp callbackApp = new CallbackApp(m_sipTransport, m_callManager, m_dialStringParser, FireProxyLogEvent, m_username, m_adminMemberId, m_outboundProxySocket);
            ThreadPool.QueueUserWorkItem(delegate { callbackApp.Callback(dest1, dest2, delaySeconds); });
        }

        /// <summary>
        /// Sends a SIP response to the client call. If a final response is sent then the client call will hang up.
        /// </summary>
        /// <param name="statusCode"></param>
        /// <param name="reason"></param>
        public void Respond(int statusCode, string reason) {
            try {
                SIPReplyApp replyApp = new SIPReplyApp();
                SIPResponse sipResponse = replyApp.Start(statusCode, reason);
                if ((int)sipResponse.Status >= 300) {
                    m_dialPlanContext.CallFailed(sipResponse.Status, sipResponse.ReasonPhrase);
                    m_executingScript.StopExecution();
                }
                else if ((int)sipResponse.Status < 200) {
                    m_dialPlanContext.CallProgress(sipResponse.Status, sipResponse.ReasonPhrase, null, null);
                }
            }
            catch (Exception excp) {
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

        public bool IsAvailable()
        {
            return IsAvailable(Username, SIPAppServerCore.DEFAULT_DOMAIN);
        }

        /// <summary>
        /// Checks whether the calling user has a registered contact. If so returns true otherwise false.
        /// </summary>
        public bool IsAvailable(string username, string domain)
        {
            try {
                string canonicalDomain = m_getCanonicalDomainDelegate(domain);
                if (canonicalDomain.IsNullOrBlank()) {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "The " + domain + " is not a serviced domain.", Username));
                    return false;
                }
                else {
                    SIPAccount sipAccount = GetSIPAccount_External(s => s.SIPUsername == username && s.SIPDomain == domain);
                    if (sipAccount == null) {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No sip account exists in IsAvailable for " + username + "@" + domain + ".", Username));
                        return false;
                    }
                    else {
                        SIPRegistrarBinding[] bindings = GetBindings(username, domain);
                        return (bindings != null && bindings.Length > 0);
                    }
                }
            }
            catch (Exception excp) {
                Log("Exception IsAvailable. " + excp.Message);
                return false;
            }
        }

        public SIPRegistrarBinding[] GetBindings()
        {
            return GetBindings(Username, SIPAppServerCore.DEFAULT_DOMAIN);
        }

        /// <summary>
        /// Gets an array of the registered contacts for the dialplan owner's SIP account.
        /// </summary>
        public SIPRegistrarBinding[] GetBindings(string username, string domain)
        {
            try
            {
                string canonicalDomain = m_getCanonicalDomainDelegate(domain);
                if (canonicalDomain.IsNullOrBlank()) {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "The " + domain + " is not a serviced domain.", Username));
                    return null;
                }
                else {
                    SIPAccount sipAccount = GetSIPAccount_External(s => s.SIPUsername == username && s.SIPDomain == domain);
                    if (sipAccount == null) {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No sip account exists in GetBindings for " + username + "@" + domain + ".", Username));
                        return null;
                    }
                    else if (sipAccount.Owner != m_username) {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "You are not authorised to call GetBindings for " + username + "@" + domain + ".", Username));
                        return null;
                    }
                    else {
                        List<SIPRegistrarBinding> bindings = GetSIPAccountBindings_External(s => s.SIPAccountId == sipAccount.Id, null, 0, Int32.MaxValue);

                        if (bindings != null) {
                            return bindings.ToArray();
                        }
                        else {
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
        /// Replaces the remote end of a call in response to an in-dialogue request from the remote end. As an example of use if a 
        /// MESSAGE request is received on an established call then calling this function will lookup the dialogue the MESSAGE request belongs to.
        /// If a match is found it will call the new destination and when the new call is answered or gets early media the remote end (the
        /// end the MESSAGE request came from) will be hungup and the local end of the dialogue will be re-INVITED to the media on the new call.
        /// </summary>
        /// <param name="newDest"></param>
        /// <returns></returns>
        /*public CallResult ReplaceCall(string newDest)
        {
            // Lookup dialog for request.
            SIPDialogue dialogue = m_callManager.GetDialogue(m_sipRequest.Header.CallId, m_sipRequest.Header.To.ToTag, m_sipRequest.Header.From.FromTag, m_sipRequest.ReceivedFrom);

            if (dialogue == null)
            {
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "The dialogue for the ReplaceCall request could not be found, call failed.", m_username));
                return CallResult.Error;
            }
            else
            {
                //m_currentCall = new SwitchCallMulti( m_sipDomains);
                //Queue<List<SIPCallDescriptorStruct>> callsQueue = m_currentCall.BuildCallList(m_sipRequest, newDest, m_sipProviders);

                ReplaceCallApp replaceCallApp = new ReplaceCallApp(m_dialPlanLogDelegate, m_sipTransport, m_callManager, dialogue, m_username);

                replaceCallApp.Start(null);
                replaceCallApp.CallComplete += (clientTransaction, completedResult, errorMessage) => { m_completedResult = completedResult; m_waitForCallCompleted.Set(); };

                ExtendScriptTimeout(m_maxRingTime + DEFAULT_CREATECALL_RINGTIME);
                if (m_waitForCallCompleted.WaitOne(m_maxRingTime * 1000, false))
                {
                    // Call answered.
                    ExtendScriptTimeout(0);

                    if (m_completedResult == CallResult.ClientCancelled || m_completedResult == CallResult.Answered)
                    {
                        // Client has cancelled the call. Terminate the script at this point.
                        EndScript();
                    }

                    return m_completedResult;
                }
                else
                {
                    // Call timed out.
                    m_currentCall.DialPlanCancelCallLeg();
                    return CallResult.TimedOut;
                }
            }
        }*/

        public void SetSIPHeaders(string customHeaders)
        {
            if (customHeaders == null || customHeaders.Trim().Length == 0)
            {
                return;
            }
            else if (Regex.Match(customHeaders.Trim(), @"(^|\|)(Via|To|From|Contact|CSeq|Call-ID|Max-Forwards|Content)\s*:").Success)
            {
                Log("Cannot set critical header " + customHeaders + ".");
            }
            else
            {
                m_customSIPHeaders = customHeaders.Trim();
            }
        }

        public void ClearSIPHeaders()
        {
            m_customSIPHeaders = null;
        }

        public void SetFromHeader(string fromName, string fromUser, string fromHost)
        {
            m_customFromName = fromName;
            m_customFromUser = fromUser;
            m_customFromHost = fromHost;
        }

        public void ClearFromHeader()
        {
            m_customFromName = null;
            m_customFromUser = null;
            m_customFromHost = null;
        }

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

                        //if (Regex.Match(number, pattern).Success)
                        //{
                        //    Log("enum substitute /" + number + "/" + pattern + "/" + substitute + "/");
                        //    return Regex.Replace(number, pattern, substitute);
                        //}
                        //else
                        //{
                            // Remove the domain from number and match.
                            string domainlessNumber = number.Substring(0, number.IndexOf('.'));
                            Log("enum substitute /" + domainlessNumber + "/" + pattern + "/" + substitute + "/");
                            return Regex.Replace(domainlessNumber, pattern, substitute);
                        //}
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
                    number = number.Trim().Trim(new char[] { '+', '0' });
                    Match match = Regex.Match(number, @"(?<number>\d+)\.(?<domain>.+)");
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

        private void ExtendScriptTimeout(int seconds) {
            m_executingScript.EndTime = DateTime.Now.AddSeconds(seconds + DialPlanExecutingScript.MAX_SCRIPTPROCESSING_SECONDS);
        }

        private void UpdateCallQueueFromHeaders(Queue<List<SIPCallDescriptor>> callsQueue, string fromName, string fromUser, string fromHost)
        {
            if (callsQueue != null && callsQueue.Count > 0)
            {
                List<SIPCallDescriptor>[] callsArray = callsQueue.ToArray();
                Array.Reverse(callsArray);
                for (int callsLegIndex = 0; callsLegIndex < callsArray.Length; callsLegIndex++)
                {
                    if (callsArray[callsLegIndex].Count > 0)
                    {
                        for (int callIndex = 0; callIndex < callsArray[callsLegIndex].Count; callIndex++)
                        {
                            SIPCallDescriptor call = callsArray[callsLegIndex][callIndex];
                            SIPFromHeader currentFrom = SIPFromHeader.ParseFromHeader(call.From);
                            if (fromName != null)
                            {
                                currentFrom.FromName = fromName;
                            }
                            if (fromUser != null)
                            {
                                currentFrom.FromURI.User = fromUser;
                            }
                            if (fromHost != null)
                            {
                                currentFrom.FromURI.Host = fromHost;
                            }
                            call.From = currentFrom.ToString();
                            callsArray[callsLegIndex][callIndex] = call;
                        }
                    }
                }
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            try
            {
                if (m_dialPlanContext.TraceLog != null)
                {
                    m_dialPlanContext.TraceLog.AppendLine(monitorEvent.EventType + "=> " + monitorEvent.Message);
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
    }
}
