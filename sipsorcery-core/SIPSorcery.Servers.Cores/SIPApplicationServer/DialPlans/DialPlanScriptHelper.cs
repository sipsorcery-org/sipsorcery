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
    public enum DialPlanRedirectModeEnum
    {
        None = 0,   // Redirect responses are ignored.
        Add = 1,    // A redirect response will be treated as a standard SIP URI and will be added as an additional call leg to the currently executing Dial command.
        Replace = 2,// A redirect response will be treated as a replacement call and will result in the current dialplan being terminated and re-executed with the new destination.
    }

    /// <summary>
    /// Helper functions for use in dial plan scripts.
    /// </summary>
    public class DialPlanScriptHelper
    {
        private const int DEFAULT_CREATECALL_RINGTIME = 60;     // Set ring time for calls being created by dial plan as 60s as there is nothing that can cancel the call.
        private const int MAXCALLBACK_DELAY_SECONDS = 15;       // The maximum seconds a callback method can be delayed for.
        private const int ENUM_LOOKUP_TIMEOUT = 5;              // Default timeout in seconds for ENUM lookups.

        private string CRLF = SIPConstants.CRLF;
        private static int m_maxRingTime = SIPTimings.MAX_RING_TIME;
        private static string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;
        private static SIPSchemesEnum m_defaultScheme = SIPSchemesEnum.sip;

        private static ILog logger = AppState.logger;
        private SIPMonitorLogDelegate m_dialPlanLogDelegate;

        private SIPTransport m_sipTransport;

        private Guid m_executionId;
        private List<SIPProvider> m_sipProviders;
        private ExtendScriptLifetimeDelegate m_extendLifeDelegate;

        private DialogueBridgeCreatedDelegate m_createBridgeDelegate;
        private GetCanonicalDomainDelegate m_getCanonicalDomainDelegate;
        private SIPRequest m_sipRequest;                                // This is a copy of the SIP request from m_clientTransaction.
        private UASInviteTransaction m_clientTransaction;
        private SwitchCallMulti m_currentCall;
        private SIPEndPoint m_outboundProxySocket;           // If this app forwards calls via na outbound proxy this value will be set.
        private string m_customSIPHeaders;                  // Allows a dialplan user to add or customise SIP headers.
        private string m_customFromName;
        private string m_customFromUser;
        private string m_customFromHost;
        private SIPCallDirection m_callDirection;

        private ManualResetEvent m_waitForCallCompleted = new ManualResetEvent(false);
        private CallResult m_completedResult;
        
        private string m_callFailureMessage;                // The error message from the first call leg on the final dial attempt used when the call fails to provide a reason.
        public string LastFailureMessage
        {
            get { return m_callFailureMessage; }
        }

        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetSIPAccountBindings_External;   // This event must be wired up to an external function in order to be able to lookup bindings that have been registered for a SIP account.
        private SIPCallManager m_callManager;

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

        private string m_initialTraceMessage;
        public StringBuilder TraceLog;
        private bool m_trace = false;
        public bool Trace
        {
            get { return m_trace; }
            set
            {
                if (value && !m_trace)
                {
                    // Start new trace log.
                    TraceLog = new StringBuilder();
                    TraceLog.AppendLine("DialPlan=> Dialplan trace commenced at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + ".");
                    TraceLog.AppendLine(m_initialTraceMessage);
                    m_trace = true;
                }
                else
                {
                    m_trace = false;
                    TraceLog = null;
                }
            }
        }
      
        public DialPlanScriptHelper(
            SIPTransport sipTransport,
            Guid executionId,
            SIPMonitorLogDelegate logDelegate, 
            DialogueBridgeCreatedDelegate createBridgeDelegate,
            UASInviteTransaction clientTransaction,
            SIPRequest sipRequest,
            SIPCallDirection callDirection,
            string username,
            string adminMemberId,
            List<SIPProvider> sipProviders,
            ExtendScriptLifetimeDelegate extendLifeDelegate,
            GetCanonicalDomainDelegate getCanonicalDomain,
            SIPCallManager callManager,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getSIPAccountBindings,
            SIPEndPoint outboundProxySocket
            )
        {
            m_sipTransport = sipTransport;
            m_executionId = executionId;
            m_dialPlanLogDelegate = logDelegate;
            m_createBridgeDelegate = createBridgeDelegate;
            m_clientTransaction = clientTransaction;
            m_sipRequest = sipRequest;
            m_callDirection = callDirection;
            m_username = username;
            m_adminMemberId = adminMemberId;
            m_sipProviders = sipProviders;
            m_extendLifeDelegate = extendLifeDelegate;
            m_getCanonicalDomainDelegate = getCanonicalDomain;
            m_callManager = callManager;
            GetSIPAccount_External = getSIPAccount;
            GetSIPAccountBindings_External = getSIPAccountBindings;
            m_outboundProxySocket = outboundProxySocket;

            if (m_clientTransaction != null)
            {
                clientTransaction.TransactionTraceMessage += new SIPTransactionTraceMessageDelegate(TransactionTraceMessage);
                m_initialTraceMessage = SIPMonitorEventTypesEnum.SIPTransaction + "=>" + "Request received " + m_clientTransaction.LocalSIPEndPoint +
                    "<-" + m_clientTransaction.RemoteEndPoint + CRLF + m_clientTransaction.TransactionRequest.ToString();
            }
        }

        /// <summary>
        /// Attempts to dial a series of forwards and bridges the first one that connects with the client call.
        /// </summary>
        /// <param name="data">The dial string containing the list of call legs to attempt to forward the call to.</param>
        /// <returns>A code that best represents how the dial command ended.</returns>
        public CallResult Dial(string data)
        {
            return Dial(data, m_maxRingTime);
        }

        /// <summary>
        /// Attempts to dial a series of forwards and bridges the first one that connects with the client call.
        /// </summary>
        /// <param name="data">The dial string containing the list of call legs to attempt to forward the call to.</param>
        /// <param name="ringTimeout">The period in seconds to perservere with the dial command attempt without a final response before giving up.</param>
        /// <returns>A code that best represents how the dial command ended.</returns>
        public CallResult Dial(string data, int ringTimeout)
        {
            return Dial(data, ringTimeout, 0, DialPlanRedirectModeEnum.None, m_clientTransaction);
        }

        /// <summary>
        /// Attempts to dial a series of forwards and bridges the first one that connects with the client call.
        /// </summary>
        /// <param name="data">The dial string containing the list of call legs to attempt to forward the call to.</param>
        /// /// <param name="answeredCallLimit">If greater than 0 this specifies the period in seconds an answered call will be hungup after.</param>
        /// <param name="ringTimeout">The period in seconds to perservere with the dial command attempt without a final response before giving up.</param>
        /// <returns>A code that best represents how the dial command ended.</returns>
        public CallResult Dial(string data, int ringTimeout, int answeredCallLimit)
        {
            return Dial(data, ringTimeout, answeredCallLimit, DialPlanRedirectModeEnum.None, m_clientTransaction);
        }

        /// <summary>
        /// Attempts to dial a series of forwards and bridges the first one that connects with the client call.
        /// </summary>
        /// <param name="data">The dial string containing the list of call legs to attempt to forward the call to.</param>
        /// <param name="ringTimeout">The period in seconds to perservere with the dial command attempt without a final response before giving up.</param>
        /// <param name="answeredCallLimit">If greater than 0 this specifies the period in seconds an answered call will be hungup after.</param>
        /// <param name="redirectMode">Specifies how redirect responses will be handled.</param>
        /// <returns>A code that best represents how the dial command ended.</returns>
        public CallResult Dial(string data, int ringTimeout, int answeredCallLimit, DialPlanRedirectModeEnum redirectMode)
        {
            return Dial(data, ringTimeout, answeredCallLimit, redirectMode, m_clientTransaction);
        }

        /// <summary>
        /// Logs a message with the proxy. Typically this records the message in the database and also prints it out
        /// on the proxy monitor telnet console.
        /// </summary>
        /// <param name="message"></param>
        public void Log(string message)
        {
            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, message, Username));
        }

        /// <summary>
        /// See Callback method below.
        /// </summary>
        //public CallResult Callback(string dest1, string dest2)
        //{
        //    return Callback(dest1, dest2, 0);
        //}

        /// <summary>
        /// Establishes a new call with the client end tied to the proxy. Since the proxy will not be sending any audio the idea is that once
        /// the call is up it should be re-INVITED off somewhere else pronto to avoid the callee sitting their listening to dead air.
        /// </summary>
        /// <param name="dest1">The dial string of the first call to place.</param>
        /// <param name="dest2">The dial string of the second call to place.</param>
        /// <param name="delaySeconds">Delay in seconds before placing the first call. Gives the user a chance to hangup their phone if they are calling themselves back.</param>
        /// <returns>The result of the call.</returns>
        /*public CallResult Callback(string dest1, string dest2, int delaySeconds)
        {
            try
            {
                if (delaySeconds > 0)
                {
                    delaySeconds = (delaySeconds > MAXCALLBACK_DELAY_SECONDS) ? MAXCALLBACK_DELAY_SECONDS : delaySeconds;
                    ExtendScriptTimeout(delaySeconds);
                    Thread.Sleep(delaySeconds * 1000);
                }

                #region Find the call leg destinations.

                SIPDialStringParser resolver = new SIPDialStringParser(m_sipTransport, m_username, m_sipProviders, GetSIPAccountBindings, m_getCanonicalDomainDelegate);

                SIPCallDescriptor call1Struct = SIPCallDescriptor.Empty;
                string localDomain = resolver.GetLocalDomain(dest1);
                if (localDomain != null)
                {
                    List<SIPCallDescriptor> localContacts = resolver.GetCallListForLocalUser(dest1, m_sipRequest.Header.From.ToString(), null);
                    if (localContacts != null && localContacts.Count > 0)
                    {
                        call1Struct = localContacts[0];
                    }
                }
                else
                {
                    call1Struct = resolver.GetCallStructForComamnd(m_sipRequest, dest1);
                }

                SIPCallDescriptor call2Struct = SIPCallDescriptor.Empty;
                string localDomainLeg2 = resolver.GetLocalDomain(dest1);
                if (localDomainLeg2 != null)
                {
                    List<SIPCallDescriptor> localContacts = resolver.GetCallListForLocalUser(dest2, m_sipRequest.Header.From.ToString(), null);
                    if (localContacts != null && localContacts.Count > 0)
                    {
                        call2Struct = localContacts[0];
                    }
                }
                else
                {
                    call2Struct = resolver.GetCallStructForComamnd(m_sipRequest, dest2);
                }

                #endregion

                if (call1Struct == SIPCallDescriptor.Empty)
                {
                    Log("Call not proceeding as the first call leg of " + dest1 + " did not result in a valid destination.");
                    return CallResult.Error;
                }
                else if (call2Struct == SIPCallDescriptor.Empty)
                {
                    Log("Call not proceeding as the second call leg of " + dest1 + " did not result in a valid destination.");
                    return CallResult.Error;
                }
                else
                {
                    Log("Callback proceeding for " + call1Struct.Uri.ToString() + " and " + call2Struct.Uri.ToString() + ".");

                    IPEndPoint contactEndPoint = m_sipTransport.GetTransportContact(null);

                    ClientUserAgent uac1 = new ClientUserAgent(m_sipTransport, Username);
                    uac1.CallFinalResponseReceived += new UserAgentFinalResponseDelegate(CallFinalResponseReceived);

                    Log("Calling first call leg " + dest1 + ".");
                    m_waitForCallCompleted.Reset();
                    uac1.Call(call1Struct, m_sdpContentType, GetInviteRequestBody(contactEndPoint));

                    ExtendScriptTimeout(DEFAULT_CREATECALL_RINGTIME);

                    if (m_waitForCallCompleted.WaitOne(DEFAULT_CREATECALL_RINGTIME * 1000, false))
                    {
                        if (uac1.CallDialogue != null)
                        {
                            string call1SDPIPAddress = uac1.RemoteSDP.Media[0].ConnectionAddress;
                            int call1SDPPort = uac1.RemoteSDP.Media[0].Port;
                            Log("The first call leg to " + dest1 + " was successful, audio socket=" + call1SDPIPAddress + ":" + call1SDPPort + ".");

                            ClientUserAgent uac2 = new ClientUserAgent(m_sipTransport, Username);
                            uac2.CallFinalResponseReceived += new UserAgentFinalResponseDelegate(CallFinalResponseReceived);

                            Log("Calling second call leg " + dest2 + ".");
                            m_waitForCallCompleted.Reset();
                            uac2.Call(call2Struct, m_sdpContentType, GetInviteRequestBody(contactEndPoint));

                            ExtendScriptTimeout(DEFAULT_CREATECALL_RINGTIME);

                            if (m_waitForCallCompleted.WaitOne(DEFAULT_CREATECALL_RINGTIME * 1000, false))
                            {
                                if (uac2.CallDialogue != null)
                                {
                                    string call2SDPIPAddress = uac2.RemoteSDP.Media[0].ConnectionAddress;
                                    int call2SDPPort = uac2.RemoteSDP.Media[0].Port;
                                    Log("The second call leg to " + dest2 + " was successful, audio socket=" + call2SDPIPAddress + ":" + call2SDPPort + ".");

                                    uac1.Reinvite(call2SDPIPAddress, call2SDPPort);
                                    uac2.Reinvite(call1SDPIPAddress, call1SDPPort);

                                    SendRTPPacket(call2SDPIPAddress + ":" + call2SDPPort, call1SDPIPAddress + ":" + call1SDPPort);
                                    SendRTPPacket(call1SDPIPAddress + ":" + call1SDPPort, call2SDPIPAddress + ":" + call2SDPPort);

                                    m_createBridgeDelegate(uac1.CallDialogue, uac2.CallDialogue, Username);

                                    return CallResult.Answered;
                                }
                                else
                                {
                                    Log("The second call leg to " + dest2 + " was not accepted.");
                                    return CallResult.Error;
                                }
                            }
                            else
                            {
                                Log("Second call leg " + dest2 + " failed to answer within " + DEFAULT_CREATECALL_RINGTIME + "s.");
                                return CallResult.Error;
                            }
                        }
                        else
                        {
                            Log("The first call leg to " + dest1 + " was not accepted.");
                            return CallResult.Error;
                        }
                    }
                    else
                    {
                        Log("First call leg " + dest1 + " failed to answer within " + DEFAULT_CREATECALL_RINGTIME + "s.");
                        return CallResult.Error;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialPlanScriptHelper Call. " + excp);
                Log("Exception in Call. " + excp);
                return CallResult.Error;
            }
        }*/

        /// <summary>
        /// Sends a SIP response to the client call. If a final response is sent then the client call will hang up.
        /// </summary>
        /// <param name="statusCode"></param>
        /// <param name="reason"></param>
        public void Respond(int statusCode, string reason)
        {
            if (m_clientTransaction != null)
            {
                SIPReplyApp replyApp = new SIPReplyApp(FireProxyLogEvent, m_clientTransaction, m_username);
                replyApp.Start(statusCode.ToString() + "," + reason);

                if (statusCode >= 200)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "A " + statusCode + " final response was sent to the client at " + m_clientTransaction.RemoteEndPoint + ", terminating script.", m_username));
                    EndScript();
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "A " + statusCode + " informational response was sent to the client at " + m_clientTransaction.RemoteEndPoint + ".", m_username));
                }
            }
            else
            {
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "The client transaction could not be matched for a Respond command, no repsonse sent.", m_username));
            }

        }

        /// <summary>
        /// Temporary replacement for a regular expression match while the Ruby engine is missing regex's.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="pattern"></param>
        /// <returns></returns>
        public bool RegexMatch(string input, string pattern)
        {
            if (input == null || input.Trim().Length == 0 || pattern == null || pattern.Trim().Length == 0)
            {
                return false;
            }
            else
            {
                return Regex.Match(input, pattern, RegexOptions.IgnoreCase).Success;
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
            if (username != Username && Username != "aaron")
            {
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "You are not authorised to call IsAvailable for " + username + "@" + domain + ".", Username));
                return false;
            }

            SIPRegistrarBinding[] bindings = GetBindings(username, domain);
            return (bindings != null && bindings.Length > 0);
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
                if (username != Username && Username != "aaron")
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "You are not authorised to call GetBindings for " + username + "@" + domain + ".", Username));
                    return null;
                }

                SIPParameterlessURI currentUser = new SIPParameterlessURI(m_defaultScheme, domain, username);

                //SIPRegistrarRecord registrarRecord = SIPRegistrations.Lookup(currentUser);
                SIPAccount sipAccount = GetSIPAccount_External(s => s.SIPUsername == username && s.SIPDomain == domain);
                List<SIPRegistrarBinding> bindings = GetSIPAccountBindings_External(s => s.SIPAccountId == sipAccount.Id, 0, Int32.MaxValue);

                if (bindings != null)
                {
                    return bindings.ToArray();
                }
                else
                {
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetBindings. " + excp);
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
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "The dialogue for the ReplaceCall request could not be found, call failed.", m_username));
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

        private CallResult Dial(string data, int ringTimeout, int answeredCallLimit, DialPlanRedirectModeEnum redirectMode, UASInviteTransaction clientTransaction)
        {
            SIPRequest sipRequest = clientTransaction.TransactionRequest;
            SIPDialStringParser callResolver = new SIPDialStringParser(m_sipTransport, m_username, m_sipProviders, GetSIPAccount_External, GetSIPAccountBindings_External, m_getCanonicalDomainDelegate);
            LastDialled = new List<SIPTransaction>();
            m_currentCall = new SwitchCallMulti(m_sipTransport, FireProxyLogEvent, m_createBridgeDelegate, clientTransaction, Username, m_adminMemberId, LastDialled, m_outboundProxySocket);

            try
            {
                m_currentCall.CallComplete += new CallCompletedDelegate(switchCallMulti_CallCompleted);
                m_completedResult = CallResult.Unknown;
                m_waitForCallCompleted.Reset();

                Queue<List<SIPCallDescriptor>> callsQueue = callResolver.ParseDialString(sipRequest, data, m_customSIPHeaders);

                if (m_customFromName != null || m_customFromUser != null || m_customFromHost != null)
                {
                    UpdateCallQueueFromHeaders(callsQueue, m_customFromName, m_customFromUser, m_customFromHost);
                }

                m_currentCall.Start(callsQueue);

                // Wait for an answer.
                ringTimeout = (ringTimeout > m_maxRingTime) ? m_maxRingTime : ringTimeout;
                ExtendScriptTimeout(ringTimeout + DEFAULT_CREATECALL_RINGTIME);
                if (m_waitForCallCompleted.WaitOne(ringTimeout * 1000, false))
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
                    m_currentCall.CancelAllCallLegs();
                    return CallResult.TimedOut;
                }
            }
            catch (ThreadAbortException)
            {
                // This exception will be thrown under normal circumstances as the script will abort the thread if a call is answered.
                return m_completedResult;
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialPlanScriptHelper Dial. " + excp);
                return CallResult.Error;
            }
            finally
            {
                m_currentCall.CallComplete -= new CallCompletedDelegate(switchCallMulti_CallCompleted);
                m_currentCall = null;
            }
        }

        private void switchCallMulti_CallCompleted(UASInviteTransaction clientTransaction, CallResult completedResult, string errorMessage)
        {
            m_completedResult = completedResult;
            m_callFailureMessage = errorMessage;
            m_waitForCallCompleted.Set();
        }

        private void CallFinalResponseReceived(SIPTransaction transaction)
        {
            m_waitForCallCompleted.Set();
        }

        private string GetInviteRequestBody(IPEndPoint localSIPEndPoint)
        {
            string body =
               "v=0" + CRLF +
                "o=- " + Crypto.GetRandomInt(1000, 5000).ToString() + " 2 IN IP4 " + localSIPEndPoint.Address.ToString() + CRLF +
                "s=session" + CRLF +
                "c=IN IP4 " + localSIPEndPoint.Address.ToString() + CRLF +
                "t=0 0" + CRLF +
                "m=audio " + Crypto.GetRandomInt(10000, 20000).ToString() + " RTP/AVP 0 18 101" + CRLF +
                "a=rtpmap:0 PCMU/8000" + CRLF +
                "a=rtpmap:18 G729/8000" + CRLF +
                "a=rtpmap:101 telephone-event/8000" + CRLF +
                "a=fmtp:101 0-16" + CRLF +
                "a=recvonly";

            return body;
        }

        private void SendRTPPacket(string sourceSocket, string destinationSocket)
        {
            try
            {
                //logger.Debug("Attempting to send RTP packet from " + sourceSocket + " to " + destinationSocket + ".");
                Log("Attempting to send RTP packet from " + sourceSocket + " to " + destinationSocket + ".");

                IPEndPoint sourceEP = IPSocket.GetIPEndPoint(sourceSocket);
                IPEndPoint destEP = IPSocket.GetIPEndPoint(destinationSocket);

                RTPPacket rtpPacket = new RTPPacket(80);
                rtpPacket.Header.SequenceNumber = (UInt16)6500;
                rtpPacket.Header.Timestamp = 100000;

                UDPPacket udpPacket = new UDPPacket(sourceEP.Port, destEP.Port, rtpPacket.GetBytes());
                IPv4Header ipHeader = new IPv4Header(ProtocolType.Udp, Crypto.GetRandomInt(6), sourceEP.Address, destEP.Address);
                IPv4Packet ipPacket = new IPv4Packet(ipHeader, udpPacket.GetBytes());

                byte[] data = ipPacket.GetBytes();

                Socket rawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                rawSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, 1);

                rawSocket.SendTo(data, destEP);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendRTPPacket. " + excp.Message);
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

        private void ExtendScriptTimeout(int seconds)
        {
            m_extendLifeDelegate(m_executionId, DateTime.Now.AddSeconds(seconds + DialPlanEngine.MAX_SCRIPTPROCESSING_SECONDS));
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

        private void EndScript()
        {
            //m_extendLifeDelegate(m_executionId, DateTime.Now);
            Thread.CurrentThread.Abort();
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.SIPTransaction, message, Username));
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            try
            {
                if (Trace && TraceLog != null)
                {
                    TraceLog.AppendLine(monitorEvent.EventType + "=> " + monitorEvent.Message);
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
