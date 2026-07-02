//-----------------------------------------------------------------------------
// Filename: NeuralAvatarRenderer.cs
//
// Description: The model-driven IAvatarRenderer - a photoreal talking head produced by
// Wav2Lip. It is the counterpart to the SkiaSharp MaxHeadroomVideoSource: instead of
// drawing a cartoon and choosing a mouth shape from loudness, it streams the speech PCM
// to a Python "neural sidecar" (neural/neural_sidecar.py) that runs the audio-to-video
// model and streams lip-synced BGR frames back. Those frames are encoded onto the WebRTC
// video track. This is the same decoupling as bitHuman's push_audio()/flush() and
// LiveKit's DataStreamAudioOutput -> avatar worker; the sidecar is our "avatar worker".
//
// Flow:
//   PushAudio(PCM)  --WebSocket-->  sidecar (Wav2Lip)  --BGR frames-->  frame queue
//                                                                          |
//   render timer @25fps  --> encode (last/queued frame) --> OnVideoSourceEncodedSample
//
// The render timer runs continuously so the RTP video clock keeps ticking even during
// silence (it re-emits the last frame), which keeps the browser's RTCP A/V sync stable -
// the same reason MaxHeadroom pins a continuous audio clock. Note: the model needs a
// short audio look-ahead per frame, so the mouth currently lags the audio by ~200 ms;
// tightening that (audio delay / timestamped frames) is the follow-up.
//
// Select this renderer with AVATAR_RENDERER=neural and start the sidecar first
// (see neural/README.md). Frames are fixed at 640x480 BGR to match the sidecar.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace demo;

public sealed class NeuralAvatarRenderer : IAvatarRenderer
{
    public const int WIDTH = 640;
    public const int HEIGHT = 480;

    private const int VIDEO_SAMPLING_RATE = 90000;
    private const int FRAMES_PER_SECOND = 25;
    private const int FRAME_BYTES = WIDTH * HEIGHT * 3; // BGR888.
    private const int MAX_QUEUE = 3;                    // keep latency low: drop stale frames.

    public static readonly List<VideoFormat> SupportedFormats = new()
    {
        new VideoFormat(VideoCodecsEnum.H264, 100, VIDEO_SAMPLING_RATE, "packetization-mode=1")
    };

    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<NeuralAvatarRenderer>();

    private readonly MediaFormatManager<VideoFormat> _formatManager = new(SupportedFormats);
    private readonly IVideoEncoder _videoEncoder;
    private readonly string _sidecarUrl;
    private readonly int _frameSpacingMs = 1000 / FRAMES_PER_SECOND;

    // Outgoing messages to the sidecar (audio + begin/end control), drained by one sender
    // task so ClientWebSocket only ever has a single concurrent send.
    private readonly Channel<(bool isText, byte[] payload)> _out =
        Channel.CreateUnbounded<(bool, byte[])>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentQueue<byte[]> _frames = new();
    private byte[] _lastFrame;

    private ClientWebSocket _ws;
    private CancellationTokenSource _cts;
    private Timer _renderTimer;
    private volatile bool _paused;
    private volatile bool _closed;
    private volatile bool _faulted;

    public event EncodedSampleDelegate OnVideoSourceEncodedSample;
    public event SourceErrorDelegate OnVideoSourceError;
    // Fired by a real model renderer; unused here (we publish encoded frames only).
#pragma warning disable CS0067
    public event RawVideoSampleDelegate OnVideoSourceRawSample;
    public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;
#pragma warning restore CS0067

    public NeuralAvatarRenderer(IVideoEncoder encoder, string sidecarUrl = "ws://127.0.0.1:5002")
    {
        _videoEncoder = encoder;
        _sidecarUrl = sidecarUrl;
    }

    // --- IAvatarRenderer: the audio -> face hand-off ------------------------------------

    /// <summary>
    /// The sidecar buffers PCM and paces the mouth on its own 25fps emitter, so the speaker
    /// should push audio as fast as it has it - the model's ~200ms mel look-ahead then never
    /// waits on real-time delivery, which is what kept the mouth behind the voice.
    /// </summary>
    public bool PacesAudioInternally => true;

    public void BeginSpeech() => Send(true, Encoding.UTF8.GetBytes("{\"type\":\"begin\"}"));

    public void PushAudio(ReadOnlySpan<short> pcm16, int sampleRate)
    {
        if (pcm16.Length == 0 || _closed)
        {
            return;
        }
        var bytes = new byte[pcm16.Length * sizeof(short)];
        MemoryMarshal.AsBytes(pcm16).CopyTo(bytes);
        Send(false, bytes);
    }

    public void EndSpeech() => Send(true, Encoding.UTF8.GetBytes("{\"type\":\"end\"}"));

    private void Send(bool isText, byte[] payload)
    {
        if (!_closed && !_faulted)
        {
            _out.Writer.TryWrite((isText, payload));
        }
    }

