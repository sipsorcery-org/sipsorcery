// ============================================================================
// FileName: SIPNonInviteServerUserAgent.cs
//
// Description:
// A user agent that can process non-invite requests such as MESSAGEs. The 
// purpose of having a user agent for such requests is so they can be passed to 
// a dial plan for processing.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 Apr 2011	Aaron Clauson	Created, Hobart Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SIPNonInviteServerUserAgent : ISIPServerUserAgent
    {
        private static ILogger logger = Log.Logger;

        private SIPTransport m_sipTransport;
        private SIPNonInviteTransaction m_transaction;
        private SIPEndPoint m_outboundProxy;                   // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private bool m_isAuthenticated;

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

        private ISIPAccount m_sipAccount;
        public ISIPAccount SIPAccount
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

        public bool IsB2B => false;
        public bool IsInvite => false;
        public SIPRequest CallRequest => m_transaction.TransactionRequest;
        public string CallDestination => m_transaction.TransactionRequest.URI.User;
        public bool IsUASAnswered => false;

        public UASInviteTransaction ClientTransaction => throw new NotImplementedException();

#pragma warning disable CS0067
        public event SIPUASDelegate CallCancelled;
        public event SIPUASDelegate NoRingTimeout;
        public event SIPUASDelegate TransactionComplete;
#pragma warning restore

        public SIPNonInviteServerUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            string sipUsername,
            string sipDomain,
            SIPCallDirection callDirection,
            ISIPAccount sipAccount,
            SIPNonInviteTransaction transaction)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_sipUsername = sipUsername;
            m_sipDomain = sipDomain;
            m_sipCallDirection = callDirection;
            m_sipAccount = sipAccount;
            m_transaction = transaction;

            //m_transaction.TransactionTraceMessage += TransactionTraceMessage;
            //m_uasTransaction.UASInviteTransactionTimedOut += ClientTimedOut;
            //m_uasTransaction.UASInviteTransactionCancelled += UASTransactionCancelled;
            //m_uasTransaction.TransactionRemoved += new SIPTransactionRemovedDelegate(UASTransaction_TransactionRemoved);
            //m_uasTransaction.TransactionStateChanged += (t) => { logger.Debug("Transaction state change to " + t.TransactionState + ", uri=" + t.TransactionRequestURI.ToString() + "."); };
        }

        public bool AuthenticateCall()
        {
            m_isAuthenticated = false;

            try
            {
                if (m_sipAccount == null)
                {
                    // If no SIP account specified then the assumption is authentication is not required.
                    m_isAuthenticated = true;
                }
                else
                {
                    SIPRequest sipRequest = m_transaction.TransactionRequest;
                    SIPEndPoint localSIPEndPoint = (!sipRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedOn) : sipRequest.LocalSIPEndPoint;
                    SIPEndPoint remoteEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : sipRequest.RemoteSIPEndPoint;

                    SIPRequestAuthenticationResult authenticationResult = SIPRequestAuthenticator.AuthenticateSIPRequest(localSIPEndPoint, remoteEndPoint, sipRequest, m_sipAccount);
                    if (authenticationResult.Authenticated)
                    {
                        if (authenticationResult.WasAuthenticatedByIP)
                        {
                            logger.LogDebug(m_transaction.TransactionRequest.Method + " request from " + remoteEndPoint.ToString() + " successfully authenticated by IP address.");
                        }
                        else
                        {
                            logger.LogDebug(m_transaction.TransactionRequest.Method + " request from " + remoteEndPoint.ToString() + " successfully authenticated by digest.");
                        }

                        m_isAuthenticated = true;
                    }
                    else
                    {
                        // Send authorisation failure or required response
                        SIPResponse authReqdResponse = SIPResponse.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                        authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                        logger.LogDebug(m_transaction.TransactionRequest.Method + " request not authenticated for " + m_sipUsername + "@" + m_sipDomain + ", responding with " + authenticationResult.ErrorResponse + ".");
                        m_transaction.SendResponse(authReqdResponse);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPNonInviteUserAgent AuthenticateCall. " + excp.Message);
                Reject(SIPResponseStatusCodesEnum.InternalServerError, null, null);
            }

            return m_isAuthenticated;
        }

        public void AnswerNonInvite(SIPResponseStatusCodesEnum answerStatus, string reasonPhrase, string[] customHeaders, string contentType, string body)
        {
            logger.LogDebug(m_transaction.TransactionRequest.Method + " request succeeded with a response status of " + (int)answerStatus + " " + reasonPhrase + ".");
            SIPResponse answerResponse = SIPResponse.GetResponse(m_transaction.TransactionRequest, answerStatus, reasonPhrase);

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

            m_transaction.SendResponse(answerResponse);
        }

        public void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase)
        {
            Reject(failureStatus, reasonPhrase, null);
        }

        public void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase, string[] customHeaders)
        {
            logger.LogDebug(m_transaction.TransactionRequest.Method + " request failed with a response status of " + (int)failureStatus + " " + reasonPhrase + ".");
            SIPResponse failureResponse = SIPResponse.GetResponse(m_transaction.TransactionRequest, failureStatus, reasonPhrase);
            m_transaction.SendResponse(failureResponse);
        }

        public void NoCDR()
        {
            throw new NotImplementedException();
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

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI, string[] customHeaders)
        {
            throw new NotImplementedException();
        }

        public void Progress(SIPResponseStatusCodesEnum progressStatus, string reasonPhrase, string[] customHeaders, string progressContentType, string progressBody)
        {
            throw new NotImplementedException();
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI)
        {
            throw new NotImplementedException();
        }
    }
}
