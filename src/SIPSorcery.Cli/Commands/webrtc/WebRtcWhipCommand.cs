//-----------------------------------------------------------------------------
// Filename: WebRtcWhipCommand.cs
//
// Description: The "sipsorcery webrtc whip" verb. Publishes a generated video
// test pattern to a WHIP (WebRTC-HTTP Ingestion Protocol) endpoint using the
// SIPSorcery stack as the sender, exercising the full send pipeline: generate ->
// encode -> RTP packetise -> SRTP -> ICE/DTLS socket. It is the publishing
// counterpart to "webrtc whip-server" (a library->library loopback when pointed
// at it) and also publishes to any WHIP ingest (Broadcast Box, MediaRTC, ...).
//
// Where "video-bench" measures the encoder/packetiser in isolation (no network),
// this adds the real WebRTC transport. The encoder is selectable: vp8.net (the
// managed Vpx.Net VP8 codec, no native deps but limited throughput) or ffmpeg
// (the SIPSorceryMedia.FFmpeg H264 encoder). Frames are generated at the chosen
// resolution/preset and sent at --fps, or flat out with --max-rate to find the
// send-pipeline ceiling (use --max-rate only against a local receiver).
//
// Signalling is the WHIP HTTP exchange: POST the SDP offer as application/sdp,
// apply the returned answer, and DELETE the resource on teardown.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 15 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using Vpx.Net;

namespace SIPSorcery.Cli.Commands;

