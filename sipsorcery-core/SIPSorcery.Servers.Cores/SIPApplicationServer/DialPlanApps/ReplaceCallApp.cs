//-----------------------------------------------------------------------------
// Filename: ReplaceCallApp.cs
//
// Description: Accepts an in-dialogue SIP request, such as MESSAGE, and initiates
// a new call. If the new call is answered the established call has the remote end
// hungup (the end the MESSAGE came from) and the local end is re-INVITEd into the 
// new call.
// 
// History:
// 07 Aug 2008	    Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2007 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Text.RegularExpressions;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public class ReplaceCallApp
    {
        private static ILog logger = AppState.GetLogger("sipproxy");

        private event SIPMonitorLogDelegate m_statefulProxyLogEvent;

        private SIPDialogue m_dialogue;

        private string m_username = null; 
        public string Owner
        {
            get { return m_username; }
        }

        private SIPTransport m_sipTransport;
        private CallManager m_callManager;

        public event CallCompletedDelegate CallComplete;

        public ReplaceCallApp(
            SIPMonitorLogDelegate statefulProxyLogEvent,
            SIPTransport sipTransport,
            CallManager callManager,
            SIPDialogue dialogue,
            string username)
        {
            m_statefulProxyLogEvent = statefulProxyLogEvent;
            m_sipTransport = sipTransport;
            m_callManager = callManager;
            m_dialogue = dialogue;
            m_username = username;
        }

        public void Start(string commandData)
        {
            try
            {
                logger.Debug("ReplaceCallApp Start.");

                IPEndPoint serverEndPoint = IPSocket.ParseSocketString("194.213.29.100:5060");
                SIPCallDescriptor replaceCallStruct = new SIPCallDescriptor("", "", "sip:303@sip.blueface.ie", null, null, serverEndPoint.ToString(), null, null, SIPCallDirection.Out, null, null);

                SIPClientUserAgent uac = new SIPClientUserAgent(m_sipTransport, m_username);
                uac.CallFinalResponseReceived += new UserAgentFinalResponseDelegate(ReplacementCallFinalResponseReceived);
                uac.Call(SIPURI.ParseSIPURI("sip:303@sip.blueface.ie"), SIPFromHeader.ParseFromHeader("<sip:@sip.blueface.ie>"), "", "", "application/sdp", m_dialogue.SDP);

                /*SIPRequest inviteRequest = GetInviteRequest(replaceCallStruct, m_sipTransport.GetDefaultTransportContact(SIPProtocolsEnum.UDP), null, CallProperties.CreateBranchId(), CallProperties.CreateNewCallId(), null);

                // Create a new UAC transaction for forwarded leg of the call
                m_replacementTransaction = m_sipTransport.CreateUACTransaction(inviteRequest, serverEndPoint, m_sipTransport.GetTransportContact(null), inviteRequest.URI.Protocol);
                m_replacementTransaction.CDR.Owner = Owner;
                //m_replacementTransaction.UACInviteTransactionInformationResponseReceived += new SIPTransactionResponseReceivedDelegate(SwitchServerInformationResponseReceived);
                m_replacementTransaction.UACInviteTransactionFinalResponseReceived += new SIPTransactionResponseReceivedDelegate(ReplacementCallFinalResponseReceived);
                //m_replacementTransaction.UACInviteTransactionTimedOut += new SIPTransactionTimedOutDelegate(SwitchServerTimedOut);
                //m_replacementTransaction.TransactionTraceMessage += new SIPTransactionTraceMessageDelegate(TransactionTraceMessage);

                //FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorServerTypesEnum.Switch, "Calling " + inviteRequest.URI.CanonicalAddress + "->" + SIPURI.ParseSIPURI(ForwardedCallStruct.Uri).CanonicalAddress + " via " + ForwardedEndPoint + ".", Owner));

                m_replacementTransaction.SendInviteRequest(serverEndPoint, m_replacementTransaction.TransactionRequest);*/
            }
            catch (Exception excp)
            {
                logger.Error("Exception ReplaceCallApp Start. " + excp.Message);
            }
        }

        private void ReplacementCallFinalResponseReceived(SIPTransaction transaction)
        {
            try
            {
                SIPDialogue replacementDialogue = new SIPDialogue(
                        m_sipTransport,
                        transaction.TransactionRequest.Header.CallId,
                        (transaction.TransactionFinalResponse.Header.RecordRoutes != null) ? transaction.TransactionFinalResponse.Header.RecordRoutes.Reversed() : null,
                        transaction.TransactionFinalResponse.Header.To.ToUserField,
                        transaction.TransactionFinalResponse.Header.From.FromUserField,
                        transaction.TransactionFinalResponse.Header.CSeq,
                        transaction.TransactionRequest.Header.Contact[0].ContactURI,
                        transaction.TransactionFinalResponse.Header.To.ToTag,
                        transaction.TransactionFinalResponse.Header.From.FromTag,
                        transaction.SendFromEndPoint,
                        ((UACInviteTransaction)transaction).CDR,
                        m_username,
                        null,
                        transaction.TransactionFinalResponse.Body);

                SIPDialogue keepingDialogue = m_callManager.GetOppositeDialogue(m_dialogue);
                SIPResponse finalResponse = transaction.TransactionFinalResponse;
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatefulProxy, SIPMonitorEventTypesEnum.DialPlan, "ReplaceCall " + finalResponse.Status + " response received, hanging up orphaned dialogue.", m_username));

                // Need to re-invite original call to new response.
                m_callManager.ReInvite(keepingDialogue, replacementDialogue);

                // Replacement call answered hangup call on the remote end of dialogue.
                //m_dialogue.Hangup();

                CallComplete(null, CallResult.Answered, null);
            }
            catch (Exception excp)
            {
                logger.Error("Exception ReplacementCallFinalResponseReceived. " + excp.Message);
            }
        }

        private SIPRequest GetInviteRequest(SIPCallDescriptor switchCallStruct, IPEndPoint localEndPoint, string inviteBody, string branchId, string callId, string proxyContactHost)
        {
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, switchCallStruct.Uri);
            SIPProtocolsEnum protocol = inviteRequest.URI.Protocol;

            SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader("<sip:test@sipsorcery.com>"), SIPToHeader.ParseToHeader("<sip:test@sipsorcery.com>"), 1, callId);
            inviteHeader.From.FromTag = CallProperties.CreateNewTag();

            // For incoming calls forwarded via the dial plan the username needs to go into the Contact header.
            string contactHost = (proxyContactHost != null) ? proxyContactHost : IPSocket.GetSocketString(localEndPoint);
            if (switchCallStruct.Username != null && switchCallStruct.Username.Trim().Length > 0)
            {
                inviteHeader.Contact = SIPContactHeader.ParseContactHeader("sip:" + switchCallStruct.Username + "@" + contactHost);
            }
            else
            {
                inviteHeader.Contact = SIPContactHeader.ParseContactHeader("sip:" + contactHost);
            }

            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            //inviteHeader.UserAgent = m_userAgent;
            inviteRequest.Header = inviteHeader;

            SIPViaHeader viaHeader = new SIPViaHeader(localEndPoint.Address.ToString(), localEndPoint.Port, branchId, protocol);
            inviteRequest.Header.Via.PushViaHeader(viaHeader);

            //inviteRequest.Body = inviteBody;
            //inviteRequest.Header.ContentLength = inviteBody.Length;
            //inviteRequest.Header.ContentType = "application/sdp";

            return inviteRequest;
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            if (m_statefulProxyLogEvent != null)
            {
                try
                {
                    m_statefulProxyLogEvent(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireProxyLogEvent ReplaceCallApp. " + excp.Message);
                }
            }
        }
    }
}
