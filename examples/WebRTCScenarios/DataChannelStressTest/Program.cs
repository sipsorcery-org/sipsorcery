//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A console application to load test the WebRTC data channel
// send message API.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 06 Aug 2020	Aaron Clauson	Created based on example from @Terricide.
// 10 Apr 2021  Aaron Clauson   Adjusted for new SCTP stack.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.Demo
{
    /// <summary>
    /// The test mechanism is:
    ///  - Create a series of WebRTC peer connection peers. Each pair has one data channel created by default.
    ///  - Create any additional data channels required.
    ///  - Do N number of sends on each data channel. Each send involves:
    ///    - Create a random byte buffer, take a sha256 hash of it and send. Wait X seconds for a response.
    ///    - On the receiving data channel hash the random buffer and ensure it matches the supplied sha256.
    ///      Send string response back to sender with the send number.
    ///    - Upon receiving response on the original sending channel commence the next send.
    /// </summary>
    class Program
    {
        /// <summary>
        /// The number of WebRTC peer connection pairs to use for the test. Each pair creates two
        /// peer connections and establishes a DTLS connection. A single data channel is created at
        /// connection time. If the TEST_DATACHANNELS_PER_PEER_CONNECTION is larger than 1 then
        /// additional data channels will be created after the peer connection is established.
        /// Recommended values are between 1 and 10.
        /// </summary>
        const int TEST_PEER_CONNECTIONS_COUNT = 10;

        /// <summary>
        /// The number of data channels to establish between each WebRTC peer connection pair. At
        /// least one connection is always created.
        /// Recommended values are between 1 and 10.
        /// </summary>
        const int TEST_DATACHANNELS_PER_PEER_CONNECTION = 3;

        /// <summary>
        /// The maximum data payload to set on the messages sent for each data channel test. The message
        /// size for a data channel send is limited by RTCSctpTransport.SCTP_DEFAULT_MAX_MESSAGE_SIZE of 262144.
        /// 68 bytes are required for the SCTP fields and 69 byes are required for an integer and string field 
        /// so the maximum this can be set at is 262007.
        /// Recommended values are between 1 and 262007.
        /// </summary>
        const int TEST_MAX_DATA_PAYLOAD = 262007;

        /// <summary>
        /// The number of test sends to do on each data channel. The total number of sends carried out is:
        ///  peer connection pairs x data channels per pair x data channel sends
        /// Recommended values are between 1 and 1000.
        /// </summary>
        const int TEST_DATACHANNEL_SENDS = 100;

        /// <summary>
        /// The amount of time to wait for a data channel send to finish before timing
        /// out and assuming it failed.
        /// </summary>
        const int TEST_DATACHANNEL_SEND_TIMEOUT_SEONDS = 2;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static List<PeerConnectionPair> connectionPairs = new List<PeerConnectionPair>();

        static void Main()
        {
            Console.WriteLine("WebRTC Data Channel Load Test Program");
            Console.WriteLine("Press ctrl-c to exit.");

            RunCommand().Wait();
        }

        private static async Task RunCommand()
        {
            AddConsoleLogger();

            // Connect the peer connection pairs.
            Stopwatch connectSW = Stopwatch.StartNew();

            List<Task> connectPairsTasks = new List<Task>();

            for (int i = 0; i < TEST_PEER_CONNECTIONS_COUNT; i++)
            {
                int id = i;

                var t = Task.Run(async () =>
                {
                    var pair = new PeerConnectionPair(id);
                    connectionPairs.Add(pair);
                    await pair.Connect();
                });
                connectPairsTasks.Add(t);
            }

            await Task.WhenAll(connectPairsTasks);

            connectSW.Stop();
            Console.WriteLine($"Data channel open tasks completed in {connectSW.ElapsedMilliseconds:0.##}ms.");
            foreach (var pair in connectionPairs)
            {
                Console.WriteLine($"PC pair {pair.Name} src datachannel {pair.DC.readyState} streamid {pair.DC.id}, " +
                    $"dst datachannel {pair.PCDst.DataChannels.Single().readyState} streamid {pair.PCDst.DataChannels.Single().id}.");

                char a = 'a';
                for (int j = 1; j < TEST_DATACHANNELS_PER_PEER_CONNECTION; j++)
                {
                    char dcid = (char)((int)a + j);
                    var dstdcB = await pair.PCDst.createDataChannel($"{PeerConnectionPair.DATACHANNEL_LABEL_PREFIX}-{pair.ID}-{dcid}");
                }
            }

            foreach (var pair in connectionPairs)
            {
                Console.WriteLine($"Data channels for peer connection pair {pair.Name}:");

                foreach (var srcdc in pair.PCSrc.DataChannels)
                {
                    var dstdc = pair.PCDst.DataChannels.SingleOrDefault(x => x.id == srcdc.id);

                    Console.WriteLine($" {srcdc.label}: src status {srcdc.readyState} streamid {srcdc.id} <-> " +
                        $"dst status {dstdc.readyState} streamid {dstdc.id}.");

                    srcdc.onmessage += OnData;
                    dstdc.onmessage += OnData;
                }
            }

            // Do the data channel sends on each peer connection pair.
            Stopwatch sendSW = Stopwatch.StartNew();

            var taskList = new List<Task>();

            foreach (var pair in connectionPairs)
            {
                foreach (var dc in pair.PCSrc.DataChannels)
                {
                    taskList.Add(Task.Run(() =>
                    {
                        var sw = Stopwatch.StartNew();

                        for (int i = 0; i < TEST_DATACHANNEL_SENDS; i++)
                        {
                            var sendConfirmedSig = pair.StreamSendConfirmed[dc.id.Value];
                            sendConfirmedSig.Reset();

                            var packetNum = new Message();
                            packetNum.Num = i;
                            packetNum.Data = new byte[TEST_MAX_DATA_PAYLOAD];
                            Crypto.GetRandomBytes(packetNum.Data);
                            packetNum.SHA256 = Crypto.GetSHA256Hash(packetNum.Data);

                            Console.WriteLine($"{dc.label}: stream id {dc.id}, send {i}.");
                            //Console.WriteLine($"Send {i} from dc {dc.label}, sha256 {packetNum.SHA256}.");

                            dc.send(packetNum.ToData());

                            if (!sendConfirmedSig.Wait(TEST_DATACHANNEL_SEND_TIMEOUT_SEONDS * 1000))
                            {
                                throw new ApplicationException($"Data channel send on {dc.label} timed out waiting for confirmation on send {i}.");
                            }
                        }
                        
                        Console.WriteLine($"{dc.label} data channel sends finished in {sw.ElapsedMilliseconds/1000:0.##}s.");
                    }));
                }
            }

            await Task.WhenAll(taskList.ToArray());
            Console.WriteLine($"Done in {sendSW.ElapsedMilliseconds}ms");

            Console.WriteLine("Press any key to exit.");
            Console.ReadLine();

            foreach (var pair in connectionPairs)
            {
                pair.PCSrc.Close("normal");
                pair.PCDst.Close("normal");
            }
        }

        private static void OnData(RTCDataChannel dc, DataChannelPayloadProtocols proto, byte[] data)
        {
            if (proto == DataChannelPayloadProtocols.WebRTC_String)
            {
                Console.WriteLine($"{dc.label}: return recv stream id {dc.id}, send# {Encoding.UTF8.GetString(data)}");
                int pairID = int.Parse(Regex.Match(dc.label, @".*-(?<id>\d+)-.*").Result("${id}"));
                connectionPairs.Single(x => x.ID == pairID).StreamSendConfirmed[dc.id.Value].Set();
            }
            else if (proto == DataChannelPayloadProtocols.WebRTC_Binary)
            {
                var packet = BytesToStructure<Message>(data);
                var sha256 = Crypto.GetSHA256Hash(packet.Data);
                Console.WriteLine($"{dc.label}: recv stream id {dc.id}, send# {packet.Num}.");
                //Console.WriteLine($"{dc.label}: recv {packet.Num}, sha256 {sha256}.");

                if (sha256 != packet.SHA256)
                {
                    throw new ApplicationException($"Data channel message sha256 hash {sha256} did not match expected hash {packet.SHA256}.");
                }

                dc.send(packet.Num.ToString());
            }
        }

        static T BytesToStructure<T>(byte[] bytes)
        {
            int size = Marshal.SizeOf(typeof(T));
            if (bytes.Length < size)
            {
                throw new Exception("Invalid parameter");
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, ptr, size);
                return (T)Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// Adds a console logger.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            logger = factory.CreateLogger<Program>();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Message
        {
            public int Num;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 65)]
            public string SHA256;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = TEST_MAX_DATA_PAYLOAD)]
            public byte[] Data;
            public byte[] ToData()
            {
                int total = Marshal.SizeOf(typeof(Message));//Get size of struct data
                byte[] buf = new byte[total];//byte array & its size
                IntPtr ptr = Marshal.AllocHGlobal(total);//pointer to byte array
                Marshal.StructureToPtr(this, ptr, true);
                Marshal.Copy(ptr, buf, 0, total);
                Marshal.FreeHGlobal(ptr);
                return buf;
            }
        }
    }

    class PeerConnectionPair
    {
        public const string DATACHANNEL_LABEL_PREFIX = "dc";

        public string Name;
        public RTCPeerConnection PCSrc;
        public RTCPeerConnection PCDst;
        public RTCDataChannel DC;
        public int ID { get; private set; }
        public ConcurrentDictionary<ushort, ManualResetEventSlim> StreamSendConfirmed =
            new ConcurrentDictionary<ushort, ManualResetEventSlim>();

        public PeerConnectionPair(int id)
        {
            ID = id;

            Name = $"PC{ID}";
            PCSrc = new RTCPeerConnection();
            PCDst = new RTCPeerConnection();

            PCSrc.onconnectionstatechange += (state) => Console.WriteLine($"Peer connection pair {Name} state changed to {state}.");

            PCSrc.ondatachannel += (dc) => StreamSendConfirmed.TryAdd(dc.id.Value, new ManualResetEventSlim());
        }

        public async Task Connect()
        {
            TaskCompletionSource<bool> dcAOpened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            DC = await PCSrc.createDataChannel($"{DATACHANNEL_LABEL_PREFIX}-{ID}-a");

            DC.onopen += () =>
            {
                Console.WriteLine($"Peer connection pair {Name} A data channel opened.");
                StreamSendConfirmed.TryAdd(DC.id.Value, new ManualResetEventSlim());
                dcAOpened.TrySetResult(true);
            };

            var offer = PCSrc.createOffer();
            await PCSrc.setLocalDescription(offer);

            if (PCDst.setRemoteDescription(offer) != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException($"SDP negotiation failed for peer connection pair {Name}.");
            }

            var answer = PCDst.createAnswer();
            await PCDst.setLocalDescription(answer);

            if (PCSrc.setRemoteDescription(answer) != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException($"SDP negotiation failed for peer connection pair {Name}.");
            }

            await Task.WhenAll(dcAOpened.Task);
        }
    }

}