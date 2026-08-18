using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

[Trait("Category", "unit")]
public class GeminiLiveServiceCollectionExtensionsUnitTests
{
    private readonly ILogger logger;

    public GeminiLiveServiceCollectionExtensionsUnitTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        logger = TestLogHelper.InitTestLogger(output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Empty_Api_Key_Is_Rejected(string apiKey)
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddGeminiLiveRealtime(apiKey));
    }

    [Fact]
    public void The_End_Point_And_Transport_Resolve_From_The_Container()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGeminiLiveRealtime("test-api-key");

        using var provider = services.BuildServiceProvider();

        var transport = provider.GetRequiredService<IGeminiLiveWebSocketClient>();
        Assert.IsType<GeminiLiveWebSocketClient>(transport);

        var endPoint = provider.GetRequiredService<IGeminiLiveEndPoint>();
        Assert.IsType<GeminiLiveEndPoint>(endPoint);
        Assert.NotNull(endPoint.Messenger);
    }

    [Fact]
    public void An_Existing_Transport_Registration_Is_Left_In_Place()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGeminiLiveWebSocketClient>(new FakeGeminiLiveWebSocketClient());
        services.AddGeminiLiveRealtime("test-api-key");

        using var provider = services.BuildServiceProvider();

        Assert.IsType<FakeGeminiLiveWebSocketClient>(provider.GetRequiredService<IGeminiLiveWebSocketClient>());
    }

    [Fact]
    public void The_Container_Disposes_The_End_Point_It_Created()
    {
        logger.LogDebug("--> {MethodName}", System.Reflection.MethodBase.GetCurrentMethod()?.Name);

        var services = new ServiceCollection();
        services.AddLogging();
        var transport = new FakeGeminiLiveWebSocketClient();
        services.AddSingleton<IGeminiLiveWebSocketClient>(transport);
        services.AddGeminiLiveRealtime("test-api-key");

        var provider = services.BuildServiceProvider();
        var endPoint = provider.GetRequiredService<IGeminiLiveEndPoint>();
        Assert.NotNull(endPoint);

        provider.Dispose();

        Assert.Equal(1, transport.DisposeCount);
    }
}
