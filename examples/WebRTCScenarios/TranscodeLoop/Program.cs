// TranscodeLoop: a browser engine (WebView2) sends a test stream to SIPSorcery over WebRTC,
// SIPSorcery decodes it, encodes it again and sends it back to the page over a second WebRTC
// connection. The window shows the stream at each step so a person can check it by eye.
// See README.md for the switches.

namespace SIPSorcery.TranscodeLoop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowError(e.ExceptionObject as Exception);

        try
        {
            ApplicationConfiguration.Initialize();
            LibraryLog.Install();
            var options = Options.Parse(args);
            Application.Run(new MainForm(options));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private static void ShowError(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Options.DataDir);
            File.AppendAllText(Path.Combine(Options.DataDir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* the box still shows */ }
        MessageBox.Show(ex?.ToString() ?? "Unknown error", "TranscodeLoop failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

/// <summary>
/// The switches. Those for the stream (source, size, codecs...) only set the page's controls
/// before it starts; everything can be changed in the window too.
/// </summary>
internal sealed class Options
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "TranscodeLoop");

    /// <summary>Settings for the page, passed in its query string.</summary>
    public Dictionary<string, string> Page { get; } = new();

    /// <summary>Saves a PNG of the window this many seconds after the stream starts.</summary>
    public string? CapturePath { get; private set; }
    public double CaptureAfter { get; private set; } = 10;

    /// <summary>Closes the app this many seconds after the stream starts.</summary>
    public double? ExitAfter { get; private set; }

    /// <summary>Writes the last stats (C# and page) as JSON here as the app closes.</summary>
    public string? StatsPath { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        string Next(ref int i) => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--start": o.Page["start"] = "1"; break;
                case "--source": o.Page["source"] = Next(ref i); break;          // card | fake
                case "--size": o.Page["size"] = Next(ref i); break;              // 640x480
                case "--fps": o.Page["fps"] = Next(ref i); break;
                case "--mode": o.Page["mode"] = Next(ref i); break;              // transcode | relay
                case "--codec-in": o.Page["in"] = Next(ref i); break;            // VP8 | H264
                case "--decoder": o.Page["dec"] = Next(ref i); break;            // vp8.net | ffmpeg
                case "--codec-out": o.Page["out"] = Next(ref i); break;
                case "--encoder": o.Page["enc"] = Next(ref i); break;
                case "--browser-kbps": o.Page["kbps"] = Next(ref i); break;
                case "--encoder-kbps": o.Page["enckbps"] = Next(ref i); break;
                case "--drop-a": o.Page["dropA"] = Next(ref i); break;            // percent of leg A frames dropped before decode
                case "--loss-b": o.Page["lossB"] = Next(ref i); break;            // percent of leg B packets dropped
                case "--no-decoded-view": o.Page["decoded"] = "0"; break;
                case "--capture": o.CapturePath = Path.GetFullPath(Next(ref i)); o.Page["start"] = "1"; break;
                case "--capture-after": o.CaptureAfter = double.Parse(Next(ref i), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--exit-after": o.ExitAfter = double.Parse(Next(ref i), System.Globalization.CultureInfo.InvariantCulture); o.Page["start"] = "1"; break;
                case "--stats": o.StatsPath = Path.GetFullPath(Next(ref i)); break;
                default: throw new ArgumentException($"Unknown switch {args[i]}. See README.md.");
            }
        }
        return o;
    }
}
