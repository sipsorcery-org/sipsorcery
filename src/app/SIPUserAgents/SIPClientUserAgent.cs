//-----------------------------------------------------------------------------
// Filename: SIPClientUserAgent.cs
//
// Description: Implementation of a SIP Client User Agent that can be used to initiate SIP calls.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 22 Feb 2008	Aaron Clauson   Created, Hobart, Australia.
// 30 Oct 2019  Aaron Clauson   Added support for reliable provisional responses as per RFC3262.
// rj2: use CallID,BranchId from CallDescriptor in Call-method
// rj2: return SIPRequest in Call-method
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SIPClientUserAgent : ISIPClientUserAgent
    {
        private const char OUTBOUNDPROXY_AS_ROUTESET_CHAR = '<';    // If this character exists in the call descriptor OutboundProxy setting it gets treated as a Route set.

        private static ILogger logger = Log.Logger;

        private static string m_userAgent = SIPConstants.SIP_USERAGENT_STRING;

        private SIPTransport m_sipTransport;

        private SIPCallDescriptor m_sipCallDescriptor;              // Describes the server leg of the call from the sipswitch.
        //private SIPEndPoint m_serverEndPoint;
        private UACInviteTransaction m_serverTransaction;
        private bool m_callCancelled;                               // It's possible for the call to be cancelled before the INVITE has been sent. This could occur if a DNS lookup on the server takes a while.
        private bool m_hungupOnCancel;                              // Set to true if a call has been cancelled AND and then an OK response was received AND a BYE has been sent to hang it up. This variable is used to stop another BYE transaction being generated.
        private int m_serverAuthAttempts;                           // Used to determine if credentials for a server leg call fail.
        internal SIPNonInviteTransaction m_cancelTransaction;        // If the server call is cancelled this transaction contains the CANCEL in case it needs to be resent.
        private SIPEndPoint m_outboundProxy;                        // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private SIPDialogue m_sipDialogue;

        public event SIPCallResponseDelegate CallTrying;
        public event SIPCallResponseDelegate CallRinging;
        public event SIPCallResponseDelegate CallAnswered;
        public event SIPCallFailedDelegate CallFailed;

        public Func<SIPRequest, SIPRequest> AdjustInvite;

        public UACInviteTransaction ServerTransaction
        {
            get { return m_serverTransaction; }
        }

        public bool IsUACAnswered
        {
            get { return m_serverTransaction.TransactionFinalResponse != null; }
        }

        public SIPDialogue SIPDialogue
        {
            get { return m_sipDialogue; }
        }

        public SIPCallDescriptor CallDescriptor
        {
            get { return m_sipCallDescriptor; }
        }

        public SIPCallDescriptor SipCallDescriptor { get => m_sipCallDescriptor; set => m_sipCallDescriptor = value; }

        /// <summary>
        /// Determines whether the agent will operate with support for reliable provisional responses as per RFC3262.
        /// If support is not desired it should be set to false before the initial INVITE request is sent.
        /// </summary>
        public bool PrackSupported { get; set; } = true;

        /// <summary>
        /// Creates a new SIP user agent client to act as the client on a SIP INVITE transaction.
        /// </summary>
        /// <param name="sipTransport">The SIP transport this user agent will use for sending and receiving SIP messages.</param>
        public SIPClientUserAgent(SIPTransport sipTransport)
        {
            m_sipTransport = sipTransport;
        }

        public SIPClientUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy?.CopyOf();
        }

        /// <summary>
        /// Gets the destination of the remote SIP end point for this call.
        /// </summary>
        /// <param name="sipCallDescriptor">The call descriptor containing the settings to use to place the call.</param>
        /// <returns>The server end point for the call.</returns>
        public async Task<SIPEndPoint> GetCallDestination(SIPCallDescriptor sipCallDescriptor)
        {
            SIPURI callURI = SIPURI.ParseSIPURI(sipCallDescriptor.Uri);
            SIPEndPoint serverEndPoint = null;

            // If the outbound proxy is a loopback address, as it will normally be for local deployments, then it cannot be overriden.
            if (m_outboundProxy != null && IPAddress.IsLoopback(m_outboundProxy.Address))
            {
                serverEndPoint = m_outboundProxy;
            }
            else if (!sipCallDescriptor.ProxySendFrom.IsNullOrBlank())
            {
                // If the binding has a specific proxy end point sent then the request needs to be forwarded to the proxy's default end point for it to take care of.
                //SIPEndPoint outboundProxyEndPoint = SIPEndPoint.ParseSIPEndPoint(sipCallDescriptor.ProxySendFrom);
                //m_outboundProxy = new SIPEndPoint(SIPProtocolsEnum.udp, outboundProxyEndPoint.Address, SIPConstants.DEFAULT_SIP_PORT);
                //m_serverEndPoint = m_outboundProxy;
                m_outboundProxy = SIPEndPoint.ParseSIPEndPoint(sipCallDescriptor.ProxySendFrom);
                serverEndPoint = m_outboundProxy;
                logger.LogDebug($"SIPClientUserAgent Call using alternate outbound proxy of {serverEndPoint}.");
            }
            else if (m_outboundProxy != null)
            {
                // Using the system outbound proxy only, no additional user routing requirements.
                serverEndPoint = m_outboundProxy;
            }

            // No outbound proxy, determine the forward destination based on the SIP request.
            if (serverEndPoint == null)
            {
                //SIPDNSLookupResult lookupResult = null;
                SIPEndPoint lookupResult = null;
                double lookupDurationMilliseconds = 0;

                if (sipCallDescriptor.RouteSet != null && sipCallDescriptor.RouteSet.IndexOf(OUTBOUNDPROXY_AS_ROUTESET_CHAR) != -1)
                {
                    var routeSet = new SIPRouteSet();
                    routeSet.PushRoute(new SIPRoute(sipCallDescriptor.RouteSet, true));
                    logger.LogDebug("Route set for call " + routeSet.ToString() + ".");
                    //lookupResult = m_sipTransport.GetURIEndPoint(routeSet.TopRoute.URI, false);
                    lookupResult = await m_sipTransport.ResolveSIPUriAsync(routeSet.TopRoute.URI).ConfigureAwait(false);
                }
                else
                {
                    logger.LogDebug("SIPClientUserAgent attempting to resolve " + callURI.Host + ".");
                    //lookupResult = m_sipTransport.GetURIEndPoint(callURI, false);
                    DateTime lookupStartedAt = DateTime.Now;
                    lookupResult = await m_sipTransport.ResolveSIPUriAsync(callURI).ConfigureAwait(false);
                    lookupDurationMilliseconds = DateTime.Now.Subtract(lookupStartedAt).TotalMilliseconds;
                }

                if (lookupResult == null)
                {
                    logger.LogDebug($"SIPClientUserAgent DNS failure resolving {callURI.Host} in {lookupDurationMilliseconds:0.##}ms. Call cannot proceed.");
                }
                else
                {
                    logger.LogDebug($"SIPClientUserAgent resolved {callURI.Host} to {lookupResult} in {lookupDurationMilliseconds:0.##}ms.");
                    serverEndPoint = lookupResult;
                }
            }

            return serverEndPoint;
        }

        public SIPRequest Call(SIPCallDescriptor sipCallDescriptor)
        {
            return Call(sipCallDescriptor, null);
        }

        /// <summary>
        /// Initiates the call to the remote user agent server.
        /// </summary>
        /// <param name="sipCallDescriptor">The descriptor for the call that describes how to reach the user agent server and other properties.</param>
        /// <param name="serverEndPoint">Optional. If the server end point for the call is known or has been resolved in advance. If
        /// not set the SIP transport layer will attempt to resolve the destination at sending time.</param>
        public SIPRequest Call(SIPCallDescriptor sipCallDescriptor, SIPEndPoint serverEndPoint)
        {
            try
            {
                m_sipCallDescriptor = sipCallDescriptor;
                SIPURI callURI = SIPURI.ParseSIPURI(sipCallDescriptor.Uri);
                SIPRouteSet routeSet = null;

                logger.LogDebug($"UAC commencing call to {SIPURI.ParseSIPURI(m_sipCallDescriptor.Uri).CanonicalAddress}.");

                // A custom route set may have been specified for the call.
                if (m_sipCallDescriptor.RouteSet != null && m_sipCallDescriptor.RouteSet.IndexOf(OUTBOUNDPROXY_AS_ROUTESET_CHAR) != -1)
                {
                    try
                    {
                        routeSet = new SIPRouteSet();
                        routeSet.PushRoute(new SIPRoute(m_sipCallDescriptor.RouteSet, true));
                    }
                    catch
                    {
                        logger.LogDebug("Error an outbound proxy value was not recognised in SIPClientUserAgent Call. " + m_sipCallDescriptor.RouteSet + ".");
                    }
                }

                string content = sipCallDescriptor.Content;

                if (content.IsNullOrBlank())
                {
                    logger.LogDebug("Body on UAC call was empty.");
                }

                if (this.m_sipCallDescriptor.BranchId.IsNullOrBlank())
                {
                    this.m_sipCallDescriptor.BranchId = CallProperties.CreateBranchId();
                }

                if (this.m_sipCallDescriptor.CallId.IsNullOrBlank())
                {
                    this.m_sipCallDescriptor.CallId = CallProperties.CreateNewCallId();
                }

                SIPRequest inviteRequest = GetInviteRequest(m_sipCallDescriptor, m_sipCallDescriptor.BranchId, m_sipCallDescriptor.CallId, routeSet, content, sipCallDescriptor.ContentType);

                // Now that we have a destination socket create a new UAC transaction for forwarded leg of the call.
                m_serverTransaction = new UACInviteTransaction(m_sipTransport, inviteRequest, m_outboundProxy);
                m_serverTransaction.CDR.DialPlanContextID = m_sipCallDescriptor.DialPlanContextID;

                m_serverTransaction.UACInviteTransactionInformationResponseReceived += ServerInformationResponseReceived;
                m_serverTransaction.UACInviteTransactionFinalResponseReceived += ServerFinalResponseReceived;
                m_serverTransaction.UACInviteTransactionTimedOut += ServerTimedOut;

                m_serverTransaction.SendInviteRequest();

                return inviteRequest;
            }
            catch (ApplicationException appExcp)
            {
                m_serverTransaction?.CancelCall(appExcp.Message);
                CallFailed?.Invoke(this, appExcp.Message, null);
                return null;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UserAgentClient Call. " + excp.Message);
                m_serverTransaction?.CancelCall("Unknown exception");
                CallFailed?.Invoke(this, excp.Message, null);
                return null;
            }
        }

        /// <summary>
        /// Cancels an in progress call. This method should be called prior to the remote user agent server answering the call.
        /// </summary>
        public void Cancel()
        {
            try
            {
                m_callCancelled = true;

                // Cancel server call.
                if (m_serverTransaction == null)
                {
                    logger.LogDebug("Cancelling forwarded call leg " + m_sipCallDescriptor.Uri + ", server transaction has not been created yet no CANCEL request required.");
                }
                else if (m_cancelTransaction != null)
                {
                    if (m_cancelTransaction.TransactionState != SIPTransactionStatesEnum.Completed)
                    {
                        logger.LogDebug("Call " + m_serverTransaction.TransactionRequest.URI.ToString() + " has already been cancelled once, trying again.");
                        m_cancelTransaction.SendRequest();
                    }
                    else
                    {
                        logger.LogDebug("Call " + m_serverTransaction.TransactionRequest.URI.ToString() + " has already responded to CANCEL, probably overlap in messages not re-sending.");
                    }
                }
                else //if (m_serverTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding || m_serverTransaction.TransactionState == SIPTransactionStatesEnum.Trying)
                {
                    logger.LogDebug("Cancelling forwarded call leg, sending CANCEL to " + m_serverTransaction.TransactionRequest.URI.ToString() + ".");

                    // No response has been received from the server so no CANCEL request necessary, stop any retransmits of the INVITE.
                    m_serverTransaction.CancelCall();

                    SIPRequest cancelRequest = GetCancelRequest(m_serverTransaction.TransactionRequest);

                    // If auth header is included inside INVITE request, we re-include them inside CANCEL request
                    if (m_serverTransaction.TransactionRequest.Header.AuthenticationHeader != null)
                    {
                        string username = (m_sipCallDescriptor.AuthUsername == null || m_sipCallDescriptor.AuthUsername.Trim().Length <= 0 ? m_sipCallDescriptor.Username : m_sipCallDescriptor.AuthUsername);
                        SIPAuthorisationDigest authDigest = m_serverTransaction.TransactionRequest.Header.AuthenticationHeader.SIPDigest;
                        authDigest.SetCredentials(username, m_sipCallDescriptor.Password, m_sipCallDescriptor.Uri, SIPMethodsEnum.CANCEL.ToString());

                        cancelRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authDigest);
                        cancelRequest.Header.AuthenticationHeader.SIPDigest.IncrementNonceCount();
                        cancelRequest.Header.AuthenticationHeader.SIPDigest.Response = authDigest.Digest;
                    }

                    m_cancelTransaction = new SIPNonInviteTransaction(m_sipTransport, cancelRequest, m_outboundProxy);
                    m_cancelTransaction.SendRequest();
                }

                CallFailed?.Invoke(this, "Call cancelled by user.", null);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception CancelServerCall. " + excp.Message);
            }
        }

        public void Update(CRMHeaders crmHeaders)
        {
            try
            {
                logger.LogDebug("Sending UPDATE to " + m_serverTransaction.TransactionRequest.URI.ToString() + ".");

                SIPRequest updateRequest = GetUpdateRequest(m_serverTransaction.TransactionRequest, crmHeaders);
                SIPNonInviteTransaction updateTransaction = new SIPNonInviteTransaction(m_sipTransport, updateRequest, m_outboundProxy);
                updateTransaction.SendRequest();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPClientUserAgent Update. " + excp.Message);
            }
        }

        public void Hangup()
        {
            if (m_sipDialogue == null)
            {
                return;
            }

            try
            {
                //SIPRequest byeRequest = GetByeRequest(m_serverTransaction.TransactionFinalResponse, m_sipDialogue.RemoteTarget);
                SIPRequest byeRequest = m_sipDialogue.GetInDialogRequest(SIPMethodsEnum.BYE);
                byeRequest.SetSendFromHints(m_serverTransaction.TransactionRequest.LocalSIPEndPoint);
                SIPNonInviteTransaction byeTransaction = new SIPNonInviteTransaction(m_sipTransport, byeRequest, m_outboundProxy);
                byeTransaction.NonInviteTransactionFinalResponseReceived += ByeServerFinalResponseReceived;
                byeTransaction.NonInviteTransactionTimedOut += (tx) => logger.LogDebug($"Bye request for {m_sipCallDescriptor.Uri} timed out.");
                byeTransaction.SendRequest();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPClientUserAgent Hangup. " + excp.Message);
            }
        }

        private Task<SocketError> ServerFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                logger.LogDebug("Response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + m_serverTransaction.TransactionRequest.URI.ToString() + ".");

                m_serverTransaction.UACInviteTransactionInformationResponseReceived -= ServerInformationResponseReceived;
                m_serverTransaction.UACInviteTransactionFinalResponseReceived -= ServerFinalResponseReceived;

                if (m_callCancelled && sipResponse.Status == SIPResponseStatusCodesEnum.RequestTerminated)
                {
                    // No action required. Correctly received request terminated on an INVITE we cancelled.
                }
                else if (m_callCancelled)
                {
                    #region Call has been cancelled, hangup.

                    if (m_hungupOnCancel)
                    {
                        logger.LogDebug("A cancelled call to " + m_sipCallDescriptor.Uri + " has been answered AND has already been hungup, no further action being taken.");
                    }
                    else
                    {
                        m_hungupOnCancel = true;

                        logger.LogDebug("A cancelled call to " + m_sipCallDescriptor.Uri + " has been answered, hanging up.");

                        if (sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0)
                        {
                            SIPURI byeURI = sipResponse.Header.Contact[0].ContactURI;
                            SIPRequest byeRequest = GetByeRequest(sipResponse, byeURI);
                            SIPNonInviteTransaction byeTransaction = new SIPNonInviteTransaction(m_sipTransport, byeRequest, m_outboundProxy);
                            byeTransaction.SendRequest();
                        }
                        else
                        {
                            logger.LogDebug("No contact header provided on response for cancelled call to " + m_sipCallDescriptor.Uri + " no further action.");
                        }
                    }

                    #endregion
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    #region Authenticate client call to third party server.

                    if (!m_callCancelled)
                    {
                        if (m_sipCallDescriptor.Password.IsNullOrBlank())
                        {
                            // No point trying to authenticate if there is no password to use.
                            logger.LogDebug("Forward leg failed, authentication was requested but no credentials were available.");
                            CallFailed?.Invoke(this, "Authentication requested when no credentials available", sipResponse);
                        }
                        else if (m_serverAuthAttempts == 0)
                        {
                            m_serverAuthAttempts = 1;

                            // Resend INVITE with credentials.
                            string username = (m_sipCallDescriptor.AuthUsername != null && m_sipCallDescriptor.AuthUsername.Trim().Length > 0) ? m_sipCallDescriptor.AuthUsername : m_sipCallDescriptor.Username;
                            SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                            authRequest.SetCredentials(username, m_sipCallDescriptor.Password, m_sipCallDescriptor.Uri, SIPMethodsEnum.INVITE.ToString());

                            SIPRequest authInviteRequest = m_serverTransaction.TransactionRequest;
                            authInviteRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
                            authInviteRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;
                            authInviteRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                            authInviteRequest.Header.CSeq = authInviteRequest.Header.CSeq + 1;

                            // Create a new UAC transaction to establish the authenticated server call.
                            var originalCallTransaction = m_serverTransaction;
                            m_serverTransaction = new UACInviteTransaction(m_sipTransport, authInviteRequest, m_outboundProxy);
                            if (m_serverTransaction.CDR != null)
                            {
                                m_serverTransaction.CDR.DialPlanContextID = m_sipCallDescriptor.DialPlanContextID;
                                m_serverTransaction.CDR.Updated();
                            }
                            m_serverTransaction.UACInviteTransactionInformationResponseReceived += ServerInformationResponseReceived;
                            m_serverTransaction.UACInviteTransactionFinalResponseReceived += ServerFinalResponseReceived;
                            m_serverTransaction.UACInviteTransactionTimedOut += ServerTimedOut;

                            m_serverTransaction.SendInviteRequest();
                        }
                        else
                        {
                            CallFailed?.Invoke(this, "Authentication with provided credentials failed", sipResponse);
                        }
                    }

                    #endregion
                }
                else
                {
                    if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
                    {
                        if (sipResponse.Body.IsNullOrBlank())
                        {
                            logger.LogDebug("Body on UAC response was empty.");
                        }

                        m_sipDialogue = new SIPDialogue(m_serverTransaction);
                        m_sipDialogue.CallDurationLimit = m_sipCallDescriptor.CallDurationLimit;

                        m_sipDialogue.CRMPersonName = sipResponse.Header.CRMPersonName;
                        m_sipDialogue.CRMCompanyName = sipResponse.Header.CRMCompanyName;
                        m_sipDialogue.CRMPictureURL = sipResponse.Header.CRMPictureURL;
                    }

                    CallAnswered?.Invoke(this, sipResponse);
                }

                return Task.FromResult(SocketError.Success);
            }
            catch (Exception excp)
            {
                logger.LogDebug("Exception ServerFinalResponseReceived. " + excp.Message);
                return Task.FromResult(SocketError.Fault);
            }
        }

        private Task<SocketError> ServerInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            logger.LogDebug("Information response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + m_serverTransaction.TransactionRequest.URI.ToString() + ".");

            if (m_callCancelled)
            {
                // Call was cancelled in the interim.
                Cancel();
            }
            else
            {
                if (sipResponse.Status == SIPResponseStatusCodesEnum.Ringing || sipResponse.Status == SIPResponseStatusCodesEnum.SessionProgress)
                {
                    CallRinging?.Invoke(this, sipResponse);
                }
                else
                {
                    CallTrying?.Invoke(this, sipResponse);
                }
            }

            return Task.FromResult(SocketError.Success);
        }

        private void ServerTimedOut(SIPTransaction sipTransaction)
        {
            if (!m_callCancelled)
            {
                CallFailed?.Invoke(this, "Timeout, no response from server", null);
            }
        }

        private Task<SocketError> ByeServerFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                logger.LogDebug("Response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + sipTransaction.TransactionRequest.URI.ToString() + ".");

                SIPNonInviteTransaction transaction = sipTransaction as SIPNonInviteTransaction;
                transaction.NonInviteTransactionFinalResponseReceived -= ByeServerFinalResponseReceived;

                if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    string username = (m_sipCallDescriptor.AuthUsername == null || m_sipCallDescriptor.AuthUsername.Trim().Length <= 0 ? m_sipCallDescriptor.Username : m_sipCallDescriptor.AuthUsername);
                    SIPAuthorisationDigest authDigest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                    authDigest.SetCredentials(username, m_sipCallDescriptor.Password, m_sipCallDescriptor.Uri, SIPMethodsEnum.BYE.ToString());

                    SIPRequest authRequest = transaction.TransactionRequest;
                    authRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authDigest);
                    authRequest.Header.AuthenticationHeader.SIPDigest.Response = authDigest.Digest;
                    authRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                    authRequest.Header.CSeq++;

                    SIPNonInviteTransaction authByeTransaction = new SIPNonInviteTransaction(m_sipTransport, authRequest, m_outboundProxy);
                    authByeTransaction.NonInviteTransactionTimedOut += (tx) => logger.LogDebug($"Authenticated Bye request for {m_sipCallDescriptor.Uri} timed out.");
                    authByeTransaction.SendRequest();
                }

                return Task.FromResult(SocketError.Success);
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception ByServerFinalResponseReceived. {excp.Message}");
                return Task.FromResult(SocketError.Fault);
            }
        }

        private SIPRequest GetInviteRequest(SIPCallDescriptor sipCallDescriptor, string branchId, string callId, SIPRouteSet routeSet, string content, string contentType)
        {
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, sipCallDescriptor.Uri);

            SIPHeader inviteHeader = new SIPHeader(sipCallDescriptor.GetFromHeader(), SIPToHeader.ParseToHeader(sipCallDescriptor.To), 1, callId);

            inviteHeader.From.FromTag = CallProperties.CreateNewTag();

            inviteHeader.Contact = new List<SIPContactHeader>() { SIPContactHeader.GetDefaultSIPContactHeader() };
            inviteHeader.Contact[0].ContactURI.User = sipCallDescriptor.Username;
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteHeader.UserAgent = m_userAgent;
            inviteHeader.Routes = routeSet;
            inviteHeader.Supported = SIPExtensionHeaders.REPLACES + ", " + SIPExtensionHeaders.NO_REFER_SUB
                + ((PrackSupported == true) ? ", " + SIPExtensionHeaders.PRACK : "");

            inviteRequest.Header = inviteHeader;

            if (!sipCallDescriptor.ProxySendFrom.IsNullOrBlank())
            {
                inviteHeader.ProxySendFrom = sipCallDescriptor.ProxySendFrom;
            }

            SIPViaHeader viaHeader = new SIPViaHeader(new IPEndPoint(IPAddress.Any, 0), branchId);
            inviteRequest.Header.Vias.PushViaHeader(viaHeader);

            inviteRequest.Body = content;
            inviteRequest.Header.ContentLength = (inviteRequest.Body != null) ? inviteRequest.Body.Length : 0;
            inviteRequest.Header.ContentType = contentType;

            // Add custom CRM headers.
            if (CallDescriptor.CRMHeaders != null)
            {
                inviteHeader.CRMPersonName = CallDescriptor.CRMHeaders.PersonName;
                inviteHeader.CRMCompanyName = CallDescriptor.CRMHeaders.CompanyName;
                inviteHeader.CRMPictureURL = CallDescriptor.CRMHeaders.AvatarURL;
            }

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
                        else if (customHeader.Trim().StartsWith(SIPHeaders.SIP_HEADER_TO + ":"))
                        {
                            var customToHeader = SIPUserField.ParseSIPUserField(customHeader.Substring(customHeader.IndexOf(":") + 1).Trim());
                            if (customToHeader != null)
                            {
                                inviteRequest.Header.To.ToUserField = customToHeader;
                            }
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

            if (AdjustInvite != null)
            {
                inviteRequest = AdjustInvite(inviteRequest);
            }

            return inviteRequest;
        }

        private SIPRequest GetCancelRequest(SIPRequest inviteRequest)
        {
            SIPRequest cancelRequest = new SIPRequest(SIPMethodsEnum.CANCEL, inviteRequest.URI);
            cancelRequest.SetSendFromHints(inviteRequest.LocalSIPEndPoint);

            SIPHeader inviteHeader = inviteRequest.Header;
            SIPHeader cancelHeader = new SIPHeader(inviteHeader.From, inviteHeader.To, inviteHeader.CSeq, inviteHeader.CallId);
            cancelRequest.Header = cancelHeader;
            cancelHeader.CSeqMethod = SIPMethodsEnum.CANCEL;
            cancelHeader.Routes = inviteHeader.Routes;
            cancelHeader.ProxySendFrom = inviteHeader.ProxySendFrom;
            cancelHeader.Vias = inviteHeader.Vias;

            return cancelRequest;
        }

        private SIPRequest GetByeRequest(SIPResponse inviteResponse, SIPURI byeURI)
        {
            SIPRequest byeRequest = new SIPRequest(SIPMethodsEnum.BYE, byeURI);
            byeRequest.SetSendFromHints(inviteResponse.LocalSIPEndPoint);

            SIPFromHeader byeFromHeader = inviteResponse.Header.From;
            SIPToHeader byeToHeader = inviteResponse.Header.To;
            int cseq = inviteResponse.Header.CSeq + 1;

            SIPHeader byeHeader = new SIPHeader(byeFromHeader, byeToHeader, cseq, inviteResponse.Header.CallId);
            byeHeader.CSeqMethod = SIPMethodsEnum.BYE;
            byeHeader.ProxySendFrom = m_serverTransaction.TransactionRequest.Header.ProxySendFrom;
            byeRequest.Header = byeHeader;
            byeRequest.Header.Routes = (inviteResponse.Header.RecordRoutes != null) ? inviteResponse.Header.RecordRoutes.Reversed() : null;
            byeRequest.Header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader());

            return byeRequest;
        }

        private SIPRequest GetUpdateRequest(SIPRequest inviteRequest, CRMHeaders crmHeaders)
        {
            SIPRequest updateRequest = new SIPRequest(SIPMethodsEnum.UPDATE, inviteRequest.URI);
            updateRequest.SetSendFromHints(inviteRequest.LocalSIPEndPoint);

            SIPHeader inviteHeader = inviteRequest.Header;
            SIPHeader updateHeader = new SIPHeader(inviteHeader.From, inviteHeader.To, inviteHeader.CSeq + 1, inviteHeader.CallId);
            inviteRequest.Header.CSeq++;
            updateRequest.Header = updateHeader;
            updateHeader.CSeqMethod = SIPMethodsEnum.UPDATE;
            updateHeader.Routes = inviteHeader.Routes;
            updateHeader.ProxySendFrom = inviteHeader.ProxySendFrom;

            SIPViaHeader viaHeader = new SIPViaHeader(inviteRequest.LocalSIPEndPoint, CallProperties.CreateBranchId());
            updateHeader.Vias.PushViaHeader(viaHeader);

            // Add custom CRM headers.
            if (crmHeaders != null)
            {
                updateHeader.CRMPersonName = crmHeaders.PersonName;
                updateHeader.CRMCompanyName = crmHeaders.CompanyName;
                updateHeader.CRMPictureURL = crmHeaders.AvatarURL;
            }

            return updateRequest;
        }

        private SIPEndPoint GetRemoteTargetEndpoint()
        {
            SIPURI dstURI = (m_sipDialogue.RouteSet == null) ? m_sipDialogue.RemoteTarget : m_sipDialogue.RouteSet.TopRoute.URI;
            return dstURI.ToSIPEndPoint();
        }
    }
}