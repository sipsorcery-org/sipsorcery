# RTP Session

The @"SIPSorcery.Net.RTPSession" class provides the building blocks for dealing with the [Real-Time Transport Protocol](https://tools.ietf.org/html/rfc3550). It is used to transport audio and video packets between session participants. 

In general it is not necessary for an application to use this class directly. It should typically use one of the higher level classes described below. Having an understanding of this class is useful to be aware of how audio and video packets are communicated in near real-time.

There are currently two higher level classes that wrap the @"SIPSorcery.Net.RTPSession" class for use in common scenarios:

 - The @"SIPSorcery.Media.VoIPMediaSession" class which is the recommended implementation for SIP applications. 
 - The @"SIPSorcery.Net.RTCPeerConnection" which is for WebRTC applications.

## Features

As well as taking care of the plumbing required to send and receive media packets the @"SIPSorcery.Net.RTPSession" class performs a number of additional functions that are required for the correct operation of an RTP connection.

 - Takes care of creating and monitoring the required UDP socket(s) (one for multiplexed RTP & RTCP or two if not).
 - Takes care of creating the required RTCP session(s) (one for a single media type or two if audio and video are multiplexed).
 - Can generate an SDP offer based on the media types that have been added using the @"SIPSorcery.Net.RTPSession.addTrack(SIPSorcery.Net.MediaStreamTrack)" method.
 - Takes care of matching the local/remote SDP offer/answer and setting the payload types ID's.
 - Provides `Send` methods for common payload encodings such as VPX, H264 and MJPEG.
 - Provides a `Send` method for an RTP event which is utilised to send DTMF tones.
 - Provides hooks for setting Secure Real-time Protocol (SRTP) protect and unprotect functions.

The @"SIPSorcery.Net.RTPSession" class has been updated to support WebRTC sessions which behave differently compared to the original RTP used by most SIP devices. The main change is that WebRTC multiplexes all packets (STUN, RTP (audio and video) and RTCP) on a single connection. Standard RTP only supports a single packet type per connection and uses multiple sockets for RTP and RTCP and if required an additional socket pair for video.

## Working with Cross Platform Media

The `SIPSorcery` libraries have been separated to facilitate cross platform support. The main library is designed to be platform agnostic and work on all platforms that support `.NET Standard 2.0`.

The main library can create SIP and WebRTC calls as well as transport the audio and video packets for them. But it can't generate or do anything useful with the audio or video samples in those packets. For that platform specific libraries that can use audio and video devices, such as microphones, speakers and webcams are required.

In addition most video and some audio codecs do not have `.NET Core` implementations and require native libraries to be used. In some cases, such as `FFmpeg`, native libraries can be used from `.NET` applications in a cross platform manner. If a particular native library does not have cross platform packages then it will often mean a platform specific `.NET Core` library is required.

The separate [SIPSorceryMedia.Abstractions](https://github.com/sipsorcery/SIPSorceryMedia.Abstractions) library contains a set of interfaces that platform specific libraries need to implement in order to work with the main library.

At the time of writing two libraries have been created to work with audio and video devices on different platforms:

 - [SIPSorceryMedia.Windows](https://github.com/sipsorcery/SIPSorceryMedia.Windows) - A `Windows` specific library. Makes use UWP classes for webcam capture and [NAudio](https://github.com/naudio/NAudio) for microphone capture and speaker playback. 
 
 - [SIPSorceryMedia.FFmpeg](https://github.com/sipsorcery/SIPSorceryMedia.FFmpeg) - A cross platform library that is designed to work on any platform that supports `.NET Core` and can install the [FFmpeg](https://www.ffmpeg.org/) libraries. At the time of writing this library is still a work in progress and does not support audio or video devices.

## VoIPMediaSession

The rest of this article describes the @"SIPSorcery.Media.VoIPMediaSession" class which is designed for use with SIP/VoIP applications. For WebRTC see the [RTCPeerConnection article](rtcpeerconnection.md).

The @"SIPSorcery.Media.VoIPMediaSession" class acts as a bridge between the @"SIPSorcery.Net.RTPSession" class and the platform specific media library, such as the `SIPSorceryMedia.Windows` library.

The code snippet below shows how to instantiate a @"SIPSorcery.Media.VoIPMediaSession".

````csharp
using SIPSorcery.Media;
using SIPSorceryMedia.Windows

var winAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder());
var voipMediaSession = new VoIPMediaSession(winAudioEndPoint.ToMediaEndPoints());
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
using SIPSorcery.Media;
using SIPSorceryMedia.Windows

var winAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder());
var voipMediaSession = new VoIPMediaSession(winAudioEndPoint.ToMediaEndPoints());
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
