using Godot;
using System;
using System.Collections.Generic;

// The 2D Live2D (Cubism) avatar, rendered by the gd_cubism GDExtension. The moc3 model is loaded
// as a Node2D into the capture viewport; liveness (blink/gaze/head-sway/breath) and lip-sync are
// driven onto the standard Cubism parameters.
//
// Crucially, the parameters are written from the model's cubism_process effect signal - which fires
// AFTER the idle motion updates the model each cycle - so our values override the motion instead of
// being overwritten by it. Setting them from _Process (before the motion) leaves the mouth shut.
/// <summary>
/// Per-model Live2D tuning. Most Cubism models work with the defaults; models needing overrides get
/// an entry in the registry in <c>AvatarStreamer</c> (keyed by their <c>Models/Live2D/&lt;name&gt;</c>
/// folder). Extend with framing/mouth-param fields as more models are added.
/// </summary>
public sealed class Live2DAvatarConfig
{
    /// <summary>
    /// Order drawables by static draw order instead of the dynamic render order. Render order is
    /// correct for most models; this remains available for models that need static ordering.
    /// </summary>
    public bool UseDrawOrder { get; init; }

    /// <summary>
    /// Optional drawable set for face details that gd_cubism otherwise places behind the face.
    /// These are restored above the regular model layers before mouth layers are applied.
    /// </summary>
    public string[] ForegroundDrawableIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional drawable sets for models whose closed/open mouth layers are not ordered correctly
    /// by gd_cubism. The sets are cross-faded from <c>mouthOpen</c> after gd_cubism renders them.
    /// </summary>
    public string[] ClosedMouthDrawableIds { get; init; } = Array.Empty<string>();
    public string[] OpenMouthDrawableIds { get; init; } = Array.Empty<string>();
}

public sealed class Live2DAvatarModel : IAvatarModel, IAvatarMotionController
{
    private const string DefaultModelPath = "res://Models/Live2D/Ren/runtime/ren.model3.json";
    private const float FramingTransitionSeconds = 0.5f;

    private readonly string _modelPath;
    private readonly Live2DAvatarConfig _config;

    /// <summary>
    /// Creates the Live2D avatar. <paramref name="modelPath"/> is the <c>res://</c> path of the
    /// Cubism <c>*.model3.json</c>; defaults to the bundled Ren model. <paramref name="config"/>
    /// carries per-model tuning (for example, drawable ordering or mouth-layer overrides).
    /// </summary>
    public Live2DAvatarModel(string modelPath = DefaultModelPath, Live2DAvatarConfig? config = null)
    {
        _modelPath = modelPath;
        _config = config ?? new Live2DAvatarConfig();
    }

    private Node2D? _model;
    private SubViewport _viewport = null!;
    private readonly Dictionary<string, GodotObject> _parameters = new(StringComparer.Ordinal);
    // Cache of driven-name -> resolved parameter (or null), spanning modern/legacy id conventions.
    private readonly Dictionary<string, GodotObject?> _resolved = new(StringComparer.Ordinal);
    private readonly List<MeshInstance2D> _foregroundMeshes = new();
    private readonly List<MeshInstance2D> _closedMouthMeshes = new();
    private readonly List<MeshInstance2D> _openMouthMeshes = new();
    // The model's lip-sync parameter id(s), read from its model3.json LipSync group.
    private string[] _lipSyncIds = { "ParamMouthOpenY" };
    private AvatarMotionInfo[] _motions = Array.Empty<AvatarMotionInfo>();

    private double _blinkClock;
    private double _nextBlink = 2.0;
    private bool _ready;
    private bool _motionPlaying;
    private bool _motionLooping;
    private bool _returnToIdle;
    private bool _fullBodyRequested;
    private float _fullBodyBlend;

    // Latest liveness values, computed in Update() and applied in the cubism_process effect.
    private float _eyeOpen = 1f, _gazeX, _gazeY, _angleX, _angleY, _angleZ, _breath, _mouthOpen;

