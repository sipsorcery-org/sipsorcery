// Streams a VRM avatar, rendered and animated entirely in-process by Godot/C#, to a browser
// over WebRTC using SIPSorcery. The avatar-driving half (VRM load + blink/gaze/breath/head +
// simulated-speech lipsync onto VRoid facial morph targets) mirrors the CodexGodot prototype;
// the world is rendered into a fixed-size SubViewport whose frames are read back each tick,
// VP8-encoded (pure C#, SIPSorcery.VP8), and sent over an RTCPeerConnection.
//
// Signalling is a tiny in-process HttpListener that serves the viewer page and answers
// POST /offer - browse http://localhost:8081 and click Connect.
using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Vpx.Net;
using demo;   // Max-demo speech pipeline (IAvatarMouth / IAvatarSpeaker / SherpaTtsSpeaker).

// The Godot avatar is the IAvatarMouth: the TTS speaker feeds it synthesised PCM, which drives
// the VRM mouth morphs. It is NOT an IVideoSource - it owns its own capture/VP8/WebRTC video path.
public partial class AvatarStreamer : Node, IAvatarMouth
{
    private const int Width = 640;
    private const int Height = 480;
    private const int Fps = 25;
    private const uint FrameDurationMs = 1000 / Fps;   // 40ms - an exact ms->RTP divisor for VP8.
    private const int HttpPort = 8081;

    // --- Rendering / avatar (built in code, inside a SubViewport) -------------------------
    private SubViewport _viewport = null!;
    private IAvatarModel _model = null!;   // VRM (3D) or Live2D (2D), chosen at launch.
    private double _time;
    private float _mouthOpen;
    private float _audioLevel;

    // --- Capture / encode / WebRTC --------------------------------------------------------
    private double _nextCaptureTime;
    private readonly BlockingCollection<(int W, int H, byte[] Bgr)> _encodeQueue =
        new(new ConcurrentQueue<(int, int, byte[])>(), boundedCapacity: 2);
    private Thread _encodeThread = null!;
    private readonly CancellationTokenSource _cts = new();

    private Vp8NetVideoEncoderEndPoint _encoder = null!;
    private volatile RTCPeerConnection? _pc;
    private HttpListener? _http;
    private Label _status = null!;

    // --- Speech (Max-demo pipeline, in-process) -------------------------------------------
    private const string MaleVoiceDir = @"C:\tools\sherpa-tts\vits-piper-en_US-ryan-high";
    // Female Piper voices tried (in order) for the VRM; falls back to the male voice if none exist.
    private static readonly string[] FemaleVoiceDirs =
    {
        @"C:\tools\sherpa-tts\vits-piper-en_US-hfc_female-medium",
        @"C:\tools\sherpa-tts\vits-piper-en_US-amy-medium",
        @"C:\tools\sherpa-tts\vits-piper-en_US-amy-low",
        @"C:\tools\sherpa-tts\vits-piper-en_US-kathleen-low",
    };
    private const float SpeechToMouthGain = 7.0f;   // RMS of speech (~0.1) -> mouth openness (0..1).
    private string _avatarKind = "vrm";
    private const string LlmGgufPath = @"C:\tools\llm\Llama-3.2-3B-Instruct-Q4_K_M.gguf";

    // The avatar's personality (LLM system prompt). Adjustable here or via the AVATAR_PERSONA
    // environment variable - this is the one knob that gives the VRM its own character.
    private const string DefaultPersona =
        "You are Aria, a warm, upbeat virtual avatar streamed live over WebRTC. Reply in one or two " +
        "short, friendly, conversational sentences. Plain text only - no stage directions or emojis.";
    private static string Persona =>
        System.Environment.GetEnvironmentVariable("AVATAR_PERSONA") is { Length: > 0 } p ? p : DefaultPersona;
    private AudioExtrasSource _audioSource = null!;
    private IAvatarSpeaker? _speaker;
    private ISpeechRecognizer? _recognizer;
    private ILlmClient? _llm;
    private readonly AudioEncoder _micDecoder = new();
    private static readonly AudioFormat PcmuFormat = new(SDPWellKnownMediaFormatsEnum.PCMU);
    private int _audioStarted;                       // 0/1 guard so StartAudio runs once.
    private int _recognizerStarted;                  // 0/1 guard so the recogniser starts once.
    private volatile bool _speaking;
    private volatile float _speechLevel;             // RMS of the latest pushed audio window.

