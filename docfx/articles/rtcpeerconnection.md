# WebRTC RTCPeerConnection

The most important class in the `SIPSorcery` library for `WebRTC` is @"SIPSorcery.Net.RTCPeerConnection". It closely follow the [W3 RTCPeerConnection Interface](https://www.w3.org/TR/webrtc/#rtcpeerconnection-interface).

`WebRTC` has no equivalent of `SIP` signaling. Instead the @"SIPSorcery.Net.RTCPeerConnection" is an an enhanced @"SIPSorcery.Net.RTPSession". That means there is more work to create a `WebRTC` connection than a `SIP` call. The demo applications primarily use a simple web socket signaling layer. This is not part of any specification and can be replaced as needed.

## Usage

The steps involved in getting a WebRTC connection to a state where audio and video packets can be exchanged are:

 - Instantiate the @"SIPSorcery.Net.RTCPeerConnection" instance,
 - Add the audio and/or video tracks as required,
 - Call the `createOffer` method to acquire an SDP offer that can be sent to the remote peer,
 - Send the SDP offer and get the SDP answer from the remote peer (this exchange is not part of the WebRTC specification and can be done using any signalling layer, examples are SIP, web sockets etc),
 - Once the SDP exchange has occurred the ICE checks can start in order to establish the optimal network path between the two peers. ICE candidates typically need to be passed between peers using the signalling layer,
 - Once ICE has established a the DTLS handshake will occur,,
 - If the DTLS handshake is successful the keying material it produces is used to initialise the SRTP contexts,
 - After the SRTP contexts are initialised the RTP media and RTCP packets can be exchanged in the normal manner.

The code snippets below are from the [WebRTC Get Started demo](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStarted).

The demo establishes a `WebRTC` peer connection with a browser and sends audio and video. The demo program uses a video test pattern and for pre-record music file for its sources. This is because the browser will need to use the default system webcam and microphone.

See the [WebRTC Receiver demo](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCReceiver) for an example on how to receive audio and video from a browser.

**Step 1:** Some kind of signaling layer needs to be established so that the `WebRTC` peers can exchange their SDP payloads and ICE candidates. The snippet below creates a web socket server that can send and receive JSON encoded messages.

The web socket server will try and establish an `RTCPeerConnection` with any web socket client that connects.

The @"SIPSorcery.Net.WebRTCWebSocketPeer" is a helper class for working with web sockets and `WebRTC`. It is NOT part of the `WebRTC` specification. It's role is to pass JSON messages between the two peers.

````csharp
using System;
using System.Net;
using SIPSorcery.Net;
using WebSocketSharp.Server;

const int WEBSOCKET_PORT = 8081;

// Start web socket.
Console.WriteLine("Starting web socket server...");
var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
webSocketServer.Start();

Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");

````

**Step 2:** When a web socket client connects the `CreatePeerConnection` method will be called to create and configure a new @"SIPSorcery.Net.RTCPeerConnection".

The snippet below shows how to create and configure the @"SIPSorcery.Net.RTCPeerConnection". The code is not a concise as the equivalent `SIP` example as again `WebRTC` does not provide a signaling to abstract away the details, however, the logic can be broken down into manageable chunks and anyone familiar with the [RTCPeerConnection javascript]((https://www.w3.org/TR/webrtc/#rtcpeerconnection-interface) API should find it similar.

````csharp
using System;
using System.Collections.Generic;
using SIPSorcery.Media;
using System.Net;
using SIPSorcery.Net;

private const string STUN_URL = "stun:stun.sipsorcery.com";

private static RTCPeerConnection CreatePeerConnection()
{
    RTCConfiguration config = new RTCConfiguration
    {
        iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
    };
    var pc = new RTCPeerConnection(config);

    var testPatternSource = new VideoTestPatternSource();
    var windowsVideoEndPoint = new SIPSorceryMedia.Windows.WindowsVideoEndPoint(true);
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
        Console.WriteLine($"Peer connection state change to {state}.");

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
    pc.OnReceiveReport += (re, media, rr) => Console.WriteLine($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
    pc.OnSendReport += (media, sr) => Console.WriteLine($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
    pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => Console.WriteLine($"STUN {msg.Header.MessageType} received from {ep}.");
    pc.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state change to {state}.");

    return pc;
}
````

**Step 2.1:** Create the @"SIPSorcery.Net.RTCPeerConnection" object.

Various configuration options can be supplied when creating the @"SIPSorcery.Net.RTCPeerConnection". The most important option is the list of ICE (STUN and TURN) servers. The list of ICE servers can typically be left empty if the peer connection is between two applications on the same machine or network.

````csharp
using System.Collections.Generic;
using SIPSorcery.Net;

const string STUN_URL = "stun:stun.sipsorcery.com";

RTCConfiguration config = new RTCConfiguration
{
    iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
};
var pc = new RTCPeerConnection(config);
````

**Step 2.2:** Set up the audio and video end points. The approach to connecting audio and video end points to the @"SIPSorcery.Net.RTCPeerConnection" is identical to connecting them to the [VoIP Media Session](voipmediasession.md), also see [overview](index.md).

In the snippet below a video test pattern source is created. It generates a video stream from a static test pattern image overlaid with a constantly updated timestamp. A `WindowsVideoEndPoint` from the [SIPSorceryMedia.Windows](https://github.com/sipsorcery/SIPSorceryMedia.Windows) library is also created. It supplies the `VP8` video encoding functionality for the test pattern images and also decoding for the `VP8` video samples received from the browser.

A pre-recorded music file source is also created to send audio samples to the browser.

````csharp
using System.Collections.Generic;
using SIPSorcery.Media;
using SIPSorcery.Net;

var testPatternSource = new VideoTestPatternSource();
var windowsVideoEndPoint = new SIPSorceryMedia.Windows.WindowsVideoEndPoint(true);
var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
````

**Step 2.3:** Add the media to the @"SIPSorcery.Net.RTCPeerConnection" object.

The details about the audio and video streams are needed to generate an SDP offer. Adding the media tracks gives the @"SIPSorcery.Net.RTCPeerConnection" the required information.

````csharp
MediaStreamTrack videoTrack = new MediaStreamTrack(windowsVideoEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
pc.addTrack(videoTrack);
MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
pc.addTrack(audioTrack);
````

**Step 2.4:** Hook up the audio and video event handlers.

This is how the audio and video samples get into and out of the @"SIPSorcery.Net.RTCPeerConnection" object. The @"SIPSorcery.Net.RTCPeerConnection" object can package and transport audio and video BUT it is not able to generate or consume the samples. Hooking up the media end points fixes that.

In the snippet below the test pattern source is hooked up to the `VP8` encoder from the `WindowsVideoEndPoint` (the `VP8` codec is from a native library) and then to the @"SIPSorcery.Net.RTCSession.SendVideo(uint,byte[])" which is inherited by @"SIPSorcery.Net.RTCPeerConnection".

`[test pattern source] --> [windows video end point VP8 encoder] --> [RTC peer connection SendVideo]`

For the audio source the @"SIPSorcery.Media.MuLawEncoder" encoder is very simple and does not require a native library.

`[music source] --> [RTC peer connection SendAudio]`

The final line in the snippet is not related to audio or video samples. Instead it's an event to tell the `WindowsVideoEndPoint` which codec was negotiated during the SDP exchange. In this cse it's a redundant call since the `WindowsVideoEndPoint` only supports `VP8`. The [SIPSorceryMedia.FFmpeg](https://github.com/sipsorcery/SIPSorceryMedia.FFmpeg) supports `VP8` and `H264` and it needs to be informed which codec the `WebRTC` peers agreed on.

````csharp
testPatternSource.OnVideoSourceRawSample += windowsVideoEndPoint.ExternalVideoSourceRawSample;
windowsVideoEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;
audioSource.OnAudioSourceEncodedSample += pc.SendAudio;
pc.OnVideoFormatsNegotiated += (sdpFormat) =>
    windowsVideoEndPoint.SetVideoSourceFormat(SDPMediaFormatInfo.GetVideoCodecForSdpFormat(sdpFormat.First().FormatCodec));
````

**Step 2.5:** Hook up the audio and video stop start calls to the @"SIPSorcery.Net.RTCPeerConnection" connection state changes.

The @"SIPSorcery.Net.RTCPeerConnection.onconnectionstatechange" is it's most important event. It indicates the connection state between the `WebRTC` peers. When it's connected audio and video can be exchanged. When it fails or is closed audio and video will stop.

In the snippet below the 3 media sources are all started when the connection state changes to `RTCPeerConnectionState.connected` and closed when it changes to `RTCPeerConnectionState.closed`.

````csharp
pc.onconnectionstatechange += async (state) =>
{
    Console.WriteLine($"Peer connection state change to {state}.");

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
````

**Step 3:** Generate and send SDP offer.

Once the @"SIPSorcery.Net.RTCPeerConnection" object has been created it is able to generate an SDP offer that can be sent to the remote peer.

In the [WebRTC Get Started demo](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCGetStarted) being described here that logic is not shown as it is contained in the @"SIPSorcery.Net.WebRTCWebSocketPeer".

The snippet below shows how to generate the SDP offer.

````csharp
var offerSdp = pc.createOffer(null);
await pc.setLocalDescription(offerSdp);
````

Once the offer has been acquired it needs to somehow be sent. This can be done using a signaling layer or even copied and pasted into the browser. For this demo JSON encoding over web sockets is used (see @"SIPSorcery.Net.WebRTCWebSocketPeer" for complete logic).

````csharp
 Context.WebSocket.Send(JsonConvert.SerializeObject(offerSdp,
                 new Newtonsoft.Json.Converters.StringEnumConverter()));
````

**Step 4:** Receive SDP answer from remote peer.

As with sending the SDP offer different approaches can be used to receive the offer. The snippet below shows receiving a JSON encoded web socket messages.

The answer needs to be set on the @"SIPSorcery.Net.RTCPeerConnection". There are a number of things that can go wrong during the SDP negotiation process, such as no compatible codecs. The @"SIPSorcery.Net.SetDescriptionResultEnum` result should always be checked to ensure the negotiation was successful.

````csharp
RTCSessionDescriptionInit descriptionInit = JsonConvert.DeserializeObject<RTCSessionDescriptionInit>(e.Data);
var result = _pc.setRemoteDescription(descriptionInit);
if (result != SetDescriptionResultEnum.OK)
{
    logger.LogWarning($"Failed to set remote description, {result}.");
    _pc.Close("failed to set remote description");
    this.Close();
}
````

**Step 5:** Exchange ICE candidates.

Once the SDP negotiation has succeeded the next step is to determine the optimal network path between the peers. To do that ICE candidates need to be exchanged. Once again this is a signaling function and can be done using different approaches. The code snippet below is using JSON over a web socket connection.

Sending an ICE candidate.

````csharp
pc.onicecandidate += (iceCandidate) =>
{
    if (pc.signalingState == RTCSignalingState.have_remote_offer)
    {
        Context.WebSocket.Send(iceCandidate.toJSON());
    }
};
````

Receiving an ICE candidate.

````csharp
 logger.LogDebug("Got remote ICE candidate.");
var iceCandidateInit = JsonConvert.DeserializeObject<RTCIceCandidateInit>(e.Data);
pc.addIceCandidate(iceCandidateInit);
````

**Step 6:** Wait for the connection to establish.

Once the ICE connection attempts start it should be a matter of waiting for it to complete. Once it does, and providing the audio and video event handlers were hooked up correctly, the browser should display the test pattern video and play music.

