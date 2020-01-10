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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SIPClientUserAgent : ISIPClientUserAgent
    {
        private const char OUTBOUNDPROXY_AS_ROUTESET_CHAR = '<';    // If this character exists in the call descriptor OutboundProxy setting it gets treated as a Route set.

        private static ILogger logger = Log.Logger;

        private static string m_userAgent = SIPConstants.SIP_USERAGENT_STRING;
        private static readonly int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static readonly string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;

        private SIPTransport m_sipTransport;
        private SIPMonitorLogDelegate Log_External;

        public string Owner { get; private set; }                   // If the UAC is authenticated holds the username of the client.
        public string AdminMemberId { get; private set; }           // If the UAC is authenticated holds the username of the client.

        // Real-time call control properties.
        public string AccountCode { get; set; }
        public decimal ReservedCredit { get; set; }
        public int ReservedSeconds { get; set; }
        public decimal Rate { get; set; }

        private SIPCallDescriptor m_sipCallDescriptor;              // Describes the server leg of the call from the sipswitch.
        private SIPEndPoint m_serverEndPoint;
        private UACInviteTransaction m_serverTransaction;
        private bool m_callCancelled;                               // It's possible for the call to be cancelled before the INVITE has been sent. This could occur if a DNS lookup on the server takes a while.
        private bool m_hungupOnCancel;                              // Set to true if a call has been cancelled AND and then an Ok response was received AND a BYE has been sent to hang it up. This variable is used to stop another BYE transaction being generated.
        private int m_serverAuthAttempts;                           // Used to determine if credentials for a server leg call fail.
        private SIPNonInviteTransaction m_cancelTransaction;        // If the server call is cancelled this transaction contains the CANCEL in case it needs to be resent.
        private SIPEndPoint m_outboundProxy;                        // If the system needs to use an outbound proxy for every request this will be set and overrides any user supplied values.
        private SIPDialogue m_sipDialogue;

        private RtccGetCustomerDelegate RtccGetCustomer_External;
        private RtccGetRateDelegate RtccGetRate_External;
        private RtccGetBalanceDelegate RtccGetBalance_External;
        private RtccReserveInitialCreditDelegate RtccReserveInitialCredit_External;
        private RtccUpdateCdrDelegate RtccUpdateCdr_External;

        public event SIPCallResponseDelegate CallTrying;
        public event SIPCallResponseDelegate CallRinging;
        public event SIPCallResponseDelegate CallAnswered;
        public event SIPCallFailedDelegate CallFailed;

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
            Log_External = Log_External = (ev) => logger.LogDebug(ev?.Message);
        }

        public SIPClientUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            string owner,
            string adminMemberId,
            SIPMonitorLogDelegate logDelegate)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy?.CopyOf();
            Owner = owner;
            AdminMemberId = adminMemberId;
            Log_External = logDelegate;

            // If external logging is not required assign an empty handler to stop null reference exceptions.
            if (Log_External == null)
            {
                Log_External = (e) => { };
            }
        }

        public SIPClientUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            string owner,
            string adminMemberId,
            SIPMonitorLogDelegate logDelegate,
            RtccGetCustomerDelegate rtccGetCustomer,
            RtccGetRateDelegate rtccGetRate,
            RtccGetBalanceDelegate rtccGetBalance,
            RtccReserveInitialCreditDelegate rtccReserveInitialCredit,
            RtccUpdateCdrDelegate rtccUpdateCdr
            ) : this(sipTransport, outboundProxy, owner, adminMemberId, logDelegate)
        {
            RtccGetCustomer_External = rtccGetCustomer;
            RtccGetRate_External = rtccGetRate;
            RtccGetBalance_External = rtccGetBalance;
            RtccReserveInitialCredit_External = rtccReserveInitialCredit;
            RtccUpdateCdr_External = rtccUpdateCdr;
        }

        /// <summary>
        /// Gets the desintation of the remote SIP end point for this call.
        /// </summary>
        /// <param name="sipCallDescriptor">The call descriptor containing the settings to use to place the call.</param>
        /// <returns>The server end point for the call.</returns>
        public SIPEndPoint GetCallDestination(SIPCallDescriptor sipCallDescriptor)
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
                SIPEndPoint outboundProxyEndPoint = SIPEndPoint.ParseSIPEndPoint(sipCallDescriptor.ProxySendFrom);
                m_outboundProxy = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(outboundProxyEndPoint.Address, m_defaultSIPPort));
                m_serverEndPoint = m_outboundProxy;
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "SIPClientUserAgent Call using alternate outbound proxy of " + m_outboundProxy + ".", Owner));
            }
            else if (m_outboundProxy != null)
            {
                // Using the system outbound proxy only, no additional user routing requirements.
                serverEndPoint = m_outboundProxy;
            }

            // No outbound proxy, determine the forward destination based on the SIP request.
            if (serverEndPoint == null)
            {
                SIPDNSLookupResult lookupResult = null;

                if (sipCallDescriptor.RouteSet != null && sipCallDescriptor.RouteSet.IndexOf(OUTBOUNDPROXY_AS_ROUTESET_CHAR) != -1)
                {
                    var routeSet = new SIPRouteSet();
                    routeSet.PushRoute(new SIPRoute(sipCallDescriptor.RouteSet, true));
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Route set for call " + routeSet.ToString() + ".", Owner));
                    lookupResult = m_sipTransport.GetURIEndPoint(routeSet.TopRoute.URI, false);
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Attempting to resolve " + callURI.Host + ".", Owner));
                    lookupResult = m_sipTransport.GetURIEndPoint(callURI, false);
                }

                if (lookupResult.LookupError != null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "DNS error resolving " + callURI.Host + ", " + lookupResult.LookupError + ". Call cannot proceed.", Owner));
                }
                else
                {
                    serverEndPoint = lookupResult.GetSIPEndPoint();
                }
            }

            return serverEndPoint;
        }

        /// <summary>
        /// Initiates the call to the remote user agent server.
        /// </summary>
        /// <param name="sipCallDescriptor">The descriptor for the call that describes how to reach the user agent server and other properties.</param>
        public SIPRequest Call(SIPCallDescriptor sipCallDescriptor)
        {
            try
            {
                m_sipCallDescriptor = sipCallDescriptor;
                SIPURI callURI = SIPURI.ParseSIPURI(sipCallDescriptor.Uri);
                SIPRouteSet routeSet = null;

                if (!m_callCancelled)
                {
                    m_serverEndPoint = GetCallDestination(sipCallDescriptor);

                    if (m_callCancelled)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Call was cancelled during DNS resolution of " + callURI.Host, Owner));
                        CallFailed?.Invoke(this, "Cancelled by caller");
                    }
                    else if (m_serverEndPoint != null)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Switching to " + SIPURI.ParseSIPURI(m_sipCallDescriptor.Uri).CanonicalAddress + " via " + m_serverEndPoint + ".", Owner));

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
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Error an outbound proxy value was not recognised in SIPClientUserAgent Call. " + m_sipCallDescriptor.RouteSet + ".", Owner));
                            }
                        }

                        string content = sipCallDescriptor.Content;

                        if (content.IsNullOrBlank())
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Body on UAC call was empty.", Owner));
                        }
                        else if (m_sipCallDescriptor.ContentType == m_sdpContentType)
                        {
                            if (!m_sipCallDescriptor.MangleResponseSDP)
                            {
                                IPEndPoint sdpEndPoint = SDP.GetSDPRTPEndPoint(content);
                                if (sdpEndPoint != null)
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC call was set to NOT mangle, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                                }
                                else
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC call was set to NOT mangle, RTP socket could not be determined.", Owner));
                                }
                            }
                            else
                            {
                                IPEndPoint sdpEndPoint = SDP.GetSDPRTPEndPoint(content);
                                if (sdpEndPoint != null)
                                {
                                    if (!IPSocket.IsPrivateAddress(sdpEndPoint.Address.ToString()))
                                    {
                                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC call had public IP not mangled, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                                    }
                                    else
                                    {
                                        bool wasSDPMangled = false;
                                        if (sipCallDescriptor.MangleIPAddress != null)
                                        {
                                            if (sdpEndPoint != null)
                                            {
                                                content = SIPPacketMangler.MangleSDP(content, sipCallDescriptor.MangleIPAddress.ToString(), out wasSDPMangled);
                                            }
                                        }

                                        if (wasSDPMangled)
                                        {
                                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC call had RTP socket mangled from " + sdpEndPoint.ToString() + " to " + sipCallDescriptor.MangleIPAddress.ToString() + ":" + sdpEndPoint.Port + ".", Owner));
                                        }
                                        else if (sdpEndPoint != null)
                                        {
                                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC could not be mangled, using original RTP socket of " + sdpEndPoint.ToString() + ".", Owner));
                                        }
                                    }
                                }
                                else
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP RTP socket on UAC call could not be determined.", Owner));
                                }
                            }
                        }

                        if (this.m_sipCallDescriptor.BranchId.IsNullOrBlank())
                        {
                            this.m_sipCallDescriptor.BranchId = CallProperties.CreateBranchId();
                        }
                        if (this.m_sipCallDescriptor.CallId.IsNullOrBlank())
                        {
                            this.m_sipCallDescriptor.CallId = CallProperties.CreateNewCallId();
                        }

                        SIPRequest switchServerInvite = GetInviteRequest(m_sipCallDescriptor, m_sipCallDescriptor.BranchId, m_sipCallDescriptor.CallId, routeSet, content, sipCallDescriptor.ContentType);

                        // Now that we have a destination socket create a new UAC transaction for forwarded leg of the call.
                        m_serverTransaction = new UACInviteTransaction(m_sipTransport, switchServerInvite, m_outboundProxy);
                        m_serverTransaction.CDR.DialPlanContextID = m_sipCallDescriptor.DialPlanContextID;

                        #region Real-time call control processing.

                        string rtccError = null;

                        if (m_serverTransaction.CDR != null)
                        {
                            m_serverTransaction.CDR.Owner = Owner;
                            m_serverTransaction.CDR.AdminMemberId = AdminMemberId;

                            m_serverTransaction.CDR.Updated();

                            if (m_sipCallDescriptor.AccountCode != null && RtccGetCustomer_External != null)
                            {
                                //var customerAccount = m_customerAccountDataLayer.CheckAccountCode(Owner, m_sipCallDescriptor.AccountCode);
                                var customerAccount = RtccGetCustomer_External(Owner, m_sipCallDescriptor.AccountCode);

                                if (customerAccount == null)
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A billable call could not proceed as no account exists for account code or number " + m_sipCallDescriptor.AccountCode + ".", Owner));
                                    logger.LogDebug("A billable call could not proceed as no account exists for account code or number " + m_sipCallDescriptor.AccountCode + " and owner " + Owner + ".");
                                    rtccError = "Real-time call control invalid account code";
                                }
                                else
                                {
                                    AccountCode = customerAccount.AccountCode;

                                    string rateDestination = m_sipCallDescriptor.Uri;
                                    if (SIPURI.TryParse(m_sipCallDescriptor.Uri, out var rateDstUri))
                                    {
                                        rateDestination = rateDstUri.User;
                                    }

                                    //var rate = m_customerAccountDataLayer.GetRate(Owner, m_sipCallDescriptor.RateCode, rateDestination, customerAccount.RatePlan);
                                    var rate = RtccGetRate_External(Owner, m_sipCallDescriptor.RateCode, rateDestination, customerAccount.RatePlan);

                                    if (rate == null)
                                    {
                                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A billable call could not proceed as no rate could be determined for destination " + rateDestination + ".", Owner));
                                        logger.LogDebug("A billable call could not proceed as no rate could be determined for destination " + rateDestination + " and owner " + Owner + ".");
                                        rtccError = "Real-time call control no rate";
                                    }
                                    else
                                    {
                                        Rate = rate.RatePerIncrement;

                                        if (rate.RatePerIncrement == 0 && rate.SetupCost == 0)
                                        {
                                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "The rate and setup cost for the " + rateDestination + "were both zero. The call will be allowed to proceed with no RTCC reservation.", Owner));
                                        }
                                        else
                                        {
                                            //decimal balance = m_customerAccountDataLayer.GetBalance(AccountCode);
                                            decimal balance = RtccGetBalance_External(AccountCode);

                                            if (balance < Rate)
                                            {
                                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A billable call could not proceed as the available credit for " + AccountCode + " was not sufficient for 60 seconds to destination " + rateDestination + ".", Owner));
                                                logger.LogDebug("A billable call could not proceed as the available credit for " + AccountCode + " was not sufficient for 60 seconds to destination " + rateDestination + " and owner " + Owner + ".");
                                                rtccError = "Real-time call control insufficient credit";
                                            }
                                            else
                                            {
                                                int intialSeconds = 0;
                                                //var reservationCost = m_customerAccountDataLayer.ReserveInitialCredit(AccountCode, rate, m_serverTransaction.CDR, out intialSeconds);
                                                var reservationCost = RtccReserveInitialCredit_External(AccountCode, rate.ID, m_serverTransaction.CDR, out intialSeconds);

                                                if (reservationCost == Decimal.MinusOne)
                                                {
                                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Call will not proceed as the intial real-time call control credit reservation failed.", Owner));
                                                    logger.LogDebug("Call will not proceed as the intial real-time call control credit reservation failed for owner " + Owner + ".");
                                                    rtccError = "Real-time call control initial reservation failed";
                                                }
                                                else
                                                {
                                                    ReservedCredit = reservationCost;
                                                    ReservedSeconds = intialSeconds;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        #endregion

                        if (rtccError == null)
                        {
                            m_serverTransaction.UACInviteTransactionInformationResponseReceived += ServerInformationResponseReceived;
                            m_serverTransaction.UACInviteTransactionFinalResponseReceived += ServerFinalResponseReceived;
                            m_serverTransaction.UACInviteTransactionTimedOut += ServerTimedOut;
                            m_serverTransaction.TransactionTraceMessage += TransactionTraceMessage;

                            m_serverTransaction.SendInviteRequest();
                        }
                        else
                        {
                            m_serverTransaction.CancelCall(rtccError);
                            CallFailed?.Invoke(this, rtccError);
                        }

                        return switchServerInvite;
                    }
                    else
                    {
                        if (routeSet == null || routeSet.Length == 0)
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Forward leg failed, could not resolve URI host " + callURI.Host, Owner));
                            m_serverTransaction?.CancelCall("Unresolvable destination " + callURI.Host);
                            CallFailed?.Invoke(this, "unresolvable destination " + callURI.Host);
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Forward leg failed, could not resolve top Route host " + routeSet.TopRoute.Host, Owner));
                            m_serverTransaction?.CancelCall("Unresolvable destination " + routeSet.TopRoute.Host);
                            CallFailed?.Invoke(this, "unresolvable destination " + routeSet.TopRoute.Host);
                        }
                    }
                }
            }
            catch (ApplicationException appExcp)
            {
                m_serverTransaction?.CancelCall(appExcp.Message);
                CallFailed?.Invoke(this, appExcp.Message);
            }
            catch (AggregateException aggExcp)
            {
                foreach (var innerExcp in aggExcp.InnerExceptions)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Exception UserAgentClient Call. " + innerExcp.Message, Owner));
                }
                m_serverTransaction?.CancelCall($"Aggregate exception, inner exception count {aggExcp.InnerExceptions.Count}.");
                CallFailed?.Invoke(this, aggExcp.InnerExceptions.First().Message);
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Exception UserAgentClient Call. " + excp.Message, Owner));
                m_serverTransaction?.CancelCall("Unknown exception");
                CallFailed?.Invoke(this, excp.Message);
            }
            return null;
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
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Cancelling forwarded call leg " + m_sipCallDescriptor.Uri + ", server transaction has not been created yet no CANCEL request required.", Owner));
                }
                else if (m_cancelTransaction != null)
                {
                    if (m_cancelTransaction.TransactionState != SIPTransactionStatesEnum.Completed)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Call " + m_serverTransaction.TransactionRequest.URI.ToString() + " has already been cancelled once, trying again.", Owner));
                        m_cancelTransaction.SendRequest();
                    }
                    else
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Call " + m_serverTransaction.TransactionRequest.URI.ToString() + " has already responded to CANCEL, probably overlap in messages not re-sending.", Owner));
                    }
                }
                else //if (m_serverTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding || m_serverTransaction.TransactionState == SIPTransactionStatesEnum.Trying)
                {
                    //logger.LogDebug("Cancelling forwarded call leg, sending CANCEL to " + ForwardedTransaction.TransactionRequest.URI.ToString() + " (transid: " + ForwardedTransaction.TransactionId + ").");
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Cancelling forwarded call leg, sending CANCEL to " + m_serverTransaction.TransactionRequest.URI.ToString() + ".", Owner));

                    // No reponse has been received from the server so no CANCEL request neccessary, stop any retransmits of the INVITE.
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
                    m_cancelTransaction.TransactionTraceMessage += TransactionTraceMessage;
                    m_cancelTransaction.SendRequest();
                }

                CallFailed?.Invoke(this, "Call cancelled by user.");
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Exception CancelServerCall. " + excp.Message, Owner));
            }
        }

        public void Update(CRMHeaders crmHeaders)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Sending UPDATE to " + m_serverTransaction.TransactionRequest.URI.ToString() + ".", Owner));

                SIPRequest updateRequest = GetUpdateRequest(m_serverTransaction.TransactionRequest, crmHeaders);
                SIPNonInviteTransaction updateTransaction = new SIPNonInviteTransaction(m_sipTransport, updateRequest, m_outboundProxy);
                updateTransaction.TransactionTraceMessage += TransactionTraceMessage;
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
                SIPRequest byeRequest = GetByeRequest(m_serverTransaction.TransactionFinalResponse, m_sipDialogue.RemoteTarget);
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
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + m_serverTransaction.TransactionRequest.URI.ToString() + ".", Owner));

                m_serverTransaction.UACInviteTransactionInformationResponseReceived -= ServerInformationResponseReceived;
                m_serverTransaction.UACInviteTransactionFinalResponseReceived -= ServerFinalResponseReceived;
                m_serverTransaction.TransactionTraceMessage -= TransactionTraceMessage;

                if (m_callCancelled && sipResponse.Status == SIPResponseStatusCodesEnum.RequestTerminated)
                {
                    // No action required. Correctly received request terminated on an INVITE we cancelled.
                }
                else if (m_callCancelled)
                {
                    #region Call has been cancelled, hangup.

                    if (m_hungupOnCancel)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "A cancelled call to " + m_sipCallDescriptor.Uri + " has been answered AND has already been hungup, no further action being taken.", Owner));
                    }
                    else
                    {
                        m_hungupOnCancel = true;

                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "A cancelled call to " + m_sipCallDescriptor.Uri + " has been answered, hanging up.", Owner));

                        if (sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0)
                        {
                            SIPURI byeURI = sipResponse.Header.Contact[0].ContactURI;
                            SIPRequest byeRequest = GetByeRequest(sipResponse, byeURI);
                            SIPNonInviteTransaction byeTransaction = new SIPNonInviteTransaction(m_sipTransport, byeRequest, m_outboundProxy);
                            byeTransaction.SendRequest();
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "No contact header provided on response for cancelled call to " + m_sipCallDescriptor.Uri + " no further action.", Owner));
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
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Forward leg failed, authentication was requested but no credentials were available.", Owner));
                            CallFailed?.Invoke(this, "Authentication requested when no credentials available");
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
                                m_serverTransaction.CDR.Owner = Owner;
                                m_serverTransaction.CDR.AdminMemberId = AdminMemberId;
                                m_serverTransaction.CDR.DialPlanContextID = m_sipCallDescriptor.DialPlanContextID;

                                m_serverTransaction.CDR.Updated();

                                if (AccountCode != null)
                                {
                                    RtccUpdateCdr_External?.Invoke(originalCallTransaction.CDR?.CDRId.ToString(), m_serverTransaction.CDR);
                                }

                                logger.LogDebug("RTCC reservation was reallocated from CDR " + originalCallTransaction.CDR?.CDRId + " to " + m_serverTransaction.CDR?.CDRId + " for owner " + Owner + ".");
                            }
                            m_serverTransaction.UACInviteTransactionInformationResponseReceived += ServerInformationResponseReceived;
                            m_serverTransaction.UACInviteTransactionFinalResponseReceived += ServerFinalResponseReceived;
                            m_serverTransaction.UACInviteTransactionTimedOut += ServerTimedOut;
                            m_serverTransaction.TransactionTraceMessage += TransactionTraceMessage;

                            m_serverTransaction.SendInviteRequest();
                        }
                        else
                        {
                            CallFailed?.Invoke(this, "Authentication with provided credentials failed");
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
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Body on UAC response was empty.", Owner));
                        }
                        else if (m_sipCallDescriptor.ContentType == m_sdpContentType)
                        {
                            if (!m_sipCallDescriptor.MangleResponseSDP)
                            {
                                IPEndPoint sdpEndPoint = SDP.GetSDPRTPEndPoint(sipResponse.Body);
                                string sdpSocket = (sdpEndPoint != null) ? sdpEndPoint.ToString() : "could not determine";
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC response was set to NOT mangle, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                            }
                            else
                            {
                                //m_callInProgress = false; // the call is now established
                                //logger.LogDebug("Final response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + ForwardedTransaction.TransactionRequest.URI.ToString() + ".");
                                // Determine of response SDP should be mangled.

                                IPEndPoint sdpEndPoint = SDP.GetSDPRTPEndPoint(sipResponse.Body);
                                //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "UAC response SDP was mangled from sdp=" + sdpEndPoint.Address.ToString() + ", proxyfrom=" + sipResponse.Header.ProxyReceivedFrom + ", mangle=" + m_sipCallDescriptor.MangleResponseSDP + ".", null));
                                if (sdpEndPoint != null)
                                {
                                    if (!IPSocket.IsPrivateAddress(sdpEndPoint.Address.ToString()))
                                    {
                                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC response had public IP not mangled, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                                    }
                                    else
                                    {
                                        bool wasSDPMangled = false;
                                        string publicIPAddress = null;

                                        if (!sipResponse.Header.ProxyReceivedFrom.IsNullOrBlank())
                                        {
                                            IPAddress remoteUASAddress = SIPEndPoint.ParseSIPEndPoint(sipResponse.Header.ProxyReceivedFrom).Address;
                                            if (IPSocket.IsPrivateAddress(remoteUASAddress.ToString()) && m_sipCallDescriptor.MangleIPAddress != null)
                                            {
                                                // If the response has arrived here on a private IP address then it must be
                                                // for a local version install and an incoming call that needs it's response mangled.
                                                if (!IPSocket.IsPrivateAddress(m_sipCallDescriptor.MangleIPAddress.ToString()))
                                                {
                                                    publicIPAddress = m_sipCallDescriptor.MangleIPAddress.ToString();
                                                }
                                            }
                                            else
                                            {
                                                publicIPAddress = remoteUASAddress.ToString();
                                            }
                                        }
                                        else if (!IPSocket.IsPrivateAddress(remoteEndPoint.Address.ToString()) && remoteEndPoint.Address != IPAddress.Any)
                                        {
                                            publicIPAddress = remoteEndPoint.Address.ToString();
                                        }
                                        else if (m_sipCallDescriptor.MangleIPAddress != null)
                                        {
                                            publicIPAddress = m_sipCallDescriptor.MangleIPAddress.ToString();
                                        }

                                        if (publicIPAddress != null)
                                        {
                                            sipResponse.Body = SIPPacketMangler.MangleSDP(sipResponse.Body, publicIPAddress, out wasSDPMangled);
                                        }

                                        if (wasSDPMangled)
                                        {
                                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC response had RTP socket mangled from " + sdpEndPoint.ToString() + " to " + publicIPAddress + ":" + sdpEndPoint.Port + ".", Owner));
                                        }
                                        else if (sdpEndPoint != null)
                                        {
                                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP on UAC response could not be mangled, RTP socket " + sdpEndPoint.ToString() + ".", Owner));
                                        }
                                    }
                                }
                                else
                                {
                                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "SDP RTP socket on UAC response could not be determined.", Owner));
                                }
                            }
                        }

                        m_sipDialogue = new SIPDialogue(m_serverTransaction, Owner, AdminMemberId);
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
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.Error, "Exception ServerFinalResponseReceived. " + excp.Message, Owner));
                return Task.FromResult(SocketError.Fault);
            }
        }

        private Task<SocketError> ServerInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Information response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + m_serverTransaction.TransactionRequest.URI.ToString() + ".", Owner));

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
                CallFailed?.Invoke(this, "Timeout, no response from server");
            }
        }

        private Task<SocketError> ByeServerFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.DialPlan, "Response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " for " + sipTransaction.TransactionRequest.URI.ToString() + ".", Owner));

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
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.Error, string.Concat("Exception ByServerFinalResponseReceived. ", excp.Message), this.Owner));
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
            inviteHeader.Supported = (PrackSupported == true) ? SIPExtensionHeaders.PRACK : null; // Let the uas know whether or not we're supporting reliable provisional responses.

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

        private void TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.SIPTransaction, message, Owner));
        }

        private SIPEndPoint GetRemoteTargetEndpoint()
        {
            SIPURI dstURI = (m_sipDialogue.RouteSet == null) ? m_sipDialogue.RemoteTarget : m_sipDialogue.RouteSet.TopRoute.URI;
            return dstURI.ToSIPEndPoint();
        }
    }
}