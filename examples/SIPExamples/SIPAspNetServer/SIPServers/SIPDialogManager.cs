// ============================================================================
// FileName: SIPDialogManager.cs
//
// Description:
// Manages established dialogues.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 10 Feb 2008  Aaron Clauson   Created, Hobart, Australia.
// 01 Jan 2021  Aaron Clauson   Modified for .NET Core and tailored For ASP.NET server project. 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPAspNetServer.DataAccess;

namespace SIPAspNetServer
{
    public class SIPDialogManager
    {
        private readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<SIPDialogManager>();

        private static string m_userAgentString = SIPConstants.SIP_USERAGENT_STRING;
        private static string m_remoteHangupCause = SIPConstants.SIP_REMOTEHANGUP_CAUSE;
        private static readonly int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static readonly string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;

        //private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;
        //private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        //private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        //private SIPAssetPersistor<SIPCDRAsset> m_sipCDRPersistor;

        private SIPCallDataLayer m_sipCallDataLayer;
        private CDRDataLayer m_cdrDataLayer;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;

        /// <summary>
        /// Keeps a track of transactions that are processing in-dialog requests, such as BYE, re-INVITE etc.
        /// <Forwarded transaction id, Origin transaction id>.
        /// </summary>
        private ConcurrentDictionary<string, string> m_inDialogueTransactions = new ConcurrentDictionary<string, string>();

        public event Action<SIPDialogue> OnCallHungup;

        public SIPDialogManager(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;

            m_sipCallDataLayer = new SIPCallDataLayer();
            m_cdrDataLayer = new CDRDataLayer();
        }

        public void ProcessInDialogueRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            SIPDialogue dialogue = GetDialogue(sipRequest);

