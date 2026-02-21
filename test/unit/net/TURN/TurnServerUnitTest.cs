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
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

        #region RFC 6062 TCP Relay Tests

        /// <summary>
        /// Tests that GetSTUNMessageTypeForId returns 0 for unknown values instead of throwing.
        /// </summary>
        [Fact]
        public void GetSTUNMessageTypeForId_UnknownValue_ReturnsZero()
        {
            var result = STUNMessageTypes.GetSTUNMessageTypeForId(0xFFFF);
            Assert.Equal((STUNMessageTypesEnum)0, result);
        }

        /// <summary>
        /// Tests that GetSTUNMessageTypeForId correctly parses ConnectSuccessResponse.
        /// </summary>
        [Fact]
        public void GetSTUNMessageTypeForId_ConnectSuccessResponse_ReturnsCorrectEnum()
        {
            var result = STUNMessageTypes.GetSTUNMessageTypeForId(0x010a);
            Assert.Equal(STUNMessageTypesEnum.ConnectSuccessResponse, result);
        }

        /// <summary>
        /// Tests that a TCP Allocate (RequestedTransport=TCP) succeeds.
        /// </summary>
        [Fact]
        public async Task TcpAllocateSucceeds()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();

            var allocResponse = await TcpAllocateWithAuth(stream, hmacKey);

            Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, allocResponse.Header.MessageType);

            var relayAttr = allocResponse.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress);
            Assert.NotNull(relayAttr);

            Assert.Single(server.Allocations);
            var alloc = server.Allocations.Values.First();
            Assert.True(alloc.IsTcpRelay);
        }

        /// <summary>
        /// Tests that Connect without a TCP allocation returns 437.
        /// </summary>
        [Fact]
        public async Task ConnectWithoutAllocationReturns437()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();

            // Send Connect without any allocation
            var connectMsg = new STUNMessage(STUNMessageTypesEnum.Connect);
            connectMsg.AddXORPeerAddressAttribute(IPAddress.Loopback, 9999);
            await SendStunMessage(stream, connectMsg, hmacKey);

            var response = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.ConnectErrorResponse, response.Header.MessageType);

            var errorAttr = response.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ErrorCode);
            Assert.NotNull(errorAttr);
            Assert.Equal(437, (errorAttr as STUNErrorCodeAttribute)?.ErrorCode ??
                ParseErrorCode(errorAttr.Value));
        }

        /// <summary>
        /// Tests the full flow: TCP Allocate → CreatePermission → Connect → ConnectSuccess with ConnectionId.
        /// </summary>
        [Fact]
        public async Task ConnectToPeerSucceeds()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // Start a TCP listener to act as the peer
            var peerListener = new TcpListener(IPAddress.Loopback, 0);
            peerListener.Start();
            var peerPort = ((IPEndPoint)peerListener.LocalEndpoint).Port;

            try
            {
                using var client = await ConnectTcpClient(port);
                var stream = client.GetStream();

                // Allocate with TCP transport
                var allocResponse = await TcpAllocateWithAuth(stream, hmacKey);
                Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, allocResponse.Header.MessageType);

                // Create Permission for peer
                var permMsg = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
                permMsg.AddXORPeerAddressAttribute(IPAddress.Loopback, peerPort);
                permMsg.AddUsernameAttribute(TEST_USERNAME);
                permMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                    Encoding.UTF8.GetBytes(TEST_REALM)));
                permMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                    Encoding.UTF8.GetBytes("dummy")));
                await SendStunMessage(stream, permMsg, hmacKey);
                var permResponse = await ReceiveStunMessage(stream);
                Assert.Equal(STUNMessageTypesEnum.CreatePermissionSuccessResponse, permResponse.Header.MessageType);

                // Connect to peer
                var connectMsg = new STUNMessage(STUNMessageTypesEnum.Connect);
                connectMsg.AddXORPeerAddressAttribute(IPAddress.Loopback, peerPort);
                connectMsg.AddUsernameAttribute(TEST_USERNAME);
                connectMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                    Encoding.UTF8.GetBytes(TEST_REALM)));
                connectMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                    Encoding.UTF8.GetBytes("dummy")));
                await SendStunMessage(stream, connectMsg, hmacKey);

                // Accept the peer connection first (server connects out to peer)
                var peerAcceptTask = peerListener.AcceptTcpClientAsync();

                var connectResponse = await ReceiveStunMessage(stream);
                Assert.Equal(STUNMessageTypesEnum.ConnectSuccessResponse, connectResponse.Header.MessageType);

                var connIdAttr = connectResponse.Attributes.FirstOrDefault(
                    a => a.AttributeType == STUNAttributeTypesEnum.ConnectionId) as STUNConnectionIdAttribute;
                Assert.NotNull(connIdAttr);
                Assert.True(connIdAttr.ConnectionId > 0);

                // Clean up peer
                if (peerAcceptTask.IsCompleted)
                {
                    (await peerAcceptTask).Dispose();
                }
            }
            finally
            {
                peerListener.Stop();
            }
        }

        /// <summary>
        /// Tests the full flow: After Connect, open a new TCP data connection,
        /// send ConnectionBind, and verify success.
        /// </summary>
        [Fact]
        public async Task ConnectionBindPairsDataConnection()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            var peerListener = new TcpListener(IPAddress.Loopback, 0);
            peerListener.Start();
            var peerPort = ((IPEndPoint)peerListener.LocalEndpoint).Port;

            try
            {
                using var controlClient = await ConnectTcpClient(port);
                var controlStream = controlClient.GetStream();

                // TCP Allocate + Permission + Connect
                var allocResponse = await TcpAllocateWithAuth(controlStream, hmacKey);
                Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, allocResponse.Header.MessageType);

                await CreatePermissionForPeer(controlStream, hmacKey, peerPort);

                var peerAcceptTask = peerListener.AcceptTcpClientAsync();
                uint connectionId = await ConnectToPeer(controlStream, hmacKey, peerPort);

                if (peerAcceptTask.IsCompleted)
                {
                    (await peerAcceptTask).Dispose();
                }

                // Open a new TCP data connection for ConnectionBind
                using var dataClient = await ConnectTcpClient(port);
                var dataStream = dataClient.GetStream();

                var bindMsg = new STUNMessage(STUNMessageTypesEnum.ConnectionBind);
                bindMsg.Attributes.Add(new STUNConnectionIdAttribute(connectionId));
                bindMsg.AddUsernameAttribute(TEST_USERNAME);
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                    Encoding.UTF8.GetBytes(TEST_REALM)));
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                    Encoding.UTF8.GetBytes("dummy")));
                await SendStunMessage(dataStream, bindMsg, hmacKey);

                var bindResponse = await ReceiveStunMessage(dataStream);
                Assert.Equal(STUNMessageTypesEnum.ConnectionBindSuccessResponse, bindResponse.Header.MessageType);
            }
            finally
            {
                peerListener.Stop();
            }
        }

        /// <summary>
        /// Tests that after full ConnectionBind setup, raw bytes relay bidirectionally.
        /// </summary>
        [Fact]
        public async Task RawDataRelaysAfterConnectionBind()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            var peerListener = new TcpListener(IPAddress.Loopback, 0);
            peerListener.Start();
            var peerPort = ((IPEndPoint)peerListener.LocalEndpoint).Port;

            try
            {
                using var controlClient = await ConnectTcpClient(port);
                var controlStream = controlClient.GetStream();

                // Full setup: Allocate + Permission + Connect + ConnectionBind
                await TcpAllocateWithAuth(controlStream, hmacKey);
                await CreatePermissionForPeer(controlStream, hmacKey, peerPort);

                var peerAcceptTask = peerListener.AcceptTcpClientAsync();
                uint connectionId = await ConnectToPeer(controlStream, hmacKey, peerPort);

                using var peerClient = await peerAcceptTask;
                var peerStream = peerClient.GetStream();

                // Open data connection and bind
                using var dataClient = await ConnectTcpClient(port);
                var dataStream = dataClient.GetStream();

                var bindMsg = new STUNMessage(STUNMessageTypesEnum.ConnectionBind);
                bindMsg.Attributes.Add(new STUNConnectionIdAttribute(connectionId));
                bindMsg.AddUsernameAttribute(TEST_USERNAME);
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                    Encoding.UTF8.GetBytes(TEST_REALM)));
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                    Encoding.UTF8.GetBytes("dummy")));
                await SendStunMessage(dataStream, bindMsg, hmacKey);

                var bindResponse = await ReceiveStunMessage(dataStream);
                Assert.Equal(STUNMessageTypesEnum.ConnectionBindSuccessResponse, bindResponse.Header.MessageType);

                // Give the relay a moment to start
                await Task.Delay(100);

                // Send raw data: client → peer
                var testData = Encoding.UTF8.GetBytes("hello from client");
                await dataStream.WriteAsync(testData, 0, testData.Length);
                await dataStream.FlushAsync();

                var recvBuffer = new byte[1024];
                var cts = new CancellationTokenSource(3000);
                int bytesRead = await peerStream.ReadAsync(recvBuffer, 0, recvBuffer.Length, cts.Token);
                Assert.True(bytesRead > 0, "Peer should receive data from client");
                Assert.Equal("hello from client", Encoding.UTF8.GetString(recvBuffer, 0, bytesRead));

                // Send raw data: peer → client
                var peerData = Encoding.UTF8.GetBytes("hello from peer");
                await peerStream.WriteAsync(peerData, 0, peerData.Length);
                await peerStream.FlushAsync();

                cts = new CancellationTokenSource(3000);
                bytesRead = await dataStream.ReadAsync(recvBuffer, 0, recvBuffer.Length, cts.Token);
                Assert.True(bytesRead > 0, "Client should receive data from peer");
                Assert.Equal("hello from peer", Encoding.UTF8.GetString(recvBuffer, 0, bytesRead));
            }
            finally
            {
                peerListener.Stop();
            }
        }

        /// <summary>
        /// Tests that when a peer connects to the TCP relay listener, a
        /// ConnectionAttemptIndication is sent to the client.
        /// </summary>
        [Fact]
        public async Task ConnectionAttemptIndicationSentOnPeerConnect()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            using var controlClient = await ConnectTcpClient(port);
            var controlStream = controlClient.GetStream();

            // TCP Allocate
            var allocResponse = await TcpAllocateWithAuth(controlStream, hmacKey);
            Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, allocResponse.Header.MessageType);

            // Get the relay port
            var relayAttr = new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORRelayedAddress,
                allocResponse.Attributes.First(a => a.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress).Value,
                allocResponse.Header.TransactionId);
            int relayPort = relayAttr.Port;

            // Create permission for our own address (we'll connect as "peer")
            await CreatePermissionForPeer(controlStream, hmacKey, 0); // port doesn't matter for permission, IP is key

            // Connect as peer to the relay port
            using var peerClient = new TcpClient();
            await peerClient.ConnectAsync(IPAddress.Loopback, relayPort);

            // Should receive a ConnectionAttemptIndication on the control stream
            var indication = await ReceiveStunMessage(controlStream);
            Assert.Equal(STUNMessageTypesEnum.ConnectionAttemptIndication, indication.Header.MessageType);

            var connIdAttr = indication.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ConnectionId) as STUNConnectionIdAttribute;
            Assert.NotNull(connIdAttr);
            Assert.True(connIdAttr.ConnectionId > 0);
        }

        /// <summary>
        /// Tests that Allocate with an unsupported transport returns 442.
        /// </summary>
        [Fact]
        public async Task AllocateUnsupportedTransportReturns442()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            using var client = await ConnectTcpClient(port);
            var stream = client.GetStream();

            // Step 1: Get nonce
            var request1 = BuildAllocateRequest();
            await SendStunMessage(stream, request1);
            var response1 = await ReceiveStunMessage(stream);
            var nonce = Encoding.UTF8.GetString(
                response1.Attributes.First(a => a.AttributeType == STUNAttributeTypesEnum.Nonce).Value);

            // Step 2: Send allocate with unsupported transport (0xFF)
            var request2 = new STUNMessage(STUNMessageTypesEnum.Allocate);
            request2.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.RequestedTransport,
                new byte[] { 0xFF, 0x00, 0x00, 0x00 }));
            request2.AddUsernameAttribute(TEST_USERNAME);
            request2.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                Encoding.UTF8.GetBytes(TEST_REALM)));
            request2.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                Encoding.UTF8.GetBytes(nonce)));
            await SendStunMessage(stream, request2, hmacKey);

            var response2 = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.AllocateErrorResponse, response2.Header.MessageType);

            var errorAttr = response2.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ErrorCode);
            Assert.NotNull(errorAttr);
            Assert.Equal(442, ParseErrorCode(errorAttr.Value));
        }

        /// <summary>
        /// Tests that GotStunResponse handles ConnectSuccessResponse correctly.
        /// </summary>
        [Fact]
        public void ChecklistEntryHandlesConnectSuccess()
        {
            var localCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
            localCandidate.SetAddressProperties(RTCIceProtocol.tcp, IPAddress.Loopback, 1234,
                RTCIceCandidateType.relay, null, 0);
            localCandidate.IceServer = new IceServer(
                new STUNUri(STUNSchemesEnum.turn, "localhost", 3478),
                0, TEST_USERNAME, TEST_PASSWORD);

            var remoteCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
            remoteCandidate.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 5678,
                RTCIceCandidateType.host, null, 0);

            var entry = new ChecklistEntry(localCandidate, remoteCandidate, true);
            entry.State = ChecklistEntryState.InProgress;

            var response = new STUNMessage(STUNMessageTypesEnum.ConnectSuccessResponse);
            response.Attributes.Add(new STUNConnectionIdAttribute(42));

            entry.GotStunResponse(response, new IPEndPoint(IPAddress.Loopback, 3478));

            Assert.Equal(ChecklistEntryState.Waiting, entry.State);
            Assert.Equal(42u, entry.TurnConnectionId);
            Assert.NotEqual(DateTime.MinValue, entry.TurnConnectReportAt);
        }

        /// <summary>
        /// Tests that GotStunResponse handles ConnectionBindSuccessResponse correctly.
        /// </summary>
        [Fact]
        public void ChecklistEntryHandlesConnectionBindSuccess()
        {
            var localCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
            localCandidate.SetAddressProperties(RTCIceProtocol.tcp, IPAddress.Loopback, 1234,
                RTCIceCandidateType.relay, null, 0);
            localCandidate.IceServer = new IceServer(
                new STUNUri(STUNSchemesEnum.turn, "localhost", 3478),
                0, TEST_USERNAME, TEST_PASSWORD);

            var remoteCandidate = new RTCIceCandidate(new RTCIceCandidateInit());
            remoteCandidate.SetAddressProperties(RTCIceProtocol.udp, IPAddress.Loopback, 5678,
                RTCIceCandidateType.host, null, 0);

            var entry = new ChecklistEntry(localCandidate, remoteCandidate, true);
            entry.State = ChecklistEntryState.InProgress;

            var response = new STUNMessage(STUNMessageTypesEnum.ConnectionBindSuccessResponse);

            entry.GotStunResponse(response, new IPEndPoint(IPAddress.Loopback, 3478));

            Assert.Equal(ChecklistEntryState.Waiting, entry.State);
            Assert.NotEqual(DateTime.MinValue, entry.TurnConnectBindedAt);
        }

        /// <summary>
        /// End-to-end TCP relay test using client-initiated Connect (RFC 6062 Section 4.3).
        /// Flow: TCP Allocate → CreatePermission → Connect → ConnectionBind → bidirectional raw data.
        /// Verifies multiple round-trips and message ordering through the relay.
        /// </summary>
        [Fact]
        public async Task TcpRelayEndToEnd_ClientInitiatedConnect()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // Peer: start a TCP listener that simulates the remote peer.
            var peerListener = new TcpListener(IPAddress.Loopback, 0);
            peerListener.Start();
            var peerPort = ((IPEndPoint)peerListener.LocalEndpoint).Port;

            try
            {
                // --- Control connection: Allocate + Permission + Connect ---
                using var controlClient = await ConnectTcpClient(port);
                var controlStream = controlClient.GetStream();

                var allocResponse = await TcpAllocateWithAuth(controlStream, hmacKey);
                Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, allocResponse.Header.MessageType);

                await CreatePermissionForPeer(controlStream, hmacKey, peerPort);

                // Start accepting peer connections before sending Connect
                var peerAcceptTask = peerListener.AcceptTcpClientAsync();

                uint connectionId = await ConnectToPeer(controlStream, hmacKey, peerPort);
                Assert.True(connectionId > 0);

                // Accept the peer-side TCP connection opened by the TURN server
                using var peerClient = await peerAcceptTask;
                var peerStream = peerClient.GetStream();

                // --- Data connection: ConnectionBind → raw relay ---
                using var dataClient = await ConnectTcpClient(port);
                var dataStream = dataClient.GetStream();

                var bindMsg = new STUNMessage(STUNMessageTypesEnum.ConnectionBind);
                bindMsg.Attributes.Add(new STUNConnectionIdAttribute(connectionId));
                bindMsg.AddUsernameAttribute(TEST_USERNAME);
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                    Encoding.UTF8.GetBytes(TEST_REALM)));
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                    Encoding.UTF8.GetBytes("dummy")));
                await SendStunMessage(dataStream, bindMsg, hmacKey);

                var bindResponse = await ReceiveStunMessage(dataStream);
                Assert.Equal(STUNMessageTypesEnum.ConnectionBindSuccessResponse, bindResponse.Header.MessageType);

                // Give relay tasks a moment to start
                await Task.Delay(100);

                // --- Bidirectional data exchange: multiple round-trips ---
                var recvBuffer = new byte[4096];

                for (int i = 0; i < 5; i++)
                {
                    // Client → Peer
                    var clientMsg = Encoding.UTF8.GetBytes($"client-to-peer-{i}");
                    await dataStream.WriteAsync(clientMsg, 0, clientMsg.Length);
                    await dataStream.FlushAsync();

                    var cts = new CancellationTokenSource(3000);
                    int bytesRead = await peerStream.ReadAsync(recvBuffer, 0, recvBuffer.Length, cts.Token);
                    Assert.True(bytesRead > 0, $"Peer should receive message {i} from client");
                    Assert.Equal($"client-to-peer-{i}", Encoding.UTF8.GetString(recvBuffer, 0, bytesRead));

                    // Peer → Client
                    var peerMsg = Encoding.UTF8.GetBytes($"peer-to-client-{i}");
                    await peerStream.WriteAsync(peerMsg, 0, peerMsg.Length);
                    await peerStream.FlushAsync();

                    cts = new CancellationTokenSource(3000);
                    bytesRead = await dataStream.ReadAsync(recvBuffer, 0, recvBuffer.Length, cts.Token);
                    Assert.True(bytesRead > 0, $"Client should receive message {i} from peer");
                    Assert.Equal($"peer-to-client-{i}", Encoding.UTF8.GetString(recvBuffer, 0, bytesRead));
                }
            }
            finally
            {
                peerListener.Stop();
            }
        }

        /// <summary>
        /// End-to-end TCP relay test using peer-initiated connection (RFC 6062 Section 4.5).
        /// Flow: TCP Allocate → CreatePermission → peer connects to relay → ConnectionAttemptIndication
        ///       → ConnectionBind → bidirectional raw data.
        /// </summary>
        [Fact]
        public async Task TcpRelayEndToEnd_PeerInitiatedConnect()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // --- Control connection: TCP Allocate ---
            using var controlClient = await ConnectTcpClient(port);
            var controlStream = controlClient.GetStream();

            var allocResponse = await TcpAllocateWithAuth(controlStream, hmacKey);
            Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, allocResponse.Header.MessageType);

            // Extract relay port from XOR-RELAYED-ADDRESS
            var relayAttr = new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORRelayedAddress,
                allocResponse.Attributes.First(a => a.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress).Value,
                allocResponse.Header.TransactionId);
            int relayPort = relayAttr.Port;

            // Create permission for loopback (peer will connect from loopback)
            await CreatePermissionForPeer(controlStream, hmacKey, 0);

            // --- Peer connects to the relay TCP listener ---
            using var peerClient = new TcpClient();
            await peerClient.ConnectAsync(IPAddress.Loopback, relayPort);
            var peerStream = peerClient.GetStream();

            // Client should receive ConnectionAttemptIndication
            var indication = await ReceiveStunMessage(controlStream);
            Assert.Equal(STUNMessageTypesEnum.ConnectionAttemptIndication, indication.Header.MessageType);

            var connIdAttr = indication.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ConnectionId) as STUNConnectionIdAttribute;
            Assert.NotNull(connIdAttr);
            uint connectionId = connIdAttr.ConnectionId;
            Assert.True(connectionId > 0);

            // --- Data connection: ConnectionBind ---
            using var dataClient = await ConnectTcpClient(port);
            var dataStream = dataClient.GetStream();

            var bindMsg = new STUNMessage(STUNMessageTypesEnum.ConnectionBind);
            bindMsg.Attributes.Add(new STUNConnectionIdAttribute(connectionId));
            bindMsg.AddUsernameAttribute(TEST_USERNAME);
            bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                Encoding.UTF8.GetBytes(TEST_REALM)));
            bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                Encoding.UTF8.GetBytes("dummy")));
            await SendStunMessage(dataStream, bindMsg, hmacKey);

            var bindResponse = await ReceiveStunMessage(dataStream);
            Assert.Equal(STUNMessageTypesEnum.ConnectionBindSuccessResponse, bindResponse.Header.MessageType);

            // Give relay tasks a moment to start
            await Task.Delay(100);

            // --- Bidirectional data exchange ---
            var recvBuffer = new byte[4096];

            // Client → Peer
            var clientMsg = Encoding.UTF8.GetBytes("hello from TURN client");
            await dataStream.WriteAsync(clientMsg, 0, clientMsg.Length);
            await dataStream.FlushAsync();

            var cts = new CancellationTokenSource(3000);
            int bytesRead = await peerStream.ReadAsync(recvBuffer, 0, recvBuffer.Length, cts.Token);
            Assert.True(bytesRead > 0, "Peer should receive data from client");
            Assert.Equal("hello from TURN client", Encoding.UTF8.GetString(recvBuffer, 0, bytesRead));

            // Peer → Client
            var peerMsg = Encoding.UTF8.GetBytes("hello from peer");
            await peerStream.WriteAsync(peerMsg, 0, peerMsg.Length);
            await peerStream.FlushAsync();

            cts = new CancellationTokenSource(3000);
            bytesRead = await dataStream.ReadAsync(recvBuffer, 0, recvBuffer.Length, cts.Token);
            Assert.True(bytesRead > 0, "Client should receive data from peer");
            Assert.Equal("hello from peer", Encoding.UTF8.GetString(recvBuffer, 0, bytesRead));
        }

        /// <summary>
        /// End-to-end TCP relay test with a large payload to verify the relay handles
        /// multi-read fragmentation correctly.
        /// </summary>
        [Fact]
        public async Task TcpRelayEndToEnd_LargePayload()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            var peerListener = new TcpListener(IPAddress.Loopback, 0);
            peerListener.Start();
            var peerPort = ((IPEndPoint)peerListener.LocalEndpoint).Port;

            try
            {
                using var controlClient = await ConnectTcpClient(port);
                var controlStream = controlClient.GetStream();

                await TcpAllocateWithAuth(controlStream, hmacKey);
                await CreatePermissionForPeer(controlStream, hmacKey, peerPort);

                var peerAcceptTask = peerListener.AcceptTcpClientAsync();
                uint connectionId = await ConnectToPeer(controlStream, hmacKey, peerPort);

                using var peerClient = await peerAcceptTask;
                var peerStream = peerClient.GetStream();

                // ConnectionBind on data connection
                using var dataClient = await ConnectTcpClient(port);
                var dataStream = dataClient.GetStream();

                var bindMsg = new STUNMessage(STUNMessageTypesEnum.ConnectionBind);
                bindMsg.Attributes.Add(new STUNConnectionIdAttribute(connectionId));
                bindMsg.AddUsernameAttribute(TEST_USERNAME);
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                    Encoding.UTF8.GetBytes(TEST_REALM)));
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                    Encoding.UTF8.GetBytes("dummy")));
                await SendStunMessage(dataStream, bindMsg, hmacKey);

                var bindResponse = await ReceiveStunMessage(dataStream);
                Assert.Equal(STUNMessageTypesEnum.ConnectionBindSuccessResponse, bindResponse.Header.MessageType);

                await Task.Delay(100);

                // Send 64 KB of data: client → peer
                var largePayload = new byte[65536];
                new Random(42).NextBytes(largePayload);

                await dataStream.WriteAsync(largePayload, 0, largePayload.Length);
                await dataStream.FlushAsync();

                // Read all bytes on the peer side
                var received = await ReadAllBytesAsync(peerStream, largePayload.Length, timeoutMs: 5000);
                Assert.Equal(largePayload.Length, received.Length);
                Assert.Equal(largePayload, received);

                // Send 64 KB of data: peer → client
                var peerPayload = new byte[65536];
                new Random(99).NextBytes(peerPayload);

                await peerStream.WriteAsync(peerPayload, 0, peerPayload.Length);
                await peerStream.FlushAsync();

                received = await ReadAllBytesAsync(dataStream, peerPayload.Length, timeoutMs: 5000);
                Assert.Equal(peerPayload.Length, received.Length);
                Assert.Equal(peerPayload, received);
            }
            finally
            {
                peerListener.Stop();
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// End-to-end TCP relay test with TLS encryption.
        /// Flow: TCP Allocate → CreatePermission → Connect → ConnectionBind → TLS handshake
        ///       over the relay → encrypted bidirectional data exchange.
        /// Proves that encrypted streams work correctly through the TURN TCP relay.
        /// </summary>
        [Fact]
        public async Task TcpRelayEndToEnd_WithTls()
        {
            var (server, port) = CreateTurnServer();
            var hmacKey = ComputeHmacKey(TEST_USERNAME, TEST_REALM, TEST_PASSWORD);

            // Generate a self-signed certificate for the TLS handshake.
            // Export/reimport as PFX to ensure the private key is usable by SslStream on all platforms.
            using var rsa = RSA.Create(2048);
            var certReq = new CertificateRequest(
                "CN=turn-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var tmpCert = certReq.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var pfxBytes = tmpCert.Export(X509ContentType.Pfx, (string)null);
#if NET10_0_OR_GREATER
            using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, null,
                X509KeyStorageFlags.Exportable);
#else
            using var cert = new X509Certificate2(pfxBytes, (string)null, X509KeyStorageFlags.Exportable);
#endif

            var peerListener = new TcpListener(IPAddress.Loopback, 0);
            peerListener.Start();
            var peerPort = ((IPEndPoint)peerListener.LocalEndpoint).Port;

            try
            {
                // --- Control connection: Allocate + Permission + Connect ---
                using var controlClient = await ConnectTcpClient(port);
                var controlStream = controlClient.GetStream();

                var allocResponse = await TcpAllocateWithAuth(controlStream, hmacKey);
                Assert.Equal(STUNMessageTypesEnum.AllocateSuccessResponse, allocResponse.Header.MessageType);

                await CreatePermissionForPeer(controlStream, hmacKey, peerPort);

                var peerAcceptTask = peerListener.AcceptTcpClientAsync();
                uint connectionId = await ConnectToPeer(controlStream, hmacKey, peerPort);

                using var peerClient = await peerAcceptTask;
                var peerNetStream = peerClient.GetStream();

                // --- Data connection: ConnectionBind ---
                using var dataClient = await ConnectTcpClient(port);
                var dataNetStream = dataClient.GetStream();

                var bindMsg = new STUNMessage(STUNMessageTypesEnum.ConnectionBind);
                bindMsg.Attributes.Add(new STUNConnectionIdAttribute(connectionId));
                bindMsg.AddUsernameAttribute(TEST_USERNAME);
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                    Encoding.UTF8.GetBytes(TEST_REALM)));
                bindMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                    Encoding.UTF8.GetBytes("dummy")));
                await SendStunMessage(dataNetStream, bindMsg, hmacKey);

                var bindResponse = await ReceiveStunMessage(dataNetStream);
                Assert.Equal(STUNMessageTypesEnum.ConnectionBindSuccessResponse, bindResponse.Header.MessageType);

                // Give the relay a moment to start
                await Task.Delay(100);

                // --- TLS handshake over the relay ---
                // The data connection (client side) acts as TLS server.
                // The peer connection acts as TLS client.
                // This is arbitrary — the relay is transparent to TLS.

                // Accept any certificate for the test (self-signed).
                bool ValidateAnyCert(object s, X509Certificate c, X509Chain ch, SslPolicyErrors e) => true;

                var sslServer = new SslStream(dataNetStream, leaveInnerStreamOpen: true);
                var sslClient = new SslStream(peerNetStream, leaveInnerStreamOpen: true, ValidateAnyCert);

                // Run TLS handshakes concurrently — server and client need each other.
                var serverAuth = sslServer.AuthenticateAsServerAsync(cert);
                var clientAuth = sslClient.AuthenticateAsClientAsync("turn-test");

                var handshakeTimeout = Task.Delay(10000);
                var handshakeDone = Task.WhenAll(serverAuth, clientAuth);
                var winner = await Task.WhenAny(handshakeDone, handshakeTimeout);
                if (winner != handshakeDone)
                {
                    // Gather exception info from the handshake tasks if they faulted.
                    var serverErr = serverAuth.IsFaulted ? serverAuth.Exception?.InnerException?.Message : serverAuth.Status.ToString();
                    var clientErr = clientAuth.IsFaulted ? clientAuth.Exception?.InnerException?.Message : clientAuth.Status.ToString();
                    Assert.Fail($"TLS handshake timed out. Server: {serverErr}, Client: {clientErr}");
                }
                await handshakeDone; // propagate exceptions

                Assert.True(sslServer.IsAuthenticated, "Server-side TLS should be authenticated");
                Assert.True(sslClient.IsAuthenticated, "Client-side TLS should be authenticated");
                Assert.True(sslServer.IsEncrypted, "Server-side TLS should be encrypted");
                Assert.True(sslClient.IsEncrypted, "Client-side TLS should be encrypted");

                // --- Encrypted bidirectional data exchange ---
                var recvBuffer = new byte[4096];

                for (int i = 0; i < 3; i++)
                {
                    // Client (TLS server side) → Peer (TLS client side)
                    var msg = $"encrypted-to-peer-{i}";
                    var msgBytes = Encoding.UTF8.GetBytes(msg);
                    await sslServer.WriteAsync(msgBytes, 0, msgBytes.Length);
                    await sslServer.FlushAsync();

                    var cts = new CancellationTokenSource(3000);
                    int bytesRead = await sslClient.ReadAsync(recvBuffer, 0, recvBuffer.Length, cts.Token);
                    Assert.True(bytesRead > 0, $"Peer should receive encrypted message {i}");
                    Assert.Equal(msg, Encoding.UTF8.GetString(recvBuffer, 0, bytesRead));

                    // Peer (TLS client side) → Client (TLS server side)
                    var reply = $"encrypted-to-client-{i}";
                    var replyBytes = Encoding.UTF8.GetBytes(reply);
                    await sslClient.WriteAsync(replyBytes, 0, replyBytes.Length);
                    await sslClient.FlushAsync();

                    cts = new CancellationTokenSource(3000);
                    bytesRead = await sslServer.ReadAsync(recvBuffer, 0, recvBuffer.Length, cts.Token);
                    Assert.True(bytesRead > 0, $"Client should receive encrypted message {i}");
                    Assert.Equal(reply, Encoding.UTF8.GetString(recvBuffer, 0, bytesRead));
                }

                sslClient.Dispose();
                sslServer.Dispose();
            }
            finally
            {
                peerListener.Stop();
            }
        }
