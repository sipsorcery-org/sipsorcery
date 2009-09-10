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
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

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
        private event SIPMonitorLogDelegate m_statefulProxyLogEvent;           // Used to send log messages back to the application server core.
        private QueueNewCallDelegate QueueNewCall_External;

        private string m_username;                          // The call owner.
        private string m_adminMemberId;
        private SIPEndPoint m_outboundProxySocket;          // If this app forwards calls via an outbound proxy this value will be set.
        private SIPResponseStatusCodesEnum m_lastFailureStatus; // If the call fails the first leg that returns an error will be used as the reason on the error response.
        private string m_lastFailureReason;

        private bool m_callAnswered;                        // Set to true once the first Ok response has been received from a forwarded call leg.
        private bool m_commandCancelled;
        private ISIPClientUserAgent m_answeredUAC;

        internal event CallProgressDelegate CallProgress;
        internal event CallFailedDelegate CallFailed;
        internal event CallAnsweredDelegate CallAnswered;

        private Queue<List<SIPCallDescriptor>> m_priorityCallsQueue = new Queue<List<SIPCallDescriptor>>(); // A queue of mulitple call legs that will be attempted until the call is answered ot there are none left.
        private List<ISIPClientUserAgent> m_switchCalls = new List<ISIPClientUserAgent>();                    // Holds the multiple forwards that are currently being attempted.
        private List<SIPTransaction> m_switchCallTransactions;                                              // Used to maintain a list of all the transactions used in the call. Allows response codes and such to be checked.    
        private List<SIPCallDescriptor> m_delayedCalls = new List<SIPCallDescriptor>();
   
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
        /// <param name="statefulProxyLogEvent">Logging delegate to send log messages that the proxy will log.</param>
        /// <param name="clientTransaction">The transaction for the request that initiated this forwarded call.</param>
        /// <param name="manglePrivateAddresses"></param>
        /// <param name="username">The username of the call owner.</param>
        /// <param name="emailAddress">The email address of the call owner. Used to email the optional SIP trace to.</param>
        public ForkCall(
            SIPTransport sipTransport,
            SIPMonitorLogDelegate statefulProxyLogEvent,
            QueueNewCallDelegate queueNewCall,
            string username,
            string adminMemberId,
            List<SIPTransaction> switchCallTransactions,
            SIPEndPoint outboundProxy)
        {
            m_sipTransport = sipTransport;
            m_statefulProxyLogEvent = statefulProxyLogEvent;
            QueueNewCall_External = queueNewCall;
            m_username = username;
            m_adminMemberId = adminMemberId;
            m_switchCallTransactions = switchCallTransactions;
            m_outboundProxySocket = outboundProxy;
        }

        /// <summary>
        /// Starts a call based on multiple forking call legs. As each call leg fails the next leg is popped off the queue and attempted.
        /// </summary>
        /// <param name="callsQueue"></param>
        public void Start(Queue<List<SIPCallDescriptor>> callsQueue)
        {
            if (callsQueue == null || callsQueue.Count == 0)
            {
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No callable destinations were provided in Dial command, returning.", m_username));
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
                for (int index = 0; index < callDescriptors.Count; index++ )
                {
                    int availableThreads = 0;
                    int ioCompletionThreadsAvailable = 0;
                    ThreadPool.GetAvailableThreads(out availableThreads, out ioCompletionThreadsAvailable);
                    
                    if (availableThreads <= 0) {
                        logger.Warn("The ThreadPool had no threads available in the pool to start a ForkCall leg, task will be queued.");
                    }

                    SIPCallDescriptor callDescriptor = callDescriptors[index];
                    if (callDescriptor.ToSIPAccount != null) {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "ForkCall commencing call leg to " + callDescriptor.ToSIPAccount.SIPUsername + "@" + callDescriptor.ToSIPAccount.SIPDomain + ".", m_username));
                    }
                    else {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "ForkCall commencing call leg to " + callDescriptor.Uri + ".", m_username));
                    }
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
                Thread.CurrentThread.Name = THEAD_NAME + DateTime.Now.ToString("HHmmss") + "-" + Crypto.GetRandomString(3);
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
                // A call will not be delayed if there are no other calls being attempted.
                if (callDescriptor.DelaySeconds != 0 && m_switchCalls.Count > 0)
                {
                    callDescriptor.DelayMRE = new ManualResetEvent(false);
                    lock (m_delayedCalls)
                    {
                        m_delayedCalls.Add(callDescriptor);
                    }

                    int delaySeconds = (callDescriptor.DelaySeconds > MAX_DELAY_SECONDS) ? MAX_DELAY_SECONDS : callDescriptor.DelaySeconds;
                    if (callDescriptor.ToSIPAccount != null) {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Delaying call leg to " + callDescriptor.ToSIPAccount.SIPUsername + "@" + callDescriptor.ToSIPAccount.SIPDomain + " by " + delaySeconds + "s.", m_username));
                    }
                    else {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Delaying call leg to " + callDescriptor.Uri.ToString() + " by " + delaySeconds + "s.", m_username));
                    }
                    callDescriptor.DelayMRE.WaitOne(delaySeconds * 1000);
                }

                lock (m_delayedCalls) {
                    m_delayedCalls.Remove(callDescriptor);
                }

                if (!m_callAnswered && !m_commandCancelled)
                {
                    ISIPClientUserAgent uacCall = null;

                    if (callDescriptor.ToSIPAccount == null) {
                        uacCall = new SIPClientUserAgent(m_sipTransport, m_outboundProxySocket, m_username, m_adminMemberId, m_statefulProxyLogEvent);
                    }
                    else {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Creating B2B call for " + callDescriptor.ToSIPAccount.SIPUsername + "@" + callDescriptor.ToSIPAccount.SIPDomain + ".", m_username));
                        uacCall = new SIPB2BUserAgent(m_statefulProxyLogEvent, QueueNewCall_External, m_sipTransport, m_username, m_adminMemberId);
                    }

                    lock (m_switchCalls) {
                        m_switchCalls.Add(uacCall);
                    }

                    uacCall.CallAnswered += UACCallAnswered;
                    uacCall.CallFailed += UACCallFailed;
                    uacCall.CallRinging += UACCallProgress;

                    uacCall.Call(callDescriptor);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ForkCall StartNewCall. " + excp.Message);
            }
        }

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

            m_lastFailureStatus = SIPResponseStatusCodesEnum.TemporarilyNotAvailable;
            m_lastFailureReason = errorMessage;

            if (m_switchCallTransactions != null)
            {
                m_switchCallTransactions.Add(uac.ServerTransaction);
            }

            uac.CallAnswered -= UACCallAnswered;
            uac.CallFailed -= UACCallFailed;
            uac.CallRinging -= UACCallProgress;

            CallLegCompleted();
        }

        private void UACCallProgress(ISIPClientUserAgent uac, SIPResponse progressResponse) {
            try {
                if (m_commandCancelled)
                {
                    //logger.Debug("Call " + uac.CallDescriptor.Uri + " should not be in a progress state after a cancel. Cancel again.");
                    uac.Cancel();
                }
                else {
                    CallProgress(progressResponse.Status, progressResponse.ReasonPhrase, null, progressResponse.Header.ContentType, progressResponse.Body);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ForkCall UACCallProgress. " + excp);
            }
        }

        private void UACCallAnswered(ISIPClientUserAgent answeredUAC, SIPResponse answeredResponse)
        {
            try
            {
                // Remove the current call from the pending list.
                lock (m_switchCalls) {
                    m_switchCalls.Remove(answeredUAC);
                }

                if (m_switchCallTransactions != null) {
                    m_switchCallTransactions.Add(answeredUAC.ServerTransaction);
                }

                if (answeredResponse != null && answeredResponse.StatusCode >= 200 && answeredResponse.StatusCode <= 299)
                {
                    if (!m_callAnswered && !m_commandCancelled)
                    {
                        // This is the first call we've got an answer on.
                        m_callAnswered = true;
                        m_answeredUAC = answeredUAC;

                        if (CallAnswered != null) {
                            CallAnswered(answeredResponse.Status, answeredResponse.ReasonPhrase, null, null, answeredResponse.Header.ContentType, answeredResponse.Body, answeredUAC.SIPDialogue);
                        }

                        // Cancel/hangup and other calls on this leg that are still around.
                        CancelNotRequiredCallLegs(CallCancelCause.NormalClearing);
                    }
                    else
                    {
                        // Call already answered or cancelled already, hangup (send BYE).
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call leg " + answeredUAC.CallDescriptor.Uri + " answered but call was already answered or cancelled, hanging up.", m_username));
                        SIPDialogue sipDialogue = new SIPDialogue(answeredUAC.ServerTransaction, m_username, m_adminMemberId);
                        sipDialogue.Hangup(m_sipTransport, m_outboundProxySocket);
                    }
                }
                else if (answeredResponse != null && answeredResponse.StatusCode >= 300 && answeredResponse.StatusCode <= 399) {
                    SIPURI redirectURI = answeredResponse.Header.Contact[0].ContactURI;

                    if (redirectURI == null) {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect target could not be determined from redirect response, ignoring.", m_username));
                    }
                    else if (answeredUAC.CallDescriptor.RedirectMode == SIPCallRedirectModesEnum.Add) {
                        // A redirect response was received. Create a new call leg(s) using the SIP URIs in the contact header of the response.
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response to " + redirectURI.ToString() + " accepted.", m_username));
                        SIPCallDescriptor redirectCallDescriptor = new SIPCallDescriptor(null, null, redirectURI.ToString(), null, null, null, null, null, SIPCallDirection.Out, null, null, null);
                        StartNewCallAsync(redirectCallDescriptor);
                    }
                    else if (answeredUAC.CallDescriptor.RedirectMode == SIPCallRedirectModesEnum.Replace) {
                        // In the Replace redirect mode the existing dialplan execution needs to be cancelled and the single redirect call be used to replace it.
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response to " + redirectURI.ToString() + " rejected as Replace mode not yet implemented.", m_username));
                    }
                    else {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Redirect response to " + redirectURI.ToString() + " rejected as not enabled in dial string.", m_username));
                    }
                }
                else if(answeredResponse != null ) {
                    // This call leg failed, record the failure status and reason.
                    m_lastFailureStatus = answeredResponse.Status;
                    m_lastFailureReason = answeredResponse.ReasonPhrase;

                    if (m_switchCallTransactions != null) {
                        m_switchCallTransactions.Add(answeredUAC.ServerTransaction);
                    }
                }

                CallLegCompleted();
            }
            catch (Exception excp)
            {
                logger.Error("Exception ForkCall UACCallAnswered. " + excp);
            }
        }

        public void CancelNotRequiredCallLegs(CallCancelCause cancelCause) {
            try {
                m_commandCancelled = true;
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Cancelling all call legs for ForkCall app.", m_username));
                
                // Cancel all forwarded call legs.
                if (m_switchCalls.Count > 0) {
                    ISIPClientUserAgent[] inProgressCalls = (from ua in m_switchCalls where !ua.IsUACAnswered select ua).ToArray();
                    for (int index = 0; index < inProgressCalls.Length; index++) {
                        ISIPClientUserAgent uac = inProgressCalls[index];
                        uac.Cancel();
                    }
                }

                // Signal any delayed calls that they are no longer required.
                foreach (SIPCallDescriptor callDescriptor in m_delayedCalls) {
                    callDescriptor.DelayMRE.Set();
                }

                CallLegCompleted();
            }
            catch (Exception excp) {
                logger.Error("Exception ForkCall CancelAllCallLegs. " + excp);
            }
        }

        /// <summary>
        /// Fired after each call leg forward attempt is completed.
        /// </summary>
        private void CallLegCompleted() {
            try {
                if (!m_callAnswered && !m_commandCancelled) {
                    if (m_switchCalls.Count > 0 || m_delayedCalls.Count > 0) {
                        // There are still calls on this leg in progress.

                        // If there are no current calls then start the next delayed one.
                        if (m_switchCalls.Count == 0) {
                            SIPCallDescriptor nextCall = null;
                            lock (m_delayedCalls) {
                                foreach (SIPCallDescriptor call in m_delayedCalls) {
                                    if (nextCall == null || nextCall.DelaySeconds > call.DelaySeconds) {
                                        nextCall = call;
                                    }
                                }
                            }

                            if (nextCall != null) {
                                nextCall.DelayMRE.Set();
                            }
                        }
                    }
                    else if (m_priorityCallsQueue.Count != 0 && !m_callAnswered) {
                        List<SIPCallDescriptor> nextPrioritycalls = m_priorityCallsQueue.Dequeue();
                        Start(nextPrioritycalls);
                    }
                    else if (CallFailed != null) {
                        // No more call legs to attempt, or call has already been answered or cancelled.
                        if (m_lastFailureStatus != SIPResponseStatusCodesEnum.None) {
                            CallFailed(m_lastFailureStatus, m_lastFailureReason, null);
                        }
                        else {
                            CallFailed(SIPResponseStatusCodesEnum.TemporarilyNotAvailable, "All forwards failed.", null);
                        }
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception CallLegCompleted. " + excp);
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent) {
            try {
                if (m_statefulProxyLogEvent != null) {
                    m_statefulProxyLogEvent(monitorEvent);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception FireProxyLogEvent ForkCall. " + excp.Message);
            }
        }

        #region Unit testing.

		#if UNITTEST

		[TestFixture]
		public class ForkCallUnitTest
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
        }

        #endif

        #endregion
    }
}