    // --- IVideoSource plumbing ----------------------------------------------------------

    public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
    public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
    public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
    public void ForceKeyFrame() => _videoEncoder?.ForceKeyFrame();
    public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
    public bool IsVideoSourcePaused() => _paused;

    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
        throw new NotImplementedException("The neural renderer generates its own frames from audio.");
    public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) =>
        throw new NotImplementedException("The neural renderer generates its own frames from audio.");

    public async Task StartVideo()
    {
        if (_ws != null || _closed)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();

        try
        {
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _ws.ConnectAsync(new Uri(_sidecarUrl), connectCts.Token).ConfigureAwait(false);
            await ReadHelloAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (Exception excp)
        {
            _faulted = true;
            logger.LogError("Could not connect to the neural sidecar at {Url} ({Error}). " +
                "Start it first: python neural/neural_sidecar.py --persona <face>.", _sidecarUrl, excp.Message);
            OnVideoSourceError?.Invoke("neural sidecar unavailable");
            return;
        }

        _ = Task.Run(() => SendLoopAsync(_cts.Token));
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        _renderTimer = new Timer(RenderTick, null, 0, _frameSpacingMs);
        logger.LogInformation("Neural avatar renderer connected to sidecar {Url}.", _sidecarUrl);
    }

    /// <summary>Reads the sidecar's opening JSON hello and checks the frame geometry matches.</summary>
    private async Task ReadHelloAsync(CancellationToken ct)
    {
        var buf = new byte[4096];
        var result = await _ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
        if (result.MessageType == WebSocketMessageType.Text)
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buf, 0, result.Count));
            int w = doc.RootElement.GetProperty("w").GetInt32();
            int h = doc.RootElement.GetProperty("h").GetInt32();
            if (w != WIDTH || h != HEIGHT)
            {
                logger.LogWarning("Sidecar frame size {W}x{H} differs from renderer {RW}x{RH}; frames may be dropped.",
                    w, h, WIDTH, HEIGHT);
            }
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var (isText, payload) in _out.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var type = isText ? WebSocketMessageType.Text : WebSocketMessageType.Binary;
                await _ws.SendAsync(payload, type, true, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception excp)
        {
            logger.LogWarning("Neural sidecar send loop ended: {Error}", excp.Message);
        }
    }

    /// <summary>Reassembles WebSocket messages into full BGR frames and queues them.</summary>
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        using var asm = new MemoryStream(FRAME_BYTES);
        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(buf, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    continue; // control/hello; no text frames expected mid-stream.
                }

                asm.Write(buf, 0, result.Count);
                if (result.EndOfMessage)
                {
                    if (asm.Length == FRAME_BYTES)
                    {
                        EnqueueFrame(asm.ToArray());
                    }
                    asm.SetLength(0);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception excp)
        {
            logger.LogWarning("Neural sidecar receive loop ended: {Error}", excp.Message);
        }
    }

    private void EnqueueFrame(byte[] frame)
    {
        _frames.Enqueue(frame);
        while (_frames.Count > MAX_QUEUE && _frames.TryDequeue(out _)) { } // drop stale frames.
    }

    /// <summary>25 fps clock: emit the next queued frame (or re-emit the last), keeping the RTP clock alive.</summary>
    private void RenderTick(object state)
    {
        if (_closed || _paused || _faulted || _videoEncoder == null || _formatManager.SelectedFormat.IsEmpty())
        {
            return;
        }
        if (OnVideoSourceEncodedSample == null)
        {
            return;
        }

        if (_frames.TryDequeue(out var frame))
        {
            _lastFrame = frame;
        }
        if (_lastFrame == null)
        {
            return; // nothing to show yet (sidecar hasn't produced a frame).
        }

        try
        {
            var encoded = _videoEncoder.EncodeVideo(WIDTH, HEIGHT, _lastFrame, VideoPixelFormatsEnum.Bgr, _formatManager.SelectedFormat.Codec);
            if (encoded != null)
            {
                uint durationRtpTS = VIDEO_SAMPLING_RATE / FRAMES_PER_SECOND;
                OnVideoSourceEncodedSample?.Invoke(durationRtpTS, encoded);
            }
        }
        catch (Exception excp)
        {
            _faulted = true;
            _renderTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            logger.LogError(excp, "Fatal error encoding neural frame; stopping video.");
            OnVideoSourceError?.Invoke(excp.Message);
        }
    }

    public Task PauseVideo() { _paused = true; return Task.CompletedTask; }
    public Task ResumeVideo() { _paused = false; return Task.CompletedTask; }

    public Task CloseVideo()
    {
        _closed = true;
        _renderTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cts?.Cancel();
        _out.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _closed = true;
        _renderTimer?.Dispose();
        try { _cts?.Cancel(); } catch { }
        _out.Writer.TryComplete();
        try { _ws?.Dispose(); } catch { }
        _cts?.Dispose();
        _videoEncoder?.Dispose();
    }
}
