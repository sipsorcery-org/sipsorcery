using System;
using System.Net;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.UnitTests
{
    [TestClass]
    public class SIPTransactionEngineUnitTest
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


        [TestMethod]
        [ExpectedException(typeof(ApplicationException))]
        public void DuplicateTransactionUnitTest()
        {
            SIPTransactionEngine clientEngine = new SIPTransactionEngine();

            SIPURI dummyURI = SIPURI.ParseSIPURI("sip:dummy@mysipswitch.com");
            SIPRequest inviteRequest = GetDummyINVITERequest(dummyURI);

            SIPEndPoint dummySIPEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 1234));
            UACInviteTransaction clientTransaction = new UACInviteTransaction(new SIPTransport(MockSIPDNSManager.Resolve, null), inviteRequest, dummySIPEndPoint, dummySIPEndPoint, null);
            clientEngine.AddTransaction(clientTransaction);
            clientEngine.AddTransaction(clientTransaction);
        }

        /// <summary>
        /// Tests that the transaction ID is correctly generated and matched for a request and response pair.
        /// </summary>
        [TestMethod]
        public void MatchOnRequestAndResponseTest()
        {

            SIPTransactionEngine transactionEngine = new SIPTransactionEngine();
            SIPEndPoint dummySIPEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 1234));

            SIPRequest inviteRequest = SIPRequest.ParseSIPRequest("INVITE sip:dummy@udp:127.0.0.1:12014 SIP/2.0" + m_CRLF +
                "Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27" + m_CRLF +
                "To: <sip:dummy@udp:127.0.0.1:12014>" + m_CRLF +
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

            SIPTransaction transaction = new UACInviteTransaction(new SIPTransport(MockSIPDNSManager.Resolve, null), inviteRequest, dummySIPEndPoint, dummySIPEndPoint, null);
            transactionEngine.AddTransaction(transaction);

            SIPResponse sipResponse = SIPResponse.ParseSIPResponse("SIP/2.0 603 Nothing listening" + m_CRLF +
                "Via: SIP/2.0/UDP 127.0.0.1:1234;branch=z9hG4bK5f37455955ca433a902f8fea0ce2dc27;rport=12013" + m_CRLF +
                "To: <sip:dummy@udp:127.0.0.1:12014>" + m_CRLF +
                "From: <sip:unittest@mysipswitch.com>;tag=2062917371" + m_CRLF +
                "Call-ID: 8ae45c15425040179a4285d774ccbaf6" + m_CRLF +
                "CSeq: 1 INVITE" + m_CRLF +
                "Content-Length: 0" + m_CRLF +
                m_CRLF);

            Assert.IsNotNull(transactionEngine.GetTransaction(sipResponse), "Transaction should have matched, check the hashing mechanism.");
        }

        /// <summary>
        /// Tests the production and recognition of an ACK request for this transaction engine.
        /// The test uses two different transaction engine instances with one acting as the client and one as the server.
        /// </summary>
        [TestMethod]
        public void AckRecognitionUnitTest()
        {
            SIPTransport clientTransport = null;
            SIPTransport serverTransport = null;

            try
            {
                SIPTransactionEngine clientEngine = new SIPTransactionEngine();     // Client side of the INVITE.
                SIPEndPoint clientEndPoint = new SIPEndPoint(SIPProtocolsEnum.udp, new IPEndPoint(IPAddress.Loopback, 12013));
                clientTransport = new SIPTransport(MockSIPDNSManager.Resolve, clientEngine, new SIPUDPChannel(clientEndPoint.GetIPEndPoint()), false);
                SetTransportTraceEvents(clientTransport);

                SIPTransactionEngine serverEngine = new SIPTransactionEngine();     // Server side of the INVITE.
                UASInviteTransaction serverTransaction = null;
                SIPEndPoint serverEndPoint = new SIPEndPoint(new IPEndPoint(IPAddress.Loopback, 12014));
                serverTransport = new SIPTransport(MockSIPDNSManager.Resolve, serverEngine, new SIPUDPChannel(serverEndPoint.GetIPEndPoint()), false);
                SetTransportTraceEvents(serverTransport);
                serverTransport.SIPTransportRequestReceived += (localEndPoint, remoteEndPoint, sipRequest) =>
                {
                    Console.WriteLine("Server Transport Request In: " + sipRequest.Method + ".");
                    serverTransaction = serverTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localEndPoint, null);
                    SetTransactionTraceEvents(serverTransaction);
                    serverTransaction.GotRequest(localEndPoint, remoteEndPoint, sipRequest);
                };

                SIPURI dummyURI = SIPURI.ParseSIPURI("sip:dummy@" + serverEndPoint);
                SIPRequest inviteRequest = GetDummyINVITERequest(dummyURI);
                inviteRequest.LocalSIPEndPoint = clientTransport.GetDefaultTransportContact(SIPProtocolsEnum.udp);

                // Send the invite to the server side.
                UACInviteTransaction clientTransaction = new UACInviteTransaction(clientTransport, inviteRequest, serverEndPoint, clientEndPoint, null);
                SetTransactionTraceEvents(clientTransaction);
                clientEngine.AddTransaction(clientTransaction);
                clientTransaction.SendInviteRequest(serverEndPoint, inviteRequest);

                Thread.Sleep(500);

                Assert.IsTrue(clientTransaction.TransactionState == SIPTransactionStatesEnum.Completed, "Client transaction in incorrect state.");
                Assert.IsTrue(serverTransaction.TransactionState == SIPTransactionStatesEnum.Confirmed, "Server transaction in incorrect state.");
            }
            finally
            {
                if (clientTransport != null)
                {
                    clientTransport.Shutdown();
                }

                if (serverTransport != null)
                {
                    serverTransport.Shutdown();
                }
            }
        }

        [TestMethod]
        public void AckRecognitionIIUnitTest()
        {
            SIPTransactionEngine engine = new SIPTransactionEngine();     // Client side of the INVITE.

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
            UASInviteTransaction serverTransaction = new UASInviteTransaction(new SIPTransport(MockSIPDNSManager.Resolve, null), inviteRequest, dummySIPEndPoint, dummySIPEndPoint, null, IPAddress.Loopback, true);
            engine.AddTransaction(serverTransaction);

            //SIPResponse errorResponse = SIPTransport.GetResponse(inviteRequest.Header, SIPResponseStatusCodesEnum.Decline, "Unit Test", null, null);

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

            Assert.IsTrue(matchingTransaction.TransactionId == serverTransaction.TransactionId, "ACK transaction did not match INVITE transaction.");
        }

        private SIPRequest GetDummyINVITERequest(SIPURI dummyURI)
        {
            string dummyFrom = "<sip:unittest@mysipswitch.com>";
            string dummyContact = "sip:127.0.0.1:1234";
            SIPRequest inviteRequest = new SIPRequest(SIPMethodsEnum.INVITE, dummyURI);

            SIPHeader inviteHeader = new SIPHeader(SIPFromHeader.ParseFromHeader(dummyFrom), new SIPToHeader(null, dummyURI, null), 1, CallProperties.CreateNewCallId());
            inviteHeader.From.FromTag = CallProperties.CreateNewTag();
            inviteHeader.Contact = SIPContactHeader.ParseContactHeader(dummyContact);
            inviteHeader.CSeqMethod = SIPMethodsEnum.INVITE;
            inviteHeader.UserAgent = "unittest";
            inviteRequest.Header = inviteHeader;

            SIPViaHeader viaHeader = new SIPViaHeader("127.0.0.1", 1234, CallProperties.CreateBranchId(), SIPProtocolsEnum.udp);
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
            //Console.WriteLine(sipTransaction.GetType() + " Trace (" + sipTransaction.TransactionId + "): " + message);
        }

        void transaction_TransactionStateChanged(SIPTransaction sipTransaction)
        {
            Console.WriteLine(sipTransaction.GetType() + " State Change (" + sipTransaction.TransactionId + "): " + sipTransaction.TransactionState);
        }

        void transaction_TransactionRemoved(SIPTransaction sipTransaction)
        {
            Console.WriteLine(sipTransaction.GetType() + " Removed (" + sipTransaction.TransactionId + ")");
        }

        void transport_UnrecognisedMessageReceived(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, byte[] buffer)
        {
            Console.WriteLine("Unrecognised: " + localEndPoint + "<-" + fromEndPoint.ToString() + " " + buffer.Length + " bytes.");
        }

        void transport_STUNRequestReceived(IPEndPoint receivedEndPoint, IPEndPoint remoteEndPoint, byte[] buffer, int bufferLength)
        {
            Console.WriteLine("STUN: " + receivedEndPoint + "<-" + remoteEndPoint.ToString() + " " + buffer.Length + " bufferLength.");
        }

        void transport_SIPResponseOutTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint toEndPoint, SIPResponse sipResponse)
        {
            Console.WriteLine("Response Out: " + localEndPoint + "->" + toEndPoint.ToString() + "\n" + sipResponse.ToString());
        }

        void transport_SIPResponseInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, SIPResponse sipResponse)
        {
            Console.WriteLine("Response In: " + localEndPoint + "<-" + fromEndPoint.ToString() + "\n" + sipResponse.ToString());
        }

        void transport_SIPRequestOutTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint toEndPoint, SIPRequest sipRequest)
        {
            Console.WriteLine("Request Out: " + localEndPoint + "->" + toEndPoint.ToString() + "\n" + sipRequest.ToString());
        }

        void transport_SIPRequestInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, SIPRequest sipRequest)
        {
            Console.WriteLine("Request In: " + localEndPoint + "<-" + fromEndPoint.ToString() + "\n" + sipRequest.ToString());
        }

        void transport_SIPBadResponseInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, string message, SIPValidationFieldsEnum errorField, string rawMessage)
        {
            Console.WriteLine("Bad Response: " + localEndPoint + "<-" + fromEndPoint.ToString() + " " + errorField + ". " + message + "\n" + rawMessage);
        }

        void transport_SIPBadRequestInTraceEvent(SIPEndPoint localEndPoint, SIPEndPoint fromEndPoint, string message, SIPValidationFieldsEnum errorField, string rawMessage)
        {
            Console.WriteLine("Bad Request: " + localEndPoint + "<-" + fromEndPoint.ToString() + " " + errorField + "." + message + "\n" + rawMessage);
        }
    }
}
