//-----------------------------------------------------------------------------
// Filename: WebRtcWhepCommand.cs
//
// Description: The "sipsorcery webrtc whep" verb. Performs a full WebRTC
// connection to a WHEP (WebRTC-HTTP Egress Protocol) endpoint: SDP offer via
// HTTP POST, ICE connectivity checks, DTLS handshake and SRTP media reception.
// Reports whether the connection succeeded and how many media packets arrived,
// distinguishing "could not connect" from the quieter failure mode of
// "connected but no media flowed".
//
// Signalling is a single HTTP POST of application/sdp returning the answer
// (WHIP is RFC 9725; WHEP is the equivalent egress draft). Trickle ICE is
// avoided by gathering all candidates before sending the offer.
//
// Received video can be rendered or captured with --video: "play" spawns an
// ffplay window, a file path captures the bitstream (H264 Annex B, VP8 in
// IVF) and "-" writes it to stdout for piping (the result then moves to
// stderr), e.g. "--video - | mpv --vo=tct -" renders video in the terminal.
// Decode is delegated to the consumer so no video codecs run in-process.
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
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

public sealed class WebRtcWhepCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 10;
    private const int DEFAULT_MEDIA_DURATION_SECONDS = 5;
    private const int VP8_PAYLOAD_ID = 96;
    private const int H264_PAYLOAD_ID = 100;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// Lost counts gaps in the RTP sequence numbers: genuine network loss, but ALSO packets the
    /// library dropped between the wire and the application, e.g. SRTP authentication failures,
    /// which makes a non-zero value a prompt to rerun with --verbose and look closer.
    /// </summary>
    private sealed record WhepResult(
        bool Success,
        string Url,
        int? HttpStatus,
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
        long? VideoBytesWritten = null);

    public WebRtcWhepCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var urlArg = new Argument<string>("url")
        {
            Description = "The WHEP endpoint URL, e.g. https://b.siobud.com/api/whep."
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "Optional bearer token for the Authorization header. For Broadcast Box this is the stream key."
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

        var command = new Command("whep", "Connect to a WHEP endpoint (full ICE/DTLS/SRTP) and verify media is received.");
        command.Arguments.Add(urlArg);
        command.Options.Add(tokenOption);
        command.Options.Add(durationOption);
        command.Options.Add(videoOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(urlArg)!,
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(videoOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string url, string? token, int durationSeconds, string? videoOut,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(WebRtcWhepCommand));

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return WriteResult(asJson, stdoutClaimed: false,
                new WhepResult(false, url, null, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                    $"Could not parse \"{url}\" as an HTTP or HTTPS URL."),
                ExitCodes.InvalidArgument);
        }

        using var videoSink = VideoSink.Create(videoOut, logger, out string? videoSinkError);

        if (videoSinkError != null)
        {
            return WriteResult(asJson, videoSink.IsStdout,
                new WhepResult(false, url, null, "new", null, null, 0, 0, 0, 0, 0, 0, 0, 0, videoSinkError),
                ExitCodes.InvalidArgument);
        }

        var pc = new RTCPeerConnection();
        Uri? resourceUri = null;
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        try
        {
            // Receive only tracks. Offer the codecs the library can negotiate so the server can
            // match whatever the publisher is sending. No decoding is done, packets are counted.
            var audioTrack = new MediaStreamTrack(new List<AudioFormat>
            {
                AudioCommonlyUsedFormats.OpusWebRTC,
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)
            }, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(audioTrack);

            var videoTrack = new MediaStreamTrack(new List<VideoFormat>
            {
                new VideoFormat(VideoCodecsEnum.VP8, VP8_PAYLOAD_ID),
                new VideoFormat(VideoCodecsEnum.H264, H264_PAYLOAD_ID, parameters: "packetization-mode=1")
            }, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(videoTrack);

            var audioStats = new RtpStreamStats();
            var videoStats = new RtpStreamStats();
            pc.OnRtpPacketReceived += RtpStreamStats.CreateRtpHandler(audioStats, videoStats, logger);

            if (videoSink.IsActive)
            {
                // Depacketised (still encoded) frames; decode is delegated to the sink's consumer.
                pc.OnVideoFrameReceived += (remoteEndPoint, timestamp, frame, format) =>
                    videoSink.WriteFrame(frame, timestamp, format);
            }

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

            // Gather all ICE candidates up front so the offer is complete and no trickle (HTTP
            // PATCH) support is needed.
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
                return WriteResult(asJson, videoSink.IsStdout,
                    new WhepResult(false, url, (int)response.StatusCode, pc.connectionState.ToString(), null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                        $"The WHEP endpoint returned HTTP {(int)response.StatusCode}. {detail}".TrimEnd()),
                    ExitCodes.Failed);
            }

            // The Location header identifies the session resource and is used for the DELETE teardown.
            if (response.Headers.Location != null)
            {
                resourceUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(endpointUri, response.Headers.Location);
            }

            var setAnswerResult = pc.setRemoteDescription(new RTCSessionDescriptionInit
            {
                type = RTCSdpType.answer,
                sdp = responseBody
            });

            if (setAnswerResult != SetDescriptionResultEnum.OK)
            {
                return WriteResult(asJson, videoSink.IsStdout,
                    new WhepResult(false, url, (int)response.StatusCode, pc.connectionState.ToString(), null, null, 0, 0, 0, 0, 0, 0, 0, 0,
                        $"The SDP answer could not be applied: {setAnswerResult}."),
                    ExitCodes.Failed);
            }

            var connectCompleted = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            if (connectCompleted != connected.Task || !await connected.Task.ConfigureAwait(false))
            {
                return WriteResult(asJson, videoSink.IsStdout,
                    new WhepResult(false, url, (int)response.StatusCode, pc.connectionState.ToString(),
                        stopwatch.ElapsedMilliseconds, null,
                        audioStats.Packets, audioStats.Lost, audioStats.OutOfOrder, audioStats.Duplicates,
                        videoStats.Packets, videoStats.Lost, videoStats.OutOfOrder, videoStats.Duplicates,
                        ct.IsCancellationRequested ? "Cancelled." :
                        connectCompleted == connected.Task
                            ? $"The peer connection failed (state {pc.connectionState})."
                            : $"The peer connection did not reach connected within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            long connectTimeMs = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Connected in {ConnectTimeMs}ms, receiving media for {Duration}s.", connectTimeMs, durationSeconds);

            await Task.Delay(TimeSpan.FromSeconds(durationSeconds), ct).ConfigureAwait(false);

            bool gotMedia = audioStats.Packets + videoStats.Packets > 0;

            // Dispose the sink before writing the result so files are finalised, ffplay drains
            // and, in stdout mode, the bitstream completes before the result lands on stderr.
            videoSink.Dispose();

            return WriteResult(asJson, videoSink.IsStdout,
                new WhepResult(gotMedia, url, (int)response.StatusCode, pc.connectionState.ToString(),
                    connectTimeMs, durationSeconds * 1000,
                    audioStats.Packets, audioStats.Lost, audioStats.OutOfOrder, audioStats.Duplicates,
                    videoStats.Packets, videoStats.Lost, videoStats.OutOfOrder, videoStats.Duplicates,
                    gotMedia ? null : "The connection succeeded but no media packets were received. Is anything publishing to the stream?",
                    videoSink.IsActive ? videoSink.FramesWritten : null,
                    videoSink.IsActive ? videoSink.BytesWritten : null),
                gotMedia ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson, videoSink.IsStdout,
                new WhepResult(false, url, null, pc.connectionState.ToString(), null, null, 0, 0, 0, 0, 0, 0, 0, 0, "Cancelled or HTTP request timed out."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson, videoSink.IsStdout,
                new WhepResult(false, url, null, pc.connectionState.ToString(), null, null, 0, 0, 0, 0, 0, 0, 0, 0, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            // Best effort WHEP session teardown.
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
                    logger.LogDebug("WHEP session DELETE failed: {Error}", excp.Message);
                }
            }

            pc.Close("whep probe complete");
        }
    }

    private static int WriteResult(bool asJson, bool stdoutClaimed, WhepResult result, int exitCode)
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
            string videoSink = result.VideoFrames != null ? $", {result.VideoFrames} video frames ({result.VideoBytesWritten} bytes) written" : string.Empty;
            output.WriteLine($"Connected to {result.Url} in {result.ConnectTimeMs}ms. " +
                $"Received {result.AudioPackets} audio ({FormatAnomalies(result.AudioLost, result.AudioOutOfOrder, result.AudioDuplicates)}) and " +
                $"{result.VideoPackets} video ({FormatAnomalies(result.VideoLost, result.VideoOutOfOrder, result.VideoDuplicates)}) packets in {result.MediaDurationMs}ms{videoSink}.");
        }
        else
        {
            Console.Error.WriteLine($"WHEP connection to {result.Url} failed (state {result.ConnectionState}): {result.Error}");
        }

        if (result.AudioLost + result.VideoLost > 0)
        {
            // A sequence gap means the packet never reached the application. That is genuine
            // network loss OR packets the library dropped on the way up, e.g. SRTP
            // authentication failures, which only show up in the verbose logs.
            Console.Error.WriteLine($"Warning: {result.AudioLost + result.VideoLost} packet(s) missing from the RTP sequence. " +
                "This can be network loss or packets dropped internally (e.g. SRTP authentication failures). Rerun with --verbose to investigate.");
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

