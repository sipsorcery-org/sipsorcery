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

    public class SIPServerUserAgent
    {
        private const string THREAD_NAME = "uas-";

        private static ILog logger = AssemblyState.logger;

        private SIPMonitorLogDelegate Log_External = SIPMonitorEvent.DefaultSIPMonitorLogger;
        private SIPAuthoriseRequestDelegate SIPAuthoriseRequest_External;

        private SIPTransport m_sipTransport;  
        private UASInviteTransaction m_uasTransaction;
        private SIPEndPoint m_outboundProxy;                   // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private SIPDialogue m_sipDialogue;
        private string m_authorisedSIPUsername;
        private string m_authorisedSIPDomain;

        public SIPDialogue SIPDialogue
        {
            get { return m_sipDialogue; }
        }

        public UASInviteTransaction SIPTransaction
        {
            get { return m_uasTransaction; }
        }

        public string AuthorisedSIPUsername
        {
            get { return m_authorisedSIPUsername; }
        }

        public string AuthorisedSIPDomain
        {
            get { return m_authorisedSIPDomain; }
        }

        public event SIPIncomingCallDelegate NewCall;
        public event SIPIncomingCallCancelledDelegate CallCancelled;

        public SIPServerUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPAuthoriseRequestDelegate sipAuthoriseRequest,
            SIPMonitorLogDelegate logDelegate)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            SIPAuthoriseRequest_External = sipAuthoriseRequest;
            Log_External = logDelegate ?? Log_External; 
        }

        public void InviteRequestReceivedAsync(SIPRequest inviteRequest, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint)
        {
            ThreadPool.QueueUserWorkItem(delegate { InviteRequestReceived(inviteRequest, localSIPEndPoint, remoteEndPoint); });
        }

        public void InviteRequestReceived(SIPRequest inviteRequest, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint)
        {
            try
            {
                if (Thread.CurrentThread.Name.IsNullOrBlank())
                {
                    Thread.CurrentThread.Name = THREAD_NAME + DateTime.Now.ToString("HHmmss") + "-" + Crypto.GetRandomString(3);
                }
                
                m_uasTransaction = m_sipTransport.CreateUASTransaction(inviteRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                m_uasTransaction.TransactionTraceMessage += TransactionTraceMessage;
                m_uasTransaction.UASInviteTransactionTimedOut += ClientTimedOut;
                m_uasTransaction.UASInviteTransactionCancelled += UASTransactionCancelled;
                m_uasTransaction.UASSIPRequestAuthenticate = SIPAuthoriseRequest_External;
                m_uasTransaction.NewCallReceived += NewCallReceived;

                m_uasTransaction.GotRequest(localSIPEndPoint, remoteEndPoint, inviteRequest);
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.DialPlan, "Exception SIPServerUserAgent CallReceived. " + excp.Message, null));
                // Don't throw here as method will typically be invoked on a separate thread and throwing an exception will crash the app.
                //throw;
            }
        }

        public void SetRinging()
        {
            SIPResponse ringingResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, SIPResponseStatusCodesEnum.Ringing, null);
            m_uasTransaction.SendInformationalResponse(ringingResponse);
        }

        public void Answer(string owner, string adminMemberId, string contentType, string body)
        {
            SIPResponse okResponse = m_uasTransaction.GetOkResponse(m_uasTransaction.TransactionRequest, m_uasTransaction.TransactionRequest.LocalSIPEndPoint, contentType, body);

            if (body != null)
            {
                okResponse.Header.ContentType = contentType;
                okResponse.Header.ContentLength = body.Length;
                okResponse.Body = body;
            }

            m_uasTransaction.SendFinalResponse(okResponse);
            m_sipDialogue = new SIPDialogue(m_sipTransport, m_uasTransaction, owner, adminMemberId);
        }

        public void Reject(SIPResponseStatusCodesEnum rejectCode, string rejectReason)
        {
            SIPResponse rejectResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, rejectCode, rejectReason);
            m_uasTransaction.SendFinalResponse(rejectResponse);
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI)
        {
            SIPResponse redirectResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, redirectCode, null);
            redirectResponse.Header.Contact = SIPContactHeader.CreateSIPContactList(redirectURI);
            m_uasTransaction.SendFinalResponse(redirectResponse);
        }

        private void NewCallReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            UASInviteTransaction uasInviteTransaction = (UASInviteTransaction)sipTransaction;
            if (uasInviteTransaction.AuthorisationResult != null && uasInviteTransaction.AuthorisationResult.Authorised)
            {
                m_authorisedSIPUsername = uasInviteTransaction.AuthorisationResult.SIPUsername;
                m_authorisedSIPDomain = uasInviteTransaction.AuthorisationResult.SIPDomain;
            }

            if (NewCall != null)
            {
                NewCall(this);
            }
            else
            {
                SIPResponse notFoundResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, null);
                sipTransaction.SendFinalResponse(notFoundResponse);
            }
        }

        private void UASTransactionCancelled(SIPTransaction sipTransaction)
        {
            if (CallCancelled != null)
            {
                CallCancelled(this);
            }
        }

        private void ClientTimedOut(SIPTransaction sipTransaction)
        {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.DialPlan, "Timed out waiting for client ACK.", null));
        }

        public void Hangup()
        {
            m_sipDialogue.Hangup();
            
           /*IPEndPoint byeEndPoint = byeRequest.GetRequestEndPoint();

            if (byeEndPoint != null)
            {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Hanging up call, sending BYE to " + byeEndPoint + ".", Owner));
                SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(byeRequest, byeEndPoint, m_localSIPEndPoint.SocketEndPoint, byeRequest.URI.Protocol);
                byeTransaction.NonInviteTransactionFinalResponseReceived += ByeFinalResponseReceived;
                byeTransaction.TransactionTraceMessage += TransactionTraceMessage;
                byeTransaction.SendReliableRequest();
            }
            else
            {
                string host = (byeRequest.Header.Routes != null && byeRequest.Header.Routes.Length > 0) ? byeRequest.Header.Routes.TopRoute.URI.Host : byeRequest.URI.Host;
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Could not hangup call as BYE request end point could not be resolved " + host + ".", Owner));
            }*/
        }

        public void Hungup(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Call hangup request from server at " + remoteEndPoint + ".", null));
            SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
            byeTransaction.TransactionTraceMessage += TransactionTraceMessage;
            SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
            byeTransaction.SendFinalResponse(byeResponse);
        }

        private void ByeFinalResponseReceived(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "BYE response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".", null));
        }

        private SIPRequest GetByeRequest(SIPResponse inviteResponse, SIPURI byeURI, IPEndPoint localEndPoint)
        {
            SIPRequest byeRequest = new SIPRequest(SIPMethodsEnum.BYE, byeURI);
            byeRequest.LocalSIPEndPoint = inviteResponse.LocalSIPEndPoint;

            SIPFromHeader byeFromHeader = inviteResponse.Header.From;
            SIPToHeader byeToHeader = inviteResponse.Header.To;
            int cseq = inviteResponse.Header.CSeq + 1;

            SIPHeader byeHeader = new SIPHeader(byeFromHeader, byeToHeader, cseq, inviteResponse.Header.CallId);
            byeHeader.CSeqMethod = SIPMethodsEnum.BYE;
            byeRequest.Header = byeHeader;

            byeRequest.Header.Routes = (inviteResponse.Header.RecordRoutes != null) ? inviteResponse.Header.RecordRoutes.Reversed() : null;

            SIPViaHeader viaHeader = new SIPViaHeader(localEndPoint.Address.ToString(), localEndPoint.Port, CallProperties.CreateBranchId());
            byeRequest.Header.Vias.PushViaHeader(viaHeader);

            return byeRequest;
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.SIPTransaction, message, null));
        }
    }
}
