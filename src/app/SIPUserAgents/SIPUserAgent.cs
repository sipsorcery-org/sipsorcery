//-----------------------------------------------------------------------------
// Filename: SIPUserAgent.cs
//
// Description: A "full" SIP user agent that encompasses both client and server user agents.
// It is also able to manage in dialog operations after the call is established 
// (the client and server user agents don't handle in dialog operations).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Nov 2019	Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    /// <summary>
    /// A "full" SIP user agent that encompasses both client and server user agents.
    /// It is also able to manage in dialog operations after the call is established 
    /// (the client and server user agents don't handle in dialog operations).
    /// 
    /// Unlike other user agents this one also manages its own RTP session object
    /// which means it can handle things like call on and off hold, RTP end point
    /// changes and sending DTMF events.
    /// </summary>
    public class SIPUserAgent
    {
        private static readonly string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;
        private static readonly string m_sipReferContentType = SIPMIMETypes.REFER_CONTENT_TYPE;
        private static string m_userAgent = SIPConstants.SIP_USERAGENT_STRING;

        private static ILogger logger = Log.Logger;

        /// <summary>
        /// Client user agent for placing calls.
        /// </summary>
        private SIPClientUserAgent m_uac;

        /// <summary>
        /// Server user agent for receiving calls.
        /// </summary>
        private SIPServerUserAgent m_uas;

        /// <summary>
        /// The SIP transport layer for sending requests and responses.
        /// </summary>
        private SIPTransport m_transport;

        /// <summary>
        /// If set all communications are sent to this address irrespective of what the 
        /// request and response headers indicate.
        /// </summary>
        private SIPEndPoint m_outboundProxy;

        /// <summary>
        /// The media (RTP) session in use for the current call.
        /// </summary>
        public IMediaSession MediaSession { get; private set; }

        /// <summary>
        /// Indicates whether there is an active call or not
        /// </summary>
        public bool IsCallActive
        {
            get
            {
                return Dialogue?.DialogueState == SIPDialogueStateEnum.Confirmed;
            }
        }

        /// <summary>
        /// Once either the client or server call is answered this will hold the SIP
        /// dialogue that was created by the call.
        /// </summary>
        public SIPDialogue Dialogue
        {
            get
            {
                if (m_uac != null)
                {
                    return m_uac.SIPDialogue;
                }

                return m_uas?.SIPDialogue;
            }
        }

        /// <summary>
        /// For a call initiated by us this is the call descriptor that was used.
        /// </summary>
        public SIPCallDescriptor CallDescriptor
        {
            get { return m_uac?.CallDescriptor; }
        }

        /// <summary>
        /// The remote party has received our call request and is working on it.
        /// </summary>
        public event SIPCallResponseDelegate ClientCallTrying;

        /// <summary>
        /// The remote party has progressed our call request to ringing/in progress.
        /// </summary>
        public event SIPCallResponseDelegate ClientCallRinging;

        /// <summary>
        /// The in progress call attempt was answered.
        /// </summary>
        public event SIPCallResponseDelegate ClientCallAnswered;

        /// <summary>
        /// The in progress call attempt failed.
        /// </summary>
        public event SIPCallFailedDelegate ClientCallFailed;

        /// <summary>
        /// For calls accepted by this user agent this event will be fired if the call
        /// is cancelled before it gets answered.
        /// </summary>
        public event SIPUASDelegate ServerCallCancelled;

        /// <summary>
        /// The remote call party has sent us a new re-INVITE request that this
        /// class didn't know how to or couldn't handle. Things we can
        /// handle are on and off hold. Common examples of what we can't handle
        /// are changing RTP end points, changing codecs etc.
        /// </summary>
        public event Action<UASInviteTransaction> OnReinviteRequest;

        /// <summary>
        /// Call was hungup by the remote party. Applies to calls initiated by us and calls received
        /// by us. An example of when this user agent will initiate a hang up is when a transfer is
        /// accepted by the remote calling party.
        /// </summary>
        public event Action OnCallHungup;

        /// <summary>
        /// Fires when a NOTIFY request is received that contains an update about the 
        /// status of a transfer.
        /// </summary>
        public event Action<string> OnTransferNotify;

        /// <summary>
        /// Creates a new SIP client and server combination user agent.
        /// </summary>
        /// <param name="transport">The transport layer to use for requests and responses.</param>
        /// <param name="outboundProxy">Optional. If set all requests and responses will be forwarded to this
        /// end point irrespective of their headers.</param>
        public SIPUserAgent(SIPTransport transport, SIPEndPoint outboundProxy)
        {
            m_transport = transport;
            m_outboundProxy = outboundProxy;

            m_transport.SIPTransportRequestReceived += SIPTransportRequestReceived;
        }

        /// <summary>
        /// Attempts to place a new outgoing call.
        /// </summary>
        /// <param name="sipCallDescriptor">A call descriptor containing the information about how and where to place the call.</param>
        /// <param name="mediaSession">The media session used for this call</param>
        public async Task Call(SIPCallDescriptor sipCallDescriptor, IMediaSession mediaSession)
        {
            m_uac = new SIPClientUserAgent(m_transport);
            m_uac.CallTrying += ClientCallTryingHandler;
            m_uac.CallRinging += ClientCallRingingHandler;
            m_uac.CallAnswered += ClientCallAnsweredHandler;
            m_uac.CallFailed += ClientCallFailedHandler;

            SIPEndPoint serverEndPoint = m_uac.GetCallDestination(sipCallDescriptor);

            if (serverEndPoint != null)
            {
                MediaSession = mediaSession;
                MediaSession.SessionMediaChanged += MediaSessionOnSessionMediaChanged;

                var sdp = await MediaSession.CreateOffer(serverEndPoint.Address).ConfigureAwait(false);
                sipCallDescriptor.Content = sdp;

                m_uac.Call(sipCallDescriptor);
            }
            else
            {
                ClientCallFailed?.Invoke(m_uac, $"Could not resolve destination when placing call to {sipCallDescriptor.Uri}.");
                CallEnded();
            }
        }

        private void MediaSessionOnSessionMediaChanged(string sdpMessage)
        {
            SendReInviteRequest(sdpMessage);
        }

        /// <summary>
        /// Cancel our call attempt prior to it being answered.
        /// </summary>
        public void Cancel()
        {
            MediaSession.SessionMediaChanged -= MediaSessionOnSessionMediaChanged;
            MediaSession?.Close();

            if (m_uac != null)
            {
                if (m_uac.IsUACAnswered == false)
                {
                    m_uac.Cancel();
                }
                else
                {
                    m_uac.Hangup();
                }
            }
        }

        /// <summary>
        /// Hangup established call
        /// </summary>
        public void Hangup()
        {
            MediaSession?.Close();
            Dialogue?.Hangup(m_transport, m_outboundProxy);
            CallEnded();
        }

        /// <summary>
        /// This method can be used to start the processing of a new incoming call request.
        /// The user agent will is acting as a server for this operation and it can be considered
        /// the opposite of the Call method. This is only the first step in answering an incoming
        /// call. It can still be rejected or answered after this point.
        /// </summary>
        /// <param name="inviteRequest">The invite request representing the incoming call.</param>
        /// <returns>An ID string that needs to be supplied when the call is answered or rejected 
        /// (used to manage multiple pending incoming calls).</returns>
        public SIPServerUserAgent AcceptCall(SIPRequest inviteRequest)
        {
            UASInviteTransaction uasTransaction = new UASInviteTransaction(m_transport, inviteRequest, m_outboundProxy);
            SIPServerUserAgent uas = new SIPServerUserAgent(m_transport, m_outboundProxy, null, null, SIPCallDirection.In, null, null, null, uasTransaction);
            uas.CallCancelled += (pendingUas) =>
            {
                CallEnded();
                ServerCallCancelled?.Invoke(pendingUas);
            };

            uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
            uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

            return uas;
        }

        /// <summary>
        /// Answers the call request contained in the user agent server parameter. Note the
        /// <see cref="AcceptCall(SIPRequest)"/> method should be used to create the user agent server.
        /// Any existing call will be hungup.
        /// </summary>
        /// <param name="uas">The user agent server holding the pending call to answer.</param>
        /// <param name="mediaSession">The media session used for this call</param>
        public async Task Answer(SIPServerUserAgent uas, IMediaSession mediaSession)
        {
            // This call is now taking over any existing call.
            if (IsCallActive)
            {
                Hangup();
            }

            var sipRequest = uas.ClientTransaction.TransactionRequest;

            MediaSession = mediaSession;
            MediaSession.SessionMediaChanged += MediaSessionOnSessionMediaChanged;
            MediaSession.OnRtpDisconnected += Hangup;

            var sdpAnswer = await MediaSession.AnswerOffer(sipRequest.Body).ConfigureAwait(false);

            m_uas = uas;
            m_uas.Answer(m_sdpContentType, sdpAnswer, null, SIPDialogueTransferModesEnum.Default);
            Dialogue.DialogueState = SIPDialogueStateEnum.Confirmed;
        }

        /// <summary>
        /// Initiates a blind transfer by asking the remote call party to call the specified destination.
        /// If the transfer is accepted the current call will be hungup.
        /// </summary>
        /// <param name="destination">The URI to transfer the call to.</param>
        /// <param name="timeout">Timeout for the transfer request to get accepted.</param>
        /// <param name="ct">Cancellation token. Can be set to cancel the transfer prior to it being
        /// accepted or timing out.</param>
        /// <returns>True if the transfer was accepted by the Transferee or false if not.</returns>
        public Task<bool> BlindTransfer(SIPURI destination, TimeSpan timeout, CancellationToken ct)
        {
            if (Dialogue == null)
            {
                logger.LogWarning("Blind transfer was called on the SIPUserAgent when no dialogue was available.");
                return Task.FromResult(false);
            }
            else
            {
                var referRequest = GetReferRequest(destination);
                return Transfer(referRequest, timeout, ct);
            }
        }

        /// <summary>
        /// Initiates an attended transfer by asking the remote call party to call the specified destination.
        /// If the transfer is accepted the current call will be hungup.
        /// </summary>
        /// <param name="transferee">The dialog that will be replaced on the initial call party.</param>
        /// <param name="timeout">Timeout for the transfer request to get accepted.</param>
        /// <param name="ct">Cancellation token. Can be set to cancel the transfer prior to it being
        /// accepted or timing out.</param>
        /// <returns>True if the transfer was accepted by the Transferee or false if not.</returns>
        public Task<bool> AttendedTransfer(SIPDialogue transferee, TimeSpan timeout, CancellationToken ct)
        {
            if (Dialogue == null || transferee == null)
            {
                logger.LogWarning("Attended transfer was called on the SIPUserAgent when no dialogue was available.");
                return Task.FromResult(false);
            }
            else
            {
                var referRequest = GetReferRequest(transferee);
                return Transfer(referRequest, timeout, ct);
            }
        }

        /// <summary>
        /// Processes a transfer once the REFER request has been determined.
        /// </summary>
        /// <param name="referRequest">The REFER request for the transfer.</param>
        /// <param name="timeout">Timeout for the transfer request to get accepted.</param>
        /// <param name="ct">Cancellation token. Can be set to cancel the transfer prior to it being
        /// accepted or timing out.</param>
        /// <returns>True if the transfer was accepted by the Transferee or false if not.</returns>
        private async Task<bool> Transfer(SIPRequest referRequest, TimeSpan timeout, CancellationToken ct)
        {
            if (Dialogue == null)
            {
                logger.LogWarning("Transfer was called on the SIPUserAgent when no dialogue was available.");
                return false;
            }
            else
            {
                TaskCompletionSource<bool> transferAccepted = new TaskCompletionSource<bool>();

                SIPNonInviteTransaction referTx = new SIPNonInviteTransaction(m_transport, referRequest, null);

                SIPTransactionResponseReceivedDelegate referTxStatusHandler = (localSIPEndPoint, remoteEndPoint, sipTransaction, sipResponse) =>
                {
                    // This handler has to go on a separate thread or the SIPTransport "ProcessInMessage" thread will be blocked.
                    Task.Run(() =>
                    {
                        if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.REFER && sipResponse.Status == SIPResponseStatusCodesEnum.Accepted)
                        {
                            logger.LogInformation("Call transfer was accepted by remote server.");
                            transferAccepted.SetResult(true);
                        }
                        else
                        {
                            transferAccepted.SetResult(false);
                        }
                    }, ct);

                    return Task.FromResult(SocketError.Success);
                };

                referTx.NonInviteTransactionFinalResponseReceived += referTxStatusHandler;
                referTx.SendRequest();

                await Task.WhenAny(transferAccepted.Task, Task.Delay((int)timeout.TotalMilliseconds, ct)).ConfigureAwait(false);

                referTx.NonInviteTransactionFinalResponseReceived -= referTxStatusHandler;

                if (transferAccepted.Task.IsCompleted)
                {
                    return transferAccepted.Task.Result;
                }
                else
                {
                    logger.LogWarning($"Call transfer request timed out after {timeout.TotalMilliseconds}ms.");
                    return false;
                }
            }
        }

        /// <summary>
        /// Handler for when an in dialog request is received on an established call.
        /// Typical types of request will be re-INVITES for things like putting a call on or
        /// off hold and REFER requests for transfers. Some in dialog request types, such 
        /// as re-INVITES have specific events so they can be bubbled up to the 
        /// application to deal with.
        /// </summary>
        /// <param name="sipRequest">The in dialog request received.</param>
        private async Task DialogRequestReceivedAsync(SIPRequest sipRequest)
        {
            // Make sure the request matches our dialog and is not a stray.
            // A dialog request should match on to tag, from tag and call ID. We'll be more 
            // accepting just in case the sender got the tags wrong.
            if (Dialogue == null || sipRequest.Header.CallId != Dialogue.CallId)
            {
                var noCallLegResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                var sendResult = await SendResponseAsync(noCallLegResponse);
                if (sendResult != SocketError.Success)
                {
                    logger.LogWarning($"SIPUserAgent send response failed in DialogRequestReceivedAsync with {sendResult}.");
                }
            }
            else
            {
                if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    logger.LogDebug($"Matching dialogue found for {sipRequest.StatusLine}.");
                    Dialogue.DialogueState = SIPDialogueStateEnum.Terminated;

                    SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await SendResponseAsync(okResponse).ConfigureAwait(false);

                    CallEnded();
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    logger.LogDebug($"Re-INVITE request received {sipRequest.StatusLine}.");

                    UASInviteTransaction reInviteTransaction = new UASInviteTransaction(m_transport, sipRequest, m_outboundProxy);

                    try
                    {
                        var answerSdp = await MediaSession.RemoteReInvite(sipRequest.Body).ConfigureAwait(false);

                        Dialogue.RemoteSDP = sipRequest.Body;
                        Dialogue.SDP = answerSdp;
                        Dialogue.RemoteCSeq = sipRequest.Header.CSeq;

                        var okResponse = reInviteTransaction.GetOkResponse(SDP.SDP_MIME_CONTENTTYPE, Dialogue.SDP);
                        reInviteTransaction.SendFinalResponse(okResponse);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "MediaSession can't process the re-INVITE request.");

                        if (OnReinviteRequest == null)
                        {
                            // The application isn't prepared to accept re-INVITE requests and we can't work out what it was for. 
                            // We'll reject as gently as we can to try and not lose the call.
                            SIPResponse notAcceptableResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptable, null);
                            reInviteTransaction.SendFinalResponse(notAcceptableResponse);
                        }
                        else
                        {
                            // The application is going to handle the re-INVITE request. We'll send a Trying response as a precursor.
                            SIPResponse tryingResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
                            await reInviteTransaction.SendProvisionalResponse(tryingResponse);
                            OnReinviteRequest.Invoke(reInviteTransaction);
                        }
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "OPTIONS request for established dialogue " + dialogue.DialogueName + ".", dialogue.Owner));
                    SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    okResponse.Body = Dialogue.RemoteSDP;
                    okResponse.Header.ContentLength = okResponse.Body.Length;
                    okResponse.Header.ContentType = m_sdpContentType;
                    await SendResponseAsync(okResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.MESSAGE)
                {
                    //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "MESSAGE for call " + sipRequest.URI.ToString() + ": " + sipRequest.Body + ".", dialogue.Owner));
                    SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await m_transport.SendResponseAsync(okResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.REFER)
                {
                    if (sipRequest.Header.ReferTo.IsNullOrBlank())
                    {
                        // A REFER request must have a Refer-To header.
                        //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Bad REFER request, no Refer-To header.", dialogue.Owner));
                        SIPResponse invalidResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing mandatory Refer-To header");
                        await SendResponseAsync(invalidResponse);
                    }
                    else
                    {
                        //TODO: Add handling logic for in transfer requests from the remote call party.
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.NOTIFY)
                {
                    SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await SendResponseAsync(okResponse);

                    if (sipRequest.Body?.Length > 0 && sipRequest.Header.ContentType?.Contains(m_sipReferContentType) == true)
                    {
                        OnTransferNotify?.Invoke(sipRequest.Body);
                    }
                }
            }
        }

        /// <summary>
        /// Sends a re-INVITE request to the remote call party with the supplied SDP.
        /// </summary>
        private void SendReInviteRequest(string sdp)
        {
            if (Dialogue == null)
            {
                logger.LogWarning("No dialog available, re-INVITE request cannot be sent.");
            }
            else
            {
                Dialogue.SDP = sdp;

                var reinviteRequest = Dialogue.GetInDialogRequest(SIPMethodsEnum.INVITE);
                reinviteRequest.Header.UserAgent = m_userAgent;
                reinviteRequest.Header.ContentType = m_sdpContentType;
                reinviteRequest.Body = sdp;
                reinviteRequest.Header.Supported = SIPExtensionHeaders.PRACK;

                if (m_uac != null)
                {
                    reinviteRequest.Header.Contact = m_uac.ServerTransaction.TransactionRequest.Header.Contact;
                    reinviteRequest.SetSendFromHints(m_uac.ServerTransaction.TransactionRequest.LocalSIPEndPoint);
                }
                else if (m_uas != null)
                {
                    reinviteRequest.Header.Contact = m_uas.ClientTransaction.TransactionFinalResponse.Header.Contact;
                    reinviteRequest.SetSendFromHints(m_uas.ClientTransaction.TransactionFinalResponse.LocalSIPEndPoint);
                }
                else
                {
                    reinviteRequest.Header.Contact = new List<SIPContactHeader>() { SIPContactHeader.GetDefaultSIPContactHeader() };
                }

                UACInviteTransaction reinviteTransaction = new UACInviteTransaction(m_transport, reinviteRequest, m_outboundProxy);
                reinviteTransaction.SendInviteRequest();
                reinviteTransaction.UACInviteTransactionFinalResponseReceived += ReinviteRequestFinalResponseReceived;
            }
        }

        /// <summary>
        /// This user agent will check incoming SIP requests for any that match its current dialog.
        /// </summary>
        /// <param name="localSIPEndPoint">The local end point the request was received on.</param>
        /// <param name="remoteEndPoint">The remote end point the request came from.</param>
        /// <param name="sipRequest">The SIP request.</param>
        private async Task SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (Dialogue != null)
            {
                if (sipRequest.Header.From != null &&
                    sipRequest.Header.From.FromTag != null &&
                    sipRequest.Header.To != null &&
                    sipRequest.Header.To.ToTag != null &&
                    sipRequest.Header.CallId == Dialogue.CallId)
                {
                    try
                    {
                        await DialogRequestReceivedAsync(sipRequest);
                    }
                    catch (Exception excp)
                    {
                        // There no point bubbling this exception up. The next class up is the transport layer and
                        // it doesn't know what to do if a request can't be dealt with.
                        logger.LogError(excp, $"Exception SIPUserAgent.SIPTransportRequestReceived. {excp.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Handles responses to our re-INVITE requests.
        /// </summary>
        /// <param name="localSIPEndPoint">The local end point the response was received on.</param>
        /// <param name="remoteEndPoint">The remote end point the response came from.</param>
        /// <param name="sipTransaction">The UAS transaction the response is part of.</param>
        /// <param name="sipResponse">The SIP response.</param>
        private Task<SocketError> ReinviteRequestFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
            {
                // Update the remote party's SDP.
                Dialogue.RemoteSDP = sipResponse.Body;
                _ = MediaSession.OfferAnswered(sipResponse.Body);
            }
            else
            {
                logger.LogWarning($"Re-INVITE request failed with response {sipResponse.ShortDescription}.");
            }

            return Task.FromResult(SocketError.Success);
        }

        /// <summary>
        /// Takes care of sending a response based on whether the outbound proxy is set or not.
        /// </summary>
        /// <param name="response">The response to send.</param>
        /// <returns>Send result.</returns>
        private Task<SocketError> SendResponseAsync(SIPResponse response)
        {
            if (m_outboundProxy != null)
            {
                return m_transport.SendResponseAsync(m_outboundProxy, response);
            }
            else
            {
                return m_transport.SendResponseAsync(response);
            }
        }

        /// <summary>
        /// Event handler for a client call (one initiated by us) receiving a trying response.
        /// </summary>
        /// <param name="uac">The client user agent used to initiate the call.</param>
        /// <param name="sipResponse">The INVITE trying response.</param>
        private void ClientCallTryingHandler(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            if (ClientCallTrying != null)
            {
                ClientCallTrying(uac, sipResponse);
            }
            else
            {
                logger.LogDebug($"Call attempt to {m_uac.CallDescriptor.Uri} received a trying response {sipResponse.ShortDescription}.");
            }
        }

        /// <summary>
        /// Event handler for a client call (one initiated by us) receiving an in progress response.
        /// </summary>
        /// <param name="uac">The client user agent used to initiate the call.</param>
        /// <param name="sipResponse">The INVITE ringing response.</param>
        private void ClientCallRingingHandler(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            if (ClientCallRinging != null)
            {
                ClientCallRinging(uac, sipResponse);
            }
            else
            {
                logger.LogWarning($"Call attempt to {m_uac.CallDescriptor.Uri} received a ringing response {sipResponse.ShortDescription}.");
            }
        }

        /// <summary>
        /// Event handler for a client call (one initiated by us) failing.
        /// </summary>
        /// <param name="uac">The client user agent used to initiate the call.</param>
        /// <param name="errorMessage">An error message indicating the reason for the failure.</param>
        private void ClientCallFailedHandler(ISIPClientUserAgent uac, string errorMessage)
        {
            if (ClientCallFailed != null)
            {
                ClientCallFailed(uac, errorMessage);
            }
            else
            {
                logger.LogWarning($"Call attempt to {m_uac.CallDescriptor.Uri} failed with {errorMessage}.");
            }
        }

        /// <summary>
        /// Event handler for a client call (one initiated by us) being answered.
        /// </summary>
        /// <param name="uac">The client user agent used to initiate the call.</param>
        /// <param name="sipResponse">The INVITE success response.</param>
        private void ClientCallAnsweredHandler(ISIPClientUserAgent uac, SIPResponse sipResponse) =>
            _ = ClientCallAnsweredHandlerAsync(uac, sipResponse);

        /// <summary>
        /// Event handler for a client call (one initiated by us) being answered.
        /// </summary>
        /// <param name="uac">The client user agent used to initiate the call.</param>
        /// <param name="sipResponse">The INVITE success response.</param>
        private async Task ClientCallAnsweredHandlerAsync(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
            {
                // Only set the remote RTP end point if there hasn't already been a packet received on it.
                await MediaSession.OfferAnswered(sipResponse.Body).ConfigureAwait(false);

                Dialogue.DialogueState = SIPDialogueStateEnum.Confirmed;

                if (ClientCallAnswered != null)
                {
                    ClientCallAnswered(uac, sipResponse);
                }
                else
                {
                    logger.LogDebug($"Call attempt to {m_uac.CallDescriptor.Uri} was answered.");
                }
            }
            else
            {
                logger.LogDebug($"Call attempt was answered with failure response {sipResponse.ShortDescription}.");
                ClientCallFailed?.Invoke(uac, sipResponse.ReasonPhrase);
                CallEnded();
            }
        }

        /// <summary>
        /// Builds the REFER request to initiate a blind transfer on an established call.
        /// </summary>
        /// <param name="referToUri">The SIP URI to transfer the call to.</param>
        /// <returns>A SIP REFER request.</returns>
        private SIPRequest GetReferRequest(SIPURI referToUri)
        {
            SIPRequest referRequest = Dialogue.GetInDialogRequest(SIPMethodsEnum.REFER);
            referRequest.Header.ReferTo = referToUri.ToString();
            referRequest.Header.Supported = SIPExtensionHeaders.NO_REFER_SUB;
            referRequest.Header.Contact = new List<SIPContactHeader> { SIPContactHeader.GetDefaultSIPContactHeader() };
            return referRequest;
        }

        /// <summary>
        /// Builds the REFER request to initiate an attended transfer on an established call.
        /// </summary>
        /// <param name="target">A target dialogue representing the Transferee.</param>
        /// <returns>A SIP REFER request.</returns>
        private SIPRequest GetReferRequest(SIPDialogue target)
        {
            SIPRequest referRequest = Dialogue.GetInDialogRequest(SIPMethodsEnum.REFER);
            SIPURI targetUri = target.RemoteTarget.CopyOf();
            referRequest.Header.Contact = new List<SIPContactHeader> { SIPContactHeader.GetDefaultSIPContactHeader() };

            SIPParameters replacesHeaders = new SIPParameters();

            if (target.Direction == SIPCallDirection.Out)
            {
                replacesHeaders.Set("Replaces", SIPEscape.SIPURIParameterEscape($"{target.CallId};to-tag={target.LocalTag};from-tag={target.RemoteTag}"));
                var from = new SIPUserField(target.LocalUserField.Name, target.LocalUserField.URI.CopyOf(), null);
                referRequest.Header.ReferredBy = from.ToString();
            }
            else
            {
                replacesHeaders.Set("Replaces", SIPEscape.SIPURIParameterEscape($"{target.CallId};to-tag={target.RemoteTag};from-tag={target.LocalTag}"));
                var from = new SIPUserField(target.RemoteUserField.Name, target.RemoteUserField.URI.CopyOf(), null);
                referRequest.Header.ReferredBy = from.ToString();
            }

            targetUri.Headers = replacesHeaders;
            var referTo = new SIPUserField(null, targetUri, null);
            referRequest.Header.ReferTo = referTo.ToString();

            return referRequest;
        }

        /// <summary>
        /// The current call has ended. Reset the state of the user agent.
        /// </summary>
        private void CallEnded()
        {
            m_uac = null;
            m_uas = null;

            if (MediaSession != null)
            {
                MediaSession.SessionMediaChanged -= MediaSessionOnSessionMediaChanged;
                MediaSession.Close();
                MediaSession = null;
            }

            OnCallHungup?.Invoke();
        }
    }
}
