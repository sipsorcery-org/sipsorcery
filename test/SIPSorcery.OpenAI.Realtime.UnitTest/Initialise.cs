//-----------------------------------------------------------------------------
// Filename: Initialise.cs
//
// Description: Assembly initialiser for SIPSorcery unit tests.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 02 Jun 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Serilog;
using Serilog.Extensions.Logging;

namespace SIPSorcery.OpenAI.Realtime.UnitTests;

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
        SIPSorcery.LogFactory.Set(new SerilogLoggerFactory(serilog));
        return new SerilogLoggerProvider(serilog).CreateLogger("unit");
    }
}
