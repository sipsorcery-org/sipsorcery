// ============================================================================
// FileName: DialPlanEngine.cs
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
// 11 Jun 2010  Aaron Clauson   Added per user execution count property.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Australia (www.sipsorcery.com)
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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Transactions;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;
using Microsoft.Scripting;
using Microsoft.Scripting.Interpreter;
using Microsoft.Scripting.Hosting;
using IronRuby;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.AppServer.DialPlan
{
    /// <summary>
    /// Dial plan is in the form:
    /// 
    /// exten =	 100,1,Switch(anonymous.invalid,,612@freeworlddialup.com)
    /// exten =~ 101,1,Switch(anonymous.invalid,,303@sip.blueface.ie)
    /// 
    /// </summary>
    public class DialPlanEngine
    {
        private const string MONITOR_THREAD_NAME = "dialplanengine-monitor";
        public const string SCRIPT_REQUESTOBJECT_NAME = "req";        // Access using $req from the Ruby script.
        public const string SCRIPT_HELPEROBJECT_NAME = "sys";         // Access using $sys from the Ruby script.
        public const string SCRIPT_CRMOBJECT_NAME = "crm";            // Access using $crm from the Ruby script.
        public const string SCRIPT_LOOKUPOBJECT_NAME = "lookup";      // Access using $lookup from the Ruby script.
        public const int ABSOLUTEMAX_SCRIPTPROCESSING_SECONDS = 300;  // The absolute maximum amount of seconds a script thread will be allowed to execute for.
        public const int MAX_ALLOWED_SCRIPTSCOPES = 20;               // The maximum allowed number of scopes one if which is required for each simultaneously executing script.
        private const string RUBY_COMMON_COPY_EXTEN = ".tmp";
        private const int RUBY_COMMON_RELOAD_INTERVAL = 2;

        private static ILog logger;

        //private ScriptEngine m_scriptEngine;
        private List<DialPlanExecutingScript> m_runningScripts = new List<DialPlanExecutingScript>();
        private int m_dialPlanScriptContextsCreated;
        public bool StopScriptMonitoring = false;                    // Set to true to stop the thread keeping an eye on long running dial plan scripts.
        public int ScriptCount
        {
            get { return m_runningScripts.Count; }
        }

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxySocket;                           // If this app forwards calls via an outbound proxy this value will be set.
        private SIPMonitorLogDelegate LogDelegate_External;                  // Delegate from proxy core to fire when log messages should be bubbled up to the core.
        private SIPAssetPersistor<SIPAccount> m_sipAccountPersistor;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPAssetGetListDelegate<SIPRegistrarBinding> GetSIPAccountBindings_External;
        private GetCanonicalDomainDelegate GetCanonicalDomainDelegate_External;
        private SIPAssetPersistor<SIPDialPlan> m_dialPlanPersistor;
        private string m_impersonationUsername;
        private string m_impersonationPassword;

        private DateTime? m_rubyCommonLastReload;
        private string m_rubyScriptCommonPath;
        private string m_rubyScriptCommon;

        static DialPlanEngine()
        {
            logger = AppState.GetLogger("dialplanengine");
        }

        public DialPlanEngine(
            SIPTransport sipTransport,
            GetCanonicalDomainDelegate getCanonicalDomain,
            SIPMonitorLogDelegate logDelegate,
            SIPAssetPersistor<SIPAccount> sipAssetPersistor,
            SIPAssetGetListDelegate<SIPRegistrarBinding> getBindings,
            SIPAssetPersistor<SIPDialPlan> dialPlanPersistor,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPEndPoint outboundProxySocket,
            string rubyScriptCommonPath,
            string impersonationUsername,
            string impersonationPassword)
        {
            m_sipTransport = sipTransport;
            GetCanonicalDomainDelegate_External = getCanonicalDomain;
            LogDelegate_External = logDelegate;
            m_sipAccountPersistor = sipAssetPersistor;
            GetSIPAccountBindings_External = getBindings;
            m_dialPlanPersistor = dialPlanPersistor;
            m_sipDialoguePersistor = sipDialoguePersistor;
            m_outboundProxySocket = outboundProxySocket;
            m_rubyScriptCommonPath = rubyScriptCommonPath;
            m_impersonationUsername = impersonationUsername;
            m_impersonationPassword = impersonationPassword;

            LoadRubyCommonScript();

            Thread monitorScriptsThread = new Thread(new ThreadStart(MonitorScripts));
            monitorScriptsThread.Name = MONITOR_THREAD_NAME;
            monitorScriptsThread.Start();
        }

        /// <summary>
        /// Gets the number of currently executing dial plan scripts for the specified username.
        /// </summary>
        /// <param name="username">The username to get the execution count for.</param>
        /// <returns>The number of currently executing dial plan scripts for the specified user.</returns>
        public int GetExecutionCountForUser(string username)
        {
            try
            {
                return (from script in m_runningScripts where script.Owner == username select script).Count();
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetExecutionCountForUser. " + excp.Message);
                return 0;
            }
        }

        public void Execute(
          DialPlanContext dialPlanContext,
          ISIPServerUserAgent uas,
          SIPCallDirection callDirection,
          DialogueBridgeCreatedDelegate createBridgeDelegate,
          ISIPCallManager callManager)
        {
            if (dialPlanContext == null)
            {
                throw new ArgumentNullException("The DialPlanContext parameter cannot be null when attempting to execute a dialplan.");
            }

            if (uas.IsUASAnswered)
            {
                // This can occur if the call is cancelled by the caller between when the INVITE was received and when the dialplan execution was ready.
                logger.Warn("Dialplan execution for " + dialPlanContext.SIPDialPlan.DialPlanName + " for " + uas.CallDirection + " call to " + uas.CallDestination + " did not proceed as call already answered.");
                dialPlanContext.DialPlanExecutionFinished();
            }
            else
            {
                if (dialPlanContext.ContextType == DialPlanContextsEnum.Line)
                {
                    ThreadPool.QueueUserWorkItem(delegate { ExecuteDialPlanLine((DialPlanLineContext)dialPlanContext, uas, callDirection, createBridgeDelegate, callManager); });
                }
                else
                {
                    ExecuteDialPlanScript((DialPlanScriptContext)dialPlanContext, uas, callDirection, createBridgeDelegate, callManager);
                }
            }
        }

        /// <summary>
        /// Processes the matched dial plan command for an outgoing call request. This method is used for "exten =>" formatted dial plans. In addition if the dial
        /// plan owner has requested that their dialplan be used for incoming calls it will process those as well.
        /// </summary>
        /// <param name="localEndPoint">The SIP Proxy socket the request was received on.</param>
        /// <param name="remoteEndPoint">The socket the request was recevied from.</param>
        /// <param name="transaction">The SIP Invite transaction that initiated the dial plan processing.</param>
        /// <param name="manglePrivateAddresses">If true private IP addresses will be subtituted for the remote socket.</param>
        /// <param name="canonicalFromDomain">If (and only if) the call is an outgoing call this will be set to the canonical domain of the host in the SIP From
        /// header. An outgoing call is one from an authenticated user destined for an external SIP URI. If the call is an incoming this will be null.</param>
        /// <param name="canonicalToDomain">If (and only if) the call is an incoming call this will be set to the canonical domain of the host in the SIP URI
        /// request. An incoming call is one from an external caller to a URI corresponding to a hosted domain on this SIP Proxy.</param>
        private void ExecuteDialPlanLine(
            DialPlanLineContext dialPlanContext,
            ISIPServerUserAgent uas,
            SIPCallDirection callDirection,
            DialogueBridgeCreatedDelegate createBridgeDelegate,
            ISIPCallManager callManager)
        {
            try
            {
                //SIPRequest sipRequest = uas.CallRequest;
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Executing line dial plan for call to " + uas.CallDestination + ".", dialPlanContext.Owner));

                DialPlanCommand matchedCommand = dialPlanContext.GetDialPlanMatch(uas.CallDestination);

                if (matchedCommand == null)
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Destination " + uas.CallDestination + " not found in line dial plan " + dialPlanContext.SIPDialPlan.DialPlanName + ".", dialPlanContext.Owner));
                    dialPlanContext.CallFailed(SIPResponseStatusCodesEnum.NotFound, null, null);
                }
                else if (Regex.Match(matchedCommand.Command, "Switch|Dial", RegexOptions.IgnoreCase).Success)
                {
                    if (matchedCommand.Data != null && matchedCommand.Data.Trim().Length > 0)
                    {
                        DialStringParser dialStringParser = new DialStringParser(m_sipTransport, dialPlanContext.Owner, dialPlanContext.SIPAccount, dialPlanContext.SIPProviders, m_sipAccountPersistor.Get, GetSIPAccountBindings_External, GetCanonicalDomainDelegate_External, LogDelegate_External, dialPlanContext.SIPDialPlan.DialPlanName);
                        ForkCall ForkCall = new ForkCall(m_sipTransport, FireProxyLogEvent, callManager.QueueNewCall, dialStringParser, dialPlanContext.Owner, dialPlanContext.AdminMemberId, m_outboundProxySocket, null, null);
                        ForkCall.CallProgress += dialPlanContext.CallProgress;
                        ForkCall.CallFailed += dialPlanContext.CallFailed;
                        ForkCall.CallAnswered += dialPlanContext.CallAnswered;
                        Queue<List<SIPCallDescriptor>> calls = dialStringParser.ParseDialString(DialPlanContextsEnum.Line, uas.CallRequest.Copy(), matchedCommand.Data, null, null, null, dialPlanContext.CallersNetworkId, null, null, null, null, CustomerServiceLevels.None);
                        ForkCall.Start(calls);
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Error processing dialplan Dial command the dial string was empty.", dialPlanContext.Owner));
                    }
                }
                //else if (Regex.Match(matchedCommand.Command, "RTSP", RegexOptions.IgnoreCase).Success)
                //{
                //    RTSPApp rtspCall = new RTSPApp(FireProxyLogEvent, (UASInviteTransaction)transaction, dialPlanContext.Owner);
                //    rtspCall.Start(matchedCommand.Data);
                //}
                else if (Regex.Match(matchedCommand.Command, "SIPReply", RegexOptions.IgnoreCase).Success)
                {
                    string[] replyFields = matchedCommand.Data.Split(',');
                    string statusMessage = (replyFields.Length > 1 && replyFields[1] != null) ? replyFields[1].Trim() : null;
                    SIPResponseStatusCodesEnum status = SIPResponseStatusCodes.GetStatusTypeForCode(Convert.ToInt32(replyFields[0]));
                    if ((int)status >= 300)
                    {
                        dialPlanContext.CallFailed(status, statusMessage, null);
                    }
                    else if ((int)status < 200)
                    {
                        dialPlanContext.CallProgress(status, statusMessage, null, null, null, null);
                    }
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Command " + matchedCommand.Command + " is not a valid dial plan command.", dialPlanContext.Owner));
                    dialPlanContext.CallFailed(SIPResponseStatusCodesEnum.InternalServerError, "Invalid dialplan command " + matchedCommand.Command, null);
                }
            }
            catch (Exception excp)
            {
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Error executing line dialplan for " + uas.CallRequest.URI.ToString() + ". " + excp.Message, dialPlanContext.Owner));
                dialPlanContext.DialPlanExecutionFinished();
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
        private void ExecuteDialPlanScript(
            DialPlanScriptContext dialPlanContext,
            ISIPServerUserAgent uas,
            SIPCallDirection callDirection,
            DialogueBridgeCreatedDelegate createBridgeDelegate,
            ISIPCallManager callManager)
        {
            try
            {
                if (uas == null)
                {
                    throw new ArgumentNullException("The ISIPServerUserAgent parameter cannot be null when attempting to execute a dialplan script.");
                }

                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.NewCall, "Executing script dial plan for call to " + uas.CallDestination + ".", dialPlanContext.Owner));

                if (!dialPlanContext.DialPlanScript.IsNullOrBlank())
                {
                    DialPlanExecutingScript dialPlanExecutionScript = null;
                    int runningScriptCount = (from script in m_runningScripts where !script.Complete select script).Count();

                    if (runningScriptCount < MAX_ALLOWED_SCRIPTSCOPES)
                    {
                        m_dialPlanScriptContextsCreated++;
                        //FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Creating DialPlanExecutingScript number " + m_dialPlanScriptContextsCreated + " for dialplan execution for script owned by " + dialPlanContext.Owner + ".", null));
                        dialPlanExecutionScript = new DialPlanExecutingScript(FireProxyLogEvent);
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Running script limit of " + MAX_ALLOWED_SCRIPTSCOPES + " reached.", null));
                        lock (m_runningScripts)
                        {
                            foreach (DialPlanExecutingScript runningScript in m_runningScripts)
                            {
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, " running script owner=" + runningScript.Owner + ", dialplan name=" + runningScript.ExecutingDialPlanContext.SIPDialPlan.DialPlanName + ", start time=" + runningScript.StartTime.ToString("dd MMM yyyy HH:mm:ss") + ", is complete=" + runningScript.Complete + ".", null));
                            }
                        }
                    }

                    if (dialPlanExecutionScript != null)
                    {
                        dialPlanExecutionScript.Initialise(dialPlanContext);

                        DialPlanScriptFacade planFacade = new DialPlanScriptFacade(
                            m_sipTransport,
                            dialPlanExecutionScript,
                            FireProxyLogEvent,
                            createBridgeDelegate,
                            (uas.CallRequest != null) ? uas.CallRequest.Copy() : null,      // A different copy to the req object. Stops inadvertent changes in the dialplan.
                            callDirection,
                            dialPlanContext,
                            GetCanonicalDomainDelegate_External,
                            callManager,
                            m_sipAccountPersistor,
                            m_dialPlanPersistor,
                            m_sipDialoguePersistor,
                            GetSIPAccountBindings_External,
                            m_outboundProxySocket,
                            this);

                        DialPlanCRMFacade crmFacade = new DialPlanCRMFacade(FireProxyLogEvent, dialPlanContext);
                        DialPlanLookupFacade lookupFacade = new DialPlanLookupFacade(FireProxyLogEvent, dialPlanContext.Owner);

                        ScriptScope rubyScope = dialPlanExecutionScript.DialPlanScriptScope;
                        rubyScope.SetVariable(SCRIPT_HELPEROBJECT_NAME, planFacade);
                        rubyScope.SetVariable(SCRIPT_CRMOBJECT_NAME, crmFacade);
                        rubyScope.SetVariable(SCRIPT_LOOKUPOBJECT_NAME, lookupFacade);
                        if (uas.CallRequest != null)
                        {
                            rubyScope.SetVariable(SCRIPT_REQUESTOBJECT_NAME, uas.CallRequest.Copy());
                        }

                        dialPlanExecutionScript.DialPlanScriptThread = new Thread(new ParameterizedThreadStart(delegate { ExecuteScript(dialPlanExecutionScript, dialPlanContext, planFacade, m_rubyScriptCommon + dialPlanContext.DialPlanScript); }));

                        lock (m_runningScripts)
                        {
                            m_runningScripts.Add(dialPlanExecutionScript);
                        }

                        dialPlanExecutionScript.DialPlanScriptThread.Start();
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Error processing call " + uas.CallDestination + " there were no script slots available, script could not be executed.", dialPlanContext.Owner));
                        dialPlanContext.CallFailed(SIPResponseStatusCodesEnum.InternalServerError, "Dial plan script engine was overloaded", null);
                    }
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A script dial plan was empty, execution cannot continue.", dialPlanContext.Owner));
                    dialPlanContext.CallFailed(SIPResponseStatusCodesEnum.InternalServerError, "Dial plan script was empty", null);
                }
            }
            catch (Exception excp)
            {
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Error executing script dialplan for " + uas.CallDestination + ". " + excp.Message, dialPlanContext.Owner));
                dialPlanContext.CallFailed(SIPResponseStatusCodesEnum.InternalServerError, "Dial plan exception starting script", null);
            }
        }

        private void ExecuteScript(
            DialPlanExecutingScript executingScript,
            DialPlanContext dialPlanContext,
            DialPlanScriptFacade planFacade,
            string script)
        {
            try
            {
                Thread.CurrentThread.Name = "dialplanscript-" + executingScript.ScriptNumber;
                if (m_impersonationUsername != null && m_impersonationPassword != null)
                {
                    WrapperImpersonationContext impersonationConext = new WrapperImpersonationContext(null, m_impersonationUsername, m_impersonationPassword);
                    impersonationConext.Enter();
                }
                //logger.Debug(Thread.CurrentThread.Name + " identity=" + WindowsIdentity.GetCurrent().Name + ".");
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dial plan execution starting on thread " + Thread.CurrentThread.Name + " for " + dialPlanContext.Owner + ".", null));
                executingScript.DialPlanScriptEngine.Execute(script, executingScript.DialPlanScriptScope);
                //FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dial plan execution finished after full script run on thread " + Thread.CurrentThread.Name + " for " + dialPlanContext.Owner + ".", null));
            }
            catch (ApplicationException appExcp)
            {
                if (appExcp.Message != "Script was halted by external intervention.")
                {
                    logger.Error("ApplicationException ExecuteScript. " + appExcp.Message);
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "There was an exception executing your dial plan script: " + appExcp.Message, executingScript.Owner));
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "ApplicationException on user " + executingScript.Owner + "'s dial plan script. " + appExcp.Message, null));
                    executingScript.ExecutionError = appExcp.Message;
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dial plan execution finished after being halted due to execution interrupt on thread " + Thread.CurrentThread.Name + " for " + dialPlanContext.Owner + ".", null));
                }
            }
            //catch (System.Scripting.SyntaxErrorException)
            catch (SyntaxErrorException syntaxExcp)
            {
                logger.Warn("SyntaxErrorException. Owner=" + dialPlanContext.Owner + ", DialPlanName=" + dialPlanContext.SIPDialPlan.DialPlanName + ", Line=" + syntaxExcp.Line + ".");
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "There was a syntax error in your dial plan on line " + syntaxExcp.Line + ", please check.", executingScript.Owner));
                executingScript.ExecutionError = "Dial plan syntax error";
            }
            catch (MissingMethodException missingExcp)
            {
                logger.Warn("MissingMethodException. " + missingExcp.Message);
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "There was a missing method exception in your dial plan: " + missingExcp.Message + ".", executingScript.Owner));
                executingScript.ExecutionError = "Dial plan missing method";
            }
            catch (ThreadAbortException) { }
            catch (Exception excp)
            {
                logger.Error("Exception ExecuteScript (" + excp.GetType() + "). " + excp.Message);
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "There was an exception executing your dial plan script: " + excp.Message, executingScript.Owner));
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Exception on user " + executingScript.Owner + "'s dial plan script (" + excp.GetType() + "). " + excp.Message, null));
                executingScript.ExecutionError = "Dial plan exception";
            }
            finally
            {
                executingScript.StopExecution();
            }
        }

        private void MonitorScripts()
        {
            try
            {
                while (!StopScriptMonitoring)
                {
                    DialPlanExecutingScript[] killScripts = null;

                    bool lockTaken = false;
                    Monitor.TryEnter(m_runningScripts, 100, ref lockTaken);
                    if (lockTaken)
                    {
                        try
                        {
                            killScripts = (from script in m_runningScripts
                                           where
                                               script.Complete ||
                                               DateTime.Now > script.EndTime ||
                                               DateTime.Now.Subtract(script.StartTime).TotalSeconds > ABSOLUTEMAX_SCRIPTPROCESSING_SECONDS
                                           select script).ToArray();
                        }
                        finally
                        {
                            Monitor.Exit(m_runningScripts);
                        }
                    }
                    else
                    {
                        logger.Warn("Dialplan engine monitoring thread could not acquire a lock on the running scripts list.");
                    }

                    if (killScripts != null)
                    {
                        for (int index = 0; index < killScripts.Length; index++)
                        {
                            DialPlanExecutingScript killScript = killScripts[index];
                            DialPlanContext dialPlanContext = killScript.ExecutingDialPlanContext;

                            if (!killScript.Complete)
                            {
                                killScript.LogDelegate(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Long running dialplan script is being forcefully terminated.", killScript.Owner));
                            }

                            try
                            {
                                if (dialPlanContext != null && !dialPlanContext.IsAnswered)
                                {
                                    // The dialplan script has finished but the client call has not been answered. There could have be an
                                    // error executing the script or the dialplan could have completed without getting an answer.
                                    if (!killScript.ExecutionError.IsNullOrBlank())
                                    {
                                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dial plan execution completed without answering and had an execution error message of " + killScript.ExecutionError + ".", killScript.Owner));
                                        dialPlanContext.CallFailed(SIPResponseStatusCodesEnum.InternalServerError, killScript.ExecutionError, null);
                                    }
                                    else if (killScript.LastFailureStatus != SIPResponseStatusCodesEnum.None)
                                    {
                                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dial plan execution completed without answering and a last failure status of " + killScript.LastFailureStatus + " " + killScript.LastFailureReason + ".", killScript.Owner));
                                        dialPlanContext.CallFailed(killScript.LastFailureStatus, killScript.LastFailureReason, null);
                                    }
                                    else
                                    {
                                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dial plan execution completed without answering and with no last failure status.", killScript.Owner));
                                        dialPlanContext.CallFailed(SIPResponseStatusCodesEnum.TemporarilyUnavailable, null, null);
                                    }
                                }
                                else
                                {
                                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dial plan execution completed with normal clearing.", killScript.Owner));
                                }

                                long gcMemory = GC.GetTotalMemory(false);
                                long physicalMemory = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64;

                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dial plan finished for " + killScript.Owner + ", gc memory=" + gcMemory + ", physical memory=" + physicalMemory + ", running script count=" + ScriptCount + ".", null));
                            }
                            catch (Exception finallyExcp)
                            {
                                logger.Error("Exception MonitorScripts Kill. " + finallyExcp.Message);
                            }
                            finally
                            {
                                try
                                {
                                    /*if (killScript.DialPlanScriptThread != null && killScript.DialPlanScriptThread.IsAlive)
                                    {
                                        //logger.Debug("Aborting dialplan script thread.");
                                        killScript.DialPlanScriptThread.Abort();
                                    }*/

                                    killScript.StopExecution();
                                }
                                catch (ThreadStateException) { } // This exception is thrown when aborting a thread in a suspended state and is expected behaviour.
                                catch (Exception killExcp)
                                {
                                    logger.Error("Exception MonitorScripts aborting thread (" + killExcp.GetType().ToString() + "). " + killExcp.Message);
                                }

                                lock (m_runningScripts)
                                {
                                    m_runningScripts.Remove(killScript);
                                }
                                logger.Debug("Executing script " + killScript.DialPlanScriptThread.Name + " removed from running scripts list, running script count=" + ScriptCount + ".");
                            }
                        }
                    }

                    Thread.Sleep(500);
                }

                if (StopScriptMonitoring)
                {
                    if (m_runningScripts.Count > 0)
                    {
                        for (int index = 0; index < m_runningScripts.Count; index++)
                        {
                            if (m_runningScripts[index].DialPlanScriptThread != null && m_runningScripts[index].DialPlanScriptThread.IsAlive)
                            {
                                try
                                {
                                    m_runningScripts[index].DialPlanScriptThread.Abort();
                                }
                                catch (Exception finalKill)
                                {
                                    logger.Debug("Exception on script final kill. " + finalKill.Message);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MonitorScripts. " + excp);
            }
        }

        private void LoadRubyCommonScript()
        {
            if (m_rubyScriptCommonPath != null && File.Exists(m_rubyScriptCommonPath))
            {
                ReloadCommonRubyScript(null, null);
                string dir = Path.GetDirectoryName(m_rubyScriptCommonPath);
                string file = Path.GetFileName(m_rubyScriptCommonPath);
                logger.Debug("Starting file watch on Ruby Common Script " + dir + @"\" + file + ".");
                FileSystemWatcher rubyCommonWatcher = new FileSystemWatcher(dir, file);
                rubyCommonWatcher.Changed += new FileSystemEventHandler(ReloadCommonRubyScript);
                rubyCommonWatcher.EnableRaisingEvents = true;
            }
        }

        private void ReloadCommonRubyScript(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (m_rubyCommonLastReload == null || DateTime.Now.Subtract(m_rubyCommonLastReload.Value).TotalSeconds > RUBY_COMMON_RELOAD_INTERVAL)
                {
                    m_rubyCommonLastReload = DateTime.Now;

                    logger.Debug("Ruby common script file changed, reloading ruby common script.");

                    File.Copy(m_rubyScriptCommonPath, m_rubyScriptCommonPath + RUBY_COMMON_COPY_EXTEN, true);
                    StreamReader proxyRuntimeReader = new StreamReader(m_rubyScriptCommonPath + RUBY_COMMON_COPY_EXTEN);
                    string rubyScriptCommon = proxyRuntimeReader.ReadToEnd();
                    proxyRuntimeReader.Close();

                    m_rubyScriptCommon = rubyScriptCommon;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception rubyScriptWatcher_Changed. " + excp);
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            try
            {
                if (LogDelegate_External != null)
                {
                    LogDelegate_External(monitorEvent);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireProxyLogEvent DialPlanEngine. " + excp.Message);
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

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);

                Console.WriteLine("dst=" + dialPlanContext.m_commands[0].Destination + ", data=" + dialPlanContext.m_commands[0].Data + ".");
                Console.WriteLine("dst=" + dialPlanContext.m_commands[1].Destination + ", data=" + dialPlanContext.m_commands[1].Data + ".");

                Assert.IsTrue(dialPlanContext.m_commands.Count == 2, "The dial plan was not correctly parsed.");
                Assert.IsTrue(dialPlanContext.m_commands[0].Operation == DialPlanOpsEnum.Equals, "Command 1 oeration not correct.");
                Assert.IsTrue(dialPlanContext.m_commands[1].Operation == DialPlanOpsEnum.Equals, "Command 2 oeration not correct.");
                Assert.IsTrue(dialPlanContext.m_commands[0].Destination == "100", "Command 1 destination not correct.");
                Assert.IsTrue(dialPlanContext.m_commands[1].Destination == "101", "Command 2 destination not correct.");
                Assert.IsTrue(dialPlanContext.m_commands[0].Command == "Switch", "Command 1 command not correct.");
                Assert.IsTrue(dialPlanContext.m_commands[1].Command == "Switch", "Command 2 command not correct.");
                Assert.IsTrue(dialPlanContext.m_commands[0].Data == "\"anonymous.invalid\", \"password\", \"612@freeworlddialup.com\"", "Command 1 data not correct.");
                Assert.IsTrue(dialPlanContext.m_commands[1].Data == "\"username\", \"password\", \"303@sip.blueface.ie\"", "Command 2 data not correct.");

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

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);

                Console.WriteLine("dst=" + dialPlanContext.m_commands[0].Destination + ", data=" + dialPlanContext.m_commands[0].Data + ".");
                Console.WriteLine("dst=" + dialPlanContext.m_commands[1].Destination + ", data=" + dialPlanContext.m_commands[1].Data + ".");

                Assert.IsTrue(dialPlanContext.m_commands.Count == 2, "The dial plan was not correctly parsed.");
                Assert.IsTrue(dialPlanContext.m_commands[0].Command == "Switch", "The dial plan command was not correct.");
                Assert.IsTrue(dialPlanContext.m_commands[0].Data == "anonymous.invalid, password, 612@freeworlddialup.com", "The dial plan data was not correct.");

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

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);

                Assert.IsTrue(dialPlanContext.m_commands.Count == 3, "The dial plan was not correctly parsed.");
                Assert.IsTrue(dialPlanContext.m_commands[0].Operation == DialPlanOpsEnum.Equals, "Command 1 operation was incorrect.");
                Assert.IsTrue(dialPlanContext.m_commands[1].Operation == DialPlanOpsEnum.Regex, "Command 2 operation was incorrect.");
                Assert.IsTrue(dialPlanContext.m_commands[2].Operation == DialPlanOpsEnum.Equals, "Command 3 operation was incorrect.");

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

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3200@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsTrue(dialPlanContext.m_commands.Count == 3, "The dial plan was not correctly parsed.");
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

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3200@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsTrue(dialPlanContext.m_commands.Count == 3, "The dial plan was not correctly parsed.");
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

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3100@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsTrue(dialPlanContext.m_commands.Count == 3, "The dial plan was not correctly parsed.");
                Assert.IsTrue(commandMatch.Data == "anon, password, 3@sip.blueface.ie", "The dial plan command match was not correct.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleXMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3X.,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:300@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleZMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3Z.,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:310@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleNMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3N.,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:320@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleRangeMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3[2-57-9].,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void SingleRangeNoMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3[2-57-9].,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:360@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNull(commandMatch, "The dial plan should not have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void MutliRangeMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3[2-57-9]X[1-3],1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3802@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void MutliRangeNoMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan = "exten => _3[2-57-9]X[1-3].,1,Switch(anon, password, 1@sip.blueface.ie)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:3807@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

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

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);

                Assert.IsNotNull(dialPlan, "The default dial plan could not be loaded.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void RegexWithCommaLoadDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    "exten =~ /3d{4,6}/,1,Switch(anon,,${dst}@sip.blueface.ie, \"sip switch\" <sip:anon@sip.mysipswitch.com>, 194.213.29.100)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);

                Assert.IsNotNull(dialPlan, "The dial plan could not be loaded.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void RegexWithCommaMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    @"exten =~ /\d{3,6}/,1,Switch(anon,,${dst}@sip.blueface.ie, ""sip switch"" <sip:anon@sip.mysipswitch.com>, 194.213.29.100)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void RegexWithCommaNoMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    @"exten =~ /\D{3,6}/,1,Switch(anon,,${dst}@sip.blueface.ie, ""sip switch"" <sip:anon@sip.mysipswitch.com>, 194.213.29.100)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNull(commandMatch, "The dial plan should not have returned a match.");

                Console.WriteLine("---------------------------------");
            }

            [Test]
            public void RegexWithNoCommaMatchDialPlanTest()
            {
                Console.WriteLine("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);

                string testDialPlan =
                    @"exten =~ \d{3},1,Switch(anon,,${dst}@sip.blueface.ie, ""sip switch"" <sip:anon@sip.mysipswitch.com>, 194.213.29.100)";

                SIPDialPlan dialPlan = new SIPDialPlan(null, null, null, testDialPlan, SIPDialPlanScriptTypesEnum.Asterisk);
                SIPRequest request = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:380@sip.mysipswitch.com"));
                DialPlanLineContext dialPlanContext = new DialPlanLineContext(null, null, null, null, null, dialPlan, null, null, null);
                DialPlanCommand commandMatch = dialPlanContext.GetDialPlanMatch(request);

                Assert.IsNotNull(commandMatch, "The dial plan should have returned a match.");

                Console.WriteLine("---------------------------------");
            }
		}

#endif

        #endregion
    }
}
