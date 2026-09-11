# Description

This demo serves a dual purpose:

 - A demo of how to get started with the sipsorcery library when using FFmpeg for video encoding.
 - A demo of how to host a sipsorcery webrtc application in a docker container.
 
The demo can be run locally on Windows in the same manner as the other demoes. The correct version of FFmpeg must be installed as per the instructions [here](https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg).

The demo "should" also work in a docker container and was verified to do so. The hardest thing about getting the contianer to work and the most likely thing to break iy
is a mismtach in the verion of the FFmpeg binaries supported by the [FFmpeg.Autogen](https://github.com/Ruslan-B/FFmpeg.AutoGen) library used by sipsorcery.

As the time of writing the correct FFmpeg version was `n7.0` and a docker build image was created [here](https://github.com/sipsorcery-org/SIPSorceryMedia.FFmpeg/tree/master/ffmpeg-build).

# Docker Build

`c:\dev\sipsorcery\examples\WebRTCExamples\WebRTCFFmpegGetStarted> docker build -t webrtcgetstarted --progress=plain -f Dockerfile .`

# Docker Run

Establishing a WebRTC connection to a docker container has not yet been successful. The normal docker address range of 172.x.x.x isn't directly accessible from the host OS. Typically access to docker 
containers relies on port mapping but that doesn't work with the WebRTC ICE mechanism. Below are the options attempted. All of which were unsuccessful. The docker image will work without any special 
networking options if hosted externally, for example in a kubernetes cluster. The recommended alternative is to run the app directly using `dotnet run`.

`docker run --rm -it -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" webrtcgetstarted`
or
`docker run --rm -it --network=host -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" webrtcgetstarted`
or
`docker run --rm -it -p 8080:8080 -p 50042:50042/udp -e ASPNETCORE_URLS="http://0.0.0.0:8080" -e BIND_PORT="50042" webrtcgetstarted`
or
`docker run --rm -it -p 8080:8080 -p 50042:50042/udp --add-host=host.docker.internal:host-gateway -e ASPNETCORE_URLS="http://0.0.0.0:8080" -e BIND_PORT="50042" webrtcgetstarted`
or
`docker run --rm -it -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" -e WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER="True" webrtcgetstarted`
or
`docker run --rm -it -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" -e WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER="True" -e STUN_URL="stun:stun.cloudflare.com" webrtcgetstarted`

# From DockerHub

`docker run --rm -it -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" sipsorcery/webrtcgetstarted`

# Troubleshooting

`c:\dev\sipsorcery\examples\WebRTCExamples\WebRTCFFmpegGetStarted> docker run --rm -it -p 8080:8080 -e ASPNETCORE_URLS="http://0.0.0.0:8080" --entrypoint "/bin/bash" webrtcgetstarted`