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
    private volatile string? _pendingAvatarSwitch;   // set by POST /avatar (HTTP thread), applied in _Process.
    private string _avatarsJson = "[]";              // available avatar specs, served to the web UI.
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
    private const string SherpaTtsDir = @"C:\tools\sherpa-tts";
    private const string MaleVoiceDir = SherpaTtsDir + @"\vits-piper-en_US-ryan-high";
    // Female Piper voices tried (in order); falls back to the male voice if none exist.
    private static readonly string[] FemaleVoiceDirs =
    {
        SherpaTtsDir + @"\vits-piper-en_US-hfc_female-medium",
        SherpaTtsDir + @"\vits-piper-en_US-amy-medium",
        SherpaTtsDir + @"\vits-piper-en_US-amy-low",
        SherpaTtsDir + @"\vits-piper-en_US-kathleen-low",
    };
    private const float SpeechToMouthGain = 7.0f;   // RMS of speech (~0.1) -> mouth openness (0..1).
    private string _avatarKind = "vrm";
    private string? _avatarGender;                   // inferred from the model's female/male folder, if any.
    private string? _currentVoiceDir;                // the active TTS voice folder (avoids needless reloads).
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
        var (kind, modelName) = ResolveAvatar();
        _avatarKind = kind;
        GD.Print($"Avatar model: {kind}{(modelName is null ? "" : $" ({modelName})")}");
        BuildViewport();
        (_model, _avatarGender) = CreateAvatarModel(kind, modelName);
        _model.Build(_viewport);
        BuildInterface();
        BuildAvatarsJson();

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

    /// <summary>
    /// Creates (or replaces) the TTS speaker for <paramref name="voiceDir"/>. No-op if that voice is
    /// already active, so switching between avatars of the same gender does not reload the model.
    /// </summary>
    private void ApplyVoice(string voiceDir)
    {
        if (voiceDir == _currentVoiceDir && _speaker != null)
        {
            return;
        }
        if (!System.IO.Directory.Exists(voiceDir))
        {
            GD.PushWarning($"Sherpa voice model not found at {voiceDir}; the avatar will render but not speak.");
            return;
        }
        try
        {
            (_speaker as IDisposable)?.Dispose();
            _speaker = new SherpaTtsSpeaker(voiceDir, this, _audioSource);
            _currentVoiceDir = voiceDir;
            GD.Print($"Sherpa TTS voice: {System.IO.Path.GetFileName(voiceDir)}.");
        }
        catch (Exception excp)
        {
            GD.PushError($"Failed to create Sherpa TTS speaker: {excp}");
        }
    }

    private void TryCreateSpeaker()
    {
        // Text-to-speech: the voice follows the avatar's gender folder (see ResolveVoiceDir).
        ApplyVoice(ResolveVoiceDir());

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
        // Apply a pending avatar switch (requested from the HTTP thread) on the main thread.
        var pending = _pendingAvatarSwitch;
        if (pending != null)
        {
            _pendingAvatarSwitch = null;
            SwitchAvatar(pending);
        }

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

    /// <summary>
    /// Swaps the live avatar without dropping the WebRTC session: frees the current model's nodes
    /// from the capture viewport and builds the requested one in their place. Must run on the main
    /// thread (called from _Process). The stream keeps flowing - the encoder just reads whatever the
    /// viewport now renders. Note: switching to a VRM whose scene has not been imported yet will fail
    /// to load; Live2D models need no import.
    /// </summary>
    private void SwitchAvatar(string spec)
    {
        var (kind, name) = ParseAvatarSpec(spec);
        GD.Print($"Switching avatar to {kind}{(name is null ? "" : $" ({name})")}.");

        foreach (var child in _viewport.GetChildren())
        {
            child.QueueFree();
        }

        _avatarKind = kind;
        (_model, _avatarGender) = CreateAvatarModel(kind, name);
        _model.Build(_viewport);
        ApplyVoice(ResolveVoiceDir());   // re-resolve the voice so it follows the new avatar's gender.
    }

    /// <summary>Builds the JSON array of available avatar specs (each VRM under Models/ and each
    /// Live2D model folder) served to the web UI's switcher.</summary>
    private void BuildAvatarsJson()
    {
        var sb = new StringBuilder("[");
        bool first = true;
        void Add(string spec)
        {
            if (!first) { sb.Append(','); }
            sb.Append('"').Append(spec).Append('"');
            first = false;
        }

        // VRM: flat Models/*.vrm plus gendered Models/vrm/{female,male}/*.vrm.
        void AddVrmsIn(string dir)
        {
            using var d = DirAccess.Open(dir);
            if (d == null) { return; }
            foreach (var file in d.GetFiles())
            {
                if (file.EndsWith(".vrm", StringComparison.OrdinalIgnoreCase))
                {
                    Add("vrm:" + file.Substring(0, file.Length - 4));
                }
            }
        }
        AddVrmsIn("res://Models");
        foreach (var gender in GenderDirs) { AddVrmsIn($"res://Models/vrm/{gender}"); }

        // Live2D: flat Models/Live2D/<name>/ plus gendered Models/Live2D/{female,male}/<name>/.
        void AddLive2DIn(string dir)
        {
            using var d = DirAccess.Open(dir);
            if (d == null) { return; }
            foreach (var sub in d.GetDirectories())
            {
                if (sub is "female" or "male") { continue; }
                if (FindModel3Json($"{dir}/{sub}/runtime") != null)
                {
                    Add("live2d:" + sub);
                }
            }
        }
        AddLive2DIn("res://Models/Live2D");
        foreach (var gender in GenderDirs) { AddLive2DIn($"res://Models/Live2D/{gender}"); }

        sb.Append(']');
        _avatarsJson = sb.ToString();
    }

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

    // Optional voice-gender folders: a model under Models/<type>/female or /male infers that voice
    // gender (overridable with --gender / --voice). Checked in this order; flat layout has no gender.
    private static readonly string[] GenderDirs = { "female", "male" };

    private static (IAvatarModel model, string? gender) CreateAvatarModel(string kind, string? name) => kind switch
    {
        "ren" => CreateLive2DModel(name ?? "ren"),   // the 'ren' alias defaults to the Ren model
        "live2d" or "cubism" => CreateLive2DModel(name),
        _ => CreateVrmModel(name),
    };

    // Per-model Live2D overrides, keyed by the model folder name (case-insensitive). Unlisted models
    // use the defaults (render order). Add an entry when a model needs tuning.
    private static readonly System.Collections.Generic.Dictionary<string, Live2DAvatarConfig> Live2DConfigs =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Ren (older Cubism sample) only renders its mouth under draw order.
        ["ren"] = new Live2DAvatarConfig { UseDrawOrder = true },
    };

    /// <summary>Builds the VRM avatar for <paramref name="name"/> and returns its inferred voice
    /// gender (from a Models/vrm/female|male folder, or null for the flat Models/&lt;name&gt;.vrm).</summary>
    private static (IAvatarModel, string?) CreateVrmModel(string? name)
    {
        var (path, gender) = ResolveVrmPath(name ?? "UserAvatar");
        return (new VrmAvatarModel(path ?? "res://Models/UserAvatar.vrm"), gender);
    }

    private static (string? path, string? gender) ResolveVrmPath(string name)
    {
        foreach (var gender in GenderDirs)
        {
            string p = $"res://Models/vrm/{gender}/{name}.vrm";
            if (Godot.FileAccess.FileExists(p))
            {
                return (p, gender);
            }
        }
        string flat = $"res://Models/{name}.vrm";
        return Godot.FileAccess.FileExists(flat) ? (flat, null) : (null, null);
    }

    /// <summary>
    /// Builds the Live2D avatar for <paramref name="name"/> (or the first model found when
    /// null/missing) and returns its inferred voice gender. Gender folders
    /// (<c>Models/Live2D/female|male/</c>) are checked before the flat <c>Models/Live2D/&lt;name&gt;/</c>.
    /// gd_cubism reads the model at runtime, so no Godot import is needed - just drop the folder in.
    /// </summary>
    private static (IAvatarModel, string?) CreateLive2DModel(string? name)
    {
        var (path, folder, gender) = ResolveLive2D(name);
        if (path is null)
        {
            GD.PushError("No Live2D model found. Add one under Models/Live2D/[female|male/]<name>/runtime/.");
            return (new Live2DAvatarModel(), null);
        }
        var config = Live2DConfigs.GetValueOrDefault(folder) ?? new Live2DAvatarConfig();
        return (new Live2DAvatarModel(path, config), gender);
    }

    /// <summary>
    /// Resolves a Live2D model to (model3.json path, folder name, gender). With a name it checks the
    /// gender folders then the flat location; with no name (or a miss) it auto-discovers the first
    /// model, preferring gendered folders. Gender is null for flat (non-gendered) models.
    /// </summary>
    private static (string? path, string folder, string? gender) ResolveLive2D(string? name)
    {
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var gender in GenderDirs)
            {
                var p = FindModel3Json($"res://Models/Live2D/{gender}/{name}/runtime");
                if (p != null)
                {
                    return (p, name, gender);
                }
            }
            var flat = FindModel3Json($"res://Models/Live2D/{name}/runtime");
            if (flat != null)
            {
                return (flat, name, null);
            }
            GD.PushWarning($"No Live2D model '{name}'; using the first one found.");
        }

        foreach (var gender in GenderDirs)
        {
            using var genderDir = DirAccess.Open($"res://Models/Live2D/{gender}");
            if (genderDir != null)
            {
                foreach (var sub in genderDir.GetDirectories())
                {
                    var p = FindModel3Json($"res://Models/Live2D/{gender}/{sub}/runtime");
                    if (p != null)
                    {
                        return (p, sub, gender);
                    }
                }
            }
        }

        using (var root = DirAccess.Open("res://Models/Live2D"))
        {
            if (root != null)
            {
                foreach (var sub in root.GetDirectories())
                {
                    if (sub is "female" or "male")
                    {
                        continue;
                    }
                    var p = FindModel3Json($"res://Models/Live2D/{sub}/runtime");
                    if (p != null)
                    {
                        return (p, sub, null);
                    }
                }
            }
        }
        return (null, "", null);
    }

    /// <summary>Returns the first <c>*.model3.json</c> in <paramref name="dir"/> (a res:// path), or null.</summary>
    private static string? FindModel3Json(string dir)
    {
        using var da = DirAccess.Open(dir);
        if (da == null)
        {
            return null;
        }
        foreach (var file in da.GetFiles())
        {
            if (file.EndsWith(".model3.json", StringComparison.OrdinalIgnoreCase))
            {
                return $"{dir}/{file}";
            }
        }
        return null;
    }

    /// <summary>
    /// Voice model directory, resolved in priority order:
    /// <list type="number">
    /// <item>an exact voice via <c>--voice</c> / <c>AVATAR_VOICE</c> (a folder under
    /// <c>C:\tools\sherpa-tts</c>, given as the full folder name or the short suffix, e.g.
    /// <c>amy-medium</c> -> <c>vits-piper-en_US-amy-medium</c>);</item>
    /// <item>a gender via <c>--gender</c> / <c>AVATAR_GENDER</c> (<c>male</c> / <c>female</c>);</item>
    /// <item>otherwise the avatar's default (VRM -> female, Live2D -> male).</item>
    /// </list>
    /// A female selection with no female voice installed falls back to the male voice.
    /// </summary>
    private string ResolveVoiceDir()
    {
        // 1. Exact voice override.
        var voice = ResolveArg("--voice", "AVATAR_VOICE");
        if (!string.IsNullOrWhiteSpace(voice))
        {
            var dir = ResolveVoicePath(voice);
            if (System.IO.Directory.Exists(dir))
            {
                return dir;
            }
            GD.PushWarning($"Requested voice '{voice}' not found at {dir}; falling back to a default voice.");
        }

        // 2. Explicit --gender, else the gender inferred from the model's folder, else the kind
        //    default (VRM -> female, Live2D -> male).
        var gender = (ResolveArg("--gender", "AVATAR_GENDER") ?? _avatarGender)?.Trim().ToLowerInvariant();
        bool female = gender switch
        {
            "female" or "f" or "woman" => true,
            "male" or "m" or "man" => false,
            _ => _avatarKind is not ("live2d" or "ren" or "cubism"),
        };

        if (female)
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

    /// <summary>Resolves a voice name to a sherpa-tts folder: the exact folder if it exists,
    /// otherwise the conventional <c>vits-piper-en_US-&lt;name&gt;</c> form.</summary>
    private static string ResolveVoicePath(string voice)
    {
        voice = voice.Trim();
        string exact = System.IO.Path.Combine(SherpaTtsDir, voice);
        if (System.IO.Directory.Exists(exact))
        {
            return exact;
        }
        return System.IO.Path.Combine(SherpaTtsDir, $"vits-piper-en_US-{voice}");
    }

    /// <summary>
    /// Reads a launch option from <c>--flag value</c> / <c>--flag=value</c> (after <c>--</c> on the
    /// Godot command line) or the given environment variable, which takes precedence. Returns null
    /// when neither is set.
    /// </summary>
    private static string? ResolveArg(string flag, string envVar)
    {
        string? value = null;
        var args = OS.GetCmdlineUserArgs();
        string eq = flag + "=";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(eq, StringComparison.OrdinalIgnoreCase))
            {
                value = args[i].Substring(eq.Length);
            }
            else if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                value = args[i + 1];
            }
        }
        var env = System.Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            value = env;
        }
        return value;
    }

    /// <summary>
    /// Avatar selection: <c>--avatar &lt;kind&gt;[:&lt;name&gt;]</c> (space- or =-separated) passed after
    /// <c>--</c> on the Godot command line, or the <c>AVATAR_MODEL</c> environment variable. The
    /// optional <c>:name</c> picks a specific model: <c>vrm:Alice</c> -> <c>Models/Alice.vrm</c>,
    /// <c>live2d:Haru</c> -> <c>Models/Live2D/Haru/runtime/*.model3.json</c>. Defaults to the VRM
    /// (<c>Models/UserAvatar.vrm</c>). Returns the lower-cased kind and the model name, or null when
    /// no name was given (use the default model).
    /// </summary>
    private static (string kind, string? name) ResolveAvatar()
        => ParseAvatarSpec(ResolveArg("--avatar", "AVATAR_MODEL") ?? "vrm");

    /// <summary>Parses a <c>&lt;kind&gt;[:&lt;name&gt;]</c> avatar spec into a lower-cased kind and an
    /// optional (case-preserved) model name.</summary>
    private static (string kind, string? name) ParseAvatarSpec(string spec)
    {
        spec = spec.Trim();
        int colon = spec.IndexOf(':');
        string kind = (colon >= 0 ? spec.Substring(0, colon) : spec).Trim().ToLowerInvariant();
        string? name = colon >= 0 ? spec.Substring(colon + 1).Trim() : null;
        if (string.IsNullOrEmpty(name))
        {
            name = null;
        }
        return (kind, name);
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
                else if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url?.AbsolutePath == "/avatars")
                {
                    await WriteResponse(ctx, _avatarsJson, "application/json").ConfigureAwait(false);
                }
                else if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/avatar")
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    string spec = (await reader.ReadToEndAsync().ConfigureAwait(false)).Trim();
                    if (!string.IsNullOrWhiteSpace(spec))
                    {
                        _pendingAvatarSwitch = spec;   // applied on the main thread in _Process.
                        await WriteResponse(ctx, "ok", "text/plain").ConfigureAwait(false);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 400;
                        await WriteResponse(ctx, "missing avatar spec", "text/plain").ConfigureAwait(false);
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
  <select id='avatarSel'></select>
  <button onclick='switchAvatar()'>Switch avatar</button>
</div>
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
async function loadAvatars() {
  try {
    const list = await (await fetch('/avatars')).json();
    const sel = document.getElementById('avatarSel');
    sel.innerHTML = '';
    list.forEach(s => { const o = document.createElement('option'); o.value = s; o.textContent = s; sel.appendChild(o); });
  } catch (e) {}
}
async function switchAvatar() {
  const s = document.getElementById('avatarSel').value;
  if (s) {
    await fetch('/avatar', { method: 'POST', body: s });
    document.getElementById('status').textContent = 'switched to ' + s;
  }
}
loadAvatars();
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
