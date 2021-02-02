//-----------------------------------------------------------------------------
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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.UnitTests
{
    public class TestLogHelper
    {
        public static Microsoft.Extensions.Logging.ILogger InitTestLogger(Xunit.Abstractions.ITestOutputHelper output)
        {
#if DEBUG
            string template = "{Timestamp:HH:mm:ss.ffff} [{Level}] {Scope} {Message}{NewLine}{Exception}";
            //var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var serilog = new LoggerConfiguration()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .Enrich.WithProperty("ThreadId", System.Threading.Thread.CurrentThread.ManagedThreadId)
                .WriteTo.TestOutput(output, outputTemplate: template)
                .WriteTo.Console(outputTemplate: template)
                .CreateLogger();
            SIPSorcery.LogFactory.Set(new SerilogLoggerFactory(serilog));
            return new SerilogLoggerProvider(serilog).CreateLogger("unit");

#else
            return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
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

            SIPMessageSent = new AutoResetEvent(false);
        }

        public string LastSIPMessageSent { get; private set; }

        public AutoResetEvent SIPMessageSent { get; }

        public override Task<SocketError> SendAsync(SIPEndPoint destinationEndPoint, byte[] buffer, bool canInitiateConnection, string connectionIDHint)
        {
            LastSIPMessageSent = System.Text.Encoding.UTF8.GetString(buffer);
            SIPMessageSent.Set();
            return Task.FromResult(SocketError.Success);
        }

        public override Task<SocketError> SendSecureAsync(SIPEndPoint destinationEndPoint, byte[] buffer, string serverCertificate, bool canInitiateConnection, string connectionIDHint)
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

    public class MockSIPUriResolver
    {
        public static Task<SIPEndPoint> ResolveSIPUri(SIPURI uri, bool preferIPv6)
        {
            if (IPSocket.TryParseIPEndPoint(uri.Host, out var ipEndPoint))
            {
                return Task.FromResult(new SIPEndPoint(uri.Protocol, ipEndPoint));
            }
            else
            {
                return Task.FromResult<SIPEndPoint>(null);
            }
        }
    }

    public class MockMediaSession : IMediaSession
    {
        private const string RTP_MEDIA_PROFILE = "RTP/AVP";

        public SDP RemoteDescription { get; private set; }

        public bool IsClosed { get; private set; }
        public bool HasAudio => true;
        public bool HasVideo => false;
        public IPAddress RtpBindAddress => null;

#pragma warning disable 67
        public event Action<string> OnRtpClosed;
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;
        public event Action<IPEndPoint, RTPEvent, RTPHeader> OnRtpEvent;
#pragma warning restore 67

        public void Close(string reason)
        {
            IsClosed = true;
        }

        public SDP CreateAnswer(IPAddress connectionAddress)
        {
            SDP answerSdp = new SDP(IPAddress.Loopback);
            answerSdp.SessionId = Crypto.GetRandomInt(5).ToString();

            answerSdp.Connection = new SDPConnectionInformation(connectionAddress ?? IPAddress.Loopback);

            SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.audio,
               1234,
               new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });

            audioAnnouncement.Transport = RTP_MEDIA_PROFILE;

            answerSdp.Media.Add(audioAnnouncement);

            return answerSdp;
        }

        public SDP CreateOffer(IPAddress connectionAddress)
        {
            SDP offerSdp = new SDP(IPAddress.Loopback);
            offerSdp.SessionId = Crypto.GetRandomInt(5).ToString();

            offerSdp.Connection = new SDPConnectionInformation(connectionAddress ?? IPAddress.Loopback);

            SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.audio,
               1234,
               new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });

            audioAnnouncement.Transport = RTP_MEDIA_PROFILE;

            offerSdp.Media.Add(audioAnnouncement);

            return offerSdp;
        }

        public SetDescriptionResultEnum SetRemoteDescription(SdpType type, SDP sessionDescription)
        {
            RemoteDescription = sessionDescription;
            return SetDescriptionResultEnum.OK;
        }

        public Task Start()
        {
            return Task.CompletedTask;
        }

        public void SetMediaStreamStatus(SDPMediaTypesEnum kind, MediaStreamStatusEnum status)
        {
            //throw new NotImplementedException();
        }

        public Task SendDtmf(byte tone, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