public sealed class WebRtcWhipCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 15;
    private const int DEFAULT_DURATION_SECONDS = 10;
    private const int DEFAULT_FPS = 30;
    private const string DEFAULT_PRESET = "720p";
    private const string DEFAULT_ENCODER = "vp8.net";
    private const int VP8_PAYLOAD_ID = 96;
    private const int H264_PAYLOAD_ID = 100;
    private const uint VIDEO_CLOCK_RATE = 90000;
    private const int RING_SIZE = 16;
    private const double BITS_PER_PIXEL_PER_FRAME = 0.1;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record WhipResult(
        bool Success,
        string Url,
        string Encoder,
        string Codec,
        int Width,
        int Height,
        int TargetFps,
        string ConnectionState,
        long? ConnectTimeMs,
        int? MediaDurationMs,
        int FramesSent,
        long BytesSent,
        double AchievedFps,
        double EncodeMsAvg,
        string? Error);

    public WebRtcWhipCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var urlArg = new Argument<string>("url")
        {
            Description = "The WHIP endpoint URL to publish to, e.g. http://localhost:8080/whip."
        };

        var presetOption = new Option<string>("--preset")
        {
            Description = $"Resolution preset ({VideoPresets.Names}). Ignored if --size is given.",
            DefaultValueFactory = _ => DEFAULT_PRESET
        };

        var sizeOption = new Option<string?>("--size", "-s")
        {
            Description = "Explicit frame size WxH (e.g. 1280x720), overriding --preset."
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = "Target frame rate to publish at (ignored with --max-rate).",
            DefaultValueFactory = _ => DEFAULT_FPS
        };

        var encoderOption = new Option<string>("--encoder")
        {
            Description = "Video encoder: vp8.net (managed Vpx.Net VP8, no native deps) or ffmpeg (SIPSorceryMedia.FFmpeg H264).",
            DefaultValueFactory = _ => DEFAULT_ENCODER
        };

        var bitrateOption = new Option<int>("--bitrate")
        {
            Description = "Target encoder bitrate in bits per second (ffmpeg encoder only). 0 (default) derives it from the resolution and frame rate."
        };

        var maxRateOption = new Option<bool>("--max-rate")
        {
            Description = "Send as fast as the encoder and transport allow (ignores --fps) to measure the send pipeline ceiling. Use only against a local receiver, never a remote ingest."
        };

        var ffmpegPathOption = new Option<string?>("--ffmpeg-path")
        {
            Description = "Directory containing the FFmpeg shared libraries for --encoder ffmpeg. Defaults to the system path."
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "Optional bearer token for the WHIP endpoint Authorization header."
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to publish for after the connection is established.",
            DefaultValueFactory = _ => DEFAULT_DURATION_SECONDS
        };

        var command = new Command("whip", "Publish a test pattern to a WHIP endpoint using the SIPSorcery stack (full send pipeline: encode, RTP/SRTP, ICE/DTLS).");
        command.Arguments.Add(urlArg);
        command.Options.Add(presetOption);
        command.Options.Add(sizeOption);
        command.Options.Add(fpsOption);
        command.Options.Add(encoderOption);
        command.Options.Add(bitrateOption);
        command.Options.Add(maxRateOption);
        command.Options.Add(ffmpegPathOption);
        command.Options.Add(tokenOption);
        command.Options.Add(durationOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(urlArg)!,
            parseResult.GetValue(presetOption)!,
            parseResult.GetValue(sizeOption),
            parseResult.GetValue(fpsOption),
            parseResult.GetValue(encoderOption)!,
            parseResult.GetValue(bitrateOption),
            parseResult.GetValue(maxRateOption),
            parseResult.GetValue(ffmpegPathOption),
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string url, string preset, string? size, int fps, string encoder, int bitrate,
        bool maxRate, string? ffmpegPath, string? token, int durationSeconds, int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(WebRtcWhipCommand));

        encoder = encoder.ToLowerInvariant();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return WriteResult(asJson,
                new WhipResult(false, url, encoder, "", 0, 0, fps, "new", null, null, 0, 0, 0, 0,
                    $"Could not parse \"{url}\" as an HTTP or HTTPS URL."),
                ExitCodes.InvalidArgument);
        }

        if (fps < 1)
        {
            return WriteResult(asJson,
                new WhipResult(false, url, encoder, "", 0, 0, fps, "new", null, null, 0, 0, 0, 0, "--fps must be at least 1."),
                ExitCodes.InvalidArgument);
        }

        // Resolve the requested resolution (an explicit --size overrides the preset).
        int width, height;
        if (!string.IsNullOrWhiteSpace(size))
        {
            string[] parts = size.ToLowerInvariant().Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height) || width < 2 || height < 2)
            {
                return WriteResult(asJson,
                    new WhipResult(false, url, encoder, "", 0, 0, fps, "new", null, null, 0, 0, 0, 0,
                        $"Could not parse --size \"{size}\". Expected WxH, e.g. 1280x720."),
                    ExitCodes.InvalidArgument);
            }
        }
        else if (!VideoPresets.TryResolve(preset, out width, out height, out string? presetError))
        {
            return WriteResult(asJson,
                new WhipResult(false, url, encoder, "", 0, 0, fps, "new", null, null, 0, 0, 0, 0, presetError),
                ExitCodes.InvalidArgument);
        }

        // Build the encoder and the matching track codec.
        IVideoEncoder videoEncoder;
        VideoCodecsEnum codec;
        VideoFormat videoFormat;

        if (encoder == "vp8.net")
        {
            // The managed VP8 encoder requires the coded dimensions to be positive multiples of 16.
            (width, height) = (RoundUpTo16(width), RoundUpTo16(height));
            videoEncoder = new VP8Codec();
            codec = VideoCodecsEnum.VP8;
            videoFormat = new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID);
        }
        else if (encoder == "ffmpeg")
        {
            try
            {
                FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL, ffmpegPath, logger);
            }
            catch (Exception excp)
            {
                return WriteResult(asJson,
                    new WhipResult(false, url, encoder, "h264", width, height, fps, "new", null, null, 0, 0, 0, 0,
                        $"Could not initialise FFmpeg for --encoder ffmpeg: {excp.Message}. Install the FFmpeg shared libraries (e.g. winget install ffmpeg) or pass --ffmpeg-path."),
                    ExitCodes.TransportError);
            }

            // libvpx/libx264 need even dimensions.
            (width, height) = (RoundUpToEven(width), RoundUpToEven(height));
            var ffmpegEncoder = new FFmpegVideoEncoder();
            int effectiveBitrate = bitrate > 0 ? bitrate : (int)(width * (long)height * fps * BITS_PER_PIXEL_PER_FRAME);
            ffmpegEncoder.SetBitrate(effectiveBitrate, null, null, null);
            videoEncoder = ffmpegEncoder;
            codec = VideoCodecsEnum.H264;
            videoFormat = new VideoFormat(VideoCodecsEnum.H264, H264_PAYLOAD_ID, parameters: "packetization-mode=1");
        }
        else
        {
            return WriteResult(asJson,
                new WhipResult(false, url, encoder, "", width, height, fps, "new", null, null, 0, 0, 0, 0,
                    $"Unknown --encoder \"{encoder}\". Expected vp8.net or ffmpeg."),
                ExitCodes.InvalidArgument);
        }

        string codecName = codec.ToString().ToLowerInvariant();

        var pc = new RTCPeerConnection();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        Uri? resourceUri = null;

        try
        {
            pc.addTrack(new MediaStreamTrack(new List<VideoFormat> { videoFormat }, MediaStreamStatusEnum.SendOnly));

            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug("Publisher peer connection state changed to {State}.", state);
                if (state == RTCPeerConnectionState.connected) { connected.TrySetResult(true); }
                else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed) { connected.TrySetResult(false); }
            };

            var offer = pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
            await pc.setLocalDescription(offer).ConfigureAwait(false);

            var stopwatch = Stopwatch.StartNew();

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = new StringContent(offer.sdp, Encoding.UTF8, "application/sdp")
            };
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string detail = responseBody.Length > 200 ? responseBody[..200] : responseBody;
                return WriteResult(asJson,
                    new WhipResult(false, url, encoder, codecName, width, height, fps, pc.connectionState.ToString(), null, null, 0, 0, 0, 0,
                        $"The WHIP endpoint returned HTTP {(int)response.StatusCode}. {detail}".TrimEnd()),
                    ExitCodes.Failed);
            }

            // The Location header identifies the session resource for the DELETE teardown (RFC 9725).
            if (response.Headers.Location != null)
            {
                resourceUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(endpointUri, response.Headers.Location);
            }

            var setAnswerResult = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = responseBody });
            if (setAnswerResult != SetDescriptionResultEnum.OK)
            {
                return WriteResult(asJson,
                    new WhipResult(false, url, encoder, codecName, width, height, fps, pc.connectionState.ToString(), null, null, 0, 0, 0, 0,
                        $"The SDP answer could not be applied: {setAnswerResult}."),
                    ExitCodes.Failed);
            }

            var connectCompleted = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);
            if (connectCompleted != connected.Task || !await connected.Task.ConfigureAwait(false))
            {
                return WriteResult(asJson,
                    new WhipResult(false, url, encoder, codecName, width, height, fps, pc.connectionState.ToString(), stopwatch.ElapsedMilliseconds, null, 0, 0, 0, 0,
                        connectCompleted == connected.Task
                            ? $"The peer connection failed (state {pc.connectionState})."
                            : $"The peer connection did not reach connected within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            long connectTimeMs = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Connected in {ConnectTimeMs}ms, publishing {Width}x{Height} {Codec} for {Duration}s.", connectTimeMs, width, height, codecName, durationSeconds);

            // ---- Send loop. Generate, encode and send frames either paced to --fps or flat out. ----
            byte[][] ring = GenerateI420Ring(width, height, RING_SIZE);
            Console.Error.WriteLine($"Publishing {width}x{height} {codecName} ({encoder}) {(maxRate ? "flat out" : $"at {fps} fps")} for {durationSeconds}s.");

            uint rtpDuration = VIDEO_CLOCK_RATE / (uint)fps;
            int framesSent = 0;
            int framesAttempted = 0;
            long bytesSent = 0;
            double encodeMsSum = 0;

            var sendStopwatch = Stopwatch.StartNew();
            long durationMs = durationSeconds * 1000L;

            while (sendStopwatch.ElapsedMilliseconds < durationMs && !ct.IsCancellationRequested && pc.connectionState == RTCPeerConnectionState.connected)
            {
                byte[] raw = ring[framesAttempted % ring.Length];

                long start = Stopwatch.GetTimestamp();
                byte[]? encoded = videoEncoder.EncodeVideo(width, height, raw, VideoPixelFormatsEnum.I420, codec);
                encodeMsSum += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                framesAttempted++;

                if (encoded != null && encoded.Length > 0)
                {
                    pc.SendVideo(rtpDuration, encoded);
                    framesSent++;
                    bytesSent += encoded.Length;
                }

                if (!maxRate)
                {
                    long targetMs = (long)framesAttempted * 1000 / fps;
                    long sleepMs = targetMs - sendStopwatch.ElapsedMilliseconds;
                    if (sleepMs > 1)
                    {
                        try { await Task.Delay((int)sleepMs, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { break; }
                    }
                }
            }

            sendStopwatch.Stop();

            double elapsedSec = sendStopwatch.Elapsed.TotalSeconds;
            double achievedFps = elapsedSec > 0 ? Math.Round(framesSent / elapsedSec, 1) : 0;
            double encodeMsAvg = framesAttempted > 0 ? Math.Round(encodeMsSum / framesAttempted, 3) : 0;

            bool sentMedia = framesSent > 0;

            return WriteResult(asJson,
                new WhipResult(sentMedia, url, encoder, codecName, width, height, fps, pc.connectionState.ToString(),
                    connectTimeMs, (int)sendStopwatch.ElapsedMilliseconds, framesSent, bytesSent, achievedFps, encodeMsAvg,
                    sentMedia ? null : "Connected but no frames were encoded/sent (check the encoder)."),
                sentMedia ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson,
                new WhipResult(false, url, encoder, codecName, width, height, fps, pc.connectionState.ToString(), null, null, 0, 0, 0, 0, "Cancelled or a request timed out."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new WhipResult(false, url, encoder, codecName, width, height, fps, pc.connectionState.ToString(), null, null, 0, 0, 0, 0, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            // Best effort WHIP session teardown.
            if (resourceUri != null)
            {
                try
                {
                    using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, resourceUri);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                    await httpClient.SendAsync(deleteRequest, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception excp)
                {
                    logger.LogDebug("WHIP session DELETE failed: {Error}", excp.Message);
                }
            }

            pc.Close("whip publish complete");
            videoEncoder.Dispose();
        }
    }

    private static int RoundUpTo16(int value) => (value + 15) & ~15;

    private static int RoundUpToEven(int value) => (value + 1) & ~1;

    /// <summary>
    /// Generates a ring of distinct I420 frames with a shifting textured pattern so the encoder sees
    /// inter-frame motion and moderate detail. Each buffer is width*height*3/2 bytes.
    /// </summary>
    private static byte[][] GenerateI420Ring(int width, int height, int count)
    {
        int ySize = width * height;
        int cSize = (width / 2) * (height / 2);
        var ring = new byte[count][];

        for (int f = 0; f < count; f++)
        {
            var buf = new byte[ySize + 2 * cSize];
            int shift = f * 8;

            for (int y = 0; y < height; y++)
            {
                int rowBase = y * width;
                for (int x = 0; x < width; x++)
                {
                    buf[rowBase + x] = (byte)((x + y + shift) ^ ((x >> 3) + (y >> 3)));
                }
            }

            for (int y = 0; y < height / 2; y++)
            {
                int uRow = ySize + y * (width / 2);
                int vRow = ySize + cSize + y * (width / 2);
                for (int x = 0; x < width / 2; x++)
                {
                    buf[uRow + x] = (byte)(128 + (((x + shift) & 0x3F) - 32));
                    buf[vRow + x] = (byte)(128 + (((y - shift) & 0x3F) - 32));
                }
            }

            ring[f] = buf;
        }

        return ring;
    }

    private static int WriteResult(bool asJson, WhipResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"WHIP publish to {result.Url} OK: connected in {result.ConnectTimeMs}ms, sent {result.FramesSent} {result.Width}x{result.Height} " +
                $"{result.Codec} frames ({result.Encoder}) at {result.AchievedFps} fps over {result.MediaDurationMs}ms (encode {result.EncodeMsAvg}ms/frame avg).");
        }
        else
        {
            Console.Error.WriteLine($"WHIP publish to {result.Url} failed (state {result.ConnectionState}): {result.Error}");
        }

        return exitCode;
    }
}
