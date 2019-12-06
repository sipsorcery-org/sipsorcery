﻿//-----------------------------------------------------------------------------
// Filename: Initialise.cs
//
// Description: Assembly initialiser for SIPSorcery unit tests.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 14 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Serilog;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using Xunit.Abstractions;

namespace SIPSorcery.UnitTests
{
    public class TestLogHelper
    {
        public static void InitTestLogger(ITestOutputHelper output)
        {
#if DEBUG
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.TestOutput(output, Serilog.Events.LogEventLevel.Verbose)
                .WriteTo.Console(Serilog.Events.LogEventLevel.Verbose)
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);

            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
#endif
        }
    }

    internal class MockSIPChannel : SIPChannel
    {
        public MockSIPChannel(IPEndPoint channelEndPoint)
        {
            ListeningIPAddress = channelEndPoint.Address;
            Port = channelEndPoint.Port;
            SIPProtocol = SIPProtocolsEnum.udp;
            ID = Crypto.GetRandomInt(5).ToString();
        }

        public override void Send(IPEndPoint destinationEndPoint, byte[] buffer, string connectionIDHint)
        {
            throw new NotImplementedException();
        }

        public override Task<SocketError> SendAsync(IPEndPoint destinationEndPoint, byte[] buffer, string connectionIDHint)
        {
            throw new NotImplementedException();
        }

        public override void SendSecure(IPEndPoint destinationEndPoint, byte[] buffer, string serverCertificate, string connectionIDHint)
        {
            throw new NotImplementedException();
        }


        public override Task<SocketError> SendSecureAsync(IPEndPoint destinationEndPoint, byte[] buffer, string serverCertificate, string connectionIDHint)
        {
            throw new NotImplementedException();
        }

        public override void Close()
        { }

        public override void Dispose()
        { }

        public override bool HasConnection(string connectionID)
        {
            throw new NotImplementedException();
        }

        public override bool HasConnection(IPEndPoint remoteEndPoint)
        {
            throw new NotImplementedException();
        }
    }
}
