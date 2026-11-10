using System.Text.Json;

namespace SIPSorcery.Softphone;

/// <summary>
/// Remembers where the window was and how big, per profile, in
/// <c>%LOCALAPPDATA%\SIPSorcery\Softphonev2\window-{profile}.json</c>: saved on close, put back on
/// start. Windows doesn't do this for an app; each app keeps its own. A window closed maximised
/// opens maximised, over the normal size it had; one closed minimised opens normally. If the saved
/// place is no longer on a screen (a monitor unplugged, say), the window opens at its default size
/// and where Windows puts it, so it can't come up out of reach.
/// </summary>
internal static class WindowPlacement
{
    private sealed record Saved(int X, int Y, int Width, int Height, bool Maximized);

    private static string PathFor(string profile) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIPSorcery", "Softphonev2", $"window-{profile}.json");

    /// <summary>Puts the form where it was last closed. Call from the constructor, before it's shown.</summary>
    public static void Restore(Form form, string profile)
    {
        Saved? saved;
        try { saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(PathFor(profile))); }
        catch { return; }   // first run, or an unreadable file: the default size
        if (saved == null) return;

        var bounds = new Rectangle(saved.X, saved.Y,
            Math.Max(saved.Width, form.MinimumSize.Width), Math.Max(saved.Height, form.MinimumSize.Height));
        // Only if the title bar is still somewhere you can grab it.
        var titleBar = new Rectangle(bounds.X + 40, bounds.Y, Math.Max(bounds.Width - 80, 1), 30);
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(titleBar))) return;

        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = bounds;
        if (saved.Maximized) form.WindowState = FormWindowState.Maximized;
    }

    /// <summary>Saves the form's place. Call while it closes. A failure to save never stops the close.</summary>
    public static void Save(Form form, string profile)
    {
        // Maximised or minimised, RestoreBounds is the normal size and place to go back to.
        var b = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        if (b.Width <= 0 || b.Height <= 0) return;
        try
        {
            var path = PathFor(profile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new Saved(b.X, b.Y, b.Width, b.Height,
                form.WindowState == FormWindowState.Maximized)));
        }
        catch { /* not worth stopping the close for */ }
    }
}
