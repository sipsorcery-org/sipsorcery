using Godot;
using System;

// The 3D VRM avatar: a glTF/VRM humanoid rendered with a Camera3D + lights inside the capture
// viewport, posed into a natural A-pose, with blink/breath/head-sway liveness and mouth driven by
// the VRoid Fcl_MTH_* morph targets. (Extracted from the original single-file AvatarStreamer.)
public sealed class VrmAvatarModel : IAvatarModel
{
    private const string DefaultModelPath = "res://Models/UserAvatar.vrm";

    private readonly string _modelPath;

    /// <summary>
    /// Creates the VRM avatar. <paramref name="modelPath"/> is the <c>res://</c> path of the .vrm
    /// (an imported scene); defaults to <c>Models/UserAvatar.vrm</c>.
    /// </summary>
    public VrmAvatarModel(string modelPath = DefaultModelPath)
    {
        _modelPath = modelPath;
    }

    private Node3D _avatar = null!;
    private MeshInstance3D? _headMesh;

    private readonly Random _random = new();
    private double _nextBlink = 1.4;
    private double _blinkStarted = -1;

    public void Build(SubViewport viewport)
    {
        // Head-and-shoulders framing: aim at head height and use a narrow (telephoto) FOV so the
        // crop is tight without the perspective distortion of moving the camera in close.
        var camera = new Camera3D
        {
            Position = new Vector3(0f, 0.5f, 4.2f),
            Fov = 10,
            Current = true,
        };
        viewport.AddChild(camera);

        var key = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28, -30, 0),
            LightEnergy = 1.35f,
            ShadowEnabled = true,
        };
        viewport.AddChild(key);

        var fill = new OmniLight3D
        {
            Position = new Vector3(-2.5f, 1.8f, 3.5f),
            LightColor = new Color("7dd3fc"),
            LightEnergy = 5.5f,
            OmniRange = 8,
        };
        viewport.AddChild(fill);

        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("09111a"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("a9c2d6"),
                AmbientLightEnergy = 0.42f,
            },
        };
        viewport.AddChild(environment);

        var packed = GD.Load<PackedScene>(_modelPath);
        if (packed == null)
        {
            GD.PushError($"VRM model could not be loaded from {_modelPath}.");
            return;
        }

        _avatar = packed.Instantiate<Node3D>();
        _avatar.Name = "Avatar";
        _avatar.Position = new Vector3(0f, -0.8f, 0f);
        _avatar.RotationDegrees = new Vector3(0, 180, 0);
        _avatar.GetNodeOrNull("VRMSpringBoneController")?.Free();
        viewport.AddChild(_avatar);

        _headMesh = _avatar.GetNodeOrNull<MeshInstance3D>("Skeleton3D/Face");
        if (_headMesh == null)
        {
            GD.PushWarning("VRM facial mesh 'Skeleton3D/Face' not found; morph-driven expressions disabled.");
        }

        PoseRestArms();

        // Some VRM scenes ship their own Camera3D that becomes current when instantiated; make sure
        // our head-and-shoulders camera is the one the viewport renders.
        camera.MakeCurrent();
    }

    public void Update(double delta, double time, float mouthOpen)
    {
        if (_avatar == null)
        {
            return;
        }
        UpdateBlink(time);
        UpdatePose(delta, time);
        UpdateMouth(time, mouthOpen);
    }

    /// <summary>
    /// VRM models load in a T-pose. Measured from the rest skeleton: both upper arms lie in the
    /// skeleton X-Y plane (left points -X, right +X), so swinging them down is a rotation about the
    /// skeleton Z axis. Rotating just the upper arm carries the forearm+hand with it (A-pose).
    /// </summary>
    private void PoseRestArms()
    {
        var skeleton = _avatar.GetNodeOrNull<Skeleton3D>("Skeleton3D");
        if (skeleton == null)
        {
            return;
        }

        int leftArm = ResolveUpperArmBone(skeleton, left: true);
        int rightArm = ResolveUpperArmBone(skeleton, left: false);
        if (leftArm < 0 || rightArm < 0)
        {
            GD.PushWarning(
                $"VRM upper-arm bones not found (left={leftArm}, right={rightArm}); the avatar will " +
                "keep its imported pose (typically a T-pose). The model uses an unrecognised bone " +
                "naming convention - extend ResolveUpperArmBone in VrmAvatarModel.cs.");
            return;
        }

        float swing = Mathf.DegToRad(62);
        RotateBoneInSkeletonSpace(skeleton, leftArm, Vector3.Back, swing);    // Back = +Z
        RotateBoneInSkeletonSpace(skeleton, rightArm, Vector3.Back, -swing);
    }

    /// <summary>
    /// Resolves the left/right upper-arm bone index across VRM exporters, which do not agree on
    /// humanoid bone names: Godot/UniVRM use "LeftUpperArm", VRoid uses "J_Bip_L_UpperArm", Blender
    /// uses "upper_arm.L", etc. Tries the common spellings, then falls back to a side-aware pattern
    /// match on the skeleton's bone list. Returns -1 if no upper-arm bone can be identified.
    /// </summary>
    private static int ResolveUpperArmBone(Skeleton3D skeleton, bool left)
    {
        string[] candidates = left
            ? new[] { "LeftUpperArm", "J_Bip_L_UpperArm", "UpperArm.L", "upper_arm.L", "LeftArm", "Left arm" }
            : new[] { "RightUpperArm", "J_Bip_R_UpperArm", "UpperArm.R", "upper_arm.R", "RightArm", "Right arm" };
        foreach (var name in candidates)
        {
            int i = skeleton.FindBone(name);
            if (i >= 0)
            {
                return i;
            }
        }

        for (int i = 0; i < skeleton.GetBoneCount(); i++)
        {
            string nm = skeleton.GetBoneName(i).ToLowerInvariant();
            if (!nm.Contains("upperarm") && !nm.Contains("upper_arm"))
            {
                continue;
            }
            bool isLeft = nm.Contains("left") || nm.Contains("_l_") || nm.EndsWith("_l") || nm.EndsWith(".l");
            bool isRight = nm.Contains("right") || nm.Contains("_r_") || nm.EndsWith("_r") || nm.EndsWith(".r");
            if (left && isLeft && !isRight)
            {
                return i;
            }
            if (!left && isRight && !isLeft)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Rotate a bone by <paramref name="radians"/> about <paramref name="axis"/> (skeleton space),
    /// on top of its current pose. The pose is stored relative to the parent bone, so the rotation
    /// is conjugated into the parent's frame - this makes the axis behave predictably regardless of
    /// the rig's baked bone roll.
    /// </summary>
    private static void RotateBoneInSkeletonSpace(Skeleton3D skeleton, int idx, Vector3 axis, float radians)
    {
        if (idx < 0)
        {
            return;
        }

        int parent = skeleton.GetBoneParent(idx);
        Basis parentGlobal = parent >= 0 ? skeleton.GetBoneGlobalRest(parent).Basis : Basis.Identity;
        Basis rSkeleton = new Basis(axis.Normalized(), radians);
        Basis rLocal = parentGlobal.Inverse() * rSkeleton * parentGlobal;

        Quaternion pose = skeleton.GetBonePoseRotation(idx);
        skeleton.SetBonePoseRotation(idx, (rLocal * new Basis(pose)).GetRotationQuaternion());
    }

    private void UpdateBlink(double time)
    {
        if (_blinkStarted < 0 && time >= _nextBlink)
        {
            _blinkStarted = time;
        }

        var openness = 1f;
        if (_blinkStarted >= 0)
        {
            var phase = (float)((time - _blinkStarted) / 0.16);
            openness = phase < 0.5f ? 1f - phase * 2f : (phase - 0.5f) * 2f;
            if (phase >= 1)
            {
                _blinkStarted = -1;
                _nextBlink = time + 2.1 + _random.NextDouble() * 3.4;
                openness = 1;
            }
        }

        SetBlend("Fcl_EYE_Close_L", 1f - openness);
        SetBlend("Fcl_EYE_Close_R", 1f - openness);
    }

    private void UpdatePose(double delta, double time)
    {
        var targetRotation = new Vector3(
            (float)Math.Sin(time * 0.63) * 0.035f,
            (float)Math.Sin(time * 0.41) * 0.075f,
            (float)Math.Sin(time * 0.53) * 0.025f);
        var baseRotation = new Vector3(0, Mathf.Pi, 0);
        _avatar.Rotation = _avatar.Rotation.Lerp(baseRotation + targetRotation, (float)Math.Min(1, delta * 2.5));

        var breath = 1f + (float)Math.Sin(time * 1.7) * 0.012f;
        _avatar.Scale = new Vector3(1, breath, 1);
    }

    private void UpdateMouth(double time, float mouthOpen)
    {
        var vowelPhase = (float)((time * 3.2) % 5.0);
        SetBlend("Fcl_MTH_A", mouthOpen * TriangleWeight(vowelPhase, 0));
        SetBlend("Fcl_MTH_I", mouthOpen * TriangleWeight(vowelPhase, 1));
        SetBlend("Fcl_MTH_U", mouthOpen * TriangleWeight(vowelPhase, 2));
        SetBlend("Fcl_MTH_E", mouthOpen * TriangleWeight(vowelPhase, 3));
        SetBlend("Fcl_MTH_O", mouthOpen * TriangleWeight(vowelPhase, 4));
    }

    private static float TriangleWeight(float phase, float center)
    {
        var distance = Math.Abs(phase - center);
        distance = Math.Min(distance, 5f - distance);
        return Mathf.Clamp(1f - distance, 0, 1);
    }

    private void SetBlend(string blendName, float value)
    {
        _headMesh?.Set($"blend_shapes/{blendName}", Mathf.Clamp(value, 0, 1));
    }
}
