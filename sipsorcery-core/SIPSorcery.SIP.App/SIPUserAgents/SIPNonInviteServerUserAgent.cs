// ============================================================================
// FileName: SIPNonInviteServerUserAgent.cs
//
// Description:
// A user agent that can process non-invite requests such as MESSAGEs. The purpose of having a user agent
// for such requests is so they can be passed to a dial plan for processing.
//
// Author(s):
// Aaron Clauson
//
// History:
// 24 Apr 2011	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com)
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
// ============================================================================

using System;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPNonInviteServerUserAgent : ISIPServerUserAgent
    {
        private static ILog logger = AppState.logger;

        private SIPMonitorLogDelegate Log_External = (e) => { }; //SIPMonitorEvent.DefaultSIPMonitorLogger;
        private SIPAuthenticateRequestDelegate SIPAuthenticateRequest_External;
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;

        private SIPTransport m_sipTransport;
        private SIPNonInviteTransaction m_transaction;
        private SIPEndPoint m_outboundProxy;                   // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private SIPDialogue m_sipDialogue;
        private bool m_isAuthenticated;

        private string m_owner;
        private string m_adminMemberId;
        private string m_sipUsername;
        private string m_sipDomain;
        private SIPCallDirection m_sipCallDirection;
       
        public SIPCallDirection CallDirection
        {
            get { return m_sipCallDirection; }
        }
        public SIPDialogue SIPDialogue
        {
            get { throw new NotImplementedException(); }
        }

        private SIPAccount m_sipAccount;
        public SIPAccount SIPAccount
        {
            get
            {
                return m_sipAccount;
            }
            set
            {
                m_sipAccount = value;
            }
        }

        public bool IsAuthenticated
        {
            get
            {
                return m_isAuthenticated;
            }
            set
            {
                m_isAuthenticated = value;
            }
        }

        public bool IsB2B
        {
            get { return false; }
        }
        public bool IsInvite
        {
            get { return false; }
        } 
        public SIPRequest CallRequest
        {
            get { return m_transaction.TransactionRequest; }
        }
        public string CallDestination
        {
            get { return m_transaction.TransactionRequest.URI.User; }
        }
        public bool IsUASAnswered
        {
            get { return false; }
        }
        public string Owner
        {
            get { return m_owner; }
        }

        public event SIPUASDelegate CallCancelled;
        public event SIPUASDelegate NoRingTimeout;
        public event SIPUASDelegate TransactionComplete;
        public event SIPUASStateChangedDelegate UASStateChanged;

        public SIPNonInviteServerUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            string sipUsername,
            string sipDomain,
            SIPCallDirection callDirection,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            SIPAuthenticateRequestDelegate sipAuthenticateRequest,
            SIPMonitorLogDelegate logDelegate,
            SIPNonInviteTransaction transaction)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_sipUsername = sipUsername;
            m_sipDomain = sipDomain;
            m_sipCallDirection = callDirection;
            GetSIPAccount_External = getSIPAccount;
            SIPAuthenticateRequest_External = sipAuthenticateRequest;
            Log_External = logDelegate ?? Log_External;
            m_transaction = transaction;

            m_transaction.TransactionTraceMessage += TransactionTraceMessage;
            //m_uasTransaction.UASInviteTransactionTimedOut += ClientTimedOut;
            //m_uasTransaction.UASInviteTransactionCancelled += UASTransactionCancelled;
            //m_uasTransaction.TransactionRemoved += new SIPTransactionRemovedDelegate(UASTransaction_TransactionRemoved);
            //m_uasTransaction.TransactionStateChanged += (t) => { logger.Debug("Transaction state change to " + t.TransactionState + ", uri=" + t.TransactionRequestURI.ToString() + "."); };
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
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Rejecting public " + m_transaction.TransactionRequest.Method +" request for " + m_sipUsername + "@" + m_sipDomain + ", SIP account not found.", null));
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
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Rejecting authentication required " + m_transaction.TransactionRequest.Method + " for " + m_sipUsername + "@" + m_sipDomain + ", SIP account not found.", null));
                        Reject(SIPResponseStatusCodesEnum.Forbidden, null, null);
                    }
                    else
                    {
                        SIPRequest sipRequest = m_transaction.TransactionRequest;
                        SIPEndPoint localSIPEndPoint = (!sipRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedOn) : sipRequest.LocalSIPEndPoint;
                        SIPEndPoint remoteEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : sipRequest.RemoteSIPEndPoint;

                        SIPRequestAuthenticationResult authenticationResult = SIPAuthenticateRequest_External(localSIPEndPoint, remoteEndPoint, sipRequest, m_sipAccount, Log_External);
                        if (authenticationResult.Authenticated)
                        {
                            if (authenticationResult.WasAuthenticatedByIP)
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, m_transaction.TransactionRequest.Method + " request from " + remoteEndPoint.ToString() + " successfully authenticated by IP address.", m_sipAccount.Owner));
                            }
                            else
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, m_transaction.TransactionRequest.Method + " request from " + remoteEndPoint.ToString() + " successfully authenticated by digest.", m_sipAccount.Owner));
                            }

                            SetOwner(m_sipAccount.Owner, m_sipAccount.AdminMemberId);
                            m_isAuthenticated = true;
                        }
                        else
                        {
                            // Send authorisation failure or required response
                            SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                            authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, m_transaction.TransactionRequest.Method + " request not authenticated for " + m_sipUsername + "@" + m_sipDomain + ", responding with " + authenticationResult.ErrorResponse + ".", null));
                            m_transaction.SendFinalResponse(authReqdResponse);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPNonInviteUserAgent AuthenticateCall. " + excp.Message);
                Reject(SIPResponseStatusCodesEnum.InternalServerError, null, null);
            }

            return m_isAuthenticated;
        }

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody)
        {
            throw new NotImplementedException();
        }

        public SIPDialogue Answer(string contentType, string body, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode)
        {
            throw new NotImplementedException();
        }

        public SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogue answeredDialogue, SIPDialogueTransferModesEnum transferMode)
        {
            throw new NotImplementedException();
        }

        public void AnswerNonInvite(SIPResponseStatusCodesEnum answerStatus, string reasonPhrase, string[] customHeaders, string contentType, string body)
        {
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, m_transaction.TransactionRequest.Method + " request succeeded with a response status of " + (int)answerStatus + " " + reasonPhrase + ".", m_owner));
            SIPResponse answerResponse = SIPTransport.GetResponse(m_transaction.TransactionRequest, answerStatus, reasonPhrase);

            if (customHeaders != null && customHeaders.Length > 0)
            {
                foreach (string header in customHeaders)
                {
                    answerResponse.Header.UnknownHeaders.Add(header);
                }
            }

            if (!body.IsNullOrBlank())
            {
                answerResponse.Header.ContentType = contentType;
                answerResponse.Body = body;
            }

            m_transaction.SendFinalResponse(answerResponse);
        }

        public void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase, string[] customHeaders)
        {
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, m_transaction.TransactionRequest.Method + " request failed with a response status of " + (int)failureStatus + " " + reasonPhrase + ".", m_owner));
            SIPResponse failureResponse = SIPTransport.GetResponse(m_transaction.TransactionRequest, failureStatus, reasonPhrase);
            m_transaction.SendFinalResponse(failureResponse);
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI)
        {
            throw new NotImplementedException();
        }

        public void NoCDR()
        {
            throw new NotImplementedException();
        }

        public void SetTraceDelegate(SIPTransactionTraceMessageDelegate traceDelegate)
        {
            traceDelegate(m_transaction, SIPMonitorEventTypesEnum.SIPTransaction + "=>" + m_transaction.TransactionRequest.Method + " request received " + m_transaction.LocalSIPEndPoint +
                "<-" + m_transaction.RemoteEndPoint + "\r\n" + m_transaction.TransactionRequest.ToString());
        }

        public void SetOwner(string owner, string adminMemberId)
        {
            m_owner = owner;
            m_adminMemberId = adminMemberId;
        }

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.SIPTransaction, message, null));
        }

        public void SetDialPlanContextID(Guid dialPlanContextID)
        {
           
        }
    }
}