    public void Build(SubViewport viewport)
    {
        _viewport = viewport;

        // Opaque background: the Cubism model is 2D with transparency, and the frame readback drops
        // alpha, so without this the stream background would be undefined.
        var background = new ColorRect
        {
            Color = new Color("09111a"),
            Position = Vector2.Zero,
            Size = new Vector2(viewport.Size.X, viewport.Size.Y),
        };
        viewport.AddChild(background);

        if (!ClassDB.ClassExists("GDCubismUserModel"))
        {
            GD.PushError("gd_cubism extension not loaded (GDCubismUserModel missing). Enable it in project.godot.");
            return;
        }

        if (ClassDB.Instantiate("GDCubismUserModel").AsGodotObject() is not Node2D model)
        {
            GD.PushError("GDCubismUserModel could not be instantiated as a Node2D.");
            return;
        }

        _model = model;
        _model.Name = "RenLive2D";
        _model.Connect("motion_finished", Callable.From(OnMotionFinished));
        // A lower priority makes gd_cubism update first. Models with a drawable-layer override can
        // then adjust those layers in this model's Update(), after the extension resets their Z.
        if (_config.ForegroundDrawableIds.Length > 0 ||
            _config.ClosedMouthDrawableIds.Length > 0 ||
            _config.OpenMouthDrawableIds.Length > 0)
        {
            _model.ProcessPriority = -1;
        }
        viewport.AddChild(_model);

        // Attach the custom effect BEFORE loading assets: gd_cubism gathers its effect children
        // when the model loads, so an effect added afterwards never gets its signals invoked.
        // Drive all parameters from cubism_process. It runs after motions/expressions have updated
        // their values but before gd_cubism copies parameter resources into the Cubism model and
        // calculates the drawable vertices, so our mouth value affects the frame being rendered.
        if (ClassDB.ClassExists("GDCubismEffectCustom") &&
            ClassDB.Instantiate("GDCubismEffectCustom").AsGodotObject() is Node effect)
        {
            _model.AddChild(effect);
            effect.Connect("cubism_process", Callable.From((Node2D _m, double _d) => ApplyParameters()));
        }
        else
        {
            GD.PushWarning("GDCubismEffectCustom unavailable; lip-sync may be overridden by the idle motion.");
        }

        // Order drawables by static draw order instead of the dynamic render order for models that
        // need it (some older Cubism samples). Set before loading so the initial build uses it.
        _model.Call("set_use_draw_order", _config.UseDrawOrder);
        _model.Call("set_load_expressions", true);
        _model.Call("set_load_motions", true);
        _model.Call("set_physics_evaluate", true);
        _model.Call("set_process_callback", 1);
        _model.Call("set_assets", _modelPath);

        CacheParameters();
        CacheMouthMeshes();
        _lipSyncIds = LoadLipSyncIds(_modelPath);
        _motions = LoadMotions(_modelPath);
        // Motions are loaded but not started automatically. The viewer can explicitly play one;
        // procedural liveness fills the gaps while no authored motion is active.

        _ready = true;
        GD.Print($"Live2D model loaded from {_modelPath}. Parameters: {_parameters.Count}, motions: {_motions.Length}");
    }

    public void Update(double delta, double time, float mouthOpen)
    {
        if (!_ready || _model == null)
        {
            return;
        }

        UpdateFraming(delta);

        _gazeX = Mathf.Sin((float)time * 0.72f) * 0.35f;
        _gazeY = Mathf.Sin((float)time * 0.51f + 0.7f) * 0.18f;
        _angleX = Mathf.Sin((float)time * 0.55f) * 9.0f;
        _angleY = Mathf.Sin((float)time * 0.43f + 1.1f) * 5.0f;
        _angleZ = Mathf.Sin((float)time * 0.36f + 0.2f) * 4.0f;
        _breath = 0.5f + 0.5f * Mathf.Sin((float)time * 1.8f);
        _mouthOpen = mouthOpen;

        _blinkClock += delta;
        _eyeOpen = 1.0f;
        if (_blinkClock >= _nextBlink)
        {
            var blinkT = (float)((_blinkClock - _nextBlink) / 0.18);
            _eyeOpen = blinkT < 1.0f ? Mathf.Abs(blinkT * 2.0f - 1.0f) : 1.0f;
            if (blinkT >= 1.0f)
            {
                _blinkClock = 0.0;
                _nextBlink = 1.8 + GD.Randf() * 2.8;
            }
        }

        ApplyDrawableOverrides();
    }

