//-----------------------------------------------------------------------------
// Filename: SIPB2BUserAgent.cs
//
// Description: Implementation of a SIP Back-to-back User Agent.
// 
// History:
// 21 Jul 2009	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
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
using log4net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// This class represents a back-to-back (B2B) user agent (UA) that is used to attach an outgoing
    /// call (UAC) to an incoming (UAS) call. Normally the UAC call would be the client side of a call that
    /// is placed to an external UAS in this case it's the client side of a call to a UAS in the same process.
    /// The use for this class is to allow an outgoing call from a SIP Account to another SIP Account's incoming
    /// dial plan.
    /// </summary>
    public class SIPB2BUserAgent : ISIPServerUserAgent, ISIPClientUserAgent
    {
        private static ILog logger = AppState.logger;
        private static readonly SIPEndPoint m_blackhole = new SIPEndPoint(new IPEndPoint(SIPTransport.BlackholeAddress, 0));

        private SIPMonitorLogDelegate Log_External;
        private QueueNewCallDelegate QueueNewCall_External;
        private SIPTransport m_sipTransport;

        // Real-time call control properties (not used by this user agent).
        //public string AccountCode { get; set; }
        //public decimal ReservedCredit { get; set; }
        //public int ReservedSeconds { get; set; }
        //public decimal Rate { get; set; }

        // UAC fields.
        private string m_uacOwner;
        private string m_uacAdminMemberId;
        private UACInviteTransaction m_uacTransaction;
        private SIPCallDescriptor m_uacCallDescriptor;
        public string Owner { get { return m_uacOwner; } }
        public string AdminMemberId { get { return m_uacAdminMemberId; } }
        public UACInviteTransaction ServerTransaction { get { return m_uacTransaction; } }
        public SIPCallDescriptor CallDescriptor { get { return m_uacCallDescriptor; } }

        public event SIPCallResponseDelegate CallTrying;
        public event SIPCallResponseDelegate CallRinging;
        public event SIPCallResponseDelegate CallAnswered;
        public event SIPCallFailedDelegate CallFailed;

        // UAS fields.
        private UASInviteTransaction m_uasTransaction;
        public bool IsB2B { get { return true; } }
        public bool IsAuthenticated { get { return false; } set { } }
        public bool IsInvite
        {
            get { return true; }
        } 

        public SIPCallDirection CallDirection { get { return SIPCallDirection.In; } }
        public UASInviteTransaction SIPTransaction
        {
            get { return m_uasTransaction; }
        }

        public SIPAccount SIPAccount
        {
            get { return m_uacCallDescriptor.ToSIPAccount; }
            set { }
        }

        public SIPRequest CallRequest
        {
            get { return m_uasTransaction.TransactionRequest; }
        }

        public string CallDestination
        {
            get { return m_uasTransaction.TransactionRequest.URI.User; }
        }

        public bool IsUASAnswered
        {
            get { return m_uasTransaction != null && m_uacTransaction.TransactionFinalResponse != null; }
        }

        public bool IsUACAnswered
        {
            get { return m_uacTransaction != null && m_uacTransaction.TransactionFinalResponse != null; }
        }

        public event SIPUASDelegate CallCancelled;
        public event SIPUASDelegate NoRingTimeout;
        public event SIPUASDelegate TransactionComplete;
        public event SIPUASStateChangedDelegate UASStateChanged;

        // UAS and UAC field.
        private SIPDialogue m_sipDialogue;
        public SIPDialogue SIPDialogue { get { return m_sipDialogue; } }
        //private SIPAccount m_destinationSIPAccount;

        public SIPB2BUserAgent(
            SIPMonitorLogDelegate logDelegate,
            QueueNewCallDelegate queueCall,
            SIPTransport sipTranpsort,
            string uacOwner,
            string uacAdminMemberId
            )
        {
            Log_External = logDelegate;
            QueueNewCall_External = queueCall;
            m_sipTransport = sipTranpsort;
            m_uacOwner = uacOwner;
            m_uacAdminMemberId = uacAdminMemberId;
        }

        #region UAC methods.

        public void Call(SIPCallDescriptor sipCallDescriptor)
        {
            try
            {
                m_uacCallDescriptor = sipCallDescriptor;
                SIPRequest uacInviteRequest = GetInviteRequest(m_uacCallDescriptor.Uri, sipCallDescriptor);
                if (sipCallDescriptor.MangleResponseSDP && sipCallDescriptor.MangleIPAddress != null)
                {
                    uacInviteRequest.Header.ProxyReceivedFrom = sipCallDescriptor.MangleIPAddress.ToString();
                }
                uacInviteRequest.Body = sipCallDescriptor.Content;
                uacInviteRequest.Header.ContentType = sipCallDescriptor.ContentType;
                uacInviteRequest.LocalSIPEndPoint = m_blackhole;
                uacInviteRequest.RemoteSIPEndPoint = m_blackhole;

                // Now that we have a destination socket create a new UAC transaction for forwarded leg of the call.
                m_uacTransaction = m_sipTransport.CreateUACTransaction(uacInviteRequest, m_blackhole, m_blackhole, null);
                if (m_uacTransaction.CDR != null)
                {
                    m_uacTransaction.CDR.Owner = m_uacOwner;
                    m_uacTransaction.CDR.AdminMemberId = m_uacAdminMemberId;
                    m_uacTransaction.CDR.DialPlanContextID = (m_uacCallDescriptor != null) ? m_uacCallDescriptor.DialPlanContextID : Guid.Empty;
                }

                //uacTransaction.UACInviteTransactionInformationResponseReceived += ServerInformationResponseReceived;
                //uacTransaction.UACInviteTransactionFinalResponseReceived += ServerFinalResponseReceived;
                //uacTransaction.UACInviteTransactionTimedOut += ServerTimedOut;
                //uacTransaction.TransactionTraceMessage += TransactionTraceMessage;

                m_uacTransaction.SendInviteRequest(m_blackhole, m_uacTransaction.TransactionRequest);

                SIPRequest uasInviteRequest = uacInviteRequest.Copy();
                uasInviteRequest.LocalSIPEndPoint = m_blackhole;
                uasInviteRequest.RemoteSIPEndPoint = m_blackhole;
                uasInviteRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                m_uasTransaction = m_sipTransport.CreateUASTransaction(uasInviteRequest, m_blackhole, m_blackhole, null);

                SetOwner(sipCallDescriptor.ToSIPAccount.Owner, sipCallDescriptor.ToSIPAccount.AdminMemberId);
                //m_uasTransaction.TransactionTraceMessage += TransactionTraceMessage;
                //m_uasTransaction.UASInviteTransactionTimedOut += ClientTimedOut;
                //m_uasTransaction.UASInviteTransactionCancelled += (t) => { };

                QueueNewCall_External(this);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPB2BUserAgent Call. " + excp.Message);
            }
        }

        public void Cancel()
        {
            try
            {
                logger.Debug("SIPB2BUserAgent Cancel.");
                m_uasTransaction.CancelCall();
                m_uacTransaction.CancelCall();

                if (CallCancelled != null)
                {
                    CallCancelled(this);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPB2BUserAgent Cancel. " + excp.Message);
            }
        }

        public void Update(CRMHeaders crmHeaders)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region UAS methods.

        public void SetTraceDelegate(SIPTransactionTraceMessageDelegate traceDelegate)
        {
            m_uasTransaction.TransactionTraceMessage += traceDelegate;
        }

        public bool LoadSIPAccountForIncomingCall()
        {
            return true;
        }

        public bool AuthenticateCall()
        {
            return false;
        }

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody)
        {
            try
            {
                if (!IsUASAnswered)
                {
                    if ((int)progressStatus >= 200)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "B2BUA call was passed an invalid response status of " + (int)progressStatus + ", ignoring.", m_uacOwner));
                    }
                    else
                    {
                        if (UASStateChanged != null)
                        {
                            UASStateChanged(this, progressStatus, reasonPhrase);
                        }

                        if (m_uasTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding)
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "B2BUA call ignoring progress response with status of " + (int)progressStatus + " as already in " + m_uasTransaction.TransactionState + ".", m_uacOwner));
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "B2BUA call progressing with " + progressStatus + ".", m_uacOwner));
                            SIPResponse uasProgressResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, progressStatus, reasonPhrase);
                            m_uasTransaction.SendInformationalResponse(uasProgressResponse);

                            SIPResponse uacProgressResponse = SIPTransport.GetResponse(m_uacTransaction.TransactionRequest, progressStatus, reasonPhrase);
                            if (!progressBody.IsNullOrBlank())
                            {
                                uacProgressResponse.Body = progressBody;
                                uacProgressResponse.Header.ContentType = progressContentType;
                            }
                            if (customHeaders != null && customHeaders.Length > 0)
                            {
                                foreach (string header in customHeaders)
                                {
                                    uacProgressResponse.Header.UnknownHeaders.Add(header);
                                }
                            }
                            m_uacTransaction.GotResponse(m_blackhole, m_blackhole, uacProgressResponse);
                            CallRinging((ISIPClientUserAgent)this, uacProgressResponse);
                        }
                    }
                }
                else
                {
                    logger.Warn("B2BUserAgent Progress fired on already answered call.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception B2BUserAgent Progress. " + excp.Message);
            }
        }

        public SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode)
        {
            return Answer(contentType, body, answeredDialogue, transferMode);
        }

        public SIPDialogue Answer(string contentType, string body, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode)
        {
            try
            {
                logger.Debug("SIPB2BUserAgent Answer.");
                m_sipDialogue = answeredDialogue;

                if (UASStateChanged != null)
                {
                    UASStateChanged(this, SIPResponseStatusCodesEnum.Ok, null);
                }

                SIPResponse uasOkResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, SIPResponseStatusCodesEnum.Ok, null);
                m_uasTransaction.SendFinalResponse(uasOkResponse);
                m_uasTransaction.ACKReceived(m_blackhole, m_blackhole, null);

                SIPResponse uacOkResponse = SIPTransport.GetResponse(m_uacTransaction.TransactionRequest, SIPResponseStatusCodesEnum.Ok, null);
                uacOkResponse.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(SIPSchemesEnum.sip, m_blackhole)) };
                m_uacTransaction.GotResponse(m_blackhole, m_blackhole, uacOkResponse);
                uacOkResponse.Header.ContentType = contentType;
                if (!body.IsNullOrBlank())
                {
                    uacOkResponse.Body = body;
                    uacOkResponse.Header.ContentLength = body.Length;
                }
                CallAnswered((ISIPClientUserAgent)this, uacOkResponse);
                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPB2BUSerAgent Answer. " + excp.Message);
                throw;
            }
        }

        public void AnswerNonInvite(SIPResponseStatusCodesEnum answerStatus, string reasonPhrase, string[] customHeaders, string contentType, string body)
        {
            throw new NotImplementedException();
        }

        public void Reject(SIPResponseStatusCodesEnum rejectCode, string rejectReason, string[] customHeaders)
        {
            logger.Debug("SIPB2BUserAgent Reject.");

            if (UASStateChanged != null)
            {
                UASStateChanged(this, rejectCode, rejectReason);
            }

            SIPResponse uasfailureResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, rejectCode, rejectReason);
            m_uasTransaction.SendFinalResponse(uasfailureResponse);

            SIPResponse uacfailureResponse = SIPTransport.GetResponse(m_uacTransaction.TransactionRequest, rejectCode, rejectReason);
            if (customHeaders != null && customHeaders.Length > 0)
            {
                foreach (string header in customHeaders)
                {
                    uacfailureResponse.Header.UnknownHeaders.Add(header);
                }
            }
            m_uacTransaction.GotResponse(m_blackhole, m_blackhole, uacfailureResponse);
            CallAnswered((ISIPClientUserAgent)this, uacfailureResponse);
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI)
        {
            logger.Debug("SIPB2BUserAgent Redirect.");
            //m_uas.Redirect(redirectCode, redirectURI);
        }

        public void NoCDR()
        {
            m_uasTransaction.CDR = null;
            m_uacTransaction.CDR = null;
        }

        public void SetOwner(string owner, string adminMemberId)
        {
            if (m_uasTransaction.CDR != null)
            {
                m_uasTransaction.CDR.Owner = owner;
                m_uasTransaction.CDR.AdminMemberId = adminMemberId;
                m_uasTransaction.CDR.DialPlanContextID = (m_uacCallDescriptor != null) ? m_uacCallDescriptor.DialPlanContextID : Guid.Empty;

                m_uasTransaction.CDR.Updated();
            }
        }

        public void SetDialPlanContextID(Guid dialPlanContextID)
        {
            if (m_uasTransaction.CDR != null)
            {
                m_uasTransaction.CDR.DialPlanContextID = dialPlanContextID;

                m_uasTransaction.CDR.Updated();
            }
        }

        #endregion

        private SIPRequest GetInviteRequest(string callURI, SIPCallDescriptor sipCallDescriptor)
        {
            SIPFromHeader fromHeader = sipCallDescriptor.GetFromHeader();

            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, SIPURI.ParseSIPURI(callURI));
            inviteRequest.LocalSIPEndPoint = m_blackhole;

            SIPHeader inviteHeader = new SIPHeader(fromHeader, new SIPToHeader(null, inviteRequest.URI, null), 1, CallProperties.CreateNewCallId());

            inviteHeader.From.FromTag = CallProperties.CreateNewTag();

            // For incoming calls forwarded via the dial plan the username needs to go into the Contact header.
            inviteHeader.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(inviteRequest.URI.Scheme, m_blackhole)) };
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteRequest.Header = inviteHeader;

            SIPViaHeader viaHeader = new SIPViaHeader(m_blackhole, CallProperties.CreateBranchId());
            inviteRequest.Header.Vias.PushViaHeader(viaHeader);

            try
            {
                if (sipCallDescriptor.CustomHeaders != null && sipCallDescriptor.CustomHeaders.Count > 0)
                {
                    foreach (string customHeader in sipCallDescriptor.CustomHeaders)
                    {
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
    }
}
