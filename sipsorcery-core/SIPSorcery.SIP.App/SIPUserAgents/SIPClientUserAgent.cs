//-----------------------------------------------------------------------------
// Filename: SIPClientUserAgent.cs
//
// Description: Implementation of a SIP Client User Agent that can be used to initiate SIP calls.
// 
// History:
// 22 Feb 2008	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorcery.SIP;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    public class SIPClientUserAgent : ISIPClientUserAgent
    {
        private const int DNS_LOOKUP_TIMEOUT = 5000;
        private const char OUTBOUNDPROXY_AS_ROUTESET_CHAR = '<';    // If this character exists in the call descriptor OutboundProxy setting it gets treated as a Route set.

        private static ILog logger = AssemblyState.logger;

        private static string m_userAgent = SIPConstants.SIP_USERAGENT_STRING;
        private static readonly int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static readonly string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;
        //private static string m_transportParam = SIPHeaderAncillary.SIP_HEADERANC_TRANSPORT;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_localSIPEndPoint;
        private SIPMonitorLogDelegate Log_External;

        public string Owner { get; private set; }                   // If the UAC is authenticated holds the username of the client.
        public string AdminMemberId { get; private set; }          // If the UAC is authenticated holds the username of the client.

        private SIPCallDescriptor m_sipCallDescriptor;              // Describes the server leg of the call from the sipswitch.
        private SIPEndPoint m_serverEndPoint;
        private UACInviteTransaction m_serverTransaction;
        private bool m_callCancelled;                               // It's possible for the call to be cancelled before the INVITE has been sent. This could occur if a DNS lookup on the server takes a while.
        private bool m_hungupOnCancel;                              // Set to true if a call has been cancelled AND and then an Ok response was received AND a BYE has been sent to hang it up. This variable is used to stop another BYE transaction being generated.
        private int m_serverAuthAttempts;                           // Used to determine if credentials for a server leg call fail.
        private SIPNonInviteTransaction m_cancelTransaction;        // If the server call is cancelled this transaction contains the CANCEL in case it needs to be resent.
        private SIPEndPoint m_outboundProxy;                        // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private SIPDialogue m_sipDialogue;

        public event SIPCallResponseDelegate CallTrying;
        public event SIPCallResponseDelegate CallRinging;
        public event SIPCallResponseDelegate CallAnswered;
        public event SIPCallFailedDelegate CallFailed;

        public UACInviteTransaction ServerTransaction
        {
            get { return m_serverTransaction; }
        }

        public bool IsUACAnswered
        {
            get { return m_serverTransaction.TransactionFinalResponse != null; }
        }

        public SIPDialogue SIPDialogue
        {
            get { return m_sipDialogue; }
        }

        public SIPCallDescriptor CallDescriptor
        {
            get { return m_sipCallDescriptor; }
        }

        public SIPClientUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            string owner,
            string adminMemberId,
            SIPMonitorLogDelegate logDelegate)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = (outboundProxy != null) ? SIPEndPoint.ParseSIPEndPoint(outboundProxy.ToString()) : null;
            Owner = owner;
            AdminMemberId = adminMemberId;
            Log_External = logDelegate;

            // If external logging is not required assign an empty handler to stop null reference exceptions.
            if (Log_External == null)
            {
                Log_External = (e) => { };
            }
        }

        public void Call(SIPCallDescriptor sipCallDescriptor)
        {
            try
            {
                m_sipCallDescriptor = sipCallDescriptor;
                SIPURI callURI = SIPURI.ParseSIPURI(sipCallDescriptor.Uri);
                SIPRouteSet routeSet = null;

                if (!m_callCancelled)
                {

                    if (!sipCallDescriptor.ProxySendFrom.IsNullOrBlank())
                    {
                        // If the call descriptor needs to use a specific outbound proxy it needs to override the default one.
                        SIPEndPoint outboundProxyEndPoint = SIPEndPoint.ParseSIPEndPoint(sipCallDescriptor.ProxySendFrom);
                        m_outboundProxy = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(outboundProxyEndPoint.SocketEndPoint.Address, m_defaultSIPPort));
                        m_serverEndPoint = m_outboundProxy;
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "SIPClientUserAgent Call using alternate outbound proxy of " + m_outboundProxy + ".", Owner));
                    }
                    else if (m_outboundProxy != null)
                    {
                        // Using the system outbound proxy only, no additional user routing requirements.
                        m_serverEndPoint = m_outboundProxy;
                    }

                    // A custom route set may have been specified for the call.
                    if (m_sipCallDescriptor.RouteSet != null && m_sipCallDescriptor.RouteSet.IndexOf(OUTBOUNDPROXY_AS_ROUTESET_CHAR) != -1)
                    {
                        try
                        {
                            routeSet = new SIPRouteSet();
                            routeSet.PushRoute(new SIPRoute(m_sipCallDescriptor.RouteSet, true));
                        }
                        catch
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Error an outbound proxy value was not recognised in SIPClientUserAgent Call. " + m_sipCallDescriptor.RouteSet + ".", Owner));
                        }
                    }

                    // No outbound proxy, determine the forward destination based on the SIP request.
                    if (m_serverEndPoint == null)
                    {
                        if (routeSet == null || routeSet.Length == 0)
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Attempting to resolve " + callURI.Host + ".", Owner));
                            m_serverEndPoint = m_sipTransport.GetURIEndPoint(callURI, true);
                        }
                        else
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Route set for call " + routeSet.ToString() + ".", Owner));
                            m_serverEndPoint = m_sipTransport.GetURIEndPoint(routeSet.TopRoute.URI, true);
                        }
                    }

                    if (m_serverEndPoint != null)
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Switching to " + SIPURI.ParseSIPURI(m_sipCallDescriptor.Uri).CanonicalAddress + " via " + m_serverEndPoint + ".", Owner));

                        m_localSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint(m_serverEndPoint);
                        if (m_localSIPEndPoint == null)
                        {
                            throw new ApplicationException("The call could not locate an appropriate SIP transport channel for protocol " + callURI.Protocol + ".");
                        }

                        string content = sipCallDescriptor.Content;

                        if (content.IsNullOrBlank())
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Body on UAC call was empty.", Owner));
                        }
                        else if (m_sipCallDescriptor.ContentType == m_sdpContentType)
                        {
                            if (!m_sipCallDescriptor.MangleResponseSDP)
                            {
                                IPEndPoint sdpEndPoint = SDP.GetSDPRTPEndPoint(content);
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC call was set to NOT mangle, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                            }
                            else
                            {
                                IPEndPoint sdpEndPoint = SDP.GetSDPRTPEndPoint(content);
                                if (sdpEndPoint != null)
                                {
                                    if (!SIPTransport.IsPrivateAddress(sdpEndPoint.Address.ToString()))
                                    {
                                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC call had public IP not mangled, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                                    }
                                    else
                                    {
                                        bool wasSDPMangled = false;
                                        if (sipCallDescriptor.MangleIPAddress != null)
                                        {
                                            if (sdpEndPoint != null)
                                            {
                                                content = SIPPacketMangler.MangleSDP(content, sipCallDescriptor.MangleIPAddress.ToString(), out wasSDPMangled);
                                            }
                                        }

                                        if (wasSDPMangled)
                                        {
                                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC call had RTP socket mangled from " + sdpEndPoint.ToString() + " to " + sipCallDescriptor.MangleIPAddress.ToString() + ":" + sdpEndPoint.Port + ".", Owner));
                                        }
                                        else if (sdpEndPoint != null)
                                        {
                                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC could not be mangled, using original RTP socket of " + sdpEndPoint.ToString() + ".", Owner));
                                        }
                                    }
                                }
                                else
                                {
                                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP RTP socket on UAC call could not be determined.", Owner));
                                }
                            }
                        }

                        SIPRequest switchServerInvite = GetInviteRequest(m_sipCallDescriptor, CallProperties.CreateBranchId(), CallProperties.CreateNewCallId(), m_localSIPEndPoint, routeSet, content, sipCallDescriptor.ContentType);

                        // Now that we have a destination socket create a new UAC transaction for forwarded leg of the call.
                        m_serverTransaction = m_sipTransport.CreateUACTransaction(switchServerInvite, m_serverEndPoint, m_localSIPEndPoint, m_outboundProxy);
                        if (m_serverTransaction.CDR != null)
                        {
                            m_serverTransaction.CDR.Owner = Owner;
                        }

                        m_serverTransaction.UACInviteTransactionInformationResponseReceived += ServerInformationResponseReceived;
                        m_serverTransaction.UACInviteTransactionFinalResponseReceived += ServerFinalResponseReceived;
                        m_serverTransaction.UACInviteTransactionTimedOut += ServerTimedOut;
                        m_serverTransaction.TransactionTraceMessage += TransactionTraceMessage;

                        m_serverTransaction.SendInviteRequest(m_serverEndPoint, m_serverTransaction.TransactionRequest);
                    }
                    else
                    {
                        if (routeSet == null || routeSet.Length == 0)
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Forward leg failed, could not resolve URI host " + callURI.Host, Owner));
                            FireCallFailed(this, "unresolvable destination " + callURI.Host);
                        }
                        else
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Forward leg failed, could not resolve top Route host " + routeSet.TopRoute.Host, Owner));
                            FireCallFailed(this, "unresolvable destination " + routeSet.TopRoute.Host);
                        }
                    }
                }
            }
            catch (ApplicationException appExcp)
            {
                FireCallFailed(this, appExcp.Message);
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Exception UserAgentClient Call. " + excp.Message, Owner));
                FireCallFailed(this, excp.Message);
            }
        }

        public void Cancel()
        {
            try
            {
                m_callCancelled = true;

                // Cancel server call.
                if (m_serverTransaction == null)
                {
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Cancelling forwarded call leg " + m_sipCallDescriptor.Uri.ToString() + ", server transaction has not been created yet no CANCEL request required.", Owner));
                }
                else if (m_cancelTransaction != null)
                {
                    if (m_cancelTransaction.TransactionState != SIPTransactionStatesEnum.Completed)
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Call " + m_serverTransaction.TransactionRequest.URI.ToString() + " has already been cancelled once, trying again.", Owner));
                        m_cancelTransaction.SendRequest(m_cancelTransaction.TransactionRequest);
                    }
                    else
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Call " + m_serverTransaction.TransactionRequest.URI.ToString() + " has already responded to CANCEL, probably overlap in messages not re-sending.", Owner));
                    }
                }
                else //if (m_serverTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding || m_serverTransaction.TransactionState == SIPTransactionStatesEnum.Trying)
                {
                    //logger.Debug("Cancelling forwarded call leg, sending CANCEL to " + ForwardedTransaction.TransactionRequest.URI.ToString() + " (transid: " + ForwardedTransaction.TransactionId + ").");
                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Cancelling forwarded call leg, sending CANCEL to " + m_serverTransaction.TransactionRequest.URI.ToString() + ".", Owner));

                    // No reponse has been received from the server so no CANCEL request neccessary, stop any retransmits of the INVITE.
                    m_serverTransaction.CancelCall();

                    SIPRequest cancelRequest = GetCancelRequest(m_serverTransaction.TransactionRequest);
                    m_cancelTransaction = m_sipTransport.CreateNonInviteTransaction(cancelRequest, m_serverEndPoint, m_serverTransaction.LocalSIPEndPoint, m_outboundProxy);
                    m_cancelTransaction.TransactionTraceMessage += TransactionTraceMessage;
                    m_cancelTransaction.SendRequest(m_serverEndPoint, cancelRequest);
                }
                //else
                //{
                // No reponse has been received from the server so no CANCEL request neccessary, stop any retransmits of the INVITE.
                //    m_serverTransaction.CancelCall();
                //    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Cancelling forwarded call leg " + m_sipCallDescriptor.Uri.ToString() + ", no response from server has been received so no CANCEL request required.", Owner));
                //}

                FireCallFailed(this, "Call cancelled by user.");
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Exception CancelServerCall. " + excp.Message, Owner));
            }
        }

        private void ServerFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                //if (Thread.CurrentThread.Name.IsNullOrBlank())
                //{
                //    Thread.CurrentThread.Name = THREAD_NAME + DateTime.Now.ToString("HHmmss") + "-" + Crypto.GetRandomString(3);
                //}

                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + m_serverTransaction.TransactionRequest.URI.ToString() + ".", Owner));
                //m_sipTrace += "Received " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " " + localEndPoint + "<-" + remoteEndPoint + "\r\n" + sipResponse.ToString();

                m_serverTransaction.UACInviteTransactionInformationResponseReceived -= ServerInformationResponseReceived;
                m_serverTransaction.UACInviteTransactionFinalResponseReceived -= ServerFinalResponseReceived;
                m_serverTransaction.TransactionTraceMessage -= TransactionTraceMessage;

                if (m_callCancelled && sipResponse.Status == SIPResponseStatusCodesEnum.RequestTerminated)
                {
                    // No action required. Correctly received request terminated on an INVITE we cancelled.
                }
                else if (m_callCancelled)
                {

                    #region Call has been cancelled, hangup.

                    if (m_hungupOnCancel)
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "A cancelled call to " + m_sipCallDescriptor.Uri.ToString() + " has been answered AND has already been hungup, no further action being taken.", Owner));
                    }
                    else
                    {
                        m_hungupOnCancel = true;

                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "A cancelled call to " + m_sipCallDescriptor.Uri.ToString() + " has been answered, hanging up.", Owner));

                        if (sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0)
                        {
                            SIPURI byeURI = sipResponse.Header.Contact[0].ContactURI;
                            SIPRequest byeRequest = GetByeRequest(sipResponse, byeURI, localSIPEndPoint);

                            SIPEndPoint byeEndPoint = m_sipTransport.GetRequestEndPoint(byeRequest, m_outboundProxy, true);

                            if (byeEndPoint != null)
                            {
                                SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(byeRequest, byeEndPoint, localSIPEndPoint, m_outboundProxy);
                                byeTransaction.SendReliableRequest();
                            }
                            else
                            {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Could not end BYE on cancelled call as request end point could not be determined " + byeRequest.URI.ToString(), Owner));
                            }
                        }
                        else
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "No contact header provided on response for cancelled call to " + m_sipCallDescriptor.Uri.ToString() + " no further action.", Owner));
                        }
                    }

                    #endregion
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    //logger.Debug("AuthReqd Final response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + m_serverTransaction.TransactionRequest.URI.ToString() + ".");

                    #region Authenticate client call to third party server.

                    if (!m_callCancelled)
                    {
                        if (m_sipCallDescriptor.Password == null || m_sipCallDescriptor.Password.Trim().Length == 0)
                        {
                            // No point trying to authenticate if there is no password to use.
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Forward leg failed, authentication was requested but no credentials were available.", Owner));
                            FireCallFailed(this, "Authentication requested when no credentials available");
                        }
                        else if (m_serverAuthAttempts == 0)
                        {
                            m_serverAuthAttempts = 1;

                            // Resend INVITE with credentials.
                            string username = (m_sipCallDescriptor.AuthUsername != null && m_sipCallDescriptor.AuthUsername.Trim().Length > 0) ? m_sipCallDescriptor.AuthUsername : m_sipCallDescriptor.Username;
                            SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                            authRequest.SetCredentials(username, m_sipCallDescriptor.Password, m_sipCallDescriptor.Uri.ToString(), SIPMethodsEnum.INVITE.ToString());

                            SIPRequest authInviteRequest = m_serverTransaction.TransactionRequest;

                            if (SIPProviderMagicJack.IsMagicJackRequest(sipResponse))
                            {
                                authInviteRequest.Header.AuthenticationHeader = SIPProviderMagicJack.GetAuthenticationHeader(sipResponse);
                            }
                            else
                            {
                                authInviteRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
                                authInviteRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;
                            }

                            authInviteRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                            authInviteRequest.Header.CSeq = authInviteRequest.Header.CSeq + 1;

                            // Create a new UAC transaction to establish the authenticated server call.
                            m_serverTransaction = m_sipTransport.CreateUACTransaction(authInviteRequest, m_serverEndPoint, localSIPEndPoint, m_outboundProxy);
                            if (m_serverTransaction.CDR != null)
                            {
                                m_serverTransaction.CDR.Owner = Owner;
                            }
                            m_serverTransaction.UACInviteTransactionInformationResponseReceived += ServerInformationResponseReceived;
                            m_serverTransaction.UACInviteTransactionFinalResponseReceived += ServerFinalResponseReceived;
                            m_serverTransaction.UACInviteTransactionTimedOut += ServerTimedOut;
                            m_serverTransaction.TransactionTraceMessage += TransactionTraceMessage;

                            //logger.Debug("Sending authenticated switchcall INVITE to " + ForwardedCallStruct.Host + ".");
                            m_serverTransaction.SendInviteRequest(m_serverEndPoint, authInviteRequest);
                            //m_sipTrace += "Sending " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + " " + localEndPoint + "->" + ForwardedTransaction.TransactionRequest.GetRequestEndPoint() + "\r\n" + ForwardedTransaction.TransactionRequest.ToString();
                        }
                        else
                        {
                            //logger.Debug("Authentication of client call to switch server failed.");
                            FireCallFailed(this, "Authentication with provided credentials failed");
                        }
                    }

                    #endregion
                }
                else
                {
                    if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
                    {
                        if (sipResponse.Body.IsNullOrBlank())
                        {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Body on UAC response was empty.", Owner));
                        }
                        else if (m_sipCallDescriptor.ContentType == m_sdpContentType)
                        {
                            if (!m_sipCallDescriptor.MangleResponseSDP)
                            {
                                IPEndPoint sdpEndPoint = SDP.GetSDPRTPEndPoint(sipResponse.Body);
                                string sdpSocket = (sdpEndPoint != null) ? sdpEndPoint.ToString() : "could not determine";
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC response was set to NOT mangle, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                            }
                            else
                            {
                                //m_callInProgress = false; // the call is now established
                                //logger.Debug("Final response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + ForwardedTransaction.TransactionRequest.URI.ToString() + ".");
                                // Determine of response SDP should be mangled.

                                IPEndPoint sdpEndPoint = SDP.GetSDPRTPEndPoint(sipResponse.Body);
                                //Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "UAC response SDP was mangled from sdp=" + sdpEndPoint.Address.ToString() + ", proxyfrom=" + sipResponse.Header.ProxyReceivedFrom + ", mangle=" + m_sipCallDescriptor.MangleResponseSDP + ".", null));
                                if (sdpEndPoint != null)
                                {
                                    if (!SIPTransport.IsPrivateAddress(sdpEndPoint.Address.ToString()))
                                    {
                                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC response had public IP not mangled, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                                    }
                                    else
                                    {
                                        bool wasSDPMangled = false;
                                        string publicIPAddress = null;

                                        if (!sipResponse.Header.ProxyReceivedFrom.IsNullOrBlank())
                                        {
                                            IPAddress remoteUASAddress = SIPEndPoint.ParseSIPEndPoint(sipResponse.Header.ProxyReceivedFrom).SocketEndPoint.Address;
                                            if (SIPTransport.IsPrivateAddress(remoteUASAddress.ToString()) && m_sipCallDescriptor.MangleIPAddress != null)
                                            {
                                                // If the response has arrived here on a private IP address then it must be
                                                // for an local version install and an incoming call that needs it's response mangled.
                                                publicIPAddress = m_sipCallDescriptor.MangleIPAddress.ToString();
                                            }
                                            else
                                            {
                                                publicIPAddress = remoteUASAddress.ToString();
                                            }
                                        }
                                        else if (!SIPTransport.IsPrivateAddress(remoteEndPoint.SocketEndPoint.Address.ToString()))
                                        {
                                            publicIPAddress = remoteEndPoint.SocketEndPoint.Address.ToString();
                                        }
                                        else if (m_sipCallDescriptor.MangleIPAddress != null)
                                        {
                                            publicIPAddress = m_sipCallDescriptor.MangleIPAddress.ToString();
                                        }

                                        if (publicIPAddress != null)
                                        {
                                            sipResponse.Body = SIPPacketMangler.MangleSDP(sipResponse.Body, publicIPAddress, out wasSDPMangled);
                                        }

                                        if (wasSDPMangled)
                                        {
                                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC response had RTP socket mangled from " + sdpEndPoint.ToString() + " to " + publicIPAddress + ":" + sdpEndPoint.Port + ".", Owner));
                                        }
                                        else if (sdpEndPoint != null)
                                        {
                                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC response could not be mangled, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                                        }
                                    }
                                }
                                else
                                {
                                    Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP RTP socket on UAC response could not be determined.", Owner));
                                }
                            }
                        }

                        m_sipDialogue = new SIPDialogue(m_serverTransaction, Owner, AdminMemberId);
                        m_sipDialogue.CallDurationLimit = m_sipCallDescriptor.CallDurationLimit;
                    }

                    FireCallAnswered(this, sipResponse);
                }
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.Error, "Exception ServerFinalResponseReceived. " + excp.Message, Owner));
            }
        }

        private void ServerInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Information response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + m_serverTransaction.TransactionRequest.URI.ToString() + ".", Owner));

            if (m_callCancelled)
            {
                // Call was cancelled in the interim.
                Cancel();
            }
            else
            {
                if (sipResponse.Status == SIPResponseStatusCodesEnum.Ringing || sipResponse.Status == SIPResponseStatusCodesEnum.SessionProgress)
                {
                    FireCallRinging(this, sipResponse);
                }
                else
                {
                    FireCallTrying(this, sipResponse);
                }
            }
        }

        private void ServerTimedOut(SIPTransaction sipTransaction)
        {
            FireCallFailed(this, "Timeout, no response from server");
        }

        public void Hangup()
        {
            m_sipDialogue.Hangup(m_sipTransport, m_outboundProxy);
        }

        public void Hungup(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Call hangup request from server at " + remoteEndPoint + ".", Owner));
            SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
            byeTransaction.TransactionTraceMessage += TransactionTraceMessage;
            SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
            byeTransaction.SendFinalResponse(byeResponse);
        }

        private void ByeFinalResponseReceived(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "BYE response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".", Owner));
        }

        private SIPRequest GetInviteRequest(SIPCallDescriptor sipCallDescriptor, string branchId, string callId, SIPEndPoint localSIPEndPoint, SIPRouteSet routeSet, string content, string contentType)
        {
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, sipCallDescriptor.Uri);
            inviteRequest.LocalSIPEndPoint = localSIPEndPoint;

            SIPHeader inviteHeader = new SIPHeader(sipCallDescriptor.GetFromHeader(), SIPToHeader.ParseToHeader(sipCallDescriptor.To), 1, callId);

            inviteHeader.From.FromTag = CallProperties.CreateNewTag();

            // For incoming calls forwarded via the dial plan the username needs to go into the Contact header.
            inviteHeader.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(inviteRequest.URI.Scheme, localSIPEndPoint)) };
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteHeader.UserAgent = m_userAgent;
            inviteHeader.Routes = routeSet;
            inviteRequest.Header = inviteHeader;

            if (!sipCallDescriptor.ProxySendFrom.IsNullOrBlank())
            {
                inviteHeader.ProxySendFrom = sipCallDescriptor.ProxySendFrom;
            }

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, branchId);
            inviteRequest.Header.Vias.PushViaHeader(viaHeader);

            inviteRequest.Body = content;
            inviteRequest.Header.ContentLength = (inviteRequest.Body != null) ? inviteRequest.Body.Length : 0;
            inviteRequest.Header.ContentType = contentType;

            try
            {
                if (sipCallDescriptor.CustomHeaders != null && sipCallDescriptor.CustomHeaders.Count > 0)
                {
                    foreach (string customHeader in sipCallDescriptor.CustomHeaders)
                    {
                        //string headerName = customHeader.Key as string;
                        //string headerValue = customHeader.Value as string;

                        if (customHeader.IsNullOrBlank())
                        {
                            continue;
                        }
                        else if (customHeader.Trim().StartsWith(SIPHeaders.SIP_HEADER_USERAGENT))
                        {
                            inviteRequest.Header.UserAgent = customHeader.Substring(customHeader.IndexOf(":") + 1).Trim();
                        }
                        else
                        {
                            inviteRequest.Header.UnknownHeaders.Add(customHeader);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception Parsing CustomHeader for GetInviteRequest. " + excp.Message + sipCallDescriptor.CustomHeaders);
            }

            return inviteRequest;
        }

        private SIPRequest GetCancelRequest(SIPRequest inviteRequest)
        {
            SIPRequest cancelRequest = new SIPRequest(SIPMethodsEnum.CANCEL, inviteRequest.URI);
            cancelRequest.LocalSIPEndPoint = inviteRequest.LocalSIPEndPoint;

            SIPHeader inviteHeader = inviteRequest.Header;
            SIPHeader cancelHeader = new SIPHeader(inviteHeader.From, inviteHeader.To, inviteHeader.CSeq, inviteHeader.CallId);
            cancelRequest.Header = cancelHeader;
            cancelHeader.CSeqMethod = SIPMethodsEnum.CANCEL;
            cancelHeader.Routes = inviteHeader.Routes;
            cancelHeader.ProxySendFrom = inviteHeader.ProxySendFrom;
            cancelHeader.Vias = inviteHeader.Vias;

            return cancelRequest;
        }

        private SIPRequest GetByeRequest(SIPResponse inviteResponse, SIPURI byeURI, SIPEndPoint localSIPEndPoint)
        {
            SIPRequest byeRequest = new SIPRequest(SIPMethodsEnum.BYE, byeURI);
            byeRequest.LocalSIPEndPoint = localSIPEndPoint;

            SIPFromHeader byeFromHeader = inviteResponse.Header.From;
            SIPToHeader byeToHeader = inviteResponse.Header.To;
            int cseq = inviteResponse.Header.CSeq + 1;

            SIPHeader byeHeader = new SIPHeader(byeFromHeader, byeToHeader, cseq, inviteResponse.Header.CallId);
            byeHeader.CSeqMethod = SIPMethodsEnum.BYE;
            byeHeader.ProxySendFrom = m_serverTransaction.TransactionRequest.Header.ProxySendFrom; ;
            byeRequest.Header = byeHeader;

            byeRequest.Header.Routes = (inviteResponse.Header.RecordRoutes != null) ? inviteResponse.Header.RecordRoutes.Reversed() : null;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
            byeRequest.Header.Vias.PushViaHeader(viaHeader);

            return byeRequest;
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.SIPTransaction, message, Owner));
        }

        private void FireCallTrying(SIPClientUserAgent uac, SIPResponse tryingResponse)
        {
            if (CallTrying != null)
            {
                CallTrying(uac, tryingResponse);
            }
        }

        private void FireCallRinging(SIPClientUserAgent uac, SIPResponse ringingResponse)
        {
            if (CallRinging != null)
            {
                CallRinging(uac, ringingResponse);
            }
        }

        private void FireCallAnswered(SIPClientUserAgent uac, SIPResponse answeredResponse)
        {
            if (CallAnswered != null)
            {
                CallAnswered(uac, answeredResponse);
            }
        }

        private void FireCallFailed(SIPClientUserAgent uac, string errorMessage)
        {
            if (CallFailed != null)
            {
                CallFailed(uac, errorMessage);
            }
        }
    }
}
