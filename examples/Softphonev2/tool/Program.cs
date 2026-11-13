// sipsorcery-softphone: starts the SIPSorcery Softphone, the desktop app this tool's package
// carries in app/ (see Softphonev2.Tool.csproj), passing on any options (--profile, --dial and
// the rest, as in the desktop README), and hands the terminal straight back.

using System.Diagnostics;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The SIPSorcery Softphone runs on Windows 10 or 11 only.");
    return 1;
}

var exe = Path.Combine(AppContext.BaseDirectory, "app", "Softphone.exe");
if (!File.Exists(exe))
{
    Console.Error.WriteLine($"The softphone isn't where it should be ({exe}). Reinstall the tool: dotnet tool update -g SIPSorcery.Softphone --prerelease");
    return 1;
}

var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
foreach (var arg in args) start.ArgumentList.Add(arg);
try
{
    using var app = Process.Start(start);
    return app is null ? 1 : 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Couldn't start the softphone: {ex.Message}");
    return 1;
}
