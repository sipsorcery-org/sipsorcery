using Serilog;
using Serilog.Extensions.Logging;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

public class TestLogHelper
{
    public static Microsoft.Extensions.Logging.ILogger InitTestLogger(Xunit.Abstractions.ITestOutputHelper output)
    {
        string template = "{Timestamp:HH:mm:ss.ffff} [{Level}] {Scope} {Message}{NewLine}{Exception}";
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
            .Enrich.WithProperty("ThreadId", System.Threading.Thread.CurrentThread.ManagedThreadId)
            .WriteTo.TestOutput(output, outputTemplate: template)
            .WriteTo.Console(outputTemplate: template)
            .CreateLogger();
        return new SerilogLoggerProvider(serilog).CreateLogger("unit");
    }
}