    // Runs after motions/expressions and before the model updates its drawable vertices via the
    // cubism_process signal, so these values override animation and affect the current frame.
    private void ApplyParameters()
    {
        // Drive the model's declared lip-sync parameter(s) with the current mouth-open amount. Which
        // parameter that is varies per model - ParamMouthOpenY, PARAM_MOUTH_OPEN_Y (legacy), or a
        // vowel like ParamA - so it is read from the model3.json LipSync group (see LoadLipSyncIds).
        foreach (var id in _lipSyncIds)
        {
            SetParameter(id, _mouthOpen);
        }
        // Let authored motions own expression, eyes, head, body and breath. TTS retains the
        // declared lip-sync parameters so speech remains synchronised while a gesture plays.
        if (_motionPlaying)
        {
            return;
        }
        // ParamMouthForm shapes the mouth (pucker/smile) and is NOT part of lip-sync; a negative form
        // can leave the mouth parted at rest. Keep it neutral so the mouth fully closes when idle.
        SetParameter("ParamMouthForm", 0f);
        SetParameter("ParamEyeLOpen", _eyeOpen);
        SetParameter("ParamEyeROpen", _eyeOpen);
        SetParameter("ParamEyeBallX", _gazeX);
        SetParameter("ParamEyeBallY", _gazeY);
        SetParameter("ParamAngleX", _angleX);
        SetParameter("ParamAngleY", _angleY);
        SetParameter("ParamAngleZ", _angleZ);
        SetParameter("ParamBodyAngleX", _angleX * 0.35f);
        SetParameter("ParamBodyAngleZ", _angleZ * 0.5f);
        SetParameter("ParamBreath", _breath);
    }

    public IReadOnlyList<AvatarMotionInfo> Motions => _motions;

    public bool PlayMotion(string group, int index, bool loop)
    {
        if (!_ready || _model == null || !HasMotion(group, index))
        {
            return false;
        }

        bool defaultIdle = IsDefaultIdle(group, index);
        _motionPlaying = true;
        _motionLooping = loop;
        _returnToIdle = !defaultIdle;
        _fullBodyRequested = !defaultIdle;
        GD.Print(defaultIdle
            ? $"Playing Live2D default idle motion '{group}'[{index}]."
            : $"Playing Live2D motion '{group}'[{index}] with full-body framing.");
        if (loop)
        {
            _model.Call("start_motion_loop", group, index, 3, true, true);
        }
        else
        {
            _model.Call("start_motion", group, index, 3);
        }
        return true;
    }

    public void StopMotion(bool returnToIdle = true)
    {
        bool startIdle = returnToIdle && _returnToIdle;
        _motionPlaying = false;
        _motionLooping = false;
        _returnToIdle = false;
        _fullBodyRequested = false;
        if (_model != null)
        {
            _model.Call("stop_motion");
        }
        if (startIdle)
        {
            StartDefaultIdle();
        }
    }

    private bool HasMotion(string group, int index)
    {
        foreach (var motion in _motions)
        {
            if (motion.Group == group && motion.Index == index)
            {
                return true;
            }
        }
        return false;
    }

    private void OnMotionFinished()
    {
        if (_motionLooping)
        {
            return;
        }

        if (_returnToIdle && StartDefaultIdle())
        {
            return;
        }

        _motionPlaying = false;
        _returnToIdle = false;
        _fullBodyRequested = false;
    }

    private static bool IsDefaultIdle(string group, int index) =>
        index == 0 && group.Equals("Idle", StringComparison.OrdinalIgnoreCase);

    private bool StartDefaultIdle()
    {
        if (_model == null)
        {
            return false;
        }
        foreach (var motion in _motions)
        {
            if (!IsDefaultIdle(motion.Group, motion.Index))
            {
                continue;
            }

            _motionPlaying = true;
            _motionLooping = true;
            _returnToIdle = false;
            _fullBodyRequested = false;
            GD.Print($"Returning to Live2D default idle '{motion.Group}'[{motion.Index}] and portrait framing.");
            _model.Call("start_motion_loop", motion.Group, motion.Index, 3, true, true);
            return true;
        }
        return false;
    }

    private void CacheMouthMeshes()
    {
        _foregroundMeshes.Clear();
        _closedMouthMeshes.Clear();
        _openMouthMeshes.Clear();
        if (_model == null)
        {
            return;
        }

        CacheDrawableSet(_config.ForegroundDrawableIds, _foregroundMeshes);
        CacheDrawableSet(_config.ClosedMouthDrawableIds, _closedMouthMeshes);
        CacheDrawableSet(_config.OpenMouthDrawableIds, _openMouthMeshes);
    }

