using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SIPSorcery;
using SIPSorcery.Net;
using WebRTCAspire.Web;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorPages();

// Configure JSON options for minimal APIs
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});


// Add WebRTC services to the container.
builder.Services.AddSingleton(typeof(WebRTCHostedService));
builder.Services.AddHostedService<WebRTCHostedService>();

var app = builder.Build();

LogFactory.Set(app.Services.GetRequiredService<ILoggerFactory>());

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapDefaultEndpoints();

var group = app.MapGroup("/api/webrtc");

group.MapGet("/getoffer", async (string id, WebRTCHostedService webRTCServer, ILogger<Program> logger) =>
{
    using var _ = logger.BeginScope("GetOffer {PeerId}.", id);
    logger.LogDebug("WebRTCController GetOffer {PeerId}.", id);
    var offer = await webRTCServer.GetOffer(id);
    return Results.Ok(offer);
});

app.MapPost("/setanswer", async (string id, [FromBody] RTCSessionDescriptionInit answer, WebRTCHostedService webRTCServer, ILogger<Program> logger) =>
{
    using var _ = logger.BeginScope("SetAnswer {PeerId}.", id);

    logger.LogDebug("SetAnswer {PeerId} {type} {sdp}.", id, answer?.type, answer?.sdp);

    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest("The id cannot be empty in SetAnswer.");
    }
    else if (string.IsNullOrWhiteSpace(answer?.sdp))
    {
        return Results.BadRequest("The SDP answer cannot be empty in SetAnswer.");
    }

    webRTCServer.SetRemoteDescription(id, answer);
    return Results.Ok();
});

app.MapPost("/addicecandidate", async (string id, [FromBody] RTCIceCandidateInit iceCandidate, WebRTCHostedService webRTCServer, ILogger<Program> logger) =>
{
    using var _ = logger.BeginScope("AddIceCandidate {PeerId}.", id);

    logger.LogDebug("AddIceCandidate {PeerId} {IceCandidate}.", id, iceCandidate?.candidate);

    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest("The id cannot be empty in AddIceCandidate.");
    }
    else if (string.IsNullOrWhiteSpace(iceCandidate?.candidate))
    {
        return Results.BadRequest("The candidate field cannot be empty in AddIceCandidate.");
    }

    webRTCServer.AddIceCandidate(id, iceCandidate);

    return Results.Ok();
});

app.Run();
