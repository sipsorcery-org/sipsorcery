//-----------------------------------------------------------------------------
// Filename: SIPCancelTransaction.cs
//
// Description: SIP Transaction created in response to a CANCEL request.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Feb 2006	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPCancelTransaction : SIPTransaction
    {
        public event SIPTransactionResponseReceivedDelegate CancelTransactionFinalResponseReceived;

        private UASInviteTransaction m_originalTransaction;

        internal SIPCancelTransaction(SIPTransport sipTransport, SIPRequest sipRequest, UASInviteTransaction originalTransaction)
            : base(sipTransport, sipRequest, originalTransaction.OutboundProxy)
        {
            m_originalTransaction = originalTransaction;
            TransactionType = SIPTransactionTypesEnum.NonInvite;
            TransactionRequestReceived += SIPCancelTransaction_TransactionRequestReceived;
            TransactionFinalResponseReceived += SIPCancelTransaction_TransactionFinalResponseReceived;
            TransactionRemoved += SIPCancelTransaction_TransactionRemoved;
        }

        private void SIPCancelTransaction_TransactionRemoved(SIPTransaction transaction)
        {
            // Remove event handlers.
            CancelTransactionFinalResponseReceived = null;
        }

        private void SIPCancelTransaction_TransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            if (sipResponse.StatusCode < 200)
            {
                logger.LogWarning("A SIP CANCEL transaction received an unexpected SIP information response " + sipResponse.ReasonPhrase + ".");
            }
            else
            {
                CancelTransactionFinalResponseReceived?.Invoke(localSIPEndPoint, remoteEndPoint, sipTransaction, sipResponse);
            }
        }

        private void SIPCancelTransaction_TransactionRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            try
            {
                SIPResponse cancelResponse;

                if (m_originalTransaction != null)
                {
                    m_originalTransaction.CancelCall();
                    cancelResponse = GetCancelResponse(sipRequest, SIPResponseStatusCodesEnum.Ok);
                }
                else
                {
                    cancelResponse = GetCancelResponse(sipRequest, SIPResponseStatusCodesEnum.CallLegTransactionDoesNotExist);
                }

                SendFinalResponse(cancelResponse);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPCancelTransaction GotRequest. " + excp.Message);
            }
        }

        private SIPResponse GetCancelResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum sipResponseCode)
        {
            try
            {
                SIPResponse cancelResponse = new SIPResponse(sipResponseCode, null);
                cancelResponse.SetSendFromHints(sipRequest.LocalSIPEndPoint);

                SIPHeader requestHeader = sipRequest.Header;
                cancelResponse.Header = new SIPHeader(requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);
                cancelResponse.Header.CSeqMethod = SIPMethodsEnum.CANCEL;
                cancelResponse.Header.Vias = requestHeader.Vias;
                cancelResponse.Header.MaxForwards = Int32.MinValue;

                return cancelResponse;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GetCancelResponse. " + excp.Message);
                throw excp;
            }
        }
    }
}
