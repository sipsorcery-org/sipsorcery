| Target        | SIPSorcery    | Examples <br/> (Windows Only)    | Softphone <br/> (Windows Only) |
| --------------| ------------- |:-------------|:--------- |
| net46        | [![Build status](https://ci.appveyor.com/api/projects/status/1prvhq7jyw0s5fb1/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery/branch/master) | | |
| netstandard2.0 | ![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-std20/badge.svg) |  |  |
| dotnetcore3.1 | <table><tr><td>Windows</td><td>![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-core31-win/badge.svg)</td></tr><tr><td>MacOS</td><td>![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-core31-mac/badge.svg)</td></tr><tr><td>Ubuntu</td><td>![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-core31-ubuntu/badge.svg)</td></tr></table> | ![](https://github.com/sipsorcery/sipsorcery/workflows/examples-core31/badge.svg) <br> [![Examples build status](https://ci.appveyor.com/api/projects/status/4myf11mda0p69ysm/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-mre1o/branch/master) | [![Softphone build status](https://ci.appveyor.com/api/projects/status/xx1bcttkk4gbrd3y/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-0p6s4/branch/master) |


This repository contains the source for a C# .NET library with full support for the Session Initiation Protocol [(SIP)](https://tools.ietf.org/html/rfc3261) and the Real-time Transport Protocol [(RTP)](https://tools.ietf.org/html/rfc3550). 

**This library does not provide any interface for audio/video capturing/rendering. For Windows the companion [SIPSorceryMedia](https://github.com/sipsorcery/sipsorcery-media) library does provide some audio & video functions. Supporting cross platform audio/video with .NET Core is a major project by itself.**

**NEW (Jul 2020)**: Pre-release support for Web Real-Time Communication [(WebRTC)](https://www.w3.org/TR/webrtc/) now includes a C# DTLS/SRTP implementation (native libraries no longer required) thanks to @rafcsoares. See [Getting Started WebRTC](#getting-started-webrtc).

## Installation

The library is compliant with .NET Standard 2.0, .Net Core 3.1 and .NET Framework 4.6. It is available via NuGet.

For .NET Core:

````bash
dotnet add package SIPSorcery -v 4.0.58-pre
````

With Visual Studio Package Manager Console (or search for [SIPSorcery on NuGet](https://www.nuget.org/packages/SIPSorcery/)):

````ps1
Install-Package SIPSorcery -v 4.0.58-pre
````

## Documentation

Class reference documentation and articles explaining common usage are available at [https://sipsorcery.github.io/sipsorcery/](https://sipsorcery.github.io/sipsorcery/).

## Getting Started SIP/VoIP

The simplest possible example to place an audio-only SIP call is shown below. This example relies on the Windows specific `SIPSorceryMedia` library to play the received audio and only works on Windows (due to lack of audio device support on non-Windows platforms).

````bash
dotnet new console --name SIPGetStarted
cd SIPGetStarted
dotnet add package SIPSorcery -v 4.0.58-pre
dotnet add package SIPSorceryMedia -v 4.0.58-pre
code . # If you have Visual Studio Code https://code.visualstudio.com installed
# edit Program.cs and paste in the contents below.
dotnet run
# if successful you will hear the current time read out.
ctrl-c
````

````csharp
using System;
using System.Threading.Tasks;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;

namespace SIPGetStarted
{
    class Program
    {
         private static string DESTINATION = "time@sipsorcery.com";
        
        static async Task Main()
        {
            Console.WriteLine("SIP Get Started");
            
            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            var rtpSession = new RtpAVSession(new AudioOptions { AudioSource = AudioSourcesEnum.CaptureDevice }, null);

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
            Console.WriteLine($"Call result {((callResult) ? "success" : "failure")}.");
        }
    }
}
````

The [GetStarted](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted) example contains the full source and project file for the example above.

The three key classes in the above example are described in dedicated articles:

 - [SIPTransport](https://sipsorcery.github.io/sipsorcery/articles/transport.html),
 - [SIPUserAgent](https://sipsorcery.github.io/sipsorcery/articles/sipuseragent.html),
 - [RTPSession](https://sipsorcery.github.io/sipsorcery/articles/rtpsession.html) base class for `RtpAVSession`.

The [examples folder](https://github.com/sipsorcery/sipsorcery/tree/master/examples) contains sample code to demonstrate other common cases including:

  - [Get Started](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted): Simplest example. Demonstrates how to place a SIP call.
  - [Get Started Video](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStartedVideo): Adds video to the [Get Started](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted) example.
  - [SIP Proxy](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPProxy): Very rudimentary example for a SIP Proxy and SIP Registrar. 
  - [Registration Client](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentRegister): Demonstrates how to use the `SIPRegistrationUserAgent` class to register with a SIP Registrar server.
  - [SIP Call Client](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentClient): Demonstrates how to use `SIPClientUserAgent` class to place a call to a SIP server user agent.
  - [SIP Call Server](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentServer): Demonstrates how to use the `SIPServerUserAgent` class to receive a call from a SIP client user agent.
  - [SoftPhone](https://github.com/sipsorcery/sipsorcery/tree/master/examples/Softphone): A very rudimentary SIP softphone implementation.
  - [Get Started Web Socket](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStartedWebSocket): An example of how to create a web socket listener to send and receive SIP messages. An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/websocket-sipchannel.html).
  - [STUN Server](https://github.com/sipsorcery/sipsorcery/tree/master/examples/StunServer): An example of how to create a basic STUN ([RFC3849](https://tools.ietf.org/html/rfc3489)) server. An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/stunserver.html).
  - [Call Hold and Blind Transfer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/CallHoldAndTransfer): An example of how to place a call on hold and perform a blind transfer using a REFER request as specified in [RFC3515](https://tools.ietf.org/html/rfc3515). An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/callholdtransfer.html).
  - [Call Attended Transfer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/AttendedTransfer): An example of how to perform an attended transfer. An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/attendedtransfer.html).
  - [Send DTMF (as RTP events)](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SendDtmf): An example of how to send DTMF tones using RTP events as specified in [RFC2833](https://tools.ietf.org/html/rfc2833). An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/senddtmf.html).

## Getting Started WebRTC

The core of the code required to establish a WebRTC connection is demonstrated below. The code shown will build but will not establish a connection due to no mechanism to exchange the SDP offer and answer between peers. A full working example with a web socket signalling mechanism is available in the [WebRTCTestPatternServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCTestPatternServer) example.

If you are familiar with the [WebRTC javascript API](https://www.w3.org/TR/webrtc/) the API in this project aims to be as close to it as possible.

````bash
dotnet new console --name WebRTCGetStarted
cd WebRTCGetStarted
dotnet add package SIPSorcery -v 4.0.58-pre
dotnet add package SIPSorceryMedia -v 4.0.58-pre
code . # If you have Visual Studio Code (https://code.visualstudio.com) installed
# edit Program.cs and paste in the contents below.
dotnet run
````

````csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIPSorcery.Net;

namespace WebRTCGetStarted
{
    class Program
    {
        static async Task Main()
        {
            Console.WriteLine("Get Started WebRTC");
            
            var pc = new RTCPeerConnection(null);

            MediaStreamTrack videoTrack = new MediaStreamTrack(
              SDPMediaTypesEnum.video, 
              false, 
              new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, 
              MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);

            pc.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state change to {state}.");
            pc.onconnectionstatechange += (state) => Console.WriteLine($"Peer connection state change to {state}.");

            var offerSdp = pc.createOffer(null);
            await pc.setLocalDescription(offerSdp);

            Console.ReadLine();
        }
    }
}
````

Some of the WebRTC examples available are:

 - [WebRTCTestPatternServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCTestPatternServer): The simplest example. This program serves up a test pattern video stream to a WebRTC peer.
 - [WebRTCServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCServer): This example extends the test pattern example and can act as a media source for a peer. It has two source options:
   - An mp4 file.
   - Capture devices (webcam and microphone).
 The example includes an html file which runs in a Browser and will connect to a sample program running on the same machine.
- [WebRTCReceiver](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCReceiver): A receive only example. It attempts to connect to a WebRTC peer and display the video stream that it receives.

#### SIPSorceryMedia Install

.NET Core does not provide any audio or video capture capabilities nor any audio rendering function (UWP does and there are some [tricks](https://blogs.windows.com/windowsdeveloper/2017/01/25/calling-windows-10-apis-desktop-application/) to get at its API but it's fragile). A lot of the uses for SIP and WebRTC revolve around such capabilities and functions. A companion Windows specific library which fills this gap is maintained at [SIPSorceryMedia](https://github.com/sipsorcery/sipsorcery-media). For non-Windows applications there is no known .NET Core library that provides audio and video functions.

The [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) class from the `SIPSorceryMedia` library wraps the audio and video functions and integrates with the [SIPUserAgent](https://sipsorcery.github.io/sipsorcery/api/SIPSorcery.SIP.App.SIPUserAgent.html) class for ease of use.

**Note that the `RtpAVSession` class is only available as a pre-release in version 4.0.28-pre and greater.**

The `SIPSorceryMedia` library is compliant with .NET Core 3.1. It is available via NuGet:

For .NET Core:

````bash
dotnet add package SIPSorceryMedia -v 4.0.58-pre
````

With Visual Studio Package Manager Console (or search for [SIPSorceryMedia on NuGet](https://www.nuget.org/packages/SIPSorceryMedia/)):

````ps1
Install-Package SIPSorceryMedia -v 4.0.58-pre
````
