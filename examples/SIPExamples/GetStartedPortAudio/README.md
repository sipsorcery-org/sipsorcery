This example program uses the audio capture and rendering capabilities from the cross platform [PortAudio library](http://www.portaudio.com/).

A few different .NET Core PortAudio wrappers were tried with varying degrees of success. The current wrapper, [ProjectCeilidh.PortAudio](https://github.com/Ceilidh-Team/PortAudio) is included as source code since no up to date nuget package is available.

PortAudio is a large library that in turn wraps many other lower level Audio API's from different platforms. The `ProjectCeilidh.PortAudio` allows the desired API to be selected and at the time of writing the ones chose are:

 - For Windows: [DirectSound](https://en.wikipedia.org/wiki/DirectSound)
 - For Unix (including Macos): [ALSA](https://en.wikipedia.org/wiki/Advanced_Linux_Sound_Architecture)

 Other API's are available, for example the newer [WASAPI](https://en.wikipedia.org/wiki/Technical_features_new_to_Windows_Vista#Audio_stack_architecture) for Windows, but it seems to take extra sophistication to match up each API with the device parameters (channel count, sampling frequency, bits per sample etc). The purpose of this example is to demonstrate one approach that can be used to integrate an audio library with the `sipsorcery` library. At best it should be considered a starting point. If you come up with something better please consider contributing it back via a Pull Request.