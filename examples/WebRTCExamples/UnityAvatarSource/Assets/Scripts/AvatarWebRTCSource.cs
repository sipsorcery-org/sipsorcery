//-----------------------------------------------------------------------------
// Filename: AvatarWebRTCSource.cs
//
// Description: Milestone 1 of a Unity-rendered avatar streamed over WebRTC with
// SIPSorcery - the SEND-side twin of the UnityVideoSink example. Everything is
// created procedurally so setup is: empty scene -> empty GameObject -> add this
// component -> Play -> browse http://localhost:8081 -> Connect.
//
//   placeholder puppet (quads; mouth driven by a sine "voice")   <- swap for Live2D
//        v  render to RenderTexture (dedicated avatar camera)
//        v  ReadPixels each ~40ms -> RGBA32 -> flipped BGR24
//        v  background encode thread -> Vp8NetVideoEncoderEndPoint (pure C# VP8,
//           no native plugins - the same philosophy as UnityVideoSink)
//        v  RTCPeerConnection.SendVideo (+ a silence audio track for A/V SDP)
//
// Signaling matches the WebRTCMaxHeadroom demo: this component hosts a tiny
// HttpListener that serves the browser page and answers POST /offer.
//
// The mouth hook is deliberately shaped like the Max demo's IAvatarRenderer
// (SetMouthLevel 0..1): milestone 2 replaces the quad puppet with a Live2D Cubism
// model by mapping the same level onto ParamMouthOpenY.
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
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Camera = UnityEngine.Camera;   // disambiguate from SIPSorceryMedia.FFmpeg.Camera.

public class AvatarWebRTCSource : MonoBehaviour
{
    public const int WIDTH = 640;
    public const int HEIGHT = 480;
    public const int FPS = 25;
    public const int HTTP_PORT = 8081;

    private const int VIDEO_CLOCK = 90000;                 // RTP video sampling rate.
    private const uint FRAME_RTP_DURATION = VIDEO_CLOCK / FPS;   // 3600 - constant, clean.
    private static readonly List<VideoFormat> VideoFormats = new()
    {
        new VideoFormat(VideoCodecsEnum.VP8, 96, VIDEO_CLOCK)
    };

    private static ILogger logger;

    // Rendering / capture.
    private Camera _avatarCamera;
    private RenderTexture _renderTexture;
    private Texture2D _readbackTexture;
    private Transform _mouth;
    private float _mouthBaseScaleY;
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

    void Awake()
    {
        // Each stage reports its own failure - a silent exception here otherwise leaves the
        // scene playing (the puppet renders) with no listener and no clue why.
        try
        {
            SIPSorcery.LogFactory.Set(new UnityLoggerFactory());
            logger = SIPSorcery.LogFactory.CreateLogger("avatar");

            BuildPuppet();
            BuildCamera();

            _readbackTexture = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, mipChain: false);

            // FFmpeg native libraries: use FFMPEG_LIBS_PATH, else the folder of ffmpeg.exe on
            // PATH (the winget "shared" build ships avcodec-*.dll etc. beside the exe).
            var ffmpegLibs = ResolveFFmpegLibPath();
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_WARNING, ffmpegLibs, logger);
            Debug.Log($"FFmpeg initialised from '{ffmpegLibs ?? "(system PATH)"}'.");

