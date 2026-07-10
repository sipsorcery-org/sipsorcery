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
public sealed class Live2DAvatarModel : IAvatarModel
{
    private const string ModelPath = "res://Models/Live2D/Ren/runtime/ren.model3.json";

    private Node2D? _model;
    private SubViewport _viewport = null!;
    private readonly Dictionary<string, GodotObject> _parameters = new(StringComparer.Ordinal);

    private double _blinkClock;
    private double _nextBlink = 2.0;
    private bool _ready;

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
        viewport.AddChild(_model);

        // Attach the custom effect BEFORE loading assets: gd_cubism gathers its effect children
        // when the model loads, so an effect added afterwards never gets its signals invoked.
        // Drive all parameters from its cubism_epilogue signal, which runs after the idle motion
        // updates the model, so our values (the mouth in particular) override the motion.
        if (ClassDB.ClassExists("GDCubismEffectCustom") &&
            ClassDB.Instantiate("GDCubismEffectCustom").AsGodotObject() is Node effect)
        {
            _model.AddChild(effect);
            effect.Connect("cubism_epilogue", Callable.From((Node2D _m, double _d) => ApplyParameters()));
        }
        else
        {
            GD.PushWarning("GDCubismEffectCustom unavailable; lip-sync may be overridden by the idle motion.");
        }

        _model.Call("set_load_expressions", true);
        _model.Call("set_load_motions", true);
        _model.Call("set_physics_evaluate", true);
        _model.Call("set_process_callback", 1);
        _model.Call("set_assets", ModelPath);

        CacheParameters();
        // No idle motion: it animates ParamMouthOpenY and blends against our lip-sync each frame,
        // which suppresses the mouth. We drive blink/gaze/sway/breath ourselves (plus physics), so
        // the authored idle motion is both redundant and harmful here.

        _ready = true;
        GD.Print($"Live2D model loaded from {ModelPath}. Parameters: {_parameters.Count}");
    }

    public void Update(double delta, double time, float mouthOpen)
    {
        if (!_ready || _model == null)
        {
            return;
        }

        Place();

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
    }

    // Runs inside the model's update cycle (after the idle motion) via the cubism_epilogue signal,
    // so these values (the mouth especially) override the motion instead of being overwritten.
    private void ApplyParameters()
    {
        SetParameter("ParamMouthOpenY", _mouthOpen);
        // ParamMouthForm shapes the mouth (pucker/smile) and is NOT part of lip-sync; on this model
        // a negative form leaves the mouth parted at rest. Keep it neutral so the mouth fully closes
        // when idle - the LipSync parameter (ParamMouthOpenY) carries all the talking.
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

    private void CacheParameters()
    {
        _parameters.Clear();
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

    /// <summary>Zoom the framing onto the model's head and shoulders (a portrait crop).</summary>
    private void Place()
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
        var extent = Math.Max(sizeInPixels.X, sizeInPixels.Y);
        if (extent <= 0)
        {
            return;
        }

        // Scale well past a full-body fit and drop the origin below the frame so the head and
        // shoulders fill the view (tuned against the Ren model).
        var scale = _viewport.Size.Y * 2.75f / extent;
        _model.Position = new Vector2(_viewport.Size.X * 0.5f, _viewport.Size.Y * 1.32f);
        _model.Scale = new Vector2(scale, scale);
    }

    private void SetParameter(string id, float value)
    {
        if (_parameters.TryGetValue(id, out var parameter))
        {
            parameter.Call("set_value", value);
        }
    }
}
