// ============================================================================
// FileName: SIPDialPlanEngine.cs
//
// Description:
// Converts an Asterisk like SIP dial plan into a useable form for the Proxy server.
//
// Author(s):
// Aaron Clauson
//
// History:
// ??     2006  Aaron Clauson   Created.
// 26 Aug 2007  Aaron Clauson   Added the ability to set the From header and the send to socket in a dial plan command.
// 08 Feb 2008  Aaron Clauson   Added SIP Providers to dial plan to facilitate multi-legged forwards in the Switch command.
// 16 Feb 2008  Aaron Clauson   Added capability for Ruby scripted dial plans.
// 28 Sep 2008  Aaron Clauson   Renamed from SIPDialPlan to SIPDialPlanEngine.
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
using System.Scripting;
using BlueFace.Sys;
using BlueFace.Sys.Net;
using BlueFace.VoIP.App;
using BlueFace.VoIP.App.SIP;
using BlueFace.VoIP.Net;
using BlueFace.VoIP.Net.App;
using BlueFace.VoIP.Net.RTP;
using BlueFace.VoIP.Net.SIP;
using BlueFace.VoIP.SIPServer;
using BlueFace.VoIP.SIPServerCores.StatefulProxy;
using BlueFace.VoIP.SIPServerCores.StatefulProxy.DialPlanApps;
using BlueFace.VoIP.UserAgents;
using Heijden.DNS;
using log4net;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Ruby;
using Ruby.Compiler;
using Ruby.Hosting;
//using IronRuby.Compiler;
//using IronRuby.Hosting;
//using IronRuby;
using agsXMPP;
using agsXMPP.protocol;
using agsXMPP.protocol.client;

#if UNITTEST
using NUnit.Framework;
#endif

namespace BlueFace.VoIP.SIPServerCores.StatefulProxy
{


    public delegate void ExtendScriptLifetimeDelegate(Guid Id, DateTime endTime);
    public delegate void SetTraceDelegate(int threadId, bool trace);

	/// <summary>
	/// Dial plan is in the form:
	/// 
	/// exten =	 100,1,Switch(anonymous.invalid,,612@freeworlddialup.com)
	/// exten =~ 101,1,Switch(anonymous.invalid,,303@sip.blueface.ie)
	/// 
	/// </summary>
	public class SIPDialPlanEngine
	{
        private class SIPDialPlanExecutingScript
        {
            public Guid Id;
            public Thread DialPlanScriptThread;
            public ScriptScope DialPlanScriptScope;
            public bool InUse;
            public string Username;
            public DateTime StartTime;
            public DateTime EndTime;
            public SIPMonitorLogDelegate LogDelegate;

            public SIPDialPlanExecutingScript(ScriptScope scriptScope, SIPMonitorLogDelegate logDelegate)
            {
                Id = Guid.NewGuid();
                DialPlanScriptScope = scriptScope;
                InUse = true;
                LogDelegate = logDelegate;
            }

            public void Initialise(string username)
            {
                Username = username;
                StartTime = DateTime.Now;
                EndTime = StartTime.AddSeconds(MAX_SCRIPTPROCESSING_SECONDS);
                //DialPlanScriptScope.ClearVariables();
            }

            public void StopExecution()
            {
                InUse = false;
                 DialPlanScriptThread.Abort();
            }
        }

        private const string TRACE_FROM_ADDRESS = "siptrace@mysipswitch.com";
        private const string TRACE_SUBJECT = "MySIPSwitch Trace";
        public const string RUBYDIALPLAN_ID = "#Ruby";         // Character used to mark the start and end of a dial plan extension regular expression. The delimiters are optional EXCEPT if the regex has a comma.
        public const string SCRIPT_REQUESTOBJECT_NAME = "req";  // Access using $req from the Ruby script.
        public const string SCRIPT_HELPEROBJECT_NAME = "sys";   // Access using $sys from the Ruby script.
        public const int MAX_SCRIPTPROCESSING_SECONDS = 10;           // The maximum amount of time a script will be able to execute for without completing or executing a Dial command.
        public const int ABSOLUTEMAX_SCRIPTPROCESSING_SECONDS = 300;  // The absolute maximum amount of seconds a script thread will be allowed to execute for.
        public const int MAX_ALLOWED_SCRIPTSCOPES = 20;         // The maximum allowed number of scopesone if which is required for each simultaneously executing script.

		private static ILog logger = AppState.GetLogger("sipproxy");
        private static string m_crLF = SIPConstants.CRLF;

        private static ScriptRuntime m_scriptRuntime;
        private static Dictionary<Guid, SIPDialPlanExecutingScript> m_scriptScopes = new Dictionary<Guid, SIPDialPlanExecutingScript>();
        private static int m_dialPlanScriptContextsCreated;
        public static bool StopScriptMonitoring = false;                    // Set to true to stop the thread keeping an eye on long running dial plan scripts.
        public static string TraceDirectory = null;                         // The directory on the proxy where SIP traces will be dumped.
        public static int ScriptCount
        {
            get { return m_scriptScopes.Count; }
        }

        public string Owner;                        // The username of the dialplan owner.
        public string EmailAddress;                 // For SIP traces.
        public DateTime LastUpdate;                 // The last update time from the DialPlan object used to create this engine. Used to check when a reload is required.
        public bool IsScript = false;               // If true indicates the dial plan is a Ruby or Python script.
        public DateTime LastUsed = DateTime.Now;    // The last time the dial plan was used. 
        public List<SIPProvider> m_sipProviders = new List<SIPProvider>();
        private string m_script;

        private SIPTransport m_sipTransport;
        private IPEndPoint m_outboundProxySocket;                // If this app forwards calls via an outbound proxy this value will be set.
        private SIPMonitorLogDelegate m_proxyCoreLogDelegate;    // Delegate from proxy core to fire when log messages should be bubbled up to the core.
        public event GetSIPAccountBindingsDelegate GetSIPAccountBindings;
        private GetCanonicalDomainDelegate m_getCanonicalDomainDelegate;
        private string m_initialTraceMessage = null;
        public StringBuilder TraceLog;

