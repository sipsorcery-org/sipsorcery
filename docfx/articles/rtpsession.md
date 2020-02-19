## RTP Session

The [RTPSession](xref:SIPSorcery.Net.RTPSession) class provides the building blocks for dealing with the [Real-Time Transport Protocol](https://tools.ietf.org/html/rfc3550). It is used to transport audio and video packets between session participants.

There are two higher level classes that wrap the [RTPSession](xref:SIPSorcery.Net.RTPSession) class for use in common scenarios:

 - The [RTPMediaSession](xref:SIPSorcery.SIP.App.RTPMediaSession) which is the recommended implementation for SIP applications.
 - The [WebRtcSession](xref:SIPSorcery.Net.WebRtcSession) which needs to be used when creating a WebRTC peer connection.

### Features

As well as taking care of the plumbing required to send and receive media packets the [RTPSession](xref:SIPSorcery.Net.RTPSession) class does a number of additional functions that are required for correct operation of an RTP connection.

 - Takes care of creating and monitoring the required UDP socket(s) (one for multiplexed RTP & RTCP or two if not).
 - Takes care of creating the required RTCP session(s) (one for a single media type or two if audio and video are multiplexed).
 - Can generate an SDP offer based on the media types that have been added using the `AddTrack` method.
 - Takes care of matching the local/remote SDP offer/answer and setting the payload types ID's.
 - Provides `Send` methods for common payload encodings such as VPX, H264 and JPEG.
 - Provides a `Send` method for an RTP event which is utilised to send DTMF tones.
 - Provides hooks for setting Secure Real-time Protocol (SRTP) protect and unprotect functions.

The [RTPSession](xref:SIPSorcery.Net.RTPSession) class has been updated to support WebRTC sessions which behave differently to the original RTP used by most SIP devices. The main change is that WebRTC multiplexes all packets (STUN, RTP (audio and video) and RTCP) on a single connection. Standard RTP only supports a single packet type per connection and thus needs multiple sockets for RTP and RTCP.

Higher level applications do not need to be concerned with these differences but do need to make sure they use the correct session class:

 - [RTPMediaSession](xref:SIPSorcery.SIP.App.RTPMediaSession) for SIP applications.
 - [WebRtcSession](xref:SIPSorcery.Net.WebRtcSession) for WebRTC applications.

### Usage

This article describes how to use the [RTPMediaSession](xref:SIPSorcery.SIP.App.RTPMediaSession). For WebRTC see the [webrtcsession article](webrtcsession.md).

#### Creating an RTPMediaSession

The code snippet below shows how to instantiate an [RTPMediaSession](xref:SIPSorcery.SIP.App.RTPMediaSession) for a typical RTP connection that will transmit audio PCMU packets over IPv4.

Two UDP sockets will be created:

 - The RTP socket will be set to a random even port between 10000 and 20000.
 - The RTCP port will be set to the RTP port plus one.

````csharp
using SIPSorcery.SIP.App;

var rtpMediaSession = new RTPMediaSession(SDPMediaTypesEnum.audio, new SDPMediaFormat(SDPMediaFormatsEnum.PCMU), AddressFamily.InterNetwork);
````

#### Controlling an RTPMediaSession 

If the [RTPMediaSession](xref:SIPSorcery.SIP.App.RTPMediaSession) is used with the [SIPUserAgent](xref:SIPSorcery.SIP.App.SIPUserAgent) class then normally the control functions can all be done via it. If it is used independently then there are methods to:

 - Create an SDP offer or answer.
 - Set the remote SDP offer or answer.
 - Detect whether the remote SDP has put a session on or off hold.
 - Send an RTP event for a DTMF tone.

#### Media Handling

Rendering the audio or video packets is the hardest part of using any of the RTP Session classes. The .Net Framework and Core libraries do not provide any audio or video rendering capabilities. To render the media a 3rd party component is required.

 - For audio the best option is [NAudio](https://github.com/naudio/NAudio).
 - For video the only known option is the companion [SIPSorceryMedia library](https://github.com/sipsorcery/sipsorcery-media). It has functions to decode VP8 packets to RGB frames which can be displayed in a Windows Forms or WPF application.

#### Audio Capture

[NAudio](https://github.com/naudio/NAudio) is used extensively in the example applications. The snippet below shows one approach to connecting its audio capture function to the [RTPMediaSession](xref:SIPSorcery.SIP.App.RTPMediaSession). This snippet is taken from the [Getting Started](https://github.com/sipsorcery/sipsorcery/blob/master/examples/GetStarted/Program.cs) example program.

````csharp
using System;
using System.Net.Sockets;
using NAudio.Wave;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);  // PCMU format used by both input and output streams.
int INPUT_SAMPLE_PERIOD_MILLISECONDS = 20;             // This sets the frequency of the RTP packets.

var rtpSession = new RTPMediaSession(SDPMediaTypesEnum.audio, new SDPMediaFormat(SDPMediaFormatsEnum.PCMU), AddressFamily.InterNetwork);

// Get the default audio capture device.
WaveInEvent waveInEvent = new WaveInEvent();
WaveFormat waveFormat = _waveFormat;
waveInEvent.BufferMilliseconds = INPUT_SAMPLE_PERIOD_MILLISECONDS;
waveInEvent.NumberOfBuffers = 1;
waveInEvent.DeviceNumber = 0;
waveInEvent.WaveFormat = waveFormat;

// Wire up the RTP media session to the audio input device.
uint rtpSendTimestamp = 0;
waveInEvent.DataAvailable += (object sender, WaveInEventArgs args) =>
{
    byte[] sample = new byte[args.Buffer.Length / 2];
    int sampleIndex = 0;

    for (int index = 0; index < args.BytesRecorded; index += 2)
    {
        var ulawByte = NAudio.Codecs.MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(args.Buffer, index));
        sample[sampleIndex++] = ulawByte;
    }

    rtpSession.SendAudioFrame(rtpSendTimestamp, (int)SDPMediaFormatsEnum.PCMU, sample);
    rtpSendTimestamp += (uint)(8000 / waveInEvent.BufferMilliseconds);
};

waveInEvent.StartRecording();
````

#### Audio Rendering

The snippet below shows one approach to connecting [NAudio](https://github.com/naudio/NAudio)'s audio rendering function to the [RTPMediaSession](xref:SIPSorcery.SIP.App.RTPMediaSession). This snippet is taken from the [Getting Started](https://github.com/sipsorcery/sipsorcery/blob/master/examples/GetStarted/Program.cs) example program.

````csharp
using System;
using System.Net.Sockets;
using NAudio.Wave;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;

WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);  // PCMU format used by both input and output streams.

var rtpSession = new RTPMediaSession(SDPMediaTypesEnum.audio, new SDPMediaFormat(SDPMediaFormatsEnum.PCMU), AddressFamily.InterNetwork);

WaveOutEvent waveOutEvent = new WaveOutEvent();
var waveProvider = new BufferedWaveProvider(_waveFormat);
waveProvider.DiscardOnBufferOverflow = true;
waveOutEvent.Init(waveProvider);
waveOutEvent.Play();

// Wire up the RTP media session to the audio output device.
rtpSession.OnRtpPacketReceived += (mediaType, rtpPacket) =>
{
    var sample = rtpPacket.Payload;
    for (int index = 0; index < sample.Length; index++)
    {
        short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
        waveProvider.AddSamples(pcmSample, 0, 2);
    }
};
````

#### Video Capture

The snippet below uses the [SIPSorceryMedia library](https://github.com/sipsorcery/sipsorcery-media) to sample a video capture device using the Windows Media Foundation, encode it as VP8 and hand it over to the [RTPMediaSession](xref:SIPSorcery.SIP.App.RTPMediaSession) for sending. This snippet is taken from the [Softphone](https://github.com/sipsorcery/sipsorcery/blob/master/examples/Softphone/Media/MediaManager.cs) example program.

````csharp
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia;

const int VIDEO_WIDTH = 640;
const int VIDEO_HEIGHT = 480;
const int VIDEO_STRIDE = 1920;
const int VP8_TIMESTAMP_SPACING = 3000;

var rtpSession = new RTPMediaSession(SDPMediaTypesEnum.video, new SDPMediaFormat(SDPMediaFormatsEnum.VP8), AddressFamily.InterNetwork);

var vpxEncoder = new VpxEncoder();
vpxEncoder.InitEncoder(VIDEO_WIDTH, VIDEO_HEIGHT, VIDEO_STRIDE);
var imageConverter = new ImageConvert();
var videoSampler = new MediaSource();
videoSampler.Init(0, 0, VideoSubTypesEnum.RGB24, VIDEO_WIDTH, VIDEO_HEIGHT);

Task.Run(() =>
{

    int sampleCount = 0;
    uint rtpTimestamp = 0;

    while (true)
    {
        byte[] videoSample = null;
        var sample = videoSampler.GetSample(ref videoSample);

        if (sample != null && sample.HasVideoSample)
        {
            // This event encodes the sample and forwards it to the RTP media session for network transmission.
            IntPtr rawSamplePtr = Marshal.AllocHGlobal(videoSample.Length);
            Marshal.Copy(videoSample, 0, rawSamplePtr, videoSample.Length);

            byte[] yuv = null;

            unsafe
            {
                imageConverter.ConvertRGBtoYUV((byte*)rawSamplePtr, VideoSubTypesEnum.RGB24, VIDEO_WIDTH, VIDEO_HEIGHT, VIDEO_STRIDE, VideoSubTypesEnum.I420, ref yuv);
            }

            Marshal.FreeHGlobal(rawSamplePtr);

            IntPtr yuvPtr = Marshal.AllocHGlobal(yuv.Length);
            Marshal.Copy(yuv, 0, yuvPtr, yuv.Length);

            byte[] encodedBuffer = null;

            unsafe
            {
                vpxEncoder.Encode((byte*)yuvPtr, yuv.Length, sampleCount++, ref encodedBuffer);
            }

            Marshal.FreeHGlobal(yuvPtr);

            rtpSession.SendVp8Frame(rtpTimestamp, (int)SDPMediaFormatsEnum.VP8, encodedBuffer);
            rtpTimestamp += VP8_TIMESTAMP_SPACING;
        }
    }
});

````

#### Video Rendering

The snippet below uses the [SIPSorceryMedia library](https://github.com/sipsorcery/sipsorcery-media) to decode a VP8 encoded sample and return an RGB bitmap. This snippet is taken from the [Softphone](https://github.com/sipsorcery/sipsorcery/blob/master/examples/Softphone/Media/MediaManager.cs) example program.

````csharp
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia;

var rtpSession = new RTPMediaSession(SDPMediaTypesEnum.video, new SDPMediaFormat(SDPMediaFormatsEnum.VP8), AddressFamily.InterNetwork);

var vpxDecoder = new VpxEncoder();
vpxDecoder.InitDecoder();
var imageConverter = new ImageConvert();

// Decode video packets received.
rtpSession.OnRtpPacketReceived += (mediaType, rtpPacket) =>
{
    IntPtr encodedBufferPtr = Marshal.AllocHGlobal(rtpPacket.Payload.Length);
    Marshal.Copy(rtpPacket.Payload, 0, encodedBufferPtr, rtpPacket.Payload.Length);

    byte[] decodedBuffer = null;
    uint decodedImgWidth = 0;
    uint decodedImgHeight = 0;

    unsafe
    {
        vpxDecoder.Decode((byte*)encodedBufferPtr, rtpPacket.Payload.Length, ref decodedBuffer, ref decodedImgWidth, ref decodedImgHeight);
    }

    Marshal.FreeHGlobal(encodedBufferPtr);

    if (decodedBuffer != null && decodedBuffer.Length > 0)
    {
        IntPtr decodedSamplePtr = Marshal.AllocHGlobal(decodedBuffer.Length);
        Marshal.Copy(decodedBuffer, 0, decodedSamplePtr, decodedBuffer.Length);

        byte[] bmp = null;
        int stride = 0;

        unsafe
        {
            imageConverter.ConvertYUVToRGB((byte*)decodedSamplePtr, VideoSubTypesEnum.I420, Convert.ToInt32(decodedImgWidth), Convert.ToInt32(decodedImgHeight), VideoSubTypesEnum.RGB24, ref bmp, ref stride);
        }

        Marshal.FreeHGlobal(decodedSamplePtr);

        // Do something with the decoded bitmap data.
        //OnRemoteVideoSampleReady?.Invoke(bmp, Convert.ToInt32(decodedImgWidth), Convert.ToInt32(decodedImgHeight));
    }
};
````