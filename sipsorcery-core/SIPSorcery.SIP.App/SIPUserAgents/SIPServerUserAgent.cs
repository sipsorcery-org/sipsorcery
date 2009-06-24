//-----------------------------------------------------------------------------
// Filename: SIPServerUserAgent.cs
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
using System.Data;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.Sys;
using SIPSorcery.SIP;
using Heijden.DNS;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP.App
{
    public delegate void SIPIncomingCallDelegate(SIPServerUserAgent uas);
    public delegate void SIPIncomingCallCancelledDelegate(SIPServerUserAgent uas);

    public class SIPServerUserAgent {
        private const string THREAD_NAME = "uas-";

        private static ILog logger = AssemblyState.logger;

        private SIPMonitorLogDelegate Log_External = SIPMonitorEvent.DefaultSIPMonitorLogger;
        private SIPAuthenticateRequestDelegate SIPAuthenticateRequest_External;
        
        private SIPTransport m_sipTransport;
        private UASInviteTransaction m_uasTransaction;
        private SIPEndPoint m_outboundProxy;                   // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private SIPDialogue m_sipDialogue;
        private bool m_isAuthenticated;
        private SIPAccount m_sipAccount;

        public SIPDialogue SIPDialogue {
            get { return m_sipDialogue; }
        }

        public UASInviteTransaction SIPTransaction {
            get { return m_uasTransaction; }
        }

        public SIPAccount SIPAccount {
            get { return m_sipAccount; }
        }

        public bool IsAuthenticated {
            get { return m_isAuthenticated; }
        }

        public event SIPIncomingCallDelegate NewCall;
        public event SIPIncomingCallCancelledDelegate CallCancelled;

        public SIPServerUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPAccount sipAccount,
            SIPAuthenticateRequestDelegate sipAuthenticateRequest,
            SIPMonitorLogDelegate logDelegate) {

            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_sipAccount = sipAccount;
            SIPAuthenticateRequest_External = sipAuthenticateRequest;
            Log_External = logDelegate ?? Log_External;
        }

        public void InviteRequestReceivedAsync(SIPRequest inviteRequest, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint) {
            ThreadPool.QueueUserWorkItem(delegate { InviteRequestReceived(inviteRequest, localSIPEndPoint, remoteEndPoint); });
        }

        public void InviteRequestReceived(SIPRequest inviteRequest, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint) {
            try {
                if (Thread.CurrentThread.Name.IsNullOrBlank()) {
                    Thread.CurrentThread.Name = THREAD_NAME + DateTime.Now.ToString("HHmmss") + "-" + Crypto.GetRandomString(3);
                }

                m_uasTransaction = m_sipTransport.CreateUASTransaction(inviteRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                m_uasTransaction.TransactionTraceMessage += TransactionTraceMessage;
                m_uasTransaction.UASInviteTransactionTimedOut += ClientTimedOut;
                m_uasTransaction.UASInviteTransactionCancelled += UASTransactionCancelled;
                m_uasTransaction.NewCallReceived += NewCallReceived;

                m_uasTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, inviteRequest);
            }
            catch (Exception excp) {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.DialPlan, "Exception SIPServerUserAgent CallReceived. " + excp.Message, null));
                logger.Error("Exception SIPServerUserAgent InviteRequestReceived. " + excp.Message);
                // Don't throw here as method will typically be invoked on a separate thread and throwing an exception will crash the app.
                //throw;
            }
        }

        public void SetRinging() {
            SIPResponse ringingResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, SIPResponseStatusCodesEnum.Ringing, null);
            m_uasTransaction.SendInformationalResponse(ringingResponse);
        }

        public void Answer(string owner, string adminMemberId, string contentType, string body) {
            SIPResponse okResponse = m_uasTransaction.GetOkResponse(m_uasTransaction.TransactionRequest, m_uasTransaction.TransactionRequest.LocalSIPEndPoint, contentType, body);

            if (body != null) {
                okResponse.Header.ContentType = contentType;
                okResponse.Header.ContentLength = body.Length;
                okResponse.Body = body;
            }

            m_uasTransaction.SendFinalResponse(okResponse);
            m_sipDialogue = new SIPDialogue(m_sipTransport, m_uasTransaction, owner, adminMemberId);
        }

        public void Reject(SIPResponseStatusCodesEnum rejectCode, string rejectReason) {
            SIPResponse rejectResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, rejectCode, rejectReason);
            m_uasTransaction.SendFinalResponse(rejectResponse);
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI) {
            SIPResponse redirectResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, redirectCode, null);
            redirectResponse.Header.Contact = SIPContactHeader.CreateSIPContactList(redirectURI);
            m_uasTransaction.SendFinalResponse(redirectResponse);
        }

        private void NewCallReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest) {
            try {

                UASInviteTransaction uasInviteTransaction = (UASInviteTransaction)sipTransaction;

                if (m_sipAccount == null) {
                    SIPResponse notFoundResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, null);
                    sipTransaction.SendFinalResponse(notFoundResponse);
                }
                else if (SIPAuthenticateRequest_External == null) {
                    // No authentication has been asked for pass the call straight through.
                    if (NewCall != null) {
                        NewCall(this);
                    }
                }
                else {
                    SIPRequestAuthenticationResult authenticationResult = SIPAuthenticateRequest_External(localSIPEndPoint, remoteEndPoint, sipRequest, m_sipAccount, Log_External);
                    if (authenticationResult.Authenticated) {
                        SIPEndPoint remoteUAEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : remoteEndPoint;
                        if (authenticationResult.WasAuthenticatedByIP) {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "New call from " + remoteUAEndPoint.ToString() + " successfully authenticated by IP address.", m_sipAccount.Owner));
                        }
                        else {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "New call from " + remoteUAEndPoint.ToString() + " successfully authenticated by digest.", m_sipAccount.Owner));
                        }

                        m_isAuthenticated = true;
                        if (NewCall != null) {
                            NewCall(this);
                        }
                        else {
                            SIPResponse notFoundResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, null);
                            sipTransaction.SendFinalResponse(notFoundResponse);
                        }
                    }
                    else {
                        m_isAuthenticated = false;
                        // Send authorisation failure or required response
                        SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                        authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                        uasInviteTransaction.SendFinalResponse(authReqdResponse);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPServerUserAgent NewCallReceived. " + excp.Message);
                SIPResponse errorResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                sipTransaction.SendFinalResponse(errorResponse);
            }
        }

        private void UASTransactionCancelled(SIPTransaction sipTransaction) {
            if (CallCancelled != null) {
                CallCancelled(this);
            }
        }

        private void ClientTimedOut(SIPTransaction sipTransaction) {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.DialPlan, "Timed out waiting for client ACK.", null));
        }

        public void Hangup() {
            m_sipDialogue.Hangup();
        }

        public void Hungup(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Call hangup request from server at " + remoteEndPoint + ".", null));
            SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
            byeTransaction.TransactionTraceMessage += TransactionTraceMessage;
            SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
            byeTransaction.SendFinalResponse(byeResponse);
        }

        private void ByeFinalResponseReceived(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "BYE response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".", null));
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message) {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.SIPTransaction, message, null));
        }
    }
}