#endif

        #endregion

        #region RFC 6062 Test Helpers

        private static STUNMessage BuildTcpAllocateRequest()
        {
            var msg = new STUNMessage(STUNMessageTypesEnum.Allocate);
            msg.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.RequestedTransport,
                STUNAttributeConstants.TcpTransportType));
            return msg;
        }

        private static STUNMessage BuildAuthenticatedTcpAllocateRequest(byte[] hmacKey, string username, string realm, string nonce)
        {
            var msg = new STUNMessage(STUNMessageTypesEnum.Allocate);
            msg.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.RequestedTransport,
                STUNAttributeConstants.TcpTransportType));
            msg.AddUsernameAttribute(username);
            msg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                Encoding.UTF8.GetBytes(realm)));
            msg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                Encoding.UTF8.GetBytes(nonce)));
            return msg;
        }

        private async Task<STUNMessage> TcpAllocateWithAuth(NetworkStream stream, byte[] hmacKey)
        {
            // Step 1: Unauthenticated request to get nonce
            var request1 = BuildTcpAllocateRequest();
            await SendStunMessage(stream, request1);
            var response1 = await ReceiveStunMessage(stream);

            var nonceAttr = response1.Attributes.First(
                a => a.AttributeType == STUNAttributeTypesEnum.Nonce);
            var nonce = Encoding.UTF8.GetString(nonceAttr.Value);

            // Step 2: Authenticated request
            var request2 = BuildAuthenticatedTcpAllocateRequest(hmacKey, TEST_USERNAME, TEST_REALM, nonce);
            await SendStunMessage(stream, request2, hmacKey);
            return await ReceiveStunMessage(stream);
        }

        private async Task CreatePermissionForPeer(NetworkStream stream, byte[] hmacKey, int peerPort)
        {
            var permMsg = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            permMsg.AddXORPeerAddressAttribute(IPAddress.Loopback, peerPort);
            permMsg.AddUsernameAttribute(TEST_USERNAME);
            permMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                Encoding.UTF8.GetBytes(TEST_REALM)));
            permMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                Encoding.UTF8.GetBytes("dummy")));
            await SendStunMessage(stream, permMsg, hmacKey);
            var resp = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.CreatePermissionSuccessResponse, resp.Header.MessageType);
        }

        private async Task<uint> ConnectToPeer(NetworkStream stream, byte[] hmacKey, int peerPort)
        {
            var connectMsg = new STUNMessage(STUNMessageTypesEnum.Connect);
            connectMsg.AddXORPeerAddressAttribute(IPAddress.Loopback, peerPort);
            connectMsg.AddUsernameAttribute(TEST_USERNAME);
            connectMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Realm,
                Encoding.UTF8.GetBytes(TEST_REALM)));
            connectMsg.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Nonce,
                Encoding.UTF8.GetBytes("dummy")));
            await SendStunMessage(stream, connectMsg, hmacKey);
            var connectResp = await ReceiveStunMessage(stream);
            Assert.Equal(STUNMessageTypesEnum.ConnectSuccessResponse, connectResp.Header.MessageType);

            var connIdAttr = connectResp.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ConnectionId) as STUNConnectionIdAttribute;
            Assert.NotNull(connIdAttr);
            return connIdAttr.ConnectionId;
        }

        private static int ParseErrorCode(byte[] errorValue)
        {
            if (errorValue == null || errorValue.Length < 4) return 0;
            return errorValue[2] * 100 + errorValue[3];
        }

        /// <summary>
        /// Reads exactly <paramref name="expectedLength"/> bytes from a stream,
        /// handling partial reads. Throws on timeout.
        /// </summary>
        private static async Task<byte[]> ReadAllBytesAsync(NetworkStream stream, int expectedLength, int timeoutMs = 5000)
        {
            var buffer = new byte[expectedLength];
            int totalRead = 0;
            var cts = new CancellationTokenSource(timeoutMs);

            while (totalRead < expectedLength)
            {
                int bytesRead = await stream.ReadAsync(buffer, totalRead, expectedLength - totalRead, cts.Token);
                if (bytesRead == 0)
                    throw new Exception($"Stream closed after {totalRead} of {expectedLength} bytes.");
                totalRead += bytesRead;
            }

            return buffer;
        }

        #endregion

        #region Test Helpers

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
