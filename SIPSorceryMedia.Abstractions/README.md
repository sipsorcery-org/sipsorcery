# SIPSorceryMedia.Abstractions

This project provides the logic for the interfaces required by the [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) real-time communications library and the components that provide functions such as:

 - Access to audio or video devices (example [SIPSorceryMedia.Windows](https://github.com/sipsorcery-org/SIPSorceryMedia.Windows)).
 - Access to codecs from native libraries (examples [SIPSorceryMedia.Encoders](https://github.com/sipsorcery-org/SIPSorceryMedia.Encoders) and [SIPSorceryMedia.FFmpeg](https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg)).

# Important Interfaces

The most important interfacs contained in this library are:

  - IAudioEncoder: Needs to be implemented by classes that provide audio decoding and/or encoding. An example is the [AudioEncoder](https://github.com/sipsorcery-org/sipsorcery/blob/master/src/app/Media/Codecs/AudioEncoder.cs) class.
 
  - IVideoEncoder: Needs to be implemented by classes that provide video decoding and/or encoding. An example is the [VpxVideoEncoder](https://github.com/sipsorcery-org/SIPSorceryMedia.Encoders/blob/master/src/VpxVideoEncoder.cs#L25) class.
  
  - IAudioSource: Needs to be implemented by classes that act as a source of raw audio samples. Typically a microphone. An example is the [WindowsAudioEndPoint](https://github.com/sipsorcery-org/SIPSorceryMedia.Windows/blob/master/src/WindowsAudioEndPoint.cs#L32) class.
  
  - IAudioSink: Needs to be implemented by classes that act as a sink for raw audio samples. Typically an audio speaker. An example is the [WindowsAudioEndPoint](https://github.com/sipsorcery-org/SIPSorceryMedia.Windows/blob/master/src/WindowsAudioEndPoint.cs#L32) class.
   
  - IVideoSource: Needs to be implemented by classes that act as a source of raw video frames. Typically a webcam. An examples is the [WindowsVideoEndPoint](https://github.com/sipsorcery-org/SIPSorceryMedia.Windows/blob/master/src/WindowsVideoEndPoint.cs#L48).
  
  - IVideoSink: Needs to be implemented by classes that act as a sink for raw video frames. The video sink is usually a bitmap or some kind of graphics surface. An examples is the [WindowsVideoEndPoint](https://github.com/sipsorcery-org/SIPSorceryMedia.Windows/blob/master/src/WindowsVideoEndPoint.cs#L48).
