//-----------------------------------------------------------------------------
// Filename: UASInviteTransaction.cs
//
// Description: SIP Transaction that implements UAS (User Agent Server) functionality for
// an INVITE transaction.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//  
// History:
// 21 Nov 2006	Aaron Clauson	Created, Dublin, Ireland.
// 30 Oct 2019  Aaron Clauson   Added support for reliable provisional responses as per RFC3262.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// The server transaction for an INVITE request. This transaction processes incoming calls RECEIVED by the application.
    /// </summary>
    public class UASInviteTransaction : SIPTransaction
    {
        private static string m_sipServerAgent = SIPConstants.SIP_SERVER_STRING;

        /// <summary>
        /// The local tag is set on the To SIP header and forms part of the information used to identify a SIP dialog.
        /// </summary>
        public string LocalTag { get; set; }

        public event SIPTransactionCancelledDelegate UASInviteTransactionCancelled;
        public event SIPTransactionRequestReceivedDelegate NewCallReceived;
        public event SIPTransactionTimedOutDelegate UASInviteTransactionTimedOut;

        internal UASInviteTransaction(
            SIPTransport sipTransport,
            SIPRequest sipRequest,
            SIPEndPoint dstEndPoint,
            SIPEndPoint localSIPEndPoint,
            SIPEndPoint outboundProxy,
            bool noCDR = false)
            : base(sipTransport, sipRequest, dstEndPoint, localSIPEndPoint, outboundProxy)
        {
            TransactionType = SIPTransactionTypesEnum.InviteServer;
            m_remoteTag = sipRequest.Header.From.FromTag;

            if (sipRequest.Header.To.ToTag == null)
            {
                // This UAS needs to set the To Tag.
                m_localTag = CallProperties.CreateNewTag();
            }
            else
            {
                // This is a re-INVITE.
                m_localTag = sipRequest.Header.To.ToTag;
            }

            //logger.LogDebug("New UASTransaction (" + TransactionId + ") for " + TransactionRequest.URI.ToString() + " to " + RemoteEndPoint + ".");
            SIPEndPoint localEP = SIPEndPoint.TryParse(sipRequest.Header.ProxyReceivedOn) ?? localSIPEndPoint;
            SIPEndPoint remoteEP = SIPEndPoint.TryParse(sipRequest.Header.ProxyReceivedFrom) ?? dstEndPoint;

            if (!noCDR)
            {
                CDR = new SIPCDR(SIPCallDirection.In, sipRequest.URI, sipRequest.Header.From, sipRequest.Header.CallId, localEP, remoteEP);
            }

            TransactionRequestReceived += UASInviteTransaction_TransactionRequestReceived;
            TransactionInformationResponseReceived += UASInviteTransaction_TransactionResponseReceived;
            TransactionFinalResponseReceived += UASInviteTransaction_TransactionResponseReceived;
            TransactionTimedOut += UASInviteTransaction_TransactionTimedOut;
            TransactionRemoved += UASInviteTransaction_TransactionRemoved;
        }

        private void UASInviteTransaction_TransactionRemoved(SIPTransaction transaction)
        {
            // Remove event handlers.
            UASInviteTransactionCancelled = null;
            NewCallReceived = null;
            UASInviteTransactionTimedOut = null;
            CDR = null;
        }

        private void UASInviteTransaction_TransactionTimedOut(SIPTransaction sipTransaction)
        {
            UASInviteTransactionTimedOut?.Invoke(this);
            CDR?.TimedOut();
        }

        private void UASInviteTransaction_TransactionResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            logger.LogWarning("UASInviteTransaction received unexpected response, " + sipResponse.ReasonPhrase + " from " + remoteEndPoint.ToString() + ", ignoring.");
        }

        private void UASInviteTransaction_TransactionRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            try
            {
                if (TransactionState == SIPTransactionStatesEnum.Terminated)
                {
                    logger.LogDebug("Request received by UASInviteTransaction for a terminated transaction, ignoring.");
                }
                else if (sipRequest.Method != SIPMethodsEnum.INVITE)
                {
                    logger.LogWarning("Unexpected " + sipRequest.Method + " passed to UASInviteTransaction.");
                }
                else
                {
                    if (TransactionState != SIPTransactionStatesEnum.Trying)
                    {
                        SIPResponse tryingResponse = GetInfoResponse(m_transactionRequest, SIPResponseStatusCodesEnum.Trying);
                        SendProvisionalResponse(tryingResponse);
                    }

                    // Notify new call subscribers.
                    if (NewCallReceived != null)
                    {
                        NewCallReceived(localSIPEndPoint, remoteEndPoint, this, sipRequest);
                    }
                    else
                    {
                        // Nobody wants to answer this call so return an error response.
                        SIPResponse declinedResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Decline, "Nothing listening");
                        SendFinalResponse(declinedResponse);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UASInviteTransaction GotRequest. " + excp.Message);
            }
        }

        public override void SendProvisionalResponse(SIPResponse sipResponse)
        {
            try
            {
                base.SendProvisionalResponse(sipResponse);
                CDR?.Progress(sipResponse.Status, sipResponse.ReasonPhrase, null, null);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UASInviteTransaction SendProvisionalResponse. " + excp.Message);
                throw;
            }
        }

        public override void SendFinalResponse(SIPResponse sipResponse)
        {
            try
            {
                base.SendFinalResponse(sipResponse);
                CDR?.Answered(sipResponse.StatusCode, sipResponse.Status, sipResponse.ReasonPhrase, null, null);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UASInviteTransaction SendFinalResponse. " + excp.Message);
                throw;
            }
        }

        public void CancelCall()
        {
            try
            {
                if (TransactionState == SIPTransactionStatesEnum.Calling || TransactionState == SIPTransactionStatesEnum.Trying || TransactionState == SIPTransactionStatesEnum.Proceeding)
                {
                    base.Cancel();

                    SIPResponse cancelResponse = SIPTransport.GetResponse(TransactionRequest, SIPResponseStatusCodesEnum.RequestTerminated, null);
                    SendFinalResponse(cancelResponse);

                    UASInviteTransactionCancelled?.Invoke(this);
                }
                else
                {
                    logger.LogWarning("A request was made to cancel transaction " + TransactionId + " that was not in the calling, trying or proceeding states, state=" + TransactionState + ".");
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UASInviteTransaction CancelCall. " + excp.Message);
                throw;
            }
        }

        public SIPResponse GetOkResponse(SIPRequest sipRequest, SIPEndPoint localSIPEndPoint, string contentType, string messageBody)
        {
            try
            {
                SIPResponse okResponse = new SIPResponse(SIPResponseStatusCodesEnum.Ok, null, sipRequest.LocalSIPEndPoint, sipRequest.RemoteSIPEndPoint);

                SIPHeader requestHeader = sipRequest.Header;
                // Let the transport layer set the contact at send time.
                SIPURI contactUri = new SIPURI(sipRequest.URI.Scheme, IPAddress.Any, 0);

                okResponse.Header = new SIPHeader(new SIPContactHeader(null, contactUri), requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);

                okResponse.Header.To.ToTag = m_localTag;
                okResponse.Header.CSeqMethod = requestHeader.CSeqMethod;
                okResponse.Header.Vias = requestHeader.Vias;
                okResponse.Header.Server = m_sipServerAgent;
                okResponse.Header.MaxForwards = Int32.MinValue;
                okResponse.Header.RecordRoutes = requestHeader.RecordRoutes;
                okResponse.Header.Supported = (PrackSupported == true) ? SIPExtensionHeaders.PRACK : null;

                okResponse.Body = messageBody;
                okResponse.Header.ContentType = contentType;
                okResponse.Header.ContentLength = (messageBody != null) ? messageBody.Length : 0;

                return okResponse;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GetOkResponse. " + excp.Message);
                throw excp;
            }
        }
    }
}
