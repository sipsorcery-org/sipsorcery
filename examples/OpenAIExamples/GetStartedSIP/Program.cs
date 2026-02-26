//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example SIP application that can be used to interact with
// OpenAI's real-time API https://platform.openai.com/docs/guides/realtime-sip.
//
// NOTE: As of 05 Sep 2025 this example does work to establish an audio stream and is
// able to receive web socket messages. There is no echo cancellation feature in this
// demo so if not provided by the by your Windows audio device then ChatGPT will end
// up talking to itself (as a workaround use a headset).
//
// Prerequisites:
// In order to get OpenAI to answer your SIP call you need to be able to receive 
// a webhook. This example uses ASP.NET Core to host an HTTP endpoint that can receive
// the webhook but it must be publicly accessible, ngrok is a good tool for this.
//
// The approach used for tesing this application:
//
// 1. Register a domain with ngrok at https://dashboard.ngrok.com/domains
// 2. In your OpenAI poject at https://platform.openai.com/settings/ choose webhooks
//    and add the URL for your ngrok domain, e.g. https://<your ngrok domain>/webhook
// 3. Start ngrok to forward HTTP traffic to your local machine:
//    ngrok http --url=<your ngrok domain> https://localhost:53742
//
// Usage:
// set OPENAI_API_KEY=your_openai_key
// set OPENAI_PROJECT_ID = proj_jjxxxxxxxxx4p
// dotnet run
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Aug 2025	Aaron Clauson	Created, Dublin, Ireland.
// 26 Feb 2026  Aaron Clauson   Moved to SIPSorcery mono repo.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace demo;

public class RealtimeCallIncoming
{
    public required string Id { get; set; }
    public required string Type { get; set; }
    public int CreatedAt { get; set; }
    public RealtimeCallIncomingData? Data { get; set; }
}

public class RealtimeCallIncomingData
{
    public required string Call_ID { get; set; }
}

//record call_accept(string type = "realtime", string instructions = "You are a support agent.", string model = "gpt-4o-realtime-preview-2024-12-17");
record call_accept(string type = "realtime", string instructions = "You are a support agent.", string model = "gpt-realtime");

class Program
{
    private const string HTTP_CLIENT_NAME_OPENAI = "openai";

    private const string ENV_VAR_OPENAI_API_KEY = "OPENAI_API_KEY";

    private const string ENV_VAR_OPENAI_PROJECT_ID = "OPENAI_PROJECT_ID";

    static void Main()
    {
        var builder = WebApplication.CreateBuilder();

        Log.Logger = new LoggerConfiguration()
            //.MinimumLevel.Debug()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        builder.Host.UseSerilog();

        var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(loggerFactory);

        Log.Logger.Information("SIP OpenAI Demo Program");

        var openAIApiKey = Environment.GetEnvironmentVariable(ENV_VAR_OPENAI_API_KEY);

        if (string.IsNullOrWhiteSpace(openAIApiKey))
        {
            Log.Logger.Error($"Please provide your OpenAI key as an environment variable. For example: set {ENV_VAR_OPENAI_API_KEY}=<your openai api key>");
            return;
        }

        var openAIProjectID = Environment.GetEnvironmentVariable(ENV_VAR_OPENAI_PROJECT_ID);

        if (string.IsNullOrWhiteSpace(openAIProjectID))
        {
            Log.Logger.Error($"Please provide your OpenAI project ID as an environment variable. For example: set {ENV_VAR_OPENAI_PROJECT_ID}=<your openai poject ID>, e.g. proj_jj....U4p");
            return;
        }

        var logger = loggerFactory.CreateLogger<Program>();

        InitialiseHttpClients(builder, openAIApiKey);

        var app = builder.Build();

        // HTTP endpoint to receive webhooks from OpenAI. Until the webhook is received and responded to the call will not be answered.
        app.MapPost("/webhook", async (RealtimeCallIncoming incoming, [FromServices] IHttpClientFactory httpClientFactory) =>
        {
            var callID = incoming.Data?.Call_ID;

            Log.Information("Webhook received. CallId={CallId} From={From} To={To} Model={Model}", callID);

            if (string.IsNullOrWhiteSpace(callID))
            {
                Log.Error("Webhook missing call_id.");
            }
            else
            {
                Log.Information("Call accepted (initiating accept POST and WS). CallId={CallId}", callID);

                var acceptUrl = $"v1/realtime/calls/{callID}/accept";
                var acceptPayload = new call_accept();

                var http = httpClientFactory.CreateClient("openai");
                var resp = await http.PostAsJsonAsync(acceptUrl, acceptPayload);

                if (!resp.IsSuccessStatusCode)
                {
                    Log.Error("Accept POST failed for CallId={CallId} Status={Status} Body={Body}", callID, resp.StatusCode, await resp.Content.ReadAsStringAsync());
                }
                else
                {
                    Log.Information("Accept POST succeeded for CallId={CallId}", callID);

                    var cts = new CancellationTokenSource();
                    _ = Task.Run(() => StartWebSocketConnection(openAIApiKey, callID, cts));
                }
            }

            return Results.Ok();
        });

        _ = Task.Run(() => StartSIPCall(openAIProjectID));

        app.Run();
    }

