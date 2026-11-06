using System.Text.Json;

namespace SIPSorcery.Softphone;

public sealed class AccountSettings
{
    public string Server { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? DisplayName { get; set; }
}

/// <summary>
/// The prototype's whole configuration: the SIP port and an optional account to register with.
/// Read from <c>softphone.json</c> (path given with <c>--config</c>, otherwise next to the exe).
/// There's no settings UI yet, and the password is stored in plain text; the real app will
/// keep accounts in its store with the password protected by DPAPI.
/// </summary>
public sealed class Settings
{
    public int SipPort { get; set; } = 5060;
    public AccountSettings? Account { get; set; }

    /// <summary>A separate WebView2 profile, so two copies can run side by side for testing.</summary>
    public string Profile { get; set; } = "default";

    /// <summary>Chromium's fake camera and microphone (a test pattern and a beep), for testing.</summary>
    public bool FakeMedia { get; set; }

    /// <summary>Testing: call this address as soon as the page is up.</summary>
    public string? Dial { get; set; }

    /// <summary>Testing: answer incoming calls after a second.</summary>
    public bool AutoAnswer { get; set; }

    /// <summary>
    /// Offer video on calls. Off leaves video out of every offer and answer, for servers that
    /// reject calls with video. (The real app will have this per account.)
    /// </summary>
    public bool OfferVideo { get; set; } = true;

    /// <summary>Testing: calls and answers start with the camera off.</summary>
    public bool CameraOff { get; set; }

    /// <summary>Testing: send the test pattern instead of the camera.</summary>
    public bool TestPattern { get; set; }

    /// <summary>Testing: send the microphone's frequency bars as video instead of the camera.</summary>
    public bool AudioBars { get; set; }

    /// <summary>Testing: start with the Audio | Video switch on Video (so --dial places a video call).</summary>
    public bool VideoCall { get; set; }

    /// <summary>Testing: press Add video this many seconds into an audio call.</summary>
    public int? AddVideoAfter { get; set; }

    /// <summary>Testing: hang up a connected call after this many seconds.</summary>
    public int? HangupAfter { get; set; }

    /// <summary>Testing: start with simulated loss on the audio and video we send, in percent (the diagnostics sliders).</summary>
    public int AudioLossPercent { get; set; }
    public int VideoLossPercent { get; set; }

    /// <summary>Testing: how many calls --dial places, one after another (default 1).</summary>
    public int DialCount { get; set; } = 1;

    public bool HasAccount => !string.IsNullOrWhiteSpace(Account?.Server) && !string.IsNullOrWhiteSpace(Account?.Username);

    public static Settings Load(string[] args)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "softphone.json");
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--config") path = args[i + 1];
        }

        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new()
            : new Settings();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length: settings.SipPort = int.Parse(args[++i]); break;
                case "--profile" when i + 1 < args.Length: settings.Profile = args[++i]; break;
                case "--fake-media": settings.FakeMedia = true; break;
                case "--dial" when i + 1 < args.Length: settings.Dial = args[++i]; break;
                case "--auto-answer": settings.AutoAnswer = true; break;
                case "--no-video": settings.OfferVideo = false; break;
                case "--camera-off": settings.CameraOff = true; break;
                case "--video": settings.VideoCall = true; break;
                case "--test-pattern": settings.TestPattern = true; break;
                case "--audio-bars": settings.AudioBars = true; break;
                case "--add-video" when i + 1 < args.Length: settings.AddVideoAfter = int.Parse(args[++i]); break;
                case "--hangup-after" when i + 1 < args.Length: settings.HangupAfter = int.Parse(args[++i]); break;
                case "--loss-audio" when i + 1 < args.Length: settings.AudioLossPercent = int.Parse(args[++i]); break;
                case "--loss-video" when i + 1 < args.Length: settings.VideoLossPercent = int.Parse(args[++i]); break;
                case "--dial-count" when i + 1 < args.Length: settings.DialCount = int.Parse(args[++i]); break;
            }
        }
        return settings;
    }
}
