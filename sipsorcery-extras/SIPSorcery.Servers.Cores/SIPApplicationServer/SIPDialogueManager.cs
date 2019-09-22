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
using System.Linq;
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
    public class SIPDialogueManager : ISIPDialogueManager
    {
        private static ILog logger = AppState.logger;

        private static string m_userAgentString = SIPConstants.SIP_USERAGENT_STRING;
        private static string m_remoteHangupCause = SIPConstants.SIP_REMOTEHANGUP_CAUSE;
        private static string m_referReplacesParameter = SIPHeaderAncillary.SIP_REFER_REPLACES;
        private static string m_referNotifyEventValue = SIPEventPackage.Refer.ToString();
        private static string m_referNotifyContentType = SIPMIMETypes.REFER_CONTENT_TYPE;
        private static readonly int m_defaultSIPPort = SIPConstants.DEFAULT_SIP_PORT;
        private static readonly string m_sdpContentType = SDP.SDP_MIME_CONTENTTYPE;

        private SIPMonitorLogDelegate Log_External;
        private SIPAuthenticateRequestDelegate SIPAuthenticateRequest_External;
        private SIPAssetGetDelegate<SIPAccountAsset> GetSIPAccountAsset_External;
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPAssetPersistor<SIPCDRAsset> m_sipCDRPersistor;

        private Dictionary<string, string> m_inDialogueTransactions = new Dictionary<string, string>();     // <Forwarded transaction id, Origin transaction id>.

        public event Action<SIPDialogue> OnCallHungup;

        public static IPAddress PublicIPAddress;

        public SIPDialogueManager(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPMonitorLogDelegate logDelegate,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPAssetPersistor<SIPCDRAsset> sipCDRPersistor,
            SIPAuthenticateRequestDelegate authenticateRequestDelegate,
            SIPAssetGetDelegate<SIPAccountAsset> getSIPAccountAsset,
            GetCanonicalDomainDelegate getCanonicalDomain)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            Log_External = logDelegate;
            m_sipDialoguePersistor = sipDialoguePersistor;
            m_sipCDRPersistor = sipCDRPersistor;
            SIPAuthenticateRequest_External = authenticateRequestDelegate;
            GetSIPAccountAsset_External = getSIPAccountAsset;
            GetCanonicalDomain_External = getCanonicalDomain;
        }

        public void ProcessInDialogueRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest, SIPDialogue dialogue)
        {
            try
            {
                if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPNonInviteTransaction byeTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                    //logger.Debug("Matching dialogue found for BYE request to " + sipRequest.URI.ToString() + ".");
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);

                    string hangupReason = sipRequest.Header.Reason;
                    if (hangupReason.IsNullOrBlank())
                    {
                        hangupReason = sipRequest.Header.GetUnknownHeaderValue("X-Asterisk-HangupCause");
                    }

                    if (sipRequest.Header.SwitchboardTerminate == "both")
                    {
                        // BYE request from switchboard that's requesting two dialogues to be hungup by the server.
                        CallHungup(dialogue, hangupReason, true);
                    }
                    else
                    {
                        // Normal BYE request.
                        CallHungup(dialogue, hangupReason, false);
                    }
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    UASInviteTransaction reInviteTransaction = m_sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                    SIPResponse tryingResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Trying, null);
                    reInviteTransaction.SendInformationalResponse(tryingResponse);
                    reInviteTransaction.CDR = null;     // Don't want CDR's on re-INVITEs.
                    ForwardInDialogueRequest(dialogue, reInviteTransaction, localSIPEndPoint, remoteEndPoint);
                }
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    // Send back the remote SDP.
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "OPTIONS request for established dialogue " + dialogue.DialogueName + ".", dialogue.Owner));
                    SIPNonInviteTransaction optionsTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    okResponse.Body = dialogue.RemoteSDP;
                    okResponse.Header.ContentLength = okResponse.Body.Length;
                    okResponse.Header.ContentType = m_sdpContentType;
                    optionsTransaction.SendFinalResponse(okResponse);
                }
                else if (sipRequest.Method == SIPMethodsEnum.MESSAGE)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "MESSAGE for call " + sipRequest.URI.ToString() + ": " + sipRequest.Body + ".", dialogue.Owner));
                    SIPNonInviteTransaction messageTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                    SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    messageTransaction.SendFinalResponse(okResponse);
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

        public void ProcessInDialogueReferRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest, SIPDialogue dialogue, Func<string, SIPURI, string, SIPDialogue, ISIPServerUserAgent> blindTransfer)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER received on dialogue " + dialogue.DialogueName + ", transfer mode is " + dialogue.TransferMode + ".", dialogue.Owner));

                SIPNonInviteTransaction referTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);

                if (sipRequest.Header.ReferTo.IsNullOrBlank())
                {
                    // A REFER request must have a Refer-To header.
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Bad REFER request, no Refer-To header.", dialogue.Owner));
                    SIPResponse invalidResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing mandatory Refer-To header");
                    referTransaction.SendFinalResponse(invalidResponse);
                }
                else
                {
                    if (dialogue.TransferMode == SIPDialogueTransferModesEnum.NotAllowed)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER rejected due to dialogue permissions.", dialogue.Owner));
                        SIPResponse declineTransferResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Decline, "Transfers are disabled on dialogue");
                        referTransaction.SendFinalResponse(declineTransferResponse);
                    }
                    else if (Regex.Match(sipRequest.Header.ReferTo, m_referReplacesParameter).Success)
                    {
                        // Attended transfers are allowed unless explicitly blocked. Attended transfers are not dangerous 
                        // as no new call is created and it's the same as a re-invite.
                        if (dialogue.TransferMode == SIPDialogueTransferModesEnum.PassThru)
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER received, attended transfer, passing through, Referred-By=" + sipRequest.Header.ReferredBy + ".", dialogue.Owner));
                            ForwardInDialogueRequest(dialogue, referTransaction, localSIPEndPoint, remoteEndPoint);
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER received, attended transfer, processing on app server, Referred-By=" + sipRequest.Header.ReferredBy + ".", dialogue.Owner));
                            ProcessAttendedRefer(dialogue, referTransaction, sipRequest, localSIPEndPoint, remoteEndPoint);
                        }
                    }
                    else
                    {
                        bool referAuthenticated = false;
                        if (dialogue.TransferMode == SIPDialogueTransferModesEnum.Default)
                        {
                            string canonicalDomain = GetCanonicalDomain_External(sipRequest.Header.From.FromURI.Host, false);
                            if (!canonicalDomain.IsNullOrBlank())
                            {
                                referAuthenticated = AuthenticateReferRequest(referTransaction, sipRequest.Header.From.FromURI.User, canonicalDomain);
                            }
                        }

                        if (dialogue.TransferMode == SIPDialogueTransferModesEnum.BlindPlaceCall || referAuthenticated)
                        {
                            // A blind transfer that is permitted to initiate a new call.
                            //logger.Debug("Blind Transfer starting.");
                            SIPResponse acceptedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Accepted, null);
                            referTransaction.SendFinalResponse(acceptedResponse);
                            SendNotifyRequestForRefer(sipRequest, dialogue, localSIPEndPoint, SIPResponseStatusCodesEnum.Trying, null);
                            //SIPDialogue oppositeDialogue = GetOppositeDialogue(dialogue);
                            SIPUserField replacesUserField = SIPUserField.ParseSIPUserField(sipRequest.Header.ReferTo);
                            ISIPServerUserAgent transferUAS = blindTransfer(dialogue.Owner, replacesUserField.URI, "transfer", dialogue);
                            bool sendNotifications = true;
                            Guid originalBridgeID = dialogue.BridgeId;
                            transferUAS.UASStateChanged += (uas, status, reason) =>
                            {
                                if (sendNotifications)
                                {
                                    if (status != SIPResponseStatusCodesEnum.Trying)
                                    {
                                        // As soon as a blind transfer receives a non-100 response break the bridge as most UA's will immediately hangup the call once
                                        // they are informed it's proceeding.
                                        if (dialogue.BridgeId != Guid.Empty)
                                        {
                                            dialogue.BridgeId = Guid.Empty;
                                            m_sipDialoguePersistor.UpdateProperty(dialogue.Id, "BridgeId", dialogue.BridgeId.ToString());
                                        }

                                        // Retrieve the dialogue anew each time a new response is received in order to check if it still exists.
                                        SIPDialogueAsset updatedDialogue = m_sipDialoguePersistor.Get(dialogue.Id);
                                        if (updatedDialogue != null)
                                        {
                                            SendNotifyRequestForRefer(sipRequest, updatedDialogue.SIPDialogue, localSIPEndPoint, status, reason);
                                        }
                                        else
                                        {
                                            // The dialogue the blind transfer notifications were being sent on has been hungup no point sending any more notifications.
                                            sendNotifications = false;
                                        }
                                    }
                                }

                                if ((int)status >= 400)
                                {
                                    // The transfer has failed. Attempt to re-bridge if possible or if not hangup the orphaned end of the call.
                                    SIPDialogueAsset referDialogue = m_sipDialoguePersistor.Get(dialogue.Id);   // Dialogue that initiated the REFER.
                                    if (referDialogue != null)
                                    {
                                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Blind transfer to " + replacesUserField.URI.ToParameterlessString() + " failed with " + status + ", the initiating dialogue is still available re-creating the original bridge.", dialogue.Owner));
                                        // Re-bridging the two original dialogues.
                                        m_sipDialoguePersistor.UpdateProperty(dialogue.Id, "BridgeId", originalBridgeID.ToString());
                                    }
                                    else
                                    {
                                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Blind transfer to " + replacesUserField.URI.ToParameterlessString() + " failed with " + status + ", the initiating dialogue hungup, hanging up remaining dialogue.", dialogue.Owner));
                                        // The transfer failed and the dialogue that initiated the transfer has hungup. No point keeping the other end up so hang it up as well.
                                        string bridgeIDStr = originalBridgeID.ToString();
                                        SIPDialogueAsset orphanedDialogueAsset = m_sipDialoguePersistor.Get(d => d.BridgeId == bridgeIDStr);
                                        if (orphanedDialogueAsset != null)
                                        {
                                            HangupDialogue(orphanedDialogueAsset.SIPDialogue, "Blind transfer failed and remote end already hungup.", true);
                                        }
                                    }
                                }
                            };
                            //logger.Debug("Blind Transfer successfully initated, dial plan processing now in progress.");
                        }
                        else
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER received, blind transfer, Refer-To=" + sipRequest.Header.ReferTo + ", Referred-By=" + sipRequest.Header.ReferredBy + ".", dialogue.Owner));
                            //SIPNonInviteTransaction passThruTransaction = m_sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, m_outboundProxy);
                            ForwardInDialogueRequest(dialogue, referTransaction, localSIPEndPoint, remoteEndPoint);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProcessInDialogueReferRequest. " + excp.Message);
                throw;
            }
        }

        private bool AuthenticateReferRequest(SIPNonInviteTransaction referTransaction, string sipUsername, string sipDomain)
        {
            SIPRequest referRequest = referTransaction.TransactionRequest;

            try
            {
                if (SIPAuthenticateRequest_External == null)
                {
                    // No point trying to authenticate if we haven't been given an authentication delegate.
                    logger.Warn("Missing SIP request authentication delegate in SIPDialogueManager AuthenticateReferRequest.");
                    SIPResponse errorResponse = SIPTransport.GetResponse(referRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                    referTransaction.SendFinalResponse(errorResponse);
                }
                else if (GetSIPAccountAsset_External == null)
                {
                    // No point trying to authenticate if we haven't been given a  delegate to load the SIP account.
                    logger.Warn("Missing get SIP account delegate in SIPDialogueManager AuthenticateReferRequest.");
                    SIPResponse errorResponse = SIPTransport.GetResponse(referRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                    referTransaction.SendFinalResponse(errorResponse);
                }
                else
                {
                    SIPAccountAsset sipAccountAsset = GetSIPAccountAsset_External(s => s.SIPUsername == sipUsername && s.SIPDomain == sipDomain);

                    if (sipAccountAsset == null)
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Rejecting authentication required call for " + sipUsername + "@" + sipDomain + ", SIP account not found.", null));
                        SIPResponse errorResponse = SIPTransport.GetResponse(referRequest, SIPResponseStatusCodesEnum.Forbidden, null);
                        referTransaction.SendFinalResponse(errorResponse);
                    }
                    else
                    {
                        SIPEndPoint localSIPEndPoint = (!referRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(referRequest.Header.ProxyReceivedOn) : referRequest.LocalSIPEndPoint;
                        SIPEndPoint remoteEndPoint = (!referRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(referRequest.Header.ProxyReceivedFrom) : referRequest.RemoteSIPEndPoint;

                        SIPRequestAuthenticationResult authenticationResult = SIPAuthenticateRequest_External(localSIPEndPoint, remoteEndPoint, referRequest, sipAccountAsset.SIPAccount, Log_External);
                        if (authenticationResult.Authenticated)
                        {
                            if (authenticationResult.WasAuthenticatedByIP)
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER from " + remoteEndPoint.ToString() + " successfully authenticated by IP address.", sipAccountAsset.SIPAccount.Owner));
                            }
                            else
                            {
                                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER from " + remoteEndPoint.ToString() + " successfully authenticated by digest.", sipAccountAsset.SIPAccount.Owner));
                            }

                            return true;
                        }
                        else
                        {
                            // Send authorisation failure or required response
                            SIPResponse authReqdResponse = SIPTransport.GetResponse(referRequest, authenticationResult.ErrorResponse, null);
                            authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "REFER not authenticated for " + sipUsername + "@" + sipDomain + ", responding with " + authenticationResult.ErrorResponse + ".", null));
                            referTransaction.SendFinalResponse(authReqdResponse);

                            return false;
                        }
                    }
                }

                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIDialogueManager AuthenticateReferRequest. " + excp.Message);
                SIPResponse errorResponse = SIPTransport.GetResponse(referRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                referTransaction.SendFinalResponse(errorResponse);

                return false;
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
            Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueCreated, clientDiaglogue.Owner, clientDiaglogue.Id.ToString(), clientDiaglogue.LocalUserField.URI));

            SIPEndPoint forwardedDialogueRemoteEP = (IPSocket.IsIPSocket(forwardedDialogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(forwardedDialogue.RemoteTarget.Host) : null;
            Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueCreated, forwardedDialogue.Owner, forwardedDialogue.Id.ToString(), forwardedDialogue.LocalUserField.URI));
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
            try
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

                            if (OnCallHungup != null)
                            {
                                OnCallHungup(orphanedDialogue);
                            }
                        }
                    }
                    else
                    {
                        logger.Warn("No bridge could be found for hungup call.");
                    }

                    if (OnCallHungup != null)
                    {
                        OnCallHungup(sipDialogue);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception CallManager CallHungup. " + excp.Message);
            }
        }

        private void HangupDialogue(SIPDialogue dialogue, string hangupCause, bool sendBye)
        {
            try
            {
                //logger.Debug("Hanging up orphaned dialogue " + dialogue.DialogueName + ".");

                if (dialogue.CDRId != Guid.Empty)
                {
                    SIPCDRAsset cdr = m_sipCDRPersistor.Get(dialogue.CDRId);
                    if (cdr != null)
                    {
                        cdr.BridgeId = dialogue.BridgeId.ToString();
                        cdr.Hungup(hangupCause);
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

                if (sendBye)
                {
                    dialogue.Hangup(m_sipTransport, m_outboundProxy);

                    if (OnCallHungup != null)
                    {
                        OnCallHungup(dialogue);
                    }
                }

                m_sipDialoguePersistor.Delete(new SIPDialogueAsset(dialogue));

                SIPEndPoint orphanedDialogueRemoteEP = (IPSocket.IsIPSocket(dialogue.RemoteTarget.Host)) ? SIPEndPoint.ParseSIPEndPoint(dialogue.RemoteTarget.Host) : null;
                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved, dialogue.Owner, dialogue.Id.ToString(), dialogue.LocalUserField.URI));
            }
            catch (Exception excp)
            {
                logger.Error("Exception HangupDialogue. " + excp.Message);
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

                SIPReplacesParameter replacesParam = SIPReplacesParameter.Parse(replaces);

                if (replacesParam != null)
                {
                    SIPDialogue replacesDialogue = GetDialogue(replacesParam.CallID, replacesParam.ToTag, replacesParam.FromTag);

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
                            //if (dialogueAsset.LocalUserField.Contains(callIdentifier))
                            if (dialogueAsset.RemoteUserField.Contains(callIdentifier))
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
                    string publicIPAddress = null;

                    if (PublicIPAddress != null)
                    {
                        publicIPAddress = PublicIPAddress.ToString();
                    }
                    else if (substituteDialogue.RemoteTarget != null)
                    {
                        publicIPAddress = IPSocket.ParseHostFromSocket(substituteDialogue.RemoteTarget.Host);
                    }

                    if (publicIPAddress != null)
                    {
                        replacementSDP = SIPPacketMangler.MangleSDP(replacementSDP, publicIPAddress, out wasMangled);
                    }
                }

                if (wasMangled)
                {
                    logger.Debug("The SDP being used in a re-INVITE was mangled to " + SDP.GetSDPRTPEndPoint(replacementSDP) + ".");
                }

                // Check whether there is a need to send the re-invite by comparing the new SDP being sent with what has already been sent.
                //if (dialogue.SDP == replacementSDP)
                //{
                //    logger.Debug("A reinvite was not sent to " + dialogue.RemoteTarget.ToString() + " as the SDP has not changed.");
                //}
                //else
                //{
                // Resend even if SDP has not changed as an attempt to refresh a call having one-way audio issues.
                    logger.Debug("Reinvite SDP being sent to " + dialogue.RemoteTarget.ToString() + ":\r\n" + replacementSDP);

                    dialogue.CSeq = dialogue.CSeq + 1;
                    m_sipDialoguePersistor.UpdateProperty(dialogue.Id, "CSeq", dialogue.CSeq);
                    SIPEndPoint localSIPEndPoint = (m_outboundProxy != null) ? m_sipTransport.GetDefaultTransportContact(m_outboundProxy.Protocol) : m_sipTransport.GetDefaultTransportContact(SIPProtocolsEnum.udp);
                    SIPRequest reInviteReq = GetInviteRequest(dialogue, localSIPEndPoint, replacementSDP);

                    SIPEndPoint reinviteEndPoint = null;

                    // If the outbound proxy is a loopback address, as it will normally be for local deployments, then it cannot be overriden.
                    if (m_outboundProxy != null && IPAddress.IsLoopback(m_outboundProxy.Address))
                    {
                        reInviteReq.Header.ProxySendFrom = dialogue.ProxySendFrom;
                        reinviteEndPoint = m_outboundProxy;
                    }
                    if (!dialogue.ProxySendFrom.IsNullOrBlank())
                    {
                        reInviteReq.Header.ProxySendFrom = dialogue.ProxySendFrom;
                        // The proxy will always be listening on UDP port 5060 for requests from internal servers.
                        reinviteEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(SIPEndPoint.ParseSIPEndPoint(dialogue.ProxySendFrom).Address, m_defaultSIPPort));
                    }
                    else
                    {
                        SIPDNSLookupResult lookupResult = m_sipTransport.GetRequestEndPoint(reInviteReq, m_outboundProxy, false);
                        if (lookupResult.LookupError != null)
                        {
                            logger.Warn("ReInvite Failed to resolve " + lookupResult.URI.Host + ".");
                        }
                        else
                        {
                            reinviteEndPoint = lookupResult.GetSIPEndPoint();
                        }
                    }

                    if (reinviteEndPoint != null)
                    {
                        UACInviteTransaction reInviteTransaction = m_sipTransport.CreateUACTransaction(reInviteReq, reinviteEndPoint, localSIPEndPoint, reinviteEndPoint);
                        reInviteTransaction.CDR = null; // Don't want CDRs on re-invites.
                        reInviteTransaction.UACInviteTransactionFinalResponseReceived += ReInviteTransactionFinalResponseReceived;
                        reInviteTransaction.SendInviteRequest(reinviteEndPoint, reInviteReq);
                    }
                    else
                    {
                        throw new ApplicationException("Could not forward re-invite as request end point could not be determined.\r\n" + reInviteReq.ToString());
                    }
                //}
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

                SIPEndPoint forwardSIPEndPoint = m_outboundProxy ?? m_sipTransport.GetDefaultSIPEndPoint(new SIPEndPoint(bridgedDialogue.RemoteTarget));
                IPAddress remoteUAIPAddress = (inDialogueTransaction.TransactionRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? remoteEndPoint.Address : SIPEndPoint.ParseSIPEndPoint(inDialogueTransaction.TransactionRequest.Header.ProxyReceivedFrom).Address;

                SIPRequest forwardedRequest = inDialogueTransaction.TransactionRequest.Copy();

                // Need to remove or reset headers from the copied request that conflict with the existing dialogue requests.
                forwardedRequest.Header.RecordRoutes = null;
                forwardedRequest.Header.MaxForwards = SIPConstants.DEFAULT_MAX_FORWARDS;

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

                if (inDialogueTransaction.TransactionRequest.Body != null && inDialogueTransaction.TransactionRequest.Method == SIPMethodsEnum.INVITE)
                {
                    bool wasMangled = false;
                    forwardedRequest.Body = SIPPacketMangler.MangleSDP(inDialogueTransaction.TransactionRequest.Body, remoteUAIPAddress.ToString(), out wasMangled);
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Re-INVITE wasmangled=" + wasMangled + " remote=" + remoteUAIPAddress.ToString() + ".", null));
                    forwardedRequest.Header.ContentLength = forwardedRequest.Body.Length;
                }

                SIPEndPoint forwardEndPoint = null;
                if (!bridgedDialogue.ProxySendFrom.IsNullOrBlank())
                {
                    forwardedRequest.Header.ProxySendFrom = bridgedDialogue.ProxySendFrom;
                    // The proxy will always be listening on UDP port 5060 for requests from internal servers.
                    forwardEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(SIPEndPoint.ParseSIPEndPoint(bridgedDialogue.ProxySendFrom).Address, m_defaultSIPPort));
                }
                else
                {
                    SIPDNSLookupResult lookupResult = m_sipTransport.GetRequestEndPoint(forwardedRequest, m_outboundProxy, false);
                    if (lookupResult.LookupError != null)
                    {
                        logger.Warn("ForwardInDialogueRequest Failed to resolve " + lookupResult.URI.Host + ".");
                    }
                    else
                    {
                        forwardEndPoint = lookupResult.GetSIPEndPoint();
                    }
                }

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
        private void ProcessAttendedRefer(SIPDialogue dialogue, SIPNonInviteTransaction referTransaction, SIPRequest referRequest, SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Initiating attended transfer.", dialogue.Owner));
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
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Could not locate the dialogue for the Replaces parameter on an attended transfer.", dialogue.Owner));
                        SIPResponse errorResponse = SIPTransport.GetResponse(referRequest, SIPResponseStatusCodesEnum.BadRequest, "Could not locate replaced dialogue");
                        referTransaction.SendFinalResponse(errorResponse);
                    }
                    else
                    {
                        logger.Debug("REFER dialogue being replaced " + replacesDialogue.DialogueName + ".");

                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Replacement dialogue found on Refer, accepting.", dialogue.Owner));

                        bool sendNotifications = true;
                        if (!referRequest.Header.ReferSub.IsNullOrBlank())
                        {
                            Boolean.TryParse(referRequest.Header.ReferSub, out sendNotifications);
                        }

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

                        Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated, remainingDialogue.Owner, remainingDialogue.Id.ToString(), remainingDialogue.LocalUserField.URI));
                        Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated, remaining2Dialogue.Owner, remaining2Dialogue.Id.ToString(), remaining2Dialogue.LocalUserField.URI));
                        Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueTransfer, remainingDialogue.Owner, remainingDialogue.Id.ToString(), remainingDialogue.LocalUserField.URI));

                        SIPResponse acceptedResponse = SIPTransport.GetResponse(referRequest, SIPResponseStatusCodesEnum.Accepted, null);
                        referTransaction.SendFinalResponse(acceptedResponse);

                        if (sendNotifications)
                        {
                            SendNotifyRequestForRefer(referRequest, dialogue, localEndPoint, SIPResponseStatusCodesEnum.Trying, null);
                        }

                        logger.Debug("Reinviting " + remainingDialogue.DialogueName + " with " + remaining2Dialogue.DialogueName + ".");

                        ReInvite(remainingDialogue, remaining2Dialogue);
                        ReInvite(remaining2Dialogue, remainingDialogue);

                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Transfer dialogue re-invites complete.", dialogue.Owner));

                        if (sendNotifications)
                        {
                            SendNotifyRequestForRefer(referRequest, dialogue, localEndPoint, SIPResponseStatusCodesEnum.Ok, null);
                        }

                        // Hangup redundant dialogues.
                        logger.Debug("Hanging up redundant dialogues post transfer.");
                        logger.Debug("Hanging up " + dialogue.DialogueName + ".");
                        dialogue.Hangup(m_sipTransport, m_outboundProxy);
                        CallHungup(dialogue, "Attended transfer", false);
                        logger.Debug("Hanging up " + replacesDialogue.DialogueName + ".");
                        replacesDialogue.Hangup(m_sipTransport, m_outboundProxy);
                        CallHungup(replacesDialogue, "Attended transfer", false);
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
                //logger.Debug("SIPDialogueManager DialogueTransfer.");

                // Create bridge between answered dialogue and other end of dialogue being replaced.
                Guid newBridgeId = Guid.NewGuid();
                orphanedDialogue.BridgeId = newBridgeId;
                answeredDialogue.BridgeId = newBridgeId;
                m_sipDialoguePersistor.Update(new SIPDialogueAsset(orphanedDialogue));
                m_sipDialoguePersistor.Add(new SIPDialogueAsset(answeredDialogue));

                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueCreated, answeredDialogue.Owner, answeredDialogue.Id.ToString(), answeredDialogue.LocalUserField.URI));
                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated, orphanedDialogue.Owner, orphanedDialogue.Id.ToString(), orphanedDialogue.LocalUserField.URI));

                //logger.Debug("Hanging up dead dialogue");
                // Hangup dialogue being replaced.
                // Check if the dead dialogue has already been hungup. For blind transfers the remote end will usually hangup once it gets a NOTIFY request 
                // indicating the transfer is in progress.
                SIPDialogueAsset deadDialogueAsset = m_sipDialoguePersistor.Get(deadDialogue.Id);
                if (deadDialogueAsset != null)
                {
                    deadDialogueAsset.SIPDialogue.Hangup(m_sipTransport, m_outboundProxy);
                    CallHungup(deadDialogue, "Blind transfer", false);
                }
                //logger.Debug("Reinviting two remaining dialogues");
                // Reinvite  other end of dialogue being replaced to answered dialogue.
                ReInvite(orphanedDialogue, answeredDialogue);
                //ReInvite(answeredDialogue, orphanedDialogue.SDP);
            }
            catch (Exception excp)
            {
                logger.Error("Exception DialogueTransfer. " + excp.Message);
            }
        }

        /// <summary>
        /// An attended transfer between two separate established calls where one leg of each call is being transferred 
        /// to the other.
        /// </summary>
        /// <param name="callID1">The Call-ID of the first call leg that is no longer required and of which the opposite end will be transferred.</param>
        /// <param name="callID2">The Call-ID of the second call leg that is no longer required and of which the opposite end will be transferred.</param>
        public void DualTransfer(string username, string callID1, string callID2)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dual transfer request was received for Call-ID's " + callID1 + " and " + callID2 + ".", username));

                if (username.IsNullOrBlank())
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dual transfer was called with an empty username, the transfer was not initiated.", username));
                    throw new ApplicationException("Dual transfer was called with an empty username, the transfer was not initiated.");
                }
                else if (callID1.IsNullOrBlank())
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dual transfer was called with an empty value for CallID1, the transfer was not initiated.", username));
                    throw new ApplicationException("Dual transfer was called with an empty value for CallID1, the transfer was not initiated.");
                }
                else if (callID2.IsNullOrBlank())
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dual transfer was called with an empty value for CallID2, the transfer was not initiated.", username));
                    throw new ApplicationException("Dual transfer was called with an empty value for CallID2, the transfer was not initiated.");
                }

                SIPDialogue dialogue1 = GetDialogueRelaxed(username, callID1);
                if (dialogue1 == null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dual transfer could not locate the dialogue for Call-ID " + callID1 + ".", username));
                    throw new ApplicationException("A dual transfer could not be processed as no dialogue could be found for Call-ID " + callID1 + ".");
                }
                else if (dialogue1.Owner.ToLower() != username.ToLower())
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dual transfer could not be processed as dialogue 1 did not match the username provided.", username));
                    throw new ApplicationException("A dual transfer could not be processed as a dialogue did not match the username provided.");
                }

                SIPDialogue dialogue2 = GetDialogueRelaxed(username, callID2);
                if (dialogue2 == null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dual transfer could not locate the dialogue for Call-ID " + callID2 + ".", username));
                    throw new ApplicationException("A dual transfer could not be processed as no dialogue could be found for Call-ID " + callID2 + ".");
                }
                else if (dialogue2.Owner.ToLower() != username.ToLower())
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dual transfer could not be processed as dialogue 2 did not match the username provided.", username));
                    throw new ApplicationException("A dual transfer could not be processed as a dialogue did not match the username provided.");
                }
                else if (dialogue1 == dialogue2)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dual transfer could not be processed as a the callid's resolved to the same dialogue.", username));
                    throw new ApplicationException("A dual transfer could not be processed as a the callid's resolved to the same dialogue.");
                }

                SIPDialogue oppositeDialogue1 = GetOppositeDialogue(dialogue1);
                if (oppositeDialogue1 == null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dual transfer could not locate the opposing dialogue for Call-ID " + callID1 + ".", username));
                    throw new ApplicationException("A dual transfer could not be processed as the opposing dialogue could be found for Call-ID " + callID1 + ".");
                }

                SIPDialogue oppositeDialogue2 = GetOppositeDialogue(dialogue2);
                if (oppositeDialogue2 == null)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "A dual transfer could not locate the opposing dialogue for Call-ID " + callID2 + ".", username));
                    throw new ApplicationException("A dual transfer could not be processed as the opposing dialogue could be found for Call-ID " + callID2 + ".");
                }

                Guid newBridgeId = Guid.NewGuid();
                oppositeDialogue1.BridgeId = newBridgeId;
                oppositeDialogue2.BridgeId = newBridgeId;
                m_sipDialoguePersistor.UpdateProperty(oppositeDialogue1.Id, "BridgeID", newBridgeId.ToString());
                m_sipDialoguePersistor.UpdateProperty(oppositeDialogue2.Id, "BridgeID", newBridgeId.ToString());

                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dual transfer re-inviting dialogues " + oppositeDialogue1.DialogueName + " and " + oppositeDialogue2.DialogueName + ".", username));

                ReInvite(oppositeDialogue1, oppositeDialogue2);

                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated, oppositeDialogue1.Owner, oppositeDialogue1.Id.ToString(), oppositeDialogue1.LocalUserField.URI));
                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueUpdated, oppositeDialogue2.Owner, oppositeDialogue2.Id.ToString(), oppositeDialogue2.LocalUserField.URI));

                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dual transfer hanging up dialogue " + dialogue1.DialogueName + " Call-ID " + dialogue1.CallId + ".", username));
                dialogue1.Hangup(m_sipTransport, m_outboundProxy);
                CallHungup(dialogue1, "Attended transfer", false);

                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dual transfer hanging up dialogue " + dialogue2.DialogueName + " Call-ID " + dialogue2.CallId + ".", username));
                dialogue2.Hangup(m_sipTransport, m_outboundProxy);
                CallHungup(dialogue2, "Attended transfer", false);

                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved, dialogue1.Owner, dialogue1.Id.ToString(), dialogue1.LocalUserField.URI));
                Log_External(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved, dialogue2.Owner, dialogue2.Id.ToString(), dialogue2.LocalUserField.URI));

                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Dual transfer for Call-ID's " + callID1 + " and " + callID2 + " was successfully completed.", username));
            }
            catch (Exception excp)
            {
                logger.Error("Exception DualTransfer. " + excp.Message);
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.AppServer, SIPMonitorEventTypesEnum.DialPlan, "Error processing a dual transfer. " + excp.Message, username));
                throw;
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
                IPAddress remoteUAIPAddress = (sipResponse.Header.ProxyReceivedFrom.IsNullOrBlank()) ? remoteEndPoint.Address : SIPEndPoint.ParseSIPEndPoint(sipResponse.Header.ProxyReceivedFrom).Address;
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

        private void SendNotifyRequestForRefer(SIPRequest referRequest, SIPDialogue referDialogue, SIPEndPoint localEndPoint, SIPResponseStatusCodesEnum responseCode, string responseReason)
        {
            try
            {
                //logger.Debug("Sending NOTIFY for refer subscription to " + referDialogue.RemoteTarget.ToParameterlessString() + ", status " + responseCode + " " + responseReason + ".");

                referDialogue.CSeq++;
                m_sipDialoguePersistor.UpdateProperty(referDialogue.Id, "CSeq", referDialogue.CSeq);

                SIPRequest notifyTryingRequest = GetNotifyRequest(referRequest, referDialogue, new SIPResponse(responseCode, responseReason, null), localEndPoint);
                SIPEndPoint forwardEndPoint = null;
                SIPDNSLookupResult lookupResult = m_sipTransport.GetRequestEndPoint(notifyTryingRequest, m_outboundProxy, false);
                if (lookupResult.LookupError != null)
                {
                    logger.Warn("SendNotifyRequestForRefer Failed to resolve " + lookupResult.URI.Host + ".");
                }
                else
                {
                    forwardEndPoint = lookupResult.GetSIPEndPoint();
                }

                SIPNonInviteTransaction notifyTryingTransaction = m_sipTransport.CreateNonInviteTransaction(notifyTryingRequest, forwardEndPoint, localEndPoint, m_outboundProxy);
                notifyTryingTransaction.SendReliableRequest();
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendNotifyRequestForRefer. " + excp.Message);
            }
        }

        /// <summary>
        /// Constructs a NOTIFY request to send within the implicit subscription created when processing a REFER request.
        /// </summary>
        /// <remarks>
        /// From RFC 3515 2.4.5:
        /// The body of a NOTIFY MUST begin with a SIP Response Status-Line...
        /// </remarks>
        /// <param name="referRequest">The REFER request that created the implicit refer event subscription.</param>
        /// <param name="referDialogue">The dialogue that the REFER request has been received within.</param>
        /// <param name="referResponse">The response that has been received to whatever is doing the post-REFER processing.</param>
        /// <param name="localEndPoint">The local SIP end point that the NOTIFY request will be sent from.</param>
        /// <returns>A NOTIFY request suitable for sending to the remote end of the REFER initiating dialogue.</returns>
        private SIPRequest GetNotifyRequest(SIPRequest referRequest, SIPDialogue referDialogue, SIPResponse referResponse, SIPEndPoint localEndPoint)
        {
            try
            {
                SIPRequest notifyRequest = new SIPRequest(SIPMethodsEnum.NOTIFY, referRequest.Header.Contact[0].ContactURI);
                notifyRequest.Header = new SIPHeader(SIPFromHeader.ParseFromHeader(referDialogue.LocalUserField.ToString()), SIPToHeader.ParseToHeader(referDialogue.RemoteUserField.ToString()), referDialogue.CSeq, referDialogue.CallId);
                notifyRequest.Header.Event = m_referNotifyEventValue; // + ";id=" + referRequest.Header.CSeq;
                notifyRequest.Header.CSeqMethod = SIPMethodsEnum.NOTIFY;
                notifyRequest.Header.SubscriptionState = (referResponse.StatusCode >= 200) ? "terminated;reason=noresource" : "active;expires=60";
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
