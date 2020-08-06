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
using Serilog;

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

            var peerA = new WebRTCPeer("dcx");
            peerA.Name = "ClientA";
            var peerB = new WebRTCPeer("dcy");
            peerB.Name = "ClientB";

            peerA.OnOffer += async (sdp) => await peerB.ProcessAnswer(sdp);
            peerB.OnAnswer += async (sdp) => await peerA.ProcessAnswer(sdp);
            peerA.OnIce += (ice) => peerB.ProcessIce(ice);
            peerB.OnIce += (ice) => peerA.ProcessIce(ice);
            await peerA.Connect();
            await peerB.AwaitConnection();

            var taskList = new List<Task>();

            taskList.Add(Task.Run(async () =>
            {
                string sendLabel = "dcx";

                while (!peerA.IsDataChannelReady(sendLabel))
                {
                    Console.WriteLine($"Waiting 1s for data channel {sendLabel} to open.");
                    await Task.Delay(1000);
                }

                for (int i = 0; i < 30; i++)
                {
                    try
                    {
                        Console.WriteLine($"Data channel send {i} on {sendLabel}.");

                        var data = new byte[2048];
                        var num = BitConverter.GetBytes(i);
                        Buffer.BlockCopy(num, 0, data, 0, num.Length);
                        peerA.Send(sendLabel, data);
                        //await Task.Delay(50);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ClientA:" + ex.ToString());
                    }
                }
            }));

            //taskList.Add(Task.Run(() =>
            //{
            //    for (int i = 0; i < 1000; i++)
            //    {
            //        try
            //        {
            //            var data = new byte[2048];
            //            var num = BitConverter.GetBytes(i);
            //            Buffer.BlockCopy(num, 0, data, 0, num.Length);
            //            clientB.Send(data);
            //        }
            //        catch (Exception ex)
            //        {
            //            Console.WriteLine("ClientB:" + ex.ToString());
            //        }
            //    }
            //}));

            await Task.WhenAll(taskList.ToArray());
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
    }
}