        static SIPDialPlanEngine()
        {
            try
            {
                Thread monitorScriptsThread = new Thread(new ThreadStart(MonitorScripts));
                monitorScriptsThread.Start();

                m_scriptRuntime = IronRuby.CreateRuntime();

                //ScriptRuntimeSetup setup = new ScriptRuntimeSetup();
                //setup.LanguageSetups.Add(IronRuby.Ruby.CreateLanguageSetup());
                //m_scriptRuntime = IronRuby.Ruby.CreateRuntime(setup);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPDialPlan (static ctor.). " + excp); 
            }
        }

		public SIPDialPlanEngine(
            SIPTransport sipTransport, 
            DialPlan dialPlan,
            List<SIPProvider> sipProviders, 
            string rubyScriptCommon,
            GetCanonicalDomainDelegate getCanonicalDomain,
            IPEndPoint outboundProxySocket)
		{
            //Created++;

            m_sipTransport = sipTransport;
            m_getCanonicalDomainDelegate = getCanonicalDomain;

            Owner = dialPlan.Owner;
            EmailAddress = dialPlan.TraceEmailAddress;
            LastUpdate = dialPlan.LastUpdate;
            m_sipProviders = sipProviders;

            if (dialPlan.DialPlanScript != null && dialPlan.DialPlanScript.StartsWith(RUBYDIALPLAN_ID))
            {
                IsScript = true;
                m_script = rubyScriptCommon + dialPlan.DialPlanScript;
            }
            else if (dialPlan.DialPlanScript != null)
			{
                string[] dialPlanEntries = dialPlan.DialPlanScript.Split(new char[] { '\n' });
				ParseDialPlan(dialPlanEntries);
			}

            m_outboundProxySocket = outboundProxySocket;
		}

        /// <summary>
        /// Processes the matched dial plan command for an outgoing call request. This method is used for "exten =>" formatted dial plans. In addition if the dial
        /// plan owner has requested that their dialplan be used for incoming calls it will process those as well.
        /// </summary>
        /// <param name="coreLogDelegate">A function delegate that passes log/diagnostics events back to the SIP Proxy Core.</param>
        /// <param name="createBridgeDelegate">A function delegate that is called in the event that the dial plan command results in a call being answered and a bridge needing to be created.</param>
        /// <param name="matchedCommand">The command that was found in the dial plan to match the call.</param>
        /// <param name="localEndPoint">The SIP Proxy socket the request was received on.</param>
        /// <param name="remoteEndPoint">The socket the request was recevied from.</param>
        /// <param name="transaction">The SIP Invite transaction that initiated the dial plan processing.</param>
        /// <param name="manglePrivateAddresses">If true private IP addresses will be subtituted for the remote socket.</param>
        /// <param name="canonicalFromDomain">If (and only if) the call is an outgoing call this will be set to the canonical domain of the host in the SIP From
        /// header. An outgoing call is one from an authenticated user destined for an external SIP URI. If the call is an incoming this will be null.</param>
        /// <param name="canonicalToDomain">If (and only if) the call is an incoming call this will be set to the canonical domain of the host in the SIP URI
        /// request. An incoming call is one from an external caller to a URI corresponding to a hosted domain on this SIP Proxy.</param>
        public void ProcessDialPlanRequest(
            SIPMonitorLogDelegate coreLogDelegate, 
            DialogueBridgeCreatedDelegate createBridgeDelegate, 
            SIPDialPlanCommand matchedCommand, 
            IPEndPoint localEndPoint, 
            IPEndPoint remoteEndPoint, 
            UASInviteTransaction transaction, 
            bool manglePrivateAddresses,
            string canonicalFromDomain,
            string canonicalToDomain)
        {
            LastUsed = DateTime.Now;

            SIPRequest sipRequest = transaction.TransactionRequest;
            logger.Info("Processing dialplan for " + Owner + " and " + sipRequest.URI.ToString() + ".");
            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.NewCall, "Call received " + sipRequest.URI.ToString() + ".", Owner));

