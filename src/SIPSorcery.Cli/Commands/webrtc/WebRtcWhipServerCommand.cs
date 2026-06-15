//-----------------------------------------------------------------------------
// Filename: WebRtcWhipServerCommand.cs
//
// Description: The "sipsorcery webrtc whip-server" verb. Acts as a WHIP
// (WebRTC-HTTP Ingestion Protocol, RFC 9725) endpoint: accepts a publisher's
// SDP offer over HTTP POST, answers it, completes ICE/DTLS and receives the
// published media, reporting packet counts and sequence anomalies.
//
// The motivating use case is isolating where stream problems originate: a
// publisher (e.g. ffmpeg's whip muxer, OBS) can publish DIRECTLY to this verb
// over the loopback or LAN, removing the SFU and internet path from the
// equation. Reordering observed here is the publisher's send order; a clean
// result here with anomalies via an SFU points upstream.
//
// Received video can be rendered or captured with --video, the same as the
// "whep" verb: "play" spawns an ffplay window, a file path captures the
// bitstream (H264 Annex B, VP8 in IVF) and "-" writes it to stdout for piping.
// Decode is delegated to the consumer so no video codecs run in-process. Paired
// with a local publisher this gives a self-contained encode -> network -> decode
// -> view loop with no SFU or internet path involved.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.Cli.Commands;

