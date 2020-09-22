# SIPSorcery Guide and Reference

This site contains the usage guide and API reference for the SIPSorcery SIP and WebRTC library.

If you are new to the library here are some recommended starting points:

 - You're not sure what you want [Getting Started](articles/intro.md).
 - You want to place a SIP call [Getting Started](articles/intro.md).
 - You want to perform more advanced SIP operations like transfers, on/off hold etc. [SIPUserAgent](articles/sipuseragent.md).
 - You want to use the Real-Time Transport Protocol in your application [RTPSession](articles/rtpsession.md).
 - You want to use WebRTC in your application [RTCPeerConnection](articles/rtcpeerconnection.md).

The API reference is available [here](api/index.md).

The library source code and examples are [here](https://github.com/sipsorcery/sipsorcery).

# Cross Platform Audio and Video

The `SIPSorcery` libraries have been separated to facilitate cross platform support. The main library is designed to be platform agnostic and work on all platforms that support `.NET Standard 2.0`.

The main library can create SIP and WebRTC calls as well as transport the audio and video packets for them. **But it can't generate or do anything useful with the audio or video samples.** For that platform specific libraries that can utilise audio and video devices, such as microphones, speakers and webcams are required.

In addition most video, and some audio, codecs do not have `.NET Core` implementations and thus require native libraries to be used. In some cases, such as `FFmpeg`, the native libraries can be used from `.NET` applications in a cross platform manner. If a particular native library does not have cross platform packages then it will often mean a platform specific `.NET Core` library is required.

The separate [SIPSorceryMedia.Abstractions](https://github.com/sipsorcery/SIPSorceryMedia.Abstractions) library contains a set of interfaces that need to implemented in order to be able to generate and process audio and video samples from the main `SIPSorcery` library.

At the time of writing two implementations are available with varying degrees of support for different audio/video capabilities and codecs:

 - [SIPSorceryMedia.Windows](https://github.com/sipsorcery/SIPSorceryMedia.Windows) - A `Windows` specific library that makes use of the [MediaCapture](https://docs.microsoft.com/en-us/uwp/api/windows.media.capture.mediacapture?view=winrt-19041) control for webcam support and [NAudio](https://github.com/naudio/NAudio) for audio support. 
 
 - [SIPSorceryMedia.FFmpeg](https://github.com/sipsorcery/SIPSorceryMedia.FFmpeg) - A cross platform library that is designed to work on any platform that supports `.NET Core` and can install the [FFmpeg](https://www.ffmpeg.org/) libraries.