            try
            {
                if (matchedCommand == null)
                {
                    logger.Debug("No mapping found for " + sipRequest.URI.ToString() + " returning 404 NotFound to " + IPSocket.GetSocketString(remoteEndPoint) + ".");
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.NewCall, "Destination " + sipRequest.URI.ToString() + " not found in dial plan.", Owner));
                    SIPResponse notFoundResponse = SIPTransport.GetResponse(sipRequest.Header, SIPResponseStatusCodesEnum.NotFound, null, localEndPoint, remoteEndPoint);
                    transaction.SendFinalResponse(notFoundResponse);
                }
                else if (Regex.Match(matchedCommand.Command, "Switch|Dial", RegexOptions.IgnoreCase).Success)
                {
                    if (matchedCommand.Data != null && matchedCommand.Data.Trim().Length > 0)
                    {
                        SIPCallResolver callResolver = new SIPCallResolver(m_sipTransport, Owner, m_sipProviders, GetSIPAccountBindings, m_getCanonicalDomainDelegate);
                        SwitchCallMulti switchCallMulti = new SwitchCallMulti(m_sipTransport, FireProxyLogEvent, createBridgeDelegate, (UASInviteTransaction)transaction, manglePrivateAddresses, Owner, canonicalFromDomain, null, m_outboundProxySocket);
                        Queue<List<SIPCallDescriptor>> calls = callResolver.BuildCallList(sipRequest, matchedCommand.Data, null, null, null);
                        switchCallMulti.Start(calls);
                        switchCallMulti.CallComplete += new CallCompletedDelegate(DialPlanCallComplete);
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Error processing dialplan Dial command the dial string was empty.", Owner));
                    }
                }
                else if (Regex.Match(matchedCommand.Command, "RTSP", RegexOptions.IgnoreCase).Success)
                {
                    RTSPApp rtspCall = new RTSPApp(FireProxyLogEvent, (UASInviteTransaction)transaction, Owner);
                    rtspCall.Start(matchedCommand.Data);
                }
                else if (Regex.Match(matchedCommand.Command, "SIPReply", RegexOptions.IgnoreCase).Success)
                {
                    SIPReplyApp replyApp = new SIPReplyApp(FireProxyLogEvent, (UASInviteTransaction)transaction, Owner);
                    replyApp.Start(matchedCommand.Data);
                }
                else
                {
                    logger.Warn("Unmatched command " + matchedCommand.Command + " returning 500 InternalServerError to " + IPSocket.GetSocketString(remoteEndPoint) + ".");

                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Error, "Command " + matchedCommand.Command + " is not a valid dial plan command.", Owner));
                    SIPResponse serverErrorResponse = SIPTransport.GetResponse(sipRequest.Header, SIPResponseStatusCodesEnum.InternalServerError, "Invalid dialplan command " + matchedCommand.Command, localEndPoint, remoteEndPoint);
                    transaction.SendFinalResponse(serverErrorResponse);
                }

                //GC.Collect();
                //logger.Debug("Memory used after full collection: " + GC.GetTotalMemory(true).ToString());
            }
            catch (Exception excp)
            {
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Error, "Error in dialplan for " + sipRequest.URI.ToString() + ". " + SafeXML.MakeSafeXML(excp.Message), Owner));
            }
        }

        /// <summary>
        /// Processes a dialplan script (currently Ruby scripts only) for a received SIP INVITE request.
        /// </summary>
        /// <param name="coreLogDelegate">A function delegate that passes log/diagnostics events back to the SIP Proxy Core.</param>
        /// <param name="createBridgeDelegate">A function delegate that is called in the event that the dial plan command results in a call being answered and a bridge needing to be created.</param>
        /// <param name="localEndPoint">The SIP Proxy socket the request was received on.</param>
        /// <param name="remoteEndPoint">The socket the request was recevied from.</param>
        /// <param name="clientTransaction">The SIP Invite transaction that initiated the dial plan processing.</param>
        /// <param name="canonicalFromDomain">If (and only if) the call is an outgoing call this will be set to the canonical domain of the host in the SIP From
        /// header. An outgoing call is one from an authenticated user destined for an external SIP URI. If the call is an incoming this will be null.</param>
        /// <param name="canonicalToDomain">If (and only if) the call is an incoming call this will be set to the canonical domain of the host in the SIP URI
        /// request. An incoming call is one from an external caller to a URI corresponding to a hosted domain on this SIP Proxy.</param>
        public void ProcessRequest(
            SIPMonitorLogDelegate coreLogDelegate, 
            DialogueBridgeCreatedDelegate createBridgeDelegate, 
            IPEndPoint localEndPoint, 
            IPEndPoint remoteEndPoint, 
            UASInviteTransaction clientTransaction,
            string canonicalFromDomain,
            string canonicalToDomain)
        {
            try
            {
                LastUsed = DateTime.Now;

                TraceLog = null;
                m_initialTraceMessage = SIPMonitorEventTypesEnum.SIPTransaction + "=>" + "Request received " + localEndPoint + "<-" + remoteEndPoint + m_crLF + clientTransaction.TransactionRequest.ToString();
                m_proxyCoreLogDelegate = coreLogDelegate;
                
                if (m_script != null && m_script.Trim().Length > 0)
                {
                     SIPDialPlanExecutingScript dialPlanScriptContext = null;

                     #region Get a script scope form the queue or if there are none available and there are still free slots create a new one.

                     lock (m_scriptScopes)
                    {
                        foreach (SIPDialPlanExecutingScript scriptContext in m_scriptScopes.Values)
                        {
                            if (!scriptContext.InUse)
                            {
                                dialPlanScriptContext = scriptContext;
                                break;
                            }
                        }

                        if (dialPlanScriptContext != null)
                        {
                            dialPlanScriptContext.InUse = true;
                        }
                        else if (m_dialPlanScriptContextsCreated < MAX_ALLOWED_SCRIPTSCOPES)
                        {
                            dialPlanScriptContext = new SIPDialPlanExecutingScript(m_scriptRuntime.CreateScope("IronRuby"), FireProxyLogEvent);
                            m_scriptScopes.Add(dialPlanScriptContext.Id, dialPlanScriptContext);
                            m_dialPlanScriptContextsCreated++;
                        }
                    }

                     #endregion

                     #region If a script scope was obtained create a new thread and execute the script otherwise the callis not processed.

                     if (dialPlanScriptContext != null)
                    {
                        dialPlanScriptContext.Initialise(Owner);
                        dialPlanScriptContext.DialPlanScriptThread = new Thread(new ParameterizedThreadStart(ExecuteScript));
                        Thread scriptThread = dialPlanScriptContext.DialPlanScriptThread;
                        ScriptScope rubyScope = dialPlanScriptContext.DialPlanScriptScope;

                        SIPRequest scriptSIPRequest = clientTransaction.TransactionRequest.Copy();
                        DialPlanScriptHelper planHelper = new DialPlanScriptHelper(m_sipTransport, dialPlanScriptContext.Id, FireProxyLogEvent, createBridgeDelegate, clientTransaction, scriptSIPRequest, canonicalFromDomain, canonicalToDomain, Owner, m_sipProviders, new ExtendScriptLifetimeDelegate(ExtendScriptLifetime), m_getCanonicalDomainDelegate, null, GetSIPAccountBindings, m_outboundProxySocket);

                        rubyScope.SetVariable(SCRIPT_REQUESTOBJECT_NAME, scriptSIPRequest); 
                        rubyScope.SetVariable(SCRIPT_HELPEROBJECT_NAME, planHelper);

                        scriptThread.Start(new object[] { dialPlanScriptContext, clientTransaction, planHelper });
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Error, "Error processing call " + clientTransaction.TransactionRequest.URI.ToString() + " there were no script slots available, script could not be executed.", Owner));
                        DialPlanCallComplete(clientTransaction, CallResult.Error, "Dial plan script engine was overloaded");
                    }

                     #endregion
                }
                else
                {
                    SIPDialPlanCommand matchedCommand = GetDialPlanMatch(m_sipTransport.GetTransportContact(null), remoteEndPoint, clientTransaction.TransactionRequest);

                    if (matchedCommand != null)
                    {
                        if (Regex.Match(matchedCommand.Data, @",\s*true\s*$", RegexOptions.IgnoreCase).Success)
                        {
                            // Trace is required.
                            clientTransaction.TransactionTraceMessage += new SIPTransactionTraceMessageDelegate(TransactionTraceMessage);
                            TraceLog = new StringBuilder();
                            TraceLog.AppendLine("DialPlan=> Dialplan trace commenced at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + ".");
                            TraceLog.AppendLine(m_initialTraceMessage);
                        }

                        ProcessDialPlanRequest(FireProxyLogEvent, createBridgeDelegate, matchedCommand, localEndPoint, remoteEndPoint, clientTransaction, true, canonicalFromDomain, canonicalToDomain);
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Error processing call " + clientTransaction.TransactionRequest.URI.ToString() + " no dial plan match could be made.", Owner));
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPDialPlan ProcessRequest. " + excp.Message);
            }
        }

        /// <summary>
        /// Execute dialplan script for non-invite request such as MESSAGE.
        /// </summary>
        public void ProcessNonInviteRequest(
            SIPMonitorLogDelegate coreLogDelegate,
            CallManager callManager, 
            IPEndPoint localEndPoint,
            IPEndPoint remoteEndPoint,
            SIPRequest sipRequest)
        {
            try
            {
                LastUsed = DateTime.Now;

                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Processing non-INVITE dial plan script for " + sipRequest.Method + " " + sipRequest.URI.ToString() + ".", Owner));

                TraceLog = null;
                //m_initialTraceMessage = SIPMonitorServerTypesEnum.SIPTransaction + "=>" + "Request received " + localEndPoint + "<-" + remoteEndPoint + m_crLF + clientTransaction.TransactionRequest.ToString();
                m_proxyCoreLogDelegate = coreLogDelegate;

                if (m_script != null && m_script.Trim().Length > 0)
                {
                    SIPDialPlanExecutingScript dialPlanScriptContext = null;

                    #region Get a script scope form the queue or if there are none available and there are still free slots create a new one.

                    lock (m_scriptScopes)
                    {
                        foreach (SIPDialPlanExecutingScript scriptContext in m_scriptScopes.Values)
                        {
                            if (!scriptContext.InUse)
                            {
                                dialPlanScriptContext = scriptContext;
                                break;
                            }
                        }

                        if (dialPlanScriptContext != null)
                        {
                            dialPlanScriptContext.InUse = true;
                        }
                        else if (m_dialPlanScriptContextsCreated < MAX_ALLOWED_SCRIPTSCOPES)
                        {
                            dialPlanScriptContext = new SIPDialPlanExecutingScript(m_scriptRuntime.CreateScope("IronRuby"), FireProxyLogEvent);
                            m_scriptScopes.Add(dialPlanScriptContext.Id, dialPlanScriptContext);
                            m_dialPlanScriptContextsCreated++;
                        }
                    }

                    #endregion

                    #region If a script scope was obtained create a new thread and execute the script otherwise the callis not processed.

                    if (dialPlanScriptContext != null)
                    {
                        dialPlanScriptContext.Initialise(Owner);
                        dialPlanScriptContext.DialPlanScriptThread = new Thread(new ParameterizedThreadStart(ExecuteScript));
                        Thread scriptThread = dialPlanScriptContext.DialPlanScriptThread;
                        ScriptScope rubyScope = dialPlanScriptContext.DialPlanScriptScope;

                        DialPlanScriptHelper planHelper = new DialPlanScriptHelper(m_sipTransport, dialPlanScriptContext.Id, FireProxyLogEvent, null, null, sipRequest, null, null, Owner, m_sipProviders, new ExtendScriptLifetimeDelegate(ExtendScriptLifetime), m_getCanonicalDomainDelegate, callManager, GetSIPAccountBindings, m_outboundProxySocket);

                        rubyScope.SetVariable(SCRIPT_REQUESTOBJECT_NAME, sipRequest);
                        rubyScope.SetVariable(SCRIPT_HELPEROBJECT_NAME, planHelper);

                        scriptThread.Start(new object[] { dialPlanScriptContext, null, planHelper });
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Error processing call " + sipRequest.URI.ToString() + " there were no script slots available, script could not be executed.", Owner));
                    }

                    #endregion
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Non-Invite requests can only be processed by script dialplans.", Owner));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPDialPlan ProcessNonInviteRequest. " + excp.Message);
            }
        }

        /// <summary>
        /// The Dial command will not hangup the call if there is an error or no answer in case there are further actions required. It's up to the dialplan
        /// to hangup the call at this point if it does not wish to attempt any more forwards.
        /// </summary>
        /// <param name="clientTransaction"></param>
        /// <param name="completedResult"></param>
        private void DialPlanCallComplete(UASInviteTransaction clientTransaction, CallResult completedResult, string failureMessage)
        {
            try
            {
                // If no one wanted to answer the forwarded calls or the dial plan did not result in anywhere to forward to decline the call.
                if (clientTransaction.TransactionFinalResponse == null)
                {
                    // Need to hangup the client call.

                    // Try 503 service unavailable instead of a 6xx error code as a lot of agents seem to automatically retry the call on a 6xx response.
                    //SIPResponse errorResponse = SIPTransport.GetResponse(clientTransaction.TransactionRequest.Header, SIPResponseStatusCodesEnum.Decline, failureMessage, clientTransaction.SendFromEndPoint, clientTransaction.RemoteEndPoint);
                    SIPResponse errorResponse = SIPTransport.GetResponse(clientTransaction.TransactionRequest.Header, SIPResponseStatusCodesEnum.ServiceUnavailable, failureMessage, clientTransaction.SendFromEndPoint, clientTransaction.RemoteEndPoint);
                    clientTransaction.SendFinalResponse(errorResponse);
                }

                try
                {
                    if (TraceDirectory != null && TraceLog != null && TraceLog.Length > 0)
                    {
                        SIPMonitorEvent traceCompleteEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Dialplan trace completed at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + ".", Owner);
                        TraceLog.AppendLine(traceCompleteEvent.EventType + "=> " + traceCompleteEvent.Message);

                        string traceFilename = TraceDirectory + @"SIPTraces\" + Owner + "-" + DateTime.Now.ToString("ddMMMyyyyHHmmss") + ".txt";
                        StreamWriter traceSW = new StreamWriter(traceFilename);
                        traceSW.Write(TraceLog.ToString());
                        traceSW.Close();

                        if (EmailAddress != null)
                        {
                            logger.Debug("Emailing trace to " + EmailAddress + ".");
                            Email.SendEmail(EmailAddress, TRACE_FROM_ADDRESS, TRACE_SUBJECT, TraceLog.ToString());
                        }
                    }
                }
                catch (Exception traceExcp)
                {
                    logger.Error("Exception Recording Trace SIPDialPlan. " + traceExcp.Message);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPDialPlan CallComplete. " + excp.Message);
            }
        }

        /// <summary>
        /// Decides on which dial plan line matches an incoming call request. The decision is made by 
        /// attempting to locate a match in the user's dial plan. Variable substitutions, all case insensitive:
        ///   - ${dst} and ${exten} will be replaced with request URI user, if ${exten:2} two chars will be trimmed from the start,
        ///   - ${fromname} will be replaced with the From header name,
        ///   - ${fromuriuser} will be replaced with the From header URI user.
        ///   
        /// Extension (or more correctly SIP URI user matching):
        ///   - If the pattern starts with an _ then Asterisk compaitble mode is used:
        ///     - X = any digit,
        ///     - Z = any digit 1 to 9,
        ///     - N = any digit 2 to 9.
        ///   - Otherwise the match is either an equality or regex match depending on the operator.
        /// </summary>
        /// <param name="localEndPoint">The socket on the proxy that the call request was received on.</param>
        /// <param name="remoteEndPoint">The socket the call request was received from.</param>
        /// <param name="sipRequest">The received call request.</param>
        /// <returns>A struct indicating where and how the call should be forwarded on.</returns>
        public SIPDialPlanCommand GetDialPlanMatch(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                LastUsed = DateTime.Now;

                if (m_commands.Count > 0)
                {
                    bool match = false;

                    #region Attempt to find a matching dial plan command for the call.

                    foreach (SIPDialPlanCommand dialPlanCommand in m_commands)
                    {
                        switch (dialPlanCommand.Operation)
                        {
                            case SIPDialPlanOpsEnum.Equals:
                                // Two modes on the equals match, if pattern starts with an underscore use Asterisk compatible mode.

                                if (dialPlanCommand.Destination.StartsWith("_"))
                                {
                                    // X = \d.
                                    // Z = [1-9]
                                    // N = [2-9]
                                    string astPattern = dialPlanCommand.Destination.Substring(1);
                                    string dst = sipRequest.URI.User;
                                    int dstCharIndex = 0;
                                    int astCharIndex = 0;

                                    while (dst != null && dst.Length > dstCharIndex && astPattern != null && astPattern.Length > astCharIndex)
                                    {
                                        char astChar = astPattern.ToCharArray()[astCharIndex];
                                        string dstSubStr = dst.Substring(dstCharIndex, 1);
                                        
                                        if (astChar == '.')
                                        {
                                            match = true;
                                            break;
                                        }
                                        else if (astChar == 'x' || astChar == 'X')
                                        {
                                            if (Regex.Match(dstSubStr, @"[^\d]").Success)
                                            {
                                                break;
                                            }
                                        }
                                        else if (astChar == 'z' || astChar == 'Z')
                                        {
                                            if (Regex.Match(dstSubStr, "[^1-9]").Success)
                                            {
                                                break;
                                            }
                                        }
                                        else if (astChar == 'n' || astChar == 'N')
                                        {
                                            if (Regex.Match(dstSubStr, "[^2-9]").Success)
                                            {
                                                break;
                                            }
                                        }
                                        else if (astChar == '[')
                                        {
                                            int closingBracketIndex = astPattern.Substring(astCharIndex).IndexOf(']');  // Find the next closing bracket after the starting one for this range.
                                            string range = astPattern.Substring(astCharIndex, closingBracketIndex + 1);
                                            if (!Regex.Match(dstSubStr, range).Success)
                                            {
                                                break;
                                            }

                                            //  Move the pattern index up to the closing bracket.
                                            astCharIndex = astCharIndex + closingBracketIndex;
                                        }
                                        else if (astChar.ToString() != dstSubStr)
                                        {
                                            break;
                                        }

                                        if (dstCharIndex == dst.Length - 1 && astCharIndex == astPattern.Length - 1)
                                        {
                                            match = true;
                                            break;
                                        }
                                        else
                                        {
                                            dstCharIndex++;
                                            astCharIndex++;
                                        }
                                    }
                                }
                                else
                                {
                                    match = (dialPlanCommand.Destination == sipRequest.URI.User);
                                }
                                break;
                            case SIPDialPlanOpsEnum.Regex:
                                match = Regex.Match(sipRequest.URI.User, dialPlanCommand.Destination).Success;
                                break;
                            default:
                                // No match.
                                break;
                        }

                        if (match)
                        {
                            logger.Debug("Dial Plan Match for " + sipRequest.URI.User + " and " + dialPlanCommand.ToString());
                            return dialPlanCommand;
                        }
                    }

                    #endregion
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetDialPlanMatch. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Used for incoming calls where an exact match is required on the sipswitch username.
        /// </summary>
        public SIPDialPlanCommand GetDialPlanExactMatch(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                LastUsed = DateTime.Now;

                if (m_commands.Count > 0)
                {
                    bool match = false;
                    foreach (SIPDialPlanCommand dialPlanCommand in m_commands)
                    {
                        switch (dialPlanCommand.Operation)
                        {
                            case SIPDialPlanOpsEnum.Equals:
                                match = (dialPlanCommand.Destination == sipRequest.URI.User);
                                break;

                            default:
                                break;
                        }

                        if (match)
                        {
                            logger.Debug("Dial Plan Exact Match for " + sipRequest.URI.User + " and " + dialPlanCommand.ToString());
                            return dialPlanCommand;
                        }
                    }
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetDialPlanExactMatch. " + excp.Message);
                throw excp;
            }
        }

        /// <summary>
        /// Substitutes the special characters with variables from the SIP request.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="substituteString"></param>
        /// <returns></returns>
        public static string SubstituteRequestVars(SIPRequest request, string substituteString)
        {
            try
            {
                if (substituteString == null || substituteString.Trim().Length == 0 || request == null)
                {
                    return substituteString;
                }
                else
                {
                    string resultString = substituteString;

                    #region Replacing ${dst} and ${exten} with request URI.

                    if (Regex.Match(substituteString, @"\$\{(dst|exten)(:|\})", RegexOptions.IgnoreCase).Success)
                    {
                        MatchCollection matches = Regex.Matches(substituteString, @"\$\{(dst|exten):\d+?\}", RegexOptions.IgnoreCase);
                        foreach (Match match in matches)
                        {
                            int trimCharCount = Convert.ToInt32(Regex.Match(match.Value, @"\$\{(dst|exten):(?<count>\d+)\}", RegexOptions.IgnoreCase).Result("${count}"));

                            if (trimCharCount < request.URI.User.Length)
                            {
                                string trimmedDst = request.URI.User.Substring(trimCharCount);
                                resultString = Regex.Replace(resultString, Regex.Escape(match.Value), trimmedDst, RegexOptions.IgnoreCase);
                            }
                            else
                            {
                                logger.Warn("A SIP destination could not be trimmed " + request.URI.User + " " + substituteString + ", original destination being used.");
                            }
                        }

                        resultString = Regex.Replace(resultString, @"\$\{(dst|exten).*?\}", request.URI.User, RegexOptions.IgnoreCase);
                    }

                    #endregion

                    #region Replacing ${fromname} with the request From header name.

                    if (request.Header.From != null && request.Header.From.FromName != null && Regex.Match(substituteString, @"\$\{fromname\}", RegexOptions.IgnoreCase).Success)
                    {
                        resultString = Regex.Replace(resultString, @"\$\{fromname\}", request.Header.From.FromName, RegexOptions.IgnoreCase);
                    }

                    #endregion

                    #region Replacing ${fromuriuser} with the request From URI user.

                    if (request.Header.From != null && request.Header.From.FromURI != null && request.Header.From.FromURI.User != null && Regex.Match(substituteString, @"\$\{fromuriuser\}", RegexOptions.IgnoreCase).Success)
                    {
                        resultString = Regex.Replace(resultString, @"\$\{fromuriuser\}", request.Header.From.FromURI.User, RegexOptions.IgnoreCase);
                    }

                    #endregion

                    /*#region Replacing ${username} with the switch command username.

                    if (Regex.Match(substituteString, @"\$\{username\}", RegexOptions.IgnoreCase).Success && request.Header.From != null)
                    {
                        resultString = Regex.Replace(resultString, @"\$\{username\}", swCommand.Username, RegexOptions.IgnoreCase);
                    }

                    #endregion*/

                    // If parts of the From header were empty replace them with an empty string.
                    resultString = Regex.Replace(resultString, @"\$\{fromname\}", "", RegexOptions.IgnoreCase);
                    resultString = Regex.Replace(resultString, @"\$\{fromuriuser\}", "", RegexOptions.IgnoreCase);

                    return resultString.Trim();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SubstituteRequestVars. " + excp.Message);
                return substituteString;
            }
        }

        /// <summary>
        /// Updates the dialplan to indictae whether the execution is currently in a Dial command or not.
        /// </summary>
        /// <param name="ringing"></param>
        public static void ExtendScriptLifetime(Guid id, DateTime endTime)
        {
            if (m_scriptScopes.ContainsKey(id))
            {
                lock (m_scriptScopes)
                {
                    m_scriptScopes[id].EndTime = endTime;
                }
            }
        }

        private void ExecuteScript(object state)
        {
            SIPDialPlanExecutingScript dialPlanScriptContext = null;
            UASInviteTransaction clientTransaction = null;
            CallResult callResult = CallResult.Unknown;
            DialPlanScriptHelper planHelper = null;
            string failureMessage = null;

            try
            {
                object[] stateArray = (object[])state;
                dialPlanScriptContext = (SIPDialPlanExecutingScript)stateArray[0];
                clientTransaction = (stateArray[1] != null) ? (UASInviteTransaction)stateArray[1] : null;
                planHelper = (DialPlanScriptHelper)stateArray[2];

                ScriptScope rubyScope = dialPlanScriptContext.DialPlanScriptScope;
                rubyScope.Execute(m_script);

                failureMessage = (planHelper != null) ? planHelper.LastFailureMessage : null;

                /*string[] scriptLines = Regex.Split(scriptSource, @"(\r\n|\r|\n)");
                foreach (string scriptLine in scriptLines)
                {
                    if (scriptLine != null && scriptLine.Trim().Length > 0)
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorServerTypesEnum.DialPlan, "Executing script: " + scriptLine.Trim(), Username));
                        rubyScope.Execute(scriptLine);
                    }
                }*/
            }
            catch (ThreadAbortException)
            { }
            catch (System.Scripting.SyntaxErrorException)
            //catch (Microsoft.Scripting.SyntaxErrorException)
            {
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "There was a syntax error in your dial plan, please check.", Owner));
                callResult = CallResult.Error;
                failureMessage = "Dial plan syntax error";
            }
            catch (MissingMethodException)
            {
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "There was a missing method exception in your dial plan, please check.", Owner));
                callResult = CallResult.Error;
                failureMessage = "Dial plan missing method";
            }
            catch (Exception excp)
            {
                logger.Error("Exception ExecuteScript. " + excp.Message);
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "There was an unknown exception executing your dial plan script.", Owner));
                callResult = CallResult.Error;
                failureMessage = "Dial plan exception";
            }
            finally
            {
                lock (m_scriptScopes)
                {
                    dialPlanScriptContext.InUse = false;
                }

                if (clientTransaction != null)
                {
                    clientTransaction.TransactionTraceMessage -= new SIPTransactionTraceMessageDelegate(TransactionTraceMessage);
                    DialPlanCallComplete(clientTransaction, callResult, failureMessage);

                    try
                    {
                        if (TraceDirectory != null && planHelper != null && planHelper.TraceLog != null && planHelper.TraceLog.Length > 0)
                        {
                            SIPMonitorEvent traceCompleteEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Dialplan trace completed at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss:fff") + ".", Owner);
                            planHelper.TraceLog.AppendLine(traceCompleteEvent.EventType + "=> " + traceCompleteEvent.Message);

                            string traceFilename = TraceDirectory + @"SIPTraces\" + Owner + "-" + DateTime.Now.ToString("ddMMMyyyyHHmmss") + ".txt";
                            StreamWriter traceSW = new StreamWriter(traceFilename);
                            traceSW.Write(planHelper.TraceLog.ToString());
                            traceSW.Close();

                            if (EmailAddress != null)
                            {
                                logger.Debug("Emailing trace to " + EmailAddress + ".");
                                Email.SendEmail(EmailAddress, TRACE_FROM_ADDRESS, TRACE_SUBJECT, planHelper.TraceLog.ToString());
                            }
                        }
                    }
                    catch (Exception traceExcp)
                    {
                        logger.Error("Exception Recording Trace SIPDialPlanEngine. " + traceExcp.Message);
                    }
                }
            }
        }

        private static void MonitorScripts()
        {
            try
            {
                while (!StopScriptMonitoring)
                {
                    List<SIPDialPlanExecutingScript> killScripts = new List<SIPDialPlanExecutingScript>();

                    if (m_scriptScopes != null && m_scriptScopes.Count > 0)
                    {
                        lock (m_scriptScopes)
                        {
                            foreach (SIPDialPlanExecutingScript executingScript in m_scriptScopes.Values)
                            {
                                if (executingScript.InUse && (DateTime.Now > executingScript.EndTime || DateTime.Now.Subtract(executingScript.StartTime).TotalSeconds > ABSOLUTEMAX_SCRIPTPROCESSING_SECONDS))
                                {
                                    killScripts.Add(executingScript);
                                }
                            }
                        }
                    }

                    foreach (SIPDialPlanExecutingScript killScript in killScripts)
                    {
                        killScript.LogDelegate(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Long running dialplan script was terminated.", killScript.Username));
                        killScript.StopExecution();
                    }

                    Thread.Sleep(2000);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MonitorScripts. " + excp);
            }
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.SIPTransaction, message, Owner));
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            try
            {
                if (TraceLog != null)
                {
                    TraceLog.AppendLine(monitorEvent.EventType + "=> " + monitorEvent.Message);
                }

                if (m_proxyCoreLogDelegate != null)
                {
                    m_proxyCoreLogDelegate(monitorEvent);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireProxyLogEvent SIPDialPlanEngine. " + excp.Message);
            }
        }

		#region Unit testing.

		#if UNITTEST

		[TestFixture]
		public class SIPDialPlanUnitTest
		{			
			[TestFixtureSetUp]
			public void Init()
			{ }

            [TestFixtureTearDown]
            public void Dispose()
            { }

			[Test]
			public void SampleTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				Assert.IsTrue(true, "True was false.");

				Console.WriteLine("---------------------------------"); 
			}
			
			[Test]
			public void SimpleDialPlanTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				string testDialPlan = @"
					exten => 100,1,Switch(""anonymous.invalid"", ""password"", ""612@freeworlddialup.com"")
					exten => 101,1,Switch(""username"", ""password"", ""303@sip.blueface.ie"")
				";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);

                Console.WriteLine("dst=" + dialPlan.m_commands[0].Destination + ", data=" + dialPlan.m_commands[0].Data + ".");
                Console.WriteLine("dst=" + dialPlan.m_commands[1].Destination + ", data=" + dialPlan.m_commands[1].Data + ".");

				Assert.IsTrue(dialPlan.m_commands.Count == 2, "The dial plan was not correctly parsed.");
                Assert.IsTrue(dialPlan.m_commands[0].Operation == SIPDialPlanOpsEnum.Equals, "Command 1 oeration not correct.");
                Assert.IsTrue(dialPlan.m_commands[1].Operation == SIPDialPlanOpsEnum.Equals, "Command 2 oeration not correct.");
                Assert.IsTrue(dialPlan.m_commands[0].Destination == "100", "Command 1 destination not correct.");
                Assert.IsTrue(dialPlan.m_commands[1].Destination == "101", "Command 2 destination not correct.");
                Assert.IsTrue(dialPlan.m_commands[0].Command == "Switch", "Command 1 command not correct.");
                Assert.IsTrue(dialPlan.m_commands[1].Command == "Switch", "Command 2 command not correct.");
                Assert.IsTrue(dialPlan.m_commands[0].Data == "\"anonymous.invalid\", \"password\", \"612@freeworlddialup.com\"", "Command 1 data not correct.");
                Assert.IsTrue(dialPlan.m_commands[1].Data == "\"username\", \"password\", \"303@sip.blueface.ie\"", "Command 2 data not correct.");

				Console.WriteLine("---------------------------------"); 
			}	

			[Test]
			public void CommentOnLineTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				string testDialPlan = @"
					exten => 100,1,Switch(anonymous.invalid, password, 612@freeworlddialup.com) ; Comment
					exten => 101,1,Switch(""username"", ""password"", ""303@sip.blueface.ie)
				";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);

				Console.WriteLine("dst=" + dialPlan.m_commands[0].Destination + ", data=" + dialPlan.m_commands[0].Data + ".");
				Console.WriteLine("dst=" + dialPlan.m_commands[1].Destination + ", data=" + dialPlan.m_commands[1].Data + ".");

				Assert.IsTrue(dialPlan.m_commands.Count == 2, "The dial plan was not correctly parsed.");
                Assert.IsTrue(dialPlan.m_commands[0].Command == "Switch", "The dial plan command was not correct.");
                Assert.IsTrue(dialPlan.m_commands[0].Data == "anonymous.invalid, password, 612@freeworlddialup.com", "The dial plan data was not correct.");

				Console.WriteLine("---------------------------------"); 
			}	

			[Test]
			public void DifferentOperatorsDialPlanTest()
			{
				Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
				
				string testDialPlan = @"
					exten == 100,1,Switch(""anonymous.invalid"", ""password"", ""612@freeworlddialup.com"")
					exten =~ 101,1,Switch(""username"", ""password"", ""303@sip.blueface.ie"")
					exten = 103,1,Switch(""username"", ""password"", ""303@sip.blueface.ie)
				";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);

				Assert.IsTrue(dialPlan.m_commands.Count == 3, "The dial plan was not correctly parsed.");
                Assert.IsTrue(dialPlan.m_commands[0].Operation == SIPDialPlanOpsEnum.Equals, "Command 1 operation was incorrect.");
                Assert.IsTrue(dialPlan.m_commands[1].Operation == SIPDialPlanOpsEnum.Regex, "Command 2 operation was incorrect.");
                Assert.IsTrue(dialPlan.m_commands[2].Operation == SIPDialPlanOpsEnum.Equals, "Command 3 operation was incorrect.");

				Console.WriteLine("---------------------------------"); 
			}

            [Test]
            public void NoMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = @"
					exten => _3100,1,Switch(anon, password, 1@sip.blueface.ie)
					exten => _3300,1,Switch(anon, password, 2@sip.blueface.ie)
					exten => _3000,1,Switch(anon, password, 3@sip.blueface.ie)
				";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3200@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsTrue(dialPlan.m_commands.Count == 3, "The dial plan was not correctly parsed.");
                Assert.IsNull(commandMatch, "The dial plan produced a match when it should not have.");

                Console.WriteLine("---------------------------------");
            }	

            [Test]
            public void NMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = @"
					exten => _3100,1,Switch(anon, password, 1@sip.blueface.ie)
					exten => _3N00,1,Switch(anon, password, 2@sip.blueface.ie)
					exten => _3000,1,Switch(anon, password, 3@sip.blueface.ie)
				";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3200@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsTrue(dialPlan.m_commands.Count == 3, "The dial plan was not correctly parsed.");
                Assert.IsTrue(commandMatch.Data == "anon, password, 2@sip.blueface.ie", "The dial plan command match was not correct.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void ZMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = @"
					exten => _3000,1,Switch(anon, password, 1@sip.blueface.ie)
					exten => _3001,1,Switch(anon, password, 2@sip.blueface.ie)
					exten => _3Z00,1,Switch(anon, password, 3@sip.blueface.ie)
				";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3100@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsTrue(dialPlan.m_commands.Count == 3, "The dial plan was not correctly parsed.");
                Assert.IsTrue(commandMatch.Data == "anon, password, 3@sip.blueface.ie", "The dial plan command match was not correct.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleXMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3X.,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:300@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleZMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3Z.,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:310@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleNMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3N.,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:320@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleRangeMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3[2-57-9].,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleRangeNoMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3[2-57-9].,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:360@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNull(commandMatch, "The dial plan should not have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void MutliRangeMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3[2-57-9]X[1-3],1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3802@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void MutliRangeNoMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3[2-57-9]X[1-3].,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3807@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNull(commandMatch, "The dial plan should not have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void TestLoadOldDefaultDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    "; Example extensions\n" +
                    "exten = 100,1,Switch(anon,,303@sip.blueface.ie)\n" +
                    "exten == 101,1,Switch(anon,,612@fwd.pulver.com)\n" +
                    "exten => _*1X.,1,Switch(user,pass,${EXTEN:2}@sip.blueface.ie)\n" +
                    "exten => _*2X.,1,Switch(anon,,${EXTEN:2}@fwd.pulver.com)\n";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);

                Assert.IsNotNull(dialPlan, "The default dial plan could not be loaded.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void RegexWithCommaLoadDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    "exten =~ /3d{4,6}/,1,Switch(anon,,${dst}@sip.blueface.ie, \"sip switch\" <sip:anon@sip.mysipswitch.com>, 194.213.29.100)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);

                Assert.IsNotNull(dialPlan, "The dial plan could not be loaded.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void RegexWithCommaMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    @"exten =~ /\d{3,6}/,1,Switch(anon,,${dst}@sip.blueface.ie, ""sip switch"" <sip:anon@sip.mysipswitch.com>, 194.213.29.100)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void RegexWithCommaNoMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    @"exten =~ /\D{3,6}/,1,Switch(anon,,${dst}@sip.blueface.ie, ""sip switch"" <sip:anon@sip.mysipswitch.com>, 194.213.29.100)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNull(commandMatch, "The dial plan should not have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void RegexWithNoCommaMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    @"exten =~ \d{3},1,Switch(anon,,${dst}@sip.blueface.ie, ""sip switch"" <sip:anon@sip.mysipswitch.com>, 194.213.29.100)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, null, null, null);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                SIPDialPlanCommand commandMatch = dialPlan.GetDialPlanMatch(null, null, request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueDstVarTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
               
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${dst}123");

                Console.Write("Substituted string=" + substitutedString + ".");
 
                Assert.IsTrue(substitutedString == "380123", "The destination was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueEmptyFromNameTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${fromname} <sip:user@provider>");

                Console.Write("Substituted string=" + substitutedString + ".");

                Assert.IsTrue(substitutedString == "<sip:user@provider>", "The from header was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueDoubleDstVarTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${dst}123${dst}");

                Console.Write("Substituted string=" + substitutedString + ".");

                Assert.IsTrue(substitutedString == "380123380", "The destination was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueDoubleDstSubStrVarTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${dst:1}123${dst:2}");

                Console.Write("Substituted string=" + substitutedString + ".");

                Assert.IsTrue(substitutedString == "801230", "The destination was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SubstitueMixedDstSubStrVarTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                request.Header = new SIPHeader();
                string substitutedString = SubstituteRequestVars(request, "${dst:1}123${dst}000");

                Console.Write("Substituted string=" + substitutedString + ".");

                Assert.IsTrue(substitutedString == "80123380000", "The destination was not substituted correctly.");

                Console.WriteLine("---------------------------------");
            }
		}

		#endif

		#endregion
	}
}