            _encoder = new FFmpegVideoEncoder();
            _audioSource = new AudioExtrasSource(new AudioEncoder(),
                new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence });

            _cts = new CancellationTokenSource();
            _encodeThread = new Thread(EncodeLoop) { IsBackground = true, Name = "vp8-encode" };
            _encodeThread.Start();

            StartHttpListener();
            StartCoroutine(CaptureLoop());

            logger.LogInformation($"Avatar source running. Browse to http://localhost:{HTTP_PORT} and click Connect.");
        }
        catch (Exception excp)
        {
            Debug.LogError($"AvatarWebRTCSource failed to initialise: {excp}");
        }
    }

    void Update()
    {
        // Milestone 1 "voice": a sine wave opens and closes the mouth. Milestone 2 drives
        // this from TTS audio (and a Live2D ParamMouthOpenY) exactly like the Max demo's
        // IAvatarRenderer.PushAudio.
        float level = Mathf.Abs(Mathf.Sin(Time.time * 4f)) * (0.25f + 0.75f * Mathf.Abs(Mathf.Sin(Time.time * 0.7f)));
        SetMouthLevel(level);
    }

    /// <summary>0 = closed, 1 = fully open. The single animation hook a real voice drives.</summary>
    public void SetMouthLevel(float level)
    {
        var s = _mouth.localScale;
        s.y = _mouthBaseScaleY * (0.15f + 0.85f * Mathf.Clamp01(level));
        _mouth.localScale = s;
    }

    // --- Scene construction (procedural so no .unity scene file is needed) ---------------

    private void BuildPuppet()
    {
        var root = new GameObject("Puppet").transform;
        root.SetParent(transform, false);

        Quad(root, "Background", new Vector3(0, 0, 2f), new Vector3(8f, 6f, 1f), new Color32(0x10, 0x08, 0x20, 255));
        Quad(root, "Head", new Vector3(0, 0.2f, 1f), new Vector3(2.2f, 2.8f, 1f), new Color32(0xE8, 0xA8, 0x60, 255));
        Quad(root, "Hair", new Vector3(0, 1.75f, 0.9f), new Vector3(2.3f, 0.5f, 1f), new Color32(0xD8, 0xB0, 0x40, 255));
        Quad(root, "EyeL", new Vector3(-0.55f, 0.65f, 0.8f), new Vector3(0.45f, 0.22f, 1f), Color.white);
        Quad(root, "EyeR", new Vector3(0.55f, 0.65f, 0.8f), new Vector3(0.45f, 0.22f, 1f), Color.white);
        Quad(root, "PupilL", new Vector3(-0.55f, 0.65f, 0.7f), new Vector3(0.15f, 0.15f, 1f), Color.black);
        Quad(root, "PupilR", new Vector3(0.55f, 0.65f, 0.7f), new Vector3(0.15f, 0.15f, 1f), Color.black);

        var mouth = Quad(root, "Mouth", new Vector3(0, -0.65f, 0.8f), new Vector3(0.9f, 0.5f, 1f), new Color32(0x60, 0x10, 0x10, 255));
        _mouth = mouth.transform;
        _mouthBaseScaleY = _mouth.localScale.y;
    }

    private static GameObject Quad(Transform parent, string name, Vector3 pos, Vector3 scale, Color colour)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var mat = new Material(Shader.Find("Unlit/Color")) { color = colour };
        go.GetComponent<MeshRenderer>().material = mat;
        return go;
    }

    private void BuildCamera()
    {
        _renderTexture = new RenderTexture(WIDTH, HEIGHT, 24);
        var camGo = new GameObject("AvatarCamera");
        camGo.transform.SetParent(transform, false);
        camGo.transform.localPosition = new Vector3(0, 0, -5f);
        _avatarCamera = camGo.AddComponent<Camera>();
        _avatarCamera.orthographic = true;
        _avatarCamera.orthographicSize = 3f;
        _avatarCamera.clearFlags = CameraClearFlags.SolidColor;
        _avatarCamera.backgroundColor = Color.black;
        _avatarCamera.targetTexture = _renderTexture;
    }

    // --- Capture -> encode ----------------------------------------------------------------

    private IEnumerator CaptureLoop()
    {
        var wait = new WaitForEndOfFrame();
        float nextKeyFrameTime = 0f;

        while (!_cts.IsCancellationRequested)
        {
            yield return wait;

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
                    using var reader = new System.IO.StreamReader(ctx.Request.InputStream);
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

        var videoTrack = new MediaStreamTrack(VideoFormats, MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);
        var audioTrack = new MediaStreamTrack(_audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(audioTrack);

        pc.OnVideoFormatsNegotiated += formats => _negotiatedCodec = formats[0].Codec;
        pc.OnAudioFormatsNegotiated += formats => _audioSource.SetAudioSourceFormat(formats[0]);
        _audioSource.OnAudioSourceEncodedSample += pc.SendAudio;
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
                await _audioSource.StartAudio().ConfigureAwait(false);
                _encoder.ForceKeyFrame();
            }
            else if (state is RTCPeerConnectionState.closed or RTCPeerConnectionState.failed)
            {
                await _audioSource.CloseAudio().ConfigureAwait(false);
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
        _pc?.Close("component destroyed");
        _encoder?.Dispose();
        _renderTexture?.Release();
    }

    private const string INDEX_HTML = @"<!DOCTYPE html>
<html><head><title>Unity Avatar Source</title></head>
<body style='background:#111;color:#eee;font-family:sans-serif'>
<h2>Unity Avatar over SIPSorcery WebRTC</h2>
<video id='v' autoplay playsinline controls style='width:640px;height:480px;background:#000'></video><br/>
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
