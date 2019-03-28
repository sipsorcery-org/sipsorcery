//-----------------------------------------------------------------------------
// Filename: UACInviteTransaction.cs
//
// Description: SIP Transaction that implements UAC (Sser Agent Client) functionality for
// an INVITE transaction.
// 
// History:
// 21 Nov 2006	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2012 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Pty Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Pty Ltd. 
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
using System.Reflection;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP
{
    /// <summary>
    /// SIP transaction that initiates a call to a SIP User Agent Server. This transaction processes outgoing calls SENT by the application.
    /// </summary>
    public class UACInviteTransaction : SIPTransaction
    {
        public event SIPTransactionResponseReceivedDelegate UACInviteTransactionInformationResponseReceived;
        public event SIPTransactionResponseReceivedDelegate UACInviteTransactionFinalResponseReceived;
        public event SIPTransactionTimedOutDelegate UACInviteTransactionTimedOut;

        private List<string> _customHeader;
        private bool _sendOkAckManually = false;

        /// <param name="sendOkAckManually">If set an ACK request for the 2xx response will NOT be sent and it will be up to the application to explicitly call the SendACK request.</param>
        internal UACInviteTransaction(SIPTransport sipTransport, SIPRequest sipRequest, SIPEndPoint dstEndPoint, SIPEndPoint localSIPEndPoint, SIPEndPoint outboundProxy, List<string> customHeader, bool sendOkAckManually = false)
            : base(sipTransport, sipRequest, dstEndPoint, localSIPEndPoint, outboundProxy)
        {
            TransactionType = SIPTransactionTypesEnum.Invite;
            m_localTag = sipRequest.Header.From.FromTag;
            SIPEndPoint localEP = SIPEndPoint.TryParse(sipRequest.Header.ProxySendFrom) ?? localSIPEndPoint;
            CDR = new SIPCDR(SIPCallDirection.Out, sipRequest.URI, sipRequest.Header.From, sipRequest.Header.CallId, localEP, dstEndPoint);
            _customHeader = customHeader;
            _sendOkAckManually = sendOkAckManually;

            TransactionFinalResponseReceived += UACInviteTransaction_TransactionFinalResponseReceived;
            TransactionInformationResponseReceived += UACInviteTransaction_TransactionInformationResponseReceived;
            TransactionTimedOut += UACInviteTransaction_TransactionTimedOut;
            TransactionRequestReceived += UACInviteTransaction_TransactionRequestReceived;
            TransactionRemoved += UACInviteTransaction_TransactionRemoved;
        }

        private void UACInviteTransaction_TransactionRemoved(SIPTransaction transaction)
        {
            // Remove event handlers.
            UACInviteTransactionInformationResponseReceived = null;
            UACInviteTransactionFinalResponseReceived = null;
            UACInviteTransactionTimedOut = null;
            CDR = null;
        }

        public void SendInviteRequest(SIPEndPoint dstEndPoint, SIPRequest inviteRequest)
        {
            this.RemoteEndPoint = dstEndPoint;
            base.SendReliableRequest();
        }

        private void UACInviteTransaction_TransactionRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            logger.Warn("UACInviteTransaction received unexpected request, " + sipRequest.Method + " from " + remoteEndPoint.ToString() + ", ignoring.");
        }

        private void UACInviteTransaction_TransactionTimedOut(SIPTransaction sipTransaction)
        {
            try
            {
                if (UACInviteTransactionTimedOut != null)
                {
                    UACInviteTransactionTimedOut(sipTransaction);
                }

                if (CDR != null)
                {
                    CDR.TimedOut();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception UACInviteTransaction_TransactionTimedOut. " + excp.Message);
                throw;
            }
        }

        private void UACInviteTransaction_TransactionInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                if (UACInviteTransactionInformationResponseReceived != null)
                {
                    UACInviteTransactionInformationResponseReceived(localSIPEndPoint, remoteEndPoint, sipTransaction, sipResponse);
                }

                if (CDR != null)
                {
                    SIPEndPoint localEP = SIPEndPoint.TryParse(sipResponse.Header.ProxyReceivedOn) ?? localSIPEndPoint;
                    SIPEndPoint remoteEP = SIPEndPoint.TryParse(sipResponse.Header.ProxyReceivedFrom) ?? remoteEndPoint;
                    CDR.Progress(sipResponse.Status, sipResponse.ReasonPhrase, localEP, remoteEP);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception UACInviteTransaction_TransactionInformationResponseReceived. " + excp.Message);
            }
        }

        private void UACInviteTransaction_TransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                // BranchId for 2xx responses needs to be a new one, non-2xx final responses use same one as original request.
                if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode < 299)
                {
                    if (_sendOkAckManually == false)
                    {
                        Send2xxAckRequest(null, null);
                    }
                }
                else
                {
                    // ACK for non 2xx response is part of the INVITE transaction and gets routed to the same endpoint as the INVITE.
                    var ackRequest = GetInTransactionACKRequest(sipResponse, m_transactionRequest.URI, LocalSIPEndPoint);
                    base.SendRequest(sipResponse.RemoteSIPEndPoint, ackRequest);
                }

                if (UACInviteTransactionFinalResponseReceived != null)
                {
                    UACInviteTransactionFinalResponseReceived(localSIPEndPoint, remoteEndPoint, sipTransaction, sipResponse);
                }

                if (CDR != null)
                {
                    SIPEndPoint localEP = SIPEndPoint.TryParse(sipResponse.Header.ProxyReceivedOn) ?? localSIPEndPoint;
                    SIPEndPoint remoteEP = SIPEndPoint.TryParse(sipResponse.Header.ProxyReceivedFrom) ?? remoteEndPoint;
                    CDR.Answered(sipResponse.StatusCode, sipResponse.Status, sipResponse.ReasonPhrase, localEP, remoteEP);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception UACInviteTransaction_TransactionFinalResponseReceived. " + excp.Message);
            }
        }

        public void Send2xxAckRequest(string content, string contentType)
        {
            var sipResponse = m_transactionFinalResponse;

            if (sipResponse.Header.To != null)
            {
                m_remoteTag = sipResponse.Header.To.ToTag;
            }

            SIPURI ackURI = m_transactionRequest.URI;
            if (sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0)
            {
                ackURI = sipResponse.Header.Contact[0].ContactURI;
                // Don't mangle private contacts if there is a Record-Route header. If a proxy is putting private IP's in a Record-Route header that's its problem.
                if ((sipResponse.Header.RecordRoutes == null || sipResponse.Header.RecordRoutes.Length == 0)
                    && IPSocket.IsPrivateAddress(ackURI.Host) && !sipResponse.Header.ProxyReceivedFrom.IsNullOrBlank())
                {
                    // Setting the Proxy-ReceivedOn header is how an upstream proxy will let an agent know it should mangle the contact. 
                    SIPEndPoint remoteUASSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(sipResponse.Header.ProxyReceivedFrom);
                    ackURI.Host = remoteUASSIPEndPoint.GetIPEndPoint().ToString();
                }
            }

            // ACK for 2xx response needs to be a new transaction and gets routed based on SIP request fields.
            var ackRequest = GetNewTransactionACKRequest(sipResponse, ackURI, LocalSIPEndPoint);

            if(content.NotNullOrBlank())
            {
                ackRequest.Body = content;
                ackRequest.Header.ContentLength = ackRequest.Body.Length;
                ackRequest.Header.ContentType = contentType;
            }

            base.SendRequest(sipResponse.RemoteSIPEndPoint, ackRequest);
        }

        /// <summary>
        /// New transaction ACK requests are for 2xx responses, i.e. INVITE accepted and dialogue being created.
        /// </summary>
        /// <remarks>
        /// From RFC 3261 Chapter 17.1.1.3 - ACK for non-2xx final responses
        /// 
        /// IMPORTANT:
        /// an ACK for a non-2xx response will also have the same branch ID as the INVITE whose response it acknowledges.
        /// 
        /// The ACK request constructed by the client transaction MUST contain
        /// values for the Call-ID, From, and Request-URI that are equal to the
        /// values of those header fields in the request passed to the transport
        /// by the client transaction (call this the "original request").  The To
        /// header field in the ACK MUST equal the To header field in the
        /// response being acknowledged, and therefore will usually differ from
        /// the To header field in the original request by the addition of the
        /// tag parameter.  The ACK MUST contain a single Via header field, and
        /// this MUST be equal to the top Via header field of the original
        /// request.  The CSeq header field in the ACK MUST contain the same
        /// value for the sequence number as was present in the original request,
        /// but the method parameter MUST be equal to "ACK".
        ///
        /// If the INVITE request whose response is being acknowledged had Route
        /// header fields, those header fields MUST appear in the ACK.  This is
        /// to ensure that the ACK can be routed properly through any downstream
        /// stateless proxies.
        /// 
        /// From RFC 3261 Chapter 13.2.2.4 - ACK for 2xx final responses
        /// 
        /// IMPORTANT:
        /// an ACK for a 2xx final response is a new transaction and has a new branch ID.
        /// 
        /// The UAC core MUST generate an ACK request for each 2xx received from
        /// the transaction layer.  The header fields of the ACK are constructed
        /// in the same way as for any request sent within a dialog (see Section
        /// 12) with the exception of the CSeq and the header fields related to
        /// authentication.  The sequence number of the CSeq header field MUST be
        /// the same as the INVITE being acknowledged, but the CSeq method MUST
        /// be ACK.  The ACK MUST contain the same credentials as the INVITE.  If
        /// the 2xx contains an offer (based on the rules above), the ACK MUST
        /// carry an answer in its body.  If the offer in the 2xx response is not
        /// acceptable, the UAC core MUST generate a valid answer in the ACK and
        /// then send a BYE immediately.
        /// </remarks>
        private SIPRequest GetNewTransactionACKRequest(SIPResponse sipResponse, SIPURI ackURI, SIPEndPoint localSIPEndPoint)
        {
            SIPRequest ackRequest = new SIPRequest(SIPMethodsEnum.ACK, ackURI.ToString());
            ackRequest.LocalSIPEndPoint = localSIPEndPoint;

            SIPHeader header = new SIPHeader(TransactionRequest.Header.From, sipResponse.Header.To, sipResponse.Header.CSeq, sipResponse.Header.CallId);
            header.CSeqMethod = SIPMethodsEnum.ACK;
            header.AuthenticationHeader = TransactionRequest.Header.AuthenticationHeader;
            header.ProxySendFrom = base.TransactionRequest.Header.ProxySendFrom;

            // If the UAS supplies a desired Record-Route list use that first. Otherwise fall back to any Route list used in the original transaction.
            if (sipResponse.Header.RecordRoutes != null)
            {
                header.Routes = sipResponse.Header.RecordRoutes.Reversed();
            }
            else if(base.TransactionRequest.Header.Routes != null)
            {
                header.Routes = base.TransactionRequest.Header.Routes;
            }

            ackRequest.Header = header;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
            ackRequest.Header.Vias.PushViaHeader(viaHeader);

            AddCustomHeader(ackRequest);
            return ackRequest;
        }

        /// <summary>
        /// In transaction ACK requests are for non-2xx responses, i.e. INVITE rejected and no dialogue being created.
        /// </summary>
        private SIPRequest GetInTransactionACKRequest(SIPResponse sipResponse, SIPURI ackURI, SIPEndPoint localSIPEndPoint)
        {
            SIPRequest ackRequest = new SIPRequest(SIPMethodsEnum.ACK, ackURI.ToString());
            ackRequest.LocalSIPEndPoint = localSIPEndPoint;

            SIPHeader header = new SIPHeader(TransactionRequest.Header.From, sipResponse.Header.To, sipResponse.Header.CSeq, sipResponse.Header.CallId);
            header.CSeqMethod = SIPMethodsEnum.ACK;
            header.AuthenticationHeader = TransactionRequest.Header.AuthenticationHeader;
            header.Routes = base.TransactionRequest.Header.Routes;
            header.ProxySendFrom = base.TransactionRequest.Header.ProxySendFrom;

            ackRequest.Header = header;

            SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, sipResponse.Header.Vias.TopViaHeader.Branch);
            ackRequest.Header.Vias.PushViaHeader(viaHeader);

            AddCustomHeader(ackRequest);
            return ackRequest;
        }

        public void CancelCall(string cancelReason = null)
        {
            try
            {
                base.Cancel();

                if (CDR != null)
                {
                    CDR.Cancelled(cancelReason);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception UACInviteTransaction CancelCall. " + excp.Message);
                throw;
            }
        }

        protected void AddCustomHeader(SIPRequest request)
        {
            if (_customHeader != null && _customHeader.Count > 0)
            {
                foreach (string header in _customHeader)
                {
                    if (!header.IsNullOrBlank() && !request.Header.UnknownHeaders.Contains(header))
                        request.Header.UnknownHeaders.Add(header);
                }
            }
        }

    }
}
