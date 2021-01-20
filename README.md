| CI | win-x64 | linux-x64 | osx-x64 | Examples <br/> (win-x64) | Softphone <br/> (win-x64) |
|-|-|-|-|-|-|
| <sup>AppVeyor</sup> | [![Build status](https://ci.appveyor.com/api/projects/status/1prvhq7jyw0s5fb1/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery/branch/master) | [![Build status](https://ci.appveyor.com/api/projects/status/cark9l28ovb8o886/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-5aavr/branch/master) | [![Build status](https://ci.appveyor.com/api/projects/status/7mrg69mtolwceplg/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-jyl3x/branch/master) | [![Examples build status](https://ci.appveyor.com/api/projects/status/4myf11mda0p69ysm/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-mre1o/branch/master) | [![Softphone build status](https://ci.appveyor.com/api/projects/status/xx1bcttkk4gbrd3y/branch/master?svg=true&passingText=ok)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-0p6s4/branch/master) |
| <sup>GitHub Actions</sup> | ![](https://github.com/sipsorcery-org/sipsorcery/workflows/win-x64/badge.svg) | ![](https://github.com/sipsorcery-org/sipsorcery/workflows/linux-x64/badge.svg) | ![](https://github.com/sipsorcery-org/sipsorcery/workflows/osx-x64/badge.svg) | ![](https://github.com/sipsorcery-org/sipsorcery/workflows/egs-win-x64/badge.svg) | |
| <sup>Azure DevOps</sup>   | [![Build Status](https://dev.azure.com/aaronrc/SIPSorcery/_apis/build/status/sipsorcery.sipsorcery?branchName=master&jobName=Job&configuration=Job%20windows)](https://dev.azure.com/aaronrc/SIPSorcery/_build/latest?definitionId=3&branchName=master) | [![Build Status](https://dev.azure.com/aaronrc/SIPSorcery/_apis/build/status/sipsorcery.sipsorcery?branchName=master&jobName=Job&configuration=Job%20linux)](https://dev.azure.com/aaronrc/SIPSorcery/_build/latest?definitionId=3&branchName=master) | [![Build Status](https://dev.azure.com/aaronrc/SIPSorcery/_apis/build/status/sipsorcery.sipsorcery?branchName=master&jobName=Job&configuration=Job%20mac)](https://dev.azure.com/aaronrc/SIPSorcery/_build/latest?definitionId=3&branchName=master) | | |

**Update Jan 2021:** The repository URLs for this project have changed from `sipsorcery` to `sipsorcery-org`. A redirect currently exists but if you encounter any issues you can update your remote git URL from the command line using the commands below (adjust for the name of your remote repo):

`git remote set-url origin git@github.com:sipsorcery-org/sipsorcery.git`

or

`git remote set-url origin-http https://github.com/sipsorcery-org/sipsorcery`


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
   - [SIPSorceryMedia.Windows](https://github.com/sipsorcery-org/SIPSorceryMedia.Windows): Windows specific library that provides audio capture and playback. 
   - [SIPSorceryMedia.Encoders](https://github.com/sipsorcery-org/SIPSorceryMedia.Encoders): A Windows specific wrapper for the [VP8](https://www.webmproject.org/) video codec. The examples in this repository use it.
   - [SIPSorceryMedia.FFmpeg](https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg): An in-progress effort to provide cross platform audio, video and codec functions using PInvoke and [FFmpeg](https://ffmpeg.org/).
   - Others: **Contributions welcome**. Frequently requested are Xamarin Forms on Android/iOS and Unix (Linux and/or Mac). New implementations need to implement one or more of the Audio Sink/Source and/or Video Sink/Source interfaces from [SIPSorceryMedia.Abstractions](https://github.com/sipsorcery-org/SIPSorceryMedia.Abstractions/blob/master/src/V1/MediaEndPoints.cs).

 - This library provides only a small number of audio and video codecs (G711 and G722). Additional codecs, particularly video ones, require C or C++ libraries. An effort is underway to port the [VP8](https://www.webmproject.org/) video codec to C# see [VP8.Net](https://github.com/sipsorcery-org/VP8.Net).

## Installation

The library is compliant with .NET Standard 2.0 (encompassing .NET Core 2.0+) and .NET Framework 4.6.1 (theoretically also encompassed by `netstandard2.0` but set as an explicit target due to compatibility issues between the two). The demo applications mainly target .NET Core 3.1 with newer ones targeting .NET 5.0. It is available via NuGet.

For .NET Core:

````bash
dotnet add package SIPSorcery -v 5.0.3
````

With Visual Studio Package Manager Console (or search for [SIPSorcery on NuGet](https://www.nuget.org/packages/SIPSorcery/)):

````ps1
Install-Package SIPSorcery -v 5.0.3
````

## Documentation

Class reference documentation and articles explaining common usage are available at [https://sipsorcery-org.github.io/sipsorcery/](https://sipsorcery-org.github.io/sipsorcery/).

## Getting Started VoIP

The simplest possible example to place an audio-only SIP call is shown below. This example relies on the Windows specific `SIPSorceryMedia.Windows` library to play the received audio and only works on Windows (due to lack of .NET Core audio device support on non-Windows platforms).

````bash
dotnet new console --name SIPGetStarted -f netcoreapp3.1
cd SIPGetStarted
dotnet add package SIPSorcery -v 5.0.11-pre
dotnet add package SIPSorceryMedia.Windows -v 0.0.30-pre
# Paste the code below into Program.cs.
dotnet run
# If successful you will hear the current time read out.
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

The [GetStarted](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/SIPExamples/GetStarted) example contains the full source and project file for the example above.

The three key classes in the above example are described in dedicated articles:

 - [SIPTransport](https://sipsorcery-org.github.io/sipsorcery/articles/transport.html),
 - [SIPUserAgent](https://sipsorcery-org.github.io/sipsorcery/articles/sipuseragent.html),
 - [RTPSession](https://sipsorcery-org.github.io/sipsorcery/articles/rtpsession.html).

The [examples folder](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/SIPExamples) contains sample code to demonstrate other common SIP/VoIP cases.

## Getting Started WebRTC

The WebRTC specifications do not include directions about how signaling should be done (for VoIP the signaling protocol is SIP; WebRTC has no equivalent). The example below uses a simple JSON message exchange over web sockets for signaling. Part of the reason the `Getting Started WebRTC` is longer than the `Getting Started VoIP` example is the need for custom signaling.

The example requires two steps:

 - Run the `dotnet` console application,
 - Open an HTML page in a browser on the same machine.

 The full project file and code are available at [WebRTC Get Started](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStarted).

The example relies on the Windows specific `SIPSorceryMedia.Encoders` package, which is mainly a wrapper around [libvpx](https://chromium.googlesource.com/webm/libvpx). Hopefully in the future there will be equivalent packages for other platforms.

**Step 1:**

````bash
dotnet new console --name WebRTCGetStarted -f net5.0
cd WebRTCGetStarted
dotnet add package SIPSorcery -v 5.0.11-pre
dotnet add package SIPSorceryMedia.Encoders -v 0.0.10-pre
# Paste the code below into Program.cs.
dotnet run
````

````csharp
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Encoders;
using WebSocketSharp.Server;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;

        static void Main()
        {
            Console.WriteLine("WebRTC Get Started");

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = () => CreatePeerConnection());
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");

            Console.WriteLine("Press any key to hangup and exit.");
            Console.ReadLine();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            var pc = new RTCPeerConnection(null);

            var testPatternSource = new VideoTestPatternSource(new VpxVideoEncoder());

            MediaStreamTrack videoTrack = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);

            testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
            pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());

            pc.onconnectionstatechange += async (state) =>
            {
                Console.WriteLine($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await testPatternSource.CloseVideo();
                    testPatternSource.Dispose();
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    await testPatternSource.StartVideo();
                }
            };

            // Diagnostics.
            pc.OnReceiveReport += (re, media, rr) => Console.WriteLine($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            pc.OnSendReport += (media, sr) => Console.WriteLine($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => Console.WriteLine($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state change to {state}.");

            return Task.FromResult(pc);
        }
    }
}
````

**Step 2:**

Create an HTML file, paste the contents below into it, open it in a browser that supports WebRTC and finally press the `start` button.

````html
<!DOCTYPE html>
<head>
    <script type="text/javascript">
        const WEBSOCKET_URL = "ws://127.0.0.1:8081/"

        var pc, ws;

        async function start() {
            pc = new RTCPeerConnection();

            pc.ontrack = evt => document.querySelector('#videoCtl').srcObject = evt.streams[0];
            pc.onicecandidate = evt => evt.candidate && ws.send(JSON.stringify(evt.candidate));

            // Diagnostics.
            pc.onicegatheringstatechange = () => console.log("onicegatheringstatechange: " + pc.iceGatheringState);
            pc.oniceconnectionstatechange = () => console.log("oniceconnectionstatechange: " + pc.iceConnectionState);
            pc.onsignalingstatechange = () => console.log("onsignalingstatechange: " + pc.signalingState);
            pc.onconnectionstatechange = () => console.log("onconnectionstatechange: " + pc.connectionState);

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

**Result:**

If successful the browser should display a test pattern image. The `dotnet` console should display a steady stream of RTCP reports that are being received from the Browser.

The [examples folder](https://github.com/sipsorcery-org/sipsorcery/tree/master/examples/WebRTCExamples) contains sample code to demonstrate other common WebRTC cases.

