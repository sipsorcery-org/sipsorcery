# VoIP Media Session

The rest of this article describes the @"SIPSorcery.Media.VoIPMediaSession" class which is designed for use with SIP/VoIP applications. For WebRTC see the [RTCPeerConnection article](rtcpeerconnection.md).

The @"SIPSorcery.Media.VoIPMediaSession" class acts as a bridge between the @"SIPSorcery.Net.RTPSession" class and a separate media library, such as [SIPSorceryMedia.Windows](https://github.com/sipsorcery/SIPSorceryMedia.Windows) or [SIPSorceryMedia.FFmpeg](https://github.com/sipsorcery/SIPSorceryMedia.FFmpeg).

The code snippet below shows how to instantiate an audio only @"SIPSorcery.Media.VoIPMediaSession".

````csharp
using SIPSorcery.Media;

var audioEndPoint = new SIPSorceryMedia.Windows.WindowsAudioEndPoint(new AudioEncoder());
var voipMediaSession = new VoIPMediaSession(audioEndPoint.ToMediaEndPoints());
````

The code snippet below shows how to instantiate an audio and video @"SIPSorcery.Media.VoIPMediaSession".

````csharp
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions.V1;

var audioEndPoint = new SIPSorceryMedia.Windows.WindowsAudioEndPoint(new AudioEncoder());
var videoEndPoint = new SIPSorceryMedia.Windows.WindowsVideoEndPoint();

MediaEndPoints mediaEndPoints = new MediaEndPoints
{
    AudioSink = audioEndPoint,
    AudioSource = audioEndPoint,
    VideoSink = videoEndPoint,
    VideoSource = videoEndPoint,
};

var voipMediaSession = new VoIPMediaSession(mediaEndPoints);
````

**A different media library implementation can be used by swapping it in for `WindowsAudioEndPoint` and/or `WindowsVideoEndPoint`.**

The code snippet below shows how it's possible to combine different media sinks and sources. In this case the audio and video sources are generated from an `mp4` file and the `SIPSorceryMedia.FFmpeg` library while the audio and video samples from the remote party are handled with the `SIPSorceryMedia.Windows` library.

````csharp
using System.Collections.Generic;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions.V1;

var windowsAudioEndPoint = new SIPSorceryMedia.Windows.WindowsAudioEndPoint(new AudioEncoder(), -1, -1, true);
windowsAudioEndPoint.RestrictCodecs(new List<AudioCodecsEnum> { AudioCodecsEnum.PCMU });
var windowsVideoEndPoint = new SIPSorceryMedia.Windows.WindowsVideoEndPoint();

var mediaFileSource = new SIPSorceryMedia.FFmpeg.FFmpegFileSource("max_intro.mp4", true, new AudioEncoder());
mediaFileSource.Initialise();
mediaFileSource.RestrictCodecs(new List<VideoCodecsEnum> { VideoCodecsEnum.VP8 });
mediaFileSource.RestrictCodecs(new List<AudioCodecsEnum> { AudioCodecsEnum.PCMU });

MediaEndPoints mediaEndPoints = new MediaEndPoints
{
    AudioSink = windowsAudioEndPoint,
    AudioSource = mediaFileSource,
    VideoSink = windowsVideoEndPoint,
    VideoSource = mediaFileSource,
};

var voipMediaSession = new VoIPMediaSession(mediaEndPoints);
voipMediaSession.AcceptRtpFromAny = true;
````

## Media Control during Calls

Once a `SIP` call has been established, typically with the @"SIPSorcery.SIP.App.SIPUserAgent", certain events, such as hanging up or placing on hold, need to adjust the way audio and/or video streams are handled.

The @"SIPSorcery.SIP.App.SIPUserAgent" uses the @"SIPSorcery.SIP.App.IMediaSession" interface to control the @"SIPSorcery.Media.VoIPMediaSession". The reason for an additional interface, instead of relying on, `IAudioSink` etc, is to combine the signalling and media functions. For example placing a call on hold requires a `SIP re-INVITE` request to be sent as well as adjusting the media streams.

Applications do not hav to deal with signalling or media adjustments for standard `SIP` scenarios - such as on/off hold, transfers and hangup - they are able to be handled automatically by the @"SIPSorcery.SIP.App.SIPUserAgent".

There are cases where an application may want to adjust the media on a call other than the standard scenarios. For example playing a prompt for an Interactive Voice Response (IVR) system. For those cases the audio or video source can be called directly. The [Play Sounds](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPExamples/PlaySounds) demo application is a good example.

It creates a @"SIPSorcery.Media.VoIPMediaSession" in the standard manner.

````csharp
var windowsAudio = new WindowsAudioEndPoint(new AudioEncoder());
var voipMediaSession = new VoIPMediaSession(windowsAudio.ToMediaEndPoints());
voipMediaSession.AcceptRtpFromAny = true;
````

Once the call is established it pauses the microphone capture and switches the audio source to play pre-recorded prompts and signals from an internal generator.

````csharp
await windowsAudio.PauseAudio();

Console.WriteLine("Sending welcome message from 8KHz sample.");
await voipMediaSession.AudioExtrasSource.SendAudioFromStream(new FileStream(WELCOME_8K, FileMode.Open), AudioSamplingRatesEnum.Rate8KHz);

await Task.Delay(200);

Console.WriteLine("Sending sine wave.");
voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.SineWave);

await Task.Delay(2000);

Console.WriteLine("Sending white noise signal.");
voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.WhiteNoise);
await Task.Delay(2000);

Console.WriteLine("Sending pink noise signal.");
voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.PinkNoise);
await Task.Delay(2000);

Console.WriteLine("Sending silence.");
voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.Silence);

await Task.Delay(2000);

Console.WriteLine("Sending goodbye message from 16KHz sample.");
await voipMediaSession.AudioExtrasSource.SendAudioFromStream(new FileStream(GOODBYE_16K, FileMode.Open), AudioSamplingRatesEnum.Rate16KHz);

voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.None);

await Task.Delay(200);

// Switch to the external microphone input source.
await windowsAudio.ResumeAudio();
````
 
The full example for the above code snippet can be found in the [Play Sounds](https://github.com/sipsorcery/sipsorcery/tree/master/examples/SIPExamples/PlaySounds) demo.