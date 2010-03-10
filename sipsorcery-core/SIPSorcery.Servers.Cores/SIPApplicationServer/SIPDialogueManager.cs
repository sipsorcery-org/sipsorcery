// ============================================================================
// FileName: SIPDialogueManager.cs
//
// Description:
// Manages established dialogues.
//
// Author(s):
// Aaron Clauson
//
// History:
// 10 Feb 2008  Aaron Clauson   Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Transactions;
using SIPSorcery.AppServer.DialPlan;
using SIPSorcery.CRM;
using SIPSorcery.Net;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers
{
    public class SIPDialogueManager
    {
        private static ILog logger = AppState.logger;

        private static string m_userAgentString = SIPConstants.SIP_USERAGENT_STRING;
        private static string m_remoteHangupCause = SIPConstants.SIP_REMOTEHANGUP_CAUSE;
        private static string m_referReplacesParameter = SIPHeaderAncillary.SIP_REFER_REPLACES;
        private static string m_referNotifyEventValue = SIPConstants.SIP_REFER_NOTIFY_EVENT;
        private static string m_referNotifyContentType = SIPMIMETypes.REFER_CONTENT_TYPE;

        private SIPMonitorLogDelegate Log_External;
        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPAssetPersistor<SIPCDRAsset> m_sipCDRPersistor;

        private Dictionary<string, string> m_inDialogueTransactions = new Dictionary<string, string>();     // <Forwarded transaction id, Origin transaction id>.

        public static IPAddress PublicIPAddress;

        public SIPDialogueManager(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPMonitorLogDelegate logDelegate,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPAssetPersistor<SIPCDRAsset> sipCDRPersistor)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            Log_External = logDelegate;
            m_sipDialoguePersistor = sipDialoguePersistor;
            m_sipCDRPersistor = sipCDRPersistor;
        }

        public void ProcessInDialogueRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest, SIPDialogue dialogue, Func<string, string, string, string, string> webCallback)
        {
            try
            {
                if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                    //logger.Debug("Matching dialogue found for BYE request to " + sipRequest.URI.ToString() + ".");
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);

                    // Let the CallManager know so the forwarded leg of the call can be hung up.
                    CallHungup(dialogue, sipRequest.Header.Reason);
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    UASInviteTransaction reInviteTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                    SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
                    reInviteTransaction.SendInformationalResponse(tryingResponse);
                    reInviteTransaction.CDR = null;     // Don't want CDR's on re-INVITEs.
                    ForwardInDialogueRequest(dialogue, reInviteTransaction, localSIPEndPoint, remoteEndPoint);
                }
                else if (sipRequest.Method == SIPMethodsEnum.MESSAGE)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "MESSAGE for call " + sipRequest.URI.ToString() + ": " + sipRequest.Body + ".", dialogue.Owner));
                    SIPNonInviteTransaction messageTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    messageTransaction.SendFinalResponse(okResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.REFER)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER received on dialogue " + dialogue.DialogueName + ", transfer mode is " + dialogue.TransferMode + ".", dialogue.Owner));

                    if (sipRequest.Header.ReferTo.IsNullOrBlank())
                    {
                        // A REFER request must have a Refer-To header.
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Bad REFER request, no Refer-To header.", dialogue.Owner));
                        SIPResponse invalidResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing mandatory Refer-To header");
                        m_sipTransport.SendResponse(invalidResponse);
                    }
                    else
                    {
                        if (dialogue.TransferMode == SIPDialogueTransferModesEnum.NotAllowed)
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER rejected due to dialogue permissions.", dialogue.Owner));
                            SIPResponse declineTransferResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Decline, "Transfers are disabled on dialogue");
                            m_sipTransport.SendResponse(declineTransferResponse);
                        }
                        else if (Regex.Match(sipRequest.Header.ReferTo, m_referReplacesParameter).Success)
                        {
                            // Attended transfers are allowed unless explicitly blocked. Attended transfers are not dangerous 
                            // as no new call is created and it's the same as a re-invite.
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER received, attended transfer, Referred-By=" + sipRequest.Header.ReferredBy + ".", dialogue.Owner));
                            ProcessAttendedRefer(dialogue, sipRequest, localSIPEndPoint, remoteEndPoint);
                        }
                        else if (dialogue.TransferMode == SIPDialogueTransferModesEnum.BlindPlaceCall)
                        {
                            // A blind transfer that is permitted to initiate a new call.
                            SIPNonInviteTransaction referTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                            SIPResponse acceptedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Accepted, null);
                            referTransaction.SendFinalResponse(acceptedResponse);
                            //SIPDialogue oppositeDialogue = GetOppositeDialogue(dialogue);
                            SIPURI replacesURI = SIPURI.ParseSIPURI(sipRequest.Header.ReferTo);
                            webCallback(dialogue.Owner, replacesURI.User, "transfer", dialogue.CallId);
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER received, blind transfer, Refer-To=" + sipRequest.Header.ReferTo + ", Referred-By=" + sipRequest.Header.ReferredBy + ".", dialogue.Owner));
                            SIPNonInviteTransaction passThruTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                            ForwardInDialogueRequest(dialogue, passThruTransaction, localSIPEndPoint, remoteEndPoint);
                        }
                    }
                }
                else
                {
                    // This is a request on an established call forward through to the opposite dialogue.
                    SIPNonInviteTransaction passThruTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                    ForwardInDialogueRequest(dialogue, passThruTransaction, localSIPEndPoint, remoteEndPoint);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProcessInDialogueRequest. " + excp.Message);
                throw;
            }
        }

        public void CreateDialogueBridge(SIPDialogue clientDiaglogue, SIPDialogue forwardedDialogue, string owner)
        {
            logger.Debug("Creating dialogue bridge between " + clientDiaglogue.DialogueName + " and " + forwardedDialogue.DialogueName + ".");

            Guid bridgeId = Guid.NewGuid();
            clientDiaglogue.BridgeId = bridgeId;
            forwardedDialogue.BridgeId = bridgeId;

            m_sipDialoguePersistor.Add(new SIPDialogueAsset(clientDiaglogue));
            m_sipDialoguePersistor.Add(new SIPDialogueAsset(forwardedDialogue));

            SIPEndPoint clientDialogueRemoteEP = (IPSocket.IsIPSocket(clientDiaglogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(clientDiaglogue.RemoteTarget.Host) : null;
            Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueCreated, clientDiaglogue.Owner, clientDiaglogue));

            SIPEndPoint forwardedDialogueRemoteEP = (IPSocket.IsIPSocket(forwardedDialogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(forwardedDialogue.RemoteTarget.Host) : null;
            Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueCreated, forwardedDialogue.Owner, forwardedDialogue));
        }

        public void CallHungup(SIPDialogue sipDialogue, string hangupCause)
        {
            try
            {
                if (sipDialogue != null && sipDialogue.BridgeId != Guid.Empty)
                {
                    logger.Debug("BYE received on dialogue " + sipDialogue.DialogueName + ".");

                    SIPDialogue orphanedDialogue = GetOppositeDialogue(sipDialogue);

                    // Update CDR's.
                    if (sipDialogue.CDRId != Guid.Empty)
                    {
                        SIPCDRAsset cdr = m_sipCDRPersistor.Get(sipDialogue.CDRId);
                        if (cdr != null)
                        {
                            cdr.BridgeId = sipDialogue.BridgeId.ToString();
                            cdr.Hungup(hangupCause);
                        }
                        else
                        {
                            logger.Warn("CDR could not be found for local dialogue in SIPCallManager CallHungup.");
                        }
                    }
                    else
                    {
                        logger.Warn("There was no CDR attached to local dialogue in SIPCallManager CallHungup.");
                    }

                    if (orphanedDialogue != null)
                    {
                        logger.Debug("Hanging up orphaned dialogue " + orphanedDialogue.DialogueName + ".");

                        if (orphanedDialogue.CDRId != Guid.Empty)
                        {
                            SIPCDRAsset cdr = m_sipCDRPersistor.Get(orphanedDialogue.CDRId);
                            if (cdr != null)
                            {
                                cdr.BridgeId = orphanedDialogue.BridgeId.ToString();
                                cdr.Hungup(m_remoteHangupCause);
                            }
                            else
                            {
                                logger.Warn("CDR could not be found for remote dialogue in SIPCallManager CallHungup.");
                            }
                        }
                        else
                        {
                            logger.Warn("There was no CDR attached to orphaned dialogue in SIPCallManager CallHungup.");
                        }

                        orphanedDialogue.Hangup(m_sipTransport, m_outboundProxy);
                        m_sipDialoguePersistor.Delete(new SIPDialogueAsset(orphanedDialogue));

                        SIPEndPoint orphanedDialogueRemoteEP = (IPSocket.IsIPSocket(orphanedDialogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(orphanedDialogue.RemoteTarget.Host) : null;
                        Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved, orphanedDialogue.Owner, orphanedDialogue));
                    }

                    m_sipDialoguePersistor.Delete(new SIPDialogueAsset(sipDialogue));
                    Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved, sipDialogue.Owner, sipDialogue));
                }
                else
                {
                    logger.Warn("No bridge could be found for hungup call.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallManager CallHungup. " + excp.Message);
            }
        }

        /// <summary>
        /// Attempts to locate a dialogue for an in-dialogue transaction.
        /// </summary>
        /// <param name="transaction"></param>
        /// <returns></returns>
        public SIPDialogue GetDialogue(SIPRequest sipRequest)
        {
            try
            {
                string callId = sipRequest.Header.CallId;
                string localTag = sipRequest.Header.To.ToTag;
                string remoteTag = sipRequest.Header.From.FromTag;

                return GetDialogue(callId, localTag, remoteTag);
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetDialogue. " + excp);
                return null;
            }
        }

        public SIPDialogue GetDialogue(string replaces)
        {
            try
            {
                if (replaces.IsNullOrBlank())
                {
                    return null;
                }

                string unescapedReplaces = SIPEscape.SIPUnescapeString(replaces);

                Match replacesMatch = Regex.Match(unescapedReplaces, "^(?<callid>.*?);to-tag=(?<totag>.*?);from-tag=(?<fromtag>.*)");
                if (replacesMatch.Success)
                {
                    string callId = replacesMatch.Result("${callid}");
                    string localTag = replacesMatch.Result("${totag}");
                    string remoteTag = replacesMatch.Result("${fromtag}");

                    logger.Debug("Call-ID=" + callId + ", localtag=" + localTag + ", remotetag=" + remoteTag + ".");

                    SIPDialogue replacesDialogue = GetDialogue(callId, localTag, remoteTag);

                    if (replacesDialogue == null)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dialogue was not found for the Replaces parameter on a Refer-To header.", null));
                    }

                    return replacesDialogue;
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "The Replaces parameter on a Refer-To header was not in the expected fromat, " + replaces + ".", null));
                    return null;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetDialogue (replaces). " + excp);
                return null;
            }
        }

        public SIPDialogue GetDialogue(string callId, string localTag, string remoteTag)
        {
            try
            {
                //string dialogueId = SIPDialogue.GetDialogueId(callId, localTag, remoteTag);
                SIPDialogueAsset dialogueAsset = m_sipDialoguePersistor.Get(d => d.CallId == callId && d.LocalTag == localTag && d.RemoteTag == remoteTag);

                if (dialogueAsset != null)
                {
                    //logger.Debug("SIPDialogueManager dialogue match correctly found on dialogue hash.");
                    return dialogueAsset.SIPDialogue;
                }
                else
                {
                    // Try on To tag.
                    dialogueAsset = m_sipDialoguePersistor.Get(d => d.LocalTag == localTag);
                    if (dialogueAsset != null)
                    {
                        logger.Warn("SIPDialogueManager dialogue match found on fallback mechanism of To tag.");
                        return dialogueAsset.SIPDialogue;
                    }

                    // Try on From tag.
                    dialogueAsset = m_sipDialoguePersistor.Get(d => d.RemoteTag == remoteTag);
                    if (dialogueAsset != null)
                    {
                        logger.Warn("SIPDialogueManager dialogue match found on fallback mechanism of From tag.");
                        return dialogueAsset.SIPDialogue;
                    }

                    // As an experiment will try on the Call-ID as well. However as a safeguard it will only succeed if there is only one instance of the
                    // Call-ID in use. Since the Call-ID is not mandated by the SIP standard as being unique there it may be that matching on it causes more
                    // problems then it solves.
                    dialogueAsset = m_sipDialoguePersistor.Get(d => d.CallId == callId);
                    if (dialogueAsset != null)
                    {
                        logger.Warn("SIPDialogueManager dialogue match found on fallback mechanism of Call-ID.");
                        return dialogueAsset.SIPDialogue;
                    }
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetDialogue. " + excp);
                return null;
            }
        }

        /// <summary>
        /// This method applies very liberal rules to find a matching dialogue:
        /// 1. Treat the call identifier as a Call-ID,
        /// 2. If no dialogue matches for that try with the call identifier as the from username on the local user field,
        /// </summary>
        /// <param name="owner">The dialogue owner to use when attempting to find a match.</param>
        /// <param name="callIdentifier">A call identifier field to try and match a dialogue against.</param>
        /// <returns>A dialogue if a match is found or null otherwise.</returns>
        public SIPDialogue GetDialogueRelaxed(string owner, string callIdentifier)
        {
            if (owner.IsNullOrBlank() || callIdentifier.IsNullOrBlank())
            {
                return null;
            }
            else
            {
                owner = owner.ToLower();

                SIPDialogue callIDDialogue = GetDialogue(callIdentifier, null, null);
                if (callIDDialogue != null && callIDDialogue.Owner == owner)
                {
                    return callIDDialogue;
                }
                else
                {
                    List<SIPDialogueAsset> dialogueAssets = m_sipDialoguePersistor.Get(d => d.Owner == owner, null, 0, Int32.MaxValue);
                    if (dialogueAssets != null && dialogueAssets.Count > 0)
                    {
                        SIPDialogueAsset matchingDialogue = null;

                        foreach (SIPDialogueAsset dialogueAsset in dialogueAssets)
                        {
                            if (dialogueAsset.LocalUserField.Contains(callIdentifier))
                            {
                                if (matchingDialogue == null)
                                {
                                    matchingDialogue = dialogueAsset;
                                }
                                else
                                {
                                    // Ambiguous match, two or more dialogues match when matching on the call identifier string.
                                    return null;
                                }
                            }
                        }

                        if (matchingDialogue != null)
                        {
                            return matchingDialogue.SIPDialogue;
                        }
                    }
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
                string bridgeIdString = dialogue.BridgeId.ToString();
                SIPDialogueAsset dialogueAsset = m_sipDialoguePersistor.Get(d => d.BridgeId == bridgeIdString && d.Id != dialogue.Id);
                return (dialogueAsset != null) ? dialogueAsset.SIPDialogue : null;
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
        public void ReInvite(SIPDialogue dialogue, SIPDialogue substituteDialogue)
        {
            try
            {
                string replacementSDP = substituteDialogue.RemoteSDP;
                // Determine whether the SDP needs to be mangled.
                IPEndPoint dialogueSDPSocket = SDP.GetSDPRTPEndPoint(dialogue.RemoteSDP);
                IPEndPoint replacementSDPSocket = SDP.GetSDPRTPEndPoint(substituteDialogue.RemoteSDP);
                bool wasMangled = false;

                if (!IPSocket.IsPrivateAddress(dialogueSDPSocket.Address.ToString()) && IPSocket.IsPrivateAddress(replacementSDPSocket.Address.ToString()))
                {
                    // The SDP being used in the re-invite uses a private IP address but the SDP on the ua it's being sent to does not so mangle.
                    string publicIPAddress = (PublicIPAddress != null) ? PublicIPAddress.ToString() : IPSocket.ParseHostFromSocket(substituteDialogue.RemoteTarget.Host);
                    replacementSDP = SIPPacketMangler.MangleSDP(replacementSDP, publicIPAddress, out wasMangled);
                }

                if (wasMangled)
                {
                    logger.Debug("The SDP being used in a re-INVITE was mangled to " + SDP.GetSDPRTPEndPoint(replacementSDP) + ".");
                }

                // Check whether there is a need to send the re-invite by comparing the new SDP being sent with what has already been sent.
                if (dialogue.SDP == replacementSDP)
                {
                    logger.Debug("A reinvite was not sent to " + dialogue.RemoteTarget.ToString() + " as the SDP has not changed.");
                }
                else
                {
                    logger.Debug("Reinvite SDP being sent to " + dialogue.RemoteTarget.ToString() + ":\r\n" + replacementSDP);

                    dialogue.CSeq = dialogue.CSeq + 1;
                    m_sipDialoguePersistor.UpdateProperty(dialogue.Id, "CSeq", dialogue.CSeq);
                    SIPEndPoint localSIPEndPoint = (m_outboundProxy != null) ? m_sipTransport.GetDefaultTransportContact(m_outboundProxy.SIPProtocol) : m_sipTransport.GetDefaultTransportContact(SIPProtocolsEnum.udp);
                    SIPRequest reInviteReq = GetInviteRequest(dialogue, localSIPEndPoint, replacementSDP);
                    reInviteReq.Header.ProxySendFrom = dialogue.ProxySendFrom;
                    SIPEndPoint reinviteEndPoint = m_sipTransport.GetRequestEndPoint(reInviteReq, m_outboundProxy, true);

                    if (reinviteEndPoint != null)
                    {
                        UACInviteTransaction reInviteTransaction = m_sipTransport.CreateUACTransaction(reInviteReq, reinviteEndPoint, localSIPEndPoint, m_outboundProxy);
                        reInviteTransaction.CDR = null; // Don't want CDRs on re-invites.
                        reInviteTransaction.UACInviteTransactionFinalResponseReceived += ReInviteTransactionFinalResponseReceived;
                        reInviteTransaction.SendInviteRequest(reinviteEndPoint, reInviteReq);
                    }
                    else
                    {
                        throw new ApplicationException("Could not forward re-invite as request end point could not be determined.\r\n" + reInviteReq.ToString());
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallManager ReInvite. " + excp.Message);
                throw excp;
            }
        }

        private void ReInviteTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                //SIPRequest inviteRequest = sipTransaction.TransactionRequest;
                //SIPDialogue dialogue = GetDialogue(inviteRequest.Header.CallId, inviteRequest.Header.From.FromTag, inviteRequest.Header.To.ToTag);
                //m_dialogueBridges[dialogueId] = m_reInvitedDialogues[dialogueId];
                //m_reInvitedDialogues.Remove(dialogueId);

                if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode <= 299)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Reinvite request " + sipTransaction.TransactionRequest.URI.ToString() + " succeeeded with " + sipResponse.Status + ".", null));
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Reinvite request " + sipTransaction.TransactionRequest.URI.ToString() + " failed with " + sipResponse.Status + " " + sipResponse.ReasonPhrase + ".", null));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ReInviteTransactionFinalResponseReceived. " + excp.Message);
                throw excp;
            }
        }

        private void ForwardInDialogueRequest(SIPDialogue dialogue, SIPTransaction inDialogueTransaction, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "In dialogue request " + inDialogueTransaction.TransactionRequest.Method + " received from for uri=" + inDialogueTransaction.TransactionRequest.URI.ToString() + ".", null));

                // Update the CSeq based on the latest received request.
                dialogue.CSeq = inDialogueTransaction.TransactionRequest.Header.CSeq;

                // Get the dialogue for the other end of the bridge.
                SIPDialogue bridgedDialogue = GetOppositeDialogue(dialogue);

                SIPEndPoint forwardSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint(new SIPEndPoint(bridgedDialogue.RemoteTarget));
                IPAddress remoteUAIPAddress = (inDialogueTransaction.TransactionRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? remoteEndPoint.SocketEndPoint.Address : SIPEndPoint.ParseSIPEndPoint(inDialogueTransaction.TransactionRequest.Header.ProxyReceivedFrom).SocketEndPoint.Address;

                SIPRequest forwardedRequest = inDialogueTransaction.TransactionRequest.Copy();
                forwardedRequest.URI = bridgedDialogue.RemoteTarget;
                forwardedRequest.Header.Routes = bridgedDialogue.RouteSet;
                forwardedRequest.Header.CallId = bridgedDialogue.CallId;
                bridgedDialogue.CSeq = bridgedDialogue.CSeq + 1;
                forwardedRequest.Header.CSeq = bridgedDialogue.CSeq;
                forwardedRequest.Header.To = new SIPToHeader(bridgedDialogue.RemoteUserField.Name, bridgedDialogue.RemoteUserField.URI, bridgedDialogue.RemoteTag);
                forwardedRequest.Header.From = new SIPFromHeader(bridgedDialogue.LocalUserField.Name, bridgedDialogue.LocalUserField.URI, bridgedDialogue.LocalTag);
                forwardedRequest.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(bridgedDialogue.RemoteTarget.Scheme, forwardSIPEndPoint)) };
                forwardedRequest.Header.Vias = new SIPViaSet();
                forwardedRequest.Header.Vias.PushViaHeader(new SIPViaHeader(forwardSIPEndPoint, CallProperties.CreateBranchId()));
                forwardedRequest.Header.UserAgent = m_userAgentString;
                forwardedRequest.Header.AuthenticationHeader = null;
                forwardedRequest.Header.ProxySendFrom = bridgedDialogue.ProxySendFrom;

                if (inDialogueTransaction.TransactionRequest.Body != null && inDialogueTransaction.TransactionRequest.Method == SIPMethodsEnum.INVITE)
                {
                    bool wasMangled = false;
                    forwardedRequest.Body = SIPPacketMangler.MangleSDP(inDialogueTransaction.TransactionRequest.Body, remoteUAIPAddress.ToString(), out wasMangled);
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Re-INVITE wasmangled=" + wasMangled + " remote=" + remoteUAIPAddress.ToString() + ".", null));
                    forwardedRequest.Header.ContentLength = forwardedRequest.Body.Length;
                }

                SIPEndPoint forwardEndPoint = m_sipTransport.GetRequestEndPoint(forwardedRequest, m_outboundProxy, true);

                if (forwardEndPoint != null)
                {
                    if (inDialogueTransaction.TransactionRequest.Method == SIPMethodsEnum.INVITE)
                    {
                        UACInviteTransaction forwardedTransaction = m_sipTransport.CreateUACTransaction(forwardedRequest, forwardEndPoint, localSIPEndPoint, m_outboundProxy);
                        forwardedTransaction.CDR = null;    // Don't want CDR's on re-INVITES.
                        forwardedTransaction.UACInviteTransactionFinalResponseReceived += InDialogueTransactionFinalResponseReceived;
                        forwardedTransaction.UACInviteTransactionInformationResponseReceived += InDialogueTransactionInfoResponseReceived;
                        forwardedTransaction.TransactionRemoved += InDialogueTransactionRemoved;

                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding re-INVITE from " + remoteEndPoint + " to " + forwardedRequest.URI.ToString() + ", first hop " + forwardEndPoint + ".", dialogue.Owner));

                        forwardedTransaction.SendReliableRequest();

                        lock (m_inDialogueTransactions)
                        {
                            m_inDialogueTransactions.Add(forwardedTransaction.TransactionId, inDialogueTransaction.TransactionId);
                        }
                    }
                    else
                    {
                        SIPNonInviteTransaction forwardedTransaction = m_sipTransport.CreateNonInviteTransaction(forwardedRequest, forwardEndPoint, localSIPEndPoint, m_outboundProxy);
                        forwardedTransaction.NonInviteTransactionFinalResponseReceived += InDialogueTransactionFinalResponseReceived;
                        forwardedTransaction.NonInviteTransactionInfoResponseReceived += InDialogueTransactionInfoResponseReceived;
                        forwardedTransaction.TransactionRemoved += InDialogueTransactionRemoved;

                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding in dialogue " + forwardedRequest.Method + " from " + remoteEndPoint + " to " + forwardedRequest.URI.ToString() + ", first hop " + forwardEndPoint + ".", dialogue.Owner));

                        forwardedTransaction.SendReliableRequest();

                        lock (m_inDialogueTransactions)
                        {
                            m_inDialogueTransactions.Add(forwardedTransaction.TransactionId, inDialogueTransaction.TransactionId);
                        }
                    }

                    // Update the dialogues CSeqs so future in dialogue requests can be forwarded correctly.
                    m_sipDialoguePersistor.UpdateProperty(bridgedDialogue.Id, "CSeq", bridgedDialogue.CSeq);
                    m_sipDialoguePersistor.UpdateProperty(dialogue.Id, "CSeq", dialogue.CSeq);
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not forward in dialogue request end point could not be determined " + forwardedRequest.URI.ToString() + ".", dialogue.Owner));
                }
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Exception forwarding in dialogue request. " + excp.Message, dialogue.Owner));
            }
        }

        /// <summary>
        /// Performs an attended transfer based on a REFER request with a Replaces parameter on the Refer-To header.
        /// </summary>
        /// <param name="dialogue">The dialogue matching the the REFER request headers (Call-ID, To tag and From tag).</param>
        /// <param name="referTransaction">The REFER request.</param>
        /// <param name="localEndPoint">The local SIP end point the REFER request was received on.</param>
        /// <param name="remoteEndPoint">The remote SIP end point the REFER request was received from.</param>
        private void ProcessAttendedRefer(SIPDialogue dialogue, SIPRequest referRequest, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Initiating attended transfer .", dialogue.Owner));

                SIPNonInviteTransaction referTransaction = m_sipTransport.CreateNonInviteTransaction(referRequest, remoteEndPoint, localEndPoint, m_outboundProxy);
                SIPUserField referToField = SIPUserField.ParseSIPUserField(referRequest.Header.ReferTo);

                if (referToField == null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Error on transfer, could not parse Refer-To header: " + referRequest.Header.ReferTo + ".", dialogue.Owner));
                    SIPResponse errorResponse = SIPTransport.GetResponse(referRequest, SIPResponseStatusCodesEnum.BadRequest, "Could not parse Refer-To header");
                    referTransaction.SendFinalResponse(errorResponse);
                }
                else
                {
                    string replaces = referToField.URI.Headers.Get(m_referReplacesParameter);

                    SIPDialogue replacesDialogue = GetDialogue(replaces);
                    if (replacesDialogue == null)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not locate the dialogue for the Replaces parameter, passing through", dialogue.Owner));
                        ForwardInDialogueRequest(dialogue, referTransaction, localEndPoint, remoteEndPoint);
                    }
                    else
                    {
                        logger.Debug("REFER dialogue being replaced " + replacesDialogue.DialogueName + ".");

                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Replacement dialgoue found on Refer, accepting.", dialogue.Owner));

                        SIPDialogue remainingDialogue = GetOppositeDialogue(replacesDialogue);
                        SIPDialogue remaining2Dialogue = GetOppositeDialogue(dialogue);

                        logger.Debug("REFER dialogue remaining " + remainingDialogue.DialogueName + ".");

                        Guid newBridgeId = Guid.NewGuid();
                        remainingDialogue.BridgeId = newBridgeId;
                        remainingDialogue.CSeq++;
                        remaining2Dialogue.BridgeId = newBridgeId;
                        remaining2Dialogue.CSeq++;

                        m_sipDialoguePersistor.Update(new SIPDialogueAsset(remainingDialogue));
                        m_sipDialoguePersistor.Update(new SIPDialogueAsset(remaining2Dialogue));

                        Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated, remainingDialogue.Owner, remainingDialogue));
                        Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated, remaining2Dialogue.Owner, remaining2Dialogue));

                        SIPResponse acceptedResponse = SIPTransport.GetResponse(referRequest, SIPResponseStatusCodesEnum.Accepted, null);
                        referTransaction.SendFinalResponse(acceptedResponse);

                        SIPRequest notifyTryingRequest = GetNotifyRequest(referRequest, dialogue, new SIPResponse(SIPResponseStatusCodesEnum.Trying, null, null), localEndPoint);
                        SIPEndPoint forwardEndPoint = m_sipTransport.GetRequestEndPoint(notifyTryingRequest, m_outboundProxy, true);
                        SIPNonInviteTransaction notifyTryingTransaction = m_sipTransport.CreateNonInviteTransaction(notifyTryingRequest, forwardEndPoint, localEndPoint, m_outboundProxy);
                        notifyTryingTransaction.SendReliableRequest();

                        logger.Debug("Reinviting " + remainingDialogue.DialogueName + " with " + remaining2Dialogue.DialogueName + ".");

                        ReInvite(remainingDialogue, remaining2Dialogue);
                        ReInvite(remaining2Dialogue, remainingDialogue);

                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Transfer dialogue re-invites complete.", dialogue.Owner));

                        SIPRequest notifyOkRequest = GetNotifyRequest(referRequest, dialogue, new SIPResponse(SIPResponseStatusCodesEnum.Ok, null, null), localEndPoint);
                        SIPNonInviteTransaction notifyOkTransaction = m_sipTransport.CreateNonInviteTransaction(notifyOkRequest, forwardEndPoint, localEndPoint, m_outboundProxy);
                        notifyOkTransaction.SendReliableRequest();

                        // Hangup redundant dialogues.
                        logger.Debug("Hanging up redundant dialogues post transfer.");
                        logger.Debug("Hanging up " + dialogue.DialogueName + ".");
                        dialogue.Hangup(m_sipTransport, m_outboundProxy);
                        CallHungup(dialogue, "Attended transfer");
                        logger.Debug("Hanging up " + replacesDialogue.DialogueName + ".");
                        replacesDialogue.Hangup(m_sipTransport, m_outboundProxy);
                        CallHungup(replacesDialogue, "Attended transfer");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProcessAttendedRefer. " + excp.Message);
                throw;
            }
        }

        /// <summary>
        /// Performs transfer between 3 established dialogues (answered calls). The dead dialogue is being replaced by 
        /// the answered dialogue such that a bridged call between the dead and orphaned dialogues now becomes one between the
        /// orphaned and answered dialogues.
        /// </summary>
        /// <param name="deadDialogue">The dialogue that will be terminated.</param>
        /// <param name="orphanedDialogue">The opposite side of the dead dialogue that will be bridged with the answered dialogue.</param>
        /// <param name="answeredDialogue">The newly answered dialogue that will be bridged with the orpahned dialogue.</param>
        public void DialogueTransfer(SIPDialogue deadDialogue, SIPDialogue orphanedDialogue, SIPDialogue answeredDialogue)
        {
            try
            {
                logger.Debug("SIPDialogueManager DialogueTransfer.");

                // Create bridge between answered dialogue and other end of dialogue being replaced.
                Guid newBridgeId = Guid.NewGuid();
                orphanedDialogue.BridgeId = newBridgeId;
                answeredDialogue.BridgeId = newBridgeId;
                m_sipDialoguePersistor.Update(new SIPDialogueAsset(orphanedDialogue));
                m_sipDialoguePersistor.Add(new SIPDialogueAsset(answeredDialogue));

                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueCreated, answeredDialogue.Owner, answeredDialogue));
                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated, orphanedDialogue.Owner, orphanedDialogue));

                logger.Debug("Hanging up dead dialogue");
                // Hangup dialogue being replaced.
                deadDialogue.Hangup(m_sipTransport, m_outboundProxy);
                CallHungup(deadDialogue, "Blind transfer");

                logger.Debug("Reinviting two remaining dialogues");
                // Reinvite  other end of dialogue being replaced to answered dialogue.
                ReInvite(orphanedDialogue, answeredDialogue);
                //ReInvite(answeredDialogue, orphanedDialogue.SDP);
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialogueTransfer. " + excp.Message);
            }
        }

        private void InDialogueTransactionInfoResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            SIPDialogue dialogue = GetDialogue(sipResponse.Header.CallId, sipResponse.Header.From.FromTag, sipResponse.Header.To.ToTag);
            string owner = (dialogue != null) ? dialogue.Owner : null;

            try
            {
                // Lookup the originating transaction.
                SIPTransaction originTransaction = m_sipTransport.GetTransaction(m_inDialogueTransactions[sipTransaction.TransactionId]);

                SIPResponse response = sipResponse.Copy();
                response.Header.Vias = originTransaction.TransactionRequest.Header.Vias;
                response.Header.To = originTransaction.TransactionRequest.Header.To;
                response.Header.From = originTransaction.TransactionRequest.Header.From;
                response.Header.CallId = originTransaction.TransactionRequest.Header.CallId;
                response.Header.CSeq = originTransaction.TransactionRequest.Header.CSeq;
                response.Header.Contact = SIPContactHeader.CreateSIPContactList(new SIPURI(originTransaction.TransactionRequest.URI.Scheme, localSIPEndPoint));
                response.Header.RecordRoutes = null;    // Can't change route set within a dialogue.
                response.Header.UserAgent = m_userAgentString;

                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding in dialogue response from " + remoteEndPoint + " " + sipResponse.Header.CSeqMethod + " " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " to " + response.Header.Vias.TopViaHeader.ReceivedFromAddress + ".", owner));

                // Forward the response back to the requester.
                originTransaction.SendInformationalResponse(response);
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Exception processing in dialogue " + sipResponse.Header.CSeqMethod + " info response. " + excp.Message, owner));
            }
        }

        private void InDialogueTransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            SIPDialogue dialogue = GetDialogue(sipResponse.Header.CallId, sipResponse.Header.From.FromTag, sipResponse.Header.To.ToTag);
            string owner = (dialogue != null) ? dialogue.Owner : null;

            try
            {
                logger.Debug("Final response of " + sipResponse.StatusCode + " on " + sipResponse.Header.CSeqMethod + " in-dialogue transaction.");

                // Lookup the originating transaction.
                SIPTransaction originTransaction = m_sipTransport.GetTransaction(m_inDialogueTransactions[sipTransaction.TransactionId]);
                IPAddress remoteUAIPAddress = (sipResponse.Header.ProxyReceivedFrom.IsNullOrBlank()) ? remoteEndPoint.SocketEndPoint.Address : SIPEndPoint.ParseSIPEndPoint(sipResponse.Header.ProxyReceivedFrom).SocketEndPoint.Address;
                SIPEndPoint forwardSIPEndPoint = m_sipTransport.GetDefaultSIPEndPoint(sipResponse.Header.Vias.TopViaHeader.Transport);

                SIPResponse response = sipResponse.Copy();
                response.Header.Vias = originTransaction.TransactionRequest.Header.Vias;
                response.Header.To = originTransaction.TransactionRequest.Header.To;
                response.Header.From = originTransaction.TransactionRequest.Header.From;
                response.Header.CallId = originTransaction.TransactionRequest.Header.CallId;
                response.Header.CSeq = originTransaction.TransactionRequest.Header.CSeq;
                response.Header.Contact = new List<SIPContactHeader>() { new SIPContactHeader(null, new SIPURI(originTransaction.TransactionRequest.URI.Scheme, forwardSIPEndPoint)) };
                response.Header.RecordRoutes = null;    // Can't change route set within a dialogue.
                response.Header.UserAgent = m_userAgentString;

                if (sipResponse.Body != null && sipResponse.Header.CSeqMethod == SIPMethodsEnum.INVITE)
                {
                    bool wasMangled = false;
                    response.Body = SIPPacketMangler.MangleSDP(sipResponse.Body, remoteUAIPAddress.ToString(), out wasMangled);
                    response.Header.ContentLength = response.Body.Length;
                }

                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Forwarding in dialogue response from " + remoteEndPoint + " " + sipResponse.Header.CSeqMethod + " final response " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " to " + response.Header.Vias.TopViaHeader.ReceivedFromAddress + ".", owner));

                // Forward the response back to the requester.
                originTransaction.SendFinalResponse(response);
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.Error, "Exception processing in dialogue " + sipResponse.Header.CSeqMethod + " final response. " + excp.Message, owner));
            }
        }

        private void InDialogueTransactionRemoved(SIPTransaction sipTransaction)
        {
            try
            {
                if (m_inDialogueTransactions.ContainsKey(sipTransaction.TransactionId))
                {
                    lock (m_inDialogueTransactions)
                    {
                        m_inDialogueTransactions.Remove(sipTransaction.TransactionId);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception InDialogueTransactionStateChanged. " + excp);
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
            inviteRequest.Header.ContentType = "application/sdp";

            return inviteRequest;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// From RFC 3515 2.4.5:
        /// The body of a NOTIFY MUST begin with a SIP Response Status-Line...
        /// </remarks>
        /// <param name="referRequest"></param>
        /// <returns></returns>
        private SIPRequest GetNotifyRequest(SIPRequest referRequest, SIPDialogue referDialogue, SIPResponse referResponse, SIPEndPoint localEndPoint)
        {
            try
            {
                SIPRequest notifyRequest = new SIPRequest(SIPMethodsEnum.NOTIFY, referRequest.Header.Contact[0].ContactURI);
                notifyRequest.Header = new SIPHeader(SIPFromHeader.ParseFromHeader(referDialogue.LocalUserField.ToString()), SIPToHeader.ParseToHeader(referDialogue.RemoteUserField.ToString()), referDialogue.CSeq, referDialogue.CallId);
                notifyRequest.Header.Event = m_referNotifyEventValue;
                notifyRequest.Header.CSeqMethod = SIPMethodsEnum.NOTIFY;
                notifyRequest.Header.SubscriptionState = (referResponse.StatusCode >= 200 && referResponse.StatusCode <= 299) ? "terminated;reason=noresource" : "active;expires=32";
                notifyRequest.Header.ContentType = m_referNotifyContentType;

                SIPViaHeader viaHeader = new SIPViaHeader(localEndPoint, CallProperties.CreateBranchId());
                notifyRequest.Header.Vias.PushViaHeader(viaHeader);

                notifyRequest.Body = (referResponse.SIPVersion + " " + referResponse.StatusCode + " " + referResponse.ReasonPhrase).Trim();
                notifyRequest.Header.ContentLength = notifyRequest.Body.Length;

                return notifyRequest;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetNotifyRequest. " + excp.Message);
                throw;
            }
        }
    }
}
