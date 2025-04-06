# Description

This demo serves a dual purpose:

 - A demo of how to get started with the sipsorcery library when using FFmpeg for video encoding.
 - A demo of how to host a sipsorcery webrtc application in a docker container.
 
The demo can be run locally on Windows in the same manner as the other demoes. The correct version of FFmpeg must be installed as per the instructions [here](https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg).

The demo "should" also work in a docker container and was verified to do so. The hardest thing about getting the contianer to work and the most likely thing to break iy
is a mismtach in the verion of the FFmpeg binaries supported by the [FFmpeg.Autogen](https://github.com/Ruslan-B/FFmpeg.AutoGen) library used by sipsorcery.

As the time of writing the correct FFmpeg version was `n7.0` and a docker build image was created [here](https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg/tree/master/ffmpeg-build).

# Docker Build & Run

`c:\dev\sipsorcery\examples\WebRTCExamples\WebRTCFFmpegGetStarted> docker build -t webrtcgetstarted --progress=plain -f Dockerfile .`
`c:\dev\sipsorcery\examples\WebRTCExamples\WebRTCFFmpegGetStarted> docker run --rm -it -p 8080:8080 -p 8081:8081 webrtcgetstarted`

# Troubleshooting

`c:\dev\sipsorcery\examples\WebRTCExamples\WebRTCFFmpegGetStarted> docker run --rm -it -p 8080:8080 -p 8081:8081 --entrypoint "/bin/bash" webrtcgetstarted`