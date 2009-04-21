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
        public const string SIPPROXY_USERAGENT = "www.sipsorcery.com";
        public const string DEFAULT_DOMAIN = "sip.sipsorcery.com";
               
        private static ILog logger = AppState.GetLogger("sipproxy");

        private readonly string m_proxyViaParameterName = RegistrarCore.PROXY_VIA_PARAMETER_NAME;

        private SIPMonitorLogDelegate SIPMonitorLogEvent_External;              // Function to log messages from this core.
        private GetCanonicalDomainDelegate GetCanonicalDomain_External; 

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_registrarSocket;
        private SIPEndPoint m_regAgentSocket;
        private SIPCallManager m_callManager;
        private SIPRequestAuthoriser m_sipRequestAuthoriser;

        private SIPEndPoint m_localSEPForRegistrar; // Local socket used to forward REGISTER requests to the SIP Registrar servicing this application server (not 3rd party registrations).

        public SIPAppServerCore(
			SIPTransport sipTransport, 
            SIPEndPoint registrarSocket,
            SIPEndPoint regAgentSocket,
            GetCanonicalDomainDelegate getCanonicalDomain,
            SIPMonitorLogDelegate proxyLog,
            SIPCallManager callManager,
            SIPRequestAuthoriser sipRequestAuthoriser)
		{
			try
			{
                m_sipTransport = sipTransport;
                m_callManager = callManager;
                m_sipRequestAuthoriser = sipRequestAuthoriser;

                m_sipTransport.SIPTransportRequestReceived += GotRequest; 
                m_sipTransport.SIPTransportResponseReceived += GotResponse;

                m_registrarSocket = registrarSocket;
                m_regAgentSocket = regAgentSocket;

                GetCanonicalDomain_External = getCanonicalDomain;
                SIPMonitorLogEvent_External = proxyLog;

                if (m_registrarSocket != null)
                {
                    m_localSEPForRegistrar = m_sipTransport.GetDefaultSIPEndPoint(m_registrarSocket.SIPProtocol);
                }
			}
			catch(Exception excp)
			{
				logger.Error("Exception StatefulProxyCore (ctor). " + excp.Message);
				throw excp;
			}
		}

        public void GotRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
		{
            try
            {
                // Used in the proxy monitor messages only, plays no part in request routing.
                string fromUser = (sipRequest.Header.From != null ) ? sipRequest.Header.From.FromURI.User : null;
                string fromURI = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromURI.User + "@" + sipRequest.Header.From.FromURI.Host : "no from header";
                string toUser = (sipRequest.Header.To != null) ? sipRequest.Header.To.ToURI.User : null;
                string summaryStr = "req " + sipRequest.Method + " from=" + fromUser + ", to=" + toUser + ", " + remoteEndPoint.ToString();

                SIPDialogue dialogue = null;

                // Check dialogue requests for an existing dialogue.
                if ((sipRequest.Method == SIPMethodsEnum.BYE || sipRequest.Method == SIPMethodsEnum.INFO || sipRequest.Method == SIPMethodsEnum.INVITE ||
                    sipRequest.Method == SIPMethodsEnum.MESSAGE || sipRequest.Method == SIPMethodsEnum.NOTIFY || sipRequest.Method == SIPMethodsEnum.REFER)
                    && sipRequest.Header.From != null && sipRequest.Header.From.FromTag != null && sipRequest.Header.To != null && sipRequest.Header.To.ToTag != null )
                {
                    dialogue = m_callManager.GetDialogue(sipRequest, remoteEndPoint);
                }

                if (dialogue != null && sipRequest.Method != SIPMethodsEnum.ACK)
                {
                    #region Process in dialogue requests.

                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Matching dialogue found for " + sipRequest.Method + " to " + sipRequest.URI.ToString() + " from " + remoteEndPoint + ".", dialogue.Owner));
                    
                    if (sipRequest.Method == SIPMethodsEnum.BYE)
                    {
                        SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint);
                        //logger.Debug("Matching dialogue found for BYE request to " + sipRequest.URI.ToString() + ".");
                        SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        byeTransaction.SendFinalResponse(byeResponse);

                        // Let the CallManager know so the forwarded leg of the call can be hung up.
                        m_callManager.CallHungup(dialogue, sipRequest.Header.Reason);
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                    {
                        UASInviteTransaction reInviteTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint);
                        reInviteTransaction.CDR = null;     // Don't want CDR's on re-INVITEs.
                        m_callManager.ForwardInDialogueRequest(dialogue, reInviteTransaction, localSIPEndPoint, remoteEndPoint);
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.MESSAGE)
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "MESSAGE for call " + sipRequest.URI.ToString() + ": " + sipRequest.Body + ".", dialogue.Owner));
                        SIPNonInviteTransaction messageTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint);
                        SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        messageTransaction.SendFinalResponse(okResponse);

                        // ToDo the request must be authenticated before passing to the dialplan.
                        /*if(dialogue.Owner != null)
                        {
                            // Break back into the user's dial plan.
                            DialPlan userDialPlan = GetDialPlan_External(dialogue.Owner, dialogue.OwnerDomain);
                            if (userDialPlan != null)
                            {
                                //userDialPlan.ProcessNonInviteRequest(FireProxyLogEvent, m_callManager, localEndPoint, remoteEndPoint, sipRequest);
                            }
                        }*/
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.REFER)
                    {
                        SIPNonInviteTransaction referTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint);
                        m_callManager.Transfer(dialogue, referTransaction);
                    }
                    else
                    {
                        // This is a request on an established call forward through to the other end, no further action required.
                        SIPNonInviteTransaction passThruTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint);
                        m_callManager.ForwardInDialogueRequest(dialogue, passThruTransaction, localSIPEndPoint, remoteEndPoint);
                    }

                    #endregion
                }
                else if (sipRequest.Method == SIPMethodsEnum.NOTIFY)
                {
                    #region NOTIFY request handling.

                    // Check if the notify request is for a sipswitch user.
                    /*if (GetSIPAccount_External(sipRequest.URI.User, GetCanonicalDomain_External(sipRequest.URI.Host)) != null)
                    {
                         List<SIPRegistrarBinding> bindings = GetSIPAccountBindings_External(sipRequest.URI.User, sipRequest.URI.Host);

                        if (bindings != null)
                        {
                            foreach (SIPRegistrarBinding binding in bindings)
                            {
                                SIPURI contactURI = binding.MangledContactURI;
                                IPEndPoint contactEndPoint = m_sipTransport.GetURIEndPoint(contactURI, true);

                                // Rather than create a brand new request copy the received one and modify the headers that need to be unique.
                                SIPRequest notifyRequest = sipRequest.Copy();
                                notifyRequest.URI = contactURI;
                                notifyRequest.Header.Contact = SIPContactHeader.ParseContactHeader(localSIPEndPoint.ToString());
                                notifyRequest.Header.To = new SIPToHeader(null, contactURI, null);
                                notifyRequest.Header.CallId = CallProperties.CreateNewCallId();
                                SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
                                notifyRequest.Header.Via = new SIPViaSet();
                                notifyRequest.Header.Via.PushViaHeader(viaHeader);

                                logger.Debug("Forwarding NOTIFY to switch user binding " + contactURI.ToString() + " at " + IPSocket.GetSocketString(contactEndPoint) + ".");
                                IPEndPoint nextHopSocket = (binding.ProxyEndPoint != null) ? binding.ProxyEndPoint : contactEndPoint;
                                SIPNonInviteTransaction notifyTransaction = m_sipTransport.CreateNonInviteTransaction(notifyRequest, nextHopSocket, localSIPEndPoint);
                                notifyTransaction.SendReliableRequest();
                            }

                            // Send OK response to server.
                            SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                            m_sipTransport.SendResponse(okResponse);
                        }
                        else
                        {
                            // Send Not found response to server.
                            SIPResponse notFoundResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, null);
                            m_sipTransport.SendResponse(notFoundResponse);
                        }
                    }
                    else
                    {
                        // Send Not found response to server.
                        SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, null);
                        m_sipTransport.SendResponse(okResponse);
                    }*/

                    #endregion
                }
                else if (sipRequest.Method == SIPMethodsEnum.REGISTER)
                {
                    #region REGISTER request handling.

                    if (m_regAgentSocket != null && remoteEndPoint.ToString() == m_regAgentSocket.ToString())
                    {
                        // This is a REGISTER request for the registration agent that should be forwarded out to the 3rd party provider.

                        // The registration agent has indicated where it wants the REGISTER request sent to by adding a Route header.
                        // Remove the header in case it confuses the SIP Registrar the REGISTER is being sent to.
                        SIPRoute registrarRoute = sipRequest.Header.Routes.PopRoute();
                        sipRequest.Header.Routes = null;

                        SIPEndPoint regAgentEndPoint = m_sipTransport.GetDefaultSIPEndPoint(registrarRoute.URI.Protocol);
                        if (regAgentEndPoint != null)
                        {
                            SIPViaHeader originalHeader = sipRequest.Header.Vias.PopTopViaHeader();
                            sipRequest.Header.Vias.PushViaHeader(new SIPViaHeader(regAgentEndPoint, originalHeader.Branch));
                            //sipRequest.Header.Via.PushViaHeader(new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId()));

                            sipRequest.LocalSIPEndPoint = regAgentEndPoint;
                            m_sipTransport.SendRequest(new SIPEndPoint(registrarRoute.URI), sipRequest);
                        }
                        else
                        {
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Error, "The application server cannot forward Registration Agent requests as no " + registrarRoute.URI.Protocol + " channel has been configured.", null));
                        }
                    }
                    else if (m_registrarSocket != null)
                    {
                        if (m_localSEPForRegistrar != null)
                        {
                            // If the SIP Registrar is in the same process add a Via Header on for the received end point.
                            // This will allow the proxy and registrar to be seperated easily if the need arises.
                            sipRequest.Header.Vias.TopViaHeader.ViaParameters.Set(m_proxyViaParameterName, localSIPEndPoint.ToString());
                            sipRequest.Header.Vias.PushViaHeader(new SIPViaHeader(m_localSEPForRegistrar, CallProperties.CreateBranchId()));
                            sipRequest.LocalSIPEndPoint = m_localSEPForRegistrar;
                            m_sipTransport.SendRequest(m_registrarSocket, sipRequest);
                        }
                        else
                        {
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Error, "The application server cannot forward REGISTER requests as no UDP channel has been configured.", null));
                            SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.InternalServerError, "Registrar is not reachable");
                            m_sipTransport.SendResponse(notAllowedResponse);
                        }
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Warn, "MethodNotAllowed response for " + sipRequest.Method + " from " + fromUser + " socket " + remoteEndPoint.ToString() + ".", null));
                        SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                        m_sipTransport.SendResponse(notAllowedResponse);
                    }

                    #endregion
                }
                else if (sipRequest.Method == SIPMethodsEnum.CANCEL)
                {
                    #region CANCEL request handling.

                    UASInviteTransaction inviteTransaction = (UASInviteTransaction)m_sipTransport.GetTransaction(SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, SIPMethodsEnum.INVITE));

                    if (inviteTransaction != null)
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Cancelling call for " + sipRequest.URI.ToString() + ".", fromUser));
                        SIPCancelTransaction cancelTransaction = m_sipTransport.CreateCancelTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, inviteTransaction);
                        cancelTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, sipRequest);
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "No matching transaction was found for CANCEL to " + sipRequest.URI.ToString() + ".", fromUser));
                        SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                        m_sipTransport.SendResponse(noCallLegResponse);
                    }

                    #endregion
                }
                else if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "No dialogue matched for BYE to " + sipRequest.URI.ToString() + ".", fromUser));
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);                    
                }
                else if (sipRequest.Method == SIPMethodsEnum.REFER)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "No dialogue matched for REFER to " + sipRequest.URI.ToString() + ".", fromUser));
                    SIPResponse noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                    m_sipTransport.SendResponse(noCallLegResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.ACK)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "No transaction matched for ACK for " + sipRequest.URI.ToString() + ".", fromUser));
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    #region INVITE request processing.

                    if (GetCanonicalDomain_External(sipRequest.Header.From.FromUserField.URI.Host) != null)
                    {
                        // Call identified as outgoing call for application server serviced domain.
                        SIPServerUserAgent uasOutgoingCall = new SIPServerUserAgent(m_sipTransport, null, m_sipRequestAuthoriser.AuthoriseSIPRequest, FireProxyLogEvent);
                        uasOutgoingCall.NewCall += m_callManager.ProcessNewCall;
                        uasOutgoingCall.InviteRequestReceived(sipRequest, localSIPEndPoint, remoteEndPoint);
                    }
                    else if (GetCanonicalDomain_External(sipRequest.URI.Host) != null)
                    {
                        // Call identified as incoming call for application server serviced domain.
                        SIPServerUserAgent uasIncomingCall = new SIPServerUserAgent(m_sipTransport, null, null, FireProxyLogEvent);
                        uasIncomingCall.NewCall += m_callManager.ProcessNewCall;
                        uasIncomingCall.InviteRequestReceived(sipRequest, localSIPEndPoint, remoteEndPoint);
                    }
                    else
                    {
                        // Return not found for non-serviced domain.
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "Domain not serviced " + sipRequest.URI.ToString() + ", returning not found.", null));
                        SIPResponse notServicedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, "Domain not serviced");
                        m_sipTransport.SendResponse(notServicedResponse);
                    }

                    #endregion
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.UnrecognisedMessage, "MethodNotAllowed response for " + sipRequest.Method + " from " + fromUser + " socket " + remoteEndPoint.ToString() + ".", null));
                    SIPResponse notAllowedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    m_sipTransport.SendResponse(notAllowedResponse);
                }
            }
            catch (Exception excp)
            {
                string reqExcpError = "Exception StatefulProxyCore GotRequest (" + remoteEndPoint + "). " + excp.Message;
                logger.Error(reqExcpError);

                SIPMonitorEvent reqExcpEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Error, reqExcpError, sipRequest, null, localSIPEndPoint.SocketEndPoint, remoteEndPoint.SocketEndPoint, SIPCallDirection.In);
                FireProxyLogEvent(reqExcpEvent);

                throw excp;
            }
		}

        public void GotResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            try
            {
                // The 3 fields below are used in the proxy monitor messages only, plays no part in response processing.
                //string fromUser = (sipResponse.Header.From != null) ? sipResponse.Header.From.FromURI.User : null;
                //string toUser = (sipResponse.Header.To != null) ? sipResponse.Header.To.ToURI.User : null;
                //string summaryStr = "resp " + sipResponse.Header.CSeqMethod + " " + sipResponse.StatusCode + " " + sipResponse.Status + ", from=" + fromUser + ", to=" + toUser + ", fromsock=" + IPSocket.GetSocketString(remoteEndPoint);

                SIPViaHeader proxyVia = sipResponse.Header.Vias.PopTopViaHeader();

                if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.REGISTER)
                {
                    if (m_registrarSocket != null && remoteEndPoint.ToString() == m_registrarSocket.ToString())
                    {
                        // This is a response from the SIP Registrar. The top Via header should contain a proxy parameter indicating which
                        // local SIP end point the original REGISTER request arrived on.

                        // Remove proxy parameter from via.
                        SIPViaHeader uacVia = sipResponse.Header.Vias.TopViaHeader;
                        string proxyEndPointStr = uacVia.ViaParameters.Get(m_proxyViaParameterName);
                        sipResponse.Header.Vias.TopViaHeader.ViaParameters.Remove(m_proxyViaParameterName);  // Don't forward this via parameter back to the UA.

                        if (proxyEndPointStr != null)
                        {
                            SIPURI proxyURI = SIPURI.ParseSIPURI(proxyEndPointStr);
                            if (proxyURI != null)
                            {
                                sipResponse.LocalSIPEndPoint = new SIPEndPoint(proxyURI);
                            }
                        }

                        m_sipTransport.SendResponse(sipResponse);
                    }
                    else if (m_regAgentSocket != null)
                    {
                        // A REGISTER response that has not come from the SIP Registrar socket is for the Registration Agent. In that case we need to add back on
                        // the Via header that was removed when the original REGISTER request was passed through from the Agent.
                        sipResponse.Header.Vias.PushViaHeader(new SIPViaHeader(m_regAgentSocket, proxyVia.Branch));

                        // The registration agent will always communicate with the default UDP transport.
                        sipResponse.LocalSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint(SIPProtocolsEnum.udp);
                        m_sipTransport.SendResponse(sipResponse);
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Warn, "A REGISTER was received from " + remoteEndPoint + " that did not match the Registrar or Registration Agent.", null));
                    }
                }
                else if (m_sipTransport.IsLocalSIPEndPoint(new SIPEndPoint(proxyVia.Transport, IPSocket.ParseSocketString(proxyVia.ContactAddress))))
                //else if(proxyVia.Host == m_sipTransport.GetTransportContact(null).Address.ToString() && proxyVia.Port == m_sipTransport.GetTransportContact(null).Port)
                {
                    m_sipTransport.SendResponse(sipResponse);
                }
                else
                {
                    // Response not for this proxy, ignoring.
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Warn, "Dropping SIP response from " + remoteEndPoint + ", because the top Via header was not for this proxy, top header was " + proxyVia.ToString() + ".", null));
                }
            }
            catch (Exception excp)
            {
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.Error, "Exception StatefulProxyCore GotResponse (" + remoteEndPoint + "). " + excp.Message + "\n" + sipResponse.ToString(), null));
                throw excp;
            }
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
		public class StatefulProxyCoreUnitTest
		{           
            [TestFixtureSetUp]
			public void Init()
			{
			}
	
			[TestFixtureTearDown]
			public void Dispose()
			{			
				
			}

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
                SIPTransport sipTransport = new SIPTransport(transactionEngine, new IPEndPoint(IPAddress.Loopback, 3000), false, false);
                StatefulProxyCore statefulProxyCore = new StatefulProxyCore(sipTransport, null, null, false, null);
                sipTransport.Shutdown();
            }

            [Test]
            public void B2BOptionsStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine1 = new SIPTransactionEngine();
                SIPTransport sipTransport1 = new SIPTransport(transactionEngine1, true, false);
                sipTransport1.AddSIPChannel(new IPEndPoint(IPAddress.Loopback, 3000));
                StatefulProxyCore statefulProxyCore1 = new StatefulProxyCore(sipTransport1, null, null, false, null);

                SIPTransactionEngine transactionEngine2 = new SIPTransactionEngine();
                SIPTransport sipTransport2 = new SIPTransport(transactionEngine2, true, false);
                sipTransport2.AddSIPChannel(new IPEndPoint(IPAddress.Loopback, 3001));
                StatefulProxyCore statefulProxyCore2 = new StatefulProxyCore(sipTransport2, null, null, false, null);

                sipTransport1.SIPRequestOutTraceEvent += new SIPTransportSIPRequestOutTraceDelegate(sipTransport1_SIPRequestOutTraceEvent);
                sipTransport1.SIPResponseInTraceEvent += new SIPTransportSIPResponseInTraceDelegate(sipTransport1_SIPResponseInTraceEvent);
                sipTransport2.SIPRequestInTraceEvent += new SIPTransportSIPRequestInTraceDelegate(sipTransport2_SIPRequestInTraceEvent);
                statefulProxyCore1.StatefulProxyLogEvent += new SIPMonitorLogDelegate(statefulProxyCore1_StatefulProxyLogEvent);
                statefulProxyCore2.StatefulProxyLogEvent += new SIPMonitorLogDelegate(statefulProxyCore2_StatefulProxyLogEvent);

                SIPRequest optionsRequest = GetOptionsRequest(SIPURI.ParseSIPURI("sip:127.0.0.1:3001"), 1, sipTransport1.GetDefaultTransportContact(SIPProtocolsEnum.UDP));
                sipTransport1.SendRequest(optionsRequest, SIPProtocolsEnum.UDP);

                Thread.Sleep(200);

                // Check the NUnit Console.Out to make sure there are SIP requests and responses being displayed.

                sipTransport1.Shutdown();
                sipTransport2.Shutdown();
            }

            [Test]
            public void B2BInviteStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine1 = new SIPTransactionEngine();
                SIPTransport sipTransport1 = new SIPTransport(transactionEngine1, true, false);
                IPEndPoint sipTransport1EndPoint = new IPEndPoint(IPAddress.Loopback, 3000);
                sipTransport1.AddSIPChannel(sipTransport1EndPoint);
                StatefulProxyCore statefulProxyCore1 = new StatefulProxyCore(sipTransport1, null, null, false, null);

                SIPTransactionEngine transactionEngine2 = new SIPTransactionEngine();
                SIPTransport sipTransport2 = new SIPTransport(transactionEngine2, true, false);
                IPEndPoint sipTransport2EndPoint = new IPEndPoint(IPAddress.Loopback, 3001);
                sipTransport2.AddSIPChannel(sipTransport2EndPoint);
                StatefulProxyCore statefulProxyCore2 = new StatefulProxyCore(sipTransport2, null, null, false, null);
                statefulProxyCore2.GetCanonicalDomain += new GetCanonicalDomainDelegate(statefulProxyCore2_GetCanonicalDomain);

                sipTransport1.SIPRequestOutTraceEvent += new SIPTransportSIPRequestOutTraceDelegate(sipTransport1_SIPRequestOutTraceEvent);
                sipTransport1.SIPResponseInTraceEvent += new SIPTransportSIPResponseInTraceDelegate(sipTransport1_SIPResponseInTraceEvent);
                sipTransport2.SIPRequestInTraceEvent += new SIPTransportSIPRequestInTraceDelegate(sipTransport2_SIPRequestInTraceEvent);
                sipTransport2.SIPResponseOutTraceEvent += new SIPTransportSIPResponseOutTraceDelegate(sipTransport2_SIPResponseOutTraceEvent);
                statefulProxyCore1.StatefulProxyLogEvent += new SIPMonitorLogDelegate(statefulProxyCore1_StatefulProxyLogEvent);
                statefulProxyCore2.StatefulProxyLogEvent += new SIPMonitorLogDelegate(statefulProxyCore2_StatefulProxyLogEvent);

                SIPRequest inviteRequest = GetInviteRequest(sipTransport1EndPoint, null, sipTransport2EndPoint);
                sipTransport1.SendRequest(inviteRequest, SIPProtocolsEnum.UDP);

                Thread.Sleep(200);

                // Check the NUnit Console.Out to make sure there are SIP requests and responses being displayed.

                sipTransport1.Shutdown();
                sipTransport2.Shutdown();
            }

            [Test]
            public void B2BInviteTransactionStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine1 = new SIPTransactionEngine();
                SIPTransport sipTransport1 = new SIPTransport(transactionEngine1, true, false);
                IPEndPoint sipTransport1EndPoint = new IPEndPoint(IPAddress.Loopback, 3000);
                sipTransport1.AddSIPChannel(sipTransport1EndPoint);
                StatefulProxyCore statefulProxyCore1 = new StatefulProxyCore(sipTransport1, null, null, false, null);

                SIPTransactionEngine transactionEngine2 = new SIPTransactionEngine();
                SIPTransport sipTransport2 = new SIPTransport(transactionEngine2, true, false);
                IPEndPoint sipTransport2EndPoint = new IPEndPoint(IPAddress.Loopback, 3001);
                sipTransport2.AddSIPChannel(sipTransport2EndPoint);
                StatefulProxyCore statefulProxyCore2 = new StatefulProxyCore(sipTransport2, null, null, false, null);
                statefulProxyCore2.GetCanonicalDomain += new GetCanonicalDomainDelegate(statefulProxyCore2_GetCanonicalDomain);

                sipTransport1.SIPRequestOutTraceEvent += new SIPTransportSIPRequestOutTraceDelegate(sipTransport1_SIPRequestOutTraceEvent);
                sipTransport1.SIPResponseInTraceEvent += new SIPTransportSIPResponseInTraceDelegate(sipTransport1_SIPResponseInTraceEvent);
                sipTransport2.SIPRequestInTraceEvent += new SIPTransportSIPRequestInTraceDelegate(sipTransport2_SIPRequestInTraceEvent);
                sipTransport2.SIPResponseOutTraceEvent += new SIPTransportSIPResponseOutTraceDelegate(sipTransport2_SIPResponseOutTraceEvent);
                statefulProxyCore1.StatefulProxyLogEvent += new SIPMonitorLogDelegate(statefulProxyCore1_StatefulProxyLogEvent);
                statefulProxyCore2.StatefulProxyLogEvent += new SIPMonitorLogDelegate(statefulProxyCore2_StatefulProxyLogEvent);

                SIPRequest inviteRequest = GetInviteRequest(sipTransport1EndPoint, null, sipTransport2EndPoint);
                UACInviteTransaction uacInvite = sipTransport1.CreateUACTransaction(inviteRequest, sipTransport2EndPoint, sipTransport1EndPoint, SIPProtocolsEnum.UDP);
                uacInvite.SendInviteRequest(sipTransport2EndPoint, inviteRequest);

                Thread.Sleep(200);

                // Check the NUnit Console.Out to make sure there are SIP requests and responses being displayed.

                sipTransport1.Shutdown();
                sipTransport2.Shutdown();
            }


            [Test]
            public void B2BInviteTransactionUserFoundStatefulProxyTest()
            {
                SIPTransactionEngine transactionEngine1 = new SIPTransactionEngine();
                SIPTransport sipTransport1 = new SIPTransport(transactionEngine1, true, false);
                IPEndPoint sipTransport1EndPoint = new IPEndPoint(IPAddress.Loopback, 3000);
                sipTransport1.AddSIPChannel(sipTransport1EndPoint);
                StatefulProxyCore statefulProxyCore1 = new StatefulProxyCore(sipTransport1, null, null, false, null);

                SIPTransactionEngine transactionEngine2 = new SIPTransactionEngine();
                SIPTransport sipTransport2 = new SIPTransport(transactionEngine2, true, false);
                IPEndPoint sipTransport2EndPoint = new IPEndPoint(IPAddress.Loopback, 3001);
                sipTransport2.AddSIPChannel(sipTransport2EndPoint);
                StatefulProxyCore statefulProxyCore2 = new StatefulProxyCore(sipTransport2, null, null, false, null);
                statefulProxyCore2.GetCanonicalDomain += new GetCanonicalDomainDelegate(statefulProxyCore2_GetCanonicalDomain);
                statefulProxyCore2.GetExtensionOwner += new GetExtensionOwnerDelegate(statefulProxyCore2_GetExtensionOwner);

                sipTransport1.SIPRequestOutTraceEvent += new SIPTransportSIPRequestOutTraceDelegate(sipTransport1_SIPRequestOutTraceEvent);
                sipTransport1.SIPResponseInTraceEvent += new SIPTransportSIPResponseInTraceDelegate(sipTransport1_SIPResponseInTraceEvent);
                sipTransport2.SIPRequestInTraceEvent += new SIPTransportSIPRequestInTraceDelegate(sipTransport2_SIPRequestInTraceEvent);
                sipTransport2.SIPResponseOutTraceEvent += new SIPTransportSIPResponseOutTraceDelegate(sipTransport2_SIPResponseOutTraceEvent);
                statefulProxyCore1.StatefulProxyLogEvent += new SIPMonitorLogDelegate(statefulProxyCore1_StatefulProxyLogEvent);
                statefulProxyCore2.StatefulProxyLogEvent += new SIPMonitorLogDelegate(statefulProxyCore2_StatefulProxyLogEvent);
                statefulProxyCore2.LoadDialPlan += new LoadDialPlanDelegate(statefulProxyCore2_LoadDialPlan);

                SIPRequest inviteRequest = GetInviteRequest(sipTransport1EndPoint, null, sipTransport2EndPoint);
                UACInviteTransaction uacInvite = sipTransport1.CreateUACTransaction(inviteRequest, sipTransport2EndPoint, sipTransport1EndPoint, SIPProtocolsEnum.UDP);
                uacInvite.SendInviteRequest(sipTransport2EndPoint, inviteRequest);

                Thread.Sleep(1000);

                // Check the NUnit Console.Out to make sure there are SIP requests and responses being displayed.

                sipTransport1.Shutdown();
                sipTransport2.Shutdown();
            }

            SIPDialPlan statefulProxyCore2_LoadDialPlan(string sipAccountUsername, string sipAccountDomain)
            {
                return new SIPDialPlan(null, null, null, null, null, null, null);
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
                Console.WriteLine("StateFulProxy2-" + logEvent.EventType + ": " + logEvent.Message);
            }

            void statefulProxyCore1_StatefulProxyLogEvent(SIPMonitorEvent logEvent)
            {
                Console.WriteLine("StateFulProxy1-" + logEvent.EventType + ": " + logEvent.Message);
            }

            void sipTransport2_SIPResponseOutTraceEvent(SIPProtocolsEnum protocol, IPEndPoint localEndPoint, IPEndPoint toEndPoint, SIPResponse sipResponse)
            {
                Console.WriteLine("Response Sent (" + protocol + "): " + localEndPoint + "<-" + toEndPoint + "\r\n" + sipResponse.ToString());
            }

            void sipTransport1_SIPResponseInTraceEvent(SIPProtocolsEnum protocol, IPEndPoint localEndPoint, IPEndPoint fromEndPoint, SIPResponse sipResponse)
            {
                Console.WriteLine("Response Received (" + protocol + "): " + localEndPoint + "<-" + fromEndPoint + "\r\n" + sipResponse.ToString());
            }

            void sipTransport1_SIPRequestOutTraceEvent(SIPProtocolsEnum protocol, IPEndPoint localEndPoint, IPEndPoint toEndPoint, SIPRequest sipRequest)
            {
                Console.WriteLine("Request Sent (" + protocol + "): " + localEndPoint + "<-" + toEndPoint + "\r\n" + sipRequest.ToString());
            }

            void sipTransport2_SIPRequestInTraceEvent(SIPProtocolsEnum protocol, IPEndPoint localEndPoint, IPEndPoint fromEndPoint, SIPRequest sipRequest)
            {
                Console.WriteLine("Request Received (" + protocol + "): " + localEndPoint + "<-" + fromEndPoint + "\r\n" + sipRequest.ToString());
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
                header.Via.PushViaHeader(viaHeader);

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

                SIPViaHeader viaHeader = new SIPViaHeader(localContact.Address.ToString(), localContact.Port,CallProperties.CreateBranchId(), SIPProtocolsEnum.UDP);
                inviteRequest.Header.Via.PushViaHeader(viaHeader);

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
