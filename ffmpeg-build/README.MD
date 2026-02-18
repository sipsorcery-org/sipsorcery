The FFmpeg.AutoGen library gets generated against a specific version of the FFmpeg binaries. As soon as the deviate the dotnet AutoGen library is likely to throw an exception due to incompatible binary interfaces.

The purpose of thie docker build file is to be able to build therexact version of FFmpeg required by FFmpeg.AutoGen.

Build instructions https://trac.ffmpeg.org/wiki/CompilationGuide/Ubuntu.

```
docker build -t sipsorcery/ffmpegbuild:7.0 --progress=plain .
docker run -it sipsorcery/ffmpegbuild:7.0
docker run -it --entrypoint /bin/bash sipsorcery/ffmpegbuild:7.0
```

If (almost certainly when) the build fails comment out the failing and subsequent steps, create a debug image and then start troubleshooting.

```
docker build -t ffmpegbuild-debug --progress=plain .
docker run -it ffmpegbuild-debug
```

# Docker image versions

The ffmpeg build was verified to work when both images were based on Ubuntu 24.04 (i.e. ubuntu:24.04). An attempt to use the ffmpeg shared libraries on a Microsoft dotnet image such as mcr.microsoft.com/dotnet/aspnet:8.0 had a number of missing shared library dependencies for the ffmpeg shared libraries that could not be easily resolved with apt packages. It proved far simpler to use the Ubuntu image, to get the reqd ffmpeg dependencies, and install the aspnet runtime package (aspnetcore-runtime-8.0).

# Troubleshooting

```
ldd $(which ffmpeg)
ldd /usr/local/lib/libavdevice.so.61
```