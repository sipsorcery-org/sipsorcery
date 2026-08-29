using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

[Trait("Category", "unit")]
public class IsAliveUnitTest
{
    private ILogger logger = NullLogger.Instance;

    public IsAliveUnitTest(Xunit.Abstractions.ITestOutputHelper output)
    {
        logger = TestLogHelper.InitTestLogger(output);
    }

    [Fact]
    public void Is_Alive_Test()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);
    }
}
