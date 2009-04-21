//-----------------------------------------------------------------------------
// Filename: SwitchCallMulti.cs
//
// Description: A dial plan command that allows multiple instances of the SwitchCall.
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
using System.IO;
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

namespace SIPSorcery.Servers
{
    public delegate void CallCompletedDelegate(UASInviteTransaction clientTransaction, CallResult completedResult, string errorMessage);
    
    public enum CallResult
    {
        Unknown = 0,
        Answered = 1,           // A forward answered.
        NoAnswer = 2,           // If ringing was reported by at least one forward.
        Failed = 3,             // All forwards returned error codes.
        ClientCancelled = 4,    // Call cancelled by client user agent.
        ProxyCancelled = 5,     // Call cancelled by a proxy timeout or dial plan rule.
        TimedOut = 6,           // No response from any forward within the time limit.
        Error = 7,
    }
    
    public class SwitchCallMulti
    {
        private const string ALLFORWARDS_FAILED_REASONPHRASE = "All forwards failed";
                
        private static ILog logger = AppState.logger;

        private SIPTransport m_sipTransport;
        private event SIPMonitorLogDelegate m_statefulProxyLogEvent;             // Used to send log messages back to the proxy core.
        private event DialogueBridgeCreatedDelegate m_createBridgeDelegate;  // Used to inform the proxy core a dialogue bridge has been established.
 
        private UASInviteTransaction m_clientTransaction;   // The transaction for the client call that initiated the dial plan action.
        private string m_username;                          // The call owner.
        private string m_adminMemberId;
        private SIPEndPoint m_outboundProxySocket;          // If this app forwards calls via na outbound proxy this value will be set.
        private string m_lastFailureMessage;                // If the call fails the first leg that returns an error will be used as the reason on the error response.

        private bool m_isRinging = false;           // Set to true once the first ringing response has been sent to the client.
        private bool m_callAnswered = false;        // Set to true once the first Ok response has been received from a forwarded call leg.
        private bool m_callCancelled = false;       // Set to true if a CANCEL request is receveived from the client.
        private bool m_callTimedOut = false;        // Set to true of the client call reaches it's maximum lifetime without being sent an answer.
        private bool m_allForwardsFailed = false;   // Set to true if none of the attempted forwards answered.
        private bool m_proxyCancelled = false;      // Set to true if the proxy cancelled the call due to a dial plan rule.
        
        public event CallCompletedDelegate CallComplete;

        private Queue<List<SIPCallDescriptor>> m_priorityCallsQueue = new Queue<List<SIPCallDescriptor>>(); // A queue of mulitple call legs that will be attempted until the call is answered ot there are none left.
        private List<SIPClientUserAgent> m_switchCalls = new List<SIPClientUserAgent>();                              // Holds the multiple forwards that are currently being attempted.
        private List<SIPTransaction> m_switchCallTransactions;                                              // Used to maintain a list of all the transactions used in the call. Allows response codes and such to be checked.    
            
        public static Int64 Created;
        public static Int64 Destroyed;

        public SwitchCallMulti()
        {}

        /// <summary>
        /// The SwitchCallMulti allows a SIP call to be forked to multiple destinations. To do this it utilises multiple
        /// simultaneous SIPCallDescriptor objects and consolidates their responses to work out what should and shouldn't
        /// be forwarded onto the client that initiated the call. The SwitchCallMutli acts as a classic SIP forking proxy.
        /// 
        /// The SwitchCallMulti is capable of both multiple forwards and also of follow on forwarding in the event of a call 
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
        /// </summary>
        /// <param name="statefulProxyLogEvent">Logging delegate to send log messages that the proxy will log.</param>
        /// <param name="clientTransaction">The transaction for the request that initiated this forwarded call.</param>
        /// <param name="manglePrivateAddresses"></param>
        /// <param name="username">The username of the call owner.</param>
        /// <param name="emailAddress">The email address of the call owner. Used to email the optional SIP trace to.</param>
        public SwitchCallMulti(
            SIPTransport sipTransport,
            SIPMonitorLogDelegate statefulProxyLogEvent,
            DialogueBridgeCreatedDelegate createBridgeDelegate,
            UASInviteTransaction clientTransaction,
            string username,
            string adminMemberId,
            List<SIPTransaction> switchCallTransactions,
            SIPEndPoint outboundProxy)
        {
            Created++;
            m_sipTransport = sipTransport;
            m_statefulProxyLogEvent = statefulProxyLogEvent;
            m_createBridgeDelegate = createBridgeDelegate;
            m_clientTransaction = clientTransaction;
            m_username = username;
            m_adminMemberId = adminMemberId;
            m_switchCallTransactions = switchCallTransactions;
            m_outboundProxySocket = outboundProxy;

            m_clientTransaction.CDR.Owner = username;
            m_clientTransaction.UASInviteTransactionTimedOut += new SIPTransactionTimedOutDelegate(clientTransaction_TransactionTimedOut);
            m_clientTransaction.UASInviteTransactionCancelled += new SIPTransactionCancelledDelegate(ClientCallCancelled);
        }

