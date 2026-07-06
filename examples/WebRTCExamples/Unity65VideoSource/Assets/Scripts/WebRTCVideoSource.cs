//-----------------------------------------------------------------------------
// Filename: WebRTCVideoSource.cs
//
// Description: Streams the game's main camera view to a browser over WebRTC
// using SIPSorcery - the modern (Unity 6 / URP / Cinemachine) successor to the
// original 2019 UnityVideoSource example. The pipeline mirrors the working
// UnityAvatarSource example:
//
//   mirror camera tracks Camera.main (so the Game view is untouched)
//        v  renders to a fixed-size RenderTexture
//        v  ReadPixels each frame period -> RGBA32 -> flipped BGR24
//        v  background encode thread -> FFmpegVideoEncoder (VP8)
//        v  RTCPeerConnection.SendVideo (+ a silence audio track for A/V SDP)
//
// Setup is zero-touch: a RuntimeInitializeOnLoadMethod bootstraps the component
// when the scene starts, so just press Play, browse to http://localhost:8081
// and click Connect.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using UnityEngine;
using UnityEngine.Rendering;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Camera = UnityEngine.Camera;   // disambiguate from SIPSorceryMedia.FFmpeg.Camera.

public class WebRTCVideoSource : MonoBehaviour
{
    public const int WIDTH = 960;
    public const int HEIGHT = 540;
    public const int FPS = 25;
    public const int HTTP_PORT = 8081;

    private const int VIDEO_CLOCK = 90000;                       // RTP video sampling rate.
    private const uint FRAME_RTP_DURATION = VIDEO_CLOCK / FPS;   // 3600 - constant, clean.
    private static readonly List<VideoFormat> VideoFormats = new()
    {
        new VideoFormat(VideoCodecsEnum.VP8, 96, VIDEO_CLOCK)
    };

    private static ILogger logger;

    // Rendering / capture.
    private Camera _mirrorCamera;
    private RenderTexture _renderTexture;
    private Texture2D _readbackTexture;
    private float _nextCaptureTime;

    // Encode pipeline (background thread - encoding must not stall the Unity main thread).
    private FFmpegVideoEncoder _encoder;
    private VideoCodecsEnum _negotiatedCodec = VideoCodecsEnum.VP8;
    private RTCPeerConnection _pcForEncode;                 // captured for SendVideo off-thread.
    private readonly BlockingCollection<byte[]> _encodeQueue =
        new(new ConcurrentQueue<byte[]>(), boundedCapacity: 2);
    private Thread _encodeThread;

    // WebRTC / signaling.
    private AudioExtrasSource _audioSource;
    private RTCPeerConnection _pc;
    private HttpListener _http;
    private CancellationTokenSource _cts;

