//-----------------------------------------------------------------------------
// Filename: SIPTransaction.cs
//
// Description: SIP Transaction.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 14 Feb 2006	Aaron Clauson	Created, Dublin, Ireland.
// 30 Oct 2019  Aaron Clauson   Added support for reliable provisional responses as per RFC3262.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public enum SIPTransactionStatesEnum
    {
        Calling = 1,
        Completed = 2,
        Confirmed = 3,
        Proceeding = 4,
        Terminated = 5,
        Trying = 6,

        Cancelled = 7,      // This state is not in the SIP RFC but is deemed the most practical way to record that an INVITE has been cancelled. Other states will have ramifications for the transaction lifetime.
    }

    public enum SIPTransactionTypesEnum
    {
        InviteServer = 1,   // User agent server transaction.
        NonInvite = 2,
        InviteClient = 3,   // User agent client transaction.
    }

    /// <note>
    /// A response matches a client transaction under two conditions:
    ///
    /// 1.  If the response has the same value of the branch parameter in
    /// the top Via header field as the branch parameter in the top
    /// Via header field of the request that created the transaction.
    ///
    /// 2.  If the method parameter in the CSeq header field matches the
    /// method of the request that created the transaction.  The
    /// method is needed since a CANCEL request constitutes a
    /// different transaction, but shares the same value of the branch
    /// parameter.
    /// 
    /// [RFC 3261 17.2.3 page 137]
    /// A request matches a transaction:
    ///
    /// 1. the branch parameter in the request is equal to the one in the
    ///     top Via header field of the request that created the
    ///     transaction, and
    ///
    ///  2. the sent-by value in the top Via of the request is equal to the
    ///     one in the request that created the transaction, and
    ///
    ///  3. the method of the request matches the one that created the
    ///     transaction, except for ACK, where the method of the request
    ///     that created the transaction is INVITE.
    /// </note>
    public class SIPTransaction
    {
        protected static ILogger logger = Log.Logger;

        protected static readonly int m_t1 = SIPTimings.T1;                     // SIP Timer T1 in milliseconds.
        protected static readonly int m_t6 = SIPTimings.T6;                     // SIP Timer T6 in milliseconds.
        protected static readonly int m_maxRingTime = SIPTimings.MAX_RING_TIME; // Max time an INVITE will be left ringing for (typically 10 mins).    

        public int Retransmits = 0;
        public int AckRetransmits = 0;
        public int PrackRetransmits = 0;
        public DateTime InitialTransmit = DateTime.MinValue;
        public DateTime LastTransmit = DateTime.MinValue;
        public bool DeliveryPending = true;
        public bool DeliveryFailed = false;                 // If the transport layer does not receive a response to the request in the alloted time the request will be marked as failed.
        public bool HasTimedOut { get; set; }

        private string m_transactionId;
        public string TransactionId
        {
            get { return m_transactionId; }
        }

        private string m_sentBy;                        // The contact address from the top Via header that created the transaction. This is used for matching requests to server transactions.

        public SIPTransactionTypesEnum TransactionType = SIPTransactionTypesEnum.NonInvite;
        public DateTime Created = DateTime.Now;

        /// <summary>
        /// For INVITEs this is the time they recieved the final response and is used to calculate the time 
        /// they expie as T6 after this.
        /// </summary>
        public DateTime CompletedAt = DateTime.Now;

        /// <summary>
        /// If the transaction times out this holds the value it timed out at.
        /// </summary>
        public DateTime TimedOutAt;

        protected string m_branchId;
        public string BranchId
        {
            get { return m_branchId; }
        }

        protected string m_callId;
        protected string m_localTag;
        protected string m_remoteTag;
        protected SIPRequest m_ackRequest;                  // ACK request for INVITE transactions.
        protected SIPEndPoint m_ackRequestIPEndPoint;       // Socket the ACK request was sent to.
        protected SIPRequest m_prackRequest;                // PRACK request for provisional INVITE transaction responses.
        protected SIPEndPoint m_prackRequestIPEndPoint;     // Socket the PRACK request was sent to.

        public SIPURI TransactionRequestURI
        {
            get { return m_transactionRequest.URI; }
        }
        public SIPUserField TransactionRequestFrom
        {
            get { return m_transactionRequest.Header.From.FromUserField; }
        }

        /// <summary>
        /// If not null this value is where ALL transaction requests should be sent to.
        /// </summary>
        public SIPEndPoint OutboundProxy;
        public SIPCDR CDR;

        private SIPTransactionStatesEnum m_transactionState = SIPTransactionStatesEnum.Calling;
        public SIPTransactionStatesEnum TransactionState
        {
            get { return m_transactionState; }
        }

        protected SIPRequest m_transactionRequest;          // This is the request which started the transaction and on which it is based.
        public SIPRequest TransactionRequest
        {
            get { return m_transactionRequest; }
        }

        /// <summary>
        /// The most recent provisoinal response that was requested to be sent. If reliable provisional responses
        /// are being used then this response needs to be sent reliably in the same manner as the final response.
        /// </summary>
        public SIPResponse ReliableProvisionalResponse { get; private set; }

        public int RSeq { get; private set; } = 0;

        protected SIPResponse m_transactionFinalResponse;

        /// <summary>
        /// This is the final response being sent by a UAS transaction or the one received by a UAC one.
        /// </summary>
        public SIPResponse TransactionFinalResponse
        {
            get { return m_transactionFinalResponse; }
        }

        /// <summary>
        /// If am INVITE transaction client indicates RFC3262 support in the Require or Supported header we'll deliver reliable 
        /// provisional responses.
        /// </summary> 
        protected bool PrackSupported = false;

        // These are the events that will normally be required by upper level transaction users such as registration or call agents.
        protected event SIPTransactionRequestReceivedDelegate TransactionRequestReceived;
        protected event SIPTransactionResponseReceivedDelegate TransactionInformationResponseReceived;
        protected event SIPTransactionResponseReceivedDelegate TransactionFinalResponseReceived;
        protected event SIPTransactionTimedOutDelegate TransactionTimedOut;

        // These events are normally only used for housekeeping such as retransmits on ACK's.
        protected event SIPTransactionResponseReceivedDelegate TransactionDuplicateResponse;
        protected event SIPTransactionRequestRetransmitDelegate TransactionRequestRetransmit;

        // Events that don't affect the transaction processing, i.e. used for logging/tracing.
        public event SIPTransactionStateChangeDelegate TransactionStateChanged;
        public event SIPTransactionTraceMessageDelegate TransactionTraceMessage;

        public event SIPTransactionRemovedDelegate TransactionRemoved;       // This is called just before the SIPTransaction is expired and is to let consumer classes know to remove their event handlers to prevent memory leaks.

        private SIPTransport m_sipTransport;

        /// <summary>
        /// Creates a new SIP transaction and adds it to the list of in progress transactions.
        /// </summary>
        /// <param name="sipTransport">The SIP Transport layer that is to be used with the transaction.</param>
        /// <param name="transactionRequest">The SIP Request on which the transaction is based.</param>
        protected SIPTransaction(
            SIPTransport sipTransport,
            SIPRequest transactionRequest,
            SIPEndPoint outboundProxy)
        {
            try
            {
                if (sipTransport == null)
                {
                    throw new ArgumentNullException("A SIPTransport object is required when creating a SIPTransaction.");
                }
                else if (transactionRequest == null)
                {
                    throw new ArgumentNullException("A SIPRequest object must be supplied when creating a SIPTransaction.");
                }
                else if (transactionRequest.Header.Vias.TopViaHeader == null)
                {
                    throw new ArgumentNullException("The SIP request must have a Via header when creating a SIPTransaction.");
                }

                m_sipTransport = sipTransport;
                m_transactionId = GetRequestTransactionId(transactionRequest.Header.Vias.TopViaHeader.Branch, transactionRequest.Header.CSeqMethod);
                HasTimedOut = false;

                m_transactionRequest = transactionRequest;
                m_branchId = transactionRequest.Header.Vias.TopViaHeader.Branch;
                m_callId = transactionRequest.Header.CallId;
                m_sentBy = transactionRequest.Header.Vias.TopViaHeader.ContactAddress;
                OutboundProxy = outboundProxy;

                if (transactionRequest.Header.RequiredExtensions.Contains(SIPExtensions.Prack) ||
                    transactionRequest.Header.SupportedExtensions.Contains(SIPExtensions.Prack))
                {
                    PrackSupported = true;
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPTransaction (ctor). " + excp.Message);
                throw excp;
            }
        }

        public static string GetRequestTransactionId(string branchId, SIPMethodsEnum method)
        {
            return Crypto.GetSHAHashAsString(branchId + method.ToString());
        }

        public void GotRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            FireTransactionTraceMessage($"Transaction received Request {localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");
            TransactionRequestReceived?.Invoke(localSIPEndPoint, remoteEndPoint, this, sipRequest);
        }

        public void GotResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            if (TransactionState == SIPTransactionStatesEnum.Completed || TransactionState == SIPTransactionStatesEnum.Confirmed)
            {
                FireTransactionTraceMessage($"Transaction received duplicate response {localSIPEndPoint.ToString()}<-{remoteEndPoint}: {sipResponse.ShortDescription}");

                if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.INVITE)
                {
                    if (sipResponse.StatusCode > 100 && sipResponse.StatusCode <= 199)
                    {
                        ResendPrackRequest();
                    }
                    else
                    {
                        ResendAckRequest();
                    }
                }

                TransactionDuplicateResponse?.Invoke(localSIPEndPoint, remoteEndPoint, this, sipResponse);
            }
            else
            {
                FireTransactionTraceMessage($"Transaction received Response {localSIPEndPoint.ToString()}<-{remoteEndPoint}: {sipResponse.ShortDescription}");

                if (sipResponse.StatusCode >= 100 && sipResponse.StatusCode <= 199)
                {
                    UpdateTransactionState(SIPTransactionStatesEnum.Proceeding);
                    TransactionInformationResponseReceived(localSIPEndPoint, remoteEndPoint, this, sipResponse);
                }
                else
                {
                    m_transactionFinalResponse = sipResponse;
                    UpdateTransactionState(SIPTransactionStatesEnum.Completed);
                    TransactionFinalResponseReceived(localSIPEndPoint, remoteEndPoint, this, sipResponse);
                }
            }
        }

        private void UpdateTransactionState(SIPTransactionStatesEnum transactionState)
        {
            m_transactionState = transactionState;

            if (transactionState == SIPTransactionStatesEnum.Confirmed || transactionState == SIPTransactionStatesEnum.Terminated || transactionState == SIPTransactionStatesEnum.Cancelled)
            {
                DeliveryPending = false;
            }
            else if (transactionState == SIPTransactionStatesEnum.Completed)
            {
                CompletedAt = DateTime.Now;
            }

            if (TransactionStateChanged != null)
            {
                FireTransactionStateChangedEvent();
            }
        }

        public virtual void SendFinalResponse(SIPResponse finalResponse)
        {
            m_transactionFinalResponse = finalResponse;
            UpdateTransactionState(SIPTransactionStatesEnum.Completed);
            string viaAddress = finalResponse.Header.Vias.TopViaHeader.ReceivedFromAddress;

            if (TransactionType == SIPTransactionTypesEnum.InviteServer)
            {
                FireTransactionTraceMessage($"Transaction send final response reliable {finalResponse.ShortDescription}");
                m_sipTransport.SendReliable(this);
            }
            else
            {
                FireTransactionTraceMessage($"Transaction send final response {finalResponse.ShortDescription}");
                m_sipTransport.SendResponse(finalResponse);
            }
        }

        public virtual void SendProvisionalResponse(SIPResponse sipResponse)
        {
            FireTransactionTraceMessage($"Transaction send info response (is reliable {PrackSupported}) {sipResponse.ShortDescription}");

            if (sipResponse.StatusCode == 100)
            {
                UpdateTransactionState(SIPTransactionStatesEnum.Trying);
                m_sipTransport.SendResponse(sipResponse);
            }
            else if (sipResponse.StatusCode > 100 && sipResponse.StatusCode <= 199)
            {
                UpdateTransactionState(SIPTransactionStatesEnum.Proceeding);

                if (PrackSupported == true)
                {
                    if (RSeq == 0)
                    {
                        RSeq = Crypto.GetRandomInt(1, Int32.MaxValue / 2 - 1);
                    }
                    else
                    {
                        RSeq++;
                    }

                    sipResponse.Header.RSeq = RSeq;
                    sipResponse.Header.Require += SIPExtensionHeaders.PRACK;

                    // If reliable provisional responses are supported then need to send this response reliably.
                    if (ReliableProvisionalResponse != null)
                    {
                        logger.LogWarning("A new reliable provisional response is being sent but the previous one was not yet acknowledged.");
                    }

                    ReliableProvisionalResponse = sipResponse;
                    m_sipTransport.SendReliable(this);
                }
                else
                {
                    m_sipTransport.SendResponse(sipResponse);
                }
            }
        }

        protected async Task SendRequest(SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
            FireTransactionTraceMessage($"Transaction send request {sipRequest.StatusLine}");

            if (sipRequest.Method == SIPMethodsEnum.ACK)
            {
                m_ackRequest = sipRequest;
                m_ackRequestIPEndPoint = dstEndPoint;
            }
            else if (sipRequest.Method == SIPMethodsEnum.PRACK)
            {
                m_prackRequest = sipRequest;
                m_prackRequestIPEndPoint = dstEndPoint;
            }

            await m_sipTransport.SendRequestAsync(dstEndPoint, sipRequest);
        }

        public async void SendRequest(SIPRequest sipRequest)
        {
            var lookupResult = m_sipTransport.GetRequestEndPoint(sipRequest, OutboundProxy, true);

            if (lookupResult != null && lookupResult.LookupError == null)
            {
                await SendRequest(lookupResult.GetSIPEndPoint(), sipRequest);
            }
            else
            {
                throw new ApplicationException("Could not send Transaction Request as request end point could not be determined.\r\n" + sipRequest.ToString());
            }
        }

        public void SendReliableRequest()
        {
            FireTransactionTraceMessage($"Transaction send request reliable {TransactionRequest.StatusLine}");

            if (TransactionType == SIPTransactionTypesEnum.InviteClient && this.TransactionRequest.Method == SIPMethodsEnum.INVITE)
            {
                UpdateTransactionState(SIPTransactionStatesEnum.Calling);
            }

            m_sipTransport.SendReliable(this);
        }

        protected SIPResponse GetInfoResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum sipResponseCode)
        {
            try
            {
                SIPResponse informationalResponse = new SIPResponse(sipResponseCode, null);
                informationalResponse.SetSendFromHints(sipRequest.LocalSIPEndPoint);

                SIPHeader requestHeader = sipRequest.Header;
                informationalResponse.Header = new SIPHeader(requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);
                informationalResponse.Header.CSeqMethod = requestHeader.CSeqMethod;
                informationalResponse.Header.Vias = requestHeader.Vias;
                informationalResponse.Header.MaxForwards = Int32.MinValue;
                informationalResponse.Header.Timestamp = requestHeader.Timestamp;

                return informationalResponse;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GetInformationalResponse. " + excp.Message);
                throw excp;
            }
        }

        internal void RemoveEventHandlers()
        {
            // Remove all event handlers.
            TransactionRequestReceived = null;
            TransactionInformationResponseReceived = null;
            TransactionFinalResponseReceived = null;
            TransactionTimedOut = null;
            TransactionDuplicateResponse = null;
            TransactionRequestRetransmit = null;
            TransactionStateChanged = null;
            TransactionTraceMessage = null;
            TransactionRemoved = null;
        }

        public void ACKReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            UpdateTransactionState(SIPTransactionStatesEnum.Confirmed);
        }

        /// <summary>
        /// PRACK request received to acknowledge the last provisional response that was sent.
        /// </summary>
        /// <param name="localSIPEndPoint">The SIP socket the request was received on.</param>
        /// <param name="remoteEndPoint">The remote SIP socket the request originated from.</param>
        /// <param name="sipRequest">The PRACK request.</param>
        public void PRACKReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (m_transactionState == SIPTransactionStatesEnum.Proceeding && RSeq == sipRequest.Header.RAckRSeq)
            {
                logger.LogDebug("PRACK request matched the current outstanding provisional response, setting as delivered.");
                DeliveryPending = false;
            }

            // We don't keep track of previous provisional response ACK's so always return OK if the request matched the 
            // transaction and got this far.
            var prackResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
            m_sipTransport.SendResponse(prackResponse);
        }

        private void ResendAckRequest()
        {
            try
            {
                if (m_ackRequest != null)
                {
                    SendRequest(m_ackRequest);
                    AckRetransmits += 1;
                    LastTransmit = DateTime.Now;
                }
                else
                {
                    logger.LogWarning("An ACK retransmit was required but there was no stored ACK request to send.");
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception ResendAckRequest. {excp.Message}");
            }
        }

        private void ResendPrackRequest()
        {
            try
            {
                if (m_prackRequest != null)
                {
                    SendRequest(m_prackRequest);
                    PrackRetransmits += 1;
                    LastTransmit = DateTime.Now;
                }
                else
                {
                    logger.LogWarning("A PRACK retransmit was required but there was no stored PRACK request to send.");
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception ResendPrackRequest. {excp.Message}");
            }
        }

        protected void Cancel()
        {
            UpdateTransactionState(SIPTransactionStatesEnum.Cancelled);
        }

        public void RequestRetransmit()
        {
            if (TransactionRequestRetransmit != null)
            {
                try
                {
                    TransactionRequestRetransmit(this, this.TransactionRequest, this.Retransmits);
                }
                catch (Exception excp)
                {
                    logger.LogError("Exception TransactionRequestRetransmit. " + excp.Message);
                }
            }

            //FireTransactionTraceMessage("Send Request retransmit " + Retransmits + " " + LocalSIPEndPoint.ToString() + "->" + this.RemoteEndPoint + m_crLF + this.TransactionRequest.ToString());
            FireTransactionTraceMessage($"Transaction send request retransmit {Retransmits} {this.TransactionRequest.StatusLine}");
        }

        public void OnRetransmitFinalResponse()
        {
            FireTransactionTraceMessage($"Transaction send response retransmit {Retransmits} {this.TransactionFinalResponse.ShortDescription}");
        }

        public void OnRetransmitProvisionalResponse()
        {
            FireTransactionTraceMessage($"Transaction send provisional response retransmit {Retransmits} {this.ReliableProvisionalResponse.ShortDescription}");
        }

        public void OnTimedOutProvisionalResponse()
        {
            FireTransactionTraceMessage($"Transaction provisional response delivery timed out {this.ReliableProvisionalResponse.ShortDescription}");
        }

        public void FireTransactionTimedOut()
        {
            try
            {
                TransactionTimedOut?.Invoke(this);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireTransactionTimedOut (" + TransactionId + " " + TransactionRequest.URI.ToString() + ", callid=" + TransactionRequest.Header.CallId + ", " + this.GetType().ToString() + "). " + excp.Message);
            }
        }

        public void FireTransactionRemoved()
        {
            try
            {
                TransactionRemoved?.Invoke(this);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireTransactionRemoved. " + excp.Message);
            }
        }

        private void FireTransactionStateChangedEvent()
        {
            FireTransactionTraceMessage($"Transaction state changed to {this.TransactionState}.");

            try
            {
                TransactionStateChanged?.Invoke(this);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireTransactionStateChangedEvent. " + excp.Message);
            }
        }

        private void FireTransactionTraceMessage(string message)
        {
            try
            {
                TransactionTraceMessage?.Invoke(this, message);
            }
            catch (Exception excp)
            {
                logger.LogError("Exception FireTransactionTraceMessage. " + excp.Message);
            }
        }
    }
}
