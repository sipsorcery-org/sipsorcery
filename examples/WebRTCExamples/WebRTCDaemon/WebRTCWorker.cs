//-----------------------------------------------------------------------------
// Filename: WebRtcWorker.cs
//
// Description: Long running background worker hosting WebRTC peer connections.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 18 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using WebSocketSharp.Server;

namespace WebRTCDaemon
{
    public class WebRTCWorker : BackgroundService
    {
        private const int WEBSOCKET_PORT = 8081;
        private const string STUN_URL = "stun:stun.sipsorcery.com";
        private const string MP4_PATH = "media/max_intro.mp4";
        private const string MAX_URL = "max";
        private const VideoCodecsEnum VIDEO_CODEC = VideoCodecsEnum.VP8;
        private const int VP8_OFFERED_FORMATID = 96;
        private const SDPWellKnownMediaFormatsEnum AUDIO_FORMAT = SDPWellKnownMediaFormatsEnum.PCMU;

        private readonly ILogger<WebRTCWorker> _logger;

        private string _certificatePath;
        private FFmpegFileSource _maxSource;
        private VideoTestPatternSource _testPatternSource;
        private FFmpegVideoEndPoint _testPatternEncoder;
        private AudioExtrasSource _musicSource;

        public WebRTCWorker(ILogger<WebRTCWorker> logger, IConfiguration configuration)
        {
            _logger = logger;

            _logger.LogInformation($"WebRTCWorker starting...");

            _certificatePath = configuration["CertificatePath"];

            _logger.LogInformation($"Certificate path {_certificatePath}."); 
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Set up media sources.
            _maxSource = new FFmpegFileSource(MP4_PATH, true, new AudioEncoder());
            _maxSource.Initialise();

            _testPatternSource = new VideoTestPatternSource();
            _testPatternEncoder = new FFmpegVideoEndPoint();
            _testPatternSource.OnVideoSourceRawSample += _testPatternEncoder.ExternalVideoSourceRawSample;

            _musicSource = new AudioExtrasSource(new AudioEncoder(),
                new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
            
            // The same  sources are used for all connected peers (broadcast) so the codecs need 
            // to be restricted to a single well supported option.
            _musicSource.RestrictFormats(format => format.Codec == AudioCodecsEnum.PCMU);
            _maxSource.RestrictFormats(format => format.Codec == VIDEO_CODEC);
            _testPatternEncoder.RestrictFormats(format => format.Codec == VIDEO_CODEC);

            // Start web socket.
            _logger.LogInformation("Starting web socket server...");

            WebSocketServer webSocketServer = null;

            if (!string.IsNullOrWhiteSpace(_certificatePath))
            {
                if(!File.Exists(_certificatePath))
                {
                    throw new ApplicationException($"Certificate path could not be found {_certificatePath}.");
                }

                webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
                webSocketServer.SslConfiguration.ServerCertificate = new X509Certificate2(_certificatePath);
                webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
                webSocketServer.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            }
            else
            {
                webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            }

            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = () => CreatePeerConnection(null));
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>($"/{MAX_URL}", 
                (peer) => peer.CreatePeerConnection = () => CreatePeerConnection(MAX_URL));
            webSocketServer.Start();

            _logger.LogInformation($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private Task<RTCPeerConnection> CreatePeerConnection(string url)
        {
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            var pc = new RTCPeerConnection(config);

            //mediaFileSource.OnEndOfFile += () => pc.Close("source eof");

            MediaStreamTrack videoTrack = new MediaStreamTrack(new List<VideoFormat> { new VideoFormat(VIDEO_CODEC, VP8_OFFERED_FORMATID) }, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);
            MediaStreamTrack audioTrack = new MediaStreamTrack(new List<AudioFormat> { new AudioFormat(AUDIO_FORMAT) }, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(audioTrack);

            IVideoSource videoSource = null;
            IAudioSource audioSource = null;

            if (url == MAX_URL)
            {
                videoSource = _maxSource;
                audioSource = _maxSource;
            }
            else
            {
                videoSource = _testPatternEncoder;
                audioSource = _musicSource;
            }

            pc.OnVideoFormatsNegotiated += (formats) => videoSource.SetVideoSourceFormat(formats.First());
            pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());
            videoSource.OnVideoSourceEncodedSample += pc.SendVideo;
            audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

            pc.onconnectionstatechange += async (state) =>
            {
                _logger.LogInformation($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    videoSource.OnVideoSourceEncodedSample -= pc.SendVideo;
                    audioSource.OnAudioSourceEncodedSample -= pc.SendAudio;
                    await CheckForSourceSubscribers();
                }
                else if(state == RTCPeerConnectionState.connected)
                {
                    await StartSource(url);
                }
            };

            // Diagnostics.
            //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => _logger.LogInformation($"ICE connection state change to {state}.");

            return Task.FromResult(pc);
        }

        private async Task StartSource(string url)
        {
            if(url == MAX_URL)
            {
                _maxSource.ForceKeyFrame();
                await (_maxSource.IsPaused() ? _maxSource.ResumeVideo() : _maxSource.StartVideo());
            }
            else
            {
                _testPatternEncoder.ForceKeyFrame();
                await _testPatternEncoder.StartVideo();
                
                await (_testPatternSource.IsVideoSourcePaused() ? _testPatternSource.ResumeVideo() : _testPatternSource.StartVideo());
                await (_musicSource.IsAudioSourcePaused() ? _musicSource.ResumeAudio() : _musicSource.StartAudio());
            }
        }

        private async Task CheckForSourceSubscribers()
        {
            if(!_testPatternEncoder.HasEncodedVideoSubscribers())
            {
                _logger.LogInformation("Pausing test pattern video source.");
                await _testPatternSource.PauseVideo();
            }

            if(!_musicSource.HasEncodedAudioSubscribers())
            {
                _logger.LogInformation("Pausing music audio source.");
                await _musicSource.PauseAudio();
            }

            if(!_maxSource.HasEncodedVideoSubscribers())
            {
                _logger.LogInformation("Pausing mp4 file source.");
                await _maxSource.Pause();
            }
        }
    }
}
