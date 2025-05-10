using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using OpenAI;
using OpenAI.Realtime;
using System.Collections.Generic;

namespace demo;

class Program
{
    private const string OPENAIKEY_ENVVAR = "OPENAIKEY";
    private const string OPENAI_MODEL = "gpt-4o-realtime-preview-2024-12-17";
    private const string OPENAI_VOICE = "shimmer";

    static async Task Main()
    {
        Console.WriteLine("WebRTC OpenAI Demo Program");

        var openAIKey = Environment.GetEnvironmentVariable(OPENAIKEY_ENVVAR);
        if (string.IsNullOrWhiteSpace(openAIKey))
        {
            Console.Error.WriteLine($"{OPENAIKEY_ENVVAR} environment variable not set, cannot continue.");
            return;
        }

        var pcConfig = new RTCConfiguration
        {
            X_UseRtpFeedbackProfile = true
        };

        var openaiClient = new OpenAIClient(new OpenAIAuthentication(openAIKey));
        var webrtcEndPoint = openaiClient.RealtimeEndpointWebRTC;
        webrtcEndPoint.EnableDebug = true;

        WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(webrtcEndPoint.AudioEncoder, -1, -1, false, false);
        windowsAudioEP.SetAudioSinkFormat(webrtcEndPoint.AudioFormat);
        windowsAudioEP.SetAudioSourceFormat(webrtcEndPoint.AudioFormat);
        windowsAudioEP.OnAudioSourceEncodedSample += webrtcEndPoint.SendAudio;

        webrtcEndPoint.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
        {
            windowsAudioEP.GotAudioRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
        };
        webrtcEndPoint.OnPeerConnectionConnected += async () =>
        {
            await windowsAudioEP.StartAudio();
            await windowsAudioEP.StartAudioSink();
        };
        webrtcEndPoint.OnPeerConnectionClosedOrFailed += async () => await windowsAudioEP.CloseAudio();

        // This will get sent to OpenAI once the WebRTC connection is established. It updates the session
        // that is automatically created by the OpenAI Realtime endpoint.
        var sessionConfig = new SessionConfiguration(
                OPENAI_MODEL,
                voice: OPENAI_VOICE,
                instructions: "Keep it snappy.",
                tools: new List<Tool>());

        var webrtcSession = await webrtcEndPoint.CreateSessionAsync(
            sessionConfig,
            rtcConfiguration: pcConfig);

        // Get the conversation started.
        var responseCreate = new CreateResponseRequest(new(instructions: "Say Hi."));
        await webrtcSession.SendAsync(responseCreate);

        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        ManualResetEvent exitMre = new(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            exitMre.Set();
        };
        exitMre.WaitOne();
    }
}
