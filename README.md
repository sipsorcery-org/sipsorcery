| CI | win-x64 | linux-x64 | osx-x64 | Examples <br/> (win-x64) | Softphone <br/> (win-x64) |
|-|-|-|-|-|-|
| <sup>AppVeyor</sup> | [![Build status](https://ci.appveyor.com/api/projects/status/1prvhq7jyw0s5fb1/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery/branch/master) | [![Build status](https://ci.appveyor.com/api/projects/status/cark9l28ovb8o886/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-5aavr/branch/master) | [![Build status](https://ci.appveyor.com/api/projects/status/7mrg69mtolwceplg/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-jyl3x/branch/master) | [![Examples build status](https://ci.appveyor.com/api/projects/status/4myf11mda0p69ysm/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-mre1o/branch/master) | [![Softphone build status](https://ci.appveyor.com/api/projects/status/xx1bcttkk4gbrd3y/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-0p6s4/branch/master) |
| <sup>GitHub Actions</sup> | ![](https://github.com/sipsorcery/sipsorcery/workflows/win-x64/badge.svg) | ![](https://github.com/sipsorcery/sipsorcery/workflows/linux-x64/badge.svg) | ![](https://github.com/sipsorcery/sipsorcery/workflows/osx-x64/badge.svg) | ![](https://github.com/sipsorcery/sipsorcery/workflows/egs-win-x64/badge.svg) | |
| <sup>Azure DevOps</sup>   | [![Build Status](https://dev.azure.com/aaronrc/SIPSorcery/_apis/build/status/sipsorcery.sipsorcery?branchName=master&jobName=Job&configuration=Job%20windows)](https://dev.azure.com/aaronrc/SIPSorcery/_build/latest?definitionId=3&branchName=master) | [![Build Status](https://dev.azure.com/aaronrc/SIPSorcery/_apis/build/status/sipsorcery.sipsorcery?branchName=master&jobName=Job&configuration=Job%20linux)](https://dev.azure.com/aaronrc/SIPSorcery/_build/latest?definitionId=3&branchName=master) | [![Build Status](https://dev.azure.com/aaronrc/SIPSorcery/_apis/build/status/sipsorcery.sipsorcery?branchName=master&jobName=Job&configuration=Job%20mac)](https://dev.azure.com/aaronrc/SIPSorcery/_build/latest?definitionId=3&branchName=master) | | |

## What Is It?

**This fully C# library can be used to add Real-time Communications, typically audio and video calls, to .NET Core applications.**

The diagram below is a high level overview of a Real-time audio and video call between Alice and Bob. It illustrates where the `SIPSorcery` library can help.

![Real-time Communications Overview](./img/sipsorcery_realtime_overview.png)

**Supports both VoIP ([get started](#getting-started-voip)) and WebRTC ([get started](#getting-started-webrtc)).**

**Some of the protocols supported:**

 - Session Initiation Protocol [(SIP)](https://tools.ietf.org/html/rfc3261),
 - Real-time Transport Protocol [(RTP)](https://tools.ietf.org/html/rfc3550),
 - Web Real-time Communications [(WebRTC)](https://www.w3.org/TR/webrtc/),
 - Interactive Connectivity Establishment [(ICE)](https://tools.ietf.org/html/rfc8445),
 - And more.

**Media End Points - Audio/Video Sinks and Sources:**

 - This library does not provide access to audio and video devices or native codecs. Providing cross platform access on top of .NET Core is a large undertaking. A number of efforts in separate libraries are currently in progress. 
   - [SIPSorceryMedia.Windows](https://github.com/sipsorcery/SIPSorceryMedia.Windows): Windows specific library that provides audio capture and playback. 
   - [SIPSorceryMedia.Encoders](https://github.com/sipsorcery/SIPSorceryMedia.Encoders): A Windows specific wrapper for the [VP8](https://www.webmproject.org/) video codec. The examples in this repository use it.
   - [SIPSorceryMedia.FFmpeg](https://github.com/sipsorcery/SIPSorceryMedia.FFmpeg): An in-progress effort to provide cross platform audio, video and codec functions using PInvoke and [FFmpeg](https://ffmpeg.org/).
   - Others: **Contributions welcome**. Frequently requested are Xamarin Forms on Android/iOS and Unix (Linux and/or Mac). New implementations need to implement one or more of the Audio Sink/Source and/or Video Sink/Source interfaces from [SIPSorceryMedia.Abstractions](https://github.com/sipsorcery/SIPSorceryMedia.Abstractions/blob/master/src/V1/MediaEndPoints.cs).

 - This library provides only a small number of audio and video codecs (G711 and G722). Additional codecs, particularly video ones, require C or C++ libraries. 

## Installation

The library is compliant with .NET Standard 2.0 (encompassing .NET Core 2.0+) and .NET Framework 4.6.1 (theoretically also encompassed by `netstandard2.0` but set as an explicit target due to compatibility issues between the two). It is available via NuGet.

For .NET Core:

````bash
dotnet add package SIPSorcery -v 5.0.0
````

With Visual Studio Package Manager Console (or search for [SIPSorcery on NuGet](https://www.nuget.org/packages/SIPSorcery/)):

````ps1
Install-Package SIPSorcery -v 5.0.0
````

## Documentation

Class reference documentation and articles explaining common usage are available at [https://sipsorcery.github.io/sipsorcery/](https://sipsorcery.github.io/sipsorcery/).

## Getting Started VoIP

The simplest possible example to place an audio-only SIP call is shown below. This example relies on the Windows specific `SIPSorceryMedia.Windows` library to play the received audio and only works on Windows (due to lack of .NET Core audio device support on non-Windows platforms).

````bash
dotnet new console --name SIPGetStarted -f netcoreapp3.1
cd SIPGetStarted
dotnet add package SIPSorcery -v 5.0.0
dotnet add package SIPSorceryMedia.Windows -v 0.0.18-pre
code . # If you have Visual Studio Code https://code.visualstudio.com installed.
# edit Program.cs and paste in the contents below.
dotnet run
# if successful you will hear the current time read out.
ctrl-c
````

````csharp
using System;
using System.Threading.Tasks;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;
using SIPSorceryMedia.Windows;

namespace SIPGetStarted
{
    class Program
    {
         private static string DESTINATION = "time@sipsorcery.com";
        
        static async Task Main()
        {
            Console.WriteLine("SIP Get Started");
			
            var userAgent = new SIPUserAgent();
            var winAudio = new WindowsAudioEndPoint(new AudioEncoder());
            var voipMediaSession = new VoIPMediaSession(winAudio.ToMediaEndPoints());

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession);
            Console.WriteLine($"Call result {((callResult) ? "success" : "failure")}.");

            Console.WriteLine("Press any key to hangup and exit.");
            Console.ReadLine();
        }
    }
}
````

The [GetStarted](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPExamples/GetStarted) example contains the full source and project file for the example above.

The three key classes in the above example are described in dedicated articles:

 - [SIPTransport](https://sipsorcery.github.io/sipsorcery/articles/transport.html),
 - [SIPUserAgent](https://sipsorcery.github.io/sipsorcery/articles/sipuseragent.html),
 - [RTPSession](https://sipsorcery.github.io/sipsorcery/articles/rtpsession.html).

The [examples folder](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPExamples) contains sample code to demonstrate other common SIP/VoIP cases.

## Getting Started WebRTC

The WebRTC specifications do not include directions about how signaling should be done (for VoIP the signaling protocol is SIP; WebRTC has no equivalent). The example below uses a simple JSON message exchange over web sockets for signaling. Part of the reason the `Getting Started WebRTC` is over 5 times as long as the `Getting Started VoIP` is the need for custom signaling.

The example requires two steps:

 - Run the `dotnet` console application,
 - Open an HTML page in a browser on the same machine.

 The full project file and code are available at [WebRTC Get Started](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStarted).

The example relies on the Windows specific `SIPSorceryMedia.Windows` package. Hopefully in the future there will be equivalent packages for other platforms.

Step 1:

````bash
dotnet new console --name WebRTCGetStarted -f netcoreapp3.1
cd WebRTCGetStarted
dotnet add package SIPSorcery -v 5.0.0
dotnet add package SIPSorceryMedia.Windows -v 0.0.18-pre
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Extensions.Logging
code . # If you have Visual Studio Code (https://code.visualstudio.com) installed
# edit Program.cs and paste in the contents below.
dotnet run
````

````csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using WebSocketSharp.Server;
using SIPSorcery.Media;
using Serilog.Extensions.Logging;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;
        private const string STUN_URL = "stun:stun.sipsorcery.com";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("WebRTC Get Started");

            logger = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
            Console.WriteLine("Press ctrl-c to exit.");

            // Ctrl-c will gracefully exit the call at any point.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static RTCPeerConnection CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            var pc = new RTCPeerConnection(config);

            var testPatternSource = new VideoTestPatternSource();
            WindowsVideoEndPoint windowsVideoEndPoint = new WindowsVideoEndPoint(true);
            var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

            MediaStreamTrack videoTrack = new MediaStreamTrack(windowsVideoEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(videoTrack);
            MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);

            testPatternSource.OnVideoSourceRawSample += windowsVideoEndPoint.ExternalVideoSourceRawSample;
            windowsVideoEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;
            audioSource.OnAudioSourceEncodedSample += pc.SendAudio;
            pc.OnVideoFormatsNegotiated += (sdpFormat) =>
                windowsVideoEndPoint.SetVideoSourceFormat(SDPMediaFormatInfo.GetVideoCodecForSdpFormat(sdpFormat.First().FormatCodec));
            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    await audioSource.StartAudio();
                    await windowsVideoEndPoint.StartVideo();
                    await testPatternSource.StartVideo();
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await testPatternSource.CloseVideo();
                    await windowsVideoEndPoint.CloseVideo();
                    await audioSource.CloseAudio();
                }
            };

            // Diagnostics.
            pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            return pc;
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
````

Step 2:

Create an HTML file, paste the contents below into it, open it in a browser that supports WebRTC and finally press the `start` button.

````html
<!DOCTYPE html>
<head>
    <meta charset="UTF-8">

    <script type="text/javascript">

        const STUN_URL = "stun:stun.sipsorcery.com";
        const WEBSOCKET_URL = "ws://127.0.0.1:8081/"

        var pc, ws;

        async function start() {
            pc = new RTCPeerConnection({ iceServers: [{ urls: STUN_URL }] });

            pc.ontrack = evt => document.querySelector('#videoCtl').srcObject = evt.streams[0];
            pc.onicecandidate = evt => evt.candidate && ws.send(JSON.stringify(evt.candidate));
            await navigator.mediaDevices.getUserMedia({ video: true, audio: true })
                .then(stm => stm.getTracks().forEach(track => pc.addTrack(track, stm)));

            ws = new WebSocket(document.querySelector('#websockurl').value, []);
            ws.onmessage = async function (evt) {
                if (/^[\{"'\s]*candidate/.test(evt.data)) {
                    pc.addIceCandidate(JSON.parse(evt.data));
                }
                else {
                    await pc.setRemoteDescription(new RTCSessionDescription(JSON.parse(evt.data)));
                    pc.createAnswer()
                        .then((answer) => pc.setLocalDescription(answer))
                        .then(() => ws.send(JSON.stringify(pc.localDescription)));
                }
            };
        };

        async function closePeer() {
            pc.getSenders().forEach(sender => {
                sender.track.stop();
                pc.removeTrack(sender);
            });
            await pc.close();
            await ws.close();
        };

    </script>
</head>
<body>
    <video controls autoplay="autoplay" id="videoCtl" width="640" height="480"></video>
    <div>
        <input type="text" id="websockurl" size="40" />
        <button type="button" class="btn btn-success" onclick="start();">Start</button>
        <button type="button" class="btn btn-success" onclick="closePeer();">Close</button>
    </div>
</body>

<script>
    document.querySelector('#websockurl').value = WEBSOCKET_URL;
</script>
````

Result:

If successful the browser should display a test pattern image and play a music sample. The `dotnet` console should display a steady stream of RTCP reports.

````bash
...
[19:40:25 DBG] STUN BindingRequest received from 192.168.0.50:57681.
[19:40:26 DBG] RTCP Receive for video from 192.168.11.50:57681
SDES: SSRC=3458092865, CNAME=5+ksoe4uBNfyl5u5
Sender: SSRC=3458092865, PKTS=18, BYTES=16392
[19:40:26 DBG] RTCP Receive for video from 192.168.11.50:57681
Receiver: SSRC=3458092865
 RR: SSRC=852075017, LOST=0, JITTER=390
[19:40:26 DBG] STUN BindingRequest received from 192.168.11.50:57681.
[19:40:27 DBG] RTCP Receive for video from 192.168.11.50:57681
SDES: SSRC=3458092865, CNAME=5+ksoe4uBNfyl5u5
Sender: SSRC=3458092865, PKTS=46, BYTES=39676
[19:40:27 DBG] RTCP Receive for video from 192.168.11.50:57681
Receiver: SSRC=3458092865
 RR: SSRC=852075017, LOST=0, JITTER=368
[19:40:27 DBG] STUN BindingRequest received from 192.168.11.50:57681.
[19:40:27 DBG] RTCP Receive for audio from 192.168.11.50:57681
SDES: SSRC=1049319500, CNAME=5+ksoe4uBNfyl5u5
Sender: SSRC=1049319500, PKTS=106, BYTES=16960
[19:40:27 DBG] STUN BindingRequest received from 192.168.0.50:57681.
[19:40:27 DBG] RTCP Receive for video from 192.168.11.50:57681
Receiver: SSRC=3458092865
 RR: SSRC=852075017, LOST=0, JITTER=419
 ...
````

The [examples folder](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples) contains sample code to demonstrate other common WebRTC cases.

