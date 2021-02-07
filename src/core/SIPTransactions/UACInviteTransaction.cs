//-----------------------------------------------------------------------------
// Filename: UACInviteTransaction.cs
//
// Description: SIP Transaction that implements UAC (User Agent Client) functionality for
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
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

[assembly: InternalsVisibleToAttribute("SIPSorcery.UnitTests")]

namespace SIPSorcery.SIP
{
    /// <summary>
    /// SIP transaction that initiates a call to a SIP User Agent Server. This transaction 
    /// processes outgoing calls SENT by the application.
    /// </summary>
    public class UACInviteTransaction : SIPTransaction
    {
        public event SIPTransactionResponseReceivedDelegate UACInviteTransactionInformationResponseReceived;
        public event SIPTransactionResponseReceivedDelegate UACInviteTransactionFinalResponseReceived;
        public event SIPTransactionFailedDelegate UACInviteTransactionFailed;

        private bool _sendOkAckManually = false;
        internal bool _disablePrackSupport = false;
        internal bool m_sentPrack;                  // Records whether the PRACK request was sent.
        private int m_cseq;

        /// <summary>
        /// Default constructor for user agent client INVITE transaction.
        /// </summary>
        /// <param name="sendOkAckManually">If set an ACK request for the 2xx response will NOT be sent and it will be up to 
        /// the application to explicitly call the SendACK request.</param>
        /// <param name="disablePrackSupport">If set to true then PRACK support will not be set in the initial INVITE request.</param>
        public UACInviteTransaction(SIPTransport sipTransport,
            SIPRequest sipRequest,
            SIPEndPoint outboundProxy,
            bool sendOkAckManually = false,
            bool disablePrackSupport = false)
            : base(sipTransport, sipRequest, outboundProxy)
        {
            TransactionType = SIPTransactionTypesEnum.InviteClient;
            m_localTag = sipRequest.Header.From.FromTag;
            CDR = new SIPCDR(SIPCallDirection.Out, sipRequest.URI, sipRequest.Header.From, sipRequest.Header.CallId, sipRequest.LocalSIPEndPoint, sipRequest.RemoteSIPEndPoint);
            _sendOkAckManually = sendOkAckManually;
            _disablePrackSupport = disablePrackSupport;
            m_cseq = sipRequest.Header.CSeq;

            TransactionFinalResponseReceived += UACInviteTransaction_TransactionFinalResponseReceived;
            TransactionInformationResponseReceived += UACInviteTransaction_TransactionInformationResponseReceived;
            TransactionFailed += UACInviteTransaction_TransactionFailed;
            TransactionRequestReceived += UACInviteTransaction_TransactionRequestReceived;
        }

        public void SendInviteRequest()
        {
            base.SendReliableRequest();
        }

        private Task<SocketError> UACInviteTransaction_TransactionRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPRequest sipRequest)
        {
            logger.LogWarning("UACInviteTransaction received unexpected request, " + sipRequest.Method + " from " + remoteEndPoint.ToString() + ", ignoring.");
            return Task.FromResult(SocketError.Fault);
        }

        private void UACInviteTransaction_TransactionFailed(SIPTransaction sipTransaction, SocketError failureReason)
        {
            UACInviteTransactionFailed?.Invoke(sipTransaction, failureReason);
            CDR?.TimedOut();
        }

        private async Task<SocketError> UACInviteTransaction_TransactionInformationResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                if (sipResponse.StatusCode > 100 && sipResponse.StatusCode <= 199)
                {
                    if (sipResponse.Header.RSeq > 0)
                    {
                        // Send a PRACK for this provisional response.
                        m_cseq++;
                        PRackRequest = GetAcknowledgeRequest(sipResponse, SIPMethodsEnum.PRACK, m_cseq, null, null);
                        await SendRequestAsync(PRackRequest).ConfigureAwait(false);
                    }
                }

                if (CDR != null)
                {
                    SIPEndPoint localEP = SIPEndPoint.TryParse(sipResponse.Header.ProxyReceivedOn) ?? localSIPEndPoint;
                    SIPEndPoint remoteEP = SIPEndPoint.TryParse(sipResponse.Header.ProxyReceivedFrom) ?? remoteEndPoint;
                    CDR.Progress(sipResponse.Status, sipResponse.ReasonPhrase, localEP, remoteEP);
                }

