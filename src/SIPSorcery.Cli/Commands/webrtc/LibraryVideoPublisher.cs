//-----------------------------------------------------------------------------
// Filename: LibraryVideoPublisher.cs
//
// Description: Publishes a generated video test pattern to a WHIP endpoint using
// the SIPSorcery stack itself (the full send pipeline: generate -> encode ->
// RTP packetise -> SRTP -> ICE/DTLS socket). Shared by the "webrtc whip" verb
// (publish to any endpoint) and "webrtc whip-server --publish" (an in-process
// self publish to the server's own listener). The encoder is selectable:
// vp8.net (managed Vpx.Net VP8, no native deps) or ffmpeg (SIPSorceryMedia.FFmpeg,
// H264 or VP8). Frames are sent paced to a target rate, or flat out (MaxRate) to
// find the send-pipeline ceiling.
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

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using Vpx.Net;

namespace SIPSorcery.Cli.Commands;

public static class LibraryVideoPublisher
{
    private const int VP8_PAYLOAD_ID = 96;
    private const int H264_PAYLOAD_ID = 100;
    private const uint VIDEO_CLOCK_RATE = 90000;
    private const int RING_SIZE = 16;
    // The publisher reaching "connected" can slightly precede the receiver finishing its SRTP context
    // setup. Settle briefly before the first frame so the opening keyframe is not dropped by the
    // receiver ("packet received before secure context ready"). A live encoder's first-frame latency
    // hides this; pre-encode replay, which sends instantly on connect, would otherwise expose it.
    private const int CONNECT_SETTLE_MS = 200;
    private const double BITS_PER_PIXEL_PER_FRAME = 0.1;

    /// <summary>
    /// The publish controls. DurationSeconds 0 runs until the cancellation token fires.
    /// PreEncodeFrames > 0 encodes that many frames once before connecting and replays the encoded
    /// bitstream in a loop (no encoder in the send loop), to take the encode stage out of a
    /// downstream decode measurement.
    /// </summary>
    public sealed record Settings(
        string Preset,
        string? Size,
        int Fps,
        string Encoder,
        string? Codec,
        int Bitrate,
        bool MaxRate,
        string? FfmpegPath,
        int DurationSeconds,
        int PreEncodeFrames = 0);

    /// <summary>Outcome and send-side statistics. ExitCode is an <see cref="ExitCodes"/> value.</summary>
    public sealed record Result(
        bool Success,
        int ExitCode,
        string ConnectionState,
        long? ConnectTimeMs,
        int? MediaDurationMs,
        int Width,
        int Height,
        string Codec,
        int FramesSent,
        long BytesSent,
        double AchievedFps,
        double EncodeMsAvg,
        string? Error);

    private sealed record Config(int Width, int Height, VideoCodecsEnum Codec, VideoFormat VideoFormat, bool UseFfmpeg, string CodecName);

    /// <summary>Validates the encoder/codec/frame rate/resolution settings without starting anything.</summary>
    public static bool TryValidate(Settings settings, out string? error) => TryResolveConfig(settings, out _, out error);

    public static async Task<Result> RunAsync(string url, Settings settings, string? token, int timeoutSeconds, ILogger logger, CancellationToken ct)
    {
        if (!TryResolveConfig(settings, out Config cfg, out string? cfgError))
        {
            return Fail(ExitCodes.InvalidArgument, "new", cfgError, null);
        }

        IVideoEncoder videoEncoder;
        if (!cfg.UseFfmpeg)
        {
            videoEncoder = new VP8Codec();
        }
        else
        {
            try
            {
                FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL, settings.FfmpegPath, logger);
            }
            catch (Exception excp)
            {
                return Fail(ExitCodes.TransportError, "new",
                    $"Could not initialise FFmpeg for --encoder ffmpeg: {excp.Message}. Install the FFmpeg shared libraries (e.g. winget install ffmpeg) or pass --ffmpeg-path.", cfg);
            }

            var ffmpegEncoder = new FFmpegVideoEncoder();
            int effectiveBitrate = settings.Bitrate > 0 ? settings.Bitrate : (int)(cfg.Width * (long)cfg.Height * settings.Fps * BITS_PER_PIXEL_PER_FRAME);
            ffmpegEncoder.SetBitrate(effectiveBitrate, null, null, null);
            videoEncoder = ffmpegEncoder;
        }

        var endpointUri = new Uri(url);
        var pc = new RTCPeerConnection();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        Uri? resourceUri = null;

