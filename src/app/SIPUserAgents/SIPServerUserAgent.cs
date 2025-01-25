//-----------------------------------------------------------------------------
// Filename: SIPServerUserAgent.cs
//
// Description: Implementation of a SIP Server User Agent that can be used to receive SIP calls.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 22 Feb 2008	Aaron Clauson   Created, Hobart, Australia.
// rj2: added overloads for Answer/Reject/Redirect-methods with/out customHeader
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// Implementation of a SIP Server User Agent that can be used to receive SIP calls.
    /// </summary>
    public class SIPServerUserAgent : ISIPServerUserAgent
    {
        private static ILogger logger = Log.Logger;

        protected SIPTransport m_sipTransport;
        protected UASInviteTransaction m_uasTransaction;
        protected SIPEndPoint m_outboundProxy;                   // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        protected bool m_isAuthenticated;
        protected bool m_isCancelled;
        protected bool m_isHungup;
        protected SIPDialogueTransferModesEnum m_transferMode;

        public bool IsB2B { get; protected set; } = false;
        public bool IsInvite => true;
        public SIPCallDirection CallDirection => SIPCallDirection.In;

        /// <summary>
        /// The SIP dialog that's created if we're able to successfully answer the call request.
        /// </summary>
        public SIPDialogue SIPDialogue { get; private set; }

        protected ISIPAccount m_sipAccount;

        private SIPNonInviteTransaction m_byeTransaction;       // If the server call is hungup this transaction contains the BYE in case it needs to be resent.

        public ISIPAccount SIPAccount
        {
            get { return m_sipAccount; }
            set { m_sipAccount = value; }
        }

        public bool IsAuthenticated
        {
            get { return m_isAuthenticated; }
            set { m_isAuthenticated = value; }
        }

        public bool IsHangingUp => m_byeTransaction?.DeliveryPending ?? false;
        public bool IsCancelled => m_isCancelled;
        public SIPRequest CallRequest => m_uasTransaction.TransactionRequest;
        public string CallDestination => m_uasTransaction.TransactionRequest.URI.User;
        public bool IsUASAnswered => m_uasTransaction != null && m_uasTransaction.TransactionFinalResponse != null;
        public bool IsHungup => m_isHungup;
        public UASInviteTransaction ClientTransaction => m_uasTransaction;

        /// <summary>
        /// The Session Description Protocol offer from the remote call party.
        /// </summary>
        public SDP OfferSDP
        {
            get
            {
                if (!string.IsNullOrEmpty(m_uasTransaction.TransactionRequest.Body))
                {
                    return SDP.ParseSDPDescription(m_uasTransaction.TransactionRequest.Body);
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// The caller cancelled the call request.
        /// </summary>
        public event SIPUASCancelDelegate CallCancelled;

        /// <summary>
        /// This end of the call timed out providing a ringing response. This situation can occur for SIP servers.
        /// They will attempt to forward the call to a SIP account's contacts. If none reply then the will never
        /// continue past the trying stage.
        /// </summary>
        public event SIPUASDelegate NoRingTimeout;

        /// <summary>
        /// Gets fired when the call successfully negotiates an SDP offer/answer and creates a new dialog.
        /// Typically this can occur at the same time as the transaction final response is sent. But in cases
        /// where the initial INVITE does not contain an SDP offer the dialog will not be created until the 
        /// ACK is received.
        /// </summary>
        public event Action<SIPDialogue> OnDialogueCreated;

        public SIPServerUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            UASInviteTransaction uasTransaction,
            ISIPAccount sipAccount)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_uasTransaction = uasTransaction;
            m_sipAccount = sipAccount;

            m_uasTransaction.UASInviteTransactionFailed += ClientTransactionFailed;
            m_uasTransaction.UASInviteTransactionCancelled += UASTransactionCancelled;
        }

        public bool AuthenticateCall()
        {
            m_isAuthenticated = false;

            try
            {
                if (m_sipAccount == null)
                {
                    logger.LogRejectingAuthCall(m_uasTransaction.TransactionRequestFrom);
                    Reject(SIPResponseStatusCodesEnum.Forbidden, null, null);
                }
                else
                {
                    SIPRequest sipRequest = m_uasTransaction.TransactionRequest;
                    SIPEndPoint localSIPEndPoint = (!sipRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedOn) : sipRequest.LocalSIPEndPoint;
                    SIPEndPoint remoteEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : sipRequest.RemoteSIPEndPoint;

                    var authenticationResult = SIPRequestAuthenticator.AuthenticateSIPRequest(localSIPEndPoint, remoteEndPoint, sipRequest, m_sipAccount);
                    if (authenticationResult.Authenticated)
                    {
                        if (authenticationResult.WasAuthenticatedByIP)
                        {
                            logger.LogCallAuthenticatedByIP(remoteEndPoint);
                        }
                        else
                        {
                            logger.LogCallAuthenticatedByDigest(remoteEndPoint);
                        }

                        m_isAuthenticated = true;
                    }
                    else
                    {
                        if (sipRequest.Header.HasAuthenticationHeader)
                        {
                            logger.LogCallNotAuthenticated(m_sipAccount.SIPUsername, m_sipAccount.SIPDomain, authenticationResult.ErrorResponse);
                        }

                        // Send authorisation failure or required response
                        SIPResponse authReqdResponse = SIPResponse.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                        authReqdResponse.Header.AuthenticationHeaders.Add(authenticationResult.AuthenticationRequiredHeader);
                        m_uasTransaction.SendFinalResponse(authReqdResponse);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogAuthenticateCallError(excp.Message, excp);
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
                        logger.LogInvalidUASResponse(progressStatus, (int)progressStatus);
                    }
                    else
                    {
                        //UASStateChanged?.Invoke(this, progressStatus, reasonPhrase);

                        // Allow all Trying responses through as some may contain additional useful information on the call state for the caller. 
                        // Also if the response is a 183 Session Progress with audio forward it.
                        if (m_uasTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding && progressStatus != SIPResponseStatusCodesEnum.Trying &&
                            !(progressStatus == SIPResponseStatusCodesEnum.SessionProgress && progressBody != null))
                        {
                            logger.LogIgnoreProgressResponse(progressStatus, (int)progressStatus, m_uasTransaction.TransactionState);
                        }
                        else
                        {
                            logger.LogUASCallProgressing(progressStatus);
                            SIPResponse progressResponse = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, progressStatus, reasonPhrase);

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

                            m_uasTransaction.SendProvisionalResponse(progressResponse);
                        }
                    }
                }
                else
                {
                    logger.LogProgressOnAnsweredCall();
                }
            }
            catch (Exception excp)
            {
                logger.LogProgressError(excp.Message, excp);
            }
        }

        public SIPDialogue Answer(string contentType, string body, SIPDialogueTransferModesEnum transferMode)
        {
            return Answer(contentType, body, null, transferMode);
        }

        public SIPDialogue Answer(string contentType, string body, SIPDialogueTransferModesEnum transferMode, string[] customHeaders)
        {
            return Answer(contentType, body, null, transferMode, customHeaders);
        }

        public SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogueTransferModesEnum transferMode)
        {
            return Answer(contentType, body, toTag, transferMode, null);
        }

        public SIPDialogue Answer(string contentType, string body, string toTag, SIPDialogueTransferModesEnum transferMode, string[] customHeaders)
        {
            try
            {
                m_transferMode = transferMode;

                if (m_uasTransaction.TransactionFinalResponse != null)
                {
                    logger.LogUASAlreadyAnswered();
                    return null;
                }
                else
                {
                    //UASStateChanged?.Invoke(this, SIPResponseStatusCodesEnum.Ok, null);

                    if (!toTag.IsNullOrBlank())
                    {
                        m_uasTransaction.LocalTag = toTag;
                    }

                    SIPResponse okResponse = m_uasTransaction.GetOkResponse(contentType, body);

                    if (body != null)
                    {
                        okResponse.Header.ContentType = contentType;
                        okResponse.Header.ContentLength = body.Length;
                        okResponse.Body = body;
                    }
                    if (customHeaders != null && customHeaders.Length > 0)
                    {
                        foreach (string header in customHeaders)
                        {
                            okResponse.Header.UnknownHeaders.Add(header);
                        }
                    }

                    if (OfferSDP == null)
                    {
                        // The INVITE request did not contain an SDP offer. We need to send the offer in the response and
                        // then get the answer from the ACK.
                        m_uasTransaction.OnAckReceived += OnAckAnswerReceived;
                    }

                    m_uasTransaction.SendFinalResponse(okResponse);

                    if (OfferSDP != null)
                    {
                        SIPDialogue = new SIPDialogue(m_uasTransaction);
                        SIPDialogue.TransferMode = transferMode;

                        OnDialogueCreated?.Invoke(SIPDialogue);

                        return SIPDialogue;
                    }
                    else
                    {
                        // The dialogue cannot be created until the ACK is received.
                        return null;
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogUASAnswerError(excp.Message, excp);
                throw;
            }
        }

        private Task<SocketError> OnAckAnswerReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            SIPDialogue = new SIPDialogue(m_uasTransaction);
            SIPDialogue.TransferMode = m_transferMode;

            OnDialogueCreated?.Invoke(SIPDialogue);

            return Task.FromResult(SocketError.Success);
        }

        public void AnswerNonInvite(SIPResponseStatusCodesEnum answerStatus, string reasonPhrase, string[] customHeaders, string contentType, string body)
        {
            throw new NotImplementedException();
        }

        public void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase)
        {
            Reject(failureStatus, reasonPhrase, null);
        }

        public void Reject(SIPResponseStatusCodesEnum failureStatus, string reasonPhrase, string[] customHeaders)
        {
            try
            {
                if (m_uasTransaction.TransactionFinalResponse == null)
                {
                    if ((int)failureStatus < 400)
                    {
                        logger.LogUASRejectInvalidResponseStatus(failureStatus, (int)failureStatus);
                    }
                    else
                    {
                        //UASStateChanged?.Invoke(this, failureStatus, reasonPhrase);

                        string failureReason = (!reasonPhrase.IsNullOrBlank()) ? " and " + reasonPhrase : null;

                        logger.LogUASCallFailed((int)failureStatus, failureReason);
                        SIPResponse failureResponse = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, failureStatus, reasonPhrase);

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
                    logger.LogAlreadyRejected();
                }
            }
            catch (Exception excp)
            {
                logger.LogUASRejectError(excp.Message, excp);
            }
        }

        public Task<SocketError> SendNonInviteRequest(SIPRequest sipRequest)
        {
            try
            {
                SIPNonInviteTransaction nonInvteTransaction = new SIPNonInviteTransaction(m_sipTransport, sipRequest, m_outboundProxy);
                nonInvteTransaction.SendRequest();
                return Task.FromResult(SocketError.Success);
            }
            catch (Exception excp)
            {
                logger.LogSendNonInviteRequestError(excp.Message, excp);
                return Task.FromResult(SocketError.Fault);
            }
        }
        
        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI)
        {
            Redirect(redirectCode, redirectURI, null);
        }

        public void Redirect(SIPResponseStatusCodesEnum redirectCode, SIPURI redirectURI, string[] customHeaders)
        {
            try
            {
                if (m_uasTransaction.TransactionFinalResponse == null)
                {
                    SIPResponse redirectResponse = SIPResponse.GetResponse(m_uasTransaction.TransactionRequest, redirectCode, null);
                    redirectResponse.Header.Contact = SIPContactHeader.CreateSIPContactList(redirectURI);

                    if (customHeaders != null && customHeaders.Length > 0)
                    {
                        foreach (string header in customHeaders)
                        {
                            redirectResponse.Header.UnknownHeaders.Add(header);
                        }
                    }

                    m_uasTransaction.SendFinalResponse(redirectResponse);
                }
            }
            catch (Exception excp)
            {
                logger.LogSIPServerUserAgentRedirectError(excp.Message, excp);
            }
        }

        public void NoCDR()
        {
            m_uasTransaction.CDR = null;
        }

        /// <summary>
        /// Used to hangup the call or indicate that the client hungup.
        /// </summary>
        /// <param name="clientHungup">True if the BYE request was received from the client. False if the hangup
        /// needs to originate from this agent.</param>
        public void Hangup(bool clientHungup)
        {
            if (!m_isHungup)
            {
                m_isHungup = true;

                if (SIPDialogue == null)
                {
                    return;
                }

                // Only need to send a BYE request if the client didn't already do so.
                if (clientHungup == false)
                {
                    try
                    {
                        // Cases found where the Contact in the INVITE was to a different protocol than the original request.
                        var inviteContact = m_uasTransaction.TransactionRequest.Header.Contact.FirstOrDefault();
                        if (inviteContact == null)
                        {
                            logger.LogMissingContactHeader();
                        }
                        else
                        {
                            var byeRequest = SIPDialogue.GetInDialogRequest(SIPMethodsEnum.BYE);
                            m_byeTransaction = new SIPNonInviteTransaction(m_sipTransport, byeRequest, m_outboundProxy);
                            m_byeTransaction.NonInviteTransactionFinalResponseReceived += ByeServerFinalResponseReceived;
                            m_byeTransaction.SendRequest();
                        }
                    }
                    catch (Exception excp)
                    {
                        logger.LogSIPServerUserAgentHangupError(excp.Message, excp);
                        throw;
                    }
                }
            }
        }

        private Task<SocketError> ByeServerFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                logger.LogResponse(sipResponse.StatusCode, sipResponse.ReasonPhrase, sipTransaction.TransactionRequest.URI);

                SIPNonInviteTransaction byeTransaction = sipTransaction as SIPNonInviteTransaction;

                if ((sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised) && SIPAccount != null)
                {
                    var authRequest = sipTransaction.TransactionRequest.DuplicateAndAuthenticate(sipResponse.Header.AuthenticationHeaders,
                                SIPAccount.SIPUsername, SIPAccount.SIPPassword);
                    SIPNonInviteTransaction authByeTransaction = new SIPNonInviteTransaction(m_sipTransport, authRequest, null);
                    authByeTransaction.SendRequest();
                }

                return Task.FromResult(SocketError.Success);
            }
            catch (Exception excp)
            {
                logger.LogByServerFinalResponseError(excp.Message, excp);
                return Task.FromResult(SocketError.Fault);
            }
        }

        private void UASTransactionCancelled(SIPTransaction sipTransaction, SIPRequest sipCancelRequest)
        {
            logger.LogServerUserAgentCancellationRequest();
            m_isCancelled = true;
            CallCancelled?.Invoke(this, sipCancelRequest);
        }

        private void ClientTransactionFailed(SIPTransaction sipTransaction, SocketError failureReason)
        {
            logger.LogServerUserAgentClientFailed(failureReason, m_uasTransaction.TransactionState);

            if (sipTransaction.HasTimedOut)
            {
                NoRingTimeout?.Invoke(this);
            }
        }
    }
}
