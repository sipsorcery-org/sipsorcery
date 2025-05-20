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
using System.Runtime.CompilerServices;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;
using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.UnitTests
{
    public static class TestLogHelper
    {
        public static ILogger InitTestLogger(Xunit.Abstractions.ITestOutputHelper output)
        {
            var options = new XUnitLoggerOptions
            {
                Filter = (category, level) => level >= LogLevel.Trace
            };
            var loggerFactory = new XUnitLoggerProvider(output, options);
            var logger = loggerFactory.CreateLogger("unit");
            
            var factory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(loggerFactory);
            });
            
            SIPSorcery.LogFactory.Set(factory);
            return logger;
        }
    }

    public static class TestHelper
    {
        public static string GetCurrentMethodName([CallerMemberName] string methodName = default) => methodName;
    }

    public class MockMediaSession : IMediaSession
    {
        private const string RTP_MEDIA_PROFILE = "RTP/AVP";

        public SDP RemoteDescription { get; private set; }

        public bool IsClosed { get; private set; }
        public bool HasAudio => true;
        public bool HasVideo => false;
        public bool HasText => false;
        public IPAddress RtpBindAddress => null;

        public IPAddress RtpAdvertisedAddress { get; set; }

#pragma warning disable 67
        public event Action<string> OnRtpClosed;
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket> OnRtpPacketReceived;
        public event Action<IPEndPoint, RTPEvent, RTPHeader> OnRtpEvent;
        public event Action<SDPMediaTypesEnum> OnTimeout;
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

        public Task SendText(string text, CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }
}
