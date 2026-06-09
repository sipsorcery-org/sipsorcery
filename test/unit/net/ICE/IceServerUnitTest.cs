//-----------------------------------------------------------------------------
// Filename: IceServerUnitTest.cs
//
// Description: Characterization tests for the IceServer per-server state machine
// (STUN/TURN client). These pin the transaction-ID correlation, the Allocate/
// Binding/Refresh response handling (including the 401/438 authentication retry
// path) and candidate generation, so the upcoming extraction of the ICE-server /
// TURN client out of RtpIceChannel can be verified to preserve behaviour.
//
// IceServer's internals are exercised directly (InternalsVisibleTo
// SIPSorcery.UnitTests). The response handling is a pure function of the supplied
// STUN message and the server's state - no network is involved.
//
// Author(s):
// Aaron Clauson
//
// History:
// 09 Jun 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class IceServerUnitTest
    {
        private readonly Microsoft.Extensions.Logging.ILogger logger;

        public IceServerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static IceServer CreateTurnServer(int id = 1)
        {
            STUNUri.TryParse("turn:turn.example.com:3478", out var uri);
            return new IceServer(uri, id, "user", "pass");
        }

        // Builds a STUN response carrying the server's current transaction id so GotStunResponse accepts it.
        private static STUNMessage ResponseFor(IceServer server, STUNMessageTypesEnum type)
        {
            var resp = new STUNMessage(type);
            resp.Header.TransactionId = Encoding.ASCII.GetBytes(server.TransactionID);
            return resp;
        }

        private static STUNAttribute Lifetime(uint seconds)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, seconds);
            return new STUNAttribute(STUNAttributeTypesEnum.Lifetime, bytes);
        }

        // ---- ParseIceServer ----

        [Fact]
        public void ParseIceServer_BasicStunUrl()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = IceServer.ParseIceServer("stun:stun.example.com:3478");

            Assert.Equal(System.Net.Sockets.ProtocolType.Udp, server.Protocol);
            Assert.Equal(STUNSchemesEnum.stun, server.Uri.Scheme);
            Assert.Null(server._username);
            Assert.Null(server._password);
        }

        [Fact]
        public void ParseIceServer_TurnUrlWithCredentials()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = IceServer.ParseIceServer("turn:turn.example.com?transport=tcp;user1;pass1");

            Assert.Equal(STUNSchemesEnum.turn, server.Uri.Scheme);
            Assert.Equal(System.Net.Sockets.ProtocolType.Tcp, server.Protocol);
            Assert.Equal("user1", server._username);
            Assert.Equal("pass1", server._password);
        }

        [Fact]
        public void ParseIceServer_SchemelessUrlGetsStunPrefix()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = IceServer.ParseIceServer("stun.example.com:3478");

            Assert.Equal(STUNSchemesEnum.stun, server.Uri.Scheme);
        }

        [Fact]
        public void ParseIceServer_MultipleUrlsTakesFirst()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = IceServer.ParseIceServer("stun:stun1.example.com,stun:stun2.example.com");

            Assert.Contains("stun1", server.Uri.ToString());
        }

        [Fact]
        public void ParseIceServer_NullThrowsArgumentNull()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            Assert.Throws<ArgumentNullException>(() => IceServer.ParseIceServer(null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseIceServer_EmptyThrowsArgument(string value)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            Assert.Throws<ArgumentException>(() => IceServer.ParseIceServer(value));
        }

        // ---- Transaction id ----

        [Fact]
        public void TransactionId_HasServerPrefixAndCorrectLength()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer(id: 3);

            Assert.StartsWith(IceServer.ICE_SERVER_TXID_PREFIX + "3", server.TransactionID);
            Assert.Equal(STUNHeader.TRANSACTION_ID_LENGTH, server.TransactionID.Length);
        }

        [Fact]
        public void GenerateNewTransactionID_ProducesADifferentId()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            var first = server.TransactionID;
            server.GenerateNewTransactionID();

            Assert.NotEqual(first, server.TransactionID);
            Assert.StartsWith(IceServer.ICE_SERVER_TXID_PREFIX + "1", server.TransactionID);
        }

        [Fact]
        public void IsTransactionIDMatch_MatchesOwnPrefixOnly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer(id: 2);

            Assert.True(server.IsTransactionIDMatch(server.TransactionID));
            Assert.True(server.IsTransactionIDMatch(IceServer.ICE_SERVER_TXID_PREFIX + "2" + "abcdef"));
            Assert.False(server.IsTransactionIDMatch(IceServer.ICE_SERVER_TXID_PREFIX + "7" + "abcdef"));
        }

        // ---- SetAuthenticationFields ----

        [Fact]
        public void SetAuthenticationFields_ExtractsNonceAndRealm()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            var resp = new STUNMessage(STUNMessageTypesEnum.AllocateErrorResponse);
            resp.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes("the-nonce")));
            resp.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm, Encoding.UTF8.GetBytes("the-realm")));

            server.SetAuthenticationFields(resp);

            Assert.Equal("the-nonce", Encoding.UTF8.GetString(server.Nonce));
            Assert.Equal("the-realm", Encoding.UTF8.GetString(server.Realm));
        }

        // ---- GotStunResponse ----

        [Fact]
        public void GotStunResponse_TransactionIdMismatch_IsIgnored()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            server.OutstandingRequestsSent = 5;

            var resp = new STUNMessage(STUNMessageTypesEnum.AllocateSuccessResponse);
            resp.Header.TransactionId = Encoding.ASCII.GetBytes("99999900wxyz"); // wrong prefix/id, 12 chars.

            bool candidates = server.GotStunResponse(resp, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478));

            Assert.False(candidates);
            Assert.Equal(5, server.OutstandingRequestsSent);   // untouched - response ignored.
            Assert.Null(server.RelayEndPoint);
        }

        [Fact]
        public void GotStunResponse_AllocateSuccess_SetsRelayReflexiveAndExpiry()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            server.OutstandingRequestsSent = 4;
            server.ErrorResponseCount = 2;

            var resp = ResponseFor(server, STUNMessageTypesEnum.AllocateSuccessResponse);
            resp.AddXORMappedAddressAttribute(IPAddress.Parse("203.0.113.10"), 50000);
            resp.AddXORAddressAttribute(STUNAttributeTypesEnum.XORRelayedAddress, IPAddress.Parse("198.51.100.20"), 60000);
            resp.Attributes.Add(Lifetime(600));

            bool candidates = server.GotStunResponse(resp, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478));

            Assert.True(candidates);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.10"), 50000), server.ServerReflexiveEndPoint);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("198.51.100.20"), 60000), server.RelayEndPoint);
            Assert.Equal(0, server.OutstandingRequestsSent);
            Assert.Equal(0, server.ErrorResponseCount);
            Assert.True(server.TurnTimeToExpiry > DateTime.Now.AddSeconds(590) && server.TurnTimeToExpiry < DateTime.Now.AddSeconds(610));
        }

        [Fact]
        public void GotStunResponse_AllocateUnauthorised_SetsAuthAndRotatesTransactionId()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            var originalTxId = server.TransactionID;

            var resp = ResponseFor(server, STUNMessageTypesEnum.AllocateErrorResponse);
            resp.Attributes.Add(new STUNErrorCodeAttribute(IceServer.STUN_UNAUTHORISED_ERROR_CODE, "Unauthorized"));
            resp.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes("n1")));
            resp.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm, Encoding.UTF8.GetBytes("r1")));

            bool candidates = server.GotStunResponse(resp, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478));

            Assert.False(candidates);
            Assert.Equal("n1", Encoding.UTF8.GetString(server.Nonce));
            Assert.Equal("r1", Encoding.UTF8.GetString(server.Realm));
            Assert.NotEqual(originalTxId, server.TransactionID);  // rotated for the authenticated retry.
            Assert.Equal(1, server.ErrorResponseCount);
        }

        [Fact]
        public void GotStunResponse_AllocateOtherError_IncrementsErrorCountOnly()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            var originalTxId = server.TransactionID;

            var resp = ResponseFor(server, STUNMessageTypesEnum.AllocateErrorResponse);
            resp.Attributes.Add(new STUNErrorCodeAttribute(486, "Allocation Quota Reached"));

            server.GotStunResponse(resp, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478));

            Assert.Equal(1, server.ErrorResponseCount);
            Assert.Equal(originalTxId, server.TransactionID);  // not an auth error, no rotation.
            Assert.Null(server.Nonce);
        }

        [Fact]
        public void GotStunResponse_BindingSuccess_SetsServerReflexive()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();

            var resp = ResponseFor(server, STUNMessageTypesEnum.BindingSuccessResponse);
            resp.AddXORMappedAddressAttribute(IPAddress.Parse("203.0.113.55"), 40000);

            bool candidates = server.GotStunResponse(resp, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478));

            Assert.True(candidates);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("203.0.113.55"), 40000), server.ServerReflexiveEndPoint);
        }

        [Fact]
        public void GotStunResponse_BindingUnauthorised_SetsAuthAndRotatesTransactionId()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            var originalTxId = server.TransactionID;

            var resp = ResponseFor(server, STUNMessageTypesEnum.BindingErrorResponse);
            resp.Attributes.Add(new STUNErrorCodeAttribute(IceServer.STUN_STALE_NONCE_ERROR_CODE, "Stale Nonce"));
            resp.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes("n2")));

            server.GotStunResponse(resp, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478));

            Assert.Equal("n2", Encoding.UTF8.GetString(server.Nonce));
            Assert.NotEqual(originalTxId, server.TransactionID);
        }

        [Fact]
        public void GotStunResponse_RefreshSuccess_UpdatesExpiry()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            server.TurnTimeToExpiry = DateTime.MinValue;

            var resp = ResponseFor(server, STUNMessageTypesEnum.RefreshSuccessResponse);
            resp.Attributes.Add(Lifetime(300));

            server.GotStunResponse(resp, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478));

            Assert.True(server.TurnTimeToExpiry > DateTime.Now.AddSeconds(290) && server.TurnTimeToExpiry < DateTime.Now.AddSeconds(310));
        }

        [Fact]
        public void GotStunResponse_UnrecognisedType_IncrementsErrorCount()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();

            // A request type (not a response) is unexpected here and should be counted as an error.
            var resp = ResponseFor(server, STUNMessageTypesEnum.BindingRequest);

            server.GotStunResponse(resp, new IPEndPoint(IPAddress.Parse("1.2.3.4"), 3478));

            Assert.Equal(1, server.ErrorResponseCount);
        }

        // ---- GetCandidate ----

        [Fact]
        public void GetCandidate_ServerReflexive_WhenReflexiveEndPointSet()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            server.ServerReflexiveEndPoint = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 12345);

            var candidate = server.GetCandidate(new RTCIceCandidateInit(), RTCIceCandidateType.srflx);

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.srflx, candidate.type);
            Assert.Equal("203.0.113.7", candidate.address);
            Assert.Equal(12345, candidate.port);
        }

        [Fact]
        public void GetCandidate_Relay_WhenRelayEndPointSet()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();
            server.RelayEndPoint = new IPEndPoint(IPAddress.Parse("198.51.100.8"), 23456);

            var candidate = server.GetCandidate(new RTCIceCandidateInit(), RTCIceCandidateType.relay);

            Assert.NotNull(candidate);
            Assert.Equal(RTCIceCandidateType.relay, candidate.type);
            Assert.Equal("198.51.100.8", candidate.address);
            Assert.Equal(23456, candidate.port);
        }

        [Fact]
        public void GetCandidate_ReturnsNull_WhenEndPointNotAvailable()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var server = CreateTurnServer();

            Assert.Null(server.GetCandidate(new RTCIceCandidateInit(), RTCIceCandidateType.srflx));
            Assert.Null(server.GetCandidate(new RTCIceCandidateInit(), RTCIceCandidateType.relay));
        }
    }
}
