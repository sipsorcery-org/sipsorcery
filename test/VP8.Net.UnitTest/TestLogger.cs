using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Vpx.Net.UnitTest
{
    public class TestLogger
    {
        public static ILoggerFactory GetLogger(Xunit.Abstractions.ITestOutputHelper output)
        {
            string template = "{Timestamp:HH:mm:ss.ffff} [{Level}] {Scope} {Message}{NewLine}{Exception}";
            //var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var serilog = new LoggerConfiguration()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .Enrich.WithProperty("ThreadId", System.Threading.Thread.CurrentThread.ManagedThreadId)
                .WriteTo.TestOutput(output, outputTemplate: template)
                .WriteTo.Console(outputTemplate: template)
                .CreateLogger();
            //SIPSorcery.LogFactory.Set(new SerilogLoggerFactory(serilog));
            //return new SerilogLoggerProvider(serilog).CreateLogger("unit");
            return new SerilogLoggerFactory(serilog);
        }
    }
}
