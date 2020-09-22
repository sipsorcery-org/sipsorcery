# WebRTC RTCPeerConnection

The [RTCPeerConnection](xref:SIPSorcery.Net.RTCPeerConnection) class is intended to be the equivalent of the [RTCPeerConnection](https://www.w3.org/TR/webrtc/#rtcpeerconnection-interface) available in WebRTC supporting Browsers.

**NOTE** the goal for [RTCPeerConnection](xref:SIPSorcery.Net.RTCPeerConnection)) class' public interface is to make it as close as possible to the [RTCPeerConnection](https://www.w3.org/TR/webrtc/#rtcpeerconnection-interface) interface.

## Usage

The steps involved in getting a WebRTC connection to a state where RTP media packets can be exchanged are:

 - Instantiate the @"SIPSorcery.Net.RTCPeerConnection" instance,
 - Add the audio and/or video tracks as required,
 - Call the `createOffer` method to acquire an SDP offer that can be sent to the remote peer,
 - Send the SDP offer and get the SDP answer from the remote peer (this exchange is not part of the WebRTC specification and can be done using any signalling layer, examples are SIP, web sockets etc),
 - Once the SDP exchange has occurred the ICE checks can start in order to establish the optimal network path between the two peers. ICE candidates typically need to be passed between peers using the signalling layer,
 - Once ICE has established a the DTLS handshake will occur,,
 - If the DTLS handshake is successful the keying material it produces is used to initialise the SRTP contexts,
 - After the SRTP contexts are initialised the RTP media and RTCP packets can be exchanged in the normal manner.

````csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIPSorcery.Net;

class Program
{
    static async Task Main()
    {
        Console.WriteLine("Get Started WebRTC");

        var pc = new RTCPeerConnection(null);

        // Add the required media tracks to the session.
        MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
        pc.addTrack(audioTrack);
        MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
        pc.addTrack(videoTrack);

        // Keep track of important connectivity events.
        pc.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state change to {state}.");
        pc.onconnectionstatechange += (state) => Console.WriteLine($"Peer connection state change to {state}.");

        // Create our SDP offer.
        var offerSdp = pc.createOffer(null);
        await pc.setLocalDescription(offerSdp);

        // Send our SDP offer to remote peer, using a signalling layer, and get their SDP answer.
        pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = answerSdp, type = RTCSdpType.answer });

        // Typically ICE candidates will now be exchanged using the signalling layer.

        Console.ReadLine();
    }
}
````
