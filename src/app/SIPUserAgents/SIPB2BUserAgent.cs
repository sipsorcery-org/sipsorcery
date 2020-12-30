//-----------------------------------------------------------------------------
// Filename: SIPB2BUserAgent.cs
//
// Description: Implementation of a SIP Back-to-back User Agent.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 21 Jul 2009  Aaron Clauson   Created, Hobart, Australia.
// 28 Dec 2020  Aaron Clauson   Added back into library and updated for new API. 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
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
        private static ILogger logger = Log.Logger;

        private static SIPEndPoint _blackholeEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, SIPTransport.BlackholeAddress, 0);

        //private SIPMonitorLogDelegate Log_External;
        //private QueueNewCallDelegate QueueNewCall_External;
        private SIPTransport m_sipTransport;

        // Real-time call control properties (not used by this user agent).
        //public string AccountCode { get; set; }
        //public decimal ReservedCredit { get; set; }
        //public int ReservedSeconds { get; set; }
        //public decimal Rate { get; set; }

        // UAC fields.
        private UACInviteTransaction m_uacTransaction;
        private SIPCallDescriptor m_uacCallDescriptor;
        public UACInviteTransaction ServerTransaction { get { return m_uacTransaction; } }
        public SIPCallDescriptor CallDescriptor { get { return m_uacCallDescriptor; } }

#pragma warning disable CS0067
        public event SIPCallResponseDelegate CallTrying;
        public event SIPCallFailedDelegate CallFailed;
#pragma warning restore

        public event SIPCallResponseDelegate CallRinging;
        public event SIPCallResponseDelegate CallAnswered;

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

        public ISIPAccount SIPAccount
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

#pragma warning disable CS0067
        public event SIPUASDelegate NoRingTimeout;
        public event SIPUASDelegate TransactionComplete;