        try
        {
            pc.addTrack(new MediaStreamTrack(new List<VideoFormat> { cfg.VideoFormat }, MediaStreamStatusEnum.SendOnly));

            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug("Publisher peer connection state changed to {State}.", state);
                if (state == RTCPeerConnectionState.connected) { connected.TrySetResult(true); }
                else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed) { connected.TrySetResult(false); }
            };

            // Optionally pre-encode a ring of frames now, before connecting, so the encode work does
            // not run during the send window (and does not eat the receiver's media window, which only
            // starts once connected). The send loop then replays this encoded bitstream. Frame 0 is a
            // keyframe so the ring loops cleanly. Encoded buffers are cloned in case the encoder reuses
            // an internal buffer between calls.
            byte[][]? encodedRing = null;
            double preEncodeMsAvg = 0;
            if (settings.PreEncodeFrames > 0)
            {
                logger.LogDebug("Pre-encoding {Count} {Codec} frames before connecting ...", settings.PreEncodeFrames, cfg.CodecName);
                var captured = new List<byte[]>(settings.PreEncodeFrames);
                var rawBuf = new byte[I420Size(cfg.Width, cfg.Height)];
                double preEncodeMsSum = 0;
                for (int i = 0; i < settings.PreEncodeFrames; i++)
                {
                    FillI420Frame(rawBuf, cfg.Width, cfg.Height, i);
                    long s = Stopwatch.GetTimestamp();
                    byte[]? enc = videoEncoder.EncodeVideo(cfg.Width, cfg.Height, rawBuf, VideoPixelFormatsEnum.I420, cfg.Codec);
                    preEncodeMsSum += (Stopwatch.GetTimestamp() - s) * 1000.0 / Stopwatch.Frequency;
                    if (enc != null && enc.Length > 0)
                    {
                        captured.Add((byte[])enc.Clone());
                    }
                }

                if (captured.Count == 0)
                {
                    return Fail(ExitCodes.Failed, pc.connectionState.ToString(), "Pre-encode produced no frames (check the encoder).", cfg);
                }

                encodedRing = captured.ToArray();
                preEncodeMsAvg = Math.Round(preEncodeMsSum / settings.PreEncodeFrames, 3);
                logger.LogDebug("Pre-encoded {Count} frames ({AvgMs}ms/frame avg); replaying.", encodedRing.Length, preEncodeMsAvg);
            }

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
                return Fail(ExitCodes.Failed, pc.connectionState.ToString(), $"The WHIP endpoint returned HTTP {(int)response.StatusCode}. {detail}".TrimEnd(), cfg);
            }

            if (response.Headers.Location != null)
            {
                resourceUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(endpointUri, response.Headers.Location);
            }

            var setAnswerResult = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = responseBody });
            if (setAnswerResult != SetDescriptionResultEnum.OK)
            {
                return Fail(ExitCodes.Failed, pc.connectionState.ToString(), $"The SDP answer could not be applied: {setAnswerResult}.", cfg);
            }

            var connectCompleted = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);
            if (connectCompleted != connected.Task || !await connected.Task.ConfigureAwait(false))
            {
                return Fail(ExitCodes.Timeout, pc.connectionState.ToString(),
                    connectCompleted == connected.Task
                        ? $"The peer connection failed (state {pc.connectionState})."
                        : $"The peer connection did not reach connected within {timeoutSeconds}s.",
                    cfg, stopwatch.ElapsedMilliseconds);
            }

            long connectTimeMs = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Connected in {ConnectTimeMs}ms, publishing {Width}x{Height} {Codec}.", connectTimeMs, cfg.Width, cfg.Height, cfg.CodecName);

            // Let the receiver finish installing its SRTP context before the first frame (see CONNECT_SETTLE_MS).
            try { await Task.Delay(CONNECT_SETTLE_MS, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return Fail(ExitCodes.Timeout, pc.connectionState.ToString(), "Cancelled.", cfg, connectTimeMs); }

            // ---- Send loop: send paced to the frame rate or flat out. In live mode each frame is
            // generated and encoded in the loop; in pre-encode mode the encoded ring is replayed. ----
            byte[][] ring = encodedRing ?? GenerateI420Ring(cfg.Width, cfg.Height, RING_SIZE);
            uint rtpDuration = VIDEO_CLOCK_RATE / (uint)settings.Fps;
            int framesSent = 0;
            int framesAttempted = 0;
            long bytesSent = 0;
            double encodeMsSum = 0;

            var sendStopwatch = Stopwatch.StartNew();
            long durationMs = settings.DurationSeconds > 0 ? settings.DurationSeconds * 1000L : long.MaxValue;

            while (sendStopwatch.ElapsedMilliseconds < durationMs && !ct.IsCancellationRequested && pc.connectionState == RTCPeerConnectionState.connected)
            {
                byte[]? encoded;
                if (encodedRing != null)
                {
                    encoded = encodedRing[framesAttempted % encodedRing.Length];
                    framesAttempted++;
                }
                else
                {
                    byte[] raw = ring[framesAttempted % ring.Length];
                    long start = Stopwatch.GetTimestamp();
                    encoded = videoEncoder.EncodeVideo(cfg.Width, cfg.Height, raw, VideoPixelFormatsEnum.I420, cfg.Codec);
                    encodeMsSum += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                    framesAttempted++;
                }

                if (encoded != null && encoded.Length > 0)
                {
                    pc.SendVideo(rtpDuration, encoded);
                    framesSent++;
                    bytesSent += encoded.Length;
                }

                if (!settings.MaxRate)
                {
                    long targetMs = (long)framesAttempted * 1000 / settings.Fps;
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
            // In pre-encode mode the loop does no encoding; report the pre-encode average instead.
            double encodeMsAvg = encodedRing != null ? preEncodeMsAvg
                : framesAttempted > 0 ? Math.Round(encodeMsSum / framesAttempted, 3) : 0;
            bool sentMedia = framesSent > 0;

            return new Result(sentMedia, sentMedia ? ExitCodes.Ok : ExitCodes.Failed, pc.connectionState.ToString(), connectTimeMs,
                (int)sendStopwatch.ElapsedMilliseconds, cfg.Width, cfg.Height, cfg.CodecName, framesSent, bytesSent, achievedFps, encodeMsAvg,
                sentMedia ? null : "Connected but no frames were encoded/sent (check the encoder).");
        }
        catch (OperationCanceledException)
        {
            return Fail(ExitCodes.Timeout, pc.connectionState.ToString(), "Cancelled or a request timed out.", cfg);
        }
        catch (Exception excp)
        {
            return Fail(ExitCodes.TransportError, pc.connectionState.ToString(), excp.Message, cfg);
        }
        finally
        {
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

    private static Result Fail(int exitCode, string state, string? error, Config? cfg, long? connectMs = null) =>
        new(false, exitCode, state, connectMs, null, cfg?.Width ?? 0, cfg?.Height ?? 0, cfg?.CodecName ?? string.Empty, 0, 0, 0, 0, error);

    private static bool TryResolveConfig(Settings settings, out Config cfg, out string? error)
    {
        cfg = default!;
        error = null;

        string encoder = settings.Encoder.ToLowerInvariant();
        string? codecArg = settings.Codec?.ToLowerInvariant();

        if (settings.Fps < 1)
        {
            error = "--fps must be at least 1.";
            return false;
        }

        int width, height;
        if (!string.IsNullOrWhiteSpace(settings.Size))
        {
            string[] parts = settings.Size.ToLowerInvariant().Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height) || width < 2 || height < 2)
            {
                error = $"Could not parse --size \"{settings.Size}\". Expected WxH, e.g. 1280x720.";
                return false;
            }
        }
        else if (!VideoPresets.TryResolve(settings.Preset, out width, out height, out error))
        {
            return false;
        }

        VideoCodecsEnum codec;
        VideoFormat videoFormat;
        bool useFfmpeg;

        if (encoder == "vp8.net")
        {
            if (codecArg != null && codecArg != "vp8")
            {
                error = "The vp8.net encoder only supports --codec vp8.";
                return false;
            }

            // The managed VP8 encoder requires positive multiples of 16.
            (width, height) = (RoundUpTo16(width), RoundUpTo16(height));
            codec = VideoCodecsEnum.VP8;
            videoFormat = new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID);
            useFfmpeg = false;
        }
        else if (encoder == "ffmpeg")
        {
            string wantCodec = codecArg ?? "h264";
            if (wantCodec == "h264")
            {
                codec = VideoCodecsEnum.H264;
                videoFormat = new VideoFormat(VideoCodecsEnum.H264, H264_PAYLOAD_ID, parameters: "packetization-mode=1");
            }
            else if (wantCodec == "vp8")
            {
                codec = VideoCodecsEnum.VP8;
                videoFormat = new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID);
            }
            else
            {
                error = $"Unknown --codec \"{wantCodec}\". Expected h264 or vp8.";
                return false;
            }

            // libx264/libvpx need even dimensions.
            (width, height) = (RoundUpToEven(width), RoundUpToEven(height));
            useFfmpeg = true;
        }
        else
        {
            error = $"Unknown --encoder \"{settings.Encoder}\". Expected vp8.net or ffmpeg.";
            return false;
        }

        cfg = new Config(width, height, codec, videoFormat, useFfmpeg, codec.ToString().ToLowerInvariant());
        return true;
    }

    private static int RoundUpTo16(int value) => (value + 15) & ~15;

    private static int RoundUpToEven(int value) => (value + 1) & ~1;

    private static int I420Size(int width, int height) => width * height + 2 * ((width / 2) * (height / 2));

    /// <summary>
    /// Fills a buffer with one I420 frame of a shifting textured pattern (selected by
    /// <paramref name="frameIndex"/>) so the encoder sees inter-frame motion and moderate detail.
    /// The buffer must be <see cref="I420Size"/> bytes.
    /// </summary>
    private static void FillI420Frame(byte[] buf, int width, int height, int frameIndex)
    {
        int ySize = width * height;
        int cSize = (width / 2) * (height / 2);
        int shift = frameIndex * 8;

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
    }

    /// <summary>
    /// Generates a ring of <paramref name="count"/> distinct I420 frames. Used for the live encode
    /// path; the pre-encode path generates frames one at a time into a single reused buffer.
    /// </summary>
    private static byte[][] GenerateI420Ring(int width, int height, int count)
    {
        var ring = new byte[count][];
        for (int f = 0; f < count; f++)
        {
            var buf = new byte[I420Size(width, height)];
            FillI420Frame(buf, width, height, f);
            ring[f] = buf;
        }

        return ring;
    }
}
