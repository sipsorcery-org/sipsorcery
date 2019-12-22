﻿//-----------------------------------------------------------------------------
// Filename: SIPTransactionEngine.cs
//
// Description: SIP transaction manager.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// ??	        Aaron Clauson	Created, Hobart, Australia
// 30 Oct 2019  Aaron Clauson   Added support for reliable provisional responses as per RFC3262.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    internal class SIPTransactionEngine
    {
        private const int MAX_QUEUEWAIT_PERIOD = 100;              // Maximum time to wait to check for new pending transactions.
        private static readonly int m_t1 = SIPTimings.T1;
        private static readonly int m_t2 = SIPTimings.T2;
        private static readonly int m_t6 = SIPTimings.T6;
        private const int PENDINGREQUESTS_CHECK_PERIOD = 500;      // Time between checking the pending requests queue to resend reliable requests that have not been responded to.
        private const int MAX_RELIABLETRANSMISSIONS_COUNT = 5000;  // The maximum number of pending transactions that can be outstanding .

        protected static ILogger logger = Log.Logger;

        protected static readonly int m_maxRingTime = SIPTimings.MAX_RING_TIME; // Max time an INVITE will be left ringing for (typically 10 mins).    

        private bool m_isClosed = false;
        private SIPTransport m_sipTransport;

        /// <summary>
        /// Contains a list of the transactions that are being monitored or responses and retransmitted 
        /// on when none is recieved to attempt a more reliable delivery rather then just relying on the initial 
        /// request to get through.
        /// </summary>
        private ConcurrentDictionary<string, SIPTransaction> m_pendingTransactions = new ConcurrentDictionary<string, SIPTransaction>();

        public int TransactionsCount
        {
            get { return m_pendingTransactions.Count; }
        }

        public event SIPTransactionRequestRetransmitDelegate SIPRequestRetransmitTraceEvent;
        public event SIPTransactionResponseRetransmitDelegate SIPResponseRetransmitTraceEvent;

        public SIPTransactionEngine(SIPTransport sipTransport)
        {
            m_sipTransport = sipTransport;

            Task.Run(ProcessPendingTransactions);
        }

        public void AddTransaction(SIPTransaction sipTransaction)
        {
            if (m_pendingTransactions.Count > MAX_RELIABLETRANSMISSIONS_COUNT)
            {
                throw new ApplicationException("Pending transactions list is full.");
            }
            else if (!m_pendingTransactions.ContainsKey(sipTransaction.TransactionId))
            {
                if (!m_pendingTransactions.TryAdd(sipTransaction.TransactionId, sipTransaction))
                {
                    throw new ApplicationException("Failed to add transaction to pending list.");
                }
            }

        }

        public bool Exists(string transactionID)
        {
            return m_pendingTransactions.ContainsKey(transactionID);
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
        /// Transaction matching see RFC3261 17.1.3 &amp; 17.2.3 for matching client and server transactions respectively. 
        /// IMPORTANT NOTE this transaction matching applies to all requests and responses EXCEPT ACK requests to 2xx responses see 13.2.2.4. 
        /// For ACK's to 2xx responses the ACK represents a separate transaction. However for a UAS sending an INVITE response the ACK still 
        /// has to be matched to an existing server transaction in order to transition it to a Confirmed state.
        /// 
        /// ACK's:
        ///  - The ACK for a 2xx response will have the same CallId, From Tag and To Tag.
        ///  - An ACK for a non-2xx response will have the same branch ID as the INVITE whose response it acknowledges.
        ///  
        /// PRACK Requests:
        /// (From RFC3262)
        /// A matching PRACK is defined as one within the same dialog as the response, and
        /// whose method, CSeq-num, and response-num in the RAck header field
        /// match, respectively, the method from the CSeq, the sequence number
        /// from the CSeq, and the sequence number from the RSeq of the reliable
        /// provisional response.
        /// </summary>
        /// <param name="sipRequest">The request to attempt to locate a matching transaction for.</param>
        /// <returns>A matching transaction or null if no match found.</returns>
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

            lock (m_pendingTransactions)
            {
                if (transactionId != null && m_pendingTransactions.ContainsKey(transactionId))
                {
                    return m_pendingTransactions[transactionId];
                }
                else
                {
                    // No normal match found so look fo a 2xx INVITE response waiting for an ACK.
                    if (sipRequest.Method == SIPMethodsEnum.ACK)
                    {
                        //logger.LogDebug("Looking for ACK transaction, branchid=" + sipRequest.Header.Via.TopViaHeader.Branch + ".");

                        foreach (SIPTransaction transaction in m_pendingTransactions.Values)
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
                            if ((transaction.TransactionType == SIPTransactionTypesEnum.InviteClient || transaction.TransactionType == SIPTransactionTypesEnum.InviteServer)
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
                                    //string requestEndPoint = (sipRequest.RemoteSIPEndPoint != null) ? sipRequest.RemoteSIPEndPoint.ToString() : " ? ";
                                    //logger.LogInformation("ACK for contact=" + contactAddress + ", cseq=" + sipRequest.Header.CSeq + " was matched using Call-ID mechanism (to tags: " + transaction.TransactionFinalResponse.Header.To.ToTag + "=" + sipRequest.Header.To.ToTag + ", from tags:" + transaction.TransactionFinalResponse.Header.From.FromTag + "=" + sipRequest.Header.From.FromTag + ").");
                                    return transaction;
                                }
                            }
                        }
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.PRACK)
                    {
                        foreach (SIPTransaction transaction in m_pendingTransactions.Values)
                        {
                            if (transaction.TransactionType == SIPTransactionTypesEnum.InviteServer)
                            {
                                if (transaction.TransactionRequest.Header.CallId == sipRequest.Header.CallId &&
                                    transaction.ReliableProvisionalResponse.Header.From.FromTag == sipRequest.Header.From.FromTag &&
                                    transaction.ReliableProvisionalResponse.Header.CSeq == sipRequest.Header.RAckCSeq &&
                                    transaction.ReliableProvisionalResponse.Header.RSeq == sipRequest.Header.RAckRSeq &&
                                    transaction.ReliableProvisionalResponse.Header.CSeqMethod == sipRequest.Header.RAckCSeqMethod)
                                {
                                    //logger.LogDebug("PRACK for contact=" + contactAddress + ", cseq=" + sipRequest.Header.CSeq + " was matched by callid, tags and cseq.");

                                    return transaction;
                                }
                            }
                        }
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

                m_pendingTransactions.TryGetValue(transactionId, out var transaction);
                return transaction;
            }
        }

        public SIPTransaction GetTransaction(string transactionId)
        {
            m_pendingTransactions.TryGetValue(transactionId, out var transaction);
            return transaction;
        }

        public void PrintPendingTransactions()
        {
            logger.LogDebug("=== Pending Transactions ===");

            foreach (SIPTransaction transaction in m_pendingTransactions.Values)
            {
                logger.LogDebug("Pending tansaction " + transaction.TransactionRequest.Method + " " + transaction.TransactionState + " " + DateTime.Now.Subtract(transaction.Created).TotalSeconds.ToString("0.##") + "s " + transaction.TransactionRequestURI.ToString() + " (" + transaction.TransactionId + ").");
            }
        }

        public void Shutdown()
        {
            m_isClosed = true;
        }

        /// <summary>
        /// Removes a transction from the pending list.
        /// </summary>
        /// <param name="transactionId"></param>
        private void RemoveTransaction(string transactionId)
        {
            m_pendingTransactions.TryRemove(transactionId, out _);
        }

        private void RemoveTransaction(SIPTransaction transaction)
        {
            // Remove all event handlers.
            transaction.RemoveEventHandlers();
            RemoveTransaction(transaction.TransactionId);
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

            lock (m_pendingTransactions)
            {
                foreach (SIPTransaction transaction in m_pendingTransactions.Values)
                {
                    if ((transaction.TransactionType == SIPTransactionTypesEnum.InviteClient || transaction.TransactionType == SIPTransactionTypesEnum.InviteServer) &&
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

        /// <summary>
        /// A long running method that monitors and processes a list of transactions that need to send a reliable
        /// request or response.
        /// </summary>
        private async Task ProcessPendingTransactions()
        {
            try
            {
                while (!m_isClosed)
                {
                    if (m_pendingTransactions.Count == 0)
                    {
                        // No request retransmissions wait until next one required.
                        await Task.Delay(MAX_QUEUEWAIT_PERIOD);
                    }

                    foreach (SIPTransaction transaction in m_pendingTransactions.Values.Where(x => x.DeliveryPending))
                    {
                        try
                        {
                            if (transaction.TransactionState == SIPTransactionStatesEnum.Terminated ||
                                    transaction.TransactionState == SIPTransactionStatesEnum.Confirmed ||
                                    transaction.HasTimedOut)
                            {
                                transaction.DeliveryPending = false;
                            }
                            else if (transaction.HasDeliveryExpired(m_t6))
                            {
                                if (transaction.TransactionState == SIPTransactionStatesEnum.Proceeding)
                                {
                                    // If the transaction is a UAS and still in the progress state then the timeout was 
                                    // for a provisional response and it should not set any transaction properties that 
                                    // will affect the delivery of any subsequent final response.
                                    transaction.OnTimedOutProvisionalResponse();
                                }
                                else
                                {
                                    transaction.Expire();
                                }
                            }
                            else
                            {
                                if (transaction.DeliveryPending && transaction.IsRetransmitDue(m_t1, m_t2))
                                {
                                    SocketError sendResult = SocketError.Success;

                                    switch (transaction.TransactionType)
                                    {
                                        case SIPTransactionTypesEnum.InviteServer:

                                            switch (transaction.TransactionState)
                                            {
                                                case SIPTransactionStatesEnum.Calling:
                                                    break;

                                                case SIPTransactionStatesEnum.Trying:
                                                    break;

                                                case SIPTransactionStatesEnum.Proceeding:
                                                    if (transaction.ReliableProvisionalResponse != null)
                                                    {
                                                        sendResult = await SendTransactionProvisionalResponse(transaction);
                                                    }
                                                    break;

                                                case SIPTransactionStatesEnum.Completed:
                                                    sendResult = await SendTransactionFinalResponse(transaction);
                                                    break;

                                                case SIPTransactionStatesEnum.Confirmed:
                                                    transaction.DeliveryPending = false;
                                                    break;

                                                case SIPTransactionStatesEnum.Cancelled:
                                                    transaction.DeliveryPending = false;
                                                    break;

                                                default:
                                                    logger.LogWarning($"InviteServer Transaction entered an unexpected transaction state {transaction.TransactionState}.");
                                                    transaction.DeliveryFailed = true;
                                                    break;
                                            }
                                            break;

                                        case SIPTransactionTypesEnum.InviteClient:

                                            switch (transaction.TransactionState)
                                            {
                                                case SIPTransactionStatesEnum.Calling:
                                                    sendResult = await SendTransactionRequest(transaction);
                                                    break;

                                                case SIPTransactionStatesEnum.Trying:
                                                    break;

                                                case SIPTransactionStatesEnum.Proceeding:
                                                    if (transaction.PRackRequest != null)
                                                    {
                                                        sendResult = await m_sipTransport.SendRequestAsync(transaction.PRackRequest);
                                                    }
                                                    break;

                                                case SIPTransactionStatesEnum.Completed:
                                                    transaction.DeliveryPending = false;
                                                    break;

                                                case SIPTransactionStatesEnum.Confirmed:
                                                    transaction.DeliveryPending = false;
                                                    break;

                                                case SIPTransactionStatesEnum.Cancelled:
                                                    transaction.DeliveryPending = false;
                                                    break;

                                                default:
                                                    logger.LogWarning($"InviteClient Transaction entered an unexpected transaction state {transaction.TransactionState}.");
                                                    transaction.DeliveryFailed = true;
                                                    break;
                                            }

                                            break;

                                        case SIPTransactionTypesEnum.NonInvite:

                                            switch (transaction.TransactionState)
                                            {
                                                case SIPTransactionStatesEnum.Calling:
                                                    sendResult = await SendTransactionRequest(transaction);
                                                    break;

                                                case SIPTransactionStatesEnum.Trying:
                                                    break;

                                                case SIPTransactionStatesEnum.Proceeding:
                                                    break;

                                                case SIPTransactionStatesEnum.Completed:
                                                    if (transaction.TransactionFinalResponse != null)
                                                    {
                                                        // Sending a single final response on a non-INVITE tx. The same response
                                                        // will be automatically resent if the same request is received.
                                                        sendResult = await m_sipTransport.SendResponseAsync(transaction.TransactionFinalResponse);
                                                        transaction.DeliveryPending = false;
                                                    }
                                                    break;

                                                case SIPTransactionStatesEnum.Confirmed:
                                                    transaction.DeliveryPending = false;
                                                    break;

                                                default:
                                                    logger.LogWarning($"NonInvite Transaction entered an unexpected transaction state {transaction.TransactionState}.");
                                                    transaction.DeliveryFailed = true;
                                                    break;
                                            }
                                            break;

                                        default:
                                            logger.LogWarning($"Unrecognised transaction type {transaction.TransactionType}.");
                                            break;
                                    }
                                }
                            }
                        }
                        catch (Exception excp)
                        {
                            logger.LogError($"Exception processing pending transactions. {excp.Message}");
                        }
                    }

                    RemoveExpiredTransactions();
                }

                await Task.Delay(PENDINGREQUESTS_CHECK_PERIOD);

            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransactionEngine ProcessPendingTransactions. " + excp.Message);
            }
        }

        /// <summary>
        /// Sends or resends a Invite Server transaction provisional response. Only
        /// relevant reliable provisional responses as per RFC3262 are supported.
        /// </summary>
        /// <param name="transaction">The transaction to send the provisional response for.</param>
        /// <returns>The result of the send attempt.</returns>
        private Task<SocketError> SendTransactionProvisionalResponse(SIPTransaction transaction)
        {
            // Provisional response reliable for INVITE-UAS.
            if (transaction.Retransmits > 1)
            {
                transaction.OnRetransmitProvisionalResponse();
                SIPResponseRetransmitTraceEvent?.Invoke(transaction, transaction.ReliableProvisionalResponse, transaction.Retransmits);
            }

            return m_sipTransport.SendResponseAsync(transaction.ReliableProvisionalResponse);
        }

        /// <summary>
        /// Sends or resends a transaction final response.
        /// </summary>
        /// <param name="transaction">The transaction to send the final response for.</param>
        /// <returns>The result of the send attempt.</returns>
        private Task<SocketError> SendTransactionFinalResponse(SIPTransaction transaction)
        {
            if (transaction.InitialTransmit == DateTime.MinValue)
            {
                transaction.InitialTransmit = DateTime.Now;
            }

            transaction.Retransmits = transaction.Retransmits + 1;
            transaction.LastTransmit = DateTime.Now;

            if (transaction.Retransmits > 1)
            {
                transaction.OnRetransmitFinalResponse();
                SIPResponseRetransmitTraceEvent?.Invoke(transaction, transaction.TransactionFinalResponse, transaction.Retransmits);
            }
            return m_sipTransport.SendResponseAsync(transaction.TransactionFinalResponse);
        }

        /// <summary>
        /// Sends or resends the transaction request.
        /// </summary>
        /// <param name="transaction">The transaction to resend the request for.</param>
        /// <returns>The result of the send attempt.</returns>
        private Task<SocketError> SendTransactionRequest(SIPTransaction transaction)
        {
            Task<SocketError> result = null;

            if (transaction.InitialTransmit == DateTime.MinValue)
            {
                transaction.InitialTransmit = DateTime.Now;
            }

            transaction.Retransmits = transaction.Retransmits + 1;
            transaction.LastTransmit = DateTime.Now;

            // INVITE-UAC and no-INVITE transaction types, send request reliably.
            if (transaction.Retransmits > 1)
            {
                SIPRequestRetransmitTraceEvent?.Invoke(transaction, transaction.TransactionRequest, transaction.Retransmits);
                transaction.RequestRetransmit();
            }

            if (transaction.OutboundProxy != null)
            {
                result = m_sipTransport.SendRequestAsync(transaction.OutboundProxy, transaction.TransactionRequest);
            }
            else
            {
                result = m_sipTransport.SendRequestAsync(transaction.TransactionRequest);
            }

            return result;
        }

        private void RemoveExpiredTransactions()
        {
            try
            {
                lock (m_pendingTransactions)
                {
                    List<string> expiredTransactionIds = new List<string>();

                    foreach (SIPTransaction transaction in m_pendingTransactions.Values)
                    {
                        if (transaction.TransactionType == SIPTransactionTypesEnum.InviteClient || transaction.TransactionType == SIPTransactionTypesEnum.InviteServer)
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
                        if (m_pendingTransactions.ContainsKey(transactionId))
                        {
                            SIPTransaction expiredTransaction = m_pendingTransactions[transactionId];
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
    }
}
