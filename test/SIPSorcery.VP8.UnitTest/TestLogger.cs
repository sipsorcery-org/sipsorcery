using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.Logging;

namespace Vpx.Net.UnitTest
{
    public class TestLogger
    {
        public static ILoggerFactory GetLogger(Xunit.Abstractions.ITestOutputHelper output)
        {
            var options = new XUnitLoggerOptions
            {
                Filter = (category, level) => level >= LogLevel.Debug
            };
            var loggerProvider = new XUnitLoggerProvider(output, options);

            return LoggerFactory.Create(builder =>
            {
                builder.AddProvider(loggerProvider);
            });
        }
    }
}
