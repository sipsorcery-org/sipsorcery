The FFmpeg.AutoGen library gets generated against a specific version of the FFmpeg binaries. As soon as the deviate the dotnet AutoGen library is likely to throw an exception due to incompatible binary interfaces.

The purpose of thie docker build file is to be able to build therexact version of FFmpeg required by FFmpeg.AutoGen.

Build instructions https://trac.ffmpeg.org/wiki/CompilationGuide/Ubuntu.

```
docker build -t sipsorcery/ffmpegbuild:7.0 --progress=plain .
docker run -it sipsorcery/ffmpegbuild:7.0
docker run -it --entrypoint /bin/bash sipsorcery/ffmpegbuild:7.0
```

If (almost certainly when) the build fails comment out the failing and subsequent steps, create a debug image and then start the troubleshooting.

```
docker build -t ffmpegbuild-debug --progress=plain .
docker run -it ffmpegbuild-debug
```
