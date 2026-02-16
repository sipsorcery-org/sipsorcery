//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example TURN server (RFC 5766) console application that
// starts a lightweight in-process TURN relay and demonstrates a client
// performing an allocation, creating a permission, and relaying data
// through it.
//
// Author(s):
// SIPSorcery Contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;

namespace TurnServerExample
{
    class Program
    {
        private const string USERNAME = "myuser";
        private const string PASSWORD = "mypassword";
        private const string REALM = "example.com";

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        static async Task Main()
        {
            Console.WriteLine("Example TURN Server + Client Demo");
            Console.WriteLine("==================================\n");

            AddConsoleLogger();

            // ---------------------------------------------------------------
            // 1. Start the TURN server
            // ---------------------------------------------------------------
            var config = new TurnServerConfig
            {
                ListenAddress = IPAddress.Loopback,
                Port = 3478,
                EnableTcp = true,
                EnableUdp = true,
                Username = USERNAME,
                Password = PASSWORD,
                Realm = REALM,
                RelayAddress = IPAddress.Loopback,
                DefaultLifetimeSeconds = 600,
            };

            using var server = new TurnServer(config);
            server.Start();

            Console.WriteLine($"TURN server listening on {config.ListenAddress}:{config.Port}\n");

            // ---------------------------------------------------------------
            // 2. Simulate a client performing the TURN allocation flow
            // ---------------------------------------------------------------
            await RunClientDemo(config.Port);

            Console.WriteLine("\nDemo complete. Press any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Demonstrates the full TURN client flow over TCP:
        ///   1. Allocate (unauthenticated -> 401 challenge -> authenticated -> success)
        ///   2. CreatePermission for a peer
        ///   3. SendIndication to relay data to the peer
        ///   4. Peer sends data back through the relay
        ///   5. Refresh with lifetime=0 to delete the allocation
        /// </summary>
        static async Task RunClientDemo(int serverPort)
        {
            var hmacKey = ComputeHmacKey(USERNAME, REALM, PASSWORD);

            // Connect to the TURN server over TCP
            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync(IPAddress.Loopback, serverPort);
            var stream = tcp.GetStream();

            Console.WriteLine("[Client] Connected to TURN server via TCP.\n");

            // --- Step 1: Allocate ---

            Console.WriteLine("[Client] Step 1: Sending Allocate (no credentials)...");
            var allocReq = new STUNMessage(STUNMessageTypesEnum.Allocate);
            allocReq.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.RequestedTransport,
                STUNAttributeConstants.UdpTransportType));
            await SendStun(stream, allocReq);

            var challenge = await ReceiveStun(stream);
            var errorAttr = challenge.Attributes.FirstOrDefault(
                a => a.AttributeType == STUNAttributeTypesEnum.ErrorCode);
            int errorCode = errorAttr.Value[2] * 100 + errorAttr.Value[3];
            Console.WriteLine($"[Client] Got {errorCode} challenge with REALM and NONCE.");

            // Extract nonce from the challenge
            var nonce = Encoding.UTF8.GetString(
                challenge.Attributes.First(
                    a => a.AttributeType == STUNAttributeTypesEnum.Nonce).Value);

            Console.WriteLine("[Client] Sending authenticated Allocate...");
            var authReq = new STUNMessage(STUNMessageTypesEnum.Allocate);
            authReq.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.RequestedTransport,
                STUNAttributeConstants.UdpTransportType));
            authReq.AddUsernameAttribute(USERNAME);
            authReq.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.Realm, Encoding.UTF8.GetBytes(REALM)));
            authReq.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.Nonce, Encoding.UTF8.GetBytes(nonce)));
            await SendStun(stream, authReq, hmacKey);

            var allocResp = await ReceiveStun(stream);
            Console.WriteLine($"[Client] Allocate response: {allocResp.Header.MessageType}");

            // Parse the relay address from the response
            var relayAttr = allocResp.Attributes.First(
                a => a.AttributeType == STUNAttributeTypesEnum.XORRelayedAddress);
            var relayAddr = new STUNXORAddressAttribute(
                STUNAttributeTypesEnum.XORRelayedAddress,
                relayAttr.Value, allocResp.Header.TransactionId);
            Console.WriteLine($"[Client] Relay endpoint: {relayAddr.Address}:{relayAddr.Port}\n");

            // --- Step 2: Set up a "peer" and create permission ---

            using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var peerEp = (IPEndPoint)peer.Client.LocalEndPoint;
            Console.WriteLine($"[Peer]   Listening on {peerEp}");

            Console.WriteLine("[Client] Step 2: Creating permission for peer...");
            var permReq = new STUNMessage(STUNMessageTypesEnum.CreatePermission);
            permReq.AddXORPeerAddressAttribute(peerEp.Address, peerEp.Port);
            await SendStun(stream, permReq, hmacKey);
            var permResp = await ReceiveStun(stream);
            Console.WriteLine($"[Client] CreatePermission response: {permResp.Header.MessageType}\n");

            // --- Step 3: Send data through the relay ---

            Console.WriteLine("[Client] Step 3: Sending data via SendIndication...");
            var payload = Encoding.UTF8.GetBytes("Hello from TURN client!");
            var sendInd = new STUNMessage(STUNMessageTypesEnum.SendIndication);
            sendInd.AddXORPeerAddressAttribute(peerEp.Address, peerEp.Port);
            sendInd.Attributes.Add(new STUNAttribute(STUNAttributeTypesEnum.Data, payload));
            await SendStun(stream, sendInd); // Indications are unsigned

            // Peer receives the relayed data
            var recvTask = peer.ReceiveAsync();
            var completed = await Task.WhenAny(recvTask, Task.Delay(3000));
            if (completed == recvTask)
            {
                var result = await recvTask;
                Console.WriteLine($"[Peer]   Received: \"{Encoding.UTF8.GetString(result.Buffer)}\"");
                Console.WriteLine($"[Peer]   From relay endpoint: {result.RemoteEndPoint}\n");

                // --- Step 4: Peer sends data back through the relay ---

                Console.WriteLine("[Peer]   Step 4: Sending response back through relay...");
                var response = Encoding.UTF8.GetBytes("Hello from peer!");
                await peer.SendAsync(response, response.Length,
                    new IPEndPoint(IPAddress.Loopback, relayAddr.Port));

                // Client receives DataIndication on the TCP stream
                stream.ReadTimeout = 3000;
                var hdr = new byte[4];
                int read = 0;
                while (read < 4)
                    read += await stream.ReadAsync(hdr, read, 4 - read);

                if ((hdr[0] & 0xC0) == 0x00)
                {
                    // STUN message (DataIndication)
                    int msgLen = (hdr[2] << 8) | hdr[3];
                    int remaining = 16 + msgLen;
                    var full = new byte[4 + remaining];
                    Buffer.BlockCopy(hdr, 0, full, 0, 4);
                    read = 0;
                    while (read < remaining)
                        read += await stream.ReadAsync(full, 4 + read, remaining - read);

                    var dataInd = STUNMessage.ParseSTUNMessage(full, full.Length);
                    var dataAttr = dataInd.Attributes.FirstOrDefault(
                        a => a.AttributeType == STUNAttributeTypesEnum.Data);
                    if (dataAttr != null)
                    {
                        Console.WriteLine($"[Client] Received via relay: \"{Encoding.UTF8.GetString(dataAttr.Value)}\"\n");
                    }
                }
            }
            else
            {
                Console.WriteLine("[Peer]   Timed out waiting for data.\n");
            }

            // --- Step 5: Delete the allocation ---

            Console.WriteLine("[Client] Step 5: Deleting allocation (Refresh lifetime=0)...");
            var refresh = new STUNMessage(STUNMessageTypesEnum.Refresh);
            refresh.Attributes.Add(new STUNAttribute(
                STUNAttributeTypesEnum.Lifetime, (uint)0));
            await SendStun(stream, refresh, hmacKey);
            var refreshResp = await ReceiveStun(stream);
            Console.WriteLine($"[Client] Refresh response: {refreshResp.Header.MessageType}");
        }

        #region STUN message helpers

        static byte[] ComputeHmacKey(string username, string realm, string password)
        {
            using (var md5 = MD5.Create())
            {
                return md5.ComputeHash(
                    Encoding.UTF8.GetBytes($"{username}:{realm}:{password}"));
            }
        }

        static async Task SendStun(NetworkStream stream, STUNMessage msg, byte[] hmacKey = null)
        {
            var bytes = msg.ToByteBuffer(hmacKey, false);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        static async Task<STUNMessage> ReceiveStun(NetworkStream stream)
        {
            var header = new byte[4];
            int read = 0;
            while (read < 4)
                read += await stream.ReadAsync(header, read, 4 - read);

            int msgLen = (header[2] << 8) | header[3];
            int remaining = 16 + msgLen;
            var full = new byte[4 + remaining];
            Buffer.BlockCopy(header, 0, full, 0, 4);

            read = 0;
            while (read < remaining)
                read += await stream.ReadAsync(full, 4 + read, remaining - read);

            return STUNMessage.ParseSTUNMessage(full, full.Length);
        }

        #endregion

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(logger);
            SIPSorcery.LogFactory.Set(factory);
            Log = factory.CreateLogger<Program>();
        }
    }
}