            if(dialogue == null)
            {
                SIPNonInviteTransaction nonInvTx = new SIPNonInviteTransaction(m_sipTransport, sipRequest, m_outboundProxy);
                var noLegResp = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                nonInvTx.SendResponse(noLegResp);
            }
            else if (sipRequest.Method == SIPMethodsEnum.BYE)
            {
                SIPNonInviteTransaction byeTransaction = new SIPNonInviteTransaction(m_sipTransport, sipRequest, m_outboundProxy);
                //logger.Debug("Matching dialogue found for BYE request to " + sipRequest.URI.ToString() + ".");
                SIPResponse byeResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                byeTransaction.SendResponse(byeResponse);

                // Normal BYE request.
                CallHungup(dialogue, sipRequest.Header.Reason, false);
            }
            //else if (sipRequest.Method == SIPMethodsEnum.INVITE)
            //{
            //    UASInviteTransaction reInviteTransaction = new UASInviteTransaction(m_sipTransport, sipRequest, m_outboundProxy);
            //    SIPResponse tryingResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
            //    reInviteTransaction.SendProvisionalResponse(tryingResponse);
            //    reInviteTransaction.CDR = null;     // Don't want CDR's on re-INVITEs.
            //    ForwardInDialogueRequest(dialogue, reInviteTransaction, localSIPEndPoint, remoteEndPoint);
            //}
            else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
            {
                // Send back the remote SDP.
                logger.LogDebug("OPTIONS request for established dialogue " + dialogue.DialogueName + ".");
                SIPNonInviteTransaction optionsTransaction = new SIPNonInviteTransaction(m_sipTransport, sipRequest, m_outboundProxy);
                SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                okResponse.Body = dialogue.RemoteSDP;
                okResponse.Header.ContentLength = okResponse.Body.Length;
                okResponse.Header.ContentType = m_sdpContentType;
                optionsTransaction.SendResponse(okResponse);
            }
            else if (sipRequest.Method == SIPMethodsEnum.MESSAGE)
            {
                logger.LogDebug("MESSAGE for call " + sipRequest.URI.ToString() + ": " + sipRequest.Body + ".");
                SIPNonInviteTransaction messageTransaction = new SIPNonInviteTransaction(m_sipTransport, sipRequest, m_outboundProxy);
                SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                messageTransaction.SendResponse(okResponse);
            }
            else
            {
                //// This is a request on an established call forward through to the opposite dialogue.
                //SIPNonInviteTransaction passThruTransaction = new SIPNonInviteTransaction(m_sipTransport, sipRequest, m_outboundProxy);
                //ForwardInDialogueRequest(dialogue, passThruTransaction, localSIPEndPoint, remoteEndPoint);
                logger.LogDebug($"{sipRequest.Method} received for dialogue {sipRequest.URI}, processing not yet implemented TODO!.");

                SIPNonInviteTransaction nonInvTx = new SIPNonInviteTransaction(m_sipTransport, sipRequest, m_outboundProxy);
                var noLegResp = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist, null);
                nonInvTx.SendResponse(noLegResp);
            }
        }

        public void BridgeDialogues(SIPDialogue clientDiaglogue, SIPDialogue forwardedDialogue)
        {
            logger.LogDebug($"Bridging dialogues {clientDiaglogue.DialogueName} and {forwardedDialogue.DialogueName}.");

            Guid bridgeId = Guid.NewGuid();
            clientDiaglogue.BridgeId = bridgeId;
            forwardedDialogue.BridgeId = bridgeId;

            //m_sipDialoguePersistor.Add(new SIPDialog(clientDiaglogue));
            //m_sipDialoguePersistor.Add(new SIPDialog(forwardedDialogue));

            m_sipCallDataLayer.Add(new SIPCall(clientDiaglogue));
            m_sipCallDataLayer.Add(new SIPCall(forwardedDialogue));
        }

        /// <summary>
        /// This method takes the necessary actions to terminate a bridged call.
        /// </summary>
        /// <param name="sipDialogue">The dialogue that the BYE request was received on.</param>
        /// <param name="hangupCause">If present an informational field to indicate the hangup cause.</param>
        /// <param name="sendBYEForOriginDialogue">If true means a BYE should be sent for the origin dialogue as well. This is used when a 3rd party
        /// call control agent is attempting to hangup a call.</param>
        public void CallHungup(SIPDialogue sipDialogue, string hangupCause, bool sendBYEForOriginDialogue)
        {
            if (sipDialogue != null)
            {
                //logger.Debug("BYE received on dialogue " + sipDialogue.DialogueName + ".");
                HangupDialogue(sipDialogue, hangupCause, sendBYEForOriginDialogue);

                if (sipDialogue.BridgeId != Guid.Empty)
                {
                    SIPDialogue orphanedDialogue = GetOppositeDialogue(sipDialogue);
                    if (orphanedDialogue != null)
                    {
                        HangupDialogue(orphanedDialogue, m_remoteHangupCause, true);
                        OnCallHungup?.Invoke(orphanedDialogue);
                    }
                }
                else
                {
                    logger.LogWarning("No bridge could be found for hungup call.");
                }

                OnCallHungup?.Invoke(sipDialogue);
            }
        }

        private void HangupDialogue(SIPDialogue dialogue, string hangupCause, bool sendBye)
        {
            if (dialogue.CDRId != Guid.Empty)
            {
                CDR cdr = m_cdrDataLayer.Get(dialogue.CDRId);
                if (cdr != null)
                {
                    //cdr.BridgeID = dialogue.BridgeId;
                    //cdr.Hungup(hangupCause);
                    m_cdrDataLayer.Hangup(cdr.ID, hangupCause);
                }
                else
                {
                    logger.LogWarning("CDR could not be found for remote dialogue in SIPDialogManager CallHungup.");
                }
            }
            else
            {
                logger.LogWarning("There was no CDR attached to orphaned dialogue in SIPDialogManager CallHungup.");
            }

            if (sendBye)
            {
                dialogue.Hangup(m_sipTransport, m_outboundProxy);
                OnCallHungup?.Invoke(dialogue);
            }

            m_sipCallDataLayer.Delete(dialogue.Id);
        }

        /// <summary>
        /// Attempts to locate a dialogue for an in-dialogue transaction.
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public SIPDialogue GetDialogue(SIPRequest sipRequest)
        {
            string callId = sipRequest.Header.CallId;
            string localTag = sipRequest.Header.To.ToTag;
            string remoteTag = sipRequest.Header.From.FromTag;

            return GetDialogue(callId, localTag, remoteTag);
        }

        public SIPDialogue GetDialogue(string callId, string localTag, string remoteTag)
        {
            SIPCall sipCall = m_sipCallDataLayer.Get(d => d.CallID == callId && d.LocalTag == localTag && d.RemoteTag == remoteTag);

            if (sipCall != null)
            {
                //logger.Debug("SIPDialogueManager dialogue match correctly found on dialogue hash.");
                return sipCall.ToSIPDialogue();
            }
            else
            {
                // Try on To tag.
                sipCall = m_sipCallDataLayer.Get(d => d.LocalTag == localTag);
                if (sipCall != null)
                {
                    logger.LogWarning("SIPDialogueManager dialogue match found on fallback mechanism of To tag.");
                    return sipCall.ToSIPDialogue();
                }

                // Try on From tag.
                sipCall = m_sipCallDataLayer.Get(d => d.RemoteTag == remoteTag);
                if (sipCall != null)
                {
                    logger.LogWarning("SIPDialogueManager dialogue match found on fallback mechanism of From tag.");
                    return sipCall.ToSIPDialogue();
                }

                // As an experiment will try on the Call-ID as well. However as a safeguard it will only succeed if there is only one instance of the
                // Call-ID in use. Since the Call-ID is not mandated by the SIP standard as being unique there it may be that matching on it causes more
                // problems then it solves.
                sipCall = m_sipCallDataLayer.Get(d => d.CallID == callId);
                if (sipCall != null)
                {
                    logger.LogWarning("SIPDialogueManager dialogue match found on fallback mechanism of Call-ID.");
                    return sipCall.ToSIPDialogue();
                }
            }

            return null;
        }

        /// <summary>
        /// Retrieves the other end of a call given the dialogue from one end.
        /// </summary>
        /// <param name="dialogue"></param>
        /// <returns></returns>
        public SIPDialogue GetOppositeDialogue(SIPDialogue dialogue)
        {
            if (dialogue.BridgeId != Guid.Empty)
            {
                SIPCall sipCall = m_sipCallDataLayer.Get(d => d.BridgeID == dialogue.BridgeId && d.ID != dialogue.Id);
                return (sipCall != null) ? sipCall.ToSIPDialogue() : null;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Attempts to reinvite an existing end of a call by sending a new SDP.
        /// </summary>
        /// <param name="dialogue">The dialogue describing the end of the call to be re-invited.</param>
        /// <param name="newSDP">The session description for the new dialogue desired.</param>
        //public void ReInvite(SIPDialogue dialogue, SIPDialogue substituteDialogue)
        //{
        //    string replacementSDP = substituteDialogue.RemoteSDP;
        //    // Determine whether the SDP needs to be mangled.
        //    IPEndPoint dialogueSDPSocket = SDP.GetSDPRTPEndPoint(dialogue.RemoteSDP);
        //    IPEndPoint replacementSDPSocket = SDP.GetSDPRTPEndPoint(substituteDialogue.RemoteSDP);
        //    bool wasMangled = false;

        //    if (!IPSocket.IsPrivateAddress(dialogueSDPSocket.Address.ToString()) && IPSocket.IsPrivateAddress(replacementSDPSocket.Address.ToString()))
        //    {
        //        // The SDP being used in the re-invite uses a private IP address but the SDP on the ua it's being sent to does not so mangle.
        //        string publicIPAddress = null;

        //        if (PublicIPAddress != null)
        //        {
        //            publicIPAddress = PublicIPAddress.ToString();
        //        }
        //        else if (substituteDialogue.RemoteTarget != null)
        //        {
        //            publicIPAddress = IPSocket.ParseHostFromSocket(substituteDialogue.RemoteTarget.Host);
        //        }

        //        if (publicIPAddress != null)
        //        {
        //            replacementSDP = SIPPacketMangler.MangleSDP(replacementSDP, publicIPAddress, out wasMangled);
        //        }
        //    }

        //    if (wasMangled)
        //    {
        //        logger.LogDebug("The SDP being used in a re-INVITE was mangled to " + SDP.GetSDPRTPEndPoint(replacementSDP) + ".");
        //    }

        //    // Check whether there is a need to send the re-invite by comparing the new SDP being sent with what has already been sent.
        //    //if (dialogue.SDP == replacementSDP)
        //    //{
        //    //    logger.Debug("A reinvite was not sent to " + dialogue.RemoteTarget.ToString() + " as the SDP has not changed.");
        //    //}
        //    //else
        //    //{
        //    // Resend even if SDP has not changed as an attempt to refresh a call having one-way audio issues.
        //    logger.LogDebug("Reinvite SDP being sent to " + dialogue.RemoteTarget.ToString() + ":\r\n" + replacementSDP);

        //    dialogue.CSeq = dialogue.CSeq + 1;
        //    m_sipDialoguePersistor.UpdateProperty(dialogue.Id, "CSeq", dialogue.CSeq);
        //    SIPEndPoint localSIPEndPoint = (m_outboundProxy != null) ? m_sipTransport.GetDefaultTransportContact(m_outboundProxy.Protocol) : m_sipTransport.GetDefaultTransportContact(SIPProtocolsEnum.udp);
        //    SIPRequest reInviteReq = GetInviteRequest(dialogue, localSIPEndPoint, replacementSDP);

        //    SIPEndPoint reinviteEndPoint = null;

        //    // If the outbound proxy is a loopback address, as it will normally be for local deployments, then it cannot be overriden.
        //    if (m_outboundProxy != null && IPAddress.IsLoopback(m_outboundProxy.Address))
        //    {
        //        reInviteReq.Header.ProxySendFrom = dialogue.ProxySendFrom;
        //        reinviteEndPoint = m_outboundProxy;
        //    }
        //    if (!dialogue.ProxySendFrom.IsNullOrBlank())
        //    {
        //        reInviteReq.Header.ProxySendFrom = dialogue.ProxySendFrom;
        //        // The proxy will always be listening on UDP port 5060 for requests from internal servers.
        //        reinviteEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(SIPEndPoint.ParseSIPEndPoint(dialogue.ProxySendFrom).Address, m_defaultSIPPort));
        //    }
        //    else
        //    {
        //        SIPDNSLookupResult lookupResult = m_sipTransport.GetRequestEndPoint(reInviteReq, m_outboundProxy, false);
        //        if (lookupResult.LookupError != null)
        //        {
        //            logger.Warn("ReInvite Failed to resolve " + lookupResult.URI.Host + ".");
        //        }
        //        else
        //        {
        //            reinviteEndPoint = lookupResult.GetSIPEndPoint();
        //        }
        //    }

        //    if (reinviteEndPoint != null)
        //    {
        //        UACInviteTransaction reInviteTransaction = new UACInviteTransaction(m_sipTransport, reInviteReq, reinviteEndPoint);
        //        reInviteTransaction.CDR = null; // Don't want CDRs on re-invites.
        //        reInviteTransaction.UACInviteTransactionFinalResponseReceived += ReInviteTransactionFinalResponseReceived;
        //        reInviteTransaction.SendInviteRequest(reinviteEndPoint, reInviteReq);
        //    }
        //    else
        //    {
        //        throw new ApplicationException("Could not forward re-invite as request end point could not be determined.\r\n" + reInviteReq.ToString());
        //    }
        //}

        //private void ReInviteTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        //{
        //    if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
        //    {
        //        logger.LogDebug("Reinvite request " + sipTransaction.TransactionRequest.URI.ToString() + " succeeeded with " + sipResponse.Status + ".");
        //    }
        //    else
        //    {
        //        logger.LogDebug("Reinvite request " + sipTransaction.TransactionRequest.URI.ToString() + " failed with " + sipResponse.Status + " " + sipResponse.ReasonPhrase + ".");
        //    }
        //}

        //private void ForwardInDialogueRequest(SIPDialogue dialogue, SIPTransaction inDialogueTransaction, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint)
        //{
        //    logger.LogDebug("In dialogue request " + inDialogueTransaction.TransactionRequest.Method + " received from for uri=" + inDialogueTransaction.TransactionRequest.URI.ToString() + ".");

        //    // Update the CSeq based on the latest received request.
        //    dialogue.CSeq = inDialogueTransaction.TransactionRequest.Header.CSeq;

        //    // Get the dialogue for the other end of the bridge.
        //    SIPDialogue bridgedDialogue = GetOppositeDialogue(dialogue);

        //    SIPEndPoint forwardSIPEndPoint = m_outboundProxy ?? m_sipTransport.GetDefaultSIPEndPoint(new SIPEndPoint(bridgedDialogue.RemoteTarget));
        //    IPAddress remoteUAIPAddress = (inDialogueTransaction.TransactionRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? remoteEndPoint.Address : SIPEndPoint.ParseSIPEndPoint(inDialogueTransaction.TransactionRequest.Header.ProxyReceivedFrom).Address;

        //    SIPRequest forwardedRequest = inDialogueTransaction.TransactionRequest.Copy();

        //    // Need to remove or reset headers from the copied request that conflict with the existing dialogue requests.
        //    forwardedRequest.Header.RecordRoutes = null;
        //    forwardedRequest.Header.MaxForwards = SIPConstants.DEFAULT_MAX_FORWARDS;

        //    forwardedRequest.URI = bridgedDialogue.RemoteTarget;
        //    forwardedRequest.Header.Routes = bridgedDialogue.RouteSet;
        //    forwardedRequest.Header.CallId = bridgedDialogue.CallId;
        //    bridgedDialogue.CSeq = bridgedDialogue.CSeq + 1;
        //    forwardedRequest.Header.CSeq = bridgedDialogue.CSeq;
        //    forwardedRequest.Header.To = new SIPToHeader(bridgedDialogue.RemoteUserField.Name, bridgedDialogue.RemoteUserField.URI, bridgedDialogue.RemoteTag);
        //    forwardedRequest.Header.From = new SIPFromHeader(bridgedDialogue.LocalUserField.Name, bridgedDialogue.LocalUserField.URI, bridgedDialogue.LocalTag);
        //    forwardedRequest.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(bridgedDialogue.RemoteTarget.Scheme, forwardSIPEndPoint)) };
        //    forwardedRequest.Header.Vias = new SIPViaSet();
        //    forwardedRequest.Header.Vias.PushViaHeader(new SIPViaHeader(forwardSIPEndPoint, CallProperties.CreateBranchId()));
        //    forwardedRequest.Header.UserAgent = m_userAgentString;
        //    forwardedRequest.Header.AuthenticationHeader = null;

        //    if (inDialogueTransaction.TransactionRequest.Body != null && inDialogueTransaction.TransactionRequest.Method == SIPMethodsEnum.INVITE)
        //    {
        //        bool wasMangled = false;
        //        forwardedRequest.Body = SIPPacketMangler.MangleSDP(inDialogueTransaction.TransactionRequest.Body, remoteUAIPAddress.ToString(), out wasMangled);
        //        logger.LogDebug("Re-INVITE wasmangled=" + wasMangled + " remote=" + remoteUAIPAddress.ToString() + ".");
        //        forwardedRequest.Header.ContentLength = forwardedRequest.Body.Length;
        //    }

        //    SIPEndPoint forwardEndPoint = null;
        //    if (!bridgedDialogue.ProxySendFrom.IsNullOrBlank())
        //    {
        //        forwardedRequest.Header.ProxySendFrom = bridgedDialogue.ProxySendFrom;
        //        // The proxy will always be listening on UDP port 5060 for requests from internal servers.
        //        forwardEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(SIPEndPoint.ParseSIPEndPoint(bridgedDialogue.ProxySendFrom).Address, m_defaultSIPPort));
        //    }
        //    else
        //    {
        //        SIPDNSLookupResult lookupResult = m_sipTransport.GetRequestEndPoint(forwardedRequest, m_outboundProxy, false);
        //        if (lookupResult.LookupError != null)
        //        {
        //            logger.Warn("ForwardInDialogueRequest Failed to resolve " + lookupResult.URI.Host + ".");
        //        }
        //        else
        //        {
        //            forwardEndPoint = lookupResult.GetSIPEndPoint();
        //        }
        //    }

        //    if (forwardEndPoint != null)
        //    {
        //        if (inDialogueTransaction.TransactionRequest.Method == SIPMethodsEnum.INVITE)
        //        {
        //            UACInviteTransaction forwardedTransaction = new UACInviteTransaction(m_sipTransport, forwardedRequest, m_outboundProxy);
        //            forwardedTransaction.CDR = null;    // Don't want CDR's on re-INVITES.
        //            forwardedTransaction.UACInviteTransactionFinalResponseReceived += InDialogueTransactionFinalResponseReceived;
        //            forwardedTransaction.UACInviteTransactionInformationResponseReceived += InDialogueTransactionInfoResponseReceived;
        //            forwardedTransaction.TransactionRemoved += InDialogueTransactionRemoved;

        //            logger.LogDebug("Forwarding re-INVITE from " + remoteEndPoint + " to " + forwardedRequest.URI.ToString() + ", first hop " + forwardEndPoint + ".");

        //            forwardedTransaction.SendInviteRequest();

        //            m_inDialogueTransactions.TryAdd(forwardedTransaction.TransactionId, inDialogueTransaction.TransactionId);
        //        }
        //        else
        //        {
        //            SIPNonInviteTransaction forwardedTransaction = new SIPNonInviteTransaction(m_sipTransport, forwardedRequest, m_outboundProxy);
        //            forwardedTransaction.NonInviteTransactionFinalResponseReceived += InDialogueTransactionFinalResponseReceived;
        //            forwardedTransaction.NonInviteTransactionInfoResponseReceived += InDialogueTransactionInfoResponseReceived;
        //            forwardedTransaction.TransactionRemoved += InDialogueTransactionRemoved;

        //            logger.LogDebug("Forwarding in dialogue " + forwardedRequest.Method + " from " + remoteEndPoint + " to " + forwardedRequest.URI.ToString() + ", first hop " + forwardEndPoint + ".");

        //            forwardedTransaction.SendRequest();

        //            m_inDialogueTransactions.TryAdd(forwardedTransaction.TransactionId, inDialogueTransaction.TransactionId);
        //        }

        //        // Update the dialogues CSeqs so future in dialogue requests can be forwarded correctly.
        //        m_sipDialoguePersistor.UpdateProperty(bridgedDialogue.Id, "CSeq", bridgedDialogue.CSeq);
        //        m_sipDialoguePersistor.UpdateProperty(dialogue.Id, "CSeq", dialogue.CSeq);
        //    }
        //    else
        //    {
        //        logger.LogDebug("Could not forward in dialogue request end point could not be determined " + forwardedRequest.URI.ToString() + ".");
        //    }
        //}

        //private void InDialogueTransactionInfoResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        //{
        //    SIPDialogue dialogue = GetDialogue(sipResponse.Header.CallId, sipResponse.Header.From.FromTag, sipResponse.Header.To.ToTag);

        //    // Lookup the originating transaction.
        //    SIPTransaction originTransaction = m_sipTransport.GetTransaction(m_inDialogueTransactions[sipTransaction.TransactionId]);

        //    SIPResponse response = sipResponse.Copy();
        //    response.Header.Vias = originTransaction.TransactionRequest.Header.Vias;
        //    response.Header.To = originTransaction.TransactionRequest.Header.To;
        //    response.Header.From = originTransaction.TransactionRequest.Header.From;
        //    response.Header.CallId = originTransaction.TransactionRequest.Header.CallId;
        //    response.Header.CSeq = originTransaction.TransactionRequest.Header.CSeq;
        //    response.Header.Contact = SIPContactHeader.CreateSIPContactList(new SIPURI(originTransaction.TransactionRequest.URI.Scheme, localSIPEndPoint));
        //    response.Header.RecordRoutes = null;    // Can't change route set within a dialogue.
        //    response.Header.UserAgent = m_userAgentString;

        //    logger.LogDebug("Forwarding in dialogue response from " + remoteEndPoint + " " + sipResponse.Header.CSeqMethod + " " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " to " + response.Header.Vias.TopViaHeader.ReceivedFromAddress + ".");

        //    // Forward the response back to the requester.
        //    originTransaction.SendInformationalResponse(response);
        //}

        //private void InDialogueTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        //{
        //    SIPDialogue dialogue = GetDialogue(sipResponse.Header.CallId, sipResponse.Header.From.FromTag, sipResponse.Header.To.ToTag);

        //    logger.LogDebug("Final response of " + sipResponse.StatusCode + " on " + sipResponse.Header.CSeqMethod + " in-dialogue transaction.");

        //    // Lookup the originating transaction.
        //    SIPTransaction originTransaction = m_sipTransport.GetTransaction(m_inDialogueTransactions[sipTransaction.TransactionId]);
        //    IPAddress remoteUAIPAddress = (sipResponse.Header.ProxyReceivedFrom.IsNullOrBlank()) ? remoteEndPoint.Address : SIPEndPoint.ParseSIPEndPoint(sipResponse.Header.ProxyReceivedFrom).Address;
        //    SIPEndPoint forwardSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint(sipResponse.Header.Vias.TopViaHeader.Transport);

        //    SIPResponse response = sipResponse.Copy();
        //    response.Header.Vias = originTransaction.TransactionRequest.Header.Vias;
        //    response.Header.To = originTransaction.TransactionRequest.Header.To;
        //    response.Header.From = originTransaction.TransactionRequest.Header.From;
        //    response.Header.CallId = originTransaction.TransactionRequest.Header.CallId;
        //    response.Header.CSeq = originTransaction.TransactionRequest.Header.CSeq;
        //    response.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(originTransaction.TransactionRequest.URI.Scheme, forwardSIPEndPoint)) };
        //    response.Header.RecordRoutes = null;    // Can't change route set within a dialogue.
        //    response.Header.UserAgent = m_userAgentString;

        //    if (sipResponse.Body != null && sipResponse.Header.CSeqMethod == SIPMethodsEnum.INVITE)
        //    {
        //        bool wasMangled = false;
        //        response.Body = SIPPacketMangler.MangleSDP(sipResponse.Body, remoteUAIPAddress.ToString(), out wasMangled);
        //        response.Header.ContentLength = response.Body.Length;
        //    }

        //    logger.LogDebug("Forwarding in dialogue response from " + remoteEndPoint + " " + sipResponse.Header.CSeqMethod + " final response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " to " + response.Header.Vias.TopViaHeader.ReceivedFromAddress + ".");

        //    // Forward the response back to the requester.
        //    originTransaction.SendResponse(response);
        //}

        private void InDialogueTransactionRemoved(SIPTransaction sipTransaction)
        {
            if (m_inDialogueTransactions.ContainsKey(sipTransaction.TransactionId))
            {
                m_inDialogueTransactions.TryRemove(sipTransaction.TransactionId, out _);
            }
        }

        private SIPRequest GetInviteRequest(SIPDialogue dialogue, SIPEndPoint localSIPEndPoint, string body)
        {
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, dialogue.RemoteTarget);

            SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader(dialogue.LocalUserField.ToString()), SIPToHeader.ParseToHeader(dialogue.RemoteUserField.ToString()), dialogue.CSeq, dialogue.CallId);
            SIPURI contactURI = new SIPURI(dialogue.RemoteTarget.Scheme, localSIPEndPoint);
            inviteHeader.Contact = SIPContactHeader.ParseContactHeader("<" + contactURI.ToString() + ">");
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteRequest.Header = inviteHeader;
            inviteRequest.Header.Routes = dialogue.RouteSet;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
            inviteRequest.Header.Vias.PushViaHeader(viaHeader);

            inviteRequest.Body = body;
            inviteRequest.Header.ContentLength = body.Length;
            inviteRequest.Header.ContentType = m_sdpContentType;

            return inviteRequest;
        }
    }
}
