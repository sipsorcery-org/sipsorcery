//-----------------------------------------------------------------------------
// Filename: SIPTransferServerUserAgent.cs
//
// Description: The interface definition for SIP Server User Agents (UAC).
// 
// History:
// 21 Jan 2010	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, London, UK (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Linq;
using System.Net;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// A server user agent that replaces an existing sip dialogue rather than creating a new dialogue with
    /// a client user agent.
    /// </summary>
    public class SIPTransferServerUserAgent : ISIPServerUserAgent
    {
        private static ILog logger = AppState.logger;

        public SIPCallDirection CallDirection
        {
            get { return SIPCallDirection.Out; }
        }
        public SIPDialogue SIPDialogue
        {
            get { throw new NotImplementedException("SIPTransferServerUserAgent SIPDialogue"); }
        }
        public SIPAccount SIPAccount
        {
            get
            {
                return null;
            }
            set
            {
                throw new NotImplementedException("SIPTransferServerUserAgent SIPAccount set");
            }
        }
        public bool IsAuthenticated
        {
            get
            {
                throw new NotImplementedException("SIPTransferServerUserAgent IsAuthenticated get");
            }
            set
            {
                throw new NotImplementedException("SIPTransferServerUserAgent IsAuthenticated set");
            }
        }
        public bool IsB2B
        {
            get { return false; }
        }
        public bool IsInvite 
        {
            get { return true; }
        } 
        public SIPRequest CallRequest
        {
            get { return m_dummyRequest; }
        }
        public string CallDestination
        {
            get { return m_callDestination; }
        }
        public bool IsUASAnswered
        {
            get { return m_answered; }
        }
        public string Owner
        {
            get { return m_owner; }
        }

        private SIPMonitorLogDelegate Log_External =  (e) => { }; //SIPMonitorEvent.DefaultSIPMonitorLogger;
        private BlindTransferDelegate BlindTransfer_External;
        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;                   // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private SIPDialogue m_dialogueToReplace;
        private SIPDialogue m_oppositeDialogue;
        private string m_owner;
        private string m_adminID;
        private bool m_answered;
        private string m_callDestination;
        private SIPRequest m_dummyRequest;                      // Used to get the SDP for into the dialplan.

        public event SIPUASDelegate CallCancelled;
        public event SIPUASDelegate NoRingTimeout;
        public event SIPUASDelegate TransactionComplete;
        public event SIPUASStateChangedDelegate UASStateChanged;

        public SIPTransferServerUserAgent(            
            SIPMonitorLogDelegate logDelegate,
            BlindTransferDelegate blindTransfer,
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPDialogue dialogueToReplace,
            SIPDialogue oppositeDialogue,
            string callDestination,
            string owner,
            string adminID)
        {
            Log_External = logDelegate;
            BlindTransfer_External = blindTransfer;
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_dialogueToReplace = dialogueToReplace;
            m_oppositeDialogue = oppositeDialogue;
            m_callDestination = callDestination;
            m_owner = owner;
            m_adminID = adminID;
            m_dummyRequest = CreateDummyRequest(m_dialogueToReplace, m_callDestination);
        }

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody)
        {
            if (UASStateChanged != null)
            {
                UASStateChanged(this, progressStatus, reasonPhrase);
            }

            if (progressBody != null)
            {
                // Re-invite the remote dialogue so that they can listen to some real progress tones.
            }
        }

        public SIPDialogue Answer(string contentType, string body, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode)
        {
            return Answer(contentType, body, null, answeredDialogue, transferMode);
        }

        /// <summary>
        /// An answer on a blind transfer means the remote end of the dialogue being replaced should be re-invited and then the replaced dialogue 
        /// should be hungup.
        /// </summary>
        /// <param name="contentType"></param>
        /// <param name="body"></param>
        /// <param name="toTag"></param>
        /// <param name="answeredDialogue"></param>
        /// <param name="transferMode"></param>
        /// <returns></returns>
        public SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode)
        {
            try
            {
                if (m_answered)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A blind transfer received an answer on an already answered call, hanging up dialogue.", m_owner));
                    answeredDialogue.Hangup(m_sipTransport, m_outboundProxy);
                }
                else
                {
                    m_answered = true;
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A blind transfer received an answer.", m_owner));

                    if (UASStateChanged != null)
                    {
                        UASStateChanged(this, SIPResponseStatusCodesEnum.Ok, null);
                    }

                    BlindTransfer_External(m_dialogueToReplace, m_oppositeDialogue, answeredDialogue);
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTransferServerUserAgent Answer. " + excp.Message);
                throw;
            }
        }

        public void AnswerNonInvite(SIPResponseStatusCodesEnum answerStatus, string reasonPhrase, string[] customHeaders, string contentType, string body)
        {
            throw new NotImplementedException();
        }

        public void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase, string[] customHeaders)
        {
            logger.Warn("SIPTransferServerUserAgent Reject called with " + failureStatus + " " + reasonPhrase + ".");

            if (UASStateChanged != null)
            {
                UASStateChanged(this, failureStatus, reasonPhrase);
            }
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI)
        {
            throw new NotImplementedException("SIPTransferServerUserAgent Redirect");
        }
        
        public void NoCDR()
        {
            throw new NotImplementedException("SIPTransferServerUserAgent NoCDR");
        }

        public void SetTraceDelegate(SIPTransactionTraceMessageDelegate traceDelegate)
        {
            //throw new NotImplementedException();
        }

        public bool LoadSIPAccountForIncomingCall()
        {
            throw new NotImplementedException("SIPTransferServerUserAgent LoadSIPAccountForIncomingCall");
        }

        public bool AuthenticateCall()
        {
            throw new NotImplementedException("SIPTransferServerUserAgent AuthenticateCall");
        }

        public void SetOwner(string owner, string adminMemberId)
        {
            m_owner = owner;
            m_adminID = adminMemberId;
        }

        private SIPRequest CreateDummyRequest(SIPDialogue dialogueToReplace, string callDestination)
        {
            SIPRequest dummyInvite = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURIRelaxed(callDestination + "@sipsorcery.com"));
            SIPHeader dummyHeader = new SIPHeader("<sip:anon@sipsorcery.com>", "<sip:anon@sipsorcery.com>", 1, CallProperties.CreateNewCallId());
            dummyHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            dummyHeader.Vias.PushViaHeader(new SIPViaHeader(new IPEndPoint(SIPTransport.BlackholeAddress, 0), CallProperties.CreateBranchId()));
            dummyInvite.Header = dummyHeader;
            dummyInvite.Header.ContentType = "application/sdp";
            dummyInvite.Body = dialogueToReplace.SDP;

            return dummyInvite;
        }

        public void SetDialPlanContextID(Guid dialPlanContextID)
        {
            throw new NotImplementedException("SIPTransferServerUserAgent SetDialPlanContextID");
        }

        /// <summary>
        /// Fired when a transfer is pending and the call leg that is going to be bridged hangs up before the transfer completes.
        /// </summary>
        public void PendingLegHungup()
        {
            try
            {
                if (CallCancelled != null)
                {
                    CallCancelled(this);
                }
            }
            catch(Exception excp)
            {
                logger.Error("Exception PendingLegHungup. " + excp);
            }
        }
    }
}