        /// <summary>
        /// Starts a call based on multiple forking call legs. As each call leg fails the next leg is popped off the queue and attempted.
        /// </summary>
        /// <param name="callsQueue"></param>
        public void Start(Queue<List<SIPCallDescriptor>> callsQueue)
        {
            if (callsQueue == null || callsQueue.Count == 0)
            {
                logger.Debug("No where to forward to, hanging up client call.");
                SIPRequest clientReq = m_clientTransaction.TransactionRequest;
                SIPResponse badExtenResp = SIPTransport.GetResponse(m_clientTransaction.TransactionRequest, SIPResponseStatusCodesEnum.BadExtension, "No forward legs");
                m_clientTransaction.SendFinalResponse(badExtenResp);
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
                    StartNewCall(callDescriptors[index]);
                }
            }
            else
            {
                CallLegCompleted();
            }
        }

        private void StartNewCall(SIPCallDescriptor callDescriptor)
        {
            try
            {
                SIPClientUserAgent uacCall = new SIPClientUserAgent(m_sipTransport, m_outboundProxySocket, m_username, m_adminMemberId, m_statefulProxyLogEvent);
                uacCall.CallAnswered += UACCallAnswered;
                uacCall.CallFailed += UACCallFailed;
                uacCall.CallRinging += UACCallRinging;
                lock (m_switchCalls)
                {
                    m_switchCalls.Add(uacCall);
                }

                callDescriptor.Content = m_clientTransaction.TransactionRequest.Body;
                callDescriptor.ContentType = m_clientTransaction.TransactionRequest.Header.ContentType;

                ThreadPool.QueueUserWorkItem(uacCall.CallAsync, callDescriptor);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SwitchCallMulti StartNewCall. " + excp.Message);
            }
        }

        /// <summary>
        /// The client transaction will time out after ringing for the maximum allowed time for an INVITE transaction (probably 10 minutes) or less
        /// if the invite transaction timeout value has been adjusted.
        /// </summary>
        /// <param name="sipTransaction"></param>
        private void clientTransaction_TransactionTimedOut(SIPTransaction sipTransaction)
        {
            try
            {
                logger.Debug("The SwitchCallMulti timed out after " + DateTime.Now.Subtract(sipTransaction.Created).TotalSeconds.ToString("0.##") + "s of ringing, cancelling all remaining calls and hanging up.");

                m_callTimedOut = true;

                // Cancel/hangup remaining calls.
                lock (m_switchCalls)
                {
                    foreach (SIPClientUserAgent uac in m_switchCalls)
                    {
                        uac.Cancel();
                    }
                }

                CallLegCompleted();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SwitchCallMulti clientTransaction_TransactionTimedOut. " + excp);
            }
        }

