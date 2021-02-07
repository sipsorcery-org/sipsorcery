//-----------------------------------------------------------------------------
// Filename: SIPnonInviteTransaction.cs
//
// Description: SIP Transaction for all non-INVITE transactions where no dialog 
// is required.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 18 May 2008	Aaron Clauson	Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Net.Sockets;
using System.Threading.Tasks;

namespace SIPSorcery.SIP
{
    public class SIPNonInviteTransaction : SIPTransaction
    {
        public event SIPTransactionResponseReceivedDelegate NonInviteTransactionInfoResponseReceived;
        public event SIPTransactionResponseReceivedDelegate NonInviteTransactionFinalResponseReceived;
        public event SIPTransactionFailedDelegate NonInviteTransactionFailed;
        public event SIPTransactionRequestReceivedDelegate NonInviteRequestReceived;
        public event SIPTransactionRequestRetransmitDelegate NonInviteTransactionRequestRetransmit;

        public SIPNonInviteTransaction(SIPTransport sipTransport, SIPRequest sipRequest, SIPEndPoint outboundProxy)
            : base(sipTransport, sipRequest, outboundProxy)
        {
            TransactionType = SIPTransactionTypesEnum.NonInvite;
            TransactionRequestReceived += SIPNonInviteTransaction_TransactionRequestReceived;
            TransactionInformationResponseReceived += SIPNonInviteTransaction_TransactionInformationResponseReceived;
            TransactionFinalResponseReceived += SIPNonInviteTransaction_TransactionFinalResponseReceived;
            TransactionFailed += SIPNonInviteTransaction_TransactionFailed;
            TransactionRequestRetransmit += SIPNonInviteTransaction_TransactionRequestRetransmit;

            if (sipRequest.RemoteSIPEndPoint != null)
            {
                // This transaction type can be used to represent two different things:
                // - a tx that the application wants to use to send a request reliably,
                // - a tx for a request that has been received from a remote end point and that duplicates need
                //   to be detected for.
                // This block is for the second case. The tx request has been received and the tx engine
                // needs to be signalled that it is now at the request processing stage.
                base.UpdateTransactionState(SIPTransactionStatesEnum.Proceeding);
            }
        }

        private void SIPNonInviteTransaction_TransactionFailed(SIPTransaction sipTransaction, SocketError failureReason)
        {
            NonInviteTransactionFailed?.Invoke(this, failureReason);
        }

        private Task<SocketError> SIPNonInviteTransaction_TransactionRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            return NonInviteRequestReceived?.Invoke(localSIPEndPoint, remoteEndPoint, this, sipRequest);
        }

        private Task<SocketError> SIPNonInviteTransaction_TransactionInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            return NonInviteTransactionInfoResponseReceived?.Invoke(localSIPEndPoint, remoteEndPoint, this, sipResponse);
        }

        private Task<SocketError> SIPNonInviteTransaction_TransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            return NonInviteTransactionFinalResponseReceived?.Invoke(localSIPEndPoint, remoteEndPoint, this, sipResponse);
        }

        private void SIPNonInviteTransaction_TransactionRequestRetransmit(SIPTransaction sipTransaction, SIPRequest sipRequest, int retransmitNumber)
        {
            NonInviteTransactionRequestRetransmit?.Invoke(sipTransaction, sipRequest, retransmitNumber);
        }

        public void SendRequest()
        {
            base.SendReliableRequest();
        }

        public void SendResponse(SIPResponse finalResponse)
        {
            base.SendFinalResponse(finalResponse);
        }
    }
}