    private static async Task StartSIPCall(string openAIProjectID)
    {
        Log.Logger.Information("Starting SIP call to OpenAI...");

        var sipTransport = new SIPTransport();
        sipTransport.EnableTraceLogs();

        var userAgent = new SIPUserAgent(sipTransport, null);
        var winAudio = new WindowsAudioEndPoint(new AudioEncoder(includeOpus: true));
        //winAudio.RestrictFormats(x => x.Codec == AudioCodecsEnum.OPUS); // No joy as of 5 Sep 2025. PCM only.
        var voipMediaSession = new VoIPMediaSession(winAudio.ToMediaEndPoints());
        voipMediaSession.AcceptRtpFromAny = true;

        // Place the call and wait for the result.
        bool callResult = await userAgent.Call($"{openAIProjectID}@sip.api.openai.com;transport=tls", null, null, voipMediaSession);

        Log.Logger.Information($"SIP call to OpenAI result {(callResult ? "succes" : "failure")}.");
    }

    private static async Task StartWebSocketConnection(string openAIApiKey, string callID, CancellationTokenSource cts)
    {
        ClientWebSocket? ws = null;
        try
        {
            ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");

            var url = $"wss://api.openai.com/v1/realtime?call_id={Uri.EscapeDataString(callID)}";

            Log.Information("Connecting WebSocket for CallId={CallId} URL={Url}", callID, url);

            await ws.ConnectAsync(new Uri(url), cts.Token);

            Log.Information("WebSocket connected for CallId={CallId}", callID);

            var responseCreate = new { type = "response.create", response = new { instructions = "Say Hi." } };
            var json = JsonSerializer.Serialize(responseCreate);

            await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cts.Token);
            
            Log.Debug("Sent response.create for CallId={CallId}", callID);
            
            var buffer = new byte[8192];

            while (!cts.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult? result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log.Information("WebSocket closed for CallId={CallId} Status={Status} Desc={Desc}", callID, result.CloseStatus, result.CloseStatusDescription);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                var message = sb.ToString();
                Log.Debug("[Call {CallId}] WS: {Message}", callID, message);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebSocket session error for CallId={CallId}", callID);
        }
        finally
        {
            if (ws != null) { try { ws.Abort(); ws.Dispose(); } catch { } }
        }
    }

    private static void InitialiseHttpClients(WebApplicationBuilder builder, string openAIApiKey)
    {
        // Shared HttpClient configured with auth header and logging
        builder.Services.AddHttpClient(HTTP_CLIENT_NAME_OPENAI, client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAIApiKey);
        });

        builder.Services.AddHttpLogging(logging =>
        {
            logging.LoggingFields = HttpLoggingFields.RequestHeaders | HttpLoggingFields.RequestQuery | HttpLoggingFields.RequestBody |
                HttpLoggingFields.ResponseBody | HttpLoggingFields.ResponseHeaders;
        });
    }

}
