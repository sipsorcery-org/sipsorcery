// Softphone v2, desktop prototype: one SIP video call in a WebView2 window.
//
// Options: --config <softphone.json>  --port <sip port>  --profile <name>  --fake-media
// See README.md.

namespace SIPSorcery.Softphone;

internal static class Program
{
    /// <summary>Held for the life of the process so no other copy uses the same profile.</summary>
    private static Mutex? _profileLock;

    [STAThread]
    private static void Main(string[] args)
    {
        // It's a WinExe, so there's no console: an error at startup would otherwise just close it.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowError(e.ExceptionObject as Exception);

        try
        {
            ApplicationConfiguration.Initialize();
            LibraryLog.Install();
            var settings = Settings.Load(args);
            settings.Profile = ClaimProfile(settings.Profile);
            Application.Run(new MainForm(settings));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>
    /// The profile names the log file and the WebView2 data folder, and neither can be shared by
    /// two running copies. If another copy already has this one, use the next free "name-2",
    /// "name-3", and so on, so a second copy just starts.
    /// </summary>
    private static string ClaimProfile(string profile)
    {
        for (int n = 1; ; n++)
        {
            var name = n == 1 ? profile : $"{profile}-{n}";
            var mutex = new Mutex(true, $@"Local\SIPSorcery.Softphonev2.{name}", out bool mine);
            if (mine)
            {
                _profileLock = mutex;
                return name;
            }
            mutex.Dispose();
        }
    }

    private static void ShowError(Exception? ex)
    {
        // Kept in a file too: an error while the app is closing can take the process down before
        // anyone sees the box (all that's left of it is the error sound).
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "Softphonev2");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* the box still shows */ }
        MessageBox.Show(ex?.ToString() ?? "Unknown error", "Softphone failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
