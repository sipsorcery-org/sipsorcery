## WebRTC Session

The [WebRtcSession](xref:SIPSorcery.Net.WebRtcSession) class is intended to be the equivalent of the [RTCPeerConnection](https://www.w3.org/TR/webrtc/#rtcpeerconnection-interface) available in WebRTC supporting Browsers.

**NOTE** the goal for [WebRtcSession](xref:SIPSorcery.Net.WebRtcSession) class' public interface is to make it as close as possible to the [RTCPeerConnection](https://www.w3.org/TR/webrtc/#rtcpeerconnection-interface) interface. This is currently a work in progress.

### Usage

The steps involved in getting a WebRTC session to a state where RTP media packets can be exchanged are:

 - Instantiate the [WebRtcSession](xref:SIPSorcery.Net.WebRtcSession) instance,
 - Add the audio and/or video tracks as required,
 - Call the `createOffer` method to acquire an SDP offer that can be sent to the remote peer,
 - Send the SDP offer and get the SDP answer from the remote peer (this exchange is not part of the WebRTC specification and can be done using any signalling layer, examples are SIP, web sockets etc),
 - Once the SDP exchange has occurred the ICE checks occur to establish the optimal network path between the two peers (this happens under the hood, no explicit methods need to be called),
 - Once ICE has established a the DTLS handshake is initiated by calling `DtlsHandshake.DoHandshakeAsServer`,
 - If the DTLS handshake is successful the keying material it produces is used to initialise the SRTP contexts,
 - After the SRTP contexts are initialised the media and RTCP packets can be exchanged in the normal manner.

````csharp
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia;

const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
const string DTLS_KEY_PATH = "certs/localhost_key.pem";
const string DTLS_CERTIFICATE_FINGERPRINT = "sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";

var webRtcSession = new WebRtcSession(
        AddressFamily.InterNetwork,
        DTLS_CERTIFICATE_FINGERPRINT,
        null,
        null);

// Add the required media tracks to the session.
webRtcSession.addTrack(SDPMediaTypesEnum.audio, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
webRtcSession.addTrack(SDPMediaTypesEnum.video, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });

// Create our SDP offer.
var offerSdp = await webRtcSession.createOffer();
webRtcSession.setLocalDescription(offerSdp);
            
// Send our SDP offer to remote peer and get their SDP answer.
//var answerSDP = SDP.ParseSDPDescription(sdpAnswer);
//webRtcSession.setRemoteDescription(SdpType.answer, answerSDP);

// Once the SDP offer/answer exchange has happened the ICE negotiation can commence.
// Once complete the DTLS handshake needs to occur to obtain the keying material to 
// use to initialise the SRTP context.
var dtls = new DtlsHandshake(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);
dtls.DoHandshakeAsServer((ulong)webRtcSession.RtpSession.RtpChannel.RtpSocket.Handle);

if (dtls.IsHandshakeComplete())
{
    Console.WriteLine("DTLS handshake completed successfully.");

    // The DTLS handshake completed successfully.
    // Initialise the SRTP contexts for the RTP and RTCP sessions
    // in this WebRTC session.
    var srtpSendContext = new Srtp(dtls, false);
    var srtpReceiveContext = new Srtp(dtls, true);

    webRtcSession.RtpSession.SetSecurityContext(
        srtpSendContext.ProtectRTP,
        srtpReceiveContext.UnprotectRTP,
        srtpSendContext.ProtectRTCP,
        srtpReceiveContext.UnprotectRTCP);

    webRtcSession.IsDtlsNegotiationComplete = true;
}
else
{
    Console.WriteLine("DTLS handshake failed.");
    webRtcSession.Close("dtls failure");
}
````