        /// <summary>
        /// This event occurs if it was no possible to initiate a call to the destination specified in the forwarded call. An example
        /// would be an unresolvable hostname in the destination URI.
        /// </summary>
        /// <param name="sipSwitchCall"></param>
        /// <param name="errorMessage"></param>
        private void UACCallFailed(SIPClientUserAgent uac, string errorMessage)
        {
            if (CallComplete == null)
            {
                logger.Debug("Call leg failure on an already completed call, call=" + uac.CallDescriptor.Uri + ", error=" + errorMessage + ".");
            }
            else
            {
                logger.Debug("Switch call to " + uac.CallDescriptor.Uri + " failed. " + errorMessage);

                m_lastFailureMessage = errorMessage;

                if (m_switchCallTransactions != null)
                {
                    m_switchCallTransactions.Add(uac.ServerTransaction);
                }

                uac.CallAnswered -= UACCallAnswered;
                uac.CallFailed -= UACCallFailed;
                uac.CallRinging -= UACCallRinging;

                if (m_switchCalls.Count == 1 && !m_callAnswered && !m_callCancelled)
                {
                    // Single or last leg of call complete, fire the call leg completed event.
                    lock (m_switchCalls)
                    {
                        m_switchCalls.Remove(uac);
                    }

                    CallLegCompleted();
                }
                else
                {
                    // Forwarded call has failed. No action necessary, remove from pending list.
                    lock (m_switchCalls)
                    {
                        m_switchCalls.Remove(uac);
                    }
                }
            }
        }