public sealed class WebRtcWhipServerCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 60;
    private const int DEFAULT_MEDIA_DURATION_SECONDS = 10;
    private const string DEFAULT_LISTEN_URL = "http://localhost:8080/whip";
    private const int VP8_PAYLOAD_ID = 96;
    private const int H264_PAYLOAD_ID = 100;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record WhipServerResult(
        bool Success,
        string ListenUrl,
        string ConnectionState,
        long? ConnectTimeMs,
        int? MediaDurationMs,
        int AudioPackets,
        long AudioLost,
        int AudioOutOfOrder,
        int AudioDuplicates,
        int VideoPackets,
        long VideoLost,
        int VideoOutOfOrder,
        int VideoDuplicates,
        string? Error,
        int? VideoFrames = null,
        long? VideoBytesWritten = null,
        double? VideoFps = null,
        int? VideoFramesDropped = null);

    public WebRtcWhipServerCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var listenOption = new Option<string>("--listen")
        {
            Description = $"The HTTP URL to accept WHIP publish offers on. Defaults to {DEFAULT_LISTEN_URL}.",
            DefaultValueFactory = _ => DEFAULT_LISTEN_URL
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "Optional bearer token publishers must supply in the Authorization header."
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to receive media for after the connection is established.",
            DefaultValueFactory = _ => DEFAULT_MEDIA_DURATION_SECONDS
        };

        var videoOption = new Option<string?>("--video")
        {
            Description = "Where to send the received video: \"play\" to render in an ffplay window, a file path " +
                          "(H264 is written as Annex B, VP8 in an IVF container), or \"-\" for the bitstream on stdout " +
                          "(the result then moves to stderr), e.g. pipe to \"mpv --vo=tct -\" for video in the terminal."
        };

        var decodeOption = new Option<bool>("--decode")
        {
            Description = "Decode the received frames in-process with the SIPSorcery (FFmpeg) video decoder and send " +
                          "raw RGB to the --video sink, instead of passing the encoded bitstream through for the consumer " +
                          "to decode (the default). Requires the FFmpeg shared libraries and a --video sink."
        };

        var ffmpegPathOption = new Option<string?>("--ffmpeg-path")
        {
            Description = "Directory containing the FFmpeg shared libraries for --decode. Defaults to the system path."
        };

        // Self-publish options: when --publish is set the server feeds its own listener with an ffmpeg
        // test pattern, giving a one command self-contained loop with no separate publisher process.
        var publishOption = new Option<bool>("--publish")
        {
            Description = "Also publish an ffmpeg test pattern to this server's own URL, for a self-contained loop in one command. Requires ffmpeg on the PATH."
        };

        var presetOption = new Option<string>("--preset")
        {
            Description = "With --publish: resolution preset 360p, 480p, 720p, 1080p, 1440p or 4k. Ignored if --size is given.",
            DefaultValueFactory = _ => "720p"
        };

        var sizeOption = new Option<string?>("--size", "-s")
        {
            Description = "With --publish: explicit frame size WxH (e.g. 1280x720), overriding --preset."
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = "With --publish: frame rate.",
            DefaultValueFactory = _ => 30
        };

        var codecOption = new Option<string>("--codec")
        {
            Description = "With --publish: video codec h264 (libx264) or vp8 (libvpx). h264 is the reliable choice; ffmpeg's WHIP muxer may not support vp8.",
            DefaultValueFactory = _ => "h264"
        };

        var bitrateOption = new Option<string?>("--bitrate")
        {
            Description = "With --publish: target video bitrate, e.g. 2M or 4000k. Defaults to ffmpeg's rate control for h264 and 2M for vp8."
        };

        var audioOption = new Option<bool>("--audio")
        {
            Description = "With --publish: also publish a 440 Hz Opus audio tone."
        };

        var command = new Command("whip-server", "Act as a WHIP endpoint: accept a publisher's offer (e.g. from ffmpeg or OBS) and report on the received media.");
        command.Options.Add(listenOption);
        command.Options.Add(tokenOption);
        command.Options.Add(durationOption);
        command.Options.Add(videoOption);
        command.Options.Add(decodeOption);
        command.Options.Add(ffmpegPathOption);
        command.Options.Add(publishOption);
        command.Options.Add(presetOption);
        command.Options.Add(sizeOption);
        command.Options.Add(fpsOption);
        command.Options.Add(codecOption);
        command.Options.Add(bitrateOption);
        command.Options.Add(audioOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(listenOption)!,
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(videoOption),
            parseResult.GetValue(decodeOption),
            parseResult.GetValue(ffmpegPathOption),
            parseResult.GetValue(publishOption),
            new FfmpegPublisher.Settings(
                parseResult.GetValue(presetOption)!,
                parseResult.GetValue(sizeOption),
                parseResult.GetValue(fpsOption),
                parseResult.GetValue(codecOption)!,
                parseResult.GetValue(bitrateOption),
                parseResult.GetValue(audioOption)),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string listenUrl, string? token, int durationSeconds, string? videoOut,
        bool decode, string? ffmpegPath, bool publish, FfmpegPublisher.Settings publishSettings,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(WebRtcWhipServerCommand));

        if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var listenUri) || listenUri.Scheme != Uri.UriSchemeHttp)
        {
            return WriteResult(asJson, stdoutClaimed: false,
                new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                    $"Could not parse \"{listenUrl}\" as an HTTP URL (HTTPS is not supported for the local listener)."),
                ExitCodes.InvalidArgument);
        }

        if (decode && string.IsNullOrWhiteSpace(videoOut))
        {
            return WriteResult(asJson, stdoutClaimed: false,
                new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                    "--decode requires a --video sink (e.g. --video play) for the decoded frames."),
                ExitCodes.InvalidArgument);
        }

        if (decode)
        {
            try
            {
                FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL, ffmpegPath, logger);
            }
            catch (Exception excp)
            {
                return WriteResult(asJson, stdoutClaimed: false,
                    new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                        $"Could not initialise FFmpeg for --decode: {excp.Message}. Install the FFmpeg shared libraries (e.g. winget install ffmpeg) or pass --ffmpeg-path."),
                    ExitCodes.TransportError);
            }
        }

        if (publish && !FfmpegPublisher.TryValidate(publishSettings, out string? publishError))
        {
            return WriteResult(asJson, stdoutClaimed: false,
                new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0, publishError),
                ExitCodes.InvalidArgument);
        }

        // In decode mode the frames are decoded in-process and the raw RGB sent to the sink.
        using var decoder = decode ? new FFmpegVideoEncoder() : null;
        using var videoSink = VideoSink.Create(videoOut, logger, out string? videoSinkError, decoder);

        if (videoSinkError != null)
        {
            return WriteResult(asJson, videoSink.IsStdout,
                new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0, videoSinkError),
                ExitCodes.InvalidArgument);
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://{listenUri.Authority}/");

        RTCPeerConnection? pc = null;
        FfmpegPublisher.Publisher? publisher = null;

        try
        {
            listener.Start();

            // Operator guidance, deliberately on stderr so stdout remains the result channel.
            Console.Error.WriteLine($"Waiting up to {timeoutSeconds}s for a WHIP publish offer on {listenUrl} ...");

            if (publish)
            {
                // The listener is already bound, so the self-publish cannot race the startup. The
                // publisher runs until the session ends and is stopped in the finally block.
                publisher = FfmpegPublisher.Start(listenUrl, publishSettings, token, 0, out string publishCommand, out string? startError);
                if (publisher == null)
                {
                    return WriteResult(asJson, videoSink.IsStdout,
                        new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0, startError),
                        ExitCodes.TransportError);
                }

                Console.Error.WriteLine($"Publishing to {listenUrl} with: {publishCommand}");
            }
            else
            {
                Console.Error.WriteLine($"e.g. ffmpeg -re -f lavfi -i testsrc=size=640x360 -f lavfi -i sine=frequency=440 " +
                    $"-pix_fmt yuv420p -c:v libx264 -profile:v baseline -r 25 -g 50 -c:a libopus -ar 48000 -ac 2 " +
                    $"-f whip{(token != null ? $" -authorization \"{token}\"" : string.Empty)} \"{listenUrl}\"");
            }

            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // ---- Wait for an acceptable POST with the SDP offer. ----
            HttpListenerContext? offerContext = null;
            var offerDeadline = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), overallCts.Token);
            // When self-publishing, watch for the ffmpeg publisher dying so we fail fast instead of
            // waiting out the whole offer timeout (e.g. if the codec is unsupported by its WHIP muxer).
            var publisherExit = publisher?.WaitForExitAsync(ct);

            while (offerContext == null)
            {
                var getContext = listener.GetContextAsync();
                var completed = publisherExit != null
                    ? await Task.WhenAny(getContext, offerDeadline, publisherExit).ConfigureAwait(false)
                    : await Task.WhenAny(getContext, offerDeadline).ConfigureAwait(false);

                if (ct.IsCancellationRequested)
                {
                    return WriteResult(asJson, videoSink.IsStdout,
                        new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0, "Cancelled."),
                        ExitCodes.Timeout);
                }

                if (completed == offerDeadline)
                {
                    return WriteResult(asJson, videoSink.IsStdout,
                        new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                            $"No publish offer was received within {timeoutSeconds}s."),
                        ExitCodes.Timeout);
                }

                if (publisherExit != null && completed == publisherExit)
                {
                    return WriteResult(asJson, videoSink.IsStdout,
                        new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                            $"The ffmpeg publisher exited (code {publisher!.ExitCode}) before sending a WHIP offer. The chosen --codec may be " +
                            "unsupported by ffmpeg's WHIP muxer (h264 is the reliable choice); see the ffmpeg output above."),
                        ExitCodes.TransportError);
                }

                var context = await getContext.ConfigureAwait(false);
                var request = context.Request;

                if (request.HttpMethod != "POST")
                {
                    Respond(context, HttpStatusCode.MethodNotAllowed);
                }
                else if (token != null && request.Headers["Authorization"] != $"Bearer {token}")
                {
                    logger.LogWarning("Rejected publish offer with missing or incorrect bearer token.");
                    Respond(context, HttpStatusCode.Unauthorized);
                }
                else
                {
                    offerContext = context;
                }
            }

            string offerSdp;
            using (var reader = new StreamReader(offerContext.Request.InputStream, Encoding.UTF8))
            {
                offerSdp = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            logger.LogDebug("Received WHIP offer on {Path} from {Remote}.", offerContext.Request.Url?.AbsolutePath, offerContext.Request.RemoteEndPoint);

            // ---- Create the receiving peer connection. ----
            pc = new RTCPeerConnection();

            pc.addTrack(new MediaStreamTrack(new List<AudioFormat>
            {
                AudioCommonlyUsedFormats.OpusWebRTC,
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)
            }, MediaStreamStatusEnum.RecvOnly));

            pc.addTrack(new MediaStreamTrack(new List<VideoFormat>
            {
                new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID),
                new VideoFormat(VideoCodecsEnum.H264, H264_PAYLOAD_ID, parameters: "packetization-mode=1")
            }, MediaStreamStatusEnum.RecvOnly));

            var audioStats = new RtpStreamStats();
            var videoStats = new RtpStreamStats();
            pc.OnRtpPacketReceived += RtpStreamStats.CreateRtpHandler(audioStats, videoStats, logger);

            // Count depacketised video frames and timestamp the first and last for the frame rate
            // stat (measured first frame to last frame, independent of any sink). When a sink is
            // active, hand the frame to it (the sink decodes if --decode was set).
            int videoFramesReceived = 0;
            long firstFrameTicks = 0;
            long lastFrameTicks = 0;
            pc.OnVideoFrameReceived += (remoteEndPoint, timestamp, frame, format) =>
            {
                long nowTicks = Stopwatch.GetTimestamp();
                if (Interlocked.Increment(ref videoFramesReceived) == 1)
                {
                    firstFrameTicks = nowTicks;
                }
                lastFrameTicks = nowTicks;

                if (videoSink.IsActive)
                {
                    videoSink.WriteFrame(frame, timestamp, format);
                }
            };

            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug("Peer connection state changed to {State}.", state);

                if (state == RTCPeerConnectionState.connected)
                {
                    connected.TrySetResult(true);
                }
                else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
                {
                    connected.TrySetResult(false);
                }
            };

            var setOfferResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.offer,
                sdp = offerSdp
            });

            if (setOfferResult != SetDescriptionResultEnum.OK)
            {
                Respond(offerContext, HttpStatusCode.BadRequest);
                return WriteResult(asJson, videoSink.IsStdout,
                    new WhipServerResult(false, listenUrl, pc.connectionState.ToString(), null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                        $"The publisher's SDP offer could not be applied: {setOfferResult}."),
                    ExitCodes.Failed);
            }

            var answer = pc.createAnswer();
            await pc.setLocalDescription(answer).ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            // 201 + answer SDP + a Location header identifying the session resource (RFC 9725).
            string resourcePath = $"{offerContext.Request.Url?.AbsolutePath?.TrimEnd('/')}/{Guid.NewGuid().ToString("N")[..8]}";
            var answerBytes = Encoding.UTF8.GetBytes(answer.sdp);
            offerContext.Response.StatusCode = (int)HttpStatusCode.Created;
            offerContext.Response.ContentType = "application/sdp";
            offerContext.Response.AddHeader("Location", resourcePath);
            offerContext.Response.ContentLength64 = answerBytes.Length;
            await offerContext.Response.OutputStream.WriteAsync(answerBytes, overallCts.Token).ConfigureAwait(false);
            offerContext.Response.Close();

            // ---- Service subsequent HTTP requests (DELETE = publisher hangup) in the background. ----
            var publisherEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!overallCts.IsCancellationRequested)
                    {
                        var context = await listener.GetContextAsync().ConfigureAwait(false);
                        if (context.Request.HttpMethod == "DELETE")
                        {
                            logger.LogDebug("Publisher sent DELETE for {Path}, ending the session.", context.Request.Url?.AbsolutePath);
                            Respond(context, HttpStatusCode.OK);
                            publisherEnded.TrySetResult(true);
                        }
                        else
                        {
                            // PATCH (trickle ICE) is not supported; all candidates are in the answer.
                            Respond(context, HttpStatusCode.MethodNotAllowed);
                        }
                    }
                }
                catch (Exception)
                {
                    // Listener stopped; the session is over.
                }
            }, overallCts.Token);

            // ---- Wait for the connection, then the media window. ----
            var connectCompleted = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), overallCts.Token)).ConfigureAwait(false);

            if (connectCompleted != connected.Task || !await connected.Task.ConfigureAwait(false))
            {
                return WriteResult(asJson, videoSink.IsStdout,
                    new WhipServerResult(false, listenUrl, pc.connectionState.ToString(), stopwatch.ElapsedMilliseconds, null,
                        audioStats.Packets, audioStats.Lost, audioStats.OutOfOrder, audioStats.Duplicates,
                        videoStats.Packets, videoStats.Lost, videoStats.OutOfOrder, videoStats.Duplicates,
                        ct.IsCancellationRequested ? "Cancelled." :
                        connectCompleted == connected.Task
                            ? $"The peer connection failed (state {pc.connectionState})."
                            : $"The peer connection did not reach connected within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            long connectTimeMs = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Publisher connected in {ConnectTimeMs}ms, receiving media for {Duration}s.", connectTimeMs, durationSeconds);

            var mediaWindow = Stopwatch.StartNew();
            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(durationSeconds), overallCts.Token), publisherEnded.Task).ConfigureAwait(false);
            mediaWindow.Stop();

            bool gotMedia = audioStats.Packets + videoStats.Packets > 0;

            // Dispose the sink before writing the result so files are finalised, ffplay drains
            // and, in stdout mode, the bitstream completes before the result lands on stderr.
            videoSink.Dispose();

            // Frame rate measured from the first received frame to the last (frames - 1 intervals),
            // so connection ramp-up and the idle head/tail of the media window do not skew it.
            double? videoFps = null;
            if (videoFramesReceived > 1 && lastFrameTicks > firstFrameTicks)
            {
                double frameSpanSeconds = (lastFrameTicks - firstFrameTicks) / (double)Stopwatch.Frequency;
                videoFps = Math.Round((videoFramesReceived - 1) / frameSpanSeconds, 1);
            }

            return WriteResult(asJson, videoSink.IsStdout,
                new WhipServerResult(gotMedia, listenUrl, pc.connectionState.ToString(),
                    connectTimeMs, (int)mediaWindow.ElapsedMilliseconds,
                    audioStats.Packets, audioStats.Lost, audioStats.OutOfOrder, audioStats.Duplicates,
                    videoStats.Packets, videoStats.Lost, videoStats.OutOfOrder, videoStats.Duplicates,
                    gotMedia ? null : "The publisher connected but no media packets were received.",
                    videoSink.IsActive ? videoSink.FramesWritten : null,
                    videoSink.IsActive ? videoSink.BytesWritten : null,
                    videoFps,
                    videoSink.IsActive ? videoSink.DroppedFrames : null),
                gotMedia ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (HttpListenerException excp)
        {
            return WriteResult(asJson, videoSink.IsStdout,
                new WhipServerResult(false, listenUrl, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                    $"Could not listen on {listenUrl}: {excp.Message}"),
                ExitCodes.TransportError);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson, videoSink.IsStdout,
                new WhipServerResult(false, listenUrl, pc?.connectionState.ToString() ?? "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0, "Cancelled."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson, videoSink.IsStdout,
                new WhipServerResult(false, listenUrl, pc?.connectionState.ToString() ?? "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            publisher?.Stop();
            pc?.Close("whip server probe complete");
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
    }

    private static void Respond(HttpListenerContext context, HttpStatusCode statusCode)
    {
        try
        {
            context.Response.StatusCode = (int)statusCode;
            context.Response.Close();
        }
        catch (Exception)
        {
            // The connection may already be gone; nothing to do.
        }
    }

    private static int WriteResult(bool asJson, bool stdoutClaimed, WhipServerResult result, int exitCode)
    {
        // The stdout payload rule: when the video bitstream has claimed stdout (--video -), the
        // result is commentary and moves to stderr.
        var output = stdoutClaimed ? Console.Error : Console.Out;

        if (asJson)
        {
            output.WriteLine(SerializeResult(result));
        }
        else if (result.Success)
        {
            string dropped = result.VideoFramesDropped > 0 ? $", {result.VideoFramesDropped} dropped" : string.Empty;
            string videoSink = result.VideoFrames != null ? $", {result.VideoFrames} video frames ({result.VideoBytesWritten} bytes) written{dropped}" : string.Empty;
            string videoFps = result.VideoFps != null ? $" at {result.VideoFps} fps" : string.Empty;
            output.WriteLine($"Publisher connected in {result.ConnectTimeMs}ms. " +
                $"Received {result.AudioPackets} audio ({FormatAnomalies(result.AudioLost, result.AudioOutOfOrder, result.AudioDuplicates)}) and " +
                $"{result.VideoPackets} video ({FormatAnomalies(result.VideoLost, result.VideoOutOfOrder, result.VideoDuplicates)}) packets{videoFps} in {result.MediaDurationMs}ms{videoSink}.");
        }
        else
        {
            Console.Error.WriteLine($"WHIP server on {result.ListenUrl} failed (state {result.ConnectionState}): {result.Error}");
        }

        return exitCode;
    }

    private static string FormatAnomalies(long lost, int outOfOrder, int duplicates)
    {
        if (lost == 0 && outOfOrder == 0 && duplicates == 0)
        {
            return "clean";
        }

        return $"{lost} lost, {outOfOrder} reordered, {duplicates} duplicate";
    }
}
