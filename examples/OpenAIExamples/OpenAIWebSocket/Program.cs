using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Events;
using Betalgo.Ranul.OpenAI.Extensions;
using Betalgo.Ranul.OpenAI.Managers;

// Set up a boostrap logger.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Log.Debug("ASP.NET app starting.");

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, lc) => lc
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
//.Enrich.WithMachineName()
//.Enrich.WithProperty("Application", "MyApp")
);

builder.Host.UseSerilog();
builder.Services.AddOpenAIRealtimeService();

var app = builder.Build();

app.MapGet("/", async (
    [FromServices] ILogger<Program> logger,
    [FromServices] IOpenAIRealtimeService openAiRealtime) =>
{
    logger.LogDebug("App is processing request for /.");

    await openAiRealtime.ConnectAsync();

    return "Hello World!";
});

app.MapGet("/hello", ([FromServices] ILogger<Program> logger) =>
{
    logger.LogDebug("App is processing request for /hello.");
    return "Hello World!";
});

app.Run();