        private void UACCallRinging(SIPClientUserAgent uac, SIPResponse ringingResponse)
        {
            try
            {
                 if (!m_isRinging && !m_callCancelled && (ringingResponse.Status == SIPResponseStatusCodesEnum.Ringing || ringingResponse.Status == SIPResponseStatusCodesEnum.SessionProgress))
                {
                    logger.Debug("Forwarding ringing response on " + uac.CallDescriptor.Uri + " to client.");
                    m_isRinging = true;

                    SIPResponse clientRingingResponse = SIPTransport.GetResponse(m_clientTransaction.TransactionRequest, ringingResponse.Status, ringingResponse.ReasonPhrase);

                    if (ringingResponse.Body != null)
                    {
                        clientRingingResponse.Body = ringingResponse.Body;
                        clientRingingResponse.Header.ContentType = ringingResponse.Header.ContentType;
                        clientRingingResponse.Header.ContentLength = ringingResponse.Body.Length;
                    }

                    m_clientTransaction.SendInformationalResponse(clientRingingResponse);
                }
                else if (m_callCancelled)
                {
                    logger.Debug("Call " + uac.CallDescriptor.Uri + " should not be in a progress state after a cancel. Cancel again.");
                    uac.Cancel();
                }
                else
                {
                    // Ignore any subsequent ringing responses otherwise client will get multiple ring tones.
                    logger.Debug("Ignoring " + ringingResponse.Status + " info response from " + uac.CallDescriptor.Uri + " as a ringing response has already been forwarded.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SwitchCallMulti UACCallRinging. " + excp);
            }
        }

        private void UACCallAnswered(SIPClientUserAgent answeredUAC, SIPResponse answeredResponse)
        {
            try
            {
                if (m_switchCallTransactions != null)
                {
                    m_switchCallTransactions.Add(answeredUAC.ServerTransaction);
                }

                // If there are other forwarded call legs in progress they need to be cancelled or if they have been answered hungup.
                if (answeredResponse != null && answeredResponse.StatusCode >= 200 && answeredResponse.StatusCode <= 299)
                {
                    // Cancel/hangup remaining calls.
                    lock (m_switchCalls)
                    {
                        foreach (SIPClientUserAgent uac in m_switchCalls)
                        {
                            if (uac != answeredUAC)
                            {
                                uac.Cancel();
                            }
                        }
                    }

                    answeredUAC.CallAnswered -= UACCallAnswered;
                    answeredUAC.CallFailed -= UACCallFailed;
                    answeredUAC.CallRinging -= UACCallRinging;

                    if (!m_callAnswered && !m_callCancelled)
                    {
                        logger.Debug("Call answered on leg " + answeredUAC.CallDescriptor.Uri + ".");

                        m_callAnswered = true;

                        // Send the OK through to the client.
                        //if (m_manglePrivateAddresses)
                        //{
                        //    SIPPacketMangler.MangleSIPResponse(SIPMonitorServerTypesEnum.StatefulProxy, answeredResponse, answeredUAC.ServerTransaction.RemoteEndPoint, m_username, m_statefulProxyLogEvent);
                        //}

                        SIPResponse okResponse = m_clientTransaction.GetOkResponse(m_clientTransaction.TransactionRequest, m_clientTransaction.LocalSIPEndPoint, answeredResponse.Header.ContentType, answeredResponse.Body);
                        m_clientTransaction.SendFinalResponse(okResponse);

                        // NOTE the Record-Route header does not get reversed for this Route set!! Since the Route set is being used from the server end NOT
                        // the client end a reversal will generate a Route set in the wrong order.
                        SIPDialogue clientLegDialogue = new SIPDialogue(
                            m_sipTransport,
                            m_clientTransaction,
                            m_username,
                            m_adminMemberId);

                        // Record the now established call with the call manager for in dialogue management and hangups.
                        m_createBridgeDelegate(clientLegDialogue, answeredUAC.SIPDialogue, m_username);

                        // Finished with call list now. Clear it to make sure the SwitchCallMulti object can be reclaimed.
                        lock (m_switchCalls)
                        {
                            m_switchCalls.Remove(answeredUAC);
                        }
                         
                        CallCompleted();
                    }
                    else if (m_callAnswered)
                    {
                        logger.Debug("Call answered on leg " + answeredUAC.CallDescriptor.Uri + " but call already answered, sending BYE.");
                        SIPDialogue sipDialogue = new SIPDialogue(m_sipTransport, answeredUAC.ServerTransaction, m_username, m_adminMemberId);
                        sipDialogue.Hangup();

                        lock (m_switchCalls)
                        {
                            m_switchCalls.Remove(answeredUAC);
                        }
                    }
                    else
                    {
                        // Call has been cancelled.
                        logger.Debug("Call answered on leg " + answeredUAC.CallDescriptor.Uri + " but call already cancelled, sending BYE.");
                        SIPDialogue sipDialogue = new SIPDialogue(m_sipTransport, answeredUAC.ServerTransaction, m_username, m_adminMemberId);
                        sipDialogue.Hangup();

                        lock (m_switchCalls)
                        {
                            m_switchCalls.Remove(answeredUAC);
                        }
                    }
                }
                else if (answeredResponse.StatusCode >= 300 && answeredResponse.StatusCode <= 399)
                {
                    // A redirect response was received. Create a new call leg(s) using the SIP URIs in the contact header of the response.
                    SIPURI redirectURI = answeredResponse.Header.Contact[0].ContactURI;
                    logger.Debug("A redirect response was received on call leg " + answeredUAC.CallDescriptor.Uri + " to " + redirectURI + ".");
                    SIPCallDescriptor redirectCallDescriptor = new SIPCallDescriptor(null, null, redirectURI.ToString(), null, null, null, null, null, SIPCallDirection.Out, null, null);
                    StartNewCall(redirectCallDescriptor);

                    lock (m_switchCalls)
                    {
                        m_switchCalls.Remove(answeredUAC);
                    }
                }
                else
                {
                    logger.Debug("Call to " + answeredUAC.CallDescriptor.Uri + " answered with " + answeredResponse.StatusCode + ", removing from pending list.");

                    if (m_switchCalls.Count == 1 && !m_callAnswered && !m_callCancelled)
                    {
                        lock (m_switchCalls)
                        {
                            m_switchCalls.Remove(answeredUAC);
                        }
                        CallLegCompleted();
                    }
                    else
                    {
                        // Forwarded call has failed. No action necessary, remove from pending list.
                        lock (m_switchCalls)
                        {
                            m_switchCalls.Remove(answeredUAC);
                        }
                    } 
                }  
            }
            catch (Exception excp)
            {
                logger.Error("Exception SwitchCallMulti UACCallAnswered. " + excp);
            }
        }

        private void ClientCallCancelled(SIPTransaction clientTransaction)
        {
            try
            {
                m_callCancelled = true;

                //logger.Debug("Received CANCEL request from client on call " + clientTransaction.TransactionRequest.URI.ToString() + ".");

                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "SwitchCall cancelled by client, cancelling " + m_switchCalls.Count + " server calls.", m_username));

                // Client hungup cancel all forwarded call legs.
                lock (m_switchCalls)
                {
                    for (int index = 0; index < m_switchCalls.Count; index++ )
                    {
                        m_switchCalls[index].Cancel();
                    }
                }

                CallCompleted();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SwitchCallMulti ClientCallCancelled. " + excp);
            }
        }

        public void CancelAllCallLegs()
        {
            try
            {
                m_proxyCancelled = true;

                logger.Debug("The call was cancelled by a dial plan condition.");

                // Cancel all forwarded call legs.
                lock (m_switchCalls)
                {
                    for (int index = 0; index < m_switchCalls.Count; index++)
                    {
                        m_switchCalls[index].Cancel();
                    }
                }

                // Send an error response to client.
                //SIPResponse errorResponse = SIPTransport.GetResponse(m_clientTransaction.TransactionRequest.Header, SIPResponseStatusCodesEnum.ServerTimeout, "Dial plan requested cancel", m_clientTransaction.SendFromEndPoint, m_clientTransaction.RemoteEndPoint);
                //m_clientTransaction.SendFinalResponse(errorResponse);

                CallLegCompleted();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SwitchCallMulti CancelAllCallLegs. " + excp);
            }
        }

        /// <summary>
        /// Fired after each call leg forward attempt is completed.
        /// </summary>
        private void CallLegCompleted()
        {
            try
            {
                if (m_priorityCallsQueue.Count != 0 && !m_callAnswered && !m_callCancelled && !m_callTimedOut)
                {
                    m_isRinging = false;
                    List<SIPCallDescriptor> nextPrioritycalls = m_priorityCallsQueue.Dequeue();
                    Start(nextPrioritycalls);
                }
                else
                {
                    // No more call legs to attempt, or call has already been answered or cancelled.
                    CallCompleted();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallLegCompleted. " + excp);
            }
        }

        private void CallCompleted()
        {
            try
            {
                if (m_clientTransaction != null)
                {
                    m_clientTransaction.UASInviteTransactionTimedOut -= new SIPTransactionTimedOutDelegate(clientTransaction_TransactionTimedOut);
                    m_clientTransaction.UASInviteTransactionCancelled -= new SIPTransactionCancelledDelegate(ClientCallCancelled);

                    if (m_clientTransaction.TransactionFinalResponse == null)
                    {
                        m_allForwardsFailed = true;
                    }
                }

                if (CallComplete != null)
                {
                    if (m_callCancelled)
                    {
                        CallComplete(m_clientTransaction, CallResult.ClientCancelled, "Client cancelled");
                    }
                    else if (m_proxyCancelled)
                    {
                        CallComplete(m_clientTransaction, CallResult.ProxyCancelled, "Proxy cancelled");
                    }
                    else if (m_callAnswered)
                    {
                        CallComplete(m_clientTransaction, CallResult.Answered, null);
                    }
                    else if (m_isRinging)
                    {
                        CallComplete(m_clientTransaction, CallResult.NoAnswer, "No answer");
                    }
                    else if (m_allForwardsFailed)
                    {
                        if (m_lastFailureMessage != null && m_lastFailureMessage.Trim().Length > 0)
                        {
                            CallComplete(m_clientTransaction, CallResult.Failed, "All forwards failed (" + m_lastFailureMessage + ")");
                        }
                        else
                        {
                            CallComplete(m_clientTransaction, CallResult.Failed, "All forwards failed.");
                        }
                    }
                    else if (m_callTimedOut)
                    {
                        CallComplete(m_clientTransaction, CallResult.TimedOut, "Timed out");
                    }
                    else
                    {
                        CallComplete(m_clientTransaction, CallResult.Unknown, m_lastFailureMessage);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallCompleted. " + excp);
            }
            finally
            {
                CallComplete = null;
                m_statefulProxyLogEvent = null;
                m_createBridgeDelegate = null;
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
                logger.Error("Exception FireProxyLogEvent SwitchCallMulti. " + excp.Message);
            }
        }

        ~SwitchCallMulti()
        {
            Destroyed++;
        }

        #region Unit testing.

		#if UNITTEST

		[TestFixture]
		public class SwitchCallMultiUnitTest
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
