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
using System.Linq;
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
    /// </summary>
    public class SIPUserAgent
    {
        private static readonly string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;
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
        /// dialouge that was created by the call.
        /// </summary>
        public SIPDialogue Dialogue
        {
            get
            {
                if (m_uac != null)
                {
                    return m_uac.SIPDialogue;
                }
                else if (m_uas != null)
                {
                    return m_uas.SIPDialogue;
                }
                else
                {
                    return null;
                }
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
        /// Returns true if the remote call party currently has us on hold.
        /// </summary>
        public bool OnHoldFromRemote
        {
            get
            {
                if (Dialogue == null || Dialogue.RemoteSDP == null)
                {
                    SDP remoteSDP = SDP.ParseSDPDescription(Dialogue.RemoteSDP);
                    var firstAudioStreamStatus = remoteSDP.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0);

                    return firstAudioStreamStatus == MediaStreamStatusEnum.SendOnly;
                }
                return false;
            }
        }

        /// <summary>
        /// Returns true if we have put the remote call party on hold.
        /// </summary>
        public bool OnHoldFromLocal
        {
            get
            {
                if (Dialogue == null || Dialogue.RemoteSDP == null)
                {
                    SDP localSDP = SDP.ParseSDPDescription(Dialogue.SDP);
                    var firstAudioStreamStatus = localSDP.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0);

                    return firstAudioStreamStatus == MediaStreamStatusEnum.SendOnly;
                }
                return false;
            }
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
        /// The remote call party has put us on hold.
        /// </summary>
        public event Action RemotePutOnHold;

        /// <summary>
        /// The remote call party has taken us off hold.
        /// </summary>
        public event Action RemoteTookOffHold;

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
        /// Creates a new SIP client and server combination user agent.
        /// </summary>
        /// <param name="transport">The transport layer to use for requests and responses.</param>
        /// <param name="outboundProxy">Optional. If set all requests and responses will be forwarded to this
        /// end point irrespective of their headers.</param>
        public SIPUserAgent(SIPTransport transport, SIPEndPoint outboundProxy)
        {
            m_transport = transport;
            m_outboundProxy = outboundProxy;
        }

        /// <summary>
        /// Attempts to place a new outgoing call.
        /// </summary>
        /// <param name="sipCallDescriptor">A call descriptor containing the information about how and where to place the call.</param>
        public void Call(SIPCallDescriptor sipCallDescriptor)
        {
            m_uac = new SIPClientUserAgent(m_transport);
            m_uac.CallTrying += ClientCallTryingHandler;
            m_uac.CallRinging += ClientCallRingingHandler;
            m_uac.CallAnswered += ClientCallAnsweredHandler;
            m_uac.CallFailed += ClientCallFailedHandler;
            m_uac.Call(sipCallDescriptor);
        }

        /// <summary>
        /// Cancel our call attempt prior to it being answered.
        /// </summary>
        public void Cancel()
        {
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
            if (Dialogue != null)
            {
                Dialogue.Hangup(m_transport, m_outboundProxy);
            }
        }

        /// <summary>
        /// This method can be used to start the processing of a new incoming call request.
        /// The user agent will is acting as a server for this operation and it can be considered
        /// the opposite of the Call method. This is only the first step in answering an incoming
        /// call. It can still be rejected or answered after this point.
        /// </summary>
        /// <param name="inviteRequest">The invite requestn representing the incoming call.</param>
        /// <returns>An ID string that needs to be supplied when the call is answered or rejected 
        /// (used to manage multiple pending incoming calls).</returns>
        public SIPServerUserAgent AcceptCall(SIPRequest inviteRequest)
        {
            UASInviteTransaction uasTransaction = m_transport.CreateUASTransaction(inviteRequest, m_outboundProxy);
            SIPServerUserAgent uas = new SIPServerUserAgent(m_transport, m_outboundProxy, null, null, SIPCallDirection.In, null, null, null, uasTransaction);
            uas.CallCancelled += (pendingUas) =>
            {
                ServerCallCancelled?.Invoke(pendingUas);
            };

            uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
            uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

            return uas;
        }

        /// <summary>
        /// Answers the call request contained in the user agent server. Any existing call will
        /// be hungup.
        /// </summary>
        /// <param name="SIPServerUserAgen">The user agent server to answer.</param>
        /// <param name="sdp">The session description payload to send to the remote call party.</param>
        public void Answer(SIPServerUserAgent uas, SDP sdp)
        {
            // This call is now taking over any existing call.
            Hangup();

            m_uas = uas;
            m_uas.Answer(m_sdpContentType, sdp.ToString(), null, SIPDialogueTransferModesEnum.Default);
            Dialogue.DialogueState = SIPDialogueStateEnum.Confirmed;
        }

        /// <summary>
        /// Handler for when an in dialog request is received on an established call.
        /// Typical types of request will be re-INVITES for things like putting a call on or
        /// off hold and REFER requests for transfers. Some in dialog request types, such 
        /// as re-INVITES have specific events so they can be bubbled up to the 
        /// application to deal with.
        /// </summary>
        /// <param name="request">The in dialog request received.</param>
        public async Task DialogRequestReceivedAsync(SIPRequest sipRequest)
        {
            // Make sure the request matches our dialog and is not a stray.
            // A dialog request should match on to tag, from tag and call ID. We'll be more 
            // accepting just in case the sender got the tags wrong.
            if (Dialogue == null || sipRequest.Header.CallId != Dialogue.CallId)
            {
                var noCallLegResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                var sendResult = await SendResponse(noCallLegResponse);
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

                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await SendResponse(okResponse);

                    OnCallHungup?.Invoke();
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    logger.LogDebug($"Re-INVITE request received {sipRequest.StatusLine}.");

                    UASInviteTransaction reInviteTransaction = m_transport.CreateUASTransaction(sipRequest, m_outboundProxy);

                    // Check for remote party putting us on and off hold.
                    SDP newSDPOffer = SDP.ParseSDPDescription(sipRequest.Body);
                    if(newSDPOffer.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0) == MediaStreamStatusEnum.SendRecv && OnHoldFromRemote)
                    {
                        // We've been taken off hold.
                        SDP localSDP = SDP.ParseSDPDescription(Dialogue.SDP);
                        localSDP.Media.First(x => x.Media == SDPMediaTypesEnum.audio).MediaStreamStatus = MediaStreamStatusEnum.SendRecv;
                        Dialogue.SDP = localSDP.ToString();
                        Dialogue.RemoteSDP = sipRequest.Body;
                        Dialogue.RemoteCSeq = sipRequest.Header.CSeq;

                        var okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        okResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
                        okResponse.Body = Dialogue.SDP;
                        reInviteTransaction.SendFinalResponse(okResponse);

                        RemoteTookOffHold?.Invoke();
                    }
                    else if(newSDPOffer.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0) == MediaStreamStatusEnum.SendOnly && !OnHoldFromRemote)
                    {
                        // We've been put on hold.
                        SDP localSDP = SDP.ParseSDPDescription(Dialogue.SDP);
                        localSDP.Media.First(x => x.Media == SDPMediaTypesEnum.audio).MediaStreamStatus = MediaStreamStatusEnum.RecvOnly;
                        Dialogue.SDP = localSDP.ToString();
                        Dialogue.RemoteSDP = sipRequest.Body;
                        Dialogue.RemoteCSeq = sipRequest.Header.CSeq;

                        var okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        okResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
                        okResponse.Body = Dialogue.SDP;
                        reInviteTransaction.SendFinalResponse(okResponse);

                        RemotePutOnHold?.Invoke();
                    }
                    else if (OnReinviteRequest == null)
                    {
                        // The application isn't prepared to accept re-INVITE requests. We'll reject as gently as we can to try and not lose the call.
                        SIPResponse notAcceptableResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptable, null);
                        reInviteTransaction.SendFinalResponse(notAcceptableResponse);
                    }
                    else
                    {
                        // The application is going to handle the re-INVITE request. We'll send a Trying response as a precursor.
                        SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
                        reInviteTransaction.SendProvisionalResponse(tryingResponse);
                        OnReinviteRequest(reInviteTransaction);
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "OPTIONS request for established dialogue " + dialogue.DialogueName + ".", dialogue.Owner));
                    SIPNonInviteTransaction optionsTransaction = m_transport.CreateNonInviteTransaction(sipRequest, m_outboundProxy);
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    okResponse.Body = Dialogue.RemoteSDP;
                    okResponse.Header.ContentLength = okResponse.Body.Length;
                    okResponse.Header.ContentType = m_sdpContentType;
                    optionsTransaction.SendFinalResponse(okResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.MESSAGE)
                {
                    //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "MESSAGE for call " + sipRequest.URI.ToString() + ": " + sipRequest.Body + ".", dialogue.Owner));
                    SIPNonInviteTransaction messageTransaction = m_transport.CreateNonInviteTransaction(sipRequest, m_outboundProxy);
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    messageTransaction.SendFinalResponse(okResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.REFER)
                {
                    SIPNonInviteTransaction referTransaction = m_transport.CreateNonInviteTransaction(sipRequest, m_outboundProxy);

                    if (sipRequest.Header.ReferTo.IsNullOrBlank())
                    {
                        // A REFER request must have a Refer-To header.
                        //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Bad REFER request, no Refer-To header.", dialogue.Owner));
                        SIPResponse invalidResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing mandatory Refer-To header");
                        referTransaction.SendFinalResponse(invalidResponse);
                    }
                    else
                    {
                        //TODO: Add handling logic for in transfer requests from the remote call party.
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.NOTIFY)
                {
                    // These are likely tp be notifications from REFER (transfer request) processing.
                    // We don't do anything with them at the moment.
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await SendResponse(okResponse);
                }
            }
        }

        /// <summary>
        /// Sends a re-INVITE request to the remote call party with the supplied SDP.
        /// </summary>
        /// <param name="sdp">The SDP to send to the remote call party.</param>
        public void SendReInviteRequest(SDP sdp)
        {
            if (Dialogue == null)
            {
                logger.LogWarning("No dialog available, re-INVITE request cannot be sent.");
            }
            else
            {
                var reinviteRequest = Dialogue.GetInDialogRequest(SIPMethodsEnum.INVITE);
                reinviteRequest.Header.UserAgent = m_userAgent;
                reinviteRequest.Header.ContentType = m_sdpContentType;
                reinviteRequest.Body = sdp.ToString();
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

                UACInviteTransaction reinviteTransaction = m_transport.CreateUACTransaction(reinviteRequest, m_outboundProxy);
                reinviteTransaction.SendReliableRequest();
            }
        }

        /// <summary>
        /// Initiates a blind transfer by asking the remote call party to call the specified destination. 
        /// </summary>
        /// <param name="destination">The URI to transfer the call to.</param>
        /// <param name="timeout">Timeout for the transfer request to get accepted.</param>
        /// <param name="ct">Cancellation token. Can be set to canel the transfer prior to it being
        /// accepted or timing out.</param>
        public async Task<bool> Transfer(SIPURI destination, TimeSpan timeout, CancellationToken ct)
        {
            if (Dialogue == null)
            {
                logger.LogWarning("Transfer was called on the SIPUserAgent when no dialogue was available.");
                return false;
            }
            else
            {
                TaskCompletionSource<bool> transferAccepted = new TaskCompletionSource<bool>();

                var referRequest = GetReferRequest(Dialogue, destination);

                SIPNonInviteTransaction referTx = m_transport.CreateNonInviteTransaction(referRequest, null);

                referTx.NonInviteTransactionFinalResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) =>
                {
                    if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.REFER && sipResponse.Status == SIPResponseStatusCodesEnum.Accepted)
                    {
                        logger.LogInformation("Call transfer was accepted by remote server.");

                        // Existing call is now out of the loop, hang it up.
                        Dialogue.Hangup(m_transport, m_outboundProxy);

                        transferAccepted.SetResult(true);
                    }
                };

                referTx.SendReliableRequest();

                await Task.WhenAny(new Task[] { transferAccepted.Task, Task.Delay((int)timeout.TotalMilliseconds) });

                return transferAccepted.Task.Result;
            }
        }

        /// <summary>
        /// Takes care of sending a response based on whether the outbound proxy is set or not.
        /// </summary>
        /// <param name="response">The response to send.</param>
        /// <returns>Send result.</returns>
        private async Task<SocketError> SendResponse(SIPResponse response)
        {
            if (m_outboundProxy != null)
            {
                return await m_transport.SendResponseAsync(m_outboundProxy, response);
            }
            else
            {
                return await m_transport.SendResponseAsync(response);
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
        private void ClientCallAnsweredHandler(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            if (ClientCallAnswered != null)
            {
                Dialogue.DialogueState = SIPDialogueStateEnum.Confirmed;
                ClientCallAnswered(uac, sipResponse);
            }
            else
            {
                logger.LogDebug($"Call attempt to {m_uac.CallDescriptor.Uri} was answered.");
            }
        }

        /// <summary>
        /// Builds the REFER request to initiate a blind transfer on an established call.
        /// </summary>
        /// <param name="sipDialogue">A SIP dialogue object representing the established call.</param>
        /// <param name="referToUri">The SIP URI to transfer the call to.</param>
        /// <returns>A SIP REFER request.</returns>
        private SIPRequest GetReferRequest(SIPDialogue sipDialogue, SIPURI referToUri)
        {
            SIPRequest referRequest = Dialogue.GetInDialogRequest(SIPMethodsEnum.REFER);
            referRequest.Header.ReferTo = referToUri.ToString();
            referRequest.Header.Supported = SIPExtensionHeaders.NO_REFER_SUB;
            return referRequest;
        }
    }
}
