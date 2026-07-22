using Godot;
using Engine = twodog.Engine;
using System.Collections.Generic;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Arguments before a second `--` are Godot engine arguments. Arguments after it are
        // user arguments exposed to AvatarStreamer through OS.GetCmdlineUserArgs(). If no
        // second separator is supplied, treat all arguments as user arguments for convenience.
        var separator = Array.IndexOf(args, "--");
        var godotArgs = new List<string>();
        if (separator >= 0)
        {
            godotArgs.AddRange(args[..separator]);
            godotArgs.Add("--");
            godotArgs.AddRange(args[(separator + 1)..]);
        }
        else if (args.Length > 0)
        {
            godotArgs.Add("--");
            godotArgs.AddRange(args);
        }

        using var engine = new Engine(
            "WebRTCGodotAvatar",
            Engine.ResolveProjectDir(),
            godotArgs.ToArray());
        using var godot = engine.Start();

        GD.Print("2dog is running the WebRTC Godot avatar.");
        while (!godot.Iteration())
        {
            // AvatarStreamer owns the WebRTC, speech and frame-encoding work.
        }
    }
}
