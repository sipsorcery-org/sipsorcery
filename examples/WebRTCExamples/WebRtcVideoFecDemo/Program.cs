//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Standalone WebRTC video sender demonstration featuring H.264 video,
// RFC 5109 ULPFEC, RFC 2198 RED encapsulation, impairment loss injection,
// and browser receiver recovery.
//
// Author(s):
// SIPSorcery contributors
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace WebRtcVideoFecDemo
{
    public class Program
    {
        private static RTCPeerConnection? _activePeerConnection;
        private static SyntheticH264Source? _videoSource;
        private static bool _impairmentDropActive = false;
        private static int _impairmentDropInterval = 10; // Drop 1 in every 10 packets when active
        private static int _packetCounter = 0;

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            var app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            // WebRTC Offer/Answer negotiation endpoint
            app.MapPost("/offer", async (HttpRequest request, ILogger<Program> logger) =>
            {
                string sdpOffer;
                using (var reader = new StreamReader(request.Body))
                {
                    sdpOffer = await reader.ReadToEndAsync();
                }

                logger.LogInformation("Received browser SDP Offer ({Length} bytes).", sdpOffer.Length);

                // Tear down previous session if any
                _activePeerConnection?.Close("new session started");
                _videoSource?.Dispose();

                var pc = new RTCPeerConnection(new RTCConfiguration
                {
                    iceServers = new List<RTCIceServer> { new RTCIceServer { urls = "stun:stun.cloudflare.com:3478" } }
                });

                // Configure video track: H.264 Baseline Profile + RED + ULPFEC
                var h264Format = new SDPAudioVideoMediaFormat(
                    SDPMediaTypesEnum.video,
                    96,
                    "H264",
                    90000,
                    0,
                    "level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42e01f");

                var videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false,
                    new List<SDPAudioVideoMediaFormat> { h264Format }, MediaStreamStatusEnum.SendOnly)
                {
                    Redundancy = RtpRedundancyOptions.Red | RtpRedundancyOptions.Ulpfec,
                    FecConfig = new RtpUlpfecConfig
                    {
                        TargetOverheadFraction = 0.20f, // 20% parity overhead
                        MaxGroupPackets = 16,
                        MinGroupPackets = 1
                    }
                };
                pc.addTrack(videoTrack);

                pc.onconnectionstatechange += (state) =>
                {
                    logger.LogInformation("Peer connection state: {State}", state);
                    if (state == RTCPeerConnectionState.connected && pc.VideoStream != null)
                    {
                        var originalChannel = pc.VideoStream.GetRTPChannel();
                        if (originalChannel != null && !(originalChannel is ImpairedRtpChannel))
                        {
                            var impaired = new ImpairedRtpChannel(originalChannel);
                            impaired.DropPredicate = (buffer) =>
                            {
                                if (!_impairmentDropActive) return false;
                                try
                                {
                                    var hdr = new RTPHeader(buffer);
                                    var encoder = pc.VideoStream.FecEncoder;
                                    if (encoder != null && hdr.PayloadType == encoder.RedPayloadType && buffer.Length > hdr.Length)
                                    {
                                        byte redHeader = buffer[hdr.Length];
                                        int innerPt = redHeader & 0x7F;
                                        if (innerPt == encoder.MediaPayloadType) // Negotiated primary media.
                                        {
                                            int count = System.Threading.Interlocked.Increment(ref _packetCounter);
                                            if ((count % _impairmentDropInterval) == 0)
                                            {
                                                logger.LogWarning("--> [TRANSPORT IMPAIRMENT] Deliberately dropping media packet SN={SeqNum}, TS={Timestamp}.",
                                                    hdr.SequenceNumber, hdr.Timestamp);
                                                return true;
                                            }
                                        }
                                    }
                                }
                                catch { }
                                return false;
                            };
                            pc.VideoStream.AddRtpChannel(impaired);
                        }
                    }
                };

                // Initialize synthetic H.264 video source
                _videoSource = new SyntheticH264Source(fps: 30);
                _videoSource.OnFrameReady += (duration, sample) =>
                {
                    if (pc.connectionState == RTCPeerConnectionState.connected)
                    {
                        pc.SendVideo(duration, sample);
                    }
                };

                pc.onconnectionstatechange += (state) =>
                {
                    logger.LogInformation("Peer connection state: {State}", state);
                    if (state == RTCPeerConnectionState.connected)
                    {
                        _videoSource.Start();
                    }
                    else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                    {
                        _videoSource.Stop();
                    }
                };

                var setRemoteResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = sdpOffer
                });

                if (setRemoteResult != SetDescriptionResultEnum.OK)
                {
                    logger.LogError("Failed to set remote description: {Result}", setRemoteResult);
                    return Results.BadRequest($"Set remote description error: {setRemoteResult}");
                }

                var answer = pc.createAnswer();
                await pc.setLocalDescription(answer);

                _activePeerConnection = pc;

                logger.LogInformation("Returning SDP Answer with negotiated FEC status: {FecStatus}",
                    pc.VideoStream?.FecEncoder?.IsActive == true ? "ACTIVE" : "INACTIVE");

                return Results.Text(answer.sdp, "application/sdp");
            });

            // Impairment control endpoint
            app.MapPost("/impairment/toggle", (HttpRequest request) =>
            {
                _impairmentDropActive = !_impairmentDropActive;
                return Results.Json(new
                {
                    active = _impairmentDropActive,
                    dropInterval = _impairmentDropInterval,
                    status = _impairmentDropActive ? "Dropping 1 in every 10 media packets" : "Impairment disabled (0% loss)"
                });
            });

            // Metrics monitoring endpoint
            app.MapGet("/metrics", () =>
            {
                var vs = _activePeerConnection?.VideoStream;
                var metrics = vs?.FecMetrics;

                return Results.Json(new
                {
                    connectionState = _activePeerConnection?.connectionState.ToString() ?? "none",
                    fecActive = vs?.FecEncoder?.IsActive ?? false,
                    impairmentActive = _impairmentDropActive,
                    mediaPacketsSent = metrics?.MediaPacketsSent ?? 0,
                    mediaBytesSent = metrics?.MediaBytesSent ?? 0,
                    repairPacketsGenerated = metrics?.RepairPacketsGenerated ?? 0,
                    repairPacketsSent = metrics?.RepairPacketsSent ?? 0,
                    repairBytesSent = metrics?.RepairBytesSent ?? 0,
                    packetsDroppedByImpairment = metrics?.PacketsDroppedByImpairment ?? 0,
                    packetOverheadPercent = (metrics?.PacketOverheadRatio ?? 0) * 100.0,
                    transportErrors = metrics?.TransportErrors ?? 0
                });
            });

            Console.WriteLine("==================================================================");
            Console.WriteLine("SIPSorcery Video FEC (RFC 5109 + RFC 2198) Standalone Demo");
            Console.WriteLine("Navigate your browser to: http://localhost:5000");
            Console.WriteLine("==================================================================");

            await app.RunAsync();
        }
    }

    public class ImpairedRtpChannel : RTPChannel
    {
        private readonly RTPChannel _inner;
        public Func<byte[], bool>? DropPredicate { get; set; }

        public ImpairedRtpChannel(RTPChannel inner)
            : base(false, System.Net.IPAddress.Loopback)
        {
            _inner = inner;
            RTPSrflxEndPoint = inner.RTPSrflxEndPoint;
        }

        public override System.Net.Sockets.SocketError Send(RTPChannelSocketsEnum sendOn, System.Net.IPEndPoint dstEndPoint, byte[] buffer)
        {
            if (sendOn == RTPChannelSocketsEnum.RTP && DropPredicate != null && DropPredicate(buffer))
            {
                return System.Net.Sockets.SocketError.Success;
            }
            return _inner.Send(sendOn, dstEndPoint, buffer);
        }
    }
}