#pragma warning restore

        public event SIPUASDelegate CallCancelled;
        //public event SIPUASStateChangedDelegate UASStateChanged;

        // UAS and UAC field.
        private SIPDialogue m_sipDialogue;
        public SIPDialogue SIPDialogue { get { return m_sipDialogue; } }

        public UASInviteTransaction ClientTransaction => throw new NotImplementedException();

        //private SIPAccount m_destinationSIPAccount;

        public SIPB2BUserAgent(
            //SIPMonitorLogDelegate logDelegate,
            //QueueNewCallDelegate queueCall,
            SIPTransport sipTranpsort)
        {
            //Log_External = logDelegate;
            //QueueNewCall_External = queueCall;
            m_sipTransport = sipTranpsort;
        }

        #region UAC methods.

        public SIPRequest Call(SIPCallDescriptor sipCallDescriptor)
        {
            return Call(sipCallDescriptor, null);
        }

        public SIPRequest Call(SIPCallDescriptor sipCallDescriptor, SIPEndPoint serverEndPoint)
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
                //uacInviteRequest.LocalSIPEndPoint = m_blackhole;
                //uacInviteRequest.RemoteSIPEndPoint = m_blackhole;

                // Now that we have a destination socket create a new UAC transaction for forwarded leg of the call.
                //m_uacTransaction = m_sipTransport.CreateUACTransaction(uacInviteRequest, m_blackhole, m_blackhole, null);
                m_uacTransaction = new UACInviteTransaction(m_sipTransport, uacInviteRequest, null);

                //uacTransaction.UACInviteTransactionInformationResponseReceived += ServerInformationResponseReceived;
                //uacTransaction.UACInviteTransactionFinalResponseReceived += ServerFinalResponseReceived;
                //uacTransaction.UACInviteTransactionTimedOut += ServerTimedOut;
                //uacTransaction.TransactionTraceMessage += TransactionTraceMessage;

                m_uacTransaction.SendInviteRequest();

                SIPRequest uasInviteRequest = uacInviteRequest.Copy();
                //uasInviteRequest.LocalSIPEndPoint = m_blackhole;
                //uasInviteRequest.RemoteSIPEndPoint = m_blackhole;
                uasInviteRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                //m_uasTransaction = m_sipTransport.CreateUASTransaction(uasInviteRequest, m_blackhole, m_blackhole, null);
                m_uasTransaction = new UASInviteTransaction(m_sipTransport, uasInviteRequest, null);

                //SetOwner(sipCallDescriptor.ToSIPAccount.Owner, sipCallDescriptor.ToSIPAccount.AdminMemberId);
                //m_uasTransaction.TransactionTraceMessage += TransactionTraceMessage;
                //m_uasTransaction.UASInviteTransactionTimedOut += ClientTimedOut;
                //m_uasTransaction.UASInviteTransactionCancelled += (t) => { };

                //QueueNewCall_External(this);

                return uasInviteRequest;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPB2BUserAgent Call. " + excp.Message);
                throw;
            }
        }

        public void Cancel()
        {
            try
            {
                logger.LogDebug("SIPB2BUserAgent Cancel.");
                m_uasTransaction.CancelCall();
                m_uacTransaction.CancelCall();

                if (CallCancelled != null)
                {
                    CallCancelled(this);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPB2BUserAgent Cancel. " + excp.Message);
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
                        //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "B2BUA call was passed an invalid response status of " + (int)progressStatus + ", ignoring.", m_uacOwner));
                    }
                    else
                    {
                        //if (UASStateChanged != null)
                        //{
                        //    UASStateChanged(this, progressStatus, reasonPhrase);
                        //}

                        if (m_uasTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding)
                        {
                            //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "B2BUA call ignoring progress response with status of " + (int)progressStatus + " as already in " + m_uasTransaction.TransactionState + ".", m_uacOwner));
                        }
                        else
                        {
                            //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "B2BUA call progressing with " + progressStatus + ".", m_uacOwner));
                            SIPResponse uasProgressResponse = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, progressStatus, reasonPhrase);
                            m_uasTransaction.SendProvisionalResponse(uasProgressResponse);

                            SIPResponse uacProgressResponse = SIPResponse.GetResponse(m_uacTransaction.TransactionRequest, progressStatus, reasonPhrase);
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
                            m_uacTransaction.GotResponse(_blackholeEndPoint, _blackholeEndPoint, uacProgressResponse);
                            CallRinging((ISIPClientUserAgent)this, uacProgressResponse);
                        }
                    }
                }
                else
                {
                    logger.LogWarning("B2BUserAgent Progress fired on already answered call.");
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception B2BUserAgent Progress. " + excp.Message);
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
                logger.LogDebug("SIPB2BUserAgent Answer.");
                m_sipDialogue = answeredDialogue;

                //if (UASStateChanged != null)
                //{
                //    UASStateChanged(this, SIPResponseStatusCodesEnum.Ok, null);
                //}

                SIPResponse uasOkResponse = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, SIPResponseStatusCodesEnum.Ok, null);
                m_uasTransaction.SendFinalResponse(uasOkResponse);
                m_uasTransaction.ACKReceived(_blackholeEndPoint, _blackholeEndPoint, null);

                SIPResponse uacOkResponse = SIPResponse.GetResponse(m_uacTransaction.TransactionRequest, SIPResponseStatusCodesEnum.Ok, null);
                uacOkResponse.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(SIPSchemesEnum.sip, _blackholeEndPoint)) };
                m_uacTransaction.GotResponse(_blackholeEndPoint, _blackholeEndPoint, uacOkResponse);
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
                logger.LogError("Exception SIPB2BUSerAgent Answer. " + excp.Message);
                throw;
            }
        }

        public void AnswerNonInvite(SIPResponseStatusCodesEnum answerStatus, string reasonPhrase, string[] customHeaders, string contentType, string body)
        {
            throw new NotImplementedException();
        }

        public void Reject(SIPResponseStatusCodesEnum rejectCode, string rejectReason, string[] customHeaders)
        {
            logger.LogDebug("SIPB2BUserAgent Reject.");

            //if (UASStateChanged != null)
            //{
            //    UASStateChanged(this, rejectCode, rejectReason);
            //}

            SIPResponse uasfailureResponse = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, rejectCode, rejectReason);
            m_uasTransaction.SendFinalResponse(uasfailureResponse);

            SIPResponse uacfailureResponse = SIPResponse.GetResponse(m_uacTransaction.TransactionRequest, rejectCode, rejectReason);
            if (customHeaders != null && customHeaders.Length > 0)
            {
                foreach (string header in customHeaders)
                {
                    uacfailureResponse.Header.UnknownHeaders.Add(header);
                }
            }
            m_uacTransaction.GotResponse(_blackholeEndPoint, _blackholeEndPoint, uacfailureResponse);
            CallAnswered((ISIPClientUserAgent)this, uacfailureResponse);
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI)
        {
            logger.LogDebug("SIPB2BUserAgent Redirect.");
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
            //.LocalSIPEndPoint = m_blackhole;

            SIPHeader inviteHeader = new SIPHeader(fromHeader, new SIPToHeader(null, inviteRequest.URI, null), 1, CallProperties.CreateNewCallId());

            inviteHeader.From.FromTag = CallProperties.CreateNewTag();

            // For incoming calls forwarded via the dial plan the username needs to go into the Contact header.
            inviteHeader.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(inviteRequest.URI.Scheme, _blackholeEndPoint)) };
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteRequest.Header = inviteHeader;

            SIPViaHeader viaHeader = new SIPViaHeader(_blackholeEndPoint, CallProperties.CreateBranchId());
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
                logger.LogError("Exception Parsing CustomHeader for GetInviteRequest. " + excp.Message + sipCallDescriptor.CustomHeaders);
            }

            return inviteRequest;
        }

        public SIPDialogue Answer(string contentType, string body, SIPDialogueTransferModesEnum transferMode)
        {
            throw new NotImplementedException();
        }

        public SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogueTransferModesEnum transferMode)
        {
            throw new NotImplementedException();
        }

        public SIPDialogue Answer(string contentType, string body, SIPDialogueTransferModesEnum transferMode, string[] customHeaders)
        {
            throw new NotImplementedException();
        }

        public SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogueTransferModesEnum transferMode, string[] customHeaders)
        {
            throw new NotImplementedException();
        }

        public void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase)
        {
            throw new NotImplementedException();
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI, string[] customHeaders)
        {
            throw new NotImplementedException();
        }
    }
}
