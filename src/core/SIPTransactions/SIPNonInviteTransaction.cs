//-----------------------------------------------------------------------------
// Filename: SIPnonInviteTransaction.cs
//
// Description: SIP Transaction for all non-INVITE transactions where no dialog is required.
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
        public event SIPTransactionTimedOutDelegate NonInviteTransactionTimedOut;
        public event SIPTransactionRequestReceivedDelegate NonInviteRequestReceived;
        public event SIPTransactionRequestRetransmitDelegate NonInviteTransactionRequestRetransmit;

        public SIPNonInviteTransaction(SIPTransport sipTransport, SIPRequest sipRequest, SIPEndPoint outboundProxy)
            : base(sipTransport, sipRequest, outboundProxy)
        {
            TransactionType = SIPTransactionTypesEnum.NonInvite;
            TransactionRequestReceived += SIPNonInviteTransaction_TransactionRequestReceived;
            TransactionInformationResponseReceived += SIPNonInviteTransaction_TransactionInformationResponseReceived;
            TransactionFinalResponseReceived += SIPNonInviteTransaction_TransactionFinalResponseReceived;
            TransactionTimedOut += SIPNonInviteTransaction_TransactionTimedOut;
            TransactionRemoved += SIPNonInviteTransaction_TransactionRemoved;
            TransactionRequestRetransmit += SIPNonInviteTransaction_TransactionRequestRetransmit;
        }

        private void SIPNonInviteTransaction_TransactionRemoved(SIPTransaction transaction)
        {
            // Remove all event handlers.
            NonInviteTransactionInfoResponseReceived = null;
            NonInviteTransactionFinalResponseReceived = null;
            NonInviteTransactionTimedOut = null;
            NonInviteRequestReceived = null;
        }

        private void SIPNonInviteTransaction_TransactionTimedOut(SIPTransaction sipTransaction)
        {
            NonInviteTransactionTimedOut?.Invoke(this);
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
    }
}
