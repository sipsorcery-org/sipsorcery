//-----------------------------------------------------------------------------
// Filename: SIPTransaction.cs
//
// Description: SIP Transaction.
// 
// History:
// 14 Feb 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.SIP
{
    public enum SIPTransactionStatesEnum
    {
        //Unknown = 0,
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
        Invite = 1,
        NonInvite = 2,
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
    /// 
    /// [RFC 3261 12 Dialogs] (Note: I've gotten a bit mixed up between dialogs and
    /// transactions here, AC).
    /// A dialog ID is also associated with all responses and with any
    /// request that contains a tag in the To field.  The rules for computing
    /// the dialog ID of a message depend on whether the SIP element is a UAC
    /// or UAS.  For a UAC, the Call-ID value of the dialog ID is set to the
    /// Call-ID of the message, the remote tag is set to the tag in the To
    /// field of the message, and the local tag is set to the tag in the From
    /// field of the message (these rules apply to both requests and
    /// responses).  As one would expect for a UAS, the Call-ID value of the
    /// dialog ID is set to the Call-ID of the message, the remote tag is set
    /// to the tag in the From field of the message, and the local tag is set
    /// to the tag in the To field of the message.
    /// 
    /// Notes (Not too sure on matching requests to transactions AC 09 Feb 2008):
    /// - Matching a response to a transaction can rely on the branchid in the Via header.
    /// - Matching a request to a transaction can rely on the branchid EXCEPT for an
    ///   ACK for a 2xx final response which is a new transaction and has a new branch ID.
    /// </note>
    public class SIPTransaction
    {
        protected static ILog logger = AssemblyState.logger;

        protected static readonly int m_t1 = SIPTimings.T1;                     // SIP Timer T1 in milliseconds.
        protected static readonly int m_t6 = SIPTimings.T6;                     // SIP Timer T1 in milliseconds.
        protected static readonly int m_maxRingTime = SIPTimings.MAX_RING_TIME; // Max time an INVITE will be left ringing for (typically 10 mins).    

        private static string m_crLF = SIPConstants.CRLF;

        public int Retransmits = 0;
        public int AckRetransmits = 0;
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
        public DateTime CompletedAt = DateTime.Now;     // For INVITEs thiis the time they recieved the final response and is used to calculate the time they expie as T6 after this.
        public DateTime TimedOutAt;                     // If the transaction times out this holds the value it timed out at.

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

        public SIPURI TransactionRequestURI
        {
            get { return m_transactionRequest.URI; }
        }
        public SIPUserField TransactionRequestFrom
        {
            get { return m_transactionRequest.Header.From.FromUserField; }
        }
        public SIPEndPoint RemoteEndPoint;                  // The remote socket that caused the transaction to be created or the socket a newly created transaction request was sent to.             
        public SIPEndPoint LocalSIPEndPoint;                // The local SIP endpoint the remote request was received on the if created by this stack the local SIP end point used to send the transaction.
        public SIPEndPoint OutboundProxy;                   // If not null this value is where ALL transaction requests should be sent to.
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

        protected SIPResponse m_transactionFinalResponse;   // This is the final response being sent by a UAS transaction or the one received by a UAC one.
        public SIPResponse TransactionFinalResponse
        {
            get { return m_transactionFinalResponse; }
        }

        // These are the events that will normally be required by upper level transaction users such as registration or call agents.
        protected event SIPTransactionRequestReceivedDelegate TransactionRequestReceived;
        //protected event SIPTransactionAuthenticationRequiredDelegate TransactionAuthenticationRequired;
        protected event SIPTransactionResponseReceivedDelegate TransactionInformationResponseReceived;
        protected event SIPTransactionResponseReceivedDelegate TransactionFinalResponseReceived;
        protected event SIPTransactionTimedOutDelegate TransactionTimedOut;

        // These events are normally only used for housekeeping such as retransmits on ACK's.
        protected event SIPTransactionResponseReceivedDelegate TransactionDuplicateResponse;
        protected event SIPTransactionRequestRetransmitDelegate TransactionRequestRetransmit;
        protected event SIPTransactionResponseRetransmitDelegate TransactionResponseRetransmit;

        // Events that don't affect the transaction processing, i.e. used for logging/tracing.
        public event SIPTransactionStateChangeDelegate TransactionStateChanged;
        public event SIPTransactionTraceMessageDelegate TransactionTraceMessage;

        public event SIPTransactionRemovedDelegate TransactionRemoved;       // This is called just before the SIPTransaction is expired and is to let consumer classes know to remove their event handlers to prevent memory leaks.

        public Int64 TransactionsCreated = 0;
        public Int64 TransactionsDestroyed = 0;

        private SIPTransport m_sipTransport;

        /// <summary>
        /// Creates a new SIP transaction and adds it to the list of in progress transactions.
        /// </summary>
        /// <param name="sipTransport">The SIP Transport layer that is to be used with the transaction.</param>
        /// <param name="transactionRequest">The SIP Request on which the transaction is based.</param>
        /// <param name="dstEndPoint">The socket the at the remote end of the transaction and which transaction messages will be sent to.</param>
        /// <param name="localSIPEndPoint">The socket that should be used as the send from socket for communications on this transaction. Typically this will
        /// be the socket the initial request was received on.</param>
        protected SIPTransaction(
            SIPTransport sipTransport,
            SIPRequest transactionRequest,
            SIPEndPoint dstEndPoint,
            SIPEndPoint localSIPEndPoint,
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
                /*else if (dstEndPoint == null && outboundProxy == null)
                {
                    throw new ArgumentNullException("The remote SIP end point or outbound proxy must be set when creating a SIPTransaction.");
                }*/
                else if (localSIPEndPoint == null)
                {
                    throw new ArgumentNullException("The local SIP end point must be set when creating a SIPTransaction.");
                }
                else if (transactionRequest.Header.Vias.TopViaHeader == null)
                {
                    throw new ArgumentNullException("The SIP request must have a Via header when creating a SIPTransaction.");
                }

                TransactionsCreated++;

                m_sipTransport = sipTransport;
                m_transactionId = GetRequestTransactionId(transactionRequest.Header.Vias.TopViaHeader.Branch, transactionRequest.Header.CSeqMethod);
                HasTimedOut = false;

                m_transactionRequest = transactionRequest;
                m_branchId = transactionRequest.Header.Vias.TopViaHeader.Branch;
                m_callId = transactionRequest.Header.CallId;
                m_sentBy = transactionRequest.Header.Vias.TopViaHeader.ContactAddress;
                RemoteEndPoint = dstEndPoint;
                LocalSIPEndPoint = localSIPEndPoint;
                OutboundProxy = outboundProxy;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPTransaction (ctor). " + excp.Message);
                throw excp;
            }
        }

        public static string GetRequestTransactionId(string branchId, SIPMethodsEnum method)
        {
            return Crypto.GetSHAHashAsString(branchId + method.ToString());
        }

        public void GotRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            FireTransactionTraceMessage("Received Request " + localSIPEndPoint.ToString() + "<-" + remoteEndPoint.ToString() + m_crLF + sipRequest.ToString());

            if (TransactionRequestReceived != null)
            {
                TransactionRequestReceived(localSIPEndPoint, remoteEndPoint, this, sipRequest);
            }
        }

        public void GotResponse(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            if (TransactionState == SIPTransactionStatesEnum.Completed || TransactionState == SIPTransactionStatesEnum.Confirmed)
            {
                FireTransactionTraceMessage("Received Duplicate Response " + localSIPEndPoint.ToString() + "<-" + remoteEndPoint + m_crLF + sipResponse.ToString());

                if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.INVITE)
                {
                    if (sipResponse.StatusCode >= 100 && sipResponse.StatusCode <= 199)
                    {
                        // Ignore info response on completed transaction.
                    }
                    else
                    {
                        ResendAckRequest();
                    }
                }

                if (TransactionDuplicateResponse != null)
                {
                    TransactionDuplicateResponse(localSIPEndPoint, remoteEndPoint, this, sipResponse);
                }
            }
            else
            {
                FireTransactionTraceMessage("Received Response " + localSIPEndPoint.ToString() + "<-" + remoteEndPoint + m_crLF + sipResponse.ToString());

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

            if (TransactionType == SIPTransactionTypesEnum.Invite)
            {
                FireTransactionTraceMessage("Send Final Response Reliable " + LocalSIPEndPoint.ToString() + "->" + viaAddress + m_crLF + finalResponse.ToString());
                m_sipTransport.SendSIPReliable(this);
            }
            else
            {
                FireTransactionTraceMessage("Send Final Response " + LocalSIPEndPoint.ToString() + "->" + viaAddress + m_crLF + finalResponse.ToString());
                m_sipTransport.SendResponse(finalResponse);
            }
        }

        public virtual void SendInformationalResponse(SIPResponse sipResponse)
        {
            FireTransactionTraceMessage("Send Info Response " + LocalSIPEndPoint.ToString() + "->" + this.RemoteEndPoint + m_crLF + sipResponse.ToString());

            if (sipResponse.StatusCode == 100)
            {
                UpdateTransactionState(SIPTransactionStatesEnum.Trying);
            }
            else if (sipResponse.StatusCode > 100 && sipResponse.StatusCode <= 199)
            {
                UpdateTransactionState(SIPTransactionStatesEnum.Proceeding);
            }

            m_sipTransport.SendResponse(sipResponse);
        }

        public void RetransmitFinalResponse()
        {
            try
            {
                if (TransactionFinalResponse != null && TransactionState != SIPTransactionStatesEnum.Confirmed)
                {
                    m_sipTransport.SendResponse(TransactionFinalResponse);
                    Retransmits += 1;
                    LastTransmit = DateTime.Now;
                    ResponseRetransmit();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RetransmitFinalResponse. " + excp.Message);
            }
        }

        public void SendRequest(SIPEndPoint dstEndPoint, SIPRequest sipRequest)
        {
            FireTransactionTraceMessage("Send Request " + LocalSIPEndPoint.ToString() + "->" + dstEndPoint + m_crLF + sipRequest.ToString());

            if (sipRequest.Method == SIPMethodsEnum.ACK)
            {
                m_ackRequest = sipRequest;
                m_ackRequestIPEndPoint = dstEndPoint;
            }

            m_sipTransport.SendRequest(dstEndPoint, sipRequest);
        }

        public void SendRequest(SIPRequest sipRequest)
        {
            SIPEndPoint dstEndPoint = m_sipTransport.GetRequestEndPoint(sipRequest, OutboundProxy, true).GetSIPEndPoint();

            if (dstEndPoint != null)
            {
                FireTransactionTraceMessage("Send Request " + LocalSIPEndPoint.ToString() + "->" + dstEndPoint.ToString() + m_crLF + sipRequest.ToString());

                if (sipRequest.Method == SIPMethodsEnum.ACK)
                {
                    m_ackRequest = sipRequest;
                    m_ackRequestIPEndPoint = dstEndPoint;
                }
                else
                {
                    RemoteEndPoint = dstEndPoint;
                }

                m_sipTransport.SendRequest(dstEndPoint, sipRequest);
            }
            else
            {
                throw new ApplicationException("Could not send Transaction Request as request end point could not be determined.\r\n" + sipRequest.ToString());
            }
        }

        public void SendReliableRequest()
        {
            FireTransactionTraceMessage("Send Request reliable " + LocalSIPEndPoint.ToString() + "->" + RemoteEndPoint + m_crLF + TransactionRequest.ToString());

            if (TransactionType == SIPTransactionTypesEnum.Invite && this.TransactionRequest.Method == SIPMethodsEnum.INVITE)
            {
                UpdateTransactionState(SIPTransactionStatesEnum.Calling);
            }

            m_sipTransport.SendSIPReliable(this);
        }

        protected SIPResponse GetInfoResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum sipResponseCode)
        {
            try
            {
                SIPResponse informationalResponse = new SIPResponse(sipResponseCode, null, sipRequest.LocalSIPEndPoint);

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
                logger.Error("Exception GetInformationalResponse. " + excp.Message);
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
            TransactionResponseRetransmit = null;
            TransactionStateChanged = null;
            TransactionTraceMessage = null;
            TransactionRemoved = null;
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
                    logger.Error("Exception TransactionRequestRetransmit. " + excp.Message);
                }
            }

            FireTransactionTraceMessage("Send Request retransmit " + Retransmits + " " + LocalSIPEndPoint.ToString() + "->" + this.RemoteEndPoint + m_crLF + this.TransactionRequest.ToString());
        }

        private void ResponseRetransmit()
        {
            if (TransactionResponseRetransmit != null)
            {
                try
                {
                    TransactionResponseRetransmit(this, this.TransactionFinalResponse, this.Retransmits);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception TransactionResponseRetransmit. " + excp.Message);
                }
            }

            FireTransactionTraceMessage("Send Response retransmit " + LocalSIPEndPoint.ToString() + "->" + this.RemoteEndPoint + m_crLF + this.TransactionFinalResponse.ToString());
        }

        public void ACKReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            UpdateTransactionState(SIPTransactionStatesEnum.Confirmed);
        }

        public void ResendAckRequest()
        {
            if (m_ackRequest != null)
            {
                SendRequest(m_ackRequest);
                AckRetransmits += 1;
                LastTransmit = DateTime.Now;
                //RequestRetransmit();
            }
            else
            {
                logger.Warn("An ACK retransmit was required but there was no stored ACK request to send.");
            }
        }

        public void FireTransactionTimedOut()
        {
            try
            {
                if (TransactionTimedOut != null)
                {
                    TransactionTimedOut(this);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireTransactionTimedOut (" + TransactionId + " " + TransactionRequest.URI.ToString() + ", callid=" + TransactionRequest.Header.CallId + ", " + this.GetType().ToString() + "). " + excp.Message);
            }
        }

        public void FireTransactionRemoved()
        {
            try
            {
                if (TransactionRemoved != null)
                {
                    TransactionRemoved(this);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireTransactionRemoved. " + excp.Message);
            }
        }

        protected void Cancel()
        {
            UpdateTransactionState(SIPTransactionStatesEnum.Cancelled);
        }

        private void FireTransactionStateChangedEvent()
        {
            FireTransactionTraceMessage("Transaction state changed to " + this.TransactionState + ".");

            if (TransactionStateChanged != null)
            {
                try
                {
                    TransactionStateChanged(this);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireTransactionStateChangedEvent. " + excp.Message);
                }
            }
        }

        private void FireTransactionTraceMessage(string message)
        {
            if (TransactionTraceMessage != null)
            {
                try
                {
                    TransactionTraceMessage(this, message);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireTransactionTraceMessage. " + excp.Message);
                }
            }
        }

        ~SIPTransaction()
        {
            TransactionsDestroyed++;
        }

        #region Unit testing.

#if UNITTEST

		[TestFixture]
		public class SIPTransactionUnitTest
		{
            private class MockSIPDNSManager
            {
                public static SIPDNSLookupResult Resolve(SIPURI sipURI, bool async)
                {
                    // This assumes the input SIP URI has an IP address as the host!
                    return new SIPDNSLookupResult(sipURI, new SIPEndPoint(IPSocket.ParseSocketString(sipURI.Host)));
                }
            }

            protected static readonly string m_CRLF = SIPConstants.CRLF; 
            
            [TestFixtureSetUp]
			public void Init()
			{}
	
			[TestFixtureTearDown]
			public void Dispose()
			{}
			
			[Test]
			public void CreateTransactionUnitTest()
			{
				Console.WriteLine(System.Reflection.MethodBase.GetCurrentMethod().Name);

                string sipRequestStr =
                    "INVITE sip:023434211@213.200.94.182;switchtag=902888 SIP/2.0" + m_CRLF +
                    "Record-Route: <sip:2.3.4.5;ftag=9307C640-33C;lr=on>" + m_CRLF +
                    "Via: SIP/2.0/UDP  5.6.7.2:5060" + m_CRLF +
                    "Via: SIP/2.0/UDP 1.2.3.4;branch=z9hG4bKa7ac.2bfad091.0" + m_CRLF +
                    "From: \"unknown\" <sip:00.000.00.0>;tag=9307C640-33C" + m_CRLF +
                    "To: <sip:0113001211@82.209.165.194>" + m_CRLF +
                    "Date: Thu, 21 Feb 2008 01:46:30 GMT" + m_CRLF +
                    "Call-ID: A8706191-DF5511DC-B886ED7B-395C3F7E" + m_CRLF +
                    "Supported: timer,100rel" + m_CRLF +
                    "Min-SE:  1800" + m_CRLF +
                    "Cisco-Guid: 2825897321-3746894300-3095653755-962346878" + m_CRLF +
                    "User-Agent: Cisco-SIPGateway/IOS-12.x" + m_CRLF +
                    "Allow: INVITE, OPTIONS, BYE, CANCEL, ACK, PRACK, COMET, REFER, SUBSCRIBE, NOTIFY, INFO" + m_CRLF +
                    "CSeq: 101 INVITE" + m_CRLF +
                    "Max-Forwards: 5" + m_CRLF +
                    "Timestamp: 1203558390" + m_CRLF +
                    "Contact: <sip:1.2.3.4:5060>" + m_CRLF +
                    "Expires: 180" + m_CRLF +
                    "Allow-Events: telephone-event" + m_CRLF +
                    "Content-Type: application/sdp" + m_CRLF +
                    "Content-Length: 370" + m_CRLF +
                     m_CRLF +
                    "v=0" + m_CRLF +
                    "o=CiscoSystemsSIP-GW-UserAgent 9312 7567 IN IP4 00.00.00.0" + m_CRLF +
                    "s=SIP Call" + m_CRLF +
                    "c=IN IP4 00.000.00.0" + m_CRLF +
                    "t=0 0" + m_CRLF +
                    "m=audio 16434 RTP/AVP 8 0 4 18 3 101" + m_CRLF +
                    "c=IN IP4 00.000.00.0" + m_CRLF +
                    "a=rtpmap:8 PCMA/8000" + m_CRLF +
                    "a=rtpmap:0 PCMU/8000" + m_CRLF +
                    "a=rtpmap:4 G723/8000" + m_CRLF +
                    "a=fmtp:4 annexa=no" + m_CRLF +
                    "a=rtpmap:18 G729/8000" + m_CRLF +
                    "a=fmtp:18 annexb=no" + m_CRLF +
                    "a=rtpmap:3 GSM/8000" + m_CRLF +
                    "a=rtpmap:101 telepho";

                SIPRequest request = SIPRequest.ParseSIPRequest(sipRequestStr);
                SIPTransactionEngine transactionEngine = new SIPTransactionEngine();
                SIPTransport sipTransport = new SIPTransport(MockSIPDNSManager.Resolve, transactionEngine);
                SIPEndPoint dummySIPEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 1234));
                SIPTransaction transaction = sipTransport.CreateUACTransaction(request, dummySIPEndPoint, dummySIPEndPoint, null);

                Assert.IsTrue(transaction.TransactionRequest.URI.ToString() == "sip:023434211@213.200.94.182;switchtag=902888", "Transaction request URI was incorrect.");
			}
        }

#endif

        #endregion
    }
}
