# SIPSorceryMedia.FFmpeg

This project is an example of developing a C# library that can use features from [FFmpeg](https://ffmpeg.org/) native libraries and that inegrates with the [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) real-time communications library.

The classes in this project provide functions to:

 - VP8 and H264 video codecs.
 - Use a media file, such as `mp4`, as an audio and/or video source.

# Installing FFmpeg

## For Windows

No additional steps are required for an x64 build. The nuget package includes the [FFmpeg](https://www.ffmpeg.org/) x64 binaries.

## For Linux

Install the [FFmpeg](https://www.ffmpeg.org/) binaries using the package manager for the distribution.

`sudo apt install ffmpeg`

## For Mac

Install [homebrew](https://brew.sh/)
`brew install ffmpeg`
`brew install mono-libgdiplus`

# Testing

Test with the [WebRTC Test Pattern demo](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCTestPatternServer)


