using Godot;

// A pluggable avatar: it builds itself (and whatever 3D world or 2D background it needs) into the
// capture SubViewport, and each frame animates its liveness and lip-sync. AvatarStreamer owns the
// viewport, the speech pipeline and the WebRTC path; the model only knows how to render + emote.
// Implementations: VrmAvatarModel (3D glTF/VRM) and Live2DAvatarModel (Cubism via gd_cubism).
public interface IAvatarModel
{
    /// <summary>Construct the avatar into <paramref name="viewport"/> (the fixed-size render target).</summary>
    void Build(SubViewport viewport);

    /// <summary>Per-frame liveness (blink/gaze/breath/sway) plus lip-sync from <paramref name="mouthOpen"/> (0..1).</summary>
    void Update(double delta, double time, float mouthOpen);
}