    private void CacheDrawableSet(string[] ids, List<MeshInstance2D> target)
    {
        foreach (var id in ids)
        {
            var mesh = _model?.GetNodeOrNull<MeshInstance2D>(id);
            if (mesh != null)
            {
                target.Add(mesh);
            }
            else
            {
                GD.PushWarning($"Live2D mouth drawable '{id}' was not found in {_modelPath}.");
            }
        }
    }

    private void ApplyDrawableOverrides()
    {
        float open = Mathf.Clamp(_mouthOpen, 0f, 1f);
        var closedModulate = new Color(1f, 1f, 1f, 1f - open);
        var openModulate = new Color(1f, 1f, 1f, open);
        foreach (var mesh in _foregroundMeshes)
        {
            mesh.ZIndex = 1000;
        }
        foreach (var mesh in _closedMouthMeshes)
        {
            mesh.ZIndex = 1001;
            mesh.SelfModulate = closedModulate;
        }
        foreach (var mesh in _openMouthMeshes)
        {
            mesh.ZIndex = 1002;
            mesh.SelfModulate = openModulate;
        }
    }

    /// <summary>Reads the authored motion catalogue and durations from the model3/motion3 files.</summary>
    private static AvatarMotionInfo[] LoadMotions(string modelPath)
    {
        using var file = FileAccess.Open(modelPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return Array.Empty<AvatarMotionInfo>();
        }

        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok)
        {
            return Array.Empty<AvatarMotionInfo>();
        }

        var root = json.Data.AsGodotDictionary();
        if (!root.TryGetValue("FileReferences", out var refsVar))
        {
            return Array.Empty<AvatarMotionInfo>();
        }
        var references = refsVar.AsGodotDictionary();
        if (!references.TryGetValue("Motions", out var motionsVar))
        {
            return Array.Empty<AvatarMotionInfo>();
        }