    public override void _Ready()
    {
        _avatarKind = ResolveAvatarKind();
        GD.Print($"Avatar model: {_avatarKind}");
        BuildViewport();
        _model = CreateAvatarModel(_avatarKind);
        _model.Build(_viewport);
        BuildInterface();

        // VP8 encoder endpoint (pure managed). Encoded frames are pushed straight to whatever
        // peer connection is currently live.
        _encoder = new Vp8NetVideoEncoderEndPoint();
        _encoder.OnVideoSourceEncodedSample += (durationRtpUnits, sample) =>
        {
            var pc = _pc;
            if (pc != null && pc.connectionState == RTCPeerConnectionState.connected)
            {
                pc.SendVideo(durationRtpUnits, sample);
            }
        };

        _encodeThread = new Thread(EncodeLoop) { IsBackground = true, Name = "vp8-encode" };
        _encodeThread.Start();

        // Audio: a continuous PCMU source (Silence between utterances keeps the A/V clock steady).
        // Encoded audio is forwarded to whatever peer connection is currently live, mirroring the
        // video path above.
        _audioSource = new AudioExtrasSource(new AudioEncoder(),
            new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence });
        _audioSource.RestrictFormats(f => f.Codec == AudioCodecsEnum.PCMU);
        _audioSource.OnAudioSourceEncodedSample += (durationRtpUnits, sample) =>
        {
            var pc = _pc;
            if (pc != null && pc.connectionState == RTCPeerConnectionState.connected)
            {
                pc.SendAudio(durationRtpUnits, sample);
            }
        };
        TryCreateSpeaker();

