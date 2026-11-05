namespace VideoSpike;

/// <summary>
/// The near end's side of the bench protocol, over stdin/stdout so the bench can sample the near
/// end's processes from outside (measuring from inside would add the sampler's own cost):
///   near end -> "READY"           all calls up and every tile has shown a frame (or timed out)
///   bench    -> "START"           begin the measurement window
///   bench    -> "STOP"            end it; near end answers with "STATS {json}"
///   bench    -> "EXIT"            hang up and quit
/// Run by hand (no bench), the near end starts a window by itself and prints stats every 10 s.
/// </summary>
public sealed class BenchLink
{
    public event Action? Start;
    public event Func<Task<NearEndStats>>? Stop;
    public event Action? Exit;

    public bool Interactive { get; } = !Console.IsInputRedirected;

    public static void Send(string line)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }

    public static void Log(string line) => Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {line}");

    public void Ready()
    {
        Send("READY");
        if (Interactive) _ = SelfTimedAsync();
    }

    public void Run()
    {
        if (Interactive) return;
        new Thread(() =>
        {
            string? line;
            while ((line = Console.In.ReadLine()) != null)
            {
                switch (line.Trim())
                {
                    case "START": Start?.Invoke(); break;
                    case "STOP":
                        var stats = Stop?.Invoke().GetAwaiter().GetResult();
                        Send("STATS " + System.Text.Json.JsonSerializer.Serialize(stats, Json.Options));
                        break;
                    case "EXIT": Exit?.Invoke(); return;
                }
            }
            Exit?.Invoke();
        }) { IsBackground = true, Name = "control" }.Start();
    }

    private async Task SelfTimedAsync()
    {
        await Task.Delay(3000);
        while (true)
        {
            Start?.Invoke();
            await Task.Delay(10000);
            if (Stop == null) return;
            var stats = await Stop.Invoke();
            Log(System.Text.Json.JsonSerializer.Serialize(stats, Json.Indented));
        }
    }
}
