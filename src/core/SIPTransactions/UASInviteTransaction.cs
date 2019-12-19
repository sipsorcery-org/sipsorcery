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
using System.Net.Sockets;
using System.Threading.Tasks;
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

        public UASInviteTransaction(
            SIPTransport sipTransport,
            SIPRequest sipRequest,
            SIPEndPoint outboundProxy,
            bool noCDR = false)
            : base(sipTransport, sipRequest, outboundProxy)
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
            SIPEndPoint localEP = SIPEndPoint.TryParse(sipRequest.Header.ProxyReceivedOn) ?? sipRequest.LocalSIPEndPoint;
            SIPEndPoint remoteEP = SIPEndPoint.TryParse(sipRequest.Header.ProxyReceivedFrom) ?? sipRequest.RemoteSIPEndPoint;

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

        private async void UASInviteTransaction_TransactionRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
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
                        await SendProvisionalResponseAsync(tryingResponse);
                    }

                    // Notify new call subscribers.
                    if (NewCallReceived != null)
                    {
                        NewCallReceived(localSIPEndPoint, remoteEndPoint, this, sipRequest);
                    }
                    else
                    {
                        // Nobody wants to answer this call so return an error response.
                        SIPResponse declinedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Decline, "Nothing listening");
                        await SendFinalResponseAsync(declinedResponse);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UASInviteTransaction GotRequest. " + excp.Message);
            }
        }

        public override Task<SocketError> SendProvisionalResponseAsync(SIPResponse sipResponse)
        {
            try
            {
                CDR?.Progress(sipResponse.Status, sipResponse.ReasonPhrase, null, null);
                return base.SendProvisionalResponseAsync(sipResponse);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UASInviteTransaction SendProvisionalResponse. " + excp.Message);
                throw;
            }
        }

        public override Task<SocketError> SendFinalResponseAsync(SIPResponse sipResponse)
        {
            try
            {
                CDR?.Answered(sipResponse.StatusCode, sipResponse.Status, sipResponse.ReasonPhrase, null, null);
                return base.SendFinalResponseAsync(sipResponse);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UASInviteTransaction SendFinalResponse. " + excp.Message);
                throw;
            }
        }

        public Task<SocketError> CancelCall()
        {
            try
            {
                if (TransactionState == SIPTransactionStatesEnum.Calling || TransactionState == SIPTransactionStatesEnum.Trying || TransactionState == SIPTransactionStatesEnum.Proceeding)
                {
                    base.Cancel();

                    SIPResponse cancelResponse = SIPResponse.GetResponse(TransactionRequest, SIPResponseStatusCodesEnum.RequestTerminated, null);
                    var task = SendFinalResponseAsync(cancelResponse);

                    UASInviteTransactionCancelled?.Invoke(this);

                    return task;
                }
                else
                {
                    logger.LogWarning("A request was made to cancel transaction " + TransactionId + " that was not in the calling, trying or proceeding states, state=" + TransactionState + ".");
                    return Task.FromResult(SocketError.Success);
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UASInviteTransaction CancelCall. " + excp.Message);
                throw;
            }
        }

        public SIPResponse GetOkResponse(string contentType, string messageBody)
        {
            try
            {
                SIPResponse okResponse = new SIPResponse(SIPResponseStatusCodesEnum.Ok, null);
                okResponse.SetSendFromHints(TransactionRequest.LocalSIPEndPoint);

                SIPHeader requestHeader = TransactionRequest.Header;
                okResponse.Header = new SIPHeader(SIPContactHeader.GetDefaultSIPContactHeader(), requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);
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
