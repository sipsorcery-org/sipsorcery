//-----------------------------------------------------------------------------
// Filename: SIPUserAgent.cs
//
// Description: A "full" SIP user agent that encompasses both client and server 
// user agents. It is also able to manage in dialog operations after the call 
// is established (the client and server user agents don't handle in dialog 
// operations).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Nov 2019	Aaron Clauson   Created, Dublin, Ireland.
// rj2: added overload for Answer with customHeader
// 10 May 2020  Aaron Clauson   Added handling for REFER requests as per 
//                              https://tools.ietf.org/html/rfc3515
//                              and https://tools.ietf.org/html/rfc5589.
// 17 May 2020  Aaron Clauson   Added exclusive transport option to simplify
//                              incoming call handling.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
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
    public class SIPUserAgent : IDisposable
    {
        private static readonly string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;
        private static readonly string m_sipReferContentType = SIPMIMETypes.REFER_CONTENT_TYPE;
        private static string m_userAgent = SIPConstants.SIP_USERAGENT_STRING;
        private static int WAIT_ONHOLD_TIMEOUT = SIPTimings.T1;
        private static int WAIT_DIALOG_TIMEOUT = SIPTimings.T2;

        private static ILogger logger = Log.Logger;

        private CancellationTokenSource m_cts = new CancellationTokenSource();

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
        /// If true indicates the SIP transport instance is specific to this user agent and
        /// is not being shared.
        /// </summary>
        private readonly bool m_isTransportExclusive;

        /// <summary>
        /// If set all communications are sent to this address irrespective of what the 
        /// request and response headers indicate.
        /// </summary>
        private SIPEndPoint m_outboundProxy;

        /// <summary>
        /// If a call is successfully answered this property will be set with the
        /// resultant dialogue.
        /// </summary>
        private SIPDialogue m_sipDialogue;

        /// <summary>
        /// When a call is hungup a reference is kept to the BYE transaction so it can
        /// be monitored for delivery.
        /// </summary>
        private SIPNonInviteTransaction m_byeTransaction;

        /// <summary>
        /// Holds the call descriptor for an in progress client (outbound) call.
        /// </summary>
        private SIPCallDescriptor m_callDescriptor;

        /// <summary>
        /// Used to keep track of received RTP events. An RTP event will typically span
        /// multiple packets but the application only needs to get informed once per event.
        /// </summary>
        private uint _rtpEventSsrc;

        /// <summary>
        /// When a blind and attended transfer is in progress the original call will be placed
        /// on hold (if not already). To prevent the response from the on hold re-INVITE 
        /// being applied to the media session while the new transfer call is being made or
        /// accepted we don't apply session descriptions on requests or responses with the 
        /// old (original) call ID.
        /// </summary>
        private string _oldCallID;

        /// <summary>
        /// Gets set to true if the SIP user agent has been explicitly closed and is no longer
        /// required.
        /// </summary>
        private bool _isClosed;

        /// <summary>
        /// The media (RTP) session in use for the current call.
        /// </summary>
        public IMediaSession MediaSession { get; private set; }

        /// <summary>
        /// Indicates whether there is an active call or not.
        /// </summary>
        public bool IsCallActive
        {
            get
            {
                return m_sipDialogue?.DialogueState == SIPDialogueStateEnum.Confirmed;
            }
        }

        /// <summary>
        /// Indicates whether a call initiated by this user agent is in progress but is yet
        /// to get a ring or progress response.
        /// </summary>
        public bool IsCalling
        {
            get
            {
                if (!IsCallActive && m_uac != null && m_uac.ServerTransaction != null)
                {
                    return m_uac.ServerTransaction.TransactionState == SIPTransactionStatesEnum.Calling ||
                    m_uac.ServerTransaction.TransactionState == SIPTransactionStatesEnum.Trying;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Indicates whether a call initiated by this user agent has received a ringing or progress response.
        /// </summary>
        public bool IsRinging
        {
            get
            {
                if (!IsCallActive && m_uac != null && m_uac.ServerTransaction != null)
                {
                    return m_uac.ServerTransaction.TransactionState == SIPTransactionStatesEnum.Proceeding;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Indicates whether the user agent is in the process of hanging up or cancelling a call.
        /// </summary>
        public bool IsHangingUp
        {
            get
            {
                if (m_uac != null && m_uac.m_cancelTransaction != null && m_uac.m_cancelTransaction.DeliveryPending)
                {
                    return true;
                }
                else if (m_byeTransaction != null && m_byeTransaction.DeliveryPending)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// True if we've put the remote party on hold.
        /// </summary>
        public bool IsOnLocalHold { get; private set; }

        /// <summary>
        /// True if the remote party has put us on hold.
        /// </summary>
        public bool IsOnRemoteHold { get; private set; }

        /// <summary>
        /// Once either the client or server call is answered this will hold the SIP
        /// dialogue that was created by the call.
        /// </summary>
        public SIPDialogue Dialogue
        {
            get { return m_sipDialogue; }
        }

        /// <summary>
        /// For a call initiated by us this is the call descriptor that was used.
        /// </summary>
        public SIPCallDescriptor CallDescriptor
        {
            get { return m_callDescriptor; }
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
        /// For calls accepted by this user agent this event will be fired if the call
        /// is answered but the answer response is never confirmed. This can occur if
        /// the client does not send the ACK or the ACK does not get through.
        /// </summary>
        public event SIPUASDelegate ServerCallRingTimeout;

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
        public event Action<SIPDialogue> OnCallHungup;

        /// <summary>
        /// Fires when a NOTIFY request is received that contains an update about the 
        /// status of a transfer. These events will be received by a user agent acting as the
        /// Transferor but only if the Transferee support the transfer subscription.
        /// </summary>
        public event Action<string> OnTransferNotify;

        /// <summary>
        /// Fires when a REFER request is received that requests us to place a call to a
        /// new destination. The REFER request can be a blind transfer or an attended transfer.
        /// The difference is whether the REFER request includes a Replaces parameter. If it does 
        /// it's used to inform the transfer target (the transfer destination requested) that 
        /// if they accept our call it should replace an existing one.
        /// </summary>
        /// <remarks>
        /// Parameters for event delegate:
        /// bool OnTransferRequested(SIPUserField referTo, string referredBy)
        /// SIPUserField: Is the destination that we are being asked to place a call to.
        /// string referredBy: The Referred-By header from the REFER request that requested 
        /// we do the transfer.
        /// bool: The boolean result can be returned as false to prevent the transfer. By default
        /// if no event handler is hooked up the transfer will be accepted.
        /// </remarks>
        public event Func<SIPUserField, string, bool> OnTransferRequested;

        /// <summary>
        /// Fires when the call placed as a result of a transfer request is successfully answered.
        /// The SIPUserField contains the destination that was called for the transfer.
        /// </summary>
        public event Action<SIPUserField> OnTransferToTargetSuccessful;

        /// <summary>
        /// Fires when the call placed as a result of a transfer request is rejected or fails.
        /// The SIPUserField contains the destination that was called for the transfer.
        /// </summary>
        public event Action<SIPUserField> OnTransferToTargetFailed;

        /// <summary>	
        /// The remote call party has put us on hold.	
        /// </summary>	
        public event Action RemotePutOnHold;

        /// <summary>	
        /// The remote call party has taken us off hold.	
        /// </summary>	
        public event Action RemoteTookOffHold;

        /// <summary>
        /// Gets fired when an RTP DTMF event is detected as completed on the remote party's RTP stream.
        /// </summary>
        public event Action<byte, int> OnDtmfTone;

        /// <summary>
        /// Gets fired for every RTP event packet received from the remote party. This event allows the
        /// application to decipher the vents as it wishes.
        /// </summary>
        public event Action<RTPEvent, RTPHeader> OnRtpEvent;

        /// <summary>
        /// Gets fired when a new INVITE request is detected on the SIP transport being used
        /// by this user agent.
        /// </summary>
        public event Action<SIPUserAgent, SIPRequest> OnIncomingCall;

        /// <summary>
        /// Diagnostic event to allow monitoring of the INVITE transaction state.
        /// </summary>
        public event SIPTransactionStateChangeDelegate OnTransactionStateChange;

        /// <summary>
        /// Diagnostic event to receive trace messages related to the INVITE transaction
        /// state machine processing.
        /// </summary>
        public event SIPTransactionTraceMessageDelegate OnTransactionTraceMessage;

        /// <summary>
        /// Creates a new instance where the user agent has exclusive control of the SIP transport.
        /// This is significant for incoming requests. WIth exclusive control the agent knows that
        /// any request are for it and can handle accordingly. If the transport needs to be shared 
        /// amongst multiple user agents use the alternative constructor.
        /// </summary>
        public SIPUserAgent()
        {
            m_transport = new SIPTransport();
            m_transport.SIPTransportRequestReceived += SIPTransportRequestReceived;
            m_isTransportExclusive = true;
        }

        /// <summary>
        /// Creates a new SIP client and server combination user agent with a shared SIP transport instance.
        /// With a shared transport outgoing calls and registrations work the same but for incoming calls
        /// and requests the destination needs to be coordinated externally.
        /// </summary>
        /// <param name="transport">The transport layer to use for requests and responses.</param>
        /// <param name="outboundProxy">Optional. If set all requests and responses will be forwarded to this
        /// end point irrespective of their headers.</param>
        /// <param name="isTransportExclusive">True is the SIP transport instance is for the exclusive use of 
        /// this user agent or false if it's being shared amongst multiple agents.</param>
        public SIPUserAgent(SIPTransport transport, SIPEndPoint outboundProxy, bool isTransportExclusive = false)
        {
            m_transport = transport;
            m_outboundProxy = outboundProxy;
            m_isTransportExclusive = isTransportExclusive;
            m_transport.SIPTransportRequestReceived += SIPTransportRequestReceived;
        }

        /// <summary>
        /// Attempts to place a new outgoing call AND waits for the call to be answered or fail.
        /// Use <see cref="InitiateCallAsync(SIPCallDescriptor, IMediaSession)"/> to start a call without
        /// waiting for it to complete and monitor <see cref="ClientCallAnsweredHandler"/> and
        /// <see cref="ClientCallFailedHandler"/> to detect an answer or failure.
        /// </summary>
        /// <param name="dst">The destination SIP URI to call.</param>
        /// <param name="username">Optional Username if authentication is required.</param>
        /// <param name="password">Optional. Password if authentication is required.</param>
        /// <param name="mediaSession">The RTP session for the call.</param>
        public Task<bool> Call(string dst, string username, string password, IMediaSession mediaSession)
        {
            if (mediaSession == null)
            {
                throw new ArgumentNullException("mediaSession", "A media session must be supplied when placing a call.");
            }

            if (!SIPURI.TryParse(dst, out var dstUri))
            {
                throw new ApplicationException("The destination was not recognised as a valid SIP URI.");
            }

            string fromHeader = SIPConstants.SIP_DEFAULT_FROMURI;

            if (!string.IsNullOrWhiteSpace(username))
            {
                // If the call needs to be authenticated the From header needs to be set
                // with the username and domain to match the credentials.
                fromHeader = (new SIPURI(username, dstUri.Host, null, dstUri.Scheme, dstUri.Protocol)).ToParameterlessString();
            }

            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
               username ?? SIPConstants.SIP_DEFAULT_USERNAME,
               password,
               dstUri.ToString(),
               fromHeader,
               dstUri.CanonicalAddress,
               null, null, null,
               SIPCallDirection.Out,
               SDP.SDP_MIME_CONTENTTYPE,
               null,
               null);

            return Call(callDescriptor, mediaSession);
        }

        /// <summary>
        /// Attempts to place a new outgoing call AND waits for the call to be answered or fail.
        /// Use <see cref="InitiateCallAsync(SIPCallDescriptor, IMediaSession)"/> to start a call without
        /// waiting for it to complete and monitor <see cref="ClientCallAnsweredHandler"/> and
        /// <see cref="ClientCallFailedHandler"/> to detect an answer or failure.
        /// </summary>
        /// <param name="callDescriptor">The full descriptor for the call destination. Allows customising
        /// of additional options above the standard username, password and destination URI.</param>
        /// <param name="mediaSession">The RTP session for the call.</param>
        public async Task<bool> Call(SIPCallDescriptor callDescriptor, IMediaSession mediaSession)
        {
            TaskCompletionSource<bool> callResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            ClientCallAnswered += (uac, resp) => callResult.TrySetResult(true);
            ClientCallFailed += (uac, errorMessage, result) => callResult.TrySetResult(false);

            await InitiateCallAsync(callDescriptor, mediaSession).ConfigureAwait(false);

            return await callResult.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Attempts to place a new outgoing call.
        /// </summary>
        /// <param name="sipCallDescriptor">A call descriptor containing the information about how 
        /// and where to place the call.</param>
        /// <param name="mediaSession">The media session used for this call</param>
        public async Task InitiateCallAsync(SIPCallDescriptor sipCallDescriptor, IMediaSession mediaSession)
        {
            m_cts = new CancellationTokenSource();

            m_callDescriptor = sipCallDescriptor;

            m_uac = new SIPClientUserAgent(m_transport, m_outboundProxy);
            m_uac.CallTrying += ClientCallTryingHandler;
            m_uac.CallRinging += ClientCallRingingHandler;
            m_uac.CallAnswered += ClientCallAnsweredHandler;
            m_uac.CallFailed += ClientCallFailedHandler;

            // Can be DNS lookups involved in getting the call destination.
            SIPEndPoint serverEndPoint = await m_uac.GetCallDestination(sipCallDescriptor).ConfigureAwait(false);

            if (serverEndPoint != null)
            {
                MediaSession = mediaSession;
                MediaSession.OnRtpEvent += OnRemoteRtpEvent;

                var sdpAnnounceAddress = mediaSession.RtpBindAddress ?? NetServices.GetLocalAddressForRemote(serverEndPoint.Address);

                var sdp = mediaSession.CreateOffer(sdpAnnounceAddress);
                if (sdp == null)
                {
                    ClientCallFailed?.Invoke(m_uac, $"Could not generate an offer.", null);
                    CallEnded();
                }
                else
                {
                    sipCallDescriptor.Content = sdp.ToString();
                    // This initiates the call but does not wait for an answer.
                    m_uac.Call(sipCallDescriptor, serverEndPoint);
                }
            }
            else
            {
                ClientCallFailed?.Invoke(m_uac, $"Could not resolve destination when placing call to {sipCallDescriptor.Uri}.", null);
                CallEnded();
            }
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

            if (MediaSession != null)
            {
                MediaSession.Close("call cancelled");
            }
        }

        /// <summary>
        /// Hangup established call
        /// </summary>
        public void Hangup()
        {
            if (IsCallActive)
            {
                m_cts.Cancel();

                if (MediaSession != null && !MediaSession.IsClosed)
                {
                    MediaSession?.Close("call hungup");
                }

                if (m_sipDialogue != null && m_sipDialogue.DialogueState != SIPDialogueStateEnum.Terminated)
                {
                    m_sipDialogue.Hangup(m_transport, m_outboundProxy);
                    m_byeTransaction = m_sipDialogue.m_byeTransaction;
                }

                IsOnLocalHold = false;
                IsOnRemoteHold = false;

                CallEnded();
            }
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
            SIPServerUserAgent uas = new SIPServerUserAgent(m_transport, m_outboundProxy, uasTransaction, null);
            uas.ClientTransaction.TransactionStateChanged += (tx) => OnTransactionStateChange?.Invoke(tx);
            uas.ClientTransaction.TransactionTraceMessage += (tx, msg) => OnTransactionTraceMessage?.Invoke(tx, msg);
            uas.CallCancelled += (pendingUas) =>
            {
                CallEnded();
                ServerCallCancelled?.Invoke(pendingUas);
            };
            uas.NoRingTimeout += (pendingUas) =>
            {
                ServerCallRingTimeout?.Invoke(pendingUas);
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
        public Task<bool> Answer(SIPServerUserAgent uas, IMediaSession mediaSession)
        {
            return Answer(uas, mediaSession, null);
        }

        /// <summary>
        /// Answers the call request contained in the user agent server parameter. Note the
        /// <see cref="AcceptCall(SIPRequest)"/> method should be used to create the user agent server.
        /// Any existing call will be hungup.
        /// </summary>
        /// <param name="uas">The user agent server holding the pending call to answer.</param>
        /// <param name="mediaSession">The media session used for this call</param>
        /// <param name="customHeaders">Custom SIP-Headers to use in Answer.</param>
        /// <returns>True if the call was successfully answered or false if there was a problem
        /// such as incompatible codecs.</returns>
        public async Task<bool> Answer(SIPServerUserAgent uas, IMediaSession mediaSession, string[] customHeaders)
        {
            if (uas.IsCancelled)
            {
                logger.LogDebug("The incoming call has been cancelled.");
                mediaSession?.Close("call cancelled");
                return false;
            }
            else
            {
                m_cts = new CancellationTokenSource();
                var sipRequest = uas.ClientTransaction.TransactionRequest;

                MediaSession = mediaSession;
                MediaSession.OnRtpEvent += OnRemoteRtpEvent;
                MediaSession.OnRtpClosed += (reason) =>
                {
                    if (MediaSession?.IsClosed == false)
                    {
                        logger.LogWarning($"RTP channel was closed with reason {reason}.");
                    }
                };

                string sdp = null;

                if (!String.IsNullOrEmpty(sipRequest.Body))
                {
                    // The SDP offer was included in the INVITE request.
                    SDP remoteSdp = SDP.ParseSDPDescription(sipRequest.Body);
                    var setRemoteResult = MediaSession.SetRemoteDescription(SdpType.offer, remoteSdp);

                    if (setRemoteResult != SetDescriptionResultEnum.OK)
                    {
                        logger.LogWarning($"Error setting remote description from INVITE {setRemoteResult}.");
                        uas.Reject(SIPResponseStatusCodesEnum.NotAcceptable, setRemoteResult.ToString());
                        MediaSession.Close("sdp offer not acceptable");
                        Hangup();

                        return false;
                    }
                    else
                    {
                        var sdpAnswer = MediaSession.CreateAnswer(null);
                        sdp = sdpAnswer.ToString();
                    }
                }
                else
                {
                    // No SDP offer was included in the INVITE request need to wait for the ACK.
                    var sdpAnnounceAddress = MediaSession.RtpBindAddress ?? NetServices.GetLocalAddressForRemote(sipRequest.RemoteSIPEndPoint.GetIPEndPoint().Address);
                    var sdpOffer = MediaSession.CreateOffer(sdpAnnounceAddress);
                    sdp = sdpOffer.ToString();
                }

                m_uas = uas;

                // In cases where the initial INVITE did not contain an SDP offer the action sequence is:
                // - INVITE with no SDP offer received,
                // - Reply with OK and an SDP offer,
                // - Wait for ACK with SDP answer.
                TaskCompletionSource<SIPDialogue> dialogueCreatedTcs = new TaskCompletionSource<SIPDialogue>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_uas.OnDialogueCreated += (dialogue) => dialogueCreatedTcs.TrySetResult(dialogue);

                m_uas.Answer(m_sdpContentType, sdp, null, SIPDialogueTransferModesEnum.Default, customHeaders);

                await Task.WhenAny(dialogueCreatedTcs.Task, Task.Delay(WAIT_DIALOG_TIMEOUT)).ConfigureAwait(false);

                if (m_uas.SIPDialogue != null)
                {
                    m_sipDialogue = m_uas.SIPDialogue;

                    if (MediaSession.RemoteDescription == null)
                    {
                        // If the initial INVITE did not contain an offer then the remote description will not yet be set.
                        var remoteSDP = SDP.ParseSDPDescription(m_sipDialogue.RemoteSDP);
                        var setRemoteResult = MediaSession.SetRemoteDescription(SdpType.offer, remoteSDP);

                        if (setRemoteResult != SetDescriptionResultEnum.OK)
                        {
                            // Failed to set the remote SDP from the ACK request. Only option is to hangup.
                            logger.LogWarning($"Error setting remote description from ACK {setRemoteResult}.");
                            MediaSession.Close(setRemoteResult.ToString());
                            Hangup();

                            return false;
                        }
                        else
                        {
                            // SDP from the ACK request was accepted. Start the RTP session.
                            m_sipDialogue.DialogueState = SIPDialogueStateEnum.Confirmed;
                            await MediaSession.Start().ConfigureAwait(false);

                            return true;
                        }
                    }
                    else
                    {
                        m_sipDialogue.DialogueState = SIPDialogueStateEnum.Confirmed;
                        await MediaSession.Start().ConfigureAwait(false);

                        return true;
                    }
                }
                else
                {
                    logger.LogWarning("The attempt to answer a call failed as the dialog was not created. The likely cause is the ACK not being received in time.");

                    MediaSession.Close("dialog creation failed");
                    Hangup();

                    return false;
                }
            }
        }

        /// <summary>
        /// Initiates a blind transfer by asking the remote call party to call the specified destination.
        /// If the transfer is accepted the current call will be hungup.
        /// </summary>
        /// <param name="destination">The URI to transfer the call to.</param>
        /// <param name="timeout">Timeout for the transfer request to get accepted.</param>
        /// <param name="ct">Cancellation token. Can be set to cancel the transfer prior to it being
        /// accepted or timing out.</param>
        /// <param name="customHeaders">Optional. Custom SIP-Headers that will be set in the REFER request sent 
        /// to the remote party.</param>
        /// <returns>True if the transfer was accepted by the Transferee or false if not.</returns>
        public Task<bool> BlindTransfer(SIPURI destination, TimeSpan timeout, CancellationToken ct, string[] customHeaders = null)
        {
            if (m_sipDialogue == null)
            {
                logger.LogWarning("Blind transfer was called on the SIPUserAgent when no dialogue was available.");
                return Task.FromResult(false);
            }
            else
            {
                var referRequest = GetReferRequest(destination, customHeaders);
                return Transfer(referRequest, timeout, ct);
            }
        }

        /// <summary>
        /// Initiates an attended transfer by asking the remote call party to call the specified destination.
        /// If the transfer is accepted the current call will be hungup.
        /// </summary>
        /// <param name="transferee">The dialog that will be replaced on the transfer target call party.</param>
        /// <param name="timeout">Timeout for the transfer request to get accepted.</param>
        /// <param name="ct">Cancellation token. Can be set to cancel the transfer prior to it being
        /// accepted or timing out.</param>
        /// <param name="customHeaders">Optional. Custom SIP-Headers that will be set in the REFER request sent 
        /// to the remote party.</param>
        /// <returns>True if the transfer was accepted by the Transferee or false if not.</returns>
        public Task<bool> AttendedTransfer(SIPDialogue transferee, TimeSpan timeout, CancellationToken ct, string[] customHeaders = null)
        {
            if (m_sipDialogue == null || transferee == null)
            {
                logger.LogWarning("Attended transfer was called on the SIPUserAgent when no dialogue was available.");
                return Task.FromResult(false);
            }
            else
            {
                var referRequest = GetReferRequest(transferee, customHeaders);
                return Transfer(referRequest, timeout, ct);
            }
        }

        /// <summary>
        /// Requests the RTP session to transmit a DTMF tone using an RTP event.
        /// </summary>
        /// <param name="tone">The DTMF tone to transmit.</param>
        public Task SendDtmf(byte tone)
        {
            return MediaSession.SendDtmf(tone, m_cts.Token);
        }

        /// <summary>
        /// Send a re-INVITE request to put the remote call party on hold.
        /// </summary>
        public void PutOnHold()
        {
            IsOnLocalHold = true;

            // The action we take to put a call on hold is to switch the media status
            // to send only and change the audio input from a capture device to on hold
            // music.
            ApplyHoldAndReinvite();
        }

        /// <summary>
        /// Send a re-INVITE request to take the remote call party on hold.
        /// </summary>
        public void TakeOffHold()
        {
            IsOnLocalHold = false;
            ApplyHoldAndReinvite();
        }

        /// <summary>
        /// Updates the stream status of the RTP session and sends the re-INVITE request.
        /// </summary>
        private void ApplyHoldAndReinvite()
        {
            var streamStatus = GetStreamStatusForOnHoldState();

            if (MediaSession.HasAudio)
            {
                MediaSession.SetMediaStreamStatus(SDPMediaTypesEnum.audio, streamStatus);
            }

            if (MediaSession.HasVideo)
            {
                MediaSession.SetMediaStreamStatus(SDPMediaTypesEnum.video, streamStatus);
            }

            var sdp = MediaSession.CreateOffer(null);
            SendReInviteRequest(sdp);
        }

        /// <summary>
        /// Processes a transfer by sending to the remote party once the REFER request has been constructed.
        /// </summary>
        /// <param name="referRequest">The REFER request for the transfer.</param>
        /// <param name="timeout">Timeout for the transfer request to get accepted.</param>
        /// <param name="ct">Cancellation token. Can be set to cancel the transfer prior to it being
        /// accepted or timing out.</param>
        /// <returns>True if the transfer was accepted by the Transferee or false if not.</returns>
        private async Task<bool> Transfer(SIPRequest referRequest, TimeSpan timeout, CancellationToken ct)
        {
            if (m_sipDialogue == null)
            {
                logger.LogWarning("Transfer was called on the SIPUserAgent when no dialogue was available.");
                return false;
            }
            else
            {
                TaskCompletionSource<bool> transferAccepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                SIPNonInviteTransaction referTx = new SIPNonInviteTransaction(m_transport, referRequest, null);

                SIPTransactionResponseReceivedDelegate referTxStatusHandler = (localSIPEndPoint, remoteEndPoint, sipTransaction, sipResponse) =>
                {
                    if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.REFER && sipResponse.Status == SIPResponseStatusCodesEnum.Accepted)
                    {
                        logger.LogInformation("Call transfer was accepted by remote server.");
                        transferAccepted.TrySetResult(true);
                    }
                    else
                    {
                        transferAccepted.TrySetResult(false);
                    }

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
            if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                logger.LogInformation($"Remote call party hungup {sipRequest.StatusLine}.");
                m_sipDialogue.DialogueState = SIPDialogueStateEnum.Terminated;

                SIPNonInviteTransaction byeTx = new SIPNonInviteTransaction(m_transport, sipRequest, null);
                byeTx.SendResponse(SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null));

                CallEnded();
            }
            else if (sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                logger.LogDebug($"Re-INVITE request received {sipRequest.StatusLine}.");

                UASInviteTransaction reInviteTransaction = new UASInviteTransaction(m_transport, sipRequest, m_outboundProxy);

                try
                {
                    SDP offer = SDP.ParseSDPDescription(sipRequest.Body);

                    if (sipRequest.Header.CallId == _oldCallID)
                    {
                        // A transfer is in progress and this re-INVITE belongs to the original call. More than likely
                        // the purpose of the request is to place us on hold. We'll respond with OK but not update any local state.
                        var answerSdp = MediaSession.CreateAnswer(null);
                        var okResponse = reInviteTransaction.GetOkResponse(SDP.SDP_MIME_CONTENTTYPE, answerSdp.ToString());
                        reInviteTransaction.SendFinalResponse(okResponse);
                    }
                    else
                    {
                        var setRemoteResult = MediaSession.SetRemoteDescription(SdpType.offer, offer);

                        if (setRemoteResult != SetDescriptionResultEnum.OK)
                        {
                            logger.LogWarning($"Unable to set remote description from reINVITE request {setRemoteResult}");

                            var notAcceptableResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptable, setRemoteResult.ToString());
                            reInviteTransaction.SendFinalResponse(notAcceptableResponse);
                        }
                        else
                        {
                            CheckRemotePartyHoldCondition(MediaSession.RemoteDescription);

                            if (MediaSession.HasAudio)
                            {
                                MediaSession.SetMediaStreamStatus(SDPMediaTypesEnum.audio, GetStreamStatusForOnHoldState());
                            }

                            if (MediaSession.HasVideo)
                            {
                                MediaSession.SetMediaStreamStatus(SDPMediaTypesEnum.video, GetStreamStatusForOnHoldState());
                            }

                            var answerSdp = MediaSession.CreateAnswer(null);

                            m_sipDialogue.RemoteSDP = sipRequest.Body;
                            m_sipDialogue.SDP = answerSdp.ToString();
                            m_sipDialogue.RemoteCSeq = sipRequest.Header.CSeq;

                            var okResponse = reInviteTransaction.GetOkResponse(SDP.SDP_MIME_CONTENTTYPE, m_sipDialogue.SDP);
                            reInviteTransaction.SendFinalResponse(okResponse);
                        }
                    }
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
                        await reInviteTransaction.SendProvisionalResponse(tryingResponse).ConfigureAwait(false);
                        OnReinviteRequest.Invoke(reInviteTransaction);
                    }
                }
            }
            else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
            {
                //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "OPTIONS request for established dialogue " + dialogue.DialogueName + ".", dialogue.Owner));
                SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                okResponse.Body = m_sipDialogue.RemoteSDP;
                okResponse.Header.ContentLength = okResponse.Body.Length;
                okResponse.Header.ContentType = m_sdpContentType;
                await SendResponseAsync(okResponse).ConfigureAwait(false);
            }
            else if (sipRequest.Method == SIPMethodsEnum.MESSAGE)
            {
                //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "MESSAGE for call " + sipRequest.URI.ToString() + ": " + sipRequest.Body + ".", dialogue.Owner));
                SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await m_transport.SendResponseAsync(okResponse).ConfigureAwait(false);
            }
            else if (sipRequest.Method == SIPMethodsEnum.REFER)
            {
                ProcessTransferRequest(sipRequest);
            }
            else if (sipRequest.Method == SIPMethodsEnum.NOTIFY)
            {
                SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                await SendResponseAsync(okResponse).ConfigureAwait(false);

                if (sipRequest.Body?.Length > 0 && sipRequest.Header.ContentType?.Contains(m_sipReferContentType) == true)
                {
                    OnTransferNotify?.Invoke(sipRequest.Body);
                }
            }
            else if (m_isTransportExclusive)
            {
                SIPResponse notSupportedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotImplemented, null);
                await m_transport.SendResponseAsync(notSupportedResponse).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Processes transfer (REFER) requests from the remote call party.
        /// </summary>
        private void ProcessTransferRequest(SIPRequest referRequest)
        {
            // We use a reliable response to make sure that duplicate REFER requests are ignored.
            SIPNonInviteTransaction referResponseTx = new SIPNonInviteTransaction(m_transport, referRequest, null);

            if (referRequest.Header.ReferTo.IsNullOrBlank())
            {
                // A REFER request must have a Refer-To header.
                logger.LogWarning($"A REFER request was received from {referRequest.RemoteSIPEndPoint} without a Refer-To header.");
                SIPResponse invalidResponse = SIPResponse.GetResponse(referRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing mandatory Refer-To header");
                referResponseTx.SendResponse(invalidResponse);
            }
            else if (m_sipDialogue == null || m_sipDialogue.DialogueState != SIPDialogueStateEnum.Confirmed)
            {
                // Can't replace out existing dialog if we don't have a current one.
                logger.LogWarning($"A REFER request was received from {referRequest.RemoteSIPEndPoint} when there was no dialog or the dialog was not in a ready state.");
                SIPResponse noDialogResponse = SIPResponse.GetResponse(referRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                referResponseTx.SendResponse(noDialogResponse);
            }
            else
            {
                // Check that the REFER destination is valid.
                var referToUserField = SIPUserField.ParseSIPUserField(referRequest.Header.ReferTo);
                string referredBy = referRequest.Header.ReferredBy;

                logger.LogDebug($"Transfer request received, referred by {referredBy}, refer to {referToUserField}.");

                bool acceptTransfer = true;

                // The calling application can optionally decide whether to accept transfers or not.
                if (OnTransferRequested != null)
                {
                    acceptTransfer = OnTransferRequested(referToUserField, referredBy);
                }

                if (!acceptTransfer)
                {
                    logger.LogDebug("Transfer request was rejected by application.");

                    SIPResponse rejectXferResponse = SIPResponse.GetResponse(referRequest, SIPResponseStatusCodesEnum.Decline, null);
                    referResponseTx.SendResponse(rejectXferResponse);
                }
                else
                {
                    // All checks have passed so go ahead and accept the transfer.
                    SIPResponse acceptXferResponse = SIPResponse.GetResponse(referRequest, SIPResponseStatusCodesEnum.Accepted, null);
                    referResponseTx.SendResponse(acceptXferResponse);

                    // While we process the transfer request we flag the original call so that any subsequent re-INVITE 
                    // requests or response (most likely relating to on/off hold) don't get applied to the media session.
                    _oldCallID = referRequest.Header.CallId;

                    // To keep the remote party that requested the transfer up to date with the status and outcome of the REFER request
                    // we need to create a subscription and send NOTIFY requests.

                    if (!IsOnRemoteHold)
                    {
                        // Put the remote party (who has just requested we call a new destination) on hold while we
                        // attempt a call to the referred destination.
                        PutOnHold();
                    }

                    if (MediaSession.HasAudio)
                    {
                        MediaSession.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.SendRecv);
                    }

                    if (MediaSession.HasVideo)
                    {
                        MediaSession.SetMediaStreamStatus(SDPMediaTypesEnum.video, MediaStreamStatusEnum.SendRecv);
                    }

                    // The norefersub supported header means am event subscription is not expected.
                    // TODO: Add support for the event subscription for cases where norefersub is not applicable.
                    // Need to create an implicit subscription to keep the remote party that requested the transfer up to date with the 
                    // status and outcome of the REFER request
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            SIPURI referToUri = referToUserField.URI;

                            logger.LogDebug($"Calling transfer destination URI {referToUri.ToParameterlessString()}.");

                            // Get the BYE request for the original dialog so it can be sent if answering the transfer call succeeds.
                            SIPRequest byeRequest = m_sipDialogue.GetInDialogRequest(SIPMethodsEnum.BYE);

                            List<string> customHeaders = null;

                            // Attended transfers will specify a dialog that is being replaced on the transfer Target.
                            // Blind transfers do not include a Replaces header.
                            if (referToUserField.URI.Headers.Has(SIPHeaderAncillary.SIP_REFER_REPLACES))
                            {
                                string replacesStr = referToUserField.URI.Headers.Get(SIPHeaderAncillary.SIP_REFER_REPLACES);
                                SIPReplacesParameter replaces = SIPReplacesParameter.Parse(replacesStr);
                                customHeaders = new List<string>
                                {
                                    $"{SIPHeaders.SIP_HEADER_REPLACES}: {replaces.CallID};to-tag={replaces.ToTag};from-tag={replaces.FromTag}"
                                };
                            }

                            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
                               SIPConstants.SIP_DEFAULT_USERNAME,
                               null,
                               referToUri.ToParameterlessString(),
                               SIPConstants.SIP_DEFAULT_FROMURI,
                               referToUri.ToParameterlessString(),
                               null,
                               customHeaders,
                               null,
                               SIPCallDirection.Out,
                               SDP.SDP_MIME_CONTENTTYPE,
                               null,
                               null);

                            var transferResult = await Call(callDescriptor, MediaSession).ConfigureAwait(false);

                            logger.LogDebug($"Result of calling transfer destination {transferResult}.");

                            if (!transferResult)
                            {
                                _oldCallID = null;
                                OnTransferToTargetFailed?.Invoke(referToUserField);
                                TakeOffHold();
                            }
                            else
                            {
                                OnTransferToTargetSuccessful?.Invoke(referToUserField);
                                // Hanging up original call.

                                logger.LogDebug("Transfer succeeded, hanging up original call.");

                                SIPNonInviteTransaction byeTransaction = new SIPNonInviteTransaction(m_transport, byeRequest, m_outboundProxy);
                                byeTransaction.SendRequest();
                            }
                        }
                        catch (Exception excp)
                        {
                            logger.LogError($"Exception processing transfer request. {excp.Message}");
                        }
                    }).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Sends a re-INVITE request to the remote call party with the supplied SDP.
        /// </summary>
        private void SendReInviteRequest(SDP sdp)
        {
            if (m_sipDialogue == null)
            {
                logger.LogWarning("No dialog available, re-INVITE request cannot be sent.");
            }
            else
            {
                m_sipDialogue.SDP = sdp.ToString();

                var reinviteRequest = m_sipDialogue.GetInDialogRequest(SIPMethodsEnum.INVITE);
                reinviteRequest.Header.UserAgent = m_userAgent;
                reinviteRequest.Header.ContentType = m_sdpContentType;
                reinviteRequest.Body = sdp.ToString();
                reinviteRequest.Header.Supported = SIPExtensionHeaders.REPLACES + ", " + SIPExtensionHeaders.NO_REFER_SUB + ", " + SIPExtensionHeaders.PRACK;

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
                    reinviteRequest.Header.Contact = new List<SIPContactHeader>() { SIPContactHeader.GetDefaultSIPContactHeader(reinviteRequest.URI.Scheme) };
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
            if (m_sipDialogue != null)
            {
                if (sipRequest.Header.From != null &&
                    sipRequest.Header.From.FromTag != null &&
                    sipRequest.Header.To != null &&
                    sipRequest.Header.To.ToTag != null &&
                    sipRequest.Header.CallId == m_sipDialogue.CallId)
                {
                    try
                    {
                        await DialogRequestReceivedAsync(sipRequest).ConfigureAwait(false);
                    }
                    catch (Exception excp)
                    {
                        // There no point bubbling this exception up. The next class up is the transport layer and
                        // it doesn't know what to do if a request can't be dealt with.
                        logger.LogError(excp, $"Exception SIPUserAgent.SIPTransportRequestReceived. {excp.Message}");
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE && !string.IsNullOrWhiteSpace(sipRequest.Header.Replaces))
                {
                    // This is a special case of receiving an INVITE request that is part of an attended transfer and
                    // that if successful will replace the existing dialog.
                    //UASInviteTransaction uasTx = new UASInviteTransaction(m_transport, sipRequest, null);
                    var uas = AcceptCall(sipRequest);

                    // An attended transfer INVITE should only be accepted if the dialog parameters in the Replaces header 
                    // match the current dialog. But... to be more accepting with only a small increase in risk we only 
                    // require a match on the Call-ID (only small increase in risk as if a malicious party can get 1
                    // of the three required headers they can almost certainly get all 3).
                    SIPReplacesParameter replaces = SIPReplacesParameter.Parse(sipRequest.Header.Replaces);

                    logger.LogDebug($"INVITE for attended transfer received, Replaces CallID {replaces.CallID}, our dialog Call-ID {m_sipDialogue.CallId}.");

                    if (replaces == null || replaces.CallID != m_sipDialogue.CallId)
                    {
                        logger.LogDebug("The attended transfer INVITE's Replaces header did not match the current dialog, rejecting.");
                        uas.Reject(SIPResponseStatusCodesEnum.BadRequest, null);
                    }
                    else
                    {
                        logger.LogDebug($"Proceeding with attended transfer INVITE received from {remoteEndPoint}.");
                        await AcceptAttendedTransfer(uas).ConfigureAwait(false);
                    }
                }
            }
            else if (!_isClosed && sipRequest.Method == SIPMethodsEnum.INVITE)
            {
                logger.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint}, uri:{sipRequest.URI}.");

                if (!m_isTransportExclusive)
                {
                    // If there is no handler for an incoming call request it gets ignored. The SIP transport layer can have 
                    // multiple handlers for incoming requests and it's likely a different handler is processing incoming calls.
                    OnIncomingCall?.Invoke(this, sipRequest);
                }
                else
                {
                    if (OnIncomingCall != null)
                    {
                        OnIncomingCall(this, sipRequest);
                    }
                    else
                    {
                        // This user agent has exclusive control of the transport and no incoming call handler was provided.
                        var uas = new UASInviteTransaction(m_transport, sipRequest, m_outboundProxy);
                        var notFoundResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotFound, null);
                        uas.SendFinalResponse(notFoundResponse);
                    }
                }
            }
            else if (!_isClosed && m_isTransportExclusive)
            {
                // If the transport is exclusive this is the only user agent listening and if it's not handling the request
                // nothing is.
                var notSupportedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                await m_transport.SendResponseAsync(notSupportedResponse).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Accepts and attempts to process an INVITE request from a 3rd party that is asking to use their incoming
        /// call request to replace the existing call.
        /// </summary>
        /// <param name="uas">The user agent/transaction representing the incoming INVITE.</param>
        private async Task AcceptAttendedTransfer(SIPServerUserAgent uas)
        {
            // The desired experience when accepting an attended transfer is to:
            // - If the current call is not already on hold then put it on hold,
            // - Automatically answer the new call (maybe some kind of notification should be given but that seems 
            //   overkill and annoying since the initial caller would have most likely informed them the transfer
            //   was about to take place),
            // - Re-use the media session from the initial call but adjust to use new end points and re-select the codecs,
            // - If the new call is answered then it now becomes active and a BYE request should be sent to hangup the 
            //   original call.

            // While we process the transfer request we flag the original call so that any subsequent re-INVITE 
            // requests or response (most likely relating to on/off hold) don't get applied to the media session.
            _oldCallID = uas.ClientTransaction.TransactionRequest.Header.CallId;

            if (!IsOnLocalHold)
            {
                logger.LogDebug("Current call placed on hold.");
                PutOnHold();

                // Pause to give the hold request time to get processed. Otherwise the BYE request can get sent
                // before the hold request which will be interpreted as an missing dialog on the transferor and
                // which can be confusing.
                await Task.Delay(WAIT_ONHOLD_TIMEOUT).ConfigureAwait(false);
            }

            if (MediaSession.HasAudio)
            {
                MediaSession.SetMediaStreamStatus(SDPMediaTypesEnum.audio, MediaStreamStatusEnum.SendRecv);
            }

            if (MediaSession.HasVideo)
            {
                MediaSession.SetMediaStreamStatus(SDPMediaTypesEnum.video, MediaStreamStatusEnum.SendRecv);
            }

            // Get the BYE request for the original dialog so it can be sent if answering the transfer call succeeds.
            SIPRequest byeRequest = m_sipDialogue.GetInDialogRequest(SIPMethodsEnum.BYE);

            bool answerResult = await Answer(uas, MediaSession).ConfigureAwait(false);

            if (answerResult)
            {
                logger.LogDebug("Attended transfer was successfully answered, hanging up original call.");

                // Hanging up original call.
                SIPNonInviteTransaction byeTransaction = new SIPNonInviteTransaction(m_transport, byeRequest, m_outboundProxy);
                byeTransaction.SendRequest();
            }
            else
            {
                _oldCallID = null;
                logger.LogDebug("Attended transfer answer failed, taking original call off hold.");
                TakeOffHold();
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
                if (sipResponse.Header.CallId == _oldCallID)
                {
                    // A transfer is in progress and this re-INVITE response belongs to the original call. More than likely
                    // it's the response to our on hold re-INVITE request. We don't want to update the media session state
                    // with this response.
                    logger.LogDebug($"Re-INVITE response received for original Call-ID, disregarding.");
                }
                else
                {
                    // Update the remote party's SDP.
                    m_sipDialogue.RemoteSDP = sipResponse.Body;
                    MediaSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(sipResponse.Body));
                }
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
                logger.LogInformation($"Call attempt to {m_uac.CallDescriptor.Uri} received a trying response {sipResponse.ShortDescription}.");
            }
        }

        /// <summary>
        /// Event handler for a client call (one initiated by us) receiving an in progress response.
        /// </summary>
        /// <param name="uac">The client user agent used to initiate the call.</param>
        /// <param name="sipResponse">The INVITE ringing response.</param>
        private async void ClientCallRingingHandler(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            if (sipResponse.Status == SIPResponseStatusCodesEnum.SessionProgress &&
                sipResponse.Body != null)
            {
                var setDescriptionResult = MediaSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(sipResponse.Body));
                logger.LogDebug($"Set remote description for early media result {setDescriptionResult}.");

                if (setDescriptionResult == SetDescriptionResultEnum.OK)
                {
                    await MediaSession.Start().ConfigureAwait(false);
                }
            }

            if (ClientCallRinging != null)
            {
                ClientCallRinging(uac, sipResponse);
            }
            else
            {
                logger.LogInformation($"Call attempt to {m_uac.CallDescriptor.Uri} received a ringing response {sipResponse.ShortDescription}.");
            }
        }

        /// <summary>
        /// Event handler for a client call (one initiated by us) failing.
        /// </summary>
        /// <param name="uac">The client user agent used to initiate the call.</param>
        /// <param name="errorMessage">An error message indicating the reason for the failure.</param>
        private void ClientCallFailedHandler(ISIPClientUserAgent uac, string errorMessage, SIPResponse sipResponse)
        {
            logger.LogWarning($"Call attempt to {m_uac.CallDescriptor?.Uri} failed with {errorMessage}.");

            ClientCallFailed?.Invoke(uac, errorMessage, sipResponse);
        }

        /// <summary>
        /// Event handler for a client call (one initiated by us) being answered.
        /// </summary>
        /// <param name="uac">The client user agent used to initiate the call.</param>
        /// <param name="sipResponse">The INVITE success response.</param>
        private async void ClientCallAnsweredHandler(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
            {
                if (sipResponse.Body == null && ((MediaSession as RTPSession)?.IsStarted ?? false))
                {
                    // This is a special case where no SDP answer was received in the Ok response or the ACK 
                    // BUT an SDP answer was supplied in a 183 Session Progress response. 
                    // TODO: Find the specification that details this behaviour.
                    // See https://github.com/sipsorcery-org/sipsorcery/issues/414.
                    m_sipDialogue = uac.SIPDialogue;
                    m_sipDialogue.DialogueState = SIPDialogueStateEnum.Confirmed;

                    logger.LogInformation($"Call attempt to {m_uac.CallDescriptor.Uri} was answered; no media update from early media.");

                    ClientCallAnswered?.Invoke(uac, sipResponse);
                }
                else
                {
                    var setDescriptionResult = MediaSession.SetRemoteDescription(SdpType.answer, SDP.ParseSDPDescription(sipResponse.Body));

                    if (setDescriptionResult == SetDescriptionResultEnum.OK)
                    {
                        await MediaSession.Start().ConfigureAwait(false);

                        m_sipDialogue = uac.SIPDialogue;
                        m_sipDialogue.DialogueState = SIPDialogueStateEnum.Confirmed;

                        logger.LogInformation($"Call attempt to {m_uac.CallDescriptor.Uri} was answered.");

                        ClientCallAnswered?.Invoke(uac, sipResponse);
                    }
                    else
                    {
                        logger.LogWarning($"Call attempt was answered with {sipResponse.ShortDescription} but an {setDescriptionResult} error occurred setting the remote description.");
                        ClientCallFailed?.Invoke(uac, $"Failed to set the remote description {setDescriptionResult}", sipResponse);
                        uac.SIPDialogue?.Hangup(this.m_transport, this.m_outboundProxy);
                        CallEnded();
                    }
                }
            }
            else
            {
                logger.LogWarning($"Call attempt was answered with failure response {sipResponse.ShortDescription}.");
                ClientCallFailed?.Invoke(uac, sipResponse.ReasonPhrase, sipResponse);
                CallEnded();
            }
        }

        /// <summary>
        /// Builds the REFER request to initiate a blind transfer on an established call.
        /// </summary>
        /// <param name="referToUri">The SIP URI to transfer the call to.</param>
        /// <param name="customHeaders">Optional. Can be used to set custom SIP headers in the
        /// REFER request.</param>
        /// <returns>A SIP REFER request.</returns>
        private SIPRequest GetReferRequest(SIPURI referToUri, string[] customHeaders)
        {
            SIPRequest referRequest = m_sipDialogue.GetInDialogRequest(SIPMethodsEnum.REFER);
            referRequest.Header.ReferTo = referToUri.ToString();
            referRequest.Header.Supported = SIPExtensionHeaders.NO_REFER_SUB;
            referRequest.Header.Contact = new List<SIPContactHeader> { SIPContactHeader.GetDefaultSIPContactHeader(referRequest.URI.Scheme) };

            if (customHeaders != null && customHeaders.Length > 0)
            {
                foreach (string header in customHeaders)
                {
                    referRequest.Header.UnknownHeaders.Add(header);
                }
            }

            return referRequest;
        }

        /// <summary>
        /// Builds the REFER request to initiate an attended transfer on an established call.
        /// </summary>
        /// <param name="target">A target dialogue representing the Transferee.</param>
        /// <param name="customHeaders">Optional. Can be used to set custom SIP headers in the
        /// REFER request.</param>
        /// <returns>A SIP REFER request.</returns>
        private SIPRequest GetReferRequest(SIPDialogue target, string[] customHeaders)
        {
            SIPRequest referRequest = m_sipDialogue.GetInDialogRequest(SIPMethodsEnum.REFER);
            SIPURI targetUri = target.RemoteTarget.CopyOf();
            referRequest.Header.Contact = new List<SIPContactHeader> { SIPContactHeader.GetDefaultSIPContactHeader(referRequest.URI.Scheme) };

            SIPParameters replacesHeaders = new SIPParameters();

            if (target.Direction == SIPCallDirection.Out)
            {
                replacesHeaders.Set("Replaces", SIPEscape.SIPURIParameterEscape($"{target.CallId};to-tag={target.RemoteTag};from-tag={target.LocalTag}"));
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

            if (customHeaders != null && customHeaders.Length > 0)
            {
                foreach (string header in customHeaders)
                {
                    referRequest.Header.UnknownHeaders.Add(header);
                }
            }

            return referRequest;
        }

        /// <summary>
        /// The current call has ended. Reset the state of the user agent.
        /// </summary>
        private void CallEnded()
        {
            m_uac = null;
            m_uas = null;
            m_callDescriptor = null;

            IsOnLocalHold = false;
            IsOnRemoteHold = false;

            if (MediaSession != null && !MediaSession.IsClosed)
            {
                MediaSession.Close("normal");
                MediaSession = null;
            }

            OnCallHungup?.Invoke(m_sipDialogue);

            m_sipDialogue = null;
        }

        /// <summary>
        /// Processes an in-dialog SDP offer from the remote party to check whether the 
        /// call hold status has changed.
        /// </summary>
        /// <param name="remoteSDP">The in-dialog SDP received from he remote party.</param>
        private void CheckRemotePartyHoldCondition(SDP remoteSDP)
        {
            var mediaStreamStatus = remoteSDP.GetMediaStreamStatus(SDPMediaTypesEnum.audio, 0);

            if (mediaStreamStatus == MediaStreamStatusEnum.SendOnly)
            {
                if (!IsOnRemoteHold)
                {
                    IsOnRemoteHold = true;
                    RemotePutOnHold?.Invoke();
                }
            }
            else if (mediaStreamStatus == MediaStreamStatusEnum.SendRecv && IsOnRemoteHold)
            {
                if (IsOnRemoteHold)
                {
                    IsOnRemoteHold = false;
                    RemoteTookOffHold?.Invoke();
                }
            }
        }

        /// <summary>
        /// Gets the required state of the local media tracks for the on hold state.
        /// </summary>
        /// <returns>The required state of the local media tracks to match the current on
        /// hold conditions.</returns>
        private MediaStreamStatusEnum GetStreamStatusForOnHoldState()
        {
            var streamStatus = MediaStreamStatusEnum.SendRecv;

            if (IsOnLocalHold && IsOnRemoteHold)
            {
                streamStatus = MediaStreamStatusEnum.Inactive;
            }
            else if (!IsOnLocalHold && !IsOnRemoteHold)
            {
                streamStatus = MediaStreamStatusEnum.SendRecv;
            }
            else if (IsOnLocalHold)
            {
                streamStatus = MediaStreamStatusEnum.SendOnly;
            }
            else if (IsOnRemoteHold)
            {
                streamStatus = MediaStreamStatusEnum.RecvOnly;
            }

            return streamStatus;
        }

        /// <summary>
        /// Event handler for RTP events from the remote call party. Fires
        /// when the event is completed.
        /// </summary>
        /// <param name="rtpEvent">The received RTP event.</param>
        /// <param name="rtpHeader">THe RTP header on the packet that the event was received in.</param>
        private void OnRemoteRtpEvent(IPEndPoint remoteEP, RTPEvent rtpEvent, RTPHeader rtpHeader)
        {
            OnRtpEvent?.Invoke(rtpEvent, rtpHeader);

            if (OnDtmfTone != null)
            {
                if (_rtpEventSsrc == 0)
                {
                    if (rtpEvent.EndOfEvent && rtpHeader.MarkerBit == 1)
                    {
                        // Full event is contained in a single RTP packet.
                        //logger.LogDebug($"RTP event {rtpEvent.EventID}.");

                        OnDtmfTone.Invoke(rtpEvent.EventID, rtpEvent.Duration);
                    }
                    else if (!rtpEvent.EndOfEvent)
                    {
                        //logger.LogDebug($"RTP event {rtpEvent.EventID}.");
                        _rtpEventSsrc = rtpHeader.SyncSource;

                        OnDtmfTone.Invoke(rtpEvent.EventID, rtpEvent.Duration);
                    }
                }

                if (_rtpEventSsrc != 0 && rtpEvent.EndOfEvent)
                {
                    //_logger.LogDebug($"RTP end of event {rtpEvent.EventID}.");
                    _rtpEventSsrc = 0;
                }
            }
        }

        /// <summary>
        /// Calling close indicates the SIP user agent is no longer required and it should not
        /// respond to any NEW requests. It will still respond to BYE in-dialog requests in order
        /// to correctly deal with re-transmits.
        /// </summary>
        public void Close()
        {
            _isClosed = true;
            m_transport.SIPTransportRequestReceived -= SIPTransportRequestReceived;
        }

        /// <summary>
        /// Final cleanup if instance is being discarded.
        /// </summary>
        public void Dispose()
        {
            if (IsCallActive)
            {
                Hangup();
            }

            m_transport.SIPTransportRequestReceived -= SIPTransportRequestReceived;

            if (m_isTransportExclusive)
            {
                m_transport.Shutdown();
            }
        }
    }
}
