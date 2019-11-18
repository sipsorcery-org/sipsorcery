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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// Implementation of a SIP Server User Agent that can be used to receive SIP calls.
    /// </summary>
    public class SIPServerUserAgent : ISIPServerUserAgent
    {
        private static ILogger logger = Log.Logger;

        private SIPMonitorLogDelegate Log_External = (e) => { }; //SIPMonitorEvent.DefaultSIPMonitorLogger;
        private SIPAuthenticateRequestDelegate SIPAuthenticateRequest_External;
        private GetSIPAccountDelegate GetSIPAccount_External;

        private SIPTransport m_sipTransport;
        private UASInviteTransaction m_uasTransaction;
        private SIPEndPoint m_outboundProxy;                   // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private bool m_isAuthenticated;
        private bool m_isCancelled;
        private bool m_isHungup;
        private string m_owner;
        private string m_adminMemberId;
        private string m_sipUsername;
        private string m_sipDomain;

        public bool IsB2B { get { return false; } }
        public bool IsInvite
        {
            get { return true; }
        }
        public string Owner { get { return m_owner; } }

        /// <summary>
        /// Call direction for this user agent.
        /// </summary>
        public SIPCallDirection CallDirection { get; private set; } = SIPCallDirection.In;

        /// <summary>
        /// The SIP dialog that's created if we're able to successfully answer the call request.
        /// </summary>
        public SIPDialogue SIPDialogue { get; private set; }

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

        public bool IsHungup
        {
            get { return m_isHungup; }
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
            GetSIPAccountDelegate getSIPAccount,
            SIPAuthenticateRequestDelegate sipAuthenticateRequest,
            SIPMonitorLogDelegate logDelegate,
            UASInviteTransaction uasTransaction)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_sipUsername = sipUsername;
            m_sipDomain = sipDomain;
            CallDirection = callDirection;
            GetSIPAccount_External = getSIPAccount;
            SIPAuthenticateRequest_External = sipAuthenticateRequest;
            Log_External = logDelegate ?? Log_External;
            m_uasTransaction = uasTransaction;

            m_uasTransaction.TransactionTraceMessage += TransactionTraceMessage;
            m_uasTransaction.UASInviteTransactionTimedOut += ClientTimedOut;
            m_uasTransaction.UASInviteTransactionCancelled += UASTransactionCancelled;
            m_uasTransaction.TransactionRemoved += new SIPTransactionRemovedDelegate(UASTransaction_TransactionRemoved);
        }

        private void UASTransaction_TransactionRemoved(SIPTransaction sipTransaction)
        {
            TransactionComplete?.Invoke(this);
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
                    m_sipAccount = GetSIPAccount_External(m_sipUsername, m_sipDomain);

                    if (m_sipAccount == null)
                    {
                        // A full lookup failed. Now try a partial lookup if the incoming username is in a dotted domain name format.
                        if (m_sipUsername.Contains("."))
                        {
                            string sipUsernameSuffix = m_sipUsername.Substring(m_sipUsername.LastIndexOf(".") + 1);
                            m_sipAccount = GetSIPAccount_External(sipUsernameSuffix, m_sipDomain);
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
                logger.LogError("Exception LoadSIPAccountForIncomingCall. " + excp.Message);
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
                    m_sipAccount = GetSIPAccount_External(m_sipUsername, m_sipDomain);

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

                            SetOwner(m_sipAccount.Owner, m_sipAccount.AdminMemberId);
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
                logger.LogError("Exception SIPServerUserAgent AuthenticateCall. " + excp.Message);
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
                        UASStateChanged?.Invoke(this, progressStatus, reasonPhrase);

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

                            m_uasTransaction.SendProvisionalResponse(progressResponse);
                        }
                    }
                }
                else
                {
                    logger.LogWarning("SIPServerUserAgent Progress fired on already answered call.");
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPServerUserAgent Progress. " + excp.Message);
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
                    UASStateChanged?.Invoke(this, SIPResponseStatusCodesEnum.Ok, null);

                    if (!toTag.IsNullOrBlank())
                    {
                        m_uasTransaction.LocalTag = toTag;
                    }

                    SIPResponse okResponse = m_uasTransaction.GetOkResponse(m_uasTransaction.TransactionRequest, m_uasTransaction.TransactionRequest.LocalSIPEndPoint, contentType, body);

                    if (body != null)
                    {
                        okResponse.Header.ContentType = contentType;
                        okResponse.Header.ContentLength = body.Length;
                        okResponse.Body = body;
                    }

                    m_uasTransaction.SendFinalResponse(okResponse);

                    SIPDialogue = new SIPDialogue(m_uasTransaction, m_owner, m_adminMemberId);
                    SIPDialogue.TransferMode = transferMode;

                    return SIPDialogue;
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPServerUserAgent Answer. " + excp.Message);
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
                        UASStateChanged?.Invoke(this, failureStatus, reasonPhrase);

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
                    logger.LogWarning("SIPServerUserAgent Reject fired on already answered call.");
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPServerUserAgent Reject. " + excp.Message);
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
                logger.LogError("Exception SIPServerUserAgent Redirect. " + excp.Message);
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
        public async void Hangup(bool clientHungup)
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
                    // Cases found where the Contact in the INVITE was to a different protocol than the oringinal request.
                    var inviteContact = m_uasTransaction.TransactionRequest.Header.Contact.FirstOrDefault();
                    if (inviteContact == null)
                    {
                        logger.LogWarning("The Contact header on the INVITE request was missing, BYE request cannot be generated.");
                    }
                    else
                    {
                        SIPEndPoint localEndPoint = m_uasTransaction.LocalSIPEndPoint;
                        if (inviteContact.ContactURI.Protocol != localEndPoint.Protocol)
                        {
                            var lookupResult = await SIPDNSManager.ResolveAsync(inviteContact.ContactURI);
                            if (lookupResult == null || lookupResult.EndPointResults?.Count == 0)
                            {
                                throw new ApplicationException("The Contact header URI on the INVITE request could not be resolved, BYE request cannot be generated.");
                            }
                            else
                            {
                               IPEndPoint contactUriEndPoint = lookupResult.EndPointResults.Select(x => x.LookupEndPoint).First().GetIPEndPoint();

                                logger.LogDebug($"Contact header URI {inviteContact.ContactURI} resolved to {contactUriEndPoint}.");

                                SIPChannel sendingChannel = m_sipTransport.GetSIPChannelForDestination(inviteContact.ContactURI.Protocol, contactUriEndPoint);
                                localEndPoint = sendingChannel.GetLocalSIPEndPointForDestination(contactUriEndPoint.Address);
                            }
                        }

                        SIPRequest byeRequest = GetByeRequest(localEndPoint);

                        SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(byeRequest, null, localEndPoint, m_outboundProxy);
                        byeTransaction.NonInviteTransactionFinalResponseReceived += ByeServerFinalResponseReceived;
                        byeTransaction.SendReliableRequest();
                    }
                }
                catch (Exception excp)
                {
                    logger.LogError("Exception SIPServerUserAgent Hangup. " + excp.Message);
                    throw;
                }
            }
        }

        private void ByeServerFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentServer, SIPMonitorEventTypesEnum.DialPlan, "Response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + sipTransaction.TransactionRequest.URI.ToString() + ".", Owner));

                SIPNonInviteTransaction byeTransaction = sipTransaction as SIPNonInviteTransaction;
                byeTransaction.NonInviteTransactionFinalResponseReceived -= ByeServerFinalResponseReceived;

                if ((sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised) && SIPAccount != null)
                {
                    // Resend BYE with credentials.
                    SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                    SIPURI contactUri = sipResponse.Header.Contact.Any() ? sipResponse.Header.Contact[0].ContactURI : sipResponse.Header.From.FromURI;

                    authRequest.SetCredentials(SIPAccount.SIPUsername, SIPAccount.SIPPassword, contactUri.ToString(), SIPMethodsEnum.BYE.ToString());

                    SIPRequest authByeRequest = byeTransaction.TransactionRequest;
                    authByeRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
                    authByeRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;
                    authByeRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                    authByeRequest.Header.CSeq = authByeRequest.Header.CSeq + 1;

                    SIPNonInviteTransaction bTransaction = m_sipTransport.CreateNonInviteTransaction(authByeRequest, null, localSIPEndPoint, null);
                    bTransaction.SendReliableRequest();
                }
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.Error, "Exception ByServerFinalResponseReceived. " + excp.Message, Owner));
            }
        }

        private void UASTransactionCancelled(SIPTransaction sipTransaction)
        {
            logger.LogDebug("SIPServerUserAgent got cancellation request.");
            m_isCancelled = true;
            CallCancelled?.Invoke(this);
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
                logger.LogError("Exception ClientTimedOut. " + excp.Message);
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

        private SIPRequest GetByeRequest(SIPEndPoint localSIPEndPoint)
        {
            SIPRequest byeRequest = new SIPRequest(SIPMethodsEnum.BYE, SIPDialogue.RemoteTarget);
            SIPFromHeader byeFromHeader = SIPFromHeader.ParseFromHeader(SIPDialogue.LocalUserField.ToString());
            SIPToHeader byeToHeader = SIPToHeader.ParseToHeader(SIPDialogue.RemoteUserField.ToString());
            int cseq = SIPDialogue.CSeq + 1;

            SIPHeader byeHeader = new SIPHeader(byeFromHeader, byeToHeader, cseq, SIPDialogue.CallId);
            byeHeader.CSeqMethod = SIPMethodsEnum.BYE;
            byeRequest.Header = byeHeader;
            byeRequest.Header.Routes = SIPDialogue.RouteSet;
            byeRequest.Header.ProxySendFrom = SIPDialogue.ProxySendFrom;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
            byeRequest.Header.Vias.PushViaHeader(viaHeader);

            return byeRequest;
        }
    }
}