        var result = new List<AvatarMotionInfo>();
        var groups = motionsVar.AsGodotDictionary();
        string baseDir = modelPath.Substring(0, modelPath.LastIndexOf('/'));
        foreach (var groupKey in groups.Keys)
        {
            string group = groupKey.AsString();
            var entries = groups[groupKey].AsGodotArray();
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index].AsGodotDictionary();
                if (!entry.TryGetValue("File", out var pathVar))
                {
                    continue;
                }
                string relativePath = pathVar.AsString();
                string motionPath = $"{baseDir}/{relativePath}";
                result.Add(new AvatarMotionInfo(
                    group,
                    index,
                    MotionDisplayName(relativePath),
                    LoadMotionDuration(motionPath)));
            }
        }
        return result.ToArray();
    }

    private static string MotionDisplayName(string relativePath)
    {
        int slash = Math.Max(relativePath.LastIndexOf('/'), relativePath.LastIndexOf('\\'));
        string name = slash >= 0 ? relativePath.Substring(slash + 1) : relativePath;
        const string suffix = ".motion3.json";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name.Substring(0, name.Length - suffix.Length)
            : name;
    }

    private static double LoadMotionDuration(string motionPath)
    {
        using var file = FileAccess.Open(motionPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return 0;
        }
        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok)
        {
            return 0;
        }
        var root = json.Data.AsGodotDictionary();
        if (!root.TryGetValue("Meta", out var metaVar))
        {
            return 0;
        }
        var meta = metaVar.AsGodotDictionary();
        return meta.TryGetValue("Duration", out var durationVar) ? durationVar.AsDouble() : 0;
    }

    /// <summary>
    /// Reads the model's lip-sync parameter id(s) from its <c>model3.json</c> <c>Groups</c> (the
    /// group named "LipSync"). Models differ - <c>ParamMouthOpenY</c>, <c>PARAM_MOUTH_OPEN_Y</c>
    /// (legacy), <c>ParamA</c> (vowel), etc. - so this is the reliable source. Falls back to
    /// <c>ParamMouthOpenY</c> when the file has no LipSync group.
    /// </summary>
    private static string[] LoadLipSyncIds(string modelPath)
    {
        string[] fallback = { "ParamMouthOpenY" };
        using var file = FileAccess.Open(modelPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return fallback;
        }
        var json = new Json();
        if (json.Parse(file.GetAsText()) != Error.Ok)
        {
            return fallback;
        }
        var root = json.Data.AsGodotDictionary();
        if (root.TryGetValue("Groups", out var groupsVar))
        {
            foreach (var groupVar in groupsVar.AsGodotArray())
            {
                var group = groupVar.AsGodotDictionary();
                if (group.TryGetValue("Name", out var nameVar) && nameVar.AsString() == "LipSync" &&
                    group.TryGetValue("Ids", out var idsVar))
                {
                    var ids = idsVar.AsGodotArray();
                    if (ids.Count > 0)
                    {
                        var result = new string[ids.Count];
                        for (int i = 0; i < ids.Count; i++)
                        {
                            result[i] = ids[i].AsString();
                        }
                        return result;
                    }
                }
            }
        }
        return fallback;
    }

    private void CacheParameters()
    {
        _parameters.Clear();
        _resolved.Clear();
        if (_model == null)
        {
            return;
        }

        var parameters = (Godot.Collections.Array<GodotObject>)_model.Call("get_parameters");
        foreach (var parameter in parameters)
        {
            _parameters[(string)parameter.Call("get_id")] = parameter;
        }
    }

    /// <summary>
    /// Smoothly blends between the normal head-and-shoulders crop and a full canvas fit while an
    /// authored non-idle motion is active.
    /// </summary>
    private void UpdateFraming(double delta)
    {
        if (_model == null)
        {
            return;
        }

        var canvasInfo = (Godot.Collections.Dictionary)_model.Call("get_canvas_info");
        if (canvasInfo.Count == 0)
        {
            return;
        }

        var sizeInPixels = (Vector2)canvasInfo["size_in_pixels"];
        var originInPixels = (Vector2)canvasInfo["origin_in_pixels"];
        var extent = Math.Max(sizeInPixels.X, sizeInPixels.Y);
        if (sizeInPixels.X <= 0 || sizeInPixels.Y <= 0 || extent <= 0)
        {
            return;
        }

        // Scale well past a full-body fit and drop the origin below the frame so the head and
        // shoulders fill the view (tuned against the Ren model).
        float portraitScale = _viewport.Size.Y * 2.75f / (float)extent;
        var portraitPosition = new Vector2(_viewport.Size.X * 0.5f, _viewport.Size.Y * 1.32f);

        // gd_cubism's model rectangle starts at (origin - size). Fit that complete rectangle into
        // 92% of the viewport and offset the model origin so the rectangle is centred.
        float fullBodyScale = Math.Min(
            _viewport.Size.X * 0.92f / sizeInPixels.X,
            _viewport.Size.Y * 0.92f / sizeInPixels.Y);
        var canvasCentreFromModelOrigin = originInPixels - sizeInPixels * 0.5f;
        var viewportCentre = new Vector2(_viewport.Size.X * 0.5f, _viewport.Size.Y * 0.5f);
        var fullBodyPosition = viewportCentre - canvasCentreFromModelOrigin * fullBodyScale;

        float target = _fullBodyRequested ? 1f : 0f;
        _fullBodyBlend = Mathf.MoveToward(
            _fullBodyBlend,
            target,
            (float)(delta / FramingTransitionSeconds));
        float smoothBlend = _fullBodyBlend * _fullBodyBlend * (3f - 2f * _fullBodyBlend);
        float scale = Mathf.Lerp(portraitScale, fullBodyScale, smoothBlend);
        _model.Position = portraitPosition.Lerp(fullBodyPosition, smoothBlend);
        _model.Scale = new Vector2(scale, scale);
    }

    private void SetParameter(string id, float value)
    {
        if (!_resolved.TryGetValue(id, out var parameter))
        {
            // Models use either modern (ParamMouthOpenY) or legacy Cubism 2.x (PARAM_MOUTH_OPEN_Y)
            // parameter ids; resolve the driven name against both conventions and cache the result.
            parameter = _parameters.GetValueOrDefault(id) ?? _parameters.GetValueOrDefault(ToLegacyParamId(id));
            _resolved[id] = parameter;
        }
        parameter?.Call("set_value", value);
    }

    /// <summary>
    /// Converts a modern Cubism parameter id to the legacy 2.x form, e.g. <c>ParamMouthOpenY</c> ->
    /// <c>PARAM_MOUTH_OPEN_Y</c> (insert an underscore before each interior capital, then upper-case).
    /// </summary>
    private static string ToLegacyParamId(string modern)
    {
        var sb = new System.Text.StringBuilder(modern.Length + 8);
        for (int i = 0; i < modern.Length; i++)
        {
            char c = modern[i];
            if (i > 0 && char.IsUpper(c))
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
