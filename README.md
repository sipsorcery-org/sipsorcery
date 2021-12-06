# SIPSorceryMedia.FFmpeg

This project is an example of developing a C# library that can use features from [FFmpeg](https://ffmpeg.org/) native libraries and that inegrates with the [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) real-time communications library.

The classes in this project provide functions to:

 - Video codecs: VP8, H264
 - Audio codecs: PCMU, PCMA, G722
 - Video input:
    - using local file or remote using URI (like [this](https://upload.wikimedia.org/wikipedia/commons/3/36/Cosmos_Laundromat_-_First_Cycle_-_Official_Blender_Foundation_release.webm))
    - using camera (tested on Win10 only for the moment)
    - using screen (a part or the full screen) (tested on Win10 only for the moment)
 - Audio input
    - using local file or remote using URI (like [this](https://upload.wikimedia.org/wikipedia/commons/3/36/Cosmos_Laundromat_-_First_Cycle_-_Official_Blender_Foundation_release.webm))
    - using microphone (tested on Win10 only for the moment)

You can set any Video input (or none) with any Audio input (or none)

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

Test 
- with [FFmpegFileAndDevicesTest](./test/FFmpegFileAndDevicesTest) project
- or with the [WebRTC Test Pattern demo](https://github.com/sipsorcery/sipsorcery/tree/master/examples/WebRTCExamples/WebRTCTestPatternServer)


