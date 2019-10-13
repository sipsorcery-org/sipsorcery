//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to register a SIP account. 
// 
// History:
// 07 Oct 2019	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIPSorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using Serilog;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Register
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery registration user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Logging configuration. Can be ommitted if internal SIPSorcery debug and warning messages are not required.
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;

            // If your default DNS server supports SRV records there is no need to set a specific DNS server.
            DNSManager.SetDNSServers(new List<IPEndPoint> { IPEndPoint.Parse("8.8.8.8:53") });

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());
            int port = FreePort.FindNextAvailableUDPPort(SIPConstants.DEFAULT_SIP_PORT);
            var sipChannel = new SIPUDPChannel(new IPEndPoint(LocalIPConfig.GetDefaultIPv4Address(), port));
            sipTransport.AddSIPChannel(sipChannel);

            // Create a client user agent to maintain a periodic registration with a SIP server.
            var regUserAgent = new SIPRegistrationUserAgent(
                sipTransport,
                "softphonesample",
                "password",
                "sipsorcery.com");

            // Event handlers for the different stages of the registration.
            regUserAgent.RegistrationFailed += (uri, err) => SIPSorcery.Sys.Log.Logger.LogError($"{uri.ToString()}: {err}");
            regUserAgent.RegistrationTemporaryFailure += (uri, msg) => SIPSorcery.Sys.Log.Logger.LogWarning($"{uri.ToString()}: {msg}");
            regUserAgent.RegistrationRemoved += (uri) => SIPSorcery.Sys.Log.Logger.LogError($"{uri.ToString()} registration failed.");
            regUserAgent.RegistrationSuccessful += (uri) => SIPSorcery.Sys.Log.Logger.LogInformation($"{uri.ToString()} registration succeeded.");

            // Start the thread to perform the initial registration and then periodically resend it.
            regUserAgent.Start();
        }
    }
}
