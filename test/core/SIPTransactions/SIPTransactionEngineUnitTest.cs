//-----------------------------------------------------------------------------
// Author(s):
// Aaron Clauson
// 
// History:
// 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.SIP.UnitTests
{
    [Trait("Category", "unit")]
    public class SIPTransactionEngineUnitTest
    {
        private const int TRANSACTION_EXCHANGE_TIMEOUT_MS = 15000;

        private Microsoft.Extensions.Logging.ILogger logger = null;
        protected static readonly string m_CRLF = SIPConstants.CRLF;

        public SIPTransactionEngineUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        //[Fact]
        //public void DuplicateTransactionUnitTest()
        //{
        //    SIPTransactionEngine clientEngine = new SIPTransactionEngine();

        //    SIPURI dummyURI = SIPURI.ParseSIPURI("sip:dummy@mysipswitch.com");
        //    SIPRequest inviteRequest = GetDummyINVITERequest(dummyURI);

        //    SIPEndPoint dummySIPEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 1234));
        //    UACInviteTransaction clientTransaction = new UACInviteTransaction(new SIPTransport(MockSIPDNSManager.Resolve, clientEngine), inviteRequest, null);
        //    //clientEngine.AddTransaction(clientTransaction);

        //    Assert.Throws<ApplicationException>(() => clientEngine.AddTransaction(clientTransaction));
        //}

        /// <summary>
        /// Tests that the transaction ID is correctly generated and matched for a request and response pair.
        /// </summary>
        [Fact]
        public void MatchOnRequestAndResponseTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport sipTransport = new SIPTransport();
            SIPTransactionEngine transactionEngine = sipTransport.m_transactionEngine;

            SIPRequest inviteRequest = SIPRequest.ParseSIPRequest("INVITE sip:dummy@127.0.0.1:12014 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27" + m_CRLF +
                "To: <sip:dummy@127.0.0.1:12014>" + m_CRLF +
                "From: <sip:unittest@mysipswitch.com>;tag=2062917371" + m_CRLF +
                "Call-ID: 8ae45c15425040179a4285d774ccbaf6" + m_CRLF +
                "CSeq: 1 INVITE" + m_CRLF +
                "Contact: <sip:127.0.0.1:1234>" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "User-Agent: unittest" + m_CRLF +
                "Content-Length: 5" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                m_CRLF +
                "dummy");

            SIPTransaction tx = new UACInviteTransaction(sipTransport, inviteRequest, null);
            transactionEngine.AddTransaction(tx);

            SIPResponse sipResponse = SIPResponse.ParseSIPResponse("SIP/2.0 603 Nothing listening" + m_CRLF +
                "Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013" + m_CRLF +
                "To: <sip:dummy@127.0.0.1:12014>" + m_CRLF +
                "From: <sip:unittest@mysipswitch.com>;tag=2062917371" + m_CRLF +
                "Call-ID: 8ae45c15425040179a4285d774ccbaf6" + m_CRLF +
                "CSeq: 1 INVITE" + m_CRLF +
                "Content-Length: 0" + m_CRLF +
                m_CRLF);

            Assert.True(transactionEngine.GetTransaction(sipResponse) != null, "Transaction should have matched, check the hashing mechanism.");
        }

        /// <summary>
        /// Tests the production and recognition of an ACK request for this transaction engine.
        /// The test uses two different transaction engine instances with one acting as the client and one as the server.
        /// </summary>
        [Fact]
        [Trait("Category", "txintegration")]
        public void AckRecognitionUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport clientTransport = null;
            SIPTransport serverTransport = null;

            try
            {
                TaskCompletionSource<bool> uasConfirmedTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Client side of the call.
                clientTransport = new SIPTransport();
                clientTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, 0)));
                var clientEngine = clientTransport.m_transactionEngine;
                SetTransportTraceEvents(clientTransport);

                // Server side of the call.
                UASInviteTransaction serverTransaction = null;
                serverTransport = new SIPTransport();
                serverTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, 0)));
                SIPTransactionEngine serverEngine = serverTransport.m_transactionEngine;
                SetTransportTraceEvents(serverTransport);
                serverTransport.SIPTransportRequestReceived += (localEndPoint, remoteEndPoint, sipRequest) =>
                {
                    logger.LogDebug("Server Transport Request In: " + sipRequest.Method + ".");
                    serverTransaction = new UASInviteTransaction(serverTransport, sipRequest, null);
                    SetTransactionTraceEvents(serverTransaction);
                    serverTransaction.NewCallReceived += (lep, rep, sipTransaction, newCallRequest) =>
                    {
                        logger.LogDebug("Server new call received.");
                        var busyResponse = SIPResponse.GetResponse(newCallRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                        (sipTransaction as UASInviteTransaction).SendFinalResponse(busyResponse);
                        return Task.FromResult(SocketError.Success);
                    };
                    serverTransaction.TransactionStateChanged += (tx) =>
                    {
                        if (tx.TransactionState == SIPTransactionStatesEnum.Confirmed)
                        {
                            if (!uasConfirmedTask.TrySetResult(true))
                            {
                                logger.LogWarning($"AckRecognitionUnitTest: FAILED to set result on CompletionSource.");
                            }
                        }
                    };
                    serverTransaction.GotRequest(localEndPoint, remoteEndPoint, sipRequest);

                    return Task.FromResult(0);
                };

                SIPURI dummyURI = new SIPURI("dummy", serverTransport.GetSIPChannels().First().ListeningEndPoint.ToString(), null, SIPSchemesEnum.sip);
                SIPRequest inviteRequest = GetDummyINVITERequest(dummyURI);

                // Send the invite to the server side.
                UACInviteTransaction clientTransaction = new UACInviteTransaction(clientTransport, inviteRequest, null);
                SetTransactionTraceEvents(clientTransaction);
                clientEngine.AddTransaction(clientTransaction);
                clientTransaction.SendInviteRequest();

                if (!uasConfirmedTask.Task.Wait(TRANSACTION_EXCHANGE_TIMEOUT_MS))
                {
                    logger.LogWarning($"Tasks timed out");
                }

                Assert.True(clientTransaction.TransactionState == SIPTransactionStatesEnum.Confirmed, "Client transaction in incorrect state.");
                Assert.True(serverTransaction.TransactionState == SIPTransactionStatesEnum.Confirmed, "Server transaction in incorrect state.");
            }
            finally
            {
                clientTransport.Shutdown();
                serverTransport.Shutdown();
            }
        }

        [Fact]
        public void AckRecognitionIIUnitTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            SIPTransport sipTransport = new SIPTransport();
            SIPTransactionEngine engine = sipTransport.m_transactionEngine;     // Client side of the INVITE.

            string inviteRequestStr =
                "INVITE sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 INVITE" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "Content-Type: application/sdp" + m_CRLF +
                "User-Agent: Dummy" + m_CRLF +
                m_CRLF;

            SIPRequest inviteRequest = SIPRequest.ParseSIPRequest(inviteRequestStr);

            // Server has received the invite.
            SIPEndPoint dummySIPEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 1234));
            UASInviteTransaction serverTransaction = new UASInviteTransaction(sipTransport, inviteRequest, null, true);
            engine.AddTransaction(serverTransaction);

            string ackRequestStr =
                "ACK sip:303@sip.blueface.ie SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 192.168.1.2:5065;rport;branch=z9hG4bKFBB7EAC06934405182D13950BD51F001" + m_CRLF +
                "From: SER Test X <sip:aaronxten@sip.blueface.ie:5065>;tag=196468136" + m_CRLF +
                "To: <sip:303@sip.blueface.ie>" + m_CRLF +
                "Contact: <sip:aaronxten@192.168.1.2:5065>" + m_CRLF +
                "Call-ID: A3DF9A04-0EFE-47E4-98B1-E18AA186F3D6@192.168.1.2" + m_CRLF +
                "CSeq: 49429 ACK" + m_CRLF +
                "Max-Forwards: 70" + m_CRLF +
                "User-Agent: Dummy" + m_CRLF +
                m_CRLF;

            SIPRequest ackRequest = SIPRequest.ParseSIPRequest(ackRequestStr);

            SIPTransaction matchingTransaction = engine.GetTransaction(ackRequest);

            Assert.True(matchingTransaction.TransactionId == serverTransaction.TransactionId, "ACK transaction did not match INVITE transaction.");
        }

        private SIPRequest GetDummyINVITERequest(SIPURI dummyURI)
        {
            string dummyFrom = "<sip:unittest@mysipswitch.com>";
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, dummyURI);

            SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader(dummyFrom), new SIPToHeader(null, dummyURI, null), 1, CallProperties.CreateNewCallId());
            inviteHeader.From.FromTag = CallProperties.CreateNewTag();
            inviteHeader.Contact = new List<SIPContactHeader> { SIPContactHeader.GetDefaultSIPContactHeader() };
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteHeader.UserAgent = "unittest";
            inviteRequest.Header = inviteHeader;

            SIPViaHeader viaHeader = SIPViaHeader.GetDefaultSIPViaHeader();
            inviteRequest.Header.Vias.PushViaHeader(viaHeader);

            inviteRequest.Body = "dummy";
            inviteRequest.Header.ContentLength = inviteRequest.Body.Length;
            inviteRequest.Header.ContentType = "application/sdp";

            return inviteRequest;
        }

        void SetTransportTraceEvents(SIPTransport transport)
        {
            transport.SIPBadRequestInTraceEvent += transport_SIPBadRequestInTraceEvent;
            transport.SIPBadResponseInTraceEvent += transport_SIPBadResponseInTraceEvent;
            transport.SIPRequestInTraceEvent += transport_SIPRequestInTraceEvent;
            transport.SIPRequestOutTraceEvent += transport_SIPRequestOutTraceEvent;
            transport.SIPResponseInTraceEvent += transport_SIPResponseInTraceEvent;
            transport.SIPResponseOutTraceEvent += transport_SIPResponseOutTraceEvent;
            transport.STUNRequestReceived += transport_STUNRequestReceived;
        }

        void SetTransactionTraceEvents(SIPTransaction transaction)
        {
            transaction.TransactionRemoved += new SIPTransactionRemovedDelegate(transaction_TransactionRemoved);
            transaction.TransactionStateChanged += new SIPTransactionStateChangeDelegate(transaction_TransactionStateChanged);
            transaction.TransactionTraceMessage += new SIPTransactionTraceMessageDelegate(transaction_TransactionTraceMessage);
        }

        void transaction_TransactionTraceMessage(SIPTransaction sipTransaction, string message)
        {
            logger.LogDebug(sipTransaction.GetType() + " Trace (" + sipTransaction.TransactionId + "): " + message);
        }

        void transaction_TransactionStateChanged(SIPTransaction sipTransaction)
        {
            logger.LogDebug(sipTransaction.GetType() + " State Change (" + sipTransaction.TransactionId + "): " + sipTransaction.TransactionState);
        }

        void transaction_TransactionRemoved(SIPTransaction sipTransaction)
        {
            logger.LogDebug(sipTransaction.GetType() + " Removed (" + sipTransaction.TransactionId + ")");
        }

        void transport_UnrecognisedMessageReceived(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, byte[] buffer)
        {
            logger.LogDebug("Unrecognised: " + localEndPoint + "<-" + fromEndPoint + " " + buffer.Length + " bytes.");
        }

        void transport_STUNRequestReceived(IPEndPoint receivedEndPoint, IPEndPoint remoteEndPoint, byte[] buffer, int bufferLength)
        {
            logger.LogDebug("STUN: " + receivedEndPoint + "<-" + remoteEndPoint.ToString() + " " + buffer.Length + " bufferLength.");
        }

        void transport_SIPResponseOutTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint toEndPoint, SIPResponse sipResponse)
        {
            logger.LogDebug("Response Out: " + localEndPoint + "->" + toEndPoint.ToString() + "\n" + sipResponse.ToString());
        }

        void transport_SIPResponseInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, SIPResponse sipResponse)
        {
            logger.LogDebug("Response In: " + localEndPoint + "<-" + fromEndPoint + "\n" + sipResponse.ToString());
        }

        void transport_SIPRequestOutTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint toEndPoint, SIPRequest sipRequest)
        {
            logger.LogDebug("Request Out: " + localEndPoint + "->" + toEndPoint + "\n" + sipRequest.ToString());
        }

        void transport_SIPRequestInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, SIPRequest sipRequest)
        {
            logger.LogDebug("Request In: " + localEndPoint + "<-" + fromEndPoint + "\n" + sipRequest.ToString());
        }

        void transport_SIPBadResponseInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, string message, SIPValidationFieldsEnum errorField, string rawMessage)
        {
            logger.LogDebug("Bad Response: " + localEndPoint + "<-" + fromEndPoint + " " + errorField + ". " + message + "\n" + rawMessage);
        }

        void transport_SIPBadRequestInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, string message, SIPValidationFieldsEnum errorField, string rawMessage)
        {
            logger.LogDebug("Bad Request: " + localEndPoint + "<-" + fromEndPoint + " " + errorField + "." + message + "\n" + rawMessage);
        }
    }
}
