## RTP Session

The [RTPSession](xref:SIPSorcery.Net.RTPSession) class provides the building blocks for dealing with the [Real-Time Transport Protocol](https://tools.ietf.org/html/rfc3550). It is used to transport audio and video packets between session participants.

**Note: As of Feb/Mar 2020 work is ongoing to standardise the @"SIPSorcery.Net.RTPSession" and @"SIPSorcery.Net.WebRtcSession" classes closer to the [RTCPeerConnection](https://www.w3.org/TR/webrtc/#rtcpeerconnection-interface) interface from the [WebRTC 1.0: Real-time Communication Between Browsers](https://www.w3.org/TR/webrtc) API.**

There are currently two higher level classes that wrap the @"SIPSorcery.Net.RTPSession" class for use in common scenarios:

 - The [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) which is the recommended implementation for SIP applications. This class is part of the [SIPSorceryMedia](https://github.com/sipsorcery/sipsorcery-media) library and uses Windows specific API's to provide access to the capturing/rendering devices for audio/video.
 - The @"SIPSorcery.Net.WebRtcSession" which needs to be used when creating a WebRTC peer connection.

### Features

As well as taking care of the plumbing required to send and receive media packets the @"SIPSorcery.Net.RTPSession" class performs a number of additional functions that are required for correct operation of an RTP connection.

 - Takes care of creating and monitoring the required UDP socket(s) (one for multiplexed RTP & RTCP or two if not).
 - Takes care of creating the required RTCP session(s) (one for a single media type or two if audio and video are multiplexed).
 - Can generate an SDP offer based on the media types that have been added using the @"SIPSorcery.Net.RTPSession.addTrack(SIPSorcery.Net.MediaStreamTrack)" method.
 - Takes care of matching the local/remote SDP offer/answer and setting the payload types ID's.
 - Provides `Send` methods for common payload encodings such as VPX, H264 and JPEG.
 - Provides a `Send` method for an RTP event which is utilised to send DTMF tones.
 - Provides hooks for setting Secure Real-time Protocol (SRTP) protect and unprotect functions.

The @"SIPSorcery.Net.RTPSession" class has been updated to support WebRTC sessions which behave differently to the original RTP used by most SIP devices. The main change is that WebRTC multiplexes all packets (STUN, RTP (audio and video) and RTCP) on a single connection. Standard RTP only supports a single packet type per connection and uses multiple sockets for RTP and RTCP and if required an additional socket pair for video.

Higher level applications do not need to be concerned with these differences but do need to make sure they use the correct session class:

 - [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) for SIP applications.
 - @"SIPSorcery.Net.WebRtcSession" for WebRTC applications.

### Usage

This article describes how to use the [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs). For WebRTC see the [webrtcsession article](webrtcsession.md).

#### Creating an RtpAVSession

The code snippet below shows how to instantiate an [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) for a typical RTP connection that will transmit audio PCMU packets over IPv4.

Two UDP sockets will be created:

 - The RTP socket will be set to a random even port between 10000 and 20000.
 - The RTCP port will be set to the RTP port plus one.

````csharp
// dotnet add package SIPSorceryMedia

using System.Net.Sockets;
using SIPSorcery.Media;

var rtpSession = new RtpAVSession(AddressFamily.InterNetwork, new AudioOptions { AudioSource = AudioSourcesEnum.Microphone }, null);
````

#### Controlling an RtpAVSession 

If the [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) is used with the @"SIPSorcery.SIP.App.SIPUserAgent" class then normally the control functions can all be done via it. If it is used independently then there are methods to:

 - Create an SDP offer or answer.
 - Set the remote SDP offer or answer.
 - Detect whether the remote SDP has put a session on or off hold.
 - Send an RTP event for a DTMF tone.

#### Media Handling

Rendering the audio or video packets is the hardest part of using any of the RTP Session classes. The .Net Framework and Core libraries do not provide any audio or video rendering capabilities. To render the media a 3rd party component is required.

 - For audio the best option is [NAudio](https://github.com/naudio/NAudio).
 - For video the only known option is the companion [SIPSorceryMedia library](https://github.com/sipsorcery/sipsorcery-media). It has functions to decode VP8 packets to RGB frames which can be displayed in a Windows Forms or WPF application.

#### Audio Capture

The [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) takes an `AudioOptions` parameter to its constructor that allows the source to be specified. The most common case is capturing from the default system microphone and that is done by setting the audio source to `AudioSourcesEnum.Microphone`.

````csharp
using System.Net.Sockets;
using SIPSorcery.Media;

var rtpSession = new RtpAVSession(AddressFamily.InterNetwork, new AudioOptions { AudioSource = AudioSourcesEnum.Microphone }, null);
````

#### Audio Rendering

The [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) automatically plays any received audio packets on the default system speaker.

#### Video Capture

Currently the only video source supported in [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) is a `Test Pattern` with text overlay.

````csharp
using System.Net.Sockets;
using SIPSorcery.Media;

var rtpSession = new RtpAVSession(AddressFamily.InterNetwork, 
  new AudioOptions { AudioSource = AudioSourcesEnum.Microphone }, 
  new VideoOptions { VideoSource = VideoSourcesEnum.TestPattern } 
  );
````

#### Video Rendering

The [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) will supply any video samples it receives and decodes via its `OnVideoSampleReady` event. The samples are supplied in a BGR24 bitmap format.

A snippet taken from the [Getting Started Video](https://github.com/sipsorcery/sipsorcery/blob/master/examples/GetStartedVideo/Program.cs) that renders the received bitmap in a Windows Forms picture box.

````csharp
rtpSession.OnVideoSampleReady += (byte[] sample, uint width, uint height, int stride) =>
{
    _picBox.BeginInvoke(new Action(() =>
    {
        unsafe
        {
            fixed (byte* s = sample)
            {
                System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap((int)width, (int)height, stride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, (IntPtr)s);
                _picBox.Image = bmpImage;
            }
        }
    }));
};
````

### Custom RTP Session

A custom media session can be implemented and supplied to the @"SIPSorcery.SIP.App.SIPUserAgent". A custom implementation needs to support the @"SIPSorcery.SIP.App.IMediaSession" interface. The [RtpAVSession](https://github.com/sipsorcery/sipsorcery-media/blob/master/src/RtpAVSession/RtpAVSession.cs) serves as a reference implementation.