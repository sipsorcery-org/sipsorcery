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
// 27 May 2025  Aaron Clauson   Moved from SIPSorcery main repo to SIPSorcery.OpenAI.WebRTC repo.
// 26 Feb 2026  Aaron Clauson   Moved back to SIPSorcery mono repo.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AudioScope;
using LanguageExt;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using SIPSorcery.OpenAI.Realtime;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Media;

namespace demo;

class Program
{
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

        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(openAiKey))
        {
            Log.Logger.Error("Please provide your OpenAI key as an environment variable. For example: set OPENAI_API_KEY=<your openai api key>");
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

        var aliceWebrtcEndPoint = new WebRTCEndPoint(openAiKey, loggerFactory);
        aliceWebrtcEndPoint.OnPeerConnectionConnected += () => aliceConnectedSemaphore.Release();
        var bobWebrtcEndPoint = new WebRTCEndPoint(openAiKey, loggerFactory);
        bobWebrtcEndPoint.OnPeerConnectionConnected += () => bobConnectedSemaphore.Release();

        // We'll listen in on the audio.
        InitialiseWindowsAudioEndPoint(aliceWebrtcEndPoint, Log.Logger, ALICE_CALL_LABEL, 1);
        InitialiseWindowsAudioEndPoint(bobWebrtcEndPoint, Log.Logger, BOB_CALL_LABEL, 2);

        var pcConfig = new RTCConfiguration
        {
            X_UseRtpFeedbackProfile = true,
        };

        var aliceNegotiateTask = aliceWebrtcEndPoint.StartConnect(pcConfig);
        var bobNegotiateTask = bobWebrtcEndPoint.StartConnect(pcConfig);

        Log.Information($"Both calls successfully initiated, waiting for them to connect...");

        await Task.WhenAll(aliceConnectedSemaphore.WaitAsync(), bobConnectedSemaphore.WaitAsync());

        Log.Information($"Both calls successfully connected.");

        // Get the conversation started!
        if (aliceWebrtcEndPoint.PeerConnection.IsSome && bobWebrtcEndPoint.PeerConnection.IsSome)
        {
            // Send RTP audio payloads receied from Alice to Bob.
            aliceWebrtcEndPoint.PipeAudioTo(bobWebrtcEndPoint);

            // Send RTP audio payloads receied from Bob to Alice.
            bobWebrtcEndPoint.PipeAudioTo(aliceWebrtcEndPoint);

            bobWebrtcEndPoint.DataChannelMessenger.SendSessionUpdate(RealtimeVoicesEnum.ash);
            aliceWebrtcEndPoint.DataChannelMessenger.SendResponseCreate(RealtimeVoicesEnum.shimmer, "Say Hi!");
        }
        else
        {
            Log.Error("Failed to establish peer connections for both Alice and Bob.");
            return;
        }

        if (_audioScopeForm != null)
        {
            _audioScopeForm.FormClosed += (s, e) =>
            {
                Log.Logger.Information("Audio scope form closed, exiting application.");
                aliceWebrtcEndPoint.PeerConnection.IfSome(pc => pc.Close("User exit"));
                bobWebrtcEndPoint.PeerConnection.IfSome(pc => pc.Close("User exit"));
            };
        }

        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        var exitTcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            aliceWebrtcEndPoint.PeerConnection.IfSome(pc => pc.Close("User exit"));
            bobWebrtcEndPoint.PeerConnection.IfSome(pc => pc.Close("User exit"));
            exitTcs.TrySetResult(null);
        };

        await exitTcs.Task;

        Console.WriteLine("Exiting...");

        _audioScopeForm?.Invoke(() => _audioScopeForm.Close());
    }

    private static void InitialiseWindowsAudioEndPoint(IWebRTCEndPoint webrtcEndPoint, Serilog.ILogger logger, string callLabel, int audioScopeNumber)
    {
        // TODO: The windows audio endpoint is not suitable here due to the dual multi-channel audio feed coming from the two WebRTC endpoints.
        // Switch directly to an NAudio mixer.
        var audioEncoder = new AudioEncoder(AudioCommonlyUsedFormats.OpusWebRTC);
        WindowsAudioEndPoint windowsAudioEP = new WindowsAudioEndPoint(audioEncoder, -1, -1, true, false);
        webrtcEndPoint.ConnectAudioEndPoint(windowsAudioEP);

        webrtcEndPoint.OnAudioFrameReceived += (EncodedAudioFrame encodedAudioFrame) =>
        {
            var decodedSample = audioEncoder.DecodeAudio(encodedAudioFrame.EncodedAudio, AudioCommonlyUsedFormats.OpusWebRTC);

            var samples = decodedSample
                .Select(s => new Complex(s / 32768f, 0f))
                .ToArray();

            var frame = _audioScopeForm?.Invoke(() => _audioScopeForm.ProcessAudioSample(samples, audioScopeNumber));
        };
    }
}
