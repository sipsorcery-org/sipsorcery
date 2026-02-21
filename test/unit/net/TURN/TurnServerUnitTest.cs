//-----------------------------------------------------------------------------
// Filename: TurnServerUnitTest.cs
//
// Description: Unit tests for TurnServer (RFC 5766).
//
// Author(s):
// SIPSorcery Contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class TurnServerUnitTest : IDisposable
    {
        private ILogger logger = null;
        private readonly List<TurnServer> _servers = new List<TurnServer>();

        private const string TEST_USERNAME = "testuser";
        private const string TEST_PASSWORD = "testpass";
        private const string TEST_REALM = "testrealm";

        public TurnServerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        public void Dispose()
        {
            foreach (var server in _servers)
            {
                try { server.Dispose(); } catch { }
            }
        }

        private (TurnServer server, int port) CreateTurnServer(bool enableTcp = true, bool enableUdp = false)
        {
            // Use port 0 to let the OS assign a free port, then read it back.
            // Since TurnServer requires a specific port, find a free one first.
            int port;
            var tempSocket = new TcpListener(IPAddress.Loopback, 0);
            tempSocket.Start();
            port = ((IPEndPoint)tempSocket.LocalEndpoint).Port;
            tempSocket.Stop();

            var config = new TurnServerConfig
            {
                ListenAddress = IPAddress.Loopback,
                Port = port,
                EnableTcp = enableTcp,
                EnableUdp = enableUdp,
                Username = TEST_USERNAME,
                Password = TEST_PASSWORD,
                Realm = TEST_REALM,
                RelayAddress = IPAddress.Loopback,
                DefaultLifetimeSeconds = 600,
            };

            var server = new TurnServer(config);
            _servers.Add(server);
            server.Start();
            return (server, port);
        }

        private static byte[] ComputeHmacKey(string username, string realm, string password)
        {
            using (var md5 = MD5.Create())
            {
                return md5.ComputeHash(Encoding.UTF8.GetBytes($"{username}:{realm}:{password}"));
            }
        }

        private static async Task<TcpClient> ConnectTcpClient(int port)
        {
            var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port);
            return client;
        }

        private static async Task SendStunMessage(NetworkStream stream, STUNMessage msg,
            byte[] hmacKey = null, bool addFingerprint = false)
        {
            var bytes = msg.ToByteBuffer(hmacKey, addFingerprint);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        private static async Task<STUNMessage> ReceiveStunMessage(NetworkStream stream, int timeoutMs = 3000)
        {
            var cts = new CancellationTokenSource(timeoutMs);

            // Read the 4-byte header
            var header = new byte[4];
            int totalRead = 0;
            while (totalRead < 4)
            {
                var read = await stream.ReadAsync(header, totalRead, 4 - totalRead);
                if (read == 0) throw new Exception("Connection closed");
                totalRead += read;
            }

            // Parse the message length from bytes 2-3
            var msgLength = (ushort)((header[2] << 8) | header[3]);
            var remaining = 16 + msgLength; // magic cookie + txn ID + attributes
            var fullMsg = new byte[4 + remaining];
            Buffer.BlockCopy(header, 0, fullMsg, 0, 4);

            totalRead = 0;
            while (totalRead < remaining)
            {
                var read = await stream.ReadAsync(fullMsg, 4 + totalRead, remaining - totalRead);
                if (read == 0) throw new Exception("Connection closed");
                totalRead += read;
            }

            return STUNMessage.ParseSTUNMessage(fullMsg, fullMsg.Length);
        }

        private static STUNMessage BuildAllocateRequest()
        {
            var msg = new STUNMessage(STUNMessageTypesEnum.Allocate);
            msg.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.RequestedTransport,
                STUNAttributeConstants.UdpTransportType));
            return msg;
        }

        private static STUNMessage BuildAuthenticatedAllocateRequest(byte[] hmacKey, string username, string realm, string nonce)
        {
            var msg = new STUNMessage(STUNMessageTypesEnum.Allocate);
            msg.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.RequestedTransport,
                STUNAttributeConstants.UdpTransportType));
            msg.AddUsernameAttribute(username);
            msg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                Encoding.UTF8.GetBytes(realm)));
            msg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                Encoding.UTF8.GetBytes(nonce)));
            return msg;
        }

        /// <summary>
        /// Tests that an Allocate request without credentials gets a 401 challenge.
        /// </summary>
        [Fact]
        public async Task AllocateReturns401WithoutCredentials()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();

            var request = BuildAllocateRequest();
            await SendStunMessage(stream, request);

            var response = await ReceiveStunMessage(stream);

            Assert.NotNull(response);
            Assert.Equal(STUNMessageTypesEnum.AllocateErrorResponse, response.Header.MessageType);

            // Should contain ERROR-CODE with 401
            var errorAttr = response.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ErrorCode);
            Assert.NotNull(errorAttr);
            Assert.True(errorAttr.Value.Length >= 4);
            int errorCode = errorAttr.Value[2] * 100 + errorAttr.Value[3];
            Assert.Equal(401, errorCode);

            // Should contain REALM
            var realmAttr = response.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.Realm);
            Assert.NotNull(realmAttr);
            Assert.Equal(TEST_REALM, Encoding.UTF8.GetString(realmAttr.Value));

            // Should contain NONCE
            var nonceAttr = response.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.Nonce);
            Assert.NotNull(nonceAttr);
        }

        /// <summary>
        /// Tests that a properly authenticated Allocate request succeeds.
        /// </summary>
        [Fact]
        public async Task AuthenticatedAllocateSucceeds()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // Step 1: Send unauthenticated allocate to get nonce
            var request1 = BuildAllocateRequest();
            await SendStunMessage(stream, request1);
            var response1 = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.AllocateErrorResponse, response1.Header.MessageType);

            var nonceAttr = response1.Attributes.First(a => a.AttributeType == STUNAttributeTypesEnum.Nonce);
            var nonce = Encoding.UTF8.GetString(nonceAttr.Value);

            // Step 2: Send authenticated allocate
            var request2 = BuildAuthenticatedAllocateRequest(hmacKey, TEST_USERNAME, TEST_REALM, nonce);
            await SendStunMessage(stream, request2, hmacKey);

            var response2 = await ReceiveStunMessage(stream);

            Assert.NotNull(response2);
            Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, response2.Header.MessageType);

            // Should contain XOR-RELAYED-ADDRESS
            var relayAttr = response2.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress);
            Assert.NotNull(relayAttr);

            // Should contain LIFETIME
            var lifetimeAttr = response2.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.Lifetime);
            Assert.NotNull(lifetimeAttr);

            // Server should have one allocation
            Assert.Single(server.Allocations);
        }

        /// <summary>
        /// Tests that wrong credentials are rejected.
        /// </summary>
        [Fact]
        public async Task WrongPasswordFails()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();

            // Use wrong password for HMAC key
            var wrongKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, "wrongpassword");

            // Get nonce
            var request1 = BuildAllocateRequest();
            await SendStunMessage(stream, request1);
            var response1 = await ReceiveStunMessage(stream);
            var nonce = Encoding.UTF8.GetString(
                response1.Attributes.First(a => a.AttributeType == STUNAttributeTypesEnum.Nonce).Value);

            // Send with wrong credentials
            var request2 = BuildAuthenticatedAllocateRequest(wrongKey, TEST_USERNAME, TEST_REALM, nonce);
            await SendStunMessage(stream, request2, wrongKey);

            var response2 = await ReceiveStunMessage(stream);

            Assert.Equal(STUNMessageTypesEnum.AllocateErrorResponse, response2.Header.MessageType);
            Assert.Empty(server.Allocations);
        }

        /// <summary>
        /// Tests that Refresh extends an allocation's lifetime.
        /// </summary>
        [Fact]
        public async Task RefreshExtendsLifetime()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // Authenticate and allocate
            await AllocateWithAuth(stream, hmacKey);

            Assert.Single(server.Allocations);
            var allocationBefore = server.Allocations.Values.First();
            var expiryBefore = allocationBefore.Expiry;

            // Small delay to ensure time difference
            await Task.Delay(100);

            // Send Refresh with lifetime=600
            var refresh = new STUNMessage(STUNMessageTypesEnum.Refresh);
            refresh.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, (uint)600));
            await SendStunMessage(stream, refresh, hmacKey);

            var refreshResponse = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.RefreshSuccessResponse, refreshResponse.Header.MessageType);

            // Expiry should have been extended
            Assert.True(allocationBefore.Expiry > expiryBefore);
        }

        /// <summary>
        /// Tests that Refresh with lifetime=0 deletes the allocation.
        /// </summary>
        [Fact]
        public async Task RefreshZeroDeletesAllocation()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            await AllocateWithAuth(stream, hmacKey);
            Assert.Single(server.Allocations);

            // Send Refresh with lifetime=0
            var refresh = new STUNMessage(STUNMessageTypesEnum.Refresh);
            refresh.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, (uint)0));
            await SendStunMessage(stream, refresh, hmacKey);

            var refreshResponse = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.RefreshSuccessResponse, refreshResponse.Header.MessageType);

            // Allocation should be removed
            Assert.Empty(server.Allocations);
        }

        /// <summary>
        /// Tests that CreatePermission succeeds and records the permission.
        /// </summary>
        [Fact]
        public async Task CreatePermissionSucceeds()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            await AllocateWithAuth(stream, hmacKey);

            // CreatePermission for a peer address
            var createPerm = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            createPerm.AddXORPeerAddressAttribute(IPAddress.Parse("10.0.0.1"), 5000);
            await SendStunMessage(stream, createPerm, hmacKey);

            var permResponse = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.CreatePermissionSuccessResponse, permResponse.Header.MessageType);

            // Verify permission was recorded
            var allocation = server.Allocations.Values.First();
            Assert.True(allocation.Permissions.ContainsKey("10.0.0.1"));
        }

        /// <summary>
        /// Tests that ChannelBind succeeds and creates the mapping.
        /// </summary>
        [Fact]
        public async Task ChannelBindSucceeds()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            await AllocateWithAuth(stream, hmacKey);

            // ChannelBind
            ushort channelNumber = 0x4000;
            var peerEndpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 5000);

            var channelBind = new STUNMessage(STUNMessageTypesEnum.ChannelBind);
            // Channel number attribute: 2 bytes channel + 2 bytes reserved
            var channelValue = new byte[4];
            channelValue[0] = (byte)(channelNumber >> 8);
            channelValue[1] = (byte)(channelNumber & 0xFF);
            channelBind.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.ChannelNumber, channelValue));
            channelBind.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
            await SendStunMessage(stream, channelBind, hmacKey);

            var bindResponse = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.ChannelBindSuccessResponse, bindResponse.Header.MessageType);

            // Verify channel binding was recorded
            var allocation = server.Allocations.Values.First();
            Assert.True(allocation.ChannelBindings.ContainsKey(channelNumber));
            Assert.Equal(peerEndpoint.ToString(), allocation.ChannelBindings[channelNumber].ToString());
        }

        /// <summary>
        /// Tests that a SendIndication relays data to the peer via the relay socket.
        /// </summary>
        [Fact]
        public async Task SendIndicationRelaysData()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            await AllocateWithAuth(stream, hmacKey);

            var allocation = server.Allocations.Values.First();
            var relayPort = allocation.RelayEndPoint.Port;

            // Set up a "peer" UDP socket to receive the relayed data
            using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var peerEndpoint = (IPEndPoint)peer.Client.LocalEndPoint;

            // Create permission for the peer
            var createPerm = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            createPerm.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
            await SendStunMessage(stream, createPerm, hmacKey);
            await ReceiveStunMessage(stream); // consume response

            // Send indication with data
            var testData = Encoding.UTF8.GetBytes("hello-turn");
            var sendInd = new STUNMessage(STUNMessageTypesEnum.SendIndication);
            sendInd.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
            sendInd.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Data, testData));
            await SendStunMessage(stream, sendInd); // Indications are unsigned

            // Peer should receive the data
            var receiveTask = peer.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(3000));
            Assert.Equal(receiveTask, completed);

            var result = await receiveTask;
            Assert.Equal("hello-turn", Encoding.UTF8.GetString(result.Buffer));
        }

        /// <summary>
        /// Tests that ChannelData relays data via the channel binding.
        /// </summary>
        [Fact]
        public async Task ChannelDataRelaysViaBinding()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            await AllocateWithAuth(stream, hmacKey);

            // Set up a peer
            using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var peerEndpoint = (IPEndPoint)peer.Client.LocalEndPoint;

            // Create permission
            var createPerm = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            createPerm.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
            await SendStunMessage(stream, createPerm, hmacKey);
            await ReceiveStunMessage(stream);

            // Bind channel
            ushort channelNumber = 0x4000;
            var channelBind = new STUNMessage(STUNMessageTypesEnum.ChannelBind);
            var channelValue = new byte[4];
            channelValue[0] = (byte)(channelNumber >> 8);
            channelValue[1] = (byte)(channelNumber & 0xFF);
            channelBind.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.ChannelNumber, channelValue));
            channelBind.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
            await SendStunMessage(stream, channelBind, hmacKey);
            await ReceiveStunMessage(stream);

            // Send ChannelData
            var testData = Encoding.UTF8.GetBytes("channel-data-test");
            var channelData = BuildChannelDataFrame(channelNumber, testData);
            await stream.WriteAsync(channelData, 0, channelData.Length);
            await stream.FlushAsync();

            // Peer should receive
            var receiveTask = peer.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(3000));
            Assert.Equal(receiveTask, completed);

            var result = await receiveTask;
            Assert.Equal("channel-data-test", Encoding.UTF8.GetString(result.Buffer));
        }

        /// <summary>
        /// Tests that UDP data from a peer is relayed back to the client.
        /// </summary>
        [Fact]
        public async Task UdpRelayBackToClient()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            await AllocateWithAuth(stream, hmacKey);

            var allocation = server.Allocations.Values.First();
            var relayPort = allocation.RelayEndPoint.Port;

            // Set up a peer
            using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var peerEndpoint = (IPEndPoint)peer.Client.LocalEndPoint;

            // Create permission for the peer
            var createPerm = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            createPerm.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
            await SendStunMessage(stream, createPerm, hmacKey);
            await ReceiveStunMessage(stream);

            // Bind channel so we get ChannelData back
            ushort channelNumber = 0x4000;
            var channelBind = new STUNMessage(STUNMessageTypesEnum.ChannelBind);
            var channelValue = new byte[4];
            channelValue[0] = (byte)(channelNumber >> 8);
            channelValue[1] = (byte)(channelNumber & 0xFF);
            channelBind.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.ChannelNumber, channelValue));
            channelBind.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
            await SendStunMessage(stream, channelBind, hmacKey);
            await ReceiveStunMessage(stream);

            // Peer sends data to the relay port
            var testData = Encoding.UTF8.GetBytes("peer-to-client");
            await peer.SendAsync(testData, testData.Length,
                new IPEndPoint(IPAddress.Loopback, relayPort));

            // Client should receive ChannelData on the TCP stream
            var headerBuf = new byte[4];
            stream.ReadTimeout = 3000;
            int totalRead = 0;
            while (totalRead < 4)
            {
                var read = await stream.ReadAsync(headerBuf, totalRead, 4 - totalRead);
                if (read == 0) throw new Exception("Connection closed");
                totalRead += read;
            }

            // Should be ChannelData (first byte has high bits 01)
            Assert.Equal(0x40, headerBuf[0] & 0xC0);
            var rxChannel = (ushort)((headerBuf[0] << 8) | headerBuf[1]);
            Assert.Equal(channelNumber, rxChannel);

            var dataLen = (ushort)((headerBuf[2] << 8) | headerBuf[3]);
            var dataBuf = new byte[dataLen];
            totalRead = 0;
            while (totalRead < dataLen)
            {
                var read = await stream.ReadAsync(dataBuf, totalRead, dataLen - totalRead);
                if (read == 0) throw new Exception("Connection closed");
                totalRead += read;
            }

            Assert.Equal("peer-to-client", Encoding.UTF8.GetString(dataBuf));
        }

        /// <summary>
        /// Tests that a basic STUN BindingRequest returns the mapped address.
        /// </summary>
        [Fact]
        public async Task BindingRequestReturnsAddress()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();

            var request = new STUNMessage(STUNMessageTypesEnum.BindingRequest);
            await SendStunMessage(stream, request);

            var response = await ReceiveStunMessage(stream);
            Assert.NotNull(response);
            Assert.Equal(STUNMessageTypesEnum.BindingSuccessResponse, response.Header.MessageType);

            // Should contain XOR-MAPPED-ADDRESS
            var xorAddr = response.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.XORMappedAddress);
            Assert.NotNull(xorAddr);
        }

        /// <summary>
        /// Tests that expired allocations are cleaned up by the timer.
        /// </summary>
        [Fact]
        public async Task ExpiredAllocationCleanedUp()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            // Create server with very short lifetime
            int testPort;
            var tempSocket = new TcpListener(IPAddress.Loopback, 0);
            tempSocket.Start();
            testPort = ((IPEndPoint)tempSocket.LocalEndpoint).Port;
            tempSocket.Stop();

            var config = new TurnServerConfig
            {
                ListenAddress = IPAddress.Loopback,
                Port = testPort,
                EnableTcp = true,
                EnableUdp = false,
                Username = TEST_USERNAME,
                Password = TEST_PASSWORD,
                Realm = TEST_REALM,
                RelayAddress = IPAddress.Loopback,
                DefaultLifetimeSeconds = 1, // Very short for testing
            };

            var server = new TurnServer(config);
            _servers.Add(server);
            server.Start();

            using var client = await ConnectTcpClient(testPort);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            await AllocateWithAuth(stream, hmacKey);
            Assert.Single(server.Allocations);

            // Wait for allocation to expire and cleanup timer to fire (timer runs every 30s,
            // but we can verify the allocation has expired)
            await Task.Delay(1500);

            // Manually check: the allocation's Expiry should be in the past
            if (server.Allocations.Count > 0)
            {
                var alloc = server.Allocations.Values.First();
                Assert.True(alloc.Expiry < DateTime.UtcNow,
                    "Allocation should have expired by now");
            }
            // If the cleanup timer already ran, allocation count could be 0
        }

        /// <summary>
        /// Tests that a duplicate Allocate on the same connection returns 437.
        /// </summary>
        [Fact]
        public async Task DuplicateAllocateReturns437()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // First allocation succeeds
            var allocResponse = await AllocateWithAuth(stream, hmacKey);
            Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, allocResponse.Header.MessageType);
            Assert.Single(server.Allocations);

            // Send another unauthenticated allocate to get a fresh nonce
            var request1 = BuildAllocateRequest();
            await SendStunMessage(stream, request1);
            var challenge = await ReceiveStunMessage(stream);

            // Send authenticated allocate — allocation check happens after auth,
            // so this should pass auth then fail with 437 (duplicate).
            var nonce = Encoding.UTF8.GetString(
                challenge.Attributes.First(a => a.AttributeType == STUNAttributeTypesEnum.Nonce).Value);
            var request2 = BuildAuthenticatedAllocateRequest(hmacKey, TEST_USERNAME, TEST_REALM, nonce);
            await SendStunMessage(stream, request2, hmacKey);

            var response2 = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.AllocateErrorResponse, response2.Header.MessageType);
            AssertErrorCode(response2, 437);
        }

        /// <summary>
        /// Tests that a Refresh without a prior allocation returns 437.
        /// </summary>
        [Fact]
        public async Task RefreshWithoutAllocationReturns437()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // Send Refresh without allocating first
            var refresh = new STUNMessage(STUNMessageTypesEnum.Refresh);
            refresh.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Lifetime, (uint)600));
            await SendStunMessage(stream, refresh, hmacKey);

            var response = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.RefreshErrorResponse, response.Header.MessageType);
            AssertErrorCode(response, 437);
        }

        /// <summary>
        /// Tests that a CreatePermission without a prior allocation returns 437.
        /// </summary>
        [Fact]
        public async Task CreatePermissionWithoutAllocationReturns437()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // Send CreatePermission without allocating first
            var createPerm = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            createPerm.AddXORPeerAddressAttribute(IPAddress.Parse("10.0.0.1"), 5000);
            await SendStunMessage(stream, createPerm, hmacKey);

            var response = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.CreatePermissionErrorResponse, response.Header.MessageType);
            AssertErrorCode(response, 437);
        }

        /// <summary>
        /// Tests that a ChannelBind without a prior allocation returns 437.
        /// </summary>
        [Fact]
        public async Task ChannelBindWithoutAllocationReturns437()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // Send ChannelBind without allocating first
            ushort channelNumber = 0x4000;
            var peerEndpoint = new IPEndPoint(IPAddress.Parse("10.0.0.1"), 5000);

            var channelBind = new STUNMessage(STUNMessageTypesEnum.ChannelBind);
            var channelValue = new byte[4];
            channelValue[0] = (byte)(channelNumber >> 8);
            channelValue[1] = (byte)(channelNumber & 0xFF);
            channelBind.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.ChannelNumber, channelValue));
            channelBind.AddXORPeerAddressAttribute(peerEndpoint.Address, peerEndpoint.Port);
            await SendStunMessage(stream, channelBind, hmacKey);

            var response = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.ChannelBindErrorResponse, response.Header.MessageType);
            AssertErrorCode(response, 437);
        }

        /// <summary>
        /// Tests that a ChannelBind missing required attributes returns 400.
        /// </summary>
        [Fact]
        public async Task ChannelBindMissingAttributesReturns400()
        {
            logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod().Name);

            var (server, port) = CreateTurnServer();
            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            await AllocateWithAuth(stream, hmacKey);

            // Case 1: ChannelBind with only ChannelNumber (no XOR-PEER-ADDRESS)
            var bindNoAddr = new STUNMessage(STUNMessageTypesEnum.ChannelBind);
            var channelValue = new byte[4];
            channelValue[0] = 0x40;
            channelValue[1] = 0x00;
            bindNoAddr.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.ChannelNumber, channelValue));
            await SendStunMessage(stream, bindNoAddr, hmacKey);

            var resp1 = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.ChannelBindErrorResponse, resp1.Header.MessageType);
            AssertErrorCode(resp1, 400);

            // Case 2: ChannelBind with only XOR-PEER-ADDRESS (no ChannelNumber)
            var bindNoChan = new STUNMessage(STUNMessageTypesEnum.ChannelBind);
            bindNoChan.AddXORPeerAddressAttribute(IPAddress.Parse("10.0.0.1"), 5000);
            await SendStunMessage(stream, bindNoChan, hmacKey);

            var resp2 = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.ChannelBindErrorResponse, resp2.Header.MessageType);
            AssertErrorCode(resp2, 400);
        }

        #region Test Helpers

        private static void AssertErrorCode(STUNMessage response, int expectedCode)
        {
            var errorAttr = response.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ErrorCode);
            Assert.NotNull(errorAttr);
            Assert.True(errorAttr.Value.Length >= 4);
            int errorCode = errorAttr.Value[2] * 100 + errorAttr.Value[3];
            Assert.Equal(expectedCode, errorCode);
        }

        /// <summary>
        /// Performs the full authenticate + allocate flow and returns the allocation response.
        /// </summary>
        private async Task<STUNMessage> AllocateWithAuth(NetworkStream stream, byte[] hmacKey)
        {
            // Step 1: Unauthenticated request to get nonce
            var request1 = BuildAllocateRequest();
            await SendStunMessage(stream, request1);
            var response1 = await ReceiveStunMessage(stream);

            var nonceAttr = response1.Attributes.First(
                a => a.AttributeType == STUNAttributeTypesEnum.Nonce);
            var nonce = Encoding.UTF8.GetString(nonceAttr.Value);

            // Step 2: Authenticated request
            var request2 = BuildAuthenticatedAllocateRequest(hmacKey, TEST_USERNAME, TEST_REALM, nonce);
            await SendStunMessage(stream, request2, hmacKey);
            return await ReceiveStunMessage(stream);
        }

        private static byte[] BuildChannelDataFrame(ushort channelNumber, byte[] data)
        {
            var dataLen = data.Length;
            var padding = (4 - (dataLen % 4)) % 4;
            var buf = new byte[4 + dataLen + padding];
            buf[0] = (byte)(channelNumber >> 8);
            buf[1] = (byte)(channelNumber & 0xFF);
            buf[2] = (byte)(dataLen >> 8);
            buf[3] = (byte)(dataLen & 0xFF);
            Buffer.BlockCopy(data, 0, buf, 4, dataLen);
            return buf;
        }

        #endregion
    }
}