                if (UACInviteTransactionInformationResponseReceived != null)
                {
                    return await UACInviteTransactionInformationResponseReceived(localSIPEndPoint, remoteEndPoint, sipTransaction, sipResponse).ConfigureAwait(false);
                }
                else
                {
                    return SocketError.Success;
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception UACInviteTransaction_TransactionInformationResponseReceived. " + excp.Message);
                return SocketError.Fault;
            }
        }

        private async Task<SocketError> UACInviteTransaction_TransactionFinalResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                DeliveryPending = false;
                base.UpdateTransactionState(SIPTransactionStatesEnum.Confirmed);

                // BranchId for 2xx responses needs to be a new one, non-2xx final responses use same one as original request.
                if (sipResponse.StatusCode >= 200 && sipResponse.StatusCode < 299)
                {
                    if (_sendOkAckManually == false)
                    {
                        AckRequest = GetAcknowledgeRequest(sipResponse, SIPMethodsEnum.ACK, sipResponse.Header.CSeq, null, null);
                        await SendRequestAsync(AckRequest).ConfigureAwait(false);
                    }
                }
                else
                {
                    // ACK for non 2xx response is part of the INVITE transaction and gets routed to the same endpoint as the INVITE.
                    AckRequest = GetInTransactionACKRequest(sipResponse, m_transactionRequest.URI);
                    await SendRequestAsync(AckRequest).ConfigureAwait(false);
                }

                if (CDR != null)
                {
                    SIPEndPoint localEP = SIPEndPoint.TryParse(sipResponse.Header.ProxyReceivedOn) ?? localSIPEndPoint;
                    SIPEndPoint remoteEP = SIPEndPoint.TryParse(sipResponse.Header.ProxyReceivedFrom) ?? remoteEndPoint;
                    CDR.Answered(sipResponse.StatusCode, sipResponse.Status, sipResponse.ReasonPhrase, localEP, remoteEP);
                }

                if (UACInviteTransactionFinalResponseReceived != null)
                {
                    return await UACInviteTransactionFinalResponseReceived(localSIPEndPoint, remoteEndPoint, sipTransaction, sipResponse).ConfigureAwait(false);
                }
                else
                {
                    return SocketError.Success;
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"Exception UACInviteTransaction_TransactionFinalResponseReceived. {excp.Message}");
                return SocketError.Fault;
            }
        }

        /// <summary>
        /// Generates the ACK or PRACK request to acknowledge a response. This method generates the ACK requests 
        /// for INVITE 2xx and PRACK for 1xx responses. The request needs to be sent as part of a new transaction. 
        /// Note for constructing the ACK for INVITE >= 300 responses is <seealso cref="GetInTransactionACKRequest"/>.
        /// </summary>
        /// <param name="ackResponse">The response being acknowledged.</param>
        /// <param name="ackMethod">The acknowledgement request method, either ACK or PRACK.</param>
        /// <param name="cseq">The SIP CSeq header value to set on the acknowledge request.</param>
        /// <param name="content">The optional content body for the ACK request.</param>
        /// <param name="contentType">The optional content type.</param>
        private SIPRequest GetAcknowledgeRequest(SIPResponse ackResponse, SIPMethodsEnum ackMethod, int cseq, string content, string contentType)
        {
            if (ackResponse.Header.To != null)
            {
                m_remoteTag = ackResponse.Header.To.ToTag;
            }

            SIPURI requestURI = m_transactionRequest.URI.CopyOf();
            if (ackResponse.Header.Contact?.Count > 0)
            {
                requestURI = ackResponse.Header.Contact[0].ContactURI;
                // Don't mangle private contacts if there is a Record-Route header. If a proxy is putting private IP's 
                // in a Record-Route header that's its problem.
                if ((ackResponse.Header.RecordRoutes == null || ackResponse.Header.RecordRoutes.Length == 0)
                    && IPSocket.IsPrivateAddress(requestURI.Host) && !ackResponse.Header.ProxyReceivedFrom.IsNullOrBlank())
                {
                    // Setting the Proxy-ReceivedOn header is how an upstream proxy will let an agent know it should 
                    // mangle the contact. 
                    SIPEndPoint remoteUASSIPEndPoint = SIPEndPoint.ParseSIPEndPoint(ackResponse.Header.ProxyReceivedFrom);
                    requestURI.Host = remoteUASSIPEndPoint.GetIPEndPoint().ToString();
                }
            }

            // ACK for 2xx response needs to be a new transaction and gets routed based on SIP request fields.
            var ackRequest = GetNewTxACKRequest(ackMethod, cseq, ackResponse, requestURI);

            if (content.NotNullOrBlank())
            {
                ackRequest.Body = content;
                ackRequest.Header.ContentLength = ackRequest.Body.Length;
                ackRequest.Header.ContentType = contentType;
            }

            return ackRequest;
        }

        /// <summary>
        /// New transaction ACK requests are for 2xx responses, i.e. INVITE accepted and 
        /// dialogue being created.
        /// </summary>
        /// <remarks>
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
        private SIPRequest GetNewTxACKRequest(SIPMethodsEnum method, int cseq, SIPResponse sipResponse, SIPURI ackURI)
        {
            SIPRequest acknowledgeRequest = new SIPRequest(method, ackURI.ToString());
            acknowledgeRequest.SetSendFromHints(sipResponse.LocalSIPEndPoint);

            SIPHeader header = new SIPHeader(TransactionRequest.Header.From, sipResponse.Header.To, cseq, sipResponse.Header.CallId);
            header.CSeqMethod = method;
            header.AuthenticationHeader = TransactionRequest.Header.AuthenticationHeader;
            header.ProxySendFrom = TransactionRequest.Header.ProxySendFrom;

            // If the UAS supplies a desired Record-Route list use that first. Otherwise fall back to any Route list used in the original transaction.
            if (sipResponse.Header.RecordRoutes != null)
            {
                header.Routes = sipResponse.Header.RecordRoutes.Reversed();
            }
            else if (TransactionRequest.Header.Routes != null)
            {
                header.Routes = TransactionRequest.Header.Routes;
            }

            acknowledgeRequest.Header = header;
            acknowledgeRequest.Header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader());

            if (method == SIPMethodsEnum.PRACK)
            {
                m_sentPrack = true;

                acknowledgeRequest.Header.RAckRSeq = sipResponse.Header.RSeq;
                acknowledgeRequest.Header.RAckCSeq = sipResponse.Header.CSeq;
                acknowledgeRequest.Header.RAckCSeqMethod = sipResponse.Header.CSeqMethod;
            }

            return acknowledgeRequest;
        }

        /// <summary>
        /// In transaction ACK requests are for non-2xx responses, i.e. INVITE rejected and no dialogue being created.
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
        /// </remarks>
        private SIPRequest GetInTransactionACKRequest(SIPResponse sipResponse, SIPURI ackURI)
        {
            SIPRequest ackRequest = new SIPRequest(SIPMethodsEnum.ACK, ackURI.ToString());
            ackRequest.SetSendFromHints(sipResponse.LocalSIPEndPoint);

            SIPHeader header = new SIPHeader(TransactionRequest.Header.From, sipResponse.Header.To, sipResponse.Header.CSeq, sipResponse.Header.CallId);
            header.CSeqMethod = SIPMethodsEnum.ACK;
            header.AuthenticationHeader = TransactionRequest.Header.AuthenticationHeader;
            header.Routes = base.TransactionRequest.Header.Routes;
            header.ProxySendFrom = base.TransactionRequest.Header.ProxySendFrom;

            ackRequest.Header = header;

            SIPViaHeader viaHeader = new SIPViaHeader(new IPEndPoint(IPAddress.Any, 0), sipResponse.Header.Vias.TopViaHeader.Branch);
            ackRequest.Header.Vias.PushViaHeader(viaHeader);

            return ackRequest;
        }

        /// <summary>
        /// Cancels this transaction. This does NOT generate a CANCEL request. A separate
        /// reliable transaction needs to be created for that.
        /// </summary>
        /// <param name="cancelReason">The reason for cancelling the transaction.</param>
        public void CancelCall(string cancelReason = null)
        {
            UpdateTransactionState(SIPTransactionStatesEnum.Cancelled);
            CDR?.Cancelled(cancelReason);
        }
    }
}