        StartHttpListener();
        GD.Print($"Avatar streamer running. Browse to http://localhost:{HttpPort} and click Connect.");
    }

    private void TryCreateSpeaker()
    {
        // Text-to-speech (voice out + mouth). The VRM uses a female voice when one is installed;
        // the Live2D "Ren" character keeps the male voice.
        var voiceDir = ResolveVoiceDir();
        if (System.IO.Directory.Exists(voiceDir))
        {
            try
            {
                _speaker = new SherpaTtsSpeaker(voiceDir, this, _audioSource);
                GD.Print($"Sherpa TTS speaker created ({System.IO.Path.GetFileName(voiceDir)}).");
            }
            catch (Exception excp)
            {
                GD.PushError($"Failed to create Sherpa TTS speaker: {excp}");
            }
        }
        else
        {
            GD.PushWarning($"Sherpa voice model not found at {voiceDir}; the avatar will render but not speak.");
        }

        // Large language model (turns recognised/typed text into a reply). Optional: falls back to
        // speaking the prompt verbatim when no model is present.
        try
        {
            if (System.IO.File.Exists(LlmGgufPath))
            {
                _llm = new LlamaSharpLlmClient(LlmGgufPath, gpuLayers: 0, systemPrompt: Persona);
                _ = _llm.WarmUpAsync();
                GD.Print($"LLM loaded: {_llm.Description}.");
            }
            else
            {
                GD.PushWarning($"LLM model not found at {LlmGgufPath}; text will be spoken verbatim.");
            }
        }
        catch (Exception excp)
        {
            GD.PushError($"Failed to load LLM (speaking verbatim instead): {excp}");
        }

        // Speech-to-text (browser microphone -> text). Optional.
        try
        {
            if (SherpaSpeechRecognizer.FilesPresent())
            {
                var recognizer = new SherpaSpeechRecognizer();
                recognizer.OnRecognized += text =>
                {
                    GD.Print($"Recognised: {text}");
                    _ = AskAsync(text);
                };
                _recognizer = recognizer;
                _ = SherpaSpeechRecognizer.PreloadAsync();
                GD.Print("Sherpa STT recogniser created.");
            }
            else
            {
                GD.PushWarning("Sherpa STT model not found; the avatar can speak but not listen.");
            }
        }
        catch (Exception excp)
        {
            GD.PushError($"Failed to create STT recogniser: {excp}");
        }
    }

    /// <summary>
    /// Runs a prompt (typed via /ask or recognised from the microphone) through the LLM and speaks
    /// the reply sentence-by-sentence so the avatar starts talking on the first sentence. Falls
    /// back to speaking the prompt verbatim when no LLM is configured. Returns the full reply text.
    /// </summary>
    private async Task<string> AskAsync(string prompt)
    {
        var speaker = _speaker;
        if (speaker == null || string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        try
        {
            if (_llm is { IsConfigured: true } llm)
            {
                var reply = new StringBuilder();
                await foreach (var sentence in llm.StreamReplyAsync(prompt).ConfigureAwait(false))
                {
                    reply.Append(sentence).Append(' ');
                    await speaker.SpeakAsync(sentence).ConfigureAwait(false);
                }
                var text = reply.ToString().Trim();
                GD.Print($"Reply: {text}");
                return text;
            }

            await speaker.SpeakAsync(prompt).ConfigureAwait(false);
            return prompt;
        }
        catch (Exception excp)
        {
            GD.PushError($"AskAsync failed: {excp.Message}");
            return string.Empty;
        }
    }

    // --- IAvatarMouth: the TTS speaker drives the mouth through these (called off-thread) --------

    // Reacts to pushed audio immediately (amplitude heuristic), so the speaker paces PushAudio to
    // real time alongside playback - do not buffer internally.
    public bool PacesAudioInternally => false;

    public void BeginSpeech() => _speaking = true;

    public void EndSpeech()
    {
        _speaking = false;
        _speechLevel = 0f;
    }

    // Called on the speaker's thread with 16-bit mono PCM windows. Keep it to a cheap RMS and a
    // volatile write - the render thread turns _speechLevel into mouth motion in UpdateAudioLevel.
    public void PushAudio(ReadOnlySpan<short> pcm16, int sampleRate)
    {
        if (pcm16.Length == 0)
        {
            return;
        }
        double sumSq = 0;
        for (int i = 0; i < pcm16.Length; i++)
        {
            double s = pcm16[i] / 32768.0;
            sumSq += s * s;
        }
        _speechLevel = (float)Math.Sqrt(sumSq / pcm16.Length);
    }

    public override void _Process(double delta)
    {
        _time += delta;
        UpdateAudioLevel();

        // Smooth the mouth toward the current audio level, then let the active model apply it
        // (VRM morph targets or Cubism parameters) alongside its own blink/breath/sway liveness.
        var target = Mathf.Clamp(_audioLevel, 0, 1);
        _mouthOpen = Mathf.Lerp(_mouthOpen, target, (float)Math.Min(1, delta * (target > _mouthOpen ? 22 : 18)));
        _model.Update(delta, _time, _mouthOpen);

        CaptureFrame();
    }

    // --- Scene construction ---------------------------------------------------------------

    private void BuildViewport()
    {
        // A SubViewportContainer shows the avatar in the local window. Stretch is deliberately OFF:
        // a stretching container resizes the SubViewport to the window, which would break the
        // fixed-resolution frame readback below. The avatar model fills the viewport contents.
        var container = new SubViewportContainer { Stretch = false };
        AddChild(container);

        _viewport = new SubViewport
        {
            Size = new Vector2I(Width, Height),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
        };
        container.AddChild(_viewport);
    }

    private static IAvatarModel CreateAvatarModel(string kind) => kind switch
    {
        "live2d" or "ren" or "cubism" => new Live2DAvatarModel(),
        _ => new VrmAvatarModel(),
    };

    /// <summary>
    /// Voice model directory for the current avatar: a female Piper voice for the VRM (when one is
    /// installed), the male Ryan voice for the Live2D "Ren" character. Falls back to the male voice
    /// when no female voice is present on disk.
    /// </summary>
    private string ResolveVoiceDir()
    {
        bool isLive2D = _avatarKind is "live2d" or "ren" or "cubism";
        if (!isLive2D)
        {
            foreach (var dir in FemaleVoiceDirs)
            {
                if (System.IO.Directory.Exists(dir))
                {
                    return dir;
                }
            }
        }
        return MaleVoiceDir;
    }

    /// <summary>
    /// Avatar selection: <c>--avatar &lt;vrm|ren&gt;</c> (space- or =-separated) passed after
    /// <c>--</c> on the Godot command line, or the <c>AVATAR_MODEL</c> environment variable.
    /// Defaults to the VRM.
    /// </summary>
    private static string ResolveAvatarKind()
    {
        string kind = "vrm";
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--avatar=", StringComparison.OrdinalIgnoreCase))
            {
                kind = args[i].Substring("--avatar=".Length);
            }
            else if (args[i].Equals("--avatar", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                kind = args[i + 1];
            }
        }
        var env = System.Environment.GetEnvironmentVariable("AVATAR_MODEL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            kind = env;
        }
        return kind.Trim().ToLowerInvariant();
    }

    private void BuildInterface()
    {
        var panel = new PanelContainer { Position = new Vector2(14, 14) };
        AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);
        var title = new Label { Text = "VRM  ->  WebRTC" };
        title.AddThemeFontSizeOverride("font_size", 16);
        box.AddChild(title);
        _status = new Label { Text = $"Browse http://localhost:{HttpPort} and click Connect." };
        box.AddChild(_status);
    }

    // --- Liveness (adapted from the CodexGodot prototype) ---------------------------------

    private void UpdateAudioLevel()
    {
        // Driven by real TTS audio: while speaking, the mouth follows the RMS of the PCM the
        // speaker is playing on the audio track; otherwise it snaps closed. Fast attack tracks
        // syllables; brisk release stops the mouth promptly when speech ends.
        float target = _speaking ? Mathf.Clamp(_speechLevel * SpeechToMouthGain, 0f, 1f) : 0f;
        float rate = target > _audioLevel ? 0.6f : 0.5f;
        _audioLevel = Mathf.Lerp(_audioLevel, target, rate);
    }

    // --- Capture -> encode ----------------------------------------------------------------

    private void CaptureFrame()
    {
        var pc = _pc;
        if (pc == null || pc.connectionState != RTCPeerConnectionState.connected)
        {
            return;
        }
        var now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextCaptureTime)
        {
            return;
        }
        _nextCaptureTime = now + 1.0 / Fps;

        var image = _viewport.GetTexture().GetImage();
        if (image == null)
        {
            return;
        }
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }

        int w = image.GetWidth();
        int h = image.GetHeight();
        var bgr = ToBgr(image.GetData(), w, h);
        if (!_encodeQueue.TryAdd((w, h, bgr)))
        {
            // Encoder still busy with the previous frame - drop this one.
        }
    }

    /// <summary>Godot viewport image is top-down RGBA8; the encoder wants top-down BGR24.</summary>
    private static byte[] ToBgr(byte[] rgba, int w, int h)
    {
        var bgr = new byte[w * h * 3];
        int px = Math.Min(w * h, rgba.Length / 4);
        int d = 0;
        for (int p = 0; p < px; p++)
        {
            int i = p * 4;
            bgr[d++] = rgba[i + 2];   // B
            bgr[d++] = rgba[i + 1];   // G
            bgr[d++] = rgba[i + 0];   // R
        }
        return bgr;
    }

    private void EncodeLoop()
    {
        try
        {
            foreach (var (w, h, bgr) in _encodeQueue.GetConsumingEnumerable(_cts.Token))
            {
                _encoder.ExternalVideoSourceRawSample(FrameDurationMs, w, h, bgr, VideoPixelFormatsEnum.Bgr);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception excp)
        {
            GD.PushError($"Encode loop failed: {excp}");
        }
    }

    // --- WebRTC + signalling --------------------------------------------------------------

    private void StartHttpListener()
    {
        _http = new HttpListener();
        _http.Prefixes.Add($"http://localhost:{HttpPort}/");
        _http.Prefixes.Add($"http://127.0.0.1:{HttpPort}/");
        _http.Start();
        Task.Run(() => HttpLoop(_cts.Token));
    }

    private async Task HttpLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _http != null)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync().ConfigureAwait(false); }
            catch { break; }

            try
            {
                if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/offer")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    string offerJson = await reader.ReadToEndAsync().ConfigureAwait(false);
                    string answerJson = HandleOffer(offerJson);
                    await WriteResponse(ctx, answerJson, "application/json").ConfigureAwait(false);
                }
                else if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/say")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    string text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    if (_speaker != null && !string.IsNullOrWhiteSpace(text))
                    {
                        _ = _speaker.SpeakAsync(text);
                        await WriteResponse(ctx, "ok", "text/plain").ConfigureAwait(false);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 503;
                        await WriteResponse(ctx, "no TTS configured", "text/plain").ConfigureAwait(false);
                    }
                }
                else if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/ask")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    string prompt = await reader.ReadToEndAsync().ConfigureAwait(false);
                    if (_speaker != null && !string.IsNullOrWhiteSpace(prompt))
                    {
                        var reply = await AskAsync(prompt).ConfigureAwait(false);
                        await WriteResponse(ctx, reply, "text/plain").ConfigureAwait(false);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 503;
                        await WriteResponse(ctx, "no TTS configured", "text/plain").ConfigureAwait(false);
                    }
                }
                else
                {
                    await WriteResponse(ctx, IndexHtml, "text/html").ConfigureAwait(false);
                }
            }
            catch (Exception excp)
            {
                GD.PushError($"HTTP handler error: {excp.Message}");
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }
    }

    private static async Task WriteResponse(HttpListenerContext ctx, string body, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        ctx.Response.Close();
    }

    private string HandleOffer(string offerJson)
    {
        if (!RTCSessionDescriptionInit.TryParse(offerJson, out var offer))
        {
            throw new ApplicationException("Could not parse SDP offer.");
        }

        _pc?.Close("replaced");
        var pc = new RTCPeerConnection();

        var videoTrack = new MediaStreamTrack(_encoder.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);
        pc.OnVideoFormatsNegotiated += formats => _encoder.SetVideoSourceFormat(formats[0]);

        // Audio track is SendRecv: the avatar voice goes out, and the browser microphone comes in
        // (the mic feed is where speech-to-text will hook in the next stage).
        var audioTrack = new MediaStreamTrack(_audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
        pc.addTrack(audioTrack);
        pc.OnAudioFormatsNegotiated += formats => _audioSource.SetAudioSourceFormat(formats[0]);

        // Inbound microphone: decode the received PCMU RTP to 8kHz PCM and feed speech-to-text.
        // Recognised utterances run the same AskAsync path as the /ask endpoint.
        if (_recognizer != null)
        {
            pc.OnAudioFrameReceived += frame =>
            {
                try { _recognizer.Write(_micDecoder.DecodeAudio(frame.EncodedAudio, PcmuFormat)); }
                catch (Exception excp) { GD.PushWarning($"Mic decode failed: {excp.Message}"); }
            };
        }

        // Answer picture-loss requests with a keyframe so a single lost frame can't freeze the
        // picture permanently.
        pc.OnReceiveReport += (ep, media, report) =>
        {
            if (media == SDPMediaTypesEnum.video &&
                report?.Feedback?.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI)
            {
                _encoder.ForceKeyFrame();
            }
        };

        pc.onconnectionstatechange += async state =>
        {
            GD.Print($"Peer connection state: {state}.");
            if (state == RTCPeerConnectionState.connected)
            {
                // Start the audio clock once, then greet through the TTS pipeline.
                if (Interlocked.Exchange(ref _audioStarted, 1) == 0)
                {
                    await _audioSource.StartAudio().ConfigureAwait(false);
                }
                // Begin listening to the microphone (speech-to-text) once.
                if (_recognizer != null && Interlocked.Exchange(ref _recognizerStarted, 1) == 0)
                {
                    await _recognizer.StartAsync().ConfigureAwait(false);
                }
                if (_speaker != null)
                {
                    _ = _speaker.SpeakAsync(
                        "Hi there. I'm a V-R-M avatar, rendered live in Godot and streamed to you over Web R-T-C by SIP-Sorcery. Ask me anything.");
                }
            }
        };

        pc.setRemoteDescription(offer);
        var answer = pc.createAnswer();
        pc.setLocalDescription(answer).Wait();

        _pc = pc;
        return answer.toJSON();
    }

    public override void _ExitTree()
    {
        _cts.Cancel();
        _encodeQueue.CompleteAdding();
        try { _http?.Stop(); } catch { }
        _pc?.Close("shutting down");
    }

    private const string IndexHtml = @"<!DOCTYPE html>
<html><head><title>Godot VRM over WebRTC</title></head>
<body style='background:#111;color:#eee;font-family:sans-serif;text-align:center'>
<h2>Godot VRM avatar over SIPSorcery WebRTC</h2>
<video id='v' autoplay playsinline controls style='width:640px;height:480px;background:#000'></video><br/>
<button onclick='connect()'>Connect</button>
<div style='margin-top:10px'>
  <input id='say' placeholder='Type something for the avatar to say' style='width:420px'/>
  <button onclick='say()'>Say</button>
</div>
<div style='margin-top:6px'>
  <input id='ask' placeholder='Ask the avatar a question (runs the LLM)' style='width:420px'/>
  <button onclick='ask()'>Ask</button>
</div>
<p style='color:#888'>Or just speak - the browser mic is sent to the avatar's speech recognition.</p>
<p id='status' style='color:#7dd3fc'></p>
<p id='reply' style='color:#a9c2d6'></p>
<script>
async function connect() {
  const pc = new RTCPeerConnection();
  pc.ontrack = e => { document.getElementById('v').srcObject = e.streams[0]; };
  pc.addTransceiver('video', { direction: 'recvonly' });
  // Try to add the microphone (sendrecv) so the avatar can also listen; fall back to
  // receive-only audio if the mic is unavailable or denied.
  try {
    const mic = await navigator.mediaDevices.getUserMedia({ audio: true });
    mic.getAudioTracks().forEach(t => pc.addTrack(t, mic));
  } catch (err) {
    pc.addTransceiver('audio', { direction: 'recvonly' });
  }
  const offer = await pc.createOffer();
  await pc.setLocalDescription(offer);
  await new Promise(r => { if (pc.iceGatheringState === 'complete') r();
    else pc.onicegatheringstatechange = () => pc.iceGatheringState === 'complete' && r(); });
  const resp = await fetch('/offer', { method: 'POST', body: JSON.stringify(pc.localDescription) });
  await pc.setRemoteDescription(await resp.json());
  document.getElementById('status').textContent = 'connected';
}
async function say() {
  const t = document.getElementById('say').value;
  if (t) { await fetch('/say', { method: 'POST', body: t }); }
}
async function ask() {
  const t = document.getElementById('ask').value;
  if (!t) { return; }
  document.getElementById('reply').textContent = '...';
  const r = await fetch('/ask', { method: 'POST', body: t });
  document.getElementById('reply').textContent = await r.text();
}
</script></body></html>";
}
