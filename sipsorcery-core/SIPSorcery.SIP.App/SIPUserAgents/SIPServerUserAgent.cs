//-----------------------------------------------------------------------------
// Filename: SIPServerUserAgent.cs
//
// Description: Implementation of a SIP Server User Agent that can be used to receive SIP calls.
// 
// History:
// 22 Feb 2008	Aaron Clauson	    Created.
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
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    /// <remarks>
    /// A To tag MUST be set on all non 100 Trying responses, see RFC 3261 "8.2.6.2 Headers and Tags".
    /// </remarks>
    public class SIPServerUserAgent : ISIPServerUserAgent
    {
        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate Log_External = (e) => { }; //SIPMonitorEvent.DefaultSIPMonitorLogger;
        private SIPAuthenticateRequestDelegate SIPAuthenticateRequest_External;
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;

        private SIPTransport m_sipTransport;
        private UASInviteTransaction m_uasTransaction;
        private SIPEndPoint m_outboundProxy;                   // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private SIPDialogue m_sipDialogue;
        private bool m_isAuthenticated;
        private bool m_isCancelled;
        private string m_owner;
        private string m_adminMemberId;
        private string m_sipUsername;
        private string m_sipDomain;
        private SIPCallDirection m_sipCallDirection;
        public bool IsB2B { get { return false; } }
        public bool IsInvite
        {
            get { return true; }
        } 
        public string Owner { get { return m_owner; } }

        public SIPCallDirection CallDirection
        {
            get { return m_sipCallDirection; }
        }

        public SIPDialogue SIPDialogue
        {
            get { return m_sipDialogue; }
        }

        private SIPAccount m_sipAccount;
        public SIPAccount SIPAccount
        {
            get { return m_sipAccount; }
            set { m_sipAccount = value; }
        }

        public bool IsAuthenticated
        {
            get { return m_isAuthenticated; }
            set { m_isAuthenticated = value; }
        }

        public bool IsCancelled
        {
            get { return m_isCancelled; }
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
            get { return m_uasTransaction != null && m_uasTransaction.TransactionFinalResponse != null; }
        }

        public event SIPUASDelegate CallCancelled;
        public event SIPUASDelegate NoRingTimeout;
        public event SIPUASDelegate TransactionComplete;
        public event SIPUASStateChangedDelegate UASStateChanged;

        public SIPServerUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            string sipUsername,
            string sipDomain,
            SIPCallDirection callDirection,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAuthenticateRequestDelegate sipAuthenticateRequest,
            SIPMonitorLogDelegate logDelegate,
            UASInviteTransaction uasTransaction)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_sipUsername = sipUsername;
            m_sipDomain = sipDomain;
            m_sipCallDirection = callDirection;
            GetSIPAccount_External = getSIPAccount;
            SIPAuthenticateRequest_External = sipAuthenticateRequest;
            Log_External = logDelegate ?? Log_External;
            m_uasTransaction = uasTransaction;

            m_uasTransaction.TransactionTraceMessage += TransactionTraceMessage;
            m_uasTransaction.UASInviteTransactionTimedOut += ClientTimedOut;
            m_uasTransaction.UASInviteTransactionCancelled += UASTransactionCancelled;
            m_uasTransaction.TransactionRemoved += new SIPTransactionRemovedDelegate(UASTransaction_TransactionRemoved);
            //m_uasTransaction.TransactionStateChanged += (t) => { logger.Debug("Transaction state change to " + t.TransactionState + ", uri=" + t.TransactionRequestURI.ToString() + "."); };
        }

        private void UASTransaction_TransactionRemoved(SIPTransaction sipTransaction)
        {
            if (TransactionComplete != null)
            {
                TransactionComplete(this);
            }
        }

        public void SetTraceDelegate(SIPTransactionTraceMessageDelegate traceDelegate)
        {
            m_uasTransaction.TransactionTraceMessage += traceDelegate;

            traceDelegate(m_uasTransaction, SIPMonitorEventTypesEnum.SIPTransaction + "=>" + "Request received " + m_uasTransaction.LocalSIPEndPoint +
                "<-" + m_uasTransaction.RemoteEndPoint + "\r\n" + m_uasTransaction.TransactionRequest.ToString());
        }

        public bool LoadSIPAccountForIncomingCall()
        {
            try
            {
                bool loaded = false;

                if (GetSIPAccount_External == null)
                {
                    // No point trying to authenticate if we haven't been given a delegate to load the SIP account.
                    Reject(SIPResponseStatusCodesEnum.InternalServerError, null, null);
                }
                else
                {
                    m_sipAccount = GetSIPAccount_External(s => s.SIPUsername == m_sipUsername && s.SIPDomain == m_sipDomain);

                    if (m_sipAccount == null)
                    {
                        // A full lookup failed. Now try a partial lookup if the incoming username is in a dotted domain name format.
                        if (m_sipUsername.Contains("."))
                        {
                            string sipUsernameSuffix = m_sipUsername.Substring(m_sipUsername.LastIndexOf(".") + 1);
                            m_sipAccount = GetSIPAccount_External(s => s.SIPUsername == sipUsernameSuffix && s.SIPDomain == m_sipDomain);
                        }

                        if (m_sipAccount == null)
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Rejecting public call for " + m_sipUsername + "@" + m_sipDomain + ", SIP account not found.", null));
                            Reject(SIPResponseStatusCodesEnum.NotFound, null, null);
                        }
                        else
                        {
                            loaded = true;
                        }
                    }
                    else
                    {
                        loaded = true;
                    }
                }

                if (loaded)
                {
                    SetOwner(m_sipAccount.Owner, m_sipAccount.AdminMemberId);
                }

                return loaded;
            }
            catch (Exception excp)
            {
                logger.Error("Exception LoadSIPAccountForIncomingCall. " + excp.Message);
                Reject(SIPResponseStatusCodesEnum.InternalServerError, null, null);
                return false;
            }
        }

        public bool AuthenticateCall()
        {
            m_isAuthenticated = false;

            try
            {
                if (SIPAuthenticateRequest_External == null)
                {
                    // No point trying to authenticate if we haven't been given an authentication delegate.
                    Reject(SIPResponseStatusCodesEnum.InternalServerError, null, null);
                }
                else if (GetSIPAccount_External == null)
                {
                    // No point trying to authenticate if we haven't been given a  delegate to load the SIP account.
                    Reject(SIPResponseStatusCodesEnum.InternalServerError, null, null);
                }
                else
                {
                    m_sipAccount = GetSIPAccount_External(s => s.SIPUsername == m_sipUsername && s.SIPDomain == m_sipDomain);

                    if (m_sipAccount == null)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Rejecting authentication required call for " + m_sipUsername + "@" + m_sipDomain + ", SIP account not found.", null));
                        Reject(SIPResponseStatusCodesEnum.Forbidden, null, null);
                    }
                    else
                    {
                        SIPRequest sipRequest = m_uasTransaction.TransactionRequest;
                        SIPEndPoint localSIPEndPoint = (!sipRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedOn) : sipRequest.LocalSIPEndPoint;
                        SIPEndPoint remoteEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : sipRequest.RemoteSIPEndPoint;

                        SIPRequestAuthenticationResult authenticationResult = SIPAuthenticateRequest_External(localSIPEndPoint, remoteEndPoint, sipRequest, m_sipAccount, Log_External);
                        if (authenticationResult.Authenticated)
                        {
                            if (authenticationResult.WasAuthenticatedByIP)
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "New call from " + remoteEndPoint.ToString() + " successfully authenticated by IP address.", m_sipAccount.Owner));
                            }
                            else
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "New call from " + remoteEndPoint.ToString() + " successfully authenticated by digest.", m_sipAccount.Owner));
                            }

                            SetOwner(m_sipAccount.Owner,  m_sipAccount.AdminMemberId);
                            m_isAuthenticated = true;
                        }
                        else
                        {
                            // Send authorisation failure or required response
                            SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                            authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call not authenticated for " + m_sipUsername + "@" + m_sipDomain + ", responding with " + authenticationResult.ErrorResponse + ".", null));
                            m_uasTransaction.SendFinalResponse(authReqdResponse);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPServerUserAgent AuthenticateCall. " + excp.Message);
                Reject(SIPResponseStatusCodesEnum.InternalServerError, null, null);
            }

            return m_isAuthenticated;
        }

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody)
        {
            try
            {
                if (!IsUASAnswered)
                {
                    if ((int)progressStatus >= 200)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "UAS call was passed an invalid response status of " + (int)progressStatus + ", ignoring.", m_owner));
                    }
                    else
                    {
                        if (UASStateChanged != null)
                        {
                            UASStateChanged(this, progressStatus, reasonPhrase);
                        }

                        // Allow all Trying responses through as some may contain additional useful information on the call state for the caller. 
                        // Also if the response is a 183 Session Progress with audio forward it.
                        if (m_uasTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding && progressStatus != SIPResponseStatusCodesEnum.Trying &&
                            !(progressStatus == SIPResponseStatusCodesEnum.SessionProgress && progressBody != null))
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "UAS call ignoring progress response with status of " + (int)progressStatus + " as already in " + m_uasTransaction.TransactionState + ".", m_owner));
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "UAS call progressing with " + progressStatus + ".", m_owner));
                            SIPResponse progressResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, progressStatus, reasonPhrase);

                            if (progressResponse.Status != SIPResponseStatusCodesEnum.Trying)
                            {
                                progressResponse.Header.To.ToTag = m_uasTransaction.LocalTag;
                            }

                            if (!progressBody.IsNullOrBlank())
                            {
                                progressResponse.Body = progressBody;
                                progressResponse.Header.ContentType = progressContentType;
                                progressResponse.Header.ContentLength = progressBody.Length;
                            }

                            if (customHeaders != null && customHeaders.Length > 0)
                            {
                                foreach (string header in customHeaders)
                                {
                                    progressResponse.Header.UnknownHeaders.Add(header);
                                }
                            }

                            m_uasTransaction.SendInformationalResponse(progressResponse);
                        }
                    }
                }
                else
                {
                    logger.Warn("SIPServerUserAgent Progress fired on already answered call.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPServerUserAgent Progress. " + excp.Message);
            }
        }

        public SIPDialogue Answer(string contentType, string body, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode)
        {
            return Answer(contentType, body, null, answeredDialogue, transferMode);
        }

        public SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode)
        {
            try
            {
                if (m_uasTransaction.TransactionFinalResponse != null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "UAS Answer was called on an already answered call, ignoring.", m_owner));
                    return null;
                }
                else
                {
                    if (UASStateChanged != null)
                    {
                        UASStateChanged(this, SIPResponseStatusCodesEnum.Ok, null);
                    }

                    if (!toTag.IsNullOrBlank())
                    {
                        m_uasTransaction.SetLocalTag(toTag);
                    }

                    SIPResponse okResponse = m_uasTransaction.GetOkResponse(m_uasTransaction.TransactionRequest, m_uasTransaction.TransactionRequest.LocalSIPEndPoint, contentType, body);

                    if (body != null)
                    {
                        okResponse.Header.ContentType = contentType;
                        okResponse.Header.ContentLength = body.Length;
                        okResponse.Body = body;
                    }

                    m_uasTransaction.SendFinalResponse(okResponse);

                    m_sipDialogue = new SIPDialogue(m_uasTransaction, m_owner, m_adminMemberId);
                    m_sipDialogue.TransferMode = transferMode;

                    return m_sipDialogue;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPServerUserAgent Answer. " + excp.Message);
                throw;
            }
        }

        public void AnswerNonInvite(SIPResponseStatusCodesEnum answerStatus, string reasonPhrase, string[] customHeaders, string contentType, string body)
        {
            throw new NotImplementedException();
        }

        public void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase, string[] customHeaders)
        {
            try
            {
                if (m_uasTransaction.TransactionFinalResponse == null)
                {
                    if ((int)failureStatus < 400)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "UAS Reject was passed an invalid response status of " + (int)failureStatus + ", ignoring.", m_owner));
                    }
                    else
                    {
                        if (UASStateChanged != null)
                        {
                            UASStateChanged(this, failureStatus, reasonPhrase);
                        }

                        string failureReason = (!reasonPhrase.IsNullOrBlank()) ? " and " + reasonPhrase : null;

                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "UAS call failed with a response status of " + (int)failureStatus + failureReason + ".", m_owner));
                        SIPResponse failureResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, failureStatus, reasonPhrase);

                        if (customHeaders != null && customHeaders.Length > 0)
                        {
                            foreach (string header in customHeaders)
                            {
                                failureResponse.Header.UnknownHeaders.Add(header);
                            }
                        }

                        m_uasTransaction.SendFinalResponse(failureResponse);
                    }
                }
                else
                {
                    logger.Warn("SIPServerUserAgent Reject fired on already answered call.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPServerUserAgent Reject. " + excp.Message);
            }
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI)
        {
            try
            {
                if (m_uasTransaction.TransactionFinalResponse == null)
                {
                    SIPResponse redirectResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, redirectCode, null);
                    redirectResponse.Header.Contact = SIPContactHeader.CreateSIPContactList(redirectURI);
                    m_uasTransaction.SendFinalResponse(redirectResponse);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPServerUserAgent Redirect. " + excp.Message);
            }
        }

        public void NoCDR()
        {
            m_uasTransaction.CDR = null;
        }

        private void UASTransactionCancelled(SIPTransaction sipTransaction)
        {
            logger.Debug("SIPServerUserAgent got cancellation request.");
            m_isCancelled = true;
            if (CallCancelled != null)
            {
                CallCancelled(this);
            }
        }

        private void ClientTimedOut(SIPTransaction sipTransaction)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.DialPlan, "UAS for " + m_uasTransaction.TransactionRequest.URI.ToString() + " timed out in transaction state " + m_uasTransaction.TransactionState + ".", null));
                //SIPResponse rejectResponse = SIPTransport.GetResponse(m_uasTransaction.TransactionRequest, SIPResponseStatusCodesEnum.ServerTimeout, "No info or final response received within timeout");
                //m_uasTransaction.SendFinalResponse(rejectResponse);

                if (m_uasTransaction.TransactionState == SIPTransactionStatesEnum.Calling && NoRingTimeout != null)
                {
                    NoRingTimeout(this);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ClientTimedOut. " + excp.Message);
            }
        }

        public void SetOwner(string owner, string adminMemberId)
        {
            m_owner = owner;
            m_adminMemberId = adminMemberId;

            if (m_uasTransaction.CDR != null)
            {
                m_uasTransaction.CDR.Owner = owner;
                m_uasTransaction.CDR.AdminMemberId = adminMemberId;

                m_uasTransaction.CDR.Updated();
            }
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.SIPTransaction, message, null));
        }

        public void SetDialPlanContextID(Guid dialPlanContextID)
        {
            if (m_uasTransaction.CDR != null)
            {
                m_uasTransaction.CDR.DialPlanContextID = dialPlanContextID;

                m_uasTransaction.CDR.Updated();
            }
        }
    }
}
