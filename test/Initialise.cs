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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.UnitTests
{
    public class TestLogHelper
    {
        public static Microsoft.Extensions.Logging.ILogger InitTestLogger(Xunit.Abstractions.ITestOutputHelper output)
        {
#if DEBUG
            string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.ffff} [{Level}] {Scope} {Message}{NewLine}{Exception}";
            //string template = "{Timestamp:yyyy-MM-dd HH:mm:ss.ffff} [{Level}] ({ThreadId:000}){Scope} {Message}{NewLine}{Exception}";
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .Enrich.WithProperty("ThreadId", System.Threading.Thread.CurrentThread.ManagedThreadId)
                .WriteTo.TestOutput(output, outputTemplate: template)
                .WriteTo.Console(outputTemplate: template)
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);

            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
#endif
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

            SIPMessageSent = new AutoResetEvent(false);
        }

        public string LastSIPMessageSent { get; private set; }

        public AutoResetEvent SIPMessageSent { get; }

        public override Task<SocketError> SendAsync(SIPEndPoint destinationEndPoint, byte[] buffer, string connectionIDHint)
        {
            LastSIPMessageSent = System.Text.Encoding.UTF8.GetString(buffer);
            SIPMessageSent.Set();
            return Task.FromResult(SocketError.Success);
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

    public class MockMediaSession : IMediaSession
    {
        private const string RTP_MEDIA_PROFILE = "RTP/AVP";

        public RTCSessionDescription localDescription { get; private set; }
        public RTCSessionDescription remoteDescription { get; private set; }

        public bool IsClosed { get; private set; }
        public bool HasAudio => true;
        public bool HasVideo => false;

        public event Action<byte[], uint, uint, int> OnVideoSampleReady;
        public event Action<string> OnRtpClosed;
        public event Action<SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;
        public event Action<RTPEvent> OnRtpEvent;
        public event Action<Complex[]> OnAudioScopeSampleReady;
        public event Action<Complex[]> OnHoldAudioScopeSampleReady;

        public void Close(string reason)
        {
            IsClosed = true;
        }

        public Task<SDP> createAnswer(RTCAnswerOptions options)
        {
            SDP answerSdp = new SDP(IPAddress.Loopback);
            answerSdp.SessionId = Crypto.GetRandomInt(5).ToString();

            answerSdp.Connection = new SDPConnectionInformation(IPAddress.Loopback);

            SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.audio,
               1234,
               new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });

            audioAnnouncement.Transport = RTP_MEDIA_PROFILE;

            answerSdp.Media.Add(audioAnnouncement);

            return Task.FromResult(answerSdp);
        }

        public Task<SDP> createOffer(RTCOfferOptions options)
        {
            SDP offerSdp = new SDP(IPAddress.Loopback);
            offerSdp.SessionId = Crypto.GetRandomInt(5).ToString();

            offerSdp.Connection = new SDPConnectionInformation(IPAddress.Loopback);

            SDPMediaAnnouncement audioAnnouncement = new SDPMediaAnnouncement(
                SDPMediaTypesEnum.audio,
               1234,
               new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });

            audioAnnouncement.Transport = RTP_MEDIA_PROFILE;

            offerSdp.Media.Add(audioAnnouncement);

            return Task.FromResult(offerSdp);
        }

        public Task SendDtmf(byte tone, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public void SendMedia(SDPMediaTypesEnum mediaType, uint samplePeriod, byte[] sample)
        {
            throw new NotImplementedException();
        }

        public void setLocalDescription(RTCSessionDescription sessionDescription)
        {
            localDescription = sessionDescription;
        }

        public void setRemoteDescription(RTCSessionDescription sessionDescription)
        {
            remoteDescription = sessionDescription;
        }

        public Task Start()
        {
            var audioLocalAnn = (localDescription != null) ? localDescription.sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).SingleOrDefault() : null;
            var audioRemoteAnn = (remoteDescription != null) ? remoteDescription.sdp.Media.Where(x => x.Media == SDPMediaTypesEnum.audio).SingleOrDefault() : null;

            if (audioLocalAnn == null || audioLocalAnn.MediaFormats.Count == 0)
            {
                throw new ApplicationException("Cannot start audio session without a local audio track being available.");
            }
            else if (audioRemoteAnn == null || audioRemoteAnn.MediaFormats.Count == 0)
            {
                throw new ApplicationException("Cannot start audio session without a remote audio track being available.");
            }

            return Task.CompletedTask;
        }
    }
}
