using System.Collections.Generic;

/// <summary>One authored motion exposed by an avatar model.</summary>
public sealed record AvatarMotionInfo(
    string Group,
    int Index,
    string Name,
    double DurationSeconds);

/// <summary>
/// Optional capability for avatar implementations that contain authored motions. Commands are
/// invoked on Godot's main thread by <see cref="AvatarStreamer"/>.
/// </summary>
public interface IAvatarMotionController
{
    IReadOnlyList<AvatarMotionInfo> Motions { get; }

    bool PlayMotion(string group, int index, bool loop);

    void StopMotion(bool returnToIdle = true);
}
