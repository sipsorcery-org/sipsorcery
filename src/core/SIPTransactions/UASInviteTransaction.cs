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
        /// <summary>
        /// The local tag is set on the To SIP header and forms part of the information used to identify a SIP dialog.
        /// </summary>
        public string LocalTag
        {
            get { return m_localTag; }
            set { m_localTag = value; }
        }

        public event SIPTransactionCancelledDelegate UASInviteTransactionCancelled;
        public event SIPTransactionFailedDelegate UASInviteTransactionFailed;

        /// <summary>
        /// An application will be interested in getting a notification about the ACK request if it
        /// is being used to carry the SDP answer. This occurs if the original INVITE did not contain an
        /// SDP offer.
        /// </summary>
        public event SIPTransactionRequestReceivedDelegate OnAckReceived;

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

            TransactionInformationResponseReceived += UASInviteTransaction_TransactionResponseReceived;
            TransactionFinalResponseReceived += UASInviteTransaction_TransactionResponseReceived;
            TransactionFailed += UASInviteTransaction_TransactionFailed;
            OnAckRequestReceived += UASInviteTransaction_OnAckRequestReceived;

            // UAS transactions need to be added to the engine immediately so that CANCEL
            // requests can be matched. Outbound transaction types don't need to be added to the 
            // engine until they need to be sent.
            sipTransport.AddTransaction(this);
        }

        private Task<SocketError> UASInviteTransaction_OnAckRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            return OnAckReceived?.Invoke(localSIPEndPoint, remoteEndPoint, this, sipRequest);
        }

        private void UASInviteTransaction_TransactionFailed(SIPTransaction sipTransaction, SocketError failureReason)
        {
            UASInviteTransactionFailed?.Invoke(this, failureReason);
            CDR?.TimedOut();
        }

        private Task<SocketError> UASInviteTransaction_TransactionResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            logger.LogWarning("UASInviteTransaction received unexpected response, " + sipResponse.ReasonPhrase + " from " + remoteEndPoint.ToString() + ", ignoring.");
            return Task.FromResult(SocketError.Fault);
        }

        public new Task<SocketError> SendProvisionalResponse(SIPResponse sipResponse)
        {
            CDR?.Progress(sipResponse.Status, sipResponse.ReasonPhrase, null, null);
            return base.SendProvisionalResponse(sipResponse);
        }

        public new void SendFinalResponse(SIPResponse sipResponse)
        {
            CDR?.Answered(sipResponse.StatusCode, sipResponse.Status, sipResponse.ReasonPhrase, null, null);
            base.SendFinalResponse(sipResponse);
        }

        /// <summary>
        /// Cancels this transaction stopping any further processing or transmission of a previously
        /// generated final response.
        /// </summary>
        /// <returns>A socket error with the result of the cancel.</returns>
        public void CancelCall()
        {
            if (TransactionState == SIPTransactionStatesEnum.Calling || TransactionState == SIPTransactionStatesEnum.Trying || TransactionState == SIPTransactionStatesEnum.Proceeding)
            {
                base.UpdateTransactionState(SIPTransactionStatesEnum.Cancelled);
                UASInviteTransactionCancelled?.Invoke(this);

                SIPResponse cancelResponse = SIPResponse.GetResponse(TransactionRequest, SIPResponseStatusCodesEnum.RequestTerminated, null);
                cancelResponse.Header.To.ToTag = LocalTag;
                base.SendFinalResponse(cancelResponse);
            }
            else
            {
                logger.LogWarning("A request was made to cancel transaction " + TransactionId + " that was not in the calling, trying or proceeding states, state=" + TransactionState + ".");
            }
        }

        public SIPResponse GetOkResponse(string contentType, string messageBody)
        {
            SIPResponse okResponse = new SIPResponse(SIPResponseStatusCodesEnum.Ok, null);
            okResponse.SetSendFromHints(TransactionRequest.LocalSIPEndPoint);

            SIPHeader requestHeader = TransactionRequest.Header;
            okResponse.Header = new SIPHeader(
                SIPContactHeader.GetDefaultSIPContactHeader(TransactionRequest.URI.Scheme),
                requestHeader.From, requestHeader.To,
                requestHeader.CSeq,
                requestHeader.CallId);
            okResponse.Header.To.ToTag = m_localTag;
            okResponse.Header.CSeqMethod = requestHeader.CSeqMethod;
            okResponse.Header.Vias = requestHeader.Vias;
            okResponse.Header.Server = SIPConstants.SipUserAgentVersionString;
            okResponse.Header.MaxForwards = Int32.MinValue;
            okResponse.Header.RecordRoutes = requestHeader.RecordRoutes;
            okResponse.Header.Supported = SIPExtensionHeaders.REPLACES + ", " + SIPExtensionHeaders.NO_REFER_SUB
                + ((PrackSupported == true) ? ", " + SIPExtensionHeaders.PRACK : "");

            okResponse.Body = messageBody;
            okResponse.Header.ContentType = contentType;
            okResponse.Header.ContentLength = (messageBody != null) ? messageBody.Length : 0;

            return okResponse;
        }
    }
}
