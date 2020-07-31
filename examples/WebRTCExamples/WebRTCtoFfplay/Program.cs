//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Pipes an SDP offer and forwards subsequent RTP packet to 
// an external ffplay process.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 08 Jul 2020	Aaron Clauson	Created, Dublin, Ireland.
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
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace SIPSorcery.Examples
{
    public class WebRtcClient : WebSocketBehavior
    {
        public RTCPeerConnection pc;

        public event Func<WebSocketContext, Task<RTCPeerConnection>> WebSocketOpened;
        public event Func<WebSocketContext, RTCPeerConnection, string, Task> OnMessageReceived;

        public WebRtcClient()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnMessageReceived(this.Context, pc, e.Data);
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            pc = await WebSocketOpened(this.Context);
        }
    }

    class Program
    {
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;
        private const string FFPLAY_DEFAULT_SDP_PATH = "ffplay.sdp";
        private const string FFPLAY_DEFAULT_COMMAND = "ffplay -probesize 32 -protocol_whitelist \"file,rtp,udp\" -i {0}";
        private const int FFPLAY_DEFAULT_AUDIO_PORT = 5016;
        private const int FFPLAY_DEFAULT_VIDEO_PORT = 5018;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static RTCPeerConnection _activePeerConnection;

        /// <summary>
        /// To filter the audio or video codecs when the initial offer is from the remote party
        /// add the desired codecs to these two lists. Leave empty to accept all codecs.
        /// 
        /// Note: During testing ffplay seemed to have problems if the SDP input file had multiple 
        /// codecs. It was observed to select the wrong codec for the RTP header payload ID it was 
        /// receiving. It may be that ffplay decides it can choose it's favorite codec and the remote
        /// party will honor that. The simple fix is to filter to a single audio and video codec.
        /// </summary>
        private static List<SDPMediaFormatsEnum> AudioFormatsFilter = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.OPUS };
        private static List<SDPMediaFormatsEnum> VideoFormatsFilter = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.VP9 };

        /// <summary>
        /// Set the codecs sent when the offer is made to the remote peer. Note that no encoding/decoding is
        /// done by this program. ffplay will need to support the selected codec.
        /// </summary>
        private static List<SDPMediaFormat> AudioOfferFormats = new List<SDPMediaFormat> {
            new SDPMediaFormat(SDPMediaFormatsEnum.OPUS)
            {
                FormatID = "111",
                FormatAttribute = "opus/48000/2",
                FormatParameterAttribute = "minptime=10;useinbandfec=1",
            }};
        private static List<SDPMediaFormat> VideoOfferFormats = new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP9) };

        static async Task Main()
        {
            CancellationTokenSource exitCts = new CancellationTokenSource();

            AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            //_webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            //_webSocketServer.SslConfiguration.ServerCertificate = new X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
            //_webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            //_webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
            _webSocketServer.AddWebSocketService<WebRtcClient>("/", (client) =>
            {
                client.WebSocketOpened += SendOffer;
                client.OnMessageReceived += WebSocketMessageReceived;
            });
            _webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            await Task.Run(() => OnKeyPress(exitCts.Token));

            _webSocketServer.Stop();
        }

        private static Task OnKeyPress(CancellationToken exit)
        {
            while (!exit.WaitHandle.WaitOne(0))
            {
                var keyProps = Console.ReadKey();

                if (keyProps.KeyChar == 'k')
                {
                    if (_activePeerConnection != null)
                    {
                        Console.WriteLine("Requesting key frame.");
                        var localVideoSsrc = _activePeerConnection.VideoLocalTrack.Ssrc;
                        var remoteVideoSsrc = _activePeerConnection.VideoRemoteTrack.Ssrc;
                        RTCPFeedback pli = new RTCPFeedback(localVideoSsrc, remoteVideoSsrc, PSFBFeedbackTypesEnum.PLI);
                        _activePeerConnection.SendRtcpFeedback(SDPMediaTypesEnum.video, pli);
                    }
                }
                else if (keyProps.KeyChar == 'q')
                {
                    // Quit application.
                    Console.WriteLine("Quitting");
                    break;
                }
            }

            return Task.CompletedTask;
        }

        private static async Task<RTCPeerConnection> SendOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, sending offer.");

            var pc = Createpc(context);

            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, AudioOfferFormats, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, VideoOfferFormats, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(videoTrack);

            var offerInit = pc.createOffer(null);
            await pc.setLocalDescription(offerInit);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            context.WebSocket.Send(offerInit.sdp);

            return pc;
        }

        private static RTCPeerConnection Createpc(WebSocketContext context)
        {
            var pc = new RTCPeerConnection(null);

            pc.onicecandidateerror += (candidate, error) => logger.LogWarning($"Error adding remote ICE candidate. {error} {candidate}");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
            //pc.OnReceiveReport += (type, rtcp) => logger.LogDebug($"RTCP {type} report received.");
            pc.OnRtcpBye += (reason) => logger.LogDebug($"RTCP BYE receive, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");

            pc.onicecandidate += (candidate) =>
            {
                if (pc.signalingState == RTCSignalingState.have_local_offer ||
                    pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    context.WebSocket.Send($"candidate:{candidate}");
                }
            };

            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    logger.LogDebug("Creating RTP session for ffplay.");

                    var rtpSession = CreateRtpSession(pc.AudioLocalTrack?.Capabilities, pc.VideoLocalTrack?.Capabilities);
                    pc.OnRtpPacketReceived += (rep, media, rtpPkt) =>
                    {
                        if (media == SDPMediaTypesEnum.audio && rtpSession.AudioDestinationEndPoint != null)
                        {
                            //logger.LogDebug($"Forwarding {media} RTP packet to ffplay timestamp {rtpPkt.Header.Timestamp}.");
                            rtpSession.SendRtpRaw(media, rtpPkt.Payload, rtpPkt.Header.Timestamp, rtpPkt.Header.MarkerBit, rtpPkt.Header.PayloadType);
                        }
                        else if (media == SDPMediaTypesEnum.video && rtpSession.VideoDestinationEndPoint != null)
                        {
                            //logger.LogDebug($"Forwarding {media} RTP packet to ffplay timestamp {rtpPkt.Header.Timestamp}.");
                            rtpSession.SendRtpRaw(media, rtpPkt.Payload, rtpPkt.Header.Timestamp, rtpPkt.Header.MarkerBit, rtpPkt.Header.PayloadType);
                        }
                    };
                    pc.OnRtpClosed += (reason) => rtpSession.Close(reason);
                }

            };

            _activePeerConnection = pc;

            return pc;
        }

        private static RTPSession CreateRtpSession(List<SDPMediaFormat> audioFormats, List<SDPMediaFormat> videoFormats)
        {
            var rtpSession = new RTPSession(false, false, false, IPAddress.Loopback);
            bool hasAudio = false;
            bool hasVideo = false;

            if (audioFormats != null && audioFormats.Count > 0)
            {
                MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, audioFormats, MediaStreamStatusEnum.SendRecv);
                rtpSession.addTrack(audioTrack);
                hasAudio = true;
            }

            if (videoFormats != null && videoFormats.Count > 0)
            {
                MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, videoFormats, MediaStreamStatusEnum.SendRecv);
                rtpSession.addTrack(videoTrack);
                hasVideo = true;
            }

            var sdpOffer = rtpSession.CreateOffer(null);

            // Because the SDP being written to the file is the input to ffplay the connection ports need to be changed
            // to the ones ffplay will be listening on.
            if (hasAudio)
            {
                sdpOffer.Media.Single(x => x.Media == SDPMediaTypesEnum.audio).Port = FFPLAY_DEFAULT_AUDIO_PORT;
            }

            if (hasVideo)
            {
                sdpOffer.Media.Single(x => x.Media == SDPMediaTypesEnum.video).Port = FFPLAY_DEFAULT_VIDEO_PORT;
            }

            Console.WriteLine(sdpOffer);

            using (StreamWriter sw = new StreamWriter(FFPLAY_DEFAULT_SDP_PATH))
            {
                sw.Write(sdpOffer);
            }

            string ffplayCommand = String.Format(FFPLAY_DEFAULT_COMMAND, FFPLAY_DEFAULT_SDP_PATH);
            Console.WriteLine($"Start ffplay using the command below:");
            Console.WriteLine(ffplayCommand);
            Console.WriteLine($"To request the remote peer to send a video key frame press 'k'");

            rtpSession.Start();
            rtpSession.SetDestination(SDPMediaTypesEnum.audio, new IPEndPoint(IPAddress.Loopback, FFPLAY_DEFAULT_AUDIO_PORT), new IPEndPoint(IPAddress.Loopback, FFPLAY_DEFAULT_AUDIO_PORT + 1));
            rtpSession.SetDestination(SDPMediaTypesEnum.video, new IPEndPoint(IPAddress.Loopback, FFPLAY_DEFAULT_VIDEO_PORT), new IPEndPoint(IPAddress.Loopback, FFPLAY_DEFAULT_VIDEO_PORT + 1));

            return rtpSession;
        }

        private static async Task WebSocketMessageReceived(WebSocketContext context, RTCPeerConnection pc, string message)
        {
            try
            {
                if (pc.localDescription == null)
                {
                    //logger.LogDebug("Offer SDP: " + message);
                    logger.LogDebug("Offer SDP received.");

                    // Add local media tracks depending on what was offered. Also add local tracks with the same media ID as 
                    // the remote tracks so that the media announcement in the SDP answer are in the same order.
                    SDP remoteSdp = SDP.ParseSDPDescription(message);

                    foreach (var ann in remoteSdp.Media)
                    {
                        var capbilities = FilterCodecs(ann.Media, ann.MediaFormats);
                        MediaStreamTrack track = new MediaStreamTrack(ann.Media, false, capbilities, MediaStreamStatusEnum.RecvOnly);
                        pc.addTrack(track);
                    }

                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.offer });

                    var answer = pc.createAnswer(null);
                    await pc.setLocalDescription(answer);

                    Console.WriteLine(answer.sdp);

                    context.WebSocket.Send(answer.sdp);
                }
                else if (pc.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                }
                else
                {
                    logger.LogDebug("ICE Candidate: " + message);

                    if (string.IsNullOrWhiteSpace(message) || message.Trim().ToLower() == SDP.END_ICE_CANDIDATES_ATTRIBUTE)
                    {
                        logger.LogDebug("End of candidates message received.");
                    }
                    else
                    {
                        var candInit = Newtonsoft.Json.JsonConvert.DeserializeObject<RTCIceCandidateInit>(message);
                        pc.addIceCandidate(candInit);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebSocketMessageReceived. " + excp.Message);
            }
        }

        private static List<SDPMediaFormat> FilterCodecs(SDPMediaTypesEnum mediaType, List<SDPMediaFormat> formats)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                if (AudioFormatsFilter.Count == 0)
                {
                    return formats;
                }
                else
                {
                    var audioFormats = new List<SDPMediaFormat>();

                    foreach (var format in formats)
                    {
                        if (AudioFormatsFilter.Any(x => x == format.FormatCodec))
                        {
                            audioFormats.Add(format);
                        }
                    }

                    return audioFormats;
                }
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                if (VideoFormatsFilter.Count == 0)
                {
                    return formats;
                }
                else
                {
                    var videoFormats = new List<SDPMediaFormat>();

                    foreach (var format in formats)
                    {
                        if (VideoFormatsFilter.Any(x => x == format.FormatCodec))
                        {
                            videoFormats.Add(format);
                        }
                    }

                    return videoFormats;
                }
            }
            else
            {
                return formats;
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
    }
}