    /// <summary>
    /// Creates the streaming GameObject automatically when the scene loads so no manual
    /// scene wiring is needed. Remove this method if you'd rather place the component
    /// in the scene yourself.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<WebRTCVideoSource>() == null)
        {
            var go = new GameObject("WebRTCVideoSource");
            DontDestroyOnLoad(go);
            go.AddComponent<WebRTCVideoSource>();
        }
    }

    void Awake()
    {
        // Each stage reports its own failure - a silent exception here otherwise leaves the
        // scene playing with no listener and no clue why.
        try
        {
            // A streaming source must keep producing frames when the app (or the editor
            // in play mode) loses focus - e.g. the moment the user switches to the browser
            // to click Connect. Without this the player loop suspends and the video track
            // goes silent while audio (timer-driven, off the main thread) keeps flowing.
            Application.runInBackground = true;

            SIPSorcery.LogFactory.Set(new UnityLoggerFactory());
            logger = SIPSorcery.LogFactory.CreateLogger("webrtc");

            BuildMirrorCamera();

            _readbackTexture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, mipChain: false);

            // FFmpeg native libraries: use FFMPEG_LIBS_PATH, else the folder of ffmpeg.exe on
            // PATH (the winget "shared" build ships avcodec-*.dll etc. beside the exe).
            var ffmpegLibs = ResolveFFmpegLibPath();
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_WARNING, ffmpegLibs, logger);
            Debug.Log($"FFmpeg initialised from '{ffmpegLibs ?? "(system PATH)"}'.");

            _encoder = new FFmpegVideoEncoder();

            _cts = new CancellationTokenSource();
            _encodeThread = new Thread(EncodeLoop) { IsBackground = true, Name = "vp8-encode" };
            _encodeThread.Start();

            StartHttpListener();
            StartCoroutine(CaptureLoop());

            logger.LogInformation($"WebRTC video source running. Browse to http://localhost:{HTTP_PORT} and click Connect.");
        }
        catch (Exception excp)
        {
            Debug.LogError($"WebRTCVideoSource failed to initialise: {excp}");
        }
    }

    // --- Mirror camera ---------------------------------------------------------------------

    /// <summary>
    /// A second camera renders the stream so the player's Game view is untouched. It stays
    /// disabled - CaptureLoop renders it on demand with a render request, which keeps the
    /// stream alive even when the editor is unfocused or the Game view hidden (an enabled
    /// camera or WaitForEndOfFrame capture only runs when the editor repaints).
    /// </summary>
    private void BuildMirrorCamera()
    {
        _renderTexture = new RenderTexture(WIDTH, HEIGHT, 24);
        var camGo = new GameObject("WebRTCMirrorCamera");
        camGo.transform.SetParent(transform, false);
        _mirrorCamera = camGo.AddComponent<Camera>();
        _mirrorCamera.targetTexture = _renderTexture;
        _mirrorCamera.enabled = false;
        SyncMirrorCamera();
    }

    private void SyncMirrorCamera()
    {
        var main = Camera.main;
        if (main == null || _mirrorCamera == null)
        {
            return;
        }

        _mirrorCamera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
        _mirrorCamera.fieldOfView = main.fieldOfView;
        _mirrorCamera.nearClipPlane = main.nearClipPlane;
        _mirrorCamera.farClipPlane = main.farClipPlane;
        _mirrorCamera.clearFlags = main.clearFlags;
        _mirrorCamera.backgroundColor = main.backgroundColor;
        _mirrorCamera.cullingMask = main.cullingMask;
    }

    // --- Capture -> encode ----------------------------------------------------------------

    private IEnumerator CaptureLoop()
    {
        float nextKeyFrameTime = 0f;
        var renderRequest = new RenderPipeline.StandardRequest();

        while (!_cts.IsCancellationRequested)
        {
            yield return null;

            if (Time.realtimeSinceStartup < _nextCaptureTime || _pc == null ||
                _pc.connectionState != RTCPeerConnectionState.connected)
            {
                continue;
            }
            _nextCaptureTime = Time.realtimeSinceStartup + 1f / FPS;

            // Periodic keyframe insurance: caps how long any decode glitch can freeze the
            // picture, at a small bitrate cost.
            if (Time.realtimeSinceStartup >= nextKeyFrameTime)
            {
                nextKeyFrameTime = Time.realtimeSinceStartup + 3f;
                _encoder.ForceKeyFrame();
            }

            SyncMirrorCamera();
            if (RenderPipeline.SupportsRenderRequest(_mirrorCamera, renderRequest))
            {
                renderRequest.destination = _renderTexture;
                RenderPipeline.SubmitRenderRequest(_mirrorCamera, renderRequest);
            }

            var prevActive = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            _readbackTexture.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0, recalculateMipMaps: false);
            RenderTexture.active = prevActive;

            var bgr = ToBgrFlipped(_readbackTexture.GetRawTextureData<Color32>());
            if (!_encodeQueue.TryAdd(bgr))
            {
                // Encoder still busy with the previous frame - drop this one.
            }
        }
    }

    /// <summary>Unity textures are bottom-up RGBA; the encoder wants top-down BGR24.</summary>
    private static byte[] ToBgrFlipped(Unity.Collections.NativeArray<Color32> rgba)
    {
        var bgr = new byte[WIDTH * HEIGHT * 3];
        int d = 0;
        for (int y = HEIGHT - 1; y >= 0; y--)
        {
            int row = y * WIDTH;
            for (int x = 0; x < WIDTH; x++)
            {
                var px = rgba[row + x];
                bgr[d++] = px.b;
                bgr[d++] = px.g;
                bgr[d++] = px.r;
            }
        }
        return bgr;
    }

    private void EncodeLoop()
    {
        try
        {
            foreach (var bgr in _encodeQueue.GetConsumingEnumerable(_cts.Token))
            {
                var encoded = _encoder.EncodeVideo(WIDTH, HEIGHT, bgr, VideoPixelFormatsEnum.Bgr, _negotiatedCodec);
                var pc = _pcForEncode;
                if (encoded != null && pc != null && pc.connectionState == RTCPeerConnectionState.connected)
                {
                    // Constant, correct RTP duration (90000/25 = 3600) sent straight to the
                    // peer connection - no lossy ms->RTP conversion in the path.
                    pc.SendVideo(FRAME_RTP_DURATION, encoded);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception excp)
        {
            logger.LogError($"Encode loop failed: {excp}");
        }
    }

    /// <summary>FFMPEG_LIBS_PATH override, else the directory of ffmpeg.exe found on PATH.</summary>
    private static string ResolveFFmpegLibPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("FFMPEG_LIBS_PATH");
        if (!string.IsNullOrEmpty(overridePath) && Directory.Exists(overridePath))
        {
            return overridePath;
        }
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "ffmpeg.exe")))
                {
                    return dir;
                }
            }
            catch { /* malformed PATH entry */ }
        }
        return null;   // fall back to the OS default DLL search (PATH).
    }

    // --- WebRTC + signaling ----------------------------------------------------------------

    private void StartHttpListener()
    {
        try
        {
            _http = new HttpListener();
            _http.Prefixes.Add($"http://localhost:{HTTP_PORT}/");
            _http.Prefixes.Add($"http://127.0.0.1:{HTTP_PORT}/");
            _http.Start();
            Task.Run(() => HttpLoop(_cts.Token));
            Debug.Log($"HTTP signaling listener STARTED on http://localhost:{HTTP_PORT}/.");
        }
        catch (Exception excp)
        {
            Debug.LogError($"HTTP signaling listener FAILED to start on port {HTTP_PORT}: {excp}");
            throw;
        }
    }

    private async Task HttpLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync().ConfigureAwait(false); }
            catch { break; }

            try
            {
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url.AbsolutePath == "/offer")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    string offerJson = await reader.ReadToEndAsync().ConfigureAwait(false);
                    string answerJson = await HandleOffer(offerJson).ConfigureAwait(false);
                    await WriteResponse(ctx, answerJson, "application/json").ConfigureAwait(false);
                }
                else
                {
                    await WriteResponse(ctx, INDEX_HTML, "text/html").ConfigureAwait(false);
                }
            }
            catch (Exception excp)
            {
                logger.LogError($"HTTP handler error: {excp.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }
    }

    private static async Task WriteResponse(HttpListenerContext ctx, string body, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        ctx.Response.Close();
    }

    private async Task<string> HandleOffer(string offerJson)
    {
        if (!RTCSessionDescriptionInit.TryParse(offerJson, out var offer))
        {
            throw new ApplicationException("Could not parse SDP offer.");
        }

        _pc?.Close("replaced");
        var pc = new RTCPeerConnection();

        // A fresh audio source per connection: a closed AudioExtrasSource cannot be
        // restarted (CloseAudio is terminal), so sharing one across reconnects leaves the
        // second connection silent.
        _audioSource?.CloseAudio();
        var audioSource = new AudioExtrasSource(new AudioEncoder(),
            new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence });
        _audioSource = audioSource;

        var videoTrack = new MediaStreamTrack(VideoFormats, MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);
        var audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(audioTrack);

        pc.OnVideoFormatsNegotiated += formats => _negotiatedCodec = formats[0].Codec;
        pc.OnAudioFormatsNegotiated += formats => audioSource.SetAudioSourceFormat(formats[0]);
        audioSource.OnAudioSourceEncodedSample += pc.SendAudio;
        // Video is encoded on the background EncodeLoop and sent via pc.SendVideo directly.

        // Answer the browser's picture-loss requests (PLI) with a keyframe - without this
        // a single undecodable inter-frame freezes the video permanently.
        pc.OnReceiveReport += (ep, media, report) =>
        {
            if (media == SDPMediaTypesEnum.video &&
                report?.Feedback?.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI)
            {
                logger.LogInformation("PLI received - forcing a keyframe.");
                _encoder.ForceKeyFrame();
            }
        };

        pc.onconnectionstatechange += async state =>
        {
            logger.LogInformation($"Peer connection state: {state}.");
            if (state == RTCPeerConnectionState.connected)
            {
                await audioSource.StartAudio().ConfigureAwait(false);
                _encoder.ForceKeyFrame();
            }
            else if (state is RTCPeerConnectionState.closed or RTCPeerConnectionState.failed)
            {
                await audioSource.CloseAudio().ConfigureAwait(false);
            }
        };

        pc.setRemoteDescription(offer);
        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer).ConfigureAwait(false);

        _pc = pc;
        _pcForEncode = pc;
        return answer.toJSON();
    }

    void OnDestroy()
    {
        _cts?.Cancel();
        _encodeQueue.CompleteAdding();
        try { _http?.Stop(); } catch { }
        _audioSource?.CloseAudio();
        _pc?.Close("component destroyed");
        _encoder?.Dispose();
        _renderTexture?.Release();
    }

    private const string INDEX_HTML = @"<!DOCTYPE html>
