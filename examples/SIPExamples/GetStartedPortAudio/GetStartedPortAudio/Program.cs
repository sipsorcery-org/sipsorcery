﻿//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPSorcery
// library to place a call using PortAudio for audio capture and render.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Apr 2020	Aaron Clauson	Created, Dublin, Ireland.
// 01 Aug 2020  Aaron Clauson   Switched from PortAudioSharp to 
//                              ProjectCeilidh.PortAudio.
// 26 Feb 2021  Aaron Clauson   Refactored PortAudioEndPoint to use latest abstractions.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Usage on Linux:
// This project DOES NOT install the required native libraries on
// Linux.
//
// The approach I used to install the PortAudio library on Linux was:
// - Install Microsoft's C++ package manager (yes it works on Linux) from
//   https://github.com/microsoft/vcpkg,
// - Use vcpkg to install PortAudio: ./vcpkg install portaudio,
// - Run this app using "dotnet run" in the same directory as the project file.
// 
// Port Audio ALSA devices:
// You may need to adjust your default sounds device. To list devices use:
// pactl list short sources
// To set a new default device use:
// pactl set-default-sink 1

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP.App;

namespace demo
{
    class Program
    {
        private static string DESTINATION = "helloworld@sipsorcery.cloud";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started PortAudio Demo (YMMV)");

            AddConsoleLogger();

            var userAgent = new SIPUserAgent();
            var portAudioEndPoint = new PortAudioEndPoint(new AudioEncoder());
            var voipMediaSession = new VoIPMediaSession(portAudioEndPoint.ToMediaEndPoints());
            voipMediaSession.AcceptRtpFromAny = true;

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);
            Console.WriteLine($"Call result {((callResult) ? "success" : "failure")}.");

            Console.WriteLine("press any key to exit...");
            Console.Read();

            if (userAgent.IsCallActive)
            {
                Console.WriteLine("Hanging up.");
                userAgent.Hangup();

                await Task.Delay(1000);
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}

