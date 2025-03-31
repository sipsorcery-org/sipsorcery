# SIPSorceryMedia.FFmpeg

This project is an example of developing a C# library that can use features from [FFmpeg](https://ffmpeg.org/) native libraries and that inegrates with the [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) real-time communications library.

This project has been tested successfully on Windows, MacOs and Linux.

The classes in this project provide functions to:

 - **Video codecs**: VP8, H264
 - **Audio codecs**: PCMU (G711), PCMA (G711), G722, G729 and Opus
 - **Video input**:
    - using local file or remote using URI (like [this](https://upload.wikimedia.org/wikipedia/commons/3/36/Cosmos_Laundromat_-_First_Cycle_-_Official_Blender_Foundation_release.webm))
    - using camera 
    - using screen
 - **Audio input**:
    - using local file or remote using URI (like [this](https://upload.wikimedia.org/wikipedia/commons/3/36/Cosmos_Laundromat_-_First_Cycle_-_Official_Blender_Foundation_release.webm) or [this](https://upload.wikimedia.org/wikipedia/commons/0/0f/Pop_RockBrit_%28exploration%29-en_wave.wav))
    - using microphone

You can set any **Video input** (or none) with any **Audio input** (or none)

There is no **Audio ouput** in this library. For this you can use [SIPSorcery.SDL2](https://github.com/sipsorcery-org/SIPSorcery.SDL2)

# Installing FFmpeg

## For Windows

Install the [FFmpeg](https://www.ffmpeg.org/) binaries using the packages at https://www.gyan.dev/ffmpeg/builds/#release-builds and include the FFMPEG executable in your PATH to find them automatically.

As of 14 Jan 2024, the command below works on Windows 11 and installs the required FFmpeg binaries and libraries where they can be found by SIPSorceryMedia.FFmpeg:

`winget install "FFmpeg (Shared)" --version 7.0`

Another option is to get the binaries stored on the [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen/tree/master/FFmpeg/bin/x64) Github project.

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