<html><head><title>Unity Video Source</title></head>
<body style='background:#111;color:#eee;font-family:sans-serif'>
<h2>Unity game camera over SIPSorcery WebRTC</h2>
<video id='v' autoplay playsinline controls style='width:960px;height:540px;background:#000'></video><br/>
<button onclick='connect()'>Connect</button>
<script>
async function connect() {
  const pc = new RTCPeerConnection();
  pc.ontrack = e => { document.getElementById('v').srcObject = e.streams[0]; };
  pc.addTransceiver('video', { direction: 'recvonly' });
  pc.addTransceiver('audio', { direction: 'recvonly' });
  const offer = await pc.createOffer();
  await pc.setLocalDescription(offer);
  await new Promise(r => { if (pc.iceGatheringState === 'complete') r();
    else pc.onicegatheringstatechange = () => pc.iceGatheringState === 'complete' && r(); });
  const resp = await fetch('/offer', { method: 'POST', body: JSON.stringify(pc.localDescription) });
  await pc.setRemoteDescription(await resp.json());
}
</script></body></html>";
}

// --- Unity logging bridge (same pattern as the UnityVideoSink example) --------------------

public class UnityLoggerFactory : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new UnityLogger(categoryName);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }
}

public class UnityLogger : ILogger
{
    private readonly string _category;
    public UnityLogger(string category) { _category = category; }
    public IDisposable BeginScope<TState>(TState state) => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
        Func<TState, Exception, string> formatter)
    {
        var msg = $"[{_category}] {formatter(state, exception)}";
        if (logLevel >= LogLevel.Error) { Debug.LogError(msg); }
        else if (logLevel == LogLevel.Warning) { Debug.LogWarning(msg); }
        else { Debug.Log(msg); }
    }
}
