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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using Serilog;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace SIPSorcery.Demo
{
    class Program
    {
        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        static void Main(string[] args)
        {
            Console.WriteLine("WebRTC Data Channel Load Test Program");
            Console.WriteLine("Press ctrl-c to exit.");

            RunCommand().Wait();
        }

        private static async Task RunCommand()
        {
            CancellationTokenSource exitCts = new CancellationTokenSource();

            AddConsoleLogger();

            var peerA = new WebRTCPeer("PeerA", "dcx");
            peerA.OnData = OnData;
            var peerB = new WebRTCPeer("PeerB", "dcy");
            peerB.OnData = OnData;

            // Exchange the SDP offer/answers. ICE Host candidates are included in the SDP.
            var offer = peerA.PeerConnection.createOffer(null);
            await peerA.PeerConnection.setLocalDescription(offer);

            if(peerB.PeerConnection.setRemoteDescription(offer) != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException("Couldn't set remote description.");
            }
            var answer = peerB.PeerConnection.createAnswer(null);
            await peerB.PeerConnection.setLocalDescription(answer);

            if(peerA.PeerConnection.setRemoteDescription(answer) != SetDescriptionResultEnum.OK)
            {
                throw new ApplicationException("Couldn't set remote description.");
            }

            // Wait for the peers to connect. Should take <1s if the peers are on the same host.
            while(peerA.PeerConnection.connectionState != RTCPeerConnectionState.connected &&
                peerB.PeerConnection.connectionState != RTCPeerConnectionState.connected)
            {
                Console.WriteLine("Waiting for WebRTC peers to connect...");
                await Task.Delay(1000);
            }

            var taskList = new List<Task>();

            taskList.Add(Task.Run(async () =>
            {
                string sendLabel = "dcx";

                while (!peerA.IsDataChannelReady(sendLabel))
                {
                    Console.WriteLine($"Waiting 1s for data channel {sendLabel} to open.");
                    await Task.Delay(1000);
                }

                for (int i = 0; i < 100; i++)
                {
                    try
                    {
                        Console.WriteLine($"Data channel send {i} on {sendLabel}.");

                        var num = BitConverter.GetBytes(i);
                        await peerA.SendAsync(sendLabel, num).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ClientA:" + ex.ToString());
                    }
                }
            }));

            string[] queueNames = new string[] { "ThreadA", "ThreadB", "ThreadC" };

            foreach (var queueName in queueNames)
            {
                var name = queueName;
                taskList.Add(Task.Run(async () =>
                {
                    string sendLabel = "dcx";

                    while (!peerA.IsDataChannelReady(sendLabel))
                    {
                        Console.WriteLine($"Waiting 1s for data channel {sendLabel} {name} to open.");
                        await Task.Delay(1000);
                    }

                    for (int i = 0; i < 100; i++)
                    {
                        try
                        {
                            Console.WriteLine($"Data channel send {i} on {sendLabel} {name}.");

                            var packetNum = new Message();
                            packetNum.Num = i;
                            packetNum.QueueName = $"{peerA._peerName} {sendLabel} {name}";
                            packetNum.Data = new byte[64000];
                            await peerA.SendAsync(sendLabel, packetNum.ToData()).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("ClientA:" + ex.ToString());
                        }
                    }
                }));
            }

            taskList.Add(Task.Run(async () =>
            {
                string sendLabel = "dcx";

                while (!peerA.IsDataChannelReady(sendLabel))
                {
                    Console.WriteLine($"Waiting 1s for data channel {sendLabel} to open.");
                    await Task.Delay(1000);
                }

                for (int i = 100; i < 200; i++)
                {
                    try
                    {
                        Console.WriteLine($"Data channel send {i} on {sendLabel}.");

                        var num = BitConverter.GetBytes(i);
                        await peerA.SendAsync(sendLabel, num).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ClientA:" + ex.ToString());
                    }
                }
            }));

            await Task.WhenAll(taskList.ToArray());
        }

        private static void OnData(WebRTCPeer peer, byte[] obj)
        {
            if (obj.Length < IntPtr.Size)
            {
                var pieceNum = BitConverter.ToInt32(obj, 0);
                //logger.LogDebug($"{Name}: data channel ({_dataChannel.label}:{_dataChannel.id}): {pieceNum}.");
                logger.LogDebug($"{peer._peerName}: Data channel receive: {pieceNum}, length {obj.Length}.");
            }
            else
            {
                var packet = BytesToStructure<Message>(obj);
                logger.LogDebug($"{peer._peerName}: Data channel receive: {packet.QueueName} Num: {packet.Num}.");
            }
        }

        static T BytesToStructure<T>(byte[] bytes)
        {
            int size = Marshal.SizeOf(typeof(T));
            if (bytes.Length < size)
                throw new Exception("Invalid parameter");

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
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Message
        {
            public int Num;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string QueueName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64000)]
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
}
