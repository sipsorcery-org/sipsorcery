//-----------------------------------------------------------------------------
// Filename: SIPTransactionEngine.cs
//
// Description: SIP transaction manager.
//
// Author(s):
// Aaron Clauson
// 
// History:
// ??	        Aaron Clauson	Created (aaron@sipsorcery.com), SIPSorcery Ltd, Hobart, Australia (www.sipsorcery.com)
// 30 Oct 2019  Aaron Clauson   Added support for reliable provisional responses as per RFC3262.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using SIPSorcery.Sys;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    public class SIPTransactionEngine
    {
        protected static ILogger logger = Log.Logger;

        private static readonly int m_t6 = SIPTimings.T6;
        protected static readonly int m_maxRingTime = SIPTimings.MAX_RING_TIME; // Max time an INVITE will be left ringing for (typically 10 mins).    

        private Dictionary<string, SIPTransaction> m_transactions = new Dictionary<string, SIPTransaction>();
        public int TransactionsCount
        {
            get { return m_transactions.Count; }
        }

        public SIPTransactionEngine()
        { }

        public void AddTransaction(SIPTransaction sipTransaction)
        {
            RemoveExpiredTransactions();

            lock (m_transactions)
            {
                if (!m_transactions.ContainsKey(sipTransaction.TransactionId))
                {
                    m_transactions.Add(sipTransaction.TransactionId, sipTransaction);
                }
                else
                {
                    throw new ApplicationException("An attempt was made to add a duplicate SIP transaction.");
                }
            }
        }

        public bool Exists(SIPRequest sipRequest)
        {
            return GetTransaction(sipRequest) != null;
        }

        public bool Exists(SIPResponse sipResponse)
        {
            return GetTransaction(sipResponse) != null;
        }

        /// <summary>
        /// Transaction matching see RFC3261 17.1.3 & 17.2.3 for matching client and server transactions respectively. 
        /// IMPORTANT NOTE this transaction matching applies to all requests and responses EXCEPT ACK requests to 2xx responses see 13.2.2.4. 
        /// For ACK's to 2xx responses the ACK represents a separate transaction. However for a UAS sending an INVITE response the ACK still has to be 
        /// matched to an existing server transaction in order to transition it to a Confirmed state.
        /// 
        /// ACK's:
        ///  - The ACK for a 2xx response will have the same CallId, From Tag and To Tag.
        ///  - An ACK for a non-2xx response will have the same branch ID as the INVITE whose response it acknowledges.
        /// </summary>
        /// <param name="sipRequest"></param>
        /// <returns></returns>
        public SIPTransaction GetTransaction(SIPRequest sipRequest)
        {
            // The branch is mandatory but it doesn't stop some UA's not setting it.
            if (sipRequest.Header.Vias.TopViaHeader.Branch == null || sipRequest.Header.Vias.TopViaHeader.Branch.Trim().Length == 0)
            {
                return null;
            }

            SIPMethodsEnum transactionMethod = (sipRequest.Method != SIPMethodsEnum.ACK) ? sipRequest.Method : SIPMethodsEnum.INVITE;
            string transactionId = SIPTransaction.GetRequestTransactionId(sipRequest.Header.Vias.TopViaHeader.Branch, transactionMethod);
            string contactAddress = (sipRequest.Header.Contact != null && sipRequest.Header.Contact.Count > 0) ? sipRequest.Header.Contact[0].ToString() : "no contact";

            lock (m_transactions)
            {
                //if (transactionMethod == SIPMethodsEnum.ACK)
                //{
                    //logger.LogInformation("Matching ACK with contact=" + contactAddress + ", cseq=" + sipRequest.Header.CSeq + ".");
                //}

                if (transactionId != null && m_transactions.ContainsKey(transactionId))
                {
                    //if (transactionMethod == SIPMethodsEnum.ACK)
                    //{
                        //logger.LogInformation("ACK for contact=" + contactAddress + ", cseq=" + sipRequest.Header.CSeq + " was matched by branchid.");
                    //}

                    return m_transactions[transactionId];
                }
                else
                {
                    // No normal match found so look fo a 2xx INVITE response waiting for an ACK.
                    if (sipRequest.Method == SIPMethodsEnum.ACK)
                    {
                        //logger.LogDebug("Looking for ACK transaction, branchid=" + sipRequest.Header.Via.TopViaHeader.Branch + ".");

                        foreach (SIPTransaction transaction in m_transactions.Values)
                        {
                            // According to the standard an ACK should only not get matched by the branchid on the original INVITE for a non-2xx response. However
                            // my Cisco phone created a new branchid on ACKs to 487 responses and since the Cisco also used the same Call-ID and From tag on the initial
                            // unauthenticated request and the subsequent authenticated request the condition below was found to be the best way to match the ACK.
                            /*if (transaction.TransactionType == SIPTransactionTypesEnum.Invite && transaction.TransactionFinalResponse != null && transaction.TransactionState == SIPTransactionStatesEnum.Completed)
                            {
                                if (transaction.TransactionFinalResponse.Header.CallId == sipRequest.Header.CallId &&
                                    transaction.TransactionFinalResponse.Header.To.ToTag == sipRequest.Header.To.ToTag &&
                                    transaction.TransactionFinalResponse.Header.From.FromTag == sipRequest.Header.From.FromTag)
                                {
                                    return transaction;
                                }
                            }*/

                            // As an experiment going to try matching on the Call-ID. This field seems to be unique and therefore the chance
                            // of collisions seemingly very slim. As a safeguard if there happen to be two transactions with the same Call-ID in the list the match will not be made.
                            // One case where the Call-Id match breaks down is for in-Dialogue requests in that case there will be multiple transactions with the same Call-ID and tags.
                            //if (transaction.TransactionType == SIPTransactionTypesEnum.Invite && transaction.TransactionFinalResponse != null && transaction.TransactionState == SIPTransactionStatesEnum.Completed)
                            if ((transaction.TransactionType == SIPTransactionTypesEnum.InivteClient || transaction.TransactionType == SIPTransactionTypesEnum.InviteServer)
                                && transaction.TransactionFinalResponse != null)
                            {
                                if (transaction.TransactionRequest.Header.CallId == sipRequest.Header.CallId &&
                                    transaction.TransactionFinalResponse.Header.To.ToTag == sipRequest.Header.To.ToTag &&
                                    transaction.TransactionFinalResponse.Header.From.FromTag == sipRequest.Header.From.FromTag &&
                                    transaction.TransactionFinalResponse.Header.CSeq == sipRequest.Header.CSeq)
                                {
                                    //logger.LogInformation("ACK for contact=" + contactAddress + ", cseq=" + sipRequest.Header.CSeq + " was matched by callid, tags and cseq.");

                                    return transaction;
                                }
                                else if (transaction.TransactionRequest.Header.CallId == sipRequest.Header.CallId && 
                                    transaction.TransactionFinalResponse.Header.CSeq == sipRequest.Header.CSeq &&
                                    IsCallIdUniqueForPending(sipRequest.Header.CallId))
                                {
                                    string requestEndPoint = (sipRequest.RemoteSIPEndPoint != null) ? sipRequest.RemoteSIPEndPoint.ToString() : " ? ";
                                    //logger.LogInformation("ACK for contact=" + contactAddress + ", cseq=" + sipRequest.Header.CSeq + " was matched using Call-ID mechanism (to tags: " + transaction.TransactionFinalResponse.Header.To.ToTag + "=" + sipRequest.Header.To.ToTag + ", from tags:" + transaction.TransactionFinalResponse.Header.From.FromTag + "=" + sipRequest.Header.From.FromTag + ").");
                                    return transaction;
                                }
                            }
                        }

                        //logger.LogInformation("ACK for contact=" + contactAddress + ", cseq=" + sipRequest.Header.CSeq + " was not matched.");
                    }

                    return null;
                }
            }
        }

        public SIPTransaction GetTransaction(SIPResponse sipResponse)
        {
            if (sipResponse.Header.Vias.TopViaHeader.Branch == null || sipResponse.Header.Vias.TopViaHeader.Branch.Trim().Length == 0)
            {
                return null;
            }
            else
            {
                string transactionId = SIPTransaction.GetRequestTransactionId(sipResponse.Header.Vias.TopViaHeader.Branch, sipResponse.Header.CSeqMethod);

                lock (m_transactions)
                {
                    if (m_transactions.ContainsKey(transactionId))
                    {
                        return m_transactions[transactionId];
                    }
                    else
                    {
                        return null;
                    }
                }
            }
        }

        public SIPTransaction GetTransaction(string transactionId)
        {
            lock (m_transactions)
            {
                if (m_transactions.ContainsKey(transactionId))
                {
                    return m_transactions[transactionId];
                }
                else
                {
                    return null;
                }
            }
        }

        public void RemoveExpiredTransactions()
        {
            try
            {
                lock (m_transactions)
                {
                    List<string> expiredTransactionIds = new List<string>();

                    foreach (SIPTransaction transaction in m_transactions.Values)
                    {
                        if (transaction.TransactionType == SIPTransactionTypesEnum.InivteClient || transaction.TransactionType == SIPTransactionTypesEnum.InviteServer)
                        {
                            if (transaction.TransactionState == SIPTransactionStatesEnum.Confirmed)
                            {
                                // Need to wait until the transaction timeout period is reached in case any ACK re-transmits are received.
                                if (DateTime.Now.Subtract(transaction.CompletedAt).TotalMilliseconds >= m_t6)
                                {
                                    expiredTransactionIds.Add(transaction.TransactionId);
                                }
                            }
                            else if (transaction.TransactionState == SIPTransactionStatesEnum.Completed)
                            {
                                if (DateTime.Now.Subtract(transaction.CompletedAt).TotalMilliseconds >= m_t6)
                                {
                                    expiredTransactionIds.Add(transaction.TransactionId);
                                }
                            }
                            else if (transaction.HasTimedOut)
                            {
                                // For INVITES need to give timed out transactions time to send the reliable repsonses and receive the ACKs.
                                if (DateTime.Now.Subtract(transaction.TimedOutAt).TotalSeconds >= m_t6)
                                {
                                    expiredTransactionIds.Add(transaction.TransactionId);
                                }
                            }
                            else if (transaction.TransactionState == SIPTransactionStatesEnum.Proceeding)
                            {
                                if (DateTime.Now.Subtract(transaction.Created).TotalMilliseconds >= m_maxRingTime)
                                {
                                    // INVITE requests that have been ringing too long.
                                    transaction.HasTimedOut = true;
                                    transaction.TimedOutAt = DateTime.Now;
                                    transaction.DeliveryPending = false;
                                    transaction.DeliveryFailed = true;
                                    transaction.FireTransactionTimedOut();
                                }
                            }
                            else if (DateTime.Now.Subtract(transaction.Created).TotalMilliseconds >= m_t6)
                            {
                                //logger.LogDebug("INVITE transaction (" + transaction.TransactionId + ") " + transaction.TransactionRequestURI.ToString() + " in " + transaction.TransactionState + " has been alive for " + DateTime.Now.Subtract(transaction.Created).TotalSeconds.ToString("0") + ".");

                                if (transaction.TransactionState == SIPTransactionStatesEnum.Calling ||
                                    transaction.TransactionState == SIPTransactionStatesEnum.Trying)
                                {
                                    transaction.HasTimedOut = true;
                                    transaction.TimedOutAt = DateTime.Now;
                                    transaction.DeliveryPending = false;
                                    transaction.DeliveryFailed = true;
                                    transaction.FireTransactionTimedOut();
                                }
                            }
                        }
                        else if (transaction.HasTimedOut)
                        {
                            expiredTransactionIds.Add(transaction.TransactionId);
                        }
                        else if (DateTime.Now.Subtract(transaction.Created).TotalMilliseconds >= m_t6)
                        {
                            if (transaction.TransactionState == SIPTransactionStatesEnum.Calling ||
                               transaction.TransactionState == SIPTransactionStatesEnum.Trying ||
                               transaction.TransactionState == SIPTransactionStatesEnum.Proceeding)
                            {
                                //logger.LogWarning("Timed out transaction in SIPTransactionEngine, should have been timed out in the SIP Transport layer. " + transaction.TransactionRequest.Method + ".");
                                transaction.DeliveryPending = false;
                                transaction.DeliveryFailed = true;
                                transaction.TimedOutAt = DateTime.Now;
                                transaction.HasTimedOut = true;
                                transaction.FireTransactionTimedOut();
                            }

                            expiredTransactionIds.Add(transaction.TransactionId);
                        }
                    }

                    foreach (string transactionId in expiredTransactionIds)
                    {
                        if (m_transactions.ContainsKey(transactionId))
                        {
                            SIPTransaction expiredTransaction = m_transactions[transactionId];
                            expiredTransaction.FireTransactionRemoved();
                            RemoveTransaction(expiredTransaction);
                        }
                    }
                }

                //PrintPendingTransactions();
            }
            catch (Exception excp)
            {
                logger.LogError("Exception RemoveExpiredTransaction. " + excp.Message);
            }
        }

        public void PrintPendingTransactions()
         {
             logger.LogDebug("=== Pending Transactions ===");

             foreach (SIPTransaction transaction in m_transactions.Values)
             {
                 logger.LogDebug(" Pending tansaction " + transaction.TransactionRequest.Method + " " + transaction.TransactionState + " " + DateTime.Now.Subtract(transaction.Created).TotalSeconds.ToString("0.##") + "s " + transaction.TransactionRequestURI.ToString() + " (" + transaction.TransactionId + ").");
             }
         }

        /// <summary>
        /// Should not normally be used as transactions will time out after the retransmit window has expired. This method is 
        /// used in such cases where the original request is being dropped and no action is required on a re-transmit.
        /// </summary>
        /// <param name="transactionId"></param>
        public void RemoveTransaction(string transactionId)
        {
            lock (m_transactions)
            {
                if (m_transactions.ContainsKey(transactionId))
                {
                    m_transactions.Remove(transactionId);
                }
            }
        }

        private void RemoveTransaction(SIPTransaction transaction)
        {
            // Remove all event handlers.
            transaction.RemoveEventHandlers();

            RemoveTransaction(transaction.TransactionId);
        }

        public void RemoveAll()
        {
            lock (m_transactions)
            {
                m_transactions.Clear();
            }
        }

        /// <summary>
        ///  Checks whether there is only a single transaction outstanding for a Call-ID header. This is used in an experimental trial of matching
        ///  ACK's on the Call-ID if the full check fails.
        /// </summary>
        /// <param name="callId">The SIP Header Call-ID to check for.</param>
        /// <returns>True if there is only a single pending transaction with the specified  Call-ID, false otherwise.</returns>
        private bool IsCallIdUniqueForPending(string callId)
        {
            bool match = false;
            
            lock (m_transactions)
            {
                foreach (SIPTransaction transaction in m_transactions.Values)
                {
                    if ((transaction.TransactionType == SIPTransactionTypesEnum.InivteClient || transaction.TransactionType == SIPTransactionTypesEnum.InviteServer) && 
                        transaction.TransactionFinalResponse != null && 
                        transaction.TransactionState == SIPTransactionStatesEnum.Completed &&
                        transaction.TransactionRequest.Header.CallId == callId)
                    {
                        if (!match)
                        {
                            match = true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }

            return match;
        }
    }
}
