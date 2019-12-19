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

namespace SIPSorcery.UnitTests
{
    public class TestLogHelper
    {
        public static Microsoft.Extensions.Logging.ILogger InitTestLogger(Xunit.Abstractions.ITestOutputHelper output)
        {
            string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.ffff} [{Level}] {Scope} {Message}{NewLine}{Exception}";
            //string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.ffff} [{Level}] ({ThreadId:000}){Scope} {Message}{NewLine}{Exception}";
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.WithProperty("ThreadId",System.Threading.Thread.CurrentThread.ManagedThreadId)
                .WriteTo.TestOutput(output, outputTemplate: template)
                .WriteTo.Console(outputTemplate: template)
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);

            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
            return SIPSorcery.Sys.Log.Logger;
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

        public override void Send(SIPEndPoint destinationEndPoint, byte[] buffer, string connectionIDHint)
        {
            throw new NotImplementedException();
        }

        public override Task<SocketError> SendAsync(SIPEndPoint destinationEndPoint, byte[] buffer, string connectionIDHint)
        {
            throw new NotImplementedException();
        }

        public override Task<SocketError> SendSecureAsync(SIPEndPoint destinationEndPoint, byte[] buffer, string serverCertificate, string connectionIDHint)
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

        public override bool HasConnection(SIPEndPoint remoteEndPoint)
        {
            throw new NotImplementedException();
        }

        public override bool HasConnection(Uri serverUri)
        {
            throw new NotImplementedException();
        }

        public override bool IsAddressFamilySupported(AddressFamily addresFamily)
        {
            return true;
        }

        public override bool IsProtocolSupported(SIPProtocolsEnum protocol)
        {
            return true;
        }

        /// <summary>
        /// Use to cause a mock message to be passed through to the SIP Transport class monitoring this mock channel.
        /// </summary>
        public void FireMessageReceived(SIPEndPoint localEndPoint, SIPEndPoint remoteEndPoint, byte[] sipMsgBuffer)
        {
            SIPMessageReceived.Invoke(this, localEndPoint, remoteEndPoint, sipMsgBuffer);
        }
    }

    public class MockSIPDNSManager
    {
        public static SIPDNSLookupResult Resolve(SIPURI sipURI, bool async, bool? preferIPv6)
        {
            // This assumes the input SIP URI has an IP address as the host!
            IPSocket.TryParseIPEndPoint(sipURI.Host, out var ipEndPoint);
            return new SIPDNSLookupResult(sipURI, new SIPEndPoint(ipEndPoint));
        }
    }
}
