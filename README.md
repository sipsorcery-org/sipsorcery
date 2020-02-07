| Target        | SIPSorcery    | Examples     | Softphone |
| --------------| ------------- |:-------------|:--------- |
| net452        | [![Build status](https://ci.appveyor.com/api/projects/status/1prvhq7jyw0s5fb1/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery/branch/master) | | |
| netstandard2.0 | ![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-std20/badge.svg) |  |  |
| dotnetcore3.1 | ![](https://github.com/sipsorcery/sipsorcery/workflows/sipsorcery-core31/badge.svg) | ![](https://github.com/sipsorcery/sipsorcery/workflows/examples-core31/badge.svg) <br> [![Examples build status](https://ci.appveyor.com/api/projects/status/4myf11mda0p69ysm/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-mre1o/branch/master) | [![Softphone build status](https://ci.appveyor.com/api/projects/status/xx1bcttkk4gbrd3y/branch/master?svg=true)](https://ci.appveyor.com/project/sipsorcery/sipsorcery-0p6s4/branch/master) |


This repository contains the source for a C# .NET library with full support for the Session Initiation Protocol [(SIP)](https://tools.ietf.org/html/rfc3261) and the Real-time Transport Protocol [(RTP)](https://tools.ietf.org/html/rfc3550). 

This library does NOT provide any media (audio and video) handling. There are some limited capabilities in the separate [SIPSorceryMedia](https://github.com/sipsorcery/sipsorcery-media) project but they are Windows specific and not suitable for production. This project can be used for SIP signalling and to send and receive RTP packets. To playback audio/video additional libraries such as [NAudio](https://github.com/naudio/NAudio) are required.

**NEW (Feb 2020)**: Pre-release support for Web Real-Time Communication [(WebRTC)](https://www.w3.org/TR/webrtc/) for early adopters. See [Getting Started WebRTC](#getting-started-webrtc).

Note unlike a lot of WebRTC libraries this one is not wrapping [Google's WebRTC library](https://webrtc.googlesource.com/src/+/refs/heads/master/docs/native-code/index.md) and it is also currently missing large blocks of functionality compared to Google's. If you require a dotnet library that provides functionality equivalent to Google's take a look at [Microsoft's MixedReality-WebRTC project](https://github.com/microsoft/MixedReality-WebRTC) (despite the name it's not just for Hololens).

## Installation

The library is compliant with .NET Standard 2.0, .Net Core 3.1 and .NET Framework 4.5.2. It is available via NuGet.

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

## Getting Started WebRTC

Install both the `SIPSorcery` and `SIPSorceryMedia` nuget packages. 

````bash
dotnet add package SIPSorcery --version "4.0.13-pre"
dotnet add package SIPSorceryMedia --version "4.0.13-pre"
````

The `SIPSorceryMedia` package wraps access to a number of open source libraries to provide the underlying WebRTC infrastructure for `DTLS`, `SRTP`, `VPX Codecs` as well as the `Windows Media Foundation` for audio/video capture device access. 

The `SIPSorcery.Net.WebRtcSession` class can be used to create and manage connections with a WebRTC peer which will typically be in the form of a Browser such as Chrome or Firefox.

There are 3 example applications which demonstrate different use cases:

* [WebRTCTestPatternServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCTestPatternServer): The simplest example. This program serves up a test pattern video stream to a WebRTC peer.

![Test pattern example screenshot](https://github.com/sipsorcery/sipsorcery/blob/master/img/webrtctestpattern_screenshot.png)

* [WebRTCServer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCServer): This example extends the test pattern example and can act as a media source for a peer. It has two source options:
  - An mp4 file.
  - Capture devices (webcam and microphone).
The example includes an html file which runs in a Browser and will connect to a sample program running on the same machine.

![MP4 server example screenshot](https://github.com/sipsorcery/sipsorcery/blob/master/img/webrtcsvr_screenshot.png)

* [WebRTCReceiver](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCReceiver): A receive only example. It attempts to connect to a WebRTC peer and display the video stream that it receives.

![Receive example screenshot](https://github.com/sipsorcery/sipsorcery/blob/master/img/webrtcrecv_screenshot.png)

**The WebRtcSession class and all WebRTC functionality in this library are still under heavy development. There are large blocks of functionality still missing, particularly ICE and codec support. All issues and PR's are very welcome.**

## Getting Started

The [examples folder](https://github.com/sipsorcery/sipsorcery/tree/master/examples) contains full sample code designed to demonstrate some common use cases. The [GetStarted](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted) example which places a SIP call to `sip:time@sipsorcery.com` is the best place to start and the main program is shown below.

````csharp
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using NAudio.Wave;
using Serilog;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;

namespace demo
{
    class Program
    {
        private static WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);  // PCMU format used by both input and output streams.
        private static int INPUT_SAMPLE_PERIOD_MILLISECONDS = 20;           // This sets the frequency of the RTP packets.
        private static string DESTINATION = "time@sipsorcery.com";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            AddConsoleLogger();

            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            var rtpSession = new RTPMediaSession((int)SDPMediaFormatsEnum.PCMU, AddressFamily.InterNetwork);

            // Connect audio devices to RTP session.
            WaveInEvent microphone = GetAudioInputDevice();
            var speaker = GetAudioOutputDevice();
            ConnectAudioDevicesToRtp(rtpSession, microphone, speaker);

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);

            if(callResult)
            {
                Console.WriteLine("Call attempt successful.");
                microphone.StartRecording();
            }
            else
            {
                Console.WriteLine("Call attempt failed.");
            }
          
            Console.WriteLine("press any key to exit...");
            Console.Read();

            if (userAgent.IsCallActive)
            {
                Console.WriteLine("Hanging up.");
                userAgent.Hangup();
            }

            // Clean up.
            microphone.StopRecording();
            sipTransport.Shutdown();
            SIPSorcery.Net.DNSManager.Stop();
        }

        /// <summary>
        /// Connects the RTP packets we receive to the speaker and sends RTP packets for microphone samples.
        /// </summary>
        /// <param name="rtpSession">The RTP session to use for sending and receiving.</param>
        /// <param name="microphone">The default system  audio input device found.</param>
        /// <param name="speaker">The default system audio output device.</param>
        private static void ConnectAudioDevicesToRtp(RTPMediaSession rtpSession, WaveInEvent microphone, BufferedWaveProvider speaker)
        {
            // Wire up the RTP send session to the audio input device.
            uint rtpSendTimestamp = 0;
            microphone.DataAvailable += (object sender, WaveInEventArgs args) =>
            {
                byte[] sample = new byte[args.Buffer.Length / 2];
                int sampleIndex = 0;

                for (int index = 0; index < args.BytesRecorded; index += 2)
                {
                    var ulawByte = NAudio.Codecs.MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(args.Buffer, index));
                    sample[sampleIndex++] = ulawByte;
                }

                if (rtpSession.DestinationEndPoint != null)
                {
                    rtpSession.SendAudioFrame(rtpSendTimestamp, sample);
                    rtpSendTimestamp += (uint)(8000 / microphone.BufferMilliseconds);
                }
            };

            // Wire up the RTP receive session to the audio output device.
            rtpSession.OnReceivedSampleReady += (sample) =>
            {
                for (int index = 0; index < sample.Length; index++)
                {
                    short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                    byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                    speaker.AddSamples(pcmSample, 0, 2);
                }
            };
        }

        /// <summary>
        /// Get the audio output device, e.g. speaker.
        /// Note that NAudio.Wave.WaveOut is not available for .Net Standard so no easy way to check if 
        /// there's a speaker.
        /// </summary>
        private static BufferedWaveProvider GetAudioOutputDevice()
        {
            WaveOutEvent waveOutEvent = new WaveOutEvent();
            var waveProvider = new BufferedWaveProvider(_waveFormat);
            waveProvider.DiscardOnBufferOverflow = true;
            waveOutEvent.Init(waveProvider);
            waveOutEvent.Play();

            return waveProvider;
        }

        /// <summary>
        /// Get the audio input device, e.g. microphone. The input device that will provide 
        /// audio samples that can be encoded, packaged into RTP and sent to the remote call party.
        /// </summary>
        private static WaveInEvent GetAudioInputDevice()
        {
            if (WaveInEvent.DeviceCount == 0)
            {
                throw new ApplicationException("No audio input devices available. No audio will be sent.");
            }
            else
            {
                WaveInEvent waveInEvent = new WaveInEvent();
                WaveFormat waveFormat = _waveFormat;
                waveInEvent.BufferMilliseconds = INPUT_SAMPLE_PERIOD_MILLISECONDS;
                waveInEvent.NumberOfBuffers = 1;
                waveInEvent.DeviceNumber = 0;
                waveInEvent.WaveFormat = waveFormat;

                return waveInEvent;
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}
````

#### SIP Transport Layer

To use the SIP functionality the first step is to initialise the `SIPTransport` class. It takes care of things like retransmitting requests and responses, DNS resolution, selecting the next hop for requests, matching SIP messages to transactions and more.

The `SIPTransport` class can have multiple SIP channels added to it. A SIP channel is roughly the equivalent to the HTTP connection between a Web Browser and Server. It expects all packets received to be either a SIP request or response. The types of SIP channels supported are UDP, TCP, TLS and Web Sockets.

The code below shows how to create a `SIPTransport` instance and add a single UDP channel to it. If no channel is added to the transport it will attempt to create them on demand.

````csharp
var sipTransport = new SIPTransport();
var sipChannel = new SIPUDPChannel(IPAddress.Loopback, 5060);
sipTransport.AddSIPChannel(sipChannel);
````

To shutdown the `SIPTransport` use:

````csharp
sipTransport.Shutdown();
````

#### SIP User Agent and RTP Session

The easiest way to make use of the SIP Transport layer is to use it with a `SIPUserAgent` object. The `SIPUserAgent` takes care of the SIP signalling for most common SIP functions. An `RTPMediaSession` is needed to handle the sending and receiving of `RTP` packets and the media (audio/video) information within them.

````csharp
var userAgent = new SIPUserAgent(sipTransport, null);
var rtpSession = new RTPMediaSession((int)SDPMediaFormatsEnum.PCMU, AddressFamily.InterNetwork);
````

A call can then be placed using the `SIPUserAgent.Call` method and if successfully answered then sampling of the local audio input device can be started.

````csharp
// Place the call and wait for the result.
bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);

if(callResult)
{
	Console.WriteLine("Call attempt successful.");
	microphone.StartRecording();
}
else
{
	Console.WriteLine("Call attempt failed.");
}
````

## Next Steps

Additional example programs are provided to demonstrate how to use the SIPSorcery library in some common scenarios. The example programs are in the `examples` folder.

* [Get Started](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStarted): Simplest example. Demonstrates how to place a SIP call.

* [SIP Proxy](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPProxy): Very rudimentary example for a SIP Proxy and SIP Registrar. 

* [Registration Client](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentRegister): Demonstrates how to use the `SIPRegistrationUserAgent` class to register with a SIP Registrar server.

* [SIP Call Client](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentClient): Demonstrates how to use `SIPClientUserAgent` class to place a call to a SIP server user agent.
 
* [SIP Call Server](https://github.com/sipsorcery/sipsorcery/tree/master/examples/UserAgentServer): Demonstrates how to use the `SIPServerUserAgent` class to receive a call from a SIP client user agent.
 
* [SoftPhone](https://github.com/sipsorcery/sipsorcery/tree/master/examples/Softphone): A very rudimentary SIP softphone implementation.

* [Get Started Web Socket](https://github.com/sipsorcery/sipsorcery/tree/master/examples/GetStartedWebSocket): An example of how to create a web socket listener to send and receive SIP messages. An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/websocket-sipchannel.html).

* [STUN Server](https://github.com/sipsorcery/sipsorcery/tree/master/examples/StunServer): An example of how to create a basic STUN ([RFC3849](https://tools.ietf.org/html/rfc3489)) server. An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/stunserver.html).

* [Call Hold and Blind Transfer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/CallHoldAndTransfer): An example of how to place a call on hold and perform a blind transfer using a REFER request as specified in [RFC3515](https://tools.ietf.org/html/rfc3515). An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/callholdtransfer.html).

* [Call Attended Transfer](https://github.com/sipsorcery/sipsorcery/tree/master/examples/AttendedTransfer): An example of how to perform an attended transfer. An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/attendedtransfer.html).

* [Send DTMF (as RTP events)](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SendDtmf): An example of how to send DTMF tones using RTP events as specified in [RFC2833](https://tools.ietf.org/html/rfc2833). An explanation of the example is available [here](https://sipsorcery.github.io/sipsorcery/articles/senddtmf.html).

