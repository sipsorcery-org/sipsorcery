# RTP Session

The @"SIPSorcery.Net.RTPSession" class provides the building blocks for dealing with the [Real-Time Transport Protocol](https://tools.ietf.org/html/rfc3550). It is used to transport audio and video packets between session participants. 

In general it is not necessary for an application to use this class directly. It should typically use one of the higher level classes described below. Having an understanding of this class is useful to be aware of how audio and video packets are communicated in near real-time.

There are currently two higher level classes that wrap the @"SIPSorcery.Net.RTPSession" class for use in common scenarios:

 - The @"SIPSorcery.Media.VoIPMediaSession" class which is the recommended implementation for SIP applications. See the [VoIP Media Session article](voipmediasession.md) for more details.
 - The @"SIPSorcery.Net.RTCPeerConnection" which is for WebRTC applications. See the [WebRTC RTCPeerConnection article](rtcpeerconnection.md) for more details.

## Features

As well as taking care of the plumbing required to send and receive media packets the @"SIPSorcery.Net.RTPSession" class performs a number of additional functions that are required for the correct operation of an RTP connection.

 - Takes care of creating and monitoring the required UDP socket(s) (one for multiplexed RTP & RTCP or two if not).
 - Takes care of creating the required RTCP session(s) (one for a single media type or two if audio and video are multiplexed).
 - Can generate an SDP offer based on the media types that have been added using the @"SIPSorcery.Net.RTPSession.addTrack(SIPSorcery.Net.MediaStreamTrack)" method.
 - Takes care of matching the local/remote SDP offer/answer and setting the payload types ID's.
 - Provides `Send` methods for common payload encodings such as VPX, H264 and MJPEG.
 - Provides a `Send` method for an RTP event which is utilised to send DTMF tones.
 - Provides hooks for setting Secure Real-time Protocol (SRTP) protect and unprotect functions.

The @"SIPSorcery.Net.RTPSession" class has been updated to support WebRTC peer connections which behave differently compared to the original RTP specification, used by most SIP devices. The main change is that WebRTC multiplexes all packets (STUN, RTP (audio and video) and RTCP) on a single connection. Standard RTP only supports a single packet type per connection and uses multiple sockets for RTP and RTCP.
