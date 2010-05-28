// ============================================================================
// FileName: SIPAppServerCore.cs
//
// Description:
// Stateful proxy core for MySIPSwitch service.
//
// Author(s):
// Aaron Clauson
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Data;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public class SIPAppServerCore
    {
        private static ILog logger = AppState.GetLogger("appsvr");

        private string m_dispatcherUsername = SIPCallManager.DISPATCHER_SIPACCOUNT_NAME;
        //private static string m_referReplacesParameter = SIPHeaderAncillary.SIP_REFER_REPLACES;

        private SIPMonitorLogDelegate SIPMonitorLogEvent_External;              // Function to log messages from this core.
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;
        private SIPAuthenticateRequestDelegate SIPRequestAuthenticator_External;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private SIPCallManager m_callManager;
        private SIPDialogueManager m_sipDialogueManager;
        //private SIPNotifyManager m_notifyManager;

        public SIPAppServerCore(
            SIPTransport sipTransport,
            GetCanonicalDomainDelegate getCanonicalDomain,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPMonitorLogDelegate proxyLog,
            SIPCallManager callManager,
            SIPDialogueManager sipDialogueManager,
            //SIPNotifyManager notifyManager,
            SIPAuthenticateRequestDelegate sipAuthenticateRequest,
            SIPEndPoint outboundProxy)
        {
            try
            {
                m_sipTransport = sipTransport;
                m_callManager = callManager;
                m_sipDialogueManager = sipDialogueManager;
                //m_notifyManager = notifyManager;

                m_sipTransport.SIPTransportRequestReceived += GotRequest;
                m_sipTransport.SIPTransportResponseReceived += GotResponse;

                m_outboundProxy = outboundProxy;

                GetCanonicalDomain_External = getCanonicalDomain;
                GetSIPAccount_External = getSIPAccount;
                SIPMonitorLogEvent_External = proxyLog;
                SIPRequestAuthenticator_External = sipAuthenticateRequest;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPAppServerCore (ctor). " + excp.Message);
                throw excp;
            }
        }

        public void GotRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                // Used in the proxy monitor messages only, plays no part in request routing.
                string fromUser = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromURI.User : null;
                string fromURIStr = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromURI.ToString() : "null";
                //string toUser = (sipRequest.Header.To != null) ? sipRequest.Header.To.ToURI.User : null;
                //string summaryStr = "req " + sipRequest.Method + " from=" + fromUser + ", to=" + toUser + ", " + remoteEndPoint.ToString();
                //logger.Debug("AppServerCore GotRequest " + sipRequest.Method + " from " + remoteEndPoint.ToString() + " callid=" + sipRequest.Header.CallId + ".");

                SIPDialogue dialogue = null;

                // Check dialogue requests for an existing dialogue.
                if ((sipRequest.Method == SIPMethodsEnum.BYE || sipRequest.Method == SIPMethodsEnum.INFO || sipRequest.Method == SIPMethodsEnum.INVITE ||
                    sipRequest.Method == SIPMethodsEnum.MESSAGE || sipRequest.Method == SIPMethodsEnum.NOTIFY || sipRequest.Method == SIPMethodsEnum.OPTIONS ||
                        sipRequest.Method == SIPMethodsEnum.REFER)
                    && sipRequest.Header.From != null && sipRequest.Header.From.FromTag != null && sipRequest.Header.To != null && sipRequest.Header.To.ToTag != null)
                {
                    dialogue = m_sipDialogueManager.GetDialogue(sipRequest);
                }

                if (dialogue != null && sipRequest.Method != SIPMethodsEnum.ACK)
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Matching dialogue found for " + sipRequest.Method + " to " + sipRequest.URI.ToString() + " from " + remoteEndPoint + ".", dialogue.Owner));
                    if (sipRequest.Method != SIPMethodsEnum.REFER)
                    {
                        m_sipDialogueManager.ProcessInDialogueRequest(localSIPEndPoint, remoteEndPoint, sipRequest, dialogue);
                    }
                    else
                    {
                        m_sipDialogueManager.ProcessInDialogueReferRequest(localSIPEndPoint, remoteEndPoint, sipRequest, dialogue, m_callManager.BlindTransfer);
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
                {
                    #region CANCEL request handling.

                    UASInviteTransaction inviteTransaction = (UASInviteTransaction)m_sipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                    if (inviteTransaction != null)
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Cancelling call for " + sipRequest.URI.ToString() + ".", fromUser));
                        SIPCancelTransaction cancelTransaction = m_sipTransport.CreateCancelTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, inviteTransaction);
                        cancelTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No matching transaction was found for CANCEL to " + sipRequest.URI.ToString() + ".", fromUser));
                        SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                        m_sipTransport.SendResponse(noCallLegResponse);
                    }

                    #endregion
                }
                else if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No dialogue matched for BYE to " + sipRequest.URI.ToString() + ".", fromUser));
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.REFER)
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No dialogue matched for REFER to " + sipRequest.URI.ToString() + ".", fromUser));
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.ACK)
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "No transaction matched for ACK for " + sipRequest.URI.ToString() + ".", fromUser));
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    #region INVITE request processing.

                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "AppServerCore INVITE received, uri=" + sipRequest.URI.ToString() + ", cseq=" + sipRequest.Header.CSeq + ".", null));

                    if (sipRequest.URI.User == m_dispatcherUsername)
                    {
                        // Incoming call from monitoring process checking the application server is still running.
                        UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                        uasTransaction.CDR = null;
                        SIPServerUserAgent incomingCall = new SIPServerUserAgent(m_sipTransport, m_outboundProxy, sipRequest.URI.User, sipRequest.URI.Host, SIPCallDirection.In, null, null, null, uasTransaction);
                        incomingCall.NoCDR();
                        uasTransaction.NewCallReceived += (local, remote, transaction, request) => { m_callManager.QueueNewCall(incomingCall); };
                        uasTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                    }
                    else if (GetCanonicalDomain_External(sipRequest.Header.From.FromUserField.URI.Host, false) != null)
                    {
                        // Call identified as outgoing call for application server serviced domain.
                        string fromDomain = GetCanonicalDomain_External(sipRequest.Header.From.FromUserField.URI.Host, false);
                        UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                        SIPServerUserAgent outgoingCall = new SIPServerUserAgent(m_sipTransport, m_outboundProxy, fromUser, fromDomain, SIPCallDirection.Out, GetSIPAccount_External, SIPRequestAuthenticator.AuthenticateSIPRequest, FireProxyLogEvent, uasTransaction);
                        uasTransaction.NewCallReceived += (local, remote, transaction, request) => { m_callManager.QueueNewCall(outgoingCall); };
                        uasTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                    }
                    else if (GetCanonicalDomain_External(sipRequest.URI.Host, true) != null)
                    {
                        // Call identified as incoming call for application server serviced domain.
                        if (sipRequest.URI.User.IsNullOrBlank())
                        {
                            // Cannot process incoming call if no user is specified.
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "INVITE received with an empty URI user " + sipRequest.URI.ToString() + ", returning address incomplete.", null));
                            UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                            SIPResponse addressIncompleteResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.AddressIncomplete, "No user specified");
                            uasTransaction.SendFinalResponse(addressIncompleteResponse);
                        }
                        else
                        {
                            // Send the incoing call to the call manager for processing.
                            string uriUser = sipRequest.URI.User;
                            string uriDomain = GetCanonicalDomain_External(sipRequest.URI.Host, true);
                            UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                            SIPServerUserAgent incomingCall = new SIPServerUserAgent(m_sipTransport, m_outboundProxy, uriUser, uriDomain, SIPCallDirection.In, GetSIPAccount_External, null, FireProxyLogEvent, uasTransaction);
                            uasTransaction.NewCallReceived += (local, remote, transaction, request) => { m_callManager.QueueNewCall(incomingCall); };
                            uasTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                        }
                    }
                    else
                    {
                        // Return not found for non-serviced domain.
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Domain not serviced " + sipRequest.URI.ToString() + ", returning not found.", null));
                        UASInviteTransaction uasTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                        SIPResponse notServicedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, "Domain not serviced");
                        uasTransaction.SendFinalResponse(notServicedResponse);
                    }

                    #endregion
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.UnrecognisedMessage, "MethodNotAllowed response for " + sipRequest.Method + " from " + fromUser + " socket " + remoteEndPoint.ToString() + ".", null));
                    SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    m_sipTransport.SendResponse(notAllowedResponse);
                }
            }
            catch (Exception excp)
            {
                string reqExcpError = "Exception SIPAppServerCore GotRequest (" + remoteEndPoint + "). " + excp.Message;
                logger.Error(reqExcpError);
                SIPMonitorEvent reqExcpEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, reqExcpError, sipRequest, null, localSIPEndPoint, remoteEndPoint, SIPCallDirection.In);
                FireProxyLogEvent(reqExcpEvent);
                throw excp;
            }
        }

        public void GotResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "App Server received a SIP response from " + remoteEndPoint + " that did not match an existing transaction.\n" + sipResponse.ToString(), null));
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            if (SIPMonitorLogEvent_External != null)
            {
                try
                {
                    SIPMonitorLogEvent_External(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireProxyLogEvent SIPAppServerCore. " + excp.Message);
                }
            }
        }

        #region Unit testing.

        #if UNITTEST
	
		[TestFixture]
		public class SIPAppServerCoreUnitTest
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
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);
				Assert.IsTrue(true, "True was false.");
			}

            [Test]
            public void CreateStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine = new SIPTransactionEngine();
                SIPTransport sipTransport = new SIPTransport(SIPDNSManager.Resolve, transactionEngine, new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, 3000)), false, false);
                SIPAppServerCore appServerCore = new SIPAppServerCore(sipTransport, null, null, null, null, null);
                sipTransport.Shutdown();
            }

            [Test]
            public void B2BOptionsStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine1 = new SIPTransactionEngine();
                SIPTransport sipTransport1 = new SIPTransport(SIPDNSManager.Resolve, transactionEngine1, true, false);
                sipTransport1.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, 3000)));
                SIPAppServerCore appServerCore1 = new SIPAppServerCore(sipTransport1, null, statefulProxyCore1_StatefulProxyLogEvent, null, null, null);

                SIPTransactionEngine transactionEngine2 = new SIPTransactionEngine();
                SIPTransport sipTransport2 = new SIPTransport(SIPDNSManager.Resolve, transactionEngine2, true, false);
                sipTransport2.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, 3001)));
                SIPAppServerCore appServerCore2 = new SIPAppServerCore(sipTransport2, null, statefulProxyCore2_StatefulProxyLogEvent, null, null, null);

                sipTransport1.SIPRequestOutTraceEvent += sipTransport1_SIPRequestOutTraceEvent;
                sipTransport1.SIPResponseInTraceEvent += sipTransport1_SIPResponseInTraceEvent;
                sipTransport2.SIPRequestInTraceEvent += sipTransport2_SIPRequestInTraceEvent;

                SIPRequest optionsRequest = GetOptionsRequest(SIPURI.ParseSIPURI("sip:127.0.0.1:3001"), 1, sipTransport1.GetDefaultTransportContact(SIPProtocolsEnum.udp).SocketEndPoint);
                sipTransport1.SendRequest(optionsRequest);

                Thread.Sleep(200);

                // Check the NUnit Console.Out to make sure there are SIP requests and responses being displayed.

                sipTransport1.Shutdown();
                sipTransport2.Shutdown();
            }

            [Test]
            public void B2BInviteStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine1 = new SIPTransactionEngine();
                SIPTransport sipTransport1 = new SIPTransport(SIPDNSManager.Resolve, transactionEngine1, true, false);
                IPEndPoint sipTransport1EndPoint = new IPEndPoint(IPAddress.Loopback, 3000);
                sipTransport1.AddSIPChannel(new SIPUDPChannel(sipTransport1EndPoint));
                SIPAppServerCore statefulProxyCore1 = new SIPAppServerCore(sipTransport1, null, statefulProxyCore1_StatefulProxyLogEvent, null, null, null);

                SIPTransactionEngine transactionEngine2 = new SIPTransactionEngine();
                SIPTransport sipTransport2 = new SIPTransport(SIPDNSManager.Resolve, transactionEngine2, true, false);
                IPEndPoint sipTransport2EndPoint = new IPEndPoint(IPAddress.Loopback, 3001);
                sipTransport2.AddSIPChannel(new SIPUDPChannel(sipTransport2EndPoint));
                SIPAppServerCore statefulProxyCore2 = new SIPAppServerCore(sipTransport2, statefulProxyCore2_GetCanonicalDomain, statefulProxyCore2_StatefulProxyLogEvent, null, null, null);

                sipTransport1.SIPRequestOutTraceEvent += sipTransport1_SIPRequestOutTraceEvent;
                sipTransport1.SIPResponseInTraceEvent += sipTransport1_SIPResponseInTraceEvent;
                sipTransport2.SIPRequestInTraceEvent += sipTransport2_SIPRequestInTraceEvent;
                sipTransport2.SIPResponseOutTraceEvent += sipTransport2_SIPResponseOutTraceEvent;

                SIPRequest inviteRequest = GetInviteRequest(sipTransport1EndPoint, null, sipTransport2EndPoint);
                sipTransport1.SendRequest(inviteRequest);

                Thread.Sleep(200);

                // Check the NUnit Console.Out to make sure there are SIP requests and responses being displayed.

                sipTransport1.Shutdown();
                sipTransport2.Shutdown();
            }

            [Test]
            public void B2BInviteTransactionStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine1 = new SIPTransactionEngine();
                SIPTransport sipTransport1 = new SIPTransport(SIPDNSManager.Resolve, transactionEngine1, true, false);
                IPEndPoint sipTransport1EndPoint = new IPEndPoint(IPAddress.Loopback, 3000);
                sipTransport1.AddSIPChannel(new SIPUDPChannel(sipTransport1EndPoint));
                SIPAppServerCore statefulProxyCore1 = new SIPAppServerCore(sipTransport1, null, statefulProxyCore1_StatefulProxyLogEvent, null, null, null);

                SIPTransactionEngine transactionEngine2 = new SIPTransactionEngine();
                SIPTransport sipTransport2 = new SIPTransport(SIPDNSManager.Resolve, transactionEngine2, true, false);
                IPEndPoint sipTransport2EndPoint = new IPEndPoint(IPAddress.Loopback, 3001);
                sipTransport2.AddSIPChannel(new SIPUDPChannel(sipTransport2EndPoint));
                SIPAppServerCore statefulProxyCore2 = new SIPAppServerCore(sipTransport2, statefulProxyCore2_GetCanonicalDomain, statefulProxyCore2_StatefulProxyLogEvent, null, null, null);

                sipTransport1.SIPRequestOutTraceEvent += sipTransport1_SIPRequestOutTraceEvent;
                sipTransport1.SIPResponseInTraceEvent += sipTransport1_SIPResponseInTraceEvent;
                sipTransport2.SIPRequestInTraceEvent += sipTransport2_SIPRequestInTraceEvent;
                sipTransport2.SIPResponseOutTraceEvent += sipTransport2_SIPResponseOutTraceEvent;

                SIPRequest inviteRequest = GetInviteRequest(sipTransport1EndPoint, null, sipTransport2EndPoint);
                UACInviteTransaction uacInvite = sipTransport1.CreateUACTransaction(inviteRequest, new SIPEndPoint(SIPProtocolsEnum.udp, sipTransport2EndPoint), new SIPEndPoint(SIPProtocolsEnum.udp, sipTransport1EndPoint), null);
                uacInvite.SendInviteRequest(new SIPEndPoint(SIPProtocolsEnum.udp, sipTransport2EndPoint), inviteRequest);

                Thread.Sleep(200);

                // Check the NUnit Console.Out to make sure there are SIP requests and responses being displayed.

                sipTransport1.Shutdown();
                sipTransport2.Shutdown();
            }


            [Test]
            public void B2BInviteTransactionUserFoundStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine1 = new SIPTransactionEngine();
                SIPTransport sipTransport1 = new SIPTransport(SIPDNSManager.Resolve, transactionEngine1, true, false);
                IPEndPoint sipTransport1EndPoint = new IPEndPoint(IPAddress.Loopback, 3000);
                sipTransport1.AddSIPChannel(new SIPUDPChannel(sipTransport1EndPoint));
                SIPAppServerCore statefulProxyCore1 = new SIPAppServerCore(sipTransport1, null, statefulProxyCore1_StatefulProxyLogEvent, null, null, null);

                SIPTransactionEngine transactionEngine2 = new SIPTransactionEngine();
                SIPTransport sipTransport2 = new SIPTransport(SIPDNSManager.Resolve, transactionEngine2, true, false);
                IPEndPoint sipTransport2EndPoint = new IPEndPoint(IPAddress.Loopback, 3001);
                sipTransport2.AddSIPChannel(new SIPUDPChannel(sipTransport2EndPoint));
                SIPAppServerCore statefulProxyCore2 = new SIPAppServerCore(
                    sipTransport2, 
                    statefulProxyCore2_GetCanonicalDomain, 
                    statefulProxyCore2_StatefulProxyLogEvent, 
                    null, 
                    null, 
                    null);
 
                //statefulProxyCore2.GetExtensionOwner += new GetExtensionOwnerDelegate(statefulProxyCore2_GetExtensionOwner);

                sipTransport1.SIPRequestOutTraceEvent += sipTransport1_SIPRequestOutTraceEvent;
                sipTransport1.SIPResponseInTraceEvent += sipTransport1_SIPResponseInTraceEvent;
                sipTransport2.SIPRequestInTraceEvent += sipTransport2_SIPRequestInTraceEvent;
                sipTransport2.SIPResponseOutTraceEvent += sipTransport2_SIPResponseOutTraceEvent;
                //statefulProxyCore2.LoadDialPlan += new LoadDialPlanDelegate(statefulProxyCore2_LoadDialPlan);

                SIPRequest inviteRequest = GetInviteRequest(sipTransport1EndPoint, null, sipTransport2EndPoint);
                UACInviteTransaction uacInvite = sipTransport1.CreateUACTransaction(inviteRequest, new SIPEndPoint(SIPProtocolsEnum.udp, sipTransport2EndPoint), new SIPEndPoint(SIPProtocolsEnum.udp, sipTransport1EndPoint), null);
                uacInvite.SendInviteRequest(new SIPEndPoint(SIPProtocolsEnum.udp, sipTransport2EndPoint), inviteRequest);

                Thread.Sleep(1000);

                // Check the NUnit Console.Out to make sure there are SIP requests and responses being displayed.

                sipTransport1.Shutdown();
                sipTransport2.Shutdown();
            }

            SIPDialPlan statefulProxyCore2_LoadDialPlan(string sipAccountUsername, string sipAccountDomain)
            {
                return new SIPDialPlan(null, null, null, null, SIPDialPlanScriptTypesEnum.Ruby);
            }

            string statefulProxyCore2_GetExtensionOwner(string user, string domain)
            {
                return "joe.bloggs";
            }

            string statefulProxyCore2_GetCanonicalDomain(string domain)
            {
                return domain;
            }

            void statefulProxyCore2_StatefulProxyLogEvent(SIPMonitorEvent logEvent)
            {
                Console.WriteLine("AppServerCore2-" + logEvent.EventType + ": " + logEvent.Message);
            }

            void statefulProxyCore1_StatefulProxyLogEvent(SIPMonitorEvent logEvent)
            {
                Console.WriteLine("AppServerCore1-" + logEvent.EventType + ": " + logEvent.Message);
            }

            void sipTransport2_SIPResponseOutTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint toEndPoint, SIPResponse sipResponse)
            {
                Console.WriteLine("Response Sent: " + localEndPoint.ToString() + "<-" + toEndPoint.ToString() + "\r\n" + sipResponse.ToString());
            }

            void sipTransport1_SIPResponseInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, SIPResponse sipResponse)
            {
                Console.WriteLine("Response Received: " + localEndPoint.ToString() + "<-" + fromEndPoint.ToString() + "\r\n" + sipResponse.ToString());
            }

            void sipTransport1_SIPRequestOutTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint toEndPoint, SIPRequest sipRequest)
            {
                Console.WriteLine("Request Sent: " + localEndPoint.ToString() + "<-" + toEndPoint.ToString() + "\r\n" + sipRequest.ToString());
            }

            void sipTransport2_SIPRequestInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, SIPRequest sipRequest)
            {
                Console.WriteLine("Request Received: " + localEndPoint.ToString() + "<-" + fromEndPoint.ToString() + "\r\n" + sipRequest.ToString());
            }

            private SIPRequest GetOptionsRequest(SIPURI serverURI, int cseq, IPEndPoint contact)
            {
                SIPRequest optionsRequest = new SIPRequest(SIPMethodsEnum.OPTIONS, serverURI);

                SIPFromHeader fromHeader = new SIPFromHeader(null, SIPURI.ParseSIPURI("sip:" + contact.ToString()), null);
                SIPToHeader toHeader = new SIPToHeader(null, serverURI, null);

                string callId = CallProperties.CreateNewCallId();
                string branchId = CallProperties.CreateBranchId();

                SIPHeader header = new SIPHeader(fromHeader, toHeader, cseq, callId);
                header.CSeqMethod = SIPMethodsEnum.OPTIONS;
                header.MaxForwards = 0;

                SIPViaHeader viaHeader = new SIPViaHeader(contact.Address.ToString(), contact.Port, branchId);
                header.Vias.PushViaHeader(viaHeader);

                optionsRequest.Header = header;

                return optionsRequest;
            }

            private SIPRequest GetInviteRequest(IPEndPoint localContact, string inviteBody, IPEndPoint dstEndPoint)
            {
                SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI("sip:" + dstEndPoint));

                SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader("<sip:" + localContact + ">"), SIPToHeader.ParseToHeader("<sip:" + dstEndPoint + ">"), 1, CallProperties.CreateNewCallId());
                inviteHeader.From.FromTag = CallProperties.CreateNewTag();
                inviteHeader.Contact = SIPContactHeader.ParseContactHeader("sip:" + localContact);
                inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
                inviteHeader.UserAgent = "unit test";
                inviteRequest.Header = inviteHeader;

                SIPViaHeader viaHeader = new SIPViaHeader(localContact.Address.ToString(), localContact.Port,CallProperties.CreateBranchId(), SIPProtocolsEnum.udp);
                inviteRequest.Header.Vias.PushViaHeader(viaHeader);

                //inviteRequest.Body = inviteBody;
                //inviteRequest.Header.ContentLength = inviteBody.Length;
                inviteRequest.Header.ContentType = "application/sdp";

                return inviteRequest;
            }
        }

        #endif

        #endregion
    }
}
