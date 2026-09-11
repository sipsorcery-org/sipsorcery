//-----------------------------------------------------------------------------
// Filename: TestLogger.cs
//
// Description: Helper class for test logging.
//
// Author(s):
// Copilot
//
// History:
// 25 Jun 2025	Generated	Copilot.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using MartinCostello.Logging.XUnit;

namespace Vpx.Net.TestVectors
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
