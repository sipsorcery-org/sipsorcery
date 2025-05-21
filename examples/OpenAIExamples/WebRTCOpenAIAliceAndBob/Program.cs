//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that eastablishes two connections
// with the OpenAI realtime endpoint and wires up their audio together. An OpenGL
// visualisation is used to show which agent is talking.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Jan 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AudioScope;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using SIPSorcery.OpenAI.RealtimeWebRTC;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Media;

namespace demo;

class Program
{
    private const int AUDIO_PACKET_DURATION = 20; // 20ms of audio per RTP packet for PCMU & PCMA.

    private const string ALICE_CALL_LABEL = "Alice";
    private const string BOB_CALL_LABEL = "Bob";

    private static FormAudioScope? _audioScopeForm;

    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
       .MinimumLevel.Debug()
       .Enrich.FromLogContext()
       .WriteTo.Console()
       .CreateLogger();

        var loggerFactory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(loggerFactory);

        var openAiKey = Environment.GetEnvironmentVariable("OPENAIKEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAIKEY=<your openai api key>");
            return;
        }

        // Spin up a dedicated STA thread to run WinForms for the Audio Scope.
        Thread uiThread = new Thread(() =>
        {
            // WinForms initialization must be on an STA thread.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _audioScopeForm = new FormAudioScope(true);

            Application.Run(_audioScopeForm);
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.IsBackground = true;
        uiThread.Start();

        var aliceConnectedSemaphore = new SemaphoreSlim(0, 1);
        var bobConnectedSemaphore = new SemaphoreSlim(0, 1);

        var logger = loggerFactory.CreateLogger<Program>();
        var aliceWebrtcEndPoint = new OpenAIRealtimeWebRTCEndPoint(openAiKey, logger);
        aliceWebrtcEndPoint.OnPeerConnectionConnected += () => aliceConnectedSemaphore.Release();
        var bobWebrtcEndPoint = new OpenAIRealtimeWebRTCEndPoint(openAiKey, logger);
        bobWebrtcEndPoint.OnPeerConnectionConnected += () => bobConnectedSemaphore.Release();

        // We'll listen in on the audio.
        InitialiseWindowsAudioEndPoint(aliceWebrtcEndPoint, Log.Logger, ALICE_CALL_LABEL, 1);
        InitialiseWindowsAudioEndPoint(bobWebrtcEndPoint, Log.Logger, BOB_CALL_LABEL, 2);

        var pcConfig = new RTCConfiguration
        {
            X_UseRtpFeedbackProfile = true,
        };

        var aliceNegotiateTask = aliceWebrtcEndPoint.StartConnectAsync(pcConfig);
        var bobNegotiateTask = bobWebrtcEndPoint.StartConnectAsync(pcConfig);

        Log.Information($"Both calls successfully initiated, waiting for them to connect...");

        await Task.WhenAll(aliceConnectedSemaphore.WaitAsync(), bobConnectedSemaphore.WaitAsync());

        Log.Information($"Both calls successfully connected.");

        // Send Alice's audio to Bob.
        if (aliceWebrtcEndPoint.PeerConnection != null && bobWebrtcEndPoint.PeerConnection != null)
        {
            aliceWebrtcEndPoint.PeerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                bobWebrtcEndPoint.PeerConnection.SendAudio(AUDIO_PACKET_DURATION, rtpPkt.Payload);
            };

            bobWebrtcEndPoint.PeerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                aliceWebrtcEndPoint.PeerConnection.SendAudio(AUDIO_PACKET_DURATION, rtpPkt.Payload);
            };

            bobWebrtcEndPoint.SendSessionUpdate(OpenAIVoicesEnum.ash);
            aliceWebrtcEndPoint.SendResponseCreate(OpenAIVoicesEnum.shimmer, "Say Hi!");
        }

        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        var exitTcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            aliceWebrtcEndPoint.PeerConnection?.Close("User exit");
            bobWebrtcEndPoint.PeerConnection?.Close("User exit");
            exitTcs.TrySetResult(null);
        };

        await exitTcs.Task;

        Console.WriteLine("Exiting...");

        _audioScopeForm?.Invoke(() => _audioScopeForm.Close());
    }

    private static void InitialiseWindowsAudioEndPoint(IOpenAIRealtimeWebRTCEndPoint webrtcEndPoint, Serilog.ILogger logger, string callLabel, int audioScopeNumber)
    {
        // TODO: The windows audio endpoint is not suitable here due to the dual multi-channel audio feed coming from the two WebRTC endpoints.
        // Switch directly to an NAudio mixer.
        var audioEncoder = new AudioEncoder(AudioCommonlyUsedFormats.OpusWebRTC);
        WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(audioEncoder, -1, -1, true, false);
        webrtcEndPoint.ConnectAudioEndPoint(windowsAudioEP);

        webrtcEndPoint.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt, uint delta) =>
        {
            var decodedSample = audioEncoder.DecodeAudio(rtpPkt.Payload, AudioCommonlyUsedFormats.OpusWebRTC);

            var samples = decodedSample
                .Select(s => new Complex(s / 32768f, 0f))
                .ToArray();

            var frame = _audioScopeForm?.Invoke(() => _audioScopeForm.ProcessAudioSample(samples, audioScopeNumber));
        };
    }
}
