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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
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
        /// Inidicates whether the call has been answered or not.
        /// </summary>
        public bool IsAnswered
        {
            get
            {
                if (m_uac != null)
                {
                    return m_uac.IsUACAnswered;
                }
                else if (m_uas != null)
                {
                    return m_uas.IsUASAnswered;
                }
                else
                {
                    return false;
                }
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
        /// The remote call party has sent us a new SDP. Re-INVITE requests are
        /// used to change the SDP and it can indicate a range of conditions such as:
        /// - Changing the RTP end point, e.g. a B2B server wants to reinvite iteslf out of the media path,
        /// - Call is being aplced on or off hold,
        /// - Codecs changed in response to bandwidth conditions,
        /// - And more.
        /// Applies to calls initiated by us and calls recevied by us.
        /// </summary>
        public event SIPCallSDPChangedDelegate CallRemoteSDPChanged;

        /// <summary>
        /// Call was hungup by the remote party. Applies to calls initiated by us and calls recevied
        /// by us.
        /// </summary>
        public event Action CallHungup;

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
        /// Handler for when an in dialog request is received on an established call.
        /// Typical types of request will be re-INVITES for things like putting a call on or
        /// off hold and REFER requests for transfers.
        /// </summary>
        /// <param name="request">The in dialog request received.</param>
        public async Task InCallRequestReceivedAsync(SIPRequest sipRequest)
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
                    logger.LogWarning($"DualUserAgent send response failed in InCallRequestReceivedAsync with {sendResult}.");
                }
            }
            else
            {
                if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    logger.LogDebug($"Matching dialogue found for {sipRequest.StatusLine}.");

                    SIPNonInviteTransaction byeTransaction = m_transport.CreateNonInviteTransaction(sipRequest, m_outboundProxy);
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);

                    CallHungup?.Invoke();

                    m_uac = null;
                    m_uas = null;
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    logger.LogDebug($"Re-INVITE request received {sipRequest.StatusLine}.");

                    UASInviteTransaction reInviteTransaction = m_transport.CreateUASTransaction(sipRequest, m_outboundProxy);

                    if (CallRemoteSDPChanged == null)
                    {
                        // The application isn't prepared to accept re-INVITE requests. We'll reject as gently as we can to try and not lose the call.
                        SIPResponse notAcceptableResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptable, null);
                        reInviteTransaction.SendFinalResponse(notAcceptableResponse);
                    }
                    else
                    {
                        SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
                        reInviteTransaction.SendProvisionalResponse(tryingResponse);

                        SDP originalSDP = null;
                        if (m_uac != null)
                        {
                            originalSDP = SDP.ParseSDPDescription(m_uac.ServerTransaction.TransactionFinalResponse.Body);
                        }
                        else if (m_uas != null)
                        {
                            originalSDP = SDP.ParseSDPDescription(m_uas.CallRequest.Body);
                        }

                        CallRemoteSDPChanged(sipRequest, originalSDP, SDP.ParseSDPDescription(sipRequest.Body));
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
                    //Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER received on dialogue " + dialogue.DialogueName + ", transfer mode is " + dialogue.TransferMode + ".", dialogue.Owner));

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

                    }
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
                else if(m_uas != null)
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
                ClientCallAnswered(uac, sipResponse);
            }
            else
            {
                logger.LogDebug($"Call attempt to {m_uac.CallDescriptor.Uri} was answered.");
            }
        }
    }
}
