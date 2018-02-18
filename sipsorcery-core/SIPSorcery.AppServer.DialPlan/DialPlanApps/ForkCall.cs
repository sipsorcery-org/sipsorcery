//-----------------------------------------------------------------------------
// Filename: ForkCall.cs
//
// Description: A dial plan command that facilitates forked calls.
// 
// History:
// 07 Feb 2008	    Aaron Clauson	    Created.
// 17 Apr 2008      Aaron Clauson       Added tracing.
// 10 Aug 2008      Aaron Clauson       Moved the call resolution functionality to SIPCallResolver.
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
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.AppServer.DialPlan
{
    public class ForkCall
    {
        private const string THEAD_NAME = "forkcall-";

        public const int MAX_CALLS_PER_LEG = 10;
        public const int MAX_DELAY_SECONDS = 120;
        private const string ALLFORWARDS_FAILED_REASONPHRASE = "All forwards failed";

        private static ILog logger = AppState.logger;

        private SIPTransport m_sipTransport;
        private ISIPCallManager m_callManager;
        private SIPSorcery.Entities.CustomerAccountDataLayer m_customerAccountDataLayer = new SIPSorcery.Entities.CustomerAccountDataLayer();
        private event SIPMonitorLogDelegate m_statefulProxyLogEvent;    // Used to send log messages back to the application server core.
        private QueueNewCallDelegate QueueNewCall_External;             // Function delegate to allow new calls to be placed on the call manager and run through the dialplan logic.              
        private DialPlanContext m_dialPlanContext;                      // Used to allow redirect responses that need to execute a new dial plan execution.

        private DialStringParser m_dialStringParser;            // Used to create a new list of calls if a redirect response is received to a fork call leg.
        private string m_username;                              // The call owner.
        private string m_adminMemberId;
        private SIPEndPoint m_outboundProxySocket;              // If this app forwards calls via an outbound proxy this value will be set.
        private SIPResponseStatusCodesEnum m_lastFailureStatus; // If the call fails the first leg that returns an error will be used as the reason on the error response.
        private string m_lastFailureReason;

        private bool m_callAnswered;                            // Set to true once the first Ok response has been received from a forwarded call leg.
        private bool m_commandCancelled;
        private ISIPClientUserAgent m_answeredUAC;

        internal event CallProgressDelegate CallProgress;
        internal event CallFailedDelegate CallFailed;
        internal event CallAnsweredDelegate CallAnswered;

        private Queue<List<SIPCallDescriptor>> m_priorityCallsQueue = new Queue<List<SIPCallDescriptor>>(); // A queue of mulitple call legs that will be attempted until the call is answered ot there are none left.
        private List<ISIPClientUserAgent> m_switchCalls = new List<ISIPClientUserAgent>();                  // Holds the multiple forwards that are currently being attempted.
        private List<SIPTransaction> m_switchCallTransactions = new List<SIPTransaction>();                 // Used to maintain a list of all the transactions used in the call. Allows response codes and such to be checked.    
        private List<SIPCallDescriptor> m_delayedCalls = new List<SIPCallDescriptor>();

        public SIPResponse AnsweredSIPResponse { get; private set; }

        /// <remarks>
        /// The ForkCall allows a SIP call to be forked to multiple destinations. To do this it utilises multiple
        /// simultaneous SIPCallDescriptor objects and consolidates their responses to work out what should and shouldn't
        /// be forwarded onto the client that initiated the call. The ForkCall acts as a classic SIP forking proxy.
        /// 
        /// The ForkCall is capable of both multiple forwards and also of follow on forwarding in the event of a call 
        /// leg of multiple forwards not succeeding. As an example:
        /// 
        ///     Dial(provider1&provider2|provider3&provider4|provider5&provider6)
        ///     
        /// The handling of this call would be:
        /// 1. The call would be simultaneously forwarded to provider1 and provider2,
        /// 2. If the call was not successfully answered in step 1 the  call would be simultaneously forwarded to provider3 and provider4,
        /// 3. If the call was not successfully answered in step 2 the  call would be simultaneously forwarded to provider5 and provider6,
        /// 4. If the call was not successfully answered in step 3 the client call would be sent an error response.
        /// 5. If the client cancels the call at any time during the call all forwarding operations will halt.
        /// </remarks>
        /// <param name="sipTransport">The SIP transport layer that will handle the forked calls.</param>
        /// <param name="statefulProxyLogEvent">A delegate that allows the owning object to receive notifications from the ForkCall.</param>
        /// <param name="queueNewCall">A delegate that can be used to queue a new call with the SIP application server call manager. This
        /// delegate is used when a fork call generates a B2B call that requires the incoming dialplan for a called user to be processed.</param>
        /// <param name="dialStringParser">The dial string parser is used when a redirect response is received on a forked call leg. The
        /// parser can then be applied to the redirect SIP URI to generate new call legs to be added to the ForkCall.</param>
        /// <param name="username">The username of the call owner.</param>
        /// <param name="adminMemberId">The admin ID of the call owner.</param>
        /// <param name="outboundProxy">The outbound proxy to use for all SIP traffic originated. Can be null if an outbound proxy is not 
        /// being used.</param>
        public ForkCall(
            SIPTransport sipTransport,
            SIPMonitorLogDelegate statefulProxyLogEvent,
            QueueNewCallDelegate queueNewCall,
            DialStringParser dialStringParser,
            string username,
            string adminMemberId,
            SIPEndPoint outboundProxy,
            ISIPCallManager callManager,
            DialPlanContext dialPlanContext)
        {
            m_sipTransport = sipTransport;
            m_statefulProxyLogEvent = statefulProxyLogEvent;
            QueueNewCall_External = queueNewCall;
            m_dialStringParser = dialStringParser;
            m_username = username;
            m_adminMemberId = adminMemberId;
            m_outboundProxySocket = outboundProxy;
            m_callManager = callManager;
            m_dialPlanContext = dialPlanContext;
        }

        /// <summary>
        /// See overload.
        /// </summary>
        /// <param name="switchCallTransactions">An empty list that will be filled with transactions that the ForkCall creates and that each
        /// represent an outgoing call. The calling object can use the list to check response codes to determine the result of each leg in the
        /// ForkCall.</param>
        public ForkCall(
            SIPTransport sipTransport,
            SIPMonitorLogDelegate statefulProxyLogEvent,
            QueueNewCallDelegate queueNewCall,
            DialStringParser dialStringParser,
            string username,
            string adminMemberId,
            SIPEndPoint outboundProxy,
            ISIPCallManager callManager,
            DialPlanContext dialPlanContext,
            out List<SIPTransaction> switchCallTransactions) :
            this(sipTransport, statefulProxyLogEvent, queueNewCall, dialStringParser, username, adminMemberId, outboundProxy, callManager, dialPlanContext)
        {
            switchCallTransactions = m_switchCallTransactions;
        }

        /// <summary>
        /// Starts a call based on multiple forking call legs. As each call leg fails the next leg is popped off the queue and attempted.
        /// </summary>
        /// <param name="callsQueue"></param>
        public void Start(Queue<List<SIPCallDescriptor>> callsQueue)
        {
            if (callsQueue == null || callsQueue.Count == 0)
            {
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No callable destinations were provided in Dial command, returning.", m_username));
                m_lastFailureStatus = SIPResponseStatusCodesEnum.InternalServerError;
                m_lastFailureReason = "Call list was empty";
                CallLegCompleted();
            }
            else
            {
                m_priorityCallsQueue = callsQueue;
                List<SIPCallDescriptor> calls = m_priorityCallsQueue.Dequeue();
                Start(calls);
            }
        }

        /// <summary>
        /// Starts a call based on a single multi forward call leg.
        /// </summary>
        /// <param name="calls">The list of simultaneous forwards to attempt.</param>
        public void Start(List<SIPCallDescriptor> callDescriptors)
        {
            if (callDescriptors != null && callDescriptors.Count > 0)
            {
                for (int index = 0; index < callDescriptors.Count; index++)
                {
                    int availableThreads = 0;
                    int ioCompletionThreadsAvailable = 0;
                    ThreadPool.GetAvailableThreads(out availableThreads, out ioCompletionThreadsAvailable);

                    if (availableThreads <= 0)
                    {
                        logger.Warn("The ThreadPool had no threads available in the pool to start a ForkCall leg, task will be queued.");
                    }

                    SIPCallDescriptor callDescriptor = callDescriptors[index];
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "ForkCall commencing call leg to " + callDescriptor.Uri + ".", m_username));
                    ThreadPool.QueueUserWorkItem(delegate { StartNewCallAsync(callDescriptor); });
                }
            }
            else
            {
                CallLegCompleted();
            }
        }

        private void StartNewCallAsync(SIPCallDescriptor callDescriptor)
        {
            try
            {
                callDescriptor.DialPlanContextID = (m_dialPlanContext != null) ? m_dialPlanContext.DialPlanContextID : Guid.Empty;

                if (Thread.CurrentThread.Name == null)
                {
                    Thread.CurrentThread.Name = THEAD_NAME + DateTime.Now.ToString("HHmmss") + "-" + Crypto.GetRandomString(3);
                }

                StartNewCallSync(callDescriptor);
            }
            catch (Exception excp)
            {
                logger.Error("Exception StartNewCallAsync. " + excp.Message);
            }
        }

        private void StartNewCallSync(SIPCallDescriptor callDescriptor)
        {
            try
            {
                callDescriptor.DialPlanContextID = (m_dialPlanContext != null) ? m_dialPlanContext.DialPlanContextID : Guid.Empty;

                if (callDescriptor.DelaySeconds != 0)
                {
                    callDescriptor.DelayMRE = new ManualResetEvent(false);
                    lock (m_delayedCalls)
                    {
                        m_delayedCalls.Add(callDescriptor);
                    }

                    int delaySeconds = (callDescriptor.DelaySeconds > MAX_DELAY_SECONDS) ? MAX_DELAY_SECONDS : callDescriptor.DelaySeconds;
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Delaying call leg to " + callDescriptor.Uri + " by " + delaySeconds + "s.", m_username));
                    callDescriptor.DelayMRE.WaitOne(delaySeconds * 1000);
                }

                lock (m_delayedCalls)
                {
                    m_delayedCalls.Remove(callDescriptor);
                }

                if (!m_callAnswered && !m_commandCancelled)
                {
                    ISIPClientUserAgent uacCall = null;

                    if (callDescriptor.ToSIPAccount == null)
                    {
                        if (callDescriptor.IsGoogleVoiceCall)
                        {
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Creating Google Voice user agent for " + callDescriptor.Uri + ".", m_username));
                            uacCall = new GoogleVoiceUserAgent(m_sipTransport, m_callManager, m_statefulProxyLogEvent, m_username, m_adminMemberId, m_outboundProxySocket);
                        }
                        else
                        {
                            uacCall = new SIPClientUserAgent(m_sipTransport, m_outboundProxySocket, m_username, m_adminMemberId, m_statefulProxyLogEvent,
                                m_customerAccountDataLayer.GetRtccCustomer, m_customerAccountDataLayer.GetRtccRate, m_customerAccountDataLayer.GetBalance,
                                m_customerAccountDataLayer.ReserveInitialCredit, m_customerAccountDataLayer.UpdateRealTimeCallControlCDRID);
                        }
                    }
                    else
                    {
                        if (QueueNewCall_External == null)
                        {
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "B2B calls are not supported in this dialplan manifestation.", m_username));
                        }
                        else
                        {
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Creating B2B call for " + callDescriptor.Uri + ".", m_username));
                            uacCall = new SIPB2BUserAgent(m_statefulProxyLogEvent, QueueNewCall_External, m_sipTransport, m_username, m_adminMemberId);
                        }
                    }

                    //ISIPClientUserAgent uacCall = new JingleUserAgent(m_username, m_adminMemberId, m_statefulProxyLogEvent);

                    if (uacCall != null)
                    {
                        lock (m_switchCalls)
                        {
                            m_switchCalls.Add(uacCall);
                        }

                        uacCall.CallAnswered += UACCallAnswered;
                        uacCall.CallFailed += UACCallFailed;
                        uacCall.CallRinging += UACCallProgress;
                        //uacCall.CallTrying += UACCallTrying;

                        uacCall.Call(callDescriptor);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ForkCall StartNewCall. " + excp.Message);
            }
        }

        /*private void UACCallTrying(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            try
            {
                // Test for the custom multiple redirect response.
                if (sipResponse.Status == SIPResponseStatusCodesEnum.MultipleRedirect)
                {
                    ProcessRedirect(uac, sipResponse);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ForkCall UACCallTrying. " + excp.Message);
            }
        }*/

        /// <summary>
        /// This event occurs if it was not possible to initiate a call to the destination specified in the forwarded call. An example
        /// would be an unresolvable hostname in the destination URI.
        /// </summary>
        /// <param name="sipSwitchCall"></param>
        /// <param name="errorMessage"></param>
        private void UACCallFailed(ISIPClientUserAgent uac, string errorMessage)
        {
            lock (m_switchCalls)
            {
                m_switchCalls.Remove(uac);
            }

            m_lastFailureStatus = SIPResponseStatusCodesEnum.TemporarilyUnavailable;
            m_lastFailureReason = errorMessage;

            if (m_switchCallTransactions != null && uac.ServerTransaction != null)
            {
                m_switchCallTransactions.Add(uac.ServerTransaction);
            }

            uac.CallAnswered -= UACCallAnswered;
            uac.CallFailed -= UACCallFailed;
            uac.CallRinging -= UACCallProgress;

            CallLegCompleted();
        }

        private void UACCallProgress(ISIPClientUserAgent uac, SIPResponse progressResponse)
        {
            try
            {
                if (m_commandCancelled)
                {
                    //logger.Debug("Call " + uac.CallDescriptor.Uri + " should not be in a progress state after a cancel. Cancel again.");
                    uac.Cancel();
                }
                else
                {
                    CallProgress(progressResponse.Status, progressResponse.ReasonPhrase, null, progressResponse.Header.ContentType, progressResponse.Body, uac);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ForkCall UACCallProgress. " + excp);
            }
        }

        private void UACCallAnswered(ISIPClientUserAgent answeredUAC, SIPResponse answeredResponse)
        {
            try
            {
                // Remove the current call from the pending list.
                lock (m_switchCalls)
                {
                    m_switchCalls.Remove(answeredUAC);
                }

                if (m_switchCallTransactions != null && answeredUAC.ServerTransaction != null)
                {
                    m_switchCallTransactions.Add(answeredUAC.ServerTransaction);
                }

                if (answeredResponse != null && answeredResponse.StatusCode >= 200 && answeredResponse.StatusCode <= 299)
                {
                    #region 2xx final response.

                    if (!m_callAnswered && !m_commandCancelled)
                    {
                        // This is the first call we've got an answer on.
                        m_callAnswered = true;
                        m_answeredUAC = answeredUAC;
                        AnsweredSIPResponse = answeredResponse;

                        SIPDialogueTransferModesEnum uasTransferMode = SIPDialogueTransferModesEnum.Default;
                        if (m_answeredUAC.CallDescriptor.TransferMode == SIPDialogueTransferModesEnum.NotAllowed)
                        {
                            answeredUAC.SIPDialogue.TransferMode = SIPDialogueTransferModesEnum.NotAllowed;
                            uasTransferMode = SIPDialogueTransferModesEnum.NotAllowed;
                        }
                        else if (m_answeredUAC.CallDescriptor.TransferMode == SIPDialogueTransferModesEnum.BlindPlaceCall)
                        {
                            answeredUAC.SIPDialogue.TransferMode = SIPDialogueTransferModesEnum.BlindPlaceCall;
                            uasTransferMode = SIPDialogueTransferModesEnum.BlindPlaceCall;
                        }
                        else if (m_answeredUAC.CallDescriptor.TransferMode == SIPDialogueTransferModesEnum.PassThru)
                        {
                            answeredUAC.SIPDialogue.TransferMode = SIPDialogueTransferModesEnum.PassThru;
                            uasTransferMode = SIPDialogueTransferModesEnum.PassThru;
                        }
                        /*else if (m_answeredUAC.CallDescriptor.TransferMode == SIPCallTransferModesEnum.Caller)
                        {
                            answeredUAC.SIPDialogue.TransferMode = SIPDialogueTransferModesEnum.NotAllowed;
                            uasTransferMode = SIPDialogueTransferModesEnum.Allowed;
                        }
                        else if (m_answeredUAC.CallDescriptor.TransferMode == SIPCallTransferModesEnum.Callee)
                        {
                            answeredUAC.SIPDialogue.TransferMode = SIPDialogueTransferModesEnum.Allowed;
                            uasTransferMode = SIPDialogueTransferModesEnum.NotAllowed;
                        }
                        else if (m_answeredUAC.CallDescriptor.TransferMode == SIPCallTransferModesEnum.Both)
                        {
                            answeredUAC.SIPDialogue.TransferMode = SIPDialogueTransferModesEnum.Allowed;
                            uasTransferMode = SIPDialogueTransferModesEnum.Allowed;
                        }*/

                        if (CallAnswered != null)
                        {
                            logger.Debug("Transfer mode=" + m_answeredUAC.CallDescriptor.TransferMode + ".");
                            CallAnswered(answeredResponse.Status, answeredResponse.ReasonPhrase, null, null, answeredResponse.Header.ContentType, answeredResponse.Body, answeredUAC.SIPDialogue, uasTransferMode);

                            // Cancel/hangup and other calls on this leg that are still around.
                            CancelNotRequiredCallLegs(CallCancelCause.NormalClearing);

                            if (answeredUAC.CallDescriptor.ReinviteDelay >= 0)
                            {
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, $"Initiating re-INVITE request in {answeredUAC.CallDescriptor.ReinviteDelay}s due to dial string option.", m_username));

                                // Add a delay so that the other call legs get cancelled prior to the re-INIVTE request being sent. This was done on a user request to help with calls with multiple legs having audio issues.
                                Thread.Sleep(answeredUAC.CallDescriptor.ReinviteDelay * 1000);

                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, $"Re-sending SDP: {answeredUAC.SIPDialogue.SDP}", m_username));

                                SIPDialogue dummyDialogue = new SIPDialogue();
                                dummyDialogue.RemoteSDP = answeredUAC.SIPDialogue.SDP;
                                m_callManager.ReInvite(answeredUAC.SIPDialogue, dummyDialogue);
                            }
                        }
                    }
                    else
                    {
                        // Call already answered or cancelled, hangup (send BYE).
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call leg " + answeredUAC.CallDescriptor.Uri + " answered but call was already answered or cancelled, hanging up.", m_username));
                        SIPDialogue sipDialogue = new SIPDialogue(answeredUAC.ServerTransaction, m_username, m_adminMemberId);
                        sipDialogue.Hangup(m_sipTransport, m_outboundProxySocket);
                    }

                    #endregion

                    CallLegCompleted();
                }
                else if (answeredUAC.SIPDialogue != null)
                {
                    // Google Voice calls create the dialogue without using a SIP response.
                    if (!m_callAnswered && !m_commandCancelled)
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call leg for Google Voice call to " + answeredUAC.CallDescriptor.Uri + " answered.", m_username));

                        // This is the first call we've got an answer on.
                        m_callAnswered = true;
                        m_answeredUAC = answeredUAC;

                        CallAnswered?.Invoke(SIPResponseStatusCodesEnum.Ok, null, null, null, answeredUAC.SIPDialogue.ContentType, answeredUAC.SIPDialogue.RemoteSDP, answeredUAC.SIPDialogue, SIPDialogueTransferModesEnum.NotAllowed);

                        // Cancel/hangup and other calls on this leg that are still around.
                        CancelNotRequiredCallLegs(CallCancelCause.NormalClearing);
                    }
                    else
                    {
                        // Call already answered or cancelled, hangup (send BYE).
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call leg for Google Voice call to " + answeredUAC.CallDescriptor.Uri + " answered but call was already answered or cancelled, hanging up.", m_username));
                        answeredUAC.SIPDialogue.Hangup(m_sipTransport, m_outboundProxySocket);
                    }
                }
                else if (answeredResponse != null && answeredResponse.StatusCode >= 300 && answeredResponse.StatusCode <= 399)
                {
                    ProcessRedirect(answeredUAC, answeredResponse);
                }
                else if (answeredResponse != null)
                {
                    // This call leg failed, record the failure status and reason.
                    m_lastFailureStatus = answeredResponse.Status;
                    m_lastFailureReason = answeredResponse.ReasonPhrase;

                    if (m_switchCallTransactions != null && answeredUAC.ServerTransaction != null)
                    {
                        m_switchCallTransactions.Add(answeredUAC.ServerTransaction);
                    }

                    CallLegCompleted();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ForkCall UACCallAnswered. " + excp);
            }
        }

        private void ProcessRedirect(ISIPClientUserAgent answeredUAC, SIPResponse answeredResponse)
        {
            try
            {
                SIPURI redirectURI = answeredResponse.Header.Contact[0].ContactURI;

                if (redirectURI == null)
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect target could not be determined from redirect response, ignoring.", m_username));
                }
                else
                {
                    //string canincalDomain = m_dialPlanContext.GetCanonicalDomain_External(redirectURI.Host, false);

                    if (answeredUAC.CallDescriptor.RedirectMode == SIPCallRedirectModesEnum.NewDialPlan)
                    {
                        m_dialPlanContext.ExecuteDialPlanForRedirect(answeredResponse);
                    }
                    else if (answeredUAC.CallDescriptor.RedirectMode == SIPCallRedirectModesEnum.Manual)
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response with URI " + redirectURI.ToString() + " was not acted on as redirect mode set to manual in dial string.", m_username));
                        CallLegCompleted();
                    }
                    else
                    {
                        // The redirect was not explicitly allowed so will be processed as an anonymous call.
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response to " + redirectURI.ToString() + " accepted.", m_username));
                        var redirectCalls = m_dialStringParser.GetForwardsForRedirect(redirectURI, answeredUAC.CallDescriptor);
                        //SIPCallDescriptor redirectCallDescriptor = answeredUAC.CallDescriptor.CopyOf();
                        //redirectCallDescriptor.Uri = redirectURI.ToString();
                        //StartNewCallAsync(redirectCallDescriptor);
                        if (redirectCalls != null && redirectCalls.Count > 0)
                        {
                            foreach (var redirectCall in redirectCalls)
                            {
                                StartNewCallAsync(redirectCall);
                            }
                        }
                    }
                    //else if (answeredUAC.CallDescriptor.RedirectMode == SIPCallRedirectModesEnum.Replace)
                    //{
                    //    // In the Replace redirect mode the existing dialplan execution needs to be cancelled and the single redirect call be used to replace it.
                    //    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response rejected as Replace mode not yet implemented.", m_username));
                    //    CallLegCompleted();
                    //}
                    //else
                    //{
                    //    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response with URI " + redirectURI.ToString() + " was not acted on as not enabled in dial string.", m_username));
                    //    CallLegCompleted();
                    //}

                    // A redirect response was received. Create a new call leg(s) using the SIP URIs in the contact header of the response.
                    //if (m_dialStringParser != null)
                    //{
                    //    // If there is a dial string parser available it will be used to generate a list of call destination from the redirect URI.
                    //    SIPCallDescriptor redirectCallDescriptor = answeredUAC.CallDescriptor.CopyOf();
                    //    Queue<List<SIPCallDescriptor>> redirectQueue = m_dialStringParser.ParseDialString(DialPlanContextsEnum.Script, null, redirectURI.ToString(), redirectCallDescriptor.CustomHeaders,
                    //        redirectCallDescriptor.ContentType, redirectCallDescriptor.Content, null, redirectCallDescriptor.FromDisplayName, redirectCallDescriptor.FromURIUsername, redirectCallDescriptor.FromURIHost, null, CustomerServiceLevels.None);

                    //    if (redirectQueue != null && redirectQueue.Count > 0)
                    //    {
                    //        // Only the first list in the queue is used (and there should only be a single list since it's generated from a redirect SIP URI and not 
                    //        // a full dial string).
                    //        List<SIPCallDescriptor> callDescriptors = redirectQueue.Dequeue();
                    //        for (int index = 0; index < callDescriptors.Count; index++)
                    //        {
                    //            callDescriptors[index].MangleIPAddress = redirectCallDescriptor.MangleIPAddress;
                    //            callDescriptors[index].MangleResponseSDP = redirectCallDescriptor.MangleResponseSDP;
                    //            callDescriptors[index].TransferMode = redirectCallDescriptor.TransferMode;
                    //        }
                    //        Start(callDescriptors);
                    //    }
                    //    else
                    //    {
                    //        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A redirect response to " + redirectURI.ToString() + " did not generate any new call leg destinations.", m_username));
                    //    }
                    //}
                    //else
                    //{
                    //    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response to " + redirectURI.ToString() + " accepted.", m_username));
                    //    SIPCallDescriptor redirectCallDescriptor = answeredUAC.CallDescriptor.CopyOf();
                    //    redirectCallDescriptor.Uri = redirectURI.ToString();
                    //    StartNewCallAsync(redirectCallDescriptor);
                    //}
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ForkCall ProcessRedirect. " + excp.Message);
            }
        }

        public void CancelNotRequiredCallLegs(CallCancelCause cancelCause)
        {
            try
            {
                m_commandCancelled = true;
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Cancelling all call legs for ForkCall app.", m_username));

                // Cancel all forwarded call legs.
                if (m_switchCalls.Count > 0)
                {
                    ISIPClientUserAgent[] inProgressCalls = (from ua in m_switchCalls where !ua.IsUACAnswered select ua).ToArray();
                    for (int index = 0; index < inProgressCalls.Length; index++)
                    {
                        ISIPClientUserAgent uac = inProgressCalls[index];
                        uac.Cancel();
                    }
                }

                // Signal any delayed calls that they are no longer required.
                foreach (SIPCallDescriptor callDescriptor in m_delayedCalls)
                {
                    callDescriptor.DelayMRE.Set();
                }

                CallLegCompleted();
            }
            catch (Exception excp)
            {
                logger.Error("Exception ForkCall CancelAllCallLegs. " + excp);
            }
        }

        /// <summary>
        /// Fired after each call leg forward attempt is completed.
        /// </summary>
        private void CallLegCompleted()
        {
            try
            {
                if (!m_callAnswered && !m_commandCancelled)
                {
                    if (m_switchCalls.Count > 0 || m_delayedCalls.Count > 0)
                    {
                        // There are still calls on this leg in progress.

                        // If there are no current calls then start the next delayed one.
                        if (m_switchCalls.Count == 0)
                        {
                            SIPCallDescriptor nextCall = null;
                            lock (m_delayedCalls)
                            {
                                foreach (SIPCallDescriptor call in m_delayedCalls)
                                {
                                    if (nextCall == null || nextCall.DelaySeconds > call.DelaySeconds)
                                    {
                                        nextCall = call;
                                    }
                                }
                            }

                            if (nextCall != null)
                            {
                                nextCall.DelayMRE.Set();
                            }
                        }
                    }
                    else if (m_priorityCallsQueue.Count != 0 && !m_callAnswered)
                    {
                        List<SIPCallDescriptor> nextPrioritycalls = m_priorityCallsQueue.Dequeue();
                        Start(nextPrioritycalls);
                    }
                    else if (CallFailed != null)
                    {
                        // No more call legs to attempt, or call has already been answered or cancelled.
                        if (m_lastFailureStatus != SIPResponseStatusCodesEnum.None)
                        {
                            CallFailed(m_lastFailureStatus, m_lastFailureReason, null);
                        }
                        else
                        {
                            CallFailed(SIPResponseStatusCodesEnum.TemporarilyUnavailable, "All forwards failed.", null);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallLegCompleted. " + excp);
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            try
            {
                if (m_statefulProxyLogEvent != null)
                {
                    m_statefulProxyLogEvent(monitorEvent);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireProxyLogEvent ForkCall. " + excp.Message);
            }
        }
    }
}
