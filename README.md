| Target        | SIPSorcery    | Examples     | Softphone |
| --------------| ------------- |:-------------|:--------- |
| net46        | [![Build status](https://ci.appveyor.com/api/projects/status/1prvhq7jyw0s5fb1/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery/branch/master) | | |
| netstandard2.0 | ![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-std20/badge.svg) |  |  |
| dotnetcore3.1 | ![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-core31/badge.svg) | ![](https://github.com/sipsorcery/sipsorcery/workflows/examples-core31/badge.svg) <br> [![Examples build status](https://ci.appveyor.com/api/projects/status/4myf11mda0p69ysm/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-mre1o/branch/master) | [![Softphone build status](https://ci.appveyor.com/api/projects/status/xx1bcttkk4gbrd3y/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-0p6s4/branch/master) |


This repository contains the source for a C# .NET library with full support for the Session Initiation Protocol [(SIP)](https://tools.ietf.org/html/rfc3261) and the Real-time Transport Protocol [(RTP)](https://tools.ietf.org/html/rfc3550). 

This library does NOT provide any media (audio and video) handling. For Windows the companion [SIPSorceryMedia](https://github.com/sipsorcery/sipsorcery-media) library provides audio & video functions for rendering & capture. This project can be used for SIP signalling and to send and receive RTP packets.

**NEW (Feb 2020)**: Pre-release support for Web Real-Time Communication [(WebRTC)](https://www.w3.org/TR/webrtc/) for early adopters. See [Getting Started WebRTC](#getting-started-webrtc).

## Installation

The library is compliant with .NET Standard 2.0, .Net Core 3.1 and .NET Framework 4.6. It is available via NuGet.

For .NET Core:

````bash
dotnet add package SIPSorcery
````

With Visual Studio Package Manager Console (or search for [SIPSorcery on NuGet](https://www.nuget.org/packages/SIPSorcery/)):

````ps1
Install-Package SIPSorcery
````

## Documentation

Class reference documentation and articles explaining common usage are available at [https://sipsorcery.github.io/sipsorcery/](https://sipsorcery.github.io/sipsorcery/).

## Getting Started SIP/VoIP

The simplest possible example to place an audio-only SIP call is shown below. This example relies on the Windows specific `SIPSorceryMedia` library to play the received audio.

````bash
dotnet new console --name SIPGetStarted
cd SIPGetStarted
dotnet add package SIPSorcery -v 4.0.28-pre
dotnet add package SIPSorceryMedia -v 4.0.28-pre
edit Program.cs and paste in the contents below
dotnet run
````

````csharp
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Media;

namespace SIPGetStarted
{
    class Program
    {
		 private static string DESTINATION = "time@sipsorcery.com";
		
        static async Task Main(string[] args)
        {
            Console.WriteLine("SIP Get Started");
			
			var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            var rtpSession = new RtpAVSession(AddressFamily.InterNetwork, new AudioOptions { AudioSource = AudioSourcesEnum.Microphone }, null);

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
            Console.WriteLine($"Call result {((callResult) ? "success" : "failure")}.");
        }
    }
}
````

The [GetStarted](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted) example contains the full source the example above.

The three key classes in the above example are described in dedicated articles:

 - [SIPTransport](https://sipsorcery.github.io/sipsorcery/articles/transport.html),
 - [SIPUserAgent](https://sipsorcery.github.io/sipsorcery/articles/sipuseragent.html),
 - [RTPSession](https://sipsorcery.github.io/sipsorcery/articles/rtpsession.html) base class for `RtpAVSession`.

The [examples folder](https://github.com/sipsorcery/sipsorcery/tree/master/examples) contains sample code to demonstrate other common SIP use cases such as:

 - Attended Transfers,
 - Blind Transfers,
 - Call Hold,
 - Sending DTMF tones.

 The full list of SIP examples available is:

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

The core of the code required to establish a WebRTC connection is demonstrated below. The code shown will build but will not establish a connection due to the lack of a way to exchange the SDP offers and answers between peers. A full working example with a web socket signalling mechanism is available in the [WebRTCTestPatternServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCTestPatternServer) example.

````bash
dotnet new console --name WebRTCGetStarted
cd WebRTCGetStarted
dotnet add package SIPSorcery -v 4.0.28-pre
dotnet add package SIPSorceryMedia -v 4.0.28-pre
edit Program.cs and paste in the contents below
dotnet run
````

````csharp
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia;

namespace WebRTCGetStarted
{
    class Program
    {
		private const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
        private const string DTLS_KEY_PATH = "certs/localhost_key.pem";
        private const string DTLS_CERTIFICATE_FINGERPRINT = "sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";
		
        static async Task Main(string[] args)
        {
            Console.WriteLine("Get Started WebRTC");
			
            var webRtcSession = new WebRtcSession(
                AddressFamily.InterNetwork,
                DTLS_CERTIFICATE_FINGERPRINT,
                null,
                null);

            MediaStreamTrack videoTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
            webRtcSession.addTrack(videoTrack);
			
			var offerSdp = await webRtcSession.createOffer(null);
            webRtcSession.setLocalDescription(new RTCSessionDescription { sdp = offerSdp, type = RTCSdpType.offer });
			
			// At this point the SDP offer and answer need to be exchanged with the remote peer.
			
			var answerSdp = SDP.ParseSDPDescription(sdpAnswer);
            webRtcSession.setRemoteDescription(new RTCSessionDescription { sdp = answerSdp, type = RTCSdpType.answer }); 
			
			var dtls = new DtlsHandshake(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);
            webRtcSession.OnClose += (reason) => dtls.Shutdown();
            
            dtls.DoHandshakeAsServer((ulong)webRtcSession.GetRtpChannel(SDPMediaTypesEnum.audio).RtpSocket.Handle);

            if (dtls.IsHandshakeComplete())
            {
                var srtpSendContext = new Srtp(dtls, false);
                var srtpReceiveContext = new Srtp(dtls, true);

                webRtcSession.SetSecurityContext(
                    srtpSendContext.ProtectRTP,
                    srtpReceiveContext.UnprotectRTP,
                    srtpSendContext.ProtectRTCP,
                    srtpReceiveContext.UnprotectRTCP);

                Console.WriteLine("DTLS handshake completed.");
            }
            else
            {
               Console.WriteLine("DTLS handshake failed.");
            }
			
			// If the DTLS key exchange succeeded then secure RTP packets can now be exchanged between the peers.
        }
    }
}
````

The key class for using WebRTC is described in an article:
 
  - [WebRTCSession](https://sipsorcery.github.io/sipsorcery/articles/webrtcsession.html)

The full list of WebRTC examples available is:

 - [WebRTCTestPatternServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCTestPatternServer): The simplest example. This program serves up a test pattern video stream to a WebRTC peer.
 - [WebRTCServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCServer): This example extends the test pattern example and can act as a media source for a peer. It has two source options:
  - An mp4 file.
  - Capture devices (webcam and microphone).
 The example includes an html file which runs in a Browser and will connect to a sample program running on the same machine.
- [WebRTCReceiver](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCReceiver): A receive only example. It attempts to connect to a WebRTC peer and display the video stream that it receives.

**The WebRtcSession class and all WebRTC functionality in this library are still under heavy development. There are large blocks of functionality still missing, particularly ICE and codec support. All issues and PR's are very welcome.**
